using StdMachine.Core;
using StdMachine.Core.Catalog;
using StdMachine.Core.Compiler;

namespace StdMachine.Simulation.Engine;

public enum MvState
{
    Unknown,
    Ready,
    Routing,
    Queued,
    AtStation,
    Buffered,
    ToEmpty,
    Error,
    Halted,
    OutOfService,
}

/// <summary>Mover du plan et son contexte d'exécution du process général (ST_MoverCtx).</summary>
public sealed class Mover
{
    public int Number;
    public double X, Y, C;
    public MvState State = MvState.Ready;
    public int PC = 1;
    public int Table = 1;
    public int[] RetTable = new int[5];
    public int[] RetPC = new int[5];
    public int Depth;
    public int TargetGroup, TargetOp, TargetSlot;
    public double[] OpParams = new double[8];
    public int Station;           // 1..n (rang dans MachineData.Stations), 0 = aucune
    public int Dest;              // position planaire visée (1..n), 0 = aucune
    public int Pos;               // position planaire occupée (1..n), 0 = entre deux
    public bool RequestPosted;
    public double ArrivalMs;
    public double NoStationSince = -1;
    public Part[] Slots = new Part[0];
    public bool OutOfService;
    public bool OutImmediate;
    public string? OutReason;
    public string? WaitReason;
    public bool StationAborted;
    public double LastMoveStartMs;
    public double[] Vars = new double[8];

    // Déplacement
    public bool Moving;
    public double FromX, FromY, ToX, ToY, MoveStartMs, MoveDurMs;
    public int ArrivedAtPos;

    public bool IsEmpty => Slots.All(s => !s.Present);
}

public sealed class SimTrace
{
    public uint Seq;
    public string PartId = string.Empty;
    public int Mover, Slot, Station, Group, Operation;
    public double StartMs, EndMs;
    public int Verdict;
    public int ErrorCode;
    public double[] Data = new double[Part.MaxData];
}

public sealed class SimOrder
{
    public string Number = string.Empty;
    public string Reference = string.Empty;
    public int Quantity;
    public int Good, Bad, InProcess;
    public int Created;
    public OrderState State = OrderState.Running;
    public double StartedMs;
    public double? FinishedMs;
}

/// <summary>
/// Le PLC complet simulé : machine PackML, séquenceur général, gestionnaire de movers, stations (docs/05 §§ 5–6, 9).
/// </summary>
public sealed class MachineSim
{
    private readonly CompiledTables _c;
    private readonly CatalogDocument _catalog;
    private readonly List<StationSim> _stations = new List<StationSim>();
    private readonly List<Mover> _movers = new List<Mover>();
    private readonly int[] _owner;
    private readonly double[] _lastReceived;
    private uint _traceSeq;
    private uint _reqSeq;
    private int _partCounter;
    private StateCommand? _pending;
    private double _planarTimer;
    private bool _planarActive;
    private bool _machineFault;
    private string? _machineFaultText;
    private readonly string _partPrefix;

    public MachineSim(CompiledTables compiled, CatalogDocument catalog, int seed = 1, string partPrefix = "MX4")
    {
        _c = compiled; _catalog = catalog; _partPrefix = partPrefix;
        Rng = new SimRandom(seed);
        _owner = new int[compiled.Machine.PlanarPositions.Count + 1];
        _lastReceived = new double[compiled.Machine.Stations.Count + 1];
        foreach (var sd in compiled.Stations.OrderBy(s => s.Number))
            _stations.Add(new StationSim(sd, compiled, catalog, Alarms, Rng, () => NowMs, () => OverridePercent));
        Quality = new QualityMonitor(this);
        PlaceInitialMovers();
    }

    public QualityMonitor Quality { get; }

    /// <summary>Cause d'arrêt imposée par une réaction (consommée à l'arrêt suivant).</summary>
    public string? StopCauseOverride { get; set; }

    public double NowMs { get; private set; }
    public AlarmLog Alarms { get; } = new AlarmLog();
    public SimRandom Rng { get; }
    public PackMlMachine Pack { get; } = new PackMlMachine();
    public MachineMode Mode { get; private set; } = MachineMode.Production;
    public double OverridePercent { get; set; } = 100;
    public IReadOnlyList<StationSim> Stations => _stations;
    public IReadOnlyList<Mover> Movers => _movers;
    public IReadOnlyList<int> PositionOwners => _owner;
    public CompiledTables Compiled => _c;
    public List<SimTrace> Traces { get; } = new List<SimTrace>();
    public SimOrder? Order { get; private set; }
    public List<SimOrder> OrderHistory { get; } = new List<SimOrder>();
    public string? StopCause { get; private set; }
    public bool LoadingLocked { get; set; }
    public string? LoadingLockedReason { get; set; }
    public int PartsExited { get; private set; }
    public int PartsRejected { get; private set; }
    public int PartsCreated => _partCounter;
    public DeclaredState Declared { get; set; }
    public string? DeclaredReason { get; set; }
    public bool PlanarActive => _planarActive;
    public bool LastCommandAccepted => Pack.LastCommandAccepted;
    public string? LastRefusal { get; private set; }

    /// <summary>Résultat d'opération transmis à la surveillance des rebuts (docs/05 § 5.5.6).</summary>
    public event Action<OperationResult>? OperationFinished;

    public event Action<string>? EventRaised;

    /// <summary>Une pièce vient d'être clôturée (avant la remise à zéro de l'emplacement).</summary>
    public event Action<ClosedPart>? PartClosed;

    public sealed record ClosedPart(Part Part, int Mover, int Slot, double ClosedMs, string? Order, string? Reference);

    public sealed record OperationResult(int StationIndex, int Station, int GroupIndex, int Mover, string PartId, bool Bad, bool Closed, bool ClosedBad, double TimeMs, int AlarmCode);

    private int VerdictIndex => _c.Machine.VerdictIndex;

    public StationSim? StationByNumber(int number) => _stations.FirstOrDefault(s => s.Number == number);

    public int StationIndex(int number) => _stations.FindIndex(s => s.Number == number);

    // ------------------------------------------------------------------ mise en place

    private void PlaceInitialMovers()
    {
        int n = _c.Machine.Movers;
        var order = new List<int>();
        for (int i = 0; i < _c.Machine.PlanarPositions.Count; i++) if (_c.Machine.PlanarPositions[i].Kind == PlanarPositionKind.Buffer) order.Add(i + 1);
        for (int i = 0; i < _c.Machine.PlanarPositions.Count; i++) if (_c.Machine.PlanarPositions[i].Kind == PlanarPositionKind.Park) order.Add(i + 1);
        for (int i = 0; i < n; i++)
        {
            var m = new Mover { Number = i + 1, Slots = Enumerable.Range(0, Math.Max(1, _c.Machine.Slots)).Select(_ => new Part()).ToArray() };
            if (i < order.Count)
            {
                var p = _c.Machine.PlanarPositions[order[i] - 1];
                m.X = p.X; m.Y = p.Y; m.Pos = order[i]; _owner[order[i]] = m.Number;
                m.State = p.Kind == PlanarPositionKind.Buffer ? MvState.Buffered : MvState.Ready;
            }
            _movers.Add(m);
        }
    }

    // ------------------------------------------------------------------ commandes

    public CommandOutcome Command(StateCommand cmd)
    {
        LastRefusal = null;
        if (!PackMlRules.IsAccepted(cmd, Pack.State))
        {
            LastRefusal = "Commande " + cmd + " refusée en état " + Pack.State;
            return new CommandOutcome(false, LastRefusal);
        }
        if (cmd == StateCommand.Start && StartBlocked(out var reason))
        {
            LastRefusal = reason;
            return new CommandOutcome(false, reason);
        }
        _pending = cmd;
        return new CommandOutcome(true, null);
    }

    public readonly record struct CommandOutcome(bool Accepted, string? Reason);

    public bool StartBlocked(out string reason)
    {
        reason = string.Empty;
        bool blocking = Declared == DeclaredState.PlannedMaintenance || Declared == DeclaredState.CorrectiveMaintenance || Declared == DeclaredState.Cleaning
                        || (Declared == DeclaredState.Trials && Mode == MachineMode.Production);
        if (blocking) reason = "Démarrage bloqué : état déclaré « " + Declared + " »" + (DeclaredReason != null ? " (" + DeclaredReason + ")" : string.Empty);
        return blocking;
    }

    public CommandOutcome SetMode(MachineMode mode)
    {
        if (Pack.State != PackMlState.Stopped) return new CommandOutcome(false, "Le mode ne change qu'à l'arrêt (Stopped).");
        Mode = mode;
        foreach (var s in _stations) if (!s.OutOfProduction && s.State == PackMlState.Stopped) s.Mode = mode;
        return new CommandOutcome(true, null);
    }

    /// <summary>Commande donnée à une station seule (réintégration, intervention sur une station hors production).</summary>
    public CommandOutcome StationCommand(int stationNumber, StateCommand cmd)
    {
        var s = StationByNumber(stationNumber);
        if (s == null) return new CommandOutcome(false, "Station inconnue.");
        bool machineAllows = Pack.State == PackMlState.Execute || Pack.State == PackMlState.Held || s.OutOfProduction || Pack.State == PackMlState.Stopped || Pack.State == PackMlState.Aborted;
        if (!machineAllows) return new CommandOutcome(false, "Commande de station refusée en état machine " + Pack.State + ".");
        if (!s.Command(cmd)) return new CommandOutcome(false, "Commande " + cmd + " refusée en état " + s.State + ".");
        return new CommandOutcome(true, null);
    }

    public void SetStationOutOfProduction(int number, bool value)
    {
        var s = StationByNumber(number);
        if (s == null || s.OutOfProduction == value) return;
        s.OutOfProduction = value;
        if (value && s.State == PackMlState.Execute) s.Command(StateCommand.Hold);
        EventRaised?.Invoke("Station " + number + (value ? " hors production" : " remise en production"));
    }

    public void SetMoverOutOfService(int number, bool value, bool immediate = false)
    {
        var m = _movers.FirstOrDefault(x => x.Number == number);
        if (m == null) return;
        m.OutOfService = value; m.OutImmediate = value && immediate;
        if (!value && m.State == MvState.OutOfService) { m.State = MvState.Ready; m.PC = 1; }
    }

    public void ConfirmPartsRemoved(int mover)
    {
        var m = _movers.FirstOrDefault(x => x.Number == mover);
        if (m == null || m.State != MvState.ToEmpty) return;
        foreach (var p in m.Slots.Where(p => p.Present))
        {
            p.Status = PartStatus.Removed;
            CloseOrderPart(p, false);
            p.Clear();
        }
        m.PC = 1; m.State = MvState.Ready; m.WaitReason = null;
    }

    public void StartOrder(string number, string reference, int quantity)
    {
        Order = new SimOrder { Number = number, Reference = reference, Quantity = quantity, StartedMs = NowMs, State = OrderState.Running };
    }

    public void OrderCommand(OrderCommand command)
    {
        if (Order == null) return;
        if (command == Core.OrderCommand.Suspend && Order.State == OrderState.Running) Order.State = OrderState.Suspended;
        else if (command == Core.OrderCommand.Resume && Order.State == OrderState.Suspended) Order.State = OrderState.Running;
        else if (command == Core.OrderCommand.Cancel) { Order.State = OrderState.Cancelled; OrderHistory.Add(Order); Order = null; }
    }

    // ------------------------------------------------------------------ fin des pièces en cours (docs/05 § 5.7, D77)

    /// <summary>Cause d'arrêt quand la machine s'est vidée d'elle-même (docs/02 § 8).</summary>
    public const string DrainedStopCause = StopCauses.Drained;

    /// <summary>Indicateur <c>bDrain</c> du séquenceur général : plus aucune pièce n'est chargée, les pièces engagées finissent.</summary>
    public bool Drain { get; private set; }

    private bool _drainStopSent;

    /// <summary>Pièces portées par les movers (tous emplacements) : ce qui reste à finir.</summary>
    public int PartsInProcess => _movers.Sum(m => m.Slots.Count(p => p.Present));

    /// <summary>Mover « à vider » qui porte encore des pièces et retient la fin (0 s'il n'y en a pas) : l'opérateur doit confirmer leur retrait.</summary>
    public int DrainBlockingMover => _movers.Where(m => m.State == MvState.ToEmpty && m.Slots.Any(p => p.Present)).Select(m => m.Number).FirstOrDefault();

    /// <summary>Commande <c>DrainStart</c> : acceptée en Execute et Held seulement.</summary>
    public CommandOutcome StartDrain()
    {
        if (Pack.State != PackMlState.Execute && Pack.State != PackMlState.Held)
            return new CommandOutcome(false, "La fin des pièces demande l'état Execute ou Held : la machine est " + Pack.State + ".");
        Drain = true;
        _drainStopSent = false;
        EventRaised?.Invoke("Fin des pièces en cours");
        return new CommandOutcome(true, null);
    }

    /// <summary>Commande <c>DrainCancel</c> : lève le mode tant que la machine n'est pas arrêtée ; le chargement reprend.</summary>
    public CommandOutcome CancelDrain()
    {
        if (!Drain) return new CommandOutcome(false, "Aucune fin des pièces en cours.");
        Drain = false;
        _drainStopSent = false;
        EventRaised?.Invoke("Fin des pièces annulée");
        return new CommandOutcome(true, null);
    }

    /// <summary>Une opération d'une fonction de rôle Entrée est demandée ou en cours : sa pièce naîtra à la fin, la machine n'est donc pas vide.</summary>
    private bool EntryOperationInFlight()
    {
        for (int g = 1; g <= _c.Machine.Groups.Count; g++)
        {
            if (_c.Machine.Groups[g - 1].Role != GroupRole.Entry) continue;
            if (StationsOfGroup(g).Any(i => _stations[i].OperationRunning)) return true;
        }
        return _movers.Any(m => m.TargetGroup > 0 && m.Station > 0 && _c.Machine.Groups[m.TargetGroup - 1].Role == GroupRole.Entry);
    }

    /// <summary>Quand le mode est actif et que plus rien n'est en cours : Arrêter automatique (cause « Machine vidée »).</summary>
    private void CheckDrainCompletion()
    {
        if (!Drain || _drainStopSent) return;
        if (Pack.State != PackMlState.Execute && Pack.State != PackMlState.Held) return;
        if (PartsInProcess > 0 || EntryOperationInFlight()) return;
        _drainStopSent = true;
        StopCause = DrainedStopCause; // prime sur « Pause demandée » si la machine était en Held
        EventRaised?.Invoke("Machine vidée : plus aucune pièce en cours");
        Command(StateCommand.Stop);
    }

    // ------------------------------------------------------------------ un cycle

    public void Step(double dtMs)
    {
        NowMs += dtMs;
        MachineStates(dtMs);
        if (Pack.State == PackMlState.Execute) GeneralSequencer();
        MoveMovers(dtMs);
        foreach (var s in _stations) s.Cycle(dtMs);
        PostStationCycle();
        CheckDrainCompletion();
    }

    public void Run(double ms, double stepMs = 10)
    {
        for (double t = 0; t < ms; t += stepMs) Step(stepMs);
    }

    private IEnumerable<int> UsedGroups()
    {
        var routes = _c.Machine.Instructions.Where(i => i.Op == Op.Route).Select(i => i.Args[0]).Distinct();
        return routes;
    }

    private bool StationAvailable(int idx)
    {
        var s = _stations[idx];
        return s.State != PackMlState.Aborted && s.State != PackMlState.Stopped && !s.OutOfProduction;
    }

    private bool StationRoutable(int idx)
    {
        var s = _stations[idx];
        return s.State == PackMlState.Execute && !s.OutOfProduction && !s.QualityHold && s.FaultText == null;
    }

    private void MachineStates(double dtMs)
    {
        var cmd = _pending;
        _pending = null;
        if (cmd != null) PropagateCommand(cmd.Value);

        bool complete = false;
        switch (Pack.State)
        {
            case PackMlState.Resetting:
                _planarTimer += dtMs;
                if (_planarTimer >= 1500) _planarActive = true;
                complete = _planarActive && ResetFinished();
                break;
            case PackMlState.Stopping:
                complete = _stations.All(s => s.State == PackMlState.Stopped || s.State == PackMlState.Aborted || s.OutOfProduction) && _movers.All(m => !m.Moving);
                break;
            case PackMlState.Clearing:
                complete = _stations.All(s => s.State != PackMlState.Clearing && s.State != PackMlState.Aborted || s.OutOfProduction);
                if (complete) { _machineFault = false; _machineFaultText = null; }
                break;
        }

        if (Pack.State == PackMlState.Execute) CheckMachineFaults();
        var before = Pack.State;
        Pack.Cycle(cmd, complete, _machineFault);
        if (Pack.Entry) OnMachineEntry(before, Pack.State);
        if (Pack.State == PackMlState.Aborted) _machineFault = false;
    }

    private bool ResetFinished()
    {
        // Chaque fonction utilisée par le process général a au moins une station en Idle ; une station qui échoue son Reset passe Aborted.
        foreach (int g in UsedGroups())
        {
            var idxs = StationsOfGroup(g).Where(i => !_stations[i].OutOfProduction).ToList();
            if (idxs.Count == 0) continue;
            if (idxs.All(i => _stations[i].State == PackMlState.Aborted)) { _machineFault = true; _machineFaultText = "Fonction sans station après Reset"; return false; }
            if (!idxs.Any(i => _stations[i].State == PackMlState.Idle)) return false;
        }
        return true;
    }

    private IEnumerable<int> StationsOfGroup(int group)
    {
        uint mask = _c.Machine.Groups[group - 1].StationMask;
        for (int i = 0; i < _stations.Count; i++) if ((mask & (1u << i)) != 0) yield return i;
    }

    private void CheckMachineFaults()
    {
        if (_machineFault) return;
        foreach (int g in UsedGroups())
        {
            var idxs = StationsOfGroup(g).ToList();
            if (idxs.Count == 0) continue;
            // Une station volontairement retirée (hors production, arrêt qualité) ne déclenche jamais ce défaut.
            if (idxs.Any(i => _stations[i].OutOfProduction || _stations[i].QualityHold)) continue;
            if (idxs.All(i => !StationAvailable(i)))
            {
                Fault("Fonction " + _c.Machine.Groups[g - 1].Name + " sans station disponible", 700 + g);
                return;
            }
        }
        foreach (var m in _movers) if (m.State == MvState.Error) { Fault("Mover " + m.Number + " en défaut", 720); return; }
    }

    public void Fault(string text, int code)
    {
        _machineFault = true;
        _machineFaultText ??= text;
        Alarms.Raise(0, null, code, 0, text, AlarmSeverity.MachineFault, NowMs, "Machine");
        StopCause ??= text;
    }

    private void PropagateCommand(StateCommand cmd)
    {
        switch (cmd)
        {
            case StateCommand.Reset:
                _planarTimer = 0; _planarActive = false;
                StopCause = null;
                foreach (var s in _stations) if (s.State == PackMlState.Stopped && !s.OutOfProduction) { s.Mode = Mode; s.Command(StateCommand.Reset); }
                break;
            case StateCommand.Start:
                foreach (var s in _stations) if ((s.State == PackMlState.Idle || s.State == PackMlState.Held) && !s.OutOfProduction && !s.QualityHold) s.Command(StateCommand.Start);
                break;
            case StateCommand.Hold:
                foreach (var s in _stations) if (s.State == PackMlState.Execute) s.Command(StateCommand.Hold);
                StopCause ??= "Pause demandée";
                break;
            case StateCommand.Stop:
                foreach (var s in _stations) s.Command(StateCommand.Stop);
                StopCause ??= StopCauseOverride ?? "Arrêt demandé";
                StopCauseOverride = null;
                break;
            case StateCommand.Abort:
                foreach (var s in _stations) s.Command(StateCommand.Abort);
                StopCause ??= "Abort demandé";
                break;
            case StateCommand.Clear:
                foreach (var s in _stations) if (s.State == PackMlState.Aborted) s.Command(StateCommand.Clear);
                foreach (var a in Alarms.All.Where(a => a.Active && a.Station == 0)) Alarms.Clear(a, NowMs);
                break;
        }
    }

    private void OnMachineEntry(PackMlState from, PackMlState to)
    {
        if (to == PackMlState.Aborted)
        {
            foreach (var m in _movers) if (m.Moving) FreezeMover(m);
            foreach (var s in _stations) s.Command(StateCommand.Abort);
        }
        // Le mode « fin des pièces » ne survit ni à un arrêt, ni à un Abort, ni à un défaut machine (docs/05 § 5.7).
        if (to == PackMlState.Stopping || to == PackMlState.Stopped || to == PackMlState.Aborted) { Drain = false; _drainStopSent = false; }
        if (to == PackMlState.Execute) StopCause = null;
    }

    private void FreezeMover(Mover m)
    {
        // Arrêt contrôlé : le mover s'arrête là où il est ; il reprendra sa destination au Start suivant.
        m.Moving = false;
    }

    // ------------------------------------------------------------------ séquenceur général

    private int NextReqId() => (int)++_reqSeq;

    private bool Executing => Pack.State == PackMlState.Execute;

    private void GeneralSequencer()
    {
        // En commençant par le mover qui attend depuis le plus longtemps, pour éviter la famine.
        foreach (var m in _movers.OrderBy(x => x.ArrivalMs).ThenBy(x => x.Number))
        {
            if (m.OutOfService && m.State == MvState.Ready && m.IsEmpty) { SendToPark(m); continue; }
            switch (m.State)
            {
                case MvState.Ready: ReadyMover(m); break;
                case MvState.Buffered: BufferedMover(m); break;
                case MvState.Queued: QueuedMover(m); break;
                case MvState.AtStation: AtStationMover(m); break;
                case MvState.Halted:
                    m.State = MvState.Ready;
                    break;
            }
        }
    }

    private Instr MoverInstr(Mover m) => _c.Machine.Instructions[m.PC - 1];

    private void ReadyMover(Mover m)
    {
        for (int guard = 0; guard < 64; guard++)
        {
            if (m.PC < 1 || m.PC > _c.Machine.Instructions.Count) { m.PC = 1; return; }
            var ins = MoverInstr(m);
            switch (ins.Op)
            {
                case Op.Jump: m.PC = ins.Args[0]; break;
                case Op.JumpIfFalse:
                    MarkVerdictTested(m, _c.Machine.Conditions[ins.Args[0] - 1]);
                    m.PC = EvalMachineCondition(m, ins.Args[0]) ? m.PC + 1 : ins.Args[1];
                    break;
                case Op.SetVar:
                    var part = FirstPart(m);
                    if (part != null && ins.Binds[0].Target == BindKind.PartData) part.Data[ins.Binds[0].Index] = ins.Params[0].Real;
                    m.PC++; break;
                case Op.LoopInit: m.Vars[ins.Args[0] % 8] = ins.Params[0].Real; m.PC++; break;
                case Op.LoopNext:
                    m.Vars[ins.Args[0] % 8] -= 1;
                    m.PC = m.Vars[ins.Args[0] % 8] > 0 ? ins.Args[1] : m.PC + 1;
                    break;
                case Op.Call:
                    if (m.Depth >= 4) { m.State = MvState.Error; Fault("Profondeur d'appel du process général dépassée", 904); return; }
                    m.Depth++; m.RetTable[m.Depth] = m.Table; m.RetPC[m.Depth] = m.PC + 1;
                    m.Table = ins.Args[0]; m.PC = _c.Machine.Tables[m.Table - 1].Start;
                    break;
                case Op.Return:
                    if (m.Depth == 0) { m.PC = 1; return; }
                    m.Table = m.RetTable[m.Depth]; m.PC = m.RetPC[m.Depth]; m.Depth--;
                    break;
                case Op.Alarm:
                    Alarms.Raise(0, null, 900 + ins.Args[2], 0, ins.Args[0] >= 1 && ins.Args[0] <= _c.Machine.Texts.Count ? _c.Machine.Texts[ins.Args[0] - 1] : "Alarme de process",
                        ins.Args[1] == 1 ? AlarmSeverity.Warning : ins.Args[1] == 2 ? AlarmSeverity.StationFault : AlarmSeverity.MachineFault, NowMs, "Process général");
                    if (ins.Args[1] == 3) Fault("Alarme de process", 900 + ins.Args[2]);
                    m.PC++; break;
                case Op.WaitCond:
                    if (EvalMachineCondition(m, ins.Args[0])) { m.PC++; break; }
                    return;
                case Op.End:
                    if (!m.IsEmpty) Alarms.Raise(0, null, 910, m.Number, "Pièce présente en fin de process (mover " + m.Number + ")", AlarmSeverity.Warning, NowMs, "Process général");
                    m.PC = 1; m.Table = 1; m.Depth = 0;
                    break;
                case Op.Route: StartRoute(m, ins); return;
                default: m.PC++; break;
            }
        }
    }

    private Part? FirstPart(Mover m) => m.Slots.FirstOrDefault(p => p.Present) ?? m.Slots.FirstOrDefault();

    private void MarkVerdictTested(Mover m, CCondition cond)
    {
        if (cond.Terms.Any(t => (t.Left.Source == ValueSource.PartData && t.Left.Index == VerdictIndex) || (t.Right.Source == ValueSource.PartData && t.Right.Index == VerdictIndex)))
            foreach (var p in m.Slots) if (p.Present) p.BadHandled = true;
    }

    private bool EvalMachineCondition(Mover m, int number)
    {
        var cond = _c.Machine.Conditions[number - 1];
        if (cond.Terms.Count == 0) return true;
        bool all = cond.Combine == CombineMode.All;
        foreach (var t in cond.Terms)
        {
            double a = ResolveM(m, t.Left), b = ResolveM(m, t.Right);
            bool r = t.Cmp switch
            {
                Cmp.Eq => Math.Abs(a - b) < 1e-9, Cmp.Ne => Math.Abs(a - b) >= 1e-9, Cmp.Lt => a < b, Cmp.Le => a <= b + 1e-9,
                Cmp.Gt => a > b, Cmp.Ge => a >= b - 1e-9, Cmp.IsTrue => a != 0, _ => a == 0,
            };
            if (all && !r) return false;
            if (!all && r) return true;
        }
        return all;
    }

    private double ResolveM(Mover m, CValue v)
    {
        switch (v.Source)
        {
            case ValueSource.Const: return v.Real;
            case ValueSource.PartData:
                var p = FirstPart(m);
                return p != null && v.Index > 0 && v.Index < Part.MaxData ? p.Data[v.Index] : 0;
            case ValueSource.System: return v.Index == 1 ? m.Number : 0;
            default: return 0;
        }
    }

    // ------------------------------------------------------------------ routage

    private void StartRoute(Mover m, Instr ins)
    {
        int group = ins.Args[0], op = ins.Args[1], slot = ins.Args[2];
        // 1. Redirection vers le rebut
        var cg = _c.Machine.Groups[group - 1];
        if (_c.Machine.RejectGroup > 0 && cg.Role != GroupRole.Reject && m.Slots.Any(p => p.Present && VerdictIndex > 0 && p.Data[VerdictIndex] == 2) && !m.Slots.Any(p => p.Present && p.BadHandled))
        {
            group = _c.Machine.RejectGroup; op = 1; cg = _c.Machine.Groups[group - 1];
            Array.Clear(m.OpParams, 0, 8);
        }
        else
        {
            for (int k = 0; k < 8; k++) m.OpParams[k] = k < ins.Params.Length ? ResolveM(m, ins.Params[k]) : 0;
        }
        m.TargetGroup = group; m.TargetOp = op; m.TargetSlot = slot;
        TryRoute(m);
    }

    private void TryRoute(Mover m)
    {
        var cg = _c.Machine.Groups[m.TargetGroup - 1];
        // Fin d'OF : plus de chargement
        if (cg.Role == GroupRole.Entry && IsLoadingLocked(out var why)) { WaitInBuffer(m, why); return; }
        var candidates = StationsOfGroup(m.TargetGroup).Where(StationRoutable).ToList();
        if (m.OutOfService) candidates.Clear();
        if (candidates.Count == 0)
        {
            if (m.NoStationSince < 0) m.NoStationSince = NowMs;
            bool rejectOrNone = cg.Role == GroupRole.Reject || _c.Machine.RejectGroup == 0;
            if (rejectOrNone && NowMs - m.NoStationSince > _c.Machine.NoStationDelayMs && !m.IsEmpty)
            {
                SendToPark(m, true);
                return;
            }
            WaitInBuffer(m, "Aucune station disponible pour " + cg.Name);
            return;
        }
        m.NoStationSince = -1;
        int best = candidates.OrderBy(i => LoadOf(i)).ThenBy(i => _lastReceived[i]).ThenBy(i => i).First();
        var cfg = _c.Machine.Stations[best]!;
        _lastReceived[best] = NowMs;
        m.Station = best + 1;
        FreePosition(m);
        int dest;
        if (cfg.WorkPos > 0 && _owner[cfg.WorkPos] == 0 && !StationHasQueue(best)) dest = cfg.WorkPos;
        else
        {
            dest = cfg.QueuePos.FirstOrDefault(q => _owner[q] == 0);
            if (dest == 0 && cfg.WorkPos > 0 && _owner[cfg.WorkPos] == 0) dest = cfg.WorkPos;
            if (dest == 0) { m.Station = 0; WaitInBuffer(m, "Station occupée : " + cfg.Name); return; }
        }
        MoveTo(m, dest, MvState.Routing);
    }

    private bool StationHasQueue(int stationIdx)
        => _movers.Any(x => x.Station == stationIdx + 1 && x.State == MvState.Queued);

    private int LoadOf(int stationIdx) => _movers.Count(x => x.Station == stationIdx + 1);

    private bool IsLoadingLocked(out string reason)
    {
        reason = string.Empty;
        if (LoadingLocked) { reason = LoadingLockedReason ?? "Chargement arrêté"; return true; }
        if (Order != null && (Order.State == OrderState.Suspended)) { reason = "OF suspendu"; return true; }
        if (Order != null && Order.Good + Order.InProcess >= Order.Quantity) { reason = "Fin d'OF"; return true; }
        if (Drain) { reason = "Fin des pièces"; return true; }
        return false;
    }

    private void WaitInBuffer(Mover m, string reason)
    {
        m.WaitReason = reason;
        m.Station = 0;
        // Déjà sur un tampon ou au parcage : on y reste.
        if (m.Pos > 0 && (_c.Machine.PlanarPositions[m.Pos - 1].Kind == PlanarPositionKind.Buffer || _c.Machine.PlanarPositions[m.Pos - 1].Kind == PlanarPositionKind.Park))
        {
            m.State = MvState.Buffered; return;
        }
        int best = 0; double bestD = double.MaxValue;
        for (int i = 1; i <= _c.Machine.PlanarPositions.Count; i++)
        {
            var p = _c.Machine.PlanarPositions[i - 1];
            if (p.Kind != PlanarPositionKind.Buffer || _owner[i] != 0) continue;
            double d = Math.Abs(p.X - m.X) + Math.Abs(p.Y - m.Y);
            if (d < bestD) { bestD = d; best = i; }
        }
        if (best == 0) { m.State = MvState.Buffered; return; }
        MoveTo(m, best, MvState.Routing);
    }

    private void SendToPark(Mover m, bool toEmpty = false)
    {
        int park = 0;
        for (int i = 1; i <= _c.Machine.PlanarPositions.Count; i++)
            if (_c.Machine.PlanarPositions[i - 1].Kind == PlanarPositionKind.Park && (_owner[i] == 0 || _owner[i] == m.Number)) { park = i; break; }
        m.Station = 0;
        if (toEmpty)
        {
            Alarms.Raise(0, null, 911, m.Number, "Mover " + m.Number + " à vider", AlarmSeverity.Warning, NowMs, "Mover " + m.Number);
            m.WaitReason = "Mover à vider";
        }
        if (park == 0 || m.Pos == park)
        {
            m.State = toEmpty ? MvState.ToEmpty : MvState.OutOfService;
            return;
        }
        FreePosition(m);
        MoveTo(m, park, MvState.Routing);
        m.ArrivedAtPos = toEmpty ? 1 : 2;
    }

    private void FreePosition(Mover m)
    {
        if (m.Pos > 0 && _owner[m.Pos] == m.Number) _owner[m.Pos] = 0;
        m.Pos = 0;
    }

    private void MoveTo(Mover m, int pos, MvState state)
    {
        var p = _c.Machine.PlanarPositions[pos - 1];
        if (m.Pos > 0 && _owner[m.Pos] == m.Number) _owner[m.Pos] = 0;
        _owner[pos] = m.Number;
        m.Dest = pos; m.Pos = 0;
        m.FromX = m.X; m.FromY = m.Y; m.ToX = p.X; m.ToY = p.Y;
        m.MoveStartMs = NowMs;
        double dist = Math.Sqrt((m.ToX - m.X) * (m.ToX - m.X) + (m.ToY - m.Y) * (m.ToY - m.Y));
        m.MoveDurMs = Math.Max(50, MoveTimeMs(dist));
        m.Moving = true;
        m.State = state;
        m.ArrivedAtPos = 0;
    }

    private double PlanarSpeed => _planarSpeed > 0 ? _planarSpeed : (_planarSpeed = PlanarParam(1, 1000));

    private double _planarSpeed, _planarAcc;

    private double PlanarParam(int no, double def)
    {
        var o = _c.Machine.Objects.FirstOrDefault(x => x.Type == 13);
        return o != null && o.Params[no - 1] > 0 ? o.Params[no - 1] : def;
    }

    private double MoveTimeMs(double dist)
    {
        double v = PlanarSpeed;
        double a = _planarAcc > 0 ? _planarAcc : (_planarAcc = PlanarParam(2, 5000));
        double dAcc = v * v / a;
        if (dist >= dAcc) return ((dist - dAcc) / v + 2 * v / a) * 1000;
        return 2 * Math.Sqrt(dist / a) * 1000;
    }

    // ------------------------------------------------------------------ mouvements

    private void MoveMovers(double dtMs)
    {
        double factor = Math.Max(0, OverridePercent) / 100.0;
        foreach (var m in _movers)
        {
            if (!m.Moving) continue;
            if (Pack.State == PackMlState.Aborted) continue;
            m.MoveStartMs += dtMs * (1 - factor); // l'override ralentit le déplacement
            double t = (NowMs - m.MoveStartMs) / m.MoveDurMs;
            if (t >= 1) { Arrive(m); continue; }
            double e = t * t * (3 - 2 * t);
            m.X = m.FromX + (m.ToX - m.FromX) * e;
            m.Y = m.FromY + (m.ToY - m.FromY) * e;
        }
    }

    private void Arrive(Mover m)
    {
        m.Moving = false;
        m.X = m.ToX; m.Y = m.ToY;
        m.Pos = m.Dest;
        var kind = _c.Machine.PlanarPositions[m.Pos - 1].Kind;
        m.ArrivalMs = NowMs;
        switch (kind)
        {
            case PlanarPositionKind.Work: m.State = MvState.AtStation; m.RequestPosted = false; break;
            case PlanarPositionKind.Queue: m.State = MvState.Queued; break;
            case PlanarPositionKind.Buffer: m.State = MvState.Buffered; break;
            case PlanarPositionKind.Park:
                m.State = m.ArrivedAtPos == 1 ? MvState.ToEmpty : (m.IsEmpty ? MvState.OutOfService : MvState.ToEmpty);
                if (m.OutOfService && !m.IsEmpty && m.OutImmediate) m.State = MvState.ToEmpty;
                break;
        }
    }

    private void BufferedMover(Mover m)
    {
        // Réessayer le routage à chaque cycle
        if (m.TargetGroup == 0) { m.State = MvState.Ready; return; }
        TryRoute(m);
    }

    private void QueuedMover(Mover m)
    {
        int idx = m.Station - 1;
        if (idx < 0 || idx >= _stations.Count) { m.State = MvState.Ready; return; }
        var cfg = _c.Machine.Stations[idx]!;
        if (!StationRoutable(idx))
        {
            // La station n'est plus disponible : le mover est réaffecté.
            m.State = MvState.Ready; m.Station = 0; return;
        }
        if (_owner[cfg.WorkPos] != 0) return;
        // À son tour : priorité de la fonction visée, puis ordre d'arrivée
        var first = _movers.Where(x => x.State == MvState.Queued && x.Station == m.Station)
            .OrderByDescending(x => _c.Machine.Groups[x.TargetGroup - 1].Priority).ThenBy(x => x.ArrivalMs).First();
        if (first != m) return;
        MoveTo(m, cfg.WorkPos, MvState.Routing);
    }

    private void AtStationMover(Mover m)
    {
        int idx = m.Station - 1;
        var st = _stations[idx];
        if (m.StationAborted)
        {
            // 6.4 : le mover reste en position de travail jusqu'à la fin du Clearing, puis part au rebut.
            if (st.State == PackMlState.Stopped || st.State == PackMlState.Idle || st.State == PackMlState.Execute)
            {
                // Le mover est encore à la position de travail (elle se libère quand il repart) ; il est libre pour un nouveau routage.
                m.StationAborted = false; m.Station = 0; m.State = MvState.Ready; m.RequestPosted = false;
            }
            return;
        }
        if (!m.RequestPosted)
        {
            if (st.State != PackMlState.Execute || !StationRoutable(idx)) return;
            var req = new OpRequest { ReqId = (uint)NextReqId(), Group = m.TargetGroup, Operation = m.TargetOp, Mover = m.Number, Slot = m.TargetSlot, Parts = m.Slots };
            Array.Copy(m.OpParams, req.Params, 8);
            if (st.Response.Status != OpStatus.Idle) return;
            if (st.Offer(req))
            {
                m.RequestPosted = true; _opStart[m.Number] = NowMs;
                foreach (var p in m.Slots) p.BadAtArrival = p.Present && VerdictIndex > 0 && p.Data[VerdictIndex] == 2;
            }
            return;
        }
        var resp = st.Response;
        if (resp.ReqId != st.LastRequestId) return;
        switch (resp.Status)
        {
            case OpStatus.Done:
            case OpStatus.Failed: FinishOperation(m, st, resp); break;
            case OpStatus.Aborted:
                foreach (var p in m.Slots.Where(p => p.Present)) { if (VerdictIndex > 0) p.Data[VerdictIndex] = 2; p.DefectBad = true; }
                st.AcknowledgeResponse();
                m.StationAborted = true;
                break;
        }
    }

    private readonly Dictionary<int, double> _opStart = new Dictionary<int, double>();

    // ------------------------------------------------------------------ fin d'opération (docs/05 § 5.5)

    private void FinishOperation(Mover m, StationSim st, OpResponse resp)
    {
        bool failed = resp.Status == OpStatus.Failed;
        var group = _c.Machine.Groups[m.TargetGroup - 1];
        double started = _opStart.TryGetValue(m.Number, out var s0) ? s0 : NowMs;
        bool removed = st.TakeRemovedFlag();
        st.AcknowledgeResponse();

        // 1. Libérer la position de travail
        if (m.Pos > 0 && _owner[m.Pos] == m.Number) _owner[m.Pos] = 0;
        m.Pos = 0; m.Station = 0; m.RequestPosted = false;

        var targets = m.TargetSlot == 0 ? Enumerable.Range(0, m.Slots.Length).ToList() : new List<int> { m.TargetSlot - 1 };
        int stationIdx = StationIndex(st.Number);
        foreach (int si in targets)
        {
            if (si < 0 || si >= m.Slots.Length) continue;
            var part = m.Slots[si];
            // 2. Création des pièces par une opération d'entrée réussie
            if (group.Role == GroupRole.Entry && !failed && !part.Present)
            {
                part.Present = true;
                if (string.IsNullOrEmpty(part.Id)) part.Id = _partPrefix + "-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + (++_partCounter).ToString("000000");
                else ++_partCounter;
                part.Status = PartStatus.InProcess; part.CreatedMs = NowMs; part.Order = Order != null ? 1 : 0;
                if (Order != null) { Order.Created++; Order.InProcess++; }
            }
            if (!part.Present) continue;
            if (failed && VerdictIndex > 0) part.Data[VerdictIndex] = 2;
            int verdict = VerdictIndex > 0 ? (int)part.Data[VerdictIndex] : 0;
            part.Ops.Add(new PartOp(st.Number, m.TargetGroup, verdict, started, NowMs, m.TargetOp));
            bool bad = verdict == 2;
            // Surveillance des rebuts (docs/10 § 9) : un verdict déjà mauvais à l'arrivée ne compte pas pour cette station.
            if (!part.BadAtArrival)
            {
                Quality.Record(QualityScope.Station, stationIdx, bad);
                Quality.Record(QualityScope.Group, m.TargetGroup, bad);
                uint impute = _c.Machine.GroupImputeMask.ElementAtOrDefault(m.TargetGroup - 1);
                if (impute != 0)
                    foreach (var op0 in part.Ops.Take(part.Ops.Count - 1))
                        if ((impute & (1u << (op0.Group - 1))) != 0)
                            Quality.Record(QualityScope.StationImputed, StationIndex(op0.Station), bad);
            }
            bool closed = false, closedBad = false;
            // 4. Pièce retirée par l'opération (rebut interne)
            if (part.Status == PartStatus.Removed || (removed && part.Status == PartStatus.Removed))
            {
                closed = true; closedBad = true;
                CloseOrderPart(part, true); PartsRejected++;
            }
            // 3. Sortie ou rebut : clôture
            else if (group.Role == GroupRole.Exit && !failed) { closed = true; part.Status = PartStatus.Exited; PartsExited++; CloseOrderPart(part, false); }
            else if (group.Role == GroupRole.Reject) { closed = true; closedBad = true; part.Status = PartStatus.Rejected; PartsRejected++; CloseOrderPart(part, true); }
            if (closed && !part.DefectBad)
            {
                Quality.Record(QualityScope.Mover, m.Number, closedBad || bad);
                Quality.Record(QualityScope.Machine, 0, closedBad || bad);
            }
            AddTrace(m, si, st, m.TargetGroup, m.TargetOp, part, started, verdict, resp.ErrorCode);
            OperationFinished?.Invoke(new OperationResult(stationIdx, st.Number, m.TargetGroup, m.Number, part.Id, bad, closed, closedBad, NowMs - started, resp.ErrorCode));
            if (closed)
            {
                PartClosed?.Invoke(new ClosedPart(part, m.Number, si + 1, NowMs, Order?.Number, Order?.Reference));
                part.Clear();
            }
        }

        // 7. Avancer le PC (le mover vide d'une opération de sortie ou de rebut repart au début du process)
        bool leavesEmpty = m.IsEmpty && (group.Role == GroupRole.Exit || group.Role == GroupRole.Reject);
        m.PC = leavesEmpty ? 1 : m.PC + 1;
        if (leavesEmpty) { m.Table = 1; m.Depth = 0; }
        m.State = MvState.Ready;
        m.TargetGroup = 0;
        CheckOrderCompletion();
    }

    private void AddTrace(Mover m, int slot, StationSim st, int group, int op, Part part, double started, int verdict, int error)
    {
        var t = new SimTrace { Seq = ++_traceSeq, PartId = part.Id, Mover = m.Number, Slot = slot + 1, Station = st.Number, Group = group, Operation = op, StartMs = started, EndMs = NowMs, Verdict = verdict, ErrorCode = error };
        Array.Copy(part.Data, t.Data, Part.MaxData);
        Traces.Add(t);
        if (Traces.Count > 256) Traces.RemoveAt(0);
    }

    private void CloseOrderPart(Part part, bool bad)
    {
        if (Order == null) return;
        Order.InProcess = Math.Max(0, Order.InProcess - 1);
        if (bad) Order.Bad++; else Order.Good++;
    }

    private void CheckOrderCompletion()
    {
        if (Order == null || Order.State != OrderState.Running) return;
        if (Order.Good + Order.InProcess >= Order.Quantity && Order.InProcess == 0)
        {
            Order.State = OrderState.Finished; Order.FinishedMs = NowMs;
            OrderHistory.Add(Order);
            StopCause = "Fin d'OF";
            EventRaised?.Invoke("OF terminé : " + Order.Number);
            Command(StateCommand.Stop);
        }
    }

    private void PostStationCycle()
    {
        // Les alarmes d'objet en erreur deviennent des défauts de station ; la balise est calculée à la publication.
        foreach (var s in _stations)
        {
            if (s.State == PackMlState.Aborted && StopCause == null) StopCause = s.FaultText ?? "Défaut de station " + s.Name;
        }
    }
}
