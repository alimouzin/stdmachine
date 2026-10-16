using CommunityToolkit.Mvvm.ComponentModel;
using StdMachine.Core;
using StdMachine.Hmi.Services;

namespace StdMachine.Hmi.Editor;

/// <summary>
/// Canevas d'un process : mise en page, sélection, état d'exécution en direct. Utilisé en édition (éditeur de process)
/// et en lecture seule avec suivi de l'exécution (page Station), avec le même rendu (docs/08 § 6.2 et § 7.2).
/// </summary>
public sealed partial class ProcessCanvasViewModel : ObservableObject
{
    private readonly ProcessLayoutEngine _engine = new ProcessLayoutEngine();
    private double _viewportWidth = 1100;
    private ProcessLayout _layout = new ProcessLayout();

    public ProcessCanvasViewModel(MachineProject project, StationConfig? station, ProcessDefinition process, ProcessLevel level)
    {
        Project = project;
        Station = station;
        Process = process;
        Level = level;
        Rebuild();
    }

    public MachineProject Project { get; }
    public StationConfig? Station { get; }
    public ProcessDefinition Process { get; }
    public ProcessLevel Level { get; }

    public ProcessLayout Layout => _layout;
    public IReadOnlyList<StepCardModel> Cards => _layout.Cards;
    public IReadOnlyList<BlockFrameModel> Blocks => _layout.Blocks;
    public IReadOnlyList<ConnectorModel> Connectors => _layout.Connectors;
    public IReadOnlyList<PlaceholderModel> Placeholders => _layout.Placeholders;
    public IReadOnlyList<LaneModel> Lanes => _layout.Lanes;
    public IReadOnlyList<BranchLabelModel> BranchLabels => _layout.BranchLabels;
    public IReadOnlyList<EndMarkerModel> EndMarkers => _layout.End == null ? new EndMarkerModel[0] : new[] { _layout.End };
    public double CanvasWidth => _layout.Width;
    public double CanvasHeight => _layout.Height;

    /// <summary>Vrai en mode « Suivre l'exécution » (lecture seule, états en direct).</summary>
    [ObservableProperty] private bool _isFollowing;
    [ObservableProperty] private Guid? _selectedStepId;
    [ObservableProperty] private bool _hideEmptyLanes = true;
    [ObservableProperty] private bool _isDragActive;
    [ObservableProperty] private double _zoom = 1.0;

    /// <summary>Largeur disponible : les couloirs s'y répartissent (au moins 262 px chacun).</summary>
    public void SetViewportWidth(double width)
    {
        if (Math.Abs(width - _viewportWidth) < 2) return;
        _viewportWidth = width;
        Rebuild();
    }

    public void Rebuild()
    {
        var options = new ProcessLayoutOptions
        {
            Level = Level,
            Station = Station,
            Project = Project,
            ViewportWidth = _viewportWidth,
            EndText = Process.Kind == ProcessKind.General ? "Fin · retour à l'étape 1" : Process.Kind == ProcessKind.Background ? "Retour au début du cycle" : "Fin d'opération",
            Validation = StepValidation,
        };
        _layout = _engine.Build(Process, options);
        OnPropertyChanged(nameof(Layout));
        OnPropertyChanged(nameof(Cards));
        OnPropertyChanged(nameof(Blocks));
        OnPropertyChanged(nameof(Connectors));
        OnPropertyChanged(nameof(Placeholders));
        OnPropertyChanged(nameof(Lanes));
        OnPropertyChanged(nameof(BranchLabels));
        OnPropertyChanged(nameof(EndMarkers));
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        ApplySelection();
        if (LastRuntime != null) ApplyRuntime(LastRuntime);
    }

    private (string? Error, string? Warning) StepValidation(Guid id)
    {
        string? err = null, warn = null;
        foreach (var i in AppServices.Project.Issues)
        {
            if (i.StepId != id) continue;
            if (i.Severity == ValidationSeverity.Error) err = err ?? i.Message;
            else warn = warn ?? i.Message;
        }
        return (err, warn);
    }

    // ---------------------------------------------------------------- sélection

    partial void OnSelectedStepIdChanged(Guid? value) => ApplySelection();

    private void ApplySelection()
    {
        foreach (var c in _layout.Cards) c.IsSelected = c.Id == SelectedStepId;
        foreach (var b in _layout.Blocks) b.IsSelected = b.Id == SelectedStepId;
    }

    public StepCardModel? CardOf(Guid id) => _layout.Cards.FirstOrDefault(c => c.Id == id);

    // ---------------------------------------------------------------- exécution en direct

    public StationStatus? LastRuntime { get; private set; }

    /// <summary>Applique l'état d'exécution d'une station si elle exécute ce process ; sinon efface les états.</summary>
    public void ApplyRuntime(StationStatus? status)
    {
        LastRuntime = status;
        bool match = status != null && status.RunningProcessId == Process.Id;
        var map = match ? status!.Steps.ToDictionary(s => s.StepId) : new Dictionary<Guid, StepRuntime>();
        foreach (var c in _layout.Cards)
        {
            map.TryGetValue(c.Id, out var r);
            c.ShowRuntime = match;
            c.Apply(r, c.DurationText.Replace("≈ ", ""));
        }
        foreach (var b in _layout.Blocks)
        {
            map.TryGetValue(b.Id, out var r);
            b.State = r?.State ?? StepRunState.Inactive;
            b.Result = r?.BlockResult;
        }
        foreach (var k in _layout.Connectors)
        {
            var to = k.To.HasValue ? _layout.Cards.FirstOrDefault(c => c.Id == k.To.Value) : null;
            var from = k.From.HasValue ? _layout.Cards.FirstOrDefault(c => c.Id == k.From.Value) : null;
            if (!match || to == null) { k.State = ConnectorState.Idle; continue; }
            bool toActive = to.State == StepRunState.Running || to.State == StepRunState.WaitingCondition || to.State == StepRunState.Retry || to.State == StepRunState.Locked;
            if (toActive && (from == null || from.State == StepRunState.Done)) k.State = ConnectorState.Active;
            else if (to.State == StepRunState.Done) k.State = ConnectorState.Done;
            else k.State = ConnectorState.Idle;
        }
    }

    /// <summary>Carte à garder visible : la première en cours, sinon l'erreur, sinon la suivante.</summary>
    public StepCardModel? FocusCard()
    {
        return _layout.Cards.FirstOrDefault(c => c.State == StepRunState.Error)
            ?? _layout.Cards.FirstOrDefault(c => c.State == StepRunState.Running || c.State == StepRunState.WaitingCondition || c.State == StepRunState.Retry)
            ?? _layout.Cards.FirstOrDefault(c => c.State == StepRunState.Next);
    }
}
