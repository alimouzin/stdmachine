namespace StdMachine.Core;

// Instantané PLC (docs/06 § 7) : tout l'état publié, immuable. Les vues lisent l'instantané,
// elles ne demandent jamais une valeur isolée au PLC.

public enum PlcConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    /// <summary>Connecté au simulateur (orange dans la barre du haut).</summary>
    Simulation,
    /// <summary>Données de démonstration rejouées (puce « Données de démonstration »).</summary>
    Demo,
}

/// <summary>Couleurs de la balise (fixes ou clignotantes).</summary>
[Flags]
public enum BeaconLights
{
    None = 0,
    Red = 1,
    Orange = 2,
    Green = 4,
    Blue = 8,
}

public readonly record struct BeaconState(BeaconLights Lights, BeaconLights Blinking)
{
    public static readonly BeaconState Off = new BeaconState(BeaconLights.None, BeaconLights.None);

    /// <summary>Table des balises de docs/02 § 8, pour un état PackML.</summary>
    public static BeaconState For(PackMlState state, bool maintenance = false, bool operatorWaited = false)
    {
        BeaconLights on, blink = BeaconLights.None;
        switch (state)
        {
            case PackMlState.Execute: on = BeaconLights.Green; break;
            case PackMlState.Idle: on = BeaconLights.Green; blink = BeaconLights.Green; break;
            case PackMlState.Held: on = BeaconLights.Orange; break;
            case PackMlState.Resetting:
            case PackMlState.Stopping:
            case PackMlState.Clearing: on = BeaconLights.Orange; blink = BeaconLights.Orange; break;
            case PackMlState.Aborted: on = BeaconLights.Red; blink = BeaconLights.Red; break;
            default: on = BeaconLights.Orange; break;
        }
        if (maintenance) on |= BeaconLights.Blue;
        if (operatorWaited) { on |= BeaconLights.Blue; blink |= BeaconLights.Blue; }
        return new BeaconState(on, blink);
    }
}

public enum MoverState
{
    Free,
    EnRoute,
    Queued,
    InStation,
    Error,
    /// <summary>Mover « à vider » : l'opérateur doit retirer les pièces.</summary>
    ToEmpty,
    OutOfService,
}

public enum PartState
{
    InProcess,
    Good,
    Bad,
    Exited,
    Rejected,
    Removed,
}

public enum Verdict
{
    Undefined,
    Good,
    Bad,
}

public enum AlarmSeverity
{
    Information,
    Warning,
    StationFault,
    MachineFault,
    Quality,
}

public enum AlarmState
{
    Active,
    ClearedUnacknowledged,
    Acknowledged,
}

/// <summary>État d'une carte de l'éditeur en exécution (docs/08 § 7.2).</summary>
public enum StepRunState
{
    Inactive,
    Next,
    Running,
    WaitingCondition,
    Locked,
    OverrideStopped,
    Retry,
    Done,
    Error,
    /// <summary>Branche non prise (Si / Sinon) pour le cycle en cours.</summary>
    NotTaken,
    /// <summary>Pas à pas : la carte attend le pas.</summary>
    StepPending,
}

public enum ObjectState
{
    Uninitialized,
    Ready,
    Busy,
    Error,
    Disabled,
}

public enum DeclaredState
{
    None,
    PlannedStop,
    ChangeOver,
    WaitUpstream,
    WaitDownstream,
    PlannedMaintenance,
    CorrectiveMaintenance,
    Trials,
    Cleaning,
}

public enum QualityScope
{
    Station,
    StationImputed,
    Group,
    Mover,
    Machine,
    Order,
}

public enum QualityLevel
{
    Normal,
    Warning,
    Action,
    Probation,
}

public enum OrderState
{
    Created,
    Running,
    Suspended,
    Finished,
    Cancelled,
}

public sealed record MachineStatus(
    PackMlState State,
    MachineMode Mode,
    DateTime StateSince,
    double OverridePercent,
    double EffectiveOverridePercent,
    BeaconState Beacon,
    string? StopCause,
    bool LoadingLocked,
    int ActiveFaults,
    int ActiveWarnings,
    string ProjectHash);

/// <summary>Contexte d'exécution d'une station : une ligne par branche active (docs/08 § 6.2).</summary>
public sealed record ContextStatus(int Index, string Label, Guid? StepId, string? StepTitle);

/// <summary>État en direct d'une carte du process en cours d'exécution dans une station.</summary>
public sealed record StepRuntime(
    Guid StepId,
    StepRunState State,
    double ElapsedSeconds,
    double EstimatedSeconds,
    double Progress,
    int Attempt = 0,
    int MaxAttempts = 0,
    string? Detail = null,
    IReadOnlyList<ConditionTermRuntime>? Terms = null,
    Guid? CurrentSubStep = null,
    string? BlockResult = null);

public sealed record ConditionTermRuntime(string Text, bool Satisfied);

public sealed record StationStatus(
    int Number,
    string Name,
    PackMlState State,
    DateTime StateSince,
    double OverridePercent,
    BeaconState Beacon,
    IReadOnlyList<string> Groups,
    string? Operation,
    double OperationElapsedSeconds,
    double OperationEstimatedSeconds,
    int? MoverNumber,
    string? PartId,
    Guid? RunningProcessId,
    IReadOnlyList<StepRuntime> Steps,
    IReadOnlyList<ContextStatus> Contexts,
    string? FaultText,
    bool QualityHold,
    bool OutOfProduction,
    MachineMode Mode,
    int ConsecutiveRejects,
    double RejectRatePercent)
{
    public bool IsRunningOperation => Operation != null;
}

public sealed record PartInfo(int Slot, string Id, PartState State, Verdict Verdict);

public sealed record MoverStatus(
    int Number,
    double X,
    double Y,
    double Rotation,
    MoverState State,
    int? StationNumber,
    double? DestinationX,
    double? DestinationY,
    int GeneralStep,
    string? GeneralStepLabel,
    IReadOnlyList<PartInfo> Parts,
    int QueueFor = 0);

public sealed record ObjectStatus(
    int StationNumber,
    Guid ObjectId,
    ObjectState State,
    IReadOnlyList<double> Values,
    string? ActionText,
    double ActionProgress,
    string? ErrorText,
    long ActionCount = 0,
    long ErrorCount = 0,
    double MotionHours = 0);

public sealed record AlarmInfo(
    int Id,
    DateTime Time,
    DateTime? ClearedTime,
    string Station,
    string? Object,
    int Code,
    int SubCode,
    string Text,
    string? SubText,
    AlarmSeverity Severity,
    AlarmState State,
    int? StationNumber,
    string? Context);

public sealed record QualityTrack(
    QualityScope Scope,
    string Name,
    int Consecutive,
    double RatePercent,
    int SampleCount,
    int WindowSize,
    string Window,
    QualityLevel Level,
    bool Locked,
    string? Reason,
    DateTime? Since,
    double? ImputedRatePercent,
    int ProbationLeft = 0);

public sealed record QualityStatus(IReadOnlyList<QualityTrack> Tracks, bool LoadingLocked, string? LoadingLockedReason);

public sealed record ForceEntry(
    string Kind,
    string Target,
    string Description,
    string Reason,
    string By,
    DateTime Since,
    DateTime? Until);

public sealed record ForceStatus(
    DeclaredState Declared,
    string? DeclaredReason,
    string? DeclaredBy,
    DateTime? DeclaredSince,
    DateTime? DeclaredUntil,
    bool StartBlocked,
    IReadOnlyList<ForceEntry> Entries);

public sealed record OrderInfo(
    string Number,
    string Reference,
    int Quantity,
    int Good,
    int Bad,
    int InProcess,
    OrderState State,
    DateTime Created,
    DateTime? Started,
    DateTime? EstimatedEnd,
    int BudgetBad);

public sealed record OrderStatus(OrderInfo? Current, IReadOnlyList<OrderInfo> Orders);

public sealed record NetworkDeviceInfo(string Name, string Station, bool Reachable, int LatencyMs);

public sealed record PlcSnapshot(
    DateTime Time,
    double ScenarioSeconds,
    PlcConnectionState Connection,
    MachineStatus Machine,
    IReadOnlyList<StationStatus> Stations,
    IReadOnlyList<MoverStatus> Movers,
    IReadOnlyList<ObjectStatus> Objects,
    IReadOnlyList<AlarmInfo> Alarms,
    QualityStatus Quality,
    ForceStatus Forces,
    OrderStatus Orders)
{
    public StationStatus? Station(int number) => Stations.FirstOrDefault(s => s.Number == number);

    public IEnumerable<ObjectStatus> ObjectsOf(int station) => Objects.Where(o => o.StationNumber == station);

    public int ActiveAlarmCount => Alarms.Count(a => a.State != AlarmState.Acknowledged && a.Severity != AlarmSeverity.Information);
}
