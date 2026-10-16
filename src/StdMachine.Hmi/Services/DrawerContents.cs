using System.Windows;
using System.Windows.Controls;
using StdMachine.Core;
using StdMachine.Hmi.Controls;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.Services;

/// <summary>Contenus de tiroirs construits à partir de l'instantané (mover, objet…).</summary>
public static class MoverDrawerContent
{
    public static FrameworkElement Build(MoverStatus m)
    {
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 24) };
        var tone = UiText.MoverTone(m.State);
        root.Children.Add(new StatusPill { Tone = tone, Text = UiText.MoverState(m.State), IconName = "crop_square", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) });
        root.Children.Add(Row("Position", DisplayFormat.Number(m.X, 0) + " ; " + DisplayFormat.Number(m.Y, 0) + " mm"));
        root.Children.Add(Row("Station", m.StationNumber.HasValue ? AppServices.Project.Project.Station(m.StationNumber.Value)?.Name ?? "—" : "—"));
        root.Children.Add(Row("Étape du process général", m.GeneralStepLabel ?? "Aucune"));
        root.Children.Add(Row("Destination", m.DestinationX.HasValue ? DisplayFormat.Number(m.DestinationX.Value, 0) + " ; " + DisplayFormat.Number(m.DestinationY ?? 0, 0) + " mm" : "—"));
        root.Children.Add(new TextBlock { Text = "PIÈCES", Style = Tokens.Get<Style>("Type.Caps"), Margin = new Thickness(0, 18, 0, 8) });
        if (m.Parts.Count == 0)
            root.Children.Add(new TextBlock { Text = "Aucune pièce sur ce mover.", Foreground = Tokens.Brush("Text.Secondary") });
        foreach (var p in m.Parts)
        {
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            line.Children.Add(new StatusDot { Tone = p.State == PartState.Bad ? Tone.Err : p.State == PartState.Good ? Tone.Ok : Tone.Neutral, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
            line.Children.Add(new TextBlock { Text = "Emplacement " + p.Slot + " · " + p.Id + " · " + UiText.PartState(p.State), FontWeight = FontWeights.Medium });
            root.Children.Add(line);
        }
        var go = new Button { Content = "Voir dans Pièces", Margin = new Thickness(0, 20, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        Ui.SetIcon(go, "inventory_2");
        go.Click += (_, _) => AppServices.Navigation.Navigate("Parts:" + m.Number, null, pushHistory: true);
        root.Children.Add(go);
        var send = new Button { Content = "Envoyer vers…", HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = AppServices.Snapshots.Current.Machine.Mode == MachineMode.Maintenance };
        Ui.SetIcon(send, "open_with");
        root.Children.Add(send);
        if (!send.IsEnabled)
            root.Children.Add(new TextBlock { Text = "Disponible en mode Maintenance, machine arrêtée.", Style = Tokens.Get<Style>("Type.Caption"), Margin = new Thickness(0, 6, 0, 0) });
        return root;
    }

    private static FrameworkElement Row(string key, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new TextBlock { Text = key, Style = Tokens.Get<Style>("Type.Caption"), VerticalAlignment = VerticalAlignment.Center });
        var v = new TextBlock { Text = value, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        return g;
    }
}
