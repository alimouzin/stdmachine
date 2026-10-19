using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StdMachine.Core;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Services;
using StdMachine.Hmi.Theme;
using StdMachine.Simulation.Demo;

namespace StdMachine.Hmi.ViewModels;

/// <summary>Station sur le plan et dans la liste de la page Machine.</summary>
public sealed partial class StationTileViewModel : ObservableObject
{
    public const double BoxWidth = 220, BoxHeight = 138;

    public StationTileViewModel(StationConfig cfg, double workX, double workY, IEnumerable<string> groups)
    {
        Number = cfg.Number;
        Name = cfg.Name;
        WorkX = workX; WorkY = workY;
        BoxX = workX - BoxWidth / 2;
        BoxY = workY < 360 ? -178 : 752;
        Groups = groups.ToList();
        GroupsText = string.Join(" · ", Groups);
    }

    public int Number { get; }
    public string Name { get; }
    public double WorkX { get; }
    public double WorkY { get; }
    public double BoxX { get; }
    public double BoxY { get; }
    public List<string> Groups { get; }
    public string GroupsText { get; }

    [ObservableProperty] private PackMlState _state;
    [ObservableProperty] private Tone _stateTone;
    [ObservableProperty] private string? _operation;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _timeText = string.Empty;
    [ObservableProperty] private string? _fault;
    [ObservableProperty] private bool _qualityHold;
    [ObservableProperty] private bool _outOfProduction;
    [ObservableProperty] private BeaconLights _lights;
    [ObservableProperty] private BeaconLights _blinking;
    [ObservableProperty] private bool _isAborted;
    [ObservableProperty] private bool _hasOperation;
    [ObservableProperty] private string _reintegrateLabel = string.Empty;
    [ObservableProperty] private bool _canReintegrate;
    /// <summary>Station d'entrée pendant « Finir les pièces » : son chargement est arrêté.</summary>
    [ObservableProperty] private bool _loadingStopped;

    public void Update(StationStatus s)
    {
        State = s.State;
        StateTone = UiText.StateTone(s.State);
        IsAborted = s.State == PackMlState.Aborted;
        Operation = s.Operation;
        HasOperation = s.Operation != null && s.State != PackMlState.Aborted;
        Progress = s.OperationEstimatedSeconds > 0 ? Math.Min(1, s.OperationElapsedSeconds / s.OperationEstimatedSeconds) : 0;
        TimeText = s.Operation == null ? string.Empty : DisplayFormat.Duration(s.OperationElapsedSeconds);
        Fault = s.FaultText;
        QualityHold = s.QualityHold;
        OutOfProduction = s.OutOfProduction;
        Lights = s.Beacon.Lights;
        Blinking = s.Beacon.Blinking;
        // Réintégration : Acquitter, puis Réarmer, puis Démarrer, la station seule (docs/02 § 9)
        switch (s.State)
        {
            case PackMlState.Aborted: ReintegrateLabel = Resources.Res.Get("Cmd.Clear"); CanReintegrate = true; break;
            case PackMlState.Stopped when s.StateSince > DateTime.Now.AddMinutes(-30) && s.Number == DemoProject.Pressage01 && AppServices.Snapshots.Current.Machine.State == PackMlState.Execute:
                ReintegrateLabel = Resources.Res.Get("Cmd.Reset"); CanReintegrate = true; break;
            case PackMlState.Idle when AppServices.Snapshots.Current.Machine.State == PackMlState.Execute:
                ReintegrateLabel = Resources.Res.Get("Cmd.Start"); CanReintegrate = true; break;
            default: ReintegrateLabel = string.Empty; CanReintegrate = false; break;
        }
    }
}

public sealed partial class MoverViewModel : ObservableObject
{
    public const double Size = 113;

    public MoverViewModel(int number) { Number = number; }

    public int Number { get; }

    [ObservableProperty] private double _x = double.NaN;
    [ObservableProperty] private double _y = double.NaN;
    [ObservableProperty] private MoverState _state;
    [ObservableProperty] private string _stateText = string.Empty;
    [ObservableProperty] private string? _stepLabel;
    [ObservableProperty] private bool _hasPart;
    [ObservableProperty] private PartState _partState;
    [ObservableProperty] private string? _partId;
    [ObservableProperty] private double _destX;
    [ObservableProperty] private double _destY;
    [ObservableProperty] private bool _showDestination;
    [ObservableProperty] private double _centerX;
    [ObservableProperty] private double _centerY;
    [ObservableProperty] private double _trail1X = double.NaN;
    [ObservableProperty] private double _trail1Y = double.NaN;
    [ObservableProperty] private double _trail2X = double.NaN;
    [ObservableProperty] private double _trail2Y = double.NaN;
    [ObservableProperty] private double _trail3X = double.NaN;
    [ObservableProperty] private double _trail3Y = double.NaN;
    [ObservableProperty] private bool _showTrail;
    [ObservableProperty] private int? _station;

    private readonly Queue<(double X, double Y)> _history = new Queue<(double, double)>();

    public void Update(MoverStatus m)
    {
        double nx = m.X - Size / 2, ny = m.Y - Size / 2;
        _history.Enqueue((X, Y));
        while (_history.Count > 3) _history.Dequeue();
        var h = _history.ToArray();
        bool moving = m.State == Core.MoverState.EnRoute;
        ShowTrail = moving;
        if (h.Length == 3)
        {
            Trail3X = h[0].X; Trail3Y = h[0].Y; Trail2X = h[1].X; Trail2Y = h[1].Y; Trail1X = h[2].X; Trail1Y = h[2].Y;
        }
        X = nx; Y = ny;
        CenterX = m.X; CenterY = m.Y;
        State = m.State;
        StateText = UiText.MoverState(m.State);
        StepLabel = m.GeneralStepLabel;
        Station = m.StationNumber;
        HasPart = m.Parts.Count > 0;
        PartState = HasPart ? m.Parts[0].State : Core.PartState.InProcess;
        PartId = HasPart ? m.Parts[0].Id : null;
        ShowDestination = m.State == Core.MoverState.EnRoute && m.DestinationX.HasValue;
        DestX = m.DestinationX ?? 0;
        DestY = m.DestinationY ?? 0;
    }
}

public sealed record PlanMarker(double X, double Y, double Width, double Height, string Kind, string Name);

/// <summary>Page Machine (docs/08 § 6.1) : plan planaire en direct, commandes, OF, liste des stations.</summary>
public sealed partial class MachineViewModel : ViewModelBase
{
    private readonly MachineProject _project = AppServices.Project.Project;
    private bool _syncingMode;

    public MachineViewModel()
    {
        var planar = _project.Machine.Planar;
        PlanWidth = planar.WidthMm;
        PlanHeight = planar.HeightMm;
        for (int c = 0; c < planar.Columns; c++)
            for (int r = 0; r < planar.Rows; r++)
                Tiles.Add(new PlanMarker(c * planar.TileMm + 3, r * planar.TileMm + 3, planar.TileMm - 6, planar.TileMm - 6, "Tile", string.Empty));
        foreach (var pp in _project.Machine.PlanarPositions)
        {
            switch (pp.Kind)
            {
                case PlanarPositionKind.Work: Markers.Add(new PlanMarker(pp.X - 16, pp.Y - 16, 32, 32, "Work", pp.Name)); break;
                case PlanarPositionKind.Queue: Markers.Add(new PlanMarker(pp.X - 22, pp.Y - 16, 44, 32, "Queue", pp.Name)); break;
                case PlanarPositionKind.Buffer: Markers.Add(new PlanMarker(pp.X - 56, pp.Y - 56, 112, 112, "Buffer", pp.Name)); break;
                default: Markers.Add(new PlanMarker(pp.X - 56, pp.Y - 56, 112, 112, "Park", pp.Name)); break;
            }
        }
        foreach (var st in _project.Stations)
        {
            var work = _project.Machine.PlanarPositions.First(p => p.Kind == PlanarPositionKind.Work && p.StationId == st.Id);
            Stations.Add(new StationTileViewModel(st, work.X, work.Y, _project.GroupsOf(st).Select(g => g.Name)));
        }
        for (int m = 1; m <= planar.Movers; m++) Movers.Add(new MoverViewModel(m));
        // Stations des fonctions de rôle Entrée : leur chargement s'arrête pendant « Finir les pièces ».
        _entryStations = _project.Machine.Groups.Where(g => g.Role == GroupRole.Entry)
            .SelectMany(g => g.StationIds).Select(id => _project.StationById(id)?.Number ?? 0).Where(n => n > 0).ToHashSet();

        OnSnapshots(OnSnapshot);
        AppServices.Commands.PropertyChanged += (_, _) => RefreshBusy();
        OnSnapshot(this, AppServices.Snapshots.Current);
    }

    public double PlanWidth { get; }
    public double PlanHeight { get; }
    public ObservableCollection<PlanMarker> Tiles { get; } = new ObservableCollection<PlanMarker>();
    public ObservableCollection<PlanMarker> Markers { get; } = new ObservableCollection<PlanMarker>();
    public ObservableCollection<StationTileViewModel> Stations { get; } = new ObservableCollection<StationTileViewModel>();
    public ObservableCollection<MoverViewModel> Movers { get; } = new ObservableCollection<MoverViewModel>();

    // ---- état machine
    [ObservableProperty] private PackMlState _state;
    [ObservableProperty] private string _stateSince = string.Empty;
    [ObservableProperty] private string? _stopCause;
    [ObservableProperty] private string? _declaredText;
    [ObservableProperty] private BeaconLights _lights;
    [ObservableProperty] private BeaconLights _blinking;
    [ObservableProperty] private int _modeIndex;
    [ObservableProperty] private double _overridePercent = 80;
    [ObservableProperty] private bool _canReset, _canStart, _canHold, _canStop, _canClear;
    [ObservableProperty] private bool _resetBusy, _startBusy, _holdBusy, _stopBusy, _clearBusy;

    // ---- fin des pièces en cours (docs/02 § 8, docs/08 § 6.1)
    [ObservableProperty] private bool _draining, _canDrain, _drainBusy;
    [ObservableProperty] private int _partsInProcess;
    [ObservableProperty] private double _drainProgress;
    [ObservableProperty] private string _drainText = string.Empty;
    [ObservableProperty] private string? _drainBlockedText;
    [ObservableProperty] private int _drainBlockingMover;
    private int _drainInitial;
    private bool _wasDraining;
    private readonly HashSet<int> _entryStations;

    // ---- OF
    [ObservableProperty] private string _orderTitle = string.Empty;
    [ObservableProperty] private string _orderCountText = string.Empty;
    [ObservableProperty] private double _orderGood, _orderBad, _orderActive;
    [ObservableProperty] private string _orderPercent = string.Empty;
    [ObservableProperty] private string _orderGoodText = string.Empty, _orderBadText = string.Empty, _orderActiveText = string.Empty;
    [ObservableProperty] private string _orderEnd = string.Empty;
    [ObservableProperty] private string _budgetText = string.Empty;
    [ObservableProperty] private bool _hasOrder;

    private void OnSnapshot(object? sender, PlcSnapshot s)
    {
        var m = s.Machine;
        State = m.State;
        StateSince = UiText.Since(m.StateSince, s.Time);
        StopCause = m.State == PackMlState.Aborted ? (s.Alarms.FirstOrDefault(a => a.State == AlarmState.Active && a.Severity >= AlarmSeverity.StationFault)?.Text ?? m.StopCause) : null;
        DeclaredText = s.Forces.Declared != DeclaredState.None ? UiText.Declared(s.Forces.Declared) + (s.Forces.StartBlocked ? " · démarrage bloqué" : "") : null;
        Lights = m.Beacon.Lights;
        Blinking = m.Beacon.Blinking;
        _syncingMode = true;
        ModeIndex = m.Mode == MachineMode.Production ? 0 : 1;
        _syncingMode = false;
        OverridePercent = m.OverridePercent;
        CanReset = PackMlRules.IsAccepted(StateCommand.Reset, m.State);
        CanStart = PackMlRules.IsAccepted(StateCommand.Start, m.State);
        CanHold = PackMlRules.IsAccepted(StateCommand.Hold, m.State);
        CanStop = PackMlRules.IsAccepted(StateCommand.Stop, m.State);
        CanClear = PackMlRules.IsAccepted(StateCommand.Clear, m.State);

        foreach (var st in s.Stations)
            Stations.FirstOrDefault(t => t.Number == st.Number)?.Update(st);
        foreach (var mv in s.Movers)
            Movers.FirstOrDefault(v => v.Number == mv.Number)?.Update(mv);

        UpdateDrain(m);

        var o = s.Orders.Current;
        HasOrder = o != null;
        if (o == null) OrderTitle = "Aucun OF en cours";
        if (o != null)
        {
            OrderTitle = Resources.Res.Get("Order.Short") + " " + o.Number + " · " + o.Reference;
            OrderCountText = (o.Good + o.Bad) + " / " + o.Quantity;
            OrderGood = (double)o.Good / o.Quantity;
            OrderBad = (double)o.Bad / o.Quantity;
            OrderActive = (double)o.InProcess / o.Quantity;
            OrderPercent = DisplayFormat.Percent(100.0 * (o.Good + o.Bad) / o.Quantity);
            OrderGoodText = "✓ " + o.Good;
            OrderBadText = "✗ " + o.Bad;
            OrderActiveText = "◌ " + o.InProcess + " en cours";
            OrderEnd = o.EstimatedEnd.HasValue ? "Fin estimée " + DisplayFormat.ClockShort(DateTime.Today.AddHours(16).AddMinutes(40)) : string.Empty;
            BudgetText = "Budget de rebuts : " + o.Bad + " / " + o.BudgetBad;
        }
        RefreshBusy();
    }

    /// <summary>Bandeau, bouton et libellés de « Finir les pièces » ; toast quand la machine s'est vidée d'elle-même.</summary>
    private void UpdateDrain(MachineStatus m)
    {
        Draining = m.Draining;
        CanDrain = !m.Draining && (m.State == PackMlState.Execute || m.State == PackMlState.Held);
        PartsInProcess = m.PartsInProcess;
        if (m.Draining) _drainInitial = Math.Max(m.Draining && !_wasDraining ? 1 : _drainInitial, m.PartsInProcess);
        DrainProgress = m.Draining ? 1.0 - Math.Min(1.0, (double)m.PartsInProcess / Math.Max(1, _drainInitial)) : 0;
        DrainText = Resources.Res.Get("Drain.Banner") + " — " + UiText.Plural(m.PartsInProcess, "pièce restante", "pièces restantes");
        DrainBlockingMover = m.DrainBlockingMover;
        DrainBlockedText = m.Draining && m.DrainBlockingMover > 0 ? "Le mover " + m.DrainBlockingMover + " est à vider : retirez ses pièces pour terminer." : null;
        foreach (var st in Stations) st.LoadingStopped = m.Draining && _entryStations.Contains(st.Number);
        if (_wasDraining && !m.Draining && m.StopCause == StopCauses.Drained && (m.State == PackMlState.Stopping || m.State == PackMlState.Stopped))
            AppServices.Toast.Show(Resources.Res.Get("Drain.Done"), ToastKind.Success);
        _wasDraining = m.Draining;
    }

    private void RefreshBusy()
    {
        DrainBusy = AppServices.Commands.Busy == "drain";
        var b = AppServices.Commands.Busy;
        ResetBusy = b == "cmd." + StateCommand.Reset;
        StartBusy = b == "cmd." + StateCommand.Start;
        HoldBusy = b == "cmd." + StateCommand.Hold;
        StopBusy = b == "cmd." + StateCommand.Stop;
        ClearBusy = b == "cmd." + StateCommand.Clear;
    }

    partial void OnModeIndexChanged(int value)
    {
        if (_syncingMode) return;
        _ = SetModeAsync(value == 0 ? MachineMode.Production : MachineMode.Maintenance);
    }

    partial void OnOverridePercentChanged(double value)
    {
        if (Math.Abs(AppServices.Snapshots.Current.Machine.OverridePercent - value) < 0.01) return;
        _ = AppServices.Plc.SetOverrideAsync(new OverrideTarget(OverrideLevel.Machine), value, false, System.Threading.CancellationToken.None);
    }

    private async Task SetModeAsync(MachineMode mode)
    {
        var ok = await AppServices.Commands.RunAsync("mode", () => AppServices.Plc.SetModeAsync(mode, System.Threading.CancellationToken.None));
        if (!ok)
        {
            _syncingMode = true;
            ModeIndex = AppServices.Snapshots.Current.Machine.Mode == MachineMode.Production ? 0 : 1;
            _syncingMode = false;
        }
    }

    // ---- commandes
    private Task Run(StateCommand c) => AppServices.Commands.SendAsync(c);

    [RelayCommand] private Task Reset() => Run(StateCommand.Reset);
    [RelayCommand] private Task Start() => Run(StateCommand.Start);
    [RelayCommand] private Task Hold() => Run(StateCommand.Hold);
    [RelayCommand] private Task Stop() => Run(StateCommand.Stop);
    [RelayCommand] private Task Clear() => Run(StateCommand.Clear);

    /// <summary>« Finir les pièces » : plus de chargement, les pièces engagées finissent, puis arrêt automatique. Confirmé par un appui maintenu.</summary>
    [RelayCommand] private Task Drain() => AppServices.Commands.StartDrainAsync();

    /// <summary>« Annuler la fin » : le chargement reprend, sans confirmation.</summary>
    [RelayCommand] private Task CancelDrain() => AppServices.Commands.CancelDrainAsync();

    /// <summary>Bouton « Voir la pièce » du bandeau : ouvre le tiroir du mover à vider qui retient la fin.</summary>
    [RelayCommand]
    private void ShowBlockingMover()
    {
        var mover = Movers.FirstOrDefault(x => x.Number == DrainBlockingMover);
        if (mover != null) OpenMover(mover);
    }

    /// <summary>Abort : toujours permis, sans confirmation (docs/08 § 3).</summary>
    [RelayCommand] private Task Abort() => Run(StateCommand.Abort);

    /// <summary>Une commande indisponible, touchée, explique pourquoi et propose la suite (docs/08 § 6.1).</summary>
    [RelayCommand]
    private void ExplainUnavailable(object?[]? args)
    {
        if (args == null || args.Length < 2 || args[0] is not FrameworkElement anchor || args[1] is not string name) return;
        var cmd = (StateCommand)Enum.Parse(typeof(StateCommand), name);
        var text = UiText.WhyUnavailable(cmd, State, out var suggestion);
        string? actionLabel = suggestion.HasValue ? UiText.CommandLabel(suggestion.Value) : null;
        Action? action = suggestion.HasValue ? new Action(() => _ = Run(suggestion.Value)) : null;
        AppServices.Dialogs.ShowBubble(anchor, text, actionLabel, action);
    }

    [RelayCommand]
    private async Task Reintegrate(StationTileViewModel st)
    {
        StateCommand c = st.State == PackMlState.Aborted ? StateCommand.Clear : st.State == PackMlState.Stopped ? StateCommand.Reset : StateCommand.Start;
        await AppServices.Commands.SendStationAsync(st.Number, c);
    }

    [RelayCommand]
    private void OpenStation(StationTileViewModel st)
    {
        AppServices.Selection.Station = st.Number;
        AppServices.Navigation.Navigate("Station:" + st.Number, null, pushHistory: true);
    }

    [RelayCommand]
    private void OpenMover(MoverViewModel mover)
    {
        var snap = AppServices.Snapshots.Current.Movers.FirstOrDefault(m => m.Number == mover.Number);
        if (snap == null) return;
        var panel = MoverDrawerContent.Build(snap);
        AppServices.Drawer.Show("Mover " + snap.Number, panel);
    }
}
