using System.IO;
using StdMachine.Core;
using StdMachine.Core.Catalog;
using StdMachine.Core.Compiler;

namespace StdMachine.Hmi.Services;

/// <summary>Catalogue chargé et compilation du projet courant (docs/07 § 4.1) : la validation, le listing et le déploiement s'appuient dessus.</summary>
public sealed class CompilationService
{
    public CompilationService(CatalogDocument? catalog) { Catalog = catalog; }

    public CatalogDocument? Catalog { get; }

    public bool Available => Catalog != null;

    /// <summary>Compile le projet ; null si le catalogue n'est pas disponible.</summary>
    public CompileResult? Compile(MachineProject project) => Catalog == null ? null : ProjectCompiler.Compile(project, Catalog);

    /// <summary>Charge <c>catalog/catalog.json</c> à côté de l'exécutable ; null si le fichier est absent ou illisible.</summary>
    public static CatalogDocument? TryLoadCatalog()
    {
        try
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalog", "catalog.json");
            return File.Exists(path) ? CatalogLoader.Load(path) : null;
        }
        catch (Exception) { return null; }
    }
}
