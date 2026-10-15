using Core.Model;
using Core.StateMachine;

namespace Core.Plc;

public sealed class PlcSimulatorOptions
{
    /// <summary>Durée des états transitoires (Resetting, Stopping, Clearing) d'une station.</summary>
    public TimeSpan TransitionDelay { get; init; } = TimeSpan.FromMilliseconds(1500);

    /// <summary>Durée d'une action de base ; une action de module en enchaîne plusieurs.</summary>
    public TimeSpan ActionDelay { get; init; } = TimeSpan.FromMilliseconds(800);
}

/// <summary>
/// PLC simulé : fait vivre les états des stations, accepte les commandes et exécute
/// les actions manuelles avec un délai simulé.
/// </summary>
public sealed class PlcSimulator : IPlcClient, ISimulationControl
{
    private readonly object _lock = new();
    private readonly GeneralSequencer _sequencer;
    private readonly PlcSimulatorOptions _options;
    private readonly HashSet<(string StationId, SeqState State)> _pending = new();

    public PlcSimulator(MachineDef machine, PlcSimulatorOptions? options = null)
    {
        Machine = machine;
        _options = options ?? new PlcSimulatorOptions();
        _sequencer = new GeneralSequencer(machine);
        _sequencer.Changed += () => StatusChanged?.Invoke();
    }

    public MachineDef Machine { get; }

    public event Action? StatusChanged;

    public MachineStatus GetStatus()
    {
        lock (_lock)
        {
            var stations = Machine.Stations
                .Select(s => new StationStatus(
                    s.Id, s.Name, s.Type, _sequencer.StationState(s.Id), _sequencer.IsStationAvailable(s.Id)))
                .ToList();
            return new MachineStatus(_sequencer.State, _sequencer.Mode, _sequencer.IsDegraded, stations);
        }
    }

    public bool SendCommand(SeqCommand command)
    {
        lock (_lock)
        {
            var ok = _sequencer.Apply(command);
            SchedulePending();
            return ok;
        }
    }

    public bool SendStationCommand(string stationId, SeqCommand command)
    {
        lock (_lock)
        {
            if (!Machine.Stations.Any(s => s.Id == stationId)) return false;
            var ok = _sequencer.ApplyToStation(stationId, command);
            SchedulePending();
            return ok;
        }
    }

    public bool SetMode(SeqMode mode)
    {
        lock (_lock) return _sequencer.TrySetMode(mode);
    }

    public bool ForceAbort(string stationId) => SendStationCommand(stationId, SeqCommand.Abort);

    public async Task<ManualActionResult> ExecuteManualActionAsync(
        string stationId, string targetId, string actionId, CancellationToken ct = default)
    {
        int steps;
        lock (_lock)
        {
            var station = Machine.Stations.FirstOrDefault(s => s.Id == stationId);
            if (station is null) return ManualActionResult.Unknown;
            var found = FindSteps(station, targetId, actionId);
            if (found is null) return ManualActionResult.Unknown;
            steps = found.Value;

            if (_sequencer.Mode != SeqMode.Maintenance
                || _sequencer.State != SeqState.Stopped
                || _sequencer.StationState(stationId) != SeqState.Stopped)
                return ManualActionResult.NotAllowed;
        }

        await Task.Delay(_options.ActionDelay * steps, ct).ConfigureAwait(false);
        return ManualActionResult.Done;
    }

    /// <summary>Nombre d'actions de base de l'action demandée, ou null si inconnue.</summary>
    private static int? FindSteps(StationDef station, string targetId, string actionId)
    {
        var device = station.Devices.FirstOrDefault(d => d.Id == targetId);
        if (device is not null)
            return ActionCatalog.ForDevice(device.Kind).Any(a => a.Id == actionId) ? 1 : null;

        var module = station.Modules.FirstOrDefault(m => m.Id == targetId);
        if (module is not null && ActionCatalog.ForModule(module.Type).Any(a => a.Id == actionId))
            return ActionCatalog.ModuleActionSteps(module.Type, actionId).Count;

        return null;
    }

    /// <summary>Planifie la fin des états transitoires des stations (appelé sous verrou).</summary>
    private void SchedulePending()
    {
        foreach (var id in _sequencer.StationIds)
        {
            var state = _sequencer.StationState(id);
            if (state is not (SeqState.Resetting or SeqState.Stopping or SeqState.Clearing)) continue;
            if (!_pending.Add((id, state))) continue;
            _ = CompleteLaterAsync(id, state);
        }
    }

    private async Task CompleteLaterAsync(string stationId, SeqState state)
    {
        await Task.Delay(_options.TransitionDelay).ConfigureAwait(false);
        lock (_lock)
        {
            _pending.Remove((stationId, state));
            // Si la station a quitté l'état entre-temps (Abort), il n'y a rien à terminer.
            if (_sequencer.StationState(stationId) == state) _sequencer.CompleteStation(stationId);
            SchedulePending();
        }
    }
}
