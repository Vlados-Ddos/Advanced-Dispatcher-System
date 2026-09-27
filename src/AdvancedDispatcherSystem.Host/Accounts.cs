using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;

namespace AdvancedDispatcherSystem.Host;

public sealed class UserAccount
{
    public string name, role, salt, hash;
}
public sealed class AuthSession
{
    public string name, role;
    public long expires;
}
public sealed class Accounts
{
    private readonly string path;
    private readonly object sync = new();
    private List<UserAccount> users;
    private readonly ConcurrentDictionary<string, AuthSession> sessions = new();
    private readonly SemaphoreSlim hashing = new(2, 2);
    private readonly string bootstrap;
    public bool RecoveryRequired { get; private set; }
    public Accounts(string directory, string bootstrap)
    {
        Directory.CreateDirectory(directory); path = Path.Combine(directory, "accounts.json"); this.bootstrap = bootstrap;
        if (OperatingSystem.IsWindows())
        {
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
        }
        users = new();
        if (File.Exists(path)) {
            try {
                if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("ACCOUNTS_SIZE");
                var loaded = Json.Read<List<UserAccount>>(File.ReadAllBytes(path));
                if (loaded == null || loaded.Count > 100 || loaded.Any(u => !ValidStoredAccount(u)) || loaded.Select(u => u.name).Distinct(StringComparer.Ordinal).Count() != loaded.Count)
                    throw new InvalidDataException("ACCOUNTS_SCHEMA");
                users = loaded;
            }
            catch (Exception e) when (e is JsonException || e is InvalidDataException) {
                // Preserve the exact damaged file; do not generate a password or silently
                // overwrite it. Only the existing local UMM bootstrap can repair accounts.
                File.Copy(path, path + ".invalid-" + Guid.NewGuid().ToString("N"));
                RecoveryRequired = true;
                Console.Error.WriteLine("ACCOUNTS_RECOVERY_REQUIRED " + e.GetType().Name);
            }
        }
        if (users.Count == 0 && !RecoveryRequired)
        {
            var password = Convert.ToHexString(RandomNumberGenerator.GetBytes(12));
            users.Add(Make("admin", "dispatcher", password)); Save();
            File.WriteAllText(Path.Combine(directory, "FIRST-LOGIN.txt"), "Advanced Dispatcher System\nUser: admin\nPassword: " + password + "\n\nYou can also open the dispatcher with the button in Unity Mod Manager. Change this password in the web settings.\n");
        }
    }
    private static bool ValidStoredAccount(UserAccount u)
    {
        if (u == null || string.IsNullOrWhiteSpace(u.name) || u.name == "local-owner" || u.name.Length > 48 || !u.name.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_') ||
            (u.role != "admin" && u.role != "dispatcher" && u.role != "viewer") || u.salt == null || u.hash == null) return false;
        try { return Convert.FromBase64String(u.salt).Length == 16 && Convert.FromBase64String(u.hash).Length == 32; }
        catch (FormatException) { return false; }
    }
    private static UserAccount Make(string name, string role, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        return new UserAccount { name = name, role = role, salt = Convert.ToBase64String(salt), hash = Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32)) };
    }
    private void Save(List<UserAccount> next = null)
    {
        string temp = path + ".tmp"; File.WriteAllBytes(temp, Json.Bytes(next ?? users)); File.Move(temp, path, true);
    }
    public object[] List() { lock (sync) return users.Select(x => (object)new { x.name, role = x.role == "admin" ? "dispatcher" : x.role }).ToArray(); }
    public bool Set(string name, string role, string password)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "local-owner" || name.Length > 48 || !name.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_') || (role != "dispatcher" && role != "viewer") || password == null || password.Length < 10 || password.Length > 128) return false;
        var replacement = Make(name, role, password);
        lock (sync)
        {
            int index = users.FindIndex(x => x.name == name);
            if (index < 0 && users.Count >= 100) return false;
            var next = new List<UserAccount>(users);
            if (index >= 0) next[index] = replacement; else next.Add(replacement);
            Save(next); users = next; RecoveryRequired = false;
            foreach (var item in sessions) if (item.Value.name == name) { item.Value.expires = -1; sessions.TryRemove(item.Key, out _); }
        }
        return true;
    }
    public async Task<(string Token, AuthSession Session)> Login(string name, string password, string ticket, bool loopback)
    {
        UserAccount account;
        bool ownerLogin = loopback && !string.IsNullOrEmpty(ticket) && Fixed(ticket, bootstrap);
        if (ownerLogin)
            account = new UserAccount { name = "local-owner", role = "admin" };
        else
        {
            if (name == null || name.Length > 48 || password == null || password.Length > 128) return default;
            lock (sync) account = users.FirstOrDefault(x => x.name == name);
            if (!await hashing.WaitAsync(TimeSpan.FromMilliseconds(250))) return default;
            try
            {
                var salt = account != null ? Convert.FromBase64String(account.salt) : new byte[16];
                var hash = await Task.Run(() => Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32));
                if (account == null || !CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(account.hash))) return default;
            }
            finally { hashing.Release(); }
        }
        lock (sync) {
            // A password/role edit may have occurred while PBKDF2 ran outside the lock.
            if (!ownerLogin && !users.Any(user => ReferenceEquals(user, account))) return default;
            foreach (var entry in sessions) if (entry.Value.expires < Environment.TickCount64) sessions.TryRemove(entry.Key, out _);
            if (sessions.Count >= 256) return default;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var session = new AuthSession { name = account.name, role = ownerLogin ? "admin" : account.role == "admin" ? "dispatcher" : account.role, expires = Environment.TickCount64 + 8 * 3600000L };
            sessions[token] = session; return (token, session);
        }
    }

    public AuthSession Get(string token)
    {
        if (token == null || !sessions.TryGetValue(token, out var value)) return null;
        if (value.expires < Environment.TickCount64) { sessions.TryRemove(token, out _); return null; }
        return value;
    }
    public void Logout(string token) { if (token != null && sessions.TryRemove(token, out var session)) session.expires = -1; }
    public static bool Fixed(string a, string b) => a != null && b != null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}

public sealed class RateGate
{
    private readonly ConcurrentDictionary<string, Bucket> buckets = new();
    private sealed class Bucket { public long until; public int count; }
    public bool Allow(string key, int maximum, int milliseconds)
    {
        long now = Environment.TickCount64;
        if (buckets.Count > 4096)
        {
            foreach (var item in buckets) if (item.Value.until < now) buckets.TryRemove(item.Key, out _);
            if (buckets.Count > 4096) return false;
        }
        var b = buckets.GetOrAdd(key, _ => new Bucket());
        lock (b) { if (now >= b.until) { b.until = now + milliseconds; b.count = 0; } return ++b.count <= maximum; }
    }
}
