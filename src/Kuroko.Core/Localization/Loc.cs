using System.Globalization;

namespace Kuroko.Core.Localization;

/// <summary>Minimal UI localization (German and English). Anything but "de" falls back to English.</summary>
public static class Loc
{
    private static readonly Dictionary<string, (string De, string En)> Texts = new()
    {
        ["search_placeholder"] = ("Prompt suchen …", "Search prompts …"),
        ["no_results"] = ("Keine Treffer", "No matches"),
        ["working"] = ("{0} …", "{0} …"),

        ["err_elevated"] = ("{0} läuft mit Administratorrechten; dort ist kein Zugriff möglich.",
            "{0} runs as administrator; access is not possible."),
        ["err_password"] = ("Passwortfelder werden nicht verarbeitet.", "Password fields are not processed."),
        ["err_terminal"] = ("Terminals werden nicht unterstützt (Strg+C würde das laufende Programm abbrechen).",
            "Terminals are not supported (Ctrl+C would interrupt the running program)."),
        ["err_no_text"] = ("Kein Text gefunden.", "No text found."),
        ["err_no_selection"] = ("Kein Text markiert.", "No text selected."),
        ["result_copied"] = ("Kopiert", "Copied"),
        ["err_too_long"] = ("Der Text ist zu lang (mehr als 50.000 Zeichen).", "The text is too long (more than 50,000 characters)."),
        ["err_read_only"] = ("Dieses Feld ist schreibgeschützt.", "This field is read-only."),
        ["llm_auth"] = ("{0}: API-Schlüssel fehlt oder wurde abgelehnt{1}.", "{0}: API key missing or rejected{1}."),
        ["llm_rate_limit"] = ("{0}: Rate-Limit oder Kontingent erreicht{1}.", "{0}: rate limit or quota reached{1}."),
        ["llm_model"] = ("{0}: Modell oder Adresse nicht gefunden{1}.", "{0}: model or address not found{1}."),
        ["llm_bad_request"] = ("{0}: Anfrage abgelehnt{1}.", "{0}: request rejected{1}."),
        ["llm_server"] = ("{0}: Serverfehler beim Anbieter{1}. Der Text blieb unverändert.", "{0}: provider server error{1}. The text is unchanged."),
        ["llm_network"] = ("{0}: Keine Verbindung. Der Text blieb unverändert.", "{0}: no connection. The text is unchanged."),
        ["llm_timeout"] = ("{0}: Keine Antwort innerhalb der Zeitgrenze. Der Text blieb unverändert.", "{0}: no answer in time. The text is unchanged."),
        ["llm_blocked"] = ("{0}: Die Antwort wurde vom Anbieter blockiert. Der Text blieb unverändert.", "{0}: the answer was blocked by the provider. The text is unchanged."),
        ["llm_truncated"] = ("{0}: Die Antwort war zu lang und wurde abgeschnitten. Der Text blieb unverändert.", "{0}: the answer was cut off. The text is unchanged."),
        ["llm_empty"] = ("{0}: Leere Antwort. Der Text blieb unverändert.", "{0}: empty answer. The text is unchanged."),
        ["llm_protocol"] = ("{0}: Unerwartete Antwort{1}. Der Text blieb unverändert.", "{0}: unexpected answer{1}. The text is unchanged."),
        ["llm_config"] = ("Kein Anbieter eingerichtet: {0}", "No provider set up: {0}"),
        ["err_marker_unclosed"] = ("Marker {0} ohne schließendes {1}.", "Marker {0} without a closing {1}."),
        ["err_marker_empty"] = ("Der Marker enthält keine Anweisung.", "The marker contains no instruction."),
        ["err_hotkey_in_use"] = ("Hotkey {0} ({1}) ist bereits von einer anderen App belegt.",
            "Hotkey {0} ({1}) is already used by another app."),
        ["cfg_error"] = ("{0} Die letzte funktionierende Konfiguration bleibt aktiv.", "{0} The last working configuration stays active."),
        ["cfg_more"] = (" (+{0} weitere)", " (+{0} more)"),
        ["cfg_line"] = ("Zeile", "line"),
        ["cfg_reloaded"] = ("Konfiguration geladen ({0} Prompts).", "Configuration loaded ({0} prompts)."),
        ["first_run"] = ("Kuroko läuft. Overlay öffnen: {0}. Einstellungen: Tray-Symbol → Config-Ordner öffnen.",
            "Kuroko is running. Open the overlay with {0}. Settings: tray icon → Open config folder."),
        ["hint_no_key"] = ("Für „{0}“ fehlt der API-Schlüssel (Tray-Symbol → API-Schlüssel … oder settings.toml).",
            "The API key for \"{0}\" is missing (tray icon → API keys … or settings.toml)."),
        ["hint_no_provider"] = ("Kein KI-Anbieter eingerichtet (settings.toml).", "No AI provider set up (settings.toml)."),
        ["legacy_running"] = ("Ghostwriter (der alte Name von Kuroko) läuft noch. Bitte Ghostwriter über das Tray-Symbol beenden und Kuroko dann neu starten; Einstellungen und Prompts werden dabei übernommen.",
            "Ghostwriter (the old name of Kuroko) is still running. Please quit Ghostwriter from its tray icon, then start Kuroko again; settings and prompts are taken over."),
        ["autostart_failed"] = ("Autostart konnte nicht geändert werden.", "Autostart could not be changed."),
        ["tray_autostart"] = ("Mit Windows starten", "Start with Windows"),
        ["tray_open_config"] = ("Config-Ordner öffnen", "Open config folder"),
        ["tray_reload"] = ("Config neu laden", "Reload config"),
        ["err_clipboard_busy"] = ("Die Zwischenablage ist gerade belegt.", "The clipboard is busy."),
        ["err_input_blocked"] = ("Die Tastatureingabe wurde blockiert.", "Keyboard input was blocked."),
        ["err_no_window"] = ("Kein aktives Fenster.", "No active window."),
        ["err_target_changed"] = ("Das Fenster hat gewechselt, der Text wurde nicht ersetzt.",
            "The window changed; the text was not replaced."),
        ["err_paste_ack"] = ("Die Ziel-App hat das Einfügen nicht bestätigt. Bitte den Text prüfen (Strg+Z macht es rückgängig).",
            "The target app did not confirm the paste. Please check the text (Ctrl+Z undoes it)."),
        ["err_select_all"] = ("Das Feld konnte nicht vollständig markiert werden, der Text blieb unverändert.",
            "The field could not be fully selected; the text is unchanged."),
        ["err_focus"] = ("Das Zielfenster konnte nicht wieder aktiviert werden.", "The target window could not be re-activated."),
        ["err_unexpected"] = ("Unerwarteter Fehler: {0}", "Unexpected error: {0}"),
        ["undo_label"] = ("Rückgängig", "Undo"),
        ["undo_done"] = ("Rückgängig gemacht", "Undone"),
        ["undo_nothing"] = ("Nichts zum Rückgängigmachen.", "Nothing to undo."),
        ["undo_other_field"] = ("Keine Ersetzung für dieses Feld gemerkt, nichts geändert.",
            "No replacement remembered for this field; nothing changed."),
        ["undo_changed"] = ("Der Text wurde inzwischen geändert, nichts überschrieben.",
            "The text has been changed since; nothing was overwritten."),
        ["undo_ambiguous"] = ("Das Ergebnis kommt mehrfach im Feld vor, nichts überschrieben.",
            "The result appears more than once in the field; nothing was overwritten."),

        ["tray_keys"] = ("API-Schlüssel …", "API keys …"),
        ["keys_title"] = ("Kuroko - API-Schlüssel", "Kuroko - API keys"),
        ["keys_intro"] = ("Der Schlüssel wird in der Windows-Anmeldeinformationsverwaltung gespeichert (nur für dein Benutzerkonto), nicht in settings.toml.",
            "The key is stored in the Windows Credential Manager (for your user account only), not in settings.toml."),
        ["keys_provider"] = ("Anbieter", "Provider"),
        ["keys_key"] = ("Schlüssel", "Key"),
        ["keys_save"] = ("Speichern", "Save"),
        ["keys_remove"] = ("Entfernen", "Remove"),
        ["keys_close"] = ("Schließen", "Close"),
        ["keys_none"] = ("Kein Anbieter in settings.toml eingerichtet.", "No provider set up in settings.toml."),
        ["keys_src_none"] = ("Aktuell: kein Schlüssel.", "Currently: no key."),
        ["keys_src_file"] = ("Aktuell: Schlüssel direkt in settings.toml (api_key, hat Vorrang).", "Currently: key written in settings.toml (api_key, takes precedence)."),
        ["keys_src_env"] = ("Aktuell: Umgebungsvariable {0} (hat Vorrang vor dem gespeicherten Schlüssel).",
            "Currently: environment variable {0} (takes precedence over the stored key)."),
        ["keys_src_store"] = ("Aktuell: gespeichert in der Windows-Anmeldeinformationsverwaltung ({0}).", "Currently: stored in the Windows Credential Manager ({0})."),
        ["keys_saved"] = ("Schlüssel für „{0}“ gespeichert.", "Key for \"{0}\" saved."),
        ["keys_removed"] = ("Gespeicherter Schlüssel für „{0}“ entfernt.", "Stored key for \"{0}\" removed."),
        ["keys_failed"] = ("Das hat nicht geklappt (Details in kuroko.log).", "That did not work (details in kuroko.log)."),
        ["keys_empty"] = ("Bitte zuerst einen Schlüssel eingeben.", "Please enter a key first."),

        ["tray_quit"] = ("Beenden", "Quit"),
    };

    public static string Language { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    public static string Get(string key, params object[] args)
    {
        if (!Texts.TryGetValue(key, out var pair)) return key;
        var text = Language.Equals("de", StringComparison.OrdinalIgnoreCase) ? pair.De : pair.En;
        return args.Length == 0 ? text : string.Format(CultureInfo.CurrentCulture, text, args);
    }
}
