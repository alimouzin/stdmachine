using Core.Config;
using Core.Model;
using Core.Plc;
using Core.StateMachine;

namespace Core.Tests;

public class PlcSimulatorTests
{
    private static readonly PlcSimulatorOptions Fast = new()
    {
        TransitionDelay = TimeSpan.FromMilliseconds(20),
        ActionDelay = TimeSpan.FromMilliseconds(30),
    };

    private static PlcSimulator Create() =>
        new(MachineLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "machine.json")), Fast);

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Condition jamais atteinte.");
            await Task.Delay(5);
        }
    }

    private static async Task<PlcSimulator> InExecute()
    {
        var plc = Create();
        plc.SendCommand(SeqCommand.Reset);
        await WaitFor(() => plc.GetStatus().State == SeqState.Idle);
        plc.SendCommand(SeqCommand.Start);
        Assert.Equal(SeqState.Execute, plc.GetStatus().State);
        return plc;
    }

    [Fact]
    public void Status_lists_every_declared_station_Stopped()
    {
        var s = Create().GetStatus();
        Assert.Equal(SeqState.Stopped, s.State);
        Assert.Equal(new[] { "entry1", "work1", "work2", "exit1" }, s.Stations.Select(x => x.Id));
        Assert.All(s.Stations, x => Assert.Equal(SeqState.Stopped, x.State));
    }

    [Fact]
    public async Task Reset_goes_through_Resetting_then_Idle_by_itself()
    {
        var plc = Create();
        Assert.True(plc.SendCommand(SeqCommand.Reset));
        Assert.Equal(SeqState.Resetting, plc.GetStatus().State);

        await WaitFor(() => plc.GetStatus().State == SeqState.Idle);
        Assert.All(plc.GetStatus().Stations, x => Assert.Equal(SeqState.Idle, x.State));
    }

    [Fact]
    public async Task Stop_ends_Stopped()
    {
        var plc = await InExecute();
        Assert.True(plc.SendCommand(SeqCommand.Stop));
        Assert.Equal(SeqState.Stopping, plc.GetStatus().State);
        await WaitFor(() => plc.GetStatus().State == SeqState.Stopped);
    }

    [Fact]
    public void Refused_command_returns_false()
    {
        var plc = Create();
        Assert.False(plc.SendCommand(SeqCommand.Start));
    }

    [Fact]
    public async Task ForceAbort_on_one_work_station_degrades_without_stopping_the_machine()
    {
        var plc = await InExecute();
        Assert.True(plc.ForceAbort("work1"));

        var s = plc.GetStatus();
        Assert.Equal(SeqState.Execute, s.State);
        Assert.True(s.IsDegraded);
        Assert.Equal(SeqState.Aborted, s.Stations.First(x => x.Id == "work1").State);
        Assert.False(s.Stations.First(x => x.Id == "work1").Available);
    }

    [Fact]
    public async Task ForceAbort_on_both_work_stations_aborts_the_machine_and_Clear_recovers()
    {
        var plc = await InExecute();
        plc.ForceAbort("work1");
        plc.ForceAbort("work2");
        Assert.Equal(SeqState.Aborted, plc.GetStatus().State);

        Assert.True(plc.SendCommand(SeqCommand.Clear));
        await WaitFor(() => plc.GetStatus().State == SeqState.Stopped);
    }

    [Fact]
    public async Task Aborted_station_rejoins_after_Clear_and_Reset()
    {
        var plc = await InExecute();
        plc.ForceAbort("work1");
        plc.SendStationCommand("work1", SeqCommand.Clear);
        await WaitFor(() => plc.GetStatus().Stations.First(x => x.Id == "work1").State == SeqState.Stopped);
        plc.SendStationCommand("work1", SeqCommand.Reset);
        await WaitFor(() => plc.GetStatus().Stations.First(x => x.Id == "work1").State == SeqState.Execute);
        Assert.False(plc.GetStatus().IsDegraded);
    }

    [Fact]
    public async Task Abort_during_Resetting_cancels_the_pending_completion()
    {
        var plc = Create();
        plc.SendCommand(SeqCommand.Reset);
        plc.SendCommand(SeqCommand.Abort);
        await Task.Delay(100);
        Assert.Equal(SeqState.Aborted, plc.GetStatus().State);
    }

    [Fact]
    public async Task StatusChanged_is_raised()
    {
        var plc = Create();
        var count = 0;
        plc.StatusChanged += () => Interlocked.Increment(ref count);
        plc.SendCommand(SeqCommand.Reset);
        await WaitFor(() => plc.GetStatus().State == SeqState.Idle);
        Assert.True(count >= 2);
    }

    [Fact]
    public async Task Manual_action_is_refused_outside_Maintenance_and_Stopped()
    {
        var plc = Create();
        Assert.Equal(ManualActionResult.NotAllowed, await plc.ExecuteManualActionAsync("work1", "cyl1", "moveWork"));

        plc.SetMode(SeqMode.Maintenance);
        Assert.Equal(ManualActionResult.Done, await plc.ExecuteManualActionAsync("work1", "cyl1", "moveWork"));
    }

    [Fact]
    public async Task Manual_action_takes_the_simulated_delay_per_base_action()
    {
        var plc = Create();
        plc.SetMode(SeqMode.Maintenance);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await plc.ExecuteManualActionAsync("work1", "robot", "pick"); // 4 actions de base
        watch.Stop();

        Assert.Equal(ManualActionResult.Done, result);
        Assert.True(watch.ElapsedMilliseconds >= 100, $"durée {watch.ElapsedMilliseconds} ms");
    }

    [Theory]
    [InlineData("nope", "cyl1", "moveWork")]
    [InlineData("work1", "nope", "moveWork")]
    [InlineData("work1", "cyl1", "suck")]
    [InlineData("work1", "robot", "moveWork")]
    public async Task Manual_action_on_unknown_target_or_action_is_reported(string station, string target, string action)
    {
        var plc = Create();
        plc.SetMode(SeqMode.Maintenance);
        Assert.Equal(ManualActionResult.Unknown, await plc.ExecuteManualActionAsync(station, target, action));
    }

    [Fact]
    public async Task Mode_cannot_change_outside_Stopped()
    {
        var plc = await InExecute();
        Assert.False(plc.SetMode(SeqMode.Maintenance));
    }

    [Fact]
    public void Module_steps_follow_the_standard_pick_of_the_spec()
    {
        Assert.Equal(new[] { "Move XY", "Move Z", "Aspirer", "Move Z final" },
            ActionCatalog.ModuleActionSteps(ActionCatalog.RobotXyz, "pick"));
    }
}
