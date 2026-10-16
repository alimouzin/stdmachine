using StdMachine.Core.Catalog;
using Xunit;

namespace StdMachine.Core.Tests;

public class CatalogTests
{
    private static readonly string[] ValueTypes = { "Bool", "Int", "Real", "Percent", "Duration", "Enum", "Position", "Speed", "Text", "Recipe" };
    private static readonly string[] SignalKinds = { "DI", "DO", "AI", "AO" };

    private static CatalogDocument Load() => CatalogLoader.Load(Path.Combine(AppContext.BaseDirectory, "catalog", "catalog.json"));

    [Fact]
    public void Contains_all_types_and_modules_of_doc_03()
    {
        var c = Load();
        Assert.Equal(28, c.Types.Count);
        Assert.Equal(new[] { 100, 101 }, c.Modules.Select(m => m.Id).ToArray());
    }

    [Fact]
    public void Type_ids_and_codes_are_unique_and_in_range()
    {
        var c = Load();
        var ids = c.Types.Select(t => t.Id).Concat(c.Modules.Select(m => m.Id)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(c.Types, t => Assert.InRange(t.Id, 1, 99));
        Assert.All(c.Modules, m => Assert.InRange(m.Id, 100, 149));
        var codes = c.Types.Select(t => t.Code).Concat(c.Modules.Select(m => m.Code)).ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Numbers_are_unique_within_each_type()
    {
        foreach (var t in Load().Types)
        {
            Assert.True(t.Params.Select(p => p.No).Distinct().Count() == t.Params.Count, t.Code + " : paramètres");
            Assert.True(t.Actions.Select(a => a.No).Distinct().Count() == t.Actions.Count, t.Code + " : actions");
            Assert.True(t.Signals.Select(s => s.No).Distinct().Count() == t.Signals.Count, t.Code + " : signaux");
            Assert.True(t.Errors.Select(e => e.Code).Distinct().Count() == t.Errors.Count, t.Code + " : erreurs");
            Assert.All(t.Actions, a => Assert.InRange(a.No, 1, 31));
            Assert.All(t.Params, p => Assert.InRange(p.No, 1, 24));
            Assert.All(t.Signals, s => Assert.InRange(s.No, 1, 8));
            Assert.All(t.Errors, e => Assert.True(e.Code >= 100, t.Code + " : code d'erreur propre < 100"));
        }
    }

    [Fact]
    public void Value_types_and_signal_kinds_are_known()
    {
        foreach (var t in Load().Types)
        {
            Assert.All(t.Params, p => Assert.Contains(p.Type, ValueTypes));
            Assert.All(t.Signals, s => Assert.Contains(s.Kind, SignalKinds));
            Assert.All(t.Actions.SelectMany(a => a.Params), p => Assert.Contains(p.Type, ValueTypes));
            Assert.All(t.Actions.SelectMany(a => a.Results), r => Assert.Contains(r.Type, ValueTypes));
            foreach (var p in t.Params.Where(p => p.Type == "Enum")) Assert.NotEmpty(p.Values!);
        }
    }

    [Fact]
    public void Common_errors_are_1_to_11_and_motion_types_have_a_motion_action()
    {
        var c = Load();
        Assert.Equal(Enumerable.Range(1, 11), c.CommonErrors.Select(e => e.Code));
        foreach (var t in c.Types.Where(t => t.HasMotion)) Assert.Contains(t.Actions, a => a.Motion);
    }

    [Fact]
    public void Action_param_defaults_referring_to_type_params_exist()
    {
        foreach (var t in Load().Types)
            foreach (var d in t.Actions.SelectMany(a => a.Params).Where(p => p.Default?.ValueKind == System.Text.Json.JsonValueKind.String))
            {
                var s = d.Default!.Value.GetString()!;
                Assert.Matches("^P[0-9]+$", s);
                Assert.Contains(t.Params, p => p.No == int.Parse(s.Substring(1)));
            }
    }

    [Fact]
    public void Module_functions_reference_only_existing_roles_and_actions()
    {
        var c = Load();
        var robot = c.Module(101)!;
        var roles = robot.Roles.Select(r => r.Role).ToHashSet();
        Assert.Equal(new[] { "MoveTo", "MoveAbove", "PickPart", "PlacePart", "Retract", "Reference" }, robot.Functions.Select(f => f.Id).ToArray());
        Assert.All(robot.Roles.SelectMany(r => r.Types), id => Assert.NotNull(c.Type(id)));
        foreach (var f in robot.Functions) Walk(f.Body, c, robot, roles, f.Id);
    }

    private static void Walk(System.Text.Json.JsonElement e, CatalogDocument c, CatalogModule m, HashSet<string> roles, string fn)
    {
        if (e.ValueKind == System.Text.Json.JsonValueKind.Array) { foreach (var x in e.EnumerateArray()) Walk(x, c, m, roles, fn); return; }
        if (e.ValueKind != System.Text.Json.JsonValueKind.Object) return;
        if (e.TryGetProperty("role", out var role) && role.ValueKind == System.Text.Json.JsonValueKind.String)
            Assert.True(roles.Contains(role.GetString()!), fn + " : rôle inconnu " + role.GetString());
        if (e.TryGetProperty("step", out var step) && step.GetString() == "action")
        {
            var r = m.Roles.First(x => x.Role == e.GetProperty("role").GetString());
            var no = e.GetProperty("action").GetInt32();
            Assert.Contains(r.Types, id => c.Type(id)!.Action(no) != null || id == 20 || id == 21);
        }
        foreach (var p in e.EnumerateObject()) Walk(p.Value, c, m, roles, fn);
    }
}
