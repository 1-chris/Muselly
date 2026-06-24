using System;
using System.Runtime.InteropServices;
using System.Threading;
using A = Muselly.Audio.Output.AlsaInterop;

namespace Muselly.Audio.Output;

/// <summary>
/// Linux audio output via ALSA on the "default" PCM. Opens float32 interleaved at ~48 kHz stereo and pumps
/// the render callback from a dedicated audio thread with XRUN recovery.
/// </summary>
internal sealed class AlsaOutput : IAudioOutput
{
    private const int RequestedRate = 48000;
    private const ulong RequestedPeriod = 512;

    private readonly object _lock = new();
    private AudioRenderCallback? _render;
    private IntPtr _pcm;
    private int _channels;
    private int _periodFrames;
    private float[] _buffer = Array.Empty<float>();
    private GCHandle _pin;
    private IntPtr _bufPtr;
    private Thread? _thread;
    private volatile bool _running;

    public AudioFormat Format { get; private set; } = new(RequestedRate, 2);
    public bool IsRunning { get; private set; }

    public void Start(AudioRenderCallback callback)
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _render = callback;
            Open();
            _running = true;
            _thread = new Thread(PlaybackLoop) { IsBackground = true, Priority = ThreadPriority.Highest, Name = "muselly-alsa" };
            _thread.Start();
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            _running = false;
            _thread?.Join(1000);
            _thread = null;
            if (_pcm != IntPtr.Zero) { A.snd_pcm_close(_pcm); _pcm = IntPtr.Zero; }
            if (_pin.IsAllocated) _pin.Free();
            _render = null;
        }
    }

    private void Open()
    {
        Check(A.snd_pcm_open(out _pcm, "default", A.SND_PCM_STREAM_PLAYBACK, 0), "snd_pcm_open");
        Check(A.snd_pcm_hw_params_malloc(out var hw), "hw_params_malloc");
        try
        {
            Check(A.snd_pcm_hw_params_any(_pcm, hw), "hw_params_any");
            Check(A.snd_pcm_hw_params_set_access(_pcm, hw, A.SND_PCM_ACCESS_RW_INTERLEAVED), "set_access");
            Check(A.snd_pcm_hw_params_set_format(_pcm, hw, A.SND_PCM_FORMAT_FLOAT_LE), "set_format");

            var ch = 2u;
            Check(A.snd_pcm_hw_params_set_channels_near(_pcm, hw, ref ch), "set_channels_near");
            var rate = (uint)RequestedRate;
            var dir = 0;
            Check(A.snd_pcm_hw_params_set_rate_near(_pcm, hw, ref rate, ref dir), "set_rate_near");
            var period = RequestedPeriod;
            dir = 0;
            Check(A.snd_pcm_hw_params_set_period_size_near(_pcm, hw, ref period, ref dir), "set_period_size_near");
            var bufFrames = period * 4;
            Check(A.snd_pcm_hw_params_set_buffer_size_near(_pcm, hw, ref bufFrames), "set_buffer_size_near");
            Check(A.snd_pcm_hw_params(_pcm, hw), "hw_params(commit)");
            A.snd_pcm_hw_params_get_period_size(hw, out var actualPeriod, out _);
            if (actualPeriod == 0) actualPeriod = period;

            _channels = (int)ch;
            _periodFrames = (int)actualPeriod;
            Format = new AudioFormat((int)rate, _channels);

            _buffer = new float[_periodFrames * _channels];
            _pin = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
            _bufPtr = _pin.AddrOfPinnedObject();

            Check(A.snd_pcm_prepare(_pcm), "snd_pcm_prepare");
        }
        finally
        {
            A.snd_pcm_hw_params_free(hw);
        }
    }

    private unsafe void PlaybackLoop()
    {
        var span = new Span<float>((void*)_bufPtr, _buffer.Length);
        while (_running)
        {
            try
            {
                var render = _render;
                if (render is not null) render(span);
                else span.Clear();
            }
            catch { span.Clear(); }

            var offsetFrames = 0;
            while (_running && offsetFrames < _periodFrames)
            {
                var ptr = _bufPtr + offsetFrames * _channels * sizeof(float);
                var n = A.snd_pcm_writei(_pcm, ptr, (ulong)(_periodFrames - offsetFrames));
                if (n >= 0) offsetFrames += (int)n;
                else if (A.snd_pcm_recover(_pcm, (int)n, 1) < 0) { _running = false; }
            }
        }
        A.snd_pcm_drop(_pcm);
    }

    public void Dispose() => Stop();

    private static void Check(int code, string op)
    {
        if (code < 0) throw new InvalidOperationException($"ALSA {op} failed: {A.ErrorText(code)}");
    }
}
