# Replacement dictionary

Stage 1 (shipped): user-managed "as recognized → should be" pairs, stored in
`<user data dir>/dictionary.json` (separate from `config.json`). Applied by
`DictationCoordinator` right after transcription for every engine and before history/injection.

Matching (`DictionaryMatcher`): whole-word (Unicode letter/digit boundaries, Cyrillic + Latin),
case-insensitive, multi-word phrases with flexible whitespace, longest phrase first; a capitalized
match capitalizes the replacement, ALL CAPS stays ALL CAPS. Per-entry and global enable flags,
usage counter + last-used timestamp, JSON import/export (merge by normalized phrase).

## Stage 2 hook: learning from user corrections (not implemented)

- After injection the coordinator knows the injected text and the target HWND. A future
  `ICorrectionObserver` (Windows: UI Automation `TextPattern`/`ValuePattern`) can re-read the
  field for a few seconds and diff it against what was inserted (word-level LCS).
- Each changed span becomes a candidate `DictionaryEntry` with `source = "auto"` and
  `enabled = false`, shown in the Dictionary page as a suggestion the user confirms in one click.
- `DictionaryStore.Update` is the single write path, so the observer only needs to call it.
