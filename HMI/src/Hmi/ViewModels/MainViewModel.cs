using Core.Access;
using Core.StateMachine;
using Hmi.Display;
using Hmi.Mvvm;
using Hmi.Services;

namespace Hmi.ViewModels;

public sealed class PageItem
{
    public PageItem(PageViewModel page, ActionType? requiredAction = null)
    {
        Page = page;
        RequiredAction = requiredAction;
    }

    public PageViewModel Page { get; }
    public string Title => Page.Title;

    /// <summary>Si renseigné, la page n'est visible que pour un utilisateur ayant ce droit.</summary>
    public ActionType? RequiredAction { get; }
}

public sealed class MainViewModel : ViewModelBase
{
    private readonly SessionService _session;
    private readonly PlcMonitor _monitor;
    private readonly List<PageItem> _allPages;
    private PageItem? _selectedPage;
    private bool _isLoginOpen;

    public MainViewModel(SessionService session, PlcMonitor monitor, IEnumerable<PageItem> pages)
    {
        _session = session;
        _monitor = monitor;
        _allPages = pages.ToList();
        _selectedPage = _allPages[0];

        Login = new LoginViewModel(session, () => IsLoginOpen = false);
        LoginCommand = new RelayCommand(ToggleLogin);

        _session.Changed += OnSessionChanged;
        _monitor.Updated += OnStatusUpdated;
    }

    public LoginViewModel Login { get; }
    public RelayCommand LoginCommand { get; }

    public IReadOnlyList<PageItem> VisiblePages =>
        _allPages.Where(p => p.RequiredAction is null || _session.CanDo(p.RequiredAction.Value)).ToList();

    public PageItem? SelectedPage
    {
        get => _selectedPage;
        set
        {
            if (value is null) return; // la sélection n'est jamais vidée par la liste
            if (Set(ref _selectedPage, value)) OnPropertyChanged(nameof(CurrentPage));
        }
    }

    public PageViewModel? CurrentPage => _selectedPage?.Page;

    public bool IsLoginOpen
    {
        get => _isLoginOpen;
        set => Set(ref _isLoginOpen, value);
    }

    public string UserText => _session.CurrentUser is { } u
        ? $"{u.Name} · {_session.Policy.LevelName(u.LevelRank)}"
        : "Non connecté";

    public string LoginButtonText => _session.CurrentUser is null ? "Connexion" : "Déconnexion";

    public SeqState MachineState => _monitor.Status.State;
    public string ModeText => "Mode " + StateDisplay.Label(_monitor.Status.Mode);

    private void ToggleLogin()
    {
        if (_session.CurrentUser is not null) _session.Logout();
        else
        {
            Login.Reset();
            IsLoginOpen = true;
        }
    }

    private void OnSessionChanged()
    {
        OnPropertyChanged(nameof(UserText));
        OnPropertyChanged(nameof(LoginButtonText));
        OnPropertyChanged(nameof(VisiblePages));
        // Une page devenue invisible n'est plus affichée.
        if (_selectedPage is not null && !VisiblePages.Contains(_selectedPage))
        {
            _selectedPage = _allPages[0];
            OnPropertyChanged(nameof(CurrentPage));
        }
        // La liste a été remplacée : la sélection doit être réaffichée.
        OnPropertyChanged(nameof(SelectedPage));
    }

    private void OnStatusUpdated()
    {
        OnPropertyChanged(nameof(MachineState));
        OnPropertyChanged(nameof(ModeText));
    }
}
