using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using StdMachine.Core;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Controls;

public enum BeaconSize
{
    Small,   // 16 x 56
    Medium,  // 24 x 96
}

/// <summary>
/// Balise lumineuse (docs/08 § 2.4) : reproduit la colonne réelle. Une couleur allumée a un halo ;
/// le clignotement suit <see cref="BlinkClock"/>, donc il est en phase sur tout l'écran.
/// </summary>
public class Beacon : FrameworkElement
{
    public static readonly DependencyProperty LightsProperty = DependencyProperty.Register(
        nameof(Lights), typeof(BeaconLights), typeof(Beacon), new FrameworkPropertyMetadata(BeaconLights.None, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty BlinkingProperty = DependencyProperty.Register(
        nameof(Blinking), typeof(BeaconLights), typeof(Beacon), new FrameworkPropertyMetadata(BeaconLights.None, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(BeaconSize), typeof(Beacon), new FrameworkPropertyMetadata(BeaconSize.Medium, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public Beacon()
    {
        Loaded += (_, _) => BlinkClock.Instance.PropertyChanged += OnBlink;
        Unloaded += (_, _) => BlinkClock.Instance.PropertyChanged -= OnBlink;
        SnapsToDevicePixels = true;
    }

    /// <summary>Couleurs allumées (fixes ou clignotantes).</summary>
    public BeaconLights Lights { get => (BeaconLights)GetValue(LightsProperty); set => SetValue(LightsProperty, value); }

    /// <summary>Sous-ensemble de <see cref="Lights"/> qui clignote.</summary>
    public BeaconLights Blinking { get => (BeaconLights)GetValue(BlinkingProperty); set => SetValue(BlinkingProperty, value); }

    public BeaconSize Size { get => (BeaconSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    private void OnBlink(object? sender, PropertyChangedEventArgs e)
    {
        if (Blinking != BeaconLights.None) InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
        => Size == BeaconSize.Small ? new Size(16, 56) : new Size(24, 96);

    protected override void OnRender(DrawingContext dc)
    {
        var w = DesiredSize.Width;
        var h = DesiredSize.Height;
        var housing = new Rect(0, 0, w, h);
        dc.DrawRoundedRectangle(Tokens.Brush("Bg.Sunken"), new Pen(Tokens.Brush("Border.Default"), 1), housing, w / 4, w / 4);

        var order = new[] { BeaconLights.Red, BeaconLights.Orange, BeaconLights.Green, BeaconLights.Blue };
        var pad = Size == BeaconSize.Small ? 2.0 : 3.0;
        var gap = Size == BeaconSize.Small ? 1.5 : 3.0;
        var lampH = (h - 2 * pad - gap * 3) / 4;
        var lampW = w - 2 * pad;
        var blinkOn = BlinkClock.Instance.IsOn;
        for (int i = 0; i < order.Length; i++)
        {
            var light = order[i];
            var y = pad + i * (lampH + gap);
            var rect = new Rect(pad, y, lampW, lampH);
            bool on = (Lights & light) != 0 && ((Blinking & light) == 0 || blinkOn);
            var color = ColorOf(light);
            if (on)
            {
                // Halo : flou 6 px de la même couleur à 40 % (approché par un dégradé radial).
                var halo = new RadialGradientBrush(
                    Color.FromArgb(102, color.R, color.G, color.B), Color.FromArgb(0, color.R, color.G, color.B));
                halo.Freeze();
                dc.DrawRectangle(halo, null, new Rect(rect.X - 6, rect.Y - 6, rect.Width + 12, rect.Height + 12));
                dc.DrawRoundedRectangle(new SolidColorBrush(color), null, rect, lampH / 3, lampH / 3);
            }
            else
            {
                dc.DrawRoundedRectangle(Tokens.Brush("Beacon.Off"), null, rect, lampH / 3, lampH / 3);
            }
        }
    }

    private static Color ColorOf(BeaconLights l)
    {
        switch (l)
        {
            case BeaconLights.Red: return Tokens.Color("Beacon.Red.Color");
            case BeaconLights.Orange: return Tokens.Color("Beacon.Orange.Color");
            case BeaconLights.Green: return Tokens.Color("Beacon.Green.Color");
            default: return Tokens.Color("Beacon.Blue.Color");
        }
    }
}
