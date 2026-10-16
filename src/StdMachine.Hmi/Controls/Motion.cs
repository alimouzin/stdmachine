using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using StdMachine.Hmi.Infrastructure;

namespace StdMachine.Hmi.Controls;

/// <summary>
/// Déplacement fluide sur un Canvas : lier <c>Motion.X</c> / <c>Motion.Y</c> au lieu de <c>Canvas.Left</c> / <c>Canvas.Top</c>
/// interpole la position entre deux instantanés (animation linéaire de 100 ms, docs/08 § 7.4). Pas d'animation en capture.
/// </summary>
public static class Motion
{
    public static readonly DependencyProperty XProperty = DependencyProperty.RegisterAttached(
        "X", typeof(double), typeof(Motion), new PropertyMetadata(double.NaN, (d, e) => Move(d, Canvas.LeftProperty, (double)e.NewValue, (double)e.OldValue)));
    public static double GetX(DependencyObject o) => (double)o.GetValue(XProperty);
    public static void SetX(DependencyObject o, double v) => o.SetValue(XProperty, v);

    public static readonly DependencyProperty YProperty = DependencyProperty.RegisterAttached(
        "Y", typeof(double), typeof(Motion), new PropertyMetadata(double.NaN, (d, e) => Move(d, Canvas.TopProperty, (double)e.NewValue, (double)e.OldValue)));
    public static double GetY(DependencyObject o) => (double)o.GetValue(YProperty);
    public static void SetY(DependencyObject o, double v) => o.SetValue(YProperty, v);

    private static void Move(DependencyObject d, DependencyProperty target, double value, double old)
    {
        if (double.IsNaN(value)) return;
        if (d is not UIElement el) { d.SetValue(target, value); return; }
        // Saut sans animation au premier placement, en capture, ou si le déplacement est très grand (retour au début du scénario).
        if (double.IsNaN(old) || AnimationPolicy.Capture || Math.Abs(value - old) > 400)
        {
            el.BeginAnimation(target, null);
            el.SetValue(target, value);
            return;
        }
        el.BeginAnimation(target, new DoubleAnimation(value, new Duration(TimeSpan.FromMilliseconds(100))) { FillBehavior = FillBehavior.HoldEnd });
    }
}
