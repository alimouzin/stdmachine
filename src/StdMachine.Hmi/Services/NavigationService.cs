using CommunityToolkit.Mvvm.ComponentModel;

namespace StdMachine.Hmi.Services;

/// <summary>Reçoit les paramètres de navigation (station, objet, carte à mettre en évidence…).</summary>
public interface INavigationAware
{
    void OnNavigatedTo(object? args);
}

/// <summary>
/// Description d'une page : clé de base, titre, icône du rail, fabrique de vue-modèle (qui reçoit les arguments de la clé,
/// par exemple « Station:3 » → ["3"]), clés à capturer et taille de capture.
/// </summary>
public sealed record PageInfo(string Key, string Title, string Icon, Func<string[], object> Create, int CaptureWidth = 1920, int CaptureHeight = 1080,
    string[]? CaptureKeys = null)
{
    public IEnumerable<string> Keys => CaptureKeys ?? new[] { Key };
}

/// <summary>Pages, paramètres de navigation et historique « Retour » (docs/07 § 6, docs/08 § 4.2).</summary>
public sealed class NavigationService : ObservableObject
{
    private readonly Dictionary<string, PageInfo> _pages = new Dictionary<string, PageInfo>();
    private readonly List<PageInfo> _order = new List<PageInfo>();
    private readonly Stack<(string Key, object? Args)> _history = new Stack<(string, object?)>();
    private object? _current;
    private string? _currentKey;
    private object? _currentArgs;

    public IReadOnlyList<PageInfo> Pages => _order;

    public void Register(PageInfo page)
    {
        _pages[page.Key] = page;
        _order.Add(page);
    }

    public PageInfo? Find(string key) => _pages.TryGetValue(BaseKey(key), out var p) ? p : null;

    private static string BaseKey(string key)
    {
        int i = key.IndexOf(':');
        return i < 0 ? key : key.Substring(0, i);
    }

    private static string[] KeyArgs(string key)
    {
        int i = key.IndexOf(':');
        return i < 0 ? new string[0] : key.Substring(i + 1).Split(':');
    }

    public object? Current { get => _current; private set => SetProperty(ref _current, value); }

    /// <summary>Clé complète de la page courante (« Station:2 »).</summary>
    public string? CurrentKey { get => _currentKey; private set { if (SetProperty(ref _currentKey, value)) OnPropertyChanged(nameof(CurrentBaseKey)); } }

    public string? CurrentBaseKey => _currentKey == null ? null : BaseKey(_currentKey);

    public bool CanGoBack => _history.Count > 0;

    /// <summary>
    /// Ouvre une page. <paramref name="args"/> est transmis à <see cref="INavigationAware"/> (élément à mettre en évidence) ;
    /// <paramref name="pushHistory"/> mémorise la page quittée pour « Retour » (arrivée par un lien : alarme, qualité, pièce).
    /// </summary>
    public void Navigate(string key, object? args = null, bool pushHistory = false)
    {
        var page = Find(key) ?? throw new KeyNotFoundException("Page inconnue : " + key);
        if (pushHistory && _currentKey != null) _history.Push((_currentKey, _currentArgs));
        var vm = page.Create(KeyArgs(key));
        (Current as IDisposable)?.Dispose();
        (vm as INavigationAware)?.OnNavigatedTo(args);
        _currentArgs = args;
        CurrentKey = key;
        Current = vm;
        OnPropertyChanged(nameof(CanGoBack));
    }

    public void Back()
    {
        if (_history.Count == 0) return;
        var (key, args) = _history.Pop();
        Navigate(key, args);
        OnPropertyChanged(nameof(CanGoBack));
    }
}
