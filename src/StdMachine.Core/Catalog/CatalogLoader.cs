using System.Text.Json;

namespace StdMachine.Core.Catalog;

public static class CatalogLoader
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    public static CatalogDocument Parse(string json)
        => JsonSerializer.Deserialize<CatalogDocument>(json, Options) ?? throw new InvalidDataException("Catalogue vide.");

    public static CatalogDocument Load(string path) => Parse(File.ReadAllText(path));
}
