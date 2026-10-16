using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using StdMachine.Core;
using StdMachine.Hmi.Services;

namespace StdMachine.Hmi.Controls;

/// <summary>Base d'un champ qui s'ouvre au toucher (pavé numérique ou clavier) : cible de 48 px, pas de curseur de texte.</summary>
public abstract class TapField : Control
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(TapField), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IsInvalidProperty = DependencyProperty.Register(
        nameof(IsInvalid), typeof(bool), typeof(TapField), new PropertyMetadata(false));
    public static readonly DependencyProperty IsPressedProperty = DependencyProperty.Register(
        nameof(IsPressed), typeof(bool), typeof(TapField), new PropertyMetadata(false));

    protected TapField() { Focusable = false; MinHeight = 48; }

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool IsInvalid { get => (bool)GetValue(IsInvalidProperty); set => SetValue(IsInvalidProperty, value); }
    public bool IsPressed { get => (bool)GetValue(IsPressedProperty); private set => SetValue(IsPressedProperty, value); }

    protected abstract Task OpenAsync();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (!IsEnabled) return;
        IsPressed = true;
        CaptureMouse();
        e.Handled = true;
    }

    protected override async void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsPressed) return;
        IsPressed = false;
        ReleaseMouseCapture();
        var p = e.GetPosition(this);
        if (p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight)
        {
            e.Handled = true;
            await OpenAsync();
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        IsPressed = false;
    }
}

/// <summary>Champ numérique : toucher → pavé numérique ancré près du champ, avec unité et bornes.</summary>
public class NumberField : TapField
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(NumberField),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((NumberField)d).UpdateDisplay()));
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(NumberField), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(NumberField), new PropertyMetadata(double.MinValue));
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(NumberField), new PropertyMetadata(double.MaxValue));
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(NumberField), new PropertyMetadata(1.0));
    public static readonly DependencyProperty DecimalsProperty = DependencyProperty.Register(
        nameof(Decimals), typeof(int), typeof(NumberField), new PropertyMetadata(0, (d, _) => ((NumberField)d).UpdateDisplay()));
    public static readonly DependencyProperty DisplayProperty = DependencyProperty.Register(
        nameof(Display), typeof(string), typeof(NumberField), new PropertyMetadata("0"));

    static NumberField() => DefaultStyleKeyProperty.OverrideMetadata(typeof(NumberField), new FrameworkPropertyMetadata(typeof(NumberField)));

    public NumberField() { UpdateDisplay(); }

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public int Decimals { get => (int)GetValue(DecimalsProperty); set => SetValue(DecimalsProperty, value); }
    public string Display { get => (string)GetValue(DisplayProperty); private set => SetValue(DisplayProperty, value); }

    private void UpdateDisplay() => Display = DisplayFormat.Number(Value, Decimals);

    protected override async Task OpenAsync()
    {
        var min = Minimum == double.MinValue ? -1_000_000 : Minimum;
        var max = Maximum == double.MaxValue ? 1_000_000 : Maximum;
        var result = await AppServices.Keypad.EditNumberAsync(this, new NumberEditRequest(
            string.IsNullOrEmpty(Label) ? Unit : Label, Value, Unit, min, max, Step, Decimals));
        if (result.HasValue) Value = result.Value;
    }
}

/// <summary>Champ texte : toucher → clavier AZERTY intégré.</summary>
public class TextField : TapField
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(TextField),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((TextField)d).UpdateShown()));
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder), typeof(string), typeof(TextField), new PropertyMetadata(string.Empty, (d, _) => ((TextField)d).UpdateShown()));
    public static readonly DependencyProperty ShownProperty = DependencyProperty.Register(
        nameof(Shown), typeof(string), typeof(TextField), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty IsPlaceholderProperty = DependencyProperty.Register(
        nameof(IsPlaceholder), typeof(bool), typeof(TextField), new PropertyMetadata(false));

    static TextField() => DefaultStyleKeyProperty.OverrideMetadata(typeof(TextField), new FrameworkPropertyMetadata(typeof(TextField)));

    public TextField() { UpdateShown(); }

    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Placeholder { get => (string)GetValue(PlaceholderProperty); set => SetValue(PlaceholderProperty, value); }
    public string Shown { get => (string)GetValue(ShownProperty); private set => SetValue(ShownProperty, value); }
    public bool IsPlaceholder { get => (bool)GetValue(IsPlaceholderProperty); private set => SetValue(IsPlaceholderProperty, value); }

    private void UpdateShown()
    {
        IsPlaceholder = string.IsNullOrEmpty(Text);
        Shown = IsPlaceholder ? Placeholder : Text;
    }

    protected override async Task OpenAsync()
    {
        var result = await AppServices.Keypad.EditTextAsync(this, new TextEditRequest(
            string.IsNullOrEmpty(Label) ? Placeholder : Label, Text, Placeholder));
        if (result != null) Text = result;
    }
}

/// <summary>État vide : icône 64 px, titre, phrase et action (docs/08 § 3 et § 8).</summary>
public class EmptyState : Control
{
    public static readonly DependencyProperty IconNameProperty = DependencyProperty.Register(
        nameof(IconName), typeof(string), typeof(EmptyState), new PropertyMetadata(null, (d, e) => ((EmptyState)d).IconChar = Icon.Char((string?)e.NewValue)));
    public static readonly DependencyProperty IconCharProperty = DependencyProperty.Register(
        nameof(IconChar), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(EmptyState), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(
        nameof(ActionText), typeof(string), typeof(EmptyState), new PropertyMetadata(null));
    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(
        nameof(ActionCommand), typeof(ICommand), typeof(EmptyState), new PropertyMetadata(null));

    static EmptyState() => DefaultStyleKeyProperty.OverrideMetadata(typeof(EmptyState), new FrameworkPropertyMetadata(typeof(EmptyState)));

    public string? IconName { get => (string?)GetValue(IconNameProperty); set => SetValue(IconNameProperty, value); }
    public string IconChar { get => (string)GetValue(IconCharProperty); private set => SetValue(IconCharProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string? ActionText { get => (string?)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
}

/// <summary>Cadenas + « Niveau Régleur requis » posé sur un élément protégé ; le toucher ouvre la connexion (docs/08 § 3).</summary>
public class LockOverlay : Control
{
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(
        nameof(Message), typeof(string), typeof(LockOverlay), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty CommandProperty = DependencyProperty.Register(
        nameof(Command), typeof(ICommand), typeof(LockOverlay), new PropertyMetadata(null));

    static LockOverlay() => DefaultStyleKeyProperty.OverrideMetadata(typeof(LockOverlay), new FrameworkPropertyMetadata(typeof(LockOverlay)));

    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (Command?.CanExecute(null) == true) Command.Execute(null);
        e.Handled = true;
    }
}
