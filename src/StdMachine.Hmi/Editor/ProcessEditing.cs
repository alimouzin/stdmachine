using StdMachine.Core;
using StdMachine.Simulation.Demo;

namespace StdMachine.Hmi.Editor;

/// <summary>Emplacement d'insertion d'un dépôt : une suite et un index.</summary>
public sealed record InsertTarget(Sequence Sequence, int Index);

/// <summary>Charge utile d'un glisser depuis la palette : fabrique d'étape et libellé.</summary>
public sealed record PaletteDrag(string Label, Func<Step> Create, Guid? ObjectId);

/// <summary>Modifications d'un arbre de process (docs/07 § 3.2). Fonctions pures sur l'arbre ; l'annuler / refaire est géré par l'éditeur.</summary>
public static class ProcessEditing
{
    /// <summary>Chaîne des blocs qui contiennent une suite, de la racine à la suite : (bloc, index de branche).</summary>
    public static List<(Block Block, int Branch)> OwnerChain(Sequence root, Sequence target)
    {
        var chain = new List<(Block, int)>();
        Find(root, target, chain);
        return chain;
    }

    private static bool Find(Sequence seq, Sequence target, List<(Block, int)> chain)
    {
        if (ReferenceEquals(seq, target)) return true;
        foreach (var s in seq.Steps)
        {
            if (s is not Block b) continue;
            var seqs = b.Sequences;
            for (int i = 0; i < seqs.Count; i++)
            {
                chain.Add((b, i));
                if (Find(seqs[i], target, chain)) return true;
                chain.RemoveAt(chain.Count - 1);
            }
        }
        return false;
    }

    /// <summary>
    /// Vérifie qu'insérer <paramref name="step"/> dans <paramref name="target"/> respecte les règles : pas de bloc dans lui-même,
    /// pas d'objet dans deux branches d'un même bloc En parallèle (V004), pas plus de 3 niveaux de blocs.
    /// Renvoie la raison du refus, ou null.
    /// </summary>
    public static string? ValidateInsert(Sequence root, Sequence target, Step step, StationConfig? station)
    {
        if (step is Block moving && IsDescendantSequence(moving, target))
            return "Un bloc ne peut pas être déplacé dans lui-même.";

        var chain = OwnerChain(root, target);
        if (chain.Count >= 3 && step is Block) return "Les blocs s'imbriquent sur 3 niveaux au plus.";
        var used = ProcessTree.ObjectsUsed(step);
        foreach (var (block, branch) in chain)
        {
            if (block is not ParallelBlock pb) continue;
            for (int i = 0; i < pb.Branches.Count; i++)
            {
                if (i == branch) continue;
                var other = UsedExcluding(pb.Branches[i], step.Id);
                if (other.Overlaps(used))
                {
                    var o = used.First(other.Contains);
                    string name = station?.FindObject(o)?.Name ?? "Cet objet";
                    return "Un objet ne peut pas être dans deux branches parallèles (« " + name + " » est déjà dans l'autre branche).";
                }
            }
        }
        return null;
    }

    private static HashSet<Guid> UsedExcluding(Sequence seq, Guid excluded)
    {
        var set = new HashSet<Guid>();
        void Walk(Sequence s)
        {
            foreach (var st in s.Steps)
            {
                if (st.Id == excluded) continue;
                if (st is ActionStep a) set.Add(a.ObjectId);
                else if (st is FunctionStep f) set.Add(f.ModuleObjectId);
                else if (st is Block b) foreach (var br in b.Sequences) Walk(br);
            }
        }
        Walk(seq);
        return set;
    }

    private static bool IsDescendantSequence(Block block, Sequence target)
    {
        foreach (var branch in block.Sequences)
        {
            if (ReferenceEquals(branch, target)) return true;
            foreach (var s in branch.Steps)
                if (s is Block b && IsDescendantSequence(b, target)) return true;
        }
        return false;
    }

    /// <summary>Insère une étape ; si elle est déjà dans l'arbre, la déplace (en corrigeant l'index).</summary>
    public static void InsertOrMove(Sequence root, Step step, Sequence target, int index)
    {
        var loc = ProcessTree.Find(root, step.Id);
        if (loc.HasValue)
        {
            var l = loc.Value;
            if (ReferenceEquals(l.Parent, target) && l.Index < index) index--;
            l.Parent.Steps.RemoveAt(l.Index);
        }
        index = Math.Max(0, Math.Min(index, target.Steps.Count));
        target.Steps.Insert(index, step);
    }

    public static bool Remove(Sequence root, Guid id)
    {
        var loc = ProcessTree.Find(root, id);
        if (!loc.HasValue) return false;
        loc.Value.Parent.Steps.RemoveAt(loc.Value.Index);
        return true;
    }

    // ---------------------------------------------------------------- fabriques d'étapes (palette)

    public static Step NewAction(StationConfig station, ObjectNode obj, ActionDef def)
    {
        var a = new ActionStep { ObjectId = obj.Id, ActionNo = def.No };
        FillArgs(def.Params, a.Args);
        if (def.Name == "Chercher pièce") a.Fail = new FailPolicy(FailKind.Retry, 3);
        return a;
    }

    public static Step NewFunction(ObjectNode module, FunctionDef def)
    {
        var f = new FunctionStep { ModuleObjectId = module.Id, FunctionId = def.Id };
        FillArgs(def.Params, f.Args);
        return f;
    }

    private static void FillArgs(IReadOnlyList<ParamDef> defs, List<ValueRef> args)
    {
        foreach (var d in defs)
        {
            switch (d.Type)
            {
                case "Position": args.Add(ValueRef.Const(0) with { Kind = ValueKind.NamedPos }); break; // à choisir
                case "Speed": args.Add(ValueRef.Speed((int)(d.Default == 0 ? 2 : d.Default))); break;
                default: args.Add(ValueRef.Const(d.Default)); break;
            }
        }
    }

    public static Step NewRoute(GroupConfig group, int operationNo)
    {
        var r = new RouteStep { GroupId = group.Id, OperationNo = operationNo };
        foreach (var p in group.Operations[operationNo - 1].Parameters) r.Args.Add(ValueRef.Const(p.Default));
        return r;
    }

    public static Step NewLogic(string kind, StationConfig? station, ProcessDefinition process)
    {
        switch (kind)
        {
            case "wait":
            {
                var w = new WaitConditionStep { TimeoutSeconds = 5 };
                var input = station?.AllObjects().FirstOrDefault(o => o.TypeCode == "DigitalInput");
                if (input != null)
                    w.Condition.Terms.Add(new CondTerm { Left = ValueRef.ObjValue(input.Id, 1), Op = CompareOp.Equal, Right = ValueRef.Const(1), Text = input.Name });
                return w;
            }
            case "time": return new WaitTimeStep { DurationSeconds = 1 };
            case "if":
            {
                var b = new IfBlock();
                b.Condition.Terms.Add(new CondTerm { Left = ValueRef.PartData(DemoProject.VerdictId), Op = CompareOp.Equal, Right = ValueRef.Const(1), Text = "Verdict = Bon" });
                return b;
            }
            case "repeat": return new RepeatBlock { Count = 3 };
            case "parallel": return new ParallelBlock();
            case "call": return new CallStep { ProcessId = station?.Processes.FirstOrDefault(p => p.Kind == ProcessKind.Sub && p.Id != process.Id)?.Id ?? Guid.Empty };
            case "set": return new SetStep { Target = SetTarget.Verdict, Value = ValueRef.Const(1), Label = "Verdict bon" };
            case "alarm": return new AlarmStep { Message = "Défaut du process" };
            default: return new EndStep();
        }
    }
}
