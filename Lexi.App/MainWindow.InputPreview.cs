using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private CancellationTokenSource? inputPreviewStop;
    private bool inputComposing;
    private void InitializeInputPreview()
    {
        SearchBox.AddHandler(TextCompositionManager.PreviewTextInputStartEvent,
            new TextCompositionEventHandler((_, _) => { inputComposing = true; CancelSummonedLookup(); CancelInputPreview(); }), true);
        SearchBox.AddHandler(TextCompositionManager.PreviewTextInputUpdateEvent,
            new TextCompositionEventHandler((_, _) => { inputComposing = true; CancelSummonedLookup(); CancelInputPreview(); }), true);
        // TextBox handles TextInput itself; observe handled completion too so composition cannot stay stuck.
        SearchBox.AddHandler(TextCompositionManager.TextInputEvent, new TextCompositionEventHandler((_, _) =>
        {
            inputComposing = false;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(QueueInputPreview));
        }), true);
    }
    private void CancelInputPreview()
    {
        inputPreviewStop?.Cancel(); inputPreviewStop?.Dispose(); inputPreviewStop = null;
        InputPreviewCard.Visibility = Visibility.Collapsed; InputPreviewCard.Tag = null;
    }
    private async void QueueInputPreview()
    {
        CancelInputPreview();
        if (inputComposing || !SearchBox.IsKeyboardFocused || focusClosed || string.IsNullOrWhiteSpace(SearchBox.Text)) return;
        using var stop = new CancellationTokenSource(); inputPreviewStop = stop;
        var text = SearchBox.Text;
        try
        {
            await Task.Delay(300, stop.Token);
            var preview = await session.PreviewAsync(text, stop.Token);
            if (!stop.IsCancellationRequested && ReferenceEquals(inputPreviewStop, stop) && !inputComposing && SearchBox.IsKeyboardFocused)
                ShowInputPreview(preview);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.IO.InvalidDataException or ObjectDisposedException) { }
        finally { if (ReferenceEquals(inputPreviewStop, stop)) inputPreviewStop = null; }
    }
    internal void ShowInputPreview(InputPreviewView? preview)
    {
        if (preview == null || Queries.LookupKey(SearchBox.Text) != preview.Query || inputComposing)
        { InputPreviewCard.Visibility = Visibility.Collapsed; return; }
        var label = preview.Headword == preview.Query ? preview.Headword : preview.Query + " → " + preview.Headword;
        InputPreviewText.Text = label + "：" + preview.Text;
        InputPreviewCard.Tag = preview.Query;
        InputPreviewCard.ToolTip = preview.Source + "\n" + InputPreviewText.Text + "\nEnter 或点击查看完整释义";
        AutomationProperties.SetName(InputPreviewCard, "TL;DR · " + InputPreviewText.Text);
        InputPreviewCard.Visibility = Visibility.Visible;
    }
    private async void InputPreview_Click(object sender, RoutedEventArgs args)
    { if (InputPreviewCard.Tag is string query) await SearchAsync(query); }
}
