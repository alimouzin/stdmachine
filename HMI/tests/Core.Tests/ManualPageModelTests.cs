using Core.Config;
using Core.Model;

namespace Core.Tests;

public class ManualPageModelTests
{
    private static MachineDef Sample() =>
        MachineLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "machine.json"));

    [Fact]
    public void Work_station_page_lists_every_device_with_its_catalog_actions()
    {
        var page = ManualPageModel.Build(Sample().Stations.First(s => s.Id == "work1"));

        Assert.Equal(6, page.Devices.Count);
        var cylinder = page.Devices.First(d => d.Id == "cyl1");
        Assert.Equal("Vérin", cylinder.Kind);
        Assert.Equal(new[] { "moveWork", "moveHome" }, cylinder.Actions.Select(a => a.Id));

        var axis = page.Devices.First(d => d.Id == "axX");
        Assert.Equal(new[] { "powerOnInit", "move" }, axis.Actions.Select(a => a.Id));

        var vacuum = page.Devices.First(d => d.Id == "vac");
        Assert.Equal(3, vacuum.Actions.Count);
    }

    [Fact]
    public void Modules_expose_the_module_actions()
    {
        var page = ManualPageModel.Build(Sample().Stations.First(s => s.Id == "work1"));
        var robot = Assert.Single(page.Modules);
        Assert.True(robot.IsModule);
        Assert.Equal(new[] { "goto", "pick", "place" }, robot.Actions.Select(a => a.Id));
    }

    [Fact]
    public void Page_follows_the_declaration_without_station_specific_code()
    {
        var entry = ManualPageModel.Build(Sample().Stations.First(s => s.Id == "entry1"));
        Assert.Equal(4, entry.Devices.Count);
        Assert.DoesNotContain(entry.Devices, d => d.Kind == "Vérin");

        var custom = new StationDef
        {
            Id = "x", Name = "X",
            Devices = { new DeviceDef { Id = "c", Name = "C", Kind = DeviceKind.Cylinder } },
        };
        var page = ManualPageModel.Build(custom);
        Assert.Single(page.Devices);
        Assert.Empty(page.Modules);
    }

    [Fact]
    public async Task Every_generated_action_is_executable_by_the_simulator()
    {
        var machine = Sample();
        var plc = new Core.Plc.PlcSimulator(machine, new Core.Plc.PlcSimulatorOptions
        {
            ActionDelay = TimeSpan.Zero, TransitionDelay = TimeSpan.Zero,
        });
        plc.SetMode(Core.StateMachine.SeqMode.Maintenance);

        foreach (var station in machine.Stations)
        {
            var page = ManualPageModel.Build(station);
            foreach (var target in page.Devices.Concat(page.Modules))
                foreach (var action in target.Actions)
                    Assert.Equal(Core.Plc.ManualActionResult.Done,
                        await plc.ExecuteManualActionAsync(station.Id, target.Id, action.Id));
        }
    }
}
