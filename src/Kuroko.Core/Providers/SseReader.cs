using System.Runtime.CompilerServices;
using System.Text;

namespace Kuroko.Core.Providers;

public readonly record struct SseEvent(string? Event, string Data);

/// <summary>Minimal Server-Sent-Events parser (the subset all four providers use).</summary>
public static class SseReader
{
    public static async IAsyncEnumerable<SseEvent> ReadAsync(Stream stream, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024);
        string? eventName = null;
        var data = new StringBuilder();
        var hasData = false;

        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.Length == 0)
            {
                // A blank line dispatches the event collected so far.
                if (hasData) yield return new SseEvent(eventName, data.ToString());
                eventName = null;
                data.Clear();
                hasData = false;
                continue;
            }

            if (line[0] == ':') continue; // comment / keep-alive

            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? string.Empty : line[(colon + 1)..];
            if (value.StartsWith(' ')) value = value[1..];

            switch (field)
            {
                case "event":
                    eventName = value;
                    break;
                case "data":
                    if (hasData) data.Append('\n');
                    data.Append(value);
                    hasData = true;
                    break;
            }
        }

        // The connection may close right after the last data line without the blank line.
        if (hasData) yield return new SseEvent(eventName, data.ToString());
    }
}
