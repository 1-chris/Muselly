namespace Muselly.Core.Models;

/// <summary>
/// A single line in the built-in server's activity log: a client connecting or disconnecting, or a piece of
/// content / command it requested. Kept in a small in-memory ring buffer on the host and surfaced in the
/// Connect panel while the server is running. Purely informational; never persisted.
/// </summary>
public sealed class ServerLogEntry
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public required string Message { get; init; }

    /// <summary>A compact "HH:mm:ss  message" line for display.</summary>
    public string Display => $"{Timestamp:HH:mm:ss}  {Message}";
}
