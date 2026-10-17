using StdMachine.Core;
using StdMachine.Core.Validation;
using Xunit;

namespace StdMachine.Core.Tests;

/// <summary>Portée (V020) et lecture avant écriture (V115) des variables de process (docs/13).</summary>
public class VariableUsageTests
{
    private static (StationConfig Station, ProcessDefinition Proc, StationVariable Var) Setup()
    {
        var station = new StationConfig { Name = "S" };
        var proc = new ProcessDefinition { Name = "P", Kind = ProcessKind.Operation };
        var v = new StationVariable { Name = "PosPignon", Type = "Position" };
        proc.Variables.Add(v);
        station.Processes.Add(proc);
        return (station, proc, v);
    }

    private static SetStep Write(StationVariable v) => new SetStep { Target = SetTarget.Var, TargetRef = v.Id, Value = ValueRef.Const(1) };

    private static SetStep Read(StationVariable v) => new SetStep { Target = SetTarget.PartData, Value = ValueRef.Var(v.Id) };

    private static ActionStep Bind(StationVariable v, FailPolicy? fail = null)
    {
        var a = new ActionStep { ObjectId = Guid.NewGuid(), ActionNo = 4, Fail = fail ?? new FailPolicy() };
        a.Bindings.Add(new ResultBinding(1, BindTarget.Var, v.Id));
        return a;
    }

    private static List<string> Codes(StationConfig s, ProcessDefinition p) => VariableUsage.Analyze(s, p).Select(f => f.Code).ToList();

    [Fact]
    public void Write_then_read_is_fine()
    {
        var (s, p, v) = Setup();
        p.Body.Steps.Add(Write(v));
        p.Body.Steps.Add(Read(v));
        Assert.Empty(Codes(s, p));
    }

    [Fact]
    public void Result_binding_counts_as_a_write()
    {
        var (s, p, v) = Setup();
        p.Body.Steps.Add(Bind(v));
        p.Body.Steps.Add(Read(v));
        Assert.Empty(Codes(s, p));
    }

    [Fact]
    public void Read_before_write_is_reported_with_the_variable_name_and_the_step()
    {
        var (s, p, v) = Setup();
        var read = Read(v);
        p.Body.Steps.Add(read);
        p.Body.Steps.Add(Write(v));
        var f = Assert.Single(VariableUsage.Analyze(s, p));
        Assert.Equal("V115", f.Code);
        Assert.False(f.IsError);
        Assert.Equal(read.Id, f.StepId);
        Assert.Contains("PosPignon", f.Message);
    }

    [Fact]
    public void Add_operation_reads_the_target_first()
    {
        var (s, p, v) = Setup();
        p.Body.Steps.Add(new SetStep { Target = SetTarget.Var, TargetRef = v.Id, Operation = SetOperation.Add, Value = ValueRef.Const(1) });
        Assert.Equal(new[] { "V115" }, Codes(s, p));
    }

    [Fact]
    public void Write_in_one_branch_of_an_if_is_not_guaranteed_but_in_both_it_is()
    {
        var (s, p, v) = Setup();
        var ifBlock = new IfBlock();
        ifBlock.Then.Steps.Add(Write(v));
        p.Body.Steps.Add(ifBlock);
        p.Body.Steps.Add(Read(v));
        Assert.Equal(new[] { "V115" }, Codes(s, p));

        ifBlock.Else.Steps.Add(Write(v));
        Assert.Empty(Codes(s, p));
    }

    [Fact]
    public void Parallel_branches_do_not_see_each_other_but_the_join_sees_all()
    {
        var (s, p, v) = Setup();
        var par = new ParallelBlock();
        par.Branches[0].Steps.Add(Write(v));
        par.Branches[1].Steps.Add(Read(v)); // lit pendant que l'autre branche écrit : rien n'est garanti
        p.Body.Steps.Add(par);
        p.Body.Steps.Add(Read(v));          // après la jointure : écrit
        var findings = VariableUsage.Analyze(s, p);
        Assert.Single(findings);
        Assert.Equal(par.Branches[1].Steps[0].Id, findings[0].StepId);
    }

    [Theory]
    [InlineData(FailKind.StopStation, false)]
    [InlineData(FailKind.Retry, false)]
    [InlineData(FailKind.Ignore, true)]
    [InlineData(FailKind.PartBad, true)]
    public void A_step_whose_failure_does_not_stop_the_process_does_not_guarantee_its_write(FailKind kind, bool reported)
    {
        var (s, p, v) = Setup();
        p.Body.Steps.Add(Bind(v, new FailPolicy(kind, 3)));
        p.Body.Steps.Add(Read(v));
        Assert.Equal(reported, Codes(s, p).Contains("V115"));
    }

    [Fact]
    public void Repeat_n_times_runs_its_body_at_least_once_but_while_may_not()
    {
        var (s, p, v) = Setup();
        var rep = new RepeatBlock { Mode = RepeatMode.Count, Count = 3 };
        rep.Body.Steps.Add(Write(v));
        p.Body.Steps.Add(rep);
        p.Body.Steps.Add(Read(v));
        Assert.Empty(Codes(s, p));

        rep.Mode = RepeatMode.While;
        rep.Max = 5;
        Assert.Equal(new[] { "V115" }, Codes(s, p));
    }

    [Fact]
    public void Condition_terms_count_as_reads()
    {
        var (s, p, v) = Setup();
        var wait = new WaitConditionStep();
        wait.Condition.Terms.Add(new CondTerm { Left = ValueRef.Var(v.Id), Op = CompareOp.Equal, Right = ValueRef.Const(1) });
        p.Body.Steps.Add(wait);
        Assert.Equal(new[] { "V115" }, Codes(s, p));
    }

    [Fact]
    public void A_process_variable_is_not_visible_from_another_process_V020()
    {
        var (s, p, v) = Setup();
        var other = new ProcessDefinition { Name = "Autre", Kind = ProcessKind.Background };
        other.Body.Steps.Add(Read(v));
        s.Processes.Add(other);

        var f = Assert.Single(VariableUsage.Analyze(s, other));
        Assert.Equal("V020", f.Code);
        Assert.True(f.IsError);
        Assert.Contains("PosPignon", f.Message);
        Assert.Empty(VariableUsage.Analyze(s, p).Where(x => x.Code == "V020"));
    }

    [Fact]
    public void Station_variables_are_never_analysed_because_other_processes_may_write_them()
    {
        var (s, p, _) = Setup();
        var shared = new StationVariable { Name = "Partagée", Type = "Number" };
        s.Variables.Add(shared);
        p.Body.Steps.Add(Read(shared));
        Assert.Empty(Codes(s, p));
    }
}
