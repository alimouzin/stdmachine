using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StdMachine.Core;
using StdMachine.Hmi.Editor;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Services;
using StdMachine.Hmi.Theme;
using StdMachine.Simulation.Demo;

namespace StdMachine.Hmi.ViewModels;

/// <summary>Puce de station (48 px, avec point d'état) : passer d'une station à l'autre dans les pages Station, Manuel, Studio et Éditeur.</summary>
public sealed partial class StationChipViewModel : ObservableObject
{
    private readonly Action<int> _select;

    public StationChipViewModel(int number, string name, Action<int> select) { Number = number; Name = name; _select = select; }

    [RelayCommand] private void Select() => _select(Number);

    public int Number { get; }
    public string Name { get; }
    [ObservableProperty] private Tone _tone;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _blink;
}

public sealed partial class ObjectCardViewModel : ObservableObject
{
    public ObjectCardViewModel(ObjectNode node, string typeName, string icon, int depth)
    {
        Node = node; Name = node.Name; TypeName = typeName; Icon = icon; Depth = depth; IsSimulated = node.Simulated;
    }

    public ObjectNode Node { get; }
    public string Name { get; }
    public string TypeName { get; }
    public string Icon { get; }
    public int Depth { get; }
    public bool IsSimulated { get; }

    [ObservableProperty] private ObjectState _state;
    [ObservableProperty] private Tone _tone;
    [ObservableProperty] private string _stateText = "Prêt";
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string? _action;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string? _error;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _gauge;
    [ObservableProperty] private bool _ledOn;
    [ObservableProperty] private bool _showLed;
    [ObservableProperty] private bool _showGauge;

    public void Update(ObjectStatus s)
    {
        State = s.State;
        IsBusy = s.State == ObjectState.Busy;
        HasError = s.State == ObjectState.Error;
        Error = s.ErrorText;
        Action = s.ActionText;
        Progress = s.ActionProgress;
        switch (s.State)
        {
            case ObjectState.Busy: Tone = Tone.Busy; StateText = "En cours"; break;
            case ObjectState.Error: Tone = Tone.Err; StateText = "En erreur"; break;
            case ObjectState.Disabled: Tone = Tone.Neutral; StateText = "Désactivé"; break;
            case ObjectState.Uninitialized: Tone = Tone.Warn; StateText = "Non initialisé"; break;
            default: Tone = Tone.Ok; StateText = "Prêt"; break;
        }
        var v = s.Values;
        ShowLed = false; ShowGauge = false;
        switch (Node.TypeCode)
        {
            case "LinearAxis":
                Summary = DisplayFormat.WithUnit(v[0], "mm", 2) + (v[3] > 0 ? "" : " · non référencé");
                break;
            case "RotaryAxis": Summary = DisplayFormat.WithUnit(v[0], "°", 2); break;
            case "VacuumBlow":
                Summary = DisplayFormat.WithUnit(v[2], "kPa", 1) + (v[3] > 0 ? " · pièce présente" : "");
                ShowGauge = true; Gauge = Math.Min(1, Math.Abs(v[2]) / 80.0);
                break;
            case "Press": Summary = DisplayFormat.WithUnit(v[0], "N", 0) + " · " + DisplayFormat.WithUnit(v[1], "mm", 1); break;
            case "DigitalInput": ShowLed = true; LedOn = v[0] > 0; Summary = LedOn ? "Actif" : "Inactif"; break;
            case "Counter": Summary = DisplayFormat.Number(v[0], 0) + " rebuts"; break;
            case "AsyrilEyePlus": Summary = DisplayFormat.Number(v[0], 0) + " pièces trouvées · recette " + DisplayFormat.Number(v[1], 0); break;
            case "CartesianRobot": Summary = s.ActionText ?? "Au repos"; break;
            default: Summary = string.Empty; break;
        }
    }
}

public sealed record HistoryRow(string Time, string Part, string Operation, string Duration, Verdict Verdict);

/// <summary>Page Station d'exploitation (docs/08 § 6.2) : exécution en direct du process, objets, variables, historique.</summary>
public sealed partial class StationViewModel : ViewModelBase, INavigationAware
{
    private readonly MachineProject _project = AppServices.Project.Project;
    private readonly Dictionary<Guid, ProcessCanvasViewModel> _canvases = new Dictionary<Guid, ProcessCanvasViewModel>();
    private DateTime _followSuspendedUntil = DateTime.MinValue;
    private Guid? _lastRunning;

    public StationViewModel(int number)
    {
        Number = number;
        AppServices.Selection.Station = number;
        Config = _project.Station(number)!;
        foreach (var st in _project.Stations) Chips.Add(new StationChipViewModel(st.Number, st.Name, n => AppServices.Navigation.Navigate("Station:" + n)));
        foreach (var root in Config.Objects) AddObject(root, 0);
        Groups = _project.GroupsOf(Config).Select(g => g.Name).ToList();
        Canvas = CanvasFor(DefaultProcess());
        OnSnapshots(OnSnapshot);
        OnSnapshot(this, AppServices.Snapshots.Current);
    }

    public int Number { get; }
    public StationConfig Config { get; }
    public List<string> Groups { get; }
    public ObservableCollection<StationChipViewModel> Chips { get; } = new ObservableCollection<StationChipViewModel>();
    public ObservableCollection<ObjectCardViewModel> Objects { get; } = new ObservableCollection<ObjectCardViewModel>();
    public ObservableCollection<HistoryRow> History { get; } = new ObservableCollection<HistoryRow>();
    public ObservableCollection<ContextStatus> Contexts { get; } = new ObservableCollection<ContextStatus>();
    public IReadOnlyList<StationVariable> Variables => Config.Variables;
    public IReadOnlyList<PartDataDef> PartData => _project.Machine.PartData;

    [ObservableProperty] private ProcessCanvasViewModel _canvas = null!;
    [ObservableProperty] private int _tabIndex;
    [ObservableProperty] private PackMlState _state;
    [ObservableProperty] private string _stateSince = string.Empty;
    [ObservableProperty] private BeaconLights _lights;
    [ObservableProperty] private BeaconLights _blinking;
    [ObservableProperty] private double _overridePercent = 100;
    [ObservableProperty] private bool _isAborted;
    [ObservableProperty] private string _abortTitle = string.Empty;
    [ObservableProperty] private int _reintegrationStep;
    [ObservableProperty] private bool _following = true;
    [ObservableProperty] private bool _followSuspended;
    [ObservableProperty] private string _operationTitle = "Aucune opération en cours";
    [ObservableProperty] private string _operationTime = string.Empty;
    [ObservableProperty] private string _moverText = string.Empty;
    [ObservableProperty] private string _currentTitle = string.Empty;
    [ObservableProperty] private string _currentDetail = string.Empty;
    [ObservableProperty] private string _nextTitle = string.Empty;
    [ObservableProperty] private double _operationProgress;
    [ObservableProperty] private bool _hasOperation;
    [ObservableProperty] private bool _qualityHold;
    [ObservableProperty] private string _qualityText = string.Empty;
    [ObservableProperty] private bool _outOfProduction;
    [ObservableProperty] private bool _maintenanceMode;
    [ObservableProperty] private int _granularityIndex;
    [ObservableProperty] private bool _stepModeOn;
    [ObservableProperty] private string _processName = string.Empty;
    [ObservableProperty] private string _consecutiveText = string.Empty;
    [ObservableProperty] private string _rateText = string.Empty;
    [ObservableProperty] private string _partText = string.Empty;
    [ObservableProperty] private string _aborReason = string.Empty;

    public string Name => Config.Name;
    public string ReintegratedText => Config.Name + " est réintégrée : Réarmer, puis Démarrer la station seule.";
    public bool IsDebug => false;

    public event EventHandler<StepCardModel>? FocusRequested;

    /// <summary>Demande à la vue de recentrer sur la carte en cours (à l'ouverture de la page).</summary>
    public void RequestFocus()
    {
        if (Canvas.FocusCard() is StepCardModel focus) FocusRequested?.Invoke(this, focus);
    }

    public void OnNavigatedTo(object? args)
    {
        if (args is HighlightRequest h && h.AlarmId.HasValue) { /* l'alarme est affichée dans la carte rouge */ }
    }

    private ProcessDefinition DefaultProcess()
        => Config.Processes.FirstOrDefault(p => p.Kind == ProcessKind.Operation) ?? Config.Processes.First();

    private ProcessCanvasViewModel CanvasFor(ProcessDefinition process)
    {
        if (!_canvases.TryGetValue(process.Id, out var vm))
        {
            vm = new ProcessCanvasViewModel(_project, Config, process, ProcessLevel.Station);
            _canvases[process.Id] = vm;
        }
        return vm;
    }

    private void AddObject(ObjectNode node, int depth)
    {
        var def = DemoCatalog.Get(node.TypeCode);
        Objects.Add(new ObjectCardViewModel(node, def?.Name ?? node.TypeCode, def?.Icon ?? "help", depth));
        foreach (var c in node.Children) AddObject(c, depth + 1);
    }

    // ---------------------------------------------------------------- instantané

    private void OnSnapshot(object? sender, PlcSnapshot s)
    {
        var st = s.Station(Number);
        if (st == null) return;
        foreach (var chip in Chips)
        {
            var cs = s.Station(chip.Number);
            chip.Tone = cs == null ? Tone.Neutral : UiText.StateTone(cs.State);
            chip.IsSelected = chip.Number == Number;
            chip.Blink = cs != null && cs.State == PackMlState.Aborted;
        }
        State = st.State;
        StateSince = UiText.Since(st.StateSince);
        Lights = st.Beacon.Lights;
        Blinking = st.Beacon.Blinking;
        OverridePercent = st.OverridePercent;
        IsAborted = st.State == PackMlState.Aborted;
        MaintenanceMode = s.Machine.Mode == MachineMode.Maintenance;
        QualityHold = st.QualityHold;
        OutOfProduction = st.OutOfProduction;
        QualityText = st.QualityHold ? "Arrêt qualité : " + (s.Quality.Tracks.FirstOrDefault(t => t.Scope == QualityScope.Station && t.Name == Name)?.Reason ?? "") : string.Empty;
        ConsecutiveText = st.ConsecutiveRejects + " successif" + (st.ConsecutiveRejects > 1 ? "s" : "");
        RateText = DisplayFormat.Percent(st.RejectRatePercent, 1) + " de rebut";
        AbortTitle = Name + " est en Aborted depuis " + DisplayFormat.ClockShort(st.StateSince) + " — " + (st.FaultText ?? "défaut");
        ReintegrationStep = st.State == PackMlState.Aborted ? 1 : st.State == PackMlState.Stopped && s.Machine.State == PackMlState.Execute ? 2 : st.State == PackMlState.Idle && s.Machine.State == PackMlState.Execute ? 3 : 0;

        // Process à afficher : celui qui s'exécute, sinon le dernier
        if (st.RunningProcessId.HasValue && Following && st.RunningProcessId != _lastRunning)
        {
            var proc = Config.Processes.FirstOrDefault(p => p.Id == st.RunningProcessId.Value);
            if (proc != null && proc.Id != Canvas.Process.Id) Canvas = CanvasFor(proc);
        }
        _lastRunning = st.RunningProcessId;
        ProcessName = Canvas.Process.Name;
        FollowSuspended = DateTime.Now < _followSuspendedUntil;
        Canvas.IsFollowing = true;
        Canvas.ApplyRuntime(st);

        HasOperation = st.Operation != null;
        OperationTitle = st.Operation ?? "Aucune opération en cours";
        OperationTime = st.Operation == null ? string.Empty : DisplayFormat.Duration(st.OperationElapsedSeconds) + " / ≈ " + DisplayFormat.Duration(st.OperationEstimatedSeconds);
        OperationProgress = st.OperationEstimatedSeconds > 0 ? Math.Min(1, st.OperationElapsedSeconds / st.OperationEstimatedSeconds) : 0;
        MoverText = st.MoverNumber.HasValue ? "Mover " + st.MoverNumber + " · " + st.PartId : string.Empty;
        Contexts.Clear();
        foreach (var c in st.Contexts) Contexts.Add(c);
        var current = Canvas.Cards.FirstOrDefault(c => c.State == StepRunState.Error)
            ?? Canvas.Cards.FirstOrDefault(c => c.State == StepRunState.Running || c.State == StepRunState.WaitingCondition || c.State == StepRunState.Retry);
        CurrentTitle = current?.Title ?? string.Empty;
        CurrentDetail = current == null ? string.Empty : (current.Detail ?? string.Join(" · ", current.Params.Take(2).Select(p => p.Key + " " + p.Value)));
        NextTitle = Canvas.Cards.FirstOrDefault(c => c.State == StepRunState.Next)?.Title ?? string.Empty;

        foreach (var o in s.ObjectsOf(Number))
            Objects.FirstOrDefault(x => x.Node.Id == o.ObjectId)?.Update(o);

        if (Following && !FollowSuspended && Canvas.FocusCard() is StepCardModel focus) FocusRequested?.Invoke(this, focus);
        if (History.Count == 0 || sender == this) BuildHistory(s);
    }

    private void BuildHistory(PlcSnapshot s)
    {
        History.Clear();
        var demo = AppServices.Demo;
        if (demo == null) return;
        var baseTime = s.Time.AddSeconds(-s.ScenarioSeconds);
        foreach (var v in demo.Script.Flow.Visits.Where(v => v.Station == Number && v.End <= s.ScenarioSeconds).OrderByDescending(v => v.End).Take(50))
            History.Add(new HistoryRow(DisplayFormat.Clock(baseTime.AddSeconds(v.End)), v.PartId, v.Group + " › " + v.Operation,
                DisplayFormat.Duration(v.Duration), v.Aborted ? Verdict.Bad : (v.Good ? Verdict.Good : Verdict.Bad)));
    }

    // ---------------------------------------------------------------- commandes

    [RelayCommand]
    private void SelectStation(StationChipViewModel chip)
    {
        if (chip.Number == Number) return;
        AppServices.Navigation.Navigate("Station:" + chip.Number);
    }

    [RelayCommand]
    private Task Clear() => AppServices.Commands.SendStationAsync(Number, StateCommand.Clear);

    [RelayCommand]
    private Task Reset() => AppServices.Commands.SendStationAsync(Number, StateCommand.Reset);

    [RelayCommand]
    private Task Start() => AppServices.Commands.SendStationAsync(Number, StateCommand.Start);

    [RelayCommand]
    private void SuspendFollow()
    {
        _followSuspendedUntil = DateTime.Now.AddSeconds(10);
        FollowSuspended = true;
    }

    [RelayCommand]
    private void ResumeFollow()
    {
        _followSuspendedUntil = DateTime.MinValue;
        FollowSuspended = false;
        Following = true;
    }

    [RelayCommand]
    private void OpenInEditor() => AppServices.Navigation.Navigate("ProcessEditor:" + Number + ":" + ProcessKey(Canvas.Process), null, pushHistory: true);

    [RelayCommand]
    private void OpenManual() => AppServices.Navigation.Navigate("Manual:" + Number, null, pushHistory: true);

    [RelayCommand]
    private void OpenAlarm() => AppServices.Navigation.Navigate("Alarms", null, pushHistory: true);

    [RelayCommand]
    private async Task NextStep() => await AppServices.Commands.RunAsync("step", () => AppServices.Plc.StepAsync(Number, System.Threading.CancellationToken.None));

    [RelayCommand]
    private void OpenObject(ObjectCardViewModel o)
    {
        var panel = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(24, 0, 24, 24) };
        panel.Children.Add(new Controls.StatusPill { Tone = o.Tone, Text = o.StateText, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new System.Windows.Thickness(0, 0, 0, 12) });
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = o.TypeName + " · " + Name, Foreground = Tokens.Brush("Text.Secondary") });
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = o.Summary, FontSize = 22, FontWeight = System.Windows.FontWeights.Medium, Margin = new System.Windows.Thickness(0, 12, 0, 0) });
        if (o.Action != null) panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Dernière action : " + o.Action, Margin = new System.Windows.Thickness(0, 8, 0, 0) });
        if (o.HasError)
        {
            panel.Children.Add(new System.Windows.Controls.TextBlock { Text = o.Error, Foreground = Tokens.Brush("Err.Text"), FontWeight = System.Windows.FontWeights.SemiBold, Margin = new System.Windows.Thickness(0, 14, 0, 0), TextWrapping = System.Windows.TextWrapping.Wrap });
            var ack = new System.Windows.Controls.Button { Content = "Acquitter", Margin = new System.Windows.Thickness(0, 10, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            Controls.Ui.SetVariant(ack, Controls.ButtonVariant.Danger);
            ack.Click += (_, _) => _ = Clear();
            panel.Children.Add(ack);
        }
        var manual = new System.Windows.Controls.Button { Content = "Manuel", Margin = new System.Windows.Thickness(0, 18, 8, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        Controls.Ui.SetIcon(manual, "pan_tool");
        manual.Click += (_, _) => AppServices.Navigation.Navigate("Manual:" + Number, null, pushHistory: true);
        panel.Children.Add(manual);
        var cfg = new System.Windows.Controls.Button { Content = "Configuration", Margin = new System.Windows.Thickness(0, 8, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
        Controls.Ui.SetIcon(cfg, "build");
        cfg.Click += (_, _) => AppServices.Navigation.Navigate("StationConfig:" + Number, null, pushHistory: true);
        panel.Children.Add(cfg);
        AppServices.Drawer.Show(o.Name, panel);
    }

    public static string ProcessKey(ProcessDefinition p)
    {
        switch (p.Name)
        {
            case "Presser pignon": return "presser";
            case "Sertir": return "sertir";
            case "Cycle de fond": return "fond";
            case "Initialisation": return "init";
            case "Vidage": return "clear";
            case "Charger": return "charger";
            case "Contrôler": return "controler";
            case "Décharger": return "decharger";
            case "Évacuer": return "evacuer";
            default: return p.Id.ToString("N");
        }
    }
}
