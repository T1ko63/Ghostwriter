using System.Text.Json;
using Kuroko.Core.Config;
using Tomlyn;
using Tomlyn.Model;

namespace Kuroko.Tests;

/// <summary>
/// Keeps schemas/*.schema.json (editor support via Taplo / Even Better TOML), the example configs and the loaders in step:
/// the examples load cleanly, the schemas name exactly the keys the examples use, and every value a schema allows is accepted by the app.
/// </summary>
public class SchemaTests
{
    private const string MinimalProvider = "[providers.x]\ntype = \"openai-compatible\"\nmodel = \"m\"\n";

    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Kuroko.slnx"))) return dir.FullName;
        }

        throw new InvalidOperationException("repository root (Kuroko.slnx) not found");
    }

    private static string ReadFile(string relative) => File.ReadAllText(Path.Combine(Root, relative));

    private static JsonElement Schema(string name) => JsonDocument.Parse(ReadFile(Path.Combine("schemas", name))).RootElement;

    private static TomlTable Toml(string relative) => TomlSerializer.Deserialize<TomlTable>(ReadFile(relative))!;

    /// <summary>Follows "$ref" and a single-entry "allOf" so a property shows the constraints it really has.</summary>
    private static JsonElement Resolve(JsonElement schema, JsonElement node)
    {
        while (true)
        {
            if (node.TryGetProperty("$ref", out var reference))
            {
                node = schema.GetProperty("definitions").GetProperty(reference.GetString()!.Split('/')[^1]);
            }
            else if (node.TryGetProperty("allOf", out var all) && all.GetArrayLength() == 1)
            {
                var inner = Resolve(schema, all[0]);
                if (!node.TryGetProperty("pattern", out _) && !node.TryGetProperty("enum", out _)) return inner;
                return node;
            }
            else
            {
                return node;
            }
        }
    }

    private static IEnumerable<string> Keys(JsonElement schema, JsonElement node)
        => Resolve(schema, node).GetProperty("properties").EnumerateObject().Select(p => p.Name);

    private static string[] Enum(JsonElement schema, JsonElement node)
        => Resolve(schema, node).TryGetProperty("enum", out var values) ? values.EnumerateArray().Select(v => v.GetString()!).ToArray() : [];

    [Theory]
    [InlineData("config/prompts.example.toml", "../schemas/prompts.schema.json")]
    [InlineData("config/settings.example.toml", "../schemas/settings.schema.json")]
    public void Example_points_at_its_schema_in_the_first_line_and_is_plain_utf8(string example, string schema)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Root, example));
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], $"{example} starts with a byte order mark");
        Assert.Equal($"#:schema {schema}", ReadFile(example).Split('\n')[0].TrimEnd('\r'));
        Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Root, "config", schema))));
    }

    [Theory]
    [InlineData("prompts.schema.json")]
    [InlineData("settings.schema.json")]
    public void Schema_is_plain_utf8_json(string name)
    {
        var bytes = File.ReadAllBytes(Path.Combine(Root, "schemas", name));
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..], $"{name} starts with a byte order mark (Taplo cannot read it)");
        Assert.Equal(JsonValueKind.Object, Schema(name).ValueKind);
    }

    [Fact]
    public void Example_prompts_load_without_issues()
    {
        var result = PromptsLoader.Parse(ReadFile("config/prompts.example.toml"));
        Assert.True(result.Ok, string.Join("; ", result.Issues));
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Example_settings_load_without_errors_or_unknown_keys()
    {
        var result = SettingsLoader.Parse(ReadFile("config/settings.example.toml"), _ => null);
        Assert.True(result.Ok, string.Join("; ", result.Issues));
        Assert.DoesNotContain(result.Issues, i => i.Message.Contains("unknown", StringComparison.Ordinal));
    }

    [Fact]
    public void Prompt_schema_names_exactly_the_keys_of_the_example()
    {
        var schema = Schema("prompts.schema.json");
        var promptNode = schema.GetProperty("properties").GetProperty("prompt").GetProperty("items");
        var used = ((TomlTableArray)Toml("config/prompts.example.toml")["prompt"]).SelectMany(t => t.Keys).ToHashSet();

        Assert.Equal(["prompt"], Keys(schema, schema));
        Assert.Equal(Keys(schema, promptNode).Order(), used.Order());
    }

    [Fact]
    public void Settings_schema_names_exactly_the_keys_of_the_example()
    {
        var schema = Schema("settings.schema.json");
        var example = Toml("config/settings.example.toml");
        var appearance = (TomlTable)example["appearance"];
        var providers = ((TomlTable)example["providers"]).Values.Cast<TomlTable>();
        var definitions = schema.GetProperty("definitions");

        Assert.Equal(Keys(schema, schema).Order(), example.Keys.Order());
        Assert.Equal(Keys(schema, definitions.GetProperty("appearance")).Order(), appearance.Keys.Order());
        Assert.Equal(Keys(schema, definitions.GetProperty("themeColors")).Order(), ((TomlTable)appearance["dark"]).Keys.Order());

        // The example leaves out api_key (never in a template), max_output_tokens and fallback_provider; all are listed in its comments.
        var providerKeys = providers.SelectMany(p => p.Keys).Append("api_key").Append("max_output_tokens").Append("fallback_provider").ToHashSet();
        Assert.Equal(Keys(schema, definitions.GetProperty("provider")).Order(), providerKeys.Order());
    }

    [Fact]
    public void Settings_schema_defaults_match_the_example()
    {
        var schema = Schema("settings.schema.json");
        var example = Toml("config/settings.example.toml");
        foreach (var property in schema.GetProperty("properties").EnumerateObject())
        {
            if (!property.Value.TryGetProperty("default", out var fallback)) continue;
            object expected = fallback.ValueKind switch
            {
                JsonValueKind.String => fallback.GetString()!,
                JsonValueKind.True or JsonValueKind.False => fallback.GetBoolean(),
                _ => fallback.GetDouble(),
            };
            var actual = example[property.Name] is long whole ? (double)whole : example[property.Name];
            Assert.True(Equals(expected, actual), $"{property.Name}: schema default {expected}, example {actual}");
        }
    }

    [Fact]
    public void Every_value_a_prompt_schema_enum_allows_is_accepted()
    {
        var schema = Schema("prompts.schema.json");
        var prompt = schema.GetProperty("definitions").GetProperty("prompt").GetProperty("properties");
        foreach (var key in new[] { "mode", "output" })
        {
            foreach (var value in Enum(schema, prompt.GetProperty(key)))
            {
                var result = PromptsLoader.Parse($"[[prompt]]\nname = \"A\"\nprompt = \"x\"\n{key} = \"{value}\"\n");
                Assert.True(result.Ok, $"{key} = {value}: {string.Join("; ", result.Issues)}");
            }
        }
    }

    [Fact]
    public void Every_value_a_settings_schema_enum_allows_is_accepted()
    {
        var schema = Schema("settings.schema.json");
        var definitions = schema.GetProperty("definitions");
        var checkedKeys = 0;

        void Accepts(string toml, string what)
        {
            var result = SettingsLoader.Parse(toml, _ => null);
            Assert.True(result.Ok, $"{what}: {string.Join("; ", result.Issues)}");
            Assert.DoesNotContain(result.Issues, i => i.Show);
            checkedKeys++;
        }

        foreach (var property in schema.GetProperty("properties").EnumerateObject())
        {
            foreach (var value in Enum(schema, property.Value)) Accepts($"{property.Name} = \"{value}\"\n{MinimalProvider}", $"{property.Name} = {value}");
        }

        foreach (var value in Enum(schema, definitions.GetProperty("appearance").GetProperty("properties").GetProperty("blur")))
        {
            Accepts($"[appearance]\nblur = \"{value}\"\n{MinimalProvider}", $"blur = {value}");
        }

        var provider = definitions.GetProperty("provider").GetProperty("properties");
        foreach (var value in Enum(schema, provider.GetProperty("type"))) Accepts($"[providers.x]\ntype = \"{value}\"\nmodel = \"m\"\n", $"type = {value}");
        foreach (var value in Enum(schema, provider.GetProperty("reasoning"))) Accepts($"{MinimalProvider}reasoning = \"{value}\"\n", $"reasoning = {value}");

        Assert.True(checkedKeys > 30, "the schema enums were not found");
    }

    [Theory]
    [InlineData("theme", "sepia")]
    [InlineData("overlay_position", "corner")]
    [InlineData("result_fixed_position", "middle")]
    [InlineData("language", "fr")]
    public void A_value_outside_the_schema_enum_is_rejected_by_the_app_too(string key, string value)
    {
        Assert.DoesNotContain(value, Enum(Schema("settings.schema.json"), Schema("settings.schema.json").GetProperty("properties").GetProperty(key)));
        Assert.False(SettingsLoader.Parse($"{key} = \"{value}\"\n{MinimalProvider}", _ => null).Ok);
    }
}
