using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StdMachine.Core;
using StdMachine.Hmi.DragDrop;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Services;
using StdMachine.Hmi.Theme;
using StdMachine.Simulation.Demo;

namespace StdMachine.Hmi.ViewModels;

// ---------------------------------------------------------------------------------------------------- Studio : machine (docs/08 § 6.7)

public sealed record StationDrag(Guid StationId, string Name);

/// <summary>Cible de dépôt d'une carte de fonction : y glisser une station l'ajoute (une station peut figurer dans plusieurs fonctions).</summary>
public sealed class GroupDropTarget : IDropTarget
{
    private readonly GroupConfig _group;
    private readonly Action<GroupConfig, Guid> _add;

    public GroupDropTarget(GroupConfig group, Action<GroupConfig, Guid> add) { _group = group; _add = add; }

    public DropPreview CanAccept(object payload, Point point)
    {
        if (!(payload is StationDrag s)) return DropPreview.Reject("Seules les stations se déposent ici.");
        if (_group.StationIds.Contains(s.StationId)) return DropPreview.Reject("« " + s.Name + " » est déjà membre de « " + _group.Name + " ».");
        return DropPreview.Zone(s);
    }

    public void Drop(object payload, DropPreview preview)
    {
        if (payload is StationDrag s) _add(_group, s.StationId);
    }
}

public sealed record MemberChip(Guid StationId, string Name, bool ImplementsAll, GroupCardViewModel Owner);

public sealed partial class GroupCardViewModel : ObservableObject
{
    public GroupCardViewModel(GroupConfig group, Action<GroupConfig, Guid> add, Action<GroupCardViewModel, Guid> remove)
    {
        Group = group;
        DropTarget = new GroupDropTarget(group, add);
        _remove = remove;
        Name = group.Name;
        Role = group.Role == GroupRole.Entry ? "Entrée" : group.Role == GroupRole.Exit ? "Sortie" : group.Role == GroupRole.Reject ? "Rebut" : "Travail";
        Operations = string.Join(" · ", group.Operations.Select(o => o.Name + (o.Parameters.Count > 0 ? " (" + string.Join(", ", o.Parameters.Select(p => p.Name)) + ")" : "")));
        _priority = group.Priority;
        IsReject = group.Role == GroupRole.Reject;
        Refresh();
    }

    private readonly Action<GroupCardViewModel, Guid> _remove;
    public GroupConfig Group { get; }
    public IDropTarget DropTarget { get; }
    public string Name { get; }
    public string Role { get; }
    public string Operations { get; }
    public bool IsReject { get; }
    public ObservableCollection<MemberChip> Members { get; } = new ObservableCollection<MemberChip>();

    [ObservableProperty] private double _priority;

    partial void OnPriorityChanged(double value) => Group.Priority = (int)value;

    public void Refresh()
    {
        Members.Clear();
        foreach (var id in Group.StationIds)
        {
            var st = AppServices.Project.Project.StationById(id);
            if (st == null) continue;
            // Vert si la station implémente toutes les opérations de la fonction (une opération par process de type Operation qui l'implémente).
            int implemented = st.Processes.Count(p => p.Kind == ProcessKind.Operation && p.Implements.Any(i => i.GroupId == Group.Id));
            Members.Add(new MemberChip(id, st.Name, implemented >= Group.Operations.Count || Group.Operations.Count == 0, this));
        }
    }

    [RelayCommand] private void Remove(MemberChip chip) => _remove(this, chip.StationId);
}

public sealed record StationListItem(Guid Id, int Number, string Name, string Groups, StationDrag Drag);

public sealed record PartDataRow(string Name, string Type, string Unit, bool Locked);

public sealed record IoRow(string Kind, string Index, string Name, string Terminal, string UsedBy);

public sealed record QualityThresholdRow(string Scope, string Window, string Warning, string Action, string Reaction, bool Inherited);

public sealed partial class MachineConfigViewModel : ViewModelBase
{
    private readonly MachineProject _project = AppServices.Project.Project;
    private readonly List<IoRow> _io = new List<IoRow>();

    public MachineConfigViewModel()
    {
        var planar = _project.Machine.Planar;
        PlanWidth = planar.WidthMm; PlanHeight = planar.HeightMm;
        for (int c = 0; c < planar.Columns; c++)
            for (int r = 0; r < planar.Rows; r++)
                Tiles.Add(new PlanMarker(c * planar.TileMm + 3, r * planar.TileMm + 3, planar.TileMm - 6, planar.TileMm - 6, "Tile", string.Empty));
        foreach (var pp in _project.Machine.PlanarPositions)
        {
            switch (pp.Kind)
            {
                case PlanarPositionKind.Work: Markers.Add(new PlanMarker(pp.X - 18, pp.Y - 18, 36, 36, "Work", pp.Name)); break;
                case PlanarPositionKind.Queue: Markers.Add(new PlanMarker(pp.X - 24, pp.Y - 18, 48, 36, "Queue", pp.Name)); break;
                case PlanarPositionKind.Buffer: Markers.Add(new PlanMarker(pp.X - 56, pp.Y - 56, 112, 112, "Buffer", pp.Name)); break;
                default: Markers.Add(new PlanMarker(pp.X - 56, pp.Y - 56, 112, 112, "Park", pp.Name)); break;
            }
        }
        foreach (var st in _project.Stations)
            Stations.Add(new StationListItem(st.Id, st.Number, st.Name, string.Join(" · ", _project.GroupsOf(st).Select(g => g.Name)), new StationDrag(st.Id, st.Name)));
        foreach (var g in _project.Machine.Groups) Groups.Add(new GroupCardViewModel(g, AddMember, RemoveMember));
        foreach (var d in _project.Machine.PartData) PartData.Add(new PartDataRow(d.Name, d.Type, d.Unit ?? "", d.Reserved));
        foreach (var l in DemoExtras.IoImage()) _io.Add(new IoRow(l.Kind, l.Index.ToString(), l.Name, l.Terminal, l.UsedBy));
        RefreshIo();
        Thresholds.Add(new QualityThresholdRow("Machine", "200", "3 %", "5 %", "Avertissement", false));
        Thresholds.Add(new QualityThresholdRow("Station", "50", "5 %", "10 %", "Arrêt qualité", false));
        Thresholds.Add(new QualityThresholdRow("Pressage 02", "50", "5 %", "8 %", "Arrêt qualité", false));
        Thresholds.Add(new QualityThresholdRow("Fonction", "100", "5 %", "10 %", "Avertissement", false));
        Thresholds.Add(new QualityThresholdRow("Mover", "20", "10 %", "20 %", "Mover hors service", false));
        Thresholds.Add(new QualityThresholdRow("Successifs", "—", "2", "3", "Arrêt qualité", false));
        NoStationDelay = _project.Machine.NoStationDelaySeconds;
        AppServices.Project.Undo.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo)); };
        ErrorSummary = UiText.Plural(AppServices.Project.ErrorCount, "erreur", "erreurs") + " · " + UiText.Plural(AppServices.Project.WarningCount, "avertissement", "avertissements");
    }

    public double PlanWidth { get; }
    public double PlanHeight { get; }
    public ObservableCollection<PlanMarker> Tiles { get; } = new ObservableCollection<PlanMarker>();
    public ObservableCollection<PlanMarker> Markers { get; } = new ObservableCollection<PlanMarker>();
    public ObservableCollection<StationListItem> Stations { get; } = new ObservableCollection<StationListItem>();
    public ObservableCollection<GroupCardViewModel> Groups { get; } = new ObservableCollection<GroupCardViewModel>();
    public ObservableCollection<PartDataRow> PartData { get; } = new ObservableCollection<PartDataRow>();
    public ObservableCollection<IoRow> IoRows { get; } = new ObservableCollection<IoRow>();
    public ObservableCollection<QualityThresholdRow> Thresholds { get; } = new ObservableCollection<QualityThresholdRow>();
    public IReadOnlyList<string> IoKinds { get; } = new[] { "Tous", "Entrée", "Sortie", "Analogique" };

    public bool CanUndo => AppServices.Project.Undo.CanUndo;
    public bool CanRedo => AppServices.Project.Undo.CanRedo;
    public string ErrorSummary { get; }
    public int MoverCount => _project.Machine.Planar.Movers;
    public string PlanSize => DisplayFormat.Number(PlanWidth, 0) + " × " + DisplayFormat.Number(PlanHeight, 0) + " mm";

    [ObservableProperty] private int _tabIndex;
    [ObservableProperty] private string _ioKind = "Tous";
    [ObservableProperty] private string _ioQuery = string.Empty;
    [ObservableProperty] private double _noStationDelay;
    [ObservableProperty] private bool _matrixView;

    partial void OnIoKindChanged(string value) => RefreshIo();
    partial void OnIoQueryChanged(string value) => RefreshIo();
    partial void OnNoStationDelayChanged(double value) { if (_project.Machine.NoStationDelaySeconds != (int)value) { _project.Machine.NoStationDelaySeconds = (int)value; AppServices.Project.NotifyEdited(); } }

    private void RefreshIo()
    {
        IoRows.Clear();
        string q = (IoQuery ?? "").Trim().ToLowerInvariant();
        foreach (var r in _io.Where(r => (IoKind == "Tous" || r.Kind == IoKind) && (q.Length == 0 || (r.Name + r.UsedBy + r.Terminal).ToLowerInvariant().Contains(q)))) IoRows.Add(r);
    }

    private void AddMember(GroupConfig group, Guid stationId)
    {
        var st = _project.StationById(stationId);
        AppServices.Project.Undo.Push(new DelegateEdit("Ajouter « " + st?.Name + " » à « " + group.Name + " »",
            () => { group.StationIds.Remove(stationId); RefreshAll(); },
            () => { if (!group.StationIds.Contains(stationId)) group.StationIds.Add(stationId); RefreshAll(); }));
        group.StationIds.Add(stationId);
        RefreshAll();
        AppServices.Toast.Show("« " + st?.Name + " » ajoutée à « " + group.Name + " ».", ToastKind.Success);
    }

    private void RemoveMember(GroupCardViewModel card, Guid stationId)
    {
        var group = card.Group;
        int index = group.StationIds.IndexOf(stationId);
        if (index < 0) return;
        if (group.StationIds.Count == 1) { AppServices.Toast.Show("Une fonction garde au moins une station (sinon elle n'est plus réalisable).", ToastKind.Warning); return; }
        AppServices.Project.Undo.Push(new DelegateEdit("Retirer une station de « " + group.Name + " »",
            () => { if (!group.StationIds.Contains(stationId)) group.StationIds.Insert(Math.Min(index, group.StationIds.Count), stationId); RefreshAll(); },
            () => { group.StationIds.Remove(stationId); RefreshAll(); }));
        group.StationIds.Remove(stationId);
        RefreshAll();
    }

    private void RefreshAll()
    {
        foreach (var g in Groups) g.Refresh();
        for (int i = 0; i < Stations.Count; i++)
        {
            var s = Stations[i];
            var st = _project.StationById(s.Id);
            if (st != null) Stations[i] = s with { Groups = string.Join(" · ", _project.GroupsOf(st).Select(g => g.Name)) };
        }
        AppServices.Project.NotifyEdited();
    }

    [RelayCommand] private void Undo() => AppServices.Project.Undo.Undo();
    [RelayCommand] private void Redo() => AppServices.Project.Undo.Redo();
    [RelayCommand] private void Deploy() => AppServices.Navigation.Navigate("Deploy", null, pushHistory: true);
    [RelayCommand] private void ImportCsv() => AppServices.Toast.Show("Import du CSV TwinCAT : disponible en phase 2 (modèle de projet complet).", ToastKind.Info);
    [RelayCommand] private void NewStation() => AppServices.Toast.Show("Nouvelle station : glisser depuis la palette sur le plan (phase 2).", ToastKind.Info);
    [RelayCommand] private void SetTab(int i) => TabIndex = i;
}

// ---------------------------------------------------------------------------------------------------- Studio : station (docs/08 § 6.8)

public sealed record LibraryDrag(TypeDef Type);

/// <summary>Dépôt d'un type de la bibliothèque sur la vue de dessus : crée un objet de premier niveau à l'endroit du dépôt.</summary>
public sealed class TopViewDropTarget : IDropTarget
{
    private readonly Action<TypeDef, Point> _add;
    public TopViewDropTarget(Action<TypeDef, Point> add) { _add = add; }

    public DropPreview CanAccept(object payload, Point point)
    {
        if (!(payload is LibraryDrag d)) return DropPreview.Reject("Seuls les types de la bibliothèque se déposent ici.");
        if (d.Type.Category == "Machine") return DropPreview.Reject("« " + d.Type.Name + " » est un objet machine : il se déclare dans Studio machine.");
        if (d.Type.Code == "PlanarMover" || d.Type.Code == "PlanarSystem") return DropPreview.Reject("Le système planaire se déclare dans Studio machine.");
        return DropPreview.Zone(point);
    }

    public void Drop(object payload, DropPreview preview)
    {
        if (payload is LibraryDrag d && preview.Tag is Point p) _add(d.Type, p);
    }
}

public sealed partial class TreeNodeViewModel : ObservableObject
{
    public TreeNodeViewModel(ObjectNode? node, string name, string icon, int depth, string? detail = null)
    {
        Node = node; Name = name; Icon = icon; Indent = new Thickness(depth * 18, 0, 0, 0); Detail = detail;
    }

    public ObjectNode? Node { get; }
    public string Name { get; }
    public string Icon { get; }
    public Thickness Indent { get; }
    public string? Detail { get; }
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private Tone _tone = Tone.Ok;
}

public sealed partial class TopCardViewModel : ObservableObject
{
    public TopCardViewModel(ObjectNode node, TypeDef? type)
    {
        Node = node;
        Name = node.Name;
        Icon = type?.Icon ?? "folder";
        TypeName = type?.Name ?? node.TypeCode;
        IsModule = type?.IsModule ?? node.Children.Count > 0;
        _x = node.LayoutX; _y = node.LayoutY;
        Children = node.Children.Select(c => new TopChildViewModel(c.Name, DemoCatalog.Get(c.TypeCode)?.Icon ?? "circle")).ToList();
        Width = IsModule ? 330 : 220;
        Height = IsModule ? 90 + 38 * Math.Max(1, (Children.Count + 1) / 2) : 96;
    }

    public ObjectNode Node { get; }
    public string Name { get; }
    public string Icon { get; }
    public string TypeName { get; }
    public bool IsModule { get; }
    public double Width { get; }
    public double Height { get; }
    public List<TopChildViewModel> Children { get; }
    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isSimulated;
}

public sealed record TopChildViewModel(string Name, string Icon);

public sealed record LibraryItem(TypeDef Type, string Name, string Icon, string Category, string Description, LibraryDrag Drag);

public sealed record PositionTableRow(string Name, string X, string Y, string Z, string C, string Approach, bool Taught, string State);

public sealed record SpeedRow(string Axis, string[] Values, string Detail);

public sealed record LockRow(string Object, string Actions, string Condition, bool Generated);

public sealed record CapabilityRow(string Object, string Items);

public sealed record ParamRow(string Name, string Value);

public sealed partial class StationConfigViewModel : ViewModelBase
{
    private readonly MachineProject _project = AppServices.Project.Project;
    private readonly StationConfig _station;
    private readonly List<LibraryItem> _library = new List<LibraryItem>();

    public StationConfigViewModel(int number, string? tab = null)
    {
        _station = _project.Station(number) ?? _project.Stations[0];
        AppServices.Selection.Station = _station.Number;
        foreach (var s in _project.Stations)
            Chips.Add(new StationChipViewModel(s.Number, s.Name, n => AppServices.Navigation.Navigate("StationConfig:" + n)) { IsSelected = s.Number == _station.Number });
        foreach (var t in DemoCatalog.Types.Where(t => t.Category != "Machine" && t.Code != "PlanarMover" && t.Code != "PlanarSystem" && t.Code != "Group"))
            _library.Add(new LibraryItem(t, t.Name, t.Icon, t.Category, (t.Actions.Count + t.Functions.Count) + " action" + ((t.Actions.Count + t.Functions.Count) > 1 ? "s" : ""), new LibraryDrag(t)));
        DropHandler = new TopViewDropTarget(AddObject);
        BuildAll();
        AppServices.Project.Undo.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo)); };
        if (tab == "fn") BottomTab = 6;
    }

    public string StationName => _station.Name;
    public IDropTarget DropHandler { get; }
    public ObservableCollection<StationChipViewModel> Chips { get; } = new ObservableCollection<StationChipViewModel>();
    public ObservableCollection<TreeNodeViewModel> Tree { get; } = new ObservableCollection<TreeNodeViewModel>();
    public ObservableCollection<TopCardViewModel> Cards { get; } = new ObservableCollection<TopCardViewModel>();
    public ObservableCollection<PositionTableRow> Positions { get; } = new ObservableCollection<PositionTableRow>();
    public ObservableCollection<StationVariable> Variables { get; } = new ObservableCollection<StationVariable>();
    public ObservableCollection<SpeedRow> Speeds { get; } = new ObservableCollection<SpeedRow>();
    public ObservableCollection<LockRow> Locks { get; } = new ObservableCollection<LockRow>();
    public ObservableCollection<CapabilityRow> Capabilities { get; } = new ObservableCollection<CapabilityRow>();
    public ObservableCollection<ModuleFnRow> ModuleFns { get; } = new ObservableCollection<ModuleFnRow>();
    public ObservableCollection<CustomFnRow> CustomFns { get; } = new ObservableCollection<CustomFnRow>();
    public ObservableCollection<ParamRow> Params { get; } = new ObservableCollection<ParamRow>();
    public ObservableCollection<LibraryGroup> Library { get; } = new ObservableCollection<LibraryGroup>();
    public ObservableCollection<IssueRow> Issues { get; } = new ObservableCollection<IssueRow>();
    public ObservableCollection<PressRecipe> Recipes { get; } = new ObservableCollection<PressRecipe>();
    public bool HasPress => _station.AllObjects().Any(o => o.TypeCode == "Press");
    public bool CanUndo => AppServices.Project.Undo.CanUndo;
    public bool CanRedo => AppServices.Project.Undo.CanRedo;
    public string WorkArea => "zone de travail 320 × 180 mm";

    [ObservableProperty] private int _bottomTab;
    [ObservableProperty] private int _rightTab;
    [ObservableProperty] private string _libraryQuery = string.Empty;
    [ObservableProperty] private string _summary = string.Empty;
    [ObservableProperty] private string _selectedName = string.Empty;
    [ObservableProperty] private string _selectedType = string.Empty;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private bool _hasFunctions;
    [ObservableProperty] private bool _simulated;
    [ObservableProperty] private bool _disabled;
    [ObservableProperty] private double _overridePercent = 100;
    [ObservableProperty] private string _actionsCount = string.Empty;
    [ObservableProperty] private string _processCount = string.Empty;
    [ObservableProperty] private string _variablesCount = string.Empty;
    private readonly Dictionary<Guid, Point> _dropped = new Dictionary<Guid, Point>();
    private ObjectNode? _selectedNode;
    private bool _syncing;

    partial void OnLibraryQueryChanged(string value) => BuildLibrary();

    partial void OnSimulatedChanged(bool value)
    {
        if (_syncing || _selectedNode == null) return;
        var n = _selectedNode; bool old = n.Simulated;
        AppServices.Project.Undo.Push(new DelegateEdit("Simulation de « " + n.Name + " »", () => { n.Simulated = old; Rebuild(); }, () => { n.Simulated = value; Rebuild(); }));
        n.Simulated = value; Rebuild();
    }

    partial void OnDisabledChanged(bool value)
    {
        if (_syncing || _selectedNode == null) return;
        var n = _selectedNode; bool old = n.Disabled;
        AppServices.Project.Undo.Push(new DelegateEdit("Désactiver « " + n.Name + " »", () => { n.Disabled = old; Rebuild(); }, () => { n.Disabled = value; Rebuild(); }));
        n.Disabled = value; Rebuild();
    }

    partial void OnOverridePercentChanged(double value)
    {
        if (_syncing || _selectedNode == null) return;
        _selectedNode.OverridePercent = value;
        AppServices.Project.NotifyEdited();
    }

    private void Rebuild()
    {
        var keep = _selectedNode;
        BuildAll();
        if (keep != null) SelectNode(keep);
        AppServices.Project.NotifyEdited();
    }

    private void BuildAll()
    {
        Tree.Clear(); Cards.Clear(); Positions.Clear(); Variables.Clear(); Speeds.Clear(); Locks.Clear(); Capabilities.Clear(); Recipes.Clear();
        void Walk(ObjectNode n, int depth)
        {
            var t = DemoCatalog.Get(n.TypeCode);
            Tree.Add(new TreeNodeViewModel(n, n.Name, t?.Icon ?? "folder", depth) { Tone = n.Disabled ? Tone.Neutral : n.Simulated ? Tone.Sim : Tone.Ok });
            foreach (var c in n.Children) Walk(c, depth + 1);
        }
        foreach (var o in _station.Objects) Walk(o, 0);
        Tree.Add(new TreeNodeViewModel(null, "Variables (" + _station.Variables.Count + ")", "tune", 0));
        Tree.Add(new TreeNodeViewModel(null, "Fonctions personnalisées (" + _station.CustomFunctions.Count + ") →", "edit", 0));
        Tree.Add(new TreeNodeViewModel(null, "Process (" + _station.Processes.Count + ") →", "account_tree", 0));
        double colX = 40, rowY = 24, nx = 400, ny = 24; int ncol = 0;
        foreach (var o in _station.Objects)
        {
            var card = new TopCardViewModel(o, DemoCatalog.Get(o.TypeCode)) { IsSimulated = o.Simulated };
            if (_dropped.ContainsKey(o.Id)) { card.X = _dropped[o.Id].X; card.Y = _dropped[o.Id].Y; }
            else if (card.IsModule) { card.X = colX; card.Y = rowY; rowY += card.Height + 24; }
            else { card.X = nx + ncol * 250; card.Y = ny; if (++ncol == 2) { ncol = 0; ny += 130; } }
            Cards.Add(card);
        }
        foreach (var p in _station.Positions)
            Positions.Add(new PositionTableRow(p.Name, p.Taught ? DisplayFormat.Number(p.X, 2) : "—", p.Taught ? DisplayFormat.Number(p.Y, 2) : "—", p.Taught ? DisplayFormat.Number(p.Z, 2) : "—", p.Taught ? DisplayFormat.Number(p.C, 2) : "—",
                p.Approach.HasValue ? DisplayFormat.Number(p.Approach.Value, 1) : "—", p.Taught, p.Taught ? "Apprise" : "Non apprise"));
        foreach (var v in _station.Variables) Variables.Add(v);
        var axes = _station.AllObjects().Where(o => o.TypeCode == "LinearAxis" || o.TypeCode == "RotaryAxis").ToList();
        double[] pct = { 10, 80, 30, 50, 100, 20, 60, 5 };
        foreach (var a in axes)
        {
            double vmax = a.TypeCode == "RotaryAxis" ? 720 : 600;
            Speeds.Add(new SpeedRow(a.Name, pct.Select(p => DisplayFormat.Number(p, 0)).ToArray(), "V2 = " + DisplayFormat.Number(vmax * 0.8, 0) + (a.TypeCode == "RotaryAxis" ? " °/s" : " mm/s")));
        }
        Locks.Add(new LockRow("Robot XYZC", "Move absolu, Aller à", "Aspiration au repos", true));
        Locks.Add(new LockRow("Presse", "Exécuter recette", "Présence nid presse", false));
        Locks.Add(new LockRow("Axe Z", "Tous les mouvements XY", "Z en position de sécurité", true));
        foreach (var o in _station.Objects)
        {
            var t = DemoCatalog.Get(o.TypeCode);
            if (t == null) continue;
            Capabilities.Add(new CapabilityRow(o.Name, string.Join(" · ", t.Functions.Select(f => f.Name).Concat(t.Actions.Select(a => a.Name)))));
            foreach (var c in o.Children)
            {
                var ct = DemoCatalog.Get(c.TypeCode);
                if (ct != null && ct.Actions.Count > 0 && c.TypeCode != "LinearAxis" && c.TypeCode != "RotaryAxis") Capabilities.Add(new CapabilityRow(o.Name + " › " + c.Name, string.Join(" · ", ct.Actions.Select(a => a.Name))));
            }
        }
        foreach (var r in DemoExtras.PressRecipes()) Recipes.Add(r);
        ModuleFns.Clear(); CustomFns.Clear();
        foreach (var o in _station.Objects)
            foreach (var fd in DemoCatalog.Get(o.TypeCode)?.Functions ?? new FunctionDef[0])
                ModuleFns.Add(new ModuleFnRow(o, fd, fd.Name, o.Name, string.Join(", ", fd.Params.Select(q => q.Name)), "≈ " + DisplayFormat.Number(fd.DurationSeconds, 1) + " s"));
        foreach (var cf in _station.CustomFunctions)
            CustomFns.Add(new CustomFnRow(cf, cf.Name, _station.FindObject(cf.ModuleObjectId)?.Name ?? "?", cf.BasedOn != null ? "copie de « " + cf.BasedOn + " »" : "créée de zéro",
                cf.Body.Steps.Count + (cf.Body.Steps.Count > 1 ? " actions" : " action"), cf.Params.Count == 0 ? "sans paramètre" : string.Join(", ", cf.Params.Select(q => q.Name)), "≈ " + DisplayFormat.Number(DemoFunctions.FromCustom(cf, _station).DurationSeconds, 1) + " s"));
        BuildLibrary();
        ActionsCount = Capabilities.Count + " éléments";
        Issues.Clear();
        foreach (var i in AppServices.Project.Issues.Where(i => i.Station == _station.Number).Take(5)) Issues.Add(new IssueRow(i.Code, i.Message, i.Severity, i.StepId));
        var e = AppServices.Project.Issues.Count(i => i.Station == _station.Number && i.Severity == ValidationSeverity.Error);
        var w = AppServices.Project.Issues.Count(i => i.Station == _station.Number && i.Severity == ValidationSeverity.Warning);
        Summary = UiText.Plural(e, "erreur", "erreurs") + " · " + UiText.Plural(w, "avertissement", "avertissements");
        if (_selectedNode == null) SelectNode(_station.Objects.FirstOrDefault(o => o.TypeCode == "CartesianRobot") ?? _station.Objects[0]);
    }

    private void BuildLibrary()
    {
        Library.Clear();
        string q = (LibraryQuery ?? "").Trim().ToLowerInvariant();
        foreach (var grp in _library.Where(l => q.Length == 0 || l.Name.ToLowerInvariant().Contains(q)).GroupBy(l => l.Category).OrderBy(g => g.Key == "Modules" ? 0 : 1).ThenBy(g => g.Key))
            Library.Add(new LibraryGroup(grp.Key, grp.ToList()));
    }

    [RelayCommand]
    private void Select(TreeNodeViewModel node)
    {
        if (node.Node == null)
        {
            if (node.Name.StartsWith("Process")) AppServices.Navigation.Navigate("ProcessEditor:" + _station.Number + ":presser");
            else if (node.Name.StartsWith("Fonctions")) BottomTab = 6;
            else BottomTab = 1;
            return;
        }
        SelectNode(node.Node);
    }

    [RelayCommand]
    private void SelectCard(TopCardViewModel card) => SelectNode(card.Node);

    private void SelectNode(ObjectNode node)
    {
        _selectedNode = node;
        foreach (var t in Tree) t.IsSelected = t.Node == node;
        foreach (var c in Cards) c.IsSelected = c.Node == node;
        _syncing = true;
        SelectedName = node.Name;
        SelectedType = DemoCatalog.Get(node.TypeCode)?.Name ?? node.TypeCode;
        HasSelection = true;
        HasFunctions = (DemoCatalog.Get(node.TypeCode)?.Functions.Count ?? 0) > 0;
        Simulated = node.Simulated;
        Disabled = node.Disabled;
        OverridePercent = node.OverridePercent;
        Params.Clear();
        Params.Add(new ParamRow("Rôle d'enfant", node.ChildRole ?? "—"));
        foreach (var p in node.Params) Params.Add(new ParamRow(p.Name, p.Value + (p.Unit != null ? " " + p.Unit : "")));
        if (node.Params.Count == 0)
        {
            Params.Add(new ParamRow("Raccordement", "Voir l'image E/S"));
            Params.Add(new ParamRow("Timeout par défaut", "5,0 s"));
        }
        _syncing = false;
    }

    private void AddObject(TypeDef type, Point at)
    {
        var node = new ObjectNode { TypeCode = type.Code, Name = type.Name + " " + (_station.AllObjects().Count(o => o.TypeCode == type.Code) + 1) };
        _dropped[node.Id] = new Point(Math.Max(0, at.X - 100), Math.Max(0, at.Y - 40));
        AppServices.Project.Undo.Push(new DelegateEdit("Ajouter « " + node.Name + " »", () => { _station.Objects.Remove(node); if (_selectedNode == node) _selectedNode = null; Rebuild(); }, () => { _station.Objects.Add(node); Rebuild(); }));
        _station.Objects.Add(node);
        _selectedNode = node;
        Rebuild();
        AppServices.Toast.Show("« " + node.Name + " » ajouté à la station.", ToastKind.Success);
    }

    [RelayCommand]
    private void AddPosition()
    {
        var pos = new NamedPosition { Name = "Position " + (_station.Positions.Count + 1) };
        AppServices.Project.Undo.Push(new DelegateEdit("Ajouter une position", () => { _station.Positions.Remove(pos); Rebuild(); }, () => { _station.Positions.Add(pos); Rebuild(); }));
        _station.Positions.Add(pos);
        Rebuild();
    }

    [RelayCommand] private void Undo() => AppServices.Project.Undo.Undo();
    [RelayCommand] private void Redo() => AppServices.Project.Undo.Redo();
    [RelayCommand] private void Deploy() => AppServices.Navigation.Navigate("Deploy", null, pushHistory: true);
    [RelayCommand] private void ShowFunctions() => BottomTab = 6;
    [RelayCommand] private void GoProcess() => AppServices.Navigation.Navigate("ProcessEditor:" + _station.Number + ":presser");

    // ---- fonctions personnalisées (docs/02 § 3) : copie d'une fonction de module ou création de zéro, puis éditeur dédié
    [RelayCommand]
    private void CopyFunction(ModuleFnRow row)
    {
        var fn = DemoFunctions.CopyOf(_station, row.Module, row.Def);
        EnsureUniqueName(fn);
        AddFunction(fn);
    }

    [RelayCommand]
    private void NewFunction()
    {
        var module = _selectedNode != null && (DemoCatalog.Get(_selectedNode.TypeCode)?.Functions.Count ?? 0) > 0 ? _selectedNode : _station.Objects.FirstOrDefault(o => (DemoCatalog.Get(o.TypeCode)?.Functions.Count ?? 0) > 0) ?? _station.Objects[0];
        AddFunction(DemoFunctions.NewEmpty(_station, module));
    }

    private void EnsureUniqueName(CustomFunction fn)
    {
        string baseName = fn.Name; int n = 1;
        while (_station.CustomFunctions.Any(f => f.Name == fn.Name)) fn.Name = baseName.Replace(" (copie)", "") + " (copie " + (++n) + ")";
    }

    private void AddFunction(CustomFunction fn)
    {
        AppServices.Project.Undo.Push(new DelegateEdit("Ajouter la fonction « " + fn.Name + " »", () => { _station.CustomFunctions.Remove(fn); Rebuild(); }, () => { _station.CustomFunctions.Add(fn); Rebuild(); }));
        _station.CustomFunctions.Add(fn);
        Rebuild();
        AppServices.Navigation.Navigate("FunctionEditor:" + _station.Number + ":" + fn.Id.ToString("N"));
    }

    [RelayCommand]
    private void OpenFunction(CustomFnRow row) => AppServices.Navigation.Navigate("FunctionEditor:" + _station.Number + ":" + row.Fn.Id.ToString("N"));

    [RelayCommand]
    private void DeleteFunction(CustomFnRow row)
    {
        var fn = row.Fn;
        var user = _station.Processes.FirstOrDefault(p => p.AllSteps().OfType<FunctionStep>().Any(s => s.FunctionId == fn.FunctionId));
        if (user != null) { AppServices.Toast.Show("« " + fn.Name + " » est utilisée par le process « " + user.Name + " » : retirez-la du process d'abord.", ToastKind.Warning); return; }
        int idx = _station.CustomFunctions.IndexOf(fn);
        AppServices.Project.Undo.Push(new DelegateEdit("Supprimer la fonction « " + fn.Name + " »", () => { _station.CustomFunctions.Insert(Math.Min(idx, _station.CustomFunctions.Count), fn); Rebuild(); }, () => { _station.CustomFunctions.Remove(fn); Rebuild(); }));
        _station.CustomFunctions.Remove(fn);
        Rebuild();
        AppServices.Toast.Show("Fonction « " + fn.Name + " » supprimée.", ToastKind.Info);
    }
}

public sealed record LibraryGroup(string Name, IReadOnlyList<LibraryItem> Items);

public sealed record ModuleFnRow(ObjectNode Module, FunctionDef Def, string Name, string ModuleName, string ParamsText, string Duration);

public sealed record CustomFnRow(CustomFunction Fn, string Name, string ModuleName, string BasedOn, string StepsText, string ParamsText, string Duration);
