using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using StdMachine.Core;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Resources;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Controls;

public enum BadgeSize
{
    Small,   // 24 px
    Medium,  // 32 px
    Large,   // 48 px avec description
}

/// <summary>Badge d'état PackML (docs/08 § 2.3 et § 3) : couleur, icône et mot, jamais la couleur seule.</summary>
public class StateBadge : Control
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(PackMlState), typeof(StateBadge), new PropertyMetadata(PackMlState.Stopped, (d, e) => ((StateBadge)d).OnStateChanged((PackMlState)e.OldValue)));
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(BadgeSize), typeof(StateBadge), new PropertyMetadata(BadgeSize.Medium, (d, _) => ((StateBadge)d).Refresh()));

    private static readonly DependencyPropertyKey BackKey = DependencyProperty.RegisterReadOnly(nameof(Back), typeof(Brush), typeof(StateBadge), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey ForeKey = DependencyProperty.RegisterReadOnly(nameof(Fore), typeof(Brush), typeof(StateBadge), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey EdgeKey = DependencyProperty.RegisterReadOnly(nameof(Edge), typeof(Brush), typeof(StateBadge), new PropertyMetadata(null));
    private static readonly DependencyPropertyKey LabelKey = DependencyProperty.RegisterReadOnly(nameof(Label), typeof(string), typeof(StateBadge), new PropertyMetadata(string.Empty));
    private static readonly DependencyPropertyKey DescriptionKey = DependencyProperty.RegisterReadOnly(nameof(Description), typeof(string), typeof(StateBadge), new PropertyMetadata(string.Empty));
    private static readonly DependencyPropertyKey IconCharKey = DependencyProperty.RegisterReadOnly(nameof(IconChar), typeof(string), typeof(StateBadge), new PropertyMetadata(string.Empty));
    private static readonly DependencyPropertyKey IconSizeKey = DependencyProperty.RegisterReadOnly(nameof(IconSize), typeof(double), typeof(StateBadge), new PropertyMetadata(18.0));
    private static readonly DependencyPropertyKey CornerKey = DependencyProperty.RegisterReadOnly(nameof(BadgeCorner), typeof(CornerRadius), typeof(StateBadge), new PropertyMetadata(new CornerRadius(12)));
    public static readonly DependencyProperty BadgeCornerProperty = CornerKey.DependencyProperty;
    private static readonly DependencyPropertyKey ShowDescriptionKey = DependencyProperty.RegisterReadOnly(nameof(ShowDescription), typeof(bool), typeof(StateBadge), new PropertyMetadata(false));

    public static readonly DependencyProperty BackProperty = BackKey.DependencyProperty;
    public static readonly DependencyProperty ForeProperty = ForeKey.DependencyProperty;
    public static readonly DependencyProperty EdgeProperty = EdgeKey.DependencyProperty;
    public static readonly DependencyProperty LabelProperty = LabelKey.DependencyProperty;
    public static readonly DependencyProperty DescriptionProperty = DescriptionKey.DependencyProperty;
    public static readonly DependencyProperty IconCharProperty = IconCharKey.DependencyProperty;
    public static readonly DependencyProperty IconSizeProperty = IconSizeKey.DependencyProperty;
    public static readonly DependencyProperty ShowDescriptionProperty = ShowDescriptionKey.DependencyProperty;

    private System.Windows.Shapes.Rectangle? _stripes;
    private FrameworkElement? _icon;
    private TranslateTransform? _stripeMove;
    private bool _blinking;
    private bool _striped;

    static StateBadge() => DefaultStyleKeyProperty.OverrideMetadata(typeof(StateBadge), new FrameworkPropertyMetadata(typeof(StateBadge)));

    public StateBadge()
    {
        Refresh();
        Loaded += (_, _) => { BlinkClock.Instance.PropertyChanged += OnBlink; ApplyAnimations(); };
        Unloaded += (_, _) => BlinkClock.Instance.PropertyChanged -= OnBlink;
    }

    public PackMlState State { get => (PackMlState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public BadgeSize Size { get => (BadgeSize)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Brush? Back => (Brush?)GetValue(BackProperty);
    public Brush? Fore => (Brush?)GetValue(ForeProperty);
    public Brush? Edge => (Brush?)GetValue(EdgeProperty);
    public string Label => (string)GetValue(LabelProperty);
    public string Description => (string)GetValue(DescriptionProperty);
    public string IconChar => (string)GetValue(IconCharProperty);
    public double IconSize => (double)GetValue(IconSizeProperty);
    public CornerRadius BadgeCorner => (CornerRadius)GetValue(BadgeCornerProperty);
    public bool ShowDescription => (bool)GetValue(ShowDescriptionProperty);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _stripes = GetTemplateChild("PART_Stripes") as System.Windows.Shapes.Rectangle;
        _icon = GetTemplateChild("PART_Icon") as FrameworkElement;
        ApplyAnimations();
    }

    private void OnStateChanged(PackMlState old)
    {
        Refresh();
        ApplyAnimations();
        if (IsLoaded && !AnimationPolicy.Capture)
        {
            // Rebond d'échelle 1,06 (docs/08 § 3) : 200 ms.
            var scale = new ScaleTransform(1.06, 1.06);
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = scale;
            var a = new DoubleAnimation(1.06, 1.0, new Duration(TimeSpan.FromMilliseconds(200)))
            {
                EasingFunction = Tokens.Get<IEasingFunction>("Ease.Standard"),
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
        }
    }

    private void Refresh()
    {
        var s = State;
        Tone tone;
        string icon;
        Brush back, fore, edge;
        _blinking = false;
        _striped = false;
        var transparent = Brushes.Transparent;
        switch (s)
        {
            case PackMlState.Execute:
                tone = Tone.Ok; icon = "play_circle";
                back = ToneBrushes.Dot(Tone.Ok); fore = Tokens.Brush("Text.OnColor"); edge = back;
                break;
            case PackMlState.Idle:
                tone = Tone.Ok; icon = "pause_circle";
                back = transparent; fore = ToneBrushes.Text(Tone.Ok); edge = ToneBrushes.Dot(Tone.Ok);
                break;
            case PackMlState.Aborted:
                tone = Tone.Err; icon = "report"; _blinking = true;
                back = ToneBrushes.Dot(Tone.Err); fore = Tokens.Brush("Text.OnColor"); edge = back;
                break;
            case PackMlState.Held:
                tone = Tone.Warn; icon = "pause";
                back = ToneBrushes.Bg(tone); fore = ToneBrushes.Text(tone); edge = transparent;
                break;
            case PackMlState.Resetting:
                tone = Tone.Warn; icon = "restart_alt"; _striped = true;
                back = ToneBrushes.Bg(tone); fore = ToneBrushes.Text(tone); edge = transparent;
                break;
            case PackMlState.Stopping:
                tone = Tone.Warn; icon = "stop"; _striped = true;
                back = ToneBrushes.Bg(tone); fore = ToneBrushes.Text(tone); edge = transparent;
                break;
            case PackMlState.Clearing:
                tone = Tone.Err; icon = "cleaning_services"; _striped = true;
                back = ToneBrushes.Bg(tone); fore = ToneBrushes.Text(tone); edge = transparent;
                break;
            default:
                tone = Tone.Neutral; icon = "stop_circle";
                back = ToneBrushes.Bg(tone); fore = ToneBrushes.Text(tone); edge = transparent;
                break;
        }
        SetValue(BackKey, back);
        SetValue(ForeKey, fore);
        SetValue(EdgeKey, edge);
        SetValue(LabelKey, s.ToString());
        SetValue(DescriptionKey, Res.Get("State." + s));
        SetValue(IconCharKey, Icon.Char(icon));
        SetValue(ShowDescriptionKey, Size == BadgeSize.Large);
        switch (Size)
        {
            case BadgeSize.Small: Height = 24; SetValue(IconSizeKey, 16.0); FontSize = 12; break;
            case BadgeSize.Large: Height = 48; SetValue(IconSizeKey, 28.0); FontSize = 16; break;
            default: Height = 32; SetValue(IconSizeKey, 20.0); FontSize = 14; break;
        }
        SetValue(CornerKey, new CornerRadius(Height / 2));
        if (_stripes != null) _stripes.Visibility = _striped ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyAnimations()
    {
        if (_stripes != null)
        {
            _stripes.Visibility = _striped ? Visibility.Visible : Visibility.Collapsed;
            if (_striped)
            {
                var tone = State == PackMlState.Clearing ? Tone.Err : Tone.Warn;
                var color = ToneBrushes.DotColor(tone);
                _stripeMove = new TranslateTransform();
                var tile = new DrawingBrush
                {
                    TileMode = TileMode.Tile,
                    Viewport = new Rect(0, 0, 16, 16),
                    ViewportUnits = BrushMappingMode.Absolute,
                    Viewbox = new Rect(0, 0, 16, 16),
                    ViewboxUnits = BrushMappingMode.Absolute,
                    Transform = _stripeMove,
                    Drawing = new GeometryDrawing(
                        null,
                        new Pen(new SolidColorBrush(Color.FromArgb(46, color.R, color.G, color.B)), 6),
                        Geometry.Parse("M -4,12 L 12,-4 M 4,20 L 20,4")),
                };
                _stripes.Fill = tile;
                _stripes.RadiusX = _stripes.RadiusY = Height / 2;
                Anim.Loop(_stripeMove, TranslateTransform.XProperty, 0, 16, 700);
            }
        }
        if (_icon != null)
        {
            if (State == PackMlState.Resetting && AnimationPolicy.Continuous)
            {
                _icon.RenderTransformOrigin = new Point(0.5, 0.5);
                var rot = new RotateTransform();
                _icon.RenderTransform = rot;
                rot.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, new Duration(TimeSpan.FromSeconds(3))) { RepeatBehavior = RepeatBehavior.Forever });
            }
            else
            {
                _icon.RenderTransform = Transform.Identity;
            }
            _icon.Opacity = 1;
        }
    }

    private void OnBlink(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_icon == null) return;
        _icon.Opacity = _blinking ? BlinkClock.Instance.Opacity : 1;
    }
}
