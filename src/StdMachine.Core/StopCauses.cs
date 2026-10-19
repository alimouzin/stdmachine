namespace StdMachine.Core;

/// <summary>Causes d'arrêt de la machine dont l'interface et l'interface usine ont besoin de reconnaître le texte.</summary>
public static class StopCauses
{
    /// <summary>La machine s'est vidée d'elle-même après « Finir les pièces » (docs/02 § 8, docs/05 § 5.7).</summary>
    public const string Drained = "Machine vidée";
}
