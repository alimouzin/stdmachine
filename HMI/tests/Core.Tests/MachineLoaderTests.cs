using Core.Config;
using Core.Model;

namespace Core.Tests;

public class MachineLoaderTests
{
    private static MachineDef LoadSample() =>
        MachineLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "machine.json"));

    [Fact]
    public void Sample_machine_has_entry_two_work_stations_and_exit()
    {
        var m = LoadSample();
        Assert.Equal(new[] { StationType.Entry, StationType.Work, StationType.Work, StationType.Exit },
            m.Stations.Select(s => s.Type));
    }

    [Fact]
    public void Work_stations_are_grouped_by_function()
    {
        var m = LoadSample();
        var group = Assert.Single(m.Groups);
        Assert.Equal(new[] { "work1", "work2" }, group.StationIds);
    }

    [Fact]
    public void Work_station_declares_2_cylinders_3_axes_and_a_vacuum_module()
    {
        var work = LoadSample().Stations.First(s => s.Id == "work1");
        Assert.Equal(2, work.Devices.Count(d => d.Kind == DeviceKind.Cylinder));
        Assert.Equal(3, work.Devices.Count(d => d.Kind == DeviceKind.LinearAxis));
        Assert.Equal(1, work.Devices.Count(d => d.Kind == DeviceKind.VacuumBlow));
        Assert.Contains(work.Modules, m => m.Type == ActionCatalog.RobotXyz);
    }

    [Fact]
    public void Catalog_gives_base_actions_per_device_kind()
    {
        Assert.Equal(new[] { "moveWork", "moveHome" }, ActionCatalog.ForDevice(DeviceKind.Cylinder).Select(a => a.Id));
        Assert.Equal(new[] { "powerOnInit", "move" }, ActionCatalog.ForDevice(DeviceKind.LinearAxis).Select(a => a.Id));
        Assert.Equal(new[] { "suck", "blow", "off" }, ActionCatalog.ForDevice(DeviceKind.VacuumBlow).Select(a => a.Id));
        Assert.Equal(new[] { "goto", "pick", "place" }, ActionCatalog.ForModule(ActionCatalog.RobotXyz).Select(a => a.Id));
    }

    [Fact]
    public void Duplicate_station_id_is_rejected()
    {
        var ex = Assert.Throws<MachineConfigException>(() => MachineLoader.Parse(
            """{"stations":[{"id":"a","type":"Work"},{"id":"a","type":"Work"}]}"""));
        Assert.Contains("en double", ex.Message);
    }

    [Fact]
    public void Group_with_unknown_station_is_rejected()
    {
        Assert.Throws<MachineConfigException>(() => MachineLoader.Parse(
            """{"stations":[{"id":"a","type":"Work"}],"groups":[{"id":"g","function":"f","stationIds":["b"]}]}"""));
    }

    [Fact]
    public void Station_in_two_groups_is_rejected()
    {
        Assert.Throws<MachineConfigException>(() => MachineLoader.Parse(
            """{"stations":[{"id":"a","type":"Work"}],"groups":[{"id":"g1","function":"f","stationIds":["a"]},{"id":"g2","function":"f","stationIds":["a"]}]}"""));
    }

    [Fact]
    public void Module_with_unknown_type_or_device_is_rejected()
    {
        Assert.Throws<MachineConfigException>(() => MachineLoader.Parse(
            """{"stations":[{"id":"a","type":"Work","modules":[{"id":"m","type":"Nope"}]}]}"""));
        Assert.Throws<MachineConfigException>(() => MachineLoader.Parse(
            """{"stations":[{"id":"a","type":"Work","modules":[{"id":"m","type":"RobotXyz","deviceIds":["x"]}]}]}"""));
    }

    [Fact]
    public void Machine_without_station_or_invalid_json_is_rejected()
    {
        Assert.Throws<MachineConfigException>(() => MachineLoader.Parse("""{"stations":[]}"""));
        Assert.Throws<MachineConfigException>(() => MachineLoader.Parse("{pas du json"));
    }

    [Fact]
    public void Position_always_carries_xyzc_and_a_frame()
    {
        var p = new Position(1, 2, 3, 90, "machine");
        Assert.Equal((1, 2, 3, 90, "machine"), (p.X, p.Y, p.Z, p.C, p.Frame));
    }
}
