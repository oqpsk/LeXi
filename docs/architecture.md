# 平台与仓库

LeXi 使用一个仓库维护 Windows 与未来的 macOS 版本，共享词典格式、查询与缓存规则、模型协议和回归用例。

## 目前的边界

- `Lexi.Core`：离线索引、来源数据、模型协调、词形解析、翻译、缓存与会话前进／后退记录。主体是 .NET 类库；简繁转换及客户端发现仍包含 Windows 分支，不能直接宣称已完成跨平台兼容。
- `Lexi.App`：Windows 的 WPF 界面、窗口材质、任务栏和托盘行为、本地发音与密钥保护。托盘使用 Windows Desktop 自带的 NotifyIcon；未来 macOS 前端需要独立适配菜单栏与窗口关闭语义。
- `Lexi.Tools`：词库导入与非交互检查。

[WPF 仅支持 Windows](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview/)。C# 和 .NET 类库可以复用；Windows 界面及系统服务需要独立适配。

## macOS 的后续方向

在同一仓库新增 macOS 前端项目，引用共享核心。优先评估可直接引用 .NET 的跨平台界面方案，再根据原生交互要求确定实现。当前不引入新的界面依赖，也不创建占位的 macOS 应用。

先分离客户端发现、账户登录启动、简繁转换、语音播放、凭据保护、应用数据位置等平台接口；macOS 分别适配自己的路径、系统语音与 Keychain。再在真实 Mac 上验证窗口、快捷切换、字体、发音、签名及分发。公开 README 只标明 Windows 可用。

## 升级兼容

公开品牌为 **LeXi / ξ**。既有 `Lexi.exe` 文件名、项目/程序集标识、IPC 和 AppUserModelID 暂时保留，保护现有任务栏入口与运行数据。品牌调整不迁移或清空词库、设置、历史缓存及官方客户端登录。
