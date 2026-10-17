# 功能对照

LeXi 的 Windows 版（`Lexi.App`，WPF）与 macOS 版（`apple/`，SwiftUI）共用 `Lexi.Core` 的查询、缓存、合并、来源、进度与状态文字。Windows 直接引用 Core；macOS 通过内置的 `Lexi.Host`（[协议](docs/host-protocol.md)）调用同一份 Core。两端功能和行为一致，外观各自遵循平台规范。

改变用户可见的功能或行为时，在同一个 PR 中更新本表：写明共享实现、两端入口，或另一平台的临时缺口。平台专属功能写出对应实现；没有对应时写“不适用”并说明原因。

状态：**已实现**、**计划**（尚未发布，不对外宣称）、**不适用**。

## 查询与结果

| 功能 | 共享行为（Lexi.Core） | Windows | macOS |
|---|---|---|---|
| 自动判断查词、翻译与中英方向 | `Queries.IsText`、`Translation.Target`、`QuerySession.SubmitAsync` | 已实现：单一多行输入框，Enter 查询，Shift+Enter 换行 | 已实现：多行输入框，Return 查询，Shift-Return 或 Option-Return 换行 |
| 离线词典联合查询、简繁检索、词形匹配 | `OfflineLibrary`、`ChineseVariants`（内置 OpenCC 字表，各平台相同） | 已实现 | 已实现：词库位于 `~/Library/Application Support/LeXi/data` |
| 输入建议 | `QuerySession.Suggest` | 已实现：↑↓ 预览，Tab 或点击接受，Esc 还原 | 已实现：↑↓ 预览，Tab 或点击接受，Esc 还原；输入法组字时按键留给输入法 |
| Wiktionary 在线来源与原文 | `OnlineDictionary`、`QueryView.Online` | 已实现：可展开原文与来源链接 | 已实现：可展开原文与来源链接 |
| 双模型查词、流式预览、失败隔离 | `DictionaryCoordinator`、`QuerySession` | 已实现 | 已实现 |
| 义项合并与其他表述 | `SenseMerging`、`AnswersView` | 已实现：折叠的“其他表述 / 用法细分” | 已实现：可展开的“其他表述 / 用法细分” |
| TL;DR 概览 | `QueryPresentation`（`QuickReadView`） | 已实现：粉色 TL;DR 徽标与状态 | 已实现：粉底深色 TL;DR 标记与状态 |
| 音标及来源、AI 音标标记 | `QueryPresentation`（`SavedPronunciation`） | 已实现 | 已实现 |
| 词形变化 | `QueryPresentation`（`WordFormRow`） | 已实现：右键复制词形 | 已实现：右键菜单复制词形 |
| 词源 | `EtymologyView` | 已实现：词条旁按钮弹出 | 已实现：词条旁按钮弹出 |
| 专业参考摘要 | `SpecialistReferences`、`ReferenceView` | 已实现 | 已实现 |
| 查询阶段、结束状态与状态文字 | `QueryProgressState`、`QueryProgressTracker`、`QueryView.Status` | 已实现：输入框旁状态环，悬浮查看阶段；单行底栏 | 已实现：输入框旁状态指示，点击查看阶段；底栏状态 |
| 保存的结果与重新生成 | `SavedQueries`、`QuerySession.RegenerateAsync` | 已实现：底栏“重新生成” | 已实现：底栏“重新生成”，⌘R |
| 在线补充开关 | `QuerySession.SetOnline`、`ControlText` | 已实现：底栏两态开关 | 已实现：底栏开关 |
| 文本翻译与复制译文 | `Translation`、`TranslationView` | 已实现 | 已实现 |
| 查询历史 | `QueryHistory` | 已实现：主页列表，可单条删除 | 已实现：主页列表，可单条删除；⌘Y |
| 后退 / 前进 | `QueryNavigation`、`NavigationView` | 已实现：Alt+← / Alt+→ | 已实现：工具栏按钮，⌘[ / ⌘] |

## 设置与连接

| 功能 | 共享行为（Lexi.Core） | Windows | macOS |
|---|---|---|---|
| 查询与模型设置 | `AppSettings`、`ModelCatalog`（模型选项与校验）、`QuerySession.SettingsSaved` | 已实现 | 已实现：设置窗口，修改即生效，校验与提示同 Windows |
| 离线词典启停与信息 | `DictionaryCatalog` | 已实现：设置 → 离线词典 | 已实现：设置 → 离线词典；安装词库需 PowerShell 7 运行同一安装脚本 |
| 官方客户端登录与状态 | `ProviderConnection`、`ToolLocator`（不依赖 shell PATH） | 已实现：设置 → 账号连接 | 已实现：在“终端”中打开官方登录，完成后点击“检查状态” |
| 本地发音 | `SpeechCatalog` | 已实现：Windows SAPI 声线 | 已实现：系统声线，按文字语言自动选择；声线选择计划中 |
| 在线发音与密钥 | `SpeechClient`、`ElevenLabsSpeech` | 已实现：密钥使用 Windows 当前用户加密 | 计划：密钥存入钥匙串 |

## 平台专属

| 功能 | Windows | macOS |
|---|---|---|
| 用户数据位置（`LexiPaths`） | 程序目录 `userdata/`、`data/`，便于升级保留 | `~/Library/Application Support/LeXi`（含 `data/`） |
| 窗口位置记忆 | `WindowPlacement` 保存正常位置与最大化状态 | 系统窗口恢复 |
| 关闭与后台 | 托盘图标；关闭按钮可在“设置 → 窗口”中设为最小化或退出 | 关闭窗口不退出，点击 Dock 图标重新打开；不提供关闭行为设置 |
| 单实例 | 互斥量与命名管道激活已有窗口 | macOS 应用默认单实例，单一查询窗口 |
| 背景材质 | Windows 11 Acrylic，保留不透明回退 | Liquid Glass 用于工具栏、搜索栏、底栏控件与弹出层，结果正文使用常规背景；跟随系统深浅色 |
| 快捷键 | Ctrl+L、Ctrl+,、Ctrl+Q；Esc 收起建议或最小化 | ⌘L、⌘R、⌘[、⌘]、⌘Y、⌘,、⌘Q；Esc 收起建议 |
| 查询逻辑接入 | 直接引用 `Lexi.Core` | 内置 `Lexi.Host`（JSON lines） |
| 构建与分发 | `scripts/Build.ps1`，可生成自包含版本 | `apple/scripts/build-app.sh`，按构建机器架构生成本机签名的应用；Developer ID 签名与公证计划中 |
| 发音状态 | 底栏显示生成与播放状态 | 扬声器按钮显示播放或停止 |
| 任务栏图标刷新 | `Refresh-TaskbarIcon.ps1` | 不适用：Dock 图标随应用包更新 |
