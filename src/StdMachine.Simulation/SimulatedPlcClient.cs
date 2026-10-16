using System.Diagnostics;
using StdMachine.Core;
using StdMachine.Core.Catalog;
using StdMachine.Core.Compiler;
using StdMachine.Core.Validation;
using StdMachine.Simulation.Engine;

namespace StdMachine.Simulation;

/// <summary>Commandes d'essai du simulateur au-delà de <see cref="ISimulatorControl"/> (docs/06 § 7, docs/12 § 5).</summary>
public interface ISimulatorTools
{
    void InjectObjectError(int station, Guid objectId, int errorCode, int subCode = 0);
    void SetInput(int station, Guid objectId, int signal, double value);
    void SetFailureRate(int objectType, double rate);
    void Reset(int? seed = null);
}

/// <summary>
/// Client PLC simulé (docs/06 § 7–8) : un <see cref="MachineSim"/> piloté par le temps réel (échelle réglable), qui publie des instantanés
/// à 10 Hz au plus. Il exécute le projet compilé ; il ne contient aucune logique propre à une machine.
/// </summary>
public sealed class SimulatedPlcClient : IPlcClient, IPartEventSource, ISimulatorControl, ISimulatorTools
{
    private readonly object _gate = new object();
    private readonly CatalogDocument _catalog;
    private MachineProject _project;
    private CompiledTables _compiled;
    private MachineSim _sim;
    private Thread? _thread;
    private volatile bool _stop;
    private PlcSnapshot _snapshot;
    private DateTime _epoch = DateTime.Now;
    private DateTime _lastPublish;
    private int _seed;
    private DateTime? _deployedAt;
    private readonly Dictionary<uint, Guid> _stepGuid = new Dictionary<uint, Guid>();
    private readonly Dictionary<Guid, ProcessDefinition> _processByGuid = new Dictionary<Guid, ProcessDefinition>();
    private readonly Dictionary<int, StepTracker> _trackers = new Dictionary<int, StepTracker>();
    private readonly List<ForceEntry> _forceEntries = new List<ForceEntry>();
    private readonly Dictionary<string, double> _stateSince = new Dictionary<string, double>();
    private double _stateSinceMachine;
    private PackMlState _lastMachineState = PackMlState.Stopped;
    private readonly Dictionary<int, PackMlState> _lastStationState = new Dictionary<int, PackMlState>();
    private readonly Dictionary<int, double> _stationSince = new Dictionary<int, double>();
    private string? _declaredBy;
    private DateTime? _declaredSince, _declaredUntil;
    private readonly List<AlarmKey> _acknowledged = new List<AlarmKey>();
    private readonly HashSet<int> _ackedAlarmIds = new HashSet<int>();

    public SimulatedPlcClient(MachineProject project, CatalogDocument catalog, int seed = 1)
    {
        _catalog = catalog; _seed = seed; _project = project;
        var r = ProjectCompiler.Compile(project, catalog);
        if (!r.Succeeded) throw new InvalidOperationException("Le projet ne se compile pas : " + string.Join(" ; ", r.Issues.Where(i => i.Severity == Severity.Error).Select(i => i.Code + " " + i.Message)));
        _compiled = r.Project!;
        _sim = new MachineSim(_compiled, catalog, seed);
        BindSim();
        _snapshot = BuildSnapshot();
    }

    public MachineProject Project => _project;

    public CompiledTables Compiled => _compiled;

    /// <summary>Accès direct au moteur pour les tests et les scénarios de capture.</summary>
    public MachineSim Sim => _sim;

    public double TimeScale { get; set; } = 1.0;

    public PlcConnectionState ConnectionState => PlcConnectionState.Simulation;

    public event EventHandler<PlcConnectionState>? ConnectionStateChanged { add { } remove { } }

    public PlcSnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                if (_dirty) { _snapshot = BuildSnapshot(); _dirty = false; }
                return _snapshot;
            }
        }
    }

    // Une commande a modifié l'état : le prochain accès à l'instantané le reconstruit sans attendre la publication cyclique.
    private bool _dirty;

    public event EventHandler<PlcSnapshot>? SnapshotUpdated;

    public event EventHandler<PartRecord>? PartClosed;

    // ------------------------------------------------------------------ cycle de vie

    public Task ConnectAsync(CancellationToken ct)
    {
        if (_thread == null)
        {
            _stop = false;
            _thread = new Thread(Loop) { IsBackground = true, Name = "SimPlc" };
            _thread.Start();
        }
        return Task.FromResult(0);
    }

    public Task DisposeAsync()
    {
        _stop = true;
        _thread?.Join(500);
        _thread = null;
        return Task.FromResult(0);
    }

    private void Loop()
    {
        var sw = Stopwatch.StartNew();
        double simDebt = 0;
        double last = 0;
        while (!_stop)
        {
            double now = sw.Elapsed.TotalMilliseconds;
            simDebt += (now - last) * TimeScale;
            last = now;
            int steps = 0;
            lock (_gate)
            {
                while (simDebt >= 10 && steps < 40) { _sim.Step(10); simDebt -= 10; steps++; Observe(); }
                if (simDebt > 400) simDebt = 0; // retard rattrapé : on abandonne le surplus
            }
            if ((DateTime.UtcNow - _lastPublish).TotalMilliseconds >= 100) Publish();
            Thread.Sleep(5);
        }
    }

    /// <summary>Un pas de simulation sans passer par le temps réel (tests, captures) : avance de <paramref name="ms"/> et publie.</summary>
    public void Advance(double ms)
    {
        lock (_gate)
        {
            for (double t = 0; t < ms; t += 10) { _sim.Step(10); Observe(); }
            _snapshot = BuildSnapshot();
        }
    }

    private void Publish()
    {
        PlcSnapshot s;
        lock (_gate) { _snapshot = BuildSnapshot(); _dirty = false; s = _snapshot; _lastPublish = DateTime.UtcNow; }
        SnapshotUpdated?.Invoke(this, s);
    }

    private void BindSim()
    {
        _stepGuid.Clear(); _processByGuid.Clear(); _trackers.Clear();
        foreach (var m in _compiled.Map) _stepGuid[m.StepId] = m.StepGuid;
        foreach (var st in _project.Stations)
            foreach (var p in st.Processes) _processByGuid[p.Id] = p;
        _processByGuid[_project.GeneralProcess.Id] = _project.GeneralProcess;
        foreach (var s in _sim.Stations)
        {
            var tracker = new StepTracker();
            _trackers[s.Number] = tracker;
            int number = s.Number;
            s.StepEvent += (st, id, begin) => tracker.On(id, begin);
        }
        _sim.PartClosed += OnPartClosed;
        _lastStationState.Clear(); _stationSince.Clear(); _lastMachineState = PackMlState.Stopped; _stateSinceMachine = 0;
        _epoch = DateTime.Now;
    }

    private static string OperationTable(StationSim s)
    {
        var req = s.CurrentRequest;
        var map = req == null ? null : s.Data.OpMap.FirstOrDefault(m => m.Group == req.Group && m.OperationNo == req.Operation);
        int table = s.Contexts[1].Table > 0 ? s.Contexts[1].Table : map?.Table ?? 0;
        return table > 0 ? s.Data.Tables[table - 1].Name : string.Empty;
    }

    private void OnPartClosed(MachineSim.ClosedPart c)
    {
        var part = c.Part;
        var ops = new List<PartOpRecord>();
        for (int i = 0; i < part.Ops.Count; i++)
        {
            var o = part.Ops[i];
            var st = _sim.StationByNumber(o.Station);
            var map = st?.Data.OpMap.FirstOrDefault(m => m.Group == o.Group && m.OperationNo == o.Operation);
            string op = map != null && st != null ? st.Data.Tables[map.Table - 1].Name : "Opération " + o.Operation;
            var data = new Dictionary<string, double>();
            if (i == part.Ops.Count - 1)
                for (int k = 0; k < _compiled.Machine.PartData.Count; k++)
                {
                    var pd = _compiled.Machine.PartData[k];
                    if (!pd.Reserved && part.Data[k + 1] != 0) data[pd.Name] = part.Data[k + 1];
                }
            ops.Add(new PartOpRecord(_compiled.Machine.Groups[o.Group - 1].Name, op, st?.Name ?? "Station " + o.Station, At(o.StartMs), At(o.EndMs),
                o.VerdictAfter == 1 ? Verdict.Good : o.VerdictAfter == 2 ? Verdict.Bad : Verdict.Undefined, data));
        }
        var state = part.Status == PartStatus.Exited ? PartState.Exited : part.Status == PartStatus.Rejected ? PartState.Rejected : PartState.Removed;
        var rec = new PartRecord(part.Id, c.Order, c.Reference, At(part.CreatedMs), At(c.ClosedMs), state, VerdictOf(part), c.Mover, c.Slot, ops);
        PartClosed?.Invoke(this, rec);
    }

    private void Observe()
    {
        if (_sim.Pack.State != _lastMachineState) { _lastMachineState = _sim.Pack.State; _stateSinceMachine = _sim.NowMs; }
        foreach (var s in _sim.Stations)
        {
            if (!_lastStationState.TryGetValue(s.Number, out var prev) || prev != s.State) { _lastStationState[s.Number] = s.State; _stationSince[s.Number] = _sim.NowMs; }
            var tr = _trackers[s.Number];
            if (s.OperationRunning && !tr.Active) tr.Begin(OperationTable(s), _sim.NowMs);
            else if (!s.OperationRunning && tr.Active) tr.End(_sim.NowMs, s.State == PackMlState.Aborted);
            tr.Now = _sim.NowMs;
        }
    }

    // ------------------------------------------------------------------ commandes

    private CommandResult Result(Engine.MachineSim.CommandOutcome o) { _dirty = true; return ToResult(o); }

    private static CommandResult ToResult(Engine.MachineSim.CommandOutcome o) => o.Accepted ? CommandResult.Ok : CommandResult.Refused(o.Reason ?? "Refusé");

    public Task<CommandResult> SendCommandAsync(StateCommand command, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(Result(_sim.Command(command)));
    }

    public Task<CommandResult> SendStationCommandAsync(int station, StateCommand command, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(Result(_sim.StationCommand(station, command)));
    }

    public Task<CommandResult> SetModeAsync(MachineMode mode, CancellationToken ct)
    {
        lock (_gate) return Task.FromResult(Result(_sim.SetMode(mode)));
    }

    public Task<CommandResult> SetOverrideAsync(OverrideTarget target, double percent, bool manual, CancellationToken ct)
    {
        percent = Math.Max(0, Math.Min(100, percent));
        lock (_gate)
        {
            switch (target.Level)
            {
                case OverrideLevel.Machine: _sim.OverridePercent = percent; break;
                case OverrideLevel.Station: { var s = _sim.StationByNumber(target.Station); if (s == null) return Task.FromResult(CommandResult.Refused("Station inconnue.")); s.OverridePercent = percent; break; }
                default:
                    {
                        var o = FindObject(target.Station, target.ObjectId ?? Guid.Empty);
                        if (o == null) return Task.FromResult(CommandResult.Refused("Objet inconnu."));
                        o.Cfg.Override = percent;
                        break;
                    }
            }
        }
        return Task.FromResult(CommandResult.Ok);
    }

    private SimObject? FindObject(int station, Guid id)
    {
        var s = _sim.StationByNumber(station);
        return s?.Objects.FirstOrDefault(o => o.Cfg.Id == id);
    }

    public async Task<ManualResult> ExecuteManualAsync(ManualActionRequest request, CancellationToken ct)
    {
        SimObject obj; StationSim st;
        lock (_gate)
        {
            st = _sim.StationByNumber(request.Station)!;
            if (st == null) return new ManualResult(false, "Station inconnue.");
            if (!st.ManualAllowed(out var why)) return new ManualResult(false, why);
            obj = FindObject(request.Station, request.ObjectId)!;
            if (obj == null) return new ManualResult(false, "Objet inconnu.");
        }
        var def = _catalog.Type(obj.TypeCode)?.Actions.FirstOrDefault(a => a.Name == request.Action);
        if (def == null) return await ExecuteFunctionAsync(request, st, obj, ct).ConfigureAwait(false);
        lock (_gate)
        {
            var r = st.ManualStart(obj.LocalId, def.No, ManualParams(def, request.Args));
            if (r != StartResult.Ok) return new ManualResult(false, RefusalText(r));
        }
        return await WaitActionAsync(obj, def, ct).ConfigureAwait(false);
    }

    /// <summary>Raison d'un refus de démarrage d'action, en français pour l'opérateur.</summary>
    private static string RefusalText(StartResult r) => r switch
    {
        StartResult.NotInitialized => "Objet non initialisé : lancez d'abord « Référencer » (ou « Power on / Init »).",
        StartResult.Busy => "Objet occupé : attendez la fin de l'action en cours.",
        StartResult.Disabled => "Objet désactivé.",
        StartResult.NotConfigured => "Objet non configuré.",
        StartResult.InvalidParam => "Paramètre invalide.",
        StartResult.Interlocked => "Verrouillage actif.",
        StartResult.InError => "Objet en erreur : acquittez d'abord l'erreur.",
        StartResult.UnknownAction => "Action inconnue pour ce type.",
        StartResult.ManualOnly => "Action réservée au mode manuel.",
        _ => "Refusée : " + r,
    };

    /// <summary>Attend la fin d'une action déjà démarrée sur l'objet et en rend le résultat.</summary>
    private async Task<ManualResult> WaitActionAsync(SimObject obj, CatalogAction def, CancellationToken ct)
    {
        double waited = 0;
        while (waited < 60_000)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (obj.Status == ActStatus.Done || obj.Status == ActStatus.Error || obj.State == ObjState.Error)
                {
                    bool ok = obj.Status == ActStatus.Done;
                    var results = new List<ConditionTermRuntime>();
                    for (int i = 0; i < def.Results.Count; i++) results.Add(new ConditionTermRuntime(def.Results[i].Name + " : " + DisplayFormat.Number(obj.Results.Real[i]), ok));
                    string? err = ok ? null : obj.ErrorTextOf(obj.ErrorCode);
                    obj.AcknowledgeDone();
                    return new ManualResult(ok, err, results);
                }
                if (def.Immediate && obj.State == ObjState.Ready) { obj.AcknowledgeDone(); return new ManualResult(true, null, new List<ConditionTermRuntime>()); }
            }
            await Task.Delay(20, ct).ConfigureAwait(false);
            waited += 20 * TimeScale;
        }
        return new ManualResult(false, "Délai dépassé.");
    }

    /// <summary>
    /// Fonction de module en manuel (« Aller à », « Prendre pièce »…) : dépliée comme à la compilation (docs/06 § 5.2), puis ses actions de base
    /// sont lancées l'une après l'autre sur le canal manuel (les branches en parallèle ensemble). Les arguments sont ceux de la HMI,
    /// dans l'ordre des paramètres de la fonction : une position occupe quatre valeurs (X, Y, Z, C).
    /// </summary>
    private async Task<ManualResult> ExecuteFunctionAsync(ManualActionRequest request, StationSim st, SimObject obj, CancellationToken ct)
    {
        IReadOnlyList<Step> steps;
        var literals = new Dictionary<Guid, Pos>();
        lock (_gate)
        {
            var module = _catalog.Modules.FirstOrDefault(m => m.Code == obj.TypeCode);
            var fn = module?.Functions.FirstOrDefault(f => f.Name == request.Action || f.Id == request.Action);
            if (fn == null) return new ManualResult(false, "Action inconnue : " + request.Action);
            var node = _project.Stations.First(s => s.Number == request.Station).FindObject(obj.Cfg.Id)!;
            var call = new FunctionStep { ModuleObjectId = node.Id, FunctionId = fn.Id };
            int ai = 0;
            foreach (var prm in fn.Params)
            {
                if (ai >= request.Args.Count) break; // le reste prend la valeur par défaut du catalogue
                if (prm.Type == "Position")
                {
                    if (ai + 3 >= request.Args.Count) return new ManualResult(false, "Position incomplète pour « " + prm.Name + " ».");
                    var id = Guid.NewGuid();
                    literals[id] = new Pos(request.Args[ai], request.Args[ai + 1], request.Args[ai + 2], request.Args[ai + 3]);
                    call.Args.Add(ValueRef.NamedPos(id));
                    ai += 4;
                }
                else if (prm.Type == "Speed") call.Args.Add(ValueRef.Speed((int)request.Args[ai++]));
                else call.Args.Add(ValueRef.Const(request.Args[ai++]));
            }
            try { steps = ProjectCompiler.ExpandModuleFunction(_catalog, node, call); }
            catch (CompileException ex) { return new ManualResult(false, ex.Message); }
        }
        return await RunStepsAsync(st, request.Station, steps, literals, ct).ConfigureAwait(false);
    }

    private async Task<ManualResult> RunStepsAsync(StationSim st, int station, IReadOnlyList<Step> steps, Dictionary<Guid, Pos> literals, CancellationToken ct)
    {
        foreach (var step in steps)
        {
            ManualResult r;
            switch (step)
            {
                case ActionStep a: r = await RunActionStepAsync(st, station, a, literals, ct).ConfigureAwait(false); break;
                case ParallelBlock block:
                    var all = await Task.WhenAll(block.Branches.Select(b => RunStepsAsync(st, station, b.Steps, literals, ct))).ConfigureAwait(false);
                    r = all.FirstOrDefault(x => !x.Accepted) ?? new ManualResult(true, null);
                    break;
                default: r = new ManualResult(false, "Étape non prise en charge en mode manuel."); break;
            }
            if (!r.Accepted) return r;
        }
        return new ManualResult(true, null, new List<ConditionTermRuntime>());
    }

    private async Task<ManualResult> RunActionStepAsync(StationSim st, int station, ActionStep a, Dictionary<Guid, Pos> literals, CancellationToken ct)
    {
        SimObject obj; CatalogAction def;
        lock (_gate)
        {
            obj = FindObject(station, a.ObjectId)!;
            if (obj == null) return new ManualResult(false, "Objet inconnu.");
            def = _catalog.Type(obj.TypeCode)?.Action(a.ActionNo)!;
            if (def == null) return new ManualResult(false, "Action inconnue pour " + obj.TypeCode + ".");
            var r = st.ManualStart(obj.LocalId, def.No, StepParams(station, a, literals));
            if (r != StartResult.Ok) return new ManualResult(false, RefusalText(r));
        }
        return await WaitActionAsync(obj, def, ct).ConfigureAwait(false);
    }

    /// <summary>Paramètres d'une action de base déduite d'une fonction : constantes, vitesses, positions de la HMI ou positions nommées apprises.</summary>
    private ActParams StepParams(int station, ActionStep a, Dictionary<Guid, Pos> literals)
    {
        var p = new ActParams();
        var cfg = _project.Stations.First(s => s.Number == station);
        for (int k = 0; k < a.Args.Count && k < 8; k++)
        {
            var v = a.Args[k];
            switch (v.Kind)
            {
                case ValueKind.NamedPos:
                    {
                        Pos pos;
                        if (v.Ref is Guid g && literals.TryGetValue(g, out var lit)) pos = lit;
                        else
                        {
                            var np = cfg.Positions.FirstOrDefault(x => x.Id == v.Ref);
                            if (np == null) break;
                            pos = new Pos(np.X, np.Y, np.Z, np.C, np.Frame);
                        }
                        p.Kind[k] = ParamKind.Position; p.Pos = pos; p.HasPos = true; p.Real[k] = v.Offset;
                        break;
                    }
                case ValueKind.Speed:
                    if (v.Index > 0) { p.Kind[k] = ParamKind.SpeedIndex; p.Real[k] = v.Index; }
                    else { p.Kind[k] = ParamKind.SpeedPercent; p.Real[k] = v.Number; }
                    break;
                case ValueKind.Const:
                    p.Kind[k] = ParamKind.Real; p.Real[k] = v.Number + v.Offset;
                    break;
            }
        }
        return p;
    }

    private static ActParams ManualParams(CatalogAction def, IReadOnlyList<double> args)
    {
        var p = new ActParams();
        int ai = 0;
        for (int k = 0; k < def.Params.Count && k < 8; k++)
        {
            var d = def.Params[k];
            if (d.Type == "Position" && ai + 3 < args.Count) { p.Kind[k] = ParamKind.Position; p.Pos = new Pos(args[ai], args[ai + 1], args[ai + 2], args[ai + 3]); p.HasPos = true; ai += 4; }
            else if (ai < args.Count)
            {
                p.Kind[k] = d.Type == "Speed" ? ParamKind.SpeedPercent : ParamKind.Real;
                p.Real[k] = args[ai++];
            }
        }
        return p;
    }

    private sealed class JogSession : IJogSession
    {
        private readonly Action _stop;
        public JogSession(Action stop) { _stop = stop; }
        public void Dispose() => _stop();
    }

    public IJogSession StartJog(JogRequest request)
    {
        lock (_gate)
        {
            var st = _sim.StationByNumber(request.Station);
            var obj = FindObject(request.Station, request.ObjectId);
            if (st != null && obj != null && st.ManualAllowed(out _))
            {
                var p = new ActParams();
                p.Kind[0] = ParamKind.SpeedPercent; p.Real[0] = request.SpeedPercent;
                st.ManualStart(obj.LocalId, request.Positive ? 9 : 10, p);
                return new JogSession(() => { lock (_gate) st.ManualAbort(obj.LocalId); });
            }
        }
        return new JogSession(() => { });
    }

    public Task<TeachResult> TeachPositionAsync(TeachRequest request, CancellationToken ct)
    {
        lock (_gate)
        {
            var st = _sim.StationByNumber(request.Station);
            if (st == null) return Task.FromResult(new TeachResult(false, "Station inconnue.", 0, 0, 0, 0));
            if (!st.ManualAllowed(out var why)) return Task.FromResult(new TeachResult(false, why, 0, 0, 0, 0));
            // Chaque axe du robot fournit sa composante (rôle AxisX, AxisY, AxisZ, AxisC du module).
            int Axis(string role) => st.Objects.FirstOrDefault(o => o.Parent != null && RoleOf(st, o) == role)?.LocalId ?? 0;
            var pos = st.ReadPosition(Axis("AxisX"), Axis("AxisY"), Axis("AxisZ"), Axis("AxisC"));
            st.StoreTaughtPosition(request.PositionId, pos);
            return Task.FromResult(new TeachResult(true, null, pos.X, pos.Y, pos.Z, pos.C));
        }
    }

    private string? RoleOf(StationSim st, SimObject o)
    {
        var node = _project.StationById(_project.Stations.First(s => s.Number == st.Number).Id)?.FindObject(o.Cfg.Id);
        return node?.ChildRole;
    }

    public Task<CommandResult> SetStepModeAsync(int station, StepGranularity? granularity, CancellationToken ct)
    {
        lock (_gate)
        {
            var st = _sim.StationByNumber(station);
            if (st == null) return Task.FromResult(CommandResult.Refused("Station inconnue."));
            if (st.Mode != MachineMode.Maintenance) return Task.FromResult(CommandResult.Refused("Le pas à pas demande le mode Maintenance."));
            st.SetStepMode(granularity);
            _dirty = true;
        }
        return Task.FromResult(CommandResult.Ok);
    }

    public Task<CommandResult> StepAsync(int station, CancellationToken ct)
    {
        lock (_gate)
        {
            var st = _sim.StationByNumber(station);
            if (st == null) return Task.FromResult(CommandResult.Refused("Station inconnue."));
            if (!st.StepEnabled) return Task.FromResult(CommandResult.Refused("Le pas à pas n'est pas actif."));
            st.Step();
            _dirty = true;
        }
        return Task.FromResult(CommandResult.Ok);
    }

    public Task<CommandResult> LaunchTableAsync(LaunchRequest request, CancellationToken ct)
    {
        lock (_gate)
        {
            var st = _sim.StationByNumber(request.Station);
            if (st == null) return Task.FromResult(CommandResult.Refused("Station inconnue."));
            int table = st.Data.Tables.FindIndex(t => t.ProcessId == request.ProcessId) + 1;
            if (table == 0) return Task.FromResult(CommandResult.Refused("Process inconnu."));
            _dirty = true;
            if (!st.Launch(table, request.Mover == null ? null : new OpRequest { Mover = request.Mover.Value, Slot = 1, Parts = new[] { new Part { Present = true, Id = "ESSAI" } } }))
                return Task.FromResult(CommandResult.Refused("Lancement refusé : la station doit être en Maintenance, Idle ou Execute, sans opération en cours."));
        }
        return Task.FromResult(CommandResult.Ok);
    }

    /// <summary>Déploie un nouveau projet sur le simulateur (machine à l'arrêt, docs/05 § 10) : compile, charge, repart à zéro.</summary>
    public CommandResult Deploy(MachineProject project)
    {
        lock (_gate)
        {
            if (_sim.Pack.State != PackMlState.Stopped) return CommandResult.Refused("Le déploiement demande la machine à l'arrêt (Stopped).");
            var r = ProjectCompiler.Compile(project, _catalog);
            if (!r.Succeeded) return CommandResult.Refused("Le projet ne se compile pas : " + string.Join(" ; ", r.Issues.Where(i => i.Severity == Severity.Error).Select(i => i.Code + " " + i.Message)));
            _project = project; _compiled = r.Project!;
            _sim = new MachineSim(_compiled, _catalog, _seed);
            BindSim();
            _deployedAt = DateTime.Now;
            _snapshot = BuildSnapshot();
        }
        return CommandResult.Ok;
    }

    public Task<DeployResult> DeployAsync(CompiledProject project, IProgress<DeployProgress>? progress, CancellationToken ct)
    {
        // Le substitut de la maquette ne porte que l'empreinte : on redéploie le projet courant.
        var r = Deploy(_project);
        progress?.Report(new DeployProgress("Machine", 1));
        return Task.FromResult(new DeployResult(r.Accepted, _compiled.Hash.Substring(0, 16).ToUpperInvariant(), DateTime.Now, r.Accepted ? new string[0] : new[] { r.Reason ?? "Refusé" }));
    }

    public Task<DeployedInfo> GetDeployedInfoAsync(CancellationToken ct)
        => Task.FromResult(new DeployedInfo(_compiled.Hash.Substring(0, 16).ToUpperInvariant(), _deployedAt ?? _epoch, "Simulateur"));

    public Task<CommandResult> StartOrderAsync(OrderStart order, CancellationToken ct)
    {
        lock (_gate) { _sim.StartOrder(order.Number, order.Reference, order.Quantity); _dirty = true; }
        return Task.FromResult(CommandResult.Ok);
    }

    public Task<CommandResult> OrderCommandAsync(OrderCommand command, CancellationToken ct)
    {
        lock (_gate) { _sim.OrderCommand(command); _dirty = true; }
        return Task.FromResult(CommandResult.Ok);
    }

    public Task<CommandResult> ConfirmPartsRemovedAsync(int mover, CancellationToken ct)
    {
        lock (_gate) { _sim.ConfirmPartsRemoved(mover); _dirty = true; }
        return Task.FromResult(CommandResult.Ok);
    }

    public Task<CommandResult> AcknowledgeAlarmAsync(AlarmKey? key, CancellationToken ct)
    {
        lock (_gate)
        {
            foreach (var a in _sim.Alarms.All.Where(a => key == null ? a.Severity == AlarmSeverity.Warning || a.Severity == AlarmSeverity.Information : a.Code == key.Code && a.Object == key.Object))
                _ackedAlarmIds.Add(a.Id);
            _dirty = true;
        }
        return Task.FromResult(CommandResult.Ok);
    }

    public Task<CommandResult> SetDeclaredStateAsync(DeclaredState state, string? reason, DateTime? until, CancellationToken ct)
    {
        lock (_gate)
        {
            _sim.Declared = state; _sim.DeclaredReason = reason;
            _dirty = true; _declaredSince = state == DeclaredState.None ? (DateTime?)null : DateTime.Now; _declaredUntil = until; _declaredBy = state == DeclaredState.None ? null : "Opérateur";
        }
        return Task.FromResult(CommandResult.Ok);
    }

    public Task<CommandResult> SetStationOutOfProductionAsync(int station, bool outOfProduction, string? reason, CancellationToken ct)
    {
        lock (_gate)
        {
            _dirty = true;
            _sim.SetStationOutOfProduction(station, outOfProduction);
            _forceEntries.RemoveAll(f => f.Kind == "Station" && f.Target == StationName(station));
            if (outOfProduction) _forceEntries.Add(new ForceEntry("Station", StationName(station), "Hors production", reason ?? string.Empty, "Opérateur", DateTime.Now, null));
        }
        return Task.FromResult(CommandResult.Ok);
    }

    public Task<CommandResult> SetMoverOutOfServiceAsync(int mover, bool outOfService, string? reason, CancellationToken ct)
    {
        lock (_gate)
        {
            _dirty = true;
            _sim.SetMoverOutOfService(mover, outOfService);
            _forceEntries.RemoveAll(f => f.Kind == "Mover" && f.Target == "Mover " + mover);
            if (outOfService) _forceEntries.Add(new ForceEntry("Mover", "Mover " + mover, "Hors service", reason ?? string.Empty, "Opérateur", DateTime.Now, null));
        }
        return Task.FromResult(CommandResult.Ok);
    }

    private string StationName(int number) => _sim.StationByNumber(number)?.Name ?? "Station " + number;

    public Task<CommandResult> ReleaseQualityAsync(QualityScope scope, string name, string reason, CancellationToken ct)
    {
        lock (_gate)
        {
            int index = scope switch
            {
                QualityScope.Station or QualityScope.StationImputed => _sim.Stations.ToList().FindIndex(s => s.Name == name),
                QualityScope.Group => _compiled.Machine.Groups.FindIndex(g => g.Name == name) + 1,
                QualityScope.Mover => int.TryParse(name.Replace("Mover", string.Empty).Trim(), out var n) ? n : -1,
                _ => 0,
            };
            _dirty = true;
            if (index < 0 || !_sim.Quality.Release(scope, index)) return Task.FromResult(CommandResult.Refused("Aucune réaction à libérer pour « " + name + " »."));
        }
        return Task.FromResult(CommandResult.Ok);
    }

    // ------------------------------------------------------------------ ISimulatorControl, ISimulatorTools

    public void ForceStationAbort(int station)
    {
        lock (_gate) _sim.StationByNumber(station)?.ForceAbort("Abort forcé (essai)");
    }

    public void InjectObjectError(int station, Guid objectId, int errorCode, int subCode = 0)
    {
        lock (_gate) FindObject(station, objectId)?.InjectError(errorCode, subCode);
    }

    public void SetInput(int station, Guid objectId, int signal, double value)
    {
        lock (_gate)
        {
            var o = FindObject(station, objectId);
            if (o != null) o.ForcedInputs[signal] = value;
        }
    }

    public void SetFailureRate(int objectType, double rate)
    {
        lock (_gate)
        {
            foreach (var s in _sim.Stations)
                foreach (var o in s.Objects.Where(o => o.Cfg.Type == objectType)) o.FailureRate = rate;
        }
    }

    public void Reset(int? seed = null)
    {
        lock (_gate)
        {
            if (seed != null) _seed = seed.Value;
            _sim = new MachineSim(_compiled, _catalog, _seed);
            BindSim();
            _forceEntries.Clear(); _ackedAlarmIds.Clear();
            _snapshot = BuildSnapshot();
        }
    }

    // ------------------------------------------------------------------ instantané

    private DateTime At(double simMs) => _epoch.AddMilliseconds(simMs);

    private PlcSnapshot BuildSnapshot()
    {
        var now = At(_sim.NowMs);
        var stations = _sim.Stations.Select(BuildStation).ToList();
        var movers = _sim.Movers.Select(BuildMover).ToList();
        var objects = BuildObjects();
        var alarms = BuildAlarms();
        var quality = BuildQuality();
        var forces = BuildForces();
        var orders = BuildOrders();
        int faults = alarms.Count(a => a.Severity >= AlarmSeverity.StationFault && a.Severity != AlarmSeverity.Quality && a.State == AlarmState.Active);
        int warnings = alarms.Count(a => (a.Severity == AlarmSeverity.Warning || a.Severity == AlarmSeverity.Quality) && a.State == AlarmState.Active);
        bool maintenance = _sim.Mode == MachineMode.Maintenance || forces.StartBlocked;
        bool operatorWaited = quality.LoadingLocked || quality.Tracks.Any(t => t.Locked) || _sim.Movers.Any(m => m.State == MvState.Buffered && m.WaitReason != null && m.WaitReason.StartsWith("Aucune station", StringComparison.Ordinal))
                              || _sim.Stations.Any(s => s.OutOfProduction);
        var beacon = BeaconState.For(_sim.Pack.State, maintenance, operatorWaited);
        if (stations.Any(s => s.State == PackMlState.Aborted) && _sim.Pack.State == PackMlState.Execute)
            beacon = new BeaconState(beacon.Lights | BeaconLights.Red, beacon.Blinking | BeaconLights.Red);
        double eff = _sim.OverridePercent;
        var machine = new MachineStatus(_sim.Pack.State, _sim.Mode, At(_stateSinceMachine), _sim.OverridePercent, eff, beacon, _sim.Pack.State == PackMlState.Execute ? null : _sim.StopCause,
            quality.LoadingLocked || _sim.LoadingLocked, faults, warnings, _compiled.Hash.Substring(0, 16).ToUpperInvariant());
        return new PlcSnapshot(now, _sim.NowMs / 1000.0, PlcConnectionState.Simulation, machine, stations, movers, objects, alarms, quality, forces, orders);
    }

    private StationStatus BuildStation(StationSim s)
    {
        var cfgIdx = _sim.StationIndex(s.Number);
        var groups = _compiled.Machine.Groups.Where((g, i) => (g.StationMask & (1u << cfgIdx)) != 0).Select(g => g.Name).ToList();
        var tr = _trackers[s.Number];
        string? operation = null; double elapsed = 0, estimated = 0; int? mover = null; string? partId = null; Guid? process = null;
        var steps = new List<StepRuntime>();
        var contexts = new List<ContextStatus>();
        var req = s.CurrentRequest;
        if (s.OperationRunning && req != null)
        {
            var g = _compiled.Machine.Groups.ElementAtOrDefault(req.Group - 1);
            var map = s.Data.OpMap.FirstOrDefault(m => m.Group == req.Group && m.OperationNo == req.Operation);
            int table = s.Contexts[1].Table > 0 ? s.Contexts[1].Table : map?.Table ?? 0;
            var dir = table > 0 ? s.Data.Tables[table - 1] : null;
            operation = (g != null ? g.Name + " › " : string.Empty) + (dir?.Name ?? "Opération");
            elapsed = s.OperationElapsedMs / 1000.0;
            estimated = tr.LastDurationSeconds(dir?.Name ?? string.Empty);
            mover = req.Mover;
            partId = req.Parts.FirstOrDefault(p => p.Present)?.Id;
            if (dir != null) process = dir.ProcessId;
        }
        else if (s.Contexts[1].Status != CtxStatus.Free && s.Contexts[1].Table > 0)
        {
            var dir = s.Data.Tables[s.Contexts[1].Table - 1];
            operation = dir.Name; process = dir.ProcessId;
        }
        if (process != null && _processByGuid.TryGetValue(process.Value, out var proc)) steps = BuildSteps(s, tr, proc);
        for (int i = 1; i < s.Contexts.Count; i++)
        {
            var c = s.Contexts[i];
            if (c.Status == CtxStatus.Free || c.Status == CtxStatus.Done) continue;
            Guid? g = _stepGuid.TryGetValue(c.StepId, out var gg) ? gg : (Guid?)null;
            contexts.Add(new ContextStatus(i, i == 1 ? "Opération" : i == 2 ? "Cycle de fond" : "Branche " + (i - 2), g, g != null ? TitleOf(g.Value) : null));
        }
        var beacon = BeaconState.For(s.State, s.Mode == MachineMode.Maintenance && s.OutOfProduction, false);
        var track = _sim.Quality.Track(QualityScope.Station, cfgIdx);
        double rate = track.RatePercent(_compiled.Machine.StationRules.ElementAtOrDefault(cfgIdx) ?? _compiled.Machine.Quality.StationObserved) ?? 0;
        _stationSince.TryGetValue(s.Number, out var since);
        return new StationStatus(s.Number, s.Name, s.State, At(since), s.OverridePercent, beacon, groups, operation, elapsed, estimated, mover, partId, process, steps, contexts,
            s.FaultText, s.QualityHold, s.OutOfProduction, s.Mode, track.Consecutive, rate);
    }

    private string? TitleOf(Guid step)
    {
        foreach (var p in _processByGuid.Values)
            foreach (var st in p.AllSteps())
                if (st.Id == step) return st.Label ?? st.GetType().Name.Replace("Step", string.Empty);
        return null;
    }

    private List<StepRuntime> BuildSteps(StationSim s, StepTracker tr, ProcessDefinition proc)
    {
        var list = new List<StepRuntime>();
        var all = proc.AllSteps().ToList();
        bool nextMarked = false;
        double nowMs = _sim.NowMs;
        var pending = new HashSet<Guid>();
        foreach (var c in s.Contexts)
            if (c.Status == CtxStatus.WaitStep && _compiled.OriginOf(s.Number, c.PC) is { } origin) pending.Add(origin.StepGuid);
        foreach (var st in all)
        {
            uint id = StepIds.Of(st.Id);
            var state = StepRunState.Inactive;
            double el = 0;
            if (st is Block b)
            {
                var kids = b.Sequences.SelectMany(q => ProcessTree.Flatten(q)).Where(k => !(k is Block)).ToList();
                if (kids.Any(k => tr.State(StepIds.Of(k.Id)) == TrackState.Running)) state = StepRunState.Running;
                else if (kids.Count > 0 && kids.Any(k => tr.State(StepIds.Of(k.Id)) == TrackState.Done)) state = StepRunState.Done;
            }
            else
            {
                switch (tr.State(id))
                {
                    case TrackState.Running: state = StepRunState.Running; el = (nowMs - tr.StartedAt(id)) / 1000.0; break;
                    case TrackState.Done: state = StepRunState.Done; break;
                    case TrackState.Error: state = StepRunState.Error; break;
                }
                if (state == StepRunState.Running)
                {
                    var ctx = s.Contexts.FirstOrDefault(c => c.StepId == id && c.Status != CtxStatus.Free);
                    if (ctx != null && ctx.Status == CtxStatus.WaitCond) state = StepRunState.WaitingCondition;
                    else if (ctx != null && ctx.Status == CtxStatus.WaitStep) state = StepRunState.StepPending;
                    else if (ctx != null && ctx.Retry > 0) state = StepRunState.Retry;
                }
                else if (state == StepRunState.Inactive && pending.Contains(st.Id)) { state = StepRunState.StepPending; nextMarked = true; }
                else if (state == StepRunState.Inactive && !nextMarked && s.OperationRunning) { state = StepRunState.Next; nextMarked = true; }
            }
            int attempt = 0, max = 0;
            if (state == StepRunState.Retry && st is ActionStep a) { attempt = (s.Contexts.FirstOrDefault(c => c.StepId == id)?.Retry ?? 0) + 1; max = a.Fail.Retries; }
            list.Add(new StepRuntime(st.Id, state, el, 0, 0, attempt, max, null, null, null, null));
        }
        return list;
    }

    private MoverStatus BuildMover(Mover m)
    {
        var state = m.State switch
        {
            MvState.Routing => MoverState.EnRoute,
            MvState.Queued => MoverState.Queued,
            MvState.AtStation => MoverState.InStation,
            MvState.ToEmpty => MoverState.ToEmpty,
            MvState.OutOfService => MoverState.OutOfService,
            MvState.Error => MoverState.Error,
            _ => m.OutOfService && m.State != MvState.Routing ? MoverState.OutOfService : MoverState.Free,
        };
        int? station = m.Station > 0 ? _sim.Stations[m.Station - 1].Number : (int?)null;
        double? dx = null, dy = null;
        if (m.Dest > 0 && m.Moving) { var p = _compiled.Machine.PlanarPositions[m.Dest - 1]; dx = p.X; dy = p.Y; }
        var parts = m.Slots.Select((p, i) => new { p, i }).Where(x => x.p.Present).Select(x => new PartInfo(x.i + 1, x.p.Id, ToPartState(x.p), VerdictOf(x.p))).ToList();
        string? label = m.TargetGroup > 0 ? _compiled.Machine.Groups[m.TargetGroup - 1].Name : m.WaitReason;
        return new MoverStatus(m.Number, m.X, m.Y, m.C, state, station, dx, dy, m.PC, label, parts, m.State == MvState.Queued ? m.Station : 0);
    }

    private PartState ToPartState(Part p) => p.Status switch
    {
        PartStatus.Good => PartState.Good, PartStatus.Bad => PartState.Bad, PartStatus.Exited => PartState.Exited, PartStatus.Rejected => PartState.Rejected,
        PartStatus.Removed => PartState.Removed, _ => PartState.InProcess,
    };

    private Verdict VerdictOf(Part p)
    {
        int idx = _compiled.Machine.VerdictIndex;
        double v = idx > 0 ? p.Data[idx] : 0;
        return v == 1 ? Verdict.Good : v == 2 ? Verdict.Bad : Verdict.Undefined;
    }

    private List<ObjectStatus> BuildObjects()
    {
        var list = new List<ObjectStatus>();
        foreach (var s in _sim.Stations)
            foreach (var o in s.Objects)
            {
                var state = o.State switch
                {
                    ObjState.NotInitialized => ObjectState.Uninitialized, ObjState.Ready => ObjectState.Ready, ObjState.Busy => ObjectState.Busy,
                    ObjState.Error => ObjectState.Error, _ => ObjectState.Disabled,
                };
                string? action = o.State == ObjState.Busy ? o.ActionName(o.Action) : null;
                list.Add(new ObjectStatus(s.Number, o.Cfg.Id, state, o.Values.ToArray(), action, o.Progress, o.State == ObjState.Error ? o.ErrorTextOf(o.ErrorCode) : null, o.ActionCount, o.ErrorCount));
            }
        foreach (var o in _compiled.Machine.Objects)
            list.Add(new ObjectStatus(0, o.Id, o.Disabled ? ObjectState.Disabled : ObjectState.Ready, new double[4], null, 0, null));
        return list;
    }

    private List<AlarmInfo> BuildAlarms()
    {
        var list = new List<AlarmInfo>();
        foreach (var a in _sim.Alarms.All.OrderByDescending(a => a.Id).Take(200))
        {
            bool acked = _ackedAlarmIds.Contains(a.Id);
            var state = a.Active ? (acked ? AlarmState.Acknowledged : AlarmState.Active) : (acked || a.Severity == AlarmSeverity.Information ? AlarmState.Acknowledged : AlarmState.ClearedUnacknowledged);
            string station = a.Station == 0 ? "Machine" : StationName(a.Station);
            list.Add(new AlarmInfo(a.Id, At(a.RaisedMs), a.ClearedMs != null ? At(a.ClearedMs.Value) : (DateTime?)null, station, a.Object, a.Code, a.SubCode, a.Text, null,
                a.Severity, state, a.Station == 0 ? (int?)null : a.Station, a.Context));
        }
        return list;
    }

    private QualityStatus BuildQuality()
    {
        var q = _compiled.Machine;
        // La page Qualité attend toutes les portées dès le démarrage, même sans résultat enregistré.
        for (int i = 0; i < _sim.Stations.Count; i++) { _sim.Quality.Track(QualityScope.Station, i); }
        for (int g = 1; g <= q.Groups.Count; g++) _sim.Quality.Track(QualityScope.Group, g);
        foreach (var m in _sim.Movers) _sim.Quality.Track(QualityScope.Mover, m.Number);
        _sim.Quality.Track(QualityScope.Machine, 0);
        // Les stations amont d'une fonction qui impute ses rebuts ont aussi une portée « imputés ».
        for (int g = 0; g < q.GroupImputeMask.Count; g++)
        {
            uint mask = q.GroupImputeMask[g];
            if (mask == 0) continue;
            for (int up = 0; up < q.Groups.Count; up++)
                if ((mask & (1u << up)) != 0)
                    for (int st = 0; st < _sim.Stations.Count; st++)
                        if ((q.Groups[up].StationMask & (1u << st)) != 0) _sim.Quality.Track(QualityScope.StationImputed, st);
        }
        var tracks = new List<QualityTrack>();
        foreach (var t in _sim.Quality.Tracks)
        {
            var rule = t.Scope switch
            {
                QualityScope.Station => q.StationRules.ElementAtOrDefault(t.Index) ?? q.Quality.StationObserved,
                QualityScope.StationImputed => q.Quality.StationImputed,
                QualityScope.Group => q.GroupRules.ElementAtOrDefault(t.Index - 1) ?? q.Quality.Function,
                QualityScope.Mover => q.Quality.Mover,
                _ => q.Quality.Machine,
            };
            // Taux imputé affiché à côté d'une station : celui de sa portée « imputés » quand elle existe.
            double? imputed = null;
            if (t.Scope == QualityScope.Station && _sim.Quality.Tracks.FirstOrDefault(x => x.Scope == QualityScope.StationImputed && x.Index == t.Index) is { } imp)
                imputed = imp.RatePercent(q.Quality.StationImputed) ?? 0;
            tracks.Add(new QualityTrack(t.Scope, t.Name, t.Consecutive, t.RatePercent(rule) ?? 0, t.Count, rule.Window, t.WindowText(rule.Window), t.Level, t.Locked, t.Reason,
                t.SinceMs != null ? At(t.SinceMs.Value) : (DateTime?)null, imputed, t.ProbationLeft));
        }
        var order = _sim.Order;
        if (order != null)
        {
            int total = Math.Max(1, order.Good + order.Bad);
            tracks.Add(new QualityTrack(QualityScope.Order, "OF " + order.Number, 0, 100.0 * order.Bad / total, order.Good + order.Bad, Math.Max(1, order.Quantity), string.Empty,
                QualityLevel.Normal, false, null, null, null));
        }
        return new QualityStatus(tracks, _sim.LoadingLocked, _sim.LoadingLockedReason);
    }

    private ForceStatus BuildForces()
    {
        bool blocked = _sim.StartBlocked(out _);
        return new ForceStatus(_sim.Declared, _sim.DeclaredReason, _declaredBy, _declaredSince, _declaredUntil, blocked, _forceEntries.ToList());
    }

    private OrderStatus BuildOrders()
    {
        OrderInfo Info(SimOrder o) => new OrderInfo(o.Number, o.Reference, o.Quantity, o.Good, o.Bad, o.InProcess, o.State, At(o.StartedMs), At(o.StartedMs), null, Math.Max(1, (int)Math.Ceiling(o.Quantity * 0.10)));
        var all = _sim.OrderHistory.Select(Info).ToList();
        var current = _sim.Order != null ? Info(_sim.Order) : null;
        if (current != null) all.Insert(0, current);
        return new OrderStatus(current, all);
    }

    // ------------------------------------------------------------------ suivi des cartes d'une opération

    private enum TrackState
    {
        None,
        Running,
        Done,
        Error,
    }

    private sealed class StepTracker
    {
        private readonly Dictionary<uint, TrackState> _state = new Dictionary<uint, TrackState>();
        private readonly Dictionary<uint, double> _started = new Dictionary<uint, double>();
        private readonly Dictionary<string, double> _durations = new Dictionary<string, double>();
        private uint _lastRunning;
        private string _opName = string.Empty;
        private double _opStartMs;
        public bool Active { get; private set; }
        public double Now { get; set; }

        public void Begin(string name, double nowMs) { Active = true; _opName = name; _opStartMs = nowMs; _state.Clear(); _started.Clear(); }

        public void End(double nowMs, bool aborted)
        {
            Active = false;
            if (aborted && _lastRunning != 0) _state[_lastRunning] = TrackState.Error;
            else if (_opName.Length > 0) _durations[_opName] = (nowMs - _opStartMs) / 1000.0;
        }

        public void On(uint id, bool begin)
        {
            if (begin) { _state[id] = TrackState.Running; _lastRunning = id; _started[id] = Now; }
            else _state[id] = TrackState.Done;
        }

        public TrackState State(uint id) => _state.TryGetValue(id, out var s) ? s : TrackState.None;

        public double StartedAt(uint id) => _started.TryGetValue(id, out var t) ? t : 0;

        public double LastDurationSeconds(string name) => _durations.TryGetValue(name, out var d) ? d : 0;
    }
}
