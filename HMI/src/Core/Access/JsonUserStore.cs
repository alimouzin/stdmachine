using System.Text.Json;
using System.Text.Json.Serialization;

namespace Core.Access;

/// <summary>Stockage local en JSON. Fichier absent ou illisible : valeurs par défaut.</summary>
public sealed class JsonUserStore : IUserStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;

    public JsonUserStore(string path) => _path = path;

    public AccessSettings Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var settings = JsonSerializer.Deserialize<AccessSettings>(File.ReadAllText(_path), Options);
                if (settings is { Levels.Count: > 0, Users.Count: > 0 }) return settings;
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Retombe sur les valeurs par défaut.
        }
        return AccessSettings.CreateDefault();
    }

    public void Save(AccessSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        File.WriteAllText(_path, JsonSerializer.Serialize(settings, Options));
    }
}
