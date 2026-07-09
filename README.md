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

**Быстрый релиз в Cursor:** `/new-release` → выбрать patch / minor / major → скрипт обновит версию, создаст тег и запушит.

Локально:

```powershell
pwsh ./scripts/new-release.ps1 -Bump patch
```

При пуше тега `v*` workflow [`.github/workflows/release.yml`](.github/workflows/release.yml) собирает portable-архивы на **self-hosted runner `aeza-personal`** (Linux VPS, win/linux/mac с одной машины) и прикрепляет их к релизу.

Ручной запуск: Actions → Release → Run workflow (GitHub Release создаётся только при push тега `v*`).

Перезапуск неудавшегося релиза: Actions → Release → выбрать run для тега → **Re-run all jobs** (тег пересоздавать не нужно).

| Платформа | Архив | Запуск |
|-----------|-------|--------|
| Windows | `.zip` | `Echo.App.exe` + папка `directml/` (GPU) |
| Linux | `.tar.gz` | `./Echo.App` |
| macOS (Apple Silicon) | `.tar.gz` | `./Echo.App` |

Windows-релиз включает **GPU (DirectML)** runtime в zip. CI **восстанавливает DLL из Actions cache** (без сборки Sherpa на сервере). Кэш заполняется workflow [**Seed DirectML cache**](.github/workflows/seed-directml-cache.yml) из maintainer-release `directml-runtime-<версия>`. Пересборка Sherpa — только через [**Cache DirectML runtime**](.github/workflows/cache-directml.yml) на `windows-latest` при смене версии. Подробнее: [`docs/gpu-directml.md`](docs/gpu-directml.md).

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
