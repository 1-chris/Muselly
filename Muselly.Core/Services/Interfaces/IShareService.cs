using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// Creates, lists, validates and revokes temporary guest share links. Implemented in Muselly.Server and
/// persisted to <c>shares.json</c>; a no-op stub is used on heads that can't host (the browser).
/// </summary>
public interface IShareService
{
    IReadOnlyList<ShareLink> List();

    /// <summary>Creates a link to an item. <paramref name="lifetime"/> null means no expiry.</summary>
    ShareLink Create(ShareKind kind, string key, string label, TimeSpan? lifetime);

    /// <summary>Returns the link by id whether or not it has expired (null if unknown).</summary>
    ShareLink? Find(string id);

    void Revoke(string id);
}
