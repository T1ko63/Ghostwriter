using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using static Ghostwriter.Platform.Native.NativeMethods;

namespace Ghostwriter.Platform.TextIntegration;

/// <summary>A point in physical screen pixels.</summary>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>
/// Snapshot of the window that had the focus when the hotkey fired. Captured synchronously and
/// before anything of ours takes the focus, so later steps know where to read from and write to.
/// </summary>
public sealed record TargetInfo(
    nint Window,
    nint FocusWindow,
    string WindowClass,
    uint ProcessId,
    string ProcessName,
    bool IsElevated,
    bool IsWin32PasswordEdit,
    bool IsWin32ReadOnlyEdit,
    uint ThreadId,
    ScreenPoint? CaretScreenPos)
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;
    private const int GWL_STYLE = -16;
    private const long ES_PASSWORD = 0x20;
    private const long ES_READONLY = 0x800;

    // Ctrl+C in a console sends SIGINT and would kill the running program, so the clipboard
    // strategy must never run there.
    private static readonly HashSet<string> TerminalClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "VirtualConsoleClass", "mintty",
        "org.wezfurlong.wezterm", "Alacritty",
    };

    private static readonly HashSet<string> TerminalProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal", "conhost", "OpenConsole", "wezterm-gui", "alacritty", "mintty", "ConEmu64", "ConEmu",
    };

    // Ctrl+A in these selects files/items, not text.
    private static readonly HashSet<string> ItemViewProcesses = new(StringComparer.OrdinalIgnoreCase) { "explorer" };

    // Remote desktop clients forward the clipboard asynchronously; pasting needs a longer grace period.
    private static readonly HashSet<string> RemoteProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "mstsc", "msrdc", "vmconnect", "CitrixReceiver", "wfica32", "AnyDesk", "TeamViewer",
    };

    public static bool SelfIsElevated { get; } = QueryElevation(GetCurrentProcess()) ?? false;

    public bool IsTerminal => TerminalClasses.Contains(WindowClass) || TerminalProcesses.Contains(ProcessName);

    public bool IsItemView => ItemViewProcesses.Contains(ProcessName) && WindowClass is not "Notepad";

    public bool IsRemoteClient => RemoteProcesses.Contains(ProcessName);

    public static TargetInfo? Capture()
    {
        var window = GetForegroundWindow();
        if (window == 0) return null;

        var threadId = GetWindowThreadProcessId(window, out var pid);
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        nint focus = 0;
        ScreenPoint? caret = null;
        if (GetGUIThreadInfo(threadId, ref info))
        {
            focus = info.hwndFocus;
            if (info.hwndCaret != 0 && info.rcCaret.Bottom > info.rcCaret.Top)
            {
                // rcCaret is in client coordinates of hwndCaret. Bottom-left is where an overlay should go.
                var point = new POINT { X = info.rcCaret.Left, Y = info.rcCaret.Bottom };
                if (ClientToScreen(info.hwndCaret, ref point)) caret = new ScreenPoint(point.X, point.Y);
            }
        }

        var (name, elevated) = ProcessInfo(pid);
        var windowClass = ClassOf(window);
        var passwordEdit = focus != 0
            && ClassOf(focus).Equals("Edit", StringComparison.OrdinalIgnoreCase)
            && (GetWindowLongPtr(focus, GWL_STYLE) & ES_PASSWORD) != 0;

        // Edit controls (also their WinForms/RichEdit variants) carry a read-only style bit: no point in asking the AI.
        var focusClass = focus != 0 ? ClassOf(focus) : string.Empty;
        var readOnlyEdit = focus != 0
            && focusClass.Contains("edit", StringComparison.OrdinalIgnoreCase)
            && (GetWindowLongPtr(focus, GWL_STYLE) & ES_READONLY) != 0;

        return new TargetInfo(window, focus, windowClass, pid, name, elevated, passwordEdit, readOnlyEdit, threadId, caret);
    }

    private static string ClassOf(nint hwnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : string.Empty;
    }

    private static (string Name, bool Elevated) ProcessInfo(uint pid)
    {
        var process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == 0)
        {
            // Cannot even open it: almost certainly a higher-integrity (elevated/system) process.
            return ("?", Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED);
        }

        try
        {
            var size = 1024u;
            var sb = new StringBuilder((int)size);
            var name = QueryFullProcessImageName(process, 0, sb, ref size)
                ? Path.GetFileNameWithoutExtension(sb.ToString())
                : "?";
            // If the token cannot be read from a limited process, assume the worst (elevated).
            return (name, QueryElevation(process) ?? true);
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static bool? QueryElevation(nint process)
    {
        if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return null;
        try
        {
            return GetTokenInformation(token, TokenElevation, out var elevated, sizeof(uint), out _)
                ? elevated != 0
                : null;
        }
        finally
        {
            CloseHandle(token);
        }
    }
}

