namespace Core.Access;

/// <summary>Persistance des niveaux, utilisateurs et droits.</summary>
public interface IUserStore
{
    AccessSettings Load();
    void Save(AccessSettings settings);
}

/// <summary>
/// Droits : qui peut faire quoi. Toute modification passe par un acteur qui doit avoir
/// le droit <see cref="ActionType.UserAdmin"/> ; une modification acceptée est sauvegardée.
/// </summary>
public sealed class AccessPolicy
{
    private readonly IUserStore _store;
    private readonly AccessSettings _settings;

    public AccessPolicy(IUserStore store)
    {
        _store = store;
        _settings = store.Load();
        // Un type d'action absent du fichier reprend sa valeur par défaut.
        foreach (var (action, rank) in AccessSettings.CreateDefault().Required)
            _settings.Required.TryAdd(action, rank);
    }

    public event Action? Changed;

    public IReadOnlyList<UserLevel> Levels => _settings.Levels.OrderBy(l => l.Rank).ToList();
    public IReadOnlyList<User> Users => _settings.Users.ToList();

    public int RequiredRank(ActionType action) => _settings.Required[action];

    public string LevelName(int rank) => _settings.Levels.FirstOrDefault(l => l.Rank == rank)?.Name ?? $"Niveau {rank}";

    /// <summary>Sans utilisateur connecté, aucune action protégée n'est permise.</summary>
    public bool CanDo(User? user, ActionType action) => user is not null && user.LevelRank >= RequiredRank(action);

    public User? Login(string name, string pin) =>
        _settings.Users.FirstOrDefault(u => u.Name == name && u.Pin == pin);

    // --- Modifications (réservées à UserAdmin) ---

    public bool SetRequiredRank(User? actor, ActionType action, int rank)
    {
        if (!CanDo(actor, ActionType.UserAdmin) || _settings.Levels.All(l => l.Rank != rank)) return false;
        // L'admin ne peut pas se retirer le droit d'administrer.
        if (action == ActionType.UserAdmin && actor!.LevelRank < rank) return false;
        _settings.Required[action] = rank;
        Commit();
        return true;
    }

    public bool AddLevel(User? actor, string name)
    {
        if (!CanDo(actor, ActionType.UserAdmin) || string.IsNullOrWhiteSpace(name)) return false;
        if (_settings.Levels.Any(l => l.Name == name)) return false;
        _settings.Levels.Add(new UserLevel { Rank = _settings.Levels.Max(l => l.Rank) + 1, Name = name.Trim() });
        Commit();
        return true;
    }

    public bool RenameLevel(User? actor, int rank, string name)
    {
        var level = _settings.Levels.FirstOrDefault(l => l.Rank == rank);
        if (!CanDo(actor, ActionType.UserAdmin) || level is null || string.IsNullOrWhiteSpace(name)) return false;
        if (_settings.Levels.Any(l => l != level && l.Name == name)) return false;
        level.Name = name.Trim();
        Commit();
        return true;
    }

    /// <summary>Un niveau utilisé par un utilisateur ou requis par une action ne peut pas être supprimé.</summary>
    public bool RemoveLevel(User? actor, int rank)
    {
        var level = _settings.Levels.FirstOrDefault(l => l.Rank == rank);
        if (!CanDo(actor, ActionType.UserAdmin) || level is null || _settings.Levels.Count <= 1) return false;
        if (_settings.Users.Any(u => u.LevelRank == rank) || _settings.Required.Values.Contains(rank)) return false;
        _settings.Levels.Remove(level);
        Commit();
        return true;
    }

    public bool AddUser(User? actor, string name, string pin, int levelRank)
    {
        if (!CanDo(actor, ActionType.UserAdmin) || string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(pin)) return false;
        if (_settings.Levels.All(l => l.Rank != levelRank) || _settings.Users.Any(u => u.Name == name)) return false;
        _settings.Users.Add(new User { Name = name.Trim(), Pin = pin, LevelRank = levelRank });
        Commit();
        return true;
    }

    public bool SetUserLevel(User? actor, string name, int levelRank)
    {
        var user = _settings.Users.FirstOrDefault(u => u.Name == name);
        if (!CanDo(actor, ActionType.UserAdmin) || user is null || _settings.Levels.All(l => l.Rank != levelRank)) return false;
        var previous = user.LevelRank;
        user.LevelRank = levelRank;
        // Le dernier administrateur ne peut pas perdre ses droits.
        if (!_settings.Users.Any(u => u.LevelRank >= RequiredRank(ActionType.UserAdmin)))
        {
            user.LevelRank = previous;
            return false;
        }
        Commit();
        return true;
    }

    public bool RemoveUser(User? actor, string name)
    {
        var user = _settings.Users.FirstOrDefault(u => u.Name == name);
        if (!CanDo(actor, ActionType.UserAdmin) || user is null) return false;
        var others = _settings.Users.Where(u => u != user);
        if (!others.Any(u => u.LevelRank >= RequiredRank(ActionType.UserAdmin))) return false;
        _settings.Users.Remove(user);
        Commit();
        return true;
    }

    private void Commit()
    {
        _store.Save(_settings);
        Changed?.Invoke();
    }
}
