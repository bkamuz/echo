# Echo

**Local voice dictation for your desktop.** Hold a hotkey, speak, and Echo turns your speech into text — right where your cursor is.

Echo runs entirely on your machine. Speech is captured from your microphone, recognized locally, and pasted into the active window. No cloud upload, no subscription, no account required.

## What it does

Echo is a system-wide dictation assistant. It works in any app where you can type: email, chat, documents, code editors, browsers, and more.

1. **Press and hold** your chosen hotkey (default: Ctrl+Win on Windows).
2. **Speak** while the key is held. Echo shows a tray indicator while listening.
3. **Release** the key. Echo transcribes the audio and inserts the text at your cursor.

You can review past dictations in the built-in history, switch recognition engines and models, and tune how text is inserted.

## Why Echo

- **Private by design** — recognition happens on your computer. Audio and transcripts stay local.
- **Always available** — Echo lives in the system tray and responds to a global hotkey from any app.
- **Flexible recognition** — choose between engines tuned for different languages and use cases.
- **Optional GPU acceleration** — on Windows, DirectML can speed up recognition when a compatible GPU is available.

## Recognition engines

Echo supports several offline speech models. On first use, the app downloads the model you select to your user data folder.

| Engine | Best for |
|--------|----------|
| **GigaAM** | Russian speech (default) |
| **Whisper** | Multilingual recognition; several model sizes from fast to accurate |
| **Omnilingual ASR** | Broad language coverage in a compact model |

Language, model size, microphone, and input method can all be changed in Settings.

## Supported platforms

| Platform | Status |
|----------|--------|
| **Windows 10/11 (x64)** | Fully supported — hotkey, recording, and automatic text insertion |
| **Linux (x64)** | Supported on common desktop setups (X11, Wayland). Ubuntu with GNOME on Wayland is tested end-to-end. Some environments may need extra tools for text insertion; Echo can guide you through setup. |
| **macOS (Apple Silicon)** | In development — the interface builds, but dictation is not yet functional |
| **Flatpak (Linux)** | Available; sandbox limits may apply to some system integrations |

If you try Echo on a setup not listed here, [open an issue](https://github.com/bkamuz/echo/issues) with your distro, desktop environment, and what worked or did not.

## Download

Pre-built packages are published on **[GitHub Releases](https://github.com/bkamuz/echo/releases)**.

| Platform | Format | How to run |
|----------|--------|------------|
| Windows | `.zip` (portable) | Unzip and run `Echo.App.exe` |
| Windows | `.exe` (installer) | Run the setup wizard |
| Linux | `.tar.gz` | Extract, then `./Echo.App` |
| Linux | `.deb` | `sudo apt install ./Echo-*-linux-x64.deb` |
| Linux | `.AppImage` | `chmod +x Echo-*.AppImage && ./Echo-*.AppImage` |
| Linux | `.flatpak` | `flatpak install --user ./Echo-*.flatpak` |
| macOS (Apple Silicon) | `.tar.gz` | Extract, then `./Echo.App` |

Echo checks for updates from GitHub Releases and can apply them from within the app.

## Getting started

1. Download and run Echo for your platform.
2. Open **Settings** and confirm your hotkey, microphone, and recognition engine.
3. Download a speech model when prompted (one-time; stored in your profile folder).
4. Focus any text field, **hold the hotkey**, speak, and **release** to insert the result.

Echo starts minimized to the system tray. Right-click the tray icon for quick access to settings and history.

### Where data is stored

| OS | Location |
|----|----------|
| Windows | `%APPDATA%\Echo` |
| macOS | `~/Library/Application Support/Echo` |
| Linux | `~/.config/echo` |

Models, settings, and dictation history live here. Uninstalling Echo does not remove this folder unless you delete it manually.

## Building from source

Echo is a .NET application with an Avalonia UI. To build locally:

```bash
dotnet build Echo.slnx
dotnet run --project src/Echo.App
```

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download) or newer.

## Contributing

Bug reports, platform test results, and pull requests are welcome. Please include your OS, desktop environment (on Linux), and Echo version when reporting issues.
