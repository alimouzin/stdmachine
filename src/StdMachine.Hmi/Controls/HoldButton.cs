using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace StdMachine.Hmi.Controls;

/// <summary>
/// Bouton à appui maintenu (1 s) pour Déployer, Annuler un OF, Supprimer, Confirmer un retrait (docs/08 § 3).
/// Un anneau se remplit pendant l'appui ; relâcher avant la fin annule. Jamais de clic simple.
/// </summary>
public class HoldButton : Button
{
    public static readonly DependencyProperty HoldProgressProperty = DependencyProperty.Register(
        nameof(HoldProgress), typeof(double), typeof(HoldButton), new PropertyMetadata(0.0));
    public static readonly DependencyProperty IsHoldingProperty = DependencyProperty.Register(
        nameof(IsHolding), typeof(bool), typeof(HoldButton), new PropertyMetadata(false));
    public static readonly DependencyProperty HoldMillisecondsProperty = DependencyProperty.Register(
        nameof(HoldMilliseconds), typeof(int), typeof(HoldButton), new PropertyMetadata(1000));

    public static readonly RoutedEvent ConfirmedEvent = EventManager.RegisterRoutedEvent(
        nameof(Confirmed), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(HoldButton));

    private TouchDevice? _touch;

    static HoldButton() => DefaultStyleKeyProperty.OverrideMetadata(typeof(HoldButton), new FrameworkPropertyMetadata(typeof(HoldButton)));

    public double HoldProgress { get => (double)GetValue(HoldProgressProperty); private set => SetValue(HoldProgressProperty, value); }
    public bool IsHolding { get => (bool)GetValue(IsHoldingProperty); private set => SetValue(IsHoldingProperty, value); }
    public int HoldMilliseconds { get => (int)GetValue(HoldMillisecondsProperty); set => SetValue(HoldMillisecondsProperty, value); }

    public event RoutedEventHandler Confirmed { add => AddHandler(ConfirmedEvent, value); remove => RemoveHandler(ConfirmedEvent, value); }

    protected override void OnClick() { /* jamais de clic simple */ }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (e.StylusDevice != null) return; // toucher : géré par OnPreviewTouchDown
        if (IsEnabled && CaptureMouse()) { BeginHold(); e.Handled = true; }
        else base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsHolding || e.StylusDevice != null) return;
        CancelHold();
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (_touch == null) CancelHold();
    }

    protected override void OnPreviewTouchDown(TouchEventArgs e)
    {
        if (IsEnabled && _touch == null && CaptureTouch(e.TouchDevice))
        {
            _touch = e.TouchDevice;
            BeginHold();
            e.Handled = true;
        }
    }

    protected override void OnPreviewTouchUp(TouchEventArgs e)
    {
        if (_touch != e.TouchDevice) return;
        CancelHold();
        ReleaseTouchCapture(e.TouchDevice);
        _touch = null;
        e.Handled = true;
    }

    protected override void OnLostTouchCapture(TouchEventArgs e)
    {
        base.OnLostTouchCapture(e);
        if (_touch == e.TouchDevice) { _touch = null; CancelHold(); }
    }

    private void BeginHold()
    {
        IsHolding = true;
        var a = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(HoldMilliseconds))) { FillBehavior = FillBehavior.Stop };
        a.Completed += OnCompleted;
        _animation = a;
        BeginAnimation(HoldProgressProperty, a);
    }

    private AnimationTimeline? _animation;

    private void OnCompleted(object? sender, EventArgs e)
    {
        if (!IsHolding || _animation == null) return;
        // L'animation s'est terminée sans relâchement : confirmation.
        IsHolding = false;
        HoldProgress = 0;
        _animation = null;
        ReleaseMouseCapture();
        if (_touch != null) { ReleaseTouchCapture(_touch); _touch = null; }
        RaiseEvent(new RoutedEventArgs(ConfirmedEvent, this));
        if (Command != null && Command.CanExecute(CommandParameter)) Command.Execute(CommandParameter);
    }

    private void CancelHold()
    {
        if (!IsHolding) return;
        _animation = null;
        BeginAnimation(HoldProgressProperty, null);
        HoldProgress = 0;
        IsHolding = false;
    }
}
