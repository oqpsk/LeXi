using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private async Task TranslateTextAsync(string text, bool force)
    {
        lookup?.Cancel(); lookup?.Dispose(); lookup = new();
        var token = lookup.Token;
        current = text; SetQuery(text);
        var target = Translation.Target(text, TranslationTarget.SelectedIndex switch { 1 => "zh", 2 => "en", _ => "auto" });
        var key = Translation.Key(text, target, settings);
        Welcome.Visibility = Visibility.Collapsed; EntryPanel.Visibility = Visibility.Visible;
        RefreshButton.Visibility = Visibility.Visible; RefreshButton.IsEnabled = settings.Online;
        Headword.Text = target == "zh" ? "译为中文" : "Translate to English"; Headword.FontSize = 28;
        Phonetic.Text = $"{text.Length:N0} 字符 · {ProviderName} · {settings.Model}";
        AiPanel.Children.Clear(); LocalPanel.Children.Clear(); OnlinePanel.Children.Clear(); ResultsScroll.ScrollToTop();
        var cached = cache.Read<TranslationEnvelope>("translations-v1", key);
        if (cached != null && !force)
        {
            try
            {
                var validated = Translation.Parse(JsonSerializer.Serialize(cached.Entry, Json.Options), text, target);
                RenderTranslation(validated, true); LookupStatus.Text = "已显示缓存翻译"; return;
            }
            catch (InvalidDataException) { }
        }
        if (!settings.Online) { LookupStatus.Text = "离线模式下没有这段文本的缓存翻译。开启在线补充后可翻译。"; return; }
        LookupStatus.Text = $"{ProviderName} 正在翻译…";
        var watch = Stopwatch.StartNew();
        bool finished = false;
        string? lastPreview = null;
        try
        {
            var progress = new Progress<TranslationEntry>(partial =>
            {
                if (finished || token.IsCancellationRequested || current != text) return;
                var fingerprint = string.Join("\n\n", partial.Paragraphs);
                if (fingerprint == lastPreview) return;
                lastPreview = fingerprint;
                RenderTranslation(partial, false, true); LookupStatus.Text = "翻译生成中…";
            });
            var entry = settings.Provider == "claude"
                ? await claude.TranslateAsync(text, target, settings.Model, token, progress)
                : await codex.TranslateAsync(text, target, settings, token);
            token.ThrowIfCancellationRequested(); finished = true;
            RenderTranslation(entry, false);
            await TryCacheAsync("translations-v1", key, new TranslationEnvelope(entry, DateTimeOffset.UtcNow));
            token.ThrowIfCancellationRequested();
            LookupStatus.Text = $"翻译完成 · {watch.Elapsed.TotalSeconds:0.0} 秒";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or InvalidDataException or JsonException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            if (!token.IsCancellationRequested)
            {
                AiPanel.Children.Clear(); AiPanel.Children.Add(Hint(ex.Message));
                LookupStatus.Text = "翻译未完成，可重试";
            }
        }
        finally { finished = true; }
    }
    private string ProviderName => settings.Provider == "claude" ? "Claude" : "Codex";
    private void RenderTranslation(TranslationEntry entry, bool cached, bool streaming = false)
    {
        AiPanel.Children.Clear();
        var card = Card(AiPanel, "翻译", ProviderName + (cached ? " · 缓存" : streaming ? " · 生成中" : ""), true);
        card.Children.Add(Hint(streaming ? "生成中的内容尚未完成" : "AI 翻译"));
        foreach (var paragraph in entry.Paragraphs) card.Children.Add(Selectable(paragraph, 17));
        if (!string.IsNullOrWhiteSpace(entry.Note)) card.Children.Add(Hint(entry.Note));
        if (!streaming)
        {
            var copy = new Button { Content = "复制译文", Foreground = green, HorizontalAlignment = HorizontalAlignment.Left, Padding = new(0,8,0,2) };
            copy.Click += (_, _) =>
            {
                try { Clipboard.SetText(string.Join("\n\n", entry.Paragraphs)); }
                catch (System.Runtime.InteropServices.COMException) { LookupStatus.Text = "剪贴板暂时被占用，请重试。"; }
            };
            card.Children.Add(copy);
        }
    }
}
