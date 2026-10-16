using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Resources;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Controls;

public sealed record TextEditRequest(string Title, string Value, string Placeholder);

/// <summary>Clavier AZERTY intégré (touches de 64 px), accents et majuscules (docs/07 § 8.4).</summary>
public sealed class Keyboard : Border
{
    private static readonly string[] AccentRow = { "é", "è", "ê", "ë", "à", "â", "ù", "û", "ç", "î", "ï", "ô", "œ" };
    private static readonly string[] Row1 = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0" };
    private static readonly string[] Row2 = { "a", "z", "e", "r", "t", "y", "u", "i", "o", "p" };
    private static readonly string[] Row3 = { "q", "s", "d", "f", "g", "h", "j", "k", "l", "m" };
    private static readonly string[] Row4 = { "w", "x", "c", "v", "b", "n", "'", "-", "." };

    private readonly TextEditRequest _request;
    private readonly TextBlock _display = new TextBlock();
    private readonly TextBlock _caret = new TextBlock { Text = "▏" };
    private readonly List<Button> _letterKeys = new List<Button>();
    private string _text;
    private bool _shift;

    public Keyboard(TextEditRequest request)
    {
        _request = request;
        _text = request.Value;
        Background = Tokens.Brush("Bg.Surface");
        BorderBrush = Tokens.Brush("Border.Default");
        BorderThickness = new Thickness(1);
        CornerRadius = Tokens.Radius("Radius.XL");
        Padding = new Thickness(16);
        Effect = Tokens.Get<System.Windows.Media.Effects.Effect>("Shadow.Overlay");

        var root = new StackPanel();
        var title = new TextBlock { Text = request.Title.ToUpperInvariant(), Style = Tokens.Get<Style>("Type.Caps"), Margin = new Thickness(0, 0, 0, 8) };
        root.Children.Add(title);

        var box = new Border
        {
            Background = Tokens.Brush("Bg.Sunken"),
            CornerRadius = Tokens.Radius("Radius.M"),
            Height = 64,
            Padding = new Thickness(16, 0, 16, 0),
            Margin = new Thickness(0, 0, 0, 8),
        };
        var line = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _display.FontSize = 26;
        _display.FontWeight = FontWeights.Medium;
        _caret.FontSize = 26;
        _caret.Foreground = Tokens.Brush("Accent");
        line.Children.Add(_display);
        line.Children.Add(_caret);
        box.Child = line;
        root.Children.Add(box);

        root.Children.Add(MakeRow(AccentRow, false));
        root.Children.Add(MakeRow(Row1, false, extra: BackspaceKey()));
        root.Children.Add(MakeRow(Row2, true));
        root.Children.Add(MakeRow(Row3, true));

        var r4 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var shift = Key("keyboard_capslock", null, 1.4, alt: true);
        shift.Click += (_, _) => { _shift = !_shift; UpdateCase(); };
        r4.Children.Add(shift);
        foreach (var k in Row4) r4.Children.Add(LetterKey(k, true));
        root.Children.Add(Wrap(r4));

        var r5 = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        var cancel = Key(null, Res.Get("Common.Cancel"), 2.2, alt: true);
        cancel.Click += (_, _) => Completed?.Invoke(this, null);
        var space = Key(null, "", 5.0);
        space.Click += (_, _) => Append(" ");
        var at = Key(null, "@", 1.0);
        at.Click += (_, _) => Append("@");
        var ok = Key("check", Res.Get("Common.Validate"), 2.6, alt: false);
        ok.SetValue(Ui.VariantProperty, ButtonVariant.Primary);
        ok.Click += (_, _) => Completed?.Invoke(this, _text);
        r5.Children.Add(cancel); r5.Children.Add(space); r5.Children.Add(at); r5.Children.Add(ok);
        root.Children.Add(Wrap(r5));

        Child = root;
        Refresh();
        Loaded += (_, _) => BlinkClock.Instance.PropertyChanged += OnBlink;
        Unloaded += (_, _) => BlinkClock.Instance.PropertyChanged -= OnBlink;
    }

    /// <summary>Texte validé, ou null si annulé.</summary>
    public event EventHandler<string?>? Completed;

    private void OnBlink(object? s, System.ComponentModel.PropertyChangedEventArgs e) => _caret.Opacity = BlinkClock.Instance.IsOn ? 1 : 0;

    private static FrameworkElement Wrap(FrameworkElement row)
    {
        row.Margin = new Thickness(0, 3, 0, 3);
        return row;
    }

    private Button BackspaceKey()
    {
        var b = Key("backspace", null, 1.6, alt: true);
        b.Click += (_, _) =>
        {
            if (_text.Length > 0) _text = _text.Substring(0, _text.Length - 1);
            Refresh();
        };
        return b;
    }

    private StackPanel MakeRow(string[] keys, bool letters, Button? extra = null)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        foreach (var k in keys) row.Children.Add(LetterKey(k, letters));
        if (extra != null) row.Children.Add(extra);
        return (StackPanel)Wrap(row);
    }

    private Button LetterKey(string k, bool letter)
    {
        var b = Key(null, k, 1.0);
        b.Tag = k;
        b.Click += (_, _) => Append((string)b.Tag);
        if (letter || k.Length == 1 && char.IsLetter(k[0])) _letterKeys.Add(b);
        return b;
    }

    private Button Key(string? icon, string? label, double widthFactor, bool alt = false)
    {
        var b = new Button
        {
            Style = Tokens.Get<Style>(alt ? "KeyButtonAlt" : "KeyButton"),
            Width = 64 * widthFactor + (widthFactor > 1 ? 6 * (Math.Ceiling(widthFactor) - 1) : 0),
            Height = Tokens.Number("Size.Keyboard.Key"),
            Margin = new Thickness(3, 0, 3, 0),
        };
        if (label != null) b.Content = label;
        if (icon != null) b.SetValue(Ui.IconProperty, icon);
        return b;
    }

    private void Append(string s)
    {
        _text += _shift ? s.ToUpperInvariant() : s;
        if (_shift) { _shift = false; UpdateCase(); }
        Refresh();
    }

    private void UpdateCase()
    {
        foreach (var b in _letterKeys)
        {
            var s = (string)b.Tag;
            b.Content = _shift ? s.ToUpperInvariant() : s;
        }
    }

    private void Refresh()
    {
        if (_text.Length == 0)
        {
            _display.Text = _request.Placeholder;
            _display.Foreground = Tokens.Brush("Text.Tertiary");
        }
        else
        {
            _display.Text = _text;
            _display.Foreground = Tokens.Brush("Text.Primary");
        }
    }
}
