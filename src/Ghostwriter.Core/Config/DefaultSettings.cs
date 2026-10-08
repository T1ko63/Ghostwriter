namespace Ghostwriter.Core.Config;

/// <summary>The settings.toml written on first start.</summary>
public static class DefaultSettings
{
    /// <param name="getEnv">Used to pick the default provider whose key is already in the environment.</param>
    public static string Create(Func<string, string?> getEnv)
    {
        bool Has(string name) => !string.IsNullOrWhiteSpace(getEnv(name));
        var defaultProvider = Has("GEMINI_API_KEY") ? "gemini"
            : Has("ANTHROPIC_API_KEY") ? "anthropic"
            : Has("OPENAI_API_KEY") ? "openai"
            : "gemini";

        return $$"""
            # Ghostwriter - Einstellungen. Änderungen werden nach dem Speichern automatisch übernommen (auch Hotkeys).
            # Bei einem Fehler in der Datei bleibt die letzte funktionierende Konfiguration aktiv und es erscheint eine Meldung.
            # Prompts stehen in prompts.toml.

            # Globaler Hotkey, der das Overlay öffnet.
            overlay_hotkey = "{{AppSettings.DefaultOverlayHotkey}}"

            # Macht die letzte Ersetzung rückgängig und schreibt den ursprünglichen Text zurück (mehrfach drücken = Schritt für Schritt weiter).
            # Es wird nichts überschrieben, wenn der Text im Feld inzwischen geändert wurde oder ein anderes Feld den Fokus hat.
            undo_hotkey = "{{AppSettings.DefaultUndoHotkey}}"
            # So viele Ersetzungen merkt sich die App (nur im Arbeitsspeicher, nie auf der Festplatte; 0 = Rückgängig aus).
            undo_history = {{AppSettings.DefaultUndoHistory}}

            # Marker für einmalige Anweisungen im Text, z. B.:  <<kürzer und freundlicher>> Hallo ...
            # Sie wirken in Prompts mit mode = "universal" (siehe prompts.toml): ohne Marker ist der ganze Text ein Auftrag,
            # mit Marker wird dessen Inhalt auf den übrigen Text angewendet. Der Marker steht nie im Ergebnis.
            # Die Marker dürfen am Anfang, am Ende oder mitten im Text stehen. Alternativen: "^^" und "^^", oder "%%" und "%%".
            marker_start = "<<"
            marker_end = ">>"

            theme = "system"            # system | light | dark
            # Akzentfarbe für Textcursor, Textmarkierung und Fortschrittslinie (die Auswahlzeile färbt [appearance] selection): "none" (neutral, Standard) | "system" (Windows-Akzent) | Hex wie "#3B82F6".
            accent = "none"
            overlay_position = "caret"  # caret (am Textcursor, sonst Maus) | mouse | center
            language = "auto"           # auto | de | en
            autostart = false           # mit Windows starten (auch über das Tray-Menü schaltbar)

            # Nach so vielen Sekunden Ruhe gibt die App ungenutzten Arbeitsspeicher frei (0 = nie).
            # Das kostet beim nächsten Hotkey etwa 2 ms und senkt die Anzeige im Task-Manager von ca. 200 auf wenige MB.
            idle_trim_seconds = {{AppSettings.DefaultIdleTrimSeconds}}

            # Anbieter, der verwendet wird, wenn ein Prompt keinen eigenen angibt.
            default_provider = "{{defaultProvider}}"

            # Aussehen des Overlays. Fehlt ein Wert, gilt der hier gezeigte Standard. Ein ungültiger Wert wird gemeldet und durch den Standard ersetzt.
            # Gilt der Block des aktiven Themes (theme oben): [appearance.dark] oder [appearance.light].
            [appearance]
            # Durchsichtigkeit der Fensterfläche in Prozent: 0 = deckend, 100 = vollständig durchsichtig. Text bleibt immer voll lesbar.
            # Über 90 leidet die Lesbarkeit (der Hintergrund scheint fast ungehindert durch).
            transparency = {{AppearanceSettings.DefaultTransparency}}
            # Eckenradius des Fensters in px (0-{{AppearanceSettings.MaxRadius}}).
            radius = {{AppearanceSettings.DefaultRadius}}
            # "acrylic" (Windows-11-Acrylic: Blur mit Rauschen und Systemtönung) | "blur" (schlichter Blur ohne Systemtönung, am stärksten durchsichtig)
            # | "mica" / "micaalt" (Windows-11-Materialien, undurchsichtig, nehmen nur die Farbe des Hintergrundbilds auf; micaalt ist dunkler/kräftiger) | "none" (kein Blur).
            # Bei acrylic, blur und mica rundet Windows die Ecken selbst (0, 4 oder 8 px); nur "none" nutzt den Radius exakt.
            # Ohne Windows-11-Unterstützung entsteht automatisch eine halbtransparente einfarbige Fläche.
            blur = "acrylic"
            # Feiner Rand um das Fenster: true | false.
            border = true

            [appearance.dark]
            background = "#000000"     # Farbe der Fensterfläche als #RRGGBB oder rgb(r, g, b), ohne Transparenz (die steuert transparency)
            foreground = "#F0F0F0"     # Haupttext; gedämpfter Text, Trennlinie und Scrollbar werden daraus abgeleitet
            # Hervorhebung der ausgewählten Zeile (Mauszeiger darüber: halbe Deckkraft). Erlaubt: #AARRGGBB, #RRGGBB oder rgba(r, g, b, a) mit a von 0 bis 1.
            # Achtung: Das Alpha steht VORNE (wie bei Windows/WPF), nicht hinten wie im CSS-Format #RRGGBBAA. Editoren wie VS Code zeigen #AARRGGBB deshalb falsch an;
            # deren Farbwähler schreibt rgb(...)/rgba(...), das hier eindeutig ist und ebenfalls funktioniert.
            selection  = "#40FFFFFF"

            [appearance.light]
            background = "#FFFFFF"
            foreground = "#1A1A1A"
            selection  = "#40000000"

            # Pro Anbieter: type, model und ein API-Schlüssel (api_key direkt ODER api_key_env = Name einer Umgebungsvariable).
            # Modellnamen gehören nur hierher; sie sind nirgends im Programm festgelegt - bitte auf aktuelle Namen prüfen.
            # reasoning = "off" hält die Antwort schnell (kein langes Nachdenken). Weitere Werte: default | low | medium | high.
            # Weitere optionale Felder: base_url, timeout_seconds (Standard 30), max_output_tokens (0 = automatisch).

            # Für kurze Umformulierungen zählt Tempo: gemini-2.5-flash antwortet gemessen in ca. 0,8 s, gemini-3.8-flash in 2-3 s.
            [providers.gemini]
            type = "gemini"
            model = "gemini-2.5-flash"
            api_key_env = "GEMINI_API_KEY"
            reasoning = "off"

            [providers.anthropic]
            type = "anthropic"
            model = "claude-haiku-4-5"
            api_key_env = "ANTHROPIC_API_KEY"
            reasoning = "off"

            [providers.openai]
            type = "openai"
            model = "gpt-5.5"
            api_key_env = "OPENAI_API_KEY"
            reasoning = "off"

            # Lokale Modelle über OpenAI-kompatible Server: Ollama (Port 11434) oder LM Studio (http://localhost:1234/v1).
            [providers.local]
            type = "openai-compatible"
            base_url = "http://localhost:11434/v1"
            model = "llama3.2"
            """;
    }

    public static void EnsureExists(string path, Func<string, string?> getEnv)
    {
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Create(getEnv), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
