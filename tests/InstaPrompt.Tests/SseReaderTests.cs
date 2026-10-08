using System.Text;
using InstaPrompt.Core.Providers;

namespace InstaPrompt.Tests;

public class SseReaderTests
{
    private static async Task<List<SseEvent>> ReadAll(string text)
    {
        var events = new List<SseEvent>();
        await foreach (var evt in SseReader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)))) events.Add(evt);
        return events;
    }

    [Fact]
    public async Task Reads_named_events()
    {
        var events = await ReadAll("event: a\ndata: {\"x\":1}\n\nevent: b\ndata: two\n\n");
        Assert.Equal([new SseEvent("a", "{\"x\":1}"), new SseEvent("b", "two")], events);
    }

    [Fact]
    public async Task Joins_multiline_data_with_newline()
    {
        var events = await ReadAll("data: line1\ndata: line2\n\n");
        Assert.Equal("line1\nline2", Assert.Single(events).Data);
    }

    [Fact]
    public async Task Ignores_comments_and_unknown_fields()
    {
        var events = await ReadAll(": keep-alive\nid: 7\nretry: 100\ndata: ok\n\n");
        Assert.Equal("ok", Assert.Single(events).Data);
    }

    [Fact]
    public async Task Handles_crlf_line_endings()
    {
        var events = await ReadAll("event: a\r\ndata: one\r\n\r\ndata: two\r\n\r\n");
        Assert.Equal(["one", "two"], events.Select(e => e.Data));
    }

    [Fact]
    public async Task Dispatches_last_event_without_trailing_blank_line()
    {
        var events = await ReadAll("data: last");
        Assert.Equal("last", Assert.Single(events).Data);
    }

    [Fact]
    public async Task Keeps_colons_and_spacing_in_data()
    {
        var events = await ReadAll("data:  two spaces: and colon\n\n");
        Assert.Equal(" two spaces: and colon", Assert.Single(events).Data);
    }

    [Fact]
    public async Task Blank_lines_without_data_produce_nothing()
    {
        Assert.Empty(await ReadAll("\n\n\n"));
    }
}
