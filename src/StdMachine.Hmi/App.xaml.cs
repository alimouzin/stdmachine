using System.Windows.Media;
using System.IO;
using System.Windows;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Services;

namespace StdMachine.Hmi;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) => { CrashLog.Write(e.Exception); };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write(e.ExceptionObject as Exception);
    }

    static App()
    {
        // Pile WM_POINTER pour le toucher : doit être posé avant la création de toute fenêtre.
        TouchSettings.EnableAtStartup();
    }

    private NavigationService _navigation = null!;
    private MainWindow _window = null!;
    private string? _logFile;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var options = CommandLine.Parse(e.Args);
        if (e.Args.Contains("--dump-demo"))
        {
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "demo-dump.txt"), StdMachine.Simulation.Demo.DemoDump.Run());
            Shutdown(0);
            return;
        }
        AnimationPolicy.Capture = options.IsScreenshot;
        // Les captures sont reproductibles : couloirs par défaut, sans tenir compte du choix enregistré.
        if (options.Layout != null || options.IsScreenshot) Editor.EditorSettings.Override(string.Equals(options.Layout, "flow", StringComparison.OrdinalIgnoreCase));

        FreezeResources();
        TouchSettings.RegisterGlobalHandlers();

        _navigation = new NavigationService();
        AppServices.Initialize(Dispatcher, _navigation, options.Simulator, options.Scenario, options.PlantLog);
        PageRegistry.Register(_navigation);
        _window = new MainWindow();

        if (options.IsScreenshot)
        {
            _ = RunScreenshotsAsync(options);
            return;
        }

        BlinkClock.Instance.Start();
        _ = AppServices.Plc.ConnectAsync(CancellationToken.None);
        _navigation.Navigate(options.StartPage ?? "Machine");
        if (options.Windowed)
        {
            _window.Width = 1920; _window.Height = 1080;
            _window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (options.Topmost)
            {
                // Essais d'entrée automatisés : fenêtre au-dessus de tout et réduite pour tenir sur l'écran (mise à l'échelle de la mise en page, les coordonnées 1920 × 1080 restent valables).
                _window.Topmost = true;
                var wa = SystemParameters.WorkArea;
                double f = Math.Min(1.0, Math.Min((wa.Width - 40) / 1920.0, (wa.Height - 90) / 1080.0));
                _window.Width = 1920 * f + 16; _window.Height = 1080 * f + 39;
                _window.Left = 10; _window.Top = 10; _window.WindowStartupLocation = WindowStartupLocation.Manual;
                if (_window.Content is FrameworkElement root) root.LayoutTransform = new ScaleTransform(f, f);
            }
        }
        else
        {
            // Plein écran sans bordure (docs/07 § 8.1).
            _window.WindowStyle = WindowStyle.None;
            _window.ResizeMode = ResizeMode.NoResize;
            _window.WindowState = WindowState.Maximized;
        }
        _window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { AppServices.Plc?.DisposeAsync(); } catch (Exception) { }
        base.OnExit(e);
    }

    /// <summary>Fige les brosses et effets des jetons (meilleures performances de rendu).</summary>
    private void FreezeResources()
    {
        foreach (var dict in Resources.MergedDictionaries)
            foreach (var key in dict.Keys.Cast<object>().ToList())
                if (dict[key] is Freezable f && f.CanFreeze && !f.IsFrozen) f.Freeze();
    }

    // ------------------------------------------------------------------ captures d'écran

    /// <summary>
    /// Scénario « demo » (docs/07 § 10) : instantanés du scénario scripté à 0, 30, 60 et 90 s.
    /// <c>--at</c> en choisit un seul ; sans <c>--at</c>, <c>--screenshot-all</c> les produit tous et <c>--screenshot</c> prend 30 s.
    /// </summary>
    private async Task RunScreenshotsAsync(CommandLine options)
    {
        var service = new ScreenshotService(_window, _navigation);
        var dir = options.ScreenshotAllDir ?? Path.GetDirectoryName(Path.GetFullPath(options.ScreenshotFile!))!;
        Directory.CreateDirectory(dir);
        _logFile = Path.Combine(dir, "capture.log");
        try
        {
            var times = AppServices.Demo == null ? new[] { 0.0 }
                : options.AtSeconds.HasValue ? new[] { options.AtSeconds.Value }
                : options.ScreenshotAllDir != null ? new[] { 0.0, 30.0, 60.0, 90.0 } : new[] { 30.0 };
            foreach (var at in times)
            {
                AppServices.Snapshots.Freeze(AppServices.Demo != null ? AppServices.Demo.SnapshotAt(at) : AppServices.Plc.Snapshot);
                if (options.ScreenshotAllDir != null)
                {
                    var sub = Path.Combine(dir, AppServices.Demo != null ? "demo-" + at.ToString("0") : "sim-" + (options.Scenario ?? "idle"));
                    foreach (var page in _navigation.Pages.ToList())
                        foreach (var key in page.Keys)
                        {
                            var file = Path.Combine(sub, key.Replace(':', '-') + ".png");
                            await service.CaptureAsync(key, file, page.CaptureWidth, page.CaptureHeight, options.Do);
                            Log("OK " + file);
                        }
                }
                else
                {
                    var page = _navigation.Find(options.ScreenshotPage!) ?? throw new InvalidOperationException("Page inconnue : " + options.ScreenshotPage);
                    var w = options.Width ?? page.CaptureWidth;
                    var h = options.Height ?? page.CaptureHeight;
                    await service.CaptureAsync(options.ScreenshotPage!, options.ScreenshotFile!, w, h, options.Do);
                    Log("OK " + options.ScreenshotFile);
                }
            }
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Log("ERREUR " + ex);
            Shutdown(1);
        }
    }

    private void Log(string line)
    {
        try { File.AppendAllText(_logFile!, DateTime.Now.ToString("HH:mm:ss") + " " + line + Environment.NewLine); }
        catch (IOException) { }
    }
}

/// <summary>Options de ligne de commande (docs/07 § 10).</summary>
public sealed class CommandLine
{
    public string? ScreenshotPage { get; private set; }
    public string? ScreenshotFile { get; private set; }
    public string? ScreenshotAllDir { get; private set; }
    public int? Width { get; private set; }
    public int? Height { get; private set; }
    public string? Scenario { get; private set; }
    /// <summary>Action de capture : toast, drawer, dialog, keypad, keyboard, override, login…</summary>
    public string? Do { get; private set; }
    public double? AtSeconds { get; private set; }
    public bool Windowed { get; private set; }
    /// <summary>Exécute le projet MX-4 sur le simulateur fidèle au lieu du scénario de démonstration.</summary>
    public bool Simulator { get; private set; }
    /// <summary>Avec --simulator : active le canal Journal de messages de la passerelle usine.</summary>
    public bool PlantLog { get; private set; }
    public bool Topmost { get; private set; }
    public string? StartPage { get; private set; }
    /// <summary>« flow » : éditeur de process en flux vertical sans couloirs (prototype) ; sinon couloirs.</summary>
    public string? Layout { get; private set; }

    public bool IsScreenshot => ScreenshotAllDir != null || ScreenshotPage != null;

    public static CommandLine Parse(string[] args)
    {
        var o = new CommandLine();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--screenshot":
                    o.ScreenshotPage = args[++i];
                    o.ScreenshotFile = args[++i];
                    break;
                case "--screenshot-all":
                    o.ScreenshotAllDir = args[++i];
                    break;
                case "--size":
                    var parts = args[++i].Split('x');
                    o.Width = int.Parse(parts[0]); o.Height = int.Parse(parts[1]);
                    break;
                case "--scenario":
                    o.Scenario = args[++i];
                    break;
                case "--do":
                    o.Do = args[++i];
                    break;
                case "--at":
                    o.AtSeconds = double.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--topmost":
                    o.Topmost = true;
                    break;
                case "--windowed":
                    o.Windowed = true;
                    break;
                case "--simulator":
                    o.Simulator = true;
                    break;
                case "--plant-log":
                    o.PlantLog = true;
                    break;
                case "--layout":
                    o.Layout = args[++i];
                    break;
                case "--page":
                    o.StartPage = args[++i];
                    break;
            }
        }
        return o;
    }
}
