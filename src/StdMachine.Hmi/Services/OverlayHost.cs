using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Services;

public enum OverlayPlacement
{
    /// <summary>Près d'un élément (pavé numérique, clavier).</summary>
    Anchored,
    /// <summary>Tiroir de 480 px glissant depuis la droite.</summary>
    Drawer,
    /// <summary>Centré (dialogue).</summary>
    Center,
}

/// <summary>
/// Couche de surimpression de la fenêtre : pavé numérique, clavier, tiroirs, dialogues et toasts.
/// Un toucher sur le fond atténué ferme la surimpression (annulation).
/// </summary>
public sealed class OverlayHost : Grid
{
    public static OverlayHost? Current { get; private set; }

    private readonly Grid _layers = new Grid();
    private readonly StackPanel _toasts = new StackPanel
    {
        Orientation = Orientation.Vertical,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(0, 0, 0, 32),
        IsHitTestVisible = false,
    };

    public OverlayHost()
    {
        Current = this;
        Children.Add(_layers);
        Children.Add(_toasts);
        SetZIndex(_toasts, 1000);
        // Une surimpression vide ne doit pas bloquer le toucher du contenu en dessous.
        Background = null;
    }

    public bool HasOverlay => _layers.Children.Count > 0;

    /// <summary>Affiche un contenu ; la valeur rendue le ferme (<see cref="IDisposable.Dispose"/>).</summary>
    public OverlayHandle Show(FrameworkElement content, OverlayPlacement placement, Rect? anchor = null, bool dim = true, Action? onDismiss = null)
    {
        var layer = new Grid();
        var scrim = new Rectangle { Fill = dim ? Tokens.Brush("Scrim") : Brushes.Transparent };
        layer.Children.Add(scrim);
        var handle = new OverlayHandle(this, layer, onDismiss);
        scrim.MouseLeftButtonDown += (_, e) => { handle.Dismiss(); e.Handled = true; };
        scrim.TouchDown += (_, e) => { handle.Dismiss(); e.Handled = true; };

        switch (placement)
        {
            case OverlayPlacement.Drawer:
                content.HorizontalAlignment = HorizontalAlignment.Right;
                content.VerticalAlignment = VerticalAlignment.Stretch;
                content.Width = Tokens.Number("Size.Drawer");
                var move = new TranslateTransform(content.Width, 0);
                content.RenderTransform = move;
                layer.Children.Add(content);
                _layers.Children.Add(layer);
                if (AnimationPolicy.Capture) move.X = 0;
                else move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(200))) { EasingFunction = Tokens.Get<IEasingFunction>("Ease.Standard") });
                break;
            case OverlayPlacement.Center:
                content.HorizontalAlignment = HorizontalAlignment.Center;
                content.VerticalAlignment = VerticalAlignment.Center;
                layer.Children.Add(content);
                _layers.Children.Add(layer);
                break;
            default:
                var canvas = new Canvas();
                layer.Children.Add(canvas);
                canvas.Children.Add(content);
                _layers.Children.Add(layer);
                content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                PositionAnchored(content, anchor ?? new Rect(0, 0, 0, 0));
                break;
        }        return handle;
    }

    private void PositionAnchored(FrameworkElement content, Rect anchor)
    {
        double hostW = ActualWidth > 0 ? ActualWidth : 1920;
        double hostH = ActualHeight > 0 ? ActualHeight : 1080;
        var size = content.DesiredSize;
        const double gap = 8, margin = 12;
        double x = anchor.Left;
        double y = anchor.Bottom + gap;
        if (y + size.Height > hostH - margin) y = anchor.Top - gap - size.Height;
        if (y < margin) y = Math.Max(margin, (hostH - size.Height) / 2);
        if (x + size.Width > hostW - margin) x = hostW - margin - size.Width;
        if (x < margin) x = margin;
        Canvas.SetLeft(content, x);
        Canvas.SetTop(content, y);
    }

    internal void Remove(Grid layer)
    {
        _layers.Children.Remove(layer);
    }

    // ----- Toasts -----

    public void ShowToast(FrameworkElement toast, TimeSpan duration)
    {
        toast.Opacity = 0;
        _toasts.Children.Add(toast);
        if (AnimationPolicy.Capture) toast.Opacity = 1;
        else toast.BeginAnimation(OpacityProperty, new DoubleAnimation(1, new Duration(TimeSpan.FromMilliseconds(200))));
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = AnimationPolicy.Capture ? TimeSpan.FromHours(1) : duration };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            var fade = new DoubleAnimation(0, new Duration(TimeSpan.FromMilliseconds(200)));
            fade.Completed += (_, _) => _toasts.Children.Remove(toast);
            toast.BeginAnimation(OpacityProperty, fade);
        };
        timer.Start();
    }
}

public sealed class OverlayHandle : IDisposable
{
    private readonly OverlayHost _host;
    private readonly Grid _layer;
    private readonly Action? _onDismiss;
    private bool _closed;

    internal OverlayHandle(OverlayHost host, Grid layer, Action? onDismiss)
    {
        _host = host;
        _layer = layer;
        _onDismiss = onDismiss;
    }

    /// <summary>Fermeture demandée par l'utilisateur (toucher sur le fond) : notifie l'appelant.</summary>
    public void Dismiss()
    {
        if (_closed) return;
        Dispose();
        _onDismiss?.Invoke();
    }

    public void Dispose()
    {
        if (_closed) return;
        _closed = true;
        _host.Remove(_layer);
    }
}
