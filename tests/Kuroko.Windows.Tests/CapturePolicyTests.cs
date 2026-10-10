using Kuroko.Platform.TextIntegration;

namespace Kuroko.Windows.Tests;

public class CapturePolicyTests
{
    [Fact]
    public void Only_replace_rejects_read_only_fields()
    {
        Assert.True(CapturePolicy.RejectsReadOnly(CaptureMode.Replace));
        Assert.False(CapturePolicy.RejectsReadOnly(CaptureMode.Display));
    }

    [Theory]
    [InlineData(CaptureMode.Replace, false, false, true)]
    [InlineData(CaptureMode.Replace, true, false, false)]
    [InlineData(CaptureMode.Replace, false, true, false)]
    [InlineData(CaptureMode.Display, false, false, false)]
    public void Select_all_is_only_allowed_to_replace_a_text_field(CaptureMode mode, bool itemView, bool nonText, bool expected)
        => Assert.Equal(expected, CapturePolicy.AllowsSelectAll(mode, itemView, nonText));

    [Theory]
    [InlineData("ConsoleWindowClass", "cmd", true)]
    [InlineData("CASCADIA_HOSTING_WINDOW_CLASS", "WindowsTerminal", true)]
    [InlineData("Chrome_WidgetWin_1", "wezterm-gui", true)]
    [InlineData("Notepad", "notepad", false)]
    public void Terminals_are_recognised_by_class_or_process(string windowClass, string process, bool expected)
        => Assert.Equal(expected, Target(windowClass, process).IsTerminal);

    [Fact]
    public void Explorer_is_an_item_view_and_remote_clients_are_recognised()
    {
        Assert.True(Target("CabinetWClass", "explorer").IsItemView);
        Assert.False(Target("Notepad", "notepad").IsItemView);
        Assert.True(Target("TscShellContainerClass", "mstsc").IsRemoteClient);
    }

    private static TargetInfo Target(string windowClass, string process)
        => new(1, 2, windowClass, 3, process, false, false, false, 4, null);
}
