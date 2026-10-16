using System.Windows;
using System.Windows.Media;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Controls;

public enum ButtonVariant
{
    Secondary,
    Primary,
    Ghost,
    Danger,
}

/// <summary>Propriétés attachées communes des composants (variante, icône, ton, état « en cours »).</summary>
public static class Ui
{
    public static readonly DependencyProperty VariantProperty = DependencyProperty.RegisterAttached(
        "Variant", typeof(ButtonVariant), typeof(Ui), new FrameworkPropertyMetadata(ButtonVariant.Secondary));
    public static ButtonVariant GetVariant(DependencyObject o) => (ButtonVariant)o.GetValue(VariantProperty);
    public static void SetVariant(DependencyObject o, ButtonVariant v) => o.SetValue(VariantProperty, v);

    /// <summary>Nom Material Symbols de l'icône du bouton.</summary>
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null, OnIconChanged));
    public static string? GetIcon(DependencyObject o) => (string?)o.GetValue(IconProperty);
    public static void SetIcon(DependencyObject o, string? v) => o.SetValue(IconProperty, v);

    /// <summary>Caractère de l'icône, calculé (utilisé par les gabarits).</summary>
    public static readonly DependencyProperty IconCharProperty = DependencyProperty.RegisterAttached(
        "IconChar", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(string.Empty));
    public static string GetIconChar(DependencyObject o) => (string)o.GetValue(IconCharProperty);
    public static void SetIconChar(DependencyObject o, string v) => o.SetValue(IconCharProperty, v);

    public static readonly DependencyProperty IsBusyProperty = DependencyProperty.RegisterAttached(
        "IsBusy", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false));
    public static bool GetIsBusy(DependencyObject o) => (bool)o.GetValue(IsBusyProperty);
    public static void SetIsBusy(DependencyObject o, bool v) => o.SetValue(IsBusyProperty, v);

    /// <summary>Ton du liseré d'un CommandButton « disponible » (couleur de l'état cible).</summary>
    public static readonly DependencyProperty ToneProperty = DependencyProperty.RegisterAttached(
        "Tone", typeof(Tone), typeof(Ui), new FrameworkPropertyMetadata(Tone.Neutral, OnToneChanged));
    public static Tone GetTone(DependencyObject o) => (Tone)o.GetValue(ToneProperty);
    public static void SetTone(DependencyObject o, Tone v) => o.SetValue(ToneProperty, v);

    /// <summary>Brosse du ton (point), calculée depuis <see cref="ToneProperty"/>.</summary>
    public static readonly DependencyProperty ToneBrushProperty = DependencyProperty.RegisterAttached(
        "ToneBrush", typeof(Brush), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static Brush? GetToneBrush(DependencyObject o) => (Brush?)o.GetValue(ToneBrushProperty);
    public static void SetToneBrush(DependencyObject o, Brush? v) => o.SetValue(ToneBrushProperty, v);

    /// <summary>Niveau requis affiché par un cadenas (« Niveau Régleur requis »), null = aucun.</summary>
    public static readonly DependencyProperty LockedProperty = DependencyProperty.RegisterAttached(
        "Locked", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(null));
    public static string? GetLocked(DependencyObject o) => (string?)o.GetValue(LockedProperty);
    public static void SetLocked(DependencyObject o, string? v) => o.SetValue(LockedProperty, v);

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => d.SetValue(IconCharProperty, Icon.Char((string?)e.NewValue));

    private static void OnToneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => d.SetValue(ToneBrushProperty, ToneBrushes.Dot((Tone)e.NewValue));
}
