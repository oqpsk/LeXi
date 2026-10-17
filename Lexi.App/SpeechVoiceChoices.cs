using System.Windows.Controls;
using Lexi.Core;

namespace Lexi;

internal static class SpeechVoiceChoices
{
    // Preserve a saved/private voice even before its account list has been loaded.
    public static SpeechVoice[] Eleven(SpeechVoice[]? loaded, SpeechVoice? saved)
    {
        // null means not fetched. An empty successful response stays empty.
        var voices = loaded ?? SpeechCatalog.Voices("elevenlabs", "");
        if (saved != null && SpeechCatalog.ValidElevenVoice(saved.Id) && !voices.Any(v => v.Id == saved.Id))
            return [saved, .. voices];
        return voices;
    }

    public static SpeechVoice? Pick(string provider, SpeechVoice? selected, string manual)
    {
        if (provider != "elevenlabs" || string.IsNullOrWhiteSpace(manual)) return selected;
        var id = manual.Trim();
        if (!SpeechCatalog.ValidElevenVoice(id)) throw new InvalidOperationException("请填写有效的 ElevenLabs Voice ID，或从声线列表选择。");
        return new(id, "自定义声线");
    }
}

// Both the dropdown and refresh button use the same asynchronous discovery path.
internal sealed class SpeechVoicePicker : ComboBox
{
    internal Func<bool, Task>? Discover { get; set; }
    internal Task DiscoverAsync(bool refresh = false) => Discover?.Invoke(refresh) ?? Task.CompletedTask;
    protected override async void OnDropDownOpened(EventArgs e)
    {
        base.OnDropDownOpened(e);
        await DiscoverAsync();
    }
}
