using System.Windows;
using System.Windows.Controls;
using StdMachine.Hmi.Controls;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Services;

public sealed record PickItem(string Label, string? Detail, object Tag, bool IsOk = true, string? Icon = null);

/// <summary>Liste déroulante tactile (éléments de 56 px, docs/07 § 8.4), en surimpression ancrée près du champ.</summary>
public static class Pickers
{
    public static Task<object?> PickAsync(FrameworkElement anchor, string title, IReadOnlyList<PickItem> items, object? current = null)
    {
        var host = OverlayHost.Current ?? throw new InvalidOperationException("Aucune surimpression disponible.");
        var tcs = new TaskCompletionSource<object?>();
        OverlayHandle? handle = null;

        var list = new StackPanel();
        foreach (var item in items)
        {
            var row = new Button { Height = 56, MinHeight = 56, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 2) };
            Ui.SetVariant(row, ButtonVariant.Ghost);
            var dock = new DockPanel { LastChildFill = true };
            if (item.Icon != null) dock.Children.Add(new Icon { Glyph = item.Icon, Margin = new Thickness(0, 0, 10, 0), Foreground = Tokens.Brush("Text.Secondary") });
            if (item.Detail != null)
            {
                var d = new TextBlock { Text = item.Detail, Style = Tokens.Get<Style>("Type.Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
                DockPanel.SetDock(d, Dock.Right);
                dock.Children.Add(d);
            }
            var mark = new Icon { Glyph = item.IsOk ? "check_circle" : "cancel", FontSize = 20, Foreground = Tokens.Brush(item.IsOk ? "Ok.Dot" : "Err.Dot"), Margin = new Thickness(0, 0, 8, 0), Visibility = item.Icon == null && item.Detail != null && !item.IsOk ? Visibility.Visible : Visibility.Collapsed };
            if (!item.IsOk) dock.Children.Add(mark);
            dock.Children.Add(new TextBlock { Text = item.Label, FontWeight = Equals(item.Tag, current) ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center });
            row.Content = dock;
            row.Click += (_, _) => { handle?.Dispose(); tcs.TrySetResult(item.Tag); };
            list.Children.Add(row);
        }
        var body = new DockPanel { Width = 380 };
        var head = new TextBlock { Text = title.ToUpperInvariant(), Style = Tokens.Get<Style>("Type.Caps"), Margin = new Thickness(4, 0, 0, 8) };
        DockPanel.SetDock(head, Dock.Top);
        body.Children.Add(head);
        body.Children.Add(new ScrollViewer { Content = list, MaxHeight = 56 * 9 + 20, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var card = new Border
        {
            Background = Tokens.Brush("Bg.Surface"), BorderBrush = Tokens.Brush("Border.Default"), BorderThickness = new Thickness(1),
            CornerRadius = Tokens.Radius("Radius.XL"), Padding = new Thickness(14), Child = body,
            Effect = Tokens.Get<System.Windows.Media.Effects.Effect>("Shadow.Overlay"),
        };
        var p = anchor.TransformToVisual(host).Transform(new Point(0, 0));
        handle = host.Show(card, OverlayPlacement.Anchored, new Rect(p, new Size(anchor.ActualWidth, anchor.ActualHeight)), dim: true, onDismiss: () => tcs.TrySetResult(null));
        return tcs.Task;
    }
}
