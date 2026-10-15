using Core.StateMachine;
using static Core.StateMachine.SeqCommand;
using static Core.StateMachine.SeqState;

namespace Core.Tests;

public class SequencerTests
{
    // Transitions du schéma de Docs/02-specifications-logicielles.md. Tout ce qui n'y figure pas est refusé.
    private static readonly Dictionary<(SeqState, SeqCommand), SeqState> Expected = new()
    {
        [(Stopped, Reset)] = Resetting,
        [(Idle, Start)] = Execute,
        [(Execute, Hold)] = Held,
        [(Held, Start)] = Execute,
        [(Idle, Stop)] = Stopping,
        [(Execute, Stop)] = Stopping,
        [(Held, Stop)] = Stopping,
        [(Idle, Abort)] = Aborted,
        [(Execute, Abort)] = Aborted,
        [(Held, Abort)] = Aborted,
        [(Stopping, Abort)] = Aborted,
        [(Resetting, Abort)] = Aborted,
        [(Aborted, Clear)] = Clearing,
    };

    private static Sequencer InState(SeqState target)
    {
        var s = new Sequencer();
        switch (target)
        {
            case Stopped: break;
            case Resetting: s.Apply(Reset); break;
            case Idle: s.Apply(Reset); s.Complete(); break;
            case Execute: s.Apply(Reset); s.Complete(); s.Apply(Start); break;
            case Held: s.Apply(Reset); s.Complete(); s.Apply(Start); s.Apply(Hold); break;
            case Stopping: s.Apply(Reset); s.Complete(); s.Apply(Stop); break;
            case Aborted: s.Apply(Reset); s.Complete(); s.Apply(Abort); break;
            case Clearing: s.Apply(Reset); s.Complete(); s.Apply(Abort); s.Apply(Clear); break;
        }
        Assert.Equal(target, s.State);
        return s;
    }

    public static IEnumerable<object[]> AllStateCommandPairs() =>
        from st in Enum.GetValues<SeqState>()
        from cmd in Enum.GetValues<SeqCommand>()
        select new object[] { st, cmd };

    [Fact]
    public void Model_has_8_states_6_commands_2_modes()
    {
        Assert.Equal(8, Enum.GetValues<SeqState>().Length);
        Assert.Equal(6, Enum.GetValues<SeqCommand>().Length);
        Assert.Equal(2, Enum.GetValues<SeqMode>().Length);
    }

    [Fact]
    public void Starts_in_Stopped_and_Production()
    {
        var s = new Sequencer();
        Assert.Equal(Stopped, s.State);
        Assert.Equal(SeqMode.Production, s.Mode);
    }

    [Theory]
    [MemberData(nameof(AllStateCommandPairs))]
    public void Every_state_command_pair_follows_the_diagram(SeqState state, SeqCommand command)
    {
        var s = InState(state);
        var allowed = Expected.TryGetValue((state, command), out var next);

        Assert.Equal(allowed, s.CanApply(command));
        Assert.Equal(allowed, s.Apply(command));
        Assert.Equal(allowed ? next : state, s.State);
    }

    [Theory]
    [InlineData(Resetting, Idle)]
    [InlineData(Stopping, Stopped)]
    [InlineData(Clearing, Stopped)]
    public void Transient_states_complete(SeqState state, SeqState expected)
    {
        var s = InState(state);
        Assert.True(s.Complete());
        Assert.Equal(expected, s.State);
    }

    [Theory]
    [InlineData(Stopped)]
    [InlineData(Idle)]
    [InlineData(Execute)]
    [InlineData(Held)]
    [InlineData(Aborted)]
    public void Complete_does_nothing_outside_transient_states(SeqState state)
    {
        var s = InState(state);
        Assert.False(s.Complete());
        Assert.Equal(state, s.State);
    }

    [Fact]
    public void Mode_changes_only_when_Stopped()
    {
        var s = new Sequencer();
        Assert.True(s.TrySetMode(SeqMode.Maintenance));
        Assert.Equal(SeqMode.Maintenance, s.Mode);
        Assert.True(s.TrySetMode(SeqMode.Production));

        s.Apply(Reset);
        Assert.False(s.TrySetMode(SeqMode.Maintenance));
        Assert.Equal(SeqMode.Production, s.Mode);
    }

    [Fact]
    public void Maintenance_mode_refuses_Reset_and_Start()
    {
        var s = new Sequencer();
        s.TrySetMode(SeqMode.Maintenance);
        Assert.False(s.CanApply(Reset));
        Assert.False(s.Apply(Reset));
        Assert.Equal(Stopped, s.State);
    }

    [Fact]
    public void Changed_is_raised_on_transitions_only()
    {
        var s = new Sequencer();
        var count = 0;
        s.Changed += () => count++;
        s.Apply(Start);   // refusée
        Assert.Equal(0, count);
        s.Apply(Reset);
        s.Complete();
        Assert.Equal(2, count);
    }
}
