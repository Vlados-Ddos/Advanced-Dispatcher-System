using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using AdvancedDispatcherSystem.Core;

namespace AdvancedDispatcherSystem.Host;

public sealed class UserAccount
{
    public string name, role, salt, hash;
}
public sealed class AuthSession
{
    public string name, role;
    public long expires;
    // These fields are Host-only lifecycle state. A browser may reconnect with
    // the same cookie during the short disconnect grace period, while a second
    // login for the account is rejected until that lease expires.
    internal int connections;
    internal long disconnectedAt;
    internal readonly CancellationTokenSource revoked = new();
}
public sealed class Accounts
{
    // Session expiry is an idle timeout. Every authenticated user action
    // renews this bounded lease; background health polling does not.
    public const long SessionLifetimeMs = 15 * 60 * 1000;
    // A browser may suspend a tab or briefly lose a VPN/Wi-Fi route. Keep the
    // single-account lease recoverable for a bounded 15 minutes, while still
    // releasing it when the client really stays away.
    public const long DisconnectGraceMs = 15 * 60 * 1000;
    private readonly string path;
    private readonly object sync = new();
    private List<UserAccount> users;
    private readonly ConcurrentDictionary<string, AuthSession> sessions = new();
    private readonly SemaphoreSlim hashing = new(2, 2);
    private readonly string bootstrap;
    private readonly long disconnectGraceMs;
    private readonly Dictionary<string,long> usedOwnerCodes=new();
    private readonly long ownerCodesSince=Protocol.Now;
    public bool RecoveryRequired { get; private set; }
    public Accounts(string directory, string bootstrap, long disconnectGraceMs = DisconnectGraceMs)
    {
        Directory.CreateDirectory(directory); path = Path.Combine(directory, "accounts.json"); this.bootstrap = bootstrap;
        this.disconnectGraceMs = Math.Max(1, disconnectGraceMs);
        if (OperatingSystem.IsWindows())
        {
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
        }
        else
        {
            // Portable hosts keep password hashes and private TLS keys in this
            // directory too. Do not inherit a world-readable process umask.
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
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
        if (users.Count == 0 && !RecoveryRequired && !File.Exists(path))
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
    private enum Mutation { Upsert, Create, Update }
    // Legacy in-process setup/recovery callers intentionally use upsert. HTTP
    // profile operations use Create/Update so user intent is checked atomically.
    public bool Set(string name, string role, string password) => Change(name, role, password, Mutation.Upsert) == null;
    public string Create(string name, string role, string password) => Change(name, role, password, Mutation.Create);
    public string Update(string name, string role, string password) => Change(name, role, password, Mutation.Update);
    private string Change(string name, string role, string password, Mutation mutation)
    {
        if (string.IsNullOrWhiteSpace(name) || name == "local-owner" || name.Length > 48 || !name.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_') ||
            (role != "dispatcher" && role != "viewer") ||
            (!string.IsNullOrEmpty(password) && (string.IsNullOrWhiteSpace(password) || password.Length < 10 || password.Length > 128)) ||
            (mutation == Mutation.Create && string.IsNullOrEmpty(password))) return "INVALID_ACCOUNT";
        var replacement = string.IsNullOrEmpty(password) ? null : Make(name, role, password);
        lock (sync)
        {
            int index = users.FindIndex(x => x.name == name);
            // Another request may have created/deleted this exact identity
            // while password hashing ran. Never turn an edit into a creation.
            if (mutation == Mutation.Create && index >= 0) return "ACCOUNT_EXISTS";
            if (mutation == Mutation.Update && index < 0) return "ACCOUNT_NOT_FOUND";
            if (index < 0 && users.Count >= 100) return "ACCOUNT_LIMIT";
            if (replacement == null)
            {
                if (index < 0) return "INVALID_ACCOUNT";
                var current = users[index];
                replacement = new UserAccount { name = current.name, role = role, salt = current.salt, hash = current.hash };
            }
            var next = new List<UserAccount>(users);
            if (index >= 0) next[index] = replacement; else next.Add(replacement);
            Save(next); users = next; RecoveryRequired = false;
            foreach (var item in sessions) if (item.Value.name == name) RemoveSessionLocked(item.Key);
        }
        return null;
    }
    public bool Delete(string name)
    {
        if (string.IsNullOrEmpty(name) || name == "local-owner") return false;
        lock (sync)
        {
            int index = users.FindIndex(x => x.name == name);
            if (index < 0) return false;
            var next = new List<UserAccount>(users); next.RemoveAt(index);
            Save(next); users = next;
            foreach (var item in sessions) if (item.Value.name == name) RemoveSessionLocked(item.Key);
            return true;
        }
    }
    public async Task<(string Token, AuthSession Session, string Error)> Login(string name, string password, string ticket, bool loopback, string currentToken = null)
    {
        UserAccount account;
        bool ownerLogin = loopback && OwnerAccess.Validate(bootstrap,ticket,notBefore:ownerCodesSince);
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
            if(ownerLogin) {
                foreach(var entry in usedOwnerCodes.Where(p=>p.Value<Protocol.Now).ToArray())usedOwnerCodes.Remove(entry.Key);
                if(!OwnerAccess.Validate(bootstrap,ticket,notBefore:ownerCodesSince)||usedOwnerCodes.ContainsKey(ticket)||usedOwnerCodes.Count>=256)return default;
            }
            // A password/role edit may have occurred while PBKDF2 ran outside the lock.
            if (!ownerLogin && !users.Any(user => ReferenceEquals(user, account))) return default;
            long now = Environment.TickCount64;
            CleanupExpiredLocked(now);
            // Credentials were verified above. Reopening the dispatcher in the
            // same browser retains its one session instead of locking itself out.
            if (currentToken != null && sessions.TryGetValue(currentToken, out var current) && current.name == account.name)
            {
                if (ownerLogin) usedOwnerCodes[ticket] = Protocol.Now + OwnerAccess.LifetimeMs;
                if (current.connections == 0) current.disconnectedAt = now;
                return (currentToken, current, null);
            }
            if (sessions.Values.Any(existing => string.Equals(existing.name, account.name, StringComparison.Ordinal)))
                return (null, null, "SESSION_ACTIVE");
            if (sessions.Count >= 256) return default;
            if(ownerLogin)usedOwnerCodes[ticket]=Protocol.Now+OwnerAccess.LifetimeMs;
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var session = new AuthSession { name = account.name, role = ownerLogin ? "admin" : account.role == "admin" ? "dispatcher" : account.role, expires = Environment.TickCount64 + SessionLifetimeMs, disconnectedAt = Environment.TickCount64 };
            sessions[token] = session; return (token, session, null);
        }
    }

    public AuthSession Get(string token, bool touch = true)
    {
        if (token == null) return null;
        lock (sync)
        {
            if (!sessions.TryGetValue(token, out var value)) return null;
            long now = Environment.TickCount64;
            if (ExpiredLocked(value, now)) { RemoveSessionLocked(token); return null; }
            if (touch) value.expires = now + SessionLifetimeMs;
            // Reading a cookie is not proof of a live browser connection. Only
            // BeginConnection clears the disconnect lease; otherwise /api/me
            // could strand the account for the full session lifetime.
            return value;
        }
    }
    public AuthSession BeginConnection(string token)
    {
        if (token == null) return null;
        lock (sync)
        {
            if (!sessions.TryGetValue(token, out var value)) return null;
            long now = Environment.TickCount64;
            if (ExpiredLocked(value, now)) { RemoveSessionLocked(token); return null; }
            value.connections++;
            value.disconnectedAt = 0;
            value.expires = now + SessionLifetimeMs;
            return value;
        }
    }
    public AuthSession Touch(string token)
    {
        if (token == null) return null;
        lock (sync)
        {
            if (!sessions.TryGetValue(token, out var value)) return null;
            long now = Environment.TickCount64;
            if (ExpiredLocked(value, now)) { RemoveSessionLocked(token); return null; }
            value.expires = now + SessionLifetimeMs;
            return value;
        }
    }
    public void EndConnection(string token)
    {
        if (token == null) return;
        lock (sync)
        {
            if (!sessions.TryGetValue(token, out var value)) return;
            if (value.connections > 0 && --value.connections == 0 && value.expires > Environment.TickCount64) value.disconnectedAt = Environment.TickCount64;
        }
    }
    public void Logout(string token)
    {
        if (token == null) return;
        lock (sync)
        {
            RemoveSessionLocked(token);
        }
    }
    private void RemoveSessionLocked(string token)
    {
        if (!sessions.TryRemove(token, out var session)) return;
        session.expires = -1; session.connections = 0; session.disconnectedAt = 0;
        session.revoked.Cancel();
    }
    private bool ExpiredLocked(AuthSession session, long now)
        => session.expires <= now || (session.disconnectedAt > 0 && now - session.disconnectedAt >= disconnectGraceMs);
    private void CleanupExpiredLocked(long now)
    {
        foreach (var item in sessions) if (ExpiredLocked(item.Value, now)) RemoveSessionLocked(item.Key);
    }
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
