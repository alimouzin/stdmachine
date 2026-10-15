using System.Text.Json;
using System.Text.Json.Serialization;
using Core.Model;

namespace Core.Config;

public sealed class MachineConfigException : Exception
{
    public MachineConfigException(string message) : base(message) { }
}

/// <summary>Charge la déclaration de la machine depuis un JSON et la valide.</summary>
public static class MachineLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    public static MachineDef LoadFile(string path) => Parse(File.ReadAllText(path));

    public static MachineDef Parse(string json)
    {
        MachineDef? machine;
        try
        {
            machine = JsonSerializer.Deserialize<MachineDef>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new MachineConfigException($"JSON de machine invalide : {ex.Message}");
        }
        if (machine is null) throw new MachineConfigException("JSON de machine vide.");
        Validate(machine);
        return machine;
    }

    public static void Validate(MachineDef machine)
    {
        if (machine.Stations.Count == 0) throw new MachineConfigException("La machine ne déclare aucune station.");

        RequireUnique(machine.Stations.Select(s => s.Id), "station");
        var stationIds = machine.Stations.Select(s => s.Id).ToHashSet();

        foreach (var station in machine.Stations)
        {
            if (string.IsNullOrWhiteSpace(station.Id)) throw new MachineConfigException("Station sans identifiant.");
            RequireUnique(station.Devices.Select(d => d.Id), $"device de {station.Id}");
            RequireUnique(station.Modules.Select(m => m.Id), $"module de {station.Id}");
            var deviceIds = station.Devices.Select(d => d.Id).ToHashSet();
            foreach (var module in station.Modules)
            {
                if (!ActionCatalog.IsKnownModuleType(module.Type))
                    throw new MachineConfigException($"Type de module inconnu « {module.Type} » dans {station.Id}.");
                foreach (var id in module.DeviceIds)
                    if (!deviceIds.Contains(id))
                        throw new MachineConfigException($"Le module {module.Id} de {station.Id} référence le device inconnu « {id} ».");
            }
        }

        RequireUnique(machine.Groups.Select(g => g.Id), "groupe");
        var grouped = new HashSet<string>();
        foreach (var group in machine.Groups)
            foreach (var id in group.StationIds)
            {
                if (!stationIds.Contains(id))
                    throw new MachineConfigException($"Le groupe {group.Id} référence la station inconnue « {id} ».");
                if (!grouped.Add(id))
                    throw new MachineConfigException($"La station « {id} » appartient à plusieurs groupes.");
            }
    }

    private static void RequireUnique(IEnumerable<string> ids, string what)
    {
        var seen = new HashSet<string>();
        foreach (var id in ids)
            if (!seen.Add(id)) throw new MachineConfigException($"Identifiant de {what} en double : « {id} ».");
    }
}
