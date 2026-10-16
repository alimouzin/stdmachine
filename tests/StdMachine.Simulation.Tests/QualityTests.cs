using StdMachine.Core;
using StdMachine.Simulation.Demo;
using StdMachine.Simulation.Engine;
using Xunit;
using Xunit.Abstractions;
using static StdMachine.Simulation.Tests.Harness;

namespace StdMachine.Simulation.Tests;

public class QualityTests
{
    private readonly ITestOutputHelper _out;

    public QualityTests(ITestOutputHelper o) { _out = o; }

    private static MachineSim NewMachine(int seed = 1) => new MachineSim(Compile(Sample()), Catalog(), seed);

    [Fact]
    public void Sliding_window_fills_rotates_and_waits_for_the_minimum_sample()
    {
        var m = NewMachine();
        var q = m.Quality;
        // Mover : fenêtre 20, échantillon minimal 10, seuils de taux 10 % / 20 %, successifs 2 / 3
        for (int i = 0; i < 9; i++) q.Record(QualityScope.Mover, 1, i % 4 == 3); // 2 mauvais sur 9, jamais deux d'affilée
        var t = q.Track(QualityScope.Mover, 1);
        Assert.Equal(9, t.Count);
        Assert.Null(t.RatePercent(m.Compiled.Machine.Quality.Mover));
        q.Record(QualityScope.Mover, 1, false);
        Assert.Equal(10, t.Count);
        Assert.Equal(20.0, t.RatePercent(m.Compiled.Machine.Quality.Mover));
        // La fenêtre tourne : après 30 bons de plus, plus aucun mauvais dedans
        for (int i = 0; i < 30; i++) q.Record(QualityScope.Mover, 1, false);
        Assert.Equal(20, t.Count);
        Assert.Equal(0, t.BadInWindow);
    }

    [Fact]
    public void Consecutive_rejects_warn_then_react_and_a_good_result_resets_the_counter()
    {
        var m = NewMachine();
        var q = m.Quality;
        q.Record(QualityScope.Mover, 2, true);
        q.Record(QualityScope.Mover, 2, false);
        Assert.Equal(0, q.Track(QualityScope.Mover, 2).Consecutive);
        q.Record(QualityScope.Mover, 2, true);
        q.Record(QualityScope.Mover, 2, true);
        Assert.Equal(QualityLevel.Warning, q.Track(QualityScope.Mover, 2).Level);
        Assert.Contains(m.Alarms.All, a => a.Code == 955 && a.Severity == AlarmSeverity.Warning);
        q.Record(QualityScope.Mover, 2, true);
        var t = q.Track(QualityScope.Mover, 2);
        Assert.True(t.Locked);
        Assert.Equal(QualityLevel.Action, t.Level);
        Assert.Contains(m.Alarms.All, a => a.Code == 955 && a.Severity == AlarmSeverity.Quality);
        Assert.True(m.Movers.First(x => x.Number == 2).OutOfService, "réaction : mover hors service");
    }

    [Fact]
    public void Release_resets_the_scope_and_starts_a_probation_where_two_rejects_trigger()
    {
        var m = NewMachine();
        var q = m.Quality;
        for (int i = 0; i < 3; i++) q.Record(QualityScope.Mover, 3, true);
        Assert.True(q.Release(QualityScope.Mover, 3));
        var t = q.Track(QualityScope.Mover, 3);
        Assert.Equal(0, t.Consecutive); Assert.Equal(0, t.Count); Assert.False(t.Locked);
        Assert.Equal(10, t.ProbationLeft);
        Assert.Equal(QualityLevel.Probation, t.Level);
        Assert.False(m.Movers.First(x => x.Number == 3).OutOfService);
        q.Record(QualityScope.Mover, 3, true);
        q.Record(QualityScope.Mover, 3, true);
        Assert.True(t.Locked, "deux rebuts successifs suffisent en surveillance renforcée");
        Assert.False(q.Release(QualityScope.Mover, 2), "rien à libérer sur une portée sans verrou");
    }

    [Fact]
    public void Station_hold_removes_the_station_from_routing_and_release_returns_it()
    {
        var m = NewMachine(3);
        m.Command(StateCommand.Reset);
        for (int i = 0; i < 6000 && m.Pack.State != PackMlState.Idle; i++) m.Step(10);
        m.Command(StateCommand.Start);
        m.Run(200);
        int p2 = m.StationIndex(DemoProject.Pressage02);
        for (int i = 0; i < 3; i++) m.Quality.Record(QualityScope.Station, p2, true);
        m.Run(500);
        var st = m.StationByNumber(DemoProject.Pressage02)!;
        Assert.True(st.QualityHold);
        Assert.Equal(PackMlState.Held, st.State);
        Assert.Equal(PackMlState.Execute, m.Pack.State);
        Assert.True(m.Quality.Release(QualityScope.Station, p2));
        m.Run(500);
        Assert.Equal(PackMlState.Execute, st.State);
    }

    [Fact]
    public void Function_reaction_stops_loading_and_machine_reaction_stops_the_machine()
    {
        var m = NewMachine();
        m.Command(StateCommand.Reset);
        for (int i = 0; i < 6000 && m.Pack.State != PackMlState.Idle; i++) m.Step(10);
        m.Command(StateCommand.Start);
        m.Run(100);
        for (int i = 0; i < 5; i++) m.Quality.Record(QualityScope.Group, 2, true);
        Assert.True(m.LoadingLocked);
        Assert.Contains("Arrêt qualité", m.LoadingLockedReason);
        Assert.True(m.Quality.Release(QualityScope.Group, 2));
        Assert.False(m.LoadingLocked);
        for (int i = 0; i < 5; i++) m.Quality.Record(QualityScope.Machine, 0, true);
        for (int i = 0; i < 2000 && m.Pack.State != PackMlState.Stopped; i++) m.Step(10);
        Assert.Equal(PackMlState.Stopped, m.Pack.State);
        Assert.Equal("Arrêt qualité", m.StopCause);
    }

    [Fact]
    public void Faulty_press_is_caught_by_the_quality_monitor_while_production_continues_on_the_other_station()
    {
        var m = NewMachine(8);
        var p2 = m.StationByNumber(DemoProject.Pressage02)!;
        foreach (var o in p2.Objects.Where(o => o.TypeCode == "Press")) o.FailureRate = 0.5;
        m.Command(StateCommand.Reset);
        for (int i = 0; i < 6000 && m.Pack.State != PackMlState.Idle; i++) m.Step(10);
        m.Command(StateCommand.Start);
        for (int i = 0; i < 60_000 && !p2.QualityHold; i++) m.Step(20);
        _out.WriteLine("t=" + m.NowMs / 1000 + " s, hold=" + p2.QualityHold);
        Assert.True(p2.QualityHold, "Pressage 02 passe en arrêt qualité");
        Assert.Equal(PackMlState.Execute, m.Pack.State);
        var p1 = m.StationByNumber(DemoProject.Pressage01)!;
        Assert.False(p1.QualityHold);
        int before = m.PartsExited;
        m.Run(300_000, 20);
        Assert.True(m.PartsExited > before, "la production continue sur Pressage 01");
        Assert.Equal(PackMlState.Held, p2.State);
        Assert.Contains(m.Alarms.All, a => a.Severity == AlarmSeverity.Quality);
    }

    [Fact]
    public void Defect_rejects_do_not_count_in_the_rates()
    {
        var m = NewMachine(9);
        m.Command(StateCommand.Reset);
        for (int i = 0; i < 6000 && m.Pack.State != PackMlState.Idle; i++) m.Step(10);
        m.Command(StateCommand.Start);
        var p1 = m.StationByNumber(DemoProject.Pressage01)!;
        for (int i = 0; i < 120_000 && !p1.OperationRunning; i++) m.Step(10);
        m.Run(1000);
        p1.RaiseFault("Défaut de test");
        m.Run(2_000);
        Assert.True(m.StationCommand(DemoProject.Pressage01, StateCommand.Clear).Accepted);
        m.Run(120_000);
        Assert.Equal(0, m.Quality.Track(QualityScope.Station, m.StationIndex(DemoProject.Pressage01)).TotalBad);
        Assert.True(m.PartsRejected >= 1);
        Assert.Equal(0, m.Quality.Track(QualityScope.Machine, 0).TotalBad);
    }
}
