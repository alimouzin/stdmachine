using StdMachine.Core;
using StdMachine.Core.Catalog;
using StdMachine.Persistence;
using StdMachine.Simulation.Demo;

namespace StdMachine.Persistence.Tests;

internal static class Samples
{
    public static CatalogDocument Catalog() => CatalogLoader.Load(Path.Combine(AppContext.BaseDirectory, "catalog", "catalog.json"));

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "StdMachine.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("StdMachine.sln introuvable.");
    }

    public static string SamplePath => Path.Combine(RepoRoot(), "samples", "MX-4.stdm.json");

    public static string ValidationErrorsPath => Path.Combine(RepoRoot(), "samples", "MX-4.validation-errors.stdm.json");

    /// <summary>Compare le fichier au projet généré ; <c>STDM_UPDATE_SAMPLES=1</c> le réécrit.</summary>
    public static string ReadOrUpdate(string path, MachineProject generated)
    {
        string expected = ProjectSerializer.Serialize(generated);
        if (Environment.GetEnvironmentVariable("STDM_UPDATE_SAMPLES") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected, new System.Text.UTF8Encoding(false));
        }
        return expected;
    }
}
