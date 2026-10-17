using System.Windows;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private readonly SpeechPlayback speech = new(App.UserRoot);
    private bool speaking;
    private int speechRequest;
    private string speechText = "";
    private void StopSpeech()
    {
        speechRequest++; speech.Stop(); speaking = false;
        SpeechButton.Content = Theme.Icon("SpeakerIcon"); SpeechButton.ToolTip = "播放合成发音";
    }
    private async void Speech_Click(object sender, RoutedEventArgs e)
    {
        if (speaking) { StopSpeech(); LookupStatus.Text = "发音已停止"; return; }
        await SpeakAsync(speechText);
    }
    private async Task SpeakAsync(string text)
    {
        StopSpeech(); var request = speechRequest; speaking = true;
        SpeechButton.Content = Theme.Icon("StopIcon");
        try
        {
            await speech.PlayAsync(text, SpeechOptions.From(settings), settings.Online, message =>
            { if (request == speechRequest) { SpeechButton.ToolTip = message + " · 点击停止"; LookupStatus.Text = message; } });
            if (request == speechRequest) LookupStatus.Text = "发音播放结束";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is InvalidOperationException or System.IO.IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or TimeoutException or System.ComponentModel.Win32Exception)
        { if (request == speechRequest) LookupStatus.Text = ex.Message; }
        finally { if (request == speechRequest) StopSpeech(); }
    }
}
