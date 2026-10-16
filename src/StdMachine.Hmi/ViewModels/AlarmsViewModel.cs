using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StdMachine.Core;
using StdMachine.Hmi.Controls;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Services;
using StdMachine.Hmi.Theme;
using StdMachine.Simulation.Demo;

namespace StdMachine.Hmi.ViewModels;

public sealed partial class AlarmRowViewModel : ObservableObject
{
    public AlarmRowViewModel(AlarmInfo a, bool history)
    {
        Alarm = a;
        History = history;
        Update(a);
    }

    public AlarmInfo Alarm { get; private set; }
    public bool History { get; }
    public int Id => Alarm.Id;

    [ObservableProperty] private string _timeText = string.Empty;
    [ObservableProperty] private string _durationText = string.Empty;
    [ObservableProperty] private string _where = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string? _subText;
    [ObservableProperty] private string _stateText = string.Empty;
    [ObservableProperty] private Tone _tone;
    [ObservableProperty] private string _icon = "info";
    [ObservableProperty] private bool _canAcknowledge;
    [ObservableProperty] private bool _canGoTo;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private string _code = string.Empty;

    public void Update(AlarmInfo a)
    {
        Alarm = a;
        TimeText = DisplayFormat.Clock(a.Time);
        DurationText = DisplayFormat.Duration((a.ClearedTime ?? DateTime.Now) - a.Time);
        Where = a.Object != null ? a.Station + " › " + a.Object : a.Station;
        Message = a.Text;
        SubText = a.SubText;
        StateText = UiText.AlarmStateText(a.State);
        Tone = UiText.SeverityTone(a.Severity);
        Icon = UiText.SeverityIcon(a.Severity);
        IsActive = a.State == AlarmState.Active;
        CanAcknowledge = a.State == AlarmState.ClearedUnacknowledged || (a.State == AlarmState.Active && a.Severity == AlarmSeverity.Warning);
        CanGoTo = a.StationNumber.HasValue;
        Code = a.Code + "." + a.SubCode;
    }
}

public sealed record AlarmFilterChip(string Key, string Label);

/// <summary>Page Alarmes (docs/08 § 6.4) : actives et historique, filtres en puces, tiroir de contexte.</summary>
public sealed partial class AlarmsViewModel : ViewModelBase
{
    private readonly List<AlarmRowViewModel> _history = new List<AlarmRowViewModel>();
    private readonly Dictionary<int, AlarmRowViewModel> _active = new Dictionary<int, AlarmRowViewModel>();

    public AlarmsViewModel()
    {
        foreach (var a in DemoExtras.AlarmHistory(DateTime.Now)) _history.Add(new AlarmRowViewModel(a, true));
        Filters = new[]
        {
            new AlarmFilterChip("all", "Toutes"), new AlarmFilterChip("machine", "Défauts machine"), new AlarmFilterChip("station", "Défauts station"),
            new AlarmFilterChip("warning", "Avertissements"), new AlarmFilterChip("quality", "Qualité"), new AlarmFilterChip("info", "Informations"),
        };
        OnSnapshots((_, s) => Refresh(s));
        Refresh(AppServices.Snapshots.Current);
    }

    public IReadOnlyList<AlarmFilterChip> Filters { get; }
    public ObservableCollection<AlarmRowViewModel> Rows { get; } = new ObservableCollection<AlarmRowViewModel>();

    [ObservableProperty] private int _tabIndex;
    [ObservableProperty] private string _filter = "all";
    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private int _activeCount;
    [ObservableProperty] private int _historyCount;

    partial void OnTabIndexChanged(int value) => Refresh(AppServices.Snapshots.Current);
    partial void OnFilterChanged(string value) => Refresh(AppServices.Snapshots.Current);
    partial void OnSearchChanged(string value) => Refresh(AppServices.Snapshots.Current);

    private bool Matches(AlarmRowViewModel r)
    {
        var a = r.Alarm;
        switch (Filter)
        {
            case "machine": if (a.Severity != AlarmSeverity.MachineFault) return false; break;
            case "station": if (a.Severity != AlarmSeverity.StationFault) return false; break;
            case "warning": if (a.Severity != AlarmSeverity.Warning) return false; break;
            case "quality": if (a.Severity != AlarmSeverity.Quality) return false; break;
            case "info": if (a.Severity != AlarmSeverity.Information) return false; break;
        }
        string q = (Search ?? string.Empty).Trim().ToLowerInvariant();
        if (q.Length == 0) return true;
        return (r.Where + " " + r.Message + " " + r.SubText).ToLowerInvariant().Contains(q);
    }

    private void Refresh(PlcSnapshot s)
    {
        var current = new HashSet<int>();
        foreach (var a in s.Alarms)
        {
            current.Add(a.Id);
            if (_active.TryGetValue(a.Id, out var row)) row.Update(a);
            else _active[a.Id] = new AlarmRowViewModel(a, false);
        }
        foreach (var id in _active.Keys.Where(k => !current.Contains(k)).ToList()) _active.Remove(id);

        ActiveCount = _active.Values.Count(r => r.Alarm.State != AlarmState.Acknowledged);
        HistoryCount = _history.Count + _active.Values.Count(r => r.Alarm.State == AlarmState.Acknowledged);

        IEnumerable<AlarmRowViewModel> source = TabIndex == 0
            ? _active.Values.Where(r => r.Alarm.State != AlarmState.Acknowledged).OrderByDescending(r => r.Alarm.Time)
            : _active.Values.Where(r => r.Alarm.State == AlarmState.Acknowledged).Concat(_history).OrderByDescending(r => r.Alarm.Time);
        var shown = source.Where(Matches).ToList();

        // Mise à jour sur place pour ne pas perdre le défilement ni le toucher en cours.
        var ids = shown.Select(r => r.Id).ToList();
        if (!Rows.Select(r => r.Id).SequenceEqual(ids))
        {
            Rows.Clear();
            foreach (var r in shown) Rows.Add(r);
        }
        IsEmpty = Rows.Count == 0;
        Summary = TabIndex == 0 ? UiText.Plural(ActiveCount, "alarme en cours", "alarmes en cours") : UiText.Plural(Rows.Count, "alarme dans la période", "alarmes dans la période");
    }

    [RelayCommand] private void SetFilter(string key) => Filter = key;

    [RelayCommand]
    private async Task Acknowledge(AlarmRowViewModel row)
    {
        var key = new AlarmKey(row.Alarm.Code, row.Alarm.Object);
        await AppServices.Commands.RunAsync("ack." + row.Id, () => AppServices.Plc.AcknowledgeAlarmAsync(key, CancellationToken.None), "Alarme acquittée.");
    }

    [RelayCommand]
    private void GoTo(AlarmRowViewModel row)
    {
        if (row.Alarm.StationNumber.HasValue)
            AppServices.Navigation.Navigate("Station:" + row.Alarm.StationNumber.Value, new HighlightRequest(null, row.Id), pushHistory: true);
    }

    [RelayCommand]
    private void Open(AlarmRowViewModel row)
    {
        AppServices.Drawer.Show("Alarme " + row.Code, AlarmDrawerContent.Build(row.Alarm));
    }

    [RelayCommand] private void ExportCsv() => AppServices.Toast.Show("Export CSV : disponible avec le journal réel (phase 4).", ToastKind.Info);
}

public static class AlarmDrawerContent
{
    public static FrameworkElement Build(AlarmInfo a)
    {
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 24) };
        root.Children.Add(new StatusPill { Tone = UiText.SeverityTone(a.Severity), Text = UiText.Severity(a.Severity), IconName = UiText.SeverityIcon(a.Severity), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 12) });
        root.Children.Add(new TextBlock { Text = a.Text, Style = Tokens.Get<Style>("Type.Title"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
        root.Children.Add(Row("Apparue à", DisplayFormat.Clock(a.Time)));
        root.Children.Add(Row("Durée", DisplayFormat.Duration((a.ClearedTime ?? DateTime.Now) - a.Time)));
        root.Children.Add(Row("Station › objet", a.Object != null ? a.Station + " › " + a.Object : a.Station));
        root.Children.Add(Row("Code", a.Code + "." + a.SubCode));
        root.Children.Add(Row("État", UiText.AlarmStateText(a.State)));
        if (a.SubText != null) root.Children.Add(Row("Détail", a.SubText));
        root.Children.Add(new TextBlock { Text = "CONTEXTE AU MOMENT DE L'ALARME", Style = Tokens.Get<Style>("Type.Caps"), Margin = new Thickness(0, 18, 0, 8) });
        root.Children.Add(Row("Étape en cours", a.Context ?? "—"));
        root.Children.Add(Row("Mover", a.StationNumber.HasValue ? "Mover 2" : "—"));
        root.Children.Add(Row("Pièce", a.StationNumber.HasValue ? "P-000" + (640 + a.Id % 10) : "—"));
        if (a.StationNumber.HasValue)
        {
            var go = new Button { Content = "Aller à la station", Margin = new Thickness(0, 20, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            Ui.SetIcon(go, "arrow_forward");
            Ui.SetVariant(go, ButtonVariant.Primary);
            go.Click += (_, _) => AppServices.Navigation.Navigate("Station:" + a.StationNumber.Value, new HighlightRequest(null, a.Id), pushHistory: true);
            root.Children.Add(go);
        }
        return root;
    }

    private static FrameworkElement Row(string key, string value)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new TextBlock { Text = key, Style = Tokens.Get<Style>("Type.Caption"), VerticalAlignment = VerticalAlignment.Center });
        var v = new TextBlock { Text = value, FontWeight = FontWeights.Medium, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        return g;
    }
}
