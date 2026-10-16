# Changelog

## Unreleased

## 0.4.2

- Suppress DWM's system-accent caption fill on full-client Acrylic windows, including after system theme changes, while retaining resizing and background material.
- Enlarge dictionary definitions, examples, translations, search input and supporting text; give multiline results more line spacing.
- Reduce the dark overlay on Acrylic and result cards, with brighter secondary text and the existing opaque fallback.
- Check native caption/resize flags and the Acrylic-or-opaque invariant using an HWND that is never displayed or activated.

## 0.4.1

- Replace private-use icon-font glyphs and disclosure arrows with vector geometry so global text styles cannot turn controls into missing-glyph boxes.
- Use non-adorner text selection, an explicit contrasting selected-text brush and a translucent highlight for editable and read-only text.
- Allow dragging from non-interactive areas throughout both windows while preserving selection, buttons, menus, tabs and scrollbar interaction.
- Add native Windows 11 Desktop Acrylic behind translucent surfaces, retaining opaque text and a solid fallback when transparency, composition or system support is unavailable.
- Add 15 presentation regression checks for icons, selection contrast, drag exclusions and glass availability without displaying windows or automating the desktop.

## 0.4.0

- Replace the main window and settings with a compact Raycast-inspired dark WPF design, custom window chrome, consistent controls and quieter source styling.
- Remove query-mode and translation-language selectors and the input helper row; automatically route terms, short sentences and paragraphs without switching models.
- Detect the dominant language in mixed Chinese/English text, preserve known dictionary phrases and add automatic-routing regression checks.
- Add Ctrl+, for settings and non-displaying WPF resource construction checks; retain taskbar activation, resizing, text selection and keyboard access.

## 0.3.0

- Add MOE revised Mandarin dictionary, Open English WordNet, CC-CEDICT and NAER maritime/shipbuilding terminology installers, with provenance and license handling.
- Query multiple local dictionaries with per-source settings and simplified/traditional lookup keys; preserve original text.
- Support Chinese Wiktionary entries and pass eligible offline evidence to the selected model; keep modern English slang lookup online.
- Add multiline Chinese/English text translation, automatic/manual mode selection, target language selection and separate case-sensitive translation caching, without automatic model switching.
- Isolate outdated definition caches, avoid caching unknown terms, retry malformed Claude output once with the same model, and add 24 dictionary/translation regression checks.
- Update pinned GitHub Actions to Node.js 24 releases to remove deprecated runtime warnings.

## 0.2.0 — 2026-10-16

- Windows WPF desktop dictionary with offline ECDICT lookup and inflection matching.
- Wiktionary evidence, bilingual AI definitions, examples and provenance labels.
- Claude Haiku streaming output and prepared workers; Codex Low + Fast option.
- Local cache, cancellation handling and independent context for each lookup.
- Installed offline dictionary settings and consistent Microsoft YaHei UI fonts.
- Native taskbar behavior, single-instance activation and keyboard navigation.
- Repository build scripts, offline regression checks, privacy scanning and Windows CI.
