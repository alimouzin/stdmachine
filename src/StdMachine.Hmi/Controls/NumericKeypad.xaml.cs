using System.Windows;
using System.Windows.Controls;
using StdMachine.Core;
using StdMachine.Hmi.Resources;

namespace StdMachine.Hmi.Controls;

/// <summary>Données d'une saisie numérique : valeur actuelle, unité, bornes, pas (docs/07 § 8.4).</summary>
public sealed record NumberEditRequest(string Title, double Value, string Unit, double Min, double Max, double Step, int Decimals);

/// <summary>Pavé numérique 4 × 4 de touches de 72 px, avec unité, bornes, −/+ du pas, effacement, validation.</summary>
public partial class NumericKeypad : UserControl
{
    private readonly NumberEditRequest _request;
    private string _text;
    private bool _fresh = true;

    public NumericKeypad(NumberEditRequest request)
    {
        InitializeComponent();
        _request = request;
        _text = DisplayFormat.Number(request.Value, request.Decimals).Replace(DisplayFormat.ThinSpace.ToString(), "");
        TitleText.Text = request.Title.ToUpperInvariant();
        UnitText.Text = request.Unit;
        BoundsText.Text = Res.Format("Keypad.Bounds", DisplayFormat.Number(request.Min, request.Decimals), DisplayFormat.Number(request.Max, request.Decimals), request.Unit).Trim();
        StepDownButton.Content = "−" + DisplayFormat.Number(request.Step, request.Decimals);
        StepUpButton.Content = "+" + DisplayFormat.Number(request.Step, request.Decimals);
        Refresh();
    }

    /// <summary>Résultat : la valeur validée, ou null si annulé.</summary>
    public event EventHandler<double?>? Completed;

    private void Refresh()
    {
        ValueText.Text = _text.Length == 0 ? "0" : _text;
        ValueText.Foreground = ErrorText.Text.Length > 0 ? (System.Windows.Media.Brush)FindResource("Err.Text") : (System.Windows.Media.Brush)FindResource("Text.Primary");
    }

    private void Digit_Click(object sender, RoutedEventArgs e)
    {
        var d = (string)((Button)sender).Tag;
        if (_fresh) { _text = string.Empty; _fresh = false; }
        if (_text == "0" || _text == DisplayFormat.Minus + "0") _text = _text.Substring(0, _text.Length - 1);
        _text += d;
        ErrorText.Text = string.Empty;
        Refresh();
    }

    private void Decimal_Click(object sender, RoutedEventArgs e)
    {
        if (_fresh) { _text = string.Empty; _fresh = false; }
        if (_request.Decimals == 0 || _text.Contains(",")) return;
        if (_text.Length == 0 || _text == DisplayFormat.Minus.ToString()) _text += "0";
        _text += ",";
        ErrorText.Text = string.Empty;
        Refresh();
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _fresh = false;
        if (_text.Length > 0) _text = _text.Substring(0, _text.Length - 1);
        ErrorText.Text = string.Empty;
        Refresh();
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        _fresh = false; _text = string.Empty; ErrorText.Text = string.Empty; Refresh();
    }

    private void Sign_Click(object sender, RoutedEventArgs e)
    {
        _fresh = false;
        _text = _text.StartsWith(DisplayFormat.Minus.ToString()) ? _text.Substring(1) : DisplayFormat.Minus + _text;
        ErrorText.Text = string.Empty;
        Refresh();
    }

    private void Step(int dir)
    {
        DisplayFormat.TryParse(_text, out var v);
        v += dir * _request.Step;
        v = Math.Max(_request.Min, Math.Min(_request.Max, v));
        _text = DisplayFormat.Number(v, _request.Decimals).Replace(DisplayFormat.ThinSpace.ToString(), "");
        _fresh = false;
        ErrorText.Text = string.Empty;
        Refresh();
    }

    private void StepDown_Click(object sender, RoutedEventArgs e) => Step(-1);
    private void StepUp_Click(object sender, RoutedEventArgs e) => Step(+1);

    private void Validate_Click(object sender, RoutedEventArgs e)
    {
        if (!DisplayFormat.TryParse(_text.Length == 0 ? "0" : _text, out var v))
        {
            ErrorText.Text = Res.Get("Keypad.Invalid");
            Refresh();
            return;
        }
        if (v < _request.Min || v > _request.Max)
        {
            ErrorText.Text = Res.Format("Keypad.OutOfBounds",
                DisplayFormat.Number(_request.Min, _request.Decimals), DisplayFormat.Number(_request.Max, _request.Decimals), _request.Unit).Trim();
            Refresh();
            return;
        }
        Completed?.Invoke(this, v);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Completed?.Invoke(this, null);
}
