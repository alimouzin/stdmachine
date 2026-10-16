using StdMachine.Core;
using StdMachine.Simulation.Engine;
using Xunit;

namespace StdMachine.Simulation.Tests;

public class PackMlTests
{
    private static PackMlMachine In(PackMlState s) { var m = new PackMlMachine(); m.Reset(s); return m; }

    // (état de départ, commande, état d'arrivée ou null si refusée)
    public static IEnumerable<object?[]> Cases()
    {
        var S = PackMlState.Stopped; var R = PackMlState.Resetting; var I = PackMlState.Idle; var E = PackMlState.Execute; var H = PackMlState.Held;
        var P = PackMlState.Stopping; var A = PackMlState.Aborted; var C = PackMlState.Clearing;
        yield return new object?[] { S, StateCommand.Reset, R }; yield return new object?[] { S, StateCommand.Start, null }; yield return new object?[] { S, StateCommand.Abort, A };
        yield return new object?[] { S, StateCommand.Stop, null }; yield return new object?[] { S, StateCommand.Hold, null }; yield return new object?[] { S, StateCommand.Clear, null };
        yield return new object?[] { R, StateCommand.Abort, A }; yield return new object?[] { R, StateCommand.Stop, P }; yield return new object?[] { R, StateCommand.Start, null };
        yield return new object?[] { I, StateCommand.Start, E }; yield return new object?[] { I, StateCommand.Stop, P }; yield return new object?[] { I, StateCommand.Abort, A }; yield return new object?[] { I, StateCommand.Hold, null };
        yield return new object?[] { E, StateCommand.Hold, H }; yield return new object?[] { E, StateCommand.Stop, P }; yield return new object?[] { E, StateCommand.Abort, A }; yield return new object?[] { E, StateCommand.Start, null }; yield return new object?[] { E, StateCommand.Reset, null };
        yield return new object?[] { H, StateCommand.Start, E }; yield return new object?[] { H, StateCommand.Stop, P }; yield return new object?[] { H, StateCommand.Abort, A }; yield return new object?[] { H, StateCommand.Hold, null };
        yield return new object?[] { P, StateCommand.Abort, A }; yield return new object?[] { P, StateCommand.Start, null }; yield return new object?[] { P, StateCommand.Stop, null };
        yield return new object?[] { A, StateCommand.Clear, C }; yield return new object?[] { A, StateCommand.Abort, null }; yield return new object?[] { A, StateCommand.Reset, null }; yield return new object?[] { A, StateCommand.Start, null };
        yield return new object?[] { C, StateCommand.Abort, A }; yield return new object?[] { C, StateCommand.Reset, null }; yield return new object?[] { C, StateCommand.Clear, null };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Command_is_accepted_or_refused_as_in_the_state_table(PackMlState from, StateCommand cmd, PackMlState? to)
    {
        var m = In(from);
        m.Cycle(cmd, false, false);
        Assert.Equal(to ?? from, m.State);
        Assert.Equal(to != null, m.LastCommandAccepted);
        Assert.Equal(PackMlRules.IsAccepted(cmd, from), to != null);
    }

    [Fact]
    public void Completion_moves_resetting_to_idle_and_stopping_clearing_to_stopped()
    {
        foreach (var (from, to) in new[] { (PackMlState.Resetting, PackMlState.Idle), (PackMlState.Stopping, PackMlState.Stopped), (PackMlState.Clearing, PackMlState.Stopped) })
        {
            var m = In(from);
            m.Cycle(null, true, false);
            Assert.Equal(to, m.State);
            Assert.True(m.Entry);
        }
    }

    [Theory]
    [InlineData(PackMlState.Stopped)]
    [InlineData(PackMlState.Idle)]
    [InlineData(PackMlState.Execute)]
    [InlineData(PackMlState.Clearing)]
    public void A_fault_aborts_from_any_state_but_aborted(PackMlState from)
    {
        var m = In(from);
        m.Cycle(null, false, true);
        Assert.Equal(PackMlState.Aborted, m.State);
        m.Cycle(null, false, true);
        Assert.Equal(PackMlState.Aborted, m.State);
        Assert.False(m.Entry);
    }
}
