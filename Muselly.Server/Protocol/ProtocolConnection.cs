using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace Muselly.Server.Protocol;

/// <summary>A decoded frame read off the wire: either a JSON control message or a binary chunk.</summary>
public readonly struct Frame
{
    public FrameKind Kind { get; init; }
    public Envelope? Message { get; init; }
    public int BinaryRequestId { get; init; }
    public byte[]? Binary { get; init; }
}

/// <summary>
/// Length-prefixed framing over a duplex stream (in practice an <see cref="System.Net.Security.SslStream"/>).
/// Writes are serialised behind a semaphore so concurrent senders never interleave a frame. Used by both the
/// client connector and each server-side session.
/// </summary>
public sealed class ProtocolConnection : IDisposable
{
    private readonly Stream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly byte[] _lenBuf = new byte[4];

    public ProtocolConnection(Stream stream) => _stream = stream;

    public async Task SendMessageAsync(Envelope envelope, CancellationToken ct = default)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(envelope, ProtocolJson.Options);
        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            await WriteFrameHeaderAsync(FrameKind.Json, json.Length, ct).ConfigureAwait(false);
            await _stream.WriteAsync(json, ct).ConfigureAwait(false);
            await _stream.FlushAsync(ct).ConfigureAwait(false);
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>Sends a binary stream as: StreamBegin envelope, binary chunks, StreamEnd envelope.</summary>
    public async Task SendStreamAsync(int requestId, Stream data, string contentType, long length, CancellationToken ct = default)
    {
        await SendMessageAsync(new Envelope
        {
            Id = requestId,
            Kind = EnvelopeKind.StreamBegin,
            StreamLength = length,
            ContentType = contentType
        }, ct).ConfigureAwait(false);

        var buffer = new byte[ProtocolConstants.StreamChunkSize];
        int read;
        while ((read = await data.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await _writeLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await WriteFrameHeaderAsync(FrameKind.Binary, read + 4, ct).ConfigureAwait(false);
                BinaryPrimitives.WriteInt32BigEndian(_lenBuf, requestId);
                await _stream.WriteAsync(_lenBuf.AsMemory(0, 4), ct).ConfigureAwait(false);
                await _stream.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                await _stream.FlushAsync(ct).ConfigureAwait(false);
            }
            finally { _writeLock.Release(); }
        }

        await SendMessageAsync(new Envelope { Id = requestId, Kind = EnvelopeKind.StreamEnd }, ct).ConfigureAwait(false);
    }

    private async Task WriteFrameHeaderAsync(FrameKind kind, int payloadLength, CancellationToken ct)
    {
        // Frame = [4-byte BE length = 1 + payloadLength][1-byte kind][payload]
        Span<byte> header = stackalloc byte[5];
        BinaryPrimitives.WriteInt32BigEndian(header, payloadLength + 1);
        header[4] = (byte)kind;
        await _stream.WriteAsync(header.ToArray(), ct).ConfigureAwait(false);
    }

    /// <summary>Reads the next frame. Returns null on a clean end-of-stream.</summary>
    public async Task<Frame?> ReadFrameAsync(CancellationToken ct = default)
    {
        if (!await ReadExactAsync(_lenBuf, 4, ct).ConfigureAwait(false)) return null;
        var total = BinaryPrimitives.ReadInt32BigEndian(_lenBuf);
        if (total < 1 || total > ProtocolConstants.MaxFrameLength)
            throw new ProtocolException($"Invalid frame length {total}.");

        var kindByte = new byte[1];
        if (!await ReadExactAsync(kindByte, 1, ct).ConfigureAwait(false))
            throw new ProtocolException("Truncated frame (missing kind).");

        var payloadLen = total - 1;
        var payload = new byte[payloadLen];
        if (payloadLen > 0 && !await ReadExactAsync(payload, payloadLen, ct).ConfigureAwait(false))
            throw new ProtocolException("Truncated frame (missing payload).");

        switch ((FrameKind)kindByte[0])
        {
            case FrameKind.Json:
                var env = JsonSerializer.Deserialize<Envelope>(Encoding.UTF8.GetString(payload), ProtocolJson.Options)
                          ?? throw new ProtocolException("Malformed JSON envelope.");
                return new Frame { Kind = FrameKind.Json, Message = env };

            case FrameKind.Binary:
                if (payloadLen < 4) throw new ProtocolException("Binary frame too short.");
                var id = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(0, 4));
                var data = payload.AsSpan(4).ToArray();
                return new Frame { Kind = FrameKind.Binary, BinaryRequestId = id, Binary = data };

            default:
                throw new ProtocolException($"Unknown frame kind {kindByte[0]}.");
        }
    }

    private async Task<bool> ReadExactAsync(byte[] buffer, int count, CancellationToken ct)
    {
        var offset = 0;
        while (offset < count)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(offset, count - offset), ct).ConfigureAwait(false);
            if (read == 0) return offset != 0 ? throw new ProtocolException("Connection closed mid-frame.") : false;
            offset += read;
        }
        return true;
    }

    public void Dispose()
    {
        _writeLock.Dispose();
        _stream.Dispose();
    }
}

public sealed class ProtocolException : Exception
{
    public ProtocolException(string message) : base(message) { }
    public ProtocolException(string message, Exception inner) : base(message, inner) { }
}
