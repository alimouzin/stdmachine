using StdMachine.Core;
using StdMachine.Simulation.Demo;
using StdMachine.Simulation.Engine;
using Xunit;
using Xunit.Abstractions;
using static StdMachine.Simulation.Tests.Harness;

namespace StdMachine.Simulation.Tests;

public class ClientTests
{
    private readonly ITestOutputHelper _out;

    public ClientTests(ITestOutputHelper o) { _out = o; }

    private static SimulatedPlcClient NewClient(int seed = 1) => new SimulatedPlcClient(Sample(), Catalog(), seed);

    private static void Start(SimulatedPlcClient c)
    {
        Assert.True(c.SendCommandAsync(StateCommand.Reset, default).Result.Accepted);
        c.Advance(8_000);
        Assert.Equal(PackMlState.Idle, c.Snapshot.Machine.State);
        Assert.True(c.SendCommandAsync(StateCommand.Start, default).Result.Accepted);
        c.Advance(100);
    }

    [Fact]
    public void Snapshot_is_coherent_with_the_deployed_project_and_the_simulator_connection()
    {
        var c = NewClient();
        var s = c.Snapshot;
        Assert.Equal(PlcConnectionState.Simulation, s.Connection);
        Assert.Equal(PackMlState.Stopped, s.Machine.State);
        Assert.Equal(6, s.Stations.Count);
        Assert.Equal(6, s.Movers.Count);
        Assert.Equal(16, s.Machine.ProjectHash.Length);
        Assert.All(s.Stations, st => Assert.Equal(PackMlState.Stopped, st.State));
        Assert.Equal(new[] { "Pressage pignon", "Sertissage" }, s.Station(DemoProject.Pressage01)!.Groups.ToArray());
        Assert.True(s.Objects.Count > 40);
    }

    [Fact]
    public void Commands_drive_the_machine_and_production_shows_up_in_the_snapshot()
    {
        var c = NewClient();
        Start(c);
        c.Advance(60_000);
        var s = c.Snapshot;
        Assert.Equal(PackMlState.Execute, s.Machine.State);
        Assert.Contains(s.Movers, m => m.State == MoverState.InStation);
        var working = s.Stations.Where(st => st.Operation != null).ToList();
        Assert.NotEmpty(working);
        Assert.All(working, st => Assert.NotEmpty(st.Steps));
        Assert.Contains(working.SelectMany(st => st.Steps), r => r.State == StepRunState.Running);
        Assert.Contains(working.SelectMany(st => st.Steps), r => r.State == StepRunState.Done);
        Assert.Contains(s.Objects, o => o.State == ObjectState.Busy);
        Assert.NotEmpty(s.Movers.SelectMany(m => m.Parts));
        Assert.Equal(PackMlState.Execute, s.Machine.State);
        Assert.Equal(BeaconLights.Green, s.Machine.Beacon.Lights & BeaconLights.Green);
    }

    [Fact]
    public void Refused_commands_carry_a_reason()
    {
        var c = NewClient();
        var r = c.SendCommandAsync(StateCommand.Start, default).Result;
        Assert.False(r.Accepted);
        Assert.Contains("refusée", r.Reason);
    }

    [Fact]
    public void Abort_forced_on_a_station_shows_a_station_fault_alarm_and_clear_reintegrates()
    {
        var c = NewClient();
        Start(c);
        c.Advance(30_000);
        c.ForceStationAbort(DemoProject.Pressage01);
        c.Advance(200);
        var s = c.Snapshot;
        Assert.Equal(PackMlState.Aborted, s.Station(DemoProject.Pressage01)!.State);
        Assert.Equal(PackMlState.Execute, s.Machine.State);
        Assert.Contains(s.Machine.Beacon.Blinking.ToString(), "Red");
        Assert.True(c.SendStationCommandAsync(DemoProject.Pressage01, StateCommand.Clear, default).Result.Accepted);
        c.Advance(5_000);
        Assert.Equal(PackMlState.Stopped, c.Snapshot.Station(DemoProject.Pressage01)!.State);
    }

    [Fact]
    public void Manual_action_runs_in_maintenance_on_a_stopped_station_and_is_refused_otherwise()
    {
        var c = NewClient();
        var cyl = c.Snapshot.Objects.First(o => o.StationNumber == DemoProject.Controle && o.ObjectId == c.Project.Station(DemoProject.Controle)!.AllObjects().First(x => x.TypeCode == "Cylinder").Id);
        var refused = c.ExecuteManualAsync(new ManualActionRequest(DemoProject.Controle, cyl.ObjectId, "Move travail", new double[0]), default).Result;
        Assert.False(refused.Accepted);
        Assert.True(c.SetModeAsync(MachineMode.Maintenance, default).Result.Accepted);
        c.Advance(100);
        var task = c.ExecuteManualAsync(new ManualActionRequest(DemoProject.Controle, cyl.ObjectId, "Move travail", new double[0]), default);
        for (int i = 0; i < 100 && !task.IsCompleted; i++) { c.Advance(100); Thread.Sleep(10); }
        Assert.True(task.IsCompleted);
        Assert.True(task.Result.Accepted, task.Result.Reason);
        var after = c.Snapshot.Objects.First(o => o.ObjectId == cyl.ObjectId);
        Assert.Equal(2, after.Values[0]);
    }

    [Fact]
    public void Release_quality_and_out_of_production_flow_through_the_client()
    {
        var c = NewClient();
        Start(c);
        c.Advance(1_000);
        Assert.True(c.SetStationOutOfProductionAsync(DemoProject.Pressage01, true, "Réglage", default).Result.Accepted);
        c.Advance(5_000);
        var s = c.Snapshot;
        Assert.True(s.Station(DemoProject.Pressage01)!.OutOfProduction);
        Assert.Single(s.Forces.Entries);
        Assert.Equal(PackMlState.Held, s.Station(DemoProject.Pressage01)!.State);
        c.Sim.Quality.Record(QualityScope.Mover, 1, true); c.Sim.Quality.Record(QualityScope.Mover, 1, true); c.Sim.Quality.Record(QualityScope.Mover, 1, true);
        c.Advance(200);
        Assert.Contains(c.Snapshot.Quality.Tracks, t => t.Locked && t.Name == "Mover 1");
        Assert.True(c.ReleaseQualityAsync(QualityScope.Mover, "Mover 1", "Fausse alarme", default).Result.Accepted);
        foreach (var t in c.Snapshot.Quality.Tracks.Where(t => t.Locked)) _out.WriteLine("VERROUILLÉ " + t.Scope + " " + t.Name + " " + t.Reason);
        Assert.DoesNotContain(c.Snapshot.Quality.Tracks, t => t.Locked);
    }

    [Fact]
    public void Step_mode_runs_through_the_client_in_maintenance()
    {
        var c = NewClient();
        Assert.True(c.SetModeAsync(MachineMode.Maintenance, default).Result.Accepted);
        c.Advance(100);
        Assert.True(c.SendStationCommandAsync(DemoProject.Controle, StateCommand.Reset, default).Result.Accepted);
        c.Advance(8_000);
        var proc = c.Project.Station(DemoProject.Controle)!.Processes.First(x => x.Name == "Contrôler");
        Assert.True(c.SetStepModeAsync(DemoProject.Controle, StepGranularity.Step, default).Result.Accepted);
        var launch = c.LaunchTableAsync(new LaunchRequest(DemoProject.Controle, proc.Id, null), default).Result;
        Assert.True(launch.Accepted, launch.Reason);
        c.Advance(500);
        var st = c.Snapshot.Station(DemoProject.Controle)!;
        foreach (var r in st.Steps) _out.WriteLine("CARTE " + r.StepId + " " + r.State);
        _out.WriteLine("op " + st.Operation + " ctx " + string.Join(",", c.Sim.StationByNumber(DemoProject.Controle)!.Contexts.Select(x => x.Status + "/" + x.PC)));
        Assert.Contains(st.Steps, r => r.State == StepRunState.StepPending || r.State == StepRunState.Next);
        for (int i = 0; i < 2; i++) { Assert.True(c.StepAsync(DemoProject.Controle, default).Result.Accepted); c.Advance(2_000); }
        Assert.Contains(c.Snapshot.Station(DemoProject.Controle)!.Steps, r => r.State == StepRunState.Done);
        Assert.True(c.SetStepModeAsync(DemoProject.Controle, null, default).Result.Accepted);
        c.Advance(8_000);
        Assert.Null(c.Snapshot.Station(DemoProject.Controle)!.Operation);
    }

    [Fact]
    public void Deploy_loads_a_modified_project_when_stopped_and_refuses_it_while_running()
    {
        var c = NewClient();
        var changed = Sample();
        changed.Station(DemoProject.Controle)!.Processes.First(x => x.Name == "Contrôler").Body.Steps.Add(new WaitTimeStep { DurationSeconds = 1 });
        string before = c.Snapshot.Machine.ProjectHash;
        Assert.True(c.Deploy(changed).Accepted);
        Assert.NotEqual(before, c.Snapshot.Machine.ProjectHash);
        Start(c);
        Assert.False(c.Deploy(changed).Accepted);
    }

    [Fact]
    public void Real_time_loop_publishes_snapshots_at_most_10_times_per_second_and_honours_the_time_scale()
    {
        var c = NewClient();
        int count = 0;
        c.SnapshotUpdated += (s, e) => Interlocked.Increment(ref count);
        c.TimeScale = 4;
        c.ConnectAsync(default).Wait();
        c.SendCommandAsync(StateCommand.Reset, default).Wait();
        Thread.Sleep(2200);
        c.DisposeAsync().Wait();
        Assert.InRange(count, 5, 26);
        Assert.True(c.Snapshot.ScenarioSeconds > 5, "échelle ×4 : plus de 5 s simulées en 2 s : " + c.Snapshot.ScenarioSeconds);
    }

    private static SimulatedPlcClient Scenario(string name)
    {
        var cat = Catalog();
        var c = new SimulatedPlcClient(SampleProject.Build(cat), cat, 1);
        SimScenarios.Apply(c, name);
        return c;
    }

    [Fact]
    public void Scenario_idle_is_a_stopped_machine()
    {
        var s = Scenario("idle").Snapshot;
        Assert.Equal(PackMlState.Stopped, s.Machine.State);
        Assert.All(s.Movers, m => Assert.Empty(m.Parts));
    }

    [Fact]
    public void Scenario_running_has_been_producing_for_30_seconds()
    {
        var s = Scenario("running").Snapshot;
        Assert.Equal(PackMlState.Execute, s.Machine.State);
        Assert.Contains(s.Movers, m => m.Parts.Count > 0);
        Assert.Contains(s.Stations, st => st.Operation != null);
    }

    [Fact]
    public void Scenario_station_aborted_keeps_production_on_the_other_press()
    {
        var c = Scenario("station-aborted");
        var s = c.Snapshot;
        Assert.Equal(PackMlState.Aborted, s.Station(DemoProject.Pressage01)!.State);
        Assert.Equal(PackMlState.Execute, s.Machine.State);
        Assert.Equal(PackMlState.Execute, s.Station(DemoProject.Pressage02)!.State);
        Assert.Contains(s.Alarms, a => a.Severity == AlarmSeverity.StationFault && a.State == AlarmState.Active);
    }

    [Fact]
    public void Scenario_step_mode_stops_station_2_in_maintenance_with_a_pending_card()
    {
        var s = Scenario("step-mode").Snapshot;
        var st = s.Station(DemoProject.Pressage02)!;
        Assert.Equal(MachineMode.Maintenance, st.Mode);
        Assert.Contains(st.Steps, r => r.State == StepRunState.StepPending);
        Assert.Contains(st.Steps, r => r.State == StepRunState.Done);
    }

    [Fact]
    public void Scenario_manual_has_station_1_idle_in_maintenance()
    {
        var s = Scenario("manual").Snapshot;
        Assert.Equal(MachineMode.Maintenance, s.Machine.Mode);
        Assert.Equal(PackMlState.Idle, s.Station(DemoProject.Pressage01)!.State);
    }

    [Fact]
    public void Scenario_validation_errors_shows_the_faulty_project_but_deploys_the_valid_one()
    {
        var cat = Catalog();
        var shown = SimScenarios.ProjectFor("validation-errors", cat);
        var issues = StdMachine.Core.Validation.ProjectValidator.Validate(shown, cat);
        Assert.Contains(issues, i => i.Code == "V001");
        Assert.Contains(issues, i => i.Code == "V004");
        Assert.Equal(PackMlState.Execute, Scenario("validation-errors").Snapshot.Machine.State);
    }

    [Fact]
    public void Scenario_quality_hold_holds_press_2_while_press_1_works()
    {
        var s = Scenario("quality-hold").Snapshot;
        Assert.True(s.Station(DemoProject.Pressage02)!.QualityHold);
        Assert.Equal(PackMlState.Held, s.Station(DemoProject.Pressage02)!.State);
        Assert.Equal(PackMlState.Execute, s.Station(DemoProject.Pressage01)!.State);
        Assert.Contains(s.Quality.Tracks, t => t.Locked);
    }

    [Fact]
    public void Scenario_forced_declares_a_blocking_state_and_removes_station_and_mover_from_production()
    {
        var s = Scenario("forced").Snapshot;
        Assert.True(s.Forces.StartBlocked);
        Assert.Equal(DeclaredState.CorrectiveMaintenance, s.Forces.Declared);
        Assert.True(s.Station(DemoProject.Pressage01)!.OutOfProduction);
        Assert.Equal(PackMlState.Execute, s.Station(DemoProject.Pressage02)!.State);
        Assert.Equal(2, s.Forces.Entries.Count);
        Assert.Equal(PackMlState.Execute, s.Machine.State);
    }

    [Fact]
    public void Quality_page_data_is_complete_from_the_start_with_windows_of_0_and_1()
    {
        var c = NewClient();
        var q = c.Snapshot.Quality;
        Assert.Contains(q.Tracks, t => t.Scope == QualityScope.Machine);
        Assert.Equal(6, q.Tracks.Count(t => t.Scope == QualityScope.Station));
        Assert.Equal(6, q.Tracks.Count(t => t.Scope == QualityScope.Group));
        Assert.Equal(6, q.Tracks.Count(t => t.Scope == QualityScope.Mover));
        Assert.Equal(2, q.Tracks.Count(t => t.Scope == QualityScope.StationImputed));
        c.Sim.Quality.Record(QualityScope.Mover, 1, true);
        c.Sim.Quality.Record(QualityScope.Mover, 1, false);
        c.Advance(10);
        Assert.Equal("10", c.Snapshot.Quality.Tracks.First(t => t.Scope == QualityScope.Mover && t.Name == "Mover 1").Window);
    }
}
