namespace StdMachine.Core;

// Modèle de process de docs/07 § 3.2 : arbre d'étapes et de blocs édité par l'éditeur de process.
// Les identifiants sont des Guid stables ; les numéros PLC sont calculés plus tard par le compilateur.

public enum ValueKind
{
    Const,
    Var,
    PartData,
    OpParam,
    NamedPos,
    Speed,
    Text,
    ObjValue,
    System,
    /// <summary>Paramètre de la fonction personnalisée dans laquelle l'étape se trouve (Ref = identifiant du paramètre).</summary>
    FuncParam,
}

/// <summary>Valeur d'un paramètre : constante, variable de station, donnée de pièce, paramètre d'opération, position nommée, vitesse…</summary>
public sealed record ValueRef(ValueKind Kind, double Number = 0, Guid? Ref = null, int Index = 0, string? Text = null, double Offset = 0)
{
    public static ValueRef Const(double v) => new ValueRef(ValueKind.Const, v);
    public static ValueRef Var(Guid id) => new ValueRef(ValueKind.Var, Ref: id);
    public static ValueRef PartData(Guid id) => new ValueRef(ValueKind.PartData, Ref: id);
    public static ValueRef OpParam(int n) => new ValueRef(ValueKind.OpParam, Index: n);
    public static ValueRef NamedPos(Guid id, double offset = 0) => new ValueRef(ValueKind.NamedPos, Ref: id, Offset: offset);
    public static ValueRef Speed(int n) => new ValueRef(ValueKind.Speed, Index: n);
    public static ValueRef SpeedPercent(double p) => new ValueRef(ValueKind.Speed, Number: p);
    public static ValueRef TextOf(string s) => new ValueRef(ValueKind.Text, Text: s);
    public static ValueRef ObjValue(Guid objectId, int valueNo) => new ValueRef(ValueKind.ObjValue, Ref: objectId, Index: valueNo);
    /// <summary>Paramètre de la fonction personnalisée ; <paramref name="offsetParam"/> ajoute un autre paramètre (« = Position + Approche »), stocké dans <see cref="Text"/>.</summary>
    public static ValueRef FuncParam(Guid paramId, double offset = 0, Guid? offsetParam = null) => new ValueRef(ValueKind.FuncParam, Ref: paramId, Offset: offset, Text: offsetParam?.ToString("N"));
}

public enum CompareOp
{
    Equal,
    NotEqual,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
}

public enum CombineMode
{
    All,
    Any,
}

public sealed class CondTerm
{
    public ValueRef Left { get; set; } = ValueRef.Const(0);
    public CompareOp Op { get; set; } = CompareOp.Equal;
    public ValueRef Right { get; set; } = ValueRef.Const(0);
    /// <summary>Libellé lisible de la comparaison (rempli par l'appelant : « Pièce présente sur le nid = Vrai »).</summary>
    public string? Text { get; set; }

    public CondTerm Clone() => new CondTerm { Left = Left, Op = Op, Right = Right, Text = Text };
}

/// <summary>Jusqu'à 4 comparaisons combinées par « toutes vraies » ou « au moins une vraie ».</summary>
public sealed class Condition
{
    public CombineMode Combine { get; set; } = CombineMode.All;
    public List<CondTerm> Terms { get; } = new List<CondTerm>();

    public Condition Clone()
    {
        var c = new Condition { Combine = Combine };
        foreach (var t in Terms) c.Terms.Add(t.Clone());
        return c;
    }
}

public enum FailKind
{
    StopStation,
    Retry,
    PartBad,
    Goto,
    Ignore,
}

public sealed record FailPolicy(FailKind Kind = FailKind.StopStation, int Retries = 0, Guid? GotoStepId = null);

public enum SetTarget
{
    Var,
    PartData,
    Verdict,
    PartRemoved,
}

public enum SetOperation
{
    Assign,
    Add,
    Subtract,
}

public enum RepeatMode
{
    Count,
    While,
}

public enum ProcessKind
{
    Operation,
    Background,
    Init,
    Stop,
    Clear,
    Sub,
    General,
    GeneralSub,
}

/// <summary>Suite ordonnée d'étapes (corps d'un process, branche d'un bloc).</summary>
public sealed class Sequence
{
    public List<Step> Steps { get; } = new List<Step>();

    public Sequence() { }

    public Sequence(IEnumerable<Step> steps) { Steps.AddRange(steps); }

    public Sequence Clone()
    {
        var s = new Sequence();
        foreach (var st in Steps) s.Steps.Add(st.Clone());
        return s;
    }
}

public abstract class Step
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? Label { get; set; }
    public string? Comment { get; set; }

    protected void CopyBaseTo(Step target)
    {
        target.Id = Id;
        target.Label = Label;
        target.Comment = Comment;
    }

    /// <summary>Copie profonde (même identifiants) : sert à l'annuler / refaire.</summary>
    public abstract Step Clone();

    /// <summary>Copie profonde avec de nouveaux identifiants (duplication, dépôt depuis la palette).</summary>
    public Step CloneWithNewIds()
    {
        var c = Clone();
        Renew(c);
        return c;
    }

    private static void Renew(Step s)
    {
        s.Id = Guid.NewGuid();
        if (s is Block b) foreach (var seq in b.Sequences) foreach (var child in seq.Steps) Renew(child);
    }
}

public sealed class ActionStep : Step
{
    public Guid ObjectId { get; set; }
    public int ActionNo { get; set; }
    public List<ValueRef> Args { get; } = new List<ValueRef>();
    public double? TimeoutSeconds { get; set; }
    public FailPolicy Fail { get; set; } = new FailPolicy();

    public override Step Clone()
    {
        var c = new ActionStep { ObjectId = ObjectId, ActionNo = ActionNo, TimeoutSeconds = TimeoutSeconds, Fail = Fail };
        c.Args.AddRange(Args);
        CopyBaseTo(c);
        return c;
    }
}

public sealed class FunctionStep : Step
{
    public Guid ModuleObjectId { get; set; }
    /// <summary>Fonction du module (« PickPart ») ou personnalisée (« custom:… »).</summary>
    public string FunctionId { get; set; } = string.Empty;
    public List<ValueRef> Args { get; } = new List<ValueRef>();
    public FailPolicy Fail { get; set; } = new FailPolicy();

    public override Step Clone()
    {
        var c = new FunctionStep { ModuleObjectId = ModuleObjectId, FunctionId = FunctionId, Fail = Fail };
        c.Args.AddRange(Args);
        CopyBaseTo(c);
        return c;
    }
}

public sealed class WaitConditionStep : Step
{
    public Condition Condition { get; set; } = new Condition();
    public double? TimeoutSeconds { get; set; }

    public override Step Clone()
    {
        var c = new WaitConditionStep { Condition = Condition.Clone(), TimeoutSeconds = TimeoutSeconds };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class WaitTimeStep : Step
{
    public double DurationSeconds { get; set; } = 1;

    public override Step Clone()
    {
        var c = new WaitTimeStep { DurationSeconds = DurationSeconds };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class CallStep : Step
{
    public Guid ProcessId { get; set; }

    public override Step Clone()
    {
        var c = new CallStep { ProcessId = ProcessId };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class SetStep : Step
{
    public SetTarget Target { get; set; }
    public Guid? TargetRef { get; set; }
    public SetOperation Operation { get; set; }
    public ValueRef Value { get; set; } = ValueRef.Const(0);

    public override Step Clone()
    {
        var c = new SetStep { Target = Target, TargetRef = TargetRef, Operation = Operation, Value = Value };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class AlarmStep : Step
{
    public string Message { get; set; } = string.Empty;
    public int Severity { get; set; } = 2;
    public int AlarmNo { get; set; }

    public override Step Clone()
    {
        var c = new AlarmStep { Message = Message, Severity = Severity, AlarmNo = AlarmNo };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class EndStep : Step
{
    public override Step Clone()
    {
        var c = new EndStep();
        CopyBaseTo(c);
        return c;
    }
}

/// <summary>Niveau machine : aller dans une station de la fonction et y exécuter l'opération.</summary>
public sealed class RouteStep : Step
{
    public Guid GroupId { get; set; }
    public int OperationNo { get; set; } = 1;
    public int Slot { get; set; }
    public List<ValueRef> Args { get; } = new List<ValueRef>();

    public override Step Clone()
    {
        var c = new RouteStep { GroupId = GroupId, OperationNo = OperationNo, Slot = Slot };
        c.Args.AddRange(Args);
        CopyBaseTo(c);
        return c;
    }
}

/// <summary>Bloc de logique : contient des branches d'étapes.</summary>
public abstract class Block : Step
{
    public abstract IReadOnlyList<Sequence> Sequences { get; }
}

public sealed class ParallelBlock : Block
{
    public List<Sequence> Branches { get; } = new List<Sequence> { new Sequence(), new Sequence() };

    public override IReadOnlyList<Sequence> Sequences => Branches;

    public override Step Clone()
    {
        var c = new ParallelBlock();
        c.Branches.Clear();
        foreach (var b in Branches) c.Branches.Add(b.Clone());
        CopyBaseTo(c);
        return c;
    }
}

public sealed class IfBlock : Block
{
    public Condition Condition { get; set; } = new Condition();
    public Sequence Then { get; set; } = new Sequence();
    public Sequence Else { get; set; } = new Sequence();
    public string? ElseLabel { get; set; }

    public override IReadOnlyList<Sequence> Sequences => new[] { Then, Else };

    public override Step Clone()
    {
        var c = new IfBlock { Condition = Condition.Clone(), Then = Then.Clone(), Else = Else.Clone(), ElseLabel = ElseLabel };
        CopyBaseTo(c);
        return c;
    }
}

public sealed class RepeatBlock : Block
{
    public RepeatMode Mode { get; set; } = RepeatMode.Count;
    public int Count { get; set; } = 3;
    public Condition Condition { get; set; } = new Condition();
    public int Max { get; set; } = 10;
    public Sequence Body { get; set; } = new Sequence();

    public override IReadOnlyList<Sequence> Sequences => new[] { Body };

    public override Step Clone()
    {
        var c = new RepeatBlock { Mode = Mode, Count = Count, Condition = Condition.Clone(), Max = Max, Body = Body.Clone() };
        CopyBaseTo(c);
        return c;
    }
}

public sealed record OperationRef(Guid GroupId, int OperationNo);

public sealed class ProcessDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public ProcessKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Opérations de fonctions implémentées par ce process (niveau station).</summary>
    public List<OperationRef> Implements { get; } = new List<OperationRef>();
    public Sequence Body { get; set; } = new Sequence();

    public ProcessDefinition Clone()
    {
        var c = new ProcessDefinition { Id = Id, Kind = Kind, Name = Name, Body = Body.Clone() };
        c.Implements.AddRange(Implements);
        return c;
    }

    /// <summary>Toutes les étapes, blocs compris, dans l'ordre de lecture.</summary>
    public IEnumerable<Step> AllSteps() => ProcessTree.Flatten(Body);
}

/// <summary>Navigation dans l'arbre d'un process : recherche, parent, index, parcours.</summary>
public static class ProcessTree
{
    public static IEnumerable<Step> Flatten(Sequence seq)
    {
        foreach (var s in seq.Steps)
        {
            yield return s;
            if (s is Block b)
                foreach (var branch in b.Sequences)
                    foreach (var child in Flatten(branch))
                        yield return child;
        }
    }

    public readonly record struct Location(Step Step, Sequence Parent, int Index, Block? ParentBlock);

    public static Location? Find(Sequence root, Guid id) => Find(root, id, null);

    private static Location? Find(Sequence seq, Guid id, Block? owner)
    {
        for (int i = 0; i < seq.Steps.Count; i++)
        {
            var s = seq.Steps[i];
            if (s.Id == id) return new Location(s, seq, i, owner);
            if (s is Block b)
                foreach (var branch in b.Sequences)
                {
                    var found = Find(branch, id, b);
                    if (found != null) return found;
                }
        }
        return null;
    }

    /// <summary>Vrai si <paramref name="candidate"/> est <paramref name="block"/> ou l'un de ses descendants.</summary>
    public static bool IsInside(Block block, Guid candidateId)
    {
        foreach (var branch in block.Sequences)
            foreach (var s in Flatten(branch))
                if (s.Id == candidateId) return true;
        return block.Id == candidateId;
    }

    /// <summary>Objets utilisés (actions, fonctions de module) par une suite d'étapes, blocs imbriqués compris.</summary>
    public static HashSet<Guid> ObjectsUsed(Sequence seq)
    {
        var set = new HashSet<Guid>();
        foreach (var s in Flatten(seq))
        {
            if (s is ActionStep a) set.Add(a.ObjectId);
            else if (s is FunctionStep f) set.Add(f.ModuleObjectId);
        }
        return set;
    }

    public static HashSet<Guid> ObjectsUsed(Step step)
    {
        var tmp = new Sequence();
        tmp.Steps.Add(step);
        return ObjectsUsed(tmp);
    }
}
