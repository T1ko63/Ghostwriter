# InstaPrompt

Eigene KI-Prompts in **jedem** Textfeld unter Windows: Text markieren (oder einfach tippen), Hotkey drücken,
Prompt wählen, das Ergebnis ersetzt den Text direkt im Feld. Kein Fensterwechsel, kein Kopieren und Einfügen.

Die App lebt im Tray, hat kein Hauptfenster und wird über zwei TOML-Dateien konfiguriert.

## Bedienung

| Aktion | Standard-Hotkey |
|---|---|
| Overlay öffnen (Prompt suchen/wählen) | `Ctrl+Shift+Space` |
| „Universal" direkt ausführen (schreiben **und** bearbeiten) | `Ctrl+Alt+M` |
| Anderen Prompt direkt ausführen (z. B. „Correction") | pro Prompt in `prompts.toml`, Standard `Ctrl+Alt+K` |
| Letzte Ersetzung rückgängig machen | `Ctrl+Alt+Z` (`undo_hotkey`) |

**Im Overlay:** sofort tippen filtert (Fuzzy-Suche), `↑`/`↓` und `Enter` wählen, `1`–`9` wählen direkt,
Mausklick geht auch, `Esc` bricht ab. Danach liegt der Fokus wieder genau dort, wo du warst.

**Was bearbeitet wird:** der markierte Text; ist nichts markiert, der gesamte Inhalt des Feldes.

**Universal (ein Prompt zum Schreiben und Bearbeiten):** Ein Prompt mit `mode = "universal"` entscheidet am Text:

* **Ohne Marker** ist der ganze erfasste Text ein Auftrag und wird ausgeführt:
  „Absage an Freunde für den Spieleabend am Freitag" ergibt direkt die fertige Absage.
* **Mit Marker** wird dessen Inhalt auf den übrigen Text angewendet:
  `Hallo Chef, ich bin krank. <<als Rapsong>>`, `<<kürzer und freundlicher>> Sehr geehrter Herr ...`,
  `<<auf Chinesisch>> Guten Morgen`, `... <<als Märchen>> ...`. Der Marker darf am Anfang, am Ende oder mitten im Text stehen.
* In beiden Fällen **ersetzt das Ergebnis den gesamten erfassten Text**, der Marker steht nie in der Ausgabe.
* Steht nur ein Marker da (`<<kurze Nachricht an den Chef wegen Krankschreibung bis Mittwoch>>`), ist sein Inhalt der Auftrag.
* Ein unvollständiger oder leerer Marker wird gemeldet; es wird nichts gesendet und nichts verändert.

Die Marker-Zeichen sind in `settings.toml` änderbar (`marker_start`, `marker_end`; z. B. `^^`/`^^` oder `%%`/`%%`).

**Weitere Modi:** `transform` (Standard) bearbeitet den Text mit der festen Anweisung des Prompts (Korrektur, Übersetzung, ...),
`instruction` behandelt immer den ganzen Text als Auftrag.

**Rückgängig (`Ctrl+Alt+Z`):** Die App merkt sich bei jeder erfolgreichen Ersetzung den Originaltext (samt Marker-Block) und das Ergebnis,
die letzten `undo_history` Stück (Standard 10, `0` = aus), nur im Arbeitsspeicher: nie auf der Festplatte, nie im Log, beim Beenden weg.
Der Hotkey liest das Feld, sucht das gespeicherte Ergebnis darin und schreibt an dieser Stelle das Original zurück (wiederholtes Drücken
geht Schritt für Schritt weiter zurück). Nichts wird überschrieben, wenn ein anderes Fenster/Feld den Fokus hat, das Ergebnis im Text
nicht mehr (unverändert) vorkommt oder mehrfach vorkommt; dann erscheint nur eine kurze Meldung. Zurückgeschrieben wird bewusst vom Programm selbst,
nicht per Strg+Z der Ziel-App, weil dessen Verhalten je nach App unterschiedlich ist. Preis: Der Feldinhalt wird komplett neu eingefügt, daher
können Formatierung (Rich-Text) und Cursorposition verloren gehen; die App-eigene Undo-Liste (Strg+Z) bleibt meist erhalten.

**Aussehen:** Das Overlay ist bewusst reduziert: oben die Suche, darunter nur Name und (falls vorhanden) Hotkey jedes Prompts,
links die Ziffern zum Direktwählen. Auf Windows 11 liegt es auf dem Acrylic-Material des Systems (echter Blur), sonst auf einer
halbtransparenten Fläche. Hell/Dunkel folgt Windows (`theme`), die Auswahl ist neutral; mit `accent` bekommen Auswahlzeile,
Textcursor und Fortschrittslinie eine Farbe. Alle Farben, Radien, Abstände und Schriftgrößen stehen an einer Stelle:
`src\InstaPrompt.App\Themes\Design.xaml`.

**Tray-Menü:** Config-Ordner öffnen · Config neu laden · Mit Windows starten · Beenden.

## Konfiguration

Beim ersten Start entstehen in `%APPDATA%\InstaPrompt\` (oder dem Ordner aus `INSTAPROMPT_CONFIG_DIR`) zwei Dateien
mit Kommentaren: `settings.toml` und `prompts.toml`. Änderungen werden nach dem Speichern **automatisch**
übernommen, auch die Hotkeys. Bei einem Fehler in einer Datei bleibt die letzte funktionierende Version aktiv, und
eine Meldung nennt Datei und Zeile.

### settings.toml (Auszug)

```toml
overlay_hotkey = "Ctrl+Shift+Space"
marker_start = "<<"
marker_end   = ">>"
theme = "system"            # system | light | dark
accent = "none"             # none (neutral) | system (Windows-Akzent) | Hex wie "#3B82F6"
undo_hotkey = "Ctrl+Alt+Z"  # letzte Ersetzung rückgängig
undo_history = 10           # so viele Ersetzungen werden gemerkt (0 = aus)
autostart = false
idle_trim_seconds = 90      # gibt nach Ruhe ungenutzten Speicher frei (0 = nie)
default_provider = "gemini"

[providers.gemini]
type = "gemini"             # openai | gemini | anthropic | openai-compatible
model = "gemini-2.5-flash"
api_key_env = "GEMINI_API_KEY"   # oder api_key = "..."
reasoning = "off"           # off | default | low | medium | high
```

Anbieter: `openai` (Responses API), `gemini`, `anthropic` (Messages API) und `openai-compatible` für lokale Server
wie Ollama oder LM Studio (`base_url = "http://localhost:11434/v1"`). Modellnamen stehen nur in dieser Datei.
Ein API-Schlüssel wird nie über unverschlüsseltes `http://` an fremde Hosts gesendet, nie geloggt und nur an den
gewählten Anbieter geschickt. Keine Telemetrie.

### prompts.toml

```toml
[[prompt]]
name = "Correction"
mode = "transform"          # transform (Standard) | instruction | universal
hotkey = "Ctrl+Alt+K"       # optional: führt den Prompt direkt aus, ohne Overlay
provider = "gemini"         # optional: sonst der Standard-Anbieter
model = "gemini-2.5-pro"    # optional: sonst das Modell des Anbieters
prompt = """
Du bist mein Experte für Rechtschreibung und Grammatik. ...
Gib ausschließlich den korrigierten Text aus.
"""
```

## Bauen, testen, ausführen

Voraussetzung: .NET 10 SDK (Windows).

```powershell
dotnet build                       # Debug
dotnet test                        # Unit-Tests (Marker, Config, Provider mit simuliertem Server, ...)
powershell -File tools\publish.ps1 # Release mit ReadyToRun nach publish\InstaPrompt
powershell -File tools\publish.ps1 -SelfContained   # läuft ohne installierte .NET-Runtime (~130 MB)
```

Das Icon wird mit `tools\make-icon.ps1` erzeugt.

## Aufbau

| Projekt | Inhalt |
|---|---|
| `InstaPrompt.Core` | UI-frei: Prompt-Engine, Marker-Parser, Provider-Adapter (HttpClient + SSE), Konfiguration, Hot-Reload |
| `InstaPrompt.Platform` | Win32: globale Hotkeys, Textzugriff (UI Automation und Zwischenablage), Tray, Fenster-Helfer, Autostart |
| `InstaPrompt.App` | WPF: Overlay, Status-Pille, Themes, Zusammenspiel (`AppController`) |
| `InstaPrompt.Tests` | xUnit |

**Textzugriff, gestuft:** (1) UI Automation für die Markierung, ohne die Zwischenablage anzufassen; (2) Strg+C mit
Marker-Text als Erkennung; (3) ohne Markierung das ganze Feld. Ersetzt wird per Strg+V; die Zwischenablage des Nutzers
wird vorher vollständig gesichert und danach zurückgeschrieben. Der Text im Feld wird erst angefasst, wenn die
KI-Antwort vollständig und fehlerfrei da ist.

**Bewusst nicht unterstützt:** Passwortfelder, schreibgeschützte Felder, Terminals (Strg+C würde das laufende Programm
abbrechen), Fenster mit Administratorrechten (Windows blockiert Eingaben von außen), Texte über 50.000 Zeichen.

## Fehlersuche

* **Log:** `instaprompt.log` im Config-Ordner (enthält nur Längen und Zeiten, nie Texte oder Schlüssel).
* **Hotkey reagiert nicht:** Wenn eine andere App ihn belegt, meldet InstaPrompt das beim Start/Neuladen. Anderen Hotkey wählen.
* **„API-Schlüssel fehlt":** `api_key` oder `api_key_env` im Provider-Block setzen; Umgebungsvariablen werden beim
  Start gelesen (nach dem Setzen der Variable die App neu starten oder „Config neu laden").

## Entwickler-Schalter (Umgebungsvariablen)

| Variable | Wirkung |
|---|---|
| `INSTAPROMPT_CONFIG_DIR` | anderer Config-Ordner (Tests, zweite Kopie nebenher) |
| `INSTAPROMPT_THEME` | `light`/`dark`/`system`, überschreibt `settings.toml` |
| `INSTAPROMPT_BACKDROP=off` | schaltet das Acrylic-Material ab (zeigt den Fallback wie auf Windows 10) |
| `INSTAPROMPT_DIAG=1` | loggt alle 5 s Speicher und GC-Zahlen |
| `INSTAPROMPT_TRIM_SECONDS=N` | überschreibt `idle_trim_seconds` |

## Messwerte (Release, ReadyToRun, Windows 11, `gemini-2.5-flash`)

| | |
|---|---|
| Hotkey → Overlay (erster WPF-Frame, warm; ohne DWM-Komposition) | Median 2–3 ms, p95 4 ms; erster nach dem Start ca. 9 ms (mit dem neuen Look unverändert) |
| Hotkey/Auswahl → Request verlässt die App | Median 5 ms (erster nach dem Start 32 ms) |
| Hotkey → Ergebnis eingefügt (Korrektur, kurzer Text) | Median 0,6 s, p95 0,9 s (davon fast alles Antwortzeit des Modells) |
| Start bis bereit | ca. 0,47 s (davon ca. 0,3 s das Erzeugen der WPF-Fenster) |
| Speicher | aktiv ca. 150–190 MB privat; nach 90 s Ruhe ca. 20 MB Working Set |
