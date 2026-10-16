using System.Windows;
using System.Windows.Media;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Controls;

/// <summary>
/// Composant « ProgressBar » de docs/08 § 3 : 4 px sous les cartes, 8 px dans les panneaux.
/// Remplissage simple (<see cref="Value"/>) ou composé (bonnes, mauvaises, en cours) ; bande qui glisse si indéterminé.
/// </summary>
public class ProgressStrip : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = Reg(nameof(Value), 0.0);
    public static readonly DependencyProperty GoodProperty = Reg(nameof(Good), 0.0);
    public static readonly DependencyProperty BadProperty = Reg(nameof(Bad), 0.0);
    public static readonly DependencyProperty ActiveProperty = Reg(nameof(Active), 0.0);
    public static readonly DependencyProperty PhaseProperty = Reg(nameof(Phase), 0.0);
    public static readonly DependencyProperty BarHeightProperty = DependencyProperty.Register(
        nameof(BarHeight), typeof(double), typeof(ProgressStrip), new FrameworkPropertyMetadata(4.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate), typeof(bool), typeof(ProgressStrip), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((ProgressStrip)d).UpdateAnimation()));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(ProgressStrip), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Reg(string name, double def) => DependencyProperty.Register(
        name, typeof(double), typeof(ProgressStrip), new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public ProgressStrip()
    {
        Loaded += (_, _) => UpdateAnimation();
    }

    /// <summary>Avancement simple, 0 à 1.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    /// <summary>Fractions du total (0 à 1) : bonnes (vert), mauvaises (rouge), en cours (bleu).</summary>
    public double Good { get => (double)GetValue(GoodProperty); set => SetValue(GoodProperty, value); }
    public double Bad { get => (double)GetValue(BadProperty); set => SetValue(BadProperty, value); }
    public double Active { get => (double)GetValue(ActiveProperty); set => SetValue(ActiveProperty, value); }
    public double Phase { get => (double)GetValue(PhaseProperty); set => SetValue(PhaseProperty, value); }
    public double BarHeight { get => (double)GetValue(BarHeightProperty); set => SetValue(BarHeightProperty, value); }
    public bool IsIndeterminate { get => (bool)GetValue(IsIndeterminateProperty); set => SetValue(IsIndeterminateProperty, value); }
    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }

    private void UpdateAnimation()
    {
        if (IsIndeterminate) Anim.Loop(this, PhaseProperty, 0.0, 1.0, 1400);
        else { Anim.Stop(this, PhaseProperty); }
    }

    protected override Size MeasureOverride(Size availableSize)
        => new Size(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, BarHeight);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = BarHeight, r = h / 2;
        var track = new Rect(0, 0, w, h);
        dc.DrawRoundedRectangle(Tokens.Brush("Border.Divider"), null, track, r, r);
        dc.PushClip(new RectangleGeometry(track, r, r));
        if (IsIndeterminate)
        {
            // Bande qui glisse de gauche à droite, 35 % de la largeur.
            double bw = w * 0.35;
            double x = -bw + (w + bw) * Phase;
            dc.DrawRectangle(Fill ?? Tokens.Brush("Accent"), null, new Rect(x, 0, bw, h));
        }
        else if (Good > 0 || Bad > 0 || Active > 0)
        {
            double x = 0;
            x = Segment(dc, x, Good, Tokens.Brush("Ok.Dot"), w, h);
            x = Segment(dc, x, Bad, Tokens.Brush("Err.Dot"), w, h);
            Segment(dc, x, Active, Tokens.Brush("Busy.Dot"), w, h);
        }
        else if (Value > 0)
        {
            dc.DrawRectangle(Fill ?? Tokens.Brush("Accent"), null, new Rect(0, 0, w * Math.Min(1, Value), h));
        }
        dc.Pop();
    }

    private static double Segment(DrawingContext dc, double x, double fraction, Brush brush, double w, double h)
    {
        if (fraction <= 0) return x;
        double sw = w * Math.Min(1, fraction);
        dc.DrawRectangle(brush, null, new Rect(x, 0, sw, h));
        return x + sw;
    }
}

/// <summary>Anneau d'avancement 24 / 40 px ; passe orange à 80 % du timeout effectif.</summary>
public class ProgressRing : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(ProgressRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RingSizeProperty = DependencyProperty.Register(
        nameof(RingSize), typeof(double), typeof(ProgressRing), new FrameworkPropertyMetadata(24.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty WarnAtProperty = DependencyProperty.Register(
        nameof(WarnAt), typeof(double), typeof(ProgressRing), new FrameworkPropertyMetadata(0.8, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeBrushProperty = DependencyProperty.Register(
        nameof(StrokeBrush), typeof(Brush), typeof(ProgressRing), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty IsIndeterminateProperty = DependencyProperty.Register(
        nameof(IsIndeterminate), typeof(bool), typeof(ProgressRing), new FrameworkPropertyMetadata(false, (d, _) => ((ProgressRing)d).UpdateSpin()));
    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(
        nameof(Angle), typeof(double), typeof(ProgressRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public ProgressRing() { Loaded += (_, _) => UpdateSpin(); }

    /// <summary>Avancement 0 à 1.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double RingSize { get => (double)GetValue(RingSizeProperty); set => SetValue(RingSizeProperty, value); }
    /// <summary>Seuil à partir duquel l'anneau passe orange (0,8 = 80 % du timeout).</summary>
    public double WarnAt { get => (double)GetValue(WarnAtProperty); set => SetValue(WarnAtProperty, value); }
    public Brush? StrokeBrush { get => (Brush?)GetValue(StrokeBrushProperty); set => SetValue(StrokeBrushProperty, value); }
    public bool IsIndeterminate { get => (bool)GetValue(IsIndeterminateProperty); set => SetValue(IsIndeterminateProperty, value); }
    public double Angle { get => (double)GetValue(AngleProperty); set => SetValue(AngleProperty, value); }

    private void UpdateSpin()
    {
        if (IsIndeterminate) Anim.Loop(this, AngleProperty, 0, 360, 1100);
        else Anim.Stop(this, AngleProperty);
    }

    protected override Size MeasureOverride(Size availableSize) => new Size(RingSize, RingSize);

    protected override void OnRender(DrawingContext dc)
    {
        double s = RingSize;
        double thick = s >= 40 ? 4 : 3;
        var center = new Point(s / 2, s / 2);
        double radius = (s - thick) / 2;
        dc.DrawEllipse(null, new Pen(Tokens.Brush("Border.Default"), thick), center, radius, radius);

        double v = IsIndeterminate ? 0.28 : Math.Max(0, Math.Min(1, Value));
        if (v <= 0) return;
        Brush brush = StrokeBrush ?? (v >= WarnAt && !IsIndeterminate ? Tokens.Brush("Warn.Dot") : Tokens.Brush("Busy.Dot"));
        var pen = new Pen(brush, thick) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        double start = IsIndeterminate ? Angle - 90 : -90;
        if (v >= 0.9999)
        {
            dc.DrawEllipse(null, pen, center, radius, radius);
            return;
        }
        var a0 = start * Math.PI / 180;
        var a1 = (start + 360 * v) * Math.PI / 180;
        var p0 = new Point(center.X + radius * Math.Cos(a0), center.Y + radius * Math.Sin(a0));
        var p1 = new Point(center.X + radius * Math.Cos(a1), center.Y + radius * Math.Sin(a1));
        var fig = new PathFigure { StartPoint = p0, IsClosed = false };
        fig.Segments.Add(new ArcSegment(p1, new Size(radius, radius), 0, v > 0.5, SweepDirection.Clockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        dc.DrawGeometry(null, pen, geo);
    }
}
