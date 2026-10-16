using System.Windows;
using System.Windows.Input;

namespace StdMachine.Hmi.Infrastructure;

/// <summary>Réglages tactiles globaux (docs/07 § 8.1).</summary>
public static class TouchSettings
{
    /// <summary>À appeler avant toute création de fenêtre : pile WM_POINTER, toucher plus fiable.</summary>
    public static void EnableAtStartup()
    {
        AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", true);
    }

    /// <summary>Pas de clic droit par appui long, pas de gestes Windows, pas de rebond de fenêtre.</summary>
    public static void Apply(Window window)
    {
        Stylus.SetIsPressAndHoldEnabled(window, false);
        Stylus.SetIsFlicksEnabled(window, false);
        Stylus.SetIsTapFeedbackEnabled(window, false);
        Stylus.SetIsTouchFeedbackEnabled(window, false);
    }

    /// <summary>Intercepte le retour de fin de course des défilements (pas de rebond de la fenêtre).</summary>
    public static void RegisterGlobalHandlers()
    {
        EventManager.RegisterClassHandler(typeof(UIElement), UIElement.ManipulationBoundaryFeedbackEvent,
            (EventHandler<ManipulationBoundaryFeedbackEventArgs>)((_, e) => e.Handled = true));
    }
}
