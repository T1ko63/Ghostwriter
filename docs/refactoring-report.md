# Refactoring- und Qualitäts-Audit – Kuroko (damals Ghostwriter)

> Historischer Bericht vom 2026-10-08. Kuroko hieß zu diesem Zeitpunkt noch Ghostwriter; Projektnamen, Pfade und
> Log-Dateien (`Ghostwriter.Core`, `%APPDATA%\Ghostwriter\ghostwriter.log`, …) sind im Text unverändert geblieben.
> Heute heißen sie `Kuroko.*`, `%APPDATA%\Kuroko\` und `kuroko.log`. Die Anleitung steht in [`README.md`](README.md).

Stand: 2026-10-08, Branch `refactor/stabilize` (abgezweigt von `main` @ `5a16c42`).
Phase 0 (Baseline) und Phase 1 (Analyse) sind abgeschlossen. **Es wurde noch kein Code geändert.**

---

## Phase 0: Baseline

### Git
- `main` war sauber und mit `origin/main` synchron; die Umbenennung InstaPrompt → Ghostwriter und die
  `config/*.example.toml` sind bereits im Commit `5a16c42 Initial release of Ghostwriter` enthalten.
  Ein zusätzlicher Vorab-Commit war daher nicht nötig.
- Neuer Branch: `refactor/stabilize`.

### Build und Tests (`dotnet 10.0.401`, Release, `--no-incremental`)
| Messgröße | Wert |
|---|---|
| Build-Warnungen | **0** (unterdrückt projektweit: `SYSLIB1054`) |
| Build-Fehler | 0 |
| Tests | **350/350 grün** (xUnit, ~2 s) |
| Analyzer / `.editorconfig` / `TreatWarningsAsErrors` | keine |

### Größe
| Projekt | Dateien (.cs/.xaml) | Zeilen gesamt | davon nicht leer |
|---|---:|---:|---:|
| Ghostwriter.Core | 35 | 3 408 | 2 897 |
| Ghostwriter.Platform | 14 | 2 184 | 1 844 |
| Ghostwriter.App | 13 | 1 870 | 1 646 |
| **Produktivcode** | **62** | **7 462** | **6 387** |
| Ghostwriter.Tests | 12 | 2 865 | 2 433 |
| windhawk-mod (C++) | 1 | 1 745 | – |

- Projekte: 4 (3 Produktiv, 1 Test). NuGet im Produktivcode: **1** (`Tomlyn 2.10.1`).
  Test-Pakete: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk, coverlet.collector.
- Publish (framework-dependent, win-x64, ReadyToRun, wie `tools/publish.ps1`): **2,1 MB**
  (Tomlyn.dll 0,95 MB, Ghostwriter.Core.dll 0,41 MB, Ghostwriter.dll 0,31 MB, Ghostwriter.exe 0,28 MB,
  Ghostwriter.Platform.dll 0,20 MB).

### Start- und Reaktionszeiten (aus dem App-Log `%APPDATA%\Ghostwriter\ghostwriter.log`)
Die App misst diese Werte selbst; ich habe die vorhandenen Messungen aus deinem echten Betrieb übernommen,
statt eine zweite Instanz mit globalen Hotkeys neben deiner laufenden zu starten.

| Messung | Baseline |
|---|---|
| Start bis „Ready“ (im Prozess) | 338–429 ms (davon Fenster erzeugen 134–149 ms, Overlay-Warm-up 149–177 ms, Config 18–65 ms) |
| Hotkey → erstes Overlay-Frame | 7–10 ms (Explorer), 16 ms (VSCodium) |
| Hotkey → Request gestartet | 5–27 ms |
| Ersetzen (Snapshot → Paste → Restore) | 2–18 ms |

Für Phase 3 messe ich mit derselben Methode erneut (gleiche Log-Zeilen, gleiche Apps).

---

## Phase 1: Befunde

Gesamteindruck vorweg: Die Codebasis ist bereits auf gutem Niveau. Schichten sind sauber getrennt, Fehler werden
fast überall typisiert behandelt, der Nutzertext wird erst ganz am Ende angefasst, `HttpClient` wird korrekt
wiederverwendet, API-Schlüssel werden aus Fehlermeldungen entfernt. Die wichtigsten Risiken liegen in wenigen
Randpfaden der Textintegration und in der Fehlerbehandlung an der Win32-Grenze.

Legende Risiko: **hoch** = Datenverlust oder App-Absturz realistisch, **mittel** = App bleibt hängen oder
falsches Verhalten in plausiblen Fällen, **niedrig** = selten oder nur kosmetisch.
Aufwand: S (< 1 h), M (halber Tag), L (mehr).

### A. Architektur

| ID | Fundstelle | Befund | Risiko | Vorschlag | Aufwand |
|---|---|---|---|---|---|
| A1 | gesamt | Abhängigkeiten zeigen in die richtige Richtung: `Core` (rein, keine Win32-Aufrufe) ← `Platform` (Win32) ← `App` (WPF). Keine zirkulären Abhängigkeiten. | – | Beibehalten. | – |
| A2 | `src/Ghostwriter.App/App.xaml.cs` (407 Z.) | Kompositionswurzel, Config-Anwendung, Tray-Menü, Autostart und Idle-Trimming in einer Klasse. Noch überschaubar, aber die am stärksten gemischte Datei. | niedrig | Optional: Idle-Trimming in eine eigene kleine Klasse auslagern (siehe D2). Kein Großumbau. | S |
| A3 | `src/Ghostwriter.App/AppController.cs:210-389` | `ExecuteAsync` und `UndoAsync` teilen das gleiche Gerüst (Probe → Capture → Replace → Fehlerbehandlung → `_busy` zurücksetzen). | niedrig | Nur zusammenführen, falls bei S1/S4 ohnehin angefasst. | M |
| A4 | `Core/Prompts/IPromptRunner.cs`, `Platform/TextIntegration/TextAccess.cs:80` | `ITextAccess` hat genau eine Implementierung und wird von keinem Test genutzt; `IPromptRunner` hat neben der echten nur die ungenutzte `DummyPromptRunner`. | niedrig | Interfaces behalten (billig, ermöglichen künftige Tests von `AppController`), nur `DummyPromptRunner` entfernen (E1). | S |
| A5 | statischer Zustand | `AppLog` (statisch, gewollt), `LlmProvider.WorkingVariant` (statischer Cache, siehe S13), `WindowSkin._backdrop` (statisch, von beiden Fenstern geteilt, gewollt), `Loc.Language` (statisch). Kein problematischer globaler Zustand darüber hinaus. | niedrig | Keine Änderung außer S13. | – |

### B. Stabilität und Fehlerresistenz

| ID | Fundstelle | Befund | Risiko | Vorschlag | Aufwand |
|---|---|---|---|---|---|
| **S1** | `Platform/TextIntegration/TextAccessService.cs:214`, `:230-235`, `:205` | **Fokuswechsel innerhalb desselben Fensters während der KI-Anfrage.** Vor dem Einfügen wird nur geprüft, ob das *Top-Level-Fenster* noch vorn ist. Klickt der Nutzer in der Wartezeit in ein anderes Feld desselben Fensters (z. B. Betreff statt Text in Outlook, anderes Feld in einem Formular), dann wird bei einer Ganzfeld-Erfassung über die Zwischenablage **Strg+A im neuen Feld** gesendet und dessen Inhalt komplett überschrieben; bei einer Auswahl-Erfassung landet das Ergebnis im falschen Feld. Das ist der einzige Pfad, in dem Text ohne Zutun verloren gehen kann (Strg+Z rettet es meist). | **hoch** | Vor dem Einfügen das fokussierte Steuerelement prüfen: `GetGUIThreadInfo(...).hwndFocus` gegen `TargetInfo.FocusWindow`; wenn ein UIA-Element erfasst wurde, zusätzlich dessen `RuntimeId` gegen das aktuell fokussierte Element. Bei Abweichung abbrechen mit der vorhandenen Meldung `err_target_changed`. In Browsern/Electron ist das HWND gleich, dort greift nur der UIA-Vergleich (Kosten wenige ms, nur nach der KI-Antwort). **Sichtbare Folge: In diesem Fall gibt es eine Fehlermeldung statt einer falschen Ersetzung. Braucht deine Freigabe.** | M |
| **S2** | `TextAccessService.cs:237-252` | **Einfügen nicht bestätigt oder Abbruch nach Strg+V.** Ist Strg+V schon gesendet und die Ziel-App antwortet nicht innerhalb von 2 s (oder der Nutzer klickt in diesem Moment auf die Statuspille), wird die Zwischenablage sofort zurückgesetzt. Verarbeitet die App das Strg+V danach doch noch, fügt sie den **alten Zwischenablage-Inhalt** über das (bei Ganzfeld-Erfassung komplett markierte) Feld ein. Die Meldung sagt dann „Der Text blieb unverändert“, was in diesem Fall nicht stimmt. | mittel | (a) Nach gesendetem Strg+V den Abbruch nicht mehr in die Wartezeit durchreichen (`CancellationToken.None`), damit ein später Klick die Paste nicht abbricht. (b) Meldung `err_paste_ack` ehrlicher formulieren, z. B. „…hat das Einfügen nicht bestätigt. Bitte den Text prüfen (Strg+Z).“ **(b) ändert einen sichtbaren Text: Freigabe nötig.** | S |
| **S3** | `Platform/Native/MessageWindow.cs:52-60`, `Platform/Tray/TrayIcon.cs:137`, `App/App.xaml.cs:386` | **Ausnahmen in der Fensterprozedur beenden den Prozess.** Hotkey-Callbacks und Tray-Menüaktionen laufen direkt im nativen `WndProc` ohne `try/catch`. Eine Ausnahme dort wird weder von `DispatcherUnhandledException` noch sonst abgefangen. Konkretes Beispiel: „Config-Ordner öffnen“, wenn der Ordner gelöscht wurde → `Win32Exception` aus `Process.Start` → App beendet sich. Ebenso jede unerwartete Ausnahme in `ReloadConfig` über „Config neu laden“ oder in `TargetInfo.Capture()`. | **hoch** | In `MessageWindow.Proc` jeden Handler in `try/catch` kapseln, Fehler loggen und die Nachricht als behandelt melden. Verhalten im Normalfall unverändert. | S |
| **S4** | `App/AppController.cs:100-104`, `:110-137`, `:167-182` | **„Busy“ kann dauerhaft hängen bleiben.** `_busy = true` wird vor `ShowOverlayAsync` gesetzt; die Methode und `ActivateAfterProbeAsync` laufen als Fire-and-forget ohne `try/finally`. Wirft dort etwas (z. B. beim Layout in `Present`/`MeasureDesired`), wird die Ausnahme nur als „unobserved“ geloggt, `_busy` bleibt `true` und **alle Hotkeys werden bis zum Neustart ignoriert**. | mittel | `try/catch` in beiden Methoden: Overlay schließen, `_session = null`, `_busy = false`, Fehler loggen und wie andere Fehler anzeigen. | S |
| **S5** | `Core/Config/SettingsWriter.cs:47` (aufgerufen aus `App.xaml.cs:278`) | Das Umschalten von Autostart im Tray schreibt `settings.toml` direkt mit `File.WriteAllText`. Ein Absturz oder Stromausfall mitten im Schreiben hinterlässt eine leere oder halbe Datei. | mittel | In eine temporäre Datei im selben Ordner schreiben, dann `File.Replace`/`File.Move(overwrite)`. | S |
| S6 | `TextAccessService.cs:208-241` | Bei einer *Auswahl*-Erfassung wird vor dem Einfügen nicht geprüft, ob die Auswahl noch steht. Hat der Nutzer im selben Feld inzwischen geklickt, wird das Ergebnis an der Cursorposition eingefügt (Original bleibt, Text doppelt); hat er etwas anderes markiert, wird **das** ersetzt. | niedrig | Zusammen mit S1 lösen: Wo ein UIA-Element bekannt ist, die aktuelle Auswahl gegen den erfassten Text prüfen (das Element wird dafür auch bei Auswahl-Erfassung aufbewahrt). Ohne UIA bleibt es beim heutigen Verhalten. **Teil der Freigabe zu S1.** | M |
| S7 | `TextAccessService.cs:165-166` | Kann der Sentinel nicht in die Zwischenablage gelegt werden (Zwischenablage gesperrt), meldet `CopyOnceAsync` `InputFailed` → Nutzer sieht „Die Tastatureingabe wurde blockiert“ statt „Die Zwischenablage ist gerade belegt“. | niedrig | Eigenes Ergebnis für „Zwischenablage belegt“ zurückgeben. **Andere (korrektere) Meldung: Freigabe nötig.** | S |
| S8 | `ClipboardService.cs:168-204` | Der Snapshot hat keine Obergrenze für die Gesamtgröße und zwingt andere Apps, verzögert angebotene Formate zu erzeugen (Excel, Bildbearbeitung), während die Zwischenablage gesperrt ist. Im Log: Snapshots von 2,8 MB, unkritisch. Bei sehr großen Kopien (Hunderte MB) wird es langsam und speicherhungrig. | niedrig | **Nicht ändern:** Eine Grenze würde beim Zurücksetzen Inhalte verlieren. Nur dokumentiert (siehe „Empfehlungen für später“). | – |
| S9 | `App/App.xaml.cs:50`, `:81-84`; `Core/Config/ConfigWatcher.cs:18`; `Core/Diagnostics/AppLog.cs:21` | Verschluckte Fehler: `MigrateLegacyConfigDir` hat ein leeres `catch`, der `Debouncer` fängt `Exception` ohne Log. `AppLog.Init` läuft vor den Crash-Guards und kann bei nicht beschreibbarem Ordner werfen (`UnauthorizedAccessException` wird beim Löschen nicht gefangen). | niedrig | Fehler loggen (Migration nach `AppLog.Init` protokollieren), `Debouncer` loggt die Ausnahme, `AppLog.Init` fängt auch `UnauthorizedAccessException`. | S |
| S10 | `Platform/TextIntegration/UiaProbe.cs:59-64`, `:145-161` | Abgelaufene UIA-Aufrufe laufen im Hintergrund weiter; werfen sie später einen nicht erwarteten Ausnahmetyp, landet er als „unobserved task exception“ im Log. Harmlos, aber Rauschen. | niedrig | Abgebrochene Tasks mit einer Fortsetzung beobachten, die nur loggt. | S |
| S11 | `Core/Config/ConfigManager.cs:150-177` | `ReadFile` wartet bei gesperrter Datei mit `Thread.Sleep(40)` bis zu 4× auf dem UI-Thread (Reload läuft per Dispatcher). Max. 160 ms Ruckeln beim Speichern im Editor, kein Overlay offen. | niedrig | Lassen oder Reload auf einen Pool-Thread verlegen und nur das Anwenden im UI-Thread. Nutzen gering. | S |
| S12 | `Core/Providers/LlmProvider.cs:118-136` | `WarmUpAsync` verspricht „never throws“, fängt aber nur `HttpRequestException`/`OperationCanceledException`. Andere Ausnahmen würden als unbeobachtete Task-Ausnahme enden (Fire-and-forget-Aufrufer). | niedrig | Alle Ausnahmen außer kritischen fangen und loggen. | S |
| S13 | `LlmProvider.cs:35`, `:73-74` | Der statische Cache „welche Reasoning-Variante funktioniert“ wird beim Config-Reload nicht geleert. Ändert man bei gleichem Providernamen und Modell z. B. `base_url` oder `type`, startet der nächste Aufruf mit einer veralteten Variante (kostet höchstens einen zusätzlichen Fehlversuch). | niedrig | Schlüssel um Typ und Base-URL erweitern. | S |
| S14 | `Core/Config/SettingsLoader.cs:198` | `api_key = 123` (kein Text) wird stillschweigend ignoriert; die Warnung lautet dann „no api_key or api_key_env set“. | niedrig | Falschen Typ als eigenen Fehler/Warnung melden. Keine Schemaänderung. | S |

Positiv geprüft (kein Handlungsbedarf):
- Zwischenablage wird in **jedem** Lese- und Schreibpfad per `try/finally` wiederhergestellt (`TextAccessService.cs:149-153`, `:254-260`).
- Kein Text wird angefasst, bevor die Antwort vollständig und regulär beendet ist (`ProviderPromptRunner.cs:40-46`, `LlmProvider.cs:219-222`); abgeschnittene, blockierte oder abgebrochene Streams werfen typisierte Fehler.
- Kein `async void`. Doppelauslösung ist ausgeschlossen: alle Hotkeys laufen auf dem UI-Thread und prüfen `_busy`; `MOD_NOREPEAT` ist gesetzt.
- Feste Sleeps sind weitgehend durch Ereignisse ersetzt (Clipboard-Listener, verzögertes Rendern mit Bestätigung). Verbleibend: kurze Gnadenfrist nach dem Rendern (40 ms bzw. 900 ms bei Remote-Desktop) und Polling bei der UIA-Auswahlprüfung (10 ms Takt mit Budget); beide begründet.
- Sonderfälle: Admin-Fenster, Passwortfelder (Win32 und UIA), Terminals, schreibgeschützte Felder, leere Auswahl (Fallback auf ganzes Feld), > 50 000 Zeichen und Remote-Desktop-Clients werden erkannt.
- Netzwerk: ein langlebiger `HttpClient`, Timeout bis zum ersten Byte und zwischen Chunks, genau ein Retry bei verbrauchten Verbindungen bzw. 5xx/429 mit kurzem `Retry-After`, sauberes Mapping von 4xx/5xx.
- Config: Syntaxfehler, fehlende Felder, doppelte Hotkeys und unbekannte Provider werden mit Datei und Zeile gemeldet; die letzte funktionierende Konfiguration bleibt aktiv; der Watcher ist entprellt (300 ms).
- Ressourcen: Hotkeys werden bei jedem Reload und beim Beenden abgemeldet; Clipboard-Listener, Tray-Icon, Watcher und `HttpClient` werden in `OnExit` freigegeben.

### C. Sicherheit und Datenschutz

| ID | Fundstelle | Befund | Risiko | Vorschlag | Aufwand |
|---|---|---|---|---|---|
| P1 | `TextAccessService.cs:36-41`, `UiaProbe.cs:239` | Das Log enthält den UIA-Namen des fokussierten Elements (bis 60 Zeichen). In deinem Log stehen z. B. Dateinamen aus VSCodium; in Mail- oder Chat-Apps kann das ein Betreff oder Kontaktname sein. Kein Feldinhalt, aber mehr als nötig. | niedrig | Namen nicht mehr loggen (nur Länge oder gar nicht). Nur Log betroffen. | S |
| P2 | `AppController.cs:281` | Fehlertexte des Anbieters werden geloggt (bereinigt, max. 220 Zeichen). Einige APIs zitieren in 400-Fehlern Teile der Eingabe. | niedrig | Für `BadRequest` nur Statuscode und Fehlerart loggen; die Anzeige im Pill bleibt gleich. | S |
| P3 | Lesen per Strg+C | Wenn die Ziel-App den markierten Text kopiert, landet er im Windows-Zwischenablageverlauf (Win+V) und ggf. in der Cloud-Synchronisierung; das liegt an der Ziel-App und ist ohne UIA nicht vermeidbar. Das Zurücksetzen erzeugt zudem einen doppelten Verlaufseintrag deines vorherigen Inhalts. Eigene Inhalte (Sentinel, Ergebnis) sind korrekt mit `CanIncludeInClipboardHistory=0` markiert. | niedrig | Siehe „Empfehlungen für später“. | – |
| – | gesamt | **Geprüft ohne Befund:** API-Schlüssel nur im Header (Gemini ausdrücklich nicht in der URL), Schlüssel werden in Fehlertexten durch `***` ersetzt (`LlmProvider.cs:359-365`), kein Log-Aufruf enthält Schlüssel oder Nutzertext (nur Längen und Zeiten), `http://` zu entfernten Hosts mit Schlüssel wird abgelehnt (`SettingsLoader.cs:220-225`), OpenAI mit `store=false`. Keine Telemetrie. Einzige Netzwerkaufrufe ohne Nutzeraktion: ein `HEAD` auf die Base-URL des Standard-Anbieters beim Start, nach Config-Änderungen und beim Öffnen des Overlays (Verbindungs-Warm-up, ohne Daten). | – | Beibehalten; im README erwähnenswert. | – |

### D. Performance

| ID | Fundstelle | Befund | Risiko | Vorschlag | Aufwand |
|---|---|---|---|---|---|
| D1 | `App/App.xaml.cs:99`, `Platform/Native/NativeTimer.cs` | `timeBeginPeriod(1)` gilt für die gesamte Laufzeit, auch wenn die App stundenlang nur im Tray liegt. Das erhöht die Zahl der Timer-Interrupts des Prozesses und damit den Leerlauf-Energieverbrauch. | niedrig | Nur während eines Laufs (Hotkey bis Ende von Ersetzen/Abbruch) anheben. Der Aufruf kostet Mikrosekunden; Latenz vorher/nachher messen. | S |
| D2 | `App/App.xaml.cs:163-182` | Der Idle-Trim-Timer weckt die App alle 2 s, auch nach dem Trimmen, und liest jedes Mal eine Umgebungsvariable. | niedrig | Einmaligen Timer verwenden, der nach jeder Aktivität neu auf `idle_trim_seconds` gestellt wird. | S |
| D3 | `App/App.xaml.cs:117-125` | Der Start (340–430 ms) wird von Fenstererzeugung und Overlay-Warm-up (~300 ms) dominiert. Das ist gewollt: dadurch erscheint das Overlay später in 7–10 ms. | – | Nicht ändern. | – |
| D4 | `App/App.xaml.cs:129` und `:211` | `ProviderRegistry` wird beim Start zweimal erzeugt (die erste Instanz wird sofort ersetzt). | niedrig | Erste Erzeugung entfernen. | S |
| D5 | heißer Pfad | Hotkey → Overlay: keine Reflection, die Liste wird bei unveränderten Prompts nicht neu gebaut, Fenster sind vorgewärmt. Hotkey → Request: 5–27 ms. Leerlauf-Speicher wird nach 90 s zurückgegeben. | – | Kein messbares Potenzial; nicht anfassen. | – |

### E. Totcode und Überflüssiges

Belegt per Volltextsuche über `src` und `tests` (inkl. XAML); Reflection, XAML-Bindings und Config-Bezüge geprüft.

| ID | Fundstelle | Befund | Vorschlag | Aufwand |
|---|---|---|---|---|
| E1 | `Core/Prompts/IPromptRunner.cs:11-19` | `DummyPromptRunner` wird nirgends verwendet. | entfernen | S |
| E2 | `App/Diagnostics/MemoryDiagnostics.cs:25-30` | `LogAfterCollect` wird nirgends aufgerufen. | entfernen | S |
| E3 | `Platform/Tray/TrayIcon.cs:28`, `:111-113`, `:13` | `OnDoubleClick` wird nie gesetzt; Konstanten `NIIF_NONE`, `NIIF_WARNING` ungenutzt. | entfernen | S |
| E4 | `Platform/Native/NativeMethods.cs` | `WM_DESTROY`, `WM_LBUTTONUP`, `CF_TEXT`, `GetWindowRect` ungenutzt. | entfernen | S |
| E5 | `Core/Localization/Loc.cs:70`, `:72-76` | 6 ungenutzte Texte: `err_hotkey`, `tray_open_log`, `tray_theme`, `theme_system`, `theme_light`, `theme_dark` (Reste eines früheren Tray-Menüs). | entfernen | S |
| E6 | `App/Overlay/OverlayWindow.xaml.cs:88`, `App/Themes/ThemeService.cs:45`, `Platform/TextIntegration/TextAccess.cs:71` | Ungenutzte Member: `OverlayWindow.Handle`, `ThemeService.Mode`, `ReplaceResult.Detail`; `ReadStrategy.None` ungenutzt (harmlos, kann bleiben). | entfernen | S |
| E7 | `Core/Config/SettingsLoader.cs:140-147`, `App/Themes/ThemeService.cs:139-148`, `Core/Config/ColorParser.cs:53-73` | Hex-Farben werden an drei Stellen geparst (Akzent-Validierung per Regex, Akzent-Umrechnung in WPF-Farbe, Appearance-Farben). Die Akzent-Variante erlaubt zusätzlich `#RGB`. | Akzent-Parsing in `Core` an einer Stelle bündeln, `#RGB` und `#AARRGGBB` (Alpha ignoriert) exakt wie heute erhalten; vorher Charakterisierungstests. | S |
| E8 | 5 Provider-Dateien | `Settings.BaseUrl.TrimEnd('/')` wird fünfmal wiederholt, obwohl der Loader die URL bereits kürzt (`SettingsLoader.cs:196`). | in eine geschützte Eigenschaft der Basisklasse ziehen | S |
| E9 | `Core/Ghostwriter.Core.csproj` | `ImplicitUsings`/`Nullable` doppelt (stehen schon in `Directory.Build.props`); Datei hat als einzige ein BOM. | bereinigen | S |
| E10 | `TextAccessService.cs:63`, `:233-234` | Unerreichbare Null-Prüfung; leerer `if`-Zweig `{ /* proceed */ } else return …`. | vereinfachen | S |
| E11 | `tests/…/Ghostwriter.Tests.csproj` | `coverlet.collector` wird von keinem Skript genutzt. | entfernen oder bewusst behalten (für `dotnet test --collect`) | S |

Provider-Adapter: Die gemeinsame Logik (HTTP, Retry, Timeouts, Fehler-Mapping, Reasoning-Leiter) liegt bereits in `LlmProvider`; die Adapter enthalten nur API-spezifisches (Body, Header, Events). Weitere Zusammenlegung würde nichts sparen und Lesbarkeit kosten. Kein Über-Engineering gefunden, das sich lohnend entfernen ließe.

### F. Codequalität und Wartbarkeit

| ID | Befund | Vorschlag | Aufwand |
|---|---|---|---|
| F1 | Nullable Reference Types sind aktiv und sauber (0 Warnungen). Es gibt aber keine Analyzer-Stufe, keine `.editorconfig` und kein `TreatWarningsAsErrors`. | `TreatWarningsAsErrors` einschalten und `AnalysisLevel` moderat anheben; nur echte Befunde beheben, keine Massen-Umformatierung. | M |
| F2 | Methoden sind überwiegend kurz; die längsten sind `ExecuteAsync` (~85 Z.) und `ConfigManager.Reload` (~80 Z.), beide gut kommentiert. Magic Numbers sind fast überall benannte Konstanten. Englische Bezeichner und Kommentare durchgängig. | Keine Änderung nötig. | – |
| F3 | **Testabdeckung:** `Core` ist gut abgedeckt (350 Tests: Marker-Parser 24, Prompt-Aufbau/Universal 22, Config/Validierung/Reload 36+24, Provider inkl. SSE und Fehlerfälle 49, Undo 17, Appearance 36). **Lücken:** `Platform` und `App` haben **keine** Tests (Testprojekt ist `net10.0` und referenziert nur `Core`): Entscheidungslogik von `TextAccessService` (welche Strategie, wann abbrechen), Zwischenablage-Wiederherstellung, `AppController` (Busy-Flag, Abbruch, Fehlerpfade). Außerdem nicht direkt getestet: `TomlReader.LineOf/Block`, `LlmProvider.Sanitize` (nur ein Test). | Für Phase 2 vor S1/S4: ein zweites Testprojekt `net10.0-windows` (oder das bestehende umstellen), das `AppController` mit einem Fake-`ITextAccess` und `TextAccessService` mit einer schmalen Clipboard-/Input-Abstraktion testet. Nur so weit, wie für Charakterisierungstests der angefassten Stellen nötig. | M |

---

## Priorisierte Maßnahmenliste (Vorschlag für Phase 2)

**1. Stabilitäts- und Datenverlust-Risiken**
1. S3 – Ausnahmen in `MessageWindow.Proc` abfangen (kein Absturz mehr aus Hotkey/Tray).
2. S4 – `_busy` in allen Overlay-Pfaden zuverlässig zurücksetzen.
3. S1 + S6 – Fokus/Auswahl vor dem Einfügen prüfen *(Freigabe nötig: zusätzlicher Abbruch mit Meldung)*.
4. S2 – Abbruch nach gesendetem Strg+V ignorieren; Meldung bei fehlender Bestätigung *(Freigabe für den Text)*.
5. S5 – `settings.toml` atomar schreiben.
6. S9, S12, S10 – keine verschluckten Fehler, Warm-up wirft nie.
7. S7 – richtige Meldung bei belegter Zwischenablage *(Freigabe für den Text)*.
8. P1, P2 – weniger Kontext im Log.
9. S13, S14 – kleine Config-/Cache-Korrekturen.

**2. Totcode und Vereinfachung**
E1–E6, E8–E11 (reine Entfernungen, je ein Commit pro Bereich), danach E7 mit Charakterisierungstests.

**3. Performance**
D1 (Timer-Auflösung nur während eines Laufs), D2 (Idle-Timer), D4 (doppelte Registry). Vorher/Nachher messen.

**4. Lesbarkeit**
F1 (Warnungen als Fehler, moderate Analyzer), optional A3/A2 nur, wenn durch die Punkte oben ohnehin berührt.

Erwartete Größenänderung: gering (geschätzt −80 bis −120 Zeilen Totcode, +100 bis +200 Zeilen Tests). Keine neue Abhängigkeit geplant; Tomlyn bleibt (ein eigener TOML-Parser wäre mehr Code und mehr Risiko).

## Empfehlungen für später (nicht in Phase 2, weil verhaltensändernd oder riskant)
- **Zwischenablageverlauf (P3):** Den zurückgeschriebenen Inhalt als „nicht in den Verlauf“ markieren, um Doppel-Einträge zu vermeiden. Ändert, was du in Win+V siehst; deshalb nur auf Wunsch.
- **Große Zwischenablage-Inhalte (S8):** Bei sehr großen Kopien den Lauf vorher ablehnen statt teilweise wiederherzustellen. Neue Meldung, neues Verhalten.
- **Abbruch per Esc während der KI-Anfrage:** Heute bricht Esc nur das Overlay ab; ein laufender Auftrag wird per Klick auf die Statuspille abgebrochen. Esc global abzufangen wäre ein neues Feature.
- **Größerer Umbau von `App.xaml.cs`/`AppController`** in kleinere Klassen: kein akuter Nutzen, nur bei künftigen Features.

---

## Phase 2: Umsetzung (freigegeben am 2026-10-08)

Freigegeben wurden alle drei sichtbaren Änderungen (S1/S6 Abbruch bei Fokuswechsel, S2 ehrlichere Meldung, S7 richtige
Meldung bei belegter Zwischenablage). Jeder Schritt ist ein eigener Commit; nach jedem Commit waren Build (0 Warnungen)
und alle Tests grün.

| Commit | Befund | Inhalt |
|---|---|---|
| `5af2dc5` | S3 | Ausnahmen in der Fensterprozedur (Hotkeys, Tray-Menü) werden abgefangen, geloggt und als Hinweis angezeigt, statt die App zu beenden. |
| `a48ad04` | S4 | Fehler beim Öffnen oder Aktivieren des Overlays schließen es und setzen den Busy-Zustand zurück. |
| `c682d73` | S1, S6, S10 | Vor dem Einfügen müssen fokussiertes Child-Fenster (Win32) und fokussiertes UIA-Element dieselben sein wie beim Hotkey; eine per UIA gelesene Auswahl muss noch markiert sein. Bewiesene Abweichung führt zum Abbruch mit „Das Fenster hat gewechselt…“. Kann eine Prüfung nicht rechtzeitig antworten (150 ms), gilt das alte Verhalten. Abgebrochene UIA-Aufrufe werden beobachtet. |
| `2dcc9e3` | S2, S7 | Ein Abbruch zählt nur bis zum Strg+V; danach wird die Bestätigung abgewartet. Neue Meldung „…nicht bestätigt. Bitte den Text prüfen (Strg+Z…)“. Eine beim Lesen belegte Zwischenablage heißt jetzt „Die Zwischenablage ist gerade belegt“. |
| `bd49891` | S5 | `settings.toml` wird über eine temporäre Datei atomar ersetzt (Charakterisierungstest vorab). |
| `a518d1c` | S9 | Keine leeren `catch`-Blöcke mehr (Migration, Debouncer); `AppLog.Init` wirft nie. |
| `1a6a3db` | S12, S13 | Warm-up wirft nie; der Reasoning-Cache berücksichtigt Typ und Base-URL. |
| `80f8bb8` | S14 | `api_key = 123` meldet „api_key must be text“ (weiterhin nur Warnung; Test vorab). |
| `949ab4d` | P1, P2 | Kein UIA-Elementname mehr im Log; der Detailtext abgelehnter Anfragen (400) wird angezeigt, aber nicht geloggt. |
| `c0cb637` | E1–E6 | Totcode entfernt (Liste im Commit-Text, per Volltextsuche inkl. XAML belegt). |
| `3a024ee` | E8–E11 | `BaseUrl`-Eigenschaft statt 5× `TrimEnd`, Core-csproj bereinigt, leerer `if`-Zweig entfernt, `coverlet.collector` entfernt. |
| `19f7110` | E7 | Ein Parser für Akzentfarben (`ColorParser.TryParseAccentHex`), Charakterisierungstests vorab. |
| `067b888` | D1 | 1-ms-Timerauflösung nur noch, solange Overlay oder Lauf aktiv sind. |
| `cdb188c` | D2, D4 | Idle-Trim als einmaliger Timer statt 2-s-Takt; Provider-Registry beim Start nur einmal erzeugt. |
| `ba40e73` | F1 | `TreatWarningsAsErrors` + `AnalysisLevel=latest-minimum`; 13 Fundstellen ohne Verhaltensänderung behoben. |

Tests: 350 → **375** (neu: atomares Schreiben und Encoding, `api_key`-Typ, Akzent-Validierung und -Umrechnung).

Abweichungen vom Plan:
- **E10 teilweise:** Die „unerreichbare“ Null-Prüfung in `TextAccessService.CaptureAsync` bleibt, weil der Compiler sie
  für die Nullable-Analyse braucht. Nur der leere `if`-Zweig wurde entfernt.
- **F3 (Tests für Platform/App) nicht umgesetzt** *(inzwischen umgesetzt, siehe [Nachtrag F3](#nachtrag-2026-10-10-f3-tests-für-platform-und-app))*: `ClipboardService` und `TextAccessService` rufen Win32 direkt auf.
  Sie testbar zu machen hieße, Zwischenablage und Tastatureingabe hinter neue Interfaces zu legen. Das wäre mehr Code und
  ein größerer Umbau genau im empfindlichsten Teil. Die Zwischenablage-Wiederherstellung ist weiterhin per `try/finally`
  in jedem Pfad abgesichert; die neuen Prüfungen schreiben jede Entscheidung ins Log („field check …“).
- **Größe:** Statt der geschätzten Netto-Verkleinerung ist der Produktivcode um gut 200 Zeilen gewachsen. Entfernt wurden
  rund 90 Zeilen Totcode; hinzu kamen die neuen Schutzprüfungen (S1/S6, S3, S4, S5) mit ihren Kommentaren.

## Phase 3: Verifikation

- `dotnet build -c Release --no-incremental`: **0 Warnungen, 0 Fehler**, jetzt mit `TreatWarningsAsErrors` und Analyzern.
- `dotnet test`: **375/375 grün**.
- Startzeit: beide Publish-Builds (ReadyToRun) abwechselnd je 6× mit derselben isolierten Test-Config gestartet:

| | Baseline | Nachher |
|---|---|---|
| „Ready after“ (Median) | 377 ms (363–387) | 375 ms (364–402) |
| Prozessstart bis „Ready“ (Median) | 548 ms | 542 ms |
| Working Set 1,5 s nach dem Start | 146–147 MB | 146 MB |

  Der Unterschied liegt im Rauschen.
- **Hotkey → Overlay und Hotkey → Request:** Das konnte ich nicht selbst messen, ohne Tastendrücke in deine laufende
  Sitzung zu schicken. Im heißen Pfad kommt nur ein `timeBeginPeriod`-Aufruf hinzu (Mikrosekunden). Neu ist die
  Feldprüfung *nach* der KI-Antwort und *vor* dem Einfügen (UIA, Budget 150 ms, typisch wenige ms); ihre Dauer steht als
  `field check [...] (N ms)` im Log. Bitte nach dem manuellen Test die Zeilen `first frame after`, `request sent` und
  `field check` mit der Baseline oben vergleichen.

### Manuelle Testcheckliste

In **Notepad**, einem **Browser** (Textfeld auf einer Webseite), **Word** und einer **Chat-App** jeweils:

1. **Auswahl:** Text markieren, Prompt-Hotkey (z. B. Strg+Alt+K): nur die Auswahl wird ersetzt.
2. **Ohne Auswahl:** Cursor ins Feld, nichts markiert, Overlay-Hotkey, Prompt wählen: das ganze Feld wird ersetzt.
3. **Marker:** `<<kürzer>> Langer Text …` mit dem Universal-Prompt: Marker verschwindet, Text gekürzt.
   Unvollständig `<<kürzer Text`: Meldung „Marker << ohne schließendes >>“, nichts geändert.
4. **Auftragsmodus:** `Absage an Freunde fürs Wochenende` mit Universal oder „Auftrag“: fertige Nachricht.
5. **Fehlerfall ohne Netzwerk:** WLAN aus, Prompt ausführen: „Keine Verbindung. Der Text blieb unverändert.“; Text und Zwischenablage unverändert.
6. **Abbruch mit Esc:** Overlay öffnen, Esc: Overlay zu, Fokus zurück im Feld. Während der KI-Anfrage Klick auf die Statuspille: Abbruch, Text unverändert.
7. **Neu, Fokuswechsel:** Prompt starten und während der Anfrage in ein *anderes* Feld desselben Fensters klicken (Browser: anderes Eingabefeld; Word: Kopfzeile oder Suchfeld): Meldung „Das Fenster hat gewechselt…“, beide Felder unverändert. **Gegenprobe:** im selben Feld bleiben, die Ersetzung läuft wie gewohnt. Das ist wichtig, um falsche Abbrüche auszuschließen.
8. **Neu, Auswahl geändert:** Text markieren, Prompt starten, während der Anfrage woanders ins selbe Feld klicken: Abbruch statt Einfügen am Cursor.
9. **Config-Reload mit Syntaxfehler:** in `settings.toml` eine Zeile kaputt machen und speichern: Meldung mit Datei und Zeile, alte Config bleibt aktiv; Fehler beheben, Config wird wieder geladen.
10. **Zwischenablage:** vorher ein Bild oder formatierten Text kopieren: nach jeder Ersetzung ist er unverändert in der Zwischenablage.
11. **Tray:** „Mit Windows starten“ umschalten: Häkchen und `settings.toml` stimmen überein. „Config-Ordner öffnen“ funktioniert.

---

## Abschlussbericht

### 1. Vorher/Nachher
| | Vorher | Nachher |
|---|---:|---:|
| Produktivcode (Zeilen / nicht leer) | 7 462 / 6 387 | 7 667 / 6 570 |
| Testcode (Zeilen) | 2 865 | 2 943 |
| Dateien (.cs/.xaml) Produktiv / Tests | 62 / 12 | 62 / 12 |
| Projekte | 4 | 4 |
| NuGet Produktiv / Tests | 1 / 4 | 1 / 3 |
| Tests | 350 | 375 |
| Build-Warnungen | 0 (ohne Analyzer) | 0 (mit Analyzern, Warnungen = Fehler) |
| Publish (framework-dependent, R2R) | 2 151 370 B | 2 171 850 B (+0,95 %) |
| Start „Ready“ (Median) | 377 ms | 375 ms |
| Hotkey → Overlay / → Request | 7–10 ms / 5–27 ms | siehe Phase 3 (bitte aus dem Log ablesen) |

### 2. Was behoben wurde
Siehe Tabelle „Phase 2“: Stabilität (S1–S7, S9, S10, S12–S14), Datenschutz (P1, P2), Totcode und Vereinfachung (E1–E11),
Performance (D1, D2, D4), Warnungs-Hygiene (F1).

### 3. Bewusst nicht angefasst
- **S8** (Zwischenablage-Snapshot ohne Gesamtgrenze): eine Grenze würde beim Zurücksetzen Inhalte verlieren.
- **S11** (kurze Sleeps beim Config-Lesen auf dem UI-Thread): höchstens 160 ms und nur beim Speichern im Editor; der Umbau lohnt nicht.
- **D3** (Startpfad): die rund 300 ms für Fenster und Warm-up sind der Preis für ein Overlay in 7–10 ms.
- **A2/A3** (`App.xaml.cs`/`AppController` aufteilen): kein Stabilitätsgewinn, nur Diff.
- **F3** (Tests für Platform/App): siehe oben; inzwischen umgesetzt, siehe [Nachtrag F3](#nachtrag-2026-10-10-f3-tests-für-platform-und-app).
- **Analyzer-Stufe „recommended“**: brächte vor allem Stilregeln (480× Unterstriche in Testnamen) und Kultur-Hinweise ohne praktische Wirkung.
- Provider-Adapter nicht weiter zusammengelegt: die Gemeinsamkeiten liegen schon in `LlmProvider`.

### 4. Restrisiken und Empfehlungen für später
- Die Feldprüfung (S1/S6) verlässt sich auf UIA-Laufzeit-IDs. Liefert eine App für dasselbe Feld nach der Fokus-Rückkehr
  ein anderes Element, gibt es einen **falschen Abbruch** (Meldung, kein Datenverlust). Punkt 7 der Checkliste (Gegenprobe)
  deckt das ab; die Log-Zeile `field check` zeigt, welche Prüfung angeschlagen hat.
- In Apps ohne UIA und mit nur einem Fensterhandle (manche Java-, Qt- oder Spiele-Oberflächen) kann ein Fokuswechsel
  innerhalb des Fensters weiterhin nicht erkannt werden.
- Eine *späte* Paste nach 2 s ohne Bestätigung bleibt möglich (S2); die Meldung weist jetzt darauf hin.
- Zwischenablageverlauf (P3), große Zwischenablage (S8), Esc während der Anfrage und ein größerer Umbau: wie unter
  „Empfehlungen für später“ in Phase 1.

### 5. Was sich für dich spürbar anders verhalten kann
- Klickst du während einer Anfrage in ein anderes Feld (oder hebst die Markierung auf), wird **nicht mehr eingefügt**;
  stattdessen erscheint „Das Fenster hat gewechselt, der Text wurde nicht ersetzt.“
- Neue Texte: „Die Ziel-App hat das Einfügen nicht bestätigt. Bitte den Text prüfen (Strg+Z macht es rückgängig).“ und bei
  gesperrter Zwischenablage „Die Zwischenablage ist gerade belegt.“
- Ein Klick auf die Statuspille *nach* dem Einfügen-Befehl bricht nicht mehr ab (vorher konnte er die Zwischenablage zu
  früh zurücksetzen).
- Fehler aus Tray-Menü oder Hotkey beenden die App nicht mehr, sondern zeigen „Unerwarteter Fehler: …“.
- Sonst nichts: Hotkeys, Prompt-Modi, Marker-Syntax, TOML-Formate und Abläufe sind unverändert.

---

## Nachtrag (2026-10-10): zwei Empfehlungen umgesetzt
- **Esc während der Anfrage:** `Esc` wird für die Dauer eines Laufs als temporärer Hotkey registriert (wie bei der
  Ergebnis-Karte) und wirkt wie ein Klick auf die Statuspille; nach dem Einfügen-Befehl wird weiterhin nicht abgebrochen.
  Bei Overlay-Ausgabe übernimmt die Karte `Esc`, sobald der Text gelesen ist. `AppController.RegisterRunEsc`.
- **Zwischenablageverlauf (P3):** Der zurückgeschriebene Snapshot wird mit `CanIncludeInClipboardHistory=0`,
  `CanUploadToCloudClipboard=0` und `ExcludeClipboardContentFromMonitorProcessing` markiert, damit kein Doppel-Eintrag
  entsteht. Ein leerer Snapshot bleibt leer. `ClipboardService.TryRestore`. Dass die Ziel-App beim Strg+C die Markierung
  in den Verlauf legt, bleibt unvermeidbar.

## Nachtrag (2026-10-10): F3, Tests für Platform und App

**Entscheidung:** ein zweites Testprojekt `tests/Kuroko.Windows.Tests` (`net10.0-windows`, `UseWPF`), das Core, Platform und
App referenziert. `Kuroko.Tests` bleibt `net10.0` und rein auf Core. Die Alternative, die Entscheidungslogik nach Core zu
verschieben, hätte `TargetInfo`, `UiaFocusInfo` und den ganzen Lese-/Einfüge-Ablauf umziehen lassen, also genau den
empfindlichsten Teil umgebaut. Stattdessen bleibt der Code, wo er ist, und bekommt schmale Interfaces für alles, was
Windows berührt. Die neuen Tests laufen damit nur unter Windows; Kuroko selbst läuft ohnehin nur dort.

**Neue Schnittstellen** (je ein Commit, Verhalten unverändert, vor und nach jedem Schritt Build mit 0 Warnungen und alle Tests grün):

| Interface | echte Implementierung | wofür |
|---|---|---|
| `IClipboard` | `ClipboardService` | Snapshot/Restore, Sentinel, verzögertes Einfügen, Änderungen abwarten |
| `IKeyboard` | `InputSimulator` (jetzt eine Instanz statt statisch) | Strg+A/C/V |
| `IFieldInspector` | `UiaFieldInspector` | UIA-Abfragen, Vordergrund- und Fokusfenster; das UIA-Element reist als undurchsichtiges `FieldElement` |
| `IHotkeyRegistry` | `HotkeyManager` | temporäre Hotkeys (Esc, Kopieren) |
| `IOverlayView`, `IStatusView` | `OverlayWindow`, `StatusWindow` | Prompt-Auswahl, Pille, Ergebnis-Karte |
| `IDesktop` | `Win32Desktop` | Zielfenster erfassen, Fokus zurückgeben, Timer-Auflösung, Vordergrund-Überwachung, Frame-Callback fürs Log |

Die Zeitbudgets von `TextAccessService` stehen jetzt in einem internen `TextAccessTimings` (gleiche Werte); Tests kürzen sie,
damit Timeout-Pfade nicht Sekunden dauern. `Kuroko.Platform` gibt seine Interna per `InternalsVisibleTo` nur dem Testprojekt frei.

**Tests:** 92 neue, insgesamt 539 (447 Core + 92 Windows), alle grün, auch bei achtfacher Wiederholung.
- `TextAccessServiceTests` (39): welche Strategie liest (UIA-Auswahl, ganzes Feld per UIA, Strg+C, Strg+A als Rückfall,
  Strg+A zuerst in Web-Editoren), wann nie Strg+A gesendet wird (Overlay-Ausgabe, Explorer, Nicht-Text-Elemente),
  Fehlerfälle (Zwischenablage belegt beim Sichern oder beim Sentinel, Eingabe blockiert, Text zu lang, nichts kopiert, App
  füllt die Zwischenablage in zwei Schritten), die Prüfungen vor dem Einfügen (anderes Fenster, anderes Fokusfenster,
  anderes Feld, Auswahl verschoben, Ganzfeld-Auswahl nicht beweisbar, Prüfungen ohne Antwort blockieren nicht),
  Einfügen nicht bestätigt, Abbruch vor und nach Strg+V, längere Wartezeit bei Remote-Desktop, und dass die Zwischenablage
  des Nutzers immer zurückkommt, und zwar erst nach dem Einfügen.
- `AppControllerTests` (43): Busy-Flag und Timer-Auflösung, Esc und Klick auf die Pille zu verschiedenen Zeitpunkten
  (beim Lesen, während die KI arbeitet, nach der Antwort aber vor dem Einfügen, nach dem Lauf), Fehlerpillen für jede
  Lese- und Einfügestörung, KI-Fehler, unerwartete Ausnahmen vor und nach der Antwort, Antwort aufbewahren und per
  Hotkey kopieren (auch: Hotkey belegt, Zwischenablage belegt, Pille abgelaufen), Fallback-Anbieter, Overlay-Pfade
  (Fokus zurück, Wegklicken, Passwortfeld erst nach dem Öffnen erkannt, Fehler beim Öffnen), Ergebnis-Karte (Streaming,
  Kopieren erst nach dem Ende, Esc, Fensterwechsel, neuer Hotkey, Fehler mitten in der Antwort) und Rückgängig.
- `CapturePolicyTests` (10): Lese-Regeln je Modus und die Erkennung von Terminals, Explorer und Remote-Clients.
- Die `AppController`-Tests laufen auf einem eigenen Thread mit Nachrichtenschleife (`UiThread.Run`), damit jede
  Fortsetzung wie in der App auf denselben Thread zurückkommt.

**Gegenprobe durch gezieltes Brechen:** 17 Änderungen an der Logik eingebaut, jeweils einzeln getestet und wieder entfernt.
Alle 17 wurden von mindestens einem Test erkannt:
- `TextAccessService`: Zwischenablage nach dem Lesen nicht zurückschreiben; Abbruch verkürzt das Warten nach Strg+V;
  keine Vordergrund-Prüfung; Strg+A auch bei Overlay-Ausgabe; Einfügen trotz unbewiesener Ganzfeld-Auswahl; UIA-„leer“
  auch in Web-Engines vertrauen; Zwischenablage vor der Bestätigung zurückschreiben.
- `AppController`: Busy-Flag nach dem Lauf nicht zurücksetzen; Abbruch zeigt eine Fehlerpille; Antwort bei unerwartetem
  Fehler verlieren; Antwort nicht aufbewahren; Esc nach dem Lauf nicht freigeben; Fehler beim Öffnen des Overlays lässt
  den Controller beschäftigt; Fallback-Hinweis fehlt; Klick auf die Pille ignoriert; Schließen der Karte bricht die
  Anfrage nicht ab; Kopier-Hotkey der Fehlerpille nicht registriert.

**Weiterhin nur von Hand prüfbar** (bleibt hinter den Interfaces und wird nicht simuliert; dafür gilt die
[manuelle Testcheckliste](#manuelle-testcheckliste)):
- echtes `SendInput`: gedrückt gehaltene Modifier aus dem Hotkey, UIPI bei Fenstern mit Administratorrechten;
- echte Zwischenablage: Sperren durch andere Programme, verzögertes Rendern (`WM_RENDERFORMAT`), Snapshot und Restore
  aller Formate (Bilder, formatierter Text), Verlauf-/Cloud-Markierungen;
- was echte Apps über UI Automation melden (Auswahl, Monaco-Hilfsfeld in VS Code, Laufzeit-IDs nach Fokus-Rückkehr),
  `GetGUIThreadInfo`, Fokus-Rückgabe;
- Registrieren globaler Hotkeys, der WinEvent-Hook für den Fensterwechsel, Darstellung und Platzierung von Overlay,
  Pille und Karte, echte Zeiten.

**Beobachtung, nicht geändert:** `ClipboardService.WaitForChangeAsync` reicht das Abbruch-Token an `Task.Delay` in einem
`Task.WhenAny` weiter. Ein Abbruch während des Lesens beendet das Warten deshalb nicht, sondern lässt die Schleife bis zum
Ende des Kopier-Timeouts (höchstens 450 ms) ohne Pause laufen; danach greift der Abbruch wie gewohnt. Für den Nutzer ist das
höchstens eine halbe Sekunde Verzögerung, aber unnötige CPU-Last. Kandidat für eine kleine, eigene Änderung.
