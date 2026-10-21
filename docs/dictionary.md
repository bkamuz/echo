# Replacement dictionary

Stage 1 (shipped): user-managed "as recognized → should be" pairs, stored in
`<user data dir>/dictionary.json` (separate from `config.json`). Applied by
`DictationCoordinator` right after transcription for every engine and before history/injection.

Matching (`DictionaryMatcher`): whole-word (Unicode letter/digit boundaries, Cyrillic + Latin),
case-insensitive, multi-word phrases with flexible whitespace, longest phrase first; a capitalized
match capitalizes the replacement, ALL CAPS stays ALL CAPS. Per-entry and global enable flags,
usage counter + last-used timestamp, JSON import/export (merge by normalized phrase).

## Stage 2: learning from corrections (Windows)

- `DictationCoordinator` calls `ICorrectionObserver.Observe(text)` after a successful auto-paste.
  Windows registers `UiaCorrectionObserver`; other platforms register nothing (UI hidden).
- The observer takes a UI Automation baseline of the focused control (TextPattern document range,
  else Value), locates the inserted text in it and keeps only the text before/after as an in-memory
  anchor (`InjectedSpanTracker`). It polls once per second for `learning.watch_seconds` (10–120,
  default 45) until focus moves, the anchors stop matching (message sent/field cleared) or time is up.
- `CorrectionExtractor` aligns words (LCS, case/ё-insensitive) and keeps 1–3 → 1–3 word
  replacements; case/punctuation-only edits, pure insertions/deletions, rewrites (<50% kept) and
  more than 3 hunks are dropped.
- Results go to `suggestions` (counter + last seen); Accept moves to entries (`source = "auto"`),
  Reject remembers the pair in `rejected`. Nothing is promoted automatically.
- Password fields, excluded apps (`learning.excluded_apps`) and Echo itself are skipped; an app UIA
  cannot read is logged once per process name.
