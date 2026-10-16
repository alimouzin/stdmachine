using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Controls;

/// <summary>Curseur tactile (souris, stylet, toucher) avec poignée de 40 px ; sert à l'override et aux réglages en pourcentage.</summary>
public class TouchSlider : FrameworkElement
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(TouchSlider),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, null, Coerce));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(TouchSlider), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(TouchSlider), new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(TouchSlider), new PropertyMetadata(1.0));

    private const double ThumbSize = 40;
    private bool _dragging;
    private TouchDevice? _touch;

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }

    private static object Coerce(DependencyObject d, object v)
    {
        var s = (TouchSlider)d;
        var x = (double)v;
        if (s.Step > 0) x = Math.Round(x / s.Step) * s.Step;
        return Math.Max(s.Minimum, Math.Min(s.Maximum, x));
    }

    protected override Size MeasureOverride(Size availableSize)
        => new Size(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, ThumbSize);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, cy = ActualHeight / 2;
        double span = Math.Max(1, Maximum - Minimum);
        double x = ThumbSize / 2 + (w - ThumbSize) * (Value - Minimum) / span;
        dc.DrawRoundedRectangle(Tokens.Brush("Border.Default"), null, new Rect(ThumbSize / 2, cy - 4, w - ThumbSize, 8), 4, 4);
        dc.DrawRoundedRectangle(Tokens.Brush("Accent"), null, new Rect(ThumbSize / 2, cy - 4, x - ThumbSize / 2, 8), 4, 4);
        dc.DrawEllipse(Tokens.Brush("Bg.Surface"), new Pen(_dragging ? Tokens.Brush("Accent") : Tokens.Brush("Border.Strong"), _dragging ? 3 : 2), new Point(x, cy), ThumbSize / 2 - 2, ThumbSize / 2 - 2);
    }

    private void SetFrom(double px)
    {
        double span = Maximum - Minimum;
        double t = (px - ThumbSize / 2) / Math.Max(1, ActualWidth - ThumbSize);
        Value = Minimum + span * Math.Max(0, Math.Min(1, t));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return;
        _dragging = true; CaptureMouse(); SetFrom(e.GetPosition(this).X); InvalidateVisual(); e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging && _touch == null) SetFrom(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_touch != null) return;
        _dragging = false; ReleaseMouseCapture(); InvalidateVisual();
    }

    protected override void OnTouchDown(TouchEventArgs e)
    {
        if (_touch != null || !CaptureTouch(e.TouchDevice)) return;
        _touch = e.TouchDevice; _dragging = true;
        SetFrom(e.GetTouchPoint(this).Position.X); InvalidateVisual(); e.Handled = true;
    }

    protected override void OnTouchMove(TouchEventArgs e)
    {
        if (_touch == e.TouchDevice) { SetFrom(e.GetTouchPoint(this).Position.X); e.Handled = true; }
    }

    protected override void OnTouchUp(TouchEventArgs e)
    {
        if (_touch != e.TouchDevice) return;
        ReleaseTouchCapture(e.TouchDevice); _touch = null; _dragging = false; InvalidateVisual(); e.Handled = true;
    }
}
