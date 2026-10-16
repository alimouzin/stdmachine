using StdMachine.Core;

namespace StdMachine.Simulation.Engine;

/// <summary>État d'une instance de portée de la surveillance des rebuts (ST_QualityTrack, docs/10 § 9).</summary>
public sealed class QualityTrackState
{
    public QualityScope Scope;
    public int Index;
    public string Name = string.Empty;
    public int Consecutive;
    public readonly bool[] Window = new bool[256];
    public int Head, Count, BadInWindow;
    public int ProbationLeft;
    public QualityLevel Level;
    public bool Locked;
    public string? Reason;
    public double? SinceMs;
    public long TotalGood, TotalBad;
    public QualityReaction LastReaction;

    public double? RatePercent(QualityRule r) => Count >= r.MinSample && Count > 0 ? 100.0 * BadInWindow / Count : (double?)null;

    /// <summary>La fenêtre en texte : un caractère par résultat, le plus récent à droite (« . » bon, « X » mauvais).</summary>
    public string WindowText(int max = 50)
    {
        int n = Math.Min(Count, max);
        var chars = new char[n];
        for (int i = 0; i < n; i++) chars[i] = Window[(Head - n + i + 512) % 256] ? 'X' : '.';
        return new string(chars);
    }
}

/// <summary>FB_QualityMonitor simulé (docs/10 § 9) : fenêtres glissantes, successifs, réactions graduées, libération.</summary>
public sealed class QualityMonitor
{
    private readonly Dictionary<(QualityScope, int), QualityTrackState> _tracks = new Dictionary<(QualityScope, int), QualityTrackState>();
    private readonly MachineSim _m;

    public QualityMonitor(MachineSim machine) { _m = machine; }

    public IEnumerable<QualityTrackState> Tracks => _tracks.Values.OrderBy(t => t.Scope).ThenBy(t => t.Index);

    public QualityTrackState Track(QualityScope scope, int index)
    {
        if (!_tracks.TryGetValue((scope, index), out var t))
        {
            t = new QualityTrackState { Scope = scope, Index = index, Name = NameOf(scope, index) };
            _tracks[(scope, index)] = t;
        }
        return t;
    }

    private string NameOf(QualityScope scope, int index)
    {
        switch (scope)
        {
            case QualityScope.Station:
            case QualityScope.StationImputed: return _m.Stations.ElementAtOrDefault(index)?.Name ?? "Station " + (index + 1);
            case QualityScope.Group: return _m.Compiled.Machine.Groups.ElementAtOrDefault(index - 1)?.Name ?? "Fonction " + index;
            case QualityScope.Mover: return "Mover " + index;
            case QualityScope.Machine: return "Machine";
            default: return "OF";
        }
    }

    private QualityRule RuleOf(QualityScope scope, int index)
    {
        var m = _m.Compiled.Machine;
        switch (scope)
        {
            case QualityScope.Station: return m.StationRules.ElementAtOrDefault(index) ?? m.Quality.StationObserved;
            case QualityScope.StationImputed: return m.Quality.StationImputed;
            case QualityScope.Group: return m.GroupRules.ElementAtOrDefault(index - 1) ?? m.Quality.Function;
            case QualityScope.Mover: return m.Quality.Mover;
            default: return m.Quality.Machine;
        }
    }

    /// <summary>Enregistre un résultat pour une portée (bad = rebut constaté ou imputé).</summary>
    public void Record(QualityScope scope, int index, bool bad)
    {
        var t = Track(scope, index);
        var r = RuleOf(scope, index);
        int window = Math.Max(1, Math.Min(255, r.Window));
        // fenêtre glissante
        if (t.Count >= window)
        {
            int oldest = (t.Head + 256 - t.Count) % 256;
            if (t.Window[oldest]) t.BadInWindow--;
            t.Count--;
        }
        t.Count++;
        t.Window[t.Head] = bad;
        t.Head = (t.Head + 1) % 256;
        if (bad) { t.BadInWindow++; t.Consecutive++; t.TotalBad++; } else { t.Consecutive = 0; t.TotalGood++; }

        int consecAct = t.ProbationLeft > 0 ? 2 : r.ConsecAction;
        double rate = t.Count >= r.MinSample ? 100.0 * t.BadInWindow / t.Count : -1;
        bool actConsec = consecAct > 0 && t.Consecutive >= consecAct;
        bool actRate = r.RateActionPercent > 0 && rate >= r.RateActionPercent && t.ProbationLeft == 0;
        bool warn = (r.ConsecWarn > 0 && t.Consecutive >= r.ConsecWarn) || (r.RateWarnPercent > 0 && rate >= r.RateWarnPercent);
        if (t.ProbationLeft > 0) t.ProbationLeft--;

        if ((actConsec || actRate) && !t.Locked)
        {
            t.Locked = true; t.Level = QualityLevel.Action; t.SinceMs = _m.NowMs;
            var reaction = actConsec ? r.ReactionConsec : r.ReactionRate;
            t.LastReaction = reaction;
            t.Reason = Describe(t, r, actConsec, rate, consecAct);
            React(scope, index, reaction, t);
            Raise(t, actConsec, rate, AlarmSeverity.Quality);
        }
        else if (warn && t.Level == QualityLevel.Normal)
        {
            t.Level = QualityLevel.Warning;
            Raise(t, actConsec, rate, AlarmSeverity.Warning);
        }
        else if (!warn && t.Level == QualityLevel.Warning)
        {
            t.Level = t.ProbationLeft > 0 ? QualityLevel.Probation : QualityLevel.Normal;
        }
        else if (t.Level == QualityLevel.Probation && t.ProbationLeft == 0 && !t.Locked)
        {
            t.Level = QualityLevel.Normal;
        }
    }

    private string Describe(QualityTrackState t, QualityRule r, bool consec, double rate, int consecAct)
        => consec ? t.Consecutive + " rebuts successifs (seuil " + consecAct + ")" : "taux de rebut " + rate.ToString("0.0") + " % (seuil " + r.RateActionPercent.ToString("0.#") + " %)";

    private static int CodeOf(QualityScope scope, bool consec) => scope switch
    {
        QualityScope.Station => consec ? 950 : 951,
        QualityScope.StationImputed => consec ? 952 : 953,
        QualityScope.Group => 954,
        QualityScope.Mover => 955,
        QualityScope.Machine => consec ? 956 : 957,
        _ => 958,
    };

    private void Raise(QualityTrackState t, bool consec, double rate, AlarmSeverity severity)
    {
        int station = t.Scope == QualityScope.Station || t.Scope == QualityScope.StationImputed ? _m.Stations[t.Index].Number : 0;
        int sub = consec ? t.Consecutive : (int)Math.Round(Math.Max(0, rate) * 10);
        string what = t.Scope == QualityScope.Mover ? "Mover " + t.Index : t.Name;
        string text = (severity == AlarmSeverity.Quality ? what + " : " + t.Reason + " — " + ReactionText(t.LastReaction) : what + " : " + (consec ? t.Consecutive + " rebuts successifs" : "taux de rebut " + rate.ToString("0.0") + " %"));
        _m.Alarms.Raise(station, null, CodeOf(t.Scope, consec), sub, text, severity, _m.NowMs, "Qualité");
    }

    private static string ReactionText(QualityReaction r) => r switch
    {
        QualityReaction.StationHold => "Arrêt qualité",
        QualityReaction.MoverOut => "Mover hors service",
        QualityReaction.StopLoading => "Arrêt du chargement",
        QualityReaction.MachineStop => "Arrêt de la machine",
        _ => "Avertissement",
    };

    private void React(QualityScope scope, int index, QualityReaction reaction, QualityTrackState t)
    {
        switch (reaction)
        {
            case QualityReaction.StationHold:
                if (scope == QualityScope.Station || scope == QualityScope.StationImputed)
                {
                    var s = _m.Stations[index];
                    s.QualityHold = true;
                    if (s.State == PackMlState.Execute) s.Command(StateCommand.Hold);
                }
                break;
            case QualityReaction.MoverOut:
                var mover = _m.Movers.FirstOrDefault(x => x.Number == index);
                if (mover != null) { mover.OutOfService = true; mover.OutReason = "Arrêt qualité"; }
                break;
            case QualityReaction.StopLoading:
                _m.LoadingLocked = true; _m.LoadingLockedReason = "Arrêt qualité (" + t.Name + ")";
                break;
            case QualityReaction.MachineStop:
                _m.StopCauseOverride = "Arrêt qualité";
                _m.Command(StateCommand.Stop);
                break;
        }
    }

    /// <summary>Libération (docs/10 § 6) : remet à zéro la portée, passe en surveillance renforcée et lève le verrou de l'élément.</summary>
    public bool Release(QualityScope scope, int index)
    {
        if (!_tracks.TryGetValue((scope, index), out var t) || !t.Locked) return false;
        Array.Clear(t.Window, 0, 256);
        t.Consecutive = 0; t.Count = 0; t.BadInWindow = 0; t.Head = 0;
        t.ProbationLeft = 10; t.Level = QualityLevel.Probation; t.Locked = false; t.Reason = null; t.SinceMs = null;
        switch (t.LastReaction)
        {
            case QualityReaction.StationHold:
                var s = _m.Stations[index];
                s.QualityHold = false;
                if (_m.Pack.State == PackMlState.Execute && s.State == PackMlState.Held) s.Command(StateCommand.Start);
                break;
            case QualityReaction.MoverOut:
                var mover = _m.Movers.FirstOrDefault(x => x.Number == index);
                if (mover != null) { mover.OutOfService = false; mover.OutReason = null; if (mover.State == MvState.OutOfService) { mover.State = MvState.Ready; mover.PC = 1; } }
                break;
            case QualityReaction.StopLoading:
                if (!_tracks.Values.Any(x => x != t && x.Locked && x.LastReaction == QualityReaction.StopLoading)) { _m.LoadingLocked = false; _m.LoadingLockedReason = null; }
                break;
        }
        foreach (var a in _m.Alarms.All.Where(a => a.Active && a.Severity == AlarmSeverity.Quality && a.Text.StartsWith(t.Name, StringComparison.Ordinal))) _m.Alarms.Clear(a, _m.NowMs);
        return true;
    }

    /// <summary>Remise à zéro manuelle ou au changement de référence.</summary>
    public void Reset(QualityScope scope, int index)
    {
        if (!_tracks.TryGetValue((scope, index), out var t)) return;
        Array.Clear(t.Window, 0, 256);
        t.Consecutive = 0; t.Count = 0; t.BadInWindow = 0; t.Head = 0;
    }

    public void ResetAll() => _tracks.Clear();

    public bool AnyLockOf(QualityReaction reaction) => _tracks.Values.Any(t => t.Locked && t.LastReaction == reaction);
}
