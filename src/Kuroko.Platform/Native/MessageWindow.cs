using System.ComponentModel;
using System.Runtime.InteropServices;
using Kuroko.Core.Diagnostics;
using static Kuroko.Platform.Native.NativeMethods;

namespace Kuroko.Platform.Native;

/// <summary>
/// Hidden top-level window that owns hotkeys, the clipboard listener, delayed clipboard rendering
/// and the tray icon. Must be created on a thread that pumps messages (the WPF UI thread).
/// A regular (not message-only) window is used because the clipboard owner must receive
/// WM_RENDERFORMAT.
/// </summary>
public sealed class MessageWindow : IDisposable
{
    public delegate bool MessageHandler(uint msg, nint wParam, nint lParam, out nint result);

    private const string ClassName = "Kuroko.MessageWindow";
    private const uint WS_POPUP = 0x80000000;
    private const uint WS_EX_TOOLWINDOW = 0x00000080;

    private readonly List<MessageHandler> _handlers = new();
    private readonly WndProc _wndProc; // kept in a field so the delegate is not collected
    private bool _disposed;

    public MessageWindow()
    {
        _wndProc = Proc;
        var instance = GetModuleHandle(null);
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _wndProc,
            hInstance = instance,
            lpszClassName = ClassName,
        };

        if (RegisterClassEx(ref wc) == 0)
        {
            var error = Marshal.GetLastWin32Error();
            // 1410 = class already exists (e.g. recreated after a failed start); that is fine.
            if (error != 1410) throw new Win32Exception(error);
        }

        Handle = CreateWindowEx(WS_EX_TOOLWINDOW, ClassName, "Kuroko", WS_POPUP, 0, 0, 0, 0, 0, 0, instance, 0);
        if (Handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public nint Handle { get; }

    public void AddHandler(MessageHandler handler) => _handlers.Add(handler);

    /// <summary>Called (after logging) when a handler threw, so the app can tell the user. Must not throw itself.</summary>
    public Action<Exception>? HandlerFailed { get; set; }

    private nint Proc(nint hwnd, uint msg, nint wParam, nint lParam)
    {
        foreach (var handler in _handlers)
        {
            try
            {
                if (handler(msg, wParam, lParam, out var result)) return result;
            }
            catch (Exception ex)
            {
                // An exception must never leave a native window procedure: nothing above it catches it and the
                // process would end. Hotkey callbacks and tray menu actions run in here.
                AppLog.Error($"Message 0x{msg:X4} failed.", ex);
                try
                {
                    HandlerFailed?.Invoke(ex);
                }
                catch (Exception notifyError)
                {
                    AppLog.Error("Reporting the failure failed as well.", notifyError);
                }

                return 0;
            }
        }

        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DestroyWindow(Handle);
    }
}
