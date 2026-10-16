using System.Text;

namespace StdMachine.Plant;

public abstract class PlantChannelBase : IPlantChannel
{
    private PlantChannelStatus _status;

    protected PlantChannelBase(string id, string displayName, PlantChannelStatus initial) { Id = id; DisplayName = displayName; _status = initial; }

    public string Id { get; }
    public string DisplayName { get; }
    public abstract bool IsImplemented { get; }
    public PlantChannelStatus Status => _status;
    public event EventHandler<PlantChannelStatus>? StatusChanged;

    protected void SetStatus(PlantChannelStatus status)
    {
        _status = status;
        StatusChanged?.Invoke(this, status);
    }

    public abstract Task StartAsync(ChannelSettings settings, CancellationToken ct);
    public abstract Task StopAsync(CancellationToken ct);
    public abstract Task<ChannelTestResult> TestAsync(ChannelSettings settings, CancellationToken ct);
}

/// <summary>Canal déclaré « Prévu » : les réglages s'enregistrent, l'activation n'a pas d'effet (MQTT et OPC UA, première version).</summary>
public sealed class PlannedChannel : PlantChannelBase
{
    public PlannedChannel(string id, string displayName) : base(id, displayName, PlantChannelStatus.Of(PlantChannelState.Planned, "Prévu")) { }

    public override bool IsImplemented => false;

    public override Task StartAsync(ChannelSettings settings, CancellationToken ct) { SetStatus(PlantChannelStatus.Of(PlantChannelState.Planned, "Prévu : réglages enregistrés, canal inactif")); return Task.FromResult(0); }

    public override Task StopAsync(CancellationToken ct) => Task.FromResult(0);

    public override Task<ChannelTestResult> TestAsync(ChannelSettings settings, CancellationToken ct)
        => Task.FromResult(new ChannelTestResult(false, "Canal prévu : pas encore réalisé."));
}

public sealed class MqttChannel : PlantChannelBase
{
    private readonly PlannedChannel _inner = new PlannedChannel("mqtt", "Usine MQTT");
    public MqttChannel() : base("mqtt", "Usine MQTT", PlantChannelStatus.Of(PlantChannelState.Planned, "Prévu")) { }
    public override bool IsImplemented => false;
    public override Task StartAsync(ChannelSettings settings, CancellationToken ct) => _inner.StartAsync(settings, ct);
    public override Task StopAsync(CancellationToken ct) => Task.FromResult(0);
    public override Task<ChannelTestResult> TestAsync(ChannelSettings settings, CancellationToken ct) => _inner.TestAsync(settings, ct);
}

public sealed class OpcUaChannel : PlantChannelBase
{
    public OpcUaChannel() : base("opcua", "Usine OPC UA", PlantChannelStatus.Of(PlantChannelState.Planned, "Prévu")) { }
    public override bool IsImplemented => false;
    public override Task StartAsync(ChannelSettings settings, CancellationToken ct) { SetStatus(PlantChannelStatus.Of(PlantChannelState.Planned, "Prévu : le serveur OPC UA est celui de TwinCAT (TF6100)")); return Task.FromResult(0); }
    public override Task StopAsync(CancellationToken ct) => Task.FromResult(0);
    public override Task<ChannelTestResult> TestAsync(ChannelSettings settings, CancellationToken ct) => Task.FromResult(new ChannelTestResult(false, "Canal prévu : pas encore réalisé."));
}

/// <summary>
/// Canal « Journal de messages » (docs/11 § 7.1) : un fichier JSON Lines par jour et par famille de sujets, rétention de 7 jours.
/// Chaque ligne est l'enveloppe complète du message.
/// </summary>
public sealed class LogFileChannel : PlantChannelBase, IPlantEventChannel
{
    private readonly object _gate = new object();
    private readonly Func<DateTime> _today;
    private string _directory = string.Empty;
    private int _retentionDays = 7;
    private long _sent;
    private bool _running;

    public LogFileChannel(Func<DateTime>? today = null) : base("log", "Journal de messages", PlantChannelStatus.Of(PlantChannelState.Disabled, "Désactivé"))
    {
        _today = today ?? (() => DateTime.Now);
    }

    public static string DefaultDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StdMachine", "plant-log");

    public override bool IsImplemented => true;

    public string Directory_ => _directory;

    public override Task StartAsync(ChannelSettings settings, CancellationToken ct)
    {
        lock (_gate)
        {
            _directory = settings.Get("directory", DefaultDirectory);
            _retentionDays = int.TryParse(settings.Get("retentionDays", "7"), out var d) && d > 0 ? d : 7;
            try
            {
                Directory.CreateDirectory(_directory);
                Purge();
                _running = true;
                SetStatus(PlantChannelStatus.Of(PlantChannelState.Connected, "Écrit dans " + _directory, null, _sent));
            }
            catch (Exception e)
            {
                _running = false;
                SetStatus(PlantChannelStatus.Of(PlantChannelState.Error, "Dossier inaccessible", e.Message, _sent));
            }
        }
        return Task.FromResult(0);
    }

    public override Task StopAsync(CancellationToken ct)
    {
        lock (_gate) { _running = false; SetStatus(PlantChannelStatus.Of(PlantChannelState.Disabled, "Désactivé", null, _sent)); }
        return Task.FromResult(0);
    }

    public override Task<ChannelTestResult> TestAsync(ChannelSettings settings, CancellationToken ct)
    {
        string dir = settings.Get("directory", DefaultDirectory);
        try
        {
            Directory.CreateDirectory(dir);
            string probe = Path.Combine(dir, ".test-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "test");
            File.Delete(probe);
            return Task.FromResult(new ChannelTestResult(true, "Écriture possible dans " + dir));
        }
        catch (Exception e) { return Task.FromResult(new ChannelTestResult(false, "Écriture impossible : " + e.Message)); }
    }

    public Task PublishEventAsync(PlantMessage message, CancellationToken ct) => Write(message);

    public Task PublishStateAsync(PlantMessage message, CancellationToken ct) => Write(message);

    private Task Write(PlantMessage message)
    {
        lock (_gate)
        {
            if (!_running) return Task.FromResult(0);
            try
            {
                var schema = Schemas.All.FirstOrDefault(s => s.Id == message.Schema);
                string family = schema?.Family ?? "other";
                string file = Path.Combine(_directory, _today().ToString("yyyyMMdd") + "-" + family + ".jsonl");
                File.AppendAllText(file, message.ToJson() + "\n", new UTF8Encoding(false));
                _sent++;
                if (_sent % 50 == 0) SetStatus(PlantChannelStatus.Of(PlantChannelState.Connected, "Écrit dans " + _directory, null, _sent));
            }
            catch (Exception e)
            {
                SetStatus(PlantChannelStatus.Of(PlantChannelState.Degraded, "Écriture en échec", e.Message, _sent));
            }
        }
        return Task.FromResult(0);
    }

    /// <summary>Supprime les fichiers plus vieux que la rétention.</summary>
    public void Purge()
    {
        if (_directory.Length == 0 || !Directory.Exists(_directory)) return;
        var limit = _today().Date.AddDays(-_retentionDays);
        foreach (var f in Directory.GetFiles(_directory, "*.jsonl"))
        {
            string name = Path.GetFileName(f);
            if (name.Length >= 8 && DateTime.TryParseExact(name.Substring(0, 8), "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day) && day < limit)
            {
                try { File.Delete(f); } catch (IOException) { }
            }
        }
    }

    public long MessagesSent { get { lock (_gate) return _sent; } }
}
