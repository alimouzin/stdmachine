using System.Windows;
using System.Windows.Media;

namespace StdMachine.Hmi.Theme;

/// <summary>Sens d'une couleur (docs/08 § 1 principe 2 et § 2.2).</summary>
public enum Tone
{
    Neutral,
    Ok,
    Warn,
    Err,
    Busy,
    Sim,
    Step,
    Maint,
    Accent,
}

public static class ToneBrushes
{
    public static Brush Text(Tone t) => Get(t + ".Text");
    public static Brush Bg(Tone t) => Get(t + ".Bg");
    public static Brush Dot(Tone t) => Get(t + ".Dot");

    public static Color DotColor(Tone t) => ((SolidColorBrush)Dot(t)).Color;

    /// <summary>Recherche une ressource de l'application (jetons de Tokens.xaml).</summary>
    public static Brush Get(string key)
    {
        if (t_cache.TryGetValue(key, out var b)) return b;
        var res = Application.Current?.TryFindResource(key) as Brush ?? Brushes.Magenta; // magenta = jeton manquant, visible
        t_cache[key] = res;
        return res;
    }

    public static Brush Accent => Get("Accent");
    private static readonly Dictionary<string, Brush> t_cache = new Dictionary<string, Brush>();
}

/// <summary>Accès typé aux ressources de jetons depuis le code.</summary>
public static class Tokens
{
    public static T Get<T>(string key) where T : class
        => (Application.Current?.TryFindResource(key) as T) ?? throw new KeyNotFoundException("Jeton introuvable : " + key);

    public static double Number(string key)
        => Application.Current?.TryFindResource(key) is double d ? d : throw new KeyNotFoundException("Jeton introuvable : " + key);

    public static Brush Brush(string key) => ToneBrushes.Get(key);
    public static Color Color(string key)
        => Application.Current?.TryFindResource(key) is Color c ? c : throw new KeyNotFoundException("Jeton introuvable : " + key);
    public static CornerRadius Radius(string key)
        => Application.Current?.TryFindResource(key) is CornerRadius r ? r : throw new KeyNotFoundException("Jeton introuvable : " + key);
    public static FontFamily Font(string key) => Get<FontFamily>(key);
}
