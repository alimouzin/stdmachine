namespace StdMachine.Core.Validation;

/// <summary>Constat d'une analyse de variables : sert à la validation (V020 erreur, V115 avertissement).</summary>
public sealed record VariableFinding(string Code, bool IsError, string Message, Guid StepId);

/// <summary>
/// Analyse des variables de process (docs/13) : une variable de process n'est visible que dans son process (V020), et sa lecture sur un
/// chemin où elle n'a pas été écrite avant est signalée (V115), car elle garde sa dernière valeur d'un passage à l'autre.
/// Pure : aucun effet de bord. Seules les variables de process sont analysées ; une variable de station est partagée entre process et
/// peut être écrite ailleurs.
/// </summary>
public static class VariableUsage
{
    public static List<VariableFinding> Analyze(StationConfig station, ProcessDefinition proc)
    {
        var findings = new List<VariableFinding>();
        var owner = new Dictionary<Guid, ProcessDefinition>();
        foreach (var p in station.Processes)
            foreach (var v in p.Variables) owner[v.Id] = p;
        var own = new HashSet<Guid>(proc.Variables.Select(v => v.Id));

        string NameOf(Guid id) => owner.TryGetValue(id, out var p) ? p.Variables.First(v => v.Id == id).Name : id.ToString();

        // V020 : une variable de process d'un autre process n'est pas visible ici.
        foreach (var step in proc.AllSteps())
            foreach (var id in ReadsOf(step).Concat(WritesOf(step)).Distinct())
                if (owner.TryGetValue(id, out var o) && o.Id != proc.Id)
                    findings.Add(new VariableFinding("V020", true,
                        "La variable « " + NameOf(id) + " » appartient au process « " + o.Name + " » : elle n'est pas visible dans « " + proc.Name + " ». Utilisez une variable de station pour partager une valeur.", step.Id));

        // V115 : lecture avant écriture.
        if (own.Count > 0) WalkSequence(proc.Body, new HashSet<Guid>(), own, findings, NameOf);
        return findings;
    }

    private static HashSet<Guid> WalkSequence(Sequence seq, HashSet<Guid> assigned, HashSet<Guid> own, List<VariableFinding> findings, Func<Guid, string> nameOf)
    {
        var current = new HashSet<Guid>(assigned);
        foreach (var step in seq.Steps)
        {
            foreach (var id in ReadsOf(step).Distinct())
                if (own.Contains(id) && !current.Contains(id))
                    findings.Add(new VariableFinding("V115", false,
                        "La variable « " + nameOf(id) + " » est lue ici sans avoir été écrite avant sur tous les chemins : elle garde la valeur du passage précédent.", step.Id));

            switch (step)
            {
                case ParallelBlock par:
                {
                    // Chaque branche ne voit que ce qui était écrit avant le bloc ; après la jointure, tout ce que les branches ont écrit.
                    var after = new HashSet<Guid>(current);
                    foreach (var branch in par.Branches) after.UnionWith(WalkSequence(branch, current, own, findings, nameOf));
                    current = after;
                    break;
                }
                case IfBlock ib:
                {
                    var then = WalkSequence(ib.Then, current, own, findings, nameOf);
                    var other = WalkSequence(ib.Else, current, own, findings, nameOf);
                    then.IntersectWith(other);
                    current.UnionWith(then);
                    break;
                }
                case RepeatBlock rb:
                {
                    var body = WalkSequence(rb.Body, current, own, findings, nameOf);
                    // « Répéter N fois » exécute son corps au moins une fois ; « tant que » peut ne pas l'exécuter.
                    if (rb.Mode == RepeatMode.Count && rb.Count >= 1) current = body;
                    break;
                }
                default:
                    if (WriteIsGuaranteed(step)) foreach (var id in WritesOf(step)) current.Add(id);
                    break;
            }
        }
        return current;
    }

    /// <summary>Une action dont l'échec est ignoré, marque la pièce mauvaise ou saute à une étape n'écrit pas de façon garantie.</summary>
    private static bool WriteIsGuaranteed(Step step)
        => !(step is ActionStep a && (a.Fail.Kind == FailKind.Ignore || a.Fail.Kind == FailKind.PartBad || a.Fail.Kind == FailKind.Goto));

    /// <summary>Variables lues par une étape (valeurs de paramètres, conditions, valeur affectée ; la cible d'un « += » est aussi lue).</summary>
    public static IEnumerable<Guid> ReadsOf(Step step)
    {
        foreach (var r in ValueRefsOf(step))
            if (r.Kind == ValueKind.Var && r.Ref.HasValue) yield return r.Ref.Value;
        if (step is SetStep s && s.Target == SetTarget.Var && s.TargetRef.HasValue && s.Operation != SetOperation.Assign) yield return s.TargetRef.Value;
    }

    /// <summary>Variables écrites par une étape (liaisons de résultats, « Définir variable »).</summary>
    public static IEnumerable<Guid> WritesOf(Step step)
    {
        if (step is ActionStep a)
            foreach (var b in a.Bindings)
                if (b.Target == BindTarget.Var && b.Ref.HasValue) yield return b.Ref.Value;
        if (step is SetStep s && s.Target == SetTarget.Var && s.TargetRef.HasValue) yield return s.TargetRef.Value;
    }

    private static IEnumerable<ValueRef> ValueRefsOf(Step step)
    {
        switch (step)
        {
            case ActionStep a: foreach (var r in a.Args) yield return r; break;
            case FunctionStep f: foreach (var r in f.Args) yield return r; break;
            case RouteStep rt: foreach (var r in rt.Args) yield return r; break;
            case SetStep s: yield return s.Value; break;
            case WaitConditionStep w: foreach (var r in TermRefs(w.Condition)) yield return r; break;
            case IfBlock i: foreach (var r in TermRefs(i.Condition)) yield return r; break;
            case RepeatBlock rb when rb.Mode == RepeatMode.While: foreach (var r in TermRefs(rb.Condition)) yield return r; break;
        }
    }

    private static IEnumerable<ValueRef> TermRefs(Condition c)
    {
        foreach (var t in c.Terms)
        {
            yield return t.Left;
            yield return t.Right;
        }
    }
}
