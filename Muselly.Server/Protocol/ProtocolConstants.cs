namespace Muselly.Server.Protocol;

/// <summary>Wire-level constants for the bespoke Muselly protocol (spoken only inside a TLS 1.3 tunnel).</summary>
public static class ProtocolConstants
{
    /// <summary>Bumped on incompatible wire changes; exchanged in the Hello handshake.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>Maximum accepted frame payload size (guards against malicious length prefixes). 32 MiB.</summary>
    public const int MaxFrameLength = 32 * 1024 * 1024;

    /// <summary>Size of binary stream chunks (audio/images) sent per frame. 64 KiB.</summary>
    public const int StreamChunkSize = 64 * 1024;
}

/// <summary>The two kinds of frame on the wire.</summary>
public enum FrameKind : byte
{
    /// <summary>A UTF-8 JSON <see cref="Envelope"/>.</summary>
    Json = 1,

    /// <summary>A raw binary chunk: payload is [4-byte big-endian request id][data].</summary>
    Binary = 2
}
