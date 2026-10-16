using CommunityToolkit.Mvvm.ComponentModel;

namespace StdMachine.Hmi.Services;

public sealed record UserInfo(string Id, string Name, int Level, string LevelName, DateTime LastLogin);

/// <summary>
/// Utilisateur connecté et niveaux (docs/02 § 17). Maquette : les droits réels ne sont pas appliqués ;
/// le niveau sert à l'affichage et au cadenas de la connexion à la demande.
/// </summary>
public sealed class SessionService : ObservableObject
{
    public static readonly IReadOnlyList<UserInfo> Users = new[]
    {
        new UserInfo("amartin", "A. Martin", 1, "Régleur", new DateTime(2026, 10, 16, 6, 42, 0)),
        new UserInfo("brossi", "B. Rossi", 1, "Régleur", new DateTime(2026, 10, 15, 14, 3, 0)),
        new UserInfo("cdupont", "C. Dupont", 2, "Ingénieur process", new DateTime(2026, 10, 15, 9, 20, 0)),
        new UserInfo("admin", "Administrateur", 3, "Administrateur", new DateTime(2026, 10, 12, 8, 0, 0)),
        new UserInfo("op1", "Équipe du matin", 0, "Opérateur", new DateTime(2026, 10, 16, 5, 58, 0)),
    };

    private UserInfo _user = Users[0];

    public UserInfo User { get => _user; private set { if (SetProperty(ref _user, value)) { OnPropertyChanged(nameof(Level)); OnPropertyChanged(nameof(Label)); } } }

    public int Level => _user.Level;

    /// <summary>« Régleur · A. Martin ».</summary>
    public string Label => _user.LevelName + " · " + _user.Name;

    public void Login(UserInfo user) => User = user;

    public void Logout() => User = Users[4];

    public static string LevelName(int level)
    {
        switch (level)
        {
            case 0: return "Opérateur";
            case 1: return "Régleur";
            case 2: return "Ingénieur process";
            default: return "Administrateur";
        }
    }
}
