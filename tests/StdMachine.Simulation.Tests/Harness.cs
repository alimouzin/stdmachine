using StdMachine.Core;
using StdMachine.Core.Catalog;
using StdMachine.Core.Compiler;
using StdMachine.Simulation.Demo;
using StdMachine.Simulation.Engine;

namespace StdMachine.Simulation.Tests;

internal static class Harness
{
    public const double Cycle = 10;

    public static CatalogDocument Catalog() => CatalogLoader.Load(Path.Combine(AppContext.BaseDirectory, "catalog", "catalog.json"));

    public static MachineProject Sample() => SampleProject.Build(Catalog());

    public static CompiledTables Compile(MachineProject p)
    {
        var r = ProjectCompiler.Compile(p, Catalog());
        if (!r.Succeeded) throw new InvalidOperationException(string.Join("\n", r.Issues.Select(i => i.Code + " " + i.Message)));
        return r.Project!;
    }

    public sealed class Rig
    {
        public double Now;
        public readonly AlarmLog Alarms = new AlarmLog();
        public readonly SimRandom Rng = new SimRandom(7);
        public readonly CompiledTables Compiled;
        public readonly StationSim Station;
        public double MachineOverride = 100;

        public Rig(MachineProject project, int station)
        {
            Compiled = Compile(project);
            Station = new StationSim(Compiled.Station(station)!, Compiled, Catalog(), Alarms, Rng, () => Now, () => MachineOverride);
        }

        public void Tick(double ms = Cycle)
        {
            Station.Cycle(ms);
            Now += ms;
        }

        public bool RunUntil(Func<bool> done, double maxMs = 120_000)
        {
            double limit = Now + maxMs;
            while (Now < limit)
            {
                if (done()) return true;
                Tick();
            }
            return done();
        }

        /// <summary>Reset puis Start : la station est en Execute.</summary>
        public void ToExecute()
        {
            Station.Command(StateCommand.Reset);
            Assert(RunUntil(() => Station.State == PackMlState.Idle, 60_000), "Resetting → Idle");
            Station.Command(StateCommand.Start);
            Assert(RunUntil(() => Station.State == PackMlState.Execute, 1000), "Idle → Execute");
        }

        public static void Assert(bool ok, string what) { if (!ok) throw new Xunit.Sdk.XunitException("Échec : " + what); }

        public Part NewPart()
        {
            var p = new Part { Present = true, Id = "P1", Status = PartStatus.InProcess };
            return p;
        }

        public OpRequest Request(int group, int op, Part part, params double[] ps)
        {
            var r = new OpRequest { ReqId = ++_req, Group = group, Operation = op, Mover = 1, Slot = 1, Parts = new[] { part } };
            for (int i = 0; i < ps.Length; i++) r.Params[i] = ps[i];
            return r;
        }

        private uint _req;
    }
}
