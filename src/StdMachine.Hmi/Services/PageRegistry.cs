using StdMachine.Simulation.Demo;
using StdMachine.Hmi.Resources;
using StdMachine.Hmi.ViewModels;

namespace StdMachine.Hmi.Services;

/// <summary>Pages de l'application et leurs vues-modèles (docs/08 § 5). Une page par clé ; les arguments suivent les deux-points (« Station:2 »).</summary>
public static class PageRegistry
{
    public static void Register(NavigationService nav)
    {
        nav.Register(new PageInfo("Machine", Res.Get("Rail.Machine"), "home", a => new MachineViewModel()));
        nav.Register(new PageInfo("Station", Res.Get("Rail.Stations"), "grid_view", a => new StationViewModel(a.Length > 0 ? int.Parse(a[0]) : AppServices.Selection.Station), 1920, 1080, new[] { "Station:2", "Station:3" }));
        nav.Register(new PageInfo("ProcessEditor", Res.Get("Rail.StudioProcess"), "account_tree",
            a => a.Length > 0 && a[0] == "General" ? new ProcessEditorViewModel(0, "General") : new ProcessEditorViewModel(a.Length > 0 ? int.Parse(a[0]) : AppServices.Selection.Station, a.Length > 1 ? a[1] : "presser"),
            1920, 1080, new[] { "ProcessEditor:2:presser", "ProcessEditor:General" }));
        nav.Register(new PageInfo("Alarms", Res.Get("Rail.Alarms"), "notifications", a => new AlarmsViewModel()));
        nav.Register(new PageInfo("Quality", Res.Get("Rail.Quality"), "fact_check", a => new QualityViewModel(), 1920, 1500));
        nav.Register(new PageInfo("Forces", Res.Get("Rail.Forces"), "engineering", a => new ForcesViewModel(), 1920, 1300));
        nav.Register(new PageInfo("Parts", Res.Get("Rail.Parts"), "inventory_2", a => new PartsViewModel(a.Length > 0 ? int.Parse(a[0]) : 0)));
        nav.Register(new PageInfo("Orders", Res.Get("Rail.Orders"), "list_alt", a => new OrdersViewModel()));
        nav.Register(new PageInfo("Manual", Res.Get("Rail.Manual"), "pan_tool", a => new ManualViewModel(a.Length > 0 ? int.Parse(a[0]) : AppServices.Selection.Station), 1920, 1080, new[] { "Manual:2" }));
        nav.Register(new PageInfo("MachineConfig", Res.Get("Rail.StudioMachine"), "settings", a => new MachineConfigViewModel(), 1920, 1080, new[] { "MachineConfig" }));
        nav.Register(new PageInfo("StationConfig", Res.Get("Rail.StudioStation"), "build", a => new StationConfigViewModel(a.Length > 0 ? int.Parse(a[0]) : AppServices.Selection.Station, a.Length > 1 ? a[1] : null), 1920, 1080, new[] { "StationConfig:2" }));
        nav.Register(new PageInfo("Deploy", "Déploiement", "cloud_upload", a => new DeployViewModel()));
        nav.Register(new PageInfo("Users", Res.Get("Rail.Users"), "person", a => new UsersViewModel(), 1920, 1080, new[] { "Users" }));
        nav.Register(new PageInfo("Stats", Res.Get("Rail.Stats"), "bar_chart", a => new StatsViewModel()));
        nav.Register(new PageInfo("Communications", Res.Get("Rail.Communications"), "lan", a => new CommunicationsViewModel(), 1920, 1100));
        nav.Register(new PageInfo("System", Res.Get("Rail.System"), "computer", a => new SystemViewModel(), 1920, 1150));
        nav.Register(new PageInfo("FunctionEditor", "Fonction personnalisée", "edit", a => new FunctionEditorViewModel(int.Parse(a[0]), a[1]), 1920, 1080, new[] { "FunctionEditor:2:" + DemoIds.Of("fn/2/douce").ToString("N") }));
        nav.Register(new PageInfo("Components",Res.Get("Page.Components"), "dashboard", a => new ComponentsViewModel(), 1920, 1300));
        nav.Register(new PageInfo("ComponentsInput", Res.Get("Page.ComponentsInput"), "edit", a => new ComponentsInputViewModel(), 1920, 1440));
        nav.Register(new PageInfo("DragDropTest", Res.Get("Page.DragDropTest"), "touch_app", a => new DragDropTestViewModel()));
    }
}
