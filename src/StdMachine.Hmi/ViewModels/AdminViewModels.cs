using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StdMachine.Core;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Services;
using StdMachine.Hmi.Theme;
using StdMachine.Simulation.Demo;

namespace StdMachine.Hmi.ViewModels;

// ---------------------------------------------------------------------------------------------------- Déploiement (docs/08 § 6.10)

public sealed record DeployStepInfo(int Number, string Label, bool IsDone, bool IsCurrent);

public sealed record DiffRow(string Category, string Path, string Kind, string Detail, Tone Tone);

public sealed record ConditionRow(string Text, bool Ok, string? Hint);

public sealed partial class BlockProgressRow : ObservableObject
{
    public BlockProgressRow(string name) { Name = name; }
    public string Name { get; }
    [ObservableProperty] private double _fraction;
    [ObservableProperty] private bool _done;
}

public sealed partial class DeployViewModel : ViewModelBase
{
    public DeployViewModel()
    {
        BuildIssues();
        Diffs.Add(new DiffRow("Stations", "Pressage 01 › Variables › Compteur de pièces", "Ajouté", "Variable Nombre, valeur initiale 0", Tone.Ok));
        Diffs.Add(new DiffRow("Positions", "Pressage 01 › Dépose nid", "Modifié", "Z −18,20 (avant −17,90)", Tone.Accent));
        Diffs.Add(new DiffRow("Process", "Pressage 02 › Sertir", "Modifié", "Étape 4 « Attendre condition » : délai 5,0 s (avant 3,0 s)", Tone.Accent));
        Diffs.Add(new DiffRow("Recettes", "Presse › Recette n° 3", "Modifié", "Force finale maximale 5,5 kN (avant 5,2 kN)", Tone.Accent));
        Diffs.Add(new DiffRow("Objets", "Contrôle › Palpeur Keyence GT2", "Supprimé", "Remplacé par Vision Keyence IV", Tone.Err));
        BuildSteps();
        OnSnapshots((_, s) => BuildConditions(s));
        BuildConditions(AppServices.Snapshots.Current);
        foreach (var b in new[] { "Machine", "Stations 1 à 6", "Recettes de presse" }) Blocks.Add(new BlockProgressRow(b));
    }

    public ObservableCollection<DeployStepInfo> Steps { get; } = new ObservableCollection<DeployStepInfo>();
    public ObservableCollection<IssueRow> Issues { get; } = new ObservableCollection<IssueRow>();
    public ObservableCollection<DiffRow> Diffs { get; } = new ObservableCollection<DiffRow>();
    public ObservableCollection<ConditionRow> Conditions { get; } = new ObservableCollection<ConditionRow>();
    public ObservableCollection<BlockProgressRow> Blocks { get; } = new ObservableCollection<BlockProgressRow>();

    [ObservableProperty] private int _step;
    [ObservableProperty] private int _errorCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private bool _canNext;
    [ObservableProperty] private string _nextHint = string.Empty;
    [ObservableProperty] private bool _allConditionsOk;
    [ObservableProperty] private bool _deploying;
    [ObservableProperty] private bool _finished;
    [ObservableProperty] private string _resultText = string.Empty;
    [ObservableProperty] private string _deployedText = string.Empty;

    private void BuildIssues()
    {
        Issues.Clear();
        foreach (var i in AppServices.Project.Issues) Issues.Add(new IssueRow(i.Code, i.Message, i.Severity, i.StepId));
        ErrorCount = AppServices.Project.ErrorCount;
        WarningCount = AppServices.Project.WarningCount;
        DeployedText = "Version déployée : " + AppServices.Project.DeployedHash + " · " + (AppServices.Project.DeployedAt?.ToString("dd.MM.yyyy HH:mm", DisplayFormat.Culture) ?? "—");
    }

    private void BuildSteps()
    {
        string[] labels = { "Validation", "Différences", "Conditions", "Envoi" };
        Steps.Clear();
        for (int i = 0; i < 4; i++) Steps.Add(new DeployStepInfo(i + 1, labels[i], i < Step, i == Step));
        CanNext = Step == 0 ? ErrorCount == 0 : Step == 1 ? true : Step == 2 ? AllConditionsOk : false;
        NextHint = Step == 0 && ErrorCount > 0 ? "Corrigez les erreurs pour continuer." : Step == 2 && !AllConditionsOk ? "Toutes les conditions doivent être réunies." : string.Empty;
    }

    partial void OnStepChanged(int value) => BuildSteps();

    private void BuildConditions(PlcSnapshot s)
    {
        Conditions.Clear();
        bool stopped = s.Machine.State == PackMlState.Stopped;
        Conditions.Add(new ConditionRow("Machine Stopped", stopped, stopped ? null : "Arrêter la machine depuis la page Machine."));
        bool plc = s.Connection == PlcConnectionState.Connected || s.Connection == PlcConnectionState.Simulation || s.Connection == PlcConnectionState.Demo;
        Conditions.Add(new ConditionRow("PLC connecté", plc, null));
        bool right = AppServices.Session.Level >= 2;
        Conditions.Add(new ConditionRow("Droit Déployer", right, right ? null : "Niveau Ingénieur process requis."));
        bool noFault = !s.Movers.Any(m => m.State == MoverState.Error);
        Conditions.Add(new ConditionRow("Aucun mover en défaut", noFault, null));
        AllConditionsOk = Conditions.All(c => c.Ok);
        BuildSteps();
    }

    [RelayCommand] private void Next() { if (CanNext && Step < 3) Step++; }
    [RelayCommand] private void Back() { if (Step > 0 && !Deploying) Step--; }

    [RelayCommand]
    private void Fix(IssueRow row)
    {
        if (row.StepId.HasValue) AppServices.Navigation.Navigate("ProcessEditor:2:presser", new HighlightRequest(row.StepId), pushHistory: true);
        else AppServices.Navigation.Navigate("StationConfig:2", null, pushHistory: true);
    }

    [RelayCommand]
    private async Task Deploy()
    {
        if (Deploying || Finished) return;
        if (!AllConditionsOk) { AppServices.Toast.Show("Conditions de déploiement non réunies.", ToastKind.Warning); return; }
        // Compilation réelle du projet : empreinte et nombre d'instructions envoyés au PLC (le substitut de la maquette sinon).
        var payload = new CompiledProject("A1B2C3D4E5F60718", 412);
        var compiled = AppServices.Compilation.Compile(AppServices.Project.Project);
        if (compiled != null)
        {
            if (!compiled.Succeeded)
            {
                ResultText = "Compilation impossible : " + string.Join(" ; ", compiled.Issues.Where(i => i.Severity == StdMachine.Core.Validation.Severity.Error).Take(3).Select(i => i.Code + " " + i.Message));
                AppServices.Toast.Show("Le projet ne se compile pas.", ToastKind.Error);
                return;
            }
            var tables = compiled.Project!;
            payload = new CompiledProject(tables.Hash.Substring(0, 16).ToUpperInvariant(), tables.Stations.Sum(x => x.Instructions.Count) + tables.Machine.Instructions.Count);
        }
        Deploying = true;
        var progress = new Progress<DeployProgress>(p =>
        {
            foreach (var b in Blocks)
                if (p.Block.StartsWith(b.Name.Substring(0, Math.Min(8, b.Name.Length)), StringComparison.OrdinalIgnoreCase) || (b.Name == "Stations 1 à 6" && p.Block.StartsWith("Station")))
                { b.Fraction = p.Fraction; b.Done = p.Fraction >= 1; }
        });
        var r = await AppServices.Plc.DeployAsync(payload, progress, CancellationToken.None);
        Deploying = false;
        Finished = true;
        foreach (var b in Blocks) { b.Fraction = 1; b.Done = true; }
        if (r.Success)
        {
            AppServices.Project.MarkDeployed(r.Hash ?? "", r.At);
            ResultText = "Déploiement réussi · empreinte " + r.Hash + " · " + DisplayFormat.Clock(r.At);
            AppServices.Toast.Show("Déploiement réussi.", ToastKind.Success);
            BuildIssues();
        }
        else ResultText = "Refusé par le PLC : " + string.Join(" ; ", r.Refusals);
    }

    [RelayCommand] private void Close() => AppServices.Navigation.Back();
}

// ---------------------------------------------------------------------------------------------------- Utilisateurs et droits (docs/08 § 6.11)

public sealed record UserRow(string Name, string Id, string Level, string LastLogin, bool Active);

public sealed record LevelRow(int Number, string Name, int Users);

public sealed partial class RightRowViewModel : ObservableObject
{
    public RightRowViewModel(RightLine line) { Group = line.Group; Code = line.Code; Name = line.Name; _level = line.Level; Locked = line.Locked; }
    public string Group { get; }
    public string Code { get; }
    public string Name { get; }
    public bool Locked { get; }
    [ObservableProperty] private double _level;

    partial void OnLevelChanged(double value)
    {
        if (!Locked) AppServices.Toast.Show("Droit « " + Code + " » : niveau " + (int)value + " (journalisé).", ToastKind.Info);
    }
}

public sealed record AuditRow(string Time, string User, string What, string Before, string After);

public sealed record RightGroup(string Name, IReadOnlyList<RightRowViewModel> Rights);

public sealed partial class UsersViewModel : ViewModelBase
{
    public UsersViewModel()
    {
        foreach (var u in SessionService.Users) Users.Add(new UserRow(u.Name, u.Id, u.Level + " · " + u.LevelName, u.LastLogin.ToString("dd.MM HH:mm", DisplayFormat.Culture), true));
        for (int i = 0; i <= 3; i++) Levels.Add(new LevelRow(i, SessionService.LevelName(i), SessionService.Users.Count(u => u.Level == i)));
        var rows = DemoExtras.Rights().Select(r => new RightRowViewModel(r)).ToList();
        foreach (var g in rows.GroupBy(r => r.Group)) Rights.Add(new RightGroup(g.Key, g.ToList()));
        foreach (var a in DemoExtras.Audit(DateTime.Now)) Audit.Add(new AuditRow(a.Time.ToString("dd.MM HH:mm", DisplayFormat.Culture), a.User, a.What, a.Before, a.After));
    }

    public ObservableCollection<UserRow> Users { get; } = new ObservableCollection<UserRow>();
    public ObservableCollection<LevelRow> Levels { get; } = new ObservableCollection<LevelRow>();
    public ObservableCollection<RightGroup> Rights { get; } = new ObservableCollection<RightGroup>();
    public ObservableCollection<AuditRow> Audit { get; } = new ObservableCollection<AuditRow>();

    [ObservableProperty] private int _tabIndex;

    [RelayCommand] private void ResetPassword(UserRow u) => AppServices.Toast.Show("Mot de passe de « " + u.Name + " » réinitialisé (maquette).", ToastKind.Success);
    [RelayCommand] private void NewUser() => AppServices.Toast.Show("Nouvel utilisateur : tiroir d'édition (phase 2).", ToastKind.Info);
}

// ---------------------------------------------------------------------------------------------------- Statistiques (docs/08 § 6.12)

public sealed class StatsViewModel : ViewModelBase { }

// ---------------------------------------------------------------------------------------------------- Communications (docs/08 § 6.16)

public sealed partial class ChannelViewModel : ObservableObject
{
    public ChannelViewModel(string name, string icon, string stateText, Tone tone, string detail, bool planned, bool enabled)
    {
        Name = name; Icon = icon; StateText = stateText; Tone = tone; Detail = detail; Planned = planned; _enabled = enabled;
    }

    public string Name { get; }
    public string Icon { get; }
    public string StateText { get; }
    public Tone Tone { get; }
    public string Detail { get; }
    public bool Planned { get; }
    [ObservableProperty] private bool _enabled;
}

public sealed record DeviceRow(string Name, string Station, bool Reachable, string Latency);

public sealed partial class CommunicationsViewModel : ViewModelBase
{
    public CommunicationsViewModel()
    {
        Channels.Add(new ChannelViewModel("PLC", "memory", "Connecté · Démonstration", Tone.Sim, "Mode Simulation · AMS Net Id 127.0.0.1.1.1 · port 851\nEmpreinte du projet : 9F3A1C07D2E84B66", false, true));
        Channels.Add(new ChannelViewModel("Journal de messages", "history", "Actif · 18 412 messages", Tone.Ok, "Dossier ProgramData\\MX4\\messages · rétention 7 jours", false, true));
        Channels.Add(new ChannelViewModel("Usine — événements (MQTT)", "dns", "Prévu", Tone.Neutral, "Broker, port, TLS, identifiants · sujets : pièces, alarmes, états, OF", true, false));
        Channels.Add(new ChannelViewModel("Usine — état (OPC UA TwinCAT)", "router", "Prévu (phase PLC)", Tone.Neutral, "Adresse à donner à l'usine : opc.tcp://ads-host:4840", true, false));
        Channels.Add(new ChannelViewModel("OF entrants", "list_alt", "Source locale", Tone.Ok, "Usine (MQTT) : prévu", false, true));
        Devices.Add(new DeviceRow("Lecteur de code", "Contrôle", true, "2 ms"));
        Devices.Add(new DeviceRow("Vision Asyril Eye+", "Pressage 01", true, "4 ms"));
        Devices.Add(new DeviceRow("Vision Keyence IV", "Contrôle", true, "3 ms"));
        Devices.Add(new DeviceRow("Robot XYZC", "Pressage 02", true, "1 ms"));
    }

    public ObservableCollection<ChannelViewModel> Channels { get; } = new ObservableCollection<ChannelViewModel>();
    public ObservableCollection<DeviceRow> Devices { get; } = new ObservableCollection<DeviceRow>();
    public string DevicesSummary => Devices.Count + " appareils · " + Devices.Count(d => d.Reachable) + " joignables";

    [ObservableProperty] private string _machineId = "MX-4";
    [ObservableProperty] private string _root = "entreprise/site-1/atelier-3";

    [RelayCommand] private void Test(ChannelViewModel c) => AppServices.Toast.Show("Test de « " + c.Name + " » : réussi (démonstration).", ToastKind.Success);

    [RelayCommand]
    private void Settings(ChannelViewModel c)
    {
        var root = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(24, 0, 24, 24) };
        root.Children.Add(new System.Windows.Controls.TextBlock { Text = c.Detail, TextWrapping = System.Windows.TextWrapping.Wrap, Margin = new System.Windows.Thickness(0, 0, 0, 14) });
        root.Children.Add(new Controls.TextField { Label = "Adresse", Text = "127.0.0.1.1.1", Margin = new System.Windows.Thickness(0, 0, 0, 10) });
        root.Children.Add(new Controls.NumberField { Label = "Port", Value = 851, Minimum = 1, Maximum = 65535, Step = 1, Decimals = 0, Margin = new System.Windows.Thickness(0, 0, 0, 10) });
        var apply = new System.Windows.Controls.Button { Content = "Appliquer", HorizontalAlignment = System.Windows.HorizontalAlignment.Left, Margin = new System.Windows.Thickness(0, 10, 0, 0) };
        Controls.Ui.SetVariant(apply, Controls.ButtonVariant.Primary);
        apply.Click += (_, _) => AppServices.Toast.Show("Canal relancé (démonstration), modification journalisée.", ToastKind.Success);
        root.Children.Add(apply);
        AppServices.Drawer.Show("Réglages · " + c.Name, root);
    }

    [RelayCommand] private void ExportSchemas() => AppServices.Toast.Show("Schémas JSON exportés (démonstration).", ToastKind.Success);
    [RelayCommand] private void ResetCounters() => AppServices.Toast.Show("Compteurs de production remis à zéro (démonstration).", ToastKind.Success);
}

// ---------------------------------------------------------------------------------------------------- Système (docs/08 § 6.13)

public sealed partial class SystemViewModel : ViewModelBase
{
    public SystemViewModel()
    {
        foreach (var d in DemoExtras.DeployHistory()) History.Add(new DeployHistoryRow(d.At.ToString("dd.MM.yyyy HH:mm", DisplayFormat.Culture), d.Hash, d.By, d.Summary, d.Current));
        _reduced = AnimationPolicy.Reduced;
        OnSnapshots((_, s) => ConnectionText = DescribeConnection(s.Connection));
        ConnectionText = DescribeConnection(AppServices.Snapshots.Current.Connection);
    }

    public ObservableCollection<DeployHistoryRow> History { get; } = new ObservableCollection<DeployHistoryRow>();
    public string Version => "Maquette PoC · .NET Framework 4.8 · build " + DateTime.Now.ToString("yyyy.MM.dd", DisplayFormat.Culture);

    [ObservableProperty] private string _connectionText = string.Empty;
    [ObservableProperty] private bool _reduced;
    [ObservableProperty] private bool _kiosk;
    [ObservableProperty] private double _logoutMinutes = 15;
    [ObservableProperty] private double _timeScale = 1;
    [ObservableProperty] private double _failRate = 0;

    partial void OnReducedChanged(bool value) => AnimationPolicy.Reduced = value;

    private static string DescribeConnection(PlcConnectionState s)
    {
        switch (s)
        {
            case PlcConnectionState.Demo: return "Données de démonstration rejouées (aucun PLC)";
            case PlcConnectionState.Simulation: return "Connecté au simulateur";
            case PlcConnectionState.Connected: return "Connecté";
            case PlcConnectionState.Connecting: return "Connexion en cours";
            default: return "Déconnecté";
        }
    }

    [RelayCommand] private void OpenTouchTest() => AppServices.Navigation.Navigate("DragDropTest", null, pushHistory: true);
    [RelayCommand] private void OpenComponents() => AppServices.Navigation.Navigate("Components", null, pushHistory: true);
    [RelayCommand] private void Restore(DeployHistoryRow r) => AppServices.Toast.Show("Restauration de la version " + r.Hash + " (démonstration).", ToastKind.Info);
    [RelayCommand] private void ExportConstants() => AppServices.Toast.Show("Constantes PLC exportées (démonstration).", ToastKind.Success);
    [RelayCommand] private void Info(string what) => AppServices.Toast.Show(what + " : disponible avec le projet réel (phase 2).", ToastKind.Info);
}

public sealed record DeployHistoryRow(string At, string Hash, string By, string Summary, bool Current);
