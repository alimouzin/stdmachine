using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using StdMachine.Core;

namespace StdMachine.Persistence;

/// <summary>
/// Fichier projet <c>.stdm.json</c> (docs/07 § 5.1) : JSON indenté, propriétés dans un ordre stable, <c>schemaVersion</c>,
/// une migration par version de schéma.
/// </summary>
public static class ProjectSerializer
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(MachineProject project)
    {
        var json = JsonSerializer.Serialize(project, Options);
        // Fin de ligne unique pour que les fichiers se comparent dans Git quel que soit le poste.
        return json.Replace("\r\n", "\n") + "\n";
    }

    public static MachineProject Deserialize(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("Le projet n'est pas un objet JSON.");
        int version = node["schemaVersion"]?.GetValue<int>() ?? 0;
        if (version > MachineProject.CurrentSchemaVersion)
            throw new InvalidDataException("Projet de version " + version + " : cette HMI connaît la version " + MachineProject.CurrentSchemaVersion + " au plus.");
        Migrations.Upgrade(node, version);
        return node.Deserialize<MachineProject>(Options) ?? throw new InvalidDataException("Projet vide.");
    }

    public static void Save(MachineProject project, string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        // Écriture par fichier temporaire : une coupure ne laisse jamais un projet tronqué.
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, Serialize(project), new System.Text.UTF8Encoding(false));
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }

    public static MachineProject Load(string path) => Deserialize(File.ReadAllText(path));
}

/// <summary>Une migration par version de schéma : <c>Upgrade</c> applique celles qui manquent, dans l'ordre.</summary>
public static class Migrations
{
    // Index = version de départ ; la migration n transforme la version n en n + 1.
    private static readonly Action<JsonObject>[] Steps =
    {
        // 0 → 1 : fichiers sans schemaVersion (premiers essais) : rien à transformer.
        _ => { },
    };

    public static void Upgrade(JsonObject project, int fromVersion)
    {
        for (int v = fromVersion; v < MachineProject.CurrentSchemaVersion; v++)
        {
            Steps[v](project);
            project["schemaVersion"] = v + 1;
        }
    }
}
