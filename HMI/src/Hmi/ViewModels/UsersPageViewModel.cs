using System.Collections.ObjectModel;
using Core.Access;
using Hmi.Mvvm;
using Hmi.Services;

namespace Hmi.ViewModels;

/// <summary>Une ligne de la grille « type d'action → niveau requis ».</summary>
public sealed class RightRowViewModel : ViewModelBase
{
    private readonly UsersPageViewModel _owner;
    private int _rank;

    public RightRowViewModel(UsersPageViewModel owner, ActionType action, string label, int rank)
    {
        _owner = owner;
        Action = action;
        Label = label;
        _rank = rank;
    }

    public ActionType Action { get; }
    public string Label { get; }

    public int RequiredRank
    {
        get => _rank;
        set
        {
            if (value == _rank) return;
            if (_owner.TrySetRequiredRank(Action, value)) Set(ref _rank, value);
            else OnPropertyChanged(); // remet l'affichage sur la valeur réelle
        }
    }

    public IReadOnlyList<UserLevel> Levels => _owner.Levels;
}

/// <summary>Ligne de la liste des utilisateurs (le PIN n'est jamais affiché).</summary>
public sealed record UserRow(string Name, string LevelName);

public sealed class UsersPageViewModel : PageViewModel
{
    private static readonly (ActionType Action, string Label)[] ActionLabels =
    {
        (ActionType.Command, "Commandes machine (Reset, Start, Stop, Hold, Clear)"),
        (ActionType.Abort, "Abort"),
        (ActionType.ModeChange, "Changement de mode"),
        (ActionType.ManualAction, "Actions manuelles"),
        (ActionType.OrderEdit, "Gestion des OF"),
        (ActionType.UserAdmin, "Administration des utilisateurs et droits"),
    };

    private readonly SessionService _session;
    private UserLevel? _selectedLevel;
    private UserRow? _selectedUser;
    private UserLevel? _newUserLevel;
    private string _levelName = "";
    private string _userName = "";
    private string _userPin = "";
    private string _message = "";

    public UsersPageViewModel(SessionService session) : base("Utilisateurs et droits")
    {
        _session = session;
        AddLevelCommand = new RelayCommand(
            () => Report(Policy.AddLevel(_session.CurrentUser, _levelName), "Niveau ajouté.", "Niveau refusé (nom vide ou déjà utilisé)."),
            () => CanAdmin);
        RenameLevelCommand = new RelayCommand(
            () => Report(Policy.RenameLevel(_session.CurrentUser, _selectedLevel!.Rank, _levelName), "Niveau renommé.", "Renommage refusé."),
            () => CanAdmin && _selectedLevel is not null);
        RemoveLevelCommand = new RelayCommand(
            () => Report(Policy.RemoveLevel(_session.CurrentUser, _selectedLevel!.Rank), "Niveau supprimé.", "Suppression refusée : niveau utilisé par un utilisateur ou requis par une action."),
            () => CanAdmin && _selectedLevel is not null);
        AddUserCommand = new RelayCommand(
            () => Report(Policy.AddUser(_session.CurrentUser, _userName, _userPin, _newUserLevel!.Rank), "Utilisateur ajouté.", "Utilisateur refusé (nom vide, PIN vide ou nom déjà pris)."),
            () => CanAdmin && _newUserLevel is not null);
        ChangeUserLevelCommand = new RelayCommand(
            () => Report(Policy.SetUserLevel(_session.CurrentUser, _selectedUser!.Name, _newUserLevel!.Rank), "Niveau de l'utilisateur modifié.", "Modification refusée (le dernier administrateur doit le rester)."),
            () => CanAdmin && _selectedUser is not null && _newUserLevel is not null);
        RemoveUserCommand = new RelayCommand(
            () => Report(Policy.RemoveUser(_session.CurrentUser, _selectedUser!.Name), "Utilisateur supprimé.", "Suppression refusée (le dernier administrateur doit rester)."),
            () => CanAdmin && _selectedUser is not null);

        _session.Changed += Reload;
        Reload();
    }

    private AccessPolicy Policy => _session.Policy;

    public bool CanAdmin => _session.CanDo(ActionType.UserAdmin);

    public ObservableCollection<UserLevel> Levels { get; } = new();
    public ObservableCollection<UserRow> Users { get; } = new();
    public ObservableCollection<RightRowViewModel> Rights { get; } = new();

    public RelayCommand AddLevelCommand { get; }
    public RelayCommand RenameLevelCommand { get; }
    public RelayCommand RemoveLevelCommand { get; }
    public RelayCommand AddUserCommand { get; }
    public RelayCommand ChangeUserLevelCommand { get; }
    public RelayCommand RemoveUserCommand { get; }

    public UserLevel? SelectedLevel
    {
        get => _selectedLevel;
        set { if (Set(ref _selectedLevel, value) && value is not null) LevelName = value.Name; }
    }

    public UserRow? SelectedUser
    {
        get => _selectedUser;
        set => Set(ref _selectedUser, value);
    }

    /// <summary>Niveau appliqué à un nouvel utilisateur, ou à l'utilisateur sélectionné.</summary>
    public UserLevel? NewUserLevel
    {
        get => _newUserLevel;
        set => Set(ref _newUserLevel, value);
    }

    public string LevelName { get => _levelName; set => Set(ref _levelName, value); }
    public string UserName { get => _userName; set => Set(ref _userName, value); }
    public string UserPin { get => _userPin; set => Set(ref _userPin, value); }
    public string Message { get => _message; private set => Set(ref _message, value); }

    /// <summary>Appelé par une ligne de la grille des droits.</summary>
    public bool TrySetRequiredRank(ActionType action, int rank)
    {
        var ok = Policy.SetRequiredRank(_session.CurrentUser, action, rank);
        Message = ok ? "Droit modifié." : "Modification refusée.";
        return ok;
    }

    private void Report(bool ok, string success, string failure)
    {
        Message = ok ? success : failure;
        if (ok) Reload();
    }

    private void Reload()
    {
        var selectedRank = _selectedLevel?.Rank;
        var selectedName = _selectedUser?.Name;

        Levels.Clear();
        foreach (var l in Policy.Levels) Levels.Add(l);
        Users.Clear();
        foreach (var u in Policy.Users) Users.Add(new UserRow(u.Name, Policy.LevelName(u.LevelRank)));

        Rights.Clear();
        foreach (var (action, label) in ActionLabels)
            Rights.Add(new RightRowViewModel(this, action, label, Policy.RequiredRank(action)));

        _selectedLevel = Levels.FirstOrDefault(l => l.Rank == selectedRank);
        _selectedUser = Users.FirstOrDefault(u => u.Name == selectedName);
        _newUserLevel = Levels.FirstOrDefault(l => l.Rank == _newUserLevel?.Rank) ?? Levels.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedLevel));
        OnPropertyChanged(nameof(SelectedUser));
        OnPropertyChanged(nameof(NewUserLevel));
        OnPropertyChanged(nameof(CanAdmin));
    }
}
