# Lexi.Host 协议

`lexi-host` 把 `Lexi.Core` 的查询、翻译、建议、历史、设置与在线发音提供给原生前端：macOS 应用启动它作为子进程；iOS 不能启动进程，同一个 `HostServer` 以 NativeAOT 编译进应用内的库（`Lexi.Embed`），按同样的消息逐行收发。前端不重新实现查询逻辑，只渲染 Host 发来的视图。

## 启动

```
lexi-host [--data <词库目录>] [--user <用户数据目录>] [--assets <模型说明与结构目录>]
```

未指定时使用 `LexiPaths`：macOS 为 `~/Library/Application Support/LeXi`（词库在其中的 `data`），资源目录为程序旁的 `assets`。`--version` 输出版本号。参数错误时退出码为 2，用户目录无法创建时为 3。

标准输入和标准输出各传输 UTF-8 JSON，每行一条消息。标准输出只包含协议消息；诊断信息写入标准错误，不包含查询文本、账号或密钥。标准输入关闭或收到 `shutdown` 后，Host 结束正在进行的请求并退出。

## 消息

请求必须带 `id`（数字或字符串）和 `method`，`params` 可省略：

```json
{"id": 7, "method": "query.submit", "params": {"text": "serendipity"}}
```

每个请求恰好得到一条响应，`result` 与 `error` 二选一。多个请求可以并行，响应顺序不保证与请求一致：

```json
{"id": 7, "result": {"request": 3, "revision": 41}}
{"id": 8, "error": {"code": "invalid_settings", "message": "请选择两个不同的查询模型。"}}
```

错误代码：`parse_error`、`invalid_request`、`method_not_found`、`invalid_params`、`invalid_settings` 与 `speech_failed`（`message` 是 Core 的中文提示，可直接显示）、`cancelled`、`internal_error`（只包含错误类别）。无法确定 `id` 的错误使用 `"id": null`。

查询状态通过事件推送，每次 Core 发布新视图时发送一条：

```json
{"event": "view", "data": { "request": 3, "revision": 41, "kind": "dictionary", "...": "..." }}
```

`data` 是完整的 `QueryView`（字段见 `Lexi.Core/QueryViews.cs`），字段名为 camelCase，枚举为 camelCase 字符串。`revision` 在整个会话中递增；前端丢弃不大于已显示 `revision` 的视图。`request` 变化表示新的查询页面。`progress` 为 `null` 表示没有进行中的查询；`progressGeneration` 变化表示新的进度周期。视图中的文字、来源说明、阶段和状态都已由 Core 决定，前端不再推断。`navigation.pageId` 标识当前页面，`backId`、`forwardId` 是后退、前进将到达的页面（没有时为 `-1`）；同一页面在滚动和前进后退中保持同一 id，前端可以按 id 记住各页最后的画面，在滑动返回时画出下层页面。

`progress.activity` 是可选的增量字段，取 `idle`、`querying`、`generating`、`merging`。Core 区分等待模型与收到新释义/译文，缓存文本不冒充新输出；终态与取消均为 `idle`。旧视图缺少字段时原生前端保留静态标志。该字段只驱动动效，不改变 `stage`、状态文字或查询生命周期。

`progress.items` 是可选的增量数组，每项含 `key`、`name`、`state`、`detail`、`activity` 和 `accessibleName`。`state` 取 `pending`、`querying`、`generating`、`merging`、`complete`、`failed`；查询模型与义项判断模型各占一项，翻译只含主要模型。前端用图标表达状态、单行显示名称；详情保留在提示与辅助功能中。成功终态可播放一次 CRT 收束并移除该行；缓存直接恢复时不重播，部分失败与中断保留警示。`items` 不改变协议版本，旧前端可忽略，旧视图可缺省。TL;DR 始终位于状态行和词形变化之前，计时留在底栏。

`progress.restored` 为可选布尔字段，明确标记本轮是否直接恢复完整缓存；为 `true` 时成功终态不播放退场动画，即使前端曾短暂显示等待。缺省为 `false`，不影响旧客户端。

### 本句语境

`query.submit` 带 `context` 时，各查询模型都给出结果后（与同义项合并同时进行），Core 用义项判断模型做一次轻量判断（`context.instructions.txt`、`context.schema.json`）：从各模型已完成的义项中选出查询词在这句话里的意思，并给出一句简短中文。结果按查询、句子与各模型义项缓存于 `context-picks-v1`；合并前后请求相同，恢复已保存页面时直接复用。TL;DR 改为这句话里的意思（`caption` 为 `TL;DR · 本句语境`，提示中含原句与一般释义），对应义项的 `heading.inContext` 为 `true`；该义项合并后成为另一义项的其他表述时，标记显示在该组的义项上。其余义项不变。判断进行中 TL;DR 显示“本句语境判断中”；离线、关闭自动补充、无法判断或判断失败时保持一般 TL;DR。词条本身的生成与缓存不受句子影响；“重新生成”沿用本页句子。

### 首个结果与计时

模型仍在生成、尚无完成的回答时，若某个模型已经写完一条并标为核心含义的义项，TL;DR 先显示它，`quickRead.state` 为 `预览`；本地词库摘录和已完成的回答优先。每次查询结束后，Core 把各阶段耗时（本地、在线来源、首条义项、TL;DR、各模型、合并、本句语境、总耗时）追加到用户目录的 `diagnostics/lookup-timings-v1.jsonl`，只记日期、查询路径、模型名与毫秒数，不记查询词、句子或释义；保留最近 1000 条，随“中间缓存”一同统计和清除。`Lexi.Tools timing-report <用户目录>` 输出各路径的中位数与 90 分位。

## 方法

| 方法 | 参数 | 结果 |
|---|---|---|
| `initialize` | — | `protocol`、`version`、`platform`、`paths`、当前 `view`、`settings`（同 `settings.get`）。等待离线词库加载 |
| `query.submit` | `text`，可选 `force`、`scroll`、`context`（查询词所在的句子，见 `query.candidate`）、`restore` | 查询结束后返回 `{request, revision}`；过程通过 `view` 事件推送。`restore` 为 `true` 时重新打开读者原本停留的页面（如 iOS 被系统关闭后重新启动）：页面照常进入前进后退，但不再写入查询历史 |
| `query.regenerate` | — | 跳过已保存结果重新查询当前页面 |
| `query.navigate` | `direction`（-1 或 1），可选 `scroll` | `{page, view}`；`page` 为 `null` 表示已到边界 |
| `query.home` | 可选 `scroll` | 返回查询历史页面 |
| `query.cancel` | — | 停止当前查询，保留已显示内容 |
| `query.view` | — | 当前 `QueryView` |
| `suggest` | `text` | `{suggestions}` |
| `query.preview` | `text` | `{query, headword, text, source}` 或 `null`。只读已保存的 TL;DR 和启用的本地词库；保持大小写缓存身份，显示实际匹配词；不联网、不记录历史、不发布或改变当前视图；支持 `cancel` |
| `query.candidate` | `text`，可选 `surrounding`、`start` | `{text, submit, context}` 或 `null`。用户在其他应用中按全局快捷键呼出 LeXi 时，前端把选中的文字或刚复制的文字交给 Core 判断：去掉随词带来的引号和标点；单行且不超过 64 个字符时 `submit` 为 `true`，直接查询；更长或多行的文字只填入输入框；链接、路径、邮箱和无效输入返回 `null`。前端能读到选区前后的文字时传 `surrounding` 与选区在其中的起点 `start`（UTF-16），Core 截出所在句子作为 `context`，前端随 `query.submit` 交回。不发起查询、不记录历史 |
| `history.list` | — | `{entries: [{query, translation, queriedAt, preview, kind}], error}` |
| `history.remove` | `query`、`translation` | `{removed, history}` |
| `settings.get` | — | `{settings, models, judgeModels, controls}`；`controls` 为在线开关与重新生成的共享文字 |
| `settings.update` | `settings`（只含要修改的字段） | 与 Windows 设置相同的校验（修改 `speech*` 字段时也校验发声模型与声线），保存后返回 `settings.get` 的内容 |
| `inference.models` | — | 通过官方 Codex `model/list` 和 Claude 初始化接口更新订阅模型能力，返回 `settings.get`；不访问凭据文件，离线时不发请求 |
| `inference.catalog` | — | 等本次启动的后台 OAuth 模型读取结束（在线时 Host 启动后自动读取一次官方 Codex/Claude 客户端的模型列表；离线或已结束时立即返回），然后返回 `settings.get` 的内容 |
| `inference.configure` | `provider`、`model`、`effort`、`speed` | 按当前模型能力校验并保存两个配置项，返回 `settings.get`；空字符串使用服务默认；不自动降低档位 |
| `settings.setOnline` | `online` | 切换在线补充并返回设置 |
| `dictionaries.list` | — | `{dictionaries, folder, packs}`；`packs` 是可在应用内下载的词典包（`id`、`name`、`languages`、`url`、`bytes`、`installedBytes`、`license`、`installed`），目前只有 ECDICT |
| `dictionaries.install` | `id`、`path`（前端下载好的词典包） | 核对大小和 SHA-256 与 `DictionaryPacks` 固定的一致，解压到数据目录下的临时文件夹并确认能打开，再替换到位、重新加载词库；返回 `dictionaries.list` 的内容。查询进行中返回 `busy`，文件不符返回 `invalid_dictionary` |
| `dictionaries.remove` | `id` | 删除这部词典的文件并重新加载词库，返回 `dictionaries.list` 的内容 |
| `connection.status` | `provider`（`codex` 或 `claude`），可选 `codexPath` | `{installed, signedIn, message}`，只含经过清理的摘要 |
| `connection.loginCommand` | `provider`，可选 `codexPath` | `{fileName, arguments, path, clearEnvironment}` 或 `null`。前端仅在用户点击后在终端中运行，并清除 `clearEnvironment` 中的变量 |
| `text.models` | `baseUrl`、`clientId`、`clientSecret`，可选 `apiKey` | `{models: [{provider, id, label, options}]}`；鉴权后读取列表，API Key 可留空；不保存凭据、不自动选择模型；离线时不发请求 |
| `text.credentials` | `baseUrl`、`clientId`、`clientSecret`，可选 `apiKey` | `{}`；按端点绑定到本次 Host 的进程内存，用于后续查词、翻译和义项整理；前端负责 Keychain 持久化，Host 不回传或记录这些值 |
| `speech.models` | — | `{models: [{provider, id, name, local}]}`；第一项是本机系统声线（`local` 为 `true`），其余为在线模型 |
| `speech.voices` | `provider`、`model` | `{voices: [{id, name}]}`：该模型的在线声线；本机声线由前端从系统读取，这里为空 |
| `speech.elevenVoices` | 可选 `key` | `{voices}`：ElevenLabs 账号可用声线。离线模式下不发请求 |
| `speech.cached` | `text`、`provider`、`model`、`voice` | 按 Windows 规则校验文字与声线，返回 `{path, local}`：已缓存的音频文件路径或 `null`。`local` 为 `true` 时由前端用系统声线朗读。不生成音频 |
| `speech.synthesize` | `text`、`provider`、`model`、`voice`，可选 `key` | `{path}`：缓存中的 MP3 路径，缺失时用在线服务生成。已缓存时不需要 `key`；离线模式下缺失则返回“离线模式下没有此声线的缓存发音。” |
| `keys.set` | `provider`（`openai` 或 `anthropic`），`key`（空字符串表示清除） | `{}`。只在没有官方客户端的平台（iOS）使用：查词、翻译和义项判断经由该服务的 API |
| `cancel` | `id` | 取消仍在运行的请求，`{cancelled}` |
| `shutdown` | — | 等待其余请求结束后最后响应，然后退出 |

## 自定义文本服务

`provider = compatible` 可用于主要模型、第二模型，以及 `judgeProvider = compatible` 的义项判断模型。普通设置只包含 `compatibleBaseUrl`、`compatibleStreaming`、模型 ID、服务标识和非敏感的 `inferenceProfiles`；`judgeProvider` 默认 `codex`，保留旧设置的含义。模型列表先通过 `text.models` 获取，再由用户明确选择。模型选项附带 `options`（`efforts`、`speeds`、`effortChoices`、`speedChoices`、`note`），`settings.get` 的模型还附带当前 `effort`、`speed`；旧客户端可忽略新增字段。只有服务端提供的档位可选，能力元数据按端点缓存且不含凭据。`inferenceProfiles` 按服务、模型和端点哈希区分，原有 Codex 选项继续作为尚未配置模型的兼容默认值。

原生前端从本机 Keychain 读取凭据，通过私有标准输入管道调用 `text.credentials`；Host 仅在进程内持有，并直接向配置的服务发出 HTTPS 请求，不经过额外服务器。不要记录请求参数或私有管道消息。错误只返回经过清理的中文提示及 `missing_credentials` / `authentication_failed` / `invalid_response` 等代码。401/403 不降级或重试；取消同时作用于 HTTP 发送及响应读取。SSE 未正常结束时不能作为完成结果缓存。

Windows 直接引用 Core，通过当前用户加密存储的读取函数提供凭据，无需 Host 的内存配置方法。TTS 的服务、密钥与请求路径独立。

## 发音

发音只在用户点击后请求。音频保存在用户数据目录的 `speech-cache`，每段文字、服务、模型和声线各有一个文件（`SpeechOptions.CacheKey`），写完整后才出现，与 Windows 共用同一份 `SpeechAudio`。前端播放返回的文件；本机系统声线由前端直接朗读，不经过缓存。

在线服务的 API Key 由前端从系统的安全存储读取，只随需要它的那一次请求传入。Host 不保存、不记录、不在响应或事件中返回密钥，`speech_failed` 的提示也不包含密钥或服务返回的原文。

## 模型 API

iOS 没有 Codex 与 Claude Code 客户端，查询模型改用用户自己的 OpenAI 或 Anthropic API Key（`ApiDictionary`）：同一份系统说明、JSON 结构与校验，OpenAI 使用 Responses API 的严格结构化输出，Anthropic 使用强制调用的工具。`initialize` 返回的 `platform` 为 `ios` 时，`settings` 中的模型选项只有这两种服务。义项判断模型的服务由模型 ID 决定：以 `claude` 开头的走 Anthropic，其余走 OpenAI。前端在启动和修改密钥后用 `keys.set` 传入密钥；Host 只在本次运行的内存中保留，不写入设置或缓存，也不在任何响应中返回。错误提示只包含应用自己的说明，不含服务返回的原文。

## 兼容

不兼容的变化提高 `HostServer.Protocol`。前端在 `initialize` 后检查 `protocol`，不一致时提示重新安装。新增字段和方法不改变版本号；前端忽略未知字段。

`host-check` 在进程内用合成模型检查流式视图、取消、错误和设置，并分别以 `dotnet` 和单文件发布形式启动 Host 检查标准输出与退出行为。

### Additive major-version preparation (protocol 1)

`QueryView.canRetry` is an optional boolean for compatible clients. `query.retry` retries only failed stages from the saved input/model checkpoint; explicit regeneration keeps its existing meaning. Speech/window settings do not invalidate text checkpoints.

`history.list` accepts optional `text` and `offset`, returns 50 matching entries plus `total`. Existing `entries`/`error` remain. Suggestions combine case-preserved dictionary history and the offline index; text translations are excluded.

`data.usage` returns cache categories (`id`, `name`, `bytes`, `files`). `data.clear {category}` clears only `intermediate`, `speech` or `results`, and refuses while a query runs. `data.export {path}` writes a credential-free private backup; `data.inspect {path}` returns version/history/results/hasSettings before any write; `data.import {path,includeSettings}` validates and merges. The native front end must obtain file paths from a user file panel and request confirmation after preview. Credentials never enter an archive or response. Error messages do not contain file paths or file content: `invalid_data` carries Core's own refusal (for example a backup over the size limit) or, for a damaged or foreign archive, a fixed sentence; `busy` means a query or another data operation is running; `file_failed` reports an unreadable or unwritable file or data folder.

`speech.elevenModels {key}` explicitly refreshes the account's TTS-capable model catalog, then `speech.models` reads it. Key lifetime follows `speech.elevenVoices`; the key is never persisted by Host. Existing saved model selections are retained.
