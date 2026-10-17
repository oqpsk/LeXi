using System.Diagnostics;
using System.Text.Json;

namespace Lexi.Core;

public sealed record LoginStatus(bool Installed, bool SignedIn, string Message);

public static class ProviderConnection
{
    public static ProcessStartInfo? Command(string provider, string codexPath, bool login)
    {
        if (provider is not ("codex" or "claude")) throw new ArgumentException("Unknown provider");
        var tool = provider == "codex" ? ToolLocator.Codex(codexPath) : ToolLocator.Claude();
        if (tool == null) return null;
        var info = tool.StartInfo();
        info.UseShellExecute = false; info.CreateNoWindow = !login; info.RedirectStandardOutput = !login; info.RedirectStandardError = !login;
        if (provider == "claude")
        {
            info.ArgumentList.Add("auth");
            // Match the subscription-backed dictionary worker, without reading any credential files.
            foreach (var key in new[] { "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "CLAUDE_CODE_OAUTH_TOKEN" }) info.Environment.Remove(key);
        }
        info.ArgumentList.Add(login ? "login" : provider == "codex" ? "login" : "status");
        if (!login && provider == "codex") info.ArgumentList.Add("status");
        return info;
    }

    public static LoginStatus Parse(string provider, int exitCode, string output)
    {
        if (provider == "claude")
        {
            try
            {
                using var document = JsonDocument.Parse(output);
                var root = document.RootElement;
                var signedIn = exitCode == 0 && root.TryGetProperty("loggedIn", out var value) && value.ValueKind == JsonValueKind.True;
                var mode = root.TryGetProperty("authMethod", out var method) ? method.GetString() : "";
                return new(true, signedIn, signedIn ? mode is "claude.ai" or "oauth_token" ? "已登录 Claude 订阅" : "已配置其他认证方式" : "尚未登录");
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return new(true, false, "无法确认登录状态，请更新官方客户端后重试"); }
        }
        return new(true, exitCode == 0, exitCode != 0 ? "尚未登录" :
            output.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase) ? "已登录 ChatGPT 订阅" : "已配置其他认证方式");
    }

    public static async Task<LoginStatus> CheckAsync(string provider, string codexPath, CancellationToken token)
    {
        ProcessStartInfo? info;
        try { info = Command(provider, codexPath, false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { return new(false, false, "无法访问官方客户端，请检查程序路径"); }
        if (info == null) return new(false, false, "尚未安装官方客户端");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));
        using var process = new Process { StartInfo = info };
        try
        {
            token.ThrowIfCancellationRequested();
            process.Start();
            using var registration = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), output, error);
            // Never expose raw client output: it can contain account identifiers.
            var standardOutput = await output; var standardError = await error;
            return Parse(provider, process.ExitCode, provider == "claude" ? standardOutput : standardOutput + "\n" + standardError);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return new(true, false, "状态检查超时，请稍后重试"); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        { return new(true, false, "无法启动官方客户端，请检查程序路径"); }
    }
}
