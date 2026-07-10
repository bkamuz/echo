# Echo

Кроссплатформенное приложение диктовки с локальным распознаванием речи.

## Статус платформ

Сводка по глобальному хоткею, записи звука, распознаванию и автовставке текста.

### Проверено

| Среда | Заметки |
|-------|---------|
| **Windows 10/11 (x64)** | Полный цикл диктовки: хоткей (Win32 hook), запись (WASAPI), автовставка (буфер обмена или SendInput). Опционально GPU через DirectML. |
| **Ubuntu, GNOME на Wayland** | Полный цикл диктовки: хоткей (evdev / hotkey-bridge), запись (PipeWire / ALSA), автовставка через буфер Avalonia + ydotool (Ctrl+V). Панель GNOME не мигает. Нужны `ydotool` / `ydotoold` и доступ к `/dev/input` (группа `input`). |

### Реализовано, но не проверено

| Среда | Хоткей | Автовставка | Зависимости |
|-------|--------|-------------|-------------|
| **Linux, X11** (любой DE) | evdev / hotkey-bridge | `xdotool` (Ctrl+V) или AT-SPI | `xclip` / `xsel`, `xdotool`; для AT-SPI — `python3-gi` и включённый accessibility |
| **Linux, wlroots Wayland** (Sway, Hyprland, …) | evdev / hotkey-bridge | `wtype` (Ctrl+V) | `wl-clipboard`, `wtype` |
| **Linux, KDE Plasma Wayland** | evdev / hotkey-bridge | AT-SPI или только копирование в буфер (нет ydotool / wtype) | AT-SPI в настройках KDE |
| **Linux, KDE / GNOME на X11** | evdev / hotkey-bridge | `xdotool` или AT-SPI | как для X11 |
| **Linux, Flatpak** | с ограничениями sandbox | зависит от установленных утилит внутри песочницы | автоустановка системных пакетов недоступна |

На **GNOME Wayland** AT-SPI намеренно отключён: Mutter не отдаёт сфокусированный виджет без включения специальных возможностей.

Цепочка автовставки на Linux: AT-SPI → ydotool (только GNOME Wayland) → xdotool (X11) → wtype (wlroots Wayland) → запасной режим «только буфер».

### Ещё не сделано

| Среда | Что отсутствует |
|-------|-----------------|
| **macOS (Apple Silicon)** | Заглушки: AVFoundation (микрофон), CGEventTap (хоткей), Accessibility API (фокус и вставка). UI и движки собираются, полный цикл диктовки не работает. |
| **macOS (Intel)** | Не в матрице CI / релизов |
| **Windows ARM** | Не в матрице сборки |
| **Linux без X11 / Wayland** | Сессия не определяется — хоткей и вставка недоступны |

Если проверили среду из второй таблицы — откройте issue или PR с кратким описанием дистрибутива, DE и рабочих зависимостей.

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
| Linux | `.deb` | `sudo apt install ./Echo-*-linux-x64.deb` → «Echo» в меню |
| Linux | `.AppImage` | `chmod +x Echo-*.AppImage && ./Echo-*.AppImage` |
| Linux | `.flatpak` | `flatpak install --user ./Echo-*.flatpak` |
| macOS (Apple Silicon) | `.tar.gz` | `./Echo.App` |

Windows-релиз включает **GPU (DirectML)** runtime в zip. CI **восстанавливает DLL из Actions cache** (без сборки Sherpa на сервере). Кэш заполняется workflow [**Seed DirectML cache**](.github/workflows/seed-directml-cache.yml) из maintainer-release `directml-runtime-<версия>`. Пересборка Sherpa — только через [**Cache DirectML runtime**](.github/workflows/cache-directml.yml) на `windows-latest` при смене версии. Подробнее: [`docs/gpu-directml.md`](docs/gpu-directml.md).

### Локальная сборка portable

```powershell
./build/portable.ps1
```

```bash
chmod +x build/publish.sh build/portable.sh build/linux-packages.sh build/linux/*.sh
./build/portable.sh linux-x64
./build/linux-packages.sh
./build/portable.sh osx-arm64
```

| ОС | Скрипт | Артефакты |
|----|--------|-----------|
| Windows | `portable.ps1` | `.zip` |
| Linux | `portable.sh linux-x64` | `.tar.gz` |
| Linux | `linux-packages.sh` | `.deb`, `.AppImage`, `.flatpak` (если установлены `dpkg-deb` / `flatpak-builder`) |
| macOS | `portable.sh osx-arm64` | `.tar.gz` |

На Linux portable-архив и пакеты требуют `chmod +x Echo.App` только если права потерялись при копировании; скрипты сборки выставляют `+x` автоматически.

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
