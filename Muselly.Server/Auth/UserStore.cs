using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;

namespace Muselly.Server.Auth;

/// <summary>
/// File-backed <see cref="IServerUserStore"/> (<c>server-users.json</c>). Stores only PBKDF2 hashes and
/// salts; the cleartext password never touches disk. Thread-safe.
/// </summary>
public sealed class UserStore : IServerUserStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private List<ServerUser> _users;

    public UserStore(string? path = null)
    {
        _path = path ?? StoragePaths.ServerUsersFile();
        _users = Load(_path);
    }

    public IReadOnlyList<ServerUser> Users
    {
        get { lock (_gate) return _users.ToList(); }
    }

    public event EventHandler? Changed;

    public ServerUser? Find(string username)
    {
        lock (_gate)
            return _users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
    }

    public ServerUser? Validate(string username, string password)
    {
        var user = Find(username);
        if (user is null) return null;
        return PasswordHasher.Verify(password, user) ? user : null;
    }

    public void AddOrUpdate(string username, string password, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new ArgumentException("Username required.", nameof(username));
        var (hash, salt, iterations) = PasswordHasher.Hash(password);
        lock (_gate)
        {
            var existing = _users.FirstOrDefault(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                _users.Add(new ServerUser
                {
                    Username = username,
                    Role = role,
                    PasswordHash = hash,
                    Salt = salt,
                    Iterations = iterations
                });
            }
            else
            {
                existing.Role = role;
                existing.PasswordHash = hash;
                existing.Salt = salt;
                existing.Iterations = iterations;
            }
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetRole(string username, UserRole role)
    {
        lock (_gate)
        {
            var u = _users.FirstOrDefault(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
            if (u is null) return;
            u.Role = role;
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetPassword(string username, string password)
    {
        lock (_gate)
        {
            var u = _users.FirstOrDefault(x => string.Equals(x.Username, username, StringComparison.OrdinalIgnoreCase));
            if (u is null) return;
            var (hash, salt, iterations) = PasswordHasher.Hash(password);
            u.PasswordHash = hash;
            u.Salt = salt;
            u.Iterations = iterations;
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string username)
    {
        lock (_gate)
        {
            var removed = _users.RemoveAll(u => string.Equals(u.Username, username, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return;
            Save();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Save() => JsonStore.Save(_path, new UserStoreFile { Users = _users });

    private static List<ServerUser> Load(string path)
    {
        var file = JsonStore.Load(path, () => new UserStoreFile());
        return file.Users ?? new List<ServerUser>();
    }

    private sealed class UserStoreFile
    {
        public int Version { get; set; } = 1;
        public List<ServerUser> Users { get; set; } = new();
    }
}
