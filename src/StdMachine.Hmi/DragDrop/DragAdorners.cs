using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.DragDrop;

/// <summary>
/// Fantôme : copie visuelle de l'élément (opacité 90 %, ombre portée, rotation de 2°, décalé de 12 px sous le doigt)
/// dans la couche d'adorner au-dessus de toute la fenêtre (docs/07 § 8.3).
/// </summary>
internal sealed class GhostAdorner : Adorner
{
    private readonly Border _child;
    private readonly TranslateTransform _move = new TranslateTransform();

    public GhostAdorner(UIElement adorned, Visual source, Size size, Point startOffset) : base(adorned)
    {
        IsHitTestVisible = false;
        // Instantané pris avant que la source soit atténuée : le fantôme garde l'aspect normal de l'élément.
        var dpi = VisualTreeHelper.GetDpi(adorned);
        int pw = Math.Max(1, (int)Math.Ceiling(size.Width * dpi.DpiScaleX));
        int ph = Math.Max(1, (int)Math.Ceiling(size.Height * dpi.DpiScaleY));
        var snapshot = new RenderTargetBitmap(pw, ph, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        snapshot.Render(source);
        snapshot.Freeze();
        var rect = new Rectangle
        {
            Width = size.Width,
            Height = size.Height,
            Fill = new ImageBrush(snapshot),
        };
        _child = new Border
        {
            Child = rect,
            Opacity = 0.9,
            Effect = Tokens.Get<System.Windows.Media.Effects.Effect>("Shadow.Raised"),
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        var group = new TransformGroup();
        group.Children.Add(new RotateTransform(2));
        group.Children.Add(_move);
        _child.RenderTransform = group;
        AddVisualChild(_child);
        MoveTo(startOffset);
    }

    public Point Origin { get; private set; }

    public void MoveTo(Point topLeft)
    {
        Origin = topLeft;
        _move.X = topLeft.X;
        _move.Y = topLeft.Y;
    }

    /// <summary>Retour à la place d'origine en 200 ms, puis le fantôme disparaît.</summary>
    public void ReturnTo(Point target, Action done)
    {
        var ease = Tokens.Get<IEasingFunction>("Ease.Standard");
        var d = new Duration(TimeSpan.FromMilliseconds(200));
        var ax = new DoubleAnimation(target.X, d) { EasingFunction = ease };
        var ay = new DoubleAnimation(target.Y, d) { EasingFunction = ease };
        ay.Completed += (_, _) => done();
        _move.BeginAnimation(TranslateTransform.XProperty, ax);
        _move.BeginAnimation(TranslateTransform.YProperty, ay);
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _child;
    protected override Size MeasureOverride(Size constraint) { _child.Measure(constraint); return _child.DesiredSize; }
    protected override Size ArrangeOverride(Size finalSize) { _child.Arrange(new Rect(_child.DesiredSize)); return finalSize; }
}

/// <summary>Aperçu de dépôt : trait d'insertion, zone en surbrillance, ou zone refusée avec la raison sous le doigt.</summary>
internal sealed class DropIndicatorAdorner : Adorner
{
    private static readonly Typeface LabelFace = new Typeface("Segoe UI");
    private Rect _zone = Rect.Empty;
    private DropKind _kind;
    private bool _accepted;
    private string? _reason;
    private Point _pointer;

    public DropIndicatorAdorner(UIElement adorned) : base(adorned) { IsHitTestVisible = false; }

    public void Set(Rect zone, DropKind kind, bool accepted, string? reason, Point pointer)
    {
        _zone = zone; _kind = kind; _accepted = accepted; _reason = reason; _pointer = pointer;
        InvalidateVisual();
    }

    public void Clear()
    {
        _zone = Rect.Empty; _kind = DropKind.None; _reason = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (_zone.IsEmpty || _kind == DropKind.None) return;
        if (_accepted && _kind == DropKind.Line)
        {
            var accent = Tokens.Brush("Accent");
            var r = _zone;
            if (r.Height < 3) r = new Rect(r.X, r.Y - 1.5, r.Width, 3);
            if (r.Width < 3) r = new Rect(r.X - 1.5, r.Y, 3, r.Height);
            dc.DrawRoundedRectangle(accent, null, r, 1.5, 1.5);
            double cr = 4.5;
            if (r.Width >= r.Height)
            {
                dc.DrawEllipse(accent, null, new Point(r.Left, r.Top + r.Height / 2), cr, cr);
                dc.DrawEllipse(accent, null, new Point(r.Right, r.Top + r.Height / 2), cr, cr);
            }
            else
            {
                dc.DrawEllipse(accent, null, new Point(r.Left + r.Width / 2, r.Top), cr, cr);
                dc.DrawEllipse(accent, null, new Point(r.Left + r.Width / 2, r.Bottom), cr, cr);
            }
            return;
        }

        var fill = _accepted ? Tokens.Brush("Accent.Bg") : Tokens.Brush("Err.Bg");
        var edge = _accepted ? Tokens.Brush("Accent.Border") : Tokens.Brush("Err.Dot");
        var pen = new Pen(edge, 2) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) };
        dc.DrawRoundedRectangle(fill, pen, _zone, 8, 8);

        if (!_accepted && !string.IsNullOrEmpty(_reason))
        {
            var text = new FormattedText(_reason, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, LabelFace, 14, Brushes.White, 1.0);
            text.MaxTextWidth = 360;
            var box = new Rect(_pointer.X + 16, _pointer.Y + 44, text.Width + 24, text.Height + 16);
            var hostW = (AdornedElement as FrameworkElement)?.ActualWidth ?? 1920;
            if (box.Right > hostW - 8) box.X = hostW - 8 - box.Width;
            dc.DrawRoundedRectangle(Tokens.Brush("Err.Dot"), null, box, 8, 8);
            dc.DrawText(text, new Point(box.X + 12, box.Y + 8));
        }
    }
}
