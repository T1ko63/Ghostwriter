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
The interface is pre-warmed, so the overlay appears 7–16 ms after the hotkey and the request is sent 5–27 ms after it (measured on a typical machine).

### Flexible prompts
Type an instruction alongside your text, wrapped in markers such as `<<shorter and friendlier>>`. Kuroko separates the command from the content and returns the result to the active text field.

### Minimalist interface
A Raycast-inspired overlay offers fuzzy search and number-key shortcuts for accessing prompts. Prompts can also have their own hotkeys and run without it.

### Optional Windows blur effects
A dedicated Windhawk C++ mod adds DWM blur effects without affecting the C# core’s performance.

### TOML configuration
Manage providers, models, hotkeys, and custom prompts in two readable TOML files. Changes apply as soon as you save.

### Secure API keys
Store API keys in the Windows Credential Manager from the tray menu instead of keeping them in plain text.

## Getting started

1. Build Kuroko (requires the .NET 10 SDK):
   ```powershell
   powershell -File tools\publish.ps1                 # small build, needs the .NET 10 Desktop Runtime
   powershell -File tools\publish.ps1 -SelfContained  # ~150 MB, runs anywhere
   ```
   The result lands in `publish\Kuroko` or `publish\Kuroko-selfcontained`. Start `Kuroko.exe`; it lives in the tray.
2. On first start, Kuroko creates `settings.toml` and `prompts.toml` in `%APPDATA%\Kuroko` (or in the folder set by `KUROKO_CONFIG_DIR`). Commented templates with every key are in [`config/`](config).
3. Add an API key via the tray icon → **API keys …** (**API-Schlüssel …** in the German UI). It is stored as `Kuroko:<provider>` (for example `Kuroko:gemini`) in the Windows Credential Manager. Alternatively, set `api_key_env` or `api_key` in the provider block of `settings.toml`.
4. Select text in any app, or just type, and press `Ctrl+Shift+Space` to pick a prompt.

The full guide (in German) is in [`docs/README.md`](docs/README.md).

## Disclaimer and license

This personal project is provided “AS IS,” without guaranteed support, roadmap, or maintenance. You may fork, adapt, or use it as inspiration.

Distributed under the MIT License.
