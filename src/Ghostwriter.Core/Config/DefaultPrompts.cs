using System.Text;
using Ghostwriter.Core.Prompts;

namespace Ghostwriter.Core.Config;

/// <summary>The prompts.toml written on first start, generated from the built-in prompt collection.</summary>
public static class DefaultPrompts
{
    public static string Create()
    {
        var text = new StringBuilder();
        text.AppendLine("# Ghostwriter - Prompts. Änderungen werden nach dem Speichern automatisch übernommen.");
        text.AppendLine("# Bearbeiten, löschen und ergänzen ist ausdrücklich erwünscht. Pro Prompt ein [[prompt]]-Block:");
        text.AppendLine("#   name     Anzeigename im Overlay (Pflicht, eindeutig)");
        text.AppendLine("#   prompt   die Anweisung an die KI (Pflicht); sie soll nur den reinen Ergebnistext verlangen");
        text.AppendLine("#   mode     \"transform\" (Standard): der markierte Text bzw. das ganze Feld wird bearbeitet");
        text.AppendLine("#            \"instruction\": der Text im Feld ist selbst die Anweisung (Auftrag)");
        text.AppendLine("#            \"universal\": beides in einem. Ohne Marker ist der ganze Text ein Auftrag (\"Absage an Freunde\");");
        text.AppendLine("#                         mit Marker wird dessen Inhalt auf den übrigen Text angewendet (\"<<als Rapsong>> Hallo ...\").");
        text.AppendLine("#                         Die Marker-Zeichen stehen in settings.toml (marker_start / marker_end).");
        text.AppendLine("#   hotkey   optional, z. B. \"Ctrl+Alt+K\": führt den Prompt direkt aus, ohne Overlay");
        text.AppendLine("#   provider optional: Name eines [providers.*] aus settings.toml (sonst der Standard-Anbieter)");
        text.AppendLine("#   model    optional: Modell, das statt dem des Anbieters verwendet wird");

        foreach (var prompt in BuiltInPrompts.All)
        {
            text.AppendLine();
            text.AppendLine("[[prompt]]");
            text.AppendLine($"name = {Quote(prompt.Name)}");
            text.AppendLine($"mode = \"{ModeText(prompt.Mode)}\"");
            if (prompt.Hotkey is not null) text.AppendLine($"hotkey = {Quote(prompt.Hotkey)}");
            text.AppendLine("prompt = \"\"\"");
            text.AppendLine(EscapeMultiline(prompt.Prompt));
            text.AppendLine("\"\"\"");
        }

        return text.ToString();
    }

    public static void EnsureExists(string path)
    {
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Create(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public static string ModeText(PromptMode mode) => mode switch
    {
        PromptMode.Instruction => "instruction",
        PromptMode.Universal => "universal",
        _ => "transform",
    };

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string EscapeMultiline(string value)
        => value.Replace("\\", "\\\\").Replace("\"\"\"", "\"\"\\\"");
}
