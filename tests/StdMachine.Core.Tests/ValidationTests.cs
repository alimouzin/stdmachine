using StdMachine.Core;
using StdMachine.Core.Catalog;
using StdMachine.Core.Validation;
using StdMachine.Simulation.Demo;
using Xunit;

namespace StdMachine.Core.Tests;

public class ValidationTests
{
    private static readonly CatalogDocument Cat = CatalogLoader.Load(Path.Combine(AppContext.BaseDirectory, "catalog", "catalog.json"));

    private static MachineProject Sample() => SampleProject.Build(Cat);

    private static List<string> Codes(MachineProject p) => ProjectValidator.Validate(p, Cat).Select(i => i.Code).Distinct().OrderBy(c => c).ToList();

    private static StationConfig Press1(MachineProject p) => p.Station(DemoProject.Pressage01)!;

    [Fact]
    public void Sample_has_no_error_and_no_warning()
    {
        var issues = ProjectValidator.Validate(Sample(), Cat);
        Assert.True(issues.Count == 0, string.Join("\n", issues.Select(i => i.Code + " " + i.Message)));
    }

    [Fact]
    public void Validation_errors_scenario_reports_V001_and_V004()
    {
        var codes = Codes(SampleProject.BuildValidationErrors(Cat));
        Assert.Contains("V001", codes);
        Assert.Contains("V004", codes);
        Assert.DoesNotContain("V002", codes);
    }

    // Une mutation du projet d'exemple qui doit déclencher exactement la règle donnée.
    private static readonly Dictionary<string, Action<MachineProject>> Mutation = BuildMutations();

    public static IEnumerable<object[]> Codes_() => Mutation.Keys.Select(k => new object[] { k });

    private static Dictionary<string, Action<MachineProject>> BuildMutations()
    {
        var d = new Dictionary<string, Action<MachineProject>>();
        d["V001"] = p => Press1(p).Positions.First(x => x.Name == "Dépose nid").Taught = false;
        d["V002"] = p => Press1(p).AllObjects().First(o => o.TypeCode == "Press").Signals.Clear();
        d["V003"] = p =>
        {
            var a = Press1(p).AllObjects().First(o => o.TypeCode == "VacuumBlow");
            var b = p.Station(DemoProject.Chargement)!.AllObjects().First(o => o.TypeCode == "VacuumBlow");
            b.Signals.RemoveAll(x => x.No == 1); b.Signals.Add(a.Signals.First(x => x.No == 1));
        };
        d["V004"] = p => ((ParallelBlock)Press1(p).Processes.First(x => x.Name == "Presser pignon").AllSteps().First(s => s is ParallelBlock))
            .Branches[2].Steps.Add(new ActionStep { ObjectId = Press1(p).AllObjects().First(o => o.TypeCode == "AsyrilEyePlus").Id, ActionNo = 3, Fail = new FailPolicy(FailKind.Retry, 3) });
        d["V005"] = p => p.Station(DemoProject.Rebut)!.AllObjects().First(o => o.TypeCode == "Cylinder").Params.First(x => x.No == 2).Number = 9999;
        d["V006"] = p => Press1(p).Processes.RemoveAll(x => x.Name == "Sertir");
        d["V007"] = p => { for (int i = 0; i < 70; i++) Press1(p).Variables.Add(new StationVariable { Name = "v" + i }); };
        d["V008"] = p => ((IfBlock)Press1(p).Processes.First(x => x.Name == "Presser pignon").AllSteps().First(s => s is IfBlock)).Condition.Terms.Clear();
        d["V009"] = p => Press1(p).Processes.First(x => x.Name == "Presser pignon").Body.Steps.Add(new CallStep { ProcessId = Guid.NewGuid() });
        d["V010"] = p => Press1(p).AllObjects().First(o => o.TypeCode == "CartesianRobot").Children.RemoveAll(c => c.ChildRole == "AxisX");
        d["V011"] = p => Press1(p).AllObjects().First(o => o.TypeCode == "AsyrilEyePlus").Disabled = true;
        d["V012"] = p => p.GeneralProcess.AllSteps().OfType<RouteStep>().First(r => r.Args.Count > 0).Args.Clear();
        d["V013"] = p => p.Station(DemoProject.Rebut)!.Number = DemoProject.Controle;
        d["V015"] = p => p.Machine.Groups.First(g => g.Name == "Contrôle").StationIds.Clear();
        d["V016"] = p => Press1(p).Processes.First(x => x.Name == "Presser pignon").Body.Steps.OfType<ActionStep>().First().Fail = new FailPolicy(FailKind.Goto, 0, Guid.NewGuid());
        d["V017"] = p => { var sert = Press1(p).Processes.First(x => x.Name == "Sertir"); Press1(p).Processes.First(x => x.Name == "Presser pignon").Implements.AddRange(sert.Implements); };
        d["V018"] = p => { for (int i = 0; i < 9; i++) { var g = new GroupConfig { Name = "g" + i }; g.StationIds.Add(Press1(p).Id); g.Operations.Add(new OperationSignature { Name = "o" }); p.Machine.Groups.Add(g); } };
        d["V019"] = p => p.Machine.Quality.Mover.RateWarnPercent = 50;
        d["V020"] = p =>
        {
            var v = new StationVariable { Name = "PosPrivee", Type = "Position" };
            Press1(p).Processes.First(x => x.Name == "Sertir").Variables.Add(v);
            Press1(p).Processes.First(x => x.Name == "Presser pignon").Body.Steps.Add(new SetStep { Target = SetTarget.Var, TargetRef = v.Id, Value = ValueRef.Const(1) });
        };
        d["V101"] = p => Press1(p).AllObjects().First(o => o.TypeCode == "Press").Simulated = true;
        d["V102"] = p => Press1(p).PressRecipes[0].Verified = false;
        d["V103"] = p => { p.Machine.RejectGroupId = null; p.Machine.Groups.RemoveAll(g => g.Role == GroupRole.Reject); };
        d["V104"] = p => RemoveSteps(p.GeneralProcess.Body, st => st is RouteStep r && p.Group(r.GroupId)?.Role == GroupRole.Exit);
        d["V105"] = p => Press1(p).AllObjects().First(o => o.TypeCode == "VacuumBlow").Params.First(x => x.No == 2).Number = -58;
        d["V107"] = p => p.Machine.Planar.Movers = 30;
        d["V108"] = p => Press1(p).Positions.First(x => x.Name == "Dépose nid").Z = 3;
        d["V109"] = p => Press1(p).Objects.Add(new ObjectNode { TypeCode = "Timer", Name = "Inutile" });
        d["V110"] = p => Press1(p).Processes.SelectMany(x => x.AllSteps()).OfType<ActionStep>().First(a => a.ActionNo == 3 && a.Fail.Kind == FailKind.Retry).Fail = new FailPolicy();
        d["V111"] = p => p.Machine.Groups.First(g => g.Name == "Contrôle").StationIds.Clear();
        d["V112"] = p => p.Machine.PlanarPositions.RemoveAll(pp => pp.Kind == PlanarPositionKind.Buffer || (pp.StationId == DemoProject.StationId(DemoProject.Pressage01) && pp.Kind == PlanarPositionKind.Queue));
        d["V113"] = p => p.Machine.Groups.RemoveAll(g => g.Role == GroupRole.Work && g.Name != "Contrôle"); 
        d["V115"] = p =>
        {
            var proc = Press1(p).Processes.First(x => x.Name == "Presser pignon");
            var v = new StationVariable { Name = "PosPignon", Type = "Position" };
            proc.Variables.Add(v);
            var wait = new WaitConditionStep();
            wait.Condition.Terms.Add(new CondTerm { Left = ValueRef.Var(v.Id), Op = CompareOp.Equal, Right = ValueRef.Const(1) });
            proc.Body.Steps.Insert(0, wait);
        };
        return d;
    }

    private static void RemoveSteps(Sequence seq, Func<Step, bool> match)
    {
        seq.Steps.RemoveAll(s => match(s));
        foreach (var b in seq.Steps.OfType<Block>()) foreach (var branch in b.Sequences) RemoveSteps(branch, match);
    }

    [Theory]
    [MemberData(nameof(Codes_))]
    public void Rule_fires_on_a_targeted_mutation_and_not_on_the_sample(string code)
    {
        var mutate = Mutation[code];
        Assert.DoesNotContain(code, Codes(Sample()));
        var p = Sample();
        mutate(p);
        Assert.Contains(code, Codes(p));
    }

    [Fact]
    public void Issues_carry_a_link_to_the_element()
    {
        var p = Sample();
        Press1(p).Positions.First(x => x.Name == "Dépose nid").Taught = false;
        var issue = ProjectValidator.Validate(p, Cat).First(i => i.Code == "V001");
        Assert.Equal(DemoProject.Pressage01, issue.Link.StationNumber);
        Assert.Equal(Severity.Error, issue.Severity);
    }
}
