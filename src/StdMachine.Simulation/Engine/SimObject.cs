using StdMachine.Core;
using StdMachine.Core.Catalog;
using StdMachine.Core.Compiler;

namespace StdMachine.Simulation.Engine;

public enum ObjState
{
    NotInitialized,
    Ready,
    Busy,
    Error,
    Disabled,
}

public enum ActStatus
{
    Idle,
    Busy,
    Done,
    Error,
}

public enum StartResult
{
    Ok,
    NotConfigured,
    Disabled,
    Busy,
    UnknownAction,
    ManualOnly,
    NotInitialized,
    InvalidParam,
    Interlocked,
    InError,
}

public enum ParamKind
{
    Unused,
    Real,
    Position,
    SpeedIndex,
    SpeedPercent,
    Text,
}

/// <summary>Paramètres résolus d'une action (ST_ActParams).</summary>
public sealed class ActParams
{
    public ParamKind[] Kind { get; } = new ParamKind[8];
    public double[] Real { get; } = new double[8];
    public Pos Pos { get; set; }
    public bool HasPos { get; set; }
    public string Text { get; set; } = string.Empty;
    public uint TimeoutMs { get; set; }
}

/// <summary>Résultats d'une action (ST_ActResults) : r1..r8.</summary>
public sealed class ActResults
{
    public int Count { get; set; }
    public double[] Real { get; } = new double[8];
    public Pos Pos { get; set; }
    public bool HasPos { get; set; }
    public string Text { get; set; } = string.Empty;
    /// <summary>Indice 1..8 du résultat de type position ou texte, 0 si aucun.</summary>
    public int PosResult { get; set; }
    public int TextResult { get; set; }
}

/// <summary>
/// Objet simulé : le contrat de <c>FB_ObjectBase</c> (docs/04 § 7) et le comportement simulé de chaque type (docs/03).
/// Un seul modèle générique, piloté par la configuration compilée et par le catalogue.
/// </summary>
public sealed class SimObject
{
    private readonly CatalogType? _type;
    private readonly SimRandom _rng;
    private readonly Func<double> _now;

    // Action en cours
    private double _durationMs;
    private bool _halted;
    private double _positionFrom, _positionTo;
    private bool _indefinite;
    private Action? _complete;

    public SimObject(CObjConfig cfg, CatalogType? type, int station, SimRandom rng, Func<double> now)
    {
        Cfg = cfg; _type = type; Station = station; _rng = rng; _now = now;
        TypeCode = type?.Code ?? (cfg.Type == 100 ? "Group" : cfg.Type == 101 ? "CartesianRobot" : "?");
        Name = cfg.Name;
        State = cfg.Disabled ? ObjState.Disabled : NeedsInit ? ObjState.NotInitialized : ObjState.Ready;
        InitialiseValues();
    }

    public CObjConfig Cfg { get; }
    public int Station { get; }
    public string TypeCode { get; }
    public string Name { get; }
    public SimObject? Parent { get; set; }
    public List<SimObject> Children { get; } = new List<SimObject>();
    public int LocalId => Cfg.LocalId;

    public ObjState State { get; private set; }
    public ActStatus Status { get; private set; }
    public int Action { get; private set; }
    public bool Manual { get; private set; }
    public double ElapsedMs { get; private set; }
    public double TimeoutMs { get; private set; }
    public double Progress { get; private set; }
    public int ErrorCode { get; private set; }
    public int SubCode { get; private set; }
    public double[] Values { get; } = new double[4];
    public ActResults Results { get; } = new ActResults();
    public ActParams Params { get; private set; } = new ActParams();
    public double EffOverride { get; private set; } = 100;
    public bool OverrideHalted => _halted;
    public int Interlock { get; private set; }
    public long ActionCount { get; private set; }
    public long ErrorCount { get; private set; }

    /// <summary>Verrouillage en défaut pour une action (0 = aucun) ; posé par le séquenceur de station.</summary>
    public Func<int, bool, int>? InterlockCheck { get; set; }

    /// <summary>Taux d'échec injecté (0..1) : l'action se termine en erreur 100 avec cette probabilité.</summary>
    public double FailureRate { get; set; }

    /// <summary>Entrées forcées par la HMI en mode manuel (signal → valeur).</summary>
    public Dictionary<int, double> ForcedInputs { get; } = new Dictionary<int, double>();

    public bool Simulated => Cfg.Simulated;

    public bool NeedsInit => TypeCode == "LinearAxis" || TypeCode == "RotaryAxis" || TypeCode == "AsyrilEyePlus" || TypeCode == "ExternalRobot";

    public bool IsMotion(int action) => _type?.Action(action)?.Motion ?? false;

    private double P(int no) => Cfg.Params[no - 1];

    // ------------------------------------------------------------------ valeurs initiales

    private void InitialiseValues()
    {
        switch (TypeCode)
        {
            case "Cylinder": Values[0] = 1; break; // Repos
            case "Gripper": Values[0] = 1; break; // Ouverte
            case "LinearAxis":
            case "RotaryAxis": Values[0] = P(8); break;
            case "Counter": Values[0] = P(1); break;
        }
    }

    // ------------------------------------------------------------------ contrat

    public StartResult Start(int action, ActParams p, bool manual)
    {
        if (State == ObjState.Disabled) return StartResult.Disabled;
        if (State == ObjState.Busy) return StartResult.Busy;
        if (State == ObjState.Error) return StartResult.InError;
        var def = _type?.Action(action);
        if (def == null) return StartResult.UnknownAction;
        if (def.ManualOnly && !manual) return StartResult.ManualOnly;
        if (State == ObjState.NotInitialized && !AllowedWithoutInit(action)) return StartResult.NotInitialized;
        int lockNo = def.Motion && InterlockCheck != null ? InterlockCheck(action, true) : 0;
        Interlock = lockNo;
        if (lockNo != 0) { Fail(2, lockNo); return StartResult.Interlocked; }

        Params = p; Action = action; Manual = manual;
        Results.Count = 0; Results.HasPos = false; Results.PosResult = 0; Results.TextResult = 0; Results.Text = string.Empty;
        Array.Clear(Results.Real, 0, 8);
        ElapsedMs = 0; Progress = 0; _halted = false; _indefinite = false; _complete = null;
        ErrorCode = 0; SubCode = 0;
        ActionCount++;
        TimeoutMs = EffectiveTimeout(def, p);

        var failure = Begin(action, def, p);
        if (failure != 0) { Fail(failure.Code, failure.Sub); return StartResult.Ok; }
        if (_durationMs <= 0 && !_indefinite)
        {
            _complete?.Invoke();
            Status = ActStatus.Done; // action immédiate : terminée dès Start
            State = ObjState.Ready;
            Progress = 1;
            return StartResult.Ok;
        }
        State = ObjState.Busy;
        Status = ActStatus.Busy;
        return StartResult.Ok;
    }

    private bool AllowedWithoutInit(int action)
    {
        switch (TypeCode)
        {
            case "LinearAxis":
            case "RotaryAxis": return action == 1 || action == 2 || action == 3 || action == 7 || action == 8 || action == 9 || action == 10;
            case "AsyrilEyePlus":
            case "ExternalRobot": return action == 1;
            default: return true;
        }
    }

    /// <summary>Un cycle de simulation ; <paramref name="overridePercent"/> : override effectif de cet objet (0..100).</summary>
    public void Cycle(double dtMs, double overridePercent)
    {
        EffOverride = overridePercent;
        if (State != ObjState.Busy) return;
        bool motion = IsMotion(Action);
        _halted = motion && overridePercent <= 0.0001;
        if (_halted) return; // à 0 %, le mouvement et le timeout sont suspendus
        double factor = motion ? Math.Min(1.0, overridePercent / 100.0) : 1.0;
        ElapsedMs += dtMs;
        double progressed = dtMs * factor;
        _progressMs += progressed;
        // Le timeout effectif est le nominal ÷ override (docs/02 § 13).
        double effTimeout = TimeoutMs > 0 ? TimeoutMs / (motion ? Math.Max(factor, 0.01) : 1.0) : 0;
        if (effTimeout > 0 && ElapsedMs > effTimeout && !_indefinite) { Fail(1, Action); return; }
        if (_indefinite) { UpdateAxis(); return; }
        Progress = _durationMs <= 0 ? 1 : Math.Min(1.0, _progressMs / _durationMs);
        UpdateAxis();
        if (_progressMs >= _durationMs)
        {
            Progress = 1;
            if (FailureRate > 0 && _rng.Chance(FailureRate)) { Fail(100, Action); return; }
            _complete?.Invoke();
            if (State == ObjState.Error) return;
            Status = ActStatus.Done;
            State = ObjState.Ready;
        }
    }

    private double _progressMs;

    public void Abort()
    {
        if (State == ObjState.Busy)
        {
            // Position sûre ou arrêt : le mouvement s'arrête là où il est.
            if (TypeCode == "LinearAxis" || TypeCode == "RotaryAxis") Values[0] = CurrentAxisPosition();
            if (TypeCode == "Cylinder") Values[0] = 3;
            Status = ActStatus.Idle;
            State = NeedsInit && Values[3] == 0 && IsAxis ? ObjState.NotInitialized : ObjState.Ready;
        }
        _indefinite = false;
        _complete = null;
    }

    public void Ack()
    {
        if (State == ObjState.Error)
        {
            State = Cfg.Disabled ? ObjState.Disabled : (NeedsInit && !_initialised ? ObjState.NotInitialized : ObjState.Ready);
            Status = ActStatus.Idle;
            ErrorCode = 0; SubCode = 0;
        }
    }

    /// <summary>Le séquenceur a lu les résultats : l'objet revient à l'état d'attente d'une nouvelle action.</summary>
    public void AcknowledgeDone()
    {
        if (Status == ActStatus.Done) Status = ActStatus.Idle;
    }

    public string ActionName(int action) => _type?.Action(action)?.Name ?? ("Action " + action);

    /// <summary>Texte d'une erreur : propre au type (à partir de 100) ou commune.</summary>
    public string ErrorTextOf(int code) => (_type?.Errors.FirstOrDefault(e => e.Code == code)?.Text) ?? CommonText(code);

    private static string CommonText(int code) => code switch
    {
        1 => "Timeout de l'action", 2 => "Verrouillage actif", 3 => "Objet non prêt", 4 => "Action inconnue pour ce type", 5 => "Paramètre d'action invalide",
        6 => "Action interrompue par Abort", 7 => "Objet désactivé", 8 => "Communication perdue", 9 => "Objet non initialisé", 10 => "Matériel non raccordé",
        11 => "Action réservée au mode manuel", _ => "Erreur " + code,
    };

    /// <summary>Le jugement du résultat <paramref name="resultNo"/> de l'action est-il « Bon » ? (docs/05 § 3.5)</summary>
    public bool ResultIsGood(int action, int resultNo)
    {
        var r = _type?.Action(action)?.Results.ElementAtOrDefault(resultNo - 1);
        double v = Results.Real[resultNo - 1];
        if (r?.Values != null && (int)v >= 0 && (int)v < r.Values.Count) return r.Values[(int)v] == "Bon";
        return v != 0;
    }

    public void InjectError(int code, int sub = 0)
    {
        if (State == ObjState.Disabled) return;
        Fail(code, sub);
    }

    /// <summary>Reprend l'état initial (remise à zéro du simulateur ou nouveau déploiement).</summary>
    public void ResetState()
    {
        State = Cfg.Disabled ? ObjState.Disabled : NeedsInit ? ObjState.NotInitialized : ObjState.Ready;
        Status = ActStatus.Idle; Action = 0; ErrorCode = 0; SubCode = 0; _initialised = false;
        Array.Clear(Values, 0, 4);
        InitialiseValues();
    }

    private bool _initialised;

    private bool IsAxis => TypeCode == "LinearAxis" || TypeCode == "RotaryAxis";

    private void Fail(int code, int sub)
    {
        State = ObjState.Error;
        Status = ActStatus.Error;
        ErrorCode = code; SubCode = sub;
        ErrorCount++;
        _complete = null;
        _indefinite = false;
    }

    // ------------------------------------------------------------------ timeout

    private double EffectiveTimeout(CatalogAction def, ActParams p)
    {
        if (p.TimeoutMs > 0) return p.TimeoutMs;
        int k = Action - 1;
        if (k >= 0 && k < Cfg.TimeoutMs.Length && Cfg.TimeoutMs[k] > 0) return Cfg.TimeoutMs[k];
        if (def.NoTimeout || def.Immediate) return 0;
        if (def.TimeoutParam != null) return P(def.TimeoutParam.Value);
        if (def.TimeoutCalc == "axisMove") return AxisMoveTimeout(p);
        double t = def.TimeoutMs ?? 0;
        if (def.AddsDuration) t += DurationArg(def, p);
        return t;
    }

    private double DurationArg(CatalogAction def, ActParams p)
    {
        for (int i = 0; i < def.Params.Count && i < 8; i++)
            if (def.Params[i].Type == "Duration" && p.Kind[i] == ParamKind.Real) return p.Real[i];
        for (int i = 0; i < def.Params.Count && i < 8; i++)
            if (def.Params[i].Type == "Duration" && def.Params[i].Default.HasValue)
            {
                var d = def.Params[i].Default!.Value;
                if (d.ValueKind == System.Text.Json.JsonValueKind.String) return P(int.Parse(d.GetString()!.Substring(1)));
            }
        return 0;
    }

    // ------------------------------------------------------------------ axes

    private double AxisSpeedPercent(ActParams p, int index)
    {
        if (p.Kind[index] == ParamKind.SpeedPercent) return p.Real[index];
        if (p.Kind[index] == ParamKind.SpeedIndex)
        {
            int n = (int)p.Real[index];
            double v = n >= 1 && n <= 8 ? Cfg.SpeedPercent[n - 1] : 0;
            if (v <= 0) v = n == 1 ? 10 : n == 2 ? 80 : 50;
            return v;
        }
        return 50;
    }

    private double TargetOf(ActParams p)
    {
        int comp = IsAxis ? (TypeCode == "RotaryAxis" ? 3 : (int)P(13)) : 0;
        if (p.Kind[0] == ParamKind.Position && p.HasPos) return p.Pos.Component(comp) + p.Real[0];
        return p.Real[0];
    }

    private static double TrapezoidMs(double distance, double vmax, double acc, double dec)
    {
        if (distance <= 0) return 0;
        vmax = Math.Max(vmax, 1e-6); acc = Math.Max(acc, 1e-6); dec = Math.Max(dec, 1e-6);
        double dAcc = vmax * vmax / (2 * acc), dDec = vmax * vmax / (2 * dec);
        if (dAcc + dDec <= distance) return (distance - dAcc - dDec) / vmax * 1000 + (vmax / acc + vmax / dec) * 1000;
        // Profil triangulaire
        double vpeak = Math.Sqrt(2 * distance * acc * dec / (acc + dec));
        return (vpeak / acc + vpeak / dec) * 1000;
    }

    private double AxisMoveTimeout(ActParams p)
    {
        double target = Action == 5 ? Values[0] + p.Real[0] : TargetOf(p);
        double dist = Math.Abs(target - Values[0]);
        double vnom = P(1) * AxisSpeedPercent(p, 1) / 100.0;
        return (2 * (dist / Math.Max(vnom, 1e-6) + vnom / Math.Max(P(2), 1e-6)) + 1) * 1000;
    }

    private double CurrentAxisPosition() => _positionFrom + (_positionTo - _positionFrom) * Math.Min(1.0, Math.Max(0.0, Progress));

    private void UpdateAxis()
    {
        if (IsAxis && State == ObjState.Busy && (Action == 4 || Action == 5 || Action == 3)) Values[0] = CurrentAxisPosition();
        if (IsAxis && State == ObjState.Busy && (Action == 9 || Action == 10))
        {
            double v = P(1) * AxisSpeedPercent(Params, 0) / 100.0 * (Action == 9 ? 1 : -1) * Math.Min(1.0, EffOverride / 100.0) * P(12) / 100.0;
            Values[0] = Math.Max(P(5), Math.Min(P(6), Values[0] + v * 0.01));
            Values[1] = Math.Abs(v);
        }
    }

    // ------------------------------------------------------------------ comportement des types

    private readonly struct Failure
    {
        public readonly int Code, Sub;
        public Failure(int code, int sub = 0) { Code = code; Sub = sub; }
        public static bool operator ==(Failure f, int zero) => f.Code == zero;
        public static bool operator !=(Failure f, int zero) => f.Code != zero;
        public override bool Equals(object? obj) => obj is Failure f && f.Code == Code;
        public override int GetHashCode() => Code;
        public static implicit operator Failure(int code) => new Failure(code);
    }

    private void Result(int no, double value)
    {
        Results.Real[no - 1] = value;
        if (Results.Count < no) Results.Count = no;
    }

    private Failure Begin(int action, CatalogAction def, ActParams p)
    {
        switch (TypeCode)
        {
            case "DigitalInput":
                if (action == 1) { Result(1, ReadInput(1)); return 0; }
                _durationMs = 300; // « Attendre » se termine après 300 ms si personne ne force
                _complete = () => { };
                return 0;
            case "DigitalOutput":
                Values[0] = action == 1 ? 1 : action == 2 ? 0 : Values[0];
                if (action == 3) { _durationMs = p.Kind[0] == ParamKind.Real ? p.Real[0] : P(2); _complete = () => Values[0] = 0; Values[0] = 1; }
                return 0;
            case "AnalogInput":
                Result(1, ReadInput(1)); if (action == 3) Result(2, 1); else if (action == 2) _durationMs = 200;
                return 0;
            case "AnalogOutput":
                if (action == 1) Values[0] = p.Real[0]; else if (action == 3) Values[0] = P(8); else { _durationMs = p.Real[1]; var t = p.Real[0]; _complete = () => Values[0] = t; }
                return 0;
            case "Cylinder": return BeginCylinder(action);
            case "LinearAxis":
            case "RotaryAxis": return BeginAxis(action, p);
            case "PlanarSystem":
                _durationMs = action == 1 ? 1500 : action == 2 ? 500 : 200;
                if (action == 1) { Values[0] = 0; _complete = () => { Values[0] = 1; Result(1, Values[1]); }; }
                if (action == 2) _complete = () => Values[0] = 0;
                return 0;
            case "PlanarMover":
                _durationMs = action == 1 ? 1000 : action == 2 ? 100 : 0;
                if (action == 3) { }
                return 0;
            case "VacuumBlow": return BeginVacuum(action, p);
            case "Gripper": return BeginGripper(action);
            case "Press": return BeginPress(action, p);
            case "AsyrilEyePlus": return BeginVision(action);
            case "KeyenceIV":
                if (action == 2) { _durationMs = 250; _complete = () => { Result(1, _rng.Chance(0.95) ? 1 : 0); Result(2, 0); }; }
                else if (action == 1) _durationMs = 100;
                else { Result(1, Results.Real[0]); }
                return 0;
            case "KeyenceGT2":
                if (action == 1)
                {
                    _durationMs = P(6) + 150;
                    _complete = () =>
                    {
                        double v = _rng.Gauss(P(2), Math.Abs(P(3)) / 3);
                        Result(1, v);
                        Result(2, v > P(2) + P(3) ? 0 : v < P(2) + P(4) ? 2 : 1);
                        Values[0] = v;
                    };
                }
                else if (action == 2) _durationMs = 100;
                else Result(1, Values[0]);
                return 0;
            case "CodeReader":
                if (action == 1 || action == 2)
                {
                    _durationMs = 200;
                    _complete = () => { Results.Text = "MX4-" + DateTime.UtcNow.ToString("yyyyMMdd") + "-" + (++_codeCounter).ToString("000000"); Results.TextResult = 1; Result(1, 0); Result(2, 98); if (action == 2) Result(2, 1); };
                }
                return 0;
            case "Counter": return BeginCounter(action, p);
            case "Magazine":
                _durationMs = action == 1 || action == 2 ? 600 : 0;
                if (action == 3) { Result(1, 0); Result(2, 1 + _casesTaken); Results.PosResult = 1; Results.HasPos = true; Results.Pos = new Pos(0, 0, 0, 0); }
                if (action == 4) _casesTaken++;
                if (action == 5) { Result(1, _casesTaken); Result(2, 24 - _casesTaken); Result(3, _casesTaken >= 24 ? 1 : 0); }
                return 0;
            case "VibratoryFeeder":
                _durationMs = action == 3 ? 1000 : action == 4 ? 0 : (p.Kind[1] == ParamKind.Real && p.Real[1] > 0 ? p.Real[1] : 300);
                if (action == 4) Result(1, 1);
                return 0;
            case "Beacon":
            case "Safety":
            case "Light":
            case "Timer":
                if (TypeCode == "Timer") _durationMs = p.Kind[0] == ParamKind.Real ? p.Real[0] : P(1);
                else if (TypeCode == "Light" && action == 4) _durationMs = p.Real[0] > 0 ? p.Real[0] : P(3);
                else if (TypeCode == "Safety") Result(1, 1);
                return 0;
            default:
                // Types sans modèle propre : l'action se termine après un délai court avec des résultats « bons ».
                double nominal = def.TimeoutMs ?? 0;
                _durationMs = def.Immediate ? 0 : Math.Max(50, Math.Min(500, nominal / 10));
                int rn = 0;
                foreach (var r in def.Results) { rn++; Result(rn, r.Type == "Bool" ? 1 : r.Type == "Enum" ? 1 : 0); }
                return 0;
        }
    }

    private int _codeCounter;
    private int _casesTaken;

    private double ReadInput(int signal) => ForcedInputs.TryGetValue(signal, out var v) ? v : 0;

    private Failure BeginCylinder(int action)
    {
        if (action == 3) { Result(1, Values[0]); return 0; }
        double target = action == 1 ? 2 : 1;
        _durationMs = P(3) > 0 ? P(3) : 300;
        Values[0] = 3;
        _complete = () => Values[0] = target;
        return 0;
    }

    private Failure BeginAxis(int action, ActParams p)
    {
        _positionFrom = Values[0]; _positionTo = Values[0];
        switch (action)
        {
            case 1: _durationMs = 200; _complete = () => Values[2] = 1; return 0;
            case 2: _durationMs = 200; _complete = () => { Values[2] = 0; Values[1] = 0; }; return 0;
            case 3:
                if (Values[2] == 0) return new Failure(105);
                _positionTo = P(8); _durationMs = 1500;
                _complete = () => { Values[0] = P(8); Values[3] = 1; _initialised = true; };
                return 0;
            case 4:
            case 5:
                {
                    if (Values[2] == 0) return new Failure(105);
                    if (Values[3] == 0) return new Failure(100);
                    double target = action == 4 ? TargetOf(p) : Values[0] + p.Real[0];
                    if (P(7) != 0 && (target < P(5) - 1e-9 || target > P(6) + 1e-9)) return new Failure(101);
                    int si = action == 4 ? 1 : 1;
                    double speedPct = AxisSpeedPercent(p, si);
                    double accFactor = action == 4 && p.Kind[2] == ParamKind.Real && p.Real[2] > 0 ? p.Real[2] / 100.0 : 1.0;
                    double v = P(1) * speedPct / 100.0;
                    _positionFrom = Values[0]; _positionTo = target;
                    _durationMs = TrapezoidMs(Math.Abs(target - Values[0]), v, P(2) * accFactor, P(3) * accFactor);
                    Values[1] = v;
                    _complete = () => { Values[0] = target; Values[1] = 0; Result(1, target); };
                    return 0;
                }
            case 6: _durationMs = 100; _complete = () => Values[1] = 0; return 0;
            case 7: _durationMs = 100; return 0;
            case 8: Result(1, Values[0]); return 0;
            case 9:
            case 10:
                _indefinite = true;
                return 0;
        }
        return 0;
    }

    private Failure BeginVacuum(int action, ActParams p)
    {
        switch (action)
        {
            case 1:
                Values[1] = 0; Values[0] = 1;
                _durationMs = p.Kind[0] == ParamKind.Real && p.Real[0] == 0 ? 20 : 150;
                _complete = () => { bool present = !_rng.Chance(FailureRate); Values[3] = present ? 1 : 0; Values[2] = present ? P(1) - 5 : -2; Result(1, present ? 1 : 0); };
                return 0;
            case 2:
                Values[0] = 0; Values[1] = 1;
                _durationMs = p.Kind[0] == ParamKind.Real && p.Real[0] > 0 ? p.Real[0] : P(3);
                _complete = () => { Values[1] = 0; Values[3] = 0; Values[2] = 0; };
                return 0;
            case 3: Values[0] = 0; Values[1] = 0; Values[2] = 0; Values[3] = 0; return 0;
            default: Result(1, Values[3]); return 0;
        }
    }

    private Failure BeginGripper(int action)
    {
        switch (action)
        {
            case 1: _durationMs = P(2); _complete = () => { Values[0] = 1; Values[1] = 0; }; return 0;
            case 2: _durationMs = P(2); _complete = () => Values[0] = 2; return 0;
            case 3: _durationMs = P(2); _complete = () => { Values[0] = 3; Values[1] = 1; Result(1, 1); }; return 0;
            default: Result(1, Values[1]); return 0;
        }
    }

    private Failure BeginPress(int action, ActParams p)
    {
        switch (action)
        {
            case 1:
                {
                    _durationMs = 1800; Values[2] = p.Real[0];
                    bool good = !_rng.Chance(FailureRate) && _rng.Chance(0.97);
                    double lo = RecipeForceMin, hi = RecipeForceMax;
                    double mid = (lo + hi) / 2, span = Math.Max(1, hi - lo);
                    double force = good ? mid + (_rng.Next() - 0.5) * span * 0.7 : (_rng.Chance(0.5) ? lo - span * 0.5 : hi + span * 0.5);
                    _complete = () =>
                    {
                        Values[0] = force; Values[1] = 4.2;
                        Result(1, good ? 1 : 0); Result(2, force); Result(3, 4.2); Result(4, force + 8);
                    };
                    if (force > P(1)) return new Failure(100);
                    return 0;
                }
            case 2: _durationMs = 800; _complete = () => { Values[0] = 0; Values[1] = 0; }; return 0;
            case 3: _durationMs = 200; return 0;
            default: Result(1, Values[0]); Result(2, Values[1]); return 0;
        }
    }

    /// <summary>Fenêtre de force de la recette exécutée (renseignée par le simulateur de station).</summary>
    public double RecipeForceMin { get; set; } = 800;

    public double RecipeForceMax { get; set; } = 900;

    private Failure BeginVision(int action)
    {
        switch (action)
        {
            case 1: _durationMs = 500; _complete = () => _initialised = true; return 0;
            case 2: _durationMs = 100; return 0;
            case 3:
                _durationMs = 400;
                if (_rng.Chance(0.05) || _rng.Chance(FailureRate)) return new Failure(101);
                _complete = () => { Values[0] = 1 + (int)(_rng.Next() * 6); Result(1, Values[0]); };
                return 0;
            default:
                _durationMs = 100;
                _complete = () => { Results.HasPos = true; Results.PosResult = 1; Results.Pos = new Pos(_rng.Between(180, 240), _rng.Between(90, 150), -24.8, _rng.Between(-30, 30), 2); };
                return 0;
        }
    }

    private Failure BeginCounter(int action, ActParams p)
    {
        switch (action)
        {
            case 1:
                {
                    double step = p.Kind[0] == ParamKind.Real ? p.Real[0] : 1;
                    Values[0] += step;
                    if (P(2) > 0 && Values[0] > P(2)) return new Failure(100);
                    Result(1, Values[0]); return 0;
                }
            case 2: Result(1, Values[0]); return 0;
            case 3: Values[0] = P(1); return 0;
            default: Result(1, Values[0] >= p.Real[0] ? 1 : 0); return 0;
        }
    }
}
