using StdMachine.Core;

namespace StdMachine.Simulation.Demo;

/// <summary>
/// Client PLC de démonstration (docs/07 § 9.1) : rejoue en boucle le scénario scripté de <see cref="DemoScript"/>.
/// Les commandes modifient l'état de démonstration de façon plausible : Démarrer lance le scénario, Abort le fige en
/// Aborted, Acquitter puis Réarmer le remettent au début. Ce n'est pas le simulateur : aucun séquenceur, aucun routage réel.
/// </summary>
public sealed class DemoPlcClient : IPlcClient, ISimulatorControl
{
    private readonly object _gate = new object();
    private readonly DemoUserState _user = new DemoUserState();
    private readonly System.Threading.Timer _timer;
    private double _t;
    private DateTime _last;
    private DateTime? _userUntil;
    private PlcSnapshot _snapshot;
    private bool _disposed;

    public DemoPlcClient(MachineProject project)
    {
        Project = project;
        Script = new DemoScript(project);
        _last = DateTime.UtcNow;
        _snapshot = Script.Evaluate(0, DateTime.Now, PlcConnectionState.Demo, _user);
        _timer = new System.Threading.Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public MachineProject Project { get; }
    public DemoScript Script { get; }

    public PlcConnectionState ConnectionState => PlcConnectionState.Demo;
    public event EventHandler<PlcConnectionState>? ConnectionStateChanged { add { } remove { } }

    public PlcSnapshot Snapshot { get { lock (_gate) return _snapshot; } }
    public event EventHandler<PlcSnapshot>? SnapshotUpdated;

    public double TimeScale { get; set; } = 1.0;

    /// <summary>Temps du scénario (pour les captures : instantané à 0, 30, 60 et 90 s).</summary>
    public PlcSnapshot SnapshotAt(double seconds)
    {
        lock (_gate) return Script.Evaluate(seconds, DateTime.Now, PlcConnectionState.Demo, new DemoUserState());
    }

    public Task ConnectAsync(CancellationToken ct)
    {
        _last = DateTime.UtcNow;
        _timer.Change(0, 100);
        return Task.CompletedTask;
    }

    // ------------------------------------------------------------------ boucle

    private void Tick()
    {
        PlcSnapshot snap;
        lock (_gate)
        {
            if (_disposed) return;
            var now = DateTime.UtcNow;
            double dt = (now - _last).TotalSeconds * TimeScale;
            _last = now;

            if (_user.MachineState == null)
            {
                _t += dt;
                if (_t >= DemoScript.LoopSeconds)
                {
                    _t -= DemoScript.LoopSeconds;
                    _user.Acknowledged.Clear();
                    _user.QualityReleased = false;
                }
            }
            else if (_userUntil.HasValue && DateTime.Now >= _userUntil.Value)
            {
                AdvanceUserState();
            }
            snap = Script.Evaluate(_t, DateTime.Now, PlcConnectionState.Demo, _user);
            _snapshot = snap;
        }
        SnapshotUpdated?.Invoke(this, snap);
    }

    /// <summary>États transitoires déclenchés par l'utilisateur : Stopping → Stopped, Clearing → Stopped, Resetting → Idle.</summary>
    private void AdvanceUserState()
    {
        switch (_user.MachineState)
        {
            case PackMlState.Stopping:
            case PackMlState.Clearing:
                SetUserState(PackMlState.Stopped, null);
                break;
            case PackMlState.Resetting:
                SetUserState(PackMlState.Idle, null);
                break;
        }
    }

    private void SetUserState(PackMlState state, double? seconds)
    {
        _user.MachineState = state;
        _user.MachineStateSince = DateTime.Now;
        _userUntil = seconds.HasValue ? DateTime.Now.AddSeconds(seconds.Value) : (DateTime?)null;
    }

    private void Publish()
    {
        PlcSnapshot snap;
        lock (_gate)
        {
            snap = Script.Evaluate(_t, DateTime.Now, PlcConnectionState.Demo, _user);
            _snapshot = snap;
        }
        SnapshotUpdated?.Invoke(this, snap);
    }

    // ------------------------------------------------------------------ commandes d'état

    private static string RefusalFor(StateCommand c, PackMlState s)
    {
        switch (c)
        {
            case StateCommand.Reset: return "Refusée : Réarmer n'est possible qu'à l'état Stopped (la machine est " + s + ").";
            case StateCommand.Start: return "Refusée : Démarrer est possible depuis Idle ou Held (la machine est " + s + ").";
            case StateCommand.Hold: return "Refusée : Pause n'est possible qu'en Execute (la machine est " + s + ").";
            case StateCommand.Stop: return "Refusée : Arrêter n'est pas possible à l'état " + s + ".";
            case StateCommand.Clear: return "Refusée : Acquitter n'est possible qu'à l'état Aborted (la machine est " + s + ").";
            default: return "Refusée.";
        }
    }

    public async Task<CommandResult> SendCommandAsync(StateCommand command, CancellationToken ct)
    {
        await Task.Delay(250, ct).ConfigureAwait(false);
        CommandResult result;
        lock (_gate)
        {
            var state = _snapshot.Machine.State;
            if (!PackMlRules.IsAccepted(command, state)) result = CommandResult.Refused(RefusalFor(command, state));
            else if (command == StateCommand.Start && _snapshot.Forces.StartBlocked)
                result = CommandResult.Refused("Démarrage bloqué : " + DemoText.Declared(_snapshot.Forces.Declared) + " déclarée par " + _snapshot.Forces.DeclaredBy + ".");
            else
            {
                result = CommandResult.Ok;
                switch (command)
                {
                    case StateCommand.Hold: SetUserState(PackMlState.Held, null); break;
                    case StateCommand.Start:
                        if (_user.MachineState == PackMlState.Held) _user.MachineState = null;
                        else { _user.MachineState = null; _t = DemoScript.ExecuteFrom; }
                        break;
                    case StateCommand.Stop: SetUserState(PackMlState.Stopping, 2.5); break;
                    case StateCommand.Abort: SetUserState(PackMlState.Aborted, null); break;
                    case StateCommand.Clear: SetUserState(PackMlState.Clearing, 2.5); break;
                    case StateCommand.Reset:
                        if (_user.MachineState == PackMlState.Stopped || _user.MachineState == null) SetUserState(PackMlState.Resetting, 3.0);
                        break;
                }
            }
        }
        Publish();
        return result;
    }

    public async Task<CommandResult> SendStationCommandAsync(int station, StateCommand command, CancellationToken ct)
    {
        await Task.Delay(250, ct).ConfigureAwait(false);
        lock (_gate)
        {
            var st = _snapshot.Station(station);
            if (st == null) return CommandResult.Refused("Station inconnue.");
            if (!PackMlRules.IsAccepted(command, st.State)) return CommandResult.Refused(RefusalFor(command, st.State).Replace("la machine", "la station"));
            // Réintégration de Pressage 01 : Acquitter, Réarmer, Démarrer font avancer le scénario d'une étape.
            if (station == DemoProject.Pressage01 && _user.MachineState == null)
            {
                if (command == StateCommand.Clear && _t < DemoScript.ClearFrom) _t = DemoScript.ClearFrom;
                else if (command == StateCommand.Reset && _t >= DemoScript.ClearEnd && _t < DemoScript.ResetStationFrom) _t = DemoScript.ResetStationFrom;
                else if (command == StateCommand.Start && _t >= DemoScript.IdleStationFrom && _t < DemoScript.ReintegratedAt) _t = DemoScript.ReintegratedAt;
            }
        }
        Publish();
        return CommandResult.Ok;
    }

    public Task<CommandResult> StartDrainAsync(CancellationToken ct)
        => Task.FromResult(CommandResult.Refused("Indisponible sur la maquette : lancez l'application avec --simulator."));

    public Task<CommandResult> CancelDrainAsync(CancellationToken ct)
        => Task.FromResult(CommandResult.Refused("Indisponible sur la maquette : lancez l'application avec --simulator."));

    public async Task<CommandResult> SetModeAsync(MachineMode mode, CancellationToken ct)
    {
        await Task.Delay(150, ct).ConfigureAwait(false);
        lock (_gate)
        {
            if (_snapshot.Machine.State != PackMlState.Stopped)
                return CommandResult.Refused("Refusée : le mode ne change qu'à l'état Stopped.");
            _user.Mode = mode;
        }
        Publish();
        return CommandResult.Ok;
    }

    public Task<CommandResult> SetOverrideAsync(OverrideTarget target, double percent, bool manual, CancellationToken ct)
    {
        lock (_gate) { if (target.Level == OverrideLevel.Machine) _user.OverridePercent = Math.Max(0, Math.Min(100, percent)); }
        Publish();
        return Task.FromResult(CommandResult.Ok);
    }

    // ------------------------------------------------------------------ manuel, pas à pas

    public async Task<ManualResult> ExecuteManualAsync(ManualActionRequest request, CancellationToken ct)
    {
        await Task.Delay(400, ct).ConfigureAwait(false);
        return new ManualResult(true, null, new[] { new ConditionTermRuntime("Terminée", true) });
    }

    private sealed class DemoJog : IJogSession { public void Dispose() { } }

    public IJogSession StartJog(JogRequest request) => new DemoJog();

    public async Task<TeachResult> TeachPositionAsync(TeachRequest request, CancellationToken ct)
    {
        await Task.Delay(300, ct).ConfigureAwait(false);
        return new TeachResult(true, null, 212.4, 118.8, -24.6, 0);
    }

    public Task<CommandResult> SetStepModeAsync(int station, StepGranularity? granularity, CancellationToken ct) => Task.FromResult(CommandResult.Ok);
    public Task<CommandResult> StepAsync(int station, CancellationToken ct) => Task.FromResult(CommandResult.Ok);
    public Task<CommandResult> LaunchTableAsync(LaunchRequest request, CancellationToken ct) => Task.FromResult(CommandResult.Ok);

    // ------------------------------------------------------------------ déploiement et OF

    public async Task<DeployResult> DeployAsync(CompiledProject project, IProgress<DeployProgress>? progress, CancellationToken ct)
    {
        foreach (var (block, ms) in new[] { ("Machine", 700), ("Station 1 · Chargement", 450), ("Station 2 · Pressage 01", 600), ("Station 3 · Pressage 02", 600), ("Station 4 à 6", 500), ("Recettes de presse", 400) })
        {
            progress?.Report(new DeployProgress(block, 0));
            await Task.Delay(ms / 2, ct).ConfigureAwait(false);
            progress?.Report(new DeployProgress(block, 1));
            await Task.Delay(ms / 2, ct).ConfigureAwait(false);
        }
        return new DeployResult(true, project.Hash, DateTime.Now, new string[0]);
    }

    public Task<DeployedInfo> GetDeployedInfoAsync(CancellationToken ct)
        => Task.FromResult(new DeployedInfo("9F3A1C07D2E84B66", new DateTime(2026, 10, 14, 16, 5, 0), "A. Martin"));

    public Task<CommandResult> StartOrderAsync(OrderStart order, CancellationToken ct) => Task.FromResult(CommandResult.Ok);

    public Task<CommandResult> OrderCommandAsync(OrderCommand command, CancellationToken ct)
    {
        lock (_gate) { _user.OrderSuspended = command == OrderCommand.Suspend; _user.OrderCancelled = command == OrderCommand.Cancel; }
        return Task.FromResult(CommandResult.Ok);
    }

    public Task<CommandResult> ConfirmPartsRemovedAsync(int mover, CancellationToken ct) => Task.FromResult(CommandResult.Ok);

    public Task<CommandResult> AcknowledgeAlarmAsync(AlarmKey? key, CancellationToken ct)
    {
        lock (_gate)
        {
            foreach (var a in _snapshot.Alarms)
            {
                if (a.Severity == AlarmSeverity.StationFault && a.State == AlarmState.Active) continue; // un défaut s'acquitte par la commande Acquitter
                if (key == null || (a.Code == key.Code && a.Object == key.Object)) _user.Acknowledged.Add(a.Id);
            }
        }
        Publish();
        return Task.FromResult(CommandResult.Ok);
    }

    // ------------------------------------------------------------------ forçages, qualité

    public Task<CommandResult> SetDeclaredStateAsync(DeclaredState state, string? reason, DateTime? until, CancellationToken ct)
    {
        lock (_gate)
        {
            _user.Declared = state;
            _user.DeclaredReason = reason;
            _user.DeclaredSince = state == DeclaredState.None ? (DateTime?)null : DateTime.Now;
            _user.DeclaredUntil = until;
        }
        Publish();
        return Task.FromResult(CommandResult.Ok);
    }

    public Task<CommandResult> SetStationOutOfProductionAsync(int station, bool outOfProduction, string? reason, CancellationToken ct)
    {
        lock (_gate)
        {
            if (outOfProduction) _user.OutOfProduction[station] = reason ?? "Réglage";
            else _user.OutOfProduction.Remove(station);
        }
        Publish();
        return Task.FromResult(CommandResult.Ok);
    }

    public Task<CommandResult> SetMoverOutOfServiceAsync(int mover, bool outOfService, string? reason, CancellationToken ct)
    {
        lock (_gate)
        {
            if (outOfService) _user.MoversOut[mover] = reason ?? "Nid abîmé";
            else _user.MoversOut.Remove(mover);
        }
        Publish();
        return Task.FromResult(CommandResult.Ok);
    }

    public Task<CommandResult> ReleaseQualityAsync(QualityScope scope, string name, string reason, CancellationToken ct)
    {
        lock (_gate)
        {
            _user.QualityReleased = true;
            if (_t >= DemoScript.QualityFrom && _t < DemoScript.QualityRelease) _t = DemoScript.QualityRelease;
        }
        Publish();
        return Task.FromResult(CommandResult.Ok);
    }

    public void ForceStationAbort(int station)
    {
        lock (_gate) { if (station == DemoProject.Pressage01) _t = Math.Max(_t, Script.AbortAt); }
        Publish();
    }

    public Task DisposeAsync()
    {
        lock (_gate) _disposed = true;
        _timer.Dispose();
        return Task.CompletedTask;
    }
}
