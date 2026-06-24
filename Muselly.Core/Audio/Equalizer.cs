using System;
using System.Collections.Generic;
using Muselly.Core.Services.Interfaces;

namespace Muselly.Core.Audio;

/// <summary>
/// A 10-band graphic equaliser built from cascaded RBJ peaking biquads, applied to the interleaved float
/// render block. It's real-time safe: gain changes recompute a fresh coefficient set on the UI thread and
/// publish it by an atomic reference swap, while the audio thread reads that reference and advances its own
/// per-channel filter state. Gains persist through the settings service.
/// </summary>
public sealed class Equalizer : IAudioEqualizer
{
    private static readonly int[] Frequencies = { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    private const double Q = 1.41;          // ~1 octave bands
    private const double MinDb = -12, MaxDb = 12;
    private const int MaxChannels = 8;

    private readonly ISettingsService _settings;
    private readonly double[] _gains = new double[Frequencies.Length];
    private readonly double[,] _s1 = new double[MaxChannels, Frequencies.Length];
    private readonly double[,] _s2 = new double[MaxChannels, Frequencies.Length];

    private volatile bool _enabled;
    private volatile Coeffs? _coeffs;
    private double _preampDb;
    private double _preampLinear = 1.0;
    private int _sampleRate = 48000;

    public Equalizer(ISettingsService settings)
    {
        _settings = settings;

        var saved = settings.Current.Equalizer;
        _enabled = saved.Enabled;
        _preampDb = saved.PreampDb;
        _preampLinear = Math.Pow(10, _preampDb / 20.0);
        if (saved.BandGainsDb is { Count: > 0 })
            for (var i = 0; i < _gains.Length && i < saved.BandGainsDb.Count; i++)
                _gains[i] = Math.Clamp(saved.BandGainsDb[i], MinDb, MaxDb);

        Recompute();
    }

    public IReadOnlyList<int> BandFrequencies => Frequencies;
    public int BandCount => Frequencies.Length;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            ClearState();
            Persist();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public double PreampDb
    {
        get => _preampDb;
        set
        {
            _preampDb = Math.Clamp(value, MinDb, MaxDb);
            _preampLinear = Math.Pow(10, _preampDb / 20.0);
            Persist();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? Changed;

    public double GetGain(int band) => band >= 0 && band < _gains.Length ? _gains[band] : 0;

    public void SetGain(int band, double db)
    {
        if (band < 0 || band >= _gains.Length) return;
        _gains[band] = Math.Clamp(db, MinDb, MaxDb);
        Recompute();
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Reset()
    {
        Array.Clear(_gains);
        _preampDb = 0;
        _preampLinear = 1.0;
        Recompute();
        Persist();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetSampleRate(int sampleRate)
    {
        if (sampleRate <= 0 || sampleRate == _sampleRate) return;
        _sampleRate = sampleRate;
        Recompute();
    }

    public void Process(Span<float> buffer, int channels)
    {
        if (!_enabled || channels <= 0) return;
        var c = _coeffs;
        if (c is null) return;

        var preamp = _preampLinear;
        var bands = _gains.Length;
        var frames = buffer.Length / channels;

        for (var f = 0; f < frames; f++)
        {
            for (var ch = 0; ch < channels; ch++)
            {
                if (ch >= MaxChannels) continue;
                var idx = f * channels + ch;
                double x = buffer[idx] * preamp;

                for (var b = 0; b < bands; b++)
                {
                    var y = c.B0[b] * x + _s1[ch, b];
                    _s1[ch, b] = c.B1[b] * x - c.A1[b] * y + _s2[ch, b];
                    _s2[ch, b] = c.B2[b] * x - c.A2[b] * y;
                    x = y;
                }

                // Guard against runaway values from extreme boosts (clamp, kill denormals/NaN).
                if (double.IsNaN(x)) x = 0;
                else if (x > 4) x = 4;
                else if (x < -4) x = -4;

                buffer[idx] = (float)x;
            }
        }
    }

    private void Recompute()
    {
        var n = _gains.Length;
        var c = new Coeffs(n);
        for (var i = 0; i < n; i++)
        {
            var a = Math.Pow(10, _gains[i] / 40.0);     // sqrt of linear gain (peaking)
            var w0 = 2 * Math.PI * Frequencies[i] / _sampleRate;
            var cw = Math.Cos(w0);
            var sw = Math.Sin(w0);
            var alpha = sw / (2 * Q);

            var b0 = 1 + alpha * a;
            var b1 = -2 * cw;
            var b2 = 1 - alpha * a;
            var a0 = 1 + alpha / a;
            var a1 = -2 * cw;
            var a2 = 1 - alpha / a;

            c.B0[i] = b0 / a0;
            c.B1[i] = b1 / a0;
            c.B2[i] = b2 / a0;
            c.A1[i] = a1 / a0;
            c.A2[i] = a2 / a0;
        }
        _coeffs = c; // atomic publish to the audio thread
    }

    private void ClearState()
    {
        Array.Clear(_s1);
        Array.Clear(_s2);
    }

    private void Persist()
    {
        _settings.Update(s =>
        {
            s.Equalizer.Enabled = _enabled;
            s.Equalizer.PreampDb = _preampDb;
            s.Equalizer.BandGainsDb = new List<double>(_gains);
        });
    }

    private sealed class Coeffs
    {
        public Coeffs(int n)
        {
            B0 = new double[n]; B1 = new double[n]; B2 = new double[n];
            A1 = new double[n]; A2 = new double[n];
        }

        public double[] B0 { get; }
        public double[] B1 { get; }
        public double[] B2 { get; }
        public double[] A1 { get; }
        public double[] A2 { get; }
    }
}
