using StdMachine.Core;

namespace StdMachine.Simulation.Engine;

// Simulateur fidèle du PLC (docs/05, docs/06 § 8). Le temps est discret : chaque appel de Step avance l'horloge simulée.
// Le simulateur exécute les tables compilées (CompiledTables), jamais le projet.

public struct Pos
{
    public double X, Y, Z, C;
    public int Frame;

    public Pos(double x, double y, double z, double c, int frame = 0) { X = x; Y = y; Z = z; C = c; Frame = frame; }

    public double Component(int index) => index == 0 ? X : index == 1 ? Y : index == 2 ? Z : C;
}

public enum PartStatus
{
    None,
    InProcess,
    Good,
    Bad,
    Exited,
    Rejected,
    Removed,
}

/// <summary>Pièce sur un emplacement de mover (ST_Part).</summary>
public sealed class Part
{
    public const int MaxData = 33;

    public bool Present { get; set; }
    public string Id { get; set; } = string.Empty;
    public int Order { get; set; }
    public PartStatus Status { get; set; }
    public bool BadHandled { get; set; }
    /// <summary>Verdict déjà mauvais à l'arrivée dans la station courante.</summary>
    public bool BadAtArrival { get; set; }
    /// <summary>Mauvaise à cause d'un défaut de station (non comptée dans les taux).</summary>
    public bool DefectBad { get; set; }
    public double[] Data { get; } = new double[MaxData];
    public List<PartOp> Ops { get; } = new List<PartOp>();
    public double CreatedMs { get; set; }

    public void Clear()
    {
        Present = false; Id = string.Empty; Order = 0; Status = PartStatus.None; BadHandled = false; BadAtArrival = false; DefectBad = false; CreatedMs = 0;
        Array.Clear(Data, 0, Data.Length);
        Ops.Clear();
    }
}

public sealed record PartOp(int Station, int Group, int VerdictAfter);

/// <summary>Générateur pseudo-aléatoire reproductible (graine fixe : les tests et les captures sont stables).</summary>
public sealed class SimRandom
{
    private Random _r;

    public SimRandom(int seed) { _r = new Random(seed); }

    public void Reseed(int seed) { _r = new Random(seed); }

    public double Next() => _r.NextDouble();

    public bool Chance(double probability) => _r.NextDouble() < probability;

    public double Between(double min, double max) => min + (max - min) * _r.NextDouble();

    /// <summary>Approximation gaussienne (Box-Muller).</summary>
    public double Gauss(double mean, double sigma)
    {
        double u1 = 1.0 - _r.NextDouble(), u2 = _r.NextDouble();
        return mean + sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}

/// <summary>Alarme du simulateur (docs/04 § 10) : code, objet, gravité, horodatage simulé.</summary>
public sealed class SimAlarm
{
    public int Id { get; set; }
    public int Station { get; set; }
    public string? Object { get; set; }
    public int Code { get; set; }
    public int SubCode { get; set; }
    public string Text { get; set; } = string.Empty;
    public AlarmSeverity Severity { get; set; }
    public double RaisedMs { get; set; }
    public double? ClearedMs { get; set; }
    public bool Acknowledged { get; set; }
    public string? Context { get; set; }

    public bool Active => ClearedMs == null;
}

public sealed class AlarmLog
{
    private int _next = 1;
    public List<SimAlarm> All { get; } = new List<SimAlarm>();

    public SimAlarm Raise(int station, string? obj, int code, int sub, string text, AlarmSeverity severity, double nowMs, string? context = null)
    {
        // Une alarme active identique (même station, objet, code) ne se lève qu'une fois.
        var existing = All.FirstOrDefault(a => a.Active && a.Station == station && a.Object == obj && a.Code == code && a.SubCode == sub);
        if (existing != null) return existing;
        var a = new SimAlarm { Id = _next++, Station = station, Object = obj, Code = code, SubCode = sub, Text = text, Severity = severity, RaisedMs = nowMs, Context = context };
        All.Add(a);
        return a;
    }

    public void Clear(SimAlarm a, double nowMs)
    {
        if (a.ClearedMs == null) a.ClearedMs = nowMs;
    }

    public void Downgrade(SimAlarm a) => a.Severity = AlarmSeverity.Information;

    public IEnumerable<SimAlarm> ActiveOf(int station) => All.Where(a => a.Active && a.Station == station);
}
