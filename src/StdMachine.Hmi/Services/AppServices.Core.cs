using System.Windows.Threading;
using StdMachine.Core;
using StdMachine.Simulation.Demo;

namespace StdMachine.Hmi.Services;

/// <summary>Conteneur de services construit à la main dans <c>App.xaml.cs</c> (pas de Microsoft.Extensions.Hosting dans le PoC).</summary>
public static partial class AppServices
{
    public static ProjectService Project { get; private set; } = null!;
    public static CompilationService Compilation { get; private set; } = null!;
    /// <summary>Passerelle usine (mode simulateur avec <c>--plant-log</c> : le canal Journal de messages écrit dans %ProgramData%\StdMachine\plant-log).</summary>
    public static StdMachine.Plant.PlantGateway? Plant { get; private set; }
    public static IPlcClient Plc { get; private set; } = null!;
    public static SnapshotStore Snapshots { get; private set; } = null!;
    public static SessionService Session { get; private set; } = null!;
    public static SelectionService Selection { get; private set; } = null!;
    public static PlcCommands Commands { get; private set; } = null!;
    public static NavigationService Navigation { get; private set; } = null!;

    /// <summary>Services de la maquette : projet de démonstration, <see cref="DemoPlcClient"/>, instantané, session, navigation.</summary>
    public static void Initialize(Dispatcher dispatcher, NavigationService navigation, bool simulator = false, string? scenario = null, bool plantLog = false)
    {
        Compilation = new CompilationService(CompilationService.TryLoadCatalog());
        IPlcClient client;
        if (simulator)
        {
            // Simulateur fidèle (phases 4 et 5) : le projet d'exemple MX-4 compilé et exécuté par SimulatedPlcClient.
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            var catalog = Compilation.Catalog ?? StdMachine.Core.Catalog.CatalogLoader.Load(System.IO.Path.Combine(dir, "catalog", "catalog.json"));
            string samplePath = System.IO.Path.Combine(dir, "samples", "MX-4.stdm.json");
            Func<MachineProject> load = () => System.IO.File.Exists(samplePath)
                ? StdMachine.Persistence.ProjectSerializer.Load(samplePath)
                : StdMachine.Simulation.Demo.SampleProject.Build(catalog);
            var shown = scenario == "validation-errors" ? StdMachine.Simulation.SimScenarios.ProjectFor(scenario, catalog) : load();
            Project = new ProjectService(shown, true);
            var sim = new StdMachine.Simulation.SimulatedPlcClient(scenario == "validation-errors" ? load() : shown, catalog);
            if (scenario != null) StdMachine.Simulation.SimScenarios.Apply(sim, scenario);
            client = sim;
        }
        else
        {
            Project = new ProjectService();
            client = new DemoPlcClient(Project.Project);
        }
        Plc = client;
        if (simulator && plantLog) StartPlantGateway(client);
        Snapshots = new SnapshotStore(client, dispatcher);
        Session = new SessionService();
        Selection = new SelectionService();
        Commands = new PlcCommands(client);
        Navigation = navigation;
    }

    public static DemoPlcClient? Demo => Plc as DemoPlcClient;

    private static void StartPlantGateway(IPlcClient client)
    {
        string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StdMachine", "config");
        var store = new StdMachine.Plant.CommunicationSettingsStore(System.IO.Path.Combine(dir, "communications.json"), StdMachine.Plant.SecretProtectors.Default(dir));
        var settings = store.Load();
        settings.For("log").Enabled = true;
        var channels = new StdMachine.Plant.IPlantChannel[] { new StdMachine.Plant.LogFileChannel(), new StdMachine.Plant.MqttChannel(), new StdMachine.Plant.OpcUaChannel() };
        var gateway = new StdMachine.Plant.PlantGateway(channels, settings, new StdMachine.Plant.FileSeqStore(System.IO.Path.Combine(dir, "plant-seq.txt")));
        gateway.ApplySettingsAsync(settings, CancellationToken.None).Wait();
        gateway.Attach(client);
        Plant = gateway;
        // Distribution des messages aux canaux, hors du fil de l'interface et du fil du simulateur.
        System.Threading.Tasks.Task.Run(async () =>
        {
            while (true)
            {
                try { await gateway.FlushAsync().ConfigureAwait(false); } catch (Exception) { }
                await System.Threading.Tasks.Task.Delay(500).ConfigureAwait(false);
            }
        });
    }
}
