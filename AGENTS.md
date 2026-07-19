## Learned User Preferences

- Prefers concise, practical replies; most product discussion is in Russian.
- Wants a compact Avalonia UI with little empty space; hover/focus must stay polished (no white dropdown outlines, keep hotkey field borders, dark hover matching nav, no pointer flicker on title-bar edges).
- Settings should auto-apply without a Save button and show progress while a model activates; do not auto-download models on switch—use an explicit download control when a model is missing. UI language is separate from recognition/STT language and should switch live without restart; ComboBoxes and other settings must stay populated after a language change.
- Closing the main window should minimize to the tray; tray context menu should cover open, settings, history, and exit.
- Keep the app lightweight: avoid heavy caret/mouse overlay logic if it costs much complexity or performance.
- Public README must be English product-facing copy, not internal technical notes or private-repo links.
- Use `/new-release` (`scripts/new-release.ps1`) for version bumps and tagged GitHub releases.
- Drive accents through one theme variable (acid/neon green); status-bar errors red, warnings yellow, success via the accent.
- Prefer matching already-working UI patterns over new overlay hacks; remove debug instrumentation after bugfixes. App UI strings should go through Loc keys (`{DynamicResource Loc.*}`) so language switches update labels.
- Hotkeys: capture the chord on key release; use platform-correct modifier names (especially on Linux).
- Settings order: device → engine → model; show the active section title/description in the header.
- Dictation overlay should show listening then processing states without decorative dots or noisy animation.

## Learned Workspace Facts

- Workspace folder is still named Golos; the product is Echo (rebranded from Golos)—a local, offline Avalonia/.NET dictation app (`Echo.slnx`, `src/Echo.App`).
- Recognition engines include GigaAM (Russian default), Omnilingual ASR, and optional Whisper (`-p:IncludeWhisper=true` in Debug/full builds).
- Models live under the user data folder; GigaAM must resolve a full bundle (encoder, decoder, joint, tokens), not encoder-only.
- Windows GPU acceleration uses DirectML; required native DLLs ship inside the portable zip/installer and are refreshed via GitHub Actions cache workflows.
- Releases are built by GitHub Actions from version tags; Windows auto-update reads GitHub Releases and surfaces a header update control (not a settings “Updates” group).
- Tray icons stay dynamic (listen / processing / sleep); dictation also shows a cursor-adjacent icon (no taskbar overlay).
- Default text insertion is clipboard paste; typing mode exists and should restore the previous clipboard when used.
- Core UX: global hold-to-dictate hotkey, history with copy, optional autostart, bottom status bar for readiness/version/errors, fixed-width icon sidebar aligned to the header logo.
- Cross-platform targets include Windows (primary), Linux packaging (deb/AppImage/etc.), and macOS builds; Linux hotkey labels must not show Windows/Command names incorrectly.
- Public GitHub repo `bkamuz/echo` hosts source, English README, Releases, and the Windows update manifest (`latest.json`).
- UI localization uses `LocalizationService` plus ResourceDictionaries under `Resources/i18n` (`ru.axaml` / `en.axaml`); config `ui_language` is `system` | `ru` | `en`; new languages are added by copying a dictionary and registering the code.
- Progress/status protocol uses invariant `DONE:` / `WORKING:` tokens (`ProgressMessages`), mapped to Loc keys for display—not localized mid-protocol strings.
