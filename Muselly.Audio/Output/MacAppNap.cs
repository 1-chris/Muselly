using System;
using System.Runtime.InteropServices;

namespace Muselly.Audio.Output;

/// <summary>
/// macOS App Nap suppression for the duration of playback. When an app spends a long time in the background
/// macOS "App Nap" can throttle its timers and threads, which starves the audio feed and shows up as random
/// multi-second dropouts after the app has been open for hours. Holding a <c>userInitiated</c> +
/// <c>latencyCritical</c> activity assertion (via <c>NSProcessInfo</c>) keeps the process at full speed while
/// audio is playing. Idle <i>system</i> sleep is still allowed. No-op off macOS.
/// </summary>
internal static class MacAppNap
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC, EntryPoint = "objc_getClass")]
    private static extern IntPtr GetClass(string name);

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Sel(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend_Ptr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend_BeginActivity(IntPtr receiver, IntPtr selector, ulong options, IntPtr reason);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSend_Void_Ptr(IntPtr receiver, IntPtr selector, IntPtr arg1);

    // NSActivityUserInitiatedAllowingIdleSystemSleep (0x00EFFFFF) | NSActivityLatencyCritical (0xFF00000000):
    // prevents App Nap and keeps latency low, but still lets the machine sleep when idle.
    private const ulong Options = 0x00EFFFFFUL | 0xFF00000000UL;

    private static readonly object Gate = new();
    private static IntPtr _token; // retained NSObject activity token, or Zero when not held

    public static void Begin(string reason)
    {
        if (!OperatingSystem.IsMacOS()) return;
        lock (Gate)
        {
            if (_token != IntPtr.Zero) return;

            var processInfo = MsgSend(GetClass("NSProcessInfo"), Sel("processInfo"));
            if (processInfo == IntPtr.Zero) return;

            var reasonStr = NSString(reason);
            var token = MsgSend_BeginActivity(processInfo, Sel("beginActivityWithOptions:reason:"), Options, reasonStr);
            if (token != IntPtr.Zero)
                _token = MsgSend(token, Sel("retain")); // outlives the autorelease pool
        }
    }

    public static void End()
    {
        if (!OperatingSystem.IsMacOS()) return;
        lock (Gate)
        {
            if (_token == IntPtr.Zero) return;

            var processInfo = MsgSend(GetClass("NSProcessInfo"), Sel("processInfo"));
            if (processInfo != IntPtr.Zero)
                MsgSend_Void_Ptr(processInfo, Sel("endActivity:"), _token);
            MsgSend(_token, Sel("release"));
            _token = IntPtr.Zero;
        }
    }

    private static IntPtr NSString(string value)
    {
        var ptr = Marshal.StringToCoTaskMemUTF8(value);
        try
        {
            return MsgSend_Ptr(GetClass("NSString"), Sel("stringWithUTF8String:"), ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }
}
