# Lexi

Windows 优先的轻量桌面词典。输入单词或短语，直接查看英英、英汉释义；在线 AI 补充新词、俚语和词库未收录的表达。

## 功能

- ECDICT 离线查询、词形匹配和输入建议。
- Wiktionary 英语词条作为在线来源，保留原文和来源链接。
- Claude Code / Codex 订阅登录接入，由各自官方程序管理认证。
- Claude Haiku 支持流式显示，释义先出现，例句随后补齐。
- 中文词语可通过 AI 获得中英解释；目前没有专门的汉语离线词库。
- 设置中查看离线词典名称、数量、大小、来源，并启用或停用。
- 不注册全局快捷键。可固定到任务栏第 2 位，使用 Windows 原生 Win+2 切换。

AI 结果区分“据来源整理”和“未获得词典来源核验”。来源编号表示模型引用了哪些原文，不保证生成释义正确。生成中的内容不写入缓存。

## 构建和运行

需要 Windows、PowerShell 7 和 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。使用 Git 克隆后，在仓库根目录运行：

```powershell
pwsh ./scripts/Build.ps1
pwsh ./scripts/Install-Dictionary.ps1
./artifacts/Lexi/Lexi.exe
```

构建输出到 `artifacts/Lexi/`。框架依赖版本需要 .NET 10 Desktop Runtime。词库安装脚本下载固定版本的 ECDICT，验证 SHA-256，再生成本地索引；不安装词库也能使用在线查询。

要生成带运行时的便携版：

```powershell
pwsh ./scripts/Build.ps1 -SelfContained
```

运行应用后，在“设置”中选择已安装并登录的官方客户端：

- [Claude Code](https://code.claude.com/docs/en/setup)：先完成官方订阅登录，选择 Claude Haiku。
- [Codex](https://learn.chatgpt.com/docs/cli)：先完成 ChatGPT 登录，选择 Luna Fast 或其他可用模型。

模型权限、额度和响应速度取决于服务及账号。Lexi 不附带账号，不收集登录令牌。新安装默认使用 Codex，可在设置中切换至 Claude。

## 操作

| 按键 | 操作 |
|---|---|
| Enter | 查询 |
| Esc | 收起建议列表；再次按下则最小化 |
| Ctrl+L | 选中输入框，继续查词 |
| Ctrl+Q | 退出应用 |
| 窗口关闭按钮 | 最小化 |

## 隐私和来源

运行数据位于应用旁的 `userdata/`，包括设置与查询缓存。分享安装包前应移除这个目录。在线模式会将查询词发送给 Wiktionary，启用 AI 时还会将查询词和来源片段发送给所选模型服务。离线模式不发起新的在线查询。

仓库只包含源码、合成测试数据和维护文件，不包含个人设置、登录数据、查询历史、测速日志、完整词库或第三方客户端。详见 [第三方声明](THIRD_PARTY_NOTICES.md) 和 [维护约定](CONTRIBUTING.md)。

## 开发

```powershell
pwsh ./scripts/Test.ps1
pwsh ./scripts/Check-Privacy.ps1
```

常规测试无需账号，不调用在线模型，也不操作桌面界面。Windows 自动构建会执行同样的检查。macOS 尚未实现，当前界面使用 WPF。

## 许可

Lexi 源码使用 [MIT License](LICENSE)。第三方词库和在线内容适用各自的许可，见 [第三方声明](THIRD_PARTY_NOTICES.md)。
