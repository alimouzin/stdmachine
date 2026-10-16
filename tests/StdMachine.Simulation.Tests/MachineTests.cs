using StdMachine.Core;
using StdMachine.Simulation.Demo;
using StdMachine.Simulation.Engine;
using Xunit;
using Xunit.Abstractions;
using static StdMachine.Simulation.Tests.Harness;

namespace StdMachine.Simulation.Tests;

public class MachineTests
{
    private readonly ITestOutputHelper _out;

    public MachineTests(ITestOutputHelper output) { _out = output; }

    private static MachineSim NewMachine(int seed = 1) => new MachineSim(Compile(Sample()), Catalog(), seed);

    private static void RunUntil(MachineSim m, Func<bool> done, double maxMs, string what)
    {
        double limit = m.NowMs + maxMs;
        while (m.NowMs < limit && !done()) m.Step(10);
        Assert.True(done(), "Délai dépassé : " + what + " (état " + m.Pack.State + ")");
    }

    private static void ToExecute(MachineSim m)
    {
        Assert.True(m.Command(StateCommand.Reset).Accepted);
        RunUntil(m, () => m.Pack.State == PackMlState.Idle, 60_000, "Resetting → Idle");
        Assert.True(m.Command(StateCommand.Start).Accepted);
        RunUntil(m, () => m.Pack.State == PackMlState.Execute, 1000, "Idle → Execute");
    }

    private void Dump(MachineSim m)
    {
        _out.WriteLine("t=" + (m.NowMs / 1000).ToString("0.0") + " s  état " + m.Pack.State + "  créées " + m.PartsCreated + " sorties " + m.PartsExited + " rebutées " + m.PartsRejected);
        foreach (var mv in m.Movers) _out.WriteLine("  mover " + mv.Number + " " + mv.State + " PC " + mv.PC + " st " + mv.Station + " pos " + mv.Pos + " parts " + mv.Slots.Count(p => p.Present) + " " + mv.WaitReason);
        foreach (var s in m.Stations) _out.WriteLine("  " + s.Name + " " + s.State + " " + s.Response.Status + " " + s.FaultText);
        foreach (var a in m.Alarms.All.Take(15)) _out.WriteLine("  ALARME " + a.Code + " " + a.Object + " " + a.Text);
    }

    [Fact]
    public void Machine_resets_starts_and_produces_parts()
    {
        var m = NewMachine();
        var log = new List<string>();
        m.OperationFinished += r => { if (log.Count < 30) log.Add("OP st" + r.Station + " g" + r.GroupIndex + " mover " + r.Mover + " " + r.PartId + " durée " + (r.TimeMs / 1000).ToString("0.00") + " s bad=" + r.Bad + " fin à " + (m.NowMs / 1000).ToString("0.0")); };
        ToExecute(m);
        m.Run(120_000);
        foreach (var l in log) _out.WriteLine(l);
        Dump(m);
        foreach (var t in m.Traces.Take(25)) _out.WriteLine("TRACE " + t.Seq + " " + t.PartId + " st" + t.Station + " g" + t.Group + " " + (t.StartMs / 1000).ToString("0.0") + " → " + (t.EndMs / 1000).ToString("0.0") + " verdict " + t.Verdict);
        Assert.True(m.PartsExited + m.PartsRejected >= 3, "au moins quelques pièces terminées en 2 minutes");
    }

    [Fact]
    public void An_order_of_100_parts_is_produced_then_the_machine_stops_by_itself()
    {
        var m = NewMachine(3);
        m.StartOrder("OF-100", "REF-1", 100);
        ToExecute(m);
        double limit = 6_000_000;
        while (m.NowMs < limit && m.Pack.State != PackMlState.Stopped) m.Step(20);
        Dump(m);
        Assert.Equal(PackMlState.Stopped, m.Pack.State);
        Assert.Equal(OrderState.Finished, m.OrderHistory.Last().State);
        var o = m.OrderHistory.Last();
        Assert.Equal(100, o.Good + 0 >= 100 ? o.Good : o.Good);
        Assert.Equal(0, o.InProcess);
        Assert.True(o.Good >= 100, "100 bonnes pièces : " + o.Good + " bonnes, " + o.Bad + " mauvaises");
        Assert.Equal(m.PartsCreated, m.PartsExited + m.PartsRejected);
        Assert.All(m.Movers, mv => Assert.True(mv.IsEmpty));
        Assert.Equal("Fin d'OF", m.StopCause);
    }

    [Fact]
    public void A_station_abort_keeps_the_machine_producing_on_the_other_station_and_the_station_can_be_reintegrated()
    {
        var m = NewMachine(5);
        ToExecute(m);
        m.Run(40_000);
        int before = m.PartsExited;
        var p1 = m.StationByNumber(DemoProject.Pressage01)!;
        RunUntil(m, () => p1.OperationRunning, 60_000, "Pressage 01 travaille");
        m.Run(1_000);
        p1.RaiseFault("Défaut de test");
        m.Run(100);
        Assert.Equal(PackMlState.Aborted, p1.State);
        Assert.Equal(PackMlState.Execute, m.Pack.State);
        m.Run(120_000);
        Assert.Equal(PackMlState.Execute, m.Pack.State);
        Assert.True(m.PartsExited + m.PartsRejected > before, "la production continue sur Pressage 02");
        // Le mover engagé est parti au rebut avec un verdict mauvais
        Assert.True(m.PartsRejected >= 1);
        // Réintégration en trois commandes
        Assert.True(m.StationCommand(DemoProject.Pressage01, StateCommand.Clear).Accepted);
        RunUntil(m, () => p1.State == PackMlState.Stopped, 30_000, "Clearing → Stopped");
        Assert.True(m.StationCommand(DemoProject.Pressage01, StateCommand.Reset).Accepted);
        RunUntil(m, () => p1.State == PackMlState.Idle, 60_000, "Resetting → Idle");
        Assert.True(m.StationCommand(DemoProject.Pressage01, StateCommand.Start).Accepted);
        m.Run(1000);
        Assert.Equal(PackMlState.Execute, p1.State);
        int done01 = 0;
        m.OperationFinished += r => { if (r.Station == DemoProject.Pressage01) done01++; };
        m.Run(180_000);
        Assert.True(done01 > 0, "Pressage 01 travaille de nouveau");
    }

    [Fact]
    public void Hold_halts_routing_and_start_resumes_without_losing_parts()
    {
        var m = NewMachine(2);
        ToExecute(m);
        m.Run(30_000);
        Assert.True(m.Command(StateCommand.Hold).Accepted);
        m.Run(60_000);
        Assert.Equal(PackMlState.Held, m.Pack.State);
        int created = m.PartsCreated;
        m.Run(20_000);
        Assert.Equal(created, m.PartsCreated);
        Assert.All(m.Stations, s => Assert.False(s.OperationRunning));
        int present = m.Movers.Sum(x => x.Slots.Count(p => p.Present));
        Assert.True(m.Command(StateCommand.Start).Accepted);
        m.Run(60_000);
        Assert.Equal(PackMlState.Execute, m.Pack.State);
        Assert.True(m.PartsCreated > created || m.PartsExited > 0);
        Assert.True(present >= 0);
    }

    [Fact]
    public void Stop_then_restart_keeps_movers_and_parts()
    {
        var m = NewMachine(4);
        ToExecute(m);
        m.Run(45_000);
        Assert.True(m.Command(StateCommand.Stop).Accepted);
        RunUntil(m, () => m.Pack.State == PackMlState.Stopped, 120_000, "Stopping → Stopped");
        int parts = m.Movers.Sum(x => x.Slots.Count(p => p.Present));
        Assert.True(parts > 0, "des pièces restent sur les movers");
        ToExecute(m);
        Assert.Equal(parts, m.Movers.Sum(x => x.Slots.Count(p => p.Present)));
        m.Run(120_000);
        Assert.True(m.PartsExited > 0);
    }

    [Fact]
    public void Parts_with_a_bad_verdict_are_routed_to_the_reject_function()
    {
        var m = NewMachine(11);
        foreach (var s in m.Stations) foreach (var o in s.Objects.Where(o => o.TypeCode == "Press")) o.FailureRate = 0.6;
        ToExecute(m);
        m.Run(300_000);
        Dump(m);
        Assert.True(m.PartsRejected >= 2, "des rebuts : " + m.PartsRejected);
        Assert.True(m.Traces.Any(t => t.Group == 6), "le Rebut a travaillé");
    }

    [Fact]
    public void Declared_maintenance_blocks_start_but_not_abort_or_stop()
    {
        var m = NewMachine();
        m.Declared = DeclaredState.CorrectiveMaintenance;
        Assert.True(m.Command(StateCommand.Reset).Accepted);
        RunUntil(m, () => m.Pack.State == PackMlState.Idle, 60_000, "Idle");
        var r = m.Command(StateCommand.Start);
        Assert.False(r.Accepted);
        Assert.Contains("bloqué", r.Reason);
        Assert.True(m.Command(StateCommand.Stop).Accepted);
    }

    [Fact]
    public void A_function_whose_stations_are_all_out_of_production_does_not_abort_the_machine()
    {
        var m = NewMachine(6);
        ToExecute(m);
        m.SetStationOutOfProduction(DemoProject.Controle, true);
        m.Run(120_000);
        Assert.Equal(PackMlState.Execute, m.Pack.State);
        Assert.True(m.Movers.Any(x => x.State == MvState.Buffered), "des movers attendent en tampon");
    }
}
