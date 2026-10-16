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
    public double WidthMm => Columns * TileMm;
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
    public string Name { get; set; } = string.Empty;
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
}

public sealed class MachineProject
{
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
    public string FunctionId => "custom:" + Id.ToString("N");
}
