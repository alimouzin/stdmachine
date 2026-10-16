using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StdMachine.Core;
using StdMachine.Hmi.Controls;
using StdMachine.Hmi.Editor;
using StdMachine.Hmi.Infrastructure;
using StdMachine.Hmi.Services;
using StdMachine.Hmi.Theme;
using StdMachine.Simulation.Demo;

namespace StdMachine.Hmi.ViewModels;

/// <summary>Une action de base dans la suite d'une fonction personnalisée.</summary>
public sealed partial class FnStepViewModel : ObservableObject
{
    public FnStepViewModel(Step step) { Step = step; }

    public Step Step { get; }
    [ObservableProperty] private int _number;
    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private string _icon = "help";
    [ObservableProperty] private string? _subtitle;
    [ObservableProperty] private string _linesText = string.Empty;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _durationText = string.Empty;
}

public sealed partial class FnParamViewModel : ObservableObject
{
    public FnParamViewModel(ParamDef def) { Def = def; }
    public ParamDef Def { get; }
    public string Name => Def.Name;
    public string TypeText => FunctionEditorViewModel.TypeLabel(Def);
    public string DefaultText => FunctionEditorViewModel.DefaultText(Def);
    public void Refresh() { OnPropertyChanged(nameof(Name)); OnPropertyChanged(nameof(TypeText)); OnPropertyChanged(nameof(DefaultText)); }
}

public sealed record FnArgRow(string Label, string Value, bool IsBound, bool IsError, int Index);

/// <summary>
/// Éditeur de fonction personnalisée d'un module (docs/02 § 3, maquette) : suite d'actions de base du module assemblées par glisser-déposer,
/// paramètres de la fonction, liaison des paramètres des actions aux paramètres de la fonction, essai animé.
/// </summary>
public sealed partial class FunctionEditorViewModel : ViewModelBase
{
    private readonly MachineProject _project = AppServices.Project.Project;
    private readonly StationConfig _station;
    private readonly CustomFunction _fn;
    private readonly ObjectNode _module;
    private readonly UndoService _undo = AppServices.Project.Undo;
    private readonly StepPresenter _presenter;
    private bool _syncing;
    private System.Windows.Threading.DispatcherTimer? _runTimer;

    public FunctionEditorViewModel(int station, string functionKey)
    {
        _station = _project.Station(station) ?? _project.Stations[0];
        AppServices.Selection.Station = _station.Number;
        _fn = _station.CustomFunctions.FirstOrDefault(f => f.Id.ToString("N") == functionKey) ?? _station.CustomFunctions[0];
        _module = _station.FindObject(_fn.ModuleObjectId) ?? _station.Objects[0];
        _presenter = new StepPresenter(_project, _station, null!);
        _syncing = true;
        Name = _fn.Name;
        Description = _fn.Description;
        _syncing = false;
        BuildPalette();
        Rebuild();
        _undo.PropertyChanged += (_, _) => { OnPropertyChanged(nameof(CanUndo)); OnPropertyChanged(nameof(CanRedo)); };
    }

    public ObservableCollection<FnStepViewModel> Steps { get; } = new ObservableCollection<FnStepViewModel>();
    public ObservableCollection<FnParamViewModel> Params { get; } = new ObservableCollection<FnParamViewModel>();
    public ObservableCollection<FnArgRow> Args { get; } = new ObservableCollection<FnArgRow>();
    public ObservableCollection<PaletteGroupViewModel> Groups { get; } = new ObservableCollection<PaletteGroupViewModel>();
    public ObservableCollection<string> Issues { get; } = new ObservableCollection<string>();

    public string ModuleName => _module.Name;
    public string StationName => _station.Name;
    public string BasedOnText => _fn.BasedOn != null ? "Copie de « " + _fn.BasedOn + " »" : "Créée de zéro";
    public bool CanUndo => _undo.CanUndo;
    public bool CanRedo => _undo.CanRedo;

    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _durationSummary = string.Empty;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _hasSelection;
    [ObservableProperty] private string _selectedTitle = string.Empty;
    [ObservableProperty] private string _validationSummary = string.Empty;
    [ObservableProperty] private Tone _validationTone = Tone.Ok;
    [ObservableProperty] private bool _running;
    [ObservableProperty] private string _query = string.Empty;

    private FnStepViewModel? _selected;
    private List<PaletteGroupViewModel> _all = new List<PaletteGroupViewModel>();

    partial void OnNameChanged(string value)
    {
        if (_syncing || value == _fn.Name) return;
        string old = _fn.Name;
        _undo.Push(new DelegateEdit("Renommer la fonction", () => { _fn.Name = old; RefreshName(old); }, () => { _fn.Name = value; RefreshName(value); }));
        _fn.Name = value;
        AppServices.Project.NotifyEdited();
        Validate();
    }

    partial void OnDescriptionChanged(string value)
    {
        if (_syncing) return;
        _fn.Description = value;
        AppServices.Project.NotifyEdited();
    }

    partial void OnQueryChanged(string value) => ApplyFilter();

    private void RefreshName(string n) { _syncing = true; Name = n; _syncing = false; AppServices.Project.NotifyEdited(); Validate(); }

    // ---------------------------------------------------------------- libellés

    public static string TypeLabel(ParamDef d)
    {
        switch (d.Type)
        {
            case "Position": return "Position nommée";
            case "Speed": return "Vitesse";
            case "Bool": return "Oui / non";
            case "Int": return "Entier";
            case "Duration": return "Durée";
            default: return "Nombre" + (d.Unit != null ? " (" + d.Unit + ")" : "");
        }
    }

    public static string DefaultText(ParamDef d)
    {
        switch (d.Type)
        {
            case "Position": return "à choisir à l'usage";
            case "Speed": return d.Default == 1 ? "Lente" : d.Default == 2 ? "Rapide" : d.Default == 3 ? "Approche" : "V" + d.Default;
            case "Bool": return d.Default != 0 ? "Oui" : "Non";
            default: return DisplayFormat.Number(d.Default, d.Default == Math.Floor(d.Default) ? 0 : 2) + (d.Unit != null ? " " + d.Unit : "");
        }
    }

    // ---------------------------------------------------------------- palette

    private void BuildPalette()
    {
        _all.Clear();
        void AddGroup(ObjectNode node)
        {
            var type = DemoCatalog.Get(node.TypeCode);
            if (type == null || type.Actions.Count == 0) return;
            var g = new PaletteGroupViewModel(node.Name, type.Icon);
            foreach (var a in type.Actions)
            {
                var act = a; var obj = node;
                g.Items.Add(new PaletteItemViewModel(a.Name, a.Icon, new PaletteDrag(a.Name, () => ProcessEditing.NewAction(_station, obj, act), obj.Id)));
            }
            _all.Add(g);
        }
        foreach (var c in _module.Children) AddGroup(c);
        var logic = new PaletteGroupViewModel("Logique", "alt_route");
        logic.Items.Add(new PaletteItemViewModel("Attendre durée", "timer", new PaletteDrag("Attendre durée", () => new WaitTimeStep { DurationSeconds = 0.2 }, null)));
        logic.Items.Add(new PaletteItemViewModel("Attendre condition", "hourglass_empty", new PaletteDrag("Attendre condition", () => ProcessEditing.NewLogic("wait", _station, new ProcessDefinition()), null)));
        logic.Items.Add(new PaletteItemViewModel("Si / Sinon", "alt_route", new PaletteDrag("Si / Sinon", () => new IfBlock(), null),
            "Les blocs de logique dans une fonction arrivent avec le compilateur (phase 3) ; la maquette n'assemble que des actions de base."));
        _all.Add(logic);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        Groups.Clear();
        string q = (Query ?? "").Trim().ToLowerInvariant();
        foreach (var g in _all)
        {
            if (q.Length == 0) { Groups.Add(g); continue; }
            var f = new PaletteGroupViewModel(g.Name, g.Icon);
            foreach (var i in g.Items) if (i.Label.ToLowerInvariant().Contains(q) || g.Name.ToLowerInvariant().Contains(q)) f.Items.Add(i);
            if (f.Items.Count > 0) Groups.Add(f);
        }
    }

    // ---------------------------------------------------------------- étapes

    public static string? ValidateDrop(object payload)
    {
        if (payload is PaletteDrag pd && pd.Label == "Si / Sinon") return "Les blocs de logique dans une fonction arrivent avec le compilateur (phase 3).";
        return null;
    }

    public object Materialize(object payload)
    {
        if (payload is PaletteDrag pd) return Wrap(pd.Create());
        return payload;
    }

    private FnStepViewModel Wrap(Step step)
    {
        var vm = new FnStepViewModel(step);
        Describe(vm);
        return vm;
    }

    private void Describe(FnStepViewModel vm)
    {
        var text = _presenter.Present(vm.Step);
        vm.Title = text.Title;
        vm.Icon = text.Icon;
        vm.Subtitle = text.Subtitle;
        vm.LinesText = string.Join("   ·   ", text.Params.Select(p => p.Key + " " + p.Value));
        vm.HasError = text.Params.Any(p => p.IsError) || vm.Step is ActionStep a && a.Args.Any(x => x.Kind == ValueKind.NamedPos && x.Ref == null);
        vm.DurationText = "≈ " + DisplayFormat.Number(text.DurationSeconds, 1) + " s";
    }

    /// <summary>Appelée après un dépôt dans la liste : enregistre la nouvelle suite comme modification annulable.</summary>
    public void OnListChanged()
    {
        var before = _fn.Body.Clone();
        var after = new Sequence(Steps.Select(s => s.Step));
        _undo.Push(new DelegateEdit("Modifier la suite d'actions", () => { _fn.Body = before.Clone(); Rebuild(); }, () => { _fn.Body = after.Clone(); Rebuild(); }));
        _fn.Body = after;
        AfterEdit();
        if (Steps.Count > 0 && _selected == null) Select(Steps[Steps.Count - 1]);
    }

    private void Rebuild()
    {
        Guid? keep = _selected?.Step.Id;
        Steps.Clear();
        foreach (var s in _fn.Body.Steps) Steps.Add(Wrap(s));
        Params.Clear();
        foreach (var p in _fn.Params) Params.Add(new FnParamViewModel(p));
        _selected = null;
        var again = keep.HasValue ? Steps.FirstOrDefault(s => s.Step.Id == keep.Value) : null;
        AfterEdit();
        if (again != null) Select(again); else UpdateSelection();
    }

    private void AfterEdit()
    {
        for (int i = 0; i < Steps.Count; i++) { Steps[i].Number = i + 1; Describe(Steps[i]); }
        IsEmpty = Steps.Count == 0;
        double total = Steps.Sum(s => DemoCatalog.EstimatedSeconds(s.Step, _station));
        DurationSummary = Steps.Count + (Steps.Count > 1 ? " actions" : " action") + " · ≈ " + DisplayFormat.Number(total, 1) + " s";
        AppServices.Project.NotifyEdited();
        Validate();
        UpdateSelection();
    }

    [RelayCommand]
    private void Select(FnStepViewModel step)
    {
        if (_selected != null) _selected.IsSelected = false;
        _selected = step;
        step.IsSelected = true;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        Args.Clear();
        HasSelection = _selected != null && Steps.Contains(_selected);
        if (!HasSelection) return;
        SelectedTitle = _selected!.Number + " · " + _selected.Title;
        var defs = ArgDefs(_selected.Step);
        var args = ArgList(_selected.Step);
        for (int i = 0; defs != null && i < defs.Count; i++)
        {
            bool has = args != null && i < args.Count;
            string text = has ? _presenter.ValueText(args![i], defs[i]) : "à choisir";
            bool bound = has && args![i].Kind == ValueKind.FuncParam;
            bool err = !has || (args![i].Kind == ValueKind.NamedPos && args[i].Ref == null);
            Args.Add(new FnArgRow(defs[i].Name, text, bound, err, i));
        }
    }

    private IReadOnlyList<ParamDef>? ArgDefs(Step s)
    {
        if (s is ActionStep a) return DemoCatalog.Get(_station.FindObject(a.ObjectId)?.TypeCode ?? "")?.Action(a.ActionNo)?.Params;
        return null;
    }

    private static List<ValueRef>? ArgList(Step s) => s is ActionStep a ? a.Args : null;

    // ---------------------------------------------------------------- liaison des paramètres

    [RelayCommand]
    private async Task EditArg(FrameworkElement anchor)
    {
        var row = (FnArgRow)anchor.DataContext;
        if (_selected == null || !(_selected.Step is ActionStep step)) return;
        var defs = ArgDefs(step)!;
        var def = defs[row.Index];
        while (step.Args.Count <= row.Index) step.Args.Add(ValueRef.Const(def.Default));

        var items = new List<PickItem>();
        foreach (var p in _fn.Params.Where(p => Compatible(p, def)))
            items.Add(new PickItem("= " + p.Name, "paramètre de la fonction", ValueRef.FuncParam(p.Id), true, "tune"));
        if (def.Type == "Position")
        {
            foreach (var p in _station.Positions) items.Add(new PickItem(p.Name, p.Taught ? "apprise" : "non apprise", ValueRef.NamedPos(p.Id), p.Taught));
        }
        else if (def.Type == "Speed")
        {
            items.Add(new PickItem("Lente", "V1", ValueRef.Speed(1))); items.Add(new PickItem("Rapide", "V2", ValueRef.Speed(2))); items.Add(new PickItem("Approche", "V3", ValueRef.Speed(3)));
        }
        else if (def.Type == "Bool")
        {
            items.Add(new PickItem("Oui", null, ValueRef.Const(1))); items.Add(new PickItem("Non", null, ValueRef.Const(0)));
        }
        else items.Add(new PickItem("Valeur fixe…", "saisir au pavé numérique", "number", true, "dialpad"));

        var pick = await Pickers.PickAsync(anchor, def.Name, items, step.Args[row.Index]);
        ValueRef? result = null;
        if (pick is ValueRef vr) result = vr;
        else if (pick is string)
        {
            var n = await AppServices.Keypad.EditNumberAsync(anchor, new NumberEditRequest(def.Name, step.Args[row.Index].Number, def.Unit ?? "", def.Min ?? -100000, def.Max ?? 100000, 0.5, 2));
            if (n.HasValue) result = ValueRef.Const(n.Value);
        }
        if (result == null) return;
        var value = result; var old = step.Args[row.Index]; int idx = row.Index;
        _undo.Push(new DelegateEdit("Lier « " + def.Name + " »", () => { step.Args[idx] = old; AfterEdit(); }, () => { step.Args[idx] = value; AfterEdit(); }));
        step.Args[idx] = value;
        AfterEdit();
    }

    private static bool Compatible(ParamDef fp, ParamDef target)
    {
        if (fp.Type == target.Type) return true;
        bool num(string t) => t == "Real" || t == "Int" || t == "Duration" || t == "Percent";
        return num(fp.Type) && num(target.Type);
    }

    // ---------------------------------------------------------------- paramètres de la fonction

    [RelayCommand]
    private async Task AddParam(FrameworkElement anchor)
    {
        var items = new List<PickItem>
        {
            new PickItem("Position nommée", "ex. Position, Dépose", "Position", true, "place"),
            new PickItem("Vitesse", "Lente, Rapide, Approche…", "Speed", true, "speed"),
            new PickItem("Nombre (mm)", "ex. Approche", "Real", true, "straighten"),
            new PickItem("Entier", "ex. Nombre d'essais", "Int", true, "pin"),
            new PickItem("Durée (ms)", "ex. Temps de soufflage", "Duration", true, "timer"),
            new PickItem("Oui / non", "ex. Soufflage", "Bool", true, "toggle_on"),
        };
        var pick = await Pickers.PickAsync(anchor, "Type du paramètre", items);
        if (!(pick is string type)) return;
        string baseName = type == "Position" ? "Position" : type == "Speed" ? "Vitesse" : type == "Bool" ? "Option" : type == "Duration" ? "Durée" : type == "Int" ? "Nombre" : "Distance";
        string name = baseName; int n = 1;
        while (_fn.Params.Any(p => p.Name == name)) name = baseName + " " + (++n);
        var def = new ParamDef { Name = name, Type = type, Unit = type == "Real" ? "mm" : type == "Duration" ? "ms" : null, Default = type == "Speed" ? 2 : type == "Bool" ? 1 : type == "Duration" ? 100 : type == "Real" ? 10 : 0, Min = type == "Real" ? 0 : (double?)null, Max = type == "Real" ? 50 : (double?)null };
        _undo.Push(new DelegateEdit("Ajouter le paramètre « " + name + " »", () => { _fn.Params.Remove(def); Rebuild(); }, () => { _fn.Params.Add(def); Rebuild(); }));
        _fn.Params.Add(def);
        Rebuild();
    }

    [RelayCommand]
    private async Task RenameParam(FrameworkElement anchor)
    {
        var p = (FnParamViewModel)anchor.DataContext;
        var r = await AppServices.Keypad.EditTextAsync(anchor, new TextEditRequest("Nom du paramètre", p.Def.Name, "Nom affiché sur les cartes"));
        if (string.IsNullOrWhiteSpace(r) || r == p.Def.Name) return;
        string old = p.Def.Name;
        _undo.Push(new DelegateEdit("Renommer le paramètre", () => { p.Def.Name = old; Rebuild(); }, () => { p.Def.Name = r!; Rebuild(); }));
        p.Def.Name = r!;
        Rebuild();
    }

    [RelayCommand]
    private async Task EditDefault(FrameworkElement anchor)
    {
        var p = (FnParamViewModel)anchor.DataContext;
        var d = p.Def;
        if (d.Type == "Position") { AppServices.Toast.Show("Une position nommée se choisit à chaque utilisation de la fonction dans un process.", ToastKind.Info); return; }
        double? v;
        if (d.Type == "Speed")
        {
            var pick = await Pickers.PickAsync(anchor, "Vitesse par défaut", new List<PickItem> { new PickItem("Lente", "V1", 1.0), new PickItem("Rapide", "V2", 2.0), new PickItem("Approche", "V3", 3.0) }, d.Default);
            v = pick is double pd ? pd : (double?)null;
        }
        else if (d.Type == "Bool") v = d.Default != 0 ? 0 : 1;
        else v = await AppServices.Keypad.EditNumberAsync(anchor, new NumberEditRequest("Valeur par défaut · " + d.Name, d.Default, d.Unit ?? "", d.Min ?? -100000, d.Max ?? 100000, 1, d.Type == "Int" || d.Type == "Duration" ? 0 : 1));
        if (!v.HasValue) return;
        double old = d.Default, nv = v.Value;
        _undo.Push(new DelegateEdit("Valeur par défaut", () => { d.Default = old; Rebuild(); }, () => { d.Default = nv; Rebuild(); }));
        d.Default = nv;
        Rebuild();
    }

    [RelayCommand]
    private void RemoveParam(FnParamViewModel p)
    {
        int idx = _fn.Params.IndexOf(p.Def);
        if (idx < 0) return;
        bool used = _fn.Body.Steps.OfType<ActionStep>().Any(a => a.Args.Any(x => x.Kind == ValueKind.FuncParam && (x.Ref == p.Def.Id || x.Text == p.Def.Id.ToString("N"))));
        if (used) { AppServices.Toast.Show("Le paramètre « " + p.Def.Name + " » est utilisé par une action : déliez-le d'abord.", ToastKind.Warning); return; }
        var def = p.Def;
        _undo.Push(new DelegateEdit("Supprimer le paramètre", () => { _fn.Params.Insert(Math.Min(idx, _fn.Params.Count), def); Rebuild(); }, () => { _fn.Params.Remove(def); Rebuild(); }));
        _fn.Params.Remove(def);
        Rebuild();
    }

    // ---------------------------------------------------------------- étape sélectionnée

    [RelayCommand]
    private void DeleteSelected()
    {
        if (_selected == null) return;
        var step = _selected;
        int idx = Steps.IndexOf(step);
        var before = _fn.Body.Clone();
        Steps.Remove(step);
        _selected = null;
        var after = new Sequence(Steps.Select(s => s.Step));
        _undo.Push(new DelegateEdit("Supprimer l'action", () => { _fn.Body = before.Clone(); Rebuild(); }, () => { _fn.Body = after.Clone(); Rebuild(); }));
        _fn.Body = after;
        AfterEdit();
    }

    [RelayCommand]
    private void DuplicateSelected()
    {
        if (_selected == null) return;
        var copy = _selected.Step.CloneWithNewIds();
        var before = _fn.Body.Clone();
        int idx = Steps.IndexOf(_selected);
        Steps.Insert(idx + 1, Wrap(copy));
        var after = new Sequence(Steps.Select(s => s.Step));
        _undo.Push(new DelegateEdit("Dupliquer l'action", () => { _fn.Body = before.Clone(); Rebuild(); }, () => { _fn.Body = after.Clone(); Rebuild(); }));
        _fn.Body = after;
        AfterEdit();
        Select(Steps[idx + 1]);
    }

    // ---------------------------------------------------------------- validation

    private void Validate()
    {
        Issues.Clear();
        if (string.IsNullOrWhiteSpace(_fn.Name)) Issues.Add("La fonction n'a pas de nom.");
        if (_station.CustomFunctions.Any(f => f != _fn && f.Name == _fn.Name)) Issues.Add("Une autre fonction personnalisée porte déjà le nom « " + _fn.Name + " ».");
        if (_module.Children.Count > 0 && DemoCatalog.Get(_module.TypeCode)?.Functions.Any(f => f.Name == _fn.Name) == true) Issues.Add("Ce nom est déjà celui d'une fonction du module.");
        if (_fn.Body.Steps.Count == 0) Issues.Add("La fonction est vide : glissez des actions de base depuis la palette.");
        foreach (var s in Steps.Where(s => s.HasError)) Issues.Add("Action " + s.Number + " « " + s.Title + " » : un paramètre est à choisir.");
        foreach (var p in _fn.Params)
            if (!_fn.Body.Steps.OfType<ActionStep>().Any(a => a.Args.Any(x => x.Kind == ValueKind.FuncParam && (x.Ref == p.Id || x.Text == p.Id.ToString("N")))))
                Issues.Add("Le paramètre « " + p.Name + " » n'est utilisé par aucune action.");
        int errors = Issues.Count(i => i.StartsWith("La fonction") || i.StartsWith("Une autre") || i.StartsWith("Ce nom") || i.StartsWith("Action"));
        int warns = Issues.Count - errors;
        ValidationSummary = UiText.Plural(errors, "erreur", "erreurs") + " · " + UiText.Plural(warns, "avertissement", "avertissements");
        ValidationTone = errors > 0 ? Tone.Err : warns > 0 ? Tone.Warn : Tone.Ok;
    }

    // ---------------------------------------------------------------- essai et sortie

    [RelayCommand]
    private void TryRun()
    {
        if (Running) return;
        if (Steps.Count == 0) { AppServices.Toast.Show("Rien à exécuter : la fonction est vide.", ToastKind.Warning); return; }
        Running = true;
        int i = 0;
        _runTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        _runTimer.Tick += (_, _) =>
        {
            foreach (var s in Steps) s.IsRunning = false;
            if (i >= Steps.Count)
            {
                _runTimer!.Stop();
                Running = false;
                AppServices.Toast.Show("Essai terminé (simulation) : « " + _fn.Name + " » en ≈ " + DisplayFormat.Number(Steps.Sum(s => DemoCatalog.EstimatedSeconds(s.Step, _station)), 1) + " s.", ToastKind.Success);
                return;
            }
            Steps[i].IsRunning = true;
            i++;
        };
        _runTimer.Start();
    }

    [RelayCommand]
    private void Save()
    {
        Validate();
        if (Issues.Any(x => x.StartsWith("La fonction") || x.StartsWith("Une autre") || x.StartsWith("Ce nom") || x.StartsWith("Action")))
        { AppServices.Toast.Show("Corrigez les erreurs avant d'enregistrer la fonction.", ToastKind.Error); return; }
        AppServices.Toast.Show("Fonction « " + _fn.Name + " » enregistrée : disponible dans la palette de l'éditeur de process (crayon ✎).", ToastKind.Success);
    }

    [RelayCommand] private void Back() => AppServices.Navigation.Navigate("StationConfig:" + _station.Number + ":fn");
    [RelayCommand] private void Undo() => _undo.Undo();
    [RelayCommand] private void Redo() => _undo.Redo();
    [RelayCommand] private void UseInProcess() => AppServices.Navigation.Navigate("ProcessEditor:" + _station.Number + ":presser");

    public override void Dispose()
    {
        _runTimer?.Stop();
        base.Dispose();
    }
}
