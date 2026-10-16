using System.Globalization;
using System.Windows;
using System.Windows.Data;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Infrastructure;

/// <summary>Convertisseurs de liaison communs (jamais de logique métier).</summary>
public sealed class FuncConverter : IValueConverter
{
    private readonly Func<object?, object?, object?> _convert;

    public FuncConverter(Func<object?, object?, object?> convert) { _convert = convert; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => _convert(value, parameter);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public static class Conv
{
    private static Visibility V(bool b) => b ? Visibility.Visible : Visibility.Collapsed;

    public static readonly IValueConverter BoolToVis = new FuncConverter((v, _) => V(v is bool b && b));
    public static readonly IValueConverter InvBoolToVis = new FuncConverter((v, _) => V(!(v is bool b && b)));
    public static readonly IValueConverter NotNullToVis = new FuncConverter((v, _) => V(v != null));
    public static readonly IValueConverter NullToVis = new FuncConverter((v, _) => V(v == null));
    public static readonly IValueConverter NotEmptyToVis = new FuncConverter((v, _) => V(!string.IsNullOrEmpty(v as string)));
    public static readonly IValueConverter EmptyToVis = new FuncConverter((v, _) => V(string.IsNullOrEmpty(v as string)));
    public static readonly IValueConverter InvBool = new FuncConverter((v, _) => !(v is bool b && b));
    public static readonly IValueConverter ZeroToVis = new FuncConverter((v, _) => V(v is int i && i == 0));
    public static readonly IValueConverter PositiveToVis = new FuncConverter((v, _) => V(v is int i && i > 0));

    public static readonly IValueConverter OkIcon = new FuncConverter((v, _) => v is bool b && b ? "check_circle" : "cancel");
    public static readonly IValueConverter OkBrush = new FuncConverter((v, _) => ToneBrushes.Dot(v is bool b && b ? Tone.Ok : Tone.Err));
    public static readonly IValueConverter NotThree = new FuncConverter((v, _) => V(!(v is int i && i == 3)));
    public static readonly IValueConverter ToneText = new FuncConverter((v, _) => ToneBrushes.Text(v is Tone t ? t : Tone.Neutral));
    public static readonly IValueConverter ToneBg = new FuncConverter((v, _) => ToneBrushes.Bg(v is Tone t ? t : Tone.Neutral));
    public static readonly IValueConverter ToneDot = new FuncConverter((v, _) => ToneBrushes.Dot(v is Tone t ? t : Tone.Neutral));

    /// <summary>Texte du paramètre égal à la valeur : <c>ConverterParameter=Execute</c>.</summary>
    public static readonly IValueConverter EqualsParam = new FuncConverter((v, p) => v != null && p != null && string.Equals(v.ToString(), p.ToString(), StringComparison.Ordinal));
    public static readonly IValueConverter EqualsParamToVis = new FuncConverter((v, p) => V(v != null && p != null && string.Equals(v.ToString(), p.ToString(), StringComparison.Ordinal)));
}
