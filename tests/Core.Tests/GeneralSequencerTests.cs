using Core.Config;
using Core.StateMachine;
using static Core.StateMachine.SeqCommand;
using static Core.StateMachine.SeqState;

namespace Core.Tests;

public class GeneralSequencerTests
{
    private static GeneralSequencer Create() =>
        new(MachineLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "machine.json")));

    private static void CompleteAll(GeneralSequencer g)
    {
        foreach (var id in g.StationIds.ToList()) g.CompleteStation(id);
    }

    private static GeneralSequencer InExecute()
    {
        var g = Create();
        g.Apply(Reset);
        CompleteAll(g);
        g.Apply(Start);
        Assert.Equal(Execute, g.State);
        return g;
    }

    [Fact]
    public void Starts_Stopped_with_all_stations_Stopped()
    {
        var g = Create();
        Assert.Equal(Stopped, g.State);
        Assert.All(g.StationIds, id => Assert.Equal(Stopped, g.StationState(id)));
    }

    [Fact]
    public void Reset_is_relayed_and_general_is_Idle_only_when_all_stations_are()
    {
        var g = Create();
        Assert.True(g.Apply(Reset));
        Assert.Equal(Resetting, g.State);
        Assert.All(g.StationIds, id => Assert.Equal(Resetting, g.StationState(id)));

        g.CompleteStation("entry1");
        g.CompleteStation("work1");
        g.CompleteStation("work2");
        Assert.Equal(Resetting, g.State);

        g.CompleteStation("exit1");
        Assert.Equal(Idle, g.State);
    }

    [Fact]
    public void Nominal_cycle_Start_Hold_Start_Stop()
    {
        var g = InExecute();
        Assert.All(g.StationIds, id => Assert.Equal(Execute, g.StationState(id)));

        Assert.True(g.Apply(Hold));
        Assert.Equal(Held, g.State);
        Assert.All(g.StationIds, id => Assert.Equal(Held, g.StationState(id)));

        Assert.True(g.Apply(Start));
        Assert.Equal(Execute, g.State);

        Assert.True(g.Apply(Stop));
        Assert.Equal(Stopping, g.State);
        CompleteAll(g);
        Assert.Equal(Stopped, g.State);
    }

    [Fact]
    public void Invalid_command_is_refused_without_effect()
    {
        var g = Create();
        Assert.False(g.CanApply(Start));
        Assert.False(g.Apply(Start));
        Assert.Equal(Stopped, g.State);
    }

    [Fact]
    public void Abort_of_one_work_station_is_local_and_production_continues()
    {
        var g = InExecute();
        Assert.True(g.ApplyToStation("work1", Abort));

        Assert.Equal(Execute, g.State);
        Assert.Equal(Aborted, g.StationState("work1"));
        Assert.Equal(Execute, g.StationState("work2"));
        Assert.False(g.IsStationAvailable("work1"));
        Assert.True(g.IsDegraded);
    }

    [Fact]
    public void Abort_of_the_last_station_of_a_group_aborts_the_machine()
    {
        var g = InExecute();
        g.ApplyToStation("work1", Abort);
        g.ApplyToStation("work2", Abort);

        Assert.Equal(Aborted, g.State);
        Assert.All(g.StationIds, id => Assert.Equal(Aborted, g.StationState(id)));
        Assert.False(g.IsDegraded);
    }

    [Theory]
    [InlineData("entry1")]
    [InlineData("exit1")]
    public void Abort_of_a_station_without_equivalent_aborts_the_machine(string stationId)
    {
        var g = InExecute();
        g.ApplyToStation(stationId, Abort);
        Assert.Equal(Aborted, g.State);
    }

    [Fact]
    public void Machine_Abort_aborts_everything()
    {
        var g = InExecute();
        Assert.True(g.Apply(Abort));
        Assert.Equal(Aborted, g.State);
        Assert.All(g.StationIds, id => Assert.Equal(Aborted, g.StationState(id)));
    }

    [Fact]
    public void Clear_after_machine_Abort_leads_to_Stopped()
    {
        var g = InExecute();
        g.Apply(Abort);

        Assert.True(g.Apply(Clear));
        Assert.Equal(Clearing, g.State);
        CompleteAll(g);
        Assert.Equal(Stopped, g.State);
    }

    [Fact]
    public void Aborted_station_rejoins_the_group_after_Clear_and_Reset()
    {
        var g = InExecute();
        g.ApplyToStation("work1", Abort);

        Assert.True(g.ApplyToStation("work1", Clear));
        Assert.Equal(Clearing, g.StationState("work1"));
        Assert.False(g.IsStationAvailable("work1"));

        g.CompleteStation("work1");
        Assert.Equal(Stopped, g.StationState("work1"));
        Assert.True(g.IsStationAvailable("work1"));

        Assert.True(g.ApplyToStation("work1", Reset));
        g.CompleteStation("work1");

        Assert.Equal(Execute, g.StationState("work1"));
        Assert.Equal(Execute, g.State);
        Assert.False(g.IsDegraded);
    }

    [Fact]
    public void Stop_with_an_aborted_station_ends_Stopped_and_general_Clear_acknowledges_it()
    {
        var g = InExecute();
        g.ApplyToStation("work1", Abort);
        g.Apply(Stop);
        CompleteAll(g);
        Assert.Equal(Stopped, g.State);
        Assert.Equal(Aborted, g.StationState("work1"));

        Assert.True(g.CanApply(Clear));
        g.Apply(Clear);
        CompleteAll(g);
        Assert.Equal(Stopped, g.StationState("work1"));
        Assert.Equal(Stopped, g.State);
    }

    [Fact]
    public void Station_cannot_be_aborted_while_Stopped()
    {
        var g = Create();
        Assert.False(g.ApplyToStation("work1", Abort));
        Assert.Equal(Stopped, g.State);
    }

    [Fact]
    public void Mode_is_global_and_changes_only_when_everything_is_Stopped()
    {
        var g = Create();
        Assert.True(g.TrySetMode(SeqMode.Maintenance));
        Assert.Equal(SeqMode.Maintenance, g.Mode);
        Assert.False(g.CanApply(Reset));
        Assert.False(g.CanApply(Start));

        g.TrySetMode(SeqMode.Production);
        g.Apply(Reset);
        Assert.False(g.TrySetMode(SeqMode.Maintenance));
        Assert.Equal(SeqMode.Production, g.Mode);
    }

    [Fact]
    public void Changed_is_raised_when_state_moves()
    {
        var g = Create();
        var count = 0;
        g.Changed += () => count++;
        g.Apply(Reset);
        Assert.True(count > 0);
    }
}
