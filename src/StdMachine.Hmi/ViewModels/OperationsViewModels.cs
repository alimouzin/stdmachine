using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StdMachine.Core;
using StdMachine.Hmi.Controls;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Services;
using StdMachine.Hmi.Theme;
using StdMachine.Simulation.Demo;

namespace StdMachine.Hmi.ViewModels;

// ---------------------------------------------------------------------------------------------------- Qualité (docs/10 § 11)

public sealed partial class QualityRowViewModel : ObservableObject
{
    public QualityRowViewModel(QualityTrack t) { Update(t); }

    public QualityTrack Track { get; private set; } = null!;
    public string Name => Track.Name;

    [ObservableProperty] private string _window = string.Empty;
    [ObservableProperty] private int _capacity;
    [ObservableProperty] private int _consecutive;
    [ObservableProperty] private string _rateText = string.Empty;
    [ObservableProperty] private string _imputedText = "—";
    [ObservableProperty] private string _trendIcon = "trending_flat";
    [ObservableProperty] private string _fillText = string.Empty;
    [ObservableProperty] private Tone _tone;
    [ObservableProperty] private Tone _consecutiveTone;
    [ObservableProperty] private bool _locked;
    [ObservableProperty] private bool _probation;
    [ObservableProperty] private string _probationText = string.Empty;
    [ObservableProperty] private string? _reason;

    public void Update(QualityTrack t)
    {
        Track = t;
        Window = t.Window;
        Capacity = t.WindowSize;
        Consecutive = t.Consecutive;
        RateText = DisplayFormat.Number(t.RatePercent, 1) + " %";
        ImputedText = t.ImputedRatePercent.HasValue ? DisplayFormat.Number(t.ImputedRatePercent.Value, 1) + " %" : "—";
        Locked = t.Locked;
        Reason = t.Reason;
        Probation = t.Level == QualityLevel.Probation;
        ProbationText = Probation ? "Surveillance renforcée " + (10 - t.ProbationLeft) + " / 10" : string.Empty;
        FillText = t.SampleCount < Math.Min(20, t.WindowSize) ? "En cours de remplissage " + t.SampleCount + "/20" : string.Empty;
        Tone = t.Level == QualityLevel.Action ? Tone.Err : t.Level == QualityLevel.Warning ? Tone.Warn : t.Level == QualityLevel.Probation ? Tone.Warn : Tone.Neutral;
        ConsecutiveTone = t.Consecutive >= 3 ? Tone.Err : t.Consecutive == 2 ? Tone.Warn : Tone.Neutral;
        // Tendance : moitié récente contre moitié ancienne de la fenêtre
        var w = t.Window ?? string.Empty;
        if (w.Length >= 8)
        {
            int half = w.Length / 2;
            int oldBad = w.Take(half).Count(c => c == '1'), newBad = w.Skip(half).Count(c => c == '1');
            TrendIcon = newBad > oldBad ? "trending_up" : newBad < oldBad ? "trending_down" : "trending_flat";
        }
    }
}

public sealed partial class QualityViewModel : ViewModelBase
{
    private readonly Dictionary<string, QualityRowViewModel> _rows = new Dictionary<string, QualityRowViewModel>();

    public QualityViewModel()
    {
        OnSnapshots((_, s) => Refresh(s));
        Refresh(AppServices.Snapshots.Current);
    }

    public ObservableCollection<QualityRowViewModel> Stations { get; } = new ObservableCollection<QualityRowViewModel>();
    public ObservableCollection<QualityRowViewModel> Groups { get; } = new ObservableCollection<QualityRowViewModel>();
    public ObservableCollection<QualityRowViewModel> Movers { get; } = new ObservableCollection<QualityRowViewModel>();
    public ObservableCollection<QualityRowViewModel> Locks { get; } = new ObservableCollection<QualityRowViewModel>();
    public ObservableCollection<RejectBar> Breakdown { get; } = new ObservableCollection<RejectBar>();

    [ObservableProperty] private QualityRowViewModel? _machine;
    [ObservableProperty] private QualityRowViewModel? _order;
    [ObservableProperty] private bool _hasLocks;
    [ObservableProperty] private string _orderText = string.Empty;

    private QualityRowViewModel Row(QualityTrack t)
    {
        string key = t.Scope + "/" + t.Name;
        if (_rows.TryGetValue(key, out var r)) { r.Update(t); return r; }
        return _rows[key] = new QualityRowViewModel(t);
    }

    private void Sync(ObservableCollection<QualityRowViewModel> target, IEnumerable<QualityRowViewModel> source)
    {
        var list = source.ToList();
        if (!target.SequenceEqual(list)) { target.Clear(); foreach (var r in list) target.Add(r); }
    }

    private void Refresh(PlcSnapshot s)
    {
        var tracks = s.Quality.Tracks;
        Machine = Row(tracks.First(t => t.Scope == QualityScope.Machine));
        var order = tracks.FirstOrDefault(t => t.Scope == QualityScope.Order);
        Order = order == null ? null : Row(order);
        OrderText = s.Orders.Current == null ? string.Empty : "OF " + s.Orders.Current.Number + " : " + s.Orders.Current.Bad + " / " + s.Orders.Current.BudgetBad + " (" + DisplayFormat.Number(100.0 * s.Orders.Current.Bad / Math.Max(1, s.Orders.Current.BudgetBad), 0) + " %)";
        Sync(Stations, tracks.Where(t => t.Scope == QualityScope.Station).Select(Row));
        Sync(Groups, tracks.Where(t => t.Scope == QualityScope.Group).Select(Row));
        Sync(Movers, tracks.Where(t => t.Scope == QualityScope.Mover).Select(Row));
        Sync(Locks, tracks.Where(t => t.Locked).Select(Row));
        HasLocks = Locks.Count > 0;
        if (Breakdown.Count == 0)
        {
            Breakdown.Add(new RejectBar("Pressage 02", 9, 1.0)); Breakdown.Add(new RejectBar("Pressage 01", 4, 0.45)); Breakdown.Add(new RejectBar("Contrôle", 1, 0.12));
        }
    }

    [RelayCommand]
    private async Task Release(QualityRowViewModel row)
    {
        await AppServices.Commands.RunAsync("quality." + row.Name,
            () => AppServices.Plc.ReleaseQualityAsync(row.Track.Scope, row.Track.Name, "Pièces contrôlées", CancellationToken.None),
            "Station libérée : surveillance renforcée sur les 10 prochaines pièces.");
    }

    [RelayCommand]
    private void Open(QualityRowViewModel row)
    {
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 24) };
        root.Children.Add(new TextBlock { Text = "Les dernières pièces mauvaises de la portée", Style = Tokens.Get<Style>("Type.Caption"), Margin = new Thickness(0, 0, 0, 10) });
        foreach (var r in DemoExtras.RecentRejects(DateTime.Now))
        {
            var line = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            line.Children.Add(new TextBlock { Text = r.PartId + " · " + DisplayFormat.Clock(r.Time), FontWeight = FontWeights.SemiBold });
            line.Children.Add(new TextBlock { Text = r.Operation + " — " + r.Data, Style = Tokens.Get<Style>("Type.Caption"), TextWrapping = TextWrapping.Wrap });
            root.Children.Add(line);
        }
        var go = new Button { Content = "Voir dans Pièces", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        Ui.SetIcon(go, "inventory_2");
        go.Click += (_, _) => AppServices.Navigation.Navigate("Parts", null, pushHistory: true);
        root.Children.Add(go);
        AppServices.Drawer.Show(row.Name + " · détail qualité", root);
    }
}

public sealed record RejectBar(string Name, int Count, double Fraction);

// ---------------------------------------------------------------------------------------------------- Forçages (docs/12 § 8)

public sealed partial class DeclaredButtonViewModel : ObservableObject
{
    public DeclaredButtonViewModel(DeclaredState state, string label, string icon, bool blocks)
    {
        State = state; Label = label; Icon = icon; Blocks = blocks;
    }

    public DeclaredState State { get; }
    public string Label { get; }
    public string Icon { get; }
    public bool Blocks { get; }
    [ObservableProperty] private bool _isActive;
}

public sealed partial class ForceStationViewModel : ObservableObject
{
    public ForceStationViewModel(int number, string name) { Number = number; Name = name; }
    public int Number { get; }
    public string Name { get; }
    [ObservableProperty] private PackMlState _state;
    [ObservableProperty] private bool _outOfProduction;
    [ObservableProperty] private string _detail = string.Empty;
    [ObservableProperty] private string _modeText = string.Empty;
}

public sealed partial class ForceMoverViewModel : ObservableObject
{
    public ForceMoverViewModel(int number) { Number = number; }
    public int Number { get; }
    [ObservableProperty] private bool _outOfService;
    [ObservableProperty] private string? _reason;
}

public sealed partial class ForcesViewModel : ViewModelBase
{
    public ForcesViewModel()
    {
        Buttons = new[]
        {
            new DeclaredButtonViewModel(DeclaredState.PlannedStop, "Arrêt planifié", "pause_circle", false),
            new DeclaredButtonViewModel(DeclaredState.ChangeOver, "Changement de série", "swap_horiz", false),
            new DeclaredButtonViewModel(DeclaredState.WaitUpstream, "Attente amont", "arrow_back", false),
            new DeclaredButtonViewModel(DeclaredState.WaitDownstream, "Attente aval", "arrow_forward", false),
            new DeclaredButtonViewModel(DeclaredState.PlannedMaintenance, "Maintenance planifiée", "event_available", true),
            new DeclaredButtonViewModel(DeclaredState.CorrectiveMaintenance, "Maintenance corrective", "build", true),
            new DeclaredButtonViewModel(DeclaredState.Trials, "Essais", "science", true),
            new DeclaredButtonViewModel(DeclaredState.Cleaning, "Nettoyage", "cleaning_services", true),
        };
        foreach (var st in AppServices.Project.Project.Stations) Stations.Add(new ForceStationViewModel(st.Number, st.Name));
        for (int m = 1; m <= 6; m++) Movers.Add(new ForceMoverViewModel(m));
        OnSnapshots((_, s) => Refresh(s));
        Refresh(AppServices.Snapshots.Current);
    }

    public IReadOnlyList<DeclaredButtonViewModel> Buttons { get; }
    public ObservableCollection<ForceStationViewModel> Stations { get; } = new ObservableCollection<ForceStationViewModel>();
    public ObservableCollection<ForceMoverViewModel> Movers { get; } = new ObservableCollection<ForceMoverViewModel>();
    public ObservableCollection<ForceEntryRow> Entries { get; } = new ObservableCollection<ForceEntryRow>();

    [ObservableProperty] private bool _hasDeclared;
    [ObservableProperty] private string _declaredText = "Aucun état déclaré : la machine est en exploitation normale.";
    [ObservableProperty] private bool _startBlocked;
    [ObservableProperty] private string _entriesTitle = string.Empty;
    [ObservableProperty] private bool _hasEntries;

    private void Refresh(PlcSnapshot s)
    {
        var f = s.Forces;
        HasDeclared = f.Declared != DeclaredState.None;
        StartBlocked = f.StartBlocked;
        DeclaredText = HasDeclared
            ? UiText.Declared(f.Declared) + (f.StartBlocked ? " · démarrage bloqué" : "") + " · depuis " + DisplayFormat.ClockShort(f.DeclaredSince ?? DateTime.Now) + " (" + f.DeclaredBy + ")" + (f.DeclaredUntil.HasValue ? " · jusqu'à " + DisplayFormat.ClockShort(f.DeclaredUntil.Value) : "")
            : "Aucun état déclaré : la machine est en exploitation normale.";
        foreach (var b in Buttons) b.IsActive = b.State == f.Declared;
        foreach (var st in Stations)
        {
            var ss = s.Station(st.Number);
            if (ss == null) continue;
            st.State = ss.State;
            st.OutOfProduction = ss.OutOfProduction;
            var entry = f.Entries.FirstOrDefault(e => e.Kind == "Station" && e.Target == st.Name);
            st.Detail = ss.OutOfProduction ? "Hors production" + (entry != null ? " · " + entry.Reason + " · " + DisplayFormat.ClockShort(entry.Since) : "") : (ss.Groups.Count > 0 ? string.Join(" · ", ss.Groups) : string.Empty);
            st.ModeText = ss.OutOfProduction ? "Mode " + (ss.Mode == MachineMode.Maintenance ? "Maintenance" : "Production") : string.Empty;
        }
        foreach (var m in Movers)
        {
            var e = f.Entries.FirstOrDefault(x => x.Kind == "Mover" && x.Target == "Mover " + m.Number);
            m.OutOfService = e != null;
            m.Reason = e?.Reason;
        }
        var rows = f.Entries.Select(e => new ForceEntryRow(e.Target, e.Description, e.Reason, DisplayFormat.ClockShort(e.Since), e.By, e.Until.HasValue ? "jusqu'à " + DisplayFormat.ClockShort(e.Until.Value) : string.Empty, e.Kind)).ToList();
        if (!Entries.Select(r => r.Target + r.Description).SequenceEqual(rows.Select(r => r.Target + r.Description)))
        {
            Entries.Clear();
            foreach (var r in rows) Entries.Add(r);
        }
        HasEntries = Entries.Count > 0;
        EntriesTitle = "Forçages actifs (" + Entries.Count + ")";
    }

    [RelayCommand]
    private async Task SetDeclared(DeclaredButtonViewModel b)
    {
        if (b.IsActive) return;
        if (b.Blocks)
        {
            bool ok = await AppServices.Dialogs.ConfirmAsync("Déclarer « " + b.Label + " »", "Le démarrage de la machine sera bloqué tant que cet état est déclaré.", "Déclarer");
            if (!ok) return;
        }
        await AppServices.Commands.RunAsync("declared." + b.State, () => AppServices.Plc.SetDeclaredStateAsync(b.State, "Réglage", DateTime.Now.AddHours(2), CancellationToken.None), b.Label + " déclaré.");
    }

    [RelayCommand]
    private Task LiftDeclared() => AppServices.Commands.RunAsync("declared.lift", () => AppServices.Plc.SetDeclaredStateAsync(DeclaredState.None, null, null, CancellationToken.None), "État déclaré levé.");

    [RelayCommand]
    private Task ToggleStation(ForceStationViewModel st)
        => AppServices.Commands.RunAsync("out." + st.Number, () => AppServices.Plc.SetStationOutOfProductionAsync(st.Number, !st.OutOfProduction, "Réglage", CancellationToken.None),
            st.OutOfProduction ? st.Name + " remise en production." : st.Name + " hors production.");

    [RelayCommand]
    private Task ToggleMover(ForceMoverViewModel m)
        => AppServices.Commands.RunAsync("mover." + m.Number, () => AppServices.Plc.SetMoverOutOfServiceAsync(m.Number, !m.OutOfService, "Nid abîmé", CancellationToken.None),
            m.OutOfService ? "Mover " + m.Number + " remis en service." : "Mover " + m.Number + " hors service.");

    [RelayCommand]
    private Task LiftEntry(ForceEntryRow e)
    {
        if (e.Kind == "Machine") return LiftDeclared();
        if (e.Kind == "Station")
        {
            var st = Stations.FirstOrDefault(x => x.Name == e.Target);
            return st == null ? Task.CompletedTask : ToggleStation(st);
        }
        var mv = Movers.FirstOrDefault(x => "Mover " + x.Number == e.Target);
        return mv == null ? Task.CompletedTask : ToggleMover(mv);
    }
}

public sealed record ForceEntryRow(string Target, string Description, string Reason, string Since, string By, string Until, string Kind);

// ---------------------------------------------------------------------------------------------------- OF (docs/08 § 6.6)

public sealed partial class OrderRowViewModel : ObservableObject
{
    public OrderRowViewModel(OrderInfo o, bool current)
    {
        Info = o;
        IsCurrent = current;
        Number = o.Number;
        Reference = o.Reference;
        QuantityText = o.Good + " / " + o.Quantity;
        BadText = o.Bad.ToString();
        StateText = o.State == OrderState.Running ? "En cours" : o.State == OrderState.Created ? "Créé" : o.State == OrderState.Suspended ? "Suspendu" : o.State == OrderState.Finished ? "Terminé" : "Annulé";
        Tone = o.State == OrderState.Running ? Tone.Ok : o.State == OrderState.Created ? Tone.Neutral : o.State == OrderState.Suspended ? Tone.Warn : o.State == OrderState.Finished ? Tone.Accent : Tone.Neutral;
        CreatedText = o.Created.ToString("dd.MM HH:mm", DisplayFormat.Culture);
        EndText = o.EstimatedEnd.HasValue ? o.EstimatedEnd.Value.ToString("dd.MM HH:mm", DisplayFormat.Culture) : "—";
        CanLaunch = o.State == OrderState.Created;
    }

    public OrderInfo Info { get; }
    public bool IsCurrent { get; }
    public string Number { get; }
    public string Reference { get; }
    public string QuantityText { get; }
    public string BadText { get; }
    public string StateText { get; }
    public Tone Tone { get; }
    public string CreatedText { get; }
    public string EndText { get; }
    public bool CanLaunch { get; }
}

public sealed partial class OrdersViewModel : ViewModelBase
{
    public OrdersViewModel()
    {
        OnSnapshots((_, s) => Refresh(s));
        Refresh(AppServices.Snapshots.Current);
    }

    public ObservableCollection<OrderRowViewModel> Orders { get; } = new ObservableCollection<OrderRowViewModel>();

    [ObservableProperty] private bool _hasCurrent;
    [ObservableProperty] private string _currentTitle = string.Empty;
    [ObservableProperty] private string _currentReference = string.Empty;
    [ObservableProperty] private string _goodText = "0";
    [ObservableProperty] private string _badText = "0";
    [ObservableProperty] private string _activeText = "0";
    [ObservableProperty] private string _quantityText = string.Empty;
    [ObservableProperty] private double _good, _bad, _active;
    [ObservableProperty] private string _percentText = string.Empty;
    [ObservableProperty] private string _endText = string.Empty;
    [ObservableProperty] private string _budgetText = string.Empty;
    [ObservableProperty] private bool _canSuspend, _canResume;
    [ObservableProperty] private bool _canLaunch;
    [ObservableProperty] private string _launchHint = string.Empty;

    private void Refresh(PlcSnapshot s)
    {
        var cur = s.Orders.Current;
        HasCurrent = cur != null;
        if (cur != null)
        {
            CurrentTitle = "OF " + cur.Number;
            CurrentReference = cur.Reference;
            GoodText = cur.Good.ToString(); BadText = cur.Bad.ToString(); ActiveText = cur.InProcess.ToString();
            QuantityText = cur.Good + cur.Bad + " / " + cur.Quantity;
            Good = (double)cur.Good / cur.Quantity; Bad = (double)cur.Bad / cur.Quantity; Active = (double)cur.InProcess / cur.Quantity;
            PercentText = DisplayFormat.Number(100.0 * (cur.Good + cur.Bad) / cur.Quantity, 0) + " %";
            EndText = cur.EstimatedEnd.HasValue ? "Fin estimée " + cur.EstimatedEnd.Value.ToString("dd.MM HH:mm", DisplayFormat.Culture) : string.Empty;
            BudgetText = "Rebuts " + cur.Bad + " / " + cur.BudgetBad + " autorisés";
            CanSuspend = cur.State == OrderState.Running;
            CanResume = cur.State == OrderState.Suspended;
        }
        var mstate = s.Machine.State;
        CanLaunch = mstate == PackMlState.Stopped || mstate == PackMlState.Idle;
        LaunchHint = CanLaunch ? string.Empty : "Lancer : disponible machine Stopped ou Idle.";
        var rows = s.Orders.Orders.Select(o => new OrderRowViewModel(o, cur != null && o.Number == cur.Number)).ToList();
        // Les lignes sont reconstruites uniquement si leurs valeurs changent (les compteurs bougent à chaque pièce)
        if (!Orders.Select(r => r.Number + r.QuantityText + r.StateText).SequenceEqual(rows.Select(r => r.Number + r.QuantityText + r.StateText)))
        {
            Orders.Clear();
            foreach (var r in rows) Orders.Add(r);
        }
    }

    [RelayCommand] private Task Suspend() => AppServices.Commands.RunAsync("order.suspend", () => AppServices.Plc.OrderCommandAsync(OrderCommand.Suspend, CancellationToken.None), "OF suspendu.");
    [RelayCommand] private Task Resume() => AppServices.Commands.RunAsync("order.resume", () => AppServices.Plc.OrderCommandAsync(OrderCommand.Resume, CancellationToken.None), "OF repris.");

    [RelayCommand]
    private Task Launch(OrderRowViewModel row)
        => AppServices.Commands.RunAsync("order.launch." + row.Number, () => AppServices.Plc.StartOrderAsync(new OrderStart(row.Number, row.Reference, row.Info.Quantity), CancellationToken.None), "OF " + row.Number + " lancé.");

    [RelayCommand]
    private Task CancelOrder(OrderRowViewModel row)
        => AppServices.Commands.RunAsync("order.cancel." + row.Number, () => AppServices.Plc.OrderCommandAsync(OrderCommand.Cancel, CancellationToken.None), "OF annulé.");

    [RelayCommand]
    private void NewOrder()
    {
        var root = new StackPanel { Margin = new Thickness(24, 0, 24, 24) };
        var number = new TextField { Label = "Numéro d'OF", Text = "24-120", Margin = new Thickness(0, 0, 0, 12) };
        var reference = new TextField { Label = "Référence", Text = "Platine P12", Margin = new Thickness(0, 0, 0, 12) };
        var qty = new NumberField { Label = "Quantité", Value = 500, Minimum = 1, Maximum = 100000, Step = 10, Decimals = 0, Margin = new Thickness(0, 0, 0, 12) };
        root.Children.Add(number);
        root.Children.Add(reference);
        root.Children.Add(qty);
        root.Children.Add(new TextBlock { Text = "PARAMÈTRES D'OPÉRATION PROPOSÉS", Style = Tokens.Get<Style>("Type.Caps"), Margin = new Thickness(0, 10, 0, 8) });
        root.Children.Add(new TextBlock { Text = "Pressage pignon › Presser pignon : recette n° 3\nSertissage › Sertir : recette n° 5", Style = Tokens.Get<Style>("Type.Caption"), Margin = new Thickness(0, 0, 0, 16) });
        var create = new Button { Content = "Créer l'OF", HorizontalAlignment = HorizontalAlignment.Left };
        Ui.SetVariant(create, ButtonVariant.Primary);
        Ui.SetIcon(create, "add");
        create.Click += (_, _) => AppServices.Toast.Show("OF " + number.Text + " créé (maquette : non enregistré).", ToastKind.Success);
        root.Children.Add(create);
        AppServices.Drawer.Show("Nouvel OF", root);
    }
}

// ---------------------------------------------------------------------------------------------------- Pièces et movers (docs/08 § 6.5)

public sealed partial class PartMoverRowViewModel : ObservableObject
{
    public PartMoverRowViewModel(int number) { Number = number; }
    public int Number { get; }
    [ObservableProperty] private string _stateText = string.Empty;
    [ObservableProperty] private Tone _tone;
    [ObservableProperty] private string _where = string.Empty;
    [ObservableProperty] private string _partsText = string.Empty;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _toEmpty;
}

public sealed record GeneralStepRow(int Number, string Label, bool IsCurrent);

public sealed record TraceRow(string Station, string Operation, string Start, string Duration, string Verdict, Tone VerdictTone, string KeyData, bool HasCurve);

public sealed partial class PartsViewModel : ViewModelBase, INavigationAware
{
    private int _selected = 2;

    public PartsViewModel(int mover = 0)
    {
        for (int m = 1; m <= AppServices.Project.Project.Machine.Planar.Movers; m++) Movers.Add(new PartMoverRowViewModel(m));
        foreach (var op in AppServices.Project.Project.GeneralProcess.AllSteps().OfType<RouteStep>())
        {
            var g = AppServices.Project.Project.Group(op.GroupId);
            General.Add(new GeneralStepRow(General.Count + 1, (g?.Name ?? "?") + " › " + (g?.Operations.ElementAtOrDefault(op.OperationNo - 1)?.Name ?? ""), false));
        }
        var cur = AppServices.Snapshots.Current;
        _selected = mover > 0 ? mover : (cur.Movers.FirstOrDefault(m => m.State == MoverState.InStation && m.Parts.Count > 0) ?? cur.Movers.FirstOrDefault(m => m.Parts.Count > 0) ?? cur.Movers.First()).Number;
        OnSnapshots((_, s) => Refresh(s));
        Refresh(AppServices.Snapshots.Current);
    }

    public ObservableCollection<PartMoverRowViewModel> Movers { get; } = new ObservableCollection<PartMoverRowViewModel>();
    public ObservableCollection<PartMoverRowViewModel> ToEmptyMovers { get; } = new ObservableCollection<PartMoverRowViewModel>();
    public ObservableCollection<GeneralStepRow> General { get; } = new ObservableCollection<GeneralStepRow>();
    public ObservableCollection<TraceRow> History { get; } = new ObservableCollection<TraceRow>();
    public ObservableCollection<PartInfoRow> Carried { get; } = new ObservableCollection<PartInfoRow>();

    [ObservableProperty] private string _search = string.Empty;
    [ObservableProperty] private string _selectedTitle = string.Empty;
    [ObservableProperty] private string _selectedState = string.Empty;
    [ObservableProperty] private Tone _selectedTone;
    [ObservableProperty] private int _currentGeneralStep;
    [ObservableProperty] private bool _hasHistory;
    [ObservableProperty] private string _historyTitle = string.Empty;
    [ObservableProperty] private bool _hasToEmpty;
    [ObservableProperty] private string _searchHint = string.Empty;

    public void OnNavigatedTo(object? args) { }

    [RelayCommand]
    private void SelectMover(PartMoverRowViewModel m)
    {
        _selected = m.Number;
        Refresh(AppServices.Snapshots.Current);
    }

    [RelayCommand]
    private void RunSearch()
    {
        string q = (Search ?? string.Empty).Trim();
        if (q.Length == 0) return;
        var s = AppServices.Snapshots.Current;
        var found = s.Movers.FirstOrDefault(m => m.Parts.Any(p => p.Id.Equals(q, StringComparison.OrdinalIgnoreCase)));
        if (found != null) { _selected = found.Number; SearchHint = "Pièce " + q + " sur le mover " + found.Number + "."; Refresh(s); }
        else SearchHint = "Pièce " + q + " : historique archivé (démonstration : P-000638 est affichée comme exemple).";
        if (found == null) { History.Clear(); foreach (var h in BuildHistory(q)) History.Add(h); HasHistory = true; HistoryTitle = "Historique de la pièce " + q; }
    }

    private IEnumerable<TraceRow> BuildHistory(string partId)
        => DemoExtras.PartHistory(partId, DateTime.Now).Select(h => new TraceRow(h.Station, h.Operation, DisplayFormat.Clock(h.Start), DisplayFormat.Duration(h.DurationSeconds), UiText.Verdict(h.Verdict), UiText.VerdictTone(h.Verdict), h.KeyData, h.HasCurve));

    private void Refresh(PlcSnapshot s)
    {
        foreach (var row in Movers)
        {
            var m = s.Movers.FirstOrDefault(x => x.Number == row.Number);
            if (m == null) continue;
            row.StateText = UiText.MoverState(m.State);
            row.Tone = UiText.MoverTone(m.State);
            row.Where = m.StationNumber.HasValue ? AppServices.Project.Project.Station(m.StationNumber.Value)?.Name ?? "—" : m.State == MoverState.EnRoute ? "en route" : "—";
            row.PartsText = m.Parts.Count == 0 ? "vide" : string.Join(", ", m.Parts.Select(p => p.Id));
            row.ToEmpty = m.State == MoverState.ToEmpty;
            row.IsSelected = row.Number == _selected;
        }
        var empties = Movers.Where(r => r.ToEmpty).ToList();
        if (!ToEmptyMovers.SequenceEqual(empties)) { ToEmptyMovers.Clear(); foreach (var e in empties) ToEmptyMovers.Add(e); }
        HasToEmpty = ToEmptyMovers.Count > 0;

        var sel = s.Movers.FirstOrDefault(x => x.Number == _selected);
        if (sel == null) return;
        SelectedTitle = "Mover " + sel.Number;
        SelectedState = UiText.MoverState(sel.State) + (sel.StationNumber.HasValue ? " · " + AppServices.Project.Project.Station(sel.StationNumber.Value)?.Name : "");
        SelectedTone = UiText.MoverTone(sel.State);
        CurrentGeneralStep = sel.GeneralStep;
        for (int i = 0; i < General.Count; i++)
            if (General[i].IsCurrent != (General[i].Number == sel.GeneralStep)) General[i] = General[i] with { IsCurrent = General[i].Number == sel.GeneralStep };
        Carried.Clear();
        foreach (var p in sel.Parts) Carried.Add(new PartInfoRow("Emplacement " + p.Slot, p.Id, UiText.PartState(p.State), UiText.VerdictTone(p.Verdict)));
        if (sel.Parts.Count > 0 && SearchHint.StartsWith("Pièce") == false)
        {
            string id = sel.Parts[0].Id;
            if (HistoryTitle != "Historique de la pièce " + id)
            {
                History.Clear();
                foreach (var h in BuildHistory(id)) History.Add(h);
                HistoryTitle = "Historique de la pièce " + id;
            }
            HasHistory = true;
        }
        else if (sel.Parts.Count == 0 && !SearchHint.StartsWith("Pièce")) { History.Clear(); HasHistory = false; HistoryTitle = string.Empty; }
    }

    [RelayCommand]
    private Task ConfirmRemoved(PartMoverRowViewModel m)
        => AppServices.Commands.RunAsync("removed." + m.Number, () => AppServices.Plc.ConfirmPartsRemovedAsync(m.Number, CancellationToken.None), "Retrait confirmé : le mover " + m.Number + " repart.");

    [RelayCommand]
    private void ShowCurve(TraceRow row)
    {
        var curve = DemoExtras.PressCurve();
        double maxS = curve.Max(p => p.Stroke), maxF = 8;
        const double W = 520, H = 280, L = 44, B = 30;
        var canvas = new Canvas { Width = W + L + 10, Height = H + B + 10, Margin = new Thickness(16, 0, 16, 16) };
        // fenêtre de contrôle (segment 2 : 4,0 à 5,5 kN entre 8 et 11,5 mm)
        var win = new Rectangle { Width = (11.5 - 8) / maxS * W, Height = (5.5 - 4.0) / maxF * H, Fill = Tokens.Brush("Ok.Bg"), Stroke = Tokens.Brush("Ok.Dot"), StrokeThickness = 1 };
        Canvas.SetLeft(win, L + 8 / maxS * W); Canvas.SetTop(win, 10 + H - 5.5 / maxF * H);
        canvas.Children.Add(win);
        canvas.Children.Add(new Line { X1 = L, Y1 = 10, X2 = L, Y2 = 10 + H, Stroke = Tokens.Brush("Border.Strong"), StrokeThickness = 1 });
        canvas.Children.Add(new Line { X1 = L, Y1 = 10 + H, X2 = L + W, Y2 = 10 + H, Stroke = Tokens.Brush("Border.Strong"), StrokeThickness = 1 });
        var poly = new Polyline { Stroke = Tokens.Brush("Accent"), StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round };
        foreach (var (stroke, force) in curve) poly.Points.Add(new Point(L + stroke / maxS * W, 10 + H - force / maxF * H));
        canvas.Children.Add(poly);
        var xl = new TextBlock { Text = "Course (mm)", Style = Tokens.Get<Style>("Type.Caption") };
        Canvas.SetLeft(xl, L + W / 2 - 30); Canvas.SetTop(xl, 10 + H + 6);
        canvas.Children.Add(xl);
        var yl = new TextBlock { Text = "kN", Style = Tokens.Get<Style>("Type.Caption") };
        Canvas.SetLeft(yl, 8); Canvas.SetTop(yl, 6);
        canvas.Children.Add(yl);
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = row.Operation + " · " + row.KeyData, Style = Tokens.Get<Style>("Type.Caption"), Margin = new Thickness(24, 0, 24, 12), TextWrapping = TextWrapping.Wrap });
        root.Children.Add(canvas);
        AppServices.Drawer.Show("Courbe de presse", root);
    }
}

public sealed record PartInfoRow(string Slot, string Id, string State, Tone Tone);
