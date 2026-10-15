using Core.Model;

namespace Core.StateMachine;

/// <summary>
/// Séquenceur général : relaie les commandes aux stations et agrège leur état.
/// L'Aborted d'une station est local : la machine ne passe en Aborted que si aucune
/// autre station du groupe ne peut assurer la même fonction (ou si la station n'a pas de groupe).
/// </summary>
public sealed class GeneralSequencer
{
    private readonly Sequencer _general = new();
    private readonly Dictionary<string, Sequencer> _stations;
    private readonly MachineDef _machine;

    public GeneralSequencer(MachineDef machine)
    {
        _machine = machine;
        _stations = machine.Stations.ToDictionary(s => s.Id, _ => new Sequencer());
    }

    public event Action? Changed;

    public SeqState State => _general.State;
    public SeqMode Mode => _general.Mode;
    public IEnumerable<string> StationIds => _stations.Keys;

    public SeqState StationState(string stationId) => _stations[stationId].State;

    /// <summary>Une station en défaut (Aborted ou Clearing) n'est plus disponible pour le routage.</summary>
    public bool IsStationAvailable(string stationId) =>
        _stations[stationId].State is not (SeqState.Aborted or SeqState.Clearing);

    /// <summary>Production qui continue avec au moins une station indisponible.</summary>
    public bool IsDegraded =>
        State != SeqState.Aborted && _stations.Keys.Any(id => !IsStationAvailable(id));

    public bool CanApply(SeqCommand command) => command switch
    {
        // Un Clear général sert aussi à acquitter des stations Aborted localement.
        SeqCommand.Clear => _general.CanApply(command) || AnyStationIn(SeqState.Aborted),
        _ => _general.CanApply(command),
    };

    public bool Apply(SeqCommand command)
    {
        if (!CanApply(command)) return false;
        foreach (var station in _stations.Values) station.Apply(command);
        _general.Apply(command);
        Refresh();
        return true;
    }

    public bool CanApplyToStation(string stationId, SeqCommand command) => _stations[stationId].CanApply(command);

    /// <summary>Commande locale à une station (Abort forcé, Clear, Reset de réintégration).</summary>
    public bool ApplyToStation(string stationId, SeqCommand command)
    {
        if (!_stations[stationId].Apply(command)) return false;
        Refresh();
        return true;
    }

    /// <summary>Appelé par le PLC (ou le simulateur) quand un état transitoire de station se termine.</summary>
    public bool CompleteStation(string stationId)
    {
        if (!_stations[stationId].Complete()) return false;
        Refresh();
        return true;
    }

    /// <summary>Mode global : possible seulement si la machine et toutes les stations sont Stopped.</summary>
    public bool TrySetMode(SeqMode mode)
    {
        if (State != SeqState.Stopped || _stations.Values.Any(s => s.State != SeqState.Stopped)) return false;
        foreach (var station in _stations.Values) station.TrySetMode(mode);
        _general.TrySetMode(mode);
        Changed?.Invoke();
        return true;
    }

    private bool AnyStationIn(SeqState state) => _stations.Values.Any(s => s.State == state);

    private bool HasAvailableEquivalent(string stationId)
    {
        var group = _machine.Groups.FirstOrDefault(g => g.StationIds.Contains(stationId));
        return group is not null && group.StationIds.Any(id => id != stationId && IsStationAvailable(id));
    }

    /// <summary>Recalcule l'état général à partir des stations.</summary>
    private void Refresh()
    {
        // Aborted machine : station sans équivalent disponible.
        if (_general.State is SeqState.Idle or SeqState.Execute or SeqState.Held or SeqState.Resetting or SeqState.Stopping)
        {
            var fatal = _stations.Keys.Any(id => !IsStationAvailable(id) && !HasAvailableEquivalent(id));
            if (fatal)
            {
                foreach (var station in _stations.Values) station.Apply(SeqCommand.Abort);
                _general.Apply(SeqCommand.Abort);
            }
        }

        // Réintégration : une station revenue à Idle rejoint la production en cours.
        if (_general.State == SeqState.Execute)
            foreach (var station in _stations.Values.Where(s => s.State == SeqState.Idle)) station.Apply(SeqCommand.Start);

        // Fin des états transitoires généraux quand toutes les stations actives ont terminé.
        var active = _stations.Keys.Where(IsStationAvailable).Select(id => _stations[id]).ToList();
        var done = _general.State switch
        {
            SeqState.Resetting => active.All(s => s.State == SeqState.Idle),
            SeqState.Stopping => active.All(s => s.State == SeqState.Stopped),
            SeqState.Clearing => _stations.Values.All(s => s.State == SeqState.Stopped),
            _ => false,
        };
        if (done) _general.Complete();

        Changed?.Invoke();
    }
}
