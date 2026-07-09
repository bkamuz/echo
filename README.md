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

Результат Windows: `dist/win-x64/Echo.App.exe` + папка `directml/` (если есть GPU DLL).

### Windows portable (zip)

```powershell
./build/portable.ps1
```

Собирает publish и кладёт `dist/releases/Echo-<версия>-win-x64-portable.zip` — `Echo.App.exe` и папка `directml/` (GPU).

## GitHub Releases

При пуше тега `v*` (например `v1.0.0`) workflow `.github/workflows/release.yml` собирает portable-архивы для **win-x64**, **linux-x64**, **osx-arm64** и прикрепляет их к релизу.

```bash
git tag v1.0.0
git push origin v1.0.0
```

Ручной запуск: Actions → Release → Run workflow.

| Платформа | Архив | Запуск |
|-----------|-------|--------|
| Windows | `.zip` | `Echo.App.exe` + папка `directml/` (GPU) |
| Linux | `.tar.gz` | `./Echo.App` |
| macOS (Apple Silicon) | `.tar.gz` | `./Echo.App` |

Windows-релиз включает **GPU (DirectML)** runtime в zip. CI восстанавливает DLL из [Actions cache](.github/workflows/release.yml); при первом промахе собирает Sherpa автоматически. Принудительное обновление кэша: Actions → **Cache DirectML runtime** → Run workflow. Подробнее: [`docs/gpu-directml.md`](docs/gpu-directml.md).

### Локальная сборка portable

```powershell
./build/portable.ps1
```

```bash
chmod +x build/publish.sh build/portable.sh
./build/portable.sh linux-x64
./build/portable.sh osx-arm64
```

| ОС | Скрипт | Архив |
|----|--------|-------|
| Windows | `portable.ps1` | `.zip` |
| Linux | `portable.sh linux-x64` | `.tar.gz` |
| macOS | `portable.sh osx-arm64` | `.tar.gz` |

## Установщик Windows

Нужен [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`winget install JRSoftware.InnoSetup`).

```powershell
./build/installer.ps1
```

Собирает publish и кладёт `dist/installer/Echo-Setup-<версия>.exe` (включая `directml/` для GPU). Модели и настройки в `%APPDATA%\Echo` установщик не трогает.

## Структура

- `src/Echo.Core` — конфиг, история, оркестрация
- `src/Echo.Engines` — Whisper.net и GigaAM (Sherpa-ONNX)
- `src/Echo.Platform.*` — аудио, хоткей, ввод текста
- `src/Echo.App` — Avalonia UI

Данные пользователя: `%APPDATA%\Echo` (Windows), `~/Library/Application Support/Echo` (macOS), `~/.config/echo` (Linux).
