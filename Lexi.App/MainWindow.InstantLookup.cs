using System.Windows;
using System.Windows.Interop;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private InstantLookup? instantLookup;
    private CancellationTokenSource? summonCapture;
    private bool skipSummonedLookup;

    private void InitializeInstantLookup() => instantLookup = new(new WindowInteropHelper(this).Handle);
    private void CancelSummonedLookup()
    { summonCapture?.Cancel(); summonCapture?.Dispose(); summonCapture = null; }

    private async void LookUpSummoned()
    {
        CancelSummonedLookup();
        if (skipSummonedLookup) { skipSummonedLookup = false; instantLookup?.Discard(); return; }
        if (instantLookup == null || !IsEnabled || !settings.LookupCopiedText && !settings.LookupSelectedText) return;
        using var stop = new CancellationTokenSource(); summonCapture = stop;
        var draft = SearchBox.Text; var request = shown.Request;
        try
        {
            var captured = await instantLookup.TakeAsync(settings.LookupSelectedText, settings.LookupCopiedText, stop.Token);
            if (captured == null || stop.IsCancellationRequested || focusClosed || !IsActive || !IsEnabled ||
                request != shown.Request || SearchBox.Text != draft) return;
            var candidate = LookupCandidates.From(captured.Text, captured.Surrounding, captured.Start);
            if (candidate == null) return;
            SetQuery(candidate.Text);
            FocusSearch();
            if (candidate.Submit) await SearchAsync(candidate.Text, context: candidate.Context);
        }
        catch (OperationCanceledException) { }
        finally { if (ReferenceEquals(summonCapture, stop)) summonCapture = null; }
    }
}
