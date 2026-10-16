using System.Windows;
using System.Windows.Media.Animation;

namespace StdMachine.Hmi.Theme;

/// <summary>Courbe de Bézier cubique (équivalent CSS cubic-bezier) : Ease.Standard = (0,2 ; 0 ; 0 ; 1).</summary>
public sealed class BezierEase : EasingFunctionBase
{
    public static readonly DependencyProperty X1Property = DependencyProperty.Register(nameof(X1), typeof(double), typeof(BezierEase), new PropertyMetadata(0.2));
    public static readonly DependencyProperty Y1Property = DependencyProperty.Register(nameof(Y1), typeof(double), typeof(BezierEase), new PropertyMetadata(0.0));
    public static readonly DependencyProperty X2Property = DependencyProperty.Register(nameof(X2), typeof(double), typeof(BezierEase), new PropertyMetadata(0.0));
    public static readonly DependencyProperty Y2Property = DependencyProperty.Register(nameof(Y2), typeof(double), typeof(BezierEase), new PropertyMetadata(1.0));

    public double X1 { get => (double)GetValue(X1Property); set => SetValue(X1Property, value); }
    public double Y1 { get => (double)GetValue(Y1Property); set => SetValue(Y1Property, value); }
    public double X2 { get => (double)GetValue(X2Property); set => SetValue(X2Property, value); }
    public double Y2 { get => (double)GetValue(Y2Property); set => SetValue(Y2Property, value); }

    protected override double EaseInCore(double normalizedTime)
    {
        if (normalizedTime <= 0) return 0;
        if (normalizedTime >= 1) return 1;
        double x1 = X1, y1 = Y1, x2 = X2, y2 = Y2;
        // Résolution de x(s) = t par dichotomie, puis y(s).
        double lo = 0, hi = 1, s = normalizedTime;
        for (int i = 0; i < 24; i++)
        {
            double x = Bez(s, x1, x2);
            if (x < normalizedTime) lo = s; else hi = s;
            s = (lo + hi) / 2;
        }
        return Bez(s, y1, y2);
    }

    private static double Bez(double s, double p1, double p2)
    {
        double u = 1 - s;
        return 3 * u * u * s * p1 + 3 * u * s * s * p2 + s * s * s;
    }

    protected override Freezable CreateInstanceCore() => new BezierEase();
}
