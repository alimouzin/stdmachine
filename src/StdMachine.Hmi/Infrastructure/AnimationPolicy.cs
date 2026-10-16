using System.Windows;
using System.Windows.Media.Animation;

namespace StdMachine.Hmi.Infrastructure;

/// <summary>
/// Réglage « Réduire les animations » (docs/08 § 2.7) et mode capture (animations figées à leur état médian).
/// Toutes les animations continues passent par <see cref="Anim"/> pour respecter cette politique.
/// </summary>
public static class AnimationPolicy
{
    /// <summary>Capture d'écran : tout est figé à l'état médian.</summary>
    public static bool Capture { get; set; }

    /// <summary>Réglage utilisateur : supprime pulsations, flux et secousses (garde couleurs, icônes et clignotements).</summary>
    public static bool Reduced { get; set; }

    public static bool Continuous => !Capture && !Reduced;
}

/// <summary>Animations continues (pulsation, flux, rotation) soumises à <see cref="AnimationPolicy"/>.</summary>
public static class Anim
{
    private static readonly Duration PulsePeriod = new Duration(TimeSpan.FromMilliseconds(1600));

    /// <summary>Pulsation douce d'une propriété double entre deux valeurs (halo qui respire, 1,6 s).</summary>
    public static void Pulse(Animatable target, DependencyProperty dp, double from, double to, double periodMs = 1600)
    {
        if (!AnimationPolicy.Continuous)
        {
            target.BeginAnimation(dp, null);
            target.SetValue(dp, (from + to) / 2);
            return;
        }
        var a = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(periodMs / 2)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        target.BeginAnimation(dp, a);
    }

    /// <summary>Pulsation d'un élément d'interface (même contrat que <see cref="Pulse(Animatable,DependencyProperty,double,double,double)"/>).</summary>
    public static void Pulse(UIElement target, DependencyProperty dp, double from, double to, double periodMs = 1600)
    {
        if (!AnimationPolicy.Continuous)
        {
            target.BeginAnimation(dp, null);
            target.SetValue(dp, (from + to) / 2);
            return;
        }
        var a = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(periodMs / 2)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        target.BeginAnimation(dp, a);
    }

    /// <summary>Défilement linéaire continu d'une propriété double (rayures, tirets, rotation).</summary>
    public static void Loop(Animatable target, DependencyProperty dp, double from, double to, double periodMs)
    {
        if (!AnimationPolicy.Continuous)
        {
            target.BeginAnimation(dp, null);
            target.SetValue(dp, from + (to - from) * 0.5);
            return;
        }
        var a = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(periodMs)))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        target.BeginAnimation(dp, a);
    }

    public static void Loop(UIElement target, DependencyProperty dp, double from, double to, double periodMs)
    {
        if (!AnimationPolicy.Continuous)
        {
            target.BeginAnimation(dp, null);
            target.SetValue(dp, from + (to - from) * 0.5);
            return;
        }
        var a = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(periodMs)))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        };
        target.BeginAnimation(dp, a);
    }

    /// <summary>Transition ponctuelle (ne dépend pas de la politique : durée raccourcie à zéro en capture).</summary>
    public static void To(UIElement target, DependencyProperty dp, double value, Duration duration, IEasingFunction? ease = null)
    {
        if (AnimationPolicy.Capture)
        {
            target.BeginAnimation(dp, null);
            target.SetValue(dp, value);
            return;
        }
        target.BeginAnimation(dp, new DoubleAnimation(value, duration) { EasingFunction = ease });
    }

    public static void Stop(UIElement target, DependencyProperty dp) => target.BeginAnimation(dp, null);
    public static void Stop(Animatable target, DependencyProperty dp) => target.BeginAnimation(dp, null);
}
