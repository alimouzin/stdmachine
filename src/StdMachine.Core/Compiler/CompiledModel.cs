namespace StdMachine.Core.Compiler;

// Structures déployées (docs/06 § 2 à 4), en objets C#. AdsPlcClient les sérialisera en octets ; SimulatedPlcClient les utilise directement.

public enum ValueSource
{
    None = 0,
    Const = 1,
    Var = 2,
    PartData = 3,
    OpParam = 4,
    NamedPos = 5,
    Speed = 6,
    Text = 7,
    ObjValue = 8,
    System = 9,
}

/// <summary>Valeur résolue (ST_Value). <c>Real</c> : constante, n° de valeur E1..E4 (ObjValue), pourcentage (Speed d'index 0), décalage (Var, NamedPos).</summary>
public sealed record CValue(ValueSource Source = ValueSource.None, int Index = 0, double Real = 0);

public enum BindKind
{
    None = 0,
    Var = 1,
    PartData = 2,
    PartId = 3,
    Verdict = 4,
    PartRemoved = 5,
}

public sealed record CBind(BindKind Target = BindKind.None, int Index = 0);

public enum Op
{
    Nop = 0,
    Action = 1,
    WaitCond = 2,
    WaitTime = 3,
    Jump = 4,
    JumpIfFalse = 5,
    Fork = 6,
    Join = 7,
    LoopInit = 8,
    LoopNext = 9,
    Call = 10,
    Return = 11,
    SetVar = 12,
    Alarm = 13,
    End = 14,
    StepBegin = 15,
    StepEnd = 16,
    Route = 20,
}

/// <summary>Instruction (ST_Instr). Les index d'instruction dans <c>Args</c> sont absolus et commencent à 1.</summary>
public sealed class Instr
{
    public Op Op { get; set; }
    public uint StepId { get; set; }
    public int Object { get; set; }
    public int Action { get; set; }
    public CValue[] Params { get; set; } = new CValue[0];
    public CBind[] Binds { get; set; } = new CBind[0];
    public int[] Args { get; set; } = new int[0];
    public uint TimeoutMs { get; set; }
    public FailKind OnFail { get; set; }
    public int Retries { get; set; }
    public int FailTarget { get; set; }
}

public enum TableKind
{
    Operation = 0,
    Background = 1,
    Init = 2,
    Stop = 3,
    Clear = 4,
    Sub = 5,
    General = 10,
    GeneralSub = 11,
}

public sealed record TableDir(string Name, TableKind Kind, int Start, int Length);

public enum Cmp
{
    Eq,
    Ne,
    Lt,
    Le,
    Gt,
    Ge,
    IsTrue,
    IsFalse,
}

public sealed record CCondTerm(CValue Left, Cmp Cmp, CValue Right);

public sealed record CCondition(CombineMode Combine, IReadOnlyList<CCondTerm> Terms);

public sealed record CSignalRef(IoKind Kind, int Index);

public sealed class CObjConfig
{
    public int Type { get; set; }
    public int LocalId { get; set; }
    public int ParentId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool Simulated { get; set; }
    public bool Disabled { get; set; }
    public double Override { get; set; } = 100;
    /// <summary>S1..S8 ; null = non raccordé.</summary>
    public CSignalRef?[] Signals { get; set; } = new CSignalRef?[8];
    public int Device { get; set; }
    /// <summary>P1..P24.</summary>
    public double[] Params { get; set; } = new double[24];
    /// <summary>V1..V8, 0 = non défini.</summary>
    public double[] SpeedPercent { get; set; } = new double[8];
    /// <summary>Par n° d'action 1..16, 0 = défaut du catalogue.</summary>
    public uint[] TimeoutMs { get; set; } = new uint[16];
}

public sealed record CNamedPos(double X, double Y, double Z, double C, int Frame, double Approach, bool Taught);

public sealed record CVarInit(string Type, double Value);

public sealed record CInterlock(int Object, uint ActionMask, int Cond);

public sealed record COpMap(int Group, int OperationNo, int Table);

public sealed class StationData
{
    public int Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<CObjConfig> Objects { get; } = new List<CObjConfig>();
    public List<TableDir> Tables { get; } = new List<TableDir>();
    public List<Instr> Instructions { get; } = new List<Instr>();
    public List<CCondition> Conditions { get; } = new List<CCondition>();
    public List<string> Texts { get; } = new List<string>();
    public List<CNamedPos> Positions { get; } = new List<CNamedPos>();
    public List<CVarInit> Variables { get; } = new List<CVarInit>();
    public List<CInterlock> Interlocks { get; } = new List<CInterlock>();
    public List<COpMap> OpMap { get; } = new List<COpMap>();
}

public sealed record CGroup(string Name, GroupRole Role, uint StationMask, int Operations, int Priority);

public sealed record CStationCfg(string Name, uint GroupMask, int WorkPos, IReadOnlyList<int> QueuePos, double Override);

public sealed record CPlanarPos(double X, double Y, PlanarPositionKind Kind, int Station);

public sealed class MachineData
{
    public string MachineName { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public int Movers { get; set; }
    public int Slots { get; set; }
    public int RejectGroup { get; set; }
    public int NoStationDelayMs { get; set; }
    public List<CGroup> Groups { get; } = new List<CGroup>();
    public List<CStationCfg?> Stations { get; } = new List<CStationCfg?>();
    public List<CPlanarPos> PlanarPositions { get; } = new List<CPlanarPos>();
    public int ParkPosition { get; set; }
    public List<CObjConfig> Objects { get; } = new List<CObjConfig>();
    public List<TableDir> Tables { get; } = new List<TableDir>();
    public List<Instr> Instructions { get; } = new List<Instr>();
    public List<CCondition> Conditions { get; } = new List<CCondition>();
    public List<string> Texts { get; } = new List<string>();
    public QualityConfig Quality { get; set; } = new QualityConfig();
}

public sealed record CPressSegment(PressTarget Target, double TargetValue, double SpeedPercent, double ForceMin, double ForceMax, double StrokeMin, double StrokeMax);

public sealed record CPressRecipe(int Station, int Object, int RecipeNo, IReadOnlyList<CPressSegment> Segments);

/// <summary>Où une instruction vient de l'éditeur (docs/06 § 5.5).</summary>
public sealed record MapEntry(int Station, int Instruction, Guid StepGuid, uint StepId, string Path);

public sealed class CompiledTables
{
    public MachineData Machine { get; set; } = new MachineData();
    public List<StationData> Stations { get; } = new List<StationData>();
    public List<CPressRecipe> PressRecipes { get; } = new List<CPressRecipe>();
    /// <summary>Empreinte SHA-256 (hexadécimal) des données compilées : comparée à celle que le PLC publie.</summary>
    public string Hash { get; set; } = string.Empty;
    /// <summary>Carte de compilation : instruction (station 0 = process général) ↔ carte d'origine.</summary>
    public List<MapEntry> Map { get; } = new List<MapEntry>();

    public StationData? Station(int number) => Stations.FirstOrDefault(s => s.Number == number);

    public IEnumerable<MapEntry> InstructionsOf(Guid stepGuid) => Map.Where(m => m.StepGuid == stepGuid);

    public MapEntry? OriginOf(int station, int instruction) => Map.FirstOrDefault(m => m.Station == station && m.Instruction == instruction);
}
