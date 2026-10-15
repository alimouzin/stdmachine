namespace Core.Model;

/// <summary>Action disponible sur un device ou un module (niveau 2 et 3).</summary>
public sealed record ActionDef(string Id, string Label);

/// <summary>Élément physique déclaré dans une station (niveau 1).</summary>
public sealed class DeviceDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public DeviceKind Kind { get; set; }
}

/// <summary>Groupe de devices offrant des fonctions de plus haut niveau (niveau 3).</summary>
public sealed class ModuleDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    /// <summary>Type de module connu du catalogue, par exemple "RobotXyz".</summary>
    public string Type { get; set; } = "";
    public List<string> DeviceIds { get; set; } = new();
}

public sealed class StationDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public StationType Type { get; set; }
    public List<DeviceDef> Devices { get; set; } = new();
    public List<ModuleDef> Modules { get; set; } = new();
}

/// <summary>Stations de même fonction ; le process référence la fonction, pas une station.</summary>
public sealed class GroupDef
{
    public string Id { get; set; } = "";
    public string Function { get; set; } = "";
    public List<string> StationIds { get; set; } = new();
}

public sealed class MachineDef
{
    public string Name { get; set; } = "";
    public List<StationDef> Stations { get; set; } = new();
    public List<GroupDef> Groups { get; set; } = new();
}
