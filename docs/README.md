# Kuroko

Eigene KI-Prompts in **jedem** Textfeld unter Windows: Text markieren (oder einfach tippen), Hotkey drücken,
Prompt wählen, das Ergebnis ersetzt den Text direkt im Feld. Kein Fensterwechsel, kein Kopieren und Einfügen.

Die App lebt im Tray, hat kein Hauptfenster und wird über zwei TOML-Dateien konfiguriert.

Dies ist die vollständige Anleitung. Eine englische Kurzfassung steht in der [Root-README](../README.md), der
Audit-Bericht vom Oktober 2026 in [`refactoring-report.md`](refactoring-report.md).

## Bedienung

| Aktion | Standard-Hotkey |
|---|---|
| Overlay öffnen (Prompt suchen/wählen) | `Ctrl+Shift+Space` |
| „Universal" direkt ausführen (schreiben **und** bearbeiten) | `Ctrl+Alt+M` |
| Anderen Prompt direkt ausführen (z. B. „Correction") | pro Prompt in `prompts.toml`, Standard `Ctrl+Alt+K` |
| Letzte Ersetzung rückgängig machen | `Ctrl+Alt+Z` (`undo_hotkey`) |
| Laufende Anfrage abbrechen | `Esc`, nur solange eine Anfrage läuft |
| Ergebnis-Karte schließen (und eine laufende Anfrage abbrechen) | `Esc`, nur solange eine Karte sichtbar ist |
| Ergebnis-Karte kopieren und schließen | `Ctrl+Alt+C` (`result_copy_hotkey`), nur solange eine Karte sichtbar ist |
| Ergebnis kopieren, das nicht eingefügt werden konnte | `Ctrl+Alt+C` (`result_copy_hotkey`), nur solange die Fehlerpille sichtbar ist; später Tray → „Letztes Ergebnis kopieren“ |

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

**Wenn das Einfügen scheitert:** Ist die Antwort schon da, kann aber nicht eingefügt werden (Fenster gewechselt, Zwischenablage
belegt, Eingabe blockiert, Einfügen nicht bestätigt), geht sie nicht verloren. Die Fehlerpille nennt den Grund und bietet an:
„Ctrl+Alt+C kopiert das Ergebnis“. Sie bleibt dafür 15 Sekunden stehen (Klick schließt sie), und nur so lange ist
`result_copy_hotkey` registriert. Danach liegt die Antwort weiter im Tray-Menü unter **Letztes Ergebnis kopieren**; der Eintrag
kopiert immer die letzte vollständige Antwort (auch die einer Ergebnis-Karte) und ist ausgegraut, solange es noch keine gibt.
Auch nach einem Abbruch mit `Esc` kurz vor dem Einfügen ist sie dort zu finden. Gespeichert wird sie nur im Arbeitsspeicher
(siehe [Datenschutz](#datenschutz)).

**Tray-Menü:** Letztes Ergebnis kopieren · Prompts bearbeiten · Einstellungen bearbeiten · Config-Ordner öffnen · Config neu laden ·
API-Schlüssel … · Verbindung testen · Mit Windows starten · Über Kuroko … · Beenden.

* **Prompts bearbeiten / Einstellungen bearbeiten** öffnen `prompts.toml` bzw. `settings.toml` im Standardprogramm für
  `.toml`-Dateien; ist keins eingerichtet, im Editor (Notepad). Nach dem Speichern gilt die Änderung sofort.
* **Verbindung testen** schickt eine Mini-Anfrage an den Standard-Anbieter und meldet, ob Schlüssel, Modell und Netz
  funktionieren (mit Antwortzeit), sonst den Grund, z. B. „API-Schlüssel fehlt oder wurde abgelehnt“.

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
API-Schlüssel; Schlüssel nie ins Repository einchecken.

### Editor-Unterstützung (JSON-Schema)

Für beide Dateien liegt ein JSON-Schema unter [`schemas/`](schemas/): `settings.schema.json` und `prompts.schema.json`.
Editoren mit [Taplo](https://taplo.tamasfe.dev/), etwa VSCodium/VS Code mit der Erweiterung **Even Better TOML**, zeigen
damit Vervollständigung, Beschreibungen beim Überfahren und Fehler wie unbekannte Schlüssel, falsche Typen, Werte außerhalb
der erlaubten Liste (`mode`, `output`, `theme`, Positionen …) oder Zahlen außerhalb des Bereichs. Die App prüft beim Laden
unabhängig davon selbst und meldet Fehler mit Datei und Zeile; das Schema ist nur eine Hilfe beim Schreiben.

* Die Vorlagen in `config/` verweisen in der ersten Zeile per `#:schema ../schemas/….schema.json` darauf. Für TOML ist das ein
  Kommentar, die App ignoriert ihn. Zusätzlich ordnet [`.taplo.toml`](.taplo.toml) die Dateien den Schemas zu, damit auch
  `taplo check` im Repository-Ordner funktioniert.
* Für die echten Dateien in `%APPDATA%\Kuroko\` entweder die passende Zeile als erste Zeile einfügen, mit absolutem Pfad zum
  Repository, z. B. `#:schema file:///C:/Pfad/zum/Repository/schemas/settings.schema.json`, oder in den
  VSCodium-Einstellungen (`settings.json`) zuordnen:

  ```json
  "evenBetterToml.schema.associations": {
    ".*/Kuroko/settings\\.toml$": "file:///C:/Pfad/zum/Repository/schemas/settings.schema.json",
    ".*/Kuroko/prompts\\.toml$": "file:///C:/Pfad/zum/Repository/schemas/prompts.schema.json"
  }
  ```

  `C:/Pfad/zum/Repository` durch den echten Ordner des Repositorys ersetzen (Schrägstriche `/`). Kuroko lässt eine
  `#:schema`-Zeile beim Zurückschreiben einer Einstellung (z. B. Autostart) stehen. Bleibt die Vervollständigung aus
  (VSCodium schlägt dann nur Wörter aus der Datei vor), steht der Grund unter Ausgabe → „Even Better TOML LSP“, z. B.
  `failed to load schema … no such file or directory` bei einem falschen Pfad.
* Ändert sich ein Schlüssel oder ein erlaubter Wert im Code, das Schema mit anpassen: `SchemaTests` prüft, dass Schemas,
  Vorlagen und Loader zusammenpassen.

### API-Schlüssel

Am einfachsten über das Tray-Symbol → **API-Schlüssel …**: Anbieter wählen, Schlüssel einfügen, Speichern. Der Schlüssel
landet in der Windows-Anmeldeinformationsverwaltung (generische Anmeldeinformation `Kuroko:<anbieter>`, z. B.
`Kuroko:gemini` für `[providers.gemini]`, nur für das eigene Benutzerkonto) und gilt sofort, ohne Neustart. Dort lässt
er sich auch wieder entfernen; der Dialog zeigt an, woher der Schlüssel eines Anbieters gerade kommt.

Kuroko sucht den Schlüssel in dieser Reihenfolge, der erste Treffer gilt:

1. `api_key` direkt im Provider-Block (nur lokal, nie einchecken),
2. die Umgebungsvariable aus `api_key_env`,
3. der gespeicherte Eintrag `Kuroko:<anbieter>` in der Windows-Anmeldeinformationsverwaltung.

Bestehende Konfigurationen mit `api_key_env` funktionieren unverändert. Für einen gespeicherten Schlüssel muss in
settings.toml nichts stehen; `api_key_env` darf trotzdem bleiben (ist die Variable gesetzt, hat sie Vorrang).
Alternativ in der Eingabeaufforderung: `cmdkey /generic:Kuroko:gemini /user:Kuroko /pass` (fragt den Schlüssel ab,
ohne dass er im Verlauf landet), danach „Config neu laden“.

Findet Kuroko beim Start für den Standard-Anbieter keinen Schlüssel, nennt ein Hinweis die erwartete Umgebungsvariable
(aus `api_key_env`, z. B. `GEMINI_API_KEY`) und den Weg über **API-Schlüssel …**; der Hinweis bleibt zusätzlich in den
Windows-Benachrichtigungen stehen. Ob der Schlüssel dann funktioniert, zeigt Tray-Symbol → **Verbindung testen**.

### settings.toml (Auszug)

```toml
overlay_hotkey = "Ctrl+Shift+Space"
marker_start = "<<"
marker_end   = ">>"
theme = "system"            # system | light | dark
accent = "none"             # none (neutral) | system (Windows-Akzent) | Hex wie "#3B82F6": Cursor, Textmarkierung, Fortschrittslinie
undo_hotkey = "Ctrl+Alt+Z"  # letzte Ersetzung rückgängig
undo_history = 10           # so viele Ersetzungen werden gemerkt (0 = aus)
result_copy_hotkey = "Ctrl+Alt+C"   # kopiert die Ergebnis-Karte bzw. das nicht eingefügte Ergebnis (nur solange Karte oder Fehlerpille sichtbar sind); nicht erlaubt: einfaches Ctrl+C/A/V
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
fallback_provider = ""      # Ausweich-Anbieter, leer = aus (siehe „Ausweich-Anbieter“ unten)

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

### Ausweich-Anbieter (`fallback_provider`)

Ist ein Anbieter gerade nicht erreichbar, kann Kuroko dieselbe Anfrage **einmal** an einen anderen Anbieter schicken,
z. B. an das lokale Ollama-Modell. Standardmäßig ist das aus.

```toml
fallback_provider = "local"     # global: gilt für alle Anbieter ohne eigenen Wert

[providers.anthropic]
type = "anthropic"
model = "claude-haiku-4-5"
fallback_provider = "openai"    # nur für diesen Anbieter; gilt vor dem globalen Wert ("" = hier keiner)
```

* **Wann:** nur bei „keine Verbindung“, Serverfehler (5xx) und Rate-Limit (429), und nur bevor Text angekommen ist.
  Ein abgelehnter Schlüssel oder eine abgelehnte Anfrage (4xx), eine blockierte oder abgeschnittene Antwort und eine
  Zeitüberschreitung (da hast du schon die volle Zeit gewartet) werden wie bisher gemeldet. Nach `Esc` oder einem Klick
  auf die Pille wird nichts mehr gesendet.
* **Nur ein Schritt:** Scheitert auch der Ausweich-Anbieter, kommt dessen Fehlermeldung. Sein eigener
  `fallback_provider` wird dabei nie befolgt, deshalb sind auch gegenseitige Einträge (A → B, B → A) erlaubt und
  führen nie zu einer Schleife.
* **Lokale Anbieter** (`base_url` auf `localhost`, im lokalen Netz oder ohne Punkt im Namen) nutzen den globalen Wert
  nicht: Ein Text, der den Rechner nicht verlassen soll, geht nicht in die Cloud, nur weil Ollama gerade aus ist. Wer
  das trotzdem will, trägt `fallback_provider` im Block des lokalen Anbieters ein.
* **Modell:** Der Ausweich-Anbieter nutzt sein eigenes `model`; ein `model` aus dem Prompt gehört zum Anbieter des Prompts.
* **Anzeige:** Die Pille zeigt während des Wartens „gemini nicht erreichbar, frage local …“. Nach dem Ersetzen meldet
  sie kurz, von welchem Anbieter die Antwort kam.
* **Prüfung:** Ein unbekannter Name oder ein Anbieter, der auf sich selbst zeigt, ist ein Config-Fehler mit Zeilenangabe.

Was das für den Datenschutz heißt, steht unter [Datenschutz](#datenschutz).

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
Was dabei an den Anbieter geht, steht unter [Datenschutz](#datenschutz).

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

## Datenschutz

Kuroko hat keine Telemetrie, keine Nutzungsstatistik, keine Update-Prüfung und kein Konto. Netzwerkverkehr gibt es nur
zum Anbieter, der in `settings.toml` steht.

**Was an den Anbieter geht, wenn du einen Prompt ausführst:** die Anweisung des Prompts und der erfasste Text (die
Auswahl oder der Feldinhalt, höchstens 50.000 Zeichen), dazu Modell, Ausgabegrenze und gegebenenfalls die Reasoning-Einstellung. Sonst nichts: kein Fenstertitel,
kein App-Name, kein sonstiger Inhalt der Zwischenablage. Ziel ist die `base_url` des Anbieters, den der Prompt verwendet (oder die des
Ausweich-Anbieters, siehe unten).
Was der Anbieter mit dem Text macht und wie lange er ihn aufbewahrt, regeln dessen eigene Bedingungen. Je Anbieter:

* `openai`: Die Anfrage geht mit `store = false`, die Antwort wird also nicht im Antwortverlauf des Kontos gespeichert.
* `gemini`: Die Anfrage aktiviert immer die Google-Suche (Grounding); Google kann aus dem Text Suchanfragen bilden.
* `anthropic`: keine Besonderheiten.
* `openai-compatible` mit einem lokalen Server (Ollama, LM Studio): Der Text verlässt den Rechner nicht.

**Netzaufruf ohne Prompt:** Beim Start, nach einer Config-Änderung und beim Öffnen des Overlays schickt Kuroko ein
`HEAD` an die `base_url` des Standard-Anbieters, damit die Verbindung beim eigentlichen Aufruf schon steht. Es enthält
weder Text noch Schlüssel, nur die Kennung `Kuroko/1.0`.

**Ausweich-Anbieter:** Ist `fallback_provider` gesetzt (standardmäßig aus) und der Anbieter des Prompts nicht
erreichbar, geht derselbe Inhalt (Anweisung und erfasster Text) einmal an den Ausweich-Anbieter, also an einen
**anderen Anbieter**, als der Prompt nennt, mit dessen Schlüssel und unter dessen Bedingungen. Die Pille zeigt das an.
Lokale Anbieter schicken nur dann weiter, wenn ihr eigener Block es ausdrücklich sagt (siehe
[Ausweich-Anbieter](#ausweich-anbieter-fallback_provider)).

**Verbindungstest:** Nur wenn du im Tray „Verbindung testen“ wählst, geht eine echte Anfrage an den Standard-Anbieter, mit
dem Schlüssel und dem festen Text „ping“ (höchstens 16 Ausgabe-Tokens). Text aus anderen Apps ist nicht dabei.

**API-Schlüssel:** Der Schlüssel geht nur im HTTP-Header an den gewählten Anbieter (`Authorization`, `x-api-key` bzw.
`x-goog-api-key`), nie in der URL. Über `http://` an einen entfernten Host wird er nicht gesendet; eine solche `base_url`
lehnt die Config ab (erlaubt sind `localhost`, Rechnernamen ohne Punkt, `.local` und private IP-Adressen). In
Fehlermeldungen wird er durch `***` ersetzt, ins Log kommt er nie. Gespeichert ist er dort, wo du ihn ablegst (siehe
[API-Schlüssel](#api-schlüssel)): in der Windows-Anmeldeinformationsverwaltung (nur dein Benutzerkonto, nur dieser
Rechner), in einer Umgebungsvariable oder im Klartext in `settings.toml`.

**Was lokal gespeichert wird:**

| Was | Wo | Inhalt |
|---|---|---|
| `settings.toml`, `prompts.toml` | `%APPDATA%\Kuroko\` (oder `KUROKO_CONFIG_DIR`) | deine Einstellungen und Prompts |
| `kuroko.log` | im selben Ordner, beginnt über 1 MB neu | Zeiten, Textlängen, Name des Zielprogramms (z. B. `notepad`), Prompt-Namen, Hotkeys, Config-Meldungen, Fehlerart und Fehlertext des Anbieters (bei abgelehnten Anfragen nicht, weil er Teile deines Texts zitieren kann); nie Texte, Antworten oder Schlüssel |
| API-Schlüssel | Windows-Anmeldeinformationsverwaltung `Kuroko:<anbieter>` | nur wenn du ihn dort speicherst |
| Autostart | Registry, `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` | Pfad zu `Kuroko.exe`, nur wenn „Mit Windows starten“ an ist |

**Nur im Arbeitsspeicher, beim Beenden weg:** die Rückgängig-Liste (Original und Ergebnis der letzten `undo_history`
Ersetzungen), die letzte vollständige Antwort (für „Letztes Ergebnis kopieren“, von der nächsten Antwort ersetzt) und die
gesicherte Zwischenablage während einer Ersetzung. Was Kuroko selbst kurz in die Zwischenablage legt
(Erkennungstext beim Kopieren, Ergebnis beim Einfügen), ist für den Windows-Zwischenablageverlauf und die Cloud-Synchronisierung
gesperrt. Das gilt auch für deinen vorherigen Inhalt, wenn Kuroko ihn danach zurückschreibt: Er steht schon im Verlauf, ein Lauf
erzeugt also keinen Doppel-Eintrag. Kopiert die Ziel-App beim Lesen per `Strg+C` die Markierung, landet diese allerdings im
Verlauf (das macht die Ziel-App, nicht Kuroko). Nur was du selbst kopieren lässt (`Ctrl+Alt+C` in der Ergebnis-Karte oder an der
Fehlerpille, „Letztes Ergebnis kopieren“ im Tray), legt Kuroko wie ein normales Kopieren ab, also auch im Verlauf.

**Bewusst ausgelassen:** Passwortfelder, Terminals und Fenster mit Administratorrechten liest Kuroko nicht.

## Bauen, testen, ausführen

Voraussetzung: .NET 10 SDK (Windows).

```powershell
dotnet build                       # Debug
dotnet test                        # alle Tests: Kuroko.Tests (Core) und Kuroko.Windows.Tests (Platform/App)
powershell -File tools\publish.ps1 # Release mit ReadyToRun nach publish\Kuroko
powershell -File tools\publish.ps1 -SelfContained   # läuft ohne installierte .NET-Runtime (~150 MB)
```

Empfohlen ist die kleine Variante (`publish\Kuroko`, etwa 2 MB). Sie braucht die **.NET Desktop Runtime 10** (x64) von
[dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0). Fehlt sie, meldet Windows das beim Start von
`Kuroko.exe` und bietet an, die Download-Seite zu öffnen. Wo sich nichts installieren lässt, die Variante mit `-SelfContained` nehmen.

Die Versionsnummer steht in `Directory.Build.props` (`<Version>`); Kuroko schreibt sie beim Start ins Log und zeigt sie im
Tray-Menü unter „Über Kuroko …“.

Das Icon wird mit `tools\make-icon.ps1` erzeugt.

## Aufbau

| Projekt | Inhalt |
|---|---|
| `Kuroko.Core` | UI-frei: Prompt-Engine, Marker-Parser, Provider-Adapter (HttpClient + SSE), Konfiguration, Hot-Reload |
| `Kuroko.Platform` | Win32: globale Hotkeys, Textzugriff (UI Automation und Zwischenablage), Tray, Fenster-Helfer, Autostart |
| `Kuroko.App` | WPF: Overlay, Status-Pille, Themes, Zusammenspiel (`AppController`) |
| `Kuroko.Tests` | xUnit, `net10.0`: Core (Marker, Prompt-Aufbau, Config, Provider mit simuliertem Server, Undo, …) |
| `Kuroko.Windows.Tests` | xUnit, `net10.0-windows`: Entscheidungslogik von `TextAccessService` und Ablauf von `AppController` mit Fakes |

**Tests für Platform/App:** Zwischenablage (`IClipboard`), Tastatureingaben (`IKeyboard`), UI-Automation-/Fokusabfragen
(`IFieldInspector`), Hotkeys (`IHotkeyRegistry`), die beiden WPF-Fenster (`IOverlayView`, `IStatusView`) und der Rest von
Windows (`IDesktop`) liegen hinter schmalen Interfaces. Die echten Implementierungen (`ClipboardService`, `InputSimulator`,
`UiaFieldInspector`, `HotkeyManager`, `OverlayWindow`, `StatusWindow`, `Win32Desktop`) werden in den Tests nie aufgerufen; dort
spielen Fakes die Ziel-App, die Zwischenablage und die KI. `AppController`-Tests laufen auf einem eigenen Thread mit
Nachrichtenschleife (`UiThread.Run`), wie auf dem UI-Thread. Was die Tests nicht abdecken (echtes SendInput, echte
Zwischenablage, Antworten echter Apps über UIA, Fensterdarstellung), bleibt Sache der manuellen Checkliste in
[`refactoring-report.md`](refactoring-report.md#manuelle-testcheckliste).

**Textzugriff, gestuft:** (1) UI Automation für die Markierung, ohne die Zwischenablage anzufassen; (2) Strg+C mit
Marker-Text als Erkennung; (3) ohne Markierung das ganze Feld (nicht bei Overlay-Ausgabe: dort nie Strg+A, siehe oben). Ersetzt wird per Strg+V; die Zwischenablage des Nutzers
wird vorher vollständig gesichert und danach zurückgeschrieben. Der Text im Feld wird erst angefasst, wenn die
KI-Antwort vollständig und fehlerfrei da ist. Bei Overlay-Ausgabe wird nichts ersetzt; die Antwort wird gestreamt angezeigt.

**Bewusst nicht unterstützt:** Passwortfelder, schreibgeschützte Felder (nur beim Ersetzen; für die Ergebnis-Karte sind sie erlaubt),
Terminals (Strg+C würde das laufende Programm abbrechen), Fenster mit Administratorrechten (Windows blockiert Eingaben von außen),
Texte über 50.000 Zeichen.

**Esc während einer Anfrage:** Solange Kuroko liest oder auf die Antwort wartet, ist `Esc` global registriert und bricht ab
(wie ein Klick auf die Statuspille); in dieser Zeit geht `Esc` nicht an andere Apps. Ist der Einfügen-Befehl schon gesendet,
wird nicht mehr abgebrochen. Danach wird `Esc` sofort wieder freigegeben.

**Grenzen der Ergebnis-Karte:** Sie liest nur markierten Text (siehe oben), ohne Auswahl kein Ganztext in Browsern. `Esc` und der
Kopier-Hotkey werden global abgefangen, solange die Karte sichtbar ist, und gehen in dieser Zeit nicht an andere Apps. Das Mausrad
scrollt die Karte ohne Aktivierung nur, wenn Windows „Inaktive Fenster beim Daraufzeigen scrollen“ eingeschaltet hat (Standard).
Beginnt eine Antwort mit einem Code-Block, erscheint sie erst vollständig (der Zaun wird einmal am Ende entfernt, ohne Sprung).
Während die Antwort noch läuft, wird der Kopier-Hotkey ignoriert (kein halbes Ergebnis in der Zwischenablage).

## Fehlersuche

* **Startet nicht, Meldung „.NET Desktop Runtime“:** Die kleine Variante braucht die .NET Desktop Runtime 10 (x64); installieren
  (siehe [Bauen, testen, ausführen](#bauen-testen-ausführen)) oder die Variante mit `-SelfContained` verwenden.
* **Log:** `kuroko.log` im Config-Ordner (Inhalt siehe [Datenschutz](#datenschutz); nie Texte oder Schlüssel).
* **Hotkey reagiert nicht:** Wenn eine andere App ihn belegt, meldet Kuroko das beim Start/Neuladen. Anderen Hotkey wählen.
* **„API-Schlüssel fehlt":** Schlüssel über Tray-Symbol → „API-Schlüssel …“ speichern, oder `api_key` bzw.
  `api_key_env` im Provider-Block setzen. Eine neu gesetzte Umgebungsvariable sieht nur eine neu gestartete App
  (Kuroko beenden und wieder starten; „Config neu laden“ reicht dafür nicht). Danach mit „Verbindung testen“ prüfen.

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
| Hotkey → Overlay im Alltag (aus dem Log, mit Blur, Stand 2026-10-10) | Median 18 ms, 90 % unter 27 ms (34 Aufrufe in Explorer, VSCodium, Browser und anderen Apps); erster nach dem Start 45–80 ms |
| Hotkey/Auswahl → Request verlässt die App | gemessen: Median 5 ms (erster nach dem Start 32 ms); im Alltag (aus dem Log, 105 Anfragen, Stand 2026-10-10) Median 18 ms, 90 % unter 32 ms |
| Hotkey → Ergebnis eingefügt (Korrektur, kurzer Text) | Median 0,6 s, p95 0,9 s (davon fast alles Antwortzeit des Modells) |
| Start bis bereit | Median ca. 0,38 s im Prozess, ca. 0,54 s ab Prozessstart (davon ca. 0,3 s Fenster erzeugen und Overlay vorwärmen) |
| Speicher | aktiv ca. 150–190 MB privat; nach 90 s Ruhe ca. 20 MB Working Set |
