using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;

namespace Muselly.App.Platform;

/// <summary>
/// macOS-only AppKit interop for the window's native traffic-light buttons.
///
/// In native fullscreen macOS parks the title bar (and its buttons) in an auto-hiding overlay that only
/// drops down when the pointer touches the top edge. Trying to move the real <c>standardWindowButton</c>s
/// into the content view to keep them visible is a losing battle: AppKit owns their layout and re-homes
/// them on every title-bar event (activation, Space switch, hover-reveal), repeatedly flinging them to the
/// wrong corner.
///
/// So instead, in fullscreen we simply <b>hide</b> the native buttons and let the app draw its own
/// traffic-light controls in its title strip (which it fully controls and which never move). On leaving
/// fullscreen we show the native buttons again. We only send well-known selectors to standard AppKit
/// objects, and never on a non-macOS platform.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacTitleBar
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC, EntryPoint = "sel_registerName")]
    private static extern IntPtr Sel(string name);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern IntPtr MsgSend_Long_Ret(IntPtr receiver, IntPtr selector, long arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSend_Double(IntPtr receiver, IntPtr selector, double arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSend_Long(IntPtr receiver, IntPtr selector, long arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSend_ULong(IntPtr receiver, IntPtr selector, ulong arg1);

    [DllImport(ObjC, EntryPoint = "objc_msgSend")]
    private static extern void MsgSend_Bool(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool arg1);

    private const long NSWindowStyleMaskFullScreen = 1L << 14;
    private const long NSWindowStyleMaskFullSizeContentView = 1L << 15;
    private const long NSWindowTitleHidden = 1; // NSWindowTitleVisibility.hidden

    /// <summary>
    /// Hides or shows the three native window buttons (close/minimise/zoom) by fading their alpha — NOT by
    /// <c>setHidden:</c>, which removes them from the title-bar layout and (with
    /// <c>ExtendClientAreaToDecorationsHint</c>) corrupts the window's content scaling in fullscreen. Alpha
    /// keeps them in place but invisible. Safe on any platform; a no-op off macOS or before the handle exists.
    /// </summary>
    public static void SetNativeButtonsHidden(Window window, bool hidden)
    {
        var nsWindow = NsWindow(window);
        if (nsWindow == IntPtr.Zero) return;

        var setAlpha = Sel("setAlphaValue:");
        var standardWindowButton = Sel("standardWindowButton:");
        for (long i = 0; i <= 2; i++)
        {
            var btn = MsgSend_Long_Ret(nsWindow, standardWindowButton, i);
            if (btn != IntPtr.Zero)
                MsgSend_Double(btn, setAlpha, hidden ? 0.0 : 1.0);
        }
    }

    /// <summary>
    /// Re-asserts the "extended client area" title-bar configuration (transparent title bar, hidden title,
    /// full-size content view) that Avalonia applies for <c>ExtendClientAreaToDecorationsHint</c>. macOS
    /// resets these when leaving fullscreen, which makes the real native title bar reappear in place of the
    /// app's custom strip — so we restore them. No-op while still in fullscreen, off macOS, or before the
    /// handle exists.
    /// </summary>
    public static void ApplyExtendedClientArea(Window window)
    {
        var nsWindow = NsWindow(window);
        if (nsWindow == IntPtr.Zero) return;

        var mask = (long)MsgSend(nsWindow, Sel("styleMask"));
        if ((mask & NSWindowStyleMaskFullScreen) != 0) return; // leave fullscreen's own title bar alone

        MsgSend_Long(nsWindow, Sel("setTitleVisibility:"), NSWindowTitleHidden);
        MsgSend_Bool(nsWindow, Sel("setTitlebarAppearsTransparent:"), true);
        if ((mask & NSWindowStyleMaskFullSizeContentView) == 0)
            MsgSend_ULong(nsWindow, Sel("setStyleMask:"), (ulong)(mask | NSWindowStyleMaskFullSizeContentView));
    }

    private static IntPtr NsWindow(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return IntPtr.Zero;
        // On Avalonia's macOS backend the top-level platform handle IS the NSWindow (its `AvnWindow`
        // subclass), not the content view — so we use it directly.
        return window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
    }
}
