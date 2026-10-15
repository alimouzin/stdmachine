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

    public static bool IsKnownModuleType(string type) => type == RobotXyz;

    public static IReadOnlyList<ActionDef> ForModule(string type) => type switch
    {
        RobotXyz => RobotXyzActions,
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"Type de module inconnu : {type}"),
    };
}
