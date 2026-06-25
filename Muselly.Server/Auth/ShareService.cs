using System.Security.Cryptography;
using Muselly.Core.Models;
using Muselly.Core.Services.Interfaces;
using Muselly.Core.Storage;

namespace Muselly.Server.Auth;

/// <summary>
/// File-backed <see cref="IShareService"/> (<c>shares.json</c>). Tokens are unguessable random ids. Expired
/// links are pruned lazily on read, so the management list stays clean without a background timer.
/// </summary>
public sealed class ShareService : IShareService
{
    private readonly object _gate = new();
    private readonly string _path;
    private List<ShareLink> _shares;

    public ShareService(string? path = null)
    {
        _path = path ?? StoragePaths.SharesFile();
        _shares = Load(_path);
    }

    public IReadOnlyList<ShareLink> List()
    {
        lock (_gate)
        {
            Prune();
            return _shares.ToList();
        }
    }

    public ShareLink Create(ShareKind kind, string key, string label, TimeSpan? lifetime)
    {
        var now = DateTimeOffset.Now;
        var link = new ShareLink
        {
            Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            Kind = kind,
            Key = key,
            Label = label,
            CreatedAt = now,
            ExpiresAt = lifetime is { } l ? now + l : null
        };
        lock (_gate)
        {
            _shares.Add(link);
            Save();
        }
        return link;
    }

    public ShareLink? Find(string id)
    {
        lock (_gate) return _shares.FirstOrDefault(s => s.Id == id);
    }

    public void Revoke(string id)
    {
        lock (_gate)
        {
            if (_shares.RemoveAll(s => s.Id == id) > 0) Save();
        }
    }

    private void Prune()
    {
        if (_shares.RemoveAll(s => s.IsExpired) > 0) Save();
    }

    private void Save() => JsonStore.Save(_path, new SharesFile { Shares = _shares });

    private static List<ShareLink> Load(string path)
    {
        var file = JsonStore.Load(path, () => new SharesFile());
        return file.Shares ?? new List<ShareLink>();
    }

    private sealed class SharesFile
    {
        public int Version { get; set; } = 1;
        public List<ShareLink> Shares { get; set; } = new();
    }
}
