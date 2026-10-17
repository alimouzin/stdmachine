using System.Diagnostics;
using StdMachine.Core;
using StdMachine.Core.Catalog;
using StdMachine.Core.Compiler;
using StdMachine.Simulation.Demo;
using Xunit;
using Xunit.Abstractions;

namespace StdMachine.Core.Tests;

public class CompilerTests
{
    private readonly ITestOutputHelper _out;
    private static readonly CatalogDocument Cat = CatalogLoader.Load(Path.Combine(AppContext.BaseDirectory, "catalog", "catalog.json"));

    public CompilerTests(ITestOutputHelper output) { _out = output; }

    private static CompiledTables Compile(MachineProject? p = null)
    {
        var r = ProjectCompiler.Compile(p ?? SampleProject.Build(Cat), Cat);
        Assert.True(r.Succeeded, string.Join("\n", r.Issues.Select(i => i.Code + " " + i.Message)));
        return r.Project!;
    }

    [Fact]
    public void Full_sample_compiles_without_error_in_under_200_ms()
    {
        var p = SampleProject.Build(Cat);
        ProjectCompiler.Compile(p, Cat); // chauffe le JIT
        var sw = Stopwatch.StartNew();
        var r = ProjectCompiler.Compile(p, Cat);
        sw.Stop();
        Assert.True(r.Succeeded);
        Assert.True(sw.ElapsedMilliseconds < 200, "Compilation : " + sw.ElapsedMilliseconds + " ms");
        Assert.Equal(6, r.Project!.Stations.Count);
    }

    [Fact]
    public void Process_variables_follow_the_station_variables_and_each_has_its_own_slot()
    {
        var p = SampleProject.Build(Cat);
        var station = p.Station(DemoProject.Pressage01)!;
        int stationVars = station.Variables.Count;
        var a = new StationVariable { Name = "PosPignon", Type = "Position", Initial = "0" };
        var b = new StationVariable { Name = "Essais", Type = "Number", Initial = "5" };
        station.Processes.First(x => x.Name == "Presser pignon").Variables.Add(a);
        station.Processes.First(x => x.Name == "Sertir").Variables.Add(b);
        station.Processes.First(x => x.Name == "Sertir").Body.Steps.Add(new SetStep { Target = SetTarget.Var, TargetRef = b.Id, Value = ValueRef.Const(7) });

        var data = Compile(p).Station(DemoProject.Pressage01)!;

        Assert.Equal(stationVars + 2, data.Variables.Count);
        Assert.Equal("Position", data.Variables[stationVars].Type);
        Assert.Equal(new CVarInit("Number", 5), data.Variables[stationVars + 1]);
        // « Définir variable » vise l'emplacement de la variable de process (numérotation à partir de 1).
        Assert.Contains(data.Instructions, i => i.Op == Op.SetVar && i.Binds[0].Target == BindKind.Var && i.Binds[0].Index == stationVars + 2);
    }

    [Fact]
    public void Process_variables_count_in_the_64_variable_limit()
    {
        var p = SampleProject.Build(Cat);
        var proc = p.Station(DemoProject.Pressage01)!.Processes.First(x => x.Name == "Presser pignon");
        for (int i = 0; i < 70; i++) proc.Variables.Add(new StationVariable { Name = "v" + i });
        Assert.Contains("V007", ProjectCompiler.Compile(p, Cat).Issues.Select(i => i.Code));
    }

    [Fact]
    public void Compilation_is_deterministic_and_the_hash_follows_the_content()
    {
        var a = Compile();
        var b = Compile();
        Assert.Equal(a.Hash, b.Hash);
        Assert.Equal(64, a.Hash.Length);
        var p = SampleProject.Build(Cat);
        p.Station(DemoProject.Controle)!.Positions.Add(new NamedPosition { Name = "Nouvelle", Taught = true });
        Assert.NotEqual(a.Hash, Compile(p).Hash);
    }

    [Fact]
    public void Validation_errors_block_the_compilation()
    {
        var r = ProjectCompiler.Compile(SampleProject.BuildValidationErrors(Cat), Cat);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Issues, i => i.Code == "V004");
    }

    [Fact]
    public void Pressing_operation_listing_has_the_structure_of_doc_06()
    {
        var c = Compile();
        string listing = Listing.Of(c, DemoProject.Pressage01, "Presser pignon");
        _out.WriteLine(listing);
        var st = c.Station(DemoProject.Pressage01)!;
        var dir = st.Tables.First(t => t.Name == "Presser pignon");
        var ins = st.Instructions.Skip(dir.Start - 1).Take(dir.Length).ToList();
        // Chaque carte est encadrée, les tables d'opération finissent par End(0)
        Assert.Equal(Op.End, ins.Last().Op);
        Assert.Equal(0, ins.Last().Args[0]);
        Assert.Equal(ins.Count(i => i.Op == Op.StepBegin), ins.Count(i => i.Op == Op.StepEnd));
        // « Chercher pièce » : action 3 de la vision, refaire 3 fois
        var search = ins.First(i => i.Op == Op.Action && i.Action == 3);
        Assert.Equal(FailKind.Retry, search.OnFail);
        Assert.Equal(3, search.Retries);
        // Bloc En parallèle : Fork à 3 branches + Join qui pointe vers le Fork
        var fork = ins.Single(i => i.Op == Op.Fork && st.Instructions[i.Args[1] - 1].Op == Op.StepBegin && st.Instructions[i.Args[1] - 1].Args[0] == 1);
        Assert.Equal(4, fork.Args.Count(a => a != 0));
        int forkIndex = st.Instructions.IndexOf(fork) + 1;
        var join = st.Instructions[fork.Args[0] - 1];
        Assert.Equal(Op.Join, join.Op);
        Assert.Equal(forkIndex, join.Args[0]);
        // Si / Sinon : JumpIfFalse, Jump, trois termes dans la condition
        Assert.Contains(ins, i => i.Op == Op.JumpIfFalse);
        Assert.Contains(st.Conditions, cnd => cnd.Terms.Count == 3);
        // Les positions nommées et la recette de presse (paramètre d'opération 1) sont des valeurs résolues
        Assert.Contains(ins, i => i.Op == Op.Action && i.Params.Any(v => v.Source == ValueSource.OpParam && v.Index == 1));
    }

    [Fact]
    public void Pick_part_unfolds_into_inner_steps_with_substituted_parameters()
    {
        var c = Compile();
        var st = c.Station(DemoProject.Pressage01)!;
        var dir = st.Tables.First(t => t.Name == "Presser pignon");
        var ins = st.Instructions.Skip(dir.Start - 1).Take(dir.Length).ToList();
        Assert.Contains(ins, i => i.Op == Op.StepBegin && i.Args[0] == 2);
        // Prendre pièce sur la position « Zone alimenteur »... PosPignon (variable) avec décalage d'approche +10 sur Z
        var zActions = ins.Where(i => i.Op == Op.Action && i.Action == 4 && i.Params.Length == 3 && i.Params[0].Source == ValueSource.Var).ToList();
        Assert.Contains(zActions, i => i.Params[0].Real == 10);
        // Z de sécurité = paramètre P1 du robot (−5)
        Assert.Contains(ins, i => i.Op == Op.Action && i.Action == 4 && i.Params.Length > 0 && i.Params[0].Source == ValueSource.Const && i.Params[0].Real == -5);
        // Aspiration : action 1 de l'outil avec « attendre le vide »
        Assert.Contains(ins, i => i.Op == Op.Action && i.Action == 1 && i.Params.Length == 1 && i.Params[0].Real == 1);
    }

    [Fact]
    public void Approach_offset_applies_only_to_the_approach_move_not_to_the_final_position()
    {
        var st = Compile().Station(DemoProject.Pressage01)!;
        var dir = st.Tables.First(t => t.Name == "Presser pignon");
        var z = st.Instructions.Skip(dir.Start - 1).Take(dir.Length)
            .Where(i => i.Op == Op.Action && i.Action == 4 && i.Object == 9 && i.Params[0].Source == ValueSource.NamedPos).ToList();
        Assert.Contains(z, i => i.Params[0].Real == 12);
        Assert.Contains(z, i => i.Params[0].Real == 0);
        Assert.DoesNotContain(z, i => i.Params[0].Real == 22 || i.Params[0].Real == 24);
    }

    [Fact]
    public void Objects_are_numbered_breadth_first_parents_before_children()
    {
        var st = Compile().Station(DemoProject.Pressage01)!;
        var names = st.Objects.Select(o => o.Name).ToList();
        Assert.Equal("Robot XYZC", names[0]);
        Assert.Equal(new[] { "Axe X", "Axe Y", "Axe Z", "Axe C", "Aspiration" }, names.Skip(6).Take(5).ToArray());
        foreach (var o in st.Objects.Where(o => o.ParentId != 0)) Assert.True(o.ParentId < o.LocalId);
    }

    [Fact]
    public void Operation_map_links_each_function_operation_to_its_table_and_two_functions_share_a_station()
    {
        var c = Compile();
        var st = c.Station(DemoProject.Pressage01)!;
        Assert.Equal(2, st.OpMap.Count);
        Assert.All(st.OpMap, m => Assert.Equal(TableKind.Operation, st.Tables[m.Table - 1].Kind));
        Assert.Equal(new[] { "Presser pignon", "Sertir" }, st.OpMap.Select(m => st.Tables[m.Table - 1].Name).OrderBy(x => x == "Sertir" ? 1 : 0).ToArray());
    }

    [Fact]
    public void General_process_is_made_of_routes_and_ends_with_End()
    {
        var m = Compile().Machine;
        Assert.Single(m.Tables);
        Assert.Equal(6, m.Instructions.Count(i => i.Op == Op.Route));
        Assert.Equal(Op.End, m.Instructions.Last().Op);
        Assert.Equal(3, m.Instructions.Count(i => i.Op == Op.JumpIfFalse));
        var pressing = m.Instructions.First(i => i.Op == Op.Route && i.Params.Length == 1);
        Assert.Equal(3, pressing.Params[0].Real);
    }

    [Fact]
    public void Compilation_map_goes_both_ways()
    {
        var p = SampleProject.Build(Cat);
        var c = Compile(p);
        var step = p.Station(DemoProject.Pressage01)!.Processes.First(x => x.Name == "Presser pignon").Body.Steps.OfType<FunctionStep>().First();
        var instrs = c.InstructionsOf(step.Id).ToList();
        Assert.True(instrs.Count > 10);
        Assert.All(instrs, e => Assert.Equal(StepIds.Of(step.Id), e.StepId));
        Assert.Contains("Presser pignon", c.OriginOf(DemoProject.Pressage01, instrs[0].Instruction)!.Path);
    }

    [Fact]
    public void Generated_interlocks_follow_user_interlocks_and_use_conditions()
    {
        var st = Compile().Station(DemoProject.Pressage01)!;
        Assert.Equal(3, st.Interlocks.Count);
        Assert.All(st.Interlocks, il => { Assert.Equal(0u, il.ActionMask); Assert.InRange(il.Cond, 1, st.Conditions.Count); });
    }

    [Fact]
    public void Loops_use_hidden_variables_from_the_top_and_while_has_a_guard_alarm()
    {
        var p = SampleProject.Build(Cat);
        var st = p.Station(DemoProject.Controle)!;
        var proc = st.Processes.First(x => x.Name == "Contrôler");
        var rep = new RepeatBlock { Mode = RepeatMode.Count, Count = 3 };
        rep.Body.Steps.Add(new WaitTimeStep { DurationSeconds = 0.5 });
        var wh = new RepeatBlock { Mode = RepeatMode.While, Max = 7 };
        wh.Condition.Terms.Add(new CondTerm { Left = ValueRef.Const(1), Op = CompareOp.Equal, Right = ValueRef.Const(1) });
        wh.Body.Steps.Add(new WaitTimeStep { DurationSeconds = 1 });
        proc.Body.Steps.Add(rep);
        proc.Body.Steps.Add(wh);
        var data = Compile(p).Station(DemoProject.Controle)!;
        var ins = data.Instructions;
        var inits = ins.Where(i => i.Op == Op.LoopInit).ToList();
        Assert.Equal(new[] { 64, 63 }, inits.Select(i => i.Args[0]).ToArray());
        Assert.Contains(ins, i => i.Op == Op.Alarm && i.Args[1] == 2 && i.Args[2] == 5);
        Assert.Contains("Nombre maximal de répétitions atteint", data.Texts);
        var next = ins.First(i => i.Op == Op.LoopNext);
        Assert.Equal(Op.StepBegin, ins[next.Args[1] - 1].Op);
    }

    [Fact]
    public void Too_many_instructions_is_a_compile_error_with_value_and_limit()
    {
        var p = SampleProject.Build(Cat);
        var proc = p.Station(DemoProject.Controle)!.Processes.First(x => x.Name == "Contrôler");
        for (int i = 0; i < 200; i++) proc.Body.Steps.Add(new WaitTimeStep { DurationSeconds = 1 });
        var r = ProjectCompiler.Compile(p, Cat);
        Assert.False(r.Succeeded);
        Assert.Contains(r.Issues, i => i.Code == "V007" && i.Message.Contains("512"));
    }
}
