using System.Runtime.InteropServices;
using Ghostwriter.Core.Diagnostics;
using Ghostwriter.Platform.Native;
using static Ghostwriter.Platform.Native.NativeMethods;

namespace Ghostwriter.Platform.TextIntegration;

/// <summary>All formats of the clipboard as raw bytes, so it can be put back exactly as it was.</summary>
public sealed class ClipboardSnapshot
{
    internal List<(uint Format, byte[] Data)> Items { get; } = new();

    public int FormatCount => Items.Count;

    public long TotalBytes => Items.Sum(i => (long)i.Data.Length);
}

/// <summary>
/// A text offered on the clipboard with delayed rendering: the bytes are only produced when the paste
/// target actually asks for them. <see cref="Rendered"/> completes at that moment, which replaces
/// guessing with sleeps.
/// </summary>
public sealed class DelayedTextOffer
{
    internal DelayedTextOffer(string text) => Text = text;

    internal string Text { get; }

    private readonly TaskCompletionSource _rendered = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Rendered => _rendered.Task;

    internal void MarkRendered() => _rendered.TrySetResult();
}

/// <summary>
/// Clipboard access with retries (the clipboard is a global lock), full-format snapshot/restore,
/// event-driven change detection and delayed rendering. Methods that touch the clipboard are
/// synchronous and short; call them from a worker thread, never block the UI thread that has to
/// answer WM_RENDERFORMAT.
/// </summary>
public sealed class ClipboardService : IDisposable
{
    private const long MaxFormatBytes = 64L * 1024 * 1024;

    private static readonly uint FmtExcludeFromMonitor = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private static readonly uint FmtCanIncludeInHistory = RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private static readonly uint FmtCanUploadToCloud = RegisterClipboardFormat("CanUploadToCloudClipboard");

    private readonly MessageWindow _window;
    private readonly object _gate = new();
    private readonly List<TaskCompletionSource> _waiters = new();
    private DelayedTextOffer? _offer;

    public ClipboardService(MessageWindow window)
    {
        _window = window;
        window.AddHandler(OnMessage);
        AddClipboardFormatListener(window.Handle);
    }

    public uint SequenceNumber => GetClipboardSequenceNumber();

    // ---- change detection ----

    /// <summary>Waits until the clipboard sequence number differs from <paramref name="since"/>.</summary>
    public async Task<bool> WaitForChangeAsync(uint since, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (true)
        {
            if (GetClipboardSequenceNumber() != since) return true;
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0) return false;

            var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate) _waiters.Add(waiter);

            // Re-check after registering so an update between the check and the registration is not missed.
            if (GetClipboardSequenceNumber() != since)
            {
                lock (_gate) _waiters.Remove(waiter);
                return true;
            }

            // The timeout also bounds the wait in case a listener message is dropped.
            await Task.WhenAny(waiter.Task, Task.Delay((int)Math.Min(remaining, 25), ct));
            lock (_gate) _waiters.Remove(waiter);
        }
    }

    // ---- reading / writing text ----

    public string? TryGetText()
    {
        if (!TryOpen()) return null;
        try
        {
            var handle = GetClipboardData(CF_UNICODETEXT);
            if (handle == 0) return null;
            var ptr = GlobalLock(handle);
            if (ptr == 0) return null;
            try
            {
                return Marshal.PtrToStringUni(ptr);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Puts plain text on the clipboard. With <paramref name="hidden"/> it stays out of clipboard history/cloud sync.</summary>
    public bool TrySetText(string text, bool hidden)
    {
        if (!TryOpen()) return false;
        try
        {
            EmptyClipboard();
            var memory = AllocUnicode(text);
            if (memory == 0) return false;
            if (SetClipboardData(CF_UNICODETEXT, memory) == 0)
            {
                GlobalFree(memory);
                return false;
            }

            if (hidden) AddPrivacyFormats();
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>
    /// Replaces the clipboard with a delayed-render text. Nothing is copied until a consumer requests it.
    /// </summary>
    public DelayedTextOffer? TryOfferDelayed(string text)
    {
        if (!TryOpen()) return null;
        try
        {
            EmptyClipboard();
            var offer = new DelayedTextOffer(text);
            lock (_gate) _offer = offer;

            // SetClipboardData(format, NULL) announces delayed rendering. It returns NULL on success as well,
            // so the return value cannot be used to detect failure here.
            SetClipboardData(CF_UNICODETEXT, 0);
            AddPrivacyFormats();
            return offer;
        }
        finally
        {
            CloseClipboard();
        }
    }

    // ---- snapshot / restore ----

    public bool TrySnapshot(out ClipboardSnapshot snapshot)
    {
        snapshot = new ClipboardSnapshot();
        if (!TryOpen()) return false;
        try
        {
            for (var format = EnumClipboardFormats(0); format != 0; format = EnumClipboardFormats(format))
            {
                // GDI handle formats cannot be copied as bytes; Windows re-synthesizes them from CF_DIB(V5).
                if (IsHandleFormat(format)) continue;

                var handle = GetClipboardData(format);
                if (handle == 0) continue;
                var size = GlobalSize(handle);
                if (size == 0 || size > MaxFormatBytes) continue;

                var ptr = GlobalLock(handle);
                if (ptr == 0) continue;
                try
                {
                    var bytes = new byte[(int)size];
                    Marshal.Copy(ptr, bytes, 0, bytes.Length);
                    snapshot.Items.Add((format, bytes));
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    public bool TryRestore(ClipboardSnapshot snapshot)
    {
        if (!TryOpen()) return false;
        try
        {
            EmptyClipboard();
            lock (_gate) _offer = null;
            foreach (var (format, data) in snapshot.Items)
            {
                var memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)data.Length);
                if (memory == 0) continue;
                var ptr = GlobalLock(memory);
                if (ptr == 0)
                {
                    GlobalFree(memory);
                    continue;
                }

                Marshal.Copy(data, 0, ptr, data.Length);
                GlobalUnlock(memory);
                if (SetClipboardData(format, memory) == 0) GlobalFree(memory);
            }

            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    // ---- internals ----

    private static bool IsHandleFormat(uint format)
        => format is CF_BITMAP or CF_METAFILEPICT or CF_PALETTE or CF_PENDATA or CF_ENHMETAFILE
            or CF_OWNERDISPLAY or CF_DSPBITMAP or CF_DSPMETAFILEPICT or CF_DSPENHMETAFILE
            || format is >= 0x0300 and <= 0x03FF;

    private bool TryOpen()
    {
        // Another process (clipboard manager, RDP, Office) may hold the clipboard for a few ms.
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (OpenClipboard(_window.Handle)) return true;
            Thread.Sleep(attempt < 5 ? 1 : 5);
        }

        AppLog.Warn("Clipboard stayed locked by another process.");
        return false;
    }

    private static nint AllocUnicode(string text)
    {
        var byteCount = (text.Length + 1) * 2;
        var memory = GlobalAlloc(GMEM_MOVEABLE, (nuint)byteCount);
        if (memory == 0) return 0;
        var ptr = GlobalLock(memory);
        if (ptr == 0)
        {
            GlobalFree(memory);
            return 0;
        }

        Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length);
        Marshal.WriteInt16(ptr, text.Length * 2, 0);
        GlobalUnlock(memory);
        return memory;
    }

    private static void AddPrivacyFormats()
    {
        SetDword(FmtExcludeFromMonitor, 1);
        SetDword(FmtCanIncludeInHistory, 0);
        SetDword(FmtCanUploadToCloud, 0);
    }

    private static void SetDword(uint format, uint value)
    {
        var memory = GlobalAlloc(GMEM_MOVEABLE, sizeof(uint));
        if (memory == 0) return;
        var ptr = GlobalLock(memory);
        if (ptr == 0)
        {
            GlobalFree(memory);
            return;
        }

        Marshal.WriteInt32(ptr, (int)value);
        GlobalUnlock(memory);
        if (SetClipboardData(format, memory) == 0) GlobalFree(memory);
    }

    private bool OnMessage(uint msg, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        switch (msg)
        {
            case WM_CLIPBOARDUPDATE:
                List<TaskCompletionSource> waiters;
                lock (_gate) waiters = _waiters.ToList();
                foreach (var waiter in waiters) waiter.TrySetResult();
                return true;

            case WM_RENDERFORMAT:
                // The clipboard is already open for us here; do not call OpenClipboard.
                DelayedTextOffer? offer;
                lock (_gate) offer = _offer;
                if (offer is not null && (uint)wParam == CF_UNICODETEXT)
                {
                    var memory = AllocUnicode(offer.Text);
                    if (memory != 0 && SetClipboardData(CF_UNICODETEXT, memory) == 0) GlobalFree(memory);
                    offer.MarkRendered();
                }

                return true;

            case WM_RENDERALLFORMATS:
                // Sent when we exit while still owning delayed data: render it so it survives.
                lock (_gate)
                {
                    if (_offer is not null && OpenClipboard(_window.Handle))
                    {
                        var memory = AllocUnicode(_offer.Text);
                        if (memory != 0 && SetClipboardData(CF_UNICODETEXT, memory) == 0) GlobalFree(memory);
                        CloseClipboard();
                    }
                }

                return true;

            case WM_DESTROYCLIPBOARD:
                return true;
        }

        return false;
    }

    public void Dispose() => RemoveClipboardFormatListener(_window.Handle);
}
