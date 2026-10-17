# 功能对照

LeXi 的 Windows 版（`Lexi.App`，WPF）与 macOS 版（`apple/`，SwiftUI）共用 `Lexi.Core` 的查询、缓存、合并、来源、进度与状态文字。两端功能和行为一致，外观各自遵循平台规范。

改变用户可见的功能或行为时，在同一个 PR 中更新本表：写明共享实现、两端入口，或另一平台的临时缺口。平台专属功能写出对应实现；没有对应时写“不适用”并说明原因。

状态：**已实现**、**计划**（尚未发布，不对外宣称）、**不适用**。

## 查询与结果

| 功能 | 共享行为（Lexi.Core） | Windows | macOS |
|---|---|---|---|
| 自动判断查词、翻译与中英方向 | `Queries.IsText`、`Translation.Target`、`QuerySession.SubmitAsync` | 已实现：单一多行输入框，Enter 查询，Shift+Enter 换行 | 计划 |
| 离线词典联合查询、简繁检索、词形匹配 | `OfflineLibrary`、`ChineseVariants`（内置 OpenCC 字表，各平台相同） | 已实现 | 计划 |
| 输入建议 | `QuerySession.Suggest` | 已实现：↑↓ 预览，Tab 或点击接受，Esc 还原 | 计划 |
| Wiktionary 在线来源与原文 | `OnlineDictionary`、`QueryView.Online` | 已实现：可展开原文与来源链接 | 计划 |
| 双模型查词、流式预览、失败隔离 | `DictionaryCoordinator`、`QuerySession` | 已实现 | 计划 |
| 义项合并与其他表述 | `SenseMerging`、`AnswersView` | 已实现：折叠的“其他表述 / 用法细分” | 计划 |
| TL;DR 概览 | `QueryPresentation`（`QuickReadView`） | 已实现：粉色 TL;DR 徽标与状态 | 计划 |
| 音标及来源、AI 音标标记 | `QueryPresentation`（`SavedPronunciation`） | 已实现 | 计划 |
| 词形变化 | `QueryPresentation`（`WordFormRow`） | 已实现：右键复制词形 | 计划 |
| 词源 | `EtymologyView` | 已实现：词条旁按钮弹出 | 计划 |
| 专业参考摘要 | `SpecialistReferences`、`ReferenceView` | 已实现 | 计划 |
| 查询阶段、结束状态与状态文字 | `QueryProgressState`、`QueryProgressTracker`、`QueryView.Status` | 已实现：输入框旁状态环，悬浮查看阶段；单行底栏 | 计划 |
| 保存的结果与重新生成 | `SavedQueries`、`QuerySession.RegenerateAsync` | 已实现：底栏“重新生成” | 计划 |
| 在线补充开关 | `QuerySession.SetOnline`、`ControlText` | 已实现：底栏两态开关 | 计划 |
| 文本翻译与复制译文 | `Translation`、`TranslationView` | 已实现 | 计划 |
| 查询历史 | `QueryHistory` | 已实现：主页列表，可单条删除 | 计划 |
| 后退 / 前进 | `QueryNavigation`、`NavigationView` | 已实现：Alt+← / Alt+→ | 计划：⌘[ / ⌘] |

## 设置与连接

| 功能 | 共享行为（Lexi.Core） | Windows | macOS |
|---|---|---|---|
| 查询与模型设置 | `AppSettings`、`QuerySession.SettingsSaved` | 已实现 | 计划 |
| 离线词典启停与信息 | `DictionaryCatalog` | 已实现：设置 → 离线词典 | 计划 |
| 官方客户端登录与状态 | `ProviderConnection`、`ToolLocator`（不依赖 shell PATH） | 已实现：设置 → 账号连接 | 计划 |
| 本地发音 | `SpeechCatalog` | 已实现：Windows SAPI 声线 | 计划：系统声线 |
| 在线发音与密钥 | `SpeechClient`、`ElevenLabsSpeech` | 已实现：密钥使用 Windows 当前用户加密 | 计划：密钥存入钥匙串 |

## 平台专属

| 功能 | Windows | macOS |
|---|---|---|
| 用户数据位置（`LexiPaths`） | 程序目录 `userdata/`、`data/`，便于升级保留 | `~/Library/Application Support/LeXi`（含 `data/`） |
| 窗口位置记忆 | `WindowPlacement` 保存正常位置与最大化状态 | 计划：使用系统窗口恢复 |
| 关闭与后台 | 托盘图标；关闭按钮可设为最小化或退出 | 计划：关闭窗口不退出，Dock 重新打开 |
| 单实例 | 互斥量与命名管道激活已有窗口 | 计划：macOS 应用默认单实例 |
| 背景材质 | Windows 11 Acrylic，保留不透明回退 | 计划：工具栏、控件与弹出层使用 Liquid Glass，正文使用常规背景 |
| 快捷键 | Ctrl+L、Ctrl+,、Ctrl+Q、Esc 最小化 | 计划：⌘L、⌘,、⌘Q，Esc 收起建议 |
| 任务栏图标刷新 | `Refresh-TaskbarIcon.ps1` | 不适用：Dock 图标随应用包更新 |
