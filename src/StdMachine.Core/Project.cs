using System.Text.Json.Serialization;

namespace StdMachine.Core;

// Projet machine (docs/07 § 3.1), version minimale pour la maquette : stations, objets, fonctions, positions,
// process. Le modèle complet (catalogue, raccordements, limites) arrive en phase 2.

public enum GroupRole
{
    Entry,
    Work,
    Exit,
    Reject,
}

public enum PlanarPositionKind
{
    Work,
    Queue,
    Buffer,
    Park,
}

public sealed class PlanarConfig
{
    public int Columns { get; set; } = 4;
    public int Rows { get; set; } = 3;
    public double TileMm { get; set; } = 240;
    public int Movers { get; set; } = 6;
    public int Slots { get; set; } = 1;
    public double MoverMm { get; set; } = 113;
    [JsonIgnore]
    public double WidthMm => Columns * TileMm;
    [JsonIgnore]
    public double HeightMm => Rows * TileMm;
}

public sealed class PlanarPosition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PlanarPositionKind Kind { get; set; }
    public string Name { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public Guid? StationId { get; set; }
}

public sealed class ParamDef
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    /// <summary>Real, Int, Bool, Enum, Duration, Position, Speed, Text, Recipe, Percent.</summary>
    public string Type { get; set; } = "Real";
    public string? Unit { get; set; }
    public double Default { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public IReadOnlyList<string>? Values { get; set; }
}

public sealed class OperationSignature
{
    public string Name { get; set; } = string.Empty;
    public List<ParamDef> Parameters { get; } = new List<ParamDef>();
}

public sealed class GroupConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public GroupRole Role { get; set; }
    public int Priority { get; set; } = 1;
    public List<Guid> StationIds { get; } = new List<Guid>();
    public List<OperationSignature> Operations { get; } = new List<OperationSignature>();
    public bool ImputesRejects { get; set; }
}

public sealed class PartDataDef
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Real";
    public string? Unit { get; set; }
    public bool Reserved { get; set; }
}

public sealed class ParamValue
{
    /// <summary>N° du paramètre dans le catalogue (P1..P24) ; 0 pour un paramètre de démonstration sans numéro.</summary>
    public int No { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Valeur numérique (booléens 0/1, énumérations : index) ; absente pour un paramètre libre.</summary>
    public double? Number { get; set; }
    public string Value { get; set; } = string.Empty;
    public string? Unit { get; set; }
}

public sealed class ObjectNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Code du type du catalogue (« CartesianRobot », « LinearAxis »…).</summary>
    public string TypeCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ChildRole { get; set; }
    public bool Simulated { get; set; }
    public bool Disabled { get; set; }
    public double OverridePercent { get; set; } = 100;
    /// <summary>Raccordements des signaux S1..S8 à l'image E/S (docs/06 § 1).</summary>
    public List<SignalBinding> Signals { get; } = new List<SignalBinding>();
    /// <summary>Élément du pool matériel (axe NC, esclave EtherCAT, réseau, planaire), si le type en demande un.</summary>
    public int? DeviceIndex { get; set; }
    public List<SpeedDef> Speeds { get; } = new List<SpeedDef>();
    /// <summary>Timeouts propres à cet objet (0 ou absent = défaut du catalogue).</summary>
    public List<TimeoutOverride> Timeouts { get; } = new List<TimeoutOverride>();
    public List<ParamValue> Params { get; } = new List<ParamValue>();
    public List<ObjectNode> Children { get; } = new List<ObjectNode>();
    /// <summary>Position dans la vue de dessus (mm dans la zone de travail de la station).</summary>
    public double LayoutX { get; set; }
    public double LayoutY { get; set; }

    public IEnumerable<ObjectNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.SelfAndDescendants())
                yield return d;
    }
}

public sealed record SignalBinding(int No, Guid? ChannelId);

public sealed record SpeedDef(int No, string Name, double Percent);

public sealed record TimeoutOverride(int ActionNo, int Ms);

public enum IoKind
{
    DI,
    DO,
    AI,
    AO,
}

/// <summary>Voie de l'image E/S (importée du CSV TwinCAT) : index dans le tableau de son type.</summary>
public sealed class IoChannel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public IoKind Kind { get; set; }
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Terminal { get; set; }
}

public enum HardwareKind
{
    Axis,
    EtherCat,
    Network,
    Planar,
}

/// <summary>Élément d'un pool matériel (docs/04 § 5) : axe NC, esclave EtherCAT, connexion réseau, groupe planaire.</summary>
public sealed class HardwareItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public HardwareKind Kind { get; set; }
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
}

public enum PressTarget
{
    Stroke,
    Force,
}

public sealed class PressSegmentConfig
{
    public PressTarget Target { get; set; }
    public double TargetValue { get; set; }
    public double SpeedPercent { get; set; } = 50;
    public double ForceMin { get; set; }
    public double ForceMax { get; set; }
    public double StrokeMin { get; set; }
    public double StrokeMax { get; set; }
}

/// <summary>Recette de presse (16 au plus par presse, 8 segments au plus).</summary>
public sealed class PressRecipeConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ObjectId { get; set; }
    public int No { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Vérifiée sur la presse réelle (V102 sinon).</summary>
    public bool Verified { get; set; }
    public List<PressSegmentConfig> Segments { get; } = new List<PressSegmentConfig>();
}

public sealed class Interlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ObjectId { get; set; }
    /// <summary>Actions verrouillées ; vide si <see cref="AllMotion"/>.</summary>
    public List<int> Actions { get; } = new List<int>();
    public bool AllMotion { get; set; }
    public Condition Condition { get; set; } = new Condition();
    /// <summary>Généré par un module (robot : Z en sécurité), non modifiable.</summary>
    public bool Generated { get; set; }
}

public enum QualityReaction
{
    WarnOnly,
    StationHold,
    MoverOut,
    StopLoading,
    MachineStop,
}

/// <summary>Seuils d'une portée de la surveillance des rebuts (docs/10 § 5) ; 0 désactive un seuil.</summary>
public sealed class QualityRule
{
    public QualityReaction ReactionConsec { get; set; } = QualityReaction.StationHold;
    public QualityReaction ReactionRate { get; set; } = QualityReaction.StationHold;
    public int ConsecWarn { get; set; }
    public int ConsecAction { get; set; }
    public int Window { get; set; }
    public int MinSample { get; set; }
    public double RateWarnPercent { get; set; }
    public double RateActionPercent { get; set; }
}

public sealed class QualityRuleOverride
{
    /// <summary>Station ou fonction concernée.</summary>
    public Guid TargetId { get; set; }
    public QualityRule Rule { get; set; } = new QualityRule();
}

public sealed class QualityConfig
{
    public QualityRule StationObserved { get; set; } = new QualityRule { ConsecWarn = 2, ConsecAction = 3, Window = 50, MinSample = 20, RateWarnPercent = 5, RateActionPercent = 10 };
    public QualityRule StationImputed { get; set; } = new QualityRule { ConsecWarn = 3, ConsecAction = 5, Window = 50, MinSample = 20, RateWarnPercent = 5, RateActionPercent = 10 };
    public QualityRule Function { get; set; } = new QualityRule { ReactionConsec = QualityReaction.StopLoading, ReactionRate = QualityReaction.StopLoading, ConsecAction = 5, Window = 100, MinSample = 30, RateWarnPercent = 4, RateActionPercent = 8 };
    public QualityRule Mover { get; set; } = new QualityRule { ReactionConsec = QualityReaction.MoverOut, ReactionRate = QualityReaction.MoverOut, ConsecWarn = 2, ConsecAction = 3, Window = 20, MinSample = 10, RateWarnPercent = 10, RateActionPercent = 20 };
    public QualityRule Machine { get; set; } = new QualityRule { ReactionConsec = QualityReaction.MachineStop, ReactionRate = QualityReaction.StopLoading, ConsecWarn = 3, ConsecAction = 5, Window = 200, MinSample = 50, RateWarnPercent = 3, RateActionPercent = 5 };
    public double OrderBudgetWarnPercent { get; set; } = 80;
    public double OrderBudgetActionPercent { get; set; } = 100;
    public List<QualityRuleOverride> StationOverrides { get; } = new List<QualityRuleOverride>();
    public List<QualityRuleOverride> FunctionOverrides { get; } = new List<QualityRuleOverride>();
}

public sealed class NamedPosition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double C { get; set; }
    public int Frame { get; set; }
    public double? Approach { get; set; }
    public bool Taught { get; set; }
    public DateTime? TaughtAt { get; set; }
    public string? TaughtBy { get; set; }
}

public sealed class StationVariable
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    /// <summary>Number, Bool, Position.</summary>
    public string Type { get; set; } = "Number";
    public string Initial { get; set; } = "0";
}

public sealed class StationConfig
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public int Number { get; set; }
    public double OverrideDefault { get; set; } = 100;
    public double PlanX { get; set; }
    public double PlanY { get; set; }
    public List<ObjectNode> Objects { get; } = new List<ObjectNode>();
    public List<NamedPosition> Positions { get; } = new List<NamedPosition>();
    public List<StationVariable> Variables { get; } = new List<StationVariable>();
    public List<ProcessDefinition> Processes { get; } = new List<ProcessDefinition>();
    public List<CustomFunction> CustomFunctions { get; } = new List<CustomFunction>();
    public List<PressRecipeConfig> PressRecipes { get; } = new List<PressRecipeConfig>();
    public List<Interlock> Interlocks { get; } = new List<Interlock>();

    public IEnumerable<ObjectNode> AllObjects() => Objects.SelectMany(o => o.SelfAndDescendants());

    public ObjectNode? FindObject(Guid id) => AllObjects().FirstOrDefault(o => o.Id == id);
}

public sealed class MachineConfig
{
    public PlanarConfig Planar { get; } = new PlanarConfig();
    public List<PlanarPosition> PlanarPositions { get; } = new List<PlanarPosition>();
    public List<GroupConfig> Groups { get; } = new List<GroupConfig>();
    public Guid? RejectGroupId { get; set; }
    public int NoStationDelaySeconds { get; set; } = 60;
    public List<PartDataDef> PartData { get; } = new List<PartDataDef>();
    public List<ObjectNode> MachineObjects { get; } = new List<ObjectNode>();
    public List<IoChannel> IoImage { get; } = new List<IoChannel>();
    public List<HardwareItem> HardwarePools { get; } = new List<HardwareItem>();
    public QualityConfig Quality { get; } = new QualityConfig();
}

public sealed class MachineProject
{
    /// <summary>Version courante du schéma du fichier projet ; une migration par version (docs/07 § 5.1).</summary>
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public int CatalogVersion { get; set; } = 1;
    public DateTime CreatedAt { get; set; }
    public DateTime ModifiedAt { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public MachineConfig Machine { get; } = new MachineConfig();
    public List<StationConfig> Stations { get; } = new List<StationConfig>();
    public ProcessDefinition GeneralProcess { get; set; } = new ProcessDefinition { Kind = ProcessKind.General, Name = "Process général" };

    public StationConfig? Station(int number) => Stations.FirstOrDefault(s => s.Number == number);

    public StationConfig? StationById(Guid id) => Stations.FirstOrDefault(s => s.Id == id);

    public GroupConfig? Group(Guid id) => Machine.Groups.FirstOrDefault(g => g.Id == id);

    /// <summary>Fonctions dont la station est membre (une station appartient à une ou plusieurs fonctions).</summary>
    public IEnumerable<GroupConfig> GroupsOf(StationConfig station) => Machine.Groups.Where(g => g.StationIds.Contains(station.Id));
}

/// <summary>
/// Fonction personnalisée d'un module (docs/02 § 3, docs/07 § 3.1) : copie ou création d'une suite d'actions de base du module,
/// avec ses propres paramètres. Dépliée à la compilation comme une fonction de module du catalogue.
/// </summary>
public sealed class CustomFunction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ModuleObjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    /// <summary>Fonction du catalogue dont elle est la copie (« Prendre pièce »), ou null si elle est créée de zéro.</summary>
    public string? BasedOn { get; set; }
    public List<ParamDef> Params { get; } = new List<ParamDef>();
    public Sequence Body { get; set; } = new Sequence();

    /// <summary>Identifiant utilisé dans <see cref="FunctionStep.FunctionId"/>.</summary>
    [JsonIgnore]
    public string FunctionId => "custom:" + Id.ToString("N");
}
