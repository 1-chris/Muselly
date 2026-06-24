using CommunityToolkit.Mvvm.ComponentModel;
using Muselly.Core.Audio;

namespace Muselly.App.ViewModels;

/// <summary>One band of the graphic EQ: a frequency label and a two-way gain (dB) bound to an EqSlider.</summary>
public sealed partial class EqBandViewModel : ViewModelBase
{
    private readonly IAudioEqualizer _eq;
    private readonly int _index;
    private bool _suppress;

    public EqBandViewModel(IAudioEqualizer eq, int index)
    {
        _eq = eq;
        _index = index;
        Label = FormatFrequency(eq.BandFrequencies[index]);
        _gain = eq.GetGain(index);
    }

    public string Label { get; }

    [ObservableProperty] private double _gain;

    partial void OnGainChanged(double value)
    {
        if (_suppress) return;
        _eq.SetGain(_index, value);
    }

    /// <summary>Refreshes the slider from the equaliser without re-triggering a write (used after presets/reset).</summary>
    public void SyncFromEq()
    {
        _suppress = true;
        Gain = _eq.GetGain(_index);
        _suppress = false;
    }

    private static string FormatFrequency(int hz) => hz >= 1000 ? $"{hz / 1000}k" : hz.ToString();
}
