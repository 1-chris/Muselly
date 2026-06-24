using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CA = Muselly.Audio.Output.CoreAudioInterop;

namespace Muselly.Audio.Output;

/// <summary>
/// macOS audio output via a HAL output AudioUnit (AUHAL) on the system default device. Opens float32
/// interleaved at 48 kHz stereo; CoreAudio pulls blocks from our render callback on its real-time thread.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe class MacAudioOutput : IAudioOutput
{
    private const int Rate = 48000;
    private const int Channels = 2;

    private readonly object _lock = new();
    private AudioRenderCallback? _render;
    private IntPtr _unit;
    private GCHandle _self;

    public AudioFormat Format { get; private set; } = new(Rate, Channels);
    public bool IsRunning { get; private set; }

    public void Start(AudioRenderCallback callback)
    {
        lock (_lock)
        {
            if (IsRunning) return;
            _render = callback;
            OpenUnit();
            IsRunning = true;
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!IsRunning) return;
            IsRunning = false;
            CloseUnit();
            _render = null;
        }
    }

    private void OpenUnit()
    {
        var desc = new CA.AudioComponentDescription
        {
            componentType = CA.kAudioUnitType_Output,
            componentSubType = CA.kAudioUnitSubType_HALOutput,
            componentManufacturer = CA.kAudioUnitManufacturer_Apple,
        };
        var comp = CA.AudioComponentFindNext(IntPtr.Zero, ref desc);
        if (comp == IntPtr.Zero) throw new InvalidOperationException("No CoreAudio HAL output component.");
        Check(CA.AudioComponentInstanceNew(comp, out _unit), "AudioComponentInstanceNew");

        var asbd = new CA.AudioStreamBasicDescription
        {
            mSampleRate = Rate,
            mFormatID = CA.kAudioFormatLinearPCM,
            mFormatFlags = CA.kAudioFormatFlagIsFloat | CA.kAudioFormatFlagIsPacked,
            mFramesPerPacket = 1,
            mChannelsPerFrame = Channels,
            mBitsPerChannel = 32,
            mBytesPerFrame = 4 * Channels,
            mBytesPerPacket = 4 * Channels,
        };
        SetProp(CA.kAudioUnitProperty_StreamFormat, CA.kAudioUnitScope_Input, 0, ref asbd);

        // Request a large IO buffer so a transient stall on the (managed) render path — e.g. a GC pause
        // while the UI does heavy work — has plenty of headroom and never underruns the device. ~85 ms at
        // 48 kHz. Best-effort: devices may clamp to their own supported range.
        try
        {
            uint bufferFrames = 4096;
            SetProp(CA.kAudioDevicePropertyBufferFrameSize, CA.kAudioUnitScope_Global, 0, ref bufferFrames);
        }
        catch { /* device rejected the size; keep its default */ }

        _self = GCHandle.Alloc(this);
        var cb = new CA.AURenderCallbackStruct
        {
            inputProc = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, uint, uint, IntPtr, int>)&RenderThunk,
            inputProcRefCon = GCHandle.ToIntPtr(_self),
        };
        SetProp(CA.kAudioUnitProperty_SetRenderCallback, CA.kAudioUnitScope_Input, 0, ref cb);

        Check(CA.AudioUnitInitialize(_unit), "AudioUnitInitialize");
        Check(CA.AudioOutputUnitStart(_unit), "AudioOutputUnitStart");
        Format = new AudioFormat(Rate, Channels);
    }

    private void CloseUnit()
    {
        if (_unit != IntPtr.Zero)
        {
            CA.AudioOutputUnitStop(_unit);
            CA.AudioUnitUninitialize(_unit);
            CA.AudioComponentInstanceDispose(_unit);
            _unit = IntPtr.Zero;
        }
        if (_self.IsAllocated) _self.Free();
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static int RenderThunk(IntPtr refCon, IntPtr ioActionFlags, IntPtr inTimeStamp,
        uint inBusNumber, uint inNumberFrames, IntPtr ioData)
    {
        try
        {
            if (GCHandle.FromIntPtr(refCon).Target is MacAudioOutput o) o.Render(ioData);
        }
        catch { /* never let a managed exception escape onto the RT thread */ }
        return 0;
    }

    private void Render(IntPtr ioData)
    {
        if (ioData == IntPtr.Zero) return;
        // AudioBufferList: mBuffers[0] = { mNumberChannels@8, mDataByteSize@12, mData@16 }.
        var byteSize = Marshal.ReadInt32(ioData, 12);
        var data = Marshal.ReadIntPtr(ioData, 16);
        if (data == IntPtr.Zero || byteSize <= 0) return;

        var span = new Span<float>((void*)data, byteSize / sizeof(float));
        var render = _render;
        if (render is not null) render(span);
        else span.Clear();
    }

    private void SetProp<T>(uint prop, uint scope, uint elem, ref T value) where T : unmanaged
    {
        fixed (void* p = &value)
            Check(CA.AudioUnitSetProperty(_unit, prop, scope, elem, (IntPtr)p, (uint)sizeof(T)), "AudioUnitSetProperty");
    }

    public void Dispose() => Stop();

    private static void Check(int status, string op)
    {
        if (status != 0) throw new InvalidOperationException($"{op} failed: OSStatus {status}");
    }
}
