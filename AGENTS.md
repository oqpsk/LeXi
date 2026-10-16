# Lexi maintenance

- Keep the product focused on quick dictionary lookup and user-requested text translation. Windows is the current target; macOS is future work.
- Use this repository as the canonical source. Work in a topic branch after the initial import and record changes in CHANGELOG.md.
- Run scripts/Test.ps1 and scripts/Check-Privacy.ps1 before committing. Live provider checks require the user's authorized local subscription and stay out of CI.
- Do not run computer-use or desktop UI automation tests unless the user explicitly requests them. Report build/program validation separately from observed UI behavior.
- Never commit credentials, OAuth codes, private account information, local absolute paths, userdata, query caches, logs, downloaded clients, binaries, or full dictionary data.
- Delegate authentication to the official provider CLI. Do not read or copy its tokens. Do not alter global provider settings.
- Preserve source citations and uncertainty labels. Partial output must remain visibly provisional and must not enter the cache.
- Keep offline lookup functional without online providers. Preserve user settings during updates.
- Keep dictionary and translation caches separate. Preserve case and paragraph boundaries in translations. Never automatically switch models.
- Route words, phrases and text automatically, including translation direction. Do not add query-mode or target-language selectors to the main interface.
- Retain original dictionary text and attribution. MOE entries must stay in their original form and must not be supplied as AI rewriting evidence.
- Avoid unrelated refactors and new runtime dependencies. Do not add global shortcut hooks, selection translation, launchers, account systems or subscription services.
- Use direct Chinese explanations with the result first. Avoid “不是……而是……”, “本质上”, “换句话说”, “总的来说”, “值得注意的是”, “需要注意的是”, “当然”, “其实”, and repetitive conclusions.
