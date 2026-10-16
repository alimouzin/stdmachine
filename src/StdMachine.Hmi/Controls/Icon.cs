using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Controls;

/// <summary>
/// Icône Material Symbols Outlined affichée par son point de code (table nom → code générée depuis la police),
/// jamais par ligature (docs/07 § 1).
/// </summary>
public class Icon : TextBlock
{
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(Icon), new PropertyMetadata(null, (d, _) => ((Icon)d).Update()));

    public Icon()
    {
        IsHitTestVisible = false;
        FontSize = 24;
        TextAlignment = TextAlignment.Center;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        SetResourceReference(FontFamilyProperty, "Font.Icons");
    }

    /// <summary>Nom Material Symbols (par exemple « play_circle »).</summary>
    public string? Glyph { get => (string?)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }

    /// <summary>Texte à afficher pour un nom d'icône ; vide si le nom est inconnu.</summary>
    public static string Char(string? name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        return IconCodes.Map.TryGetValue(name!, out var code) ? char.ConvertFromUtf32(code) : "?";
    }

    private void Update() => Text = Char(Glyph);
}
