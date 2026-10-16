using StdMachine.Core;

namespace StdMachine.Simulation.Demo;

public sealed record StepTiming(Guid StepId, Step Step, double Start, double End, bool Taken);

/// <summary>Chronologie estimée d'un process (catalogue + dépliage des blocs) : sert à animer les cartes du scénario.</summary>
public sealed class StepSchedule
{
    public List<StepTiming> Timings { get; } = new List<StepTiming>();
    public double Total { get; private set; }

    public static StepSchedule Build(ProcessDefinition process, StationConfig? station, bool good, double waitExtra)
    {
        var s = new StepSchedule();
        s.Total = s.Place(process.Body, 0, true, station, good, waitExtra);
        return s;
    }

    public StepTiming? Of(Guid id) => Timings.FirstOrDefault(t => t.StepId == id);

    private double Place(Sequence seq, double t, bool taken, StationConfig? station, bool good, double waitExtra)
    {
        foreach (var step in seq.Steps)
        {
            switch (step)
            {
                case ParallelBlock pb:
                {
                    double end = t;
                    foreach (var branch in pb.Branches) end = Math.Max(end, Place(branch, t, taken, station, good, waitExtra));
                    Timings.Add(new StepTiming(pb.Id, pb, t, end, taken));
                    t = end;
                    break;
                }
                case IfBlock ib:
                {
                    double endThen = Place(ib.Then, t, taken && good, station, good, waitExtra);
                    double endElse = Place(ib.Else, t, taken && !good, station, good, waitExtra);
                    double end = Math.Max(endThen, endElse);
                    Timings.Add(new StepTiming(ib.Id, ib, t, end, taken));
                    t = end;
                    break;
                }
                case RepeatBlock rb:
                {
                    double end = Place(rb.Body, t, taken, station, good, waitExtra);
                    Timings.Add(new StepTiming(rb.Id, rb, t, end, taken));
                    t = end;
                    break;
                }
                default:
                {
                    double d = taken ? DemoCatalog.EstimatedSeconds(step, station) : 0;
                    if (taken && step is WaitConditionStep) d += waitExtra;
                    Timings.Add(new StepTiming(step.Id, step, t, t + d, taken));
                    t += d;
                    break;
                }
            }
        }
        return t;
    }
}

/// <summary>Visite d'une station par un mover : arrivée, début et fin de l'opération, départ.</summary>
public sealed class Visit
{
    public int Product { get; set; }
    public int Mover { get; set; }
    public int Station { get; set; }
    public string Group { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public Guid ProcessId { get; set; }
    public StepSchedule Schedule { get; set; } = null!;
    public double Arrive { get; set; }
    public double Start { get; set; }
    public double End { get; set; }
    public double Leave { get; set; }
    public bool Aborted { get; set; }
    public double WaitExtra { get; set; }
    public bool Good { get; set; } = true;
    public bool RetryFirst { get; set; }
    public int GeneralStep { get; set; }
    public string PartId { get; set; } = string.Empty;
    public double Duration => End - Start;
}

public sealed record Seg(double T0, double T1, double X0, double Y0, double X1, double Y1, MoverState State, int? Station, int GeneralStep, string Label, int Product, int QueueFor);

public sealed class Product
{
    public int N { get; set; }
    public int Mover { get; set; }
    public string PartId { get; set; } = string.Empty;
    public double Created { get; set; }
    public double PressedAt { get; set; }
    public double Finished { get; set; }
    public bool Bad { get; set; }
    public bool Rejected { get; set; }
    public List<Visit> Visits { get; } = new List<Visit>();
}

/// <summary>Fenêtre d'indisponibilité d'une station (défaut ou arrêt qualité).</summary>
public sealed record StationWindow(int Station, double From, double To, bool Abort, double ClearEnd);

public sealed class DemoFlow
{
    public List<Product> Products { get; } = new List<Product>();
    public List<Visit> Visits { get; } = new List<Visit>();
    public List<Seg>[] MoverSegs { get; } = Enumerable.Range(0, 7).Select(_ => new List<Seg>()).ToArray();
    public (double X, double Y)[] Idle { get; } = new (double, double)[7];
    public List<StationWindow> Windows { get; } = new List<StationWindow>();

    private readonly MachineProject _project;
    private readonly Dictionary<int, (double X, double Y)> _work = new Dictionary<int, (double, double)>();
    private readonly Dictionary<int, (double X, double Y)> _queue = new Dictionary<int, (double, double)>();

    public DemoFlow(MachineProject project)
    {
        _project = project;
        foreach (var pp in project.Machine.PlanarPositions)
        {
            var st = project.Stations.FirstOrDefault(s => s.Id == pp.StationId);
            if (st == null) continue;
            if (pp.Kind == PlanarPositionKind.Work) _work[st.Number] = (pp.X, pp.Y);
            else if (pp.Kind == PlanarPositionKind.Queue) _queue[st.Number] = (pp.X, pp.Y);
        }
        var idle = project.Machine.PlanarPositions.Where(p => p.Kind == PlanarPositionKind.Buffer || p.Kind == PlanarPositionKind.Park).ToList();
        for (int i = 1; i <= 6; i++) Idle[i] = (idle[(i - 1) % idle.Count].X, idle[(i - 1) % idle.Count].Y);
    }

    public (double X, double Y) WorkPosition(int station) => _work[station];

    private static double Travel((double X, double Y) a, (double X, double Y) b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return 0.6 + Math.Sqrt(dx * dx + dy * dy) / 170.0;
    }

    private StationConfig Station(int n) => _project.Station(n)!;

    private Visit Plan(Product prod, string group, string opName, string processKey, int[] stations, double leaveT, (double X, double Y) from,
        double[] stationFree, bool good, double waitExtra)
    {
        Visit? best = null;
        foreach (int st in stations)
        {
            var cfg = Station(st);
            var proc = cfg.Processes.First(p => p.Kind == ProcessKind.Operation && p.Name == opName);
            double dur = StepSchedule.Build(proc, cfg, good, waitExtra).Total;
            double arrive = leaveT + Travel(from, _queue[st]);
            double start = Math.Max(arrive + 0.9, stationFree[st]);
            bool aborted = false;
            double abortAt = 0, clearEnd = 0;
            foreach (var w in Windows.Where(w => w.Station == st))
            {
                if (start >= w.From && start < w.To) start = w.To;
                else if (w.Abort && start < w.From && start + dur > w.From) { aborted = true; abortAt = w.From; clearEnd = w.ClearEnd; }
            }
            // Un défaut qui tombe pendant l'opération la coupe ; une fenêtre qui commence avant le début reste respectée ci-dessus.
            var v = new Visit
            {
                Product = prod.N, Mover = prod.Mover, Station = st, Group = group, Operation = opName, ProcessId = proc.Id,
                Arrive = arrive, Start = start, End = aborted ? abortAt : start + dur, Good = good, WaitExtra = waitExtra,
                Aborted = aborted, Schedule = StepSchedule.Build(proc, cfg, good, waitExtra), PartId = prod.PartId,
            };
            v.Leave = aborted ? clearEnd : v.End + 0.4;
            if (best == null || v.Start < best.Start || (v.Start == best.Start && v.Station < best.Station)) best = v;
        }
        return best!;
    }

    /// <summary>Calcule les produits, leurs visites et les trajets de chaque mover (ordonnancement glouton déterministe).</summary>
    public void Build(double firstRelease, double interval, int waitProduct, double lastRelease, (double From, double To)? mover3Out)
    {
        var moverFree = new double[7];
        var stationFree = new double[7];
        int n = 0;
        while (true)
        {
            n++;
            double wanted = firstRelease + interval * (n - 1);
            if (wanted > lastRelease) break;

            // Mover disponible le plus tôt (le mover 3 est mis de côté pendant son forçage)
            int m = 0;
            double bestFree = double.MaxValue;
            for (int i = 1; i <= 6; i++)
            {
                if (i == 3 && mover3Out.HasValue && wanted > mover3Out.Value.From - 40 && wanted < mover3Out.Value.To) continue;
                if (moverFree[i] < bestFree) { bestFree = moverFree[i]; m = i; }
            }
            var prod = new Product { N = n, Mover = m, PartId = "MX4-20261016-" + (400 + n).ToString("000000") };
            double waitExtra = n == waitProduct ? 4.0 : 0.0;

            // 1. Chargement
            var cfg1 = Station(DemoProject.Chargement);
            var chProc = cfg1.Processes.First(p => p.Kind == ProcessKind.Operation);
            double chDur = StepSchedule.Build(chProc, cfg1, true, 0).Total;
            var idle = Idle[m];
            double travel = Travel(idle, _queue[DemoProject.Chargement]);
            double start = Math.Max(Math.Max(wanted, stationFree[DemoProject.Chargement]), moverFree[m] + travel + 0.9);
            double depart = start - 0.9 - travel;   // le mover quitte son parcage juste à temps
            double arrive = depart + travel;
            if (start > lastRelease + 8) break;
            var v1 = new Visit
            {
                Product = n, Mover = m, Station = DemoProject.Chargement, Group = "Chargement", Operation = "Charger", ProcessId = chProc.Id,
                Arrive = arrive, Start = start, End = start + chDur, Schedule = StepSchedule.Build(chProc, cfg1, true, 0), GeneralStep = 1, PartId = prod.PartId,
            };
            v1.Leave = v1.End + 0.4;
            stationFree[v1.Station] = v1.Leave;
            Add(prod, v1, idle, depart);
            prod.Created = v1.End;
            var pos = _work[v1.Station];
            double t = v1.Leave;

            // 2. Pressage pignon (Pressage 01 ou 02, au plus tôt)
            var pressVisit = Plan(prod, "Pressage pignon", "Presser pignon", "presser", new[] { DemoProject.Pressage01, DemoProject.Pressage02 }, t, pos, stationFree, true, waitExtra);
            pressVisit.GeneralStep = 2;
            // Rebut décidé par la règle de la maquette : les pièces de Pressage 02 pressées pendant la dérive sont mauvaises.
            if (pressVisit.Station == DemoProject.Pressage02 && pressVisit.End > 62 && pressVisit.End < 100) prod.Bad = true;
            if (pressVisit.Aborted) prod.Bad = true;
            if (prod.Bad && !pressVisit.Aborted)
                pressVisit = Plan(prod, "Pressage pignon", "Presser pignon", "presser", new[] { pressVisit.Station }, t, pos, stationFree, false, waitExtra);
            pressVisit.GeneralStep = 2;
            Complete(prod, pressVisit, stationFree, ref pos, ref t);
            prod.PressedAt = pressVisit.End;

            if (!prod.Bad)
            {
                // 3. Contrôle
                var ctrl = Plan(prod, "Contrôle", "Contrôler", "controler", new[] { DemoProject.Controle }, t, pos, stationFree, true, 0);
                ctrl.GeneralStep = 3;
                Complete(prod, ctrl, stationFree, ref pos, ref t);
                // 4. Sertissage
                var sert = Plan(prod, "Sertissage", "Sertir", "sertir", new[] { DemoProject.Pressage01, DemoProject.Pressage02 }, t, pos, stationFree, true, 0);
                sert.GeneralStep = 4;
                Complete(prod, sert, stationFree, ref pos, ref t);
                if (sert.Aborted) prod.Bad = true;
            }

            // 5. Déchargement ou rebut
            if (!prod.Bad)
            {
                var dec = Plan(prod, "Déchargement", "Décharger", "decharger", new[] { DemoProject.Dechargement }, t, pos, stationFree, true, 0);
                dec.GeneralStep = 5;
                Complete(prod, dec, stationFree, ref pos, ref t);
            }
            else
            {
                prod.Rejected = true;
                var reb = Plan(prod, "Rebut", "Évacuer", "evacuer", new[] { DemoProject.Rebut }, t, pos, stationFree, false, 0);
                reb.GeneralStep = 5;
                Complete(prod, reb, stationFree, ref pos, ref t);
            }
            prod.Finished = t;

            // Retour au parcage
            double back = Travel(pos, idle);
            MoverSegs[m].Add(new Seg(t, t + back, pos.X, pos.Y, idle.X, idle.Y, MoverState.EnRoute, null, 1, "Retour à l'étape 1", n, 0));
            moverFree[m] = t + back + 0.5;
            Products.Add(prod);
        }
    }

    private void Complete(Product prod, Visit v, double[] stationFree, ref (double X, double Y) pos, ref double t)
    {
        stationFree[v.Station] = v.Leave;
        Add(prod, v, pos, t);
        pos = _work[v.Station];
        t = v.Leave;
    }

    private void Add(Product prod, Visit v, (double X, double Y) from, double departT)
    {
        prod.Visits.Add(v);
        Visits.Add(v);
        var segs = MoverSegs[prod.Mover];
        var q = _queue[v.Station];
        var w = _work[v.Station];
        string label = StepLabel(v);
        // Trajet vers la file d'attente de la station, attente, puis entrée en position de travail
        double arrQ = v.Arrive;
        segs.Add(new Seg(departT, arrQ, from.X, from.Y, q.X, q.Y, MoverState.EnRoute, null, v.GeneralStep, label, prod.N, 0));
        double enter = v.Start - 0.9;
        if (enter > arrQ) segs.Add(new Seg(arrQ, enter, q.X, q.Y, q.X, q.Y, MoverState.Queued, v.Station, v.GeneralStep, label, prod.N, v.Station));
        else enter = arrQ;
        segs.Add(new Seg(enter, v.Start, q.X, q.Y, w.X, w.Y, MoverState.EnRoute, v.Station, v.GeneralStep, label, prod.N, 0));
        segs.Add(new Seg(v.Start, v.Leave, w.X, w.Y, w.X, w.Y, v.Aborted ? MoverState.Error : MoverState.InStation, v.Station, v.GeneralStep, label, prod.N, 0));
    }

    private static string StepLabel(Visit v) => v.Group + " › " + v.Operation;
}
