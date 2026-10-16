using StdMachine.Core;
using StdMachine.Core.Catalog;
using StdMachine.Simulation.Demo;
using StdMachine.Simulation.Engine;

namespace StdMachine.Simulation;

/// <summary>
/// Scénarios de capture du simulateur (docs/07 § 10) : <c>idle</c>, <c>running</c>, <c>station-aborted</c>, <c>step-mode</c>, <c>manual</c>,
/// <c>validation-errors</c>, <c>quality-hold</c>, <c>forced</c>. Chacun amène le simulateur dans l'état décrit, de façon déterministe.
/// </summary>
public static class SimScenarios
{
    public static readonly string[] Names = { "idle", "running", "station-aborted", "step-mode", "manual", "validation-errors", "quality-hold", "forced" };

    /// <summary>Le projet que l'éditeur affiche pour ce scénario (celui qui a des erreurs pour <c>validation-errors</c>) ; le simulateur garde le projet valide déployé.</summary>
    public static MachineProject ProjectFor(string name, CatalogDocument catalog)
        => name == "validation-errors" ? SampleProject.BuildValidationErrors(catalog) : SampleProject.Build(catalog);

    public static void Apply(SimulatedPlcClient c, string name)
    {
        switch (name)
        {
            case "idle": break;
            case "running": Produce(c, 30_000); break;
            case "validation-errors": Produce(c, 5_000); break;
            case "station-aborted":
                Produce(c, 45_000);
                WaitForOperation(c, DemoProject.Pressage01);
                c.ForceStationAbort(DemoProject.Pressage01);
                c.Advance(20_000);
                break;
            case "step-mode":
                {
                    SetMaintenance(c);
                    Prepare(c, DemoProject.Pressage02);
                    var proc = c.Project.Station(DemoProject.Pressage02)!.Processes.First(p => p.Name == "Presser pignon");
                    c.SetStepModeAsync(DemoProject.Pressage02, StepGranularity.Step, default).Wait();
                    c.LaunchTableAsync(new LaunchRequest(DemoProject.Pressage02, proc.Id, 1), default).Wait();
                    // Quelques pas : la station s'arrête dans le bloc En parallèle (docs/07 § 10).
                    for (int i = 0; i < 40; i++)
                    {
                        c.Advance(1_500);
                        var st = c.Snapshot.Station(DemoProject.Pressage02)!;
                        if (st.Steps.Any(r => r.State == StepRunState.StepPending) && IsInParallel(c, DemoProject.Pressage02)) break;
                        c.StepAsync(DemoProject.Pressage02, default).Wait();
                    }
                    break;
                }
            case "manual":
                // Docs/05 § 7 : le canal manuel demande le mode Maintenance et une station Stopped (aucune préparation).
                SetMaintenance(c);
                break;
            case "quality-hold":
                Produce(c, 60_000);
                for (int i = 0; i < 5; i++) c.Sim.Quality.Record(QualityScope.StationImputed, c.Sim.StationIndex(DemoProject.Pressage02), true);
                c.Advance(10_000);
                break;
            case "forced":
                Produce(c, 30_000);
                c.SetDeclaredStateAsync(DeclaredState.CorrectiveMaintenance, "Panne presse", null, default).Wait();
                c.SetStationOutOfProductionAsync(DemoProject.Pressage01, true, "Réglage", default).Wait();
                c.SetMoverOutOfServiceAsync(3, true, "Nid abîmé", default).Wait();
                c.Advance(20_000);
                break;
            default: throw new ArgumentException("Scénario inconnu : " + name);
        }
    }

    private static void Produce(SimulatedPlcClient c, double ms)
    {
        c.SendCommandAsync(StateCommand.Reset, default).Wait();
        c.Advance(8_000);
        c.SendCommandAsync(StateCommand.Start, default).Wait();
        c.Advance(ms);
    }

    private static void WaitForOperation(SimulatedPlcClient c, int station)
    {
        for (int i = 0; i < 400 && !(c.Sim.StationByNumber(station)?.OperationRunning ?? false); i++) c.Advance(250);
        c.Advance(1_500);
    }

    private static void SetMaintenance(SimulatedPlcClient c)
    {
        c.SetModeAsync(MachineMode.Maintenance, default).Wait();
        c.Advance(100);
    }

    private static void Prepare(SimulatedPlcClient c, int station)
    {
        c.SendStationCommandAsync(station, StateCommand.Reset, default).Wait();
        c.Advance(8_000);
    }

    private static bool IsInParallel(SimulatedPlcClient c, int station)
        => c.Sim.StationByNumber(station)!.Contexts.Count(x => x.Status != CtxStatus.Free && x.Status != CtxStatus.Done) >= 2;
}
