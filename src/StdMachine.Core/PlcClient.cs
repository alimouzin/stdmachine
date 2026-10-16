namespace StdMachine.Core;

// Interface HMI–PLC (docs/06 § 7). La HMI ne connaît que cette interface : DemoPlcClient aujourd'hui,
// SimulatedPlcClient ensuite, AdsPlcClient plus tard.

public sealed record CommandResult(bool Accepted, string? Reason = null)
{
    public static readonly CommandResult Ok = new CommandResult(true);
    public static CommandResult Refused(string reason) => new CommandResult(false, reason);
}

public enum OverrideLevel
{
    Machine,
    Station,
    Module,
    Axis,
}

public sealed record OverrideTarget(OverrideLevel Level, int Station = 0, Guid? ObjectId = null);

public enum StepGranularity
{
    Operation,
    Step,
    Action,
}

public sealed record ManualActionRequest(int Station, Guid ObjectId, string Action, IReadOnlyList<double> Args);

public sealed record ManualResult(bool Accepted, string? Reason, IReadOnlyList<ConditionTermRuntime>? Results = null);

public sealed record JogRequest(int Station, Guid ObjectId, bool Positive, double SpeedPercent);

public interface IJogSession : IDisposable
{
}

public sealed record TeachRequest(int Station, Guid PositionId, IReadOnlyList<string> Components);

public sealed record TeachResult(bool Accepted, string? Reason, double X, double Y, double Z, double C);

public sealed record LaunchRequest(int Station, Guid ProcessId, int? Mover);

/// <summary>Résultat du compilateur (phase 3) : pour le moment, seulement l'empreinte du projet compilé.</summary>
public sealed record CompiledProject(string Hash, int InstructionCount = 0);

public sealed record DeployProgress(string Block, double Fraction);

public sealed record DeployResult(bool Success, string? Hash, DateTime At, IReadOnlyList<string> Refusals);

public sealed record DeployedInfo(string? Hash, DateTime? At, string? By);

public sealed record OrderStart(string Number, string Reference, int Quantity);

public enum OrderCommand
{
    Suspend,
    Resume,
    Cancel,
}

public sealed record AlarmKey(int Code, string? Object);

public sealed record AlarmEvent(AlarmInfo Alarm, string Event);

public sealed record TraceRecord(string PartId, string Station, string Operation, DateTime Start, DateTime End, Verdict Verdict);

public sealed record PressCurve(IReadOnlyList<(double Stroke, double Force)> Points);

public interface IPlcClient
{
    /// <summary>net48 : IAsyncDisposable (Microsoft.Bcl.AsyncInterfaces) n'est pas disponible sans paquet ; même contrat, avec Task.</summary>
    Task DisposeAsync();

    PlcConnectionState ConnectionState { get; }
    event EventHandler<PlcConnectionState>? ConnectionStateChanged;

    /// <summary>Instantané cohérent de tout l'état publié, mis à jour à chaque lecture cyclique.</summary>
    PlcSnapshot Snapshot { get; }

    /// <summary>Au plus 10 fois par seconde. Peut être levé sur un fil quelconque.</summary>
    event EventHandler<PlcSnapshot>? SnapshotUpdated;

    Task ConnectAsync(CancellationToken ct);

    Task<CommandResult> SendCommandAsync(StateCommand command, CancellationToken ct);
    Task<CommandResult> SendStationCommandAsync(int station, StateCommand command, CancellationToken ct);
    Task<CommandResult> SetModeAsync(MachineMode mode, CancellationToken ct);
    Task<CommandResult> SetOverrideAsync(OverrideTarget target, double percent, bool manual, CancellationToken ct);

    Task<ManualResult> ExecuteManualAsync(ManualActionRequest request, CancellationToken ct);
    IJogSession StartJog(JogRequest request);
    Task<TeachResult> TeachPositionAsync(TeachRequest request, CancellationToken ct);

    Task<CommandResult> SetStepModeAsync(int station, StepGranularity? granularity, CancellationToken ct);
    Task<CommandResult> StepAsync(int station, CancellationToken ct);
    Task<CommandResult> LaunchTableAsync(LaunchRequest request, CancellationToken ct);

    Task<DeployResult> DeployAsync(CompiledProject project, IProgress<DeployProgress>? progress, CancellationToken ct);
    Task<DeployedInfo> GetDeployedInfoAsync(CancellationToken ct);

    Task<CommandResult> StartOrderAsync(OrderStart order, CancellationToken ct);
    Task<CommandResult> OrderCommandAsync(OrderCommand command, CancellationToken ct);
    Task<CommandResult> ConfirmPartsRemovedAsync(int mover, CancellationToken ct);
    Task<CommandResult> AcknowledgeAlarmAsync(AlarmKey? key, CancellationToken ct);

    /// <summary>Forçages (docs/12) : pose et levée, relayés au PLC.</summary>
    Task<CommandResult> SetDeclaredStateAsync(DeclaredState state, string? reason, DateTime? until, CancellationToken ct);
    Task<CommandResult> SetStationOutOfProductionAsync(int station, bool outOfProduction, string? reason, CancellationToken ct);
    Task<CommandResult> SetMoverOutOfServiceAsync(int mover, bool outOfService, string? reason, CancellationToken ct);

    /// <summary>Libération qualité (docs/10 § 6).</summary>
    Task<CommandResult> ReleaseQualityAsync(QualityScope scope, string name, string reason, CancellationToken ct);
}

/// <summary>Disponible seulement sur le simulateur (docs/06 § 7).</summary>
public interface ISimulatorControl
{
    double TimeScale { get; set; }
    void ForceStationAbort(int station);
}
