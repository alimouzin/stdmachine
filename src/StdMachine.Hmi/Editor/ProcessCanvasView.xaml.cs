using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StdMachine.Hmi.Infrastructure;

namespace StdMachine.Hmi.Editor;

public partial class ProcessCanvasView : UserControl
{
    public ProcessCanvasView()
    {
        InitializeComponent();
    }

    public ScrollViewer ScrollViewer => Scroller;
    public UIElement SurfaceElement => Surface;

    private ProcessCanvasViewModel? Vm => DataContext as ProcessCanvasViewModel;

    /// <summary>Les en-têtes de couloirs restent collés en haut : on les décale du défilement vertical.</summary>
    private void Scroller_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        double zoom = Vm?.Zoom ?? 1;
        HeaderShift.Y = Scroller.VerticalOffset / zoom;
    }

    private void Scroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (Vm == null) return;
        Vm.SetViewportWidth(Math.Max(300, (Scroller.ViewportWidth > 0 ? Scroller.ViewportWidth : e.NewSize.Width) / Vm.Zoom - 6));
    }

    /// <summary>Tirets qui avancent dans le sens de l'exécution sur le connecteur actif (docs/08 § 7.2).</summary>
    private void Connector_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Shapes.Path path || path.DataContext is not ConnectorModel model) return;
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName != nameof(ConnectorModel.State)) return;
            Update(path, model);
        };
        model.PropertyChanged += handler;
        path.Unloaded += (_, _) => model.PropertyChanged -= handler;
        Update(path, model);
    }

    private static void Update(System.Windows.Shapes.Path path, ConnectorModel model)
    {
        if (model.State == ConnectorState.Active) Anim.Loop(path, System.Windows.Shapes.Shape.StrokeDashOffsetProperty, 6, 0, 600);
        else { path.BeginAnimation(System.Windows.Shapes.Shape.StrokeDashOffsetProperty, null); }
    }

    /// <summary>Fait défiler en douceur pour garder une carte visible (docs/08 § 7.2 : suivi de l'exécution).</summary>
    public void EnsureVisible(StepCardModel card)
    {
        double z = Vm?.Zoom ?? 1;
        double top = card.Y * z, bottom = (card.Y + card.Height) * z;
        double view = Scroller.ViewportHeight;
        double target = Scroller.VerticalOffset;
        if (top - 60 < target) target = Math.Max(0, top - 60);
        else if (bottom + 40 > target + view) target = bottom + 40 - view;
        if (Math.Abs(target - Scroller.VerticalOffset) < 2) return;
        if (AnimationPolicy.Capture) { Scroller.ScrollToVerticalOffset(target); return; }
        BeginAnimation(AnimatedOffsetProperty, new System.Windows.Media.Animation.DoubleAnimation(Scroller.VerticalOffset, target, new Duration(TimeSpan.FromMilliseconds(320)))
        { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
    }

    public static readonly DependencyProperty AnimatedOffsetProperty = DependencyProperty.Register(
        nameof(AnimatedOffset), typeof(double), typeof(ProcessCanvasView), new PropertyMetadata(0.0, (d, e) => ((ProcessCanvasView)d).Scroller.ScrollToVerticalOffset((double)e.NewValue)));

    /// <summary>Position de défilement animée (320 ms, Ease.Standard).</summary>
    public double AnimatedOffset { get => (double)GetValue(AnimatedOffsetProperty); set => SetValue(AnimatedOffsetProperty, value); }
}
