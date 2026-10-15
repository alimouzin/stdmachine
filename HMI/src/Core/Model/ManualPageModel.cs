namespace Core.Model;

/// <summary>Un device ou un module de la page manuelle, avec ses actions.</summary>
public sealed record ManualTarget(string Id, string Name, string Kind, bool IsModule, IReadOnlyList<ActionDef> Actions);

/// <summary>
/// Page manuelle d'une station, entièrement déduite de la déclaration (niveaux 1 à 3) :
/// aucun code spécifique par station.
/// </summary>
public sealed record ManualPageModel(string StationId, string StationName, IReadOnlyList<ManualTarget> Devices, IReadOnlyList<ManualTarget> Modules)
{
    public static ManualPageModel Build(StationDef station) => new(
        station.Id,
        station.Name,
        station.Devices.Select(d => new ManualTarget(d.Id, d.Name, KindLabel(d.Kind), false, ActionCatalog.ForDevice(d.Kind))).ToList(),
        station.Modules.Select(m => new ManualTarget(m.Id, m.Name, ModuleLabel(m), true, ActionCatalog.ForModule(m.Type))).ToList());

    private static string KindLabel(DeviceKind kind) => kind switch
    {
        DeviceKind.Cylinder => "Vérin",
        DeviceKind.LinearAxis => "Axe linéaire",
        DeviceKind.VacuumBlow => "Aspi/soufflage",
        _ => kind.ToString(),
    };

    private static string ModuleLabel(ModuleDef module) => $"Module · {module.DeviceIds.Count} devices";
}
