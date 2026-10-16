using StdMachine.Core;

namespace StdMachine.Simulation.Demo;

/// <summary>État modifié par l'utilisateur pendant la démonstration : il se superpose au scénario scripté.</summary>
public sealed class DemoUserState
{
    public PackMlState? MachineState { get; set; }
    public DateTime MachineStateSince { get; set; }
    public HashSet<int> Acknowledged { get; } = new HashSet<int>();
    public DeclaredState Declared { get; set; }
    public string? DeclaredReason { get; set; }
    public string DeclaredBy { get; set; } = "A. Martin";
    public DateTime? DeclaredSince { get; set; }
    public DateTime? DeclaredUntil { get; set; }
    public Dictionary<int, string> OutOfProduction { get; } = new Dictionary<int, string>();
    public Dictionary<int, string> MoversOut { get; } = new Dictionary<int, string>();
    public bool QualityReleased { get; set; }
    public double OverridePercent { get; set; } = 80;
    public MachineMode Mode { get; set; } = MachineMode.Production;
    public bool OrderSuspended { get; set; }
    public bool OrderCancelled { get; set; }
    public Dictionary<int, bool> RemovedMovers { get; } = new Dictionary<int, bool>();
}

/// <summary>
/// Scénario scripté de la démonstration (docs/07 § 9.1) : une boucle de 150 s de la machine MX-4, calculée comme une
/// fonction pure du temps. La machine démarre, les movers circulent, « Presser pignon » s'allume carte par carte
/// avec un bloc En parallèle, une condition attend, Pressage 01 passe en Aborted puis est réintégrée, une réaction
/// qualité apparaît puis est libérée, un forçage est posé.
/// </summary>
public sealed class DemoScript
{
    public const double LoopSeconds = 150;

    // Phases de la machine
    public const double ResetFrom = 3, IdleFrom = 8, ExecuteFrom = 12, StoppingFrom = 140, StoppedFrom = 146;
    // Pressage 01 : défaut, acquittement, réintégration
    public double AbortAt { get; private set; } = 57;
    public const double ClearFrom = 72, ClearEnd = 75, ResetStationFrom = 77, IdleStationFrom = 82, ReintegratedAt = 84;
    // Arrêt qualité de Pressage 02 : réaction puis libération
    public const double QualityFrom = 90, QualityRelease = 108;
    // Forçage : mover 3 hors service
    public const double ForceFrom = 115, ForceTo = 135;

    public MachineProject Project { get; }
    public DemoFlow Flow { get; private set; } = null!;

    private static readonly string[] StationNames = { "", "Chargement", "Pressage 01", "Pressage 02", "Contrôle", "Déchargement", "Rebut" };

    public DemoScript(MachineProject project)
    {
        Project = project;
        Tune();
    }

    // ------------------------------------------------------------------ réglage du scénario

    /// <summary>Cherche les paramètres d'ordonnancement qui placent un défaut en pleine presse à 57 s, une attente à 45 s…</summary>
    private void Tune()
    {
        double bestScore = -1;
        DemoFlow? best = null;
        double bestAbort = 57;
        for (double first = 13; first <= 19; first += 0.5)
        {
            for (int waitProduct = 2; waitProduct <= 5; waitProduct++)
            {
                var probe = new DemoFlow(Project);
                probe.Build(first, 9.5, waitProduct, 70, (ForceFrom, ForceTo));
                double abort = 0;
                foreach (var v in probe.Visits.Where(v => v.Station == DemoProject.Pressage01 && v.Operation == "Presser pignon"))
                {
                    var rec = v.Schedule.Of(DemoProject.StepId(v.Station, "presser", "recette"));
                    if (rec == null) continue;
                    double t = v.Start + rec.Start + 1.0;
                    if (t >= 54 && t <= 59.5) { abort = t; break; }
                }
                if (abort <= 0) continue;

                var flow = new DemoFlow(Project);
                flow.Windows.Add(new StationWindow(DemoProject.Pressage01, abort, ReintegratedAt, true, ClearEnd));
                flow.Windows.Add(new StationWindow(DemoProject.Pressage02, QualityFrom, QualityRelease, false, 0));
                flow.Build(first, 9.5, waitProduct, 70, (ForceFrom, ForceTo));
                double score = Score(flow);
                if (score > bestScore) { bestScore = score; best = flow; bestAbort = abort; }
            }
        }
        if (best == null)
        {
            // Repli : aucun réglage trouvé, on garde un ordonnancement sans défaut ajusté.
            best = new DemoFlow(Project);
            best.Windows.Add(new StationWindow(DemoProject.Pressage01, 57, ReintegratedAt, true, ClearEnd));
            best.Windows.Add(new StationWindow(DemoProject.Pressage02, QualityFrom, QualityRelease, false, 0));
            best.Build(14, 9.5, 3, 70, (ForceFrom, ForceTo));
        }
        Flow = best;
        AbortAt = bestAbort;
    }

    private double Score(DemoFlow flow)
    {
        double s = 0;
        // à 30 s, Pressage 01 est dans le bloc En parallèle de « Presser pignon »
        foreach (var v in flow.Visits.Where(v => v.Station == DemoProject.Pressage01 && v.Operation == "Presser pignon"))
        {
            var par = v.Schedule.Of(DemoProject.StepId(v.Station, "presser", "par"));
            double e = 30 - v.Start;
            if (par != null && e >= par.Start && e < par.End) s += 3;
        }
        // à 45 s, une carte « Attendre condition » attend
        foreach (var v in flow.Visits.Where(v => v.Operation == "Presser pignon"))
        {
            var w = v.Schedule.Of(DemoProject.StepId(v.Station, "presser", "attendre"));
            double e = 45 - v.Start;
            if (w != null && e >= w.Start && e < w.End && v.WaitExtra > 0) s += 3;
        }
        if (flow.Visits.Any(v => v.Aborted)) s += 5;
        if (flow.Visits.Any(v => v.Station == DemoProject.Pressage02 && v.Operation == "Presser pignon" && v.Start < 62 && v.End > 62)) s += 1;
        if (flow.Visits.Any(v => v.Station == DemoProject.Pressage01 && v.Operation == "Presser pignon" && v.Start > ReintegratedAt)) s += 1;
        s -= flow.Products.Count(p => p.Bad) > 4 ? 1 : 0;
        return s;
    }

    // ------------------------------------------------------------------ états

    private static PackMlState MachinePhase(double t, out double since)
    {
        if (t < ResetFrom) { since = 0; return PackMlState.Stopped; }
        if (t < IdleFrom) { since = ResetFrom; return PackMlState.Resetting; }
        if (t < ExecuteFrom) { since = IdleFrom; return PackMlState.Idle; }
        if (t < StoppingFrom) { since = ExecuteFrom; return PackMlState.Execute; }
        if (t < StoppedFrom) { since = StoppingFrom; return PackMlState.Stopping; }
        since = StoppedFrom;
        return PackMlState.Stopped;
    }

    private PackMlState StationPhase(int station, double t, out double since)
    {
        var m = MachinePhase(t, out since);
        if (m != PackMlState.Execute && !(station == DemoProject.Pressage01 && t >= AbortAt && t < ReintegratedAt)) return m;
        if (station == DemoProject.Pressage01 && t >= AbortAt && t < ReintegratedAt)
        {
            if (t < ClearFrom) { since = AbortAt; return PackMlState.Aborted; }
            if (t < ClearEnd) { since = ClearFrom; return PackMlState.Clearing; }
            if (t < ResetStationFrom) { since = ClearEnd; return PackMlState.Stopped; }
            if (t < IdleStationFrom) { since = ResetStationFrom; return PackMlState.Resetting; }
            since = IdleStationFrom;
            return PackMlState.Idle;
        }
        if (station == DemoProject.Pressage02 && t >= QualityFrom && t < QualityRelease)
        {
            // L'opération en cours se termine, puis la station passe en Held (arrêt qualité).
            var current = Flow.Visits.FirstOrDefault(v => v.Station == station && v.Start <= t && t < v.End);
            if (current == null)
            {
                var lastEnd = Flow.Visits.Where(v => v.Station == station && v.End <= t && v.End >= QualityFrom).Select(v => v.End).DefaultIfEmpty(QualityFrom).Max();
                since = Math.Max(QualityFrom, lastEnd);
                return PackMlState.Held;
            }
        }
        if (station == DemoProject.Pressage01 && t >= ReintegratedAt) since = ReintegratedAt;
        if (station == DemoProject.Pressage02 && t >= QualityRelease && since < QualityRelease) since = QualityRelease;
        return PackMlState.Execute;
    }

    // ------------------------------------------------------------------ instantané

    public PlcSnapshot Evaluate(double t, DateTime now, PlcConnectionState connection, DemoUserState user)
    {
        t = ((t % LoopSeconds) + LoopSeconds) % LoopSeconds;
        var baseTime = now.AddSeconds(-t);
        DateTime At(double s) => baseTime.AddSeconds(s);

        var machineState = MachinePhase(t, out var mSince);
        var machineSince = At(mSince);
        if (user.MachineState.HasValue)
        {
            machineState = user.MachineState.Value;
            machineSince = user.MachineStateSince;
        }

        var stations = new List<StationStatus>();
        foreach (var cfg in Project.Stations)
            stations.Add(BuildStation(cfg, t, At, machineState, user));
        var movers = BuildMovers(t, machineState, user);
        var objects = BuildObjects(t);
        var alarms = BuildAlarms(t, At, machineState, user);
        var quality = BuildQuality(t, At, user);
        var forces = BuildForces(t, At, user);
        var orders = BuildOrders(t, At);

        int faults = alarms.Count(a => a.Severity >= AlarmSeverity.StationFault && a.State == AlarmState.Active);
        int warnings = alarms.Count(a => (a.Severity == AlarmSeverity.Warning || a.Severity == AlarmSeverity.Quality) && a.State != AlarmState.Acknowledged);
        var stopCause = machineState == PackMlState.Aborted ? "Abort (commande)" : null;
        bool blue = user.Mode == MachineMode.Maintenance || forces.StartBlocked;
        bool operatorWaited = quality.LoadingLocked || quality.Tracks.Any(q => q.Locked);
        var beacon = BeaconState.For(machineState, blue, operatorWaited);
        if (stations.Any(s => s.State == PackMlState.Aborted) && machineState == PackMlState.Execute)
            beacon = new BeaconState(beacon.Lights | BeaconLights.Red, beacon.Blinking | BeaconLights.Red);

        double eff = user.OverridePercent;
        var machine = new MachineStatus(machineState, user.Mode, machineSince, user.OverridePercent, eff, beacon, stopCause,
            quality.LoadingLocked, faults, warnings, "9F3A1C07D2E84B66");
        return new PlcSnapshot(now, t, connection, machine, stations, movers, objects, alarms, quality, forces, orders);
    }

    // ------------------------------------------------------------------ stations

    private StationStatus BuildStation(StationConfig cfg, double t, Func<double, DateTime> at, PackMlState machineState, DemoUserState user)
    {
        int n = cfg.Number;
        var state = StationPhase(n, t, out var since);
        var sinceTime = at(since);
        if (user.MachineState.HasValue)
        {
            state = user.MachineState.Value == PackMlState.Held ? PackMlState.Held : user.MachineState.Value;
            sinceTime = user.MachineStateSince;
        }
        bool outOfProd = user.OutOfProduction.ContainsKey(n);
        if (outOfProd && state == PackMlState.Execute) state = PackMlState.Held;

        var groups = Project.GroupsOf(cfg).Select(g => g.Name).ToList();
        // Opération en cours : visite active de cette station
        Visit? visit = null;
        bool frozen = user.MachineState.HasValue && user.MachineState.Value != PackMlState.Execute;
        foreach (var v in Flow.Visits)
        {
            if (v.Station != n) continue;
            if (t >= v.Start && t < (v.Aborted ? v.Leave : v.End)) { visit = v; break; }
        }
        var steps = new List<StepRuntime>();
        var contexts = new List<ContextStatus>();
        string? operation = null;
        double elapsed = 0, estimated = 0;
        int? mover = null;
        string? partId = null;
        Guid? runningProcess = null;
        string? fault = null;

        if (visit != null && (state == PackMlState.Execute || state == PackMlState.Aborted || state == PackMlState.Held || state == PackMlState.Stopping || frozen))
        {
            bool aborted = visit.Aborted && t >= visit.End;
            double e = Math.Min(t - visit.Start, visit.Aborted ? visit.End - visit.Start : visit.Duration);
            operation = visit.Group + " › " + visit.Operation;
            elapsed = e;
            estimated = visit.Schedule.Total;
            mover = visit.Mover;
            partId = visit.PartId;
            runningProcess = visit.ProcessId;
            steps = StepRuntimes(visit, cfg, e, aborted);
            int ci = 1;
            foreach (var r in steps.Where(r => r.State == StepRunState.Running || r.State == StepRunState.WaitingCondition || r.State == StepRunState.Retry || r.State == StepRunState.Error))
            {
                var timing = visit.Schedule.Of(r.StepId);
                if (timing == null || timing.Step is Block) continue;
                contexts.Add(new ContextStatus(ci++, "Contexte " + (ci - 1), r.StepId, DemoText.Title(timing.Step, cfg)));
            }
            if (aborted) fault = "Force maximale dépassée (Presse)";
        }
        else if (state == PackMlState.Aborted)
        {
            fault = "Force maximale dépassée (Presse)";
        }

        var beacon = BeaconState.For(state, user.Mode == MachineMode.Maintenance && (n == DemoProject.Pressage01 && outOfProd), false);
        bool qualityHold = n == DemoProject.Pressage02 && t >= QualityFrom && t < QualityRelease && !user.QualityReleased && state == PackMlState.Held;
        int consec = n == DemoProject.Pressage02 ? (t >= QualityFrom && t < QualityRelease ? 3 : 0) : 0;
        double rate = n == DemoProject.Pressage02 ? (t >= 60 && t < QualityRelease ? 10.0 : 4.0) : (n == DemoProject.Pressage01 ? 2.0 : 0.0);
        return new StationStatus(n, cfg.Name, state, sinceTime, user.OverridePercent, beacon, groups, operation, elapsed, estimated, mover, partId,
            runningProcess, steps, contexts, fault, qualityHold, outOfProd, user.Mode, consec, rate);
    }

    private List<StepRuntime> StepRuntimes(Visit v, StationConfig cfg, double e, bool aborted)
    {
        var list = new List<StepRuntime>();
        var timings = v.Schedule.Timings;
        // Prochaine carte : la plus proche dans le temps parmi celles qui n'ont pas commencé
        double nextStart = timings.Where(x => x.Taken && !(x.Step is Block) && x.Start > e && x.End > x.Start).Select(x => x.Start).DefaultIfEmpty(double.MaxValue).Min();
        bool first = true;
        foreach (var x in timings)
        {
            double dur = x.End - x.Start;
            StepRunState state;
            double progress = 0, el = 0;
            string? detail = null;
            IReadOnlyList<ConditionTermRuntime>? terms = null;
            int attempt = 0, max = 0;
            string? blockResult = null;
            Guid? sub = null;
            if (!x.Taken)
            {
                state = e >= x.Start ? StepRunState.NotTaken : StepRunState.Inactive;
            }
            else if (aborted)
            {
                // Défaut : la carte en cours passe en erreur, celles d'avant sont terminées, celles d'après inactives.
                if (x.End <= e) state = StepRunState.Done;
                else if (x.Start <= e) { state = StepRunState.Error; detail = "Force maximale dépassée (Presse)"; progress = (e - x.Start) / Math.Max(dur, 0.01); el = e - x.Start; }
                else state = StepRunState.Inactive;
            }
            else if (x.Step is Block)
            {
                if (e >= x.End && x.End > x.Start) state = StepRunState.Done;
                else if (e >= x.Start && x.End > x.Start) { state = StepRunState.Running; progress = (e - x.Start) / dur; el = e - x.Start; }
                else state = e >= x.End && x.End >= x.Start && e > x.Start ? StepRunState.Done : StepRunState.Inactive;
                if (x.Step is IfBlock && e >= x.Start && e < x.Start + 2.0) blockResult = v.Good ? "Pièce bonne : oui" : "Pièce bonne : non";
            }
            else if (e >= x.End)
            {
                state = StepRunState.Done;
                el = dur;
            }
            else if (e >= x.Start && dur > 0)
            {
                state = StepRunState.Running;
                progress = (e - x.Start) / dur;
                el = e - x.Start;
                if (x.Step is WaitConditionStep w)
                {
                    state = StepRunState.WaitingCondition;
                    terms = new[] { new ConditionTermRuntime("Pièce présente sur le nid", progress > 0.85) };
                    detail = "Attente : " + (w.Condition.Terms.Count > 0 ? w.Condition.Terms[0].Text : "condition");
                }
                else if (x.Step is FunctionStep f)
                {
                    var def = DemoCatalog.Get(cfg.FindObject(f.ModuleObjectId)?.TypeCode ?? "")?.Function(f.FunctionId);
                    if (def != null && def.Inner.Length > 0)
                    {
                        int idx = Math.Min(def.Inner.Length - 1, (int)(progress * def.Inner.Length));
                        detail = def.Inner[idx];
                        sub = DemoIds.Of("sub/" + x.StepId + "/" + idx);
                    }
                }
                else if (x.Step is ActionStep a && v.RetryFirst && x.Start < 0.01 && a.Fail.Kind == FailKind.Retry)
                {
                    state = StepRunState.Retry; attempt = 2; max = a.Fail.Retries;
                }
            }
            else if (dur <= 0 && e >= x.Start && e >= nextStart - 0.001)
            {
                state = StepRunState.Done;
            }
            else if (x.Start > e && Math.Abs(x.Start - nextStart) < 0.001 && !(x.Step is Block))
            {
                state = StepRunState.Next;
            }
            else state = StepRunState.Inactive;
            first = false;
            list.Add(new StepRuntime(x.StepId, state, el, dur, Math.Min(1, Math.Max(0, progress)), attempt, max, detail, terms, sub, blockResult));
        }
        if (first) return list;
        return list;
    }

    // ------------------------------------------------------------------ movers

    private List<MoverStatus> BuildMovers(double t, PackMlState machineState, DemoUserState user)
    {
        var result = new List<MoverStatus>();
        bool frozen = user.MachineState.HasValue;
        for (int m = 1; m <= 6; m++)
        {
            var segs = Flow.MoverSegs[m];
            var idle = Flow.Idle[m];
            double x = idle.X, y = idle.Y, rot = 0;
            double? dx = null, dy = null;
            var state = MoverState.Free;
            int? station = null;
            int step = 1;
            string? label = null;
            int product = 0;
            int queueFor = 0;
            if (machineState != PackMlState.Stopped || t >= ExecuteFrom)
            {
                foreach (var s in segs)
                {
                    if (t < s.T0 || t >= s.T1) continue;
                    double f = s.T1 > s.T0 ? (t - s.T0) / (s.T1 - s.T0) : 1;
                    f = f * f * (3 - 2 * f);
                    x = s.X0 + (s.X1 - s.X0) * f;
                    y = s.Y0 + (s.Y1 - s.Y0) * f;
                    state = s.State;
                    station = s.Station;
                    step = s.GeneralStep;
                    label = s.Label;
                    product = s.Product;
                    queueFor = s.QueueFor;
                    if (s.State == MoverState.EnRoute) { dx = s.X1; dy = s.Y1; }
                    break;
                }
            }
            if (machineState == PackMlState.Stopped && t < ExecuteFrom) { state = MoverState.Free; product = 0; }
            if (frozen && user.MachineState == PackMlState.Stopped) { state = MoverState.Free; }
            if (m == 3 && t >= ForceFrom && t < ForceTo || user.MoversOut.ContainsKey(m)) { state = MoverState.OutOfService; product = 0; x = idle.X; y = idle.Y; }

            var parts = new List<PartInfo>();
            if (product > 0)
            {
                var prod = Flow.Products.First(p => p.N == product);
                if (t >= prod.Created && t < prod.Finished)
                {
                    PartState ps;
                    Verdict vd;
                    if (t < prod.PressedAt) { ps = PartState.InProcess; vd = Verdict.Undefined; }
                    else if (prod.Bad) { ps = PartState.Bad; vd = Verdict.Bad; }
                    else { ps = PartState.Good; vd = Verdict.Good; }
                    parts.Add(new PartInfo(1, prod.PartId, ps, vd));
                }
            }
            if (state == MoverState.Error) rot = 0;
            result.Add(new MoverStatus(m, x, y, rot, state, station, dx, dy, step, label, parts, queueFor));
        }
        return result;
    }

    // ------------------------------------------------------------------ objets

    private static readonly (string Key, double X, double Y, double Z, double C)[] Poses =
    {
        ("repos", 0, 0, -5, 0), ("feed", 210, 120, -24.8, 0), ("nid", 150, 60, -18.2, 0), ("mover", 268.4, 92.15, -31.6, 0), ("presse", 95, 140, -31.6, 45), ("bac", 300, 20, -12, 0),
    };

    private List<ObjectStatus> BuildObjects(double t)
    {
        var list = new List<ObjectStatus>();
        foreach (var cfg in Project.Stations)
        {
            int n = cfg.Number;
            Visit? v = Flow.Visits.FirstOrDefault(x => x.Station == n && t >= x.Start && t < (x.Aborted ? x.Leave : x.End));
            foreach (var o in cfg.AllObjects())
            {
                double[] values = { 0, 0, 0, 0 };
                var state = ObjectState.Ready;
                string? action = null;
                double progress = 0;
                string? err = null;
                if (v != null && (n == DemoProject.Pressage01 || n == DemoProject.Pressage02) && v.Operation == "Presser pignon")
                {
                    double e = t - v.Start;
                    var pose = RobotPose(v, cfg, e, out var activeObj, out var activeAction, out var activeProgress);
                    if (o.TypeCode == "LinearAxis" || o.TypeCode == "RotaryAxis")
                    {
                        values = new[]
                        {
                            o.ChildRole == "AxisX" ? pose.X : o.ChildRole == "AxisY" ? pose.Y : o.ChildRole == "AxisZ" ? pose.Z : pose.C,
                            activeObj == "robot" ? 80 : 0, 1, 1,
                        };
                        if (activeObj == "robot") { state = ObjectState.Busy; action = activeAction; progress = activeProgress; }
                    }
                    else if (o.TypeCode == "VacuumBlow")
                    {
                        bool hold = activeObj == "robot" && activeAction != null && activeAction.StartsWith("Prendre") && activeProgress > 0.5 || activeAction == "Poser pièce" && activeProgress < 0.5;
                        values = new double[] { hold ? 1 : 0, 0, hold ? -68.2 : -1.5, hold ? 1 : 0 };
                    }
                    else if (o.TypeCode == "Press")
                    {
                        var rec = v.Schedule.Of(DemoProject.StepId(n, "presser", "recette"));
                        if (rec != null && e >= rec.Start && e < rec.End)
                        {
                            double p = (e - rec.Start) / (rec.End - rec.Start);
                            values = new[] { 850 * Math.Sin(p * Math.PI / 2), 6 * p, 3, 0 };
                            state = ObjectState.Busy;
                            action = "Exécuter recette 3";
                            progress = p;
                        }
                        else values = new double[] { e >= (rec?.End ?? 99) ? 851.2 : 0, 0, 3, 0 };
                        if (v.Aborted && t >= v.End) { state = ObjectState.Error; err = "Force maximale dépassée"; values = new double[] { 2010, 6, 3, 0 }; }
                    }
                    else if (o.TypeCode == "AsyrilEyePlus")
                    {
                        values = new double[] { 3, 1, 0, 0 };
                    }
                    else if (o.TypeCode == "DigitalInput")
                    {
                        values = new double[] { e > 5.0 ? 1 : 0, 0, 0, 0 };
                    }
                    else if (o.TypeCode == "Counter")
                    {
                        values = new double[] { 7 + (n == DemoProject.Pressage02 ? 3 : 0), 0, 0, 0 };
                    }
                    else if (o.TypeCode == "CartesianRobot")
                    {
                        state = activeObj == "robot" ? ObjectState.Busy : ObjectState.Ready;
                        action = activeAction;
                        progress = activeProgress;
                    }
                }
                if (v == null && o.TypeCode == "Counter") values = new double[] { 7 + (n == DemoProject.Pressage02 ? 3 : 0), 0, 0, 0 };
                if (v == null && (o.TypeCode == "LinearAxis" || o.TypeCode == "RotaryAxis")) values = new double[] { 0, 0, 1, 1 };
                if (o.Disabled) state = ObjectState.Disabled;
                list.Add(new ObjectStatus(n, o.Id, state, values, action, progress, err));
            }
        }
        return list;
    }

    /// <summary>Position du robot pendant « Presser pignon » : interpolée entre les cibles des étapes du robot.</summary>
    private (double X, double Y, double Z, double C) RobotPose(Visit v, StationConfig cfg, double e, out string? activeObj, out string? activeAction, out double activeProgress)
    {
        activeObj = null; activeAction = null; activeProgress = 0;
        var prev = Poses[0];
        (string Key, double X, double Y, double Z, double C) cur = prev;
        foreach (var x in v.Schedule.Timings)
        {
            if (!x.Taken || !(x.Step is FunctionStep f)) continue;
            string target = "repos";
            if (f.Args.Count > 0 && f.Args[0].Kind == ValueKind.NamedPos)
            {
                var p = cfg.Positions.FirstOrDefault(q => q.Id == f.Args[0].Ref);
                target = p == null ? "repos" : KeyOf(p.Name);
            }
            else if (f.Args.Count > 0 && f.Args[0].Kind == ValueKind.Var) target = "feed";
            var tp = Poses.First(q => q.Key == target);
            if (e < x.Start) break;
            double p2 = x.End > x.Start ? Math.Min(1, (e - x.Start) / (x.End - x.Start)) : 1;
            if (e < x.End)
            {
                activeObj = "robot";
                activeAction = DemoCatalog.Get("CartesianRobot")!.Function(f.FunctionId)!.Name;
                activeProgress = p2;
                return (prev.X + (tp.X - prev.X) * p2, prev.Y + (tp.Y - prev.Y) * p2, prev.Z + (tp.Z - prev.Z) * p2, prev.C + (tp.C - prev.C) * p2);
            }
            prev = tp;
        }
        return (prev.X, prev.Y, prev.Z, prev.C);
    }

    private static string KeyOf(string name)
    {
        switch (name)
        {
            case "Zone alimenteur": return "feed";
            case "Dépose nid": return "nid";
            case "Pose presse": return "presse";
            case "Mover": return "mover";
            case "Bac rebut": return "bac";
            default: return "repos";
        }
    }

    // ------------------------------------------------------------------ alarmes, qualité, forçages, OF

    private List<AlarmInfo> BuildAlarms(double t, Func<double, DateTime> at, PackMlState machineState, DemoUserState user)
    {
        var list = new List<AlarmInfo>();
        AlarmState State(int id, AlarmState s) => user.Acknowledged.Contains(id) ? AlarmState.Acknowledged : s;

        // Historique des jours précédents
        var y = at(-86400);
        list.Add(new AlarmInfo(901, y.AddHours(-3.1), y.AddHours(-3.0), "Chargement", "Chargeur vibrant", 100, 0, "Trémie vide", null, AlarmSeverity.Warning, AlarmState.Acknowledged, 1, "Charger › Chercher pièce"));
        list.Add(new AlarmInfo(902, y.AddHours(-1.4), y.AddHours(-1.3), "Pressage 02", "Axe Z", 102, 1032, "Erreur du drive", "Erreur de suivi (NC 0x4550)", AlarmSeverity.StationFault, AlarmState.Acknowledged, 3, "Presser pignon › Prendre pièce"));
        list.Add(new AlarmInfo(903, y.AddHours(0.6), y.AddHours(0.7), "Machine", "Sécurités", 100, 3, "Circuit ouvert : porte 2", null, AlarmSeverity.MachineFault, AlarmState.Acknowledged, null, null));
        list.Add(new AlarmInfo(904, y.AddHours(2.2), y.AddHours(2.2), "Contrôle", "Palpeur hauteur", 100, 0, "Mesure hors plage", null, AlarmSeverity.Warning, AlarmState.Acknowledged, 4, "Contrôler › Mesurer"));

        if (t >= 34)
        {
            var trem = State(1001, t >= 52 ? AlarmState.ClearedUnacknowledged : AlarmState.Active);
            list.Add(new AlarmInfo(1001, at(34), t >= 52 ? at(52) : (DateTime?)null, "Pressage 02", "Chargeur vibrant", 100, 0, "Trémie vide", "Remplir la trémie du chargeur", AlarmSeverity.Warning, trem, 3, "Presser pignon › Chercher pièce"));
        }
        if (t >= AbortAt)
        {
            var s = t >= ClearEnd ? AlarmState.ClearedUnacknowledged : AlarmState.Active;
            list.Add(new AlarmInfo(1002, at(AbortAt), t >= ClearFrom ? at(ClearFrom) : (DateTime?)null, "Pressage 01", "Presse", 100, 0, "Force maximale dépassée",
                "Vérifier la recette n° 3 et le nid, puis Acquitter", AlarmSeverity.StationFault, State(1002, s), 2, "Presser pignon › Exécuter recette"));
        }
        if (t >= QualityFrom)
        {
            bool released = t >= QualityRelease || user.QualityReleased;
            list.Add(new AlarmInfo(1003, at(QualityFrom), released ? at(QualityRelease) : (DateTime?)null, "Pressage 02", null, 950, 3, "Rebuts successifs",
                "3 rebuts successifs (seuil 3) : arrêt qualité de la station", AlarmSeverity.Quality, State(1003, released ? AlarmState.ClearedUnacknowledged : AlarmState.Active), 3, null));
        }
        if (machineState == PackMlState.Aborted && user.MachineState == PackMlState.Aborted)
            list.Add(new AlarmInfo(1004, user.MachineStateSince, null, "Machine", null, 1, 0, "Abort demandé", "Arrêt d'urgence logiciel", AlarmSeverity.MachineFault, State(1004, AlarmState.Active), null, null));
        return list.OrderByDescending(a => a.Time).ToList();
    }

    private static string Window(int seed, int len, double badRate, string? tail = null)
    {
        var rnd = new Random(seed);
        var chars = new char[len];
        for (int i = 0; i < len; i++) chars[i] = rnd.NextDouble() < badRate ? '1' : '0';
        if (tail != null) for (int i = 0; i < tail.Length; i++) chars[len - tail.Length + i] = tail[i];
        return new string(chars);
    }

    private QualityStatus BuildQuality(double t, Func<double, DateTime> at, DemoUserState user)
    {
        var tracks = new List<QualityTrack>();
        bool hold = t >= QualityFrom && t < QualityRelease && !user.QualityReleased;
        bool afterRelease = t >= QualityRelease || user.QualityReleased;
        tracks.Add(new QualityTrack(QualityScope.Machine, "Machine", 0, 2.4, 200, 200, Window(1, 200, 0.024), QualityLevel.Normal, false, null, null, null));
        string[] names = { "", "Chargement", "Pressage 01", "Pressage 02", "Contrôle", "Déchargement", "Rebut" };
        for (int i = 1; i <= 4; i++)
        {
            bool p2 = i == 3;
            double rate = p2 ? (hold ? 10.0 : afterRelease ? 0.0 : 6.0) : (i == 2 ? 2.0 : 0.0);
            string w = p2 && hold ? Window(30 + i, 50, 0.06, "111") : p2 && afterRelease ? Window(40 + i, 10, 0.0) : Window(10 + i, 50, p2 ? 0.06 : 0.02);
            tracks.Add(new QualityTrack(QualityScope.Station, names[i], p2 && hold ? 3 : 0, rate, p2 && afterRelease ? 10 : 50, 50, w,
                p2 ? (hold ? QualityLevel.Action : afterRelease ? QualityLevel.Probation : QualityLevel.Warning) : QualityLevel.Normal,
                p2 && hold, p2 && hold ? "3 rebuts successifs (seuil 3) — Arrêt qualité" : null, p2 && hold ? at(QualityFrom) : (DateTime?)null,
                i == 2 ? 2.0 : i == 3 ? (hold ? 12.0 : afterRelease ? 0.0 : 8.0) : (double?)null,
                p2 && afterRelease ? Math.Max(0, 10 - (int)((t - QualityRelease) / 3)) : 0));
        }
        tracks.Add(new QualityTrack(QualityScope.StationImputed, "Pressage 01", 0, 2.0, 50, 50, Window(21, 50, 0.02), QualityLevel.Normal, false, null, null, null));
        tracks.Add(new QualityTrack(QualityScope.StationImputed, "Pressage 02", hold ? 3 : 0, hold ? 12.0 : 4.0, 50, 50, Window(22, 50, 0.04, hold ? "11" : null), hold ? QualityLevel.Warning : QualityLevel.Normal, false, null, null, null));
        foreach (var g in Project.Machine.Groups)
            tracks.Add(new QualityTrack(QualityScope.Group, g.Name, 0, g.Name == "Pressage pignon" ? 5.5 : 1.0, 100, 100, Window(50 + g.Priority, 100, 0.03), g.Name == "Pressage pignon" && hold ? QualityLevel.Warning : QualityLevel.Normal, false, null, null, null));
        for (int m = 1; m <= 6; m++)
            tracks.Add(new QualityTrack(QualityScope.Mover, "Mover " + m, 0, m == 5 ? 10.0 : 0.0, 20, 20, Window(60 + m, 20, m == 5 ? 0.1 : 0.0), m == 5 ? QualityLevel.Warning : QualityLevel.Normal, false, null, null, null));
        tracks.Add(new QualityTrack(QualityScope.Order, "OF 24-117", 0, 1.7, 600, 600, Window(70, 50, 0.017), QualityLevel.Normal, false, null, null, null));
        return new QualityStatus(tracks, false, null);
    }

    private ForceStatus BuildForces(double t, Func<double, DateTime> at, DemoUserState user)
    {
        var entries = new List<ForceEntry>();
        bool startBlocked = false;
        if (user.Declared != DeclaredState.None)
        {
            startBlocked = user.Declared == DeclaredState.PlannedMaintenance || user.Declared == DeclaredState.CorrectiveMaintenance
                || user.Declared == DeclaredState.Trials || user.Declared == DeclaredState.Cleaning;
            entries.Add(new ForceEntry("Machine", "Machine", DemoText.Declared(user.Declared), user.DeclaredReason ?? "", user.DeclaredBy, user.DeclaredSince ?? DateTime.Now, user.DeclaredUntil));
        }
        foreach (var kv in user.OutOfProduction)
            entries.Add(new ForceEntry("Station", StationNames[kv.Key], "Hors production", kv.Value, "A. Martin", user.DeclaredSince ?? DateTime.Now, null));
        if (t >= ForceFrom && t < ForceTo)
            entries.Add(new ForceEntry("Mover", "Mover 3", "Hors service", "Nid abîmé", "B. Rossi", at(ForceFrom), null));
        foreach (var kv in user.MoversOut)
            entries.Add(new ForceEntry("Mover", "Mover " + kv.Key, "Hors service", kv.Value, "A. Martin", DateTime.Now, null));
        return new ForceStatus(user.Declared, user.DeclaredReason, user.DeclaredBy, user.DeclaredSince, user.DeclaredUntil, startBlocked, entries);
    }

    private OrderStatus BuildOrders(double t, Func<double, DateTime> at)
    {
        int done = Flow.Products.Count(p => p.Finished <= t && p.Created > 0);
        int bad = Flow.Products.Count(p => p.Finished <= t && p.Bad);
        int good = done - bad;
        int inProcess = Flow.Products.Count(p => p.Created <= t && p.Finished > t);
        int qty = 600;
        var cur = new OrderInfo("24-117", "Platine P12", qty, 405 + good, 7 + bad, inProcess, OrderState.Running, at(-3 * 3600), at(-2.2 * 3600), at(16.67 * 60 * 60 / 24 + 0), 30);
        var orders = new List<OrderInfo>
        {
            cur,
            new OrderInfo("24-118", "Platine P14", 250, 0, 0, 0, OrderState.Created, at(-5000), null, null, 12),
            new OrderInfo("24-119", "Platine P12", 400, 0, 0, 0, OrderState.Created, at(-4000), null, null, 20),
            new OrderInfo("24-116", "Platine P11", 300, 300, 4, 0, OrderState.Finished, at(-86400 * 2), at(-86400 * 2 + 1200), at(-86400 * 2 + 9000), 15),
            new OrderInfo("24-115", "Platine P11", 120, 98, 22, 0, OrderState.Cancelled, at(-86400 * 3), at(-86400 * 3 + 600), at(-86400 * 3 + 2500), 6),
        };
        return new OrderStatus(cur, orders);
    }
}

/// <summary>Libellés français partagés par le scénario.</summary>
public static class DemoText
{
    public static string Declared(DeclaredState s)
    {
        switch (s)
        {
            case DeclaredState.PlannedStop: return "Arrêt planifié";
            case DeclaredState.ChangeOver: return "Changement de série";
            case DeclaredState.WaitUpstream: return "Attente amont";
            case DeclaredState.WaitDownstream: return "Attente aval";
            case DeclaredState.PlannedMaintenance: return "Maintenance planifiée";
            case DeclaredState.CorrectiveMaintenance: return "Maintenance corrective";
            case DeclaredState.Trials: return "Essais et mise au point";
            case DeclaredState.Cleaning: return "Nettoyage";
            default: return "Aucun";
        }
    }

    /// <summary>Titre court d'une étape pour les contextes d'exécution.</summary>
    public static string Title(Step step, StationConfig? cfg)
    {
        switch (step)
        {
            case ActionStep a:
            {
                var node = cfg?.FindObject(a.ObjectId);
                var def = node == null ? null : DemoCatalog.Get(node.TypeCode)?.Action(a.ActionNo);
                return (def?.Name ?? "Action") + (node != null ? " (" + node.Name + ")" : "");
            }
            case FunctionStep f:
            {
                var node = cfg?.FindObject(f.ModuleObjectId);
                var def = node == null ? null : DemoCatalog.Get(node.TypeCode)?.Function(f.FunctionId);
                return (def?.Name ?? "Fonction") + (node != null ? " (" + node.Name + ")" : "");
            }
            case WaitConditionStep: return "Attendre condition";
            case WaitTimeStep: return "Attendre durée";
            case SetStep: return "Définir";
            default: return step.Label ?? "Étape";
        }
    }
}
