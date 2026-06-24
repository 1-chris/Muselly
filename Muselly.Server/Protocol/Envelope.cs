using System.Text.Json;
using System.Text.Json.Serialization;

namespace Muselly.Server.Protocol;

/// <summary>The kind of a JSON control message.</summary>
public enum EnvelopeKind
{
    Request,
    Response,
    Error,
    StreamBegin,
    StreamEnd
}

/// <summary>
/// The JSON control message exchanged over the protocol. Requests and responses are correlated by
/// <see cref="Id"/>. Binary payloads (audio/images) are delivered as a <see cref="EnvelopeKind.StreamBegin"/>
/// envelope, then binary frames carrying the same id, then a <see cref="EnvelopeKind.StreamEnd"/> envelope.
/// </summary>
public sealed class Envelope
{
    public int Id { get; set; }

    public EnvelopeKind Kind { get; set; }

    /// <summary>The message type (see <see cref="MessageType"/>). Empty for stream/error envelopes.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The typed payload, as raw JSON.</summary>
    public JsonElement? Payload { get; set; }

    /// <summary>Set on <see cref="EnvelopeKind.Error"/> envelopes.</summary>
    public string? Error { get; set; }

    /// <summary>Total byte length announced by a <see cref="EnvelopeKind.StreamBegin"/> envelope.</summary>
    public long StreamLength { get; set; }

    /// <summary>Optional content hint for a stream (e.g. "audio/opus", "image/jpeg").</summary>
    public string? ContentType { get; set; }

    [JsonIgnore]
    public bool IsError => Kind == EnvelopeKind.Error;

    public T? GetPayload<T>() => Payload is { } p ? p.Deserialize<T>(ProtocolJson.Options) : default;
}

/// <summary>Shared JSON options for the protocol.</summary>
public static class ProtocolJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };
}
