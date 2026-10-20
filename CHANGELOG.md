# Changelog

## Unreleased — major-version preparation

- Windows read-aloud tint now centers on the visible text glyphs rather than the line box, matching Chinese definitions, English definitions, serif italic examples and wrapped paragraphs. Keep text position, highlight height and the speaker hover path unchanged; this is a WPF-only correction.

- macOS light mode no longer turns grey over a dark desktop. Light glass now carries a near-white wash, so it reads as frosted white. The results sit on white groups instead of grey ones, and the TL;DR card is a clearer pale pink. The query capsule has a hairline edge, so it stays visible on near-white glass. Dark mode is unchanged.
- Windows lookup workflow parity: summoning the window (including taskbar Win+2) consumes a fresh external copy once; optional selection reading uses UI Automation with surrounding text for Core's sentence-specific TL;DR. Native foreground/clipboard notifications carry only handles and sequence numbers. Actual reads run on summon in an isolated, time-limited helper, never synthesize Copy, and are cancelled when typing, clicking or leaving the window. Long text fills the field without submitting. Own copies and stale captures are ignored. Settings → 窗口 controls both options.
- Windows keyboard results: ↓ / Tab enters senses after suggestions, ↑ ↓ navigates, Space reads/stops a sense, Enter toggles examples, Ctrl+C copies the sense, Esc returns to the query. Ctrl+D reads/stops the headword and Ctrl+Shift+C copies the TL;DR or translation. Core's 本句 marker is rendered; keyboard focus follows a sense when merging moves its wording into alternatives.
- Complete a contextual lookup only after both merging and sentence judgment end. The active row includes sentence judgment, and total elapsed time includes it for live queries and restored entries. A failed sentence pick remains explicit and retries on its own, without regenerating definitions or merging again. Both Windows and macOS receive this shared fix. No release generated.

- Keep the TL;DR to one wording while senses merge. Each model marks its own core meaning, so after both answered the TL;DR used to list the same meaning twice ("末日刷屏：沉迷浏览负面消息；末日刷屏：沉迷刷…") until the judge finished. Until the merge ranks meanings, the TL;DR now follows the first model to complete and does not change when the other finishes; with merging off or failed it stays with that model.
- Reach a useful result sooner, measured rather than guessed.
  - **TL;DR preview.** Until any model completes, a streamed sense that the model has finished and marked as the core meaning leads the TL;DR as a 预览. With Codex and Claude in parallel, the TL;DR of a new word appeared at about 5.6–5.9 s instead of 10.5–10.9 s. Local excerpts and completed answers still take precedence.
  - **Earlier 本句语境.** The sentence's meaning is now chosen once every model has answered, alongside the sense merge instead of after it (about 20.6 s instead of about 28 s in a real lookup). The pick names a completed model sense, so it survives the merge and is reused when the page is restored. A picked sense that the merge folds into another marks that group.
  - **Timing log.** Every query logs its stage times locally (`diagnostics/lookup-timings-v1.jsonl`): local, source, first sense, TL;DR, each model, merge, sentence meaning and total. The log holds no query, sentence or answer, and `Lexi.Tools timing-report` summarises it. The log and the saved sentence picks now count, and clear, as intermediate caches.
- Show a selected word's meaning in its own sentence. When ⌘2 on macOS reads a selection (Accessibility), it also reads about 300 characters around it, and Core cuts out the sentence (`LookupContext`). Once the senses are final, the sense judge picks the one the word has there and writes a short Chinese gloss, cached per sentence. The TL;DR then reads “TL;DR · 本句语境” with that gloss, the matching sense carries a pink 本句 tag, and every other sense stays. The entry and its cache never depend on the sentence; offline, with automatic supplementation off or after a failed pick, the general TL;DR stays. Host: `query.candidate` accepts `surrounding`/`start` and returns `context`; `query.submit` accepts `context`.
- Give the macOS app an Icon Composer icon (`apple/Resources/AppIcon.icon`): a black fill with the ξ from `docs/brandmark.svg` as a glass layer. macOS now draws the rounded shape, glass and shadow itself, so the corners match every other app instead of the brandmark's own rounded plate. Windows, the tray and the README keep the brandmark with its pink outline.
- Look up without typing on macOS: ⌘2 in another app takes the text copied there in the last few seconds, or, once allowed in 设置 → 通用, the selection itself (Accessibility permission). Core's new `query.candidate` decides: words and short phrases are looked up at once with stray quotes and punctuation removed, longer text only fills the query field, and links or paths are ignored. Only the pasteboard's change count is polled; its contents are read once, at ⌘2, and LeXi's own copies never count.
- Use results from the keyboard on macOS: ↓ or Tab in the query field moves into the senses, ↑ ↓ move between them, Space reads the focused sense, Return opens its examples, ⌘C copies it and Escape returns to the query. ⌘D reads the headword and ⇧⌘C copies the TL;DR or translation. The query window no longer sets a default focus, which had pulled focus back to the query field.

- Keep TL;DR first beneath the headword on Windows and macOS, ahead of word forms and one compact model-status row. Core publishes independent actor states; animate waiting, output and merging without repeating stage text or a query-field indicator. Successful live work retires with the approved CRT shutdown (vertical squeeze, bright line, central phosphor dot), then releases its space. Cached results skip the flourish; partial failures and cancellation retain accessible details and failed-step retry. Respect motion preferences and discard stale animation completions. No release generated.

- Replace the query-row loading spinner with the same soft-matrix activity used in the footer on Windows and macOS. Share stage changes and motion preferences, keep the query-row mark unframed, and retain terminal check/warning symbols, keyboard/click stage details and stable control alignment.

- Animate the footer with the approved soft matrix on Windows and macOS: a scanning dot grid while waiting, short unfolding lines when new definitions or translation arrive, and converging dots while merging. Restore a still ξ at every terminal state and preserve Windows' history action. Use native animation, retain the current phase across token updates, and stop motion when hidden, minimized/backgrounded or Reduce Motion is enabled. macOS uses its native system color and unframed mark. No release generated.
- Finish the major update on macOS. 再次播放上一段 joins 朗读 in the read-aloud context menu, as on Windows, instead of a bottom-bar button that stayed after leaving the page. History search is a small field in the 最近查询 row rather than a second search box under the query capsule, and a search without matches says 没有匹配的历史. Cache sizes read “0 KB” rather than “Zero KB”, and the OAuth / API buttons in 查询与模型 open the sidebar pages instead of duplicate sheets. A history refresh no longer discards its reply because a duplicate refresh, such as the one a home view starts, is still in flight; only replies older than the list on screen are dropped.
- Keep Core's own wording for backup and cache errors in the host, such as a backup over the size limit; a damaged archive and an unreadable or unwritable file get fixed sentences without paths. Saved retry checkpoints keep a hash of the query settings instead of a full settings copy, so results and backups no longer carry unrelated settings.
- Redesign settings with category navigation on Windows and native sidebar/grouped forms on macOS. OAuth, API, speech and data management have their own pages. Settings windows resize and remember their own size/position, independently of query windows.
- Limit initial online-source waiting to one second before starting generation, retaining late original text without creating retrospective citations. Completed saved results still require explicit regeneration to refresh.
- Add “重试未完成” to retry failed model/source/merge stages while preserving successful provider results and their evidence. Translation retries only its failed generation stage; settings changes require explicit regeneration.
- Keep up to 10,000 explicit history entries with repeat counts, searchable/paged browsing and history-aware input suggestions. Translation paragraphs remain private local data and are excluded from word suggestions.
- Add explicit ElevenLabs TTS model discovery, capability filtering, persisted model catalogs and per-model voice preferences. Replay the previous reading with the same selected voice and cached audio; retain the verified Windows speech-start buffer.
- Add selective backup/import with a preview, retaining existing queries by default. Exclude credentials, OAuth state, audio, installed dictionaries and window placement; machine client paths stay local. Add separate intermediate, speech and completed-query cache management.
- Add a 24-case lexical quality corpus and offline scoring harness; reorganize prompt priorities around established Chinese names, central meanings, native fields and provenance. No additional paid model call or completed-cache invalidation. Online A/B quality results have not been collected.
- Remove the personal default API endpoint. Prepare a clean self-contained portable ZIP process with public-file staging, symbol/state rejection, sensitive-pattern checks, extracted-ZIP verification and SHA-256. No release or distributable ZIP generated for this change.


- On macOS, keep the headword's speaker and etymology buttons in place when playback starts. The waveform now covers the speaker instead of replacing it; a shape has no text baseline, so the swap had lifted both buttons above the headword's baseline and shifted them left. The typing preview card lines up with the query capsule and the suggestion list.
- Summon LeXi on macOS with ⌘2 from any app, the counterpart of Win+2: it brings the query window forward with the last query selected, or hides LeXi when its window is already in front, returning to the previous app. It is a system hot key that takes precedence over other apps' ⌘2 and needs no Accessibility permission. Settings → 通用 can turn it off and set LeXi to open at login. Closing the query window no longer quits LeXi.
- Read definitions and examples aloud on Windows and macOS. Pointing at a Chinese or English definition, an example or its translation shows a speaker in the card's left margin, which becomes a stop button while that block plays; the block is tinted pale gold while it is read. The context menu starts with 朗读, or 朗读所选文字 to read only the selected words. Speech uses the voice chosen in settings with the same cache and limits as the headword, and each speaker now stops only its own item. On macOS these blocks are AppKit text views so 朗读 joins the system text menu; VoiceOver offers it as an action.
- Rewrite README.md as a product overview covering both platforms: lookup and translation, offline dictionaries, AI definitions and merging, subscriptions, speech, saved results, navigation and privacy.
- On macOS, show each model's effort and speed as one “思考深度 · 速度” row of two menus directly under that model, with the capability note as its subtitle, so the primary, second and judge settings no longer repeat two look-alike rows each. A menu with nothing to choose is dimmed and says why on hover, as the Windows tooltips do. Effort choices read Low, Medium, High, Extra High, Max and Ultra on both platforms, written like the speed labels; the values sent to the service are unchanged. The API connection form keeps its opening guidance when it fills in the saved address, and Chinese Wiktionary examples stay upright, as on Windows.
- Read the OAuth model catalog from the official Codex and Claude clients once in the background after each start while online, on Windows and macOS. Every subscription model is then offered without opening the OAuth connection first; before, a new install listed only the saved model until that page was opened. Failures keep the cached catalog and saved choices, and nothing is selected for the user. The macOS 查询 settings wait for that read through the new host method `inference.catalog`, so a window opened right after launch still fills in.

- Add LeXi for macOS (`apple/`): a SwiftUI app for macOS 26+ that bundles the self-contained `Lexi.Host` and renders its views. It supports lookup, translation, suggestions, history, back/forward navigation, regeneration, the online switch, settings with the shared validation, dictionary management, official-client status and sign-in in Terminal, and system-voice speech. Liquid Glass is limited to the toolbar, search bar, footer controls and popovers; results use the normal background with pink as an accent. Keys are left to the input method while it composes text.
- Give the macOS app Apple's design language: a floating Liquid Glass query capsule, toolbar glass for navigation, history and settings, and grouped glass controls for regeneration and the online mode, with results scrolling beneath on grouped surfaces. Colors follow the system and the user's accent; pink appears only as the TL;DR mark. Settings use System Settings-style grouped forms with account status indicators; a General tab chooses system, light or dark appearance and lists the shortcuts. Each window is a single pane of Liquid Glass over the desktop, as in Control Center: light or dark glass that follows the appearance setting, takes on the wallpaper's color and becomes more opaque with Reduce Transparency. Settings controls sit centered beside their two-line labels. Parts of speech use the Windows pale gold (a deeper gold on light glass), and the etymology button shows the same Rosetta Stone as on Windows.
- Make the macOS query field ready the moment the window comes forward: it takes focus with the last query selected, and ⌘L does the same, as on Windows. Suggestions now hang below the query bar over the results instead of covering it.
- Add `apple/scripts/build-app.sh` to assemble `LeXi.app` with the host, model schemas, an icon generated from the brand mark and the license notices, and Swift tests for protocol decoding, view state, the real host and result layout. The macOS CI job runs them and builds the app.
- Add speech parity to LeXi for macOS: the 发音 settings tab offers system voices with an automatic choice, OpenAI, Grok and ElevenLabs models and voices, ElevenLabs account voice loading and a manual Voice ID, and a preview. Keys are saved in the current user's Keychain for LeXi only and passed to the host with each request that needs them. Saving replaces the Keychain item, so a rebuilt app that the old item no longer trusts can save the key again, and a refused read says so in the status bar. `LEXI_SIGN_IDENTITY` signs `build-app.sh` builds with a fixed identity so saved keys keep working across rebuilds. Online audio plays from the shared cache with `AVAudioPlayer`; generation, playback and errors appear in the status bar as on Windows.
- Move online speech generation and its audio cache into `Lexi.Core` (`SpeechAudio`): per text, provider, model and voice files written to a temporary file first, the offline rule and lazy key reading. The WPF window uses it and keeps SAPI generation and current-user key encryption. Unreachable speech services now report “无法连接语音服务，请检查网络后重试。” (ElevenLabs keeps its own wording) on both platforms instead of the system's network message.
- Add `speech.models`, `speech.voices`, `speech.elevenVoices`, `speech.cached` and `speech.synthesize` to `Lexi.Host`, with the `speech_failed` error code and speech validation in `settings.update`; the protocol version is unchanged. The host never keeps, logs or returns keys. `speech-check` and `host-check` cover the shared cache and the new methods with synthetic services.
- Local speech is identified per platform (`SpeechCatalog.Local`): `windows`/`sapi` on Windows, `macos`/`avspeech` on macOS. Settings saved with the other platform's local voice keep local speech with the automatic choice.

- Add `Lexi.Host`, a JSON-lines host for native front ends. It exposes query, translation, suggestions, history, settings, dictionaries and official-client status, streams every `QueryView` in order, supports cancelling individual requests, and keeps stdout to protocol messages only. It publishes as a self-contained single file with model schemas beside it. See docs/host-protocol.md.
- Share model choices, the “two different models” rule and the fixed Codex options through `ModelCatalog`, used by the Windows settings dialog and the host.
- Add `host-check`, covering streaming, cancellation, errors and settings validation in process, and the host executable's stdout and exit behavior; the macOS CI job also checks the published single-file host.

- Move the query lifecycle into `Lexi.Core`. `QuerySession` now performs routing, source and cache reads, model streaming, merging, saved-view restoration, translation, history and navigation, and publishes a complete `QueryView` at each step: progress stage, status text, sense rows with provenance and citations, sources, word forms, pronunciation, etymology, TL;DR and translation. The WPF window only renders these views, keeping its existing disclosure, scroll and source-expansion behavior. Settings changes mark the kept result before cancelling, so the order no longer depends on the UI thread.
- Derive simplified/traditional search keys from embedded OpenCC character tables on every platform instead of Windows `LCMapStringEx`. Older traditional-only indexes are searched with a bounded set of candidate spellings (for example 车钟 → 車鐘). Displayed dictionary text is unchanged.
- Find Codex and Claude Code without relying on the login shell PATH: besides PATH, check `~/.local/bin`, Homebrew, `/usr/local/bin`, npm global prefixes and Node version managers; run npm entry scripts through the matching `node` and pass the found folders to the client. Without a command-line install, use the Codex CLI bundled with the ChatGPT desktop app and the newest verified macOS Claude Code kept by the Claude desktop app (never its Linux sandbox build). Windows discovery is unchanged. The missing-Codex message no longer assumes `codex.exe`.
- Choose data folders per platform: Windows keeps the portable `userdata`/`data` folders; macOS uses `~/Library/Application Support/LeXi`.
- Add `session-check` and `platform-check`, a cross-platform `Lexi.Shared.slnf`, and a macOS CI job. `scripts/Test.ps1` runs the shared checks on macOS and Linux and the WPF checks on Windows.
- Add [FEATURES.md](FEATURES.md) for Windows/macOS parity, an affected-platforms section in the PR template, and split AGENTS.md into shared behavior and per-platform interface rules.

## 0.9.26 (Windows / macOS)

- Show an input TL;DR after a 300 ms typing pause, before a full query. Shared Core reads case-preserving completed saved results first, then enabled local dictionary matches, identifying the actual headword for case/inflection differences. It never calls a provider, writes history, changes navigation or replaces the current result. Windows and macOS place the compact two-line card below the input and above suggestions; arrows/acceptance update it, composition waits for commitment, and dismissal/submission/cancellation reject stale replies. Click or Enter opens the full query. Host adds the optional `query.preview` method without changing existing messages.
- Replace Windows' ineffective separate-stream speech primer with a single padded PCM playback stream. The built-in Media Foundation decoder creates a temporary WAV containing 600 ms of silence followed by all decoded audio; playback proceeds continuously through the lead-in to the original first sample. Preparation runs off the UI thread, cancellation cleans temporary files/output, and cached provider audio is never modified. macOS keeps its native AVSpeechSynthesizer/AVAudioPlayer output. This corrects the stream-start weakness; audible behavior on a particular speaker still requires listening on that device.

## 0.9.25 (Windows / macOS)

- Replace the speech playback square with the approved soft five-bar waveform on headwords, definitions, examples and the settings preview. Keep existing hit targets, hover behavior, optical alignment and click-to-stop. Preparation uses a static waveform; actual playback starts the independent rounded-bar rhythm. Native animation pauses for reduced motion, hidden/unloaded content or minimized/background windows and releases its clocks on removal. Use the shared pink for the headword on dark surfaces and the existing gold for read-aloud blocks; macOS keeps a contrasting native foreground on light glass. Speech providers, audio warm-up, caches and keys are unchanged.

## 0.9.24 (Windows / macOS)

- Reduce sense-judge output without changing the selected models, effort/speed preferences or the conservative grouping rules. Judges still review every requested pair and select the overview, but return only positive links at confidence >= 0.95 plus the reviewed pair count. Core validates identities/counts and expands omitted pairs to uncertain, so missing links cannot merge senses or create a transitive bridge. Fully enumerated plans remain readable; new judgments use versioned comparison caches. Windows and macOS use the same prompt, schema and decoder through Core/Host; completed saved query views remain unchanged.

## 0.9.23 (Windows / macOS)

- Keep generation and model-preview processing off the desktop UI synchronization context. Sample Claude and compatible-API JSON previews at most once per 80 ms and skip unchanged content; final responses still receive complete validation. Windows coalesces pending views at background dispatcher priority so input, scrolling and cancellation can run during token bursts, and stale revisions cannot overwrite the latest result. The same preview sampling applies to macOS through Core/Host.
- Replace the fixed four-entry OAuth starter catalog with official-client discovery and persisted model capabilities. Opening the OAuth connection explicitly refreshes available models and their independent effort/speed options on both platforms; Codex discovery requests all pages including hidden models, labels hidden entries and explains that invocation remains account-dependent. Keep previously selected models and cached metadata available offline or after a failed refresh. Never substitute API model IDs for OAuth models or switch the user's selections.

## 0.9.22 (Windows / macOS)

- Place compact OAuth and API connection actions together above the query model selectors, with direct access to each connection form. Prefix all shared model choices with `OAuth |` or `API |` while preserving their underlying provider/model identities and saved preferences.
- Make unavailable model controls visibly disabled, including WPF dropdown content and arrows, with dimmed inference labels and explanations available on disabled controls. macOS uses the native disabled picker state.

## 0.9.21 (Windows / macOS)

- Separate model identity from inference preferences. Label subscription choices as OAuth and direct endpoint choices as custom API; show effort and speed beneath each model using the same shared capability rules. Save explicit preferences per provider/model and, for APIs, per endpoint. The same model selected in multiple roles shares its settings.
- Read effort levels and service tiers from authenticated API model metadata and the official Codex model-list and Claude initialization interfaces, retaining public capability metadata locally for reopening. Offer defaults for unadvertised capabilities; offer Fast or faster tiers only when advertised. Preserve an unsupported saved choice visibly until the user changes it.
- Send selected `reasoning_effort` and `service_tier` in JSON and SSE Chat Completions requests, apply Codex/Claude CLI options without global configuration changes, and include preferences in prepared-worker and generation-cache identities. Remove forced Low/Fast and disabled-thinking settings; keep legacy Codex preferences and complete saved results. Reject unavailable options without silently downgrading or retrying.

## 0.9.20 (Windows / macOS)

- Add a direct OpenAI-compatible text service for dictionary lookup, translation and optional sense judging. Configure its HTTPS Base URL, enter CF Access Client ID / Client Secret and an optional API key, fetch the authenticated server model list, then explicitly choose a model. Keep existing subscriptions and selections until the user changes them.
- Send Access headers and a versioned LeXi User-Agent on every models/completions request. Support ordinary JSON and incremental Chat Completions SSE, cancel the active HTTP request/response on stop, and report authentication or incomplete-output failures without anonymous retries, redirects or provider fallbacks.
- Protect endpoint-bound credentials with Windows current-user encryption or the macOS Keychain; keep them out of settings, logs, results and repository data. Scope model caches by endpoint, preserve complete saved views, and keep TTS independent.
- Add synthetic transport, cancellation, cache, Windows secure-storage/settings, Host protocol and Swift credential checks. iOS integration remains outside this desktop update.

## 0.9.19 (Windows)

- Prime the default audio output with 350 ms of played silence before starting each synthesized clip, keeping the silent stream open across the handoff. Wait for media readiness and start speech at position zero, so output startup does not consume the first syllable. Apply this to words, definitions, examples, selected text and settings previews, including cache hits.
- Keep original audio caches intact, avoid extra synthesis requests, and close both streams on stop, replacement, failure or completion. No idle keep-alive, system audio changes or new dependencies. macOS retains its native playback.

## 0.9.18 (Windows)

- Make each read-aloud row include its speaker gutter and disclosure indentation in one transparent hit area, so moving from the text to the speaker cannot hide it in the gap. Preserve the existing text positions and speaker column.
- Center speakers on the actual first line's letterforms after layout, including English italics, Chinese fonts, wrapping and display scaling. Reposition the icon without changing row spacing.

## 0.9.17 (Windows)

- Build and package per-definition and per-example speech for Windows: reveal the speaker on hover, offer whole-block or selected-text reading in the context menu, and keep stop/highlight state attached to the item being read. Use the configured provider and existing audio cache.
- Keep English examples in italic serif type and Chinese example translations in upright serif type, matching the macOS implementation.

## 0.9.16

- Focus the search input immediately on startup, taskbar activation and explicit restore, with an input-priority retry after WPF layout and focus restoration. Declare the input as the window's logical focus target and align native keyboard focus only while the main window is already foreground and enabled.
- Select existing text for replacement on activation without reselecting text typed before a deferred retry. Cancel pending focus work on mouse interaction, deactivation, minimization and close; keep background windows and modal settings from redirecting keyboard input.

## 0.9.15

- Read ElevenLabs account voices on the first dropdown expansion, reuse that list within the settings dialog, and provide an explicit “刷新声线列表” action. Keep loading and connection errors beside the voice selector; invalidate results on key edits, reject stale responses, and preserve saved/custom choices without inventing voices for empty accounts.
- Explain Secret Key versus API Key ID beside the masked input and report the provider's `api_key_id_used_as_api_key` error for both voice discovery and synthesis. Classify known service codes without displaying raw provider messages, credentials or account details.

## 0.9.14

- Add ElevenLabs speech using its official API, explicit model/voice selection, on-demand paginated account voice loading and an optional manual Voice ID. Keep saved voice names, provide the API-key dashboard link, mask and encrypt keys locally, and preserve provider/model/voice audio cache separation.
- Keep Windows local SAPI selected on upgrade. Clarify provider access and credits in speech settings, show only the selected provider's key panel, and sanitize ElevenLabs authentication, quota and voice errors. Voice lookup and synthesis happen only on explicit clicks; no account credentials are imported.

## 0.9.13

- Move the quick-read text up by 2 DIP to center its visible letterforms beside the TL/DR badge. Preserve the existing 19-DIP type, 28-DIP line spacing, selectable text, badge size and equal horizontal gaps; the correction applies to single-line and wrapped summaries.

## 0.9.12

- Reduce the TL/DR badge from 28 to 20 DIP to sit closer to the summary's 19-DIP type size, with proportionally smaller vector letterforms. Account for the text box's internal leading inset so the visible gaps on either side of the badge are both 12 DIP; keep vertical centering and the existing summary text size.

## 0.9.11

- Keep the TL/DR mark consistent for local excerpts, model previews and final summaries. Replace font-rendered labels with a compact two-by-two vector badge using the shared pale pink and dark letterforms; center it vertically beside single-line and wrapped summary text. Keep provisional and partial-result status separate, with source/core-selection explanations in the tooltip and accessible caption.

## 0.9.10

- Preview the selected search suggestion in the input while keeping the caret at its end. Up/down browse the original candidates without launching a lookup; typing immediately edits the selected word. Clicking or pressing Tab accepts a suggestion for editing, Enter searches, and Escape restores the input from before browsing. Keep suggestion previews out of query history and saved results.

## 0.9.9

- Increase the title's size and use the main text color for clearer contrast on acrylic.
- Put the quick-read guide beside the first line of the summary: a stacked TL/DR mark for selected core meanings, or an excerpt label for unranked results. Keep provisional/partial state visible inline and move the full explanation into its tooltip and accessible name.
- Preserve safe failure categories for sense merging and show the cause, retained results and retry action in progress details. Mark the failed stage explicitly instead of marking every stage as ended. Legacy failed views remain readable without inventing a cause; no new model calls or automatic cache refresh.

## 0.9.8

- Restore saved window bounds after initial WPF/chrome layout, preserving the startup snapshot until restoration succeeds. Keep early startup geometry from overwriting the saved position.
- Theme the tray menu with the application's dark surface, readable text, rounded hover states and pale-pink selection text.
- Make scrollbars visible on acrylic using neutral-gray grips, with brighter hover/drag states and a wider hit area.
- Keep the loose shell icon in a separate content path so it cannot shadow the embedded WPF icon; validate this during program checks.
- Refresh matching LeXi taskbar shortcuts with a content-addressed packaged icon, avoiding stale executable-icon caches without resetting Explorer or unrelated shortcuts.

## 0.9.7

- Remember main-window size and position automatically, including the normal bounds behind minimization/maximization. Persist separately from preferences and recover safely from missing or invalid state.
- Use a pure-black logo background with a pale-pink rounded-square outline across the application, tray and repository brandmark.
- Treat specialist interests as a coverage checklist, not an assumed context. Preserve general definitions and their native domain; distinguish an industry application from a separate meaning in generation and merging. Remove domain-tag promotion. Existing saved queries stay unchanged until explicit regeneration.

## 0.9.6

- Unify pink accents around the logo's pale pink (#FFADD2). Use the same color for primary buttons and text selection with dark readable text; derive translucent cards and disclosure states from that color. Align the upper-right actions into two consistent columns.
- Persist completed dictionary views, including final grouping, provider attribution, pronunciation, morphology and etymology. Revisit them without network requests or automatic expiry, including after model/lookup settings change. Only explicit regeneration replaces saved content; incomplete streaming is never saved, and failed refreshes retain the previous saved result.
- Give complete translations the same manual-refresh behavior and retain the producing model's identity. Reuse valid legacy caches without automatic age/version refresh, while keeping dictionary and translation identities separate.

## 0.9.5

- Add session back/forward navigation, Alt+Left/Right shortcuts, destination tooltips and scroll restoration. Preserve query case, translation paragraphs and routing, reuse the existing cache pipeline, and keep recent-history deletion separate from the visit trail. A new query after going back clears the forward branch; refreshes do not duplicate visits.
- Separate window controls from lookup controls. A compact top toolbar holds navigation, settings and close, with a continuous labeled drag area; progress and Enter remain alongside the input.
- Make X/Alt+F4 configurable as minimize or exit, defaulting to taskbar minimization for Win+number access. Add a Windows notification-area icon with open, settings and unconditional exit actions; dispose it when the application exits. Esc still minimizes and Ctrl+Q still quits. Keep this integration in the Windows frontend.

## 0.9.4

- Put conventional Chinese translations first in quick-read glosses and full definitions. Supply the equivalent as structured metadata, retain it when the summary budget is tight, and keep the existing core-meaning selection. Refresh old model caches once online while retaining them as offline/initial fallbacks; no additional summary request.
- Replace startup examples with local query history. Keep the 30 most recent word/text submissions, preserve case and translation paragraphs, support one-click requery and individual deletion, and persist history independently of definition caches. Clear the search box or select the footer xi to return to history.

## 0.9.3

- Show first-result latency and total query duration in the footer. Include routing, cached/offline results, model completion, sense judgment and pronunciation; apply the same timing to translation and terminal failures.
- Select core meanings independently of technical coverage and display order. Reuse the existing sense judge to select overview IDs, place those meanings first, and keep every original definition accessible. New model responses identify core senses without an additional request; legacy unranked output is labeled as an excerpt until judged.
- Version comparison caches so existing definition caches receive the improved overview selection without regenerating both models. Keep short summaries focused instead of filling a second slot with a peripheral meaning.

## 0.9.2

- Use a uniformly rounded TL;DR card without a left accent rule. Preserve the existing tint, spacing and lookup behavior.

## 0.9.1

- Add a compact pink-tinted TL;DR above definitions. Reuse short glosses from the same definition request, with complete-text fallbacks for old caches and offline entries; keep preview, partial and final states explicit without another model call.
- Move etymology into a Rosetta-stone-inspired vector control beside the headword. Open a selectable, scrollable popover with source attribution, keyboard dismissal and bounded dimensions; retain content and reader position during model updates.
- Remove the standalone specialist-reference section. Keep uncovered professional meanings as attributed rows in the definition card, and place covered reference summaries inside the corresponding sense's reference disclosure.

## 0.9.0

- Preserve query case across online lookup, model requests and caches. Resolve exact-case and lowercase Wiktionary pages separately; keep abbreviation expansions visible and prevent merging unrelated expansions or attaching lowercase homograph inflections.
- Prioritize established cryptoasset, blockchain, cryptography and security meanings without dropping common usages. Add 12 attributed reference briefs with aliases, available offline, and general specialist coverage in both model requests. Raise the per-model sense budget to six and version definition/merge caches.
- Reject damaged IPA escape sequences, normalize stress and vowel length, and display slash-delimited transcriptions. Read expanded IPA templates from Wiktionary so valid online pronunciations can replace damaged local entries.
- Add a collapsed etymology disclosure with attributed Wiktionary excerpts and concise Chinese model explanations. Source etymology can join the existing definition request; unsourced historical claims are discouraged and model explanation remains identified.
- Use serif italic examples, rename source disclosures to “参考资料”, and give secondary controls persistent surfaces, borders and expanded/focus states. Preserve disclosure state and clear metadata between queries and translations.

## 0.8.2

- Return structured morphology alongside each model's definitions, covering noun/verb/adjective/adverb uses introduced after initial dictionary lookup. Update forms when each model completes; keep partial output out of the display and cache, and preserve complete data during streaming refreshes.
- Fill missing recorded forms with model-supplied morphology while retaining its own provenance. Prefer recorded spellings and the configured primary model when proposals differ; retain other proposals in the form tooltip. Late online dictionary data can take precedence without dropping the remaining model enrichment.
- Keep completed morphology in the model-specific cache for offline use. Preserve legacy definitions as an immediate/offline fallback and refresh them once online to obtain the new fields. Translation and sense-merge model selection are unchanged; morphology adds no separate model request.

## 0.8.1

- Remove redundant word-form and bilingual-definition headings. Use warm English serif italic parts of speech, upright serif word forms and serif examples, with a Song-style Chinese fallback. Align word-form labels and spellings on a shared text baseline and combine identical past/participle spellings.
- Read expanded English headword inflections from Wiktionary alongside pronunciation. Combine online verb forms with offline noun data, retain source attribution and cache forms for offline use without guessing endings or adding model calls. Query counts remain in progress details.
- Update the xi icon and in-app mark to a charcoal-black background with pink lettering.

## 0.8.0

- Rebrand the product and repository as LeXi, with a pink Greek xi (ξ) mark and multi-size application icon. Replace the detailed README with a concise product introduction and move operational details to the usage guide.
- Add a model-account connection page and a shortcut beside the primary model selector. Check local Codex/Claude sign-in status, open their official interactive OAuth login on request, and retain Codex executable configuration. Never read credential files or display raw account output.
- Keep online/offline switch geometry fixed and use equal-length labels.
- Highlight readable Chinese parts of speech, localize useful usage labels and remove neutral boilerplate. Move model provenance details into source disclosures without equating missing dictionary evidence with an incorrect definition; retain all citations and original labels.
- Display recorded word forms next to the headword: verb tenses and participles, noun plurals and adjective comparisons. Resolve inflected queries through their original lemma even when the inflected spelling has its own dictionary entry; preserve alternative spellings and avoid guessed endings.
- Document a single-repository macOS direction with a shared core and platform-specific UI/services. macOS remains future work.

## 0.7.0

- Consolidate query timing and status into a single bottom bar. Give regeneration a persistent button surface and border, and use an accessible two-state online switch with a thumb, clear labels and keyboard focus. Remove the passive dictionary count and redundant footer text.
- Add five official NAER offline terminology datasets: computer science, cross-strait computing translations, electrical engineering, physics and mathematics. Pin source checksums and retain attribution, original wording and simplified lookup aliases.
- Preserve and index both regional translations in cross-strait terminology; label them separately. Add optional dictionary IDs to the installer so existing installations can add selected sources without replacing other dictionaries.
- Verify regional lookup aliases and original text, accessible switch states and compact bottom-bar layout without opening or automating desktop windows.

## 0.6.1

- Unify search-row controls into evenly spaced 32-DIP targets with 18-DIP vector icons and consistent strokes. Remove the isolated Enter-button background and divider.
- Replace the four-dot diamond with a quiet status ring: an indeterminate arc while working, a check on completion and a warning symbol for incomplete results. Preserve full stage details on hover/focus and reduced-motion behavior.
- Increase IPA from 14 to 20 DIP and lay out the headword, phonetics and speech button in a shared inline text flow. Keep the headword and IPA on one baseline, retain wrapping for long terms, and separate the smaller AI provenance label from the phonetic text.

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
