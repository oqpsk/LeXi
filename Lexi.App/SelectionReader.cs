using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Lexi.Core;

namespace Lexi;

// Private helper mode of the same executable; dispatched before single-instance or tray startup.
// Selection APIs stay on an MTA thread. Parent-side deadline terminates an unresponsive provider.
internal static class SelectionReader
{
    internal static bool TryRun(string[] args)
    {
        if (args.FirstOrDefault() is not ("--read-selection" or "--read-clipboard")) return false;
        CapturedLookup? result = null;
        try
        {
            if (args is ["--read-selection", var rootText, var focusText] && long.TryParse(rootText, out var root) && long.TryParse(focusText, out var focus))
            {
                var thread = new Thread(() =>
                {
                    try { result = Read(new(root), new(focus)); }
                    catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException or UnauthorizedAccessException) { }
                }) { IsBackground = true };
                thread.SetApartmentState(ApartmentState.MTA); thread.Start(); thread.Join();
            }
            else if (args is ["--read-clipboard", var countText] && uint.TryParse(countText, out var count) && count == GetClipboardSequenceNumber())
            {
                if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (GetClipboardSequenceNumber() == count && Queries.Validate(text) == null) result = new(text);
                }
            }
        }
        catch (Exception e) when (e is ExternalException or InvalidOperationException or UnauthorizedAccessException) { }
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.Write(CapturedLookup.ToJson(result));
        return true;
    }

    private static CapturedLookup? Read(IntPtr root, IntPtr focus)
    {
        if (!IsWindow(root)) return null;
        GetWindowThreadProcessId(root, out var pid);
        if (pid == Environment.ProcessId) return null;
        if (focus != IntPtr.Zero && focus != root && !IsChild(root, focus)) focus = IntPtr.Zero;
        var window = AutomationElement.FromHandle(root);
        if (focus != IntPtr.Zero)
        {
            var element = AutomationElement.FromHandle(focus);
            for (var i = 0; element != null && i < 6; i++, element = TreeWalker.ControlViewWalker.GetParent(element))
            {
                if (element.Current.ProcessId != pid || element.Current.IsPassword) return null;
                if (From(element) is { } selected) return selected;
                if (element.Equals(window)) break;
            }
        }
        if (From(window) is { } direct) return direct;
        // A browser's native focused HWND is often its render host. The document beneath it
        // supplies TextPattern; never search other windows or the whole desktop.
        var candidates = window.FindAll(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.IsTextPatternAvailableProperty, true),
            new PropertyCondition(AutomationElement.IsOffscreenProperty, false)));
        for (var i = 0; i < Math.Min(32, candidates.Count); i++)
            if (From(candidates[i]) is { } selected) return selected;
        return null;
    }

    private static CapturedLookup? From(AutomationElement element)
    {
        if (element.Current.IsPassword || !element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
        var ranges = ((TextPattern)pattern).GetSelection();
        if (ranges.Length != 1) return null;
        var selected = ranges[0];
        var text = selected.GetText(8193);
        if (string.IsNullOrWhiteSpace(text) || Queries.Validate(text) != null) return null;
        if (text.Length > LookupCandidates.SubmitLength) return new(text);
        var range = selected.Clone();
        range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, -300);
        range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, 300);
        var before = range.Clone();
        before.MoveEndpointByRange(TextPatternRangeEndpoint.End, selected, TextPatternRangeEndpoint.Start);
        var start = before.GetText(601).Length;
        var surrounding = range.GetText(1300);
        return start + text.Length <= surrounding.Length && surrounding.AsSpan(start, text.Length).SequenceEqual(text)
            ? new(text, surrounding, start) : new(text);
    }

    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsChild(IntPtr parent, IntPtr child);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
}
