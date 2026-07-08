# Echo

Кроссплатформенное приложение диктовки с локальным распознаванием речи.

## Сборка

```bash
dotnet build Echo.slnx
dotnet run --project src/Echo.App
```

## Публикация

```powershell
./build/publish.ps1 win-x64
./build/publish.ps1 linux-x64
./build/publish.ps1 osx-arm64
```

## Структура

- `src/Echo.Core` — конфиг, история, оркестрация
- `src/Echo.Engines` — Whisper.net и GigaAM (Sherpa-ONNX)
- `src/Echo.Platform.*` — аудио, хоткей, ввод текста
- `src/Echo.App` — Avalonia UI

Данные пользователя: `%APPDATA%\Echo` (Windows), `~/Library/Application Support/Echo` (macOS), `~/.config/echo` (Linux).
