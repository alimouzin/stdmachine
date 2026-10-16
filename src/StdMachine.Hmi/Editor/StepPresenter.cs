using StdMachine.Core;
using StdMachine.Simulation.Demo;

namespace StdMachine.Hmi.Editor;

public sealed record ParamLine(string Key, string Value, bool IsError = false);

/// <summary>Ce qu'une carte de l'éditeur affiche : titre, icône, paramètres clés, durée estimée (docs/08 § 6.9).</summary>
public sealed record CardText(string Title, string Icon, IReadOnlyList<ParamLine> Params, double DurationSeconds, string? Subtitle);

/// <summary>
/// Transforme une étape et le projet en texte de carte. Les noms viennent du projet (objets, positions nommées, variables,
/// données de pièce, fonctions) et du catalogue allégé. Aucun nom n'est écrit en dur par station.
/// </summary>
public sealed class StepPresenter
{
    private readonly MachineProject _project;
    private readonly StationConfig? _station;
    private readonly ProcessDefinition? _process;

    public StepPresenter(MachineProject project, StationConfig? station, ProcessDefinition? process = null)
    {
        _project = project;
        _station = station;
        _process = process;
    }

    public CardText Present(Step step)
    {
        switch (step)
        {
            case ActionStep a: return PresentAction(a);
            case FunctionStep f: return PresentFunction(f);
            case WaitConditionStep w:
            {
                var lines = new List<ParamLine>();
                for (int i = 0; i < Math.Min(3, w.Condition.Terms.Count); i++)
                    lines.Add(new ParamLine(i == 0 ? "Jusqu'à" : (w.Condition.Combine == CombineMode.All ? "et" : "ou"), w.Condition.Terms[i].Text ?? "condition"));
                if (w.Condition.Terms.Count == 0) lines.Add(new ParamLine("Condition", "à définir", true));
                if (w.TimeoutSeconds.HasValue) lines.Add(new ParamLine("Délai", DisplayFormat.Duration(w.TimeoutSeconds.Value)));
                return new CardText("Attendre condition", "hourglass_empty", lines, DemoCatalog.EstimatedSeconds(w, _station), null);
            }
            case WaitTimeStep t:
                return new CardText("Attendre durée", "timer", new[] { new ParamLine("Durée", DisplayFormat.Duration(t.DurationSeconds)) }, t.DurationSeconds, null);
            case SetStep s:
            {
                string title = s.Label ?? (s.Target == SetTarget.Verdict ? "Définir le verdict" : s.Target == SetTarget.PartRemoved ? "Retirer la pièce" : "Définir une variable");
                var lines = new List<ParamLine>();
                switch (s.Target)
                {
                    case SetTarget.Verdict: lines.Add(new ParamLine("Verdict", s.Value.Number >= 2 ? "Mauvais" : "Bon")); break;
                    case SetTarget.PartRemoved: lines.Add(new ParamLine("Pièce", "retirée de la machine")); break;
                    default: lines.Add(new ParamLine("Cible", Name(s.TargetRef))); lines.Add(new ParamLine("Valeur", ValueText(s.Value))); break;
                }
                return new CardText(title, "edit", lines, 0, null);
            }
            case CallStep c:
                return new CardText("Appeler sous-process", "account_tree", new[] { new ParamLine("Process", _station?.Processes.FirstOrDefault(p => p.Id == c.ProcessId)?.Name ?? "à choisir", _station?.Processes.All(p => p.Id != c.ProcessId) ?? true) }, 1, null);
            case AlarmStep al:
                return new CardText("Signaler défaut", "report", new[] { new ParamLine("Message", al.Message.Length == 0 ? "à saisir" : al.Message, al.Message.Length == 0) }, 0, null);
            case EndStep:
                return new CardText("Fin", "flag", new ParamLine[0], 0, null);
            case RouteStep r: return PresentRoute(r);
            case ParallelBlock pb: return new CardText("En parallèle", "call_split", new[] { new ParamLine("Branches", pb.Branches.Count.ToString()) }, 0, null);
            case IfBlock ib: return new CardText("Si / Sinon", "alt_route", new[] { new ParamLine("Condition", ConditionText(ib.Condition)) }, 0, null);
            case RepeatBlock rb: return new CardText("Répéter", "repeat", new[] { new ParamLine(rb.Mode == RepeatMode.Count ? "Nombre" : "Tant que", rb.Mode == RepeatMode.Count ? rb.Count + " fois" : ConditionText(rb.Condition)) }, 0, null);
            default: return new CardText("Étape", "help", new ParamLine[0], 0, null);
        }
    }

    // ---------------------------------------------------------------- actions et fonctions

    private CardText PresentAction(ActionStep a)
    {
        var node = _station?.FindObject(a.ObjectId);
        var type = node == null ? null : DemoCatalog.Get(node.TypeCode);
        var def = type?.Action(a.ActionNo);
        if (def == null) return new CardText("Action inconnue", "help", new[] { new ParamLine("Objet", node?.Name ?? "supprimé", true) }, 0.5, node?.Name);
        var lines = ParamLines(def.Params, a.Args);
        if (a.Fail.Kind == FailKind.Retry && lines.Count < 3) lines.Add(new ParamLine("En cas d'échec", "refaire " + a.Fail.Retries + " fois"));
        return new CardText(def.Name, def.Icon, lines, def.DurationSeconds, node?.Name);
    }

    private CardText PresentFunction(FunctionStep f)
    {
        var node = _station?.FindObject(f.ModuleObjectId);
        var type = node == null ? null : DemoCatalog.Get(node.TypeCode);
        var def = DemoFunctions.FindFunction(_station, f.ModuleObjectId, f.FunctionId);
        if (def == null) return new CardText("Fonction inconnue", "help", new[] { new ParamLine("Module", node?.Name ?? "supprimé", true) }, 1.0, node?.Name);
        return new CardText(def.Name, f.FunctionId.StartsWith("custom:", StringComparison.Ordinal) ? "edit" : "precision_manufacturing", ParamLines(def.Params, f.Args), def.DurationSeconds, node?.Name);
    }

    private List<ParamLine> ParamLines(IReadOnlyList<ParamDef> defs, IReadOnlyList<ValueRef> args)
    {
        var lines = new List<ParamLine>();
        for (int i = 0; i < defs.Count && lines.Count < 3; i++)
        {
            var d = defs[i];
            if (i >= args.Count)
            {
                if (d.Type == "Position" || d.Type == "Recipe") lines.Add(new ParamLine(d.Name, "à choisir", true));
                continue;
            }
            lines.Add(new ParamLine(d.Name, ValueText(args[i], d)));
        }
        return lines;
    }

    private CardText PresentRoute(RouteStep r)
    {
        var g = _project.Group(r.GroupId);
        var op = g?.Operations.ElementAtOrDefault(r.OperationNo - 1);
        var lines = new List<ParamLine>();
        lines.Add(new ParamLine("Fonction", g?.Name ?? "inconnue", g == null));
        if (op != null)
            for (int i = 0; i < op.Parameters.Count && i < r.Args.Count && lines.Count < 3; i++)
                lines.Add(new ParamLine(op.Parameters[i].Name, ValueText(r.Args[i], op.Parameters[i])));
        double dur = 0;
        if (g != null && g.StationIds.Count > 0)
        {
            var st = _project.StationById(g.StationIds[0]);
            var proc = st?.Processes.FirstOrDefault(p => p.Implements.Any(i => i.GroupId == g.Id));
            if (proc != null) dur = StepSchedule.Build(proc, st, true, 0).Total;
        }
        return new CardText(op?.Name ?? "Opération", RouteIcon(g), lines, dur, g?.Name);
    }

    private static string RouteIcon(GroupConfig? g)
    {
        switch (g?.Role)
        {
            case GroupRole.Entry: return "login";
            case GroupRole.Exit: return "logout";
            case GroupRole.Reject: return "delete";
            default: return "build";
        }
    }

    // ---------------------------------------------------------------- valeurs

    public string ValueText(ValueRef v, ParamDef? def = null)
    {
        switch (v.Kind)
        {
            case ValueKind.Const:
                var unit = def?.Unit;
                if (def != null && def.Type == "Bool") return v.Number != 0 ? "Oui" : "Non";
                if (def != null && def.Type == "Recipe") return "n° " + DisplayFormat.Number(v.Number, 0);
                return unit != null ? DisplayFormat.WithUnit(v.Number, unit, v.Number == Math.Floor(v.Number) ? 0 : 2) : DisplayFormat.Number(v.Number, v.Number == Math.Floor(v.Number) ? 0 : 2);
            case ValueKind.Var: return Name(v.Ref);
            case ValueKind.PartData: return "donnée « " + (_project.Machine.PartData.FirstOrDefault(p => p.Id == v.Ref)?.Name ?? "?") + " »";
            case ValueKind.OpParam: return "paramètre d'opération " + v.Index;
            case ValueKind.NamedPos:
            {
                var p = _station?.Positions.FirstOrDefault(q => q.Id == v.Ref);
                var name = p?.Name ?? "?";
                return v.Offset != 0 ? name + " (+" + DisplayFormat.Number(v.Offset, 0) + " mm)" : name;
            }
            case ValueKind.Speed:
                if (v.Index == 1) return "Lente";
                if (v.Index == 2) return "Rapide";
                if (v.Index > 2) return "V" + v.Index;
                return DisplayFormat.Percent(v.Number);
            case ValueKind.Text: return "« " + v.Text + " »";
            case ValueKind.ObjValue: return (_station?.FindObject(v.Ref ?? Guid.Empty)?.Name ?? "?") + " · E" + v.Index;
            case ValueKind.FuncParam:
            {
                var fp = _station?.CustomFunctions.SelectMany(cf => cf.Params).FirstOrDefault(q => q.Id == v.Ref);
                string plus = "";
                if (v.Text != null) { var op = _station?.CustomFunctions.SelectMany(cf => cf.Params).FirstOrDefault(q => q.Id.ToString("N") == v.Text); plus = " + " + (op?.Name ?? "?"); }
                return "= " + (fp?.Name ?? "?") + plus + (v.Offset != 0 ? " +" + DisplayFormat.Number(v.Offset, 0) : "");
            }
            default: return "—";
        }
    }

    private string Name(Guid? id)
    {
        if (id == null) return "?";
        var v = _station?.Variables.FirstOrDefault(x => x.Id == id);
        if (v != null) return v.Name;
        return _project.Machine.PartData.FirstOrDefault(x => x.Id == id)?.Name ?? "?";
    }

    public string ConditionText(Condition c)
    {
        if (c.Terms.Count == 0) return "à définir";
        var parts = c.Terms.Select(t => t.Text ?? (ValueText(t.Left) + " " + OpText(t.Op) + " " + ValueText(t.Right)));
        return string.Join(c.Combine == CombineMode.All ? " et " : " ou ", parts);
    }

    private static string OpText(CompareOp op)
    {
        switch (op)
        {
            case CompareOp.Equal: return "=";
            case CompareOp.NotEqual: return "≠";
            case CompareOp.Less: return "<";
            case CompareOp.LessOrEqual: return "≤";
            case CompareOp.Greater: return ">";
            default: return "≥";
        }
    }
}
