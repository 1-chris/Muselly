using System;
using System.Runtime.InteropServices;

namespace Muselly.Audio.Output;

/// <summary>
/// Thin P/Invoke surface over ALSA's PCM API (libasound) for float32 interleaved playback. Only the
/// functions the output driver needs are bound. Linux-only; <c>libasound.so.2</c> resolves by soname.
/// </summary>
internal static class AlsaInterop
{
    private const string Asound = "libasound.so.2";

    public const int SND_PCM_STREAM_PLAYBACK = 0;
    public const int SND_PCM_ACCESS_RW_INTERLEAVED = 3;
    public const int SND_PCM_FORMAT_FLOAT_LE = 14;

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern int snd_pcm_open(out IntPtr pcm, string name, int stream, int mode);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_close(IntPtr pcm);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_prepare(IntPtr pcm);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_drop(IntPtr pcm);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_recover(IntPtr pcm, int err, int silent);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern long snd_pcm_writei(IntPtr pcm, IntPtr buffer, ulong frames);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_malloc(out IntPtr ptr);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern void snd_pcm_hw_params_free(IntPtr ptr);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_any(IntPtr pcm, IntPtr ptr);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_access(IntPtr pcm, IntPtr ptr, int access);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_format(IntPtr pcm, IntPtr ptr, int format);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_channels_near(IntPtr pcm, IntPtr ptr, ref uint channels);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_rate_near(IntPtr pcm, IntPtr ptr, ref uint rate, ref int dir);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_period_size_near(IntPtr pcm, IntPtr ptr, ref ulong frames, ref int dir);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_set_buffer_size_near(IntPtr pcm, IntPtr ptr, ref ulong frames);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params_get_period_size(IntPtr ptr, out ulong frames, out int dir);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    public static extern int snd_pcm_hw_params(IntPtr pcm, IntPtr ptr);

    [DllImport(Asound, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr snd_strerror(int errnum);

    public static string ErrorText(int code)
    {
        var ptr = snd_strerror(code);
        return ptr == IntPtr.Zero ? $"error {code}" : Marshal.PtrToStringAnsi(ptr) ?? $"error {code}";
    }

    public static bool TryProbe()
    {
        try { return NativeLibrary.TryLoad(Asound, out _); }
        catch { return false; }
    }
}
