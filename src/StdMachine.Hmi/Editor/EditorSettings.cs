using System.IO;

namespace StdMachine.Hmi.Editor;

/// <summary>
/// Réglages d'affichage de l'éditeur de process. Prototype : le mode « Flux » (une seule colonne, sans couloirs) se compare
/// au mode « Couloirs » ; le modèle et le compilateur ne changent pas (docs/13-proposition-couloirs-et-sorties.md).
/// Le choix fait dans l'éditeur est conservé entre deux lancements (fichier dans %AppData%\StdMachine).
/// </summary>
public static class EditorSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StdMachine", "editor-layout.txt");

    private static bool _flow = Load();

    /// <summary>Vrai : flux vertical sans couloirs ; faux : un couloir par objet ou par fonction.</summary>
    public static bool FlowLayout
    {
        get => _flow;
        set { _flow = value; Save(); }
    }

    /// <summary>Impose le mode pour cette exécution seulement (option <c>--layout</c>, captures) sans toucher au choix enregistré.</summary>
    public static void Override(bool flow) => _flow = flow;

    private static bool Load()
    {
        try { return File.Exists(FilePath) && File.ReadAllText(FilePath).Trim() == "flow"; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, _flow ? "flow" : "lanes");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
