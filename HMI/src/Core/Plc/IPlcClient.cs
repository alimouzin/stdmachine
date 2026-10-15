using Core.Model;
using Core.StateMachine;

namespace Core.Plc;

public sealed record StationStatus(string Id, string Name, StationType Type, SeqState State, bool Available);

public sealed record MachineStatus(
    SeqState State, SeqMode Mode, bool IsDegraded, IReadOnlyList<StationStatus> Stations);

public enum ManualActionResult
{
    Done,
    /// <summary>Mode, état machine ou état de station incompatible avec le manuel.</summary>
    NotAllowed,
    /// <summary>Station, device/module ou action inconnus.</summary>
    Unknown,
}

/// <summary>
/// Frontière HMI / PLC. La première implémentation est <see cref="PlcSimulator"/>,
/// l'implémentation ADS (Beckhoff) viendra plus tard sans changer le reste.
/// </summary>
public interface IPlcClient
{
    MachineDef Machine { get; }

    MachineStatus GetStatus();

    /// <summary>Levé à chaque changement d'état, depuis un thread quelconque.</summary>
    event Action? StatusChanged;

    /// <summary>Vrai si la commande est acceptée dans l'état et le mode courants (sert à activer les boutons).</summary>
    bool CanSendCommand(SeqCommand command);

    /// <summary>Vrai si la commande locale est acceptée par la station dans son état courant.</summary>
    bool CanSendStationCommand(string stationId, SeqCommand command);

    /// <summary>Commande envoyée au séquenceur général. Faux si refusée dans l'état courant.</summary>
    bool SendCommand(SeqCommand command);

    /// <summary>Commande locale à une station (Clear / Reset de réintégration après un Aborted local).</summary>
    bool SendStationCommand(string stationId, SeqCommand command);

    bool SetMode(SeqMode mode);

    /// <summary>
    /// Exécute une action manuelle sur un device ou un module de la station.
    /// Terminée quand l'action est finie (ou refusée tout de suite).
    /// </summary>
    Task<ManualActionResult> ExecuteManualActionAsync(
        string stationId, string targetId, string actionId, CancellationToken ct = default);
}

/// <summary>Outils de test propres au simulateur ; n'existent pas sur un vrai PLC.</summary>
public interface ISimulationControl
{
    /// <summary>Force un Abort sur une station pour tester le comportement.</summary>
    bool ForceAbort(string stationId);
}
