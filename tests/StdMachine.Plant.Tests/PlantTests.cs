using System.Text.Json.Nodes;
using StdMachine.Core;
using StdMachine.Core.Catalog;
using StdMachine.Plant;
using StdMachine.Simulation;
using StdMachine.Simulation.Demo;
using Xunit;
using Xunit.Abstractions;

namespace StdMachine.Plant.Tests;

public class PlantTests : IDisposable
{
    private readonly ITestOutputHelper _out;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "stdm-plant-" + Guid.NewGuid().ToString("N"));

    public PlantTests(ITestOutputHelper o) { _out = o; Directory.CreateDirectory(_dir); }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    private static CatalogDocument Catalog() => CatalogLoader.Load(Path.Combine(AppContext.BaseDirectory, "catalog", "catalog.json"));

    private (PlantGateway Gateway, LogFileChannel Log, CommunicationSettings Settings, FileSeqStore Seq) NewGateway(DateTime? today = null)
    {
        var settings = new CommunicationSettings { MachineId = "MX-4" };
        settings.For("log").Enabled = true;
        settings.For("log").Values["directory"] = Path.Combine(_dir, "log");
        var log = new LogFileChannel(() => today ?? DateTime.Now);
        var seq = new FileSeqStore(Path.Combine(_dir, "seq.txt"));
        var gw = new PlantGateway(new IPlantChannel[] { log, new MqttChannel(), new OpcUaChannel() }, settings, seq, () => DateTimeOffset.UtcNow);
        gw.ApplySettingsAsync(settings, default).Wait();
        return (gw, log, settings, seq);
    }

    private static IEnumerable<JsonObject> ReadAll(string logDir) => Directory.GetFiles(logDir, "*.jsonl").SelectMany(File.ReadAllLines).Where(l => l.Length > 0).Select(l => (JsonObject)JsonNode.Parse(l)!);

    private static PlantMessage ToMessage(JsonObject e)
        => new PlantMessage(e["schema"]!.GetValue<string>(), e["machineId"]!.GetValue<string>(), e["seq"]!.GetValue<long>(), DateTimeOffset.Parse(e["ts"]!.GetValue<string>()), "", (JsonObject)e["data"]!);

    [Fact]
    public void Simulated_production_of_100_parts_writes_valid_messages_with_a_gap_free_seq()
    {
        var cat = Catalog();
        var client = new SimulatedPlcClient(SampleProject.Build(cat), cat, 4);
        var (gw, log, settings, seq) = NewGateway();
        gw.Attach(client);
        gw.Process(client.Snapshot);
        client.StartOrderAsync(new OrderStart("24-117", "Platine P12", 100), default).Wait();
        client.SendCommandAsync(StateCommand.Reset, default).Wait();
        client.Advance(8_000);
        client.SendCommandAsync(StateCommand.Start, default).Wait();
        bool alarmDone = false, forceDone = false;
        for (int i = 0; i < 12000 && !client.Sim.OrderHistory.Any(o => o.State == OrderState.Finished); i++)
        {
            client.Advance(1_000);
            if (!alarmDone && i == 200) { client.ForceStationAbort(DemoProject.Pressage01); alarmDone = true; }
            if (alarmDone && i == 215) client.SendStationCommandAsync(DemoProject.Pressage01, StateCommand.Clear, default).Wait();
            if (alarmDone && i == 220) client.SendStationCommandAsync(DemoProject.Pressage01, StateCommand.Reset, default).Wait();
            if (alarmDone && i == 232) client.SendStationCommandAsync(DemoProject.Pressage01, StateCommand.Start, default).Wait();
            if (!forceDone && i == 300) { client.SetMoverOutOfServiceAsync(3, true, "Nid abîmé", default).Wait(); forceDone = true; }
            if (forceDone && i == 330) client.SetMoverOutOfServiceAsync(3, false, null, default).Wait();
            gw.Process(client.Snapshot);
            gw.FlushAsync().Wait();
            // Un opérateur libère les réactions qualité (arrêt du chargement…) pour que l'OF aille à son terme.
            foreach (var t in client.Snapshot.Quality.Tracks.Where(t => t.Locked).ToList()) client.ReleaseQualityAsync(t.Scope, t.Name, "Fausse alarme", default).Wait();
            // Si une réaction qualité a arrêté la machine, l'opérateur la relance (Réarmer puis Démarrer).
            var machineState = client.Snapshot.Machine.State;
            if (i > 10 && machineState == PackMlState.Stopped) client.SendCommandAsync(StateCommand.Reset, default).Wait();
            else if (i > 10 && machineState == PackMlState.Idle) client.SendCommandAsync(StateCommand.Start, default).Wait();
        }
        gw.Process(client.Snapshot);
        gw.FlushAsync().Wait();
        _out.WriteLine("état " + client.Snapshot.Machine.State + " exited " + client.Sim.PartsExited + " rejected " + client.Sim.PartsRejected + " created " + client.Sim.PartsCreated + " order " + client.Sim.Order?.State + " " + client.Sim.Order?.Good + "/" + client.Sim.Order?.InProcess + " t=" + client.Sim.NowMs / 1000);
        foreach (var m in client.Sim.Movers) _out.WriteLine("mover " + m.Number + " " + m.State + " " + m.WaitReason + " parts " + m.Slots.Count(x => x.Present));
        foreach (var st in client.Sim.Stations) _out.WriteLine(st.Name + " " + st.State + " " + st.FaultText);
        Assert.True(client.Sim.OrderHistory.Any(o => o.State == OrderState.Finished));

        var all = ReadAll(Path.Combine(_dir, "log")).ToList();
        _out.WriteLine(all.Count + " messages");
        // Chaque message valide son schéma
        foreach (var e in all)
        {
            var errors = Schemas.Validate(ToMessage(e));
            Assert.True(errors.Count == 0, e["schema"] + " : " + string.Join("; ", errors));
        }
        // seq croissant sans trou
        var seqs = all.Select(e => e["seq"]!.GetValue<long>()).ToList();
        Assert.Equal(Enumerable.Range(1, seqs.Count).Select(x => (long)x), seqs.OrderBy(x => x));
        Assert.Equal(seq.Last, seqs.Max());
        // 100 pièces bonnes et les rebuts éventuels : un message par pièce clôturée
        var parts = all.Where(e => e["schema"]!.GetValue<string>() == "stdmachine.event.part/1").ToList();
        Assert.Equal(client.Sim.PartsExited + client.Sim.PartsRejected, parts.Count);
        Assert.Equal(100, parts.Count(p => p["data"]!["finalState"]!.GetValue<string>() == "exited"));
        // alarmes : apparue, disparue (la station est réarmée)
        var alarms = all.Where(e => e["schema"]!.GetValue<string>() == "stdmachine.event.alarm/1").Select(e => e["data"]!["event"]!.GetValue<string>()).ToList();
        Assert.Contains("raised", alarms);
        Assert.Contains("cleared", alarms);
        // forçage posé et levé
        var forces = all.Where(e => e["schema"]!.GetValue<string>() == "stdmachine.event.force/1").Select(e => e["data"]!["event"]!.GetValue<string>()).ToList();
        Assert.Contains("set", forces);
        Assert.Contains("lifted", forces);
        // OF : lancé puis terminé ; arrêt terminé avec sa cause
        var orders = all.Where(e => e["schema"]!.GetValue<string>() == "stdmachine.event.order/1").Select(e => e["data"]!["event"]!.GetValue<string>()).ToList();
        Assert.Equal("started", orders.First());
        Assert.Contains("finished", orders);
        Assert.Contains(all, e => e["schema"]!.GetValue<string>() == "stdmachine.counters/1");
        Assert.Contains(all, e => e["schema"]!.GetValue<string>() == "stdmachine.identity/1");
        Assert.Contains(all, e => e["schema"]!.GetValue<string>() == "stdmachine.station.state/1");
        // Les fichiers sont répartis par famille
        var families = Directory.GetFiles(Path.Combine(_dir, "log")).Select(f => Path.GetFileNameWithoutExtension(f).Substring(9)).Distinct().ToList();
        Assert.Contains("part", families); Assert.Contains("alarm", families); Assert.Contains("state", families);

        // seq persistant après redémarrage
        long last = seq.Last;
        var again = new FileSeqStore(Path.Combine(_dir, "seq.txt"));
        Assert.Equal(last, again.Last);
        Assert.Equal(last + 1, again.Next());
    }

    [Fact]
    public void A_stop_event_carries_its_start_end_duration_and_cause()
    {
        var cat = Catalog();
        var client = new SimulatedPlcClient(SampleProject.Build(cat), cat, 2);
        var (gw, log, settings, seq) = NewGateway();
        gw.Process(client.Snapshot);
        client.SendCommandAsync(StateCommand.Reset, default).Wait(); client.Advance(8_000); gw.Process(client.Snapshot);
        client.SendCommandAsync(StateCommand.Start, default).Wait(); client.Advance(20_000); gw.Process(client.Snapshot);
        client.SendCommandAsync(StateCommand.Hold, default).Wait(); client.Advance(30_000); gw.Process(client.Snapshot);
        client.SendCommandAsync(StateCommand.Start, default).Wait(); client.Advance(1_000); gw.Process(client.Snapshot);
        gw.FlushAsync().Wait();
        var stop = ReadAll(Path.Combine(_dir, "log")).Single(e => e["schema"]!.GetValue<string>() == "stdmachine.event.stop/1" && e["data"]!["state"]!.GetValue<string>() == "Held")["data"]!;
        Assert.Equal("Held", stop["state"]!.GetValue<string>());
        Assert.InRange(stop["durationSeconds"]!.GetValue<double>(), 29, 33);
        Assert.Equal("operator", stop["cause"]!["kind"]!.GetValue<string>());
    }

    [Fact]
    public void Disabled_topics_are_not_published()
    {
        var cat = Catalog();
        var client = new SimulatedPlcClient(SampleProject.Build(cat), cat, 2);
        var (gw, log, settings, seq) = NewGateway();
        settings.DisabledTopics.Add("counters");
        gw.Process(client.Snapshot);
        gw.FlushAsync().Wait();
        Assert.DoesNotContain(ReadAll(Path.Combine(_dir, "log")), e => e["schema"]!.GetValue<string>() == "stdmachine.counters/1");
    }

    [Fact]
    public void Log_channel_can_be_tested_enabled_and_disabled_and_planned_channels_stay_inactive()
    {
        var (gw, log, settings, seq) = NewGateway();
        Assert.Equal(PlantChannelState.Connected, log.Status.State);
        Assert.True(log.TestAsync(settings.For("log"), default).Result.Success);
        settings.For("log").Enabled = false;
        gw.ApplySettingsAsync(settings, default).Wait();
        Assert.Equal(PlantChannelState.Disabled, log.Status.State);
        settings.For("mqtt").Enabled = true;
        settings.For("mqtt").Values["host"] = "broker.local";
        gw.ApplySettingsAsync(settings, default).Wait();
        var mqtt = gw.Channels.Single(c => c.Id == "mqtt");
        Assert.False(mqtt.IsImplemented);
        Assert.Equal(PlantChannelState.Planned, mqtt.Status.State);
        Assert.False(mqtt.TestAsync(settings.For("mqtt"), default).Result.Success);
        Assert.Equal("broker.local", gw.Settings.For("mqtt").Values["host"]);
        // Un dossier « sous un fichier » ne peut pas être créé
        string blocker = Path.Combine(_dir, "bloqueur");
        File.WriteAllText(blocker, "x");
        settings.For("log").Values["directory"] = Path.Combine(blocker, "sous-dossier");
        Assert.False(log.TestAsync(settings.For("log"), default).Result.Success);
    }

    [Fact]
    public void Settings_are_saved_with_encrypted_secrets_and_never_shown_in_clear()
    {
        var protector = new KeyedSecretProtector(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var store = new CommunicationSettingsStore(Path.Combine(_dir, "config", "communications.json"), protector);
        var s = new CommunicationSettings { MachineId = "MX-4" };
        s.For("mqtt").Enabled = true;
        s.For("mqtt").Values["host"] = "broker.local";
        s.For("mqtt").Secrets["password"] = "tres-secret";
        store.Save(s);
        string file = File.ReadAllText(Path.Combine(_dir, "config", "communications.json"));
        Assert.DoesNotContain("tres-secret", file);
        Assert.Contains("enc:", file);
        Assert.Equal("tres-secret", store.Load().For("mqtt").Secrets["password"]);
        Assert.DoesNotContain("tres-secret", Newtonsoft(CommunicationSettingsStore.Redacted(s)));
        Assert.Equal("tres-secret", s.For("mqtt").Secrets["password"]); // l'original n'est pas modifié
        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => new KeyedSecretProtector(new byte[] { 9 }).Unprotect(protector.Protect("x")));
    }

    private static string Newtonsoft(CommunicationSettings s) => System.Text.Json.JsonSerializer.Serialize(s);

    [Fact]
    public void Log_files_older_than_the_retention_are_purged()
    {
        string dir = Path.Combine(_dir, "log");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "20260901-part.jsonl"), "x");
        File.WriteAllText(Path.Combine(dir, "20261014-part.jsonl"), "x");
        var settings = new ChannelSettings { Enabled = true };
        settings.Values["directory"] = dir;
        var log = new LogFileChannel(() => new DateTime(2026, 10, 16));
        log.StartAsync(settings, default).Wait();
        Assert.False(File.Exists(Path.Combine(dir, "20260901-part.jsonl")));
        Assert.True(File.Exists(Path.Combine(dir, "20261014-part.jsonl")));
    }

    [Fact]
    public void Schemas_export_one_json_schema_per_message_and_the_validator_rejects_bad_messages()
    {
        string dir = Path.Combine(_dir, "schemas");
        Schemas.Export(dir);
        Assert.Equal(Schemas.All.Count, Directory.GetFiles(dir, "*.schema.json").Length);
        var one = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "stdmachine.event.part-1.schema.json")))!;
        Assert.Equal("stdmachine.event.part/1", one["title"]!.GetValue<string>());
        Assert.Equal(Schemas.All.Select(s => s.Id).Distinct().Count(), Schemas.All.Count);

        var bad = new PlantMessage("stdmachine.event.alarm/1", "MX-4", 1, DateTimeOffset.UtcNow, "events/alarm", new JsonObject { ["event"] = "explosion", ["severity"] = "stationFault", ["station"] = "P1", ["code"] = "pas un nombre", ["subCode"] = 0, ["text"] = "x" });
        var errors = Schemas.Validate(bad);
        Assert.Contains(errors, e => e.Contains("énumération"));
        Assert.Contains(errors, e => e.Contains("code"));
        Assert.Contains(Schemas.Validate(new PlantMessage("inconnu/1", "MX-4", 1, DateTimeOffset.UtcNow, "x", new JsonObject())), e => e.Contains("inconnu"));
        Assert.Contains(Schemas.Validate(new PlantMessage("stdmachine.status/1", "MX-4", 0, DateTimeOffset.UtcNow, "status", new JsonObject())), e => e.Contains("seq"));
    }
}
