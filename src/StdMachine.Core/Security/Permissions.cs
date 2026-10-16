using System.Security.Cryptography;

namespace StdMachine.Core.Security;

/// <summary>Un droit (docs/02 § 17) : code, libellé, niveau minimal par défaut, modifiable ou non.</summary>
public sealed record PermissionDef(string Code, string Name, int DefaultLevel, bool Locked = false, string Group = "");

public static class PermissionCatalog
{
    public static readonly IReadOnlyList<PermissionDef> All = new[]
    {
        new PermissionDef("machine.run", "Démarrer, mettre en pause, arrêter la machine", 0, false, "Machine"),
        new PermissionDef("machine.reset", "Réarmer", 0, false, "Machine"),
        new PermissionDef("machine.clear", "Acquitter (Clear)", 0, false, "Machine"),
        new PermissionDef("machine.abort", "Abort", 0, true, "Machine"),
        new PermissionDef("machine.mode", "Changer de mode", 1, false, "Machine"),
        new PermissionDef("station.reintegrate", "Réintégrer une station", 0, false, "Stations"),
        new PermissionDef("station.command", "Commander une station seule (Maintenance)", 1, false, "Stations"),
        new PermissionDef("override.machine", "Override machine", 1, false, "Stations"),
        new PermissionDef("override.local", "Override station, module, axe", 1, false, "Stations"),
        new PermissionDef("alarm.ack", "Acquitter un avertissement", 0, false, "Alarmes"),
        new PermissionDef("manual.execute", "Mode manuel : actions", 1, false, "Réglage"),
        new PermissionDef("manual.jog", "Mode manuel : jog", 1, false, "Réglage"),
        new PermissionDef("step.use", "Mode pas à pas", 1, false, "Réglage"),
        new PermissionDef("position.teach", "Apprendre une position", 1, false, "Réglage"),
        new PermissionDef("position.edit", "Saisir une position à la main", 2, false, "Réglage"),
        new PermissionDef("object.simulate", "Passer un objet en simulation", 2, false, "Réglage"),
        new PermissionDef("config.edit", "Modifier la configuration machine et station", 2, false, "Studio"),
        new PermissionDef("process.edit", "Modifier les process", 2, false, "Studio"),
        new PermissionDef("project.deploy", "Déployer", 2, false, "Studio"),
        new PermissionDef("project.manage", "Importer, exporter, restaurer un projet", 2, false, "Studio"),
        new PermissionDef("order.manage", "Créer, modifier, annuler un OF", 1, false, "Production"),
        new PermissionDef("order.run", "Lancer, suspendre un OF", 0, false, "Production"),
        new PermissionDef("part.remove", "Confirmer le retrait d'une pièce", 0, false, "Production"),
        new PermissionDef("quality.release", "Libérer après une réaction qualité", 1, false, "Qualité"),
        new PermissionDef("quality.reset", "Remettre à zéro les compteurs qualité", 2, false, "Qualité"),
        new PermissionDef("quality.config", "Régler les seuils qualité", 2, false, "Qualité"),
        new PermissionDef("user.manage", "Gérer les utilisateurs", 3, false, "Administration"),
        new PermissionDef("permission.manage", "Régler les droits et les niveaux", 3, false, "Administration"),
        new PermissionDef("system.settings", "Réglages système (connexion PLC, simulateur, interface usine)", 3, false, "Administration"),
        new PermissionDef("plant.reset", "Remettre à zéro les compteurs de production et de temps", 2, false, "Administration"),
        new PermissionDef("force.declare", "Déclarer un état de la machine, qualifier un arrêt", 0, false, "Forçages"),
        new PermissionDef("force.declareBlocking", "Déclarer un état qui bloque le démarrage", 1, false, "Forçages"),
        new PermissionDef("force.station", "Mettre une station hors production, la remettre", 1, false, "Forçages"),
        new PermissionDef("force.mover", "Mettre un mover hors service, le remettre", 1, false, "Forçages"),
        new PermissionDef("force.test", "Forçages d'essai en simulation", 2, false, "Forçages"),
    };
}

public sealed record PermissionDecision(bool Allowed, int RequiredLevel, string? Reason);

/// <summary>
/// Niveaux et droits (docs/02 § 17) : <c>Can</c> compare le niveau de l'utilisateur au niveau minimal du droit.
/// Sans connexion, la HMI est au niveau 0 ; Abort est toujours permis (D22).
/// </summary>
public sealed class PermissionService
{
    private readonly Dictionary<string, int> _levels = new Dictionary<string, int>();
    private readonly Dictionary<int, string> _levelNames = new Dictionary<int, string>
    {
        [0] = "Opérateur", [1] = "Régleur", [2] = "Ingénieur process", [3] = "Administrateur",
    };

    public PermissionService()
    {
        foreach (var p in PermissionCatalog.All) _levels[p.Code] = p.DefaultLevel;
    }

    public IReadOnlyDictionary<int, string> LevelNames => _levelNames;

    public int RequiredLevel(string code) => _levels.TryGetValue(code, out int l) ? l : throw new ArgumentException("Droit inconnu : " + code);

    public PermissionDecision Can(string code, int userLevel)
    {
        int required = RequiredLevel(code);
        if (code == "machine.abort") return new PermissionDecision(true, 0, null);
        if (userLevel >= required) return new PermissionDecision(true, required, null);
        return new PermissionDecision(false, required, "Niveau « " + LevelName(required) + " » (" + required + ") requis ; connectez-vous.");
    }

    public string LevelName(int level) => _levelNames.TryGetValue(level, out var n) ? n : "Niveau " + level;

    /// <summary>L'administrateur règle le niveau minimal d'un droit (0 à 9) ; un droit verrouillé n'est pas modifiable.</summary>
    public void SetLevel(string code, int level)
    {
        var def = PermissionCatalog.All.FirstOrDefault(p => p.Code == code) ?? throw new ArgumentException("Droit inconnu : " + code);
        if (def.Locked) throw new InvalidOperationException("Le droit « " + code + " » n'est pas modifiable.");
        if (level < 0 || level > 9) throw new ArgumentOutOfRangeException(nameof(level), "Les niveaux vont de 0 à 9.");
        _levels[code] = level;
    }

    public void SetLevelName(int level, string name)
    {
        if (level < 0 || level > 9) throw new ArgumentOutOfRangeException(nameof(level));
        _levelNames[level] = name;
    }
}

/// <summary>Empreinte d'un mot de passe ou d'un PIN : PBKDF2-SHA256, 100 000 itérations, sel aléatoire (docs/07 § 5.2).</summary>
public static class PasswordHasher
{
    public const int Iterations = 100_000;

    public static string Hash(string secret)
    {
        var salt = new byte[16];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(salt);
        return Format(Iterations, salt, Derive(secret, salt, Iterations));
    }

    public static bool Verify(string secret, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 3 || !int.TryParse(parts[0], out int iterations)) return false;
        var salt = Convert.FromBase64String(parts[1]);
        var expected = Convert.FromBase64String(parts[2]);
        var actual = Derive(secret, salt, iterations);
        // Comparaison en temps constant
        int diff = expected.Length ^ actual.Length;
        for (int i = 0; i < Math.Min(expected.Length, actual.Length); i++) diff |= expected[i] ^ actual[i];
        return diff == 0;
    }

    private static byte[] Derive(string secret, byte[] salt, int iterations)
    {
        using (var kdf = new Rfc2898DeriveBytes(secret, salt, iterations, HashAlgorithmName.SHA256)) return kdf.GetBytes(32);
    }

    private static string Format(int iterations, byte[] salt, byte[] hash) => iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(hash);
}

public sealed record UserAccount(string Id, string Name, int Level, string PasswordHash, string? PinHash, bool Active, bool MustChangePassword);

/// <summary>Session d'un utilisateur : niveau courant, déconnexion automatique après une durée sans toucher (10 minutes par défaut).</summary>
public sealed class UserSession
{
    private DateTime _lastActivity;

    public UserSession(DateTime now, TimeSpan? idleTimeout = null)
    {
        _lastActivity = now;
        IdleTimeout = idleTimeout ?? TimeSpan.FromMinutes(10);
    }

    public TimeSpan IdleTimeout { get; set; }

    public UserAccount? User { get; private set; }

    /// <summary>Sans connexion : niveau 0.</summary>
    public int Level => User?.Level ?? 0;

    public bool LoggedIn => User != null;

    public void Touch(DateTime now) => _lastActivity = now;

    public bool LogIn(UserAccount account, string secret, DateTime now)
    {
        if (!account.Active) return false;
        bool ok = PasswordHasher.Verify(secret, account.PasswordHash) || (account.PinHash != null && PasswordHasher.Verify(secret, account.PinHash));
        if (!ok) return false;
        User = account;
        _lastActivity = now;
        return true;
    }

    public void LogOut() => User = null;

    /// <summary>À appeler régulièrement : déconnecte après le délai ; renvoie vrai si la déconnexion vient d'avoir lieu.</summary>
    public bool Tick(DateTime now)
    {
        if (User == null || now - _lastActivity < IdleTimeout) return false;
        User = null;
        return true;
    }
}
