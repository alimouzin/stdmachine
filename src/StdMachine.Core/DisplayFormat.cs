using System.Globalization;

namespace StdMachine.Core;

/// <summary>
/// Seul utilitaire de formatage des nombres, unités et durées affichés (CLAUDE.md) :
/// virgule décimale, signe « − », espace fine avant l'unité (docs/08 § 2.5).
/// </summary>
public static class DisplayFormat
{
    public const char Minus = '−';
    public const char ThinSpace = ' ';

    private static readonly CultureInfo Fr = CreateCulture();

    private static CultureInfo CreateCulture()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("fr-FR").Clone();
        c.NumberFormat.NumberGroupSeparator = ThinSpace.ToString();
        c.NumberFormat.NegativeSign = Minus.ToString();
        return c;
    }

    public static CultureInfo Culture => Fr;

    /// <summary>« 268,40 » ; négatif avec le signe typographique.</summary>
    public static string Number(double value, int decimals = 2)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) return "—";
        var s = value.ToString("F" + decimals, Fr);
        if (value == 0 || s.Trim('0', ',', Minus).Length == 0) s = s.TrimStart(Minus); // pas de « −0,00 »
        return s;
    }

    /// <summary>« 850 N » : espace fine insécable avant l'unité.</summary>
    public static string WithUnit(double value, string unit, int decimals = 0)
        => Number(value, decimals) + ThinSpace + unit;

    public static string Percent(double value, int decimals = 0) => WithUnit(value, "%", decimals);

    /// <summary>« 1,2 s » sous 60 s, puis « 2 min 05 s ».</summary>
    public static string Duration(TimeSpan d)
    {
        if (d < TimeSpan.Zero) d = TimeSpan.Zero;
        if (d.TotalSeconds < 60) return Number(d.TotalSeconds, 1) + ThinSpace + "s";
        if (d.TotalHours < 1) return ((int)d.TotalMinutes).ToString(Fr) + " min " + d.Seconds.ToString("00", Fr) + " s";
        return ((int)d.TotalHours).ToString(Fr) + " h " + d.Minutes.ToString("00", Fr);
    }

    public static string Duration(double seconds) => Duration(TimeSpan.FromSeconds(seconds));

    /// <summary>« 14:32:05 ».</summary>
    public static string Clock(DateTime t) => t.ToString("HH:mm:ss", Fr);

    /// <summary>« 14:32 ».</summary>
    public static string ClockShort(DateTime t) => t.ToString("HH:mm", Fr);

    /// <summary>Analyse d'une saisie : accepte la virgule et le point, le signe − et -.</summary>
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text!.Trim().Replace(Minus, '-').Replace(',', '.').Replace(ThinSpace.ToString(), "").Replace(" ", "");
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
