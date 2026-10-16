using System.Windows;
using System.Windows.Media;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Controls;

/// <summary>Fenêtre glissante des résultats (docs/10 § 11.1) : un petit carré par pièce, le plus récent à droite, vert bon, rouge mauvais, gris vide.</summary>
public class QualityStrip : FrameworkElement
{
    public static readonly DependencyProperty WindowProperty = DependencyProperty.Register(
        nameof(Window), typeof(string), typeof(QualityStrip), new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty CapacityProperty = DependencyProperty.Register(
        nameof(Capacity), typeof(int), typeof(QualityStrip), new FrameworkPropertyMetadata(50, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));
    public static readonly DependencyProperty MarkerBadsProperty = DependencyProperty.Register(
        nameof(MarkerBads), typeof(int), typeof(QualityStrip), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>« 0 » bon, « 1 » mauvais, un caractère par résultat.</summary>
    public string Window { get => (string)GetValue(WindowProperty); set => SetValue(WindowProperty, value); }

    /// <summary>Nombre d'emplacements de la fenêtre (les premiers restent gris tant que l'échantillon se remplit).</summary>
    public int Capacity { get => (int)GetValue(CapacityProperty); set => SetValue(CapacityProperty, value); }

    /// <summary>Nombre de rebuts au seuil d'action, matérialisé par un repère vertical (0 = aucun).</summary>
    public int MarkerBads { get => (int)GetValue(MarkerBadsProperty); set => SetValue(MarkerBadsProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
        => new Size(double.IsInfinity(availableSize.Width) ? Capacity * 12 : availableSize.Width, 22);

    protected override void OnRender(DrawingContext dc)
    {
        int cap = Math.Max(1, Capacity);
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        double pitch = Math.Min(16, w / cap);
        double size = Math.Max(2, pitch - Math.Max(1.5, pitch * 0.18));
        double top = (h - Math.Min(size, h)) / 2;
        var good = Tokens.Brush("Ok.Dot");
        var bad = Tokens.Brush("Err.Dot");
        var empty = Tokens.Brush("Border.Default");
        string win = Window ?? string.Empty;
        int first = cap - win.Length;
        double x0 = w - cap * pitch;
        for (int i = 0; i < cap; i++)
        {
            Brush b = i < first ? empty : (win[i - first] == '1' ? bad : good);
            double opacity = i < first ? 0.55 : win[i - first] == '1' ? 1.0 : 0.55;
            dc.PushOpacity(opacity);
            dc.DrawRoundedRectangle(b, null, new Rect(x0 + i * pitch, top, size, Math.Min(size, h)), 2, 2);
            dc.Pop();
        }
    }
}
