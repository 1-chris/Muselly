using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Muselly.Server.Protocol;
using Muselly.Server.Transport;

namespace Muselly.Server.Client;

/// <summary>
/// A live client-side connection to a remote Muselly server. Establishes TLS 1.3 (verifying the server by
/// fingerprint), then multiplexes request/response and streaming exchanges over the single encrypted socket.
/// One instance maps to one logged-in session.
/// </summary>
public sealed class MusellyServerConnection : IDisposable
{
    private readonly ILogger _logger;
    private ProtocolConnection? _connection;
    private TcpClient? _tcp;
    private CancellationTokenSource? _cts;
    private int _nextId;

    private readonly ConcurrentDictionary<int, TaskCompletionSource<Envelope>> _pending = new();
    private readonly ConcurrentDictionary<int, StreamReceiver> _streams = new();

    public MusellyServerConnection(ILogger logger) => _logger = logger;

    public string Fingerprint { get; private set; } = string.Empty;
    public bool IsConnected => _connection is not null && _tcp?.Connected == true;

    /// <summary>
    /// Connects and completes the TLS handshake. When <paramref name="expectedFingerprint"/> is null the
    /// presented certificate is accepted and exposed via <see cref="Fingerprint"/> (first use); otherwise it
    /// must match or the connection fails.
    /// </summary>
    public async Task ConnectAsync(string host, int port, string? expectedFingerprint, CancellationToken ct = default)
    {
        var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(host, port, ct).ConfigureAwait(false);

        var ssl = await TlsTransport.AuthenticateClientAsync(tcp.GetStream(), host, expectedFingerprint,
            fp => Fingerprint = fp, ct).ConfigureAwait(false);

        _tcp = tcp;
        _connection = new ProtocolConnection(ssl);
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ReadLoopAsync(_cts.Token));
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var connection = _connection;
        if (connection is null) return;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var frame = await connection.ReadFrameAsync(ct).ConfigureAwait(false);
                if (frame is null) break;

                if (frame.Value.Kind == FrameKind.Binary)
                {
                    if (_streams.TryGetValue(frame.Value.BinaryRequestId, out var receiver))
                        receiver.Write(frame.Value.Binary!);
                    continue;
                }

                if (frame.Value.Message is not { } env) continue;
                switch (env.Kind)
                {
                    case EnvelopeKind.Response:
                        if (_pending.TryRemove(env.Id, out var tcs)) tcs.TrySetResult(env);
                        break;
                    case EnvelopeKind.Error:
                        if (_pending.TryRemove(env.Id, out var etcs))
                            etcs.TrySetException(new ProtocolException(env.Error ?? "Server error."));
                        else if (_streams.TryRemove(env.Id, out var sr))
                            sr.Fail(new ProtocolException(env.Error ?? "Server error."));
                        break;
                    case EnvelopeKind.StreamBegin:
                        if (_streams.TryGetValue(env.Id, out var begin)) begin.Begin(env.StreamLength);
                        break;
                    case EnvelopeKind.StreamEnd:
                        if (_streams.TryRemove(env.Id, out var end)) end.Complete();
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Remote connection read loop ended.");
        }
        finally
        {
            FailAllPending(new ProtocolException("Connection closed."));
        }
    }

    private void FailAllPending(Exception ex)
    {
        foreach (var kvp in _pending) kvp.Value.TrySetException(ex);
        _pending.Clear();
        foreach (var kvp in _streams) kvp.Value.Fail(ex);
        _streams.Clear();
    }

    /// <summary>Sends a request and awaits the typed response payload.</summary>
    public async Task<TResp?> RequestAsync<TResp>(string type, object? payload, CancellationToken ct = default)
    {
        var connection = _connection ?? throw new InvalidOperationException("Not connected.");
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<Envelope>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        await connection.SendMessageAsync(new Envelope
        {
            Id = id,
            Kind = EnvelopeKind.Request,
            Type = type,
            Payload = payload is null ? null : System.Text.Json.JsonSerializer.SerializeToElement(payload, ProtocolJson.Options)
        }, ct).ConfigureAwait(false);

        using (ct.Register(() => { if (_pending.TryRemove(id, out var t)) t.TrySetCanceled(); }))
        {
            var env = await tcs.Task.ConfigureAwait(false);
            return env.GetPayload<TResp>();
        }
    }

    /// <summary>Sends a request whose response is a binary stream, written to <paramref name="destination"/>.</summary>
    public async Task<long> RequestStreamAsync(string type, object? payload, Stream destination, CancellationToken ct = default)
    {
        var connection = _connection ?? throw new InvalidOperationException("Not connected.");
        var id = Interlocked.Increment(ref _nextId);
        var receiver = new StreamReceiver(destination);
        _streams[id] = receiver;

        await connection.SendMessageAsync(new Envelope
        {
            Id = id,
            Kind = EnvelopeKind.Request,
            Type = type,
            Payload = payload is null ? null : System.Text.Json.JsonSerializer.SerializeToElement(payload, ProtocolJson.Options)
        }, ct).ConfigureAwait(false);

        using (ct.Register(() => { if (_streams.TryRemove(id, out var r)) r.Fail(new OperationCanceledException()); }))
        {
            return await receiver.Completion.ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { /* ignore */ }
        FailAllPending(new ObjectDisposedException(nameof(MusellyServerConnection)));
        try { _connection?.Dispose(); } catch { /* ignore */ }
        try { _tcp?.Dispose(); } catch { /* ignore */ }
    }

    /// <summary>Collects the binary frames of a single streamed response into a destination stream.</summary>
    private sealed class StreamReceiver
    {
        private readonly Stream _destination;
        private readonly TaskCompletionSource<long> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _written;

        public StreamReceiver(Stream destination) => _destination = destination;

        public Task<long> Completion => _completion.Task;

        public void Begin(long _) { /* length is advisory */ }

        public void Write(byte[] data)
        {
            try
            {
                _destination.Write(data, 0, data.Length);
                _written += data.Length;
            }
            catch (Exception ex)
            {
                _completion.TrySetException(ex);
            }
        }

        public void Complete() => _completion.TrySetResult(_written);

        public void Fail(Exception ex) => _completion.TrySetException(ex);
    }
}
