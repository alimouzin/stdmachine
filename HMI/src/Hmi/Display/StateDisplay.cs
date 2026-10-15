using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Core.StateMachine;

namespace Hmi.Display;

/// <summary>Libellés français et couleurs des 8 états.</summary>
public static class StateDisplay
{
    public static string Label(SeqState state) => state switch
    {
        SeqState.Stopped => "Arrêté",
        SeqState.Resetting => "Initialisation",
        SeqState.Idle => "Prêt",
        SeqState.Execute => "Production",
        SeqState.Held => "En pause",
        SeqState.Stopping => "Arrêt en cours",
        SeqState.Aborted => "Défaut",
        SeqState.Clearing => "Acquittement",
        _ => state.ToString(),
    };

    public static string Label(SeqMode mode) => mode == SeqMode.Production ? "Production" : "Maintenance";

    public static Brush Brush(SeqState state) => state switch
    {
        SeqState.Stopped => Frozen(0x78, 0x84, 0x93),
        SeqState.Resetting => Frozen(0x3B, 0x82, 0xC4),
        SeqState.Idle => Frozen(0x2E, 0x9C, 0xA6),
        SeqState.Execute => Frozen(0x2F, 0x9E, 0x44),
        SeqState.Held => Frozen(0xE0, 0xA1, 0x00),
        SeqState.Stopping => Frozen(0xE6, 0x7E, 0x22),
        SeqState.Aborted => Frozen(0xD6, 0x3C, 0x3C),
        SeqState.Clearing => Frozen(0x8E, 0x5B, 0xB5),
        _ => Frozen(0x78, 0x84, 0x93),
    };

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}

public sealed class StateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SeqState state ? StateDisplay.Brush(state) : System.Windows.DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class StateToLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is SeqState state ? StateDisplay.Label(state) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
