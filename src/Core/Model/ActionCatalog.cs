namespace Core.Model;

/// <summary>
/// Seul endroit qui décrit les actions disponibles : la page manuelle et le simulateur s'y réfèrent.
/// </summary>
public static class ActionCatalog
{
    public const string RobotXyz = "RobotXyz";

    private static readonly IReadOnlyList<ActionDef> CylinderActions = new[]
    {
        new ActionDef("moveWork", "Move travail"),
        new ActionDef("moveHome", "Move repos"),
    };

    private static readonly IReadOnlyList<ActionDef> AxisActions = new[]
    {
        new ActionDef("powerOnInit", "Power on / init"),
        new ActionDef("move", "Move"),
    };

    private static readonly IReadOnlyList<ActionDef> VacuumActions = new[]
    {
        new ActionDef("suck", "Aspirer"),
        new ActionDef("blow", "Souffler"),
        new ActionDef("off", "Couper aspi/soufflage"),
    };

    private static readonly IReadOnlyList<ActionDef> RobotXyzActions = new[]
    {
        new ActionDef("goto", "Goto"),
        new ActionDef("pick", "Prise"),
        new ActionDef("place", "Place"),
    };

    public static IReadOnlyList<ActionDef> ForDevice(DeviceKind kind) => kind switch
    {
        DeviceKind.Cylinder => CylinderActions,
        DeviceKind.LinearAxis => AxisActions,
        DeviceKind.VacuumBlow => VacuumActions,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>Suite d'actions de base d'une action de module (la prise standard du doc 02 pour "pick").</summary>
    public static IReadOnlyList<string> ModuleActionSteps(string type, string actionId) => (type, actionId) switch
    {
        (RobotXyz, "goto") => new[] { "Move XY", "Move Z" },
        (RobotXyz, "pick") => new[] { "Move XY", "Move Z", "Aspirer", "Move Z final" },
        (RobotXyz, "place") => new[] { "Move XY", "Move Z", "Souffler", "Move Z final" },
        _ => throw new ArgumentOutOfRangeException(nameof(actionId), $"Action de module inconnue : {type}/{actionId}"),
    };

    public static bool IsKnownModuleType(string type) => type == RobotXyz;

    public static IReadOnlyList<ActionDef> ForModule(string type) => type switch
    {
        RobotXyz => RobotXyzActions,
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"Type de module inconnu : {type}"),
    };
}
