# 功能对照

LeXi 的 Windows 版（`Lexi.App`，WPF）与 macOS 版（`apple/`，SwiftUI）共用 `Lexi.Core` 的查询、缓存、合并、来源、进度与状态文字。Windows 直接引用 Core；macOS 通过内置的 `Lexi.Host`（[协议](docs/host-protocol.md)）调用同一份 Core。两端功能和行为一致，外观各自遵循平台规范。

改变用户可见的功能或行为时，在同一个 PR 中更新本表：写明共享实现、两端入口，或另一平台的临时缺口。平台专属功能写出对应实现；没有对应时写“不适用”并说明原因。

状态：**已实现**、**计划**（尚未发布，不对外宣称）、**不适用**。

## 查询与结果

| 功能 | 共享行为（Lexi.Core） | Windows | macOS |
|---|---|---|---|
| 自动判断查词、翻译与中英方向 | `Queries.IsText`、`Translation.Target`、`QuerySession.SubmitAsync` | 已实现：单一多行输入框，Enter 查询，Shift+Enter 换行 | 已实现：多行输入框，Return 查询，Shift-Return 或 Option-Return 换行 |
| 窗口来到前台即可输入 | 不适用：窗口行为 | 已实现：窗口激活时聚焦输入框并全选，Ctrl+L 同 | 已实现：窗口来到前台时聚焦输入框并全选上次的查询，⌘L 同 |
| 离线词典联合查询、简繁检索、词形匹配 | `OfflineLibrary`、`ChineseVariants`（内置 OpenCC 字表，各平台相同） | 已实现 | 已实现：词库位于 `~/Library/Application Support/LeXi/data` |
| 输入停顿 TL;DR | `InputPreview` / `QuerySession.PreviewAsync`：先读已保存结果，再读启用的本地词库；显示实际匹配词；不联网，不写历史，不改变导航和当前结果 | 已实现：停顿 300 ms 显示紧凑 TL;DR，跟随建议选择；组字时等待，Esc/查询时取消；点击或 Enter 打开完整查询 | 已实现：通过 Host 的 `query.preview` 读取同一内容；相同停顿、组字与取消规则，原生卡片位于输入框下方 |
| 输入建议 | `QuerySession.Suggest`：历史精确大小写、重复次数、近期查询优先，再接离线词库 | 已实现：↑↓ 预览，Tab 或点击接受，Esc 还原 | 已实现：列表悬于输入框下方；↑↓ 预览，Tab 或点击接受，Esc 还原；输入法组字时按键留给输入法 |
| Wiktionary 在线来源与原文 | `OnlineDictionary`、`QueryView.Online` | 已实现：可展开原文与来源链接 | 已实现：可展开原文与来源链接 |
| 同义项判断返回精简 | `SenseMerging`：仍逐对判断，仅返回高置信度合并关系；省略项按不确定处理，保留原文及 TL;DR 选择 | 已实现：同一 Core | 已实现：同一 Host/Core |
| 双模型查词、流式预览、失败隔离 | `DictionaryCoordinator`、`QuerySession`；后台生成与预览采样、重复内容去重，最终结果完整校验 | 已实现：仅排队最新预览，输入/滚动/取消优先 | 已实现：Host 使用同一预览采样 |
| OAuth 动态模型列表 | 官方客户端模型发现及能力缓存；在线时每次启动后在后台读取一次，失败时保留缓存与已选模型；隐藏模型单独标注；移除固定四项列表 | 已实现：启动后自动读取；打开 OAuth 连接时再次读取完整列表及各模型 effort/速度 | 已实现：Host 启动后自动读取；打开“查询”设置时等读取结束再刷新列表；打开 OAuth 连接时再次读取 |
| 义项合并与其他表述 | `SenseMerging`、`AnswersView` | 已实现：折叠的“其他表述 / 用法细分” | 已实现：可展开的“其他表述 / 用法细分” |
| TL;DR 概览 | `QueryPresentation`（`QuickReadView`） | 已实现：粉色 TL;DR 徽标与状态 | 已实现：粉底深色 TL;DR 标记与状态 |
| 音标及来源、AI 音标标记 | `QueryPresentation`（`SavedPronunciation`） | 已实现 | 已实现 |
| 词形变化 | `QueryPresentation`（`WordFormRow`） | 已实现：右键复制词形 | 已实现：右键菜单复制词形 |
| 词源 | `EtymologyView` | 已实现：词条旁罗塞塔石碑按钮弹出 | 已实现：词条旁罗塞塔石碑按钮弹出（与 Windows 同一图形） |
| 专业参考摘要 | `SpecialistReferences`、`ReferenceView` | 已实现 | 已实现 |
| 查询阶段、结束状态与状态文字 | `QueryProgressState`、`QueryProgressTracker`、`QueryView.Status` | 已实现：输入框旁状态环，悬浮查看阶段；单行底栏 | 已实现：输入框旁状态指示，点击查看阶段；底栏状态 |
| 保存的结果与重新生成 | `SavedQueries`、`QuerySession.RegenerateAsync` | 已实现：底栏“重新生成” | 已实现：底栏“重新生成”，⌘R |
| 在线补充开关 | `QuerySession.SetOnline`、`ControlText` | 已实现：底栏两态开关 | 已实现：底部玻璃按钮，以图标和文字区分在线与离线 |
| 文本翻译与复制译文 | `Translation`、`TranslationView` | 已实现 | 已实现 |
| 查询历史 | `QueryHistory`：最多 10,000 条，频次与原始大小写持久化 | 已实现：搜索与逐页加载、单条删除 | 已实现：搜索与逐页加载、单条删除；⌘Y |
| 后退 / 前进 | `QueryNavigation`、`NavigationView` | 已实现：Alt+← / Alt+→ | 已实现：工具栏按钮，⌘[ / ⌘] |

## 设置与连接

| 功能 | 共享行为（Lexi.Core） | Windows | macOS |
|---|---|---|---|
| 查询与模型设置 | `AppSettings`、`ModelCatalog`（模型选项与校验）、`QuerySession.SettingsSaved` | 已实现 | 已实现：系统设置风格的分组表单，修改即生效，校验与提示同 Windows |
| OAuth / API 连接入口与禁用状态 | `ModelCatalog` 统一 `OAuth \|` / `API \|` 前缀 | 已实现：模型选择上方并排入口；不支持的控件与标签置灰 | 已实现：查询设置顶部并排入口，打开对应连接表单；使用系统禁用样式 |
| 模型 effort / 速度与接入模式标注 | `InferenceOptions`、模型能力缓存、Chat Completions 参数和官方 CLI 参数；档位显示名（Low … Extra High … Ultra），发送值不变 | 已实现：各模型下共用两个配置项；OAuth/API 分开保存；读取列表更新能力 | 已实现：每个模型下方一行“思考深度 · 速度”，两个菜单，能力说明作副标题；无可选档位的菜单置灰，悬停说明原因；使用 Host 同一能力和校验 |
| 离线词典启停与信息 | `DictionaryCatalog` | 已实现：设置 → 离线词典 | 已实现：设置 → 离线词典；安装词库需 PowerShell 7 运行同一安装脚本 |
| 自定义 OpenAI 兼容文本服务 | `CompatibleTextClient` / `CompatibleDictionary`：直接请求 HTTPS `/models`、`/chat/completions`；每次携带 CF Access 鉴权与 LeXi/版本号，API Key 可留空；JSON/SSE、取消、错误清理及端点隔离的缓存；用于查词、翻译和义项整理 | 已实现：设置 → API 连接；读取列表后在“查询与模型”明确选择，凭据按地址由 Windows 当前用户加密 | 已实现：设置 → 查询 → API 连接（也有独立 API 分类）；读取列表并保存连接后在“查询”选择，凭据按地址保存在 Keychain；内置本地 Host 直接请求服务 |
| 官方客户端登录与状态 | `ProviderConnection`、`ToolLocator`（不依赖 shell PATH） | 已实现：设置 → OAuth 连接 | 已实现：在“终端”中打开官方登录，完成后点击“检查状态” |
| 本地发音 | `SpeechCatalog.Local`（本机声线模型与校验） | 已实现：Windows SAPI 声线，可选“按文字语言自动选择”；生成 WAV 后按同一规则缓存 | 已实现：系统声线（`AVSpeechSynthesizer`），可选“按文字语言自动选择”；直接朗读，不生成缓存文件 |
| 在线发音：OpenAI、Grok、ElevenLabs | `SpeechCatalog`（模型、声线、校验与提示）、`SpeechClient`、`ElevenLabsSpeech` | 已实现：设置 → 发音选择模型与声线，可试听 | 已实现：设置 → 发音选择模型与声线，可试听；通过 Host 的 `speech.*` 方法调用同一份 Core |
| ElevenLabs 账号声线与 Voice ID | `SpeechClient.ElevenVoicesAsync`、`SpeechAudio.ElevenVoicesAsync`（离线时不读取） | 已实现：首次展开声线列表时读取，可刷新；“指定其他声线”填写 Voice ID | 已实现：点击“读取声线列表”读取，可刷新（SwiftUI 选择器没有展开时机）；“指定其他声线”填写 Voice ID |
| 朗读释义与例句 | `SpeechAudio` 与 `SpeechCatalog.Validate`（同一份缓存与长度规则，每次最多 4096/5000 字） | 已实现：鼠标移到释义、英文释义、例句或其译文上，左侧边距浮现扬声器，播放中变为停止按钮；右键菜单首项“朗读”，有选中文字时为“朗读所选文字”；播放中的段落以淡金色底标出；英文例句斜体，中文译文正体；扬声器与正文首行的字形中心对齐，正文到按钮的悬停区域连续覆盖折叠内容缩进 | 已实现：悬停扬声器、逐段朗读与停止、朗读所选文字；右键菜单在系统的查询、翻译与拷贝之上加入“朗读”；VoiceOver 提供“朗读”操作 |
| 柔和声纹播放状态 | 不适用：原生视觉动效；准备音频时静态，开始播放后起伏，点击停止 | 已实现：五条圆角声纹；词头 18 DIP 淡粉，释义/例句 14 DIP 淡金；保留悬停与首行对齐；减少动态效果、隐藏或最小化时停动，移除后释放动画时钟；试听同 | 已实现：同一五条声纹与节奏；词头深色用淡粉、浅色用系统前景，释义/例句保留金色；减少动态效果、内容移除或场景进入后台时停动；试听同 |
| 发音缓存与离线规则 | `SpeechAudio`：按文字、服务、模型、声线分别保存，写完整后才保留；离线时只播放已缓存的在线发音 | 已实现：系统解码器在临时 PCM 文件内添加 600 ms 静音，与完整语音通过同一条流连续播放；后台准备，取消/结束后清理临时文件和输出，原发音缓存保持完整 | 已实现：`AVAudioPlayer` 播放 Host 返回的 MP3 |
| 语音密钥 | `SpeechClient`（格式校验与错误提示）；Core 不保存密钥 | 已实现：Windows 当前用户加密；未保存时可用 `OPENAI_API_KEY` 等环境变量 | 已实现：存入当前用户的钥匙串，只供 LeXi 使用，每次请求随调用传给 Host，Host 不保存。临时签名的应用重新构建后需重新保存一次，或用固定签名身份构建（apple/README.md）。不读取环境变量：从访达打开的应用拿不到 shell 环境 |

## 平台专属

| 功能 | Windows | macOS |
|---|---|---|
| 用户数据位置（`LexiPaths`） | 程序目录 `userdata/`、`data/`，便于升级保留 | `~/Library/Application Support/LeXi`（含 `data/`） |
| 窗口位置记忆 | `WindowPlacement` 保存正常位置与最大化状态 | 系统窗口恢复 |
| 外观（浅色 / 深色） | 固定深色 | 设置 → 通用 → 外观：跟随系统、浅色或深色 |
| 查询历史入口 | 底栏 ξ 标志 | 工具栏“查询历史”按钮、⌘Y |
| 关闭与后台 | 托盘图标；关闭按钮可在“设置 → 窗口”中设为最小化或退出 | 关闭窗口不退出，⌘2 或点击 Dock 图标重新打开；不提供关闭行为设置 |
| 全局呼出 | 固定到任务栏第 2 位后，Win+2 呼出或最小化 | ⌘2 在任何应用中呼出 LeXi、再按隐藏，优先于其他应用自己的 ⌘2（系统级热键，无需辅助功能权限）；“设置 → 通用”可关闭，并可设为登录时打开 |
| 单实例 | 互斥量与命名管道激活已有窗口 | macOS 应用默认单实例，单一查询窗口 |
| 设计语言 | Windows 11 Acrylic 深色浮窗，粉色点缀 | Apple 设计语言：Liquid Glass 查询胶囊、工具栏与底部控件，结果在其下滚动；窗口本身是一整块 Liquid Glass（如控制中心），透出桌面并随深浅色变为浅色或深色玻璃，开启“减少透明度”时更不透明；系统颜色与强调色，跟随深浅色；粉色用于 TL;DR 与深色界面的词头播放声纹，词性用与 Windows 相同的淡金色（浅色下加深） |
| 快捷键 | Ctrl+L、Ctrl+,、Ctrl+Q；Esc 收起建议或最小化 | ⌘2（全局）、⌘L、⌘R、⌘[、⌘]、⌘Y、⌘,、⌘Q；Esc 收起建议 |
| 查询逻辑接入 | 直接引用 `Lexi.Core` | 内置 `Lexi.Host`（JSON lines） |
| 构建与分发 | `scripts/Build.ps1`，可生成自包含版本 | `apple/scripts/build-app.sh`，按构建机器架构生成本机签名的应用；Developer ID 签名与公证计划中 |
| 发音状态 | 底栏显示生成、播放与错误状态 | 底栏显示生成、播放与错误状态；扬声器按钮显示播放或停止 |
| 任务栏图标刷新 | `Refresh-TaskbarIcon.ps1` | 不适用：Dock 图标随应用包更新 |

## 大版本准备：性能、恢复与数据

| 功能 | 共享实现 | Windows | macOS |
|---|---|---|---|
| 首次生成等待 | `QuerySession`：模型开始前最多等待在线资料 1 秒；后到资料保留原文，不追加虚假引用 | 同 Core | 同 Host/Core |
| 分步重试 | `QueryRetry` / `DictionaryCoordinator`：成功答案复用，保留原始证据和模型 | 底栏“重试未完成” | 底栏“重试未完成” |
| 释义质量评估 | `DefinitionQuality`、24 类用例、离线回放评分与人工审读流程 | 开发检查；未进行在线 A/B 验证 | 共享同一评估 |
| 设置布局与窗口记忆 | 平台原生窗口服务，独立于查词数据 | 左侧分类、分组卡片、独立 placement 文件 | 原生 NavigationSplitView、分组 Form、NSWindow frame autosave |
| TTS 模型与重播 | `SpeechModels`：显式刷新 ElevenLabs 支持 TTS 的模型，保存目录和模型声线；音频缓存按文本/模型/声线区分 | 发音页刷新；文本右键再次播放上一段 | 发音页刷新；底栏再次朗读 |
| 备份迁移 | `UserData`：预览、白名单、读取上限、校验后事务导入；合并历史，已有查询保留，设置可选 | 数据与迁移 | 原生文件面板与确认 |
| 缓存管理 | `UserData`：中间缓存、语音、已保存查询分别统计/清理；与查询互斥 | 数据与迁移 | 数据与迁移 |
| 干净分发 | 干净源码默认值；平台安全存储不进入程序 | ZIP 脚本已准备，未生成发布包 | 现有 bundle 构建；正式签名/公证仍待发布时完成 |
