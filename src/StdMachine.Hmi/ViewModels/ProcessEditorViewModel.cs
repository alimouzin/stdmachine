using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StdMachine.Core;
using StdMachine.Hmi.Controls;
using StdMachine.Hmi.DragDrop;
using StdMachine.Hmi.Editor;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Services;
using StdMachine.Hmi.Theme;
using StdMachine.Simulation.Demo;

namespace StdMachine.Hmi.ViewModels;

public sealed partial class PaletteItemViewModel : ObservableObject
{
    public PaletteItemViewModel(string label, string icon, PaletteDrag drag, string? reason = null)
    {
        Label = label; Icon = icon; Drag = drag; Reason = reason;
    }

    public string Label { get; }
    public string Icon { get; }
    public PaletteDrag Drag { get; }
    public string? Reason { get; }
    public bool IsEnabled => Reason == null;
    /// <summary>Un élément indisponible reste visible, atténué, avec sa raison (docs/08 § 6.9).</summary>
    public double ItemOpacity => IsEnabled ? 1.0 : 0.4;
    public object? DragSource => IsEnabled ? Drag : null;
}

public sealed class PaletteGroupViewModel
{
    public PaletteGroupViewModel(string name, string icon) { Name = name; Icon = icon; }
    public string Name { get; }
    public string Icon { get; }
    public ObservableCollection<PaletteItemViewModel> Items { get; } = new ObservableCollection<PaletteItemViewModel>();
}

public sealed partial class ParamRowViewModel : ObservableObject
{
    public ParamRowViewModel(string label, string value, bool isError, Func<FrameworkElement, Task> edit)
    {
        Label = label; Value = value; IsError = isError; _edit = edit;
    }

    private readonly Func<FrameworkElement, Task> _edit;
    public string Label { get; }
    public string Value { get; }
    public bool IsError { get; }

    [RelayCommand] private Task Edit(FrameworkElement anchor) => _edit(anchor);
}

/// <summary>Une variable de process dans l'onglet Données : renommer, rendre partagée (variable de station) ou supprimer.</summary>
public sealed class VariableRowViewModel
{
    public VariableRowViewModel(StationVariable variable, Func<FrameworkElement, Task> rename, Func<FrameworkElement, Task> share, Func<FrameworkElement, Task> delete)
    {
        Name = variable.Name;
        TypeText = ProcessEditorViewModel.TypeText(variable.Type);
        RenameCommand = new AsyncRelayCommand<FrameworkElement>(a => rename(a!));
        ShareCommand = new AsyncRelayCommand<FrameworkElement>(a => share(a!));
        DeleteCommand = new AsyncRelayCommand<FrameworkElement>(a => delete(a!));
    }

    public string Name { get; }
    public string TypeText { get; }
    public IAsyncRelayCommand<FrameworkElement> RenameCommand { get; }
    public IAsyncRelayCommand<FrameworkElement> ShareCommand { get; }
    public IAsyncRelayCommand<FrameworkElement> DeleteCommand { get; }
}

public sealed record ProcessTabModel(string Name, string Key, bool IsSelected);

public sealed record IssueRow(string Code, string Message, ValidationSeverity Severity, Guid? StepId);

/// <summary>Éditeur de process (docs/08 § 6.9) : palette, canevas à glisser-déposer, panneau Étape, annuler / refaire.</summary>
public sealed partial class ProcessEditorViewModel : ViewModelBase
{
    private readonly MachineProject _project = AppServices.Project.Project;
    private readonly UndoService _undo = AppServices.Project.Undo;
    private List<PaletteGroupViewModel> _allGroups = new List<PaletteGroupViewModel>();

    public ProcessEditorViewModel(int station, string processKey)
    {
        Level = station == 0 ? ProcessLevel.Machine : ProcessLevel.Station;
        Station = station == 0 ? null : _project.Station(station);
        if (Station != null) AppServices.Selection.Station = Station.Number;
        Process = ResolveProcess(processKey);
        Canvas = new ProcessCanvasViewModel(_project, Station, Process, Level);
        BuildTabs();
        BuildPalette();
        foreach (var s in _project.Stations) Chips.Add(new StationChipViewModel(s.Number, s.Name, n => AppServices.Navigation.Navigate("ProcessEditor:" + n + ":" + (Station != null ? StationViewModel.ProcessKey(Station.Processes.FirstOrDefault(p => p.Kind == ProcessKind.Operation) ?? Process) : "presser"))));
        foreach (var c in Chips) c.IsSelected = Station != null && c.Number == Station.Number;
        DropHandler = new ProcessDropTarget(() => Canvas, Commit);
        _undo.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo)); };
        AppServices.Project.Changed += OnProjectChanged;
        Canvas.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ProcessCanvasViewModel.SelectedStepId)) UpdatePanel(); };
        RefreshValidation();
        UpdatePanel();
    }

    public ProcessLevel Level { get; }
    public StationConfig? Station { get; }
    public ProcessDefinition Process { get; }
    public ProcessCanvasViewModel Canvas { get; }
    public IDropTarget DropHandler { get; }
    public ObservableCollection<StationChipViewModel> Chips { get; } = new ObservableCollection<StationChipViewModel>();
    public ObservableCollection<ProcessTabModel> Tabs { get; } = new ObservableCollection<ProcessTabModel>();
    public ObservableCollection<PaletteGroupViewModel> Groups { get; } = new ObservableCollection<PaletteGroupViewModel>();
    public ObservableCollection<ParamRowViewModel> Params { get; } = new ObservableCollection<ParamRowViewModel>();
    /// <summary>Résultats de l'étape sélectionnée et variable qui les reçoit (docs/13).</summary>
    public ObservableCollection<ParamRowViewModel> Outputs { get; } = new ObservableCollection<ParamRowViewModel>();
    /// <summary>Variables propres au process affiché (onglet Données).</summary>
    public ObservableCollection<VariableRowViewModel> ProcessVars { get; } = new ObservableCollection<VariableRowViewModel>();
    public ObservableCollection<IssueRow> Issues { get; } = new ObservableCollection<IssueRow>();

    public bool IsStationLevel => Level == ProcessLevel.Station;

    /// <summary>Mise en page du canevas : 0 = couloirs, 1 = flux vertical sans couloirs (mode par défaut, docs/13).</summary>
    public int LayoutIndex
    {
        get => EditorSettings.FlowLayout ? 1 : 0;
        set
        {
            if ((value == 1) == EditorSettings.FlowLayout) return;
            EditorSettings.FlowLayout = value == 1;
            OnPropertyChanged();
            Canvas.Rebuild();
        }
    }
    public bool CanUndo => _undo.CanUndo;
    public bool CanRedo => _undo.CanRedo;
    public string Title => Level == ProcessLevel.Machine ? "Process général" : Station!.Name + " · " + Process.Name;

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private int _panelTab;
    [ObservableProperty] private string _compiledListing = string.Empty;
    [ObservableProperty] private string _validationSummary = string.Empty;
    [ObservableProperty] private Tone _validationTone = Tone.Ok;
    [ObservableProperty] private string _chronogramText = string.Empty;
    [ObservableProperty] private int _levelIndex;
    [ObservableProperty] private string _savedText = "Enregistré à l'instant";

    // ---- panneau Étape
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _panelTitle = string.Empty;
    [ObservableProperty] private string _panelNumber = string.Empty;
    [ObservableProperty] private string _panelIcon = "help";
    [ObservableProperty] private string _panelObject = string.Empty;
    [ObservableProperty] private string _panelKind = string.Empty;
    [ObservableProperty] private string _panelDescription = string.Empty;
    [ObservableProperty] private bool _showFail;
    [ObservableProperty] private int _failIndex;
    [ObservableProperty] private bool _showRetries;
    [ObservableProperty] private double _retries = 3;
    [ObservableProperty] private bool _showTimeout;
    [ObservableProperty] private double _timeout;
    [ObservableProperty] private bool _isBlock;
    [ObservableProperty] private bool _canAddBranch;
    [ObservableProperty] private bool _canRemoveBranch;
    [ObservableProperty] private string _blockInfo = string.Empty;
    private bool _syncingPanel;

    private ProcessDefinition ResolveProcess(string key)
    {
        if (Level == ProcessLevel.Machine) return _project.GeneralProcess;
        var st = Station!;
        ProcessDefinition? p = st.Processes.FirstOrDefault(x => StationViewModel.ProcessKey(x) == key) ?? st.Processes.FirstOrDefault(x => x.Id.ToString("N") == key);
        return p ?? st.Processes.First(x => x.Kind == ProcessKind.Operation);
    }

    private void BuildTabs()
    {
        Tabs.Clear();
        if (Level == ProcessLevel.Machine) { Tabs.Add(new ProcessTabModel("Process général", "General", true)); return; }
        foreach (var p in Station!.Processes)
        {
            string name = p.Kind == ProcessKind.Operation && p.Implements.Count > 0 ? (_project.Group(p.Implements[0].GroupId)?.Name ?? "") + " › " + p.Name : p.Name;
            Tabs.Add(new ProcessTabModel(name, StationViewModel.ProcessKey(p), p.Id == Process.Id));
        }
    }

    // ---------------------------------------------------------------- palette

    private void BuildPalette()
    {
        _allGroups.Clear();
        if (Level == ProcessLevel.Station)
        {
            foreach (var root in Station!.Objects)
            {
                var type = DemoCatalog.Get(root.TypeCode);
                var g = new PaletteGroupViewModel(root.Name, type?.Icon ?? "folder");
                string? reason = root.Disabled ? "Objet désactivé" : null;
                if (type != null)
                {
                    foreach (var f in type.Functions)
                    {
                        var fn = f; var obj = root;
                        g.Items.Add(new PaletteItemViewModel(f.Name, "precision_manufacturing", new PaletteDrag(f.Name, () => ProcessEditing.NewFunction(obj, fn), obj.Id), reason));
                    }
                    // Fonctions personnalisées du module : un crayon les distingue des fonctions du catalogue (docs/08 § 6.9)
                    foreach (var cf in Station.CustomFunctions.Where(x => x.ModuleObjectId == root.Id))
                    {
                        var cdef = DemoFunctions.FromCustom(cf, Station); var cobj = root;
                        g.Items.Add(new PaletteItemViewModel(cf.Name, "edit", new PaletteDrag(cf.Name, () => ProcessEditing.NewFunction(cobj, cdef), cobj.Id), reason));
                    }
                    foreach (var a in type.Actions)
                    {
                        var act = a; var obj = root;
                        g.Items.Add(new PaletteItemViewModel(a.Name, a.Icon, new PaletteDrag(a.Name, () => ProcessEditing.NewAction(Station, obj, act), obj.Id), reason));
                    }
                }
                foreach (var child in root.Children)
                {
                    var ct = DemoCatalog.Get(child.TypeCode);
                    if (ct == null || child.TypeCode == "LinearAxis" || child.TypeCode == "RotaryAxis") continue;
                    foreach (var a in ct.Actions)
                    {
                        var act = a; var obj = child;
                        g.Items.Add(new PaletteItemViewModel(child.Name + " · " + a.Name, a.Icon, new PaletteDrag(a.Name, () => ProcessEditing.NewAction(Station, obj, act), obj.Id), child.Disabled ? "Objet désactivé" : null));
                    }
                }
                if (g.Items.Count > 0) _allGroups.Add(g);
            }
        }
        else
        {
            foreach (var group in _project.Machine.Groups)
            {
                var g = new PaletteGroupViewModel(group.Name, group.Role == GroupRole.Entry ? "login" : group.Role == GroupRole.Exit ? "logout" : group.Role == GroupRole.Reject ? "delete" : "build");
                for (int i = 0; i < group.Operations.Count; i++)
                {
                    var grp = group; int no = i + 1;
                    g.Items.Add(new PaletteItemViewModel(group.Operations[i].Name, g.Icon, new PaletteDrag(group.Operations[i].Name, () => ProcessEditing.NewRoute(grp, no), null)));
                }
                _allGroups.Add(g);
            }
        }
        var logic = new PaletteGroupViewModel("Logique", "alt_route");
        void L(string label, string icon, string kind, string? reason = null)
            => logic.Items.Add(new PaletteItemViewModel(label, icon, new PaletteDrag(label, () => ProcessEditing.NewLogic(kind, Station, Process), null), reason));
        L("Attendre condition", "hourglass_empty", "wait");
        L("Attendre durée", "timer", "time");
        L("Si / Sinon", "alt_route", "if");
        L("Répéter", "repeat", "repeat");
        if (Level == ProcessLevel.Station) L("En parallèle", "call_split", "parallel");
        else logic.Items.Add(new PaletteItemViewModel("En parallèle", "call_split", new PaletteDrag("En parallèle", () => new ParallelBlock(), null), "« En parallèle » n'existe pas au niveau machine : un mover ne peut être qu'à un endroit à la fois."));
        L("Appeler sous-process", "account_tree", "call", Level == ProcessLevel.Station && Station!.Processes.Any(p => p.Kind == ProcessKind.Sub) ? null : "Aucun sous-process défini dans cette station.");
        L("Définir variable", "edit", "set");
        L("Signaler défaut", "report", "alarm");
        L("Fin", "flag", "end");
        _allGroups.Add(logic);
        ApplyFilter();
    }

    partial void OnQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Groups.Clear();
        string q = (Query ?? string.Empty).Trim().ToLowerInvariant();
        foreach (var g in _allGroups)
        {
            if (q.Length == 0) { Groups.Add(g); continue; }
            var filtered = new PaletteGroupViewModel(g.Name, g.Icon);
            foreach (var i in g.Items) if (i.Label.ToLowerInvariant().Contains(q) || g.Name.ToLowerInvariant().Contains(q)) filtered.Items.Add(i);
            if (filtered.Items.Count > 0) Groups.Add(filtered);
        }
    }

    // ---------------------------------------------------------------- modifications annulables

    /// <summary>Applique une modification de l'arbre et des variables et l'enregistre comme commande annulable (instantanés avant / après).</summary>
    private void Apply(string description, Action mutate, Guid? select = null)
    {
        var before = Process.Body.Clone();
        var varsBefore = SnapshotVariables();
        mutate();
        var after = Process.Body.Clone();
        var varsAfter = SnapshotVariables();
        _undo.Push(new DelegateEdit(description,
            () => { Process.Body = before.Clone(); RestoreVariables(varsBefore); AfterEdit(null); },
            () => { Process.Body = after.Clone(); RestoreVariables(varsAfter); AfterEdit(null); }));
        AfterEdit(select);
    }

    private static StationVariable CloneVariable(StationVariable v) => new StationVariable { Id = v.Id, Name = v.Name, Type = v.Type, Initial = v.Initial };

    private (List<StationVariable> Process, List<StationVariable> Station) SnapshotVariables()
        => (Process.Variables.Select(CloneVariable).ToList(), Station?.Variables.Select(CloneVariable).ToList() ?? new List<StationVariable>());

    private void RestoreVariables((List<StationVariable> Process, List<StationVariable> Station) snapshot)
    {
        Process.Variables.Clear();
        Process.Variables.AddRange(snapshot.Process.Select(CloneVariable));
        if (Station == null) return;
        Station.Variables.Clear();
        Station.Variables.AddRange(snapshot.Station.Select(CloneVariable));
    }

    private void AfterEdit(Guid? select)
    {
        Canvas.Rebuild();
        AppServices.Project.NotifyEdited();
        if (select.HasValue) Canvas.SelectedStepId = select;
        else if (Canvas.SelectedStepId.HasValue && ProcessTree.Find(Process.Body, Canvas.SelectedStepId.Value) == null) Canvas.SelectedStepId = null;
        RefreshValidation();
        UpdatePanel();
    }

    private void OnProjectChanged(object? sender, EventArgs e)
    {
        RefreshValidation();
    }

    /// <summary>Dépôt réussi : insère une nouvelle étape (palette) ou déplace une étape ou un bloc existant.</summary>
    private void Commit(object payload, InsertTarget target)
    {
        if (payload is PaletteDrag pd)
        {
            var step = pd.Create();
            Apply("Ajouter « " + pd.Label + " »", () => ProcessEditing.InsertOrMove(Process.Body, step, target.Sequence, target.Index), step.Id);
            Flash(step.Id);
        }
        else if (payload is Step s)
        {
            // La suite cible peut appartenir à l'arbre actuel : le déplacement la retrouve par référence.
            Apply("Déplacer « " + new StepPresenter(_project, Station, Process).Present(s).Title + " »", () => ProcessEditing.InsertOrMove(Process.Body, s, target.Sequence, target.Index), s.Id);
            Flash(s.Id);
        }
    }

    /// <summary>Halo accent qui pulse 3 fois sur l'élément ciblé (docs/08 § 7.7) ; fondu et léger agrandissement à l'apparition.</summary>
    private void Flash(Guid id)
    {
        var card = Canvas.CardOf(id);
        if (card == null) return;
        card.IsHighlighted = true;
        var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        t.Tick += (_, _) => { t.Stop(); card.IsHighlighted = false; };
        t.Start();
    }

    [RelayCommand] private void Undo() { _undo.Undo(); }
    [RelayCommand] private void Redo() { _undo.Redo(); }

    // ---------------------------------------------------------------- panneau Étape


    public void OnSelectionChanged() => UpdatePanel();

    [RelayCommand]
    private void SelectStep(Guid? id)
    {
        Canvas.SelectedStepId = id;
        UpdatePanel();
    }

    [RelayCommand]
    private void SelectProcess(ProcessTabModel tab)
    {
        if (tab.IsSelected) return;
        AppServices.Navigation.Navigate(Level == ProcessLevel.Machine ? "ProcessEditor:General" : "ProcessEditor:" + Station!.Number + ":" + tab.Key);
    }

    [RelayCommand]
    private void GoStationLevel()
    {
        int st = AppServices.Selection.Station;
        AppServices.Navigation.Navigate("ProcessEditor:" + st + ":presser");
    }

    [RelayCommand] private void GoMachineLevel() => AppServices.Navigation.Navigate("ProcessEditor:General");
    [RelayCommand] private void ZoomIn() => Canvas.Zoom = Math.Min(2.0, Math.Round(Canvas.Zoom + 0.1, 2));
    [RelayCommand] private void ZoomOut() => Canvas.Zoom = Math.Max(0.5, Math.Round(Canvas.Zoom - 0.1, 2));
    [RelayCommand] private void ZoomReset() => Canvas.Zoom = 1.0;
    [RelayCommand] private void ZoomFit() => Canvas.Zoom = 0.75;
    [RelayCommand] private void Deploy() => AppServices.Navigation.Navigate("Deploy", null, pushHistory: true);

    private void UpdatePanel()
    {
        _syncingPanel = true;
        Params.Clear();
        Outputs.Clear();
        RefreshVariableRows();
        var id = Canvas.SelectedStepId;
        var loc = id.HasValue ? ProcessTree.Find(Process.Body, id.Value) : null;
        HasSelection = loc.HasValue;
        IsBlock = false; ShowFail = false; ShowTimeout = false; ShowRetries = false; CanAddBranch = false; CanRemoveBranch = false; BlockInfo = string.Empty;
        if (!loc.HasValue) { _syncingPanel = false; return; }
        var step = loc.Value.Step;
        var presenter = new StepPresenter(_project, Station, Process);
        var text = presenter.Present(step);
        var card = Canvas.CardOf(step.Id);
        PanelNumber = card != null ? card.Number.ToString() : "·";
        PanelTitle = text.Title;
        PanelIcon = text.Icon;
        PanelObject = text.Subtitle ?? string.Empty;
        PanelKind = step is ActionStep ? "Action de base" : step is FunctionStep ? "Fonction de module" : step is RouteStep ? "Opération" : step is Block ? "Bloc de logique" : "Logique";
        PanelDescription = Describe(step, text);

        switch (step)
        {
            case ActionStep a:
            {
                var def = DemoCatalog.Get(Station!.FindObject(a.ObjectId)?.TypeCode ?? "")?.Action(a.ActionNo);
                if (def != null) BuildParamRows(def.Params, a.Args, step);
                BuildOutputRows(a);
                ShowFail = true; FailIndex = ToIndex(a.Fail.Kind); ShowRetries = a.Fail.Kind == FailKind.Retry; Retries = a.Fail.Retries == 0 ? 3 : a.Fail.Retries;
                ShowTimeout = true; Timeout = a.TimeoutSeconds ?? (def?.DurationSeconds > 0 ? Math.Round(def.DurationSeconds * 4, 1) : 5);
                break;
            }
            case FunctionStep f:
            {
                var def = DemoFunctions.FindFunction(Station, f.ModuleObjectId, f.FunctionId);
                if (def != null) BuildParamRows(def.Params, f.Args, step);
                ShowFail = true; FailIndex = ToIndex(f.Fail.Kind); ShowRetries = f.Fail.Kind == FailKind.Retry; Retries = f.Fail.Retries == 0 ? 3 : f.Fail.Retries;
                break;
            }
            case RouteStep r:
            {
                var op = _project.Group(r.GroupId)?.Operations.ElementAtOrDefault(r.OperationNo - 1);
                if (op != null) BuildParamRows(op.Parameters, r.Args, step);
                break;
            }
            case WaitConditionStep w:
                Params.Add(new ParamRowViewModel("Condition", presenter.ConditionText(w.Condition), w.Condition.Terms.Count == 0, anchor => Task.CompletedTask));
                ShowTimeout = true; Timeout = w.TimeoutSeconds ?? 5;
                break;
            case WaitTimeStep t:
                Params.Add(new ParamRowViewModel("Durée", DisplayFormat.Duration(t.DurationSeconds), false, anchor => EditNumberAsync(anchor, "Durée", t.DurationSeconds, "s", 0.1, 3600, 0.5, 1, v => t.DurationSeconds = v, step)));
                break;
            case RepeatBlock rb:
                IsBlock = true;
                BlockInfo = rb.Mode == RepeatMode.Count ? "Répéter " + rb.Count + " fois" : "Répéter tant que " + presenter.ConditionText(rb.Condition) + " (maximum " + rb.Max + ")";
                Params.Add(new ParamRowViewModel("Nombre de répétitions", rb.Count.ToString(), false, anchor => EditNumberAsync(anchor, "Nombre de répétitions", rb.Count, "", 1, 1000, 1, 0, v => rb.Count = (int)v, step)));
                break;
            case IfBlock ib:
                IsBlock = true;
                Params.Add(new ParamRowViewModel("Condition", presenter.ConditionText(ib.Condition), ib.Condition.Terms.Count == 0, anchor => Task.CompletedTask));
                BlockInfo = "Les termes de la condition portent sur les données de pièce, les variables de station et les valeurs des objets.";
                break;
            case ParallelBlock pb:
                IsBlock = true;
                CanAddBranch = pb.Branches.Count < 4;
                CanRemoveBranch = pb.Branches.Count > 2;
                BlockInfo = pb.Branches.Count + " branches. Les branches s'attendent à la fin du bloc ; un objet ne peut pas figurer dans deux branches.";
                break;
            case SetStep s:
                Params.Add(new ParamRowViewModel("Cible", s.Target.ToString(), false, anchor => Task.CompletedTask));
                break;
            case AlarmStep al:
                Params.Add(new ParamRowViewModel("Message", al.Message, al.Message.Length == 0, async anchor =>
                {
                    var r = await AppServices.Keypad.EditTextAsync(anchor, new TextEditRequest("Message du défaut", al.Message, "Texte affiché dans l'alarme"));
                    if (r != null) Apply("Modifier le message", () => al.Message = r, step.Id);
                }));
                break;
        }
        _syncingPanel = false;
    }

    private static string Describe(Step step, CardText text)
    {
        switch (step)
        {
            case ActionStep: return "Action de base de l'objet « " + text.Subtitle + " » (niveau 2). Elle respecte le contrat d'action : démarrage seulement si l'objet est prêt, timeout, arrêt sur Abort.";
            case FunctionStep: return "Fonction de module (niveau 3) : suite d'actions de base dépliée à la compilation sur les rôles d'enfant du module.";
            case RouteStep: return "Opération (niveau 4) : le séquenceur général choisit une station disponible de la fonction, y envoie le mover et lance l'opération.";
            case WaitConditionStep: return "Attend qu'une condition soit vraie, puis passe à l'étape suivante. À l'expiration du délai, la politique d'échec s'applique.";
            case ParallelBlock: return "Exécute ses branches en même temps ; chaque branche utilise ses propres objets.";
            case IfBlock: return "Évalue une condition et exécute la branche Alors ou la branche Sinon.";
            case RepeatBlock: return "Répète son corps un nombre de fois fixé, ou tant qu'une condition est vraie, avec un maximum.";
            case SetStep: return "Affecte une valeur à une variable de station, une donnée de pièce, le verdict ou l'indicateur « pièce retirée ».";
            case AlarmStep: return "Lève un défaut de process avec le message donné.";
            default: return "Étape du process.";
        }
    }

    private void BuildParamRows(IReadOnlyList<ParamDef> defs, List<ValueRef> args, Step step)
    {
        var presenter = new StepPresenter(_project, Station, Process);
        for (int i = 0; i < defs.Count; i++)
        {
            int idx = i; var d = defs[i];
            bool has = idx < args.Count;
            string value = has ? presenter.ValueText(args[idx], d) : "à choisir";
            bool err = (!has || (args[idx].Kind == ValueKind.NamedPos && Station?.Positions.All(p => p.Id != args[idx].Ref) == true)) && (d.Type == "Position" || d.Type == "Recipe");
            Params.Add(new ParamRowViewModel(d.Name, value, err, anchor => EditParamAsync(anchor, d, idx, args, step)));
        }
    }

    // ---------------------------------------------------------------- variables et sorties (docs/13)

    public static string TypeText(string type) => type == "Position" ? "Position" : type == "Bool" ? "Booléen" : "Nombre";

    private static string ResultTypeText(string type) => type switch
    {
        "Position" => "position",
        "Int" => "entier",
        "Bool" => "booléen",
        "Text" => "texte",
        "Enum" => "jugement",
        _ => "nombre",
    };

    private bool NameTaken(string name, StationVariable? except = null)
        => Process.Variables.Concat(Station?.Variables ?? new List<StationVariable>())
            .Any(v => !ReferenceEquals(v, except) && string.Equals(v.Name, name, StringComparison.OrdinalIgnoreCase));

    private string UniqueName(string wanted)
    {
        string name = wanted.Trim();
        if (!NameTaken(name)) return name;
        for (int i = 2; ; i++)
            if (!NameTaken(name + " " + i)) return name + " " + i;
    }

    private void RefreshVariableRows()
    {
        ProcessVars.Clear();
        foreach (var v in Process.Variables)
        {
            var variable = v;
            ProcessVars.Add(new VariableRowViewModel(variable, a => RenameVariableAsync(a, variable), a => ShareVariableAsync(variable), a => DeleteVariableAsync(variable)));
        }
    }

    private async Task RenameVariableAsync(FrameworkElement anchor, StationVariable v)
    {
        var name = await AppServices.Keypad.EditTextAsync(anchor, new TextEditRequest("Nom de la variable", v.Name, "Nom visible dans les cartes et les sélecteurs"));
        if (name == null) return;
        name = name.Trim();
        if (name.Length == 0 || name == v.Name) return;
        if (NameTaken(name, v)) { AppServices.Toast.Show("Une variable porte déjà ce nom.", ToastKind.Warning); return; }
        Apply("Renommer la variable « " + v.Name + " »", () => v.Name = name);
    }

    private Task ShareVariableAsync(StationVariable v)
    {
        if (Station == null) return Task.CompletedTask;
        // Même identifiant : toutes les références restent valables, la variable devient visible dans tous les process de la station.
        Apply("Rendre la variable « " + v.Name + " » partagée", () => { Process.Variables.RemoveAll(x => x.Id == v.Id); Station.Variables.Add(CloneVariable(v)); });
        AppServices.Toast.Show("« " + v.Name + " » est maintenant une variable de station.", ToastKind.Info);
        return Task.CompletedTask;
    }

    private Task DeleteVariableAsync(StationVariable v)
    {
        int uses = Process.AllSteps().Count(s => StdMachine.Core.Validation.VariableUsage.ReadsOf(s).Concat(StdMachine.Core.Validation.VariableUsage.WritesOf(s)).Contains(v.Id));
        if (uses > 0)
        {
            AppServices.Toast.Show("« " + v.Name + " » est utilisée par " + UiText.Plural(uses, "étape", "étapes") + " : retirez d'abord ces usages.", ToastKind.Warning);
            return Task.CompletedTask;
        }
        Apply("Supprimer la variable « " + v.Name + " »", () => Process.Variables.RemoveAll(x => x.Id == v.Id));
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task NewVariable(FrameworkElement anchor)
    {
        var types = new List<PickItem>
        {
            new PickItem("Nombre", "valeur numérique", "Number", true, "pin"),
            new PickItem("Booléen", "vrai ou faux", "Bool", true, "toggle_on"),
            new PickItem("Position", "X, Y, Z, C", "Position", true, "place"),
        };
        if (await Pickers.PickAsync(anchor, "Type de la variable", types) is not string type) return;
        var name = await AppServices.Keypad.EditTextAsync(anchor, new TextEditRequest("Nom de la variable", UniqueName("Variable"), "Nom visible dans les cartes et les sélecteurs"));
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (NameTaken(name)) { AppServices.Toast.Show("Une variable porte déjà ce nom.", ToastKind.Warning); return; }
        Apply("Créer la variable « " + name + " »", () => Process.Variables.Add(new StationVariable { Name = name, Type = type }));
    }

    /// <summary>Une ligne par résultat de l'action sélectionnée : toucher pour choisir la variable (ou la donnée) qui le reçoit.</summary>
    private void BuildOutputRows(ActionStep a)
    {
        var results = ResultsOf(a);
        var presenter = new StepPresenter(_project, Station, Process);
        for (int i = 0; i < results.Count; i++)
        {
            int no = i + 1;
            var result = results[i];
            var binding = a.Bindings.FirstOrDefault(b => b.ResultNo == no);
            string value = binding == null ? "Non rangé" : presenter.BindingTargetText(binding);
            Outputs.Add(new ParamRowViewModel(result.Name + " · " + ResultTypeText(result.Type), value, false, anchor => EditOutputAsync(anchor, a, no, result)));
        }
    }

    private IReadOnlyList<StdMachine.Core.Catalog.CatalogResult> ResultsOf(ActionStep a)
    {
        var node = Station?.FindObject(a.ObjectId);
        var action = AppServices.Compilation.Catalog?.Type(node?.TypeCode ?? string.Empty)?.Action(a.ActionNo);
        return action?.Results ?? new List<StdMachine.Core.Catalog.CatalogResult>();
    }

    private static bool Compatible(string resultType, string variableType)
        => resultType == "Position" ? variableType == "Position" : resultType != "Text" && variableType != "Position";

    private async Task EditOutputAsync(FrameworkElement anchor, ActionStep a, int no, StdMachine.Core.Catalog.CatalogResult result)
    {
        const string None = "none", NewProcess = "new:process", NewStation = "new:station";
        var items = new List<PickItem> { new PickItem("Ne pas ranger", null, None, true, "block") };
        foreach (var v in Process.Variables.Where(v => Compatible(result.Type, v.Type)))
            items.Add(new PickItem(v.Name, "variable de process", new ResultBinding(no, BindTarget.Var, v.Id), true, "tune"));
        foreach (var v in (Station?.Variables ?? new List<StationVariable>()).Where(v => Compatible(result.Type, v.Type)))
            items.Add(new PickItem(v.Name, "variable de station", new ResultBinding(no, BindTarget.Var, v.Id), true, "tune"));
        if (result.Type != "Position" && result.Type != "Text")
            foreach (var d in _project.Machine.PartData.Where(d => !d.Reserved))
                items.Add(new PickItem("Donnée « " + d.Name + " »", "donnée de pièce", new ResultBinding(no, BindTarget.PartData, d.Id), true, "category"));
        if (result.Type == "Enum") items.Add(new PickItem("Verdict de la pièce", "bon ou mauvais selon le jugement", new ResultBinding(no, BindTarget.Verdict), true, "gavel"));
        if (result.Type == "Text") items.Add(new PickItem("Identifiant de la pièce", null, new ResultBinding(no, BindTarget.PartId), true, "qr_code"));
        if (result.Type != "Text")
        {
            items.Add(new PickItem("Nouvelle variable de process…", "visible seulement ici", NewProcess, true, "add"));
            items.Add(new PickItem("Nouvelle variable de station…", "partagée entre les process", NewStation, true, "add"));
        }

        object current = (object?)a.Bindings.FirstOrDefault(b => b.ResultNo == no) ?? None;
        var pick = await Pickers.PickAsync(anchor, result.Name, items, current);
        if (pick == null) return;

        if (pick is string s && s == None)
        {
            Apply("Ne plus ranger « " + result.Name + " »", () => a.Bindings.RemoveAll(b => b.ResultNo == no), a.Id);
            return;
        }
        if (pick is ResultBinding chosen)
        {
            Apply("Ranger « " + result.Name + " »", () => { a.Bindings.RemoveAll(b => b.ResultNo == no); a.Bindings.Add(chosen); }, a.Id);
            return;
        }

        // Nouvelle variable : le nom est proposé d'après le résultat et se modifie tout de suite.
        string type = result.Type == "Position" ? "Position" : result.Type == "Bool" ? "Bool" : "Number";
        var name = await AppServices.Keypad.EditTextAsync(anchor, new TextEditRequest("Nom de la variable", UniqueName(result.Name), "Nom visible dans les cartes et les sélecteurs"));
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        if (NameTaken(name)) { AppServices.Toast.Show("Une variable porte déjà ce nom.", ToastKind.Warning); return; }
        var variable = new StationVariable { Name = name, Type = type };
        bool shared = (string)pick == NewStation && Station != null;
        Apply("Ranger « " + result.Name + " » dans « " + name + " »", () =>
        {
            if (shared) Station!.Variables.Add(variable); else Process.Variables.Add(variable);
            a.Bindings.RemoveAll(b => b.ResultNo == no);
            a.Bindings.Add(new ResultBinding(no, BindTarget.Var, variable.Id));
        }, a.Id);
    }

    private async Task EditParamAsync(FrameworkElement anchor, ParamDef d, int idx, List<ValueRef> args, Step step)
    {
        while (args.Count <= idx) args.Add(ValueRef.Const(d.Default));
        ValueRef? result = null;
        switch (d.Type)
        {
            case "Position":
            {
                var items = new List<PickItem>();
                foreach (var p in Station!.Positions)
                    items.Add(new PickItem(p.Name, p.Taught ? "apprise" : "non apprise", ValueRef.NamedPos(p.Id, p.Approach ?? 0), p.Taught));
                foreach (var v in Process.Variables.Where(v => v.Type == "Position"))
                    items.Add(new PickItem(v.Name, "variable de process", ValueRef.Var(v.Id), true, "tune"));
                foreach (var v in Station.Variables.Where(v => v.Type == "Position"))
                    items.Add(new PickItem(v.Name, "variable de station", ValueRef.Var(v.Id), true, "tune"));
                var pick = await Pickers.PickAsync(anchor, d.Name, items, args[idx]);
                if (pick is ValueRef vr) result = vr;
                break;
            }
            case "Speed":
            {
                var items = new List<PickItem> { new PickItem("Lente", "V1 · 10 % de Vmax", ValueRef.Speed(1)), new PickItem("Rapide", "V2 · 80 % de Vmax", ValueRef.Speed(2)), new PickItem("Approche", "V3 · 30 % de Vmax", ValueRef.Speed(3)) };
                var pick = await Pickers.PickAsync(anchor, d.Name, items, args[idx]);
                if (pick is ValueRef vr) result = vr;
                break;
            }
            case "Bool":
                result = ValueRef.Const(args[idx].Number != 0 ? 0 : 1);
                break;
            case "Text":
            {
                var r = await AppServices.Keypad.EditTextAsync(anchor, new TextEditRequest(d.Name, args[idx].Text ?? "", ""));
                if (r != null) result = ValueRef.TextOf(r);
                break;
            }
            default:
            {
                var n = await AppServices.Keypad.EditNumberAsync(anchor, new NumberEditRequest(d.Name, args[idx].Number, d.Unit ?? "", d.Min ?? -1_000_000, d.Max ?? 1_000_000, d.Type == "Int" || d.Type == "Recipe" ? 1 : 0.5, d.Type == "Int" || d.Type == "Recipe" || d.Type == "Duration" ? 0 : 2));
                if (n.HasValue) result = ValueRef.Const(n.Value);
                break;
            }
        }
        if (result == null) return;
        var value = result;
        Apply("Modifier « " + d.Name + " »", () => args[idx] = value, step.Id);
    }

    private async Task EditNumberAsync(FrameworkElement anchor, string title, double current, string unit, double min, double max, double step, int decimals, Action<double> set, Step owner)
    {
        var n = await AppServices.Keypad.EditNumberAsync(anchor, new NumberEditRequest(title, current, unit, min, max, step, decimals));
        if (n.HasValue) Apply("Modifier « " + title + " »", () => set(n.Value), owner.Id);
    }

    // ---- politique d'échec, délai, blocs
    partial void OnFailIndexChanged(int value)
    {
        if (_syncingPanel || !Canvas.SelectedStepId.HasValue) return;
        var loc = ProcessTree.Find(Process.Body, Canvas.SelectedStepId.Value);
        if (!loc.HasValue) return;
        var kind = FromIndex(value);
        var policy = new FailPolicy(kind, kind == FailKind.Retry ? (int)Retries : 0);
        if (loc.Value.Step is ActionStep a && a.Fail.Kind != kind) Apply("Modifier la politique d'échec", () => a.Fail = policy, a.Id);
        else if (loc.Value.Step is FunctionStep f && f.Fail.Kind != kind) Apply("Modifier la politique d'échec", () => f.Fail = policy, f.Id);
    }

    partial void OnRetriesChanged(double value)
    {
        if (_syncingPanel || !Canvas.SelectedStepId.HasValue) return;
        var loc = ProcessTree.Find(Process.Body, Canvas.SelectedStepId.Value);
        if (!loc.HasValue) return;
        if (loc.Value.Step is ActionStep a && a.Fail.Kind == FailKind.Retry) Apply("Modifier le nombre d'essais", () => a.Fail = new FailPolicy(FailKind.Retry, (int)value), a.Id);
    }

    private static int ToIndex(FailKind k) => k == FailKind.StopStation ? 0 : k == FailKind.Retry ? 1 : k == FailKind.PartBad ? 2 : 3;
    private static FailKind FromIndex(int i) => i == 0 ? FailKind.StopStation : i == 1 ? FailKind.Retry : i == 2 ? FailKind.PartBad : FailKind.Ignore;

    [RelayCommand]
    private void DeleteSelected()
    {
        if (!Canvas.SelectedStepId.HasValue) return;
        var id = Canvas.SelectedStepId.Value;
        Apply("Supprimer l'étape", () => ProcessEditing.Remove(Process.Body, id));
        AppServices.Toast.Show("Étape supprimée.", ToastKind.Info);
    }

    [RelayCommand]
    private void DuplicateSelected()
    {
        if (!Canvas.SelectedStepId.HasValue) return;
        var loc = ProcessTree.Find(Process.Body, Canvas.SelectedStepId.Value);
        if (!loc.HasValue) return;
        var copy = loc.Value.Step.CloneWithNewIds();
        Apply("Dupliquer l'étape", () => loc.Value.Parent.Steps.Insert(loc.Value.Index + 1, copy), copy.Id);
    }

    [RelayCommand]
    private void AddBranch()
    {
        if (Canvas.SelectedStepId == null) return;
        var loc = ProcessTree.Find(Process.Body, Canvas.SelectedStepId.Value);
        if (loc?.Step is ParallelBlock pb && pb.Branches.Count < 4)
            Apply("Ajouter une branche", () => pb.Branches.Add(new Sequence()), pb.Id);
    }

    [RelayCommand]
    private void RemoveBranch()
    {
        if (Canvas.SelectedStepId == null) return;
        var loc = ProcessTree.Find(Process.Body, Canvas.SelectedStepId.Value);
        if (loc?.Step is ParallelBlock pb && pb.Branches.Count > 2)
            Apply("Supprimer une branche", () => pb.Branches.RemoveAt(pb.Branches.Count - 1), pb.Id);
    }

    [RelayCommand]
    private void SelectIssue(IssueRow issue)
    {
        if (issue.StepId.HasValue) { Canvas.SelectedStepId = issue.StepId; UpdatePanel(); }
    }

    // ---------------------------------------------------------------- validation et chronogramme

    partial void OnPanelTabChanged(int value)
    {
        if (value == 2) RefreshListing();
    }

    /// <summary>Onglet « Table compilée » : listing des instructions de la table du process affiché (docs/06 § 5.6).</summary>
    private void RefreshListing()
    {
        var result = AppServices.Compilation.Compile(_project);
        if (result == null) { CompiledListing = "Le catalogue (catalog/catalog.json) n'est pas disponible : le listing ne peut pas être produit."; return; }
        if (!result.Succeeded)
        {
            CompiledListing = "Compilation impossible : corrigez d'abord les erreurs de validation.\n\n" + string.Join("\n",
                result.Issues.Where(i => i.Severity == StdMachine.Core.Validation.Severity.Error).Take(12).Select(i => i.Code + "  " + i.Message));
            return;
        }
        try { CompiledListing = StdMachine.Core.Compiler.Listing.Of(result.Project!, Station?.Number ?? 0, Process.Name); }
        catch (ArgumentException) { CompiledListing = "Ce process n'a pas de table compilée."; }
    }

    private void RefreshValidation()
    {
        if (PanelTab == 2) RefreshListing();
        Issues.Clear();
        int st = Station?.Number ?? 0;
        var mine = AppServices.Project.Issues.Where(i => i.Station == null || i.Station == st || (Level == ProcessLevel.Machine)).ToList();
        foreach (var i in mine.Take(6)) Issues.Add(new IssueRow(i.Code, i.Message, i.Severity, i.StepId));
        int errors = mine.Count(i => i.Severity == ValidationSeverity.Error), warns = mine.Count(i => i.Severity == ValidationSeverity.Warning);
        ValidationSummary = UiText.Plural(errors, "erreur", "erreurs") + " · " + UiText.Plural(warns, "avertissement", "avertissements");
        ValidationTone = errors > 0 ? Tone.Err : warns > 0 ? Tone.Warn : Tone.Ok;
        if (Level == ProcessLevel.Station && Process.Kind == ProcessKind.Operation)
        {
            var sched = StepSchedule.Build(Process, Station, true, 0);
            var crit = sched.Timings.Where(t => t.Taken && !(t.Step is Block)).OrderByDescending(t => t.End - t.Start).FirstOrDefault();
            string critName = crit == null ? "—" : new StepPresenter(_project, Station, Process).Present(crit.Step).Title;
            ChronogramText = "Temps de cycle estimé " + DisplayFormat.Duration(sched.Total) + " · étape critique : " + critName + " · cadence ≈ " + DisplayFormat.Number(60.0 / Math.Max(0.1, sched.Total), 1) + " pièces/min par station";
        }
        else ChronogramText = string.Empty;
    }

    public override void Dispose()
    {
        AppServices.Project.Changed -= OnProjectChanged;
        base.Dispose();
    }
}
