using System.Windows.Threading;
using StdMachine.Core;
using StdMachine.Simulation.Demo;

namespace StdMachine.Hmi.Services;

/// <summary>Conteneur de services construit à la main dans <c>App.xaml.cs</c> (pas de Microsoft.Extensions.Hosting dans le PoC).</summary>
public static partial class AppServices
{
    public static ProjectService Project { get; private set; } = null!;
    public static IPlcClient Plc { get; private set; } = null!;
    public static SnapshotStore Snapshots { get; private set; } = null!;
    public static SessionService Session { get; private set; } = null!;
    public static SelectionService Selection { get; private set; } = null!;
    public static PlcCommands Commands { get; private set; } = null!;
    public static NavigationService Navigation { get; private set; } = null!;

    /// <summary>Services de la maquette : projet de démonstration, <see cref="DemoPlcClient"/>, instantané, session, navigation.</summary>
    public static void Initialize(Dispatcher dispatcher, NavigationService navigation)
    {
        Project = new ProjectService();
        var demo = new DemoPlcClient(Project.Project);
        Plc = demo;
        Snapshots = new SnapshotStore(demo, dispatcher);
        Session = new SessionService();
        Selection = new SelectionService();
        Commands = new PlcCommands(demo);
        Navigation = navigation;
    }

    public static DemoPlcClient? Demo => Plc as DemoPlcClient;
}
