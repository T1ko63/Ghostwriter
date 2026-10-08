using InstaPrompt.Core.Config;

namespace InstaPrompt.Tests;

public class ColorParserTests
{
    [Theory]
    [InlineData("#FF8040", 255, 0xFF, 0x80, 0x40, false)]
    [InlineData("#ff8040", 255, 0xFF, 0x80, 0x40, false)]
    [InlineData("#40FFFFFF", 0x40, 0xFF, 0xFF, 0xFF, true)]
    [InlineData("#80102030", 0x80, 0x10, 0x20, 0x30, true)]
    [InlineData("#aabbccdd", 0xAA, 0xBB, 0xCC, 0xDD, true)]
    [InlineData("  #000000  ", 255, 0, 0, 0, false)]
    public void Valid_colours_are_read_with_alpha_first(string text, int a, int r, int g, int b, bool hasAlpha)
    {
        Assert.True(ColorParser.TryParse(text, out var color, out var alpha));
        Assert.Equal(new Rgba((byte)a, (byte)r, (byte)g, (byte)b), color);
        Assert.Equal(hasAlpha, alpha);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("FFFFFF")]
    [InlineData("#FFF")]
    [InlineData("#FFFFF")]
    [InlineData("#FFFFFFF")]
    [InlineData("#FFFFFFFFF")]
    [InlineData("#GGGGGG")]
    [InlineData("#12 456")]
    [InlineData("red")]
    [InlineData("0x112233")]
    public void Invalid_colours_are_rejected(string? text)
    {
        Assert.False(ColorParser.TryParse(text, out _, out _));
    }

    [Theory]
    [InlineData("rgb(0, 0, 0)", 255, 0, 0, 0, false)]
    [InlineData("rgb(255,128,0)", 255, 255, 128, 0, false)]
    [InlineData("RGB( 1 , 2 , 3 )", 255, 1, 2, 3, false)]
    [InlineData("rgba(0, 255, 255, 0.25)", 64, 0, 255, 255, true)]
    [InlineData("rgba(0, 255, 255, 1)", 255, 0, 255, 255, true)]
    [InlineData("rgba(0, 255, 255, 0)", 0, 0, 255, 255, true)]
    [InlineData("rgba(10, 20, 30, .5)", 128, 10, 20, 30, true)]
    [InlineData("rgba(10, 20, 30, 50%)", 128, 10, 20, 30, true)]
    [InlineData("rgb(10, 20, 30, 0.5)", 128, 10, 20, 30, true)]
    public void The_css_colour_functions_are_read(string text, int a, int r, int g, int b, bool hasAlpha)
    {
        Assert.True(ColorParser.TryParse(text, out var color, out var alpha));
        Assert.Equal(new Rgba((byte)a, (byte)r, (byte)g, (byte)b), color);
        Assert.Equal(hasAlpha, alpha);
    }

    [Theory]
    [InlineData("rgb(256, 0, 0)")]
    [InlineData("rgb(0, 0)")]
    [InlineData("rgb(0, 0, 0, 0, 0)")]
    [InlineData("rgb(-1, 0, 0)")]
    [InlineData("rgb(0.5, 0, 0)")]
    [InlineData("rgba(0, 0, 0, 1.5)")]
    [InlineData("rgba(0, 0, 0, 150%)")]
    [InlineData("rgba(0, 0, 0, x)")]
    [InlineData("rgb 0, 0, 0")]
    [InlineData("hsl(0, 0%, 0%)")]
    [InlineData("rgb(0, 0, 0")]
    public void Invalid_colour_functions_are_rejected(string text)
    {
        Assert.False(ColorParser.TryParse(text, out _, out _));
    }

    [Fact]
    public void Alpha_comes_first_not_last()
    {
        // CSS would read #FF000080 as red at 50 %; here it is alpha FF, red 00, green 00, blue 80.
        ColorParser.TryParse("#FF000080", out var color, out _);
        Assert.Equal(new Rgba(0xFF, 0x00, 0x00, 0x80), color);
    }
}

public class AppearanceLoaderTests
{
    private static string? NoEnv(string name) => null;

    private static SettingsLoadResult Parse(string toml) => SettingsLoader.Parse(toml, NoEnv);

    private static AppearanceSettings Appearance(string toml)
    {
        var result = Parse(toml);
        Assert.True(result.Ok, string.Join("; ", result.Issues));
        return result.Settings!.Appearance;
    }

    private static List<ConfigIssue> Shown(SettingsLoadResult result) => result.Issues.Where(i => i.Show).ToList();

    [Fact]
    public void A_file_without_the_block_gets_the_defaults_and_no_message()
    {
        var result = Parse("theme = \"dark\"\n");

        Assert.Equal(AppearanceSettings.Default, result.Settings!.Appearance);
        Assert.Empty(Shown(result));
        Assert.Equal(50, AppearanceSettings.Default.Transparency);
        Assert.Equal(10, AppearanceSettings.Default.Radius);
        Assert.Equal(BlurMode.Acrylic, AppearanceSettings.Default.Blur);
        Assert.Equal(new Rgba(0x40, 0xFF, 0xFF, 0xFF), AppearanceSettings.Default.Dark.Selection);
        Assert.Equal(new Rgba(0x40, 0x00, 0x00, 0x00), AppearanceSettings.Default.Light.Selection);
    }

    [Fact]
    public void The_generated_default_file_gives_exactly_the_defaults_without_messages()
    {
        var result = Parse(DefaultSettings.Create(NoEnv));

        Assert.True(result.Ok);
        Assert.Equal(AppearanceSettings.Default, result.Settings!.Appearance);
        Assert.Empty(Shown(result));
    }

    [Fact]
    public void All_values_are_read()
    {
        var a = Appearance("""
            [appearance]
            transparency = 80
            radius = 20
            blur = "Mica"

            [appearance.dark]
            background = "#102030"
            foreground = "#EEEEEE"
            selection = "#80FF0000"

            [appearance.light]
            background = "#fafafa"
            foreground = "#111111"
            selection = "#223344"
            """);

        Assert.Equal(80, a.Transparency);
        Assert.Equal(20, a.Radius);
        Assert.Equal(BlurMode.Mica, a.Blur);
        Assert.Equal(Rgba.Opaque(0x10, 0x20, 0x30), a.Dark.Background);
        Assert.Equal(Rgba.Opaque(0xEE, 0xEE, 0xEE), a.Dark.Foreground);
        Assert.Equal(new Rgba(0x80, 0xFF, 0x00, 0x00), a.Dark.Selection);
        Assert.Equal(Rgba.Opaque(0xFA, 0xFA, 0xFA), a.Light.Background);
        Assert.Equal(Rgba.Opaque(0x22, 0x33, 0x44), a.Light.Selection);
    }

    [Fact]
    public void Missing_values_inside_the_block_fall_back_individually()
    {
        var a = Appearance("""
            [appearance]
            radius = 4

            [appearance.dark]
            foreground = "#FFFFFF"
            """);

        Assert.Equal(4, a.Radius);
        Assert.Equal(AppearanceSettings.DefaultTransparency, a.Transparency);
        Assert.Equal(AppearanceSettings.DefaultBlur, a.Blur);
        Assert.Equal(Rgba.Opaque(0xFF, 0xFF, 0xFF), a.Dark.Foreground);
        Assert.Equal(AppearanceSettings.DefaultDark.Background, a.Dark.Background);
        Assert.Equal(AppearanceSettings.DefaultLight, a.Light);
    }

    [Theory]
    [InlineData("transparency = 101")]
    [InlineData("transparency = -1")]
    [InlineData("transparency = \"50\"")]
    [InlineData("transparency = 50.5")]
    public void A_transparency_out_of_range_or_of_the_wrong_type_is_reported_and_replaced_by_the_default(string line)
    {
        var result = Parse($"theme = \"dark\"\n[appearance]\nradius = 7\n{line}\n");

        Assert.True(result.Ok);
        Assert.Equal(AppearanceSettings.DefaultTransparency, result.Settings!.Appearance.Transparency);
        Assert.Equal(7, result.Settings.Appearance.Radius); // the rest stays active
        var issue = Assert.Single(Shown(result));
        Assert.False(issue.IsError);
        Assert.Equal(4, issue.Line);
        Assert.Equal("settings.toml", issue.File);
        Assert.Contains("transparency", issue.Message);
    }

    [Theory]
    [InlineData("radius = 33")]
    [InlineData("radius = -2")]
    [InlineData("radius = \"big\"")]
    public void A_radius_out_of_range_is_reported_and_replaced_by_the_default(string line)
    {
        var result = Parse($"[appearance]\ntransparency = 20\n{line}\n");

        Assert.Equal(AppearanceSettings.DefaultRadius, result.Settings!.Appearance.Radius);
        Assert.Equal(20, result.Settings.Appearance.Transparency);
        var issue = Assert.Single(Shown(result));
        Assert.Equal(3, issue.Line);
        Assert.Contains("radius", issue.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void The_range_limits_themselves_are_valid(int transparency)
    {
        var result = Parse($"[appearance]\ntransparency = {transparency}\nradius = {(transparency == 0 ? 0 : 32)}\n");

        Assert.Empty(Shown(result));
        Assert.Equal(transparency, result.Settings!.Appearance.Transparency);
    }

    [Theory]
    [InlineData("acrylic", BlurMode.Acrylic)]
    [InlineData("MICA", BlurMode.Mica)]
    [InlineData("MicaAlt", BlurMode.MicaAlt)]
    [InlineData("blur", BlurMode.Blur)]
    [InlineData("none", BlurMode.None)]
    public void Blur_values_are_accepted_in_any_case(string text, BlurMode expected)
    {
        Assert.Equal(expected, Appearance($"[appearance]\nblur = \"{text}\"\n").Blur);
    }

    [Theory]
    [InlineData("blur = \"frosted\"")]
    [InlineData("blur = true")]
    public void An_invalid_blur_is_reported_and_replaced_by_acrylic(string line)
    {
        var result = Parse($"[appearance]\nradius = 3\n{line}\n");

        Assert.Equal(BlurMode.Acrylic, result.Settings!.Appearance.Blur);
        Assert.Equal(3, result.Settings.Appearance.Radius);
        var issue = Assert.Single(Shown(result));
        Assert.Equal(3, issue.Line);
        Assert.Contains("blur", issue.Message);
    }

    [Theory]
    [InlineData("#12345")]
    [InlineData("123456")]
    [InlineData("#GG0000")]
    [InlineData("red")]
    public void An_invalid_colour_falls_back_to_the_default_with_file_and_line(string value)
    {
        var result = Parse($"[appearance]\n\n[appearance.light]\nbackground = \"#EEEEEE\"\nforeground = \"{value}\"\n");

        var light = result.Settings!.Appearance.Light;
        Assert.Equal(AppearanceSettings.DefaultLight.Foreground, light.Foreground);
        Assert.Equal(Rgba.Opaque(0xEE, 0xEE, 0xEE), light.Background);
        var issue = Assert.Single(Shown(result));
        Assert.Equal(5, issue.Line);
        Assert.Contains("[appearance.light] foreground", issue.Message);
        Assert.Equal("settings.toml, line 5: " + issue.Message, issue.ToString());
    }

    [Fact]
    public void Colour_picker_formats_work_in_the_settings_file()
    {
        var result = Parse("[appearance.dark]\nbackground = \"rgb(16, 32, 48)\"\nforeground = \"rgb(255, 0, 0)\"\nselection = \"rgba(0, 255, 255, 0.25)\"\n");

        var dark = result.Settings!.Appearance.Dark;
        Assert.Empty(Shown(result));
        Assert.Equal(Rgba.Opaque(16, 32, 48), dark.Background);
        Assert.Equal(Rgba.Opaque(255, 0, 0), dark.Foreground);
        Assert.Equal(new Rgba(64, 0, 255, 255), dark.Selection);
    }

    [Fact]
    public void Alpha_in_an_rgba_background_is_ignored_and_reported_like_in_hex()
    {
        var result = Parse("[appearance.dark]\nbackground = \"rgba(16, 32, 48, 0.5)\"\n");

        Assert.Equal(Rgba.Opaque(16, 32, 48), result.Settings!.Appearance.Dark.Background);
        Assert.Contains("alpha ignored", Assert.Single(Shown(result)).Message);
    }

    [Fact]
    public void The_border_is_on_by_default()
    {
        Assert.True(AppearanceSettings.Default.Border);
        Assert.True(Appearance("theme = \"dark\"\n").Border);
        Assert.True(Appearance("[appearance]\nradius = 3\n").Border);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void The_border_can_be_switched_with_true_or_false(string text, bool expected)
    {
        var result = Parse($"[appearance]\nborder = {text}\n");

        Assert.Empty(Shown(result));
        Assert.Equal(expected, result.Settings!.Appearance.Border);
    }

    [Theory]
    [InlineData("border = \"false\"")]
    [InlineData("border = 0")]
    public void An_invalid_border_is_reported_and_stays_on(string line)
    {
        var result = Parse($"[appearance]\nradius = 4\n{line}\n");

        Assert.True(result.Settings!.Appearance.Border);
        Assert.Equal(4, result.Settings.Appearance.Radius);
        var issue = Assert.Single(Shown(result));
        Assert.Equal(3, issue.Line);
        Assert.Contains("border", issue.Message);
    }

    [Fact]
    public void An_invalid_colour_type_is_reported()
    {
        var result = Parse("[appearance.dark]\nselection = 5\n");

        Assert.Equal(AppearanceSettings.DefaultDark.Selection, result.Settings!.Appearance.Dark.Selection);
        Assert.Single(Shown(result));
    }

    [Fact]
    public void Alpha_in_the_background_is_ignored_and_reported_once()
    {
        var result = Parse("[appearance.dark]\nbackground = \"#80102030\"\n");

        Assert.Equal(Rgba.Opaque(0x10, 0x20, 0x30), result.Settings!.Appearance.Dark.Background);
        var issue = Assert.Single(Shown(result));
        Assert.Equal(2, issue.Line);
        Assert.Contains("alpha ignored", issue.Message);
    }

    [Fact]
    public void Alpha_in_the_foreground_is_ignored_and_reported()
    {
        var result = Parse("[appearance.light]\nforeground = \"#33000000\"\n");

        Assert.Equal(Rgba.Opaque(0, 0, 0), result.Settings!.Appearance.Light.Foreground);
        Assert.Contains("alpha ignored", Assert.Single(Shown(result)).Message);
    }

    [Theory]
    [InlineData("#40FFFFFF", 0x40, 0xFF, 0xFF, 0xFF)]
    [InlineData("#336699", 0xFF, 0x33, 0x66, 0x99)]
    public void The_selection_may_carry_its_own_alpha(string value, int a, int r, int g, int b)
    {
        var result = Parse($"[appearance.dark]\nselection = \"{value}\"\n");

        Assert.Empty(Shown(result));
        Assert.Equal(new Rgba((byte)a, (byte)r, (byte)g, (byte)b), result.Settings!.Appearance.Dark.Selection);
    }

    [Fact]
    public void An_unknown_setting_is_reported_but_everything_else_still_applies()
    {
        var result = Parse("[appearance]\ntransparancy = 10\nradius = 5\n");

        Assert.True(result.Ok);
        Assert.Equal(5, result.Settings!.Appearance.Radius);
        var issue = Assert.Single(Shown(result));
        Assert.Equal(2, issue.Line);
        Assert.Contains("transparancy", issue.Message);
    }

    [Fact]
    public void A_header_with_a_trailing_comment_still_gets_the_right_line_numbers()
    {
        var result = Parse("theme = \"dark\"\n[appearance]   # look\nblur = \"nope\"\n");

        Assert.Equal(3, Assert.Single(Shown(result)).Line);
    }

    [Fact]
    public void A_broken_appearance_value_never_makes_the_file_an_error()
    {
        var result = Parse("theme = \"dark\"\n[appearance]\ntransparency = 500\nblur = \"x\"\nradius = -1\n[appearance.dark]\nbackground = \"x\"\n");

        Assert.True(result.Ok);
        Assert.DoesNotContain(result.Issues, i => i.IsError);
        Assert.Equal(4, Shown(result).Count);
        Assert.Equal(AppearanceSettings.Default, result.Settings!.Appearance);
    }

    [Fact]
    public void The_theme_blocks_are_independent_and_both_validated()
    {
        var result = Parse("[appearance.dark]\nbackground = \"x\"\n[appearance.light]\nbackground = \"y\"\n");

        Assert.Equal(2, Shown(result).Count);
    }
}

public class AppearanceMathTests
{
    [Theory]
    [InlineData(0, 100, 255)]
    [InlineData(50, 50, 128)]
    [InlineData(100, 0, 0)]
    [InlineData(25, 75, 191)]
    public void Transparency_becomes_opacity_and_alpha(int transparency, int opacityPercent, int alpha)
    {
        var settings = AppearanceSettings.Default with { Transparency = transparency };

        Assert.Equal(opacityPercent, settings.OpacityPercent);
        Assert.Equal(alpha, settings.SurfaceAlpha);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 6)]
    [InlineData(32, 19)]
    public void The_row_radius_is_a_bit_smaller_than_the_window_radius(int radius, int expected)
    {
        Assert.Equal(expected, (AppearanceSettings.Default with { Radius = radius }).RowRadius);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 4)]
    [InlineData(5, 4)]
    [InlineData(6, 8)]
    [InlineData(10, 8)]
    [InlineData(32, 8)]
    public void On_a_system_material_the_radius_snaps_to_the_corner_sizes_DWM_offers(int radius, int expected)
    {
        Assert.Equal(expected, AppearanceSettings.NativeCornerRadius(radius));
    }

    [Fact]
    public void The_surface_only_takes_the_transparency_text_and_lines_stay_as_opaque_as_the_foreground()
    {
        var clear = AppearancePalette.Derive(AppearanceSettings.Default with { Transparency = 100 }, dark: true);
        var solid = AppearancePalette.Derive(AppearanceSettings.Default with { Transparency = 0 }, dark: true);

        Assert.Equal(0, clear.Surface.A);
        Assert.Equal(255, solid.Surface.A);
        Assert.Equal(solid with { Surface = clear.Surface }, clear);
        Assert.Equal(255, clear.Text.A);
    }

    [Fact]
    public void Secondary_colours_are_the_foreground_with_less_opacity()
    {
        var palette = AppearancePalette.Derive(AppearanceSettings.Default, dark: true);
        var fg = AppearanceSettings.DefaultDark.Foreground;

        foreach (var derived in new[] { palette.Muted, palette.Subtle, palette.Placeholder, palette.Separator, palette.ScrollThumb, palette.Progress })
        {
            Assert.Equal((fg.R, fg.G, fg.B), (derived.R, derived.G, derived.B));
            Assert.InRange(derived.A, 1, 254);
        }

        Assert.True(palette.Muted.A > palette.Subtle.A);
        Assert.True(palette.Subtle.A > palette.Separator.A);
    }

    [Fact]
    public void Hover_is_the_selection_with_half_the_alpha()
    {
        var dark = AppearancePalette.Derive(AppearanceSettings.Default, dark: true);
        var custom = AppearancePalette.Derive(AppearanceSettings.Default with
        {
            Light = AppearanceSettings.DefaultLight with { Selection = new Rgba(0x80, 1, 2, 3) },
        }, dark: false);

        Assert.Equal(new Rgba(0x40, 0xFF, 0xFF, 0xFF), dark.RowSelected);
        Assert.Equal(new Rgba(0x20, 0xFF, 0xFF, 0xFF), dark.RowHover);
        Assert.Equal(new Rgba(0x40, 1, 2, 3), custom.RowHover);
    }

    [Fact]
    public void The_active_theme_decides_which_block_is_used()
    {
        var dark = AppearancePalette.Derive(AppearanceSettings.Default, dark: true);
        var light = AppearancePalette.Derive(AppearanceSettings.Default, dark: false);

        Assert.Equal(AppearanceSettings.DefaultDark.Foreground, dark.Text);
        Assert.Equal(AppearanceSettings.DefaultLight.Foreground, light.Text);
        Assert.True(dark.BackgroundIsDark);
        Assert.False(light.BackgroundIsDark);
    }

    [Fact]
    public void The_edge_is_an_opaque_blend_of_foreground_and_background()
    {
        var edge = AppearancePalette.Derive(AppearanceSettings.Default, dark: true).Edge;

        Assert.Equal(255, edge.A);
        Assert.True(edge.R > 0 && edge.R < 0xF0);
    }
}
