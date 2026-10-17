# Changelog

## Unreleased

## 0.6.0

- Compare senses within each model as well as across models. Group confident facets of one lexical usage, prefer the most comprehensive original wording, and preserve all original definitions and attribution under expandable details. Keep materially different usages separate and invalidate older merge plans without discarding model results.
- Restore phonetics independently of model definitions: prefer offline pronunciation, fetch and cache English Wiktionary IPA when missing, and label model-only IPA as unverified. Reject late pronunciation updates from canceled queries.
- Add explicitly triggered pronunciation with Windows local voices by default, plus OpenAI and Grok TTS model/voice settings and preview. Encrypt user-entered cloud API keys with Windows current-user protection, cache complete audio by text/model/voice, and support cancellation without switching providers. Cloud TTS requires separate API credentials and billing.
- Move query progress into a four-dot diamond beside the search input. Show dictionary, supplement, merge and completion details on hover or keyboard focus, retain incomplete-result states, and stop animation when results settle.
- Add account-free IPA, TTS protocol, credential-encryption, cancellation, grouping and compact-progress checks. Local voice validation writes a WAV file without playing audio; cloud endpoints use synthetic responses in automated tests.

## 0.5.2

- Use a pink accent and rose text-selection highlight across the main window and settings, retaining readable selected text over Acrylic.
- Add a fixed, compact query-stage strip for dictionary lookup, model supplementation, sense merging and completion. Keep live counts labeled as preview candidates until the entire query finishes; explain that merging can reduce the count.
- Distinguish stable completion from model/source failures and incomplete merging. Stop the loading indicator on every terminal path, retain available definitions, and prevent late callbacks from reviving or overwriting a finished/newer query. Translation has its own generation/completion states, including cache and offline exits.
- Add account-free lifecycle checks for the 2-to-6-to-4 result flow, failure states, stale updates, contrast and minimum-window layout. Keep definition sizes and expandable secondary information unchanged.

## 0.5.1

- Increase dictionary result density while retaining 20-DIP Chinese definitions, 18-DIP English definitions and existing line spacing. Combine part of speech, register and provenance into one wrapping line, and place pronunciation beside the headword.
- Hide examples, detailed citations, alternate wording and model notes behind compact, keyboard-accessible disclosure controls. Keep core definitions, uncertainty, incomplete-output labels and failures visible.
- Preserve each disclosure's open or closed state during streaming and merging, including an open example from the faster model when it moves under alternate wording. Reset details for a new lookup.
- Add non-displaying checks for result height, unchanged typography, selectable examples, complete citations and disclosure state. The five-sense fixture uses about 59% less result height at the default window width.

## 0.5.0

- Query two configurable dictionary models concurrently, showing the first available definition immediately; retain the other model's results when one fails. Keep text translation on the selected primary model.
- Add configurable Codex subscription-based semantic comparison after lookup. Group confidently equivalent senses, keep distinct or uncertain meanings, prefer the primary wording and retain every original variant with its own source labels in expandable sections.
- Separate complete-definition caches by model and evidence, cache validated comparison plans, reject mismatched or incomplete judgments, and preserve all definitions on judge failure or timeout.
- Improve coverage of contemporary Internet meanings and keep slang evidence ahead of older senses when preparing dictionary context. Preserve original offline and online dictionary text in expandable source sections.
- Add account-free concurrency, cancellation, cache and semantic-grouping regression checks, non-displaying result/settings checks, and an opt-in live comparison runner.

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
