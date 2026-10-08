using InstaPrompt.Platform.Native;
using static InstaPrompt.Platform.Native.NativeMethods;

namespace InstaPrompt.Platform.Tray;

public sealed record TrayMenuItem(string Text, Action OnClick, bool Checked = false, bool Separator = false);

/// <summary>Notification-area icon with a native popup menu and balloon/toast messages.</summary>
public sealed class TrayIcon : IDisposable
{
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2, NIM_SETVERSION = 4;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4, NIF_INFO = 0x10;
    private const uint NIIF_NONE = 0, NIIF_INFO = 1, NIIF_WARNING = 2, NIIF_ERROR = 3, NIIF_NOSOUND = 0x10;
    private const uint MF_STRING = 0, MF_SEPARATOR = 0x800, MF_CHECKED = 8;
    private const uint TPM_RIGHTBUTTON = 2, TPM_RETURNCMD = 0x100, TPM_BOTTOMALIGN = 0x20;
    private const uint CallbackMessage = WM_APP + 1;
    private const nint IDI_APPLICATION = 32512;

    private readonly MessageWindow _window;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private string _tooltip;
    private bool _added;
    private nint _ownIcon;

    /// <summary>Called right before the menu opens, so labels (e.g. check marks) are always current.</summary>
    public Func<IReadOnlyList<TrayMenuItem>> MenuProvider { get; set; } = () => [];

    public Action? OnDoubleClick { get; set; }

    public TrayIcon(MessageWindow window, string tooltip)
    {
        _window = window;
        _tooltip = tooltip;
        window.AddHandler(OnMessage);
        Add();
    }

    public void SetTooltip(string tooltip)
    {
        _tooltip = tooltip;
        var data = NewData(NIF_TIP);
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    public void ShowMessage(string title, string text, bool isError = false)
    {
        var data = NewData(NIF_INFO);
        data.szInfoTitle = Truncate(title, 63);
        data.szInfo = Truncate(text, 255);
        data.dwInfoFlags = (isError ? NIIF_ERROR : NIIF_INFO) | NIIF_NOSOUND;
        Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    /// <summary>The application's own icon (embedded in the exe), at the system's small-icon size; the stock icon as a fallback.</summary>
    private nint LoadOwnIcon()
    {
        if (_ownIcon != 0) return _ownIcon;

        var exe = Environment.ProcessPath;
        if (exe is not null)
        {
            var small = new nint[1];
            if (ExtractIconEx(exe, 0, null, small, 1) > 0 && small[0] != 0) _ownIcon = small[0];
        }

        return _ownIcon != 0 ? _ownIcon : LoadIcon(0, IDI_APPLICATION);
    }

    private void Add()
    {
        var data = NewData(NIF_MESSAGE | NIF_ICON | NIF_TIP);
        data.uCallbackMessage = CallbackMessage;
        data.hIcon = LoadOwnIcon();
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
        data.uVersion = 4;
        Shell_NotifyIcon(NIM_SETVERSION, ref data);
    }

    private NOTIFYICONDATA NewData(uint flags) => new()
    {
        cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        szTip = Truncate(_tooltip, 127),
        szInfo = string.Empty,
        szInfoTitle = string.Empty,
    };

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    private bool OnMessage(uint msg, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (msg == _taskbarCreated)
        {
            // Explorer restarted; the icon is gone.
            Add();
            return true;
        }

        if (msg != CallbackMessage) return false;

        // With NOTIFYICON_VERSION_4 the low word of lParam is the mouse message.
        switch ((uint)(lParam & 0xFFFF))
        {
            case WM_RBUTTONUP:
            case 0x007B: // WM_CONTEXTMENU (keyboard)
                ShowMenu();
                break;
            case 0x0203: // WM_LBUTTONDBLCLK
                OnDoubleClick?.Invoke();
                break;
        }

        return true;
    }

    private void ShowMenu()
    {
        var items = MenuProvider();
        var menu = CreatePopupMenu();
        try
        {
            for (var i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Separator) AppendMenu(menu, MF_SEPARATOR, 0, null);
                else AppendMenu(menu, MF_STRING | (item.Checked ? MF_CHECKED : 0), (nuint)(i + 1), item.Text);
            }

            GetCursorPos(out var point);
            // Required so the menu closes when the user clicks elsewhere.
            SetForegroundWindow(_window.Handle);
            var command = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_BOTTOMALIGN, point.X, point.Y, 0, _window.Handle, 0);
            PostMessage(_window.Handle, WM_NULL, 0, 0);
            if (command > 0 && command <= items.Count) items[(int)command - 1].OnClick();
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (!_added) return;
        var data = NewData(0);
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _added = false;
        if (_ownIcon != 0)
        {
            DestroyIcon(_ownIcon);
            _ownIcon = 0;
        }
    }
}
