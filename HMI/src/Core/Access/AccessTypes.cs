namespace Core.Access;

/// <summary>Types d'action dont le niveau requis est réglé par l'admin.</summary>
public enum ActionType
{
    /// <summary>Reset, Start, Stop, Hold, Clear.</summary>
    Command,
    Abort,
    ModeChange,
    ManualAction,
    OrderEdit,
    UserAdmin,
}

/// <summary>Niveau d'utilisateur. Plus le rang est grand, plus le niveau est élevé.</summary>
public sealed class UserLevel
{
    public int Rank { get; set; }
    public string Name { get; set; } = "";
}

public sealed class User
{
    public string Name { get; set; } = "";
    public string Pin { get; set; } = "";
    public int LevelRank { get; set; }
}

/// <summary>Contenu persistant : niveaux, utilisateurs, niveau requis par type d'action.</summary>
public sealed class AccessSettings
{
    public List<UserLevel> Levels { get; set; } = new();
    public List<User> Users { get; set; } = new();
    public Dictionary<ActionType, int> Required { get; set; } = new();

    public static AccessSettings CreateDefault() => new()
    {
        Levels =
        {
            new UserLevel { Rank = 0, Name = "Opérateur" },
            new UserLevel { Rank = 1, Name = "Régleur" },
            new UserLevel { Rank = 2, Name = "Process" },
            new UserLevel { Rank = 3, Name = "Admin" },
        },
        Users =
        {
            new User { Name = "Opérateur", Pin = "1111", LevelRank = 0 },
            new User { Name = "Régleur", Pin = "2222", LevelRank = 1 },
            new User { Name = "Process", Pin = "3333", LevelRank = 2 },
            new User { Name = "Admin", Pin = "0000", LevelRank = 3 },
        },
        Required =
        {
            [ActionType.Command] = 0,
            [ActionType.Abort] = 0,
            [ActionType.ModeChange] = 1,
            [ActionType.ManualAction] = 1,
            [ActionType.OrderEdit] = 2,
            [ActionType.UserAdmin] = 3,
        },
    };
}
