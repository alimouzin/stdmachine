using StdMachine.Core;
using StdMachine.Core.Catalog;
using StdMachine.Core.Compiler;

namespace StdMachine.Simulation.Engine;

public enum CtxStatus
{
    Free,
    Running,
    WaitAction,
    WaitCond,
    WaitTime,
    WaitJoin,
    WaitStep,
    Holding,
    Done,
    Failed,
}

/// <summary>Contexte d'exécution (ST_Context). Les index d'instruction sont absolus et commencent à 1.</summary>
public sealed class Context
{
    public CtxStatus Status;
    public int Table;
    public int PC;
    public int[] RetTable = new int[5];
    public int[] RetPC = new int[5];
    public int Depth;
    public int Parent;
    public int ForkPC;
    public int Children;
    public int Retry;
    public double WaitMs;
    public uint StepId;
    public int StepDepth;
    public int ErrorCode;
    public bool StepReleased;
    public bool ActionStarted;
    public int ActionObject;
    public int ActionNo;

    public void Reset() { Status = CtxStatus.Free; Table = 0; PC = 0; Depth = 0; Parent = 0; ForkPC = 0; Children = 0; Retry = 0; WaitMs = 0; StepId = 0; StepDepth = 0; ErrorCode = 0; StepReleased = false; ActionStarted = false; }
}

public enum OpStatus
{
    Idle,
    Running,
    Done,
    Failed,
    Aborted,
}

public sealed class OpRequest
{
    public uint ReqId { get; set; }
    public int Group { get; set; }
    public int Operation { get; set; }
    public int Mover { get; set; }
    public int Slot { get; set; }
    public double[] Params { get; set; } = new double[8];
    /// <summary>Pièces portées par le mover (une par emplacement).</summary>
    public Part[] Parts { get; set; } = new Part[0];
}

public sealed class OpResponse
{
    public uint ReqId { get; set; }
    public OpStatus Status { get; set; }
    public int ErrorCode { get; set; }
}

/// <summary>
/// Séquenceur de station (docs/05 §§ 2–4, 8) : machine PackML, interpréteur à 8 contextes, objets simulés, demandes d'opération.
/// </summary>
public sealed class StationSim
{
    private const int MaxCtx = 8;
    private const int MaxInstrPerCycle = 32;
    private const int MaxDepth = 4;

    private readonly CompiledTables _project;
    private readonly AlarmLog _alarms;
    private readonly Func<double> _now;
    private readonly Func<double> _machineOverride;

    private readonly Context[] _ctx = new Context[MaxCtx + 1];
    private StateCommand? _pending;
    private bool _fault;
    private string? _faultText;
    private readonly Dictionary<int, SimObject> _byLocal = new Dictionary<int, SimObject>();

    private OpRequest? _op;
    private bool _opBad;
    private bool _opBadJump;
    private bool _holdRequested;
    private double _opStartMs;
    private uint _lastReqId;
    private bool _launchedFromMaintenance;
    private SimAlarm? _loopAlarm;

    // Pas à pas
    public bool StepEnabled { get; private set; }
    public StepGranularity StepLevel { get; private set; }
    private uint _stepSeq, _seenStepSeq;

    public StationSim(StationData data, CompiledTables project, CatalogDocument catalog, AlarmLog alarms, SimRandom rng, Func<double> now, Func<double> machineOverride)
    {
        Data = data; _project = project; _alarms = alarms; _now = now; _machineOverride = machineOverride;
        Number = data.Number;
        Name = data.Name;
        for (int i = 0; i <= MaxCtx; i++) _ctx[i] = new Context();
        foreach (var cfg in data.Objects)
        {
            var obj = new SimObject(cfg, catalog.Type(cfg.Type), Number, rng, now);
            Objects.Add(obj);
            _byLocal[cfg.LocalId] = obj;
        }
        foreach (var obj in Objects)
        {
            if (obj.Cfg.ParentId != 0 && _byLocal.TryGetValue(obj.Cfg.ParentId, out var parent)) { obj.Parent = parent; parent.Children.Add(obj); }
            obj.InterlockCheck = (action, motion) => CheckInterlocks(obj, action, motion);
        }
        Vars = new double[65];
        VarPos = new Pos[65];
        InitVariables();
        var cfgSt = project.Machine.Stations.ElementAtOrDefault(Math.Max(0, data.Number - 1));
        OverridePercent = cfgSt?.Override ?? 100;
    }

    public StationData Data { get; }
    public int Number { get; }
    public string Name { get; }
    public List<SimObject> Objects { get; } = new List<SimObject>();
    public PackMlMachine Pack { get; } = new PackMlMachine();
    public MachineMode Mode { get; set; } = MachineMode.Production;
    public double OverridePercent { get; set; } = 100;
    public double[] Vars { get; }
    public Pos[] VarPos { get; }
    public OpResponse Response { get; } = new OpResponse();
    public IReadOnlyList<Context> Contexts => _ctx;
    public PackMlState State => Pack.State;
    public string? FaultText => _faultText;
    public double OperationElapsedMs => _op == null ? 0 : _now() - _opStartMs;
    public OpRequest? CurrentRequest => _op;
    public bool OutOfProduction { get; set; }
    public bool QualityHold { get; set; }
    public bool HoldRequested => _holdRequested;
    public bool OperationRunning => _op != null && Response.Status == OpStatus.Running;
    public bool LastCommandAccepted => Pack.LastCommandAccepted;

    /// <summary>Événements d'étape (pour la HMI : carte commencée ou terminée).</summary>
    public event Action<int, uint, bool>? StepEvent;

    public SimObject? Object(int local) => _byLocal.TryGetValue(local, out var o) ? o : null;

    // ------------------------------------------------------------------ commandes

    public bool Command(StateCommand cmd)
    {
        if (!PackMlRules.IsAccepted(cmd, Pack.State)) return false;
        _pending = cmd;
        return true;
    }

    /// <summary>Défaut externe (sécurité de station ouverte, abort forcé).</summary>
    public void RaiseFault(string text)
    {
        _fault = true;
        _faultText ??= text;
    }

    public bool Offer(OpRequest req)
    {
        if (Pack.State != PackMlState.Execute || _holdRequested || _ctx[1].Status != CtxStatus.Free || _op != null) return false;
        var map = Data.OpMap.FirstOrDefault(m => m.Group == req.Group && m.OperationNo == req.Operation);
        if (map == null) return false;
        StartOperation(req, map.Table);
        return true;
    }

    /// <summary>Lancement manuel d'une table (docs/05 § 8) : station Stopped ou Idle en Maintenance.</summary>
    public bool Launch(int table, OpRequest? request)
    {
        if (Mode != MachineMode.Maintenance || _ctx[1].Status != CtxStatus.Free) return false;
        if (Pack.State != PackMlState.Idle && Pack.State != PackMlState.Execute) return false;
        _launchedFromMaintenance = true;
        StartOperation(request ?? new OpRequest(), table);
        return true;
    }

    private void StartOperation(OpRequest req, int table)
    {
        _op = req; _opBad = false; _opBadJump = false; _opStartMs = _now();
        Response.ReqId = req.ReqId; Response.Status = OpStatus.Running; Response.ErrorCode = 0;
        _lastReqId = req.ReqId;
        StartContext(1, table);
    }

    public void SetStepMode(StepGranularity? level)
    {
        StepEnabled = level != null;
        if (level != null) StepLevel = level.Value;
    }

    public void Step() { _stepSeq++; }

    // ------------------------------------------------------------------ un cycle

    public void Cycle(double dtMs)
    {
        var cmd = _pending;
        _pending = null;
        AdvanceStopTable();
        bool complete = StateComplete();
        var before = Pack.State;
        // Hold et Stop demandent la pause avant la transition pour que l'entrée soit vue au même cycle
        Pack.Cycle(cmd, complete, _fault);
        if (Pack.Entry) OnEntry(before, Pack.State);
        if (Pack.State == PackMlState.Aborted) _fault = false;

        if (_stepSeq != _seenStepSeq)
        {
            _seenStepSeq = _stepSeq;
            foreach (var c in _ctx) if (c.Status == CtxStatus.WaitStep) { c.Status = CtxStatus.Running; c.StepReleased = true; }
        }
        if (!StepEnabled)
            foreach (var c in _ctx) if (c.Status == CtxStatus.WaitStep) { c.Status = CtxStatus.Running; c.StepReleased = true; }

        RunInterpreter(dtMs);
        FinishOperation();
        UpdateObjects(dtMs);
    }

    private bool StateComplete()
    {
        switch (Pack.State)
        {
            case PackMlState.Resetting: return _ctx[1].Status == CtxStatus.Free || _ctx[1].Status == CtxStatus.Done;
            case PackMlState.Stopping:
                return _stopPhase == 2 && (_ctx[2].Status == CtxStatus.Free || _ctx[2].Status == CtxStatus.Holding);
            case PackMlState.Clearing:
                return (_ctx[1].Status == CtxStatus.Free || _ctx[1].Status == CtxStatus.Done) && Objects.All(o => o.State != ObjState.Error);
            default: return false;
        }
    }

    private int _stopPhase; // 0 : à lancer, 1 : en cours, 2 : terminée

    private void OnEntry(PackMlState from, PackMlState to)
    {
        switch (to)
        {
            case PackMlState.Stopped:
                KillContexts();
                _holdRequested = false;
                break;
            case PackMlState.Resetting:
                foreach (var o in Objects) o.Ack();
                _faultText = null;
                StartKind(TableKind.Init, 1);
                break;
            case PackMlState.Execute:
                _holdRequested = false;
                foreach (var c in _ctx) if (c.Status == CtxStatus.Holding) c.Status = CtxStatus.Running;
                if (from == PackMlState.Idle && !StepEnabled && _ctx[2].Status == CtxStatus.Free) StartKind(TableKind.Background, 2);
                break;
            case PackMlState.Held:
                _holdRequested = true;
                break;
            case PackMlState.Stopping:
                _holdRequested = true;
                _stopPhase = 0;
                break;
            case PackMlState.Idle:
                _ctx[1].Reset();
                break;
            case PackMlState.Aborted:
                foreach (var o in Objects.OrderByDescending(x => x.Cfg.ParentId == 0 ? 0 : 1)) o.Abort();
                KillContexts();
                if (_op != null) { Response.Status = OpStatus.Aborted; EndOperationBookkeeping(); }
                break;
            case PackMlState.Clearing:
                foreach (var o in Objects) o.Ack();
                _faultText = null;
                _holdRequested = false;
                StartKind(TableKind.Clear, 1);
                break;
        }
    }

    /// <summary>Stopping : une opération en cours se termine, puis la table Arrêt (si elle existe) tourne sur le contexte 1 (docs/05 § 4.2).</summary>
    private void AdvanceStopTable()
    {
        if (Pack.State != PackMlState.Stopping) return;
        if (_stopPhase == 0 && _ctx[1].Status == CtxStatus.Free && _op == null) _stopPhase = StartKind(TableKind.Stop, 1) ? 1 : 2;
        else if (_stopPhase == 1 && _ctx[1].Status == CtxStatus.Done) { _ctx[1].Reset(); _stopPhase = 2; }
    }

    private bool StartKind(TableKind kind, int ctx)
    {
        for (int i = 0; i < Data.Tables.Count; i++)
            if (Data.Tables[i].Kind == kind)
            {
                // Une table vide (seulement End) se termine vite : on la lance quand même, c'est le chemin le plus simple.
                StartContext(ctx, i + 1);
                return true;
            }
        return false;
    }

    private void StartContext(int idx, int table)
    {
        var c = _ctx[idx];
        c.Reset();
        c.Status = CtxStatus.Running;
        c.Table = table;
        c.PC = Data.Tables[table - 1].Start;
        _loopCount[idx] = 0;
    }

    private void KillContexts()
    {
        foreach (var c in _ctx) c.Reset();
    }

    private void FinishOperation()
    {
        if (_op == null) return;
        var c = _ctx[1];
        if (c.Status == CtxStatus.Done)
        {
            Response.Status = _opBad ? OpStatus.Failed : OpStatus.Done;
            c.Reset();
            EndOperationBookkeeping();
        }
        else if (c.Status == CtxStatus.Failed)
        {
            Response.Status = OpStatus.Failed;
            Response.ErrorCode = c.ErrorCode;
            c.Reset();
            EndOperationBookkeeping();
        }
    }

    private void EndOperationBookkeeping()
    {
        _op = null;
        if (_launchedFromMaintenance) { _launchedFromMaintenance = false; }
    }

    /// <summary>L'opération terminée est acquittée par le séquenceur général : la station redevient prête pour une nouvelle demande.</summary>
    public void AcknowledgeResponse()
    {
        if (Response.Status == OpStatus.Done || Response.Status == OpStatus.Failed || Response.Status == OpStatus.Aborted) Response.Status = OpStatus.Idle;
    }

    // ------------------------------------------------------------------ objets

    private void UpdateObjects(double dtMs)
    {
        double machine = _machineOverride() / 100.0;
        double station = OverridePercent / 100.0;
        foreach (var o in Objects)
        {
            if (o.TypeCode == "DigitalInput") o.Values[0] = o.ForcedInputs.TryGetValue(1, out var forced) ? forced : (_dropped ? 1 : 0);
            double eff = machine * station;
            for (var p = o; p != null; p = p.Parent) eff *= p.Cfg.Override / 100.0;
            o.Cycle(dtMs, eff * 100.0);
        }
    }

    private int CheckInterlocks(SimObject obj, int action, bool motion)
    {
        for (int i = 0; i < Data.Interlocks.Count; i++)
        {
            var il = Data.Interlocks[i];
            if (il.Object != obj.LocalId) continue;
            bool applies = il.ActionMask == 0 ? motion : (il.ActionMask & (1u << (action - 1))) != 0;
            if (!applies) continue;
            if (!EvalCondition(il.Cond, null)) return i + 1;
        }
        return 0;
    }

    // ------------------------------------------------------------------ interpréteur

    private readonly int[] _loopCount = new int[MaxCtx + 1];

    private void RunInterpreter(double dtMs)
    {
        for (int i = 1; i <= MaxCtx; i++)
        {
            var c = _ctx[i];
            if (c.Status == CtxStatus.Free || c.Status == CtxStatus.Done || c.Status == CtxStatus.Failed || c.Status == CtxStatus.Holding) continue;
            if (_fault && Pack.State == PackMlState.Aborted) break;
            int budget = MaxInstrPerCycle;
            bool blocked;
            do
            {
                blocked = ExecuteOne(i, dtMs);
                budget--;
            } while (!blocked && budget > 0 && c.Status != CtxStatus.Done && c.Status != CtxStatus.Failed && c.Status != CtxStatus.Free);
            if (!blocked && budget == 0 && c.Status == CtxStatus.Running)
            {
                // Boucle sans attente : 1 s de dépassements répétés lève l'alarme 901 et arrête la station.
                _loopCount[i]++;
                if (_loopCount[i] * dtMs >= 1000)
                {
                    _loopAlarm = _alarms.Raise(Number, null, 901, 0, "Boucle sans attente", AlarmSeverity.StationFault, _now());
                    RaiseFault("Boucle sans attente");
                }
            }
            else _loopCount[i] = 0;
        }
    }

    private Instr Instr(Context c) => Data.Instructions[c.PC - 1];

    /// <summary>Exécute ou fait avancer l'instruction au PC ; renvoie vrai si le contexte est bloqué jusqu'au cycle suivant.</summary>
    private bool ExecuteOne(int idx, double dtMs)
    {
        var c = _ctx[idx];
        if (c.PC < 1 || c.PC > Data.Instructions.Count) { FailContext(idx, 902, "PC invalide"); return true; }
        var ins = Instr(c);
        switch (ins.Op)
        {
            case Core.Compiler.Op.Nop: c.PC++; return false;

            case Core.Compiler.Op.StepBegin:
                if (_holdRequested && idx == 2 && ins.Args[0] == 1) { c.Status = CtxStatus.Holding; return true; }
                if (StepEnabled && !c.StepReleased && ins.Args[0] == 1 && StepLevel == StepGranularity.Step) { c.Status = CtxStatus.WaitStep; return true; }
                if (ins.Args[0] == 1) c.StepReleased = false;
                c.StepId = ins.StepId; c.StepDepth++;
                StepEvent?.Invoke(Number, ins.StepId, true);
                c.PC++; return false;

            case Core.Compiler.Op.StepEnd:
                c.StepDepth = Math.Max(0, c.StepDepth - 1);
                StepEvent?.Invoke(Number, ins.StepId, false);
                c.PC++; return false;

            case Core.Compiler.Op.Action: return ExecuteAction(idx, c, ins, dtMs);

            case Core.Compiler.Op.WaitCond:
                if (StepEnabled && c.Status == CtxStatus.Running && !c.StepReleased && StepLevel == StepGranularity.Action && c.WaitMs == 0 && !c.ActionStarted) { c.Status = CtxStatus.WaitStep; return true; }
                c.ActionStarted = true;
                if (EvalCondition(ins.Args[0], c)) { c.WaitMs = 0; c.Status = CtxStatus.Running; c.StepReleased = false; c.ActionStarted = false; c.PC++; return false; }
                c.Status = CtxStatus.WaitCond;
                c.WaitMs += dtMs;
                if (ins.TimeoutMs > 0 && c.WaitMs > ins.TimeoutMs) { FailContext(idx, 902, "Condition non atteinte"); c.WaitMs = 0; c.ActionStarted = false; }
                return true;

            case Core.Compiler.Op.WaitTime:
                if (StepEnabled && c.Status == CtxStatus.Running && !c.StepReleased && StepLevel == StepGranularity.Action && c.WaitMs == 0 && !c.ActionStarted) { c.Status = CtxStatus.WaitStep; return true; }
                c.ActionStarted = true;
                c.WaitMs += dtMs;
                if (c.WaitMs >= ins.Params[0].Real) { c.WaitMs = 0; c.Status = CtxStatus.Running; c.StepReleased = false; c.ActionStarted = false; c.PC++; return false; }
                c.Status = CtxStatus.WaitTime;
                return true;

            case Core.Compiler.Op.Jump: c.PC = ins.Args[0]; return false;

            case Core.Compiler.Op.JumpIfFalse:
                c.PC = EvalCondition(ins.Args[0], c) ? c.PC + 1 : ins.Args[1];
                return false;

            case Core.Compiler.Op.Fork: return ExecuteFork(idx, c, ins);

            case Core.Compiler.Op.Join: return ExecuteJoin(idx, c, ins);

            case Core.Compiler.Op.LoopInit: Vars[ins.Args[0]] = Resolve(ins.Params[0], c); c.PC++; return false;

            case Core.Compiler.Op.LoopNext:
                Vars[ins.Args[0]] -= 1;
                c.PC = Vars[ins.Args[0]] > 0 ? ins.Args[1] : c.PC + 1;
                return false;

            case Core.Compiler.Op.Call:
                if (c.Depth >= MaxDepth) { FailContext(idx, 904, "Profondeur d'appel dépassée"); return true; }
                c.Depth++; c.RetTable[c.Depth] = c.Table; c.RetPC[c.Depth] = c.PC + 1;
                c.Table = ins.Args[0]; c.PC = Data.Tables[c.Table - 1].Start;
                return false;

            case Core.Compiler.Op.Return:
                if (c.Depth == 0) { EndContext(idx, c); return true; }
                c.Table = c.RetTable[c.Depth]; c.PC = c.RetPC[c.Depth]; c.Depth--;
                return false;

            case Core.Compiler.Op.SetVar: ExecuteSet(c, ins); c.PC++; return false;

            case Core.Compiler.Op.Alarm: return ExecuteAlarm(idx, c, ins);

            case Core.Compiler.Op.End:
                if (ins.Args[0] == 1)
                {
                    c.PC = Data.Tables[c.Table - 1].Start;
                    if (idx == 2) { c.Status = _holdRequested ? CtxStatus.Holding : CtxStatus.Running; return true; }
                    return false;
                }
                EndContext(idx, c);
                return true;

            default: c.PC++; return false;
        }
    }

    private void EndContext(int idx, Context c)
    {
        if (c.Parent != 0) { c.Reset(); return; }
        c.Status = CtxStatus.Done;
    }

    private void FailContext(int idx, int code, string text)
    {
        var c = _ctx[idx];
        c.ErrorCode = code;
        _alarms.Raise(Number, null, code, 0, text, AlarmSeverity.StationFault, _now());
        RaiseFault(text);
        c.Status = CtxStatus.Failed;
    }

    // ---- Action

    private bool ExecuteAction(int idx, Context c, Instr ins, double dtMs)
    {
        var obj = Object(ins.Object);
        if (obj == null) { FailContext(idx, 5, "Objet inconnu"); return true; }
        if (c.Status != CtxStatus.WaitAction)
        {
            if (StepEnabled && !c.StepReleased && StepLevel == StepGranularity.Action && !c.ActionStarted) { c.Status = CtxStatus.WaitStep; return true; }
            c.StepReleased = false;
            var p = ResolveParams(ins, c);
            p.TimeoutMs = ins.TimeoutMs;
            PrepareRecipe(obj, ins, p);
            var r = obj.Start(ins.Action, p, false);
            c.ActionObject = ins.Object; c.ActionNo = ins.Action;
            if (r != StartResult.Ok && r != StartResult.Interlocked) return HandleActionFailure(idx, c, ins, obj, RefusalCode(r));
            if (r == StartResult.Interlocked) return HandleActionFailure(idx, c, ins, obj, 2);
            c.Status = CtxStatus.WaitAction;
            if (obj.Status == ActStatus.Done) return FinishAction(idx, c, ins, obj);
            if (obj.Status == ActStatus.Error) return HandleActionFailure(idx, c, ins, obj, obj.ErrorCode);
            return true;
        }
        if (obj.Status == ActStatus.Busy) return true;
        if (obj.Status == ActStatus.Done) return FinishAction(idx, c, ins, obj);
        if (obj.Status == ActStatus.Error) return HandleActionFailure(idx, c, ins, obj, obj.ErrorCode);
        // L'objet a été arrêté (Abort) : le contexte est tué ailleurs ; on se débloque.
        c.Status = CtxStatus.Running;
        return true;
    }

    private static int RefusalCode(StartResult r) => r switch
    {
        StartResult.Disabled => 7, StartResult.Busy => 3, StartResult.NotConfigured => 10, StartResult.UnknownAction => 4,
        StartResult.ManualOnly => 11, StartResult.NotInitialized => 9, StartResult.InvalidParam => 5, _ => 3,
    };

    private void PrepareRecipe(SimObject obj, Instr ins, ActParams p)
    {
        if (obj.TypeCode != "Press" || ins.Action != 1) return;
        int recipeNo = (int)p.Real[0];
        var recipe = _project.PressRecipes.FirstOrDefault(r => r.Station == Number && r.Object == obj.LocalId && r.RecipeNo == recipeNo);
        var force = recipe?.Segments.LastOrDefault(s => s.Target == PressTarget.Force);
        if (force != null) { obj.RecipeForceMin = force.ForceMin; obj.RecipeForceMax = force.ForceMax; }
    }

    // Présence simulée (capteurs de nid, de pièce…) : vraie après un dépôt de l'outil de la station, fausse après la prise suivante.
    private bool _dropped;

    private void TrackPresence(SimObject obj, int action)
    {
        if (obj.TypeCode == "VacuumBlow")
        {
            if (action == 1 && obj.Results.Real[0] != 0) _dropped = false;
            else if (action == 2 || action == 3) _dropped = true;
        }
        else if (obj.TypeCode == "Gripper")
        {
            if (action == 3) _dropped = false;
            else if (action == 1) _dropped = true;
        }
    }

    private bool FinishAction(int idx, Context c, Instr ins, SimObject obj)
    {
        TrackPresence(obj, ins.Action);
        StoreResults(c, ins, obj);
        c.Retry = 0; c.StepReleased = false;
        obj.AcknowledgeDone();
        c.Status = CtxStatus.Running;
        c.PC++;
        return false;
    }

    private bool HandleActionFailure(int idx, Context c, Instr ins, SimObject obj, int code)
    {
        var def = obj.ActionName(ins.Action);
        switch (ins.OnFail)
        {
            case FailKind.Retry when c.Retry < ins.Retries:
                RaiseObjectAlarm(obj, code, AlarmSeverity.Information);
                obj.Ack();
                c.Retry++;
                c.Status = CtxStatus.Running; // l'instruction sera relancée au prochain passage
                return true;
            case FailKind.PartBad:
                RaiseObjectAlarm(obj, code, AlarmSeverity.Information);
                obj.Ack();
                SetVerdict(2);
                JumpToEnd(idx, c);
                return false;
            case FailKind.Goto:
                RaiseObjectAlarm(obj, code, AlarmSeverity.Information);
                obj.Ack();
                c.Status = CtxStatus.Running; c.PC = ins.FailTarget;
                return false;
            case FailKind.Ignore:
                RaiseObjectAlarm(obj, code, AlarmSeverity.Warning);
                obj.Ack();
                c.Status = CtxStatus.Running; c.PC++;
                return false;
            default:
                RaiseObjectAlarm(obj, code, AlarmSeverity.StationFault);
                RaiseFault("Défaut de " + obj.Name + " (code " + code + ")");
                c.ErrorCode = code;
                c.Status = CtxStatus.Failed;
                return true;
        }
    }

    private void RaiseObjectAlarm(SimObject obj, int code, AlarmSeverity severity)
    {
        _alarms.Raise(Number, obj.Name, code, obj.SubCode, ErrorText(obj, code), severity, _now(), "Station " + Number);
    }

    private string ErrorText(SimObject obj, int code) => obj.ErrorTextOf(code);

    private void JumpToEnd(int idx, Context c)
    {
        _opBad = true;
        c.Retry = 0;
        c.Status = CtxStatus.Running;
        if (c.Parent != 0)
        {
            // Dans une branche : on saute au Join de la branche et on marque l'opération.
            _opBadJump = true;
            var fork = Data.Instructions[c.ForkPC - 1];
            c.PC = fork.Args[0];
            return;
        }
        var dir = Data.Tables[(c.Depth > 0 ? c.RetTable[1] : c.Table) - 1];
        c.Depth = 0;
        c.PC = dir.Start + dir.Length - 1;
    }

    // ---- Fork / Join

    private bool ExecuteFork(int idx, Context c, Instr ins)
    {
        var starts = ins.Args.Skip(1).Take(4).Where(a => a != 0).ToList();
        var children = new List<Context>();
        var indexes = new List<int>();
        for (int k = 1; k < starts.Count; k++)
        {
            int free = -1;
            for (int j = 3; j <= MaxCtx; j++) if (_ctx[j].Status == CtxStatus.Free && !indexes.Contains(j)) { free = j; break; }
            if (free < 0) { FailContext(idx, 903, "Plus de contexte libre"); return true; }
            indexes.Add(free);
        }
        for (int k = 1; k < starts.Count; k++)
        {
            var ch = _ctx[indexes[k - 1]];
            ch.Reset();
            ch.Status = CtxStatus.Running; ch.Table = c.Table; ch.PC = starts[k]; ch.Parent = idx; ch.ForkPC = c.PC; ch.StepId = c.StepId; ch.StepDepth = c.StepDepth;
            ch.Depth = 0;
            children.Add(ch);
        }
        c.Children = starts.Count - 1;
        c.PC = starts[0];
        return false;
    }

    private bool ExecuteJoin(int idx, Context c, Instr ins)
    {
        if (c.Parent != 0 && c.ForkPC == ins.Args[0])
        {
            var parent = _ctx[c.Parent];
            parent.Children = Math.Max(0, parent.Children - 1);
            c.Reset();
            return true;
        }
        if (c.Children == 0)
        {
            if (_opBadJump && idx == 1) { _opBadJump = false; var dir = Data.Tables[c.Table - 1]; c.PC = dir.Start + dir.Length - 1; c.Status = CtxStatus.Running; return false; }
            c.Status = CtxStatus.Running;
            c.PC++;
            return false;
        }
        c.Status = CtxStatus.WaitJoin;
        return true;
    }

    // ---- SetVar, Alarm

    private void ExecuteSet(Context c, Instr ins)
    {
        double v = Resolve(ins.Params[0], c);
        var target = ins.Binds[0];
        int op = ins.Args[0];
        double Apply(double current) => op == 0 ? v : op == 1 ? current + v : current - v;
        switch (target.Target)
        {
            case BindKind.Var: Vars[target.Index] = Apply(Vars[target.Index]); break;
            case BindKind.PartData:
                var part = CurrentPart();
                if (part != null && target.Index > 0 && target.Index < Part.MaxData) part.Data[target.Index] = Apply(part.Data[target.Index]);
                break;
            case BindKind.Verdict: SetVerdict((int)v); break;
            case BindKind.PartRemoved:
                var rp = CurrentPart();
                if (rp != null && v != 0) { rp.Status = PartStatus.Removed; _removed = true; }
                break;
        }
    }

    private bool _removed;

    /// <summary>Vrai si l'opération a retiré la pièce (rebut interne) ; lu et remis à zéro par le séquenceur général.</summary>
    public bool TakeRemovedFlag() { bool r = _removed; _removed = false; return r; }

    public bool LastOperationBad => _opBad;

    private void SetVerdict(int v)
    {
        var part = CurrentPart();
        int index = _project.Machine.VerdictIndex;
        if (part != null && index > 0) part.Data[index] = v;
        if (v == 2) _opBad = true;
    }

    private Part? CurrentPart()
    {
        if (_op == null) return null;
        var parts = _op.Parts;
        if (_op.Slot > 0) return _op.Slot - 1 < parts.Length ? parts[_op.Slot - 1] : null;
        return parts.FirstOrDefault(p => p.Present) ?? parts.FirstOrDefault();
    }

    private bool ExecuteAlarm(int idx, Context c, Instr ins)
    {
        string text = ins.Args[0] >= 1 && ins.Args[0] <= Data.Texts.Count ? Data.Texts[ins.Args[0] - 1] : "Défaut de process";
        int severity = ins.Args[1];
        _alarms.Raise(Number, null, 900 + ins.Args[2], 0, text, severity == 1 ? AlarmSeverity.Warning : severity == 2 ? AlarmSeverity.StationFault : AlarmSeverity.MachineFault, _now(), "Process");
        if (severity >= 2) { RaiseFault(text); c.Status = CtxStatus.Failed; c.ErrorCode = 900 + ins.Args[2]; return true; }
        c.PC++;
        return false;
    }

    // ------------------------------------------------------------------ valeurs

    private double Resolve(CValue v, Context? c)
    {
        switch (v.Source)
        {
            case ValueSource.Const: return v.Real;
            case ValueSource.Var: return Vars[v.Index] + v.Real;
            case ValueSource.PartData:
                var part = CurrentPart();
                return part != null && v.Index > 0 && v.Index < Part.MaxData ? part.Data[v.Index] : 0;
            case ValueSource.OpParam: return _op != null && v.Index >= 1 && v.Index <= 8 ? _op.Params[v.Index - 1] : 0;
            case ValueSource.ObjValue:
                var o = Object(v.Index);
                int k = (int)Math.Round(v.Real);
                return o != null && k >= 1 && k <= 4 ? o.Values[k - 1] : 0;
            case ValueSource.Speed: return v.Index;
            case ValueSource.System:
                return v.Index == 1 ? (_op != null && _op.Mover > 0 ? 1 : 0) : v.Index == 2 ? (_op?.Slot ?? 0) : 0;
            default: return 0;
        }
    }

    private ActParams ResolveParams(Instr ins, Context c)
    {
        var p = new ActParams();
        for (int k = 0; k < ins.Params.Length && k < 8; k++)
        {
            var v = ins.Params[k];
            switch (v.Source)
            {
                case ValueSource.NamedPos:
                    {
                        var np = Data.Positions[v.Index - 1];
                        p.Kind[k] = ParamKind.Position; p.Pos = new Pos(np.X, np.Y, np.Z, np.C, np.Frame); p.HasPos = true; p.Real[k] = v.Real;
                        break;
                    }
                case ValueSource.Var when Data.Variables[v.Index - 1].Type == "Position":
                    p.Kind[k] = ParamKind.Position; p.Pos = VarPos[v.Index]; p.HasPos = true; p.Real[k] = v.Real;
                    break;
                case ValueSource.Speed:
                    if (v.Index > 0) { p.Kind[k] = ParamKind.SpeedIndex; p.Real[k] = v.Index; }
                    else { p.Kind[k] = ParamKind.SpeedPercent; p.Real[k] = v.Real; }
                    break;
                case ValueSource.Text:
                    p.Kind[k] = ParamKind.Text; p.Text = v.Index >= 1 && v.Index <= Data.Texts.Count ? Data.Texts[v.Index - 1] : string.Empty;
                    break;
                case ValueSource.None: break;
                default:
                    p.Kind[k] = ParamKind.Real; p.Real[k] = Resolve(v, c);
                    break;
            }
        }
        return p;
    }

    private void StoreResults(Context c, Instr ins, SimObject obj)
    {
        for (int k = 0; k < ins.Binds.Length && k < 8; k++)
        {
            var b = ins.Binds[k];
            if (b.Target == BindKind.None) continue;
            double value = obj.Results.Real[k];
            switch (b.Target)
            {
                case BindKind.Var:
                    if (obj.Results.PosResult == k + 1 && obj.Results.HasPos) VarPos[b.Index] = obj.Results.Pos;
                    else Vars[b.Index] = value;
                    break;
                case BindKind.PartData:
                    var part = CurrentPart();
                    if (part != null && b.Index > 0 && b.Index < Part.MaxData) part.Data[b.Index] = value;
                    break;
                case BindKind.PartId:
                    var pid = CurrentPart();
                    if (pid != null) pid.Id = obj.Results.Text;
                    break;
                case BindKind.Verdict:
                    SetVerdict(obj.ResultIsGood(ins.Action, k + 1) ? 1 : 2);
                    break;
            }
        }
        // La vision a trouvé une position : les liaisons implicites sont faites par le projet ; ici seulement les liaisons explicites.
    }

    // ------------------------------------------------------------------ conditions

    private bool EvalCondition(int number, Context? c)
    {
        if (number < 1 || number > Data.Conditions.Count) return true;
        var cond = Data.Conditions[number - 1];
        if (cond.Terms.Count == 0) return true;
        bool all = cond.Combine == CombineMode.All;
        foreach (var t in cond.Terms)
        {
            double a = Resolve(t.Left, c), b = Resolve(t.Right, c);
            bool r;
            switch (t.Cmp)
            {
                case Cmp.Eq: r = Math.Abs(a - b) < 1e-9; break;
                case Cmp.Ne: r = Math.Abs(a - b) >= 1e-9; break;
                case Cmp.Lt: r = a < b; break;
                case Cmp.Le: r = a <= b + 1e-9; break;
                case Cmp.Gt: r = a > b; break;
                case Cmp.Ge: r = a >= b - 1e-9; break;
                case Cmp.IsTrue: r = a != 0; break;
                default: r = a == 0; break;
            }
            if (all && !r) return false;
            if (!all && r) return true;
        }
        return all;
    }

    private void InitVariables()
    {
        for (int i = 0; i < Data.Variables.Count; i++) Vars[i + 1] = Data.Variables[i].Value;
    }

    /// <summary>Remise à l'état initial (nouveau déploiement) : objets, variables, contextes, machine d'états.</summary>
    public void ResetAll()
    {
        KillContexts();
        foreach (var o in Objects) o.ResetState();
        Array.Clear(Vars, 0, Vars.Length);
        InitVariables();
        Pack.Reset();
        _op = null; _fault = false; _faultText = null; _holdRequested = false;
        Response.Status = OpStatus.Idle;
    }

    // ------------------------------------------------------------------ état publié

    public bool HasRunningContext() => _ctx.Skip(1).Any(c => c.Status != CtxStatus.Free && c.Status != CtxStatus.Done);

    public uint LastRequestId => _lastReqId;
}
