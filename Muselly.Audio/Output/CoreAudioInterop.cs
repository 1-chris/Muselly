using System;
using System.Runtime.InteropServices;

namespace Muselly.Audio.Output;

/// <summary>
/// Minimal P/Invoke surface over Apple's CoreAudio for playback through a HAL output AudioUnit (AUHAL).
/// Only the lifecycle + render-callback bits are bound (no device enumeration — we use the system default
/// output). Touched only on macOS.
/// </summary>
internal static class CoreAudioInterop
{
    public const string AudioUnit = "/System/Library/Frameworks/AudioUnit.framework/AudioUnit";
    public const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    public static uint FourCC(string s) => (uint)(((byte)s[0] << 24) | ((byte)s[1] << 16) | ((byte)s[2] << 8) | (byte)s[3]);

    public static readonly uint kAudioUnitType_Output = FourCC("auou");
    public static readonly uint kAudioUnitSubType_HALOutput = FourCC("ahal");
    public static readonly uint kAudioUnitManufacturer_Apple = FourCC("appl");

    public static readonly uint kAudioFormatLinearPCM = FourCC("lpcm");
    public const uint kAudioFormatFlagIsFloat = 0x1;
    public const uint kAudioFormatFlagIsPacked = 0x8;

    public const uint kAudioUnitProperty_StreamFormat = 8;
    public const uint kAudioUnitProperty_SetRenderCallback = 23;
    public const uint kAudioUnitScope_Input = 1;
    public const uint kAudioUnitScope_Global = 0;

    /// <summary>Device IO buffer size in frames; larger = more latency but more headroom against stalls.</summary>
    public static readonly uint kAudioDevicePropertyBufferFrameSize = FourCC("fsiz");

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioComponentDescription
    {
        public uint componentType;
        public uint componentSubType;
        public uint componentManufacturer;
        public uint componentFlags;
        public uint componentFlagsMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioStreamBasicDescription
    {
        public double mSampleRate;
        public uint mFormatID;
        public uint mFormatFlags;
        public uint mBytesPerPacket;
        public uint mFramesPerPacket;
        public uint mBytesPerFrame;
        public uint mChannelsPerFrame;
        public uint mBitsPerChannel;
        public uint mReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AURenderCallbackStruct
    {
        public IntPtr inputProc;
        public IntPtr inputProcRefCon;
    }

    [DllImport(AudioToolbox)]
    public static extern IntPtr AudioComponentFindNext(IntPtr inComponent, ref AudioComponentDescription desc);

    [DllImport(AudioToolbox)]
    public static extern int AudioComponentInstanceNew(IntPtr comp, out IntPtr unit);

    [DllImport(AudioToolbox)]
    public static extern int AudioComponentInstanceDispose(IntPtr unit);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitSetProperty(IntPtr unit, uint propID, uint scope, uint element, IntPtr data, uint dataSize);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitInitialize(IntPtr unit);

    [DllImport(AudioUnit)]
    public static extern int AudioUnitUninitialize(IntPtr unit);

    [DllImport(AudioUnit)]
    public static extern int AudioOutputUnitStart(IntPtr unit);

    [DllImport(AudioUnit)]
    public static extern int AudioOutputUnitStop(IntPtr unit);

    public static bool TryProbe()
    {
        try { return NativeLibrary.TryLoad(AudioUnit, out _); }
        catch { return false; }
    }
}
