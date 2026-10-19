using CommunityToolkit.Mvvm.ComponentModel;
using StdMachine.Core;
using StdMachine.Simulation.Demo;

namespace StdMachine.Hmi.Services;

/// <summary>Station courante, partagée par toutes les pages (docs/08 § 4.2) : en changer dans une page la change pour les autres.</summary>
public sealed class SelectionService : ObservableObject
{
    private int _station = DemoProject.Pressage01;

    public int Station
    {
        get => _station;
        set { if (SetProperty(ref _station, value)) StationChanged?.Invoke(this, value); }
    }

    public event EventHandler<int>? StationChanged;
}

public enum ValidationSeverity
{
    Error,
    Warning,
    Information,
}

public sealed record ValidationIssue(string Code, ValidationSeverity Severity, string Message, int? Station, Guid? StepId, string Page);

/// <summary>Projet de la maquette : le projet de démonstration, modifié en mémoire (annuler / refaire actifs, pas d'enregistrement).</summary>
public sealed class ProjectService : ObservableObject
{
    private IReadOnlyList<ValidationIssue> _issues = new ValidationIssue[0];

    private readonly bool _realValidation;

    public ProjectService() : this(DemoProject.Build(), false) { }

    /// <param name="realValidation">Vrai : la barre de validation utilise les règles V001 à V113 de <c>ProjectValidator</c> (projet complet, mode simulateur) ; faux : validation de démonstration.</param>
    public ProjectService(MachineProject project, bool realValidation)
    {
        _realValidation = realValidation;
        Project = project;
        Undo = new UndoService();
        Undo.Changed += (_, _) => Revalidate();
        Revalidate();
    }

    public MachineProject Project { get; }
    public UndoService Undo { get; }

    /// <summary>Vrai dès que le projet diffère de la version déployée (point bleu sur Déployer et sur Studio).</summary>
    public bool IsDirty { get; private set; }

    public string? DeployedHash { get; private set; } = "9F3A1C07D2E84B66";
    public DateTime? DeployedAt { get; private set; } = new DateTime(2026, 10, 14, 16, 5, 0);

    public IReadOnlyList<ValidationIssue> Issues { get => _issues; private set => SetProperty(ref _issues, value); }
    public int ErrorCount => _issues.Count(i => i.Severity == ValidationSeverity.Error);
    public int WarningCount => _issues.Count(i => i.Severity == ValidationSeverity.Warning);

    public event EventHandler? Changed;

    /// <summary>À appeler après chaque modification du projet.</summary>
    public void NotifyEdited()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(IsDirty));
        Revalidate();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void MarkDeployed(string hash, DateTime at)
    {
        DeployedHash = hash;
        DeployedAt = at;
        IsDirty = false;
        OnPropertyChanged(nameof(DeployedHash));
        OnPropertyChanged(nameof(DeployedAt));
        OnPropertyChanged(nameof(IsDirty));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<ValidationIssue> RealIssues(StdMachine.Core.Catalog.CatalogDocument catalog)
    {
        string PageOf(string page) => page == "ProcessEditor" ? "Éditeur" : page == "StationConfig" ? "Studio station" : page == "MachineConfig" ? "Studio machine" : page == "Quality" ? "Qualité" : page;
        return StdMachine.Core.Validation.ProjectValidator.Validate(Project, catalog)
            .Select(i => new ValidationIssue(i.Code,
                i.Severity == StdMachine.Core.Validation.Severity.Error ? ValidationSeverity.Error : i.Severity == StdMachine.Core.Validation.Severity.Warning ? ValidationSeverity.Warning : ValidationSeverity.Information,
                i.Message, i.Link.StationNumber, i.Link.StepId, PageOf(i.Link.Page)))
            .OrderBy(i => i.Severity).ThenBy(i => i.Code).ToList();
    }

    public void Revalidate()
    {
        Issues = _realValidation && AppServices.Compilation.Catalog != null ? RealIssues(AppServices.Compilation.Catalog) : MockValidator.Validate(Project);
        OnPropertyChanged(nameof(ErrorCount));
        OnPropertyChanged(nameof(WarningCount));
    }
}

/// <summary>
/// Validation de démonstration : quelques règles de docs/07 § 4.2 appliquées au projet en mémoire (V001, V004, V008, V101,
/// V109, V111) pour alimenter la barre de validation. La validation complète arrive en phase 2.
/// </summary>
public static class MockValidator
{
    public static IReadOnlyList<ValidationIssue> Validate(MachineProject project)
    {
        var list = new List<ValidationIssue>();
        foreach (var st in project.Stations)
        {
            var used = new HashSet<Guid>();
            foreach (var pr in st.Processes)
            {
                foreach (var step in pr.AllSteps())
                {
                    if (step is ActionStep a) used.Add(a.ObjectId);
                    if (step is FunctionStep f) used.Add(f.ModuleObjectId);
                    IEnumerable<ValueRef> args = step is ActionStep a2 ? a2.Args : step is FunctionStep f2 ? f2.Args : Enumerable.Empty<ValueRef>();
                    foreach (var v in args)
                    {
                        if (v.Kind != ValueKind.NamedPos) continue;
                        var pos = st.Positions.FirstOrDefault(p => p.Id == v.Ref);
                        if (pos != null && !pos.Taught)
                            list.Add(new ValidationIssue("V001", ValidationSeverity.Error, "Position « " + pos.Name + " » non apprise (" + st.Name + ", " + pr.Name + ").", st.Number, step.Id, "Studio station"));
                    }
                    if (step is WaitConditionStep w && (w.Condition.Terms.Count == 0))
                        list.Add(new ValidationIssue("V008", ValidationSeverity.Error, "Condition incomplète dans « " + pr.Name + " » (" + st.Name + ").", st.Number, step.Id, "Éditeur"));
                    if (step is ParallelBlock pb)
                    {
                        var seen = new Dictionary<Guid, int>();
                        for (int i = 0; i < pb.Branches.Count; i++)
                            foreach (var o in ProcessTree.ObjectsUsed(pb.Branches[i]))
                            {
                                if (seen.TryGetValue(o, out var other) && other != i)
                                    list.Add(new ValidationIssue("V004", ValidationSeverity.Error, "Un objet (« " + (st.FindObject(o)?.Name ?? "?") + " ») est utilisé dans deux branches parallèles (" + st.Name + ").", st.Number, step.Id, "Éditeur"));
                                else seen[o] = i;
                            }
                    }
                }
            }
            foreach (var o in st.AllObjects())
            {
                if (o.Simulated)
                    list.Add(new ValidationIssue("V101", ValidationSeverity.Warning, "Objet « " + o.Name + " » en simulation (" + st.Name + ").", st.Number, null, "Studio station"));
                if (o.TypeCode != "Group" && o.TypeCode != "CartesianRobot" && !used.Contains(o.Id) && !st.AllObjects().Any(p => p.Children.Contains(o) && used.Contains(p.Id)) && !o.Disabled)
                    list.Add(new ValidationIssue("V109", ValidationSeverity.Warning, "Objet « " + o.Name + " » déclaré mais jamais utilisé (" + st.Name + ").", st.Number, null, "Studio station"));
            }
        }
        foreach (var st in project.Stations)
            if (!project.GroupsOf(st).Any())
                list.Add(new ValidationIssue("V111", ValidationSeverity.Warning, "Station « " + st.Name + " » membre d'aucune fonction.", st.Number, null, "Studio machine"));
        return list
            .GroupBy(i => (i.Code, i.Message))
            .Select(g => g.First())
            .OrderBy(i => i.Severity)
            .ThenBy(i => i.Code)
            .ToList();
    }
}

/// <summary>Commandes vers le PLC avec retour visuel (docs/08 § 7.6) : indicateur rotatif, refus expliqué par un toast.</summary>
public sealed class PlcCommands : ObservableObject
{
    private readonly IPlcClient _client;
    private string? _busy;

    public PlcCommands(IPlcClient client) { _client = client; }

    public IPlcClient Client => _client;

    /// <summary>Clé de la commande en cours (pour l'indicateur rotatif du bouton), ou null.</summary>
    public string? Busy { get => _busy; private set { if (SetProperty(ref _busy, value)) OnPropertyChanged(nameof(IsBusy)); } }
    public bool IsBusy => _busy != null;

    public async Task<bool> RunAsync(string key, Func<Task<CommandResult>> action, string? successText = null)
    {
        if (Busy != null) return false;
        Busy = key;
        try
        {
            var result = await action().ConfigureAwait(true);
            if (!result.Accepted)
            {
                AppServices.Toast.Show(result.Reason ?? Resources.Res.Get("Common.Refused"), ToastKind.Error);
                return false;
            }
            if (successText != null) AppServices.Toast.Show(successText, ToastKind.Success);
            return true;
        }
        catch (OperationCanceledException) { return false; }
        finally { Busy = null; }
    }

    public Task<bool> SendAsync(StateCommand c) => RunAsync("cmd." + c, () => _client.SendCommandAsync(c, CancellationToken.None));
    public Task<bool> SendStationAsync(int station, StateCommand c) => RunAsync("scmd." + station + "." + c, () => _client.SendStationCommandAsync(station, c, CancellationToken.None));

    /// <summary>« Finir les pièces » (docs/05 § 5.7).</summary>
    public Task<bool> StartDrainAsync() => RunAsync("drain", () => _client.StartDrainAsync(CancellationToken.None));

    /// <summary>« Annuler la fin » : le chargement reprend.</summary>
    public Task<bool> CancelDrainAsync() => RunAsync("drain", () => _client.CancelDrainAsync(CancellationToken.None));
}
