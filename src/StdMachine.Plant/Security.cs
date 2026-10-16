using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StdMachine.Plant;

/// <summary>Chiffrement des secrets au repos (mots de passe, clés de certificat) : jamais en clair dans <c>communications.json</c>.</summary>
public interface ISecretProtector
{
    string Protect(string plain);
    string Unprotect(string protectedText);
}

#if NETFRAMEWORK
/// <summary>Windows DPAPI, portée machine (docs/11 § 5).</summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    public string Protect(string plain)
        => Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.LocalMachine));

    public string Unprotect(string protectedText)
        => Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(protectedText), null, DataProtectionScope.LocalMachine));
}
#endif

/// <summary>Chiffrement AES-CBC + HMAC-SHA256 avec une clé fournie : pour les tests et les systèmes sans DPAPI.</summary>
public sealed class KeyedSecretProtector : ISecretProtector
{
    private readonly byte[] _encKey, _macKey;

    public KeyedSecretProtector(byte[] key)
    {
        using (var sha = SHA256.Create())
        {
            _encKey = sha.ComputeHash(Concat(key, new byte[] { 1 }));
            _macKey = sha.ComputeHash(Concat(key, new byte[] { 2 }));
        }
    }

    private static byte[] Concat(byte[] a, byte[] b) { var r = new byte[a.Length + b.Length]; Buffer.BlockCopy(a, 0, r, 0, a.Length); Buffer.BlockCopy(b, 0, r, a.Length, b.Length); return r; }

    public string Protect(string plain)
    {
        using (var aes = Aes.Create())
        {
            aes.Key = _encKey; aes.GenerateIV();
            byte[] cipher;
            using (var enc = aes.CreateEncryptor()) { var data = Encoding.UTF8.GetBytes(plain); cipher = enc.TransformFinalBlock(data, 0, data.Length); }
            var body = Concat(aes.IV, cipher);
            using (var mac = new HMACSHA256(_macKey)) return Convert.ToBase64String(Concat(mac.ComputeHash(body), body));
        }
    }

    public string Unprotect(string protectedText)
    {
        var all = Convert.FromBase64String(protectedText);
        if (all.Length < 32 + 16 + 16) throw new CryptographicException("Secret illisible.");
        var tag = all.Take(32).ToArray();
        var body = all.Skip(32).ToArray();
        using (var mac = new HMACSHA256(_macKey))
        {
            var expected = mac.ComputeHash(body);
            int diff = 0;
            for (int i = 0; i < 32; i++) diff |= expected[i] ^ tag[i];
            if (diff != 0) throw new CryptographicException("Secret altéré.");
        }
        using (var aes = Aes.Create())
        {
            aes.Key = _encKey; aes.IV = body.Take(16).ToArray();
            using (var dec = aes.CreateDecryptor())
            {
                var cipher = body.Skip(16).ToArray();
                return Encoding.UTF8.GetString(dec.TransformFinalBlock(cipher, 0, cipher.Length));
            }
        }
    }
}

public static class SecretProtectors
{
    /// <summary>DPAPI sous Windows (net48) ; ailleurs, une clé locale stockée à côté des réglages.</summary>
    public static ISecretProtector Default(string settingsDirectory)
    {
#if NETFRAMEWORK
        return new DpapiSecretProtector();
#else
        Directory.CreateDirectory(settingsDirectory);
        string keyFile = Path.Combine(settingsDirectory, "secrets.key");
        byte[] key;
        if (File.Exists(keyFile)) key = Convert.FromBase64String(File.ReadAllText(keyFile));
        else { key = new byte[32]; using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(key); File.WriteAllText(keyFile, Convert.ToBase64String(key)); }
        return new KeyedSecretProtector(key);
#endif
    }
}

/// <summary><c>communications.json</c> : réglages des canaux ; les secrets sont chiffrés dans le fichier (préfixe <c>enc:</c>) et jamais exposés par <see cref="Redacted"/>.</summary>
public sealed class CommunicationSettingsStore
{
    private const string Prefix = "enc:";
    private readonly string _path;
    private readonly ISecretProtector _protector;

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    public CommunicationSettingsStore(string path, ISecretProtector protector) { _path = path; _protector = protector; }

    public void Save(CommunicationSettings settings)
    {
        var copy = JsonSerializer.Deserialize<CommunicationSettings>(JsonSerializer.Serialize(settings, Options), Options)!;
        foreach (var ch in copy.Channels.Values)
            foreach (var k in ch.Secrets.Keys.ToList())
                if (!ch.Secrets[k].StartsWith(Prefix, StringComparison.Ordinal)) ch.Secrets[k] = Prefix + _protector.Protect(ch.Secrets[k]);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string tmp = _path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(copy, Options), new UTF8Encoding(false));
        if (File.Exists(_path)) File.Delete(_path);
        File.Move(tmp, _path);
    }

    public CommunicationSettings Load()
    {
        if (!File.Exists(_path)) return new CommunicationSettings();
        var s = JsonSerializer.Deserialize<CommunicationSettings>(File.ReadAllText(_path), Options) ?? new CommunicationSettings();
        foreach (var ch in s.Channels.Values)
            foreach (var k in ch.Secrets.Keys.ToList())
                if (ch.Secrets[k].StartsWith(Prefix, StringComparison.Ordinal)) ch.Secrets[k] = _protector.Unprotect(ch.Secrets[k].Substring(Prefix.Length));
        return s;
    }

    /// <summary>Copie pour l'affichage : les secrets sont remplacés par des points.</summary>
    public static CommunicationSettings Redacted(CommunicationSettings settings)
    {
        var copy = JsonSerializer.Deserialize<CommunicationSettings>(JsonSerializer.Serialize(settings, Options), Options)!;
        foreach (var ch in copy.Channels.Values)
            foreach (var k in ch.Secrets.Keys.ToList()) ch.Secrets[k] = ch.Secrets[k].Length == 0 ? string.Empty : "••••••••";
        return copy;
    }
}

/// <summary>Numéro <c>seq</c> par machine : croissant, jamais réutilisé, persistant (docs/11 § 4.2).</summary>
public interface ISeqStore
{
    long Next();
    long Last { get; }
}

public sealed class FileSeqStore : ISeqStore
{
    private readonly string _path;
    private readonly object _gate = new object();
    private long _last;

    public FileSeqStore(string path)
    {
        _path = path;
        if (File.Exists(path) && long.TryParse(File.ReadAllText(path).Trim(), out var v)) _last = v;
    }

    public long Last { get { lock (_gate) return _last; } }

    public long Next()
    {
        lock (_gate)
        {
            long n = _last + 1;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            // Écrit avant de rendre le numéro : une coupure ne peut jamais le réutiliser.
            File.WriteAllText(_path, n.ToString(System.Globalization.CultureInfo.InvariantCulture));
            _last = n;
            return n;
        }
    }
}

public sealed class MemorySeqStore : ISeqStore
{
    private long _last;
    public long Last => _last;
    public long Next() => ++_last;
}
