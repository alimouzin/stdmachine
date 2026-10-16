using System.Globalization;
using System.Text.Json.Nodes;
using StdMachine.Core;

namespace StdMachine.Plant;

/// <summary>
/// Passerelle usine (docs/11 § 4, § 7.1) : construit les messages (identité, état, stations, compteurs, alarmes, arrêts, OF, pièces, qualité,
/// forçages, journal, maintenance, santé) à partir de l'instantané du PLC et des événements de pièces, leur attribue un <c>seq</c> persistant
/// et les distribue aux canaux activés. La première version n'a pas d'outbox : un message est perdu si tous les canaux d'événements sont arrêtés.
/// </summary>
public sealed class PlantGateway : IPlantGateway
{
    private readonly List<IPlantChannel> _channels;
    private readonly ISeqStore _seq;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _hmiVersion;
    private readonly object _gate = new object();
    private readonly Queue<(PlantMessage Message, bool State)> _queue = new Queue<(PlantMessage, bool)>();
    private readonly HashSet<string> _running = new HashSet<string>();

    // Suivi des changements
    private PlcSnapshot? _previous;
    private DateTime _lastCounters = DateTime.MinValue, _lastHealth = DateTime.MinValue, _lastMaintenance = DateTime.MinValue;
    private readonly Dictionary<int, byte> _alarmReported = new Dictionary<int, byte>(); // 1 levée, 2 levée+disparue, 4 acquittée
    private readonly Dictionary<PackMlState, double> _timeInState = new Dictionary<PackMlState, double>();
    private readonly Dictionary<MachineMode, double> _timeInMode = new Dictionary<MachineMode, double>();
    private readonly HashSet<string> _lockedTracks = new HashSet<string>();
    private readonly HashSet<string> _warnedTracks = new HashSet<string>();
    private readonly HashSet<string> _forceKeys = new HashSet<string>();
    private DeclaredState _declared;
    private string? _orderNumber;
    private OrderState _orderState;
    private DateTime? _stopStart;
    private PackMlState _stopWorst;
    private string? _stopCause;
    private string? _stopCauseKind;
    private int _heartbeat;
    private int _exited, _rejected;
    private string? _lastIdentityHash;

    public PlantGateway(IEnumerable<IPlantChannel> channels, CommunicationSettings settings, ISeqStore seq, Func<DateTimeOffset>? clock = null, string hmiVersion = "PoC net48")
    {
        _channels = channels.ToList();
        Settings = settings;
        _seq = seq;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _hmiVersion = hmiVersion;
    }

    public IReadOnlyList<IPlantChannel> Channels => _channels;

    public CommunicationSettings Settings { get; private set; }

    public long LastSeq => _seq.Last;

    public int Pending { get { lock (_gate) return _queue.Count; } }

    // ------------------------------------------------------------------ réglages

    public async Task ApplySettingsAsync(CommunicationSettings settings, CancellationToken ct)
    {
        Settings = settings;
        foreach (var ch in _channels)
        {
            var cs = settings.For(ch.Id);
            bool shouldRun = cs.Enabled && ch.IsImplemented;
            if (shouldRun)
            {
                if (_running.Contains(ch.Id)) await ch.StopAsync(ct).ConfigureAwait(false);
                await ch.StartAsync(cs, ct).ConfigureAwait(false);
                _running.Add(ch.Id);
            }
            else
            {
                if (_running.Remove(ch.Id)) await ch.StopAsync(ct).ConfigureAwait(false);
                else if (cs.Enabled) await ch.StartAsync(cs, ct).ConfigureAwait(false); // canal prévu : enregistre son état « Prévu »
            }
        }
    }

    // ------------------------------------------------------------------ branchement

    private IPlcClient? _client;

    public void Attach(IPlcClient client)
    {
        _client = client;
        client.SnapshotUpdated += (_, s) => Process(s);
        if (client is IPartEventSource parts) parts.PartClosed += (_, p) => PartClosed(p);
    }

    // ------------------------------------------------------------------ messages

    private PlantMessage Make(string schemaId, JsonObject data, DateTimeOffset? ts = null)
    {
        var schema = Schemas.Get(schemaId);
        string topic = schema.Topic;
        return new PlantMessage(schemaId, Settings.MachineId, _seq.Next(), ts ?? _clock(), topic, data);
    }

    private void Emit(string schemaId, JsonObject data, DateTimeOffset? ts = null)
    {
        var schema = Schemas.Get(schemaId);
        if (Settings.DisabledTopics.Contains(schema.Topic)) return;
        var m = Make(schemaId, data, ts);
        lock (_gate) _queue.Enqueue((m, !schema.IsEvent));
    }

    /// <summary>Distribue les messages en attente aux canaux d'événements démarrés, dans l'ordre.</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        while (true)
        {
            (PlantMessage Message, bool State) item;
            lock (_gate)
            {
                if (_queue.Count == 0) return;
                item = _queue.Dequeue();
            }
            foreach (var ch in _channels.OfType<IPlantEventChannel>())
            {
                if (!_running.Contains(ch.Id)) continue;
                if (item.State) await ch.PublishStateAsync(item.Message, ct).ConfigureAwait(false);
                else await ch.PublishEventAsync(item.Message, ct).ConfigureAwait(false);
            }
        }
    }

    private static string Iso(DateTime t) => t.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Iso(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static string Camel(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);

    private static string VerdictText(Verdict v) => v == Verdict.Good ? "good" : v == Verdict.Bad ? "bad" : "undefined";

    private static string ModeText(MachineMode m) => m == MachineMode.Production ? "production" : "maintenance";

    // ------------------------------------------------------------------ instantané

    /// <summary>Traite un instantané : publie les changements d'état, les événements d'alarme, d'arrêt, d'OF, de qualité et de forçage, et les messages périodiques.</summary>
    public void Process(PlcSnapshot s)
    {
        var prev = _previous;
        AccumulateTimes(prev, s);
        if (prev == null || prev.Machine.ProjectHash != s.Machine.ProjectHash || _lastIdentityHash != s.Machine.ProjectHash)
        {
            _lastIdentityHash = s.Machine.ProjectHash;
            Emit("stdmachine.identity/1", new JsonObject
            {
                ["machineName"] = Settings.MachineId, ["projectHash"] = s.Machine.ProjectHash, ["deployedAt"] = null,
                ["hmiVersion"] = _hmiVersion, ["plcVersion"] = s.Connection == PlcConnectionState.Simulation ? "Simulateur" : "TwinCAT 3",
            });
        }
        if (prev == null || MachineStateChanged(prev, s)) Emit("stdmachine.state/1", StateData(s), new DateTimeOffset(s.Time.ToUniversalTime(), TimeSpan.Zero));
        foreach (var st in s.Stations)
        {
            var before = prev?.Station(st.Number);
            if (before == null || before.State != st.State || before.Mode != st.Mode || before.Operation != st.Operation || before.QualityHold != st.QualityHold || before.OutOfProduction != st.OutOfProduction)
                Emit("stdmachine.station.state/1", new JsonObject
                {
                    ["station"] = st.Number, ["name"] = st.Name, ["state"] = st.State.ToString(), ["mode"] = ModeText(st.Mode), ["since"] = Iso(st.StateSince),
                    ["operation"] = st.Operation, ["moverNumber"] = st.MoverNumber, ["qualityHold"] = st.QualityHold, ["outOfProduction"] = st.OutOfProduction,
                });
        }
        AlarmEvents(s);
        StopEvents(prev, s);
        OrderEvents(s);
        QualityEvents(s);
        ForceEvents(s);
        if ((s.Time - _lastCounters).TotalSeconds >= Settings.CountersPeriodSeconds || prev == null) { _lastCounters = s.Time; Emit("stdmachine.counters/1", CountersData(s)); }
        if ((s.Time - _lastHealth).TotalSeconds >= Settings.HealthPeriodSeconds || prev == null)
        {
            _lastHealth = s.Time;
            Emit("stdmachine.health/1", new JsonObject { ["heartbeat"] = ++_heartbeat, ["plcConnection"] = s.Connection.ToString(), ["outboxPending"] = Pending, ["diskFreeMb"] = null });
        }
        if ((s.Time - _lastMaintenance).TotalMinutes >= 60 || prev == null) { _lastMaintenance = s.Time; Emit("stdmachine.maintenance/1", MaintenanceData(s)); }
        _previous = s;
    }

    private static bool MachineStateChanged(PlcSnapshot a, PlcSnapshot b)
        => a.Machine.State != b.Machine.State || a.Machine.Mode != b.Machine.Mode || a.Machine.OverridePercent != b.Machine.OverridePercent || a.Machine.LoadingLocked != b.Machine.LoadingLocked
           || a.Forces.Declared != b.Forces.Declared || a.Orders.Current?.Number != b.Orders.Current?.Number || a.Orders.Current?.State != b.Orders.Current?.State
           || a.Machine.ActiveFaults != b.Machine.ActiveFaults || a.Machine.ActiveWarnings != b.Machine.ActiveWarnings;

    private void AccumulateTimes(PlcSnapshot? prev, PlcSnapshot s)
    {
        if (prev == null) return;
        double dt = Math.Max(0, (s.Time - prev.Time).TotalSeconds);
        _timeInState[prev.Machine.State] = (_timeInState.TryGetValue(prev.Machine.State, out var a) ? a : 0) + dt;
        _timeInMode[prev.Machine.Mode] = (_timeInMode.TryGetValue(prev.Machine.Mode, out var b) ? b : 0) + dt;
    }

    private JsonObject StateData(PlcSnapshot s)
    {
        var unavailable = new JsonArray();
        var o = s.Orders.Current;
        return new JsonObject
        {
            ["state"] = new JsonObject { ["code"] = (int)s.Machine.State, ["name"] = s.Machine.State.ToString() },
            ["mode"] = ModeText(s.Machine.Mode), ["since"] = Iso(s.Machine.StateSince), ["overridePercent"] = s.Machine.OverridePercent,
            ["stopCause"] = s.Machine.StopCause,
            ["order"] = o == null ? null : OrderObject(o),
            ["loadingLocked"] = s.Machine.LoadingLocked, ["loadingLockedReason"] = s.Quality.LoadingLockedReason,
            ["declaredState"] = s.Forces.Declared == DeclaredState.None ? null : s.Forces.Declared.ToString(), ["startBlocked"] = s.Forces.StartBlocked,
            ["unavailableFunctions"] = unavailable,
            ["alarms"] = new JsonObject { ["faults"] = s.Machine.ActiveFaults, ["warnings"] = s.Machine.ActiveWarnings },
        };
    }

    private static JsonObject OrderObject(OrderInfo o) => new JsonObject
    {
        ["number"] = o.Number, ["reference"] = o.Reference, ["quantity"] = o.Quantity, ["good"] = o.Good, ["bad"] = o.Bad, ["inProcess"] = o.InProcess, ["state"] = o.State.ToString(),
    };

    private JsonObject CountersData(PlcSnapshot s)
    {
        var tis = new JsonObject();
        foreach (var kv in _timeInState) tis[kv.Key.ToString()] = Math.Round(kv.Value, 1);
        var tim = new JsonObject();
        foreach (var kv in _timeInMode) tim[ModeText(kv.Key)] = Math.Round(kv.Value, 1);
        var o = s.Orders.Current;
        return new JsonObject
        {
            ["partsExited"] = _exited, ["partsRejected"] = _rejected, ["inProcess"] = s.Movers.Sum(m => m.Parts.Count), ["timeInState"] = tis, ["timeInMode"] = tim,
            ["cycleTimeSeconds"] = null, ["order"] = o == null ? null : OrderObject(o),
        };
    }

    private JsonObject MaintenanceData(PlcSnapshot s)
    {
        var arr = new JsonArray();
        foreach (var o in s.Objects)
        {
            if (o.ActionCount == 0 && o.ErrorCount == 0) continue;
            string station = s.Station(o.StationNumber)?.Name ?? "Machine";
            arr.Add(new JsonObject { ["station"] = station, ["object"] = o.ObjectId.ToString("N"), ["actions"] = o.ActionCount, ["errors"] = o.ErrorCount, ["motionHours"] = o.MotionHours });
        }
        return new JsonObject { ["objects"] = arr };
    }

    // ------------------------------------------------------------------ événements

    private static string SeverityText(AlarmSeverity s) => s switch
    {
        AlarmSeverity.Information => "information", AlarmSeverity.Warning => "warning", AlarmSeverity.StationFault => "stationFault",
        AlarmSeverity.MachineFault => "machineFault", _ => "quality",
    };

    private void AlarmEvents(PlcSnapshot s)
    {
        foreach (var a in s.Alarms.OrderBy(x => x.Id))
        {
            _alarmReported.TryGetValue(a.Id, out byte seen);
            void Send(string ev)
            {
                Emit("stdmachine.event.alarm/1", new JsonObject
                {
                    ["event"] = ev, ["severity"] = SeverityText(a.Severity), ["station"] = a.Station, ["object"] = a.Object, ["objectType"] = null,
                    ["code"] = a.Code, ["subCode"] = a.SubCode, ["text"] = a.Text, ["subText"] = a.SubText,
                    ["context"] = a.Context == null ? null : new JsonObject { ["text"] = a.Context },
                }, new DateTimeOffset(ev == "raised" ? a.Time.ToUniversalTime() : (ev == "cleared" ? (a.ClearedTime ?? a.Time) : a.Time).ToUniversalTime(), TimeSpan.Zero));
            }
            if ((seen & 1) == 0) { Send("raised"); seen |= 1; }
            if ((seen & 2) == 0 && a.ClearedTime != null) { Send("cleared"); seen |= 2; }
            if ((seen & 4) == 0 && a.State == AlarmState.Acknowledged && a.Severity != AlarmSeverity.Information) { Send("acknowledged"); seen |= 4; }
            _alarmReported[a.Id] = seen;
        }
    }

    private void StopEvents(PlcSnapshot? prev, PlcSnapshot s)
    {
        var st = s.Machine.State;
        bool producing = st == PackMlState.Execute;
        if (!producing && _stopStart == null && (prev == null || prev.Machine.State == PackMlState.Execute))
        {
            _stopStart = s.Machine.StateSince < s.Time ? s.Machine.StateSince : s.Time; _stopWorst = st; _stopCause = null; _stopCauseKind = null;
        }
        if (_stopStart != null && !producing)
        {
            if (Rank(st) > Rank(_stopWorst)) _stopWorst = st;
            if (s.Machine.StopCause != null && _stopCause == null) { _stopCause = s.Machine.StopCause; _stopCauseKind = KindOf(s.Machine.StopCause, st); }
        }
        if (_stopStart != null && producing)
        {
            var worst = _stopWorst == PackMlState.Aborted ? "Aborted" : _stopWorst == PackMlState.Held ? "Held" : "Stopped";
            var cause = new JsonObject { ["kind"] = _stopCauseKind ?? (worst == "Aborted" ? "alarm" : "operator"), ["text"] = _stopCause, ["code"] = null, ["station"] = null };
            Emit("stdmachine.event.stop/1", new JsonObject
            {
                ["start"] = Iso(_stopStart.Value), ["end"] = Iso(s.Time), ["durationSeconds"] = Math.Round((s.Time - _stopStart.Value).TotalSeconds, 1), ["state"] = worst,
                ["cause"] = cause, ["declaredState"] = s.Forces.Declared == DeclaredState.None ? null : s.Forces.Declared.ToString(), ["user"] = null,
            });
            _stopStart = null;
        }
    }

    private static int Rank(PackMlState s) => s == PackMlState.Aborted ? 3 : s == PackMlState.Held ? 2 : 1;

    private static string KindOf(string cause, PackMlState state)
    {
        if (cause.StartsWith("Arrêt qualité", StringComparison.Ordinal)) return "quality";
        if (cause.StartsWith("Fin d'OF", StringComparison.Ordinal)) return "orderComplete";
        return state == PackMlState.Aborted ? "alarm" : "operator";
    }

    private void OrderEvents(PlcSnapshot s)
    {
        var o = s.Orders.Current;
        if (o != null && (_orderNumber != o.Number)) { _orderNumber = o.Number; _orderState = o.State; SendOrder("started", o); }
        else if (o != null && _orderState != o.State)
        {
            string ev = o.State == OrderState.Suspended ? "suspended" : o.State == OrderState.Running ? "resumed" : o.State == OrderState.Cancelled ? "cancelled" : o.State == OrderState.Finished ? "finished" : "progress";
            _orderState = o.State;
            SendOrder(ev, o);
        }
        else if (o == null && _orderNumber != null)
        {
            var finished = s.Orders.Orders.FirstOrDefault(x => x.Number == _orderNumber);
            if (finished != null) SendOrder(finished.State == OrderState.Cancelled ? "cancelled" : "finished", finished);
            _orderNumber = null;
        }
    }

    private void SendOrder(string ev, OrderInfo o) => Emit("stdmachine.event.order/1", new JsonObject
    {
        ["event"] = ev, ["number"] = o.Number, ["reference"] = o.Reference, ["quantity"] = o.Quantity, ["good"] = o.Good, ["bad"] = o.Bad, ["inProcess"] = o.InProcess,
    });

    private static string ScopeText(QualityScope s) => Camel(s.ToString());

    private void QualityEvents(PlcSnapshot s)
    {
        foreach (var t in s.Quality.Tracks)
        {
            string key = t.Scope + "/" + t.Name;
            if (t.Locked && _lockedTracks.Add(key))
                Emit("stdmachine.event.quality/1", new JsonObject { ["event"] = "action", ["scope"] = ScopeText(t.Scope), ["name"] = t.Name, ["reason"] = t.Reason, ["consecutive"] = t.Consecutive, ["ratePercent"] = Math.Round(t.RatePercent, 1) });
            else if (!t.Locked && _lockedTracks.Remove(key))
                Emit("stdmachine.event.quality/1", new JsonObject { ["event"] = "released", ["scope"] = ScopeText(t.Scope), ["name"] = t.Name, ["reason"] = null, ["consecutive"] = t.Consecutive, ["ratePercent"] = Math.Round(t.RatePercent, 1) });
            else if (t.Level == QualityLevel.Warning && !t.Locked && _warnedTracks.Add(key))
                Emit("stdmachine.event.quality/1", new JsonObject { ["event"] = "warning", ["scope"] = ScopeText(t.Scope), ["name"] = t.Name, ["reason"] = null, ["consecutive"] = t.Consecutive, ["ratePercent"] = Math.Round(t.RatePercent, 1) });
            if (t.Level != QualityLevel.Warning) _warnedTracks.Remove(key);
        }
    }

    private void ForceEvents(PlcSnapshot s)
    {
        var now = new HashSet<string>();
        foreach (var f in s.Forces.Entries)
        {
            string key = f.Kind + "/" + f.Target;
            now.Add(key);
            if (_forceKeys.Add(key))
                Emit("stdmachine.event.force/1", new JsonObject { ["event"] = "set", ["kind"] = Camel(f.Kind), ["target"] = f.Target, ["description"] = f.Description, ["reason"] = f.Reason, ["by"] = f.By });
        }
        if (s.Forces.Declared != DeclaredState.None) now.Add("declared/" + s.Forces.Declared);
        if (s.Forces.Declared != _declared)
        {
            if (_declared != DeclaredState.None) Emit("stdmachine.event.force/1", new JsonObject { ["event"] = "lifted", ["kind"] = "declared", ["target"] = _declared.ToString(), ["description"] = null, ["reason"] = null, ["by"] = null });
            if (s.Forces.Declared != DeclaredState.None) Emit("stdmachine.event.force/1", new JsonObject { ["event"] = "set", ["kind"] = "declared", ["target"] = s.Forces.Declared.ToString(), ["description"] = null, ["reason"] = s.Forces.DeclaredReason, ["by"] = s.Forces.DeclaredBy });
            _declared = s.Forces.Declared;
        }
        foreach (var gone in _forceKeys.Where(k => !now.Contains(k)).ToList())
        {
            _forceKeys.Remove(gone);
            var parts = gone.Split('/');
            Emit("stdmachine.event.force/1", new JsonObject { ["event"] = "lifted", ["kind"] = Camel(parts[0]), ["target"] = parts.Length > 1 ? parts[1] : gone, ["description"] = null, ["reason"] = null, ["by"] = null });
        }
    }

    // ------------------------------------------------------------------ pièces et journal

    public void PartClosed(PartRecord p)
    {
        if (p.FinalState == PartState.Exited) _exited++; else _rejected++;
        var ops = new JsonArray();
        foreach (var o in p.Operations)
        {
            var data = new JsonObject();
            foreach (var kv in o.Data) data[Camel(kv.Key.Replace(" ", string.Empty))] = new JsonObject { ["value"] = kv.Value };
            ops.Add(new JsonObject
            {
                ["function"] = o.Function, ["operation"] = o.Operation, ["station"] = o.Station, ["start"] = Iso(o.Start), ["end"] = Iso(o.End),
                ["verdictAfter"] = VerdictText(o.VerdictAfter), ["data"] = data,
            });
        }
        Emit("stdmachine.event.part/1", new JsonObject
        {
            ["partId"] = p.PartId, ["order"] = p.Order, ["reference"] = p.Reference, ["createdAt"] = Iso(p.CreatedAt), ["closedAt"] = Iso(p.ClosedAt),
            ["finalState"] = p.FinalState == PartState.Exited ? "exited" : p.FinalState == PartState.Rejected ? "rejected" : "removed",
            ["verdict"] = VerdictText(p.Verdict), ["mover"] = p.Mover, ["slot"] = p.Slot, ["operations"] = ops,
        });
    }

    /// <summary>Entrée du journal (déploiement, modification de configuration, override, action manuelle, connexion, droits).</summary>
    public void Audit(string? user, string what, string? before = null, string? after = null)
        => Emit("stdmachine.event.audit/1", new JsonObject { ["user"] = user, ["what"] = what, ["before"] = before, ["after"] = after });
}
