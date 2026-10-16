using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using StdMachine.Core;
using StdMachine.Hmi.Resources;
using StdMachine.Hmi.Services;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Controls;

/// <summary>
/// Override : valeur en grand, boutons −/+ (pas de 5 %), toucher pour ouvrir un curseur 400 px avec valeurs rapides
/// 0 / 25 / 50 / 75 / 100 % ; affiche la chaîne d'override effectif (docs/08 § 3). À 0 % : fond Warn.
/// </summary>
[TemplatePart(Name = "PART_Minus", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_Plus", Type = typeof(ButtonBase))]
[TemplatePart(Name = "PART_Value", Type = typeof(ButtonBase))]
public class OverrideControl : Control
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(OverrideControl),
        new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(OverrideControl), new PropertyMetadata("Override"));
    public static readonly DependencyProperty ChainProperty = DependencyProperty.Register(
        nameof(Chain), typeof(string), typeof(OverrideControl), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(OverrideControl), new PropertyMetadata(5.0));
    public static readonly DependencyProperty IsZeroProperty = DependencyProperty.Register(
        nameof(IsZero), typeof(bool), typeof(OverrideControl), new PropertyMetadata(false));
    public static readonly DependencyProperty DisplayProperty = DependencyProperty.Register(
        nameof(Display), typeof(string), typeof(OverrideControl), new PropertyMetadata("100 %"));
    public static readonly DependencyProperty ZeroTextProperty = DependencyProperty.Register(
        nameof(ZeroText), typeof(string), typeof(OverrideControl), new PropertyMetadata("Mouvements arrêtés"));

    static OverrideControl() => DefaultStyleKeyProperty.OverrideMetadata(typeof(OverrideControl), new FrameworkPropertyMetadata(typeof(OverrideControl)));

    public OverrideControl() { UpdateDerived(); }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    /// <summary>Chaîne d'override effectif, par exemple « 80 % × 50 % × 100 % = 40 % ».</summary>
    public string Chain { get => (string)GetValue(ChainProperty); set => SetValue(ChainProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public bool IsZero { get => (bool)GetValue(IsZeroProperty); private set => SetValue(IsZeroProperty, value); }
    public string Display { get => (string)GetValue(DisplayProperty); private set => SetValue(DisplayProperty, value); }
    public string ZeroText { get => (string)GetValue(ZeroTextProperty); set => SetValue(ZeroTextProperty, value); }

    private static object CoerceValue(DependencyObject d, object v)
        => Math.Max(0, Math.Min(100, Math.Round((double)v)));

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((OverrideControl)d).UpdateDerived();

    private void UpdateDerived()
    {
        IsZero = Value <= 0;
        Display = DisplayFormat.Percent(Value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Minus") is ButtonBase minus) minus.Click += (_, _) => Value -= Step;
        if (GetTemplateChild("PART_Plus") is ButtonBase plus) plus.Click += (_, _) => Value += Step;
        if (GetTemplateChild("PART_Value") is ButtonBase val) { _valueButton = val; val.Click += (_, _) => OpenSheet(val); }
    }

    /// <summary>Feuille d'ajustement : curseur 400 px et valeurs rapides, ancrée sous la valeur.</summary>
    private FrameworkElement? _valueButton;

    /// <summary>Ouvre la feuille d'ajustement (utilisé aussi par les captures d'écran).</summary>
    public void ShowSheet()
    {
        if (_valueButton != null) OpenSheet(_valueButton);
    }

    private void OpenSheet(FrameworkElement anchor)
    {
        var host = OverlayHost.Current;
        if (host == null) return;

        var title = new TextBlock { Text = Label.ToUpperInvariant(), Style = Tokens.Get<Style>("Type.Caps") };
        var big = new TextBlock { Style = Tokens.Get<Style>("Type.Value"), Margin = new Thickness(0, 4, 0, 10) };
        big.SetBinding(TextBlock.TextProperty, new Binding(nameof(Display)) { Source = this });
        var slider = new TouchSlider { Width = 400, Minimum = 0, Maximum = 100, Step = 1, Margin = new Thickness(0, 0, 0, 14) };
        slider.SetBinding(TouchSlider.ValueProperty, new Binding(nameof(Value)) { Source = this, Mode = BindingMode.TwoWay });

        var quick = new UniformGrid { Columns = 5, Width = 400 };
        for (int i = 0; i <= 4; i++)
        {
            var target = i * 25.0;
            var b = new Button { Content = DisplayFormat.Percent(target), Height = 48, Margin = new Thickness(i == 0 ? 0 : 4, 0, i == 4 ? 0 : 4, 0), Padding = new Thickness(0), MinWidth = 0 };
            b.Click += (_, _) => Value = target;
            quick.Children.Add(b);
        }
        var panel = new StackPanel();
        panel.Children.Add(title);
        panel.Children.Add(big);
        panel.Children.Add(slider);
        panel.Children.Add(quick);
        if (!string.IsNullOrEmpty(Chain))
            panel.Children.Add(new TextBlock { Text = Chain, Style = Tokens.Get<Style>("Type.Caption"), Margin = new Thickness(0, 12, 0, 0) });

        var card = new Border
        {
            Background = Tokens.Brush("Bg.Surface"),
            BorderBrush = Tokens.Brush("Border.Default"),
            BorderThickness = new Thickness(1),
            CornerRadius = Tokens.Radius("Radius.XL"),
            Padding = new Thickness(20),
            Effect = Tokens.Get<System.Windows.Media.Effects.Effect>("Shadow.Overlay"),
            Child = panel,
        };
        var p = anchor.TransformToVisual(host).Transform(new Point(0, 0));
        host.Show(card, OverlayPlacement.Anchored, new Rect(p, new Size(anchor.ActualWidth, anchor.ActualHeight)), dim: true);
    }
}
