# LeXi for macOS

SwiftUI 应用，最低 macOS 26。查询逻辑全部来自 `Lexi.Core`：应用内置自包含单文件发布的 `Lexi.Host`，通过 JSON lines 收发请求与视图（[协议](../docs/host-protocol.md)），界面只渲染 Host 发来的 `QueryView`。功能对照见 [FEATURES.md](../FEATURES.md)。

## 构建

需要 Xcode 26 或更新版本，以及 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)。在仓库根目录运行：

```bash
apple/scripts/build-app.sh
```

脚本发布当前架构（Apple 芯片或 Intel）的 `lexi-host`，构建 SwiftUI 应用，并生成 `artifacts/macos/LeXi.app`：

- `Contents/MacOS/LeXi`：SwiftUI 应用
- `Contents/Helpers/lexi-host`：自包含单文件 Host
- `Contents/Resources/assets`：官方客户端读取的模型说明与结构
- `Contents/Resources`：图标（由 `docs/brandmark.svg` 生成）、许可与第三方声明

仓库的 `NuGet.Config` 不含包源；脚本从 nuget.org 获取 .NET 运行时包，不引入第三方依赖。生成的应用使用临时签名，只适合在本机运行。分发版本需要 Developer ID 签名、hardened runtime（Host 需要 .NET 运行时所需的 JIT 权限）和公证，目前尚未提供。

## 开发与检查

```bash
apple/scripts/build-app.sh                     # 完整应用
swift build --package-path apple               # 只编译 Swift
LEXI_HOST="$PWD/.cache/host-publish/lexi-host" swift test --package-path apple
```

- `LeXiKit`：协议模型、`HostClient`（启动 Host、按 id 匹配响应、按顺序转交视图、取消请求）与 `LexiStore`（丢弃旧版本视图，保存每次查询的展开状态）。
- `LeXiUI`：SwiftUI 界面，采用 Apple 设计语言：Liquid Glass 查询胶囊、工具栏与底部控件浮在结果之上，结果位于连续圆角的分组区域；颜色使用系统颜色与强调色，粉色只用于 TL;DR。设置窗口沿用系统设置的分组表单。
- `LeXi`：应用入口、菜单命令与设置窗口。

测试使用 `host-check` 生成的真实 Host 输出（`Tests/LeXiKitTests/Fixtures/views.json`），并在提供 `LEXI_HOST` 或已存在 `.cache/host-publish/lexi-host` 时，用离线测试词库启动真实 Host。界面检查只在内存中布局结果视图，不显示窗口、不模拟输入、不截屏。Core 的视图字段变化后，用下面的命令重新生成样例：

```bash
dotnet Lexi.Tools/bin/Release/net10.0/Lexi.Tools.dll host-check .cache/test-dictionary .cache/host-checks Lexi.Host/bin/Release/net10.0/lexi-host.dll apple/Tests/LeXiKitTests/Fixtures/views.json
```

开发时可设置 `LEXI_HOST` 指向已发布的 `lexi-host`，让 `swift run` 或 Xcode 打开的包使用它；Host 旁的 `assets` 会一并传入。

## 数据与权限

用户数据、缓存和词库位于 `~/Library/Application Support/LeXi`（词库在其中的 `data`）。应用不使用沙盒：它需要启动本机已安装的官方 Codex / Claude Code 客户端，这些客户端自行管理登录。登录在“终端”中运行官方命令，LeXi 不读取令牌或一次性验证码。
