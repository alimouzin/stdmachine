using StdMachine.Core;
using StdMachine.Simulation.Demo;
using StdMachine.Simulation.Engine;
using Xunit;
using static StdMachine.Simulation.Tests.Harness;

namespace StdMachine.Simulation.Tests;

/// <summary>« Fin des pièces en cours » (docs/02 § 8, docs/05 § 5.7, D77) : plus de chargement, les pièces engagées finissent, puis Arrêter automatique.</summary>
public class DrainTests
{
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

    private static string Describe(MachineSim m)
        => "état " + m.Pack.State + ", drain " + m.Drain + ", pièces " + m.PartsInProcess + ", créées " + m.PartsCreated + " sorties " + m.PartsExited + " rebut " + m.PartsRejected + " ; movers : "
           + string.Join(" | ", m.Movers.Select(mv => mv.Number + " " + mv.State + " st" + mv.Station + " grp" + mv.TargetGroup + " pièces" + mv.Slots.Count(p => p.Present) + " " + mv.WaitReason))
           + " ; stations : " + string.Join(" | ", m.Stations.Select(s => s.Name + " " + s.State + (s.OperationRunning ? " op" : "")));

    /// <summary>Machine en production avec au moins une pièce engagée.</summary>
    private static MachineSim Producing(int seed = 1)
    {
        var m = NewMachine(seed);
        ToExecute(m);
        RunUntil(m, () => m.PartsInProcess >= 2, 120_000, "des pièces sont engagées");
        return m;
    }

    [Fact]
    public void Drain_is_refused_unless_the_machine_is_in_Execute_or_Held()
    {
        var m = NewMachine();
        var stopped = m.StartDrain();
        Assert.False(stopped.Accepted);
        Assert.Contains("Execute", stopped.Reason);

        Assert.True(m.Command(StateCommand.Reset).Accepted);
        RunUntil(m, () => m.Pack.State == PackMlState.Idle, 60_000, "Idle");
        Assert.False(m.StartDrain().Accepted);

        Assert.True(m.Command(StateCommand.Start).Accepted);
        RunUntil(m, () => m.Pack.State == PackMlState.Execute, 1000, "Execute");
        Assert.True(m.StartDrain().Accepted);
        Assert.True(m.Drain);
    }

    [Fact]
    public void Drain_is_accepted_in_Held()
    {
        var m = Producing();
        Assert.True(m.Command(StateCommand.Hold).Accepted);
        RunUntil(m, () => m.Pack.State == PackMlState.Held, 30_000, "Held");
        Assert.True(m.StartDrain().Accepted);
    }

    [Fact]
    public void Cancel_without_a_drain_in_progress_is_refused()
    {
        var m = Producing();
        Assert.False(m.CancelDrain().Accepted);
    }

    [Fact]
    public void No_new_part_is_loaded_and_the_machine_stops_by_itself_when_empty()
    {
        var m = Producing();
        int createdAtStart = m.PartsCreated;
        Assert.True(m.StartDrain().Accepted);

        RunUntil(m, () => m.Pack.State == PackMlState.Stopped, 180_000, "arrêt automatique");

        // Les pièces déjà chargées sont finies, rien de plus n'est créé (une opération d'entrée déjà lancée peut finir sa pièce).
        Assert.True(m.PartsCreated <= createdAtStart + 1, "pièces créées après le début : " + (m.PartsCreated - createdAtStart));
        Assert.Equal(0, m.PartsInProcess);
        Assert.All(m.Movers, mv => Assert.DoesNotContain(mv.Slots, p => p.Present));
        Assert.Equal(m.PartsCreated, m.PartsExited + m.PartsRejected);
        Assert.Equal(MachineSim.DrainedStopCause, m.StopCause);
        Assert.False(m.Drain);
    }

    [Fact]
    public void Drain_from_Held_resumes_nothing_and_waits_for_the_operator()
    {
        var m = Producing();
        Assert.True(m.Command(StateCommand.Hold).Accepted);
        RunUntil(m, () => m.Pack.State == PackMlState.Held, 30_000, "Held");
        Assert.True(m.StartDrain().Accepted);
        int before = m.PartsInProcess;
        m.Run(20_000);
        Assert.Equal(PackMlState.Held, m.Pack.State);
        Assert.True(m.Drain);
        Assert.Equal(before, m.PartsInProcess);

        // Démarrer : les pièces finissent, puis la machine se vide et s'arrête avec la cause « Machine vidée » (qui prime sur « Pause demandée »).
        Assert.True(m.Command(StateCommand.Start).Accepted);
        RunUntil(m, () => m.Pack.State == PackMlState.Stopped, 180_000, "arrêt automatique");
        Assert.Equal(MachineSim.DrainedStopCause, m.StopCause);
    }

    [Fact]
    public void Cancelling_the_drain_resumes_loading()
    {
        var m = Producing();
        Assert.True(m.StartDrain().Accepted);
        m.Run(2_000);
        Assert.True(m.CancelDrain().Accepted);
        Assert.False(m.Drain);

        int created = m.PartsCreated;
        m.Run(60_000);
        Assert.Equal(PackMlState.Execute, m.Pack.State);
        Assert.True(m.PartsCreated > created, "le chargement reprend");
    }

    [Fact]
    public void Abort_lifts_the_drain_mode()
    {
        var m = Producing();
        Assert.True(m.StartDrain().Accepted);
        Assert.True(m.Command(StateCommand.Abort).Accepted);
        RunUntil(m, () => m.Pack.State == PackMlState.Aborted, 5_000, "Aborted");
        Assert.False(m.Drain);
    }

    [Fact]
    public void Stop_lifts_the_drain_mode()
    {
        var m = Producing();
        Assert.True(m.StartDrain().Accepted);
        Assert.True(m.Command(StateCommand.Stop).Accepted);
        RunUntil(m, () => m.Pack.State == PackMlState.Stopped, 120_000, "Stopped");
        Assert.False(m.Drain);
        Assert.Equal("Arrêt demandé", m.StopCause);
    }

    [Fact]
    public void The_order_is_not_modified_and_production_resumes_at_the_next_start()
    {
        var m = NewMachine(2);
        m.StartOrder("OF-DRAIN", "REF-1", 100);
        ToExecute(m);
        RunUntil(m, () => m.PartsInProcess >= 2, 120_000, "des pièces sont engagées");
        Assert.True(m.StartDrain().Accepted);
        RunUntil(m, () => m.Pack.State == PackMlState.Stopped, 180_000, "arrêt automatique");

        Assert.NotNull(m.Order);
        Assert.Equal(OrderState.Running, m.Order!.State);
        Assert.True(m.Order.Good + m.Order.Bad > 0);
        Assert.Equal(0, m.Order.InProcess);
        int done = m.Order.Good + m.Order.Bad;

        ToExecute(m);
        m.Run(90_000);
        Assert.True(m.Order!.Good + m.Order.Bad > done, "la production de l'OF reprend");
    }

    [Fact]
    public void A_mover_to_empty_with_parts_holds_the_end_until_the_operator_confirms()
    {
        var m = Producing(3);
        // Un mover « à vider » porte des pièces que l'opérateur doit retirer. Le simulateur n'y arrive normalement que si aucune station de
        // rebut n'est disponible : on met donc dans cet état un mover vide du tampon, auquel on donne une pièce (aucune position n'est perturbée).
        RunUntil(m, () => m.Movers.Any(mv => mv.State == MvState.Buffered && !mv.Moving && mv.Slots.All(p => !p.Present)), 180_000, "un mover vide est au tampon");
        var carrying = m.Movers.First(mv => mv.State == MvState.Buffered && !mv.Moving && mv.Slots.All(p => !p.Present));
        carrying.Slots[0].Present = true;
        carrying.Slots[0].Id = "PIECE-A-RETIRER";
        carrying.State = MvState.ToEmpty;
        carrying.WaitReason = "Mover à vider";

        Assert.True(m.StartDrain().Accepted);
        m.Run(120_000);

        Assert.Equal(PackMlState.Execute, m.Pack.State);
        Assert.True(m.Drain);
        Assert.Equal(carrying.Number, m.DrainBlockingMover);

        m.ConfirmPartsRemoved(carrying.Number);
        m.Run(120_000);
        Assert.True(m.Pack.State == PackMlState.Stopped, Describe(m));
        Assert.Equal(MachineSim.DrainedStopCause, m.StopCause);
    }

    [Fact]
    public void Client_publishes_the_drain_state_and_the_blinking_blue_beacon_when_empty()
    {
        var c = new SimulatedPlcClient(Sample(), Catalog(), 1);
        Assert.True(c.SendCommandAsync(StateCommand.Reset, default).Result.Accepted);
        c.Advance(8_000);
        Assert.True(c.SendCommandAsync(StateCommand.Start, default).Result.Accepted);
        c.Advance(60_000);
        Assert.False(c.Snapshot.Machine.Draining);

        Assert.True(c.StartDrainAsync(default).Result.Accepted);
        c.Advance(100);
        var during = c.Snapshot.Machine;
        Assert.True(during.Draining);
        Assert.True(during.PartsInProcess > 0, "des pièces restent à finir");

        c.Advance(180_000);
        var after = c.Snapshot.Machine;
        Assert.Equal(PackMlState.Stopped, after.State);
        Assert.False(after.Draining);
        Assert.Equal(0, after.PartsInProcess);
        Assert.Equal(MachineSim.DrainedStopCause, after.StopCause);
        Assert.True(after.Beacon.Blinking.HasFlag(BeaconLights.Blue), "balise bleue clignotante");

        // Au Reset suivant, la balise cesse de clignoter en bleu.
        Assert.True(c.SendCommandAsync(StateCommand.Reset, default).Result.Accepted);
        c.Advance(8_000);
        Assert.False(c.Snapshot.Machine.Beacon.Blinking.HasFlag(BeaconLights.Blue));
    }

    [Fact]
    public void Client_refuses_a_drain_when_the_machine_is_stopped()
    {
        var c = new SimulatedPlcClient(Sample(), Catalog(), 1);
        var r = c.StartDrainAsync(default).Result;
        Assert.False(r.Accepted);
        Assert.False(c.CancelDrainAsync(default).Result.Accepted);
    }
}

public class DrainScenarioTests
{
    [Fact]
    public void Scenario_draining_shows_a_machine_still_finishing_its_parts()
    {
        var cat = Catalog();
        var c = new SimulatedPlcClient(SampleProject.Build(cat), cat, 1);
        SimScenarios.Apply(c, "draining");
        var m = c.Snapshot.Machine;
        Assert.Equal(PackMlState.Execute, m.State);
        Assert.True(m.Draining, "le mode est actif");
        Assert.True(m.PartsInProcess > 0, "il reste des pièces à finir pour la capture");
        // Les stations d'entrée ne chargent plus : aucune nouvelle pièce n'est créée tant que le mode dure.
        int created = c.Sim.PartsCreated;
        c.Advance(1_000);
        Assert.True(c.Sim.PartsCreated <= created + 1);
    }

    [Fact]
    public void All_scenario_names_are_known_to_apply()
    {
        var cat = Catalog();
        foreach (var name in SimScenarios.Names)
        {
            var c = new SimulatedPlcClient(SampleProject.Build(cat), cat, 1); // le simulateur garde le projet valide, même pour validation-errors
            SimScenarios.Apply(c, name); // ne lève pas
        }
    }
}
