using System.IO;
using System.Windows;
using Core.Access;
using Core.Config;
using Core.Orders;
using Core.Plc;
using Hmi.Services;
using Hmi.ViewModels;

namespace Hmi;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Composition à la main : la machine est lue dans machine.json, aucun code par station.
        var machine = MachineLoader.LoadFile(Path.Combine(AppContext.BaseDirectory, "machine.json"));
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StdMachine");

        IPlcClient plc = new PlcSimulator(machine);
        var monitor = new PlcMonitor(plc);
        var session = new SessionService(new AccessPolicy(new JsonUserStore(Path.Combine(dataDir, "access.json"))));
        IOrderSource orders = new LocalOrderSource(Path.Combine(dataDir, "orders.json"));

        var main = new MainViewModel(session, monitor, new[]
        {
            new PageItem(new MachinePageViewModel(monitor, session)),
            new PageItem(new StationPageViewModel(monitor, session)),
            new PageItem(new OrdersPageViewModel(orders, session)),
            new PageItem(new StatsPageViewModel()),
            new PageItem(new UsersPageViewModel(session), ActionType.UserAdmin),
        });

        new MainWindow { DataContext = main }.Show();
    }
}
