using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using StdMachine.Hmi.Controls;
using StdMachine.Hmi.Resources;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Services;

public enum ToastKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>Saisie par le pavé numérique et le clavier intégrés, en surimpression ancrée près du champ (docs/07 § 8.4).</summary>
public sealed class KeypadService
{
    public Task<double?> EditNumberAsync(FrameworkElement anchor, NumberEditRequest request)
    {
        var host = OverlayHost.Current ?? throw new InvalidOperationException("Aucune surimpression disponible.");
        var tcs = new TaskCompletionSource<double?>();
        var pad = new NumericKeypad(request);
        var handle = host.Show(pad, OverlayPlacement.Anchored, AnchorRect(host, anchor), dim: true, onDismiss: () => tcs.TrySetResult(null));
        pad.Completed += (_, v) => { handle.Dispose(); tcs.TrySetResult(v); };
        return tcs.Task;
    }

    public Task<string?> EditTextAsync(FrameworkElement anchor, TextEditRequest request)
    {
        var host = OverlayHost.Current ?? throw new InvalidOperationException("Aucune surimpression disponible.");
        var tcs = new TaskCompletionSource<string?>();
        var kb = new Controls.Keyboard(request);
        var handle = host.Show(kb, OverlayPlacement.Anchored, AnchorRect(host, anchor), dim: true, onDismiss: () => tcs.TrySetResult(null));
        kb.Completed += (_, v) => { handle.Dispose(); tcs.TrySetResult(v); };
        return tcs.Task;
    }

    private static Rect AnchorRect(OverlayHost host, FrameworkElement anchor)
    {
        var p = anchor.TransformToVisual(host).Transform(new Point(0, 0));
        return new Rect(p, new Size(anchor.ActualWidth, anchor.ActualHeight));
    }
}

public sealed class ToastService
{
    public void Show(string text, ToastKind kind = ToastKind.Info)
    {
        var host = OverlayHost.Current;
        if (host == null) return;
        string icon;
        Brush back;
        switch (kind)
        {
            case ToastKind.Success: icon = "check_circle"; back = Tokens.Brush("Ok.Dot"); break;
            case ToastKind.Warning: icon = "warning"; back = Tokens.Brush("Warn.Dot"); break;
            case ToastKind.Error: icon = "error"; back = Tokens.Brush("Err.Dot"); break;
            default: icon = "info"; back = Tokens.Brush("Text.Primary"); break;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new Icon { Glyph = icon, Foreground = Tokens.Brush("Text.OnColor"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(new TextBlock { Text = text, Foreground = Tokens.Brush("Text.OnColor"), FontWeight = FontWeights.Medium, VerticalAlignment = VerticalAlignment.Center });
        var toast = new Border
        {
            Background = back,
            CornerRadius = Tokens.Radius("Radius.M"),
            MinHeight = 48,
            Padding = new Thickness(18, 0, 22, 0),
            Margin = new Thickness(0, 6, 0, 0),
            Effect = Tokens.Get<System.Windows.Media.Effects.Effect>("Shadow.Raised"),
            Child = row,
        };
        host.ShowToast(toast, TimeSpan.FromSeconds(3));
    }
}

/// <summary>Tiroir de 480 px qui glisse depuis la droite (docs/08 § 3).</summary>
public sealed class DrawerService
{
    public OverlayHandle Show(string title, FrameworkElement content, Action? onClosed = null)
    {
        var host = OverlayHost.Current ?? throw new InvalidOperationException("Aucune surimpression disponible.");
        OverlayHandle? handle = null;
        var close = new Button { Content = null, Width = 48, Height = 48, MinWidth = 48 };
        Ui.SetVariant(close, ButtonVariant.Ghost);
        Ui.SetIcon(close, "close");
        close.Click += (_, _) => { handle?.Dispose(); onClosed?.Invoke(); };

        var header = new DockPanel { Margin = new Thickness(24, 12, 12, 12), LastChildFill = true };
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(new TextBlock { Text = title, Style = Tokens.Get<Style>("Type.Title"), VerticalAlignment = VerticalAlignment.Center });

        var body = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(body, 1);
        grid.Children.Add(header);
        grid.Children.Add(body);

        var panel = new Border
        {
            Background = Tokens.Brush("Bg.Surface"),
            BorderBrush = Tokens.Brush("Border.Default"),
            BorderThickness = new Thickness(1, 0, 0, 0),
            Effect = Tokens.Get<System.Windows.Media.Effects.Effect>("Shadow.Overlay"),
            Child = grid,
        };
        handle = host.Show(panel, OverlayPlacement.Drawer, dim: true, onDismiss: onClosed);
        return handle;
    }
}

/// <summary>Dialogues tactiles (jamais de MessageBox).</summary>
public sealed class DialogService
{
    /// <summary>Bulle d'explication ancrée près d'un élément : « Démarrer est possible depuis Idle ou Held… » avec le bouton qui y mène.</summary>
    public void ShowBubble(FrameworkElement anchor, string text, string? actionLabel, Action? action)
    {
        var host = OverlayHost.Current;
        if (host == null) return;
        OverlayHandle? handle = null;
        var body = new StackPanel { MaxWidth = 420 };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new Icon { Glyph = "info", Foreground = Tokens.Brush("Busy.Dot"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top });
        row.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 360 });
        body.Children.Add(row);
        if (actionLabel != null && action != null)
        {
            var b = new Button { Content = actionLabel, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            Ui.SetVariant(b, ButtonVariant.Primary);
            b.Click += (_, _) => { handle?.Dispose(); action(); };
            body.Children.Add(b);
        }
        var card = new Border
        {
            Background = Tokens.Brush("Bg.Surface"), BorderBrush = Tokens.Brush("Border.Default"), BorderThickness = new Thickness(1),
            CornerRadius = Tokens.Radius("Radius.L"), Padding = new Thickness(16), Child = body,
            Effect = Tokens.Get<System.Windows.Media.Effects.Effect>("Shadow.Overlay"),
        };
        var p = anchor.TransformToVisual(host).Transform(new Point(0, 0));
        handle = host.Show(card, OverlayPlacement.Anchored, new Rect(p, new Size(anchor.ActualWidth, anchor.ActualHeight)), dim: false);
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmLabel, bool danger = false)
    {
        var host = OverlayHost.Current ?? throw new InvalidOperationException("Aucune surimpression disponible.");
        var tcs = new TaskCompletionSource<bool>();
        OverlayHandle? handle = null;

        var cancel = new Button { Content = Res.Get("Common.Cancel"), Height = 56, MinWidth = 140, Margin = new Thickness(0, 0, 12, 0) };
        var ok = new Button { Content = confirmLabel, Height = 56, MinWidth = 160 };
        Ui.SetVariant(ok, danger ? ButtonVariant.Danger : ButtonVariant.Primary);
        cancel.Click += (_, _) => { handle?.Dispose(); tcs.TrySetResult(false); };
        ok.Click += (_, _) => { handle?.Dispose(); tcs.TrySetResult(true); };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        var stack = new StackPanel { Width = 480 };
        stack.Children.Add(new TextBlock { Text = title, Style = Tokens.Get<Style>("Type.Title") });
        stack.Children.Add(new TextBlock { Text = message, Style = Tokens.Get<Style>("Type.Body"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = Tokens.Brush("Text.Secondary") });
        stack.Children.Add(buttons);

        var card = new Border
        {
            Background = Tokens.Brush("Bg.Surface"),
            CornerRadius = Tokens.Radius("Radius.XL"),
            Padding = new Thickness(28),
            Effect = Tokens.Get<System.Windows.Media.Effects.Effect>("Shadow.Overlay"),
            Child = stack,
        };
        handle = host.Show(card, OverlayPlacement.Center, dim: true, onDismiss: () => tcs.TrySetResult(false));
        return tcs.Task;
    }
}

/// <summary>Conteneur de services construit à la main (pas de Microsoft.Extensions.Hosting dans le PoC).</summary>
public static partial class AppServices
{
    public static KeypadService Keypad { get; } = new KeypadService();
    public static ToastService Toast { get; } = new ToastService();
    public static DrawerService Drawer { get; } = new DrawerService();
    public static DialogService Dialogs { get; } = new DialogService();
}
