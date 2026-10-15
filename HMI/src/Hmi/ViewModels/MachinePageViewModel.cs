using System.Collections.ObjectModel;
using System.Windows.Input;
using Core.Access;
using Core.Plc;
using Core.StateMachine;
using Hmi.Display;
using Hmi.Mvvm;
using Hmi.Services;

namespace Hmi.ViewModels;

public sealed class CommandButtonViewModel
{
    public CommandButtonViewModel(string label, ICommand command, bool isDanger = false)
    {
        Label = label;
        Command = command;
        IsDanger = isDanger;
    }

    public string Label { get; }
    public ICommand Command { get; }
    public bool IsDanger { get; }
}

/// <summary>Balise d'une station et ses commandes locales.</summary>
public sealed class StationTileViewModel : ViewModelBase
{
    private SeqState _state;
    private bool _available = true;

    public StationTileViewModel(string id, string name, string groupText, IPlcClient plc, SessionService session, PlcMonitor monitor)
    {
        Id = id;
        Name = name;
        GroupText = groupText;

        ClearCommand = new RelayCommand(
            () => plc.SendStationCommand(id, SeqCommand.Clear),
            () => session.CanDo(ActionType.Command) && plc.CanSendStationCommand(id, SeqCommand.Clear));

        // Reset local : réintégration d'une station pendant que le reste de la machine tourne.
        ResetCommand = new RelayCommand(
            () => plc.SendStationCommand(id, SeqCommand.Reset),
            () => session.CanDo(ActionType.Command)
                  && monitor.Status.State != SeqState.Stopped
                  && plc.CanSendStationCommand(id, SeqCommand.Reset));

        ForceAbortCommand = new RelayCommand(
            () => (plc as ISimulationControl)?.ForceAbort(id),
            () => plc is ISimulationControl
                  && session.CanDo(ActionType.Abort)
                  && plc.CanSendStationCommand(id, SeqCommand.Abort));
    }

    public string Id { get; }
    public string Name { get; }
    public string GroupText { get; }

    public SeqState State
    {
        get => _state;
        private set { if (Set(ref _state, value)) OnPropertyChanged(nameof(StateText)); }
    }

    public bool Available
    {
        get => _available;
        private set => Set(ref _available, value);
    }

    public string StateText => StateDisplay.Label(_state) + (_available ? "" : " · indisponible");

    public ICommand ClearCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand ForceAbortCommand { get; }

    public void Update(StationStatus status)
    {
        State = status.State;
        Available = status.Available;
        OnPropertyChanged(nameof(StateText));
    }
}

public sealed class MachinePageViewModel : PageViewModel
{
    private readonly PlcMonitor _monitor;
    private readonly SessionService _session;

    public MachinePageViewModel(PlcMonitor monitor, SessionService session) : base("Machine")
    {
        _monitor = monitor;
        _session = session;
        var plc = monitor.Plc;

        CommandButtons = new[]
        {
            Button("Reset", SeqCommand.Reset),
            Button("Start", SeqCommand.Start),
            Button("Stop", SeqCommand.Stop),
            Button("Hold", SeqCommand.Hold),
            Button("Abort", SeqCommand.Abort, danger: true),
            Button("Clear", SeqCommand.Clear),
        };

        ToggleModeCommand = new RelayCommand(
            () => plc.SetMode(_monitor.Status.Mode == SeqMode.Production ? SeqMode.Maintenance : SeqMode.Production),
            () => _session.CanDo(ActionType.ModeChange)
                  && _monitor.Status.State == SeqState.Stopped
                  && _monitor.Status.Stations.All(s => s.State == SeqState.Stopped));

        var groups = plc.Machine.Groups;
        foreach (var station in plc.Machine.Stations)
        {
            var group = groups.FirstOrDefault(g => g.StationIds.Contains(station.Id));
            var text = group is null ? "Sans groupe" : $"Groupe : {group.Function}";
            Stations.Add(new StationTileViewModel(station.Id, station.Name, text, plc, session, monitor));
        }

        _monitor.Updated += Refresh;
        Refresh();
    }

    public IReadOnlyList<CommandButtonViewModel> CommandButtons { get; }
    public ObservableCollection<StationTileViewModel> Stations { get; } = new();
    public ICommand ToggleModeCommand { get; }

    public SeqState State => _monitor.Status.State;
    public string StateText => StateDisplay.Label(_monitor.Status.State);
    public string ModeText => StateDisplay.Label(_monitor.Status.Mode);
    public bool IsDegraded => _monitor.Status.IsDegraded;

    public string ToggleModeText => _monitor.Status.Mode == SeqMode.Production
        ? "Passer en Maintenance"
        : "Passer en Production";

    public string DegradedText
    {
        get
        {
            var down = _monitor.Status.Stations.Where(s => !s.Available).Select(s => s.Name).ToList();
            return down.Count == 0 ? "" : "Production dégradée · " + string.Join(", ", down);
        }
    }

    private CommandButtonViewModel Button(string label, SeqCommand command, bool danger = false)
    {
        var plc = _monitor.Plc;
        var right = command == SeqCommand.Abort ? ActionType.Abort : ActionType.Command;
        return new CommandButtonViewModel(
            label,
            new RelayCommand(() => plc.SendCommand(command), () => _session.CanDo(right) && plc.CanSendCommand(command)),
            danger);
    }

    private void Refresh()
    {
        foreach (var status in _monitor.Status.Stations)
            Stations.FirstOrDefault(t => t.Id == status.Id)?.Update(status);

        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ModeText));
        OnPropertyChanged(nameof(IsDegraded));
        OnPropertyChanged(nameof(ToggleModeText));
        OnPropertyChanged(nameof(DegradedText));
    }
}
