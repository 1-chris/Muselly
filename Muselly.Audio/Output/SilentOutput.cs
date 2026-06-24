using System;
using System.Threading;

namespace Muselly.Audio.Output;

/// <summary>
/// A no-sound fallback output used when no native device can be opened. It still pulls the render callback
/// on a timer at the device rate, so the playhead advances, time labels update and tracks auto-advance —
/// the UI behaves exactly as with audio, just silent. Keeps the app usable on unsupported configurations.
/// </summary>
internal sealed class SilentOutput : IAudioOutput
{
    private const int Rate = 48000;
    private const int Channels = 2;
    private const int BlockFrames = 1024;

    private Thread? _thread;
    private volatile bool _running;
    private AudioRenderCallback? _render;

    public AudioFormat Format => new(Rate, Channels);
    public bool IsRunning { get; private set; }

    public void Start(AudioRenderCallback callback)
    {
        if (IsRunning) return;
        _render = callback;
        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "muselly-silent" };
        _thread.Start();
        IsRunning = true;
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _running = false;
        _thread?.Join(500);
        _thread = null;
        _render = null;
    }

    private void Loop()
    {
        var block = new float[BlockFrames * Channels];
        var blockMs = (int)(1000.0 * BlockFrames / Rate);
        while (_running)
        {
            var render = _render;
            if (render is not null) render(block);
            Thread.Sleep(blockMs);
        }
    }

    public void Dispose() => Stop();
}
