using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Controls;

/// <summary>Convertisseur de direction des barres de défilement (vertical = inversé).</summary>
public sealed class ScrollBarDirectionConverter : IValueConverter
{
    public static readonly ScrollBarDirectionConverter Instance = new ScrollBarDirectionConverter();
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => value is Orientation o && o == Orientation.Vertical;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Contrôle qui porte un <see cref="Tone"/> et en expose les brosses calculées au gabarit.</summary>
public abstract class ToneControl : Control
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(Tone), typeof(ToneControl), new PropertyMetadata(Tone.Neutral, (d, _) => ((ToneControl)d).RefreshTone()));

    public static readonly DependencyProperty ToneTextProperty = DependencyProperty.Register(
        nameof(ToneText), typeof(Brush), typeof(ToneControl), new PropertyMetadata(null));
    public static readonly DependencyProperty ToneBgProperty = DependencyProperty.Register(
        nameof(ToneBg), typeof(Brush), typeof(ToneControl), new PropertyMetadata(null));
    public static readonly DependencyProperty ToneDotProperty = DependencyProperty.Register(
        nameof(ToneDot), typeof(Brush), typeof(ToneControl), new PropertyMetadata(null));

    protected ToneControl()
    {
        RefreshTone();
    }

    public Tone Tone { get => (Tone)GetValue(ToneProperty); set => SetValue(ToneProperty, value); }
    public Brush? ToneText { get => (Brush?)GetValue(ToneTextProperty); private set => SetValue(ToneTextProperty, value); }
    public Brush? ToneBg { get => (Brush?)GetValue(ToneBgProperty); private set => SetValue(ToneBgProperty, value); }
    public Brush? ToneDot { get => (Brush?)GetValue(ToneDotProperty); private set => SetValue(ToneDotProperty, value); }

    protected virtual void RefreshTone()
    {
        ToneText = ToneBrushes.Text(Tone);
        ToneBg = ToneBrushes.Bg(Tone);
        ToneDot = ToneBrushes.Dot(Tone);
    }
}

/// <summary>Pastille 24 px (Complet, Incomplet, Erreur, Simulé, Personnalisée…).</summary>
public class StatusPill : ToneControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(StatusPill), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IconNameProperty = DependencyProperty.Register(
        nameof(IconName), typeof(string), typeof(StatusPill), new PropertyMetadata(null, (d, e) => ((StatusPill)d).IconChar = Icon.Char((string?)e.NewValue)));
    public static readonly DependencyProperty IconCharProperty = DependencyProperty.Register(
        nameof(IconChar), typeof(string), typeof(StatusPill), new PropertyMetadata(string.Empty));

    static StatusPill() => DefaultStyleKeyProperty.OverrideMetadata(typeof(StatusPill), new FrameworkPropertyMetadata(typeof(StatusPill)));

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? IconName { get => (string?)GetValue(IconNameProperty); set => SetValue(IconNameProperty, value); }
    public string IconChar { get => (string)GetValue(IconCharProperty); private set => SetValue(IconCharProperty, value); }
}

/// <summary>Point 8 px (arbre, listes), pulse si Busy.</summary>
public class StatusDot : ToneControl
{
    public static readonly DependencyProperty PulseProperty = DependencyProperty.Register(
        nameof(Pulse), typeof(bool), typeof(StatusDot), new PropertyMetadata(false, (d, _) => ((StatusDot)d).UpdatePulse()));

    public StatusDot()
    {
        Width = 8; Height = 8;
        Loaded += (_, _) => UpdatePulse();
    }

    public bool Pulse { get => (bool)GetValue(PulseProperty); set => SetValue(PulseProperty, value); }

    private void UpdatePulse()
    {
        if (Pulse) Infrastructure.Anim.Pulse(this, OpacityProperty, 1.0, 0.35);
        else { Infrastructure.Anim.Stop(this, OpacityProperty); Opacity = 1; }
    }

    protected override void RefreshTone()
    {
        base.RefreshTone();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawEllipse(ToneDot ?? Brushes.Gray, null, new Point(ActualWidth / 2, ActualHeight / 2), ActualWidth / 2, ActualHeight / 2);
    }
}

