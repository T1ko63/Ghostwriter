# Kuroko

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
| Ergebnis-Karte schließen (und eine laufende Anfrage abbrechen) | `Esc`, nur solange eine Karte sichtbar ist |
| Ergebnis-Karte kopieren und schließen | `Ctrl+Alt+C` (`result_copy_hotkey`), nur solange eine Karte sichtbar ist |

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

**Ergebnis-Karte (Overlay-Ausgabe):** Ein Prompt mit `output = "overlay"` ersetzt nichts, sondern zeigt das Ergebnis in einer kleinen Karte;
der Text im Feld oder auf der Seite bleibt unverändert. Gedacht für Zusammenfassungen, Einwände, Rückfragen auf Webseiten und anderen
schreibgeschützten Inhalten, aber auch für Analysen in einem Entwurf, ohne ihn zu ändern. `output` ist unabhängig von `mode`
(`mode` beschreibt die Eingabe, `output` das Ziel des Ergebnisses). Aufruf wie jeder Prompt: Hotkey des Prompts oder Overlay-Palette.

* Ablauf: Hotkey, dann die gewohnte Fortschrittspille („Summary …“ mit Linie). Mit dem ersten Text wird daraus die Karte, die mit der
  Antwort wächst (feste Breite (`result_width`), höchstens `result_max_height` hoch, darüber dünne Scrollbar; sie scrollt selbst mit, bis du von Hand scrollst). Die Linie
  verschwindet, wenn die Antwort vollständig ist. Zeilen mit `- ` erscheinen als Aufzählung; reiner Text, kein Markdown.
* Die Karte nimmt nie den Fokus, hat keinen Taskleisteneintrag und ändert die Auswahl in der Ziel-App nicht. Sie hat standardmäßig
  einen festen Platz auf dem Monitor, auf dem das Zielfenster liegt (`result_position = "fixed"`, Standard unten mittig im unteren Drittel).
  Den Platz wählt `result_fixed_position`: `top-third`, `center`, `bottom-third`, `top-left`, `bottom-left`, `top-right`,
  `bottom-right`, `left`, `right`, `top`, `bottom`. `result_screen_margin` ist der Abstand zum Bildschirmrand (Standard 24 px).
  Die Karte wächst immer vom Bildschirmrand weg: oben nach unten, unten nach oben, in der Mitte nach beiden Seiten, und verlässt so nie den
  Bildschirm. Mit `result_position = "caret"` (am Textcursor, sonst an der Maus), `"mouse"` oder `"follow"` (dort, wo sich das Prompt-Menü öffnet, siehe `overlay_position`) steht sie stattdessen am Text und wächst dorthin, wo Platz ist (nach unten, sonst nach oben).
  Die Schriftgröße stellt `result_font_size` ein (Standard 15). Die Breite ist fest (`result_width`, 480); die Höhe folgt dem Text, mindestens
  `result_min_height` (Standard 0) und höchstens `result_max_height` (360). Größen und Platz gelten für die nächste Karte.
* `Esc` schließt die Karte, auch während die Antwort noch läuft; dann wird die Anfrage abgebrochen. `Ctrl+Alt+C` kopiert den ganzen Text
  (mit den `- `-Zeilen) in die Zwischenablage, zeigt kurz „Kopiert“ und schließt die Karte (erst wenn die Antwort vollständig ist). Beide
  Hotkeys sind nur registriert, solange eine Karte sichtbar ist; danach erreichen `Esc` und `Ctrl+Alt+C` wieder die anderen Apps.
* Die Karte schließt auch, wenn ein anderes Fenster nach vorn kommt (ein laufender Lauf wird dann abgebrochen), bei einem Klick auf sie,
  und wenn du einen anderen Hotkey von Kuroko drückst (dann beginnt der neue Lauf). Es gibt kein Zeitlimit.
* Ein Fehler mitten in der Antwort (Netz, Anbieter, Abbruch) ersetzt die Karte durch die Fehlerpille; es bleibt kein halbes Ergebnis stehen.
* Die Karte schreibt nichts in die Rückgängig-Liste und berührt `Ctrl+Alt+Z` nicht.

**Welcher Text gelesen wird (Overlay-Ausgabe):** nur die **Auswahl**, aus UI Automation oder über `Strg+C` (Zwischenablage gesichert und
wiederhergestellt). Die App sendet dabei **nie `Strg+A`**: auf einer Webseite würde die ganze Seite markiert, in einem Feld bliebe alles
markiert. Ohne Auswahl wird nur dann der ganze Feldtext gelesen, wenn UI Automation das Feld nachweislich als editierbar meldet (ohne
Tastendruck, nicht in Browsern/Electron); sonst erscheint „Kein Text markiert.“. Schreibgeschützte Ziele sind erlaubt. Weiterhin
abgelehnt werden Passwortfelder, Terminals und Administrator-Fenster; die Eingabe ist auf 50.000 Zeichen begrenzt. In der Palette
(`Ctrl+Shift+Space`) erscheinen auch auf schreibgeschützten Zielen alle Prompts; ein Prompt mit `output = "replace"` bekommt dort wie bisher
die Meldung „schreibgeschützt“.

**Rückgängig (`Ctrl+Alt+Z`):** Die App merkt sich bei jeder erfolgreichen Ersetzung den Originaltext (samt Marker-Block) und das Ergebnis,
die letzten `undo_history` Stück (Standard 10, `0` = aus), nur im Arbeitsspeicher: nie auf der Festplatte, nie im Log, beim Beenden weg.
Der Hotkey liest das Feld, sucht das gespeicherte Ergebnis darin und schreibt an dieser Stelle das Original zurück (wiederholtes Drücken
geht Schritt für Schritt weiter zurück). Nichts wird überschrieben, wenn ein anderes Fenster/Feld den Fokus hat, das Ergebnis im Text
nicht mehr (unverändert) vorkommt oder mehrfach vorkommt; dann erscheint nur eine kurze Meldung. Zurückgeschrieben wird bewusst vom Programm selbst,
nicht per Strg+Z der Ziel-App, weil dessen Verhalten je nach App unterschiedlich ist. Preis: Der Feldinhalt wird komplett neu eingefügt, daher
können Formatierung (Rich-Text) und Cursorposition verloren gehen; die App-eigene Undo-Liste (Strg+Z) bleibt meist erhalten.

**Position und Größe des Menüs:** Das Auswahl-Menü (und die Fortschrittspille) hat dieselben Optionen wie die Ergebnis-Karte:
`overlay_position = "fixed"` mit `overlay_fixed_position` (dieselben elf Plätze), `overlay_screen_margin`, `overlay_width`, `overlay_min_height`,
`overlay_max_height` und `overlay_font_size` (das ganze Menü skaliert mit der Schrift). Mit `caret` oder `mouse` öffnet es wie bisher am
Textcursor bzw. an der Maus. Es wächst vom Bildschirmrand weg, wenn die Liste beim Tippen länger oder kürzer wird.

**Aussehen:** Das Overlay ist bewusst reduziert: oben die Suche, darunter nur Name und (falls vorhanden) Hotkey jedes Prompts,
links die Ziffern zum Direktwählen. Hell/Dunkel folgt Windows (`theme`). Transparenz, Eckenradius, Blur und die drei Farben jedes
Themes stehen im Block `[appearance]` der `settings.toml` (siehe unten); alle Nebenfarben (gedämpfter Text, Trennlinie, Scrollbar,
Hover) werden aus der Textfarbe abgeleitet. `accent` färbt nur noch Textcursor, Textmarkierung und Fortschrittslinie, die Auswahlzeile
bestimmt `selection`. Maße, Schriftgrößen und Abstände stehen weiter an einer Stelle: `src\Kuroko.App\Themes\Design.xaml`.

**Tray-Menü:** Config-Ordner öffnen · Config neu laden · Mit Windows starten · Beenden.

## Konfiguration

Beim ersten Start entstehen in `%APPDATA%\Kuroko\` (oder dem Ordner aus `KUROKO_CONFIG_DIR`) zwei Dateien
mit Kommentaren: `settings.toml` und `prompts.toml`. Änderungen werden nach dem Speichern **automatisch**
übernommen, auch die Hotkeys. Bei einem Fehler in einer Datei bleibt die letzte funktionierende Version aktiv, und
eine Meldung nennt Datei und Zeile.

Kuroko hieß früher Ghostwriter und davor InstaPrompt. Beim ersten Start wandern `%APPDATA%\Ghostwriter\` und, falls noch
vorhanden, `%APPDATA%\InstaPrompt\` nach `%APPDATA%\Kuroko\`. Dabei wird nichts überschrieben: Was im neuen Ordner schon
liegt, bleibt, die alte Kopie bleibt dann im alten Ordner; ein leerer alter Ordner wird entfernt. Läuft die alte Version
noch, bittet Kuroko darum, sie zu beenden, und startet nicht. Alte Autostart-Einträge („Ghostwriter (Claude)“,
„InstaPrompt (Claude)“) entfernt Kuroko bei jedem Start. Umgebungsvariablen heißen jetzt `KUROKO_*` statt `GHOSTWRITER_*`.

Vollständige Vorlagen mit allen Schlüsseln, Standardwerten und kurzen Kommentaren liegen im Repository unter
[`config/settings.example.toml`](config/settings.example.toml) und [`config/prompts.example.toml`](config/prompts.example.toml).
Zum Verwenden nach `%APPDATA%\Kuroko\` kopieren und `.example` aus dem Namen entfernen. Die Vorlagen enthalten keine
API-Schlüssel; Schlüssel am besten per `api_key_env` aus einer Umgebungsvariable lesen und nie ins Repository einchecken.

### settings.toml (Auszug)

```toml
overlay_hotkey = "Ctrl+Shift+Space"
marker_start = "<<"
marker_end   = ">>"
theme = "system"            # system | light | dark
accent = "none"             # none (neutral) | system (Windows-Akzent) | Hex wie "#3B82F6": Cursor, Textmarkierung, Fortschrittslinie
undo_hotkey = "Ctrl+Alt+Z"  # letzte Ersetzung rückgängig
undo_history = 10           # so viele Ersetzungen werden gemerkt (0 = aus)
result_copy_hotkey = "Ctrl+Alt+C"   # kopiert die Ergebnis-Karte (nur solange sie sichtbar ist); nicht erlaubt: einfaches Ctrl+C/A/V
overlay_position = "caret"  # Auswahl-Menü: caret | mouse | fixed (fester Platz: overlay_fixed_position, Standard top-third)
overlay_screen_margin = 8   # weitere Schlüssel: overlay_fixed_position, overlay_font_size, overlay_width, overlay_min_height, overlay_max_height
result_position = "fixed"   # Ergebnis-Karte: fixed (fester Platz, siehe result_fixed_position) | follow (wie das Prompt-Menü) | caret | mouse
result_fixed_position = "bottom-third"  # top-third | center | bottom-third | top-left | bottom-left | top-right | bottom-right | left | right | top | bottom
result_screen_margin = 24   # Abstand der Karte zum Bildschirmrand in px, 0-200
result_font_size = 15       # Schriftgröße der Ergebnis-Karte, 8-32 (auch 14.5 ist erlaubt)
result_width = 480           # Breite der Ergebnis-Karte in px, 240-1600 (nie größer als der Monitor)
result_min_height = 0         # Mindesthöhe der Karte in px, 0-2000 (0 = so klein wie der Text)
result_max_height = 360     # maximale Höhe in px, 120-2000; darüber scrollt der Text
autostart = false
idle_trim_seconds = 90      # gibt nach Ruhe ungenutzten Speicher frei (0 = nie)
default_provider = "gemini"

[appearance]                # Aussehen: siehe „Aussehen einstellen“ unten
transparency = 50
radius = 10
blur = "acrylic"

[providers.gemini]
type = "gemini"             # openai | gemini | anthropic | openai-compatible
model = "gemini-2.5-flash"
api_key_env = "GEMINI_API_KEY"   # oder api_key = "..."
reasoning = "off"           # off | default | low | medium | high
```

### Aussehen einstellen (`[appearance]`)

```toml
[appearance]
transparency = 50           # 0-100 %: 0 = deckend, 100 = vollständig durchsichtig (über 90 leidet die Lesbarkeit)
radius       = 10           # Eckenradius in px, 0-100
blur         = "acrylic"    # acrylic | blur | mica | micaalt | none
border       = true         # feiner Rand um das Fenster: true | false
custom_corners = false   # true: der Radius gilt auch bei acrylic/mica exakt (braucht den Windhawk-Mod, Teil "Ecken"); sonst rastet Windows auf 0, 4 oder 8 px

[appearance.dark]           # gilt, wenn das aktive Theme dunkel ist
background = "#000000"      # #RRGGBB oder rgb(r, g, b); ein Alpha-Anteil wird ignoriert und gemeldet
foreground = "#F0F0F0"      # Haupttext, daraus werden alle Nebenfarben abgeleitet
selection  = "#40FFFFFF"    # ausgewählte Zeile: #AARRGGBB (Alpha ZUERST, wie WPF, nicht wie CSS), #RRGGBB oder rgba(r, g, b, a); Hover = halbe Deckkraft

[appearance.light]
background = "#FFFFFF"
foreground = "#1A1A1A"
selection  = "#40000000"
```

* **transparency** wirkt nur auf die Fensterfläche (Deckkraft = 100 − transparency, auf `background` angewendet; bei `acrylic` ist das
  die Tönung über dem Blur). Text, Trennlinie, Scrollbar und Auswahl bleiben voll sichtbar.
* **blur** (Wert `acrylic | blur | mica | micaalt | none`):
  `acrylic` = Windows-11-Acrylic (Blur mit Rauschen und Systemtönung, DWM-Material), `blur` = schlichter „Blur behind“ ohne eigene Tönung
  (über die undokumentierte `SetWindowCompositionAttribute`-Schnittstelle; die durchsichtigste Variante), `mica` = Windows-11-Mica,
  `micaalt` = Mica Alt (kräftigere, dunklere Tönung), `none` = kein Blur, die Fläche liegt mit der eingestellten Transparenz direkt über
  dem Hintergrund. Auf Systemen ohne Windows-11-Unterstützung (oder mit `KUROKO_BACKDROP=off`) gibt es immer eine halbtransparente
  einfarbige Fläche ohne Blur. Acrylic, Blur und Mica(Alt) werden von Windows erst gezeichnet, wenn das Fenster aktiv ist; die App setzt
  sie deshalb nach dem Aktivieren neu (für etwa 10 ms steht beim Öffnen kurz eine flache Fläche).
* **Mica und Mica Alt sind nicht durchsichtig:** Sie nehmen nur die Farbe des Desktop-Hintergrunds auf. `transparency` mischt dort nur die
  Tönung zwischen `background` (0) und reinem Mica (100); es scheint nichts durch. Die Status-Pille ist nie aktiv und zeigt bei Mica
  eine einfarbige Ersatzfläche.
* **custom_corners:** Mit `true` fordert die App den exakten Radius auch bei den Windows-Materialien an: Sie zeichnet den Rand mit dem echten Radius und gibt ihn an den Windhawk-Mod `kuroko-blur` weiter, dessen Teil "Ecken" (Schalter *Enable rounded corners*) in `dwm.exe` den Radius statt 0, 4 oder 8 px verwendet. Ohne laufenden Mod rundet Windows weiter selbst, der Rand der App passt dann nicht ganz dazu. Blur (für `blur = "none"`) und Ecken sind im Mod getrennt schaltbar.
* **radius:** Windows schneidet das Fenster bei Acrylic, Blur und Mica selbst zu und kennt nur 0, 4 und 8 px; der Wert rastet dort auf die
  nächste dieser Stufen ein. Bei `none` und im Fallback gilt der Radius exakt. Die Ecken der Zeilen-Hervorhebung sind aus `radius`
  abgeleitet (etwa 60 %).
* Fehlt ein Wert oder der Block, gelten die gezeigten Standardwerte. Ein ungültiger Wert (Farbe, Blur, Zahl außerhalb des Bereichs)
  erzeugt eine kurze Meldung mit Datei und Zeile, fällt auf den Standard zurück, und alles andere bleibt aktiv. Änderungen werden beim
  Speichern übernommen, nicht erst beim nächsten Einblenden.

Anbieter: `openai` (Responses API), `gemini`, `anthropic` (Messages API) und `openai-compatible` für lokale Server
wie Ollama oder LM Studio (`base_url = "http://localhost:11434/v1"`). Modellnamen stehen nur in dieser Datei.
Ein API-Schlüssel wird nie über unverschlüsseltes `http://` an fremde Hosts gesendet, nie geloggt und nur an den
gewählten Anbieter geschickt. Keine Telemetrie.

### prompts.toml

```toml
[[prompt]]
name = "Correction"
mode = "transform"          # transform (Standard) | instruction | universal: beschreibt die Eingabe
output = "replace"          # replace (Standard) | overlay: wohin das Ergebnis geht (overlay = Ergebnis-Karte, nichts wird ersetzt)
hotkey = "Ctrl+Alt+K"       # optional: führt den Prompt direkt aus, ohne Overlay
provider = "gemini"         # optional: sonst der Standard-Anbieter
model = "gemini-2.5-pro"    # optional: sonst das Modell des Anbieters
prompt = """
Du bist mein Experte für Rechtschreibung und Grammatik. ...
Gib ausschließlich den korrigierten Text aus.
"""
```

Alle Hotkeys (`overlay_hotkey`, `undo_hotkey`, `result_copy_hotkey` und die der Prompts) teilen sich einen Namensraum: Jeder darf nur einmal
vorkommen, sonst meldet die App den Fehler mit Datei und Zeile. Ein Beispiel für einen Overlay-Prompt steht in `config/prompts.example.toml`.

## Bauen, testen, ausführen

Voraussetzung: .NET 10 SDK (Windows).

```powershell
dotnet build                       # Debug
dotnet test                        # Unit-Tests (Marker, Config, Provider mit simuliertem Server, ...)
powershell -File tools\publish.ps1 # Release mit ReadyToRun nach publish\Kuroko
powershell -File tools\publish.ps1 -SelfContained   # läuft ohne installierte .NET-Runtime (~130 MB)
```

Das Icon wird mit `tools\make-icon.ps1` erzeugt.

## Aufbau

| Projekt | Inhalt |
|---|---|
| `Kuroko.Core` | UI-frei: Prompt-Engine, Marker-Parser, Provider-Adapter (HttpClient + SSE), Konfiguration, Hot-Reload |
| `Kuroko.Platform` | Win32: globale Hotkeys, Textzugriff (UI Automation und Zwischenablage), Tray, Fenster-Helfer, Autostart |
| `Kuroko.App` | WPF: Overlay, Status-Pille, Themes, Zusammenspiel (`AppController`) |
| `Kuroko.Tests` | xUnit |

**Textzugriff, gestuft:** (1) UI Automation für die Markierung, ohne die Zwischenablage anzufassen; (2) Strg+C mit
Marker-Text als Erkennung; (3) ohne Markierung das ganze Feld (nicht bei Overlay-Ausgabe: dort nie Strg+A, siehe oben). Ersetzt wird per Strg+V; die Zwischenablage des Nutzers
wird vorher vollständig gesichert und danach zurückgeschrieben. Der Text im Feld wird erst angefasst, wenn die
KI-Antwort vollständig und fehlerfrei da ist. Bei Overlay-Ausgabe wird nichts ersetzt; die Antwort wird gestreamt angezeigt.

**Bewusst nicht unterstützt:** Passwortfelder, schreibgeschützte Felder (nur beim Ersetzen; für die Ergebnis-Karte sind sie erlaubt),
Terminals (Strg+C würde das laufende Programm abbrechen), Fenster mit Administratorrechten (Windows blockiert Eingaben von außen),
Texte über 50.000 Zeichen.

**Grenzen der Ergebnis-Karte:** Sie liest nur markierten Text (siehe oben), ohne Auswahl kein Ganztext in Browsern. `Esc` und der
Kopier-Hotkey werden global abgefangen, solange die Karte sichtbar ist, und gehen in dieser Zeit nicht an andere Apps. Das Mausrad
scrollt die Karte ohne Aktivierung nur, wenn Windows „Inaktive Fenster beim Daraufzeigen scrollen“ eingeschaltet hat (Standard).
Beginnt eine Antwort mit einem Code-Block, erscheint sie erst vollständig (der Zaun wird einmal am Ende entfernt, ohne Sprung).
Während die Antwort noch läuft, wird der Kopier-Hotkey ignoriert (kein halbes Ergebnis in der Zwischenablage).

## Fehlersuche

* **Log:** `kuroko.log` im Config-Ordner (enthält nur Längen und Zeiten, nie Texte oder Schlüssel).
* **Hotkey reagiert nicht:** Wenn eine andere App ihn belegt, meldet Kuroko das beim Start/Neuladen. Anderen Hotkey wählen.
* **„API-Schlüssel fehlt":** `api_key` oder `api_key_env` im Provider-Block setzen; Umgebungsvariablen werden beim
  Start gelesen (nach dem Setzen der Variable die App neu starten oder „Config neu laden").

## Entwickler-Schalter (Umgebungsvariablen)

| Variable | Wirkung |
|---|---|
| `KUROKO_CONFIG_DIR` | anderer Config-Ordner (Tests); die Einzelinstanz-Sperre gilt dann pro Ordner, so läuft eine zweite Kopie nebenher |
| `KUROKO_THEME` | `light`/`dark`/`system`, überschreibt `settings.toml` |
| `KUROKO_BACKDROP=off` | schaltet das Windows-11-Material ab (zeigt den Fallback wie auf Windows 10: halbtransparent, ohne Blur) |
| `KUROKO_DIAG=1` | loggt alle 5 s Speicher und GC-Zahlen |
| `KUROKO_TRIM_SECONDS=N` | überschreibt `idle_trim_seconds` |

## Messwerte (Release, ReadyToRun, Windows 11, `gemini-2.5-flash`)

| | |
|---|---|
| Hotkey → Overlay (erster WPF-Frame, warm; ohne DWM-Komposition) | Median 2–3 ms, p95 4 ms; erster nach dem Start ca. 9 ms (mit dem neuen Look unverändert) |
| Hotkey/Auswahl → Request verlässt die App | Median 5 ms (erster nach dem Start 32 ms) |
| Hotkey → Ergebnis eingefügt (Korrektur, kurzer Text) | Median 0,6 s, p95 0,9 s (davon fast alles Antwortzeit des Modells) |
| Start bis bereit | ca. 0,47 s (davon ca. 0,3 s das Erzeugen der WPF-Fenster) |
| Speicher | aktiv ca. 150–190 MB privat; nach 90 s Ruhe ca. 20 MB Working Set |
