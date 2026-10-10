# 黒子 Kuroko

Designed to be invisible, engineered to be everywhere

<img width="400" height="320" alt="Screenshot 2026-10-08 074916" src="https://github.com/user-attachments/assets/6cbd501e-4fba-42eb-bd5e-9f23caae6523" />

## Features

### Lightweight and portable
Kuroko requires no installation or administrator privileges and runs directly on restricted work or corporate computers. It comes in two builds: a small one of about 2 MB that needs the .NET 10 Desktop Runtime, and a self-contained one of about 150 MB that runs on any Windows PC.

### In-text AI processing
Kuroko processes text wherever the cursor is active, including Outlook, VS Code, WhatsApp, and browsers. It replaces the selected text with the AI-generated result.

### Keyboard-first workflow
Control every step using keyboard shortcuts, from selecting text to applying the transformation.

### Fast response
The interface is pre-warmed, so in everyday use the overlay appears about 18 ms after the hotkey and the request leaves the app about 18 ms after it (medians from the log of a typical machine; the first overlay after a start takes 45–80 ms).

### Flexible prompts
Type an instruction alongside your text, wrapped in markers such as `<<shorter and friendlier>>`. Kuroko separates the command from the content and returns the result to the active text field.

### Minimalist interface
A Raycast-inspired overlay offers fuzzy search and number-key shortcuts for accessing prompts. Prompts can also have their own hotkeys and run without it.

### Optional Windows blur effects
A dedicated Windhawk C++ mod adds DWM blur effects without affecting the C# core’s performance.

### TOML configuration
Manage providers, models, hotkeys, and custom prompts in two readable TOML files. Changes apply as soon as you save. JSON schemas in `schemas/` give completion and checks in editors with Taplo (e.g. Even Better TOML).

### Secure API keys
Store API keys in the Windows Credential Manager from the tray menu instead of keeping them in plain text.

## Getting started

1. Download the zip (`Kuroko-<version>-win-x64.zip`) from the latest release on the [GitHub Releases page](https://github.com/T1ko63/Kuroko/releases/latest), unzip it into any folder and start `Kuroko.exe`; it lives in the tray.

   The zip contains the small build, which needs the **.NET Desktop Runtime 10** (x64). If it is missing, Windows says so when `Kuroko.exe` starts and offers to open the download page. Install it from [dotnet.microsoft.com](https://dotnet.microsoft.com/download/dotnet/10.0); a per-user install works without administrator rights. Where nothing can be installed, build the self-contained variant instead (see [Build from source](#build-from-source)).
2. On first start, Kuroko creates `settings.toml` and `prompts.toml` in `%APPDATA%\Kuroko` (or in the folder set by `KUROKO_CONFIG_DIR`). Commented templates with every key are in [`config/`](config).
3. Add an API key via the tray icon → **API keys …** (**API-Schlüssel …** in the German UI). It is stored as `Kuroko:<provider>` (for example `Kuroko:gemini`) in the Windows Credential Manager. Alternatively, set `api_key_env` or `api_key` in the provider block of `settings.toml` (tray → **Edit settings** opens it). If no key is found at start, a hint names the environment variable Kuroko expects. Tray → **Test connection** checks key, model and network with one tiny request.
4. Select text in any app, or just type, and press `Ctrl+Shift+Space` to pick a prompt.

## Build from source

Building requires the .NET 10 SDK:
```powershell
powershell -File tools\publish.ps1                 # small build, needs the .NET 10 Desktop Runtime
powershell -File tools\publish.ps1 -SelfContained  # ~150 MB, runs anywhere
```
The result lands in `publish\Kuroko` or `publish\Kuroko-selfcontained`. The small build is the same one as in the release zip; the self-contained build runs on PCs where the .NET 10 Desktop Runtime cannot be installed.

## Usage

| Action | Default hotkey |
|---|---|
| Open the overlay (search and pick a prompt) | `Ctrl+Shift+Space` |
| Run the Universal prompt directly (write **and** edit) | `Ctrl+Alt+M` |
| Run another prompt directly | per prompt in `prompts.toml`, e.g. `Ctrl+Alt+K` for Correction |
| Undo the last replacement | `Ctrl+Alt+Z` |
| Cancel a running request | `Esc`, only while a request is running |
| Close the result card (cancels a running request) | `Esc`, only while a card is visible |
| Copy the result card and close it | `Ctrl+Alt+C`, only while a card is visible |
| Copy an answer that could not be pasted | `Ctrl+Alt+C` while the error pill is shown, later tray → **Copy last result** |

Kuroko works on the selected text, or on the whole field if nothing is selected. The Universal prompt decides from the text:
without a marker, the text is a task (“decline Friday’s game night”) and the answer replaces it; with a marker such as
`<<shorter and friendlier>>`, the marker’s instruction is applied to the rest of the text. Prompts with
`output = "overlay"` show the answer in a small card and leave the text untouched, which also works on read-only pages.

The field is only changed once the complete answer has arrived. If the paste then fails (the window changed, the
clipboard was busy), the answer is not lost: copy it from the error pill or from the tray menu. Password fields, terminals, administrator windows and
texts over 50,000 characters are not supported.

## Privacy

* **No telemetry**, no usage statistics, no update check, no account. Kuroko only talks to the providers in `settings.toml`.
* **When you run a prompt**, the prompt’s instruction and the captured text (selection or field, up to 50,000 characters)
  go to that prompt’s provider, together with the model and output settings. No window titles, app names or other
  clipboard content. OpenAI requests are sent with `store = false`; Gemini requests always enable Google Search
  grounding. With a local OpenAI-compatible server (Ollama, LM Studio) the text never leaves your PC.
* **Fallback provider** (`fallback_provider`, off by default): if the prompt’s provider cannot be reached (no
  connection, 5xx, 429), the same text goes once to the fallback provider, so to a *different* provider than the
  prompt names. The pill says so. Local providers ignore the global setting and only fall back when their own block
  names a fallback.
* **Without a prompt**, Kuroko sends one `HEAD` request to the default provider’s `base_url` at start, after a config
  change and when the overlay opens, so the connection is ready. It carries no text and no key.
* **API keys** travel only in the request header, never in the URL, and never over plain `http://` to a remote host.
  They are masked in error messages and never logged.
* **Stored on disk:** `settings.toml`, `prompts.toml` and `kuroko.log` in `%APPDATA%\Kuroko`. The log holds timings,
  text lengths, the target program’s name and prompt names, never your text, results or keys. Undo history, the last
  answer (for **Copy last result**) and the saved clipboard stay in memory only; what Kuroko puts on the clipboard,
  including your previous content when it is put back, is kept out of clipboard history and cloud sync, so a run adds
  no duplicate entries. Only what you ask Kuroko to copy is a normal copy that may appear in clipboard history.

Details are in the German guide under [Datenschutz](docs/README.md#datenschutz).

## Documentation

* [`docs/README.md`](docs/README.md): the full guide in German (all settings, result card, undo, appearance, troubleshooting, measurements).
* [`config/`](config): commented templates of `settings.toml` and `prompts.toml` with every key.
* [`docs/refactoring-report.md`](docs/refactoring-report.md): code audit from October 2026 (German, written when Kuroko was still called Ghostwriter).

## Disclaimer and license

This personal project is provided “AS IS,” without guaranteed support, roadmap, or maintenance. You may fork, adapt, or use it as inspiration.

Distributed under the [MIT License](LICENSE).
