# Changelog

## Unreleased

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
