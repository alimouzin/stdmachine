namespace StdMachine.Hmi.Editor;

/// <summary>
/// Réglages d'affichage de l'éditeur de process. Prototype : le mode « Flux » (une seule colonne, sans couloirs) se compare
/// au mode « Couloirs » ; le modèle et le compilateur ne changent pas (docs/13-proposition-couloirs-et-sorties.md).
/// </summary>
public static class EditorSettings
{
    /// <summary>Vrai : flux vertical sans couloirs ; faux : un couloir par objet ou par fonction.</summary>
    public static bool FlowLayout { get; set; }
}
