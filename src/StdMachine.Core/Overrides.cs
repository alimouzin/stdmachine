namespace StdMachine.Core;

/// <summary>Overrides et timeouts (docs/02 § 13).</summary>
public static class Overrides
{
    /// <summary>Override effectif d'un objet : produit des overrides de tous les niveaux qui le contiennent (chacun de 0 à 100 %), en pourcentage.</summary>
    public static double Effective(params double[] levelsPercent) => Effective((IEnumerable<double>)levelsPercent);

    public static double Effective(IEnumerable<double> levelsPercent)
    {
        double f = 1.0;
        foreach (double l in levelsPercent) f *= Math.Max(0.0, Math.Min(100.0, l)) / 100.0;
        return f * 100.0;
    }

    /// <summary>L'objet est arrêté par l'override (affiché « Arrêté par override », ce n'est pas un défaut).</summary>
    public static bool IsStopped(double effectivePercent) => effectivePercent <= 0.0001;

    /// <summary>Timeout effectif d'une action de mouvement : nominal ÷ override effectif ; null à 0 % (timeout suspendu).</summary>
    public static double? EffectiveTimeoutMs(double nominalMs, double effectivePercent)
        => IsStopped(effectivePercent) ? (double?)null : nominalMs / (Math.Min(100.0, effectivePercent) / 100.0);
}
