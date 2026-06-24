using Muselly.Core.Models;

namespace Muselly.Core.Services.Interfaces;

/// <summary>
/// Extracts the full tag metadata (and embedded cover art) from an audio file, producing an immutable
/// <see cref="Track"/>. Implementations must never throw on malformed files — they return <c>null</c>.
/// </summary>
public interface IMetadataReader
{
    /// <summary>
    /// Reads metadata for a single file. Returns <c>null</c> if the file is unreadable or unsupported.
    /// Embedded artwork is extracted once per album into the artwork cache and referenced by path.
    /// </summary>
    Track? Read(string absolutePath);
}
