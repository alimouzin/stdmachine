using StdMachine.Core;
using StdMachine.Simulation.Demo;
using StdMachine.Simulation.Engine;
using Xunit;
using Xunit.Abstractions;
using static StdMachine.Simulation.Tests.Harness;

namespace StdMachine.Simulation.Tests;

public class StationTests
{
    private readonly ITestOutputHelper _out;

    public StationTests(ITestOutputHelper output) { _out = output; }

    private const int PressingGroup = 2; // Pressage pignon (rang dans la liste des fonctions, de 1)
    private const int ControlGroup = 3;
    private const int CrimpGroup = 4;

    private static int VerdictIndex(Rig r) => r.Compiled.Machine.VerdictIndex;

    [Fact]
    public void Station_resets_starts_and_runs_the_pressing_operation_to_a_verdict()
    {
        var rig = new Rig(Sample(), DemoProject.Pressage01);
        rig.ToExecute();
        var part = rig.NewPart();
        Assert.True(rig.Station.Offer(rig.Request(PressingGroup, 1, part, 3)));
        Assert.True(rig.RunUntil(() => rig.Station.Response.Status == OpStatus.Done || rig.Station.Response.Status == OpStatus.Failed || rig.Station.State == PackMlState.Aborted, 90_000),
            "L'opération ne se termine pas : " + rig.Station.State + " " + rig.Station.FaultText);
        foreach (var a in rig.Alarms.All) _out.WriteLine("ALARME " + a.Code + " " + a.Object + " " + a.Text + " " + a.Severity);
        foreach (var o in rig.Station.Objects) if (o.State == ObjState.Error) _out.WriteLine("OBJET EN ERREUR " + o.Name + " " + o.ErrorCode);
        _out.WriteLine("Fin à " + rig.Now + " ms, réponse " + rig.Station.Response.Status + " verdict " + part.Data[VerdictIndex(rig)]);
        Assert.NotEqual(PackMlState.Aborted, rig.Station.State);
        Assert.Contains(part.Data[VerdictIndex(rig)], new[] { 1.0, 2.0 });
        Assert.Equal(rig.Station.Response.Status == OpStatus.Done, part.Data[VerdictIndex(rig)] == 1.0);
        Assert.True(rig.Now > 8_000, "L'opération complète prend du temps (robot, presse)");
    }

    [Fact]
    public void Operation_is_refused_when_the_station_is_not_in_execute_or_busy()
    {
        var rig = new Rig(Sample(), DemoProject.Pressage01);
        Assert.False(rig.Station.Offer(rig.Request(PressingGroup, 1, rig.NewPart(), 3)));
        rig.ToExecute();
        Assert.True(rig.Station.Offer(rig.Request(PressingGroup, 1, rig.NewPart(), 3)));
        Assert.False(rig.Station.Offer(rig.Request(PressingGroup, 1, rig.NewPart(), 3)));
        Assert.False(rig.Station.Offer(rig.Request(9, 1, rig.NewPart())));
    }

    [Fact]
    public void Two_functions_share_the_station_each_with_its_own_table()
    {
        var rig = new Rig(Sample(), DemoProject.Pressage01);
        rig.ToExecute();
        var part = rig.NewPart();
        Assert.True(rig.Station.Offer(rig.Request(CrimpGroup, 1, part, 5)));
        Assert.True(rig.RunUntil(() => rig.Station.Response.Status != OpStatus.Running, 60_000));
        Assert.Equal("Sertir", rig.Station.Data.Tables[rig.Station.Data.OpMap.First(m => m.Group == CrimpGroup).Table - 1].Name);
        Assert.Contains(rig.Station.Response.Status, new[] { OpStatus.Done, OpStatus.Failed });
    }

    [Fact]
    public void Objects_are_initialised_by_the_init_table_and_start_ready()
    {
        var rig = new Rig(Sample(), DemoProject.Pressage01);
        var axisZ = rig.Station.Objects.First(o => o.Name == "Axe Z");
        Assert.Equal(ObjState.NotInitialized, axisZ.State);
        Assert.Equal(ObjState.Ready, rig.Station.Objects.First(o => o.Name == "Aspiration").State);
    }

    [Fact]
    public void Hold_lets_the_current_operation_finish_then_start_resumes()
    {
        var rig = new Rig(Sample(), DemoProject.Pressage01);
        rig.ToExecute();
        rig.Station.Offer(rig.Request(CrimpGroup, 1, rig.NewPart(), 5));
        rig.RunUntil(() => rig.Station.OperationRunning, 100);
        rig.Tick(); rig.Tick();
        Assert.True(rig.Station.Command(StateCommand.Hold));
        rig.Tick();
        Assert.Equal(PackMlState.Held, rig.Station.State);
        Assert.True(rig.RunUntil(() => rig.Station.Response.Status != OpStatus.Running, 60_000), "l'opération se termine malgré Hold");
        Assert.False(rig.Station.Offer(rig.Request(CrimpGroup, 1, rig.NewPart(), 5)), "aucune nouvelle demande en Held");
        rig.Station.AcknowledgeResponse();
        Assert.True(rig.Station.Command(StateCommand.Start));
        rig.Tick();
        Assert.Equal(PackMlState.Execute, rig.Station.State);
        Assert.True(rig.Station.Offer(rig.Request(CrimpGroup, 1, rig.NewPart(), 5)));
    }

    [Fact]
    public void Abort_during_an_operation_answers_aborted_kills_contexts_and_clear_returns_to_stopped()
    {
        var rig = new Rig(Sample(), DemoProject.Pressage01);
        rig.ToExecute();
        rig.Station.Offer(rig.Request(PressingGroup, 1, rig.NewPart(), 3));
        rig.RunUntil(() => rig.Now > 3000, 5000);
        Assert.True(rig.Station.Command(StateCommand.Abort));
        rig.Tick();
        Assert.Equal(PackMlState.Aborted, rig.Station.State);
        Assert.Equal(OpStatus.Aborted, rig.Station.Response.Status);
        Assert.All(rig.Station.Contexts, c => Assert.True(c.Status == CtxStatus.Free));
        Assert.True(rig.Station.Command(StateCommand.Clear));
        Assert.True(rig.RunUntil(() => rig.Station.State == PackMlState.Stopped, 30_000));
    }

    [Fact]
    public void Stop_finishes_the_operation_then_reaches_stopped()
    {
        var rig = new Rig(Sample(), DemoProject.Pressage01);
        rig.ToExecute();
        rig.Station.Offer(rig.Request(CrimpGroup, 1, rig.NewPart(), 5));
        rig.RunUntil(() => rig.Now > 1000, 2000);
        Assert.True(rig.Station.Command(StateCommand.Stop));
        Assert.True(rig.RunUntil(() => rig.Station.State == PackMlState.Stopped, 60_000));
        Assert.NotEqual(OpStatus.Aborted, rig.Station.Response.Status);
    }

    [Fact]
    public void Object_error_with_stop_station_policy_aborts_the_station_and_raises_an_alarm()
    {
        var rig = new Rig(Sample(), DemoProject.Pressage01);
        rig.ToExecute();
        rig.Station.Offer(rig.Request(PressingGroup, 1, rig.NewPart(), 3));
        rig.RunUntil(() => rig.Now > 2000, 3000);
        rig.Station.Objects.First(o => o.TypeCode == "Press").InjectError(100);
        rig.Station.Objects.First(o => o.TypeCode == "AsyrilEyePlus").InjectError(103);
        // Une erreur d'objet inactif n'abat pas la station tant qu'aucune action ne la lit : on force le cas par une action
        Assert.True(rig.RunUntil(() => rig.Station.State == PackMlState.Aborted, 90_000), "la station passe Aborted quand l'action en cours échoue ou que l'objet est lu en erreur");
        Assert.Contains(rig.Alarms.All, a => a.Station == DemoProject.Pressage01);
    }

    [Fact]
    public void Retry_policy_acknowledges_and_runs_the_instruction_again()
    {
        var rig = new Rig(Sample(), DemoProject.Pressage01);
        rig.ToExecute();
        var vision = rig.Station.Objects.First(o => o.TypeCode == "AsyrilEyePlus");
        vision.FailureRate = 0.6; // la recherche échoue souvent mais pas toujours : 3 essais suffisent la plupart du temps
        rig.Station.Offer(rig.Request(PressingGroup, 1, rig.NewPart(), 3));
        Assert.True(rig.RunUntil(() => rig.Station.Response.Status != OpStatus.Running || rig.Station.State == PackMlState.Aborted, 120_000));
        _out.WriteLine("État final " + rig.Station.State + ", alarmes " + rig.Alarms.All.Count);
        Assert.True(rig.Alarms.All.Any(a => a.Severity == AlarmSeverity.Information) || rig.Station.State != PackMlState.Aborted);
    }

    [Fact]
    public void Parallel_branches_take_the_longest_branch_not_the_sum()
    {
        double Run(params double[] seconds)
        {
            var p = Sample();
            var st = p.Station(DemoProject.Controle)!;
            var proc = st.Processes.First(x => x.Name == "Contrôler");
            proc.Body.Steps.Clear();
            var par = new ParallelBlock();
            par.Branches.Clear();
            foreach (double s in seconds) par.Branches.Add(new Sequence(new Step[] { new WaitTimeStep { DurationSeconds = s } }));
            proc.Body.Steps.Add(par);
            var rig = new Rig(p, DemoProject.Controle);
            rig.ToExecute();
            double t0 = rig.Now;
            rig.Station.Offer(rig.Request(3, 1, rig.NewPart()));
            Assert.True(rig.RunUntil(() => rig.Station.Response.Status == OpStatus.Done || rig.Station.Response.Status == OpStatus.Failed, 30_000));
            return rig.Now - t0;
        }
        double two = Run(1, 2), three = Run(1, 3, 2), four = Run(1, 1, 4, 2);
        Assert.InRange(two, 2000, 2400);
        Assert.InRange(three, 3000, 3400);
        Assert.InRange(four, 4000, 4400);
    }

    [Fact]
    public void If_else_and_repeat_follow_their_conditions()
    {
        var p = Sample();
        var st = p.Station(DemoProject.Controle)!;
        var proc = st.Processes.First(x => x.Name == "Contrôler");
        var v = new StationVariable { Id = Guid.NewGuid(), Name = "n", Type = "Number" };
        st.Variables.Add(v);
        proc.Body.Steps.Clear();
        var rep = new RepeatBlock { Mode = RepeatMode.Count, Count = 4 };
        rep.Body.Steps.Add(new SetStep { Target = SetTarget.Var, TargetRef = v.Id, Operation = SetOperation.Add, Value = ValueRef.Const(2) });
        proc.Body.Steps.Add(rep);
        var iff = new IfBlock();
        iff.Condition.Terms.Add(new CondTerm { Left = ValueRef.Var(v.Id), Op = CompareOp.Equal, Right = ValueRef.Const(8) });
        iff.Then.Steps.Add(new SetStep { Target = SetTarget.Verdict, Operation = SetOperation.Assign, Value = ValueRef.Const(1) });
        iff.Else.Steps.Add(new SetStep { Target = SetTarget.Verdict, Operation = SetOperation.Assign, Value = ValueRef.Const(2) });
        proc.Body.Steps.Add(iff);
        var rig = new Rig(p, DemoProject.Controle);
        rig.ToExecute();
        var part = rig.NewPart();
        rig.Station.Offer(rig.Request(3, 1, part));
        Assert.True(rig.RunUntil(() => rig.Station.Response.Status == OpStatus.Done || rig.Station.Response.Status == OpStatus.Failed, 10_000));
        Assert.Equal(8, rig.Station.Vars[1], 6);
        Assert.Equal(1.0, part.Data[VerdictIndex(rig)]);
    }

    [Fact]
    public void Step_mode_pauses_before_each_card_and_a_step_advances_every_branch()
    {
        var p = Sample();
        var st = p.Station(DemoProject.Controle)!;
        var proc = st.Processes.First(x => x.Name == "Contrôler");
        proc.Body.Steps.Clear();
        var par = new ParallelBlock();
        par.Branches.Clear();
        par.Branches.Add(new Sequence(new Step[] { new WaitTimeStep { DurationSeconds = 0.1 }, new WaitTimeStep { DurationSeconds = 0.1 } }));
        par.Branches.Add(new Sequence(new Step[] { new WaitTimeStep { DurationSeconds = 0.1 }, new WaitTimeStep { DurationSeconds = 0.1 } }));
        proc.Body.Steps.Add(par);
        var rig = new Rig(p, DemoProject.Controle);
        rig.ToExecute();
        rig.Station.Mode = MachineMode.Maintenance;
        rig.Station.SetStepMode(StepGranularity.Step);
        rig.Station.Offer(rig.Request(3, 1, rig.NewPart()));
        rig.Tick(); rig.Tick(); rig.Tick();
        Assert.Contains(rig.Station.Contexts, c => c.Status == CtxStatus.WaitStep);
        rig.Station.Step();
        for (int i = 0; i < 15; i++) rig.Tick();
        int waiting = rig.Station.Contexts.Count(c => c.Status == CtxStatus.WaitStep);
        Assert.True(waiting >= 2, "les deux branches attendent le prochain pas : " + waiting);
        rig.Station.SetStepMode(null);
        Assert.True(rig.RunUntil(() => rig.Station.Response.Status == OpStatus.Done, 10_000));
    }
}
