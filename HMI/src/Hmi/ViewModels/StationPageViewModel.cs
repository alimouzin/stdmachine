using System.Collections.ObjectModel;
using System.Windows.Input;
using Core.Access;
using Core.Model;
using Core.Plc;
using Core.StateMachine;
using Hmi.Display;
using Hmi.Mvvm;
using Hmi.Services;

namespace Hmi.ViewModels;

public sealed class ManualActionViewModel
{
    public ManualActionViewModel(string label, ICommand command)
    {
        Label = label;
        Command = command;
    }

    public string Label { get; }
    public ICommand Command { get; }
}

public sealed class ManualTargetViewModel
{
    public ManualTargetViewModel(ManualTarget target, IEnumerable<ManualActionViewModel> actions)
    {
        Name = target.Name;
        Kind = target.Kind;
        IsModule = target.IsModule;
        Actions = actions.ToList();
    }

    public string Name { get; }
    public string Kind { get; }
    public bool IsModule { get; }
    public IReadOnlyList<ManualActionViewModel> Actions { get; }
}

/// <summary>
/// Page d'une station, générée à partir de sa déclaration (devices et modules) :
/// aucun code spécifique par station.
/// </summary>
public sealed class StationPageViewModel : PageViewModel
{
    private readonly PlcMonitor _monitor;
    private readonly SessionService _session;
    private StationDef? _selected;
    private string _activity = "";

    public StationPageViewModel(PlcMonitor monitor, SessionService session) : base("Stations")
    {
        _monitor = monitor;
        _session = session;
        Stations = monitor.Plc.Machine.Stations;
        _monitor.Updated += Refresh;
        _session.Changed += Refresh;
        SelectedStation = Stations.FirstOrDefault();
    }

    public IReadOnlyList<StationDef> Stations { get; }

    public StationDef? SelectedStation
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            BuildTargets();
            _activity = "";
            Refresh();
        }
    }

    public ObservableCollection<ManualTargetViewModel> Devices { get; } = new();
    public ObservableCollection<ManualTargetViewModel> Modules { get; } = new();

    public SeqState StationState => _selected is null
        ? SeqState.Stopped
        : _monitor.Status.Stations.First(s => s.Id == _selected.Id).State;

    public string StationStateText => StateDisplay.Label(StationState);

    public bool ManualEnabled =>
        _selected is not null
        && _session.CanDo(ActionType.ManualAction)
        && _monitor.Status.Mode == SeqMode.Maintenance
        && _monitor.Status.State == SeqState.Stopped
        && StationState == SeqState.Stopped;

    /// <summary>Explique pourquoi le mode manuel n'est pas disponible.</summary>
    public string ManualHint
    {
        get
        {
            if (ManualEnabled) return "Mode manuel actif : les actions s'exécutent directement sur la station.";
            if (!_session.CanDo(ActionType.ManualAction)) return "Connexion avec un niveau suffisant requise pour le mode manuel.";
            if (_monitor.Status.Mode != SeqMode.Maintenance) return "Passez la machine en mode Maintenance (page Machine, état Arrêté).";
            return "Le mode manuel n'est accessible que lorsque la machine et la station sont à l'état Arrêté.";
        }
    }

    public string Activity
    {
        get => _activity;
        private set => Set(ref _activity, value);
    }

    private void BuildTargets()
    {
        Devices.Clear();
        Modules.Clear();
        if (_selected is null) return;

        var page = ManualPageModel.Build(_selected);
        foreach (var d in page.Devices) Devices.Add(BuildTarget(page.StationId, d));
        foreach (var m in page.Modules) Modules.Add(BuildTarget(page.StationId, m));
    }

    private ManualTargetViewModel BuildTarget(string stationId, ManualTarget target)
    {
        var actions = target.Actions.Select(a => new ManualActionViewModel(
            a.Label,
            new AsyncRelayCommand(() => RunAsync(stationId, target, a), () => ManualEnabled)));
        return new ManualTargetViewModel(target, actions);
    }

    private async Task RunAsync(string stationId, ManualTarget target, ActionDef action)
    {
        var name = $"{target.Name} · {action.Label}";
        Activity = $"{name} : en cours…";
        var result = await _monitor.Plc.ExecuteManualActionAsync(stationId, target.Id, action.Id);
        Activity = result switch
        {
            ManualActionResult.Done => $"{name} : terminé",
            ManualActionResult.NotAllowed => $"{name} : refusé (mode ou état incompatible)",
            _ => $"{name} : action inconnue",
        };
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(StationState));
        OnPropertyChanged(nameof(StationStateText));
        OnPropertyChanged(nameof(ManualEnabled));
        OnPropertyChanged(nameof(ManualHint));
    }
}
