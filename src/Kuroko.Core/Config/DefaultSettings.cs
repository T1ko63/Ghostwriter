using System.Globalization;

namespace Kuroko.Core.Config;

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

        return string.Create(CultureInfo.InvariantCulture, $$"""
            # Kuroko - Einstellungen. Änderungen werden nach dem Speichern automatisch übernommen (auch Hotkeys).
            # Bei einem Fehler in der Datei bleibt die letzte funktionierende Konfiguration aktiv und es erscheint eine Meldung.
            # Prompts stehen in prompts.toml.

            # Globaler Hotkey, der das Overlay öffnet.
            overlay_hotkey = "{{AppSettings.DefaultOverlayHotkey}}"

            # Macht die letzte Ersetzung rückgängig und schreibt den ursprünglichen Text zurück (mehrfach drücken = Schritt für Schritt weiter).
            # Es wird nichts überschrieben, wenn der Text im Feld inzwischen geändert wurde oder ein anderes Feld den Fokus hat.
            undo_hotkey = "{{AppSettings.DefaultUndoHotkey}}"
            # So viele Ersetzungen merkt sich die App (nur im Arbeitsspeicher, nie auf der Festplatte; 0 = Rückgängig aus).
            undo_history = {{AppSettings.DefaultUndoHistory}}

            # Kopiert den ganzen Text der Ergebnis-Karte (Prompts mit output = "overlay" in prompts.toml) in die Zwischenablage und schließt die Karte.
            # Der Hotkey ist nur registriert, solange eine Karte sichtbar ist; Esc schließt die Karte (und bricht eine laufende Anfrage ab).
            # Er kopiert auch die fertige Antwort, solange die Fehlerpille eines gescheiterten Einfügens zu sehen ist (später: Tray → Letztes Ergebnis kopieren).
            # Nicht erlaubt ist einfaches Strg+C, Strg+A und Strg+V: Diese Tasten sendet die App beim Lesen des Textes selbst.
            result_copy_hotkey = "{{AppSettings.DefaultResultCopyHotkey}}"

            # Wo die Ergebnis-Karte erscheint: "fixed" (an result_fixed_position auf dem Monitor, auf dem das Zielfenster liegt, Standard)
            # | "follow" (dort, wo sich das Prompt-Menü öffnet, siehe overlay_position) | "caret" (am Textcursor, sonst an der Maus) | "mouse" (an der Maus).
            # Bei follow, caret und mouse wächst die Karte nach unten, bei zu wenig Platz nach oben.
            result_position = "{{AppSettings.DefaultResultPosition}}"
            # Der feste Platz: top-third | center | bottom-third (waagerecht mittig, in der Mitte dieses Drittels), top-left | bottom-left |
            # top-right | bottom-right (Ecken), left | right (an diesem Rand, senkrecht mittig), top | bottom (an diesem Rand, waagerecht mittig).
            # Die Karte wächst immer vom Bildschirmrand weg (oben nach unten, unten nach oben, in der Mitte in beide Richtungen).
            result_fixed_position = "{{AppSettings.DefaultResultFixedPosition}}"
            # Abstand der Karte zum Bildschirmrand in Pixeln, 0 bis 200.
            result_screen_margin = {{AppSettings.DefaultResultScreenMargin}}
            # Schriftgröße des Kartentexts, 8 bis 32 (auch Dezimalwerte wie 14.5).
            result_font_size = {{AppSettings.DefaultResultFontSize}}
            # Größe der Karte in Pixeln (sie wird nie größer als der Monitor). Die Breite ist fest (240 bis 1600). Die Höhe folgt dem Text,
            # ist aber mindestens result_min_height (0 bis 2000, 0 = so klein wie der Text) und höchstens result_max_height (120 bis 2000); darüber scrollt der Text.
            result_width = {{AppSettings.DefaultResultWidth}}
            result_min_height = {{AppSettings.DefaultResultMinHeight}}
            result_max_height = {{AppSettings.DefaultResultMaxHeight}}

            # Marker für einmalige Anweisungen im Text, z. B.:  <<kürzer und freundlicher>> Hallo ...
            # Sie wirken in Prompts mit mode = "universal" (siehe prompts.toml): ohne Marker ist der ganze Text ein Auftrag,
            # mit Marker wird dessen Inhalt auf den übrigen Text angewendet. Der Marker steht nie im Ergebnis.
            # Die Marker dürfen am Anfang, am Ende oder mitten im Text stehen. Alternativen: "^^" und "^^", oder "%%" und "%%".
            marker_start = "<<"
            marker_end = ">>"

            theme = "system"            # system | light | dark
            # Akzentfarbe für Textcursor, Textmarkierung und Fortschrittslinie (die Auswahlzeile färbt [appearance] selection): "none" (neutral, Standard) | "system" (Windows-Akzent) | Hex wie "#3B82F6".
            accent = "none"
            overlay_position = "caret"  # caret (am Textcursor, sonst Maus) | mouse | fixed (fester Platz, siehe unten)
            # Prompt-Auswahl (und Fortschrittspille): fester Platz und Größe wie bei der Ergebnis-Karte. Der feste Platz (overlay_position = "fixed"):
            # top-third | center | bottom-third | top-left | bottom-left | top-right | bottom-right | left | right | top | bottom.
            # Das Menü wächst vom Bildschirmrand weg. Der Abstand zum Bildschirmrand gilt in Pixeln (0 bis 200).
            overlay_fixed_position = "{{AppSettings.DefaultOverlayFixedPosition}}"
            overlay_screen_margin = {{AppSettings.DefaultOverlayScreenMargin}}
            overlay_font_size = {{AppSettings.DefaultOverlayFontSize}}    # 8 bis 32; das ganze Menü skaliert mit
            overlay_width = {{AppSettings.DefaultOverlayWidth}}       # 240 bis 1600, nie größer als der Monitor
            overlay_min_height = {{AppSettings.DefaultOverlayMinHeight}}       # 0 bis 2000 (0 = so klein wie die Liste)
            overlay_max_height = {{AppSettings.DefaultOverlayMaxHeight}}    # 120 bis 2000: Suchzeile plus acht Zeilen; längere Listen scrollen
            language = "auto"           # auto | de | en
            autostart = false           # mit Windows starten (auch über das Tray-Menü schaltbar)

            # Nach so vielen Sekunden Ruhe gibt die App ungenutzten Arbeitsspeicher frei (0 = nie).
            # Das kostet beim nächsten Hotkey etwa 2 ms und senkt die Anzeige im Task-Manager von ca. 200 auf wenige MB.
            idle_trim_seconds = {{AppSettings.DefaultIdleTrimSeconds}}

            # Anbieter, der verwendet wird, wenn ein Prompt keinen eigenen angibt.
            default_provider = "{{defaultProvider}}"

            # Ausweich-Anbieter (leer = aus). Ist ein Anbieter nicht erreichbar (keine Verbindung, Serverfehler 5xx, Rate-Limit 429),
            # geht dieselbe Anfrage einmal an diesen Anbieter, z. B. "local" für das Ollama-Modell unten.
            # Datenschutz: Dein Text geht dann an diesen Anbieter statt an den des Prompts. Nicht nach Esc, nicht bei abgelehntem
            # Schlüssel oder abgelehnter Anfrage (4xx) und nicht nach einer Zeitüberschreitung; nie mehr als ein Schritt.
            # Lokale Anbieter nutzen ihn nicht (ihr Text bleibt auf dem Rechner), außer ihr eigener Block nennt einen.
            # Pro Anbieter geht auch fallback_provider = "name" im Block [providers.name]; das gilt dann vor diesem Wert ("" = keiner).
            fallback_provider = ""

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
            # true: der Radius gilt auch bei acrylic/blur/mica exakt. Braucht den Windhawk-Mod "Kuroko Blur" (Teil "Ecken", läuft in dwm.exe);
            # Windows selbst kennt dort nur 0, 4 und 8 px. Ohne Mod bleibt es bei der Rundung von Windows. false: wie bisher.
            custom_corners = false

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
            # Ohne beides gilt der Schlüssel aus der Windows-Anmeldeinformationsverwaltung (Tray-Symbol → API-Schlüssel …).
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
            """);
    }

    public static void EnsureExists(string path, Func<string, string?> getEnv)
    {
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Create(getEnv), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
