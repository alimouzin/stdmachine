using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StdMachine.Core;
using StdMachine.Hmi.Resources;
using StdMachine.Hmi.Services;
using StdMachine.Hmi.Theme;

namespace StdMachine.Hmi.ViewModels;

/// <summary>Entrée du rail de navigation (docs/08 § 4.1) avec ses pastilles.</summary>
public sealed partial class RailItem : ObservableObject
{
    public RailItem(string key, string label, string icon, string group)
    {
        Key = key; Label = label; Icon = icon; Group = group;
    }

    public string Key { get; }
    public string Label { get; }
    public string Icon { get; }
    public string Group { get; }

    [ObservableProperty] private bool _isSelected;
    /// <summary>Compteur sur l'icône (alarmes actives).</summary>
    [ObservableProperty] private int _badge;
    /// <summary>Point rouge (erreurs de validation) ou bleu (projet non déployé) sur l'icône.</summary>
    [ObservableProperty] private Tone? _dot;
    [ObservableProperty] private bool _isLocked;
    public bool IsFirstOfGroup { get; set; }
}

/// <summary>Coquille : barre du haut, rail de navigation, bandeau d'alarme (docs/08 § 4).</summary>
public sealed partial class ShellViewModel : ViewModelBase
{
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _flash;
    private bool _updatingOverride;
    private int _bannerAlarmId = -1;

    public ShellViewModel()
    {
        Navigation = AppServices.Navigation;
        Rail = new ObservableCollection<RailItem>
        {
            new RailItem("Machine", Res.Get("Rail.Machine"), "home", "Exploitation") { IsFirstOfGroup = true },
            new RailItem("Station", Res.Get("Rail.Stations"), "grid_view", "Exploitation"),
            new RailItem("Manual", Res.Get("Rail.Manual"), "pan_tool", "Exploitation"),
            new RailItem("Alarms", Res.Get("Rail.Alarms"), "notifications", "Exploitation"),
            new RailItem("Quality", Res.Get("Rail.Quality"), "fact_check", "Exploitation"),
            new RailItem("Forces", Res.Get("Rail.Forces"), "engineering", "Exploitation"),
            new RailItem("Parts", Res.Get("Rail.Parts"), "inventory_2", "Exploitation"),
            new RailItem("Orders", Res.Get("Rail.Orders"), "list_alt", "Exploitation"),
            new RailItem("MachineConfig", Res.Get("Rail.StudioMachine"), "settings", "Studio") { IsFirstOfGroup = true },
            new RailItem("StationConfig", Res.Get("Rail.StudioStation"), "build", "Studio"),
            new RailItem("ProcessEditor", Res.Get("Rail.StudioProcess"), "account_tree", "Studio"),
            new RailItem("Users", Res.Get("Rail.Users"), "person", "Administration") { IsFirstOfGroup = true },
            new RailItem("Stats", Res.Get("Rail.Stats"), "bar_chart", "Administration"),
            new RailItem("Communications", Res.Get("Rail.Communications"), "lan", "Administration"),
            new RailItem("System", Res.Get("Rail.System"), "computer", "Administration"),
        };

        Navigation.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(NavigationService.CurrentBaseKey)) UpdateSelection(); };
        AppServices.Snapshots.Updated += (_, s) => Apply(s);
        AppServices.Session.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(UserLabel)); };
        AppServices.Project.PropertyChanged += (_, _) => UpdateDots();
        AppServices.Project.Changed += (_, _) => UpdateDots();

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) => OnPropertyChanged(nameof(Clock));
        _clock.Start();

        _flash = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _flash.Tick += (_, _) => { _flash.Stop(); StateFlash = false; };

        Apply(AppServices.Snapshots.Current);
        UpdateSelection();
        UpdateDots();
    }

    public NavigationService Navigation { get; }
    public ObservableCollection<RailItem> Rail { get; }

    // ---- barre du haut
    public string MachineName => "MX-4";
    public string ProjectName => AppServices.Project.Project.Name;

    [ObservableProperty] private PackMlState _state;
    [ObservableProperty] private string _modeText = string.Empty;
    [ObservableProperty] private Tone _modeTone;
    [ObservableProperty] private string _orderText = string.Empty;
    [ObservableProperty] private string _orderCounts = string.Empty;
    [ObservableProperty] private double _orderGood;
    [ObservableProperty] private double _orderBad;
    [ObservableProperty] private double _orderActive;
    [ObservableProperty] private double _overridePercent = 80;
    [ObservableProperty] private string _overrideChain = string.Empty;
    [ObservableProperty] private int _alarmCount;
    [ObservableProperty] private Tone _alarmTone = Tone.Neutral;
    [ObservableProperty] private string _connectionText = string.Empty;
    [ObservableProperty] private Tone _connectionTone;
    [ObservableProperty] private Tone _communicationsTone = Tone.Ok;
    [ObservableProperty] private int _forceCount;
    [ObservableProperty] private bool _overrideZero;
    [ObservableProperty] private bool _isDemo;
    [ObservableProperty] private bool _stateFlash;
    [ObservableProperty] private Tone _stateTone;
    [ObservableProperty] private bool _loadingLocked;
    /// <summary>Puce de la barre du haut pendant « Finir les pièces » : « Fin des pièces · N restantes ».</summary>
    [ObservableProperty] private bool _draining;
    [ObservableProperty] private string _drainChip = string.Empty;
    public string ForceText => Res.Get("Shell.Forces") + " : " + ForceCount;

    // ---- bandeau d'alarme
    [ObservableProperty] private bool _bannerVisible;
    [ObservableProperty] private string _bannerTime = string.Empty;
    [ObservableProperty] private string _bannerWhere = string.Empty;
    [ObservableProperty] private string _bannerText = string.Empty;
    [ObservableProperty] private string _bannerMore = string.Empty;
    [ObservableProperty] private int _bannerStation;

    public string UserLabel => AppServices.Session.Label;
    public string Clock => DateTime.Now.ToString("HH:mm:ss", DisplayFormat.Culture);

    partial void OnOverridePercentChanged(double value)
    {
        if (_updatingOverride) return;
        _ = AppServices.Plc.SetOverrideAsync(new OverrideTarget(OverrideLevel.Machine), value, false, System.Threading.CancellationToken.None);
    }

    private void Apply(PlcSnapshot s)
    {
        var m = s.Machine;
        if (State != m.State)
        {
            State = m.State;
            StateTone = ToneFor(m.State);
            StateFlash = true;
            _flash.Stop();
            _flash.Start();
        }
        ModeText = m.Mode == MachineMode.Production ? Res.Get("Mode.Production") : Res.Get("Mode.Maintenance");
        ModeTone = m.Mode == MachineMode.Production ? Tone.Neutral : Tone.Maint;

        var o = s.Orders.Current;
        if (o != null)
        {
            OrderText = Res.Get("Order.Short") + " " + o.Number;
            OrderCounts = o.Good + o.Bad + o.InProcess > 0 ? (o.Good) + " / " + o.Quantity : "";
            OrderGood = (double)o.Good / o.Quantity;
            OrderBad = (double)o.Bad / o.Quantity;
            OrderActive = (double)o.InProcess / o.Quantity;
        }

        _updatingOverride = true;
        OverridePercent = m.OverridePercent;
        _updatingOverride = false;
        OverrideZero = m.OverridePercent <= 0;
        OverrideChain = m.OverridePercent.ToString("0", DisplayFormat.Culture) + " % × 100 % × 100 % = " + m.EffectiveOverridePercent.ToString("0", DisplayFormat.Culture) + " %";

        var active = s.Alarms.Where(a => a.State != AlarmState.Acknowledged && a.Severity != AlarmSeverity.Information).ToList();
        AlarmCount = active.Count;
        AlarmTone = active.Any(a => a.Severity >= AlarmSeverity.StationFault && a.State == AlarmState.Active) ? Tone.Err : active.Count > 0 ? Tone.Warn : Tone.Neutral;

        IsDemo = s.Connection == PlcConnectionState.Demo;
        switch (s.Connection)
        {
            case PlcConnectionState.Demo: ConnectionText = Res.Get("Connection.Demo"); ConnectionTone = Tone.Sim; break;
            case PlcConnectionState.Simulation: ConnectionText = Res.Get("Connection.Simulation"); ConnectionTone = Tone.Warn; break;
            case PlcConnectionState.Connected: ConnectionText = Res.Get("Connection.Connected"); ConnectionTone = Tone.Ok; break;
            default: ConnectionText = Res.Get("Connection.Disconnected"); ConnectionTone = Tone.Err; break;
        }
        ForceCount = s.Forces.Entries.Count;
        OnPropertyChanged(nameof(ForceText));
        LoadingLocked = m.LoadingLocked;
        Draining = m.Draining;
        DrainChip = Res.Get("Drain.Chip") + " · " + m.PartsInProcess;

        // Bandeau : le défaut le plus récent non acquitté
        var faults = s.Alarms.Where(a => a.Severity >= AlarmSeverity.StationFault && a.State == AlarmState.Active).OrderByDescending(a => a.Time).ToList();
        if (faults.Count > 0)
        {
            var f = faults[0];
            _bannerAlarmId = f.Id;
            BannerVisible = true;
            BannerTime = DisplayFormat.ClockShort(f.Time);
            BannerWhere = f.Station + (f.Object != null ? " › " + f.Object : "");
            BannerText = f.Text;
            BannerMore = faults.Count > 1 ? "+ " + (faults.Count - 1) + " autre" + (faults.Count > 2 ? "s" : "") : string.Empty;
            BannerStation = f.StationNumber ?? 0;
        }
        else
        {
            BannerVisible = false;
            _bannerAlarmId = -1;
        }
        UpdateDots();
    }

    public static Tone ToneFor(PackMlState s)
    {
        switch (s)
        {
            case PackMlState.Execute: return Tone.Ok;
            case PackMlState.Idle: return Tone.Ok;
            case PackMlState.Held:
            case PackMlState.Resetting:
            case PackMlState.Stopping: return Tone.Warn;
            case PackMlState.Aborted:
            case PackMlState.Clearing: return Tone.Err;
            default: return Tone.Neutral;
        }
    }

    private void UpdateSelection()
    {
        string? key = Navigation.CurrentBaseKey;
        foreach (var r in Rail) r.IsSelected = r.Key == key;
    }

    private void UpdateDots()
    {
        var s = AppServices.Snapshots.Current;
        foreach (var r in Rail)
        {
            r.Badge = r.Key == "Alarms" ? AlarmCount : 0;
            r.Dot = null;
            if (r.Group == "Studio")
            {
                if (AppServices.Project.ErrorCount > 0) r.Dot = Tone.Err;
                else if (AppServices.Project.IsDirty) r.Dot = Tone.Maint;
            }
            if (r.Key == "Forces" && s.Forces.Entries.Count > 0) r.Dot = Tone.Maint;
            if (r.Key == "Quality" && s.Quality.Tracks.Any(t => t.Locked)) r.Dot = Tone.Err;
        }
    }

    // ---- commandes
    [RelayCommand]
    private void Open(RailItem item)
    {
        int st = AppServices.Selection.Station;
        switch (item.Key)
        {
            case "Station": Navigation.Navigate("Station:" + st); break;
            case "Manual": Navigation.Navigate("Manual:" + st); break;
            case "StationConfig": Navigation.Navigate("StationConfig:" + st); break;
            case "ProcessEditor": Navigation.Navigate("ProcessEditor:" + st + ":presser"); break;
            default: Navigation.Navigate(item.Key); break;
        }
    }

    [RelayCommand] private void GoAlarms() => Navigation.Navigate("Alarms");
    [RelayCommand] private void GoForces() => Navigation.Navigate("Forces");
    [RelayCommand] private void GoCommunications() => Navigation.Navigate("Communications");
    [RelayCommand] private void Back() => Navigation.Back();

    [RelayCommand]
    private void BannerGoTo()
    {
        if (BannerStation > 0) Navigation.Navigate("Station:" + BannerStation, new HighlightRequest(null, _bannerAlarmId), pushHistory: true);
    }

    [RelayCommand]
    private async Task BannerAcknowledge()
    {
        if (BannerStation > 0) await AppServices.Commands.SendStationAsync(BannerStation, StateCommand.Clear);
        else await AppServices.Commands.SendAsync(StateCommand.Clear);
    }
}

/// <summary>Élément à mettre en évidence à l'arrivée sur une page par un lien profond (docs/08 § 7.7).</summary>
public sealed record HighlightRequest(Guid? StepId, int? AlarmId = null, Guid? ObjectId = null, string? Field = null);
