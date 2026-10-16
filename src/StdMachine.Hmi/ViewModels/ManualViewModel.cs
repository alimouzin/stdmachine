using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StdMachine.Core;
using StdMachine.Hmi.Controls;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Services;
using StdMachine.Hmi.Theme;
using StdMachine.Simulation.Demo;

namespace StdMachine.Hmi.ViewModels;

public sealed partial class ManualObjectViewModel : ObservableObject
{
    public ManualObjectViewModel(ObjectNode node, int depth, TypeDef? type)
    {
        Node = node; Depth = depth; Type = type;
        Name = node.Name;
        Icon = type?.Icon ?? "folder";
        Indent = new Thickness(depth * 20, 0, 0, 0);
    }

    public ObjectNode Node { get; }
    public int Depth { get; }
    public TypeDef? Type { get; }
    public string Name { get; }
    public string Icon { get; }
    public Thickness Indent { get; }
    [ObservableProperty] private Tone _tone = Tone.Neutral;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isBusy;
}

public sealed record ValueRow(string Label, string Value, string Unit);

public sealed record ManualActionButton(string Name, string Icon, bool HasParams, ActionDef? Action, FunctionDef? Function, string Subtitle);

public sealed record PositionRow(Guid Id, string Name, bool Taught, string X, string Y, string Z, string C, double MapX, double MapY, bool Mappable);

/// <summary>Page Mode manuel (docs/08 § 6.3) : prérequis, objets, panneau généré depuis le catalogue, positions nommées, journal.</summary>
public sealed partial class ManualViewModel : ViewModelBase
{
    private readonly StationConfig _station;
    private ManualObjectViewModel? _selected;

    public ManualViewModel(int station)
    {
        _station = AppServices.Project.Project.Station(station) ?? AppServices.Project.Project.Stations[0];
        AppServices.Selection.Station = _station.Number;
        foreach (var s in AppServices.Project.Project.Stations)
            Chips.Add(new StationChipViewModel(s.Number, s.Name, n => AppServices.Navigation.Navigate("Manual:" + n)) { IsSelected = s.Number == _station.Number });
        void Walk(ObjectNode n, int depth)
        {
            Objects.Add(new ManualObjectViewModel(n, depth, DemoCatalog.Get(n.TypeCode)));
            foreach (var c in n.Children) Walk(c, depth + 1);
        }
        foreach (var o in _station.Objects) Walk(o, 0);
        foreach (var p in _station.Positions)
        {
            Positions.Add(new PositionRow(p.Id, p.Name, p.Taught, p.Taught ? DisplayFormat.Number(p.X, 2) : "—", p.Taught ? DisplayFormat.Number(p.Y, 2) : "—",
                p.Taught ? DisplayFormat.Number(p.Z, 2) : "—", p.Taught ? DisplayFormat.Number(p.C, 2) : "—", p.X * MapScale + 30, p.Y * MapScale + 30, p.Taught));
        }
        Journal.Add(new JournalRow(DisplayFormat.ClockShort(DateTime.Now.AddMinutes(-4)), "Axe Z · Move absolu", true));
        Journal.Add(new JournalRow(DisplayFormat.ClockShort(DateTime.Now.AddMinutes(-9)), "Aspiration · Aspirer", true));
        Select(Objects.FirstOrDefault(o => o.Node.TypeCode == "CartesianRobot") ?? Objects[0]);
        OnSnapshots((_, s) => Refresh(s));
        Refresh(AppServices.Snapshots.Current);
        AppServices.Session.PropertyChanged += (_, _) => Refresh(AppServices.Snapshots.Current);
    }

    public ObservableCollection<StationChipViewModel> Chips { get; } = new ObservableCollection<StationChipViewModel>();
    public ObservableCollection<ManualObjectViewModel> Objects { get; } = new ObservableCollection<ManualObjectViewModel>();
    public ObservableCollection<ValueRow> Values { get; } = new ObservableCollection<ValueRow>();
    public ObservableCollection<ManualActionButton> Actions { get; } = new ObservableCollection<ManualActionButton>();
    public ObservableCollection<PositionRow> Positions { get; } = new ObservableCollection<PositionRow>();
    public ObservableCollection<JournalRow> Journal { get; } = new ObservableCollection<JournalRow>();

    public const double MapScale = 2.2;
    public string StationName => _station.Name;

    // prérequis
    [ObservableProperty] private bool _modeOk, _stateOk, _rightOk;
    [ObservableProperty] private bool _available;
    [ObservableProperty] private string _modeText = string.Empty, _stateText = string.Empty, _rightText = string.Empty;

    // objet sélectionné
    [ObservableProperty] private string _objectTitle = string.Empty;
    [ObservableProperty] private string _objectType = string.Empty;
    [ObservableProperty] private string _objectStateText = string.Empty;
    [ObservableProperty] private Tone _objectTone;
    [ObservableProperty] private bool _isAxis;
    [ObservableProperty] private bool _isRobot;
    [ObservableProperty] private double _travel;
    [ObservableProperty] private string _travelText = string.Empty;
    [ObservableProperty] private double _manualOverride = 50;
    [ObservableProperty] private string _lastResult = string.Empty;
    [ObservableProperty] private bool _running;
    [ObservableProperty] private double _crossX = double.NaN, _crossY = double.NaN;
    [ObservableProperty] private bool _hasActions;
    [ObservableProperty] private bool _hasValues;

    public ManualObjectViewModel? SelectedObject => _selected;

    [RelayCommand]
    private void Select(ManualObjectViewModel obj)
    {
        if (_selected != null) _selected.IsSelected = false;
        _selected = obj;
        obj.IsSelected = true;
        OnPropertyChanged(nameof(SelectedObject));
        ObjectTitle = obj.Name;
        ObjectType = obj.Type?.Name ?? obj.Node.TypeCode;
        IsAxis = obj.Node.TypeCode == "LinearAxis" || obj.Node.TypeCode == "RotaryAxis";
        IsRobot = obj.Node.TypeCode == "CartesianRobot";
        Actions.Clear();
        if (obj.Type != null)
        {
            foreach (var f in obj.Type.Functions) Actions.Add(new ManualActionButton(f.Name, "precision_manufacturing", f.Params.Count > 0, null, f, f.Params.Count > 0 ? string.Join(", ", f.Params.Select(p => p.Name)) : "sans paramètre"));
            foreach (var a in obj.Type.Actions) Actions.Add(new ManualActionButton(a.Name, a.Icon, a.Params.Count > 0, a, null, a.Params.Count > 0 ? string.Join(", ", a.Params.Select(p => p.Name)) : "sans paramètre"));
        }
        HasActions = Actions.Count > 0;
        LastResult = string.Empty;
        Refresh(AppServices.Snapshots.Current);
    }

    private static string[] LabelsFor(string type)
    {
        switch (type)
        {
            case "LinearAxis": case "RotaryAxis": return new[] { "Position|mm", "Courant|%", "Power on|", "Référencé|" };
            case "VacuumBlow": return new[] { "Aspiration|", "Soufflage|", "Pression|kPa", "Pièce présente|" };
            case "Press": return new[] { "Force|N", "Course|mm", "Recette|", "Défaut|" };
            case "AsyrilEyePlus": return new[] { "Recette|", "Prête|", "Pièces trouvées|", "Défaut|" };
            case "Counter": return new[] { "Valeur|", "Défaut|", "|", "|" };
            case "DigitalInput": return new[] { "Valeur|", "Forcée|", "|", "|" };
            default: return new[] { "|", "|", "|", "|" };
        }
    }

    private void Refresh(PlcSnapshot s)
    {
        var st = s.Station(_station.Number);
        var user = AppServices.Session.Level;
        ModeOk = s.Machine.Mode == MachineMode.Maintenance;
        StateOk = st != null && st.State == PackMlState.Stopped;
        RightOk = user >= 1;
        Available = ModeOk && StateOk && RightOk;
        ModeText = ModeOk ? "Mode Maintenance" : "Mode Production : passer en Maintenance";
        StateText = StateOk ? "Station Stopped" : "Station " + (st?.State.ToString() ?? "—") + " : l'arrêter";
        RightText = RightOk ? "Droit Régleur" : "Droit Régleur requis";

        var objects = s.ObjectsOf(_station.Number).ToDictionary(o => o.ObjectId);
        foreach (var o in Objects)
        {
            if (!objects.TryGetValue(o.Node.Id, out var os)) { o.Tone = Tone.Neutral; continue; }
            o.Tone = os.State == ObjectState.Ready ? Tone.Ok : os.State == ObjectState.Busy ? Tone.Busy : os.State == ObjectState.Error ? Tone.Err : os.State == ObjectState.Disabled ? Tone.Neutral : Tone.Warn;
            o.IsBusy = os.State == ObjectState.Busy;
        }
        if (_selected != null && objects.TryGetValue(_selected.Node.Id, out var sel))
        {
            ObjectStateText = sel.State == ObjectState.Ready ? "Prêt" : sel.State == ObjectState.Busy ? "Occupé" : sel.State == ObjectState.Error ? "Erreur" : sel.State == ObjectState.Disabled ? "Désactivé" : "Non initialisé";
            ObjectTone = _selected.Tone;
            var labels = LabelsFor(_selected.Node.TypeCode);
            var rows = new List<ValueRow>();
            for (int i = 0; i < 4 && i < sel.Values.Count; i++)
            {
                var parts = labels[i].Split('|');
                if (parts[0].Length == 0) continue;
                double v = sel.Values[i];
                bool isBool = parts[1].Length == 0 && (parts[0] == "Power on" || parts[0] == "Référencé" || parts[0] == "Aspiration" || parts[0] == "Soufflage" || parts[0] == "Pièce présente" || parts[0] == "Prête" || parts[0] == "Défaut" || parts[0] == "Forcée");
                rows.Add(new ValueRow(parts[0], isBool ? (v != 0 ? "Oui" : "Non") : DisplayFormat.Number(v, parts[1] == "N" ? 0 : 2), parts[1]));
            }
            if (!Values.SequenceEqual(rows)) { Values.Clear(); foreach (var r in rows) Values.Add(r); }
            HasValues = Values.Count > 0;
            if (IsAxis && sel.Values.Count > 0)
            {
                Travel = Math.Max(0, Math.Min(1, (sel.Values[0] + 60) / 460.0));
                TravelText = DisplayFormat.Number(sel.Values[0], 2) + " mm";
            }
            if (!string.IsNullOrEmpty(sel.ActionText)) LastResult = "En cours : " + sel.ActionText;
        }
        // Croix du robot : positions des axes X et Y
        var ax = Objects.FirstOrDefault(o => o.Node.ChildRole == "AxisX");
        var ay = Objects.FirstOrDefault(o => o.Node.ChildRole == "AxisY");
        if (ax != null && ay != null && objects.TryGetValue(ax.Node.Id, out var ox) && objects.TryGetValue(ay.Node.Id, out var oy) && ox.Values.Count > 0 && oy.Values.Count > 0)
        { CrossX = ox.Values[0] * MapScale + 30; CrossY = oy.Values[0] * MapScale + 30; }
    }

    [RelayCommand]
    private async Task SwitchToMaintenance()
        => await AppServices.Commands.RunAsync("mode", () => AppServices.Plc.SetModeAsync(MachineMode.Maintenance, CancellationToken.None), "Mode Maintenance.");

    [RelayCommand]
    private async Task StopStation()
        => await AppServices.Commands.SendStationAsync(_station.Number, StateCommand.Stop);

    [RelayCommand]
    private async Task RunAction(ManualActionButton button)
    {
        if (_selected == null) return;
        if (!Available) { AppServices.Toast.Show("Manuel indisponible : " + (!ModeOk ? "passez en Maintenance." : !StateOk ? "arrêtez la station." : "droit Régleur requis."), ToastKind.Warning); return; }
        var args = new List<double>();
        var defs = button.Action?.Params ?? button.Function?.Params;
        if (defs != null)
            foreach (var d in defs)
            {
                if (d.Type == "Position" || d.Type == "Speed" || d.Type == "Bool" || d.Type == "Text") { args.Add(d.Default); continue; }
                args.Add(d.Default);
            }
        Running = true;
        LastResult = "En cours…";
        var r = await AppServices.Plc.ExecuteManualAsync(new ManualActionRequest(_station.Number, _selected.Node.Id, button.Name, args), CancellationToken.None);
        Running = false;
        LastResult = r.Accepted ? "Terminée · " + string.Join(" · ", (r.Results ?? new ConditionTermRuntime[0]).Select(t => t.Text)) : "Refusée : " + r.Reason;
        Journal.Insert(0, new JournalRow(DisplayFormat.ClockShort(DateTime.Now), _selected.Name + " · " + button.Name, r.Accepted));
    }

    [RelayCommand]
    private async Task GoTo(PositionRow row)
    {
        if (!Available) { AppServices.Toast.Show("Manuel indisponible : conditions d'accès non réunies.", ToastKind.Warning); return; }
        if (!row.Taught) { AppServices.Toast.Show("Position non apprise : apprenez-la d'abord.", ToastKind.Warning); return; }
        bool ok = await AppServices.Dialogs.ConfirmAsync("Aller à « " + row.Name + " »", "Le robot va se déplacer à la position " + row.Name + ".", "Aller");
        if (!ok) return;
        var robot = Objects.First(o => o.Node.TypeCode == "CartesianRobot");
        var r = await AppServices.Plc.ExecuteManualAsync(new ManualActionRequest(_station.Number, robot.Node.Id, "Aller à", new[] { 0.0 }), CancellationToken.None);
        Journal.Insert(0, new JournalRow(DisplayFormat.ClockShort(DateTime.Now), "Robot XYZC · Aller à " + row.Name, r.Accepted));
        AppServices.Toast.Show("Position " + row.Name + " atteinte.", ToastKind.Success);
    }

    [RelayCommand]
    private void Teach(PositionRow row)
    {
        if (!Available) { AppServices.Toast.Show("Manuel indisponible : conditions d'accès non réunies.", ToastKind.Warning); return; }
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 24) };
        root.Children.Add(new TextBlock { Text = row.Name, Style = Tokens.Get<Style>("Type.Title"), Margin = new Thickness(0, 0, 0, 12) });
        root.Children.Add(new TextBlock { Text = "COMPOSANTES À APPRENDRE", Style = Tokens.Get<Style>("Type.Caps"), Margin = new Thickness(0, 0, 0, 8) });
        foreach (var comp in new[] { "X", "Y", "Z", "C" })
        {
            var line = new DockPanel { Height = 48 };
            var val = new TextBlock { Text = "actuel 212,40 · enregistré " + (row.Taught ? "212,10" : "—") + " · écart 0,30 mm", Style = Tokens.Get<Style>("Type.Caption"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(val, Dock.Right);
            line.Children.Add(val);
            line.Children.Add(new CheckBox { Content = comp, IsChecked = true, VerticalAlignment = VerticalAlignment.Center });
            root.Children.Add(line);
        }
        var hold = new HoldButton { Content = "Apprendre (maintenir)", Width = 280, Height = 56, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 16, 0, 0) };
        Ui.SetVariant(hold, ButtonVariant.Primary);
        Ui.SetIcon(hold, "my_location");
        hold.Confirmed += (_, _) => { AppServices.Toast.Show("Position « " + row.Name + " » apprise (maquette : non enregistrée).", ToastKind.Success); Journal.Insert(0, new JournalRow(DisplayFormat.ClockShort(DateTime.Now), "Apprentissage " + row.Name, true)); };
        root.Children.Add(hold);
        AppServices.Drawer.Show("Apprendre une position", root);
    }

    [RelayCommand]
    private void ParamSheet(ManualActionButton b)
    {
        if (!b.HasParams) { RunActionCommand.Execute(b); return; }
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 24) };
        var defs = b.Action?.Params ?? b.Function?.Params ?? new List<ParamDef>();
        foreach (var d in defs)
        {
            if (d.Type == "Position")
            {
                var f = new TextField { Label = d.Name, Text = _station.Positions.FirstOrDefault()?.Name ?? "", Margin = new Thickness(0, 0, 0, 10) };
                root.Children.Add(f);
            }
            else if (d.Type == "Speed") root.Children.Add(new TextField { Label = d.Name, Text = "Rapide (V2)", Margin = new Thickness(0, 0, 0, 10) });
            else if (d.Type == "Bool") root.Children.Add(new CheckBox { Content = d.Name, IsChecked = d.Default != 0, Margin = new Thickness(0, 0, 0, 10), Height = 44 });
            else root.Children.Add(new NumberField { Label = d.Name, Value = d.Default, Unit = d.Unit ?? "", Minimum = d.Min ?? -100000, Maximum = d.Max ?? 100000, Step = 1, Decimals = d.Type == "Int" ? 0 : 2, Margin = new Thickness(0, 0, 0, 10) });
        }
        var run = new Button { Content = "Exécuter", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        Ui.SetVariant(run, ButtonVariant.Primary);
        Ui.SetIcon(run, "play_arrow");
        run.Click += (_, _) => RunActionCommand.Execute(b);
        root.Children.Add(run);
        AppServices.Drawer.Show(b.Name, root);
    }
}

public sealed record JournalRow(string Time, string Text, bool Ok);
