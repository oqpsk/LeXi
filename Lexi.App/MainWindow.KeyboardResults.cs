using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Lexi.Core;

namespace Lexi;

public partial class MainWindow
{
    private readonly List<(SenseView Sense, Border Row)> keyboardSenses = [];
    private readonly Dictionary<string, ToggleButton> disclosureToggles = [];
    private string? focusedSense;

    internal bool EnterResults()
    {
        if (inputComposing || shown.Kind != QueryViewKind.Dictionary || keyboardSenses.Count == 0) return false;
        CancelSearchFocus(); CancelInputPreview(); DismissSuggestions();
        return FocusSense(0);
    }

    private bool FocusSense(int index)
    {
        if (index < 0 || index >= keyboardSenses.Count) return false;
        var (sense, row) = keyboardSenses[index];
        focusedSense = sense.Key;
        row.Focus(); row.BringIntoView();
        return row.IsKeyboardFocused;
    }

    private void AttachSenseKeyboard(Border row, SenseView sense)
    {
        row.Focusable = true;
        KeyboardNavigation.SetIsTabStop(row, true);
        AutomationProperties.SetName(row, sense.Chinese);
        AutomationProperties.SetHelpText(row, "上下键切换义项，空格朗读，Enter 展开例句，Ctrl+C 复制，Esc 回到输入框");
        row.GotKeyboardFocus += (_, _) =>
        {
            if (!row.IsKeyboardFocused) return;
            focusedSense = sense.Key; row.Background = Theme.Brush("AccentSoft");
        };
        row.LostKeyboardFocus += (_, _) => row.Background = Brushes.Transparent;
        row.PreviewKeyDown += async (_, e) =>
        {
            // Text selections, disclosure controls and their own shortcuts keep working.
            if (!row.IsKeyboardFocused || inputComposing) return;
            if (e.IsRepeat && e.Key is Key.Space or Key.Enter) { e.Handled = true; return; }
            var modifiers = Keyboard.Modifiers;
            var index = keyboardSenses.FindIndex(s => s.Sense.Key == sense.Key);
            if (modifiers == ModifierKeys.Control && e.Key == Key.C)
            { CopyLookupText(string.Join("\n", new[] { sense.Chinese, sense.English }.Where(t => t.Length > 0))); e.Handled = true; return; }
            if (modifiers != ModifierKeys.None) return;
            switch (e.Key)
            {
                case Key.Down: e.Handled = true; FocusSense(Math.Min(index + 1, keyboardSenses.Count - 1)); break;
                case Key.Up:
                    e.Handled = true;
                    if (index <= 0) FocusSearch(); else FocusSense(index - 1);
                    break;
                case Key.Escape: e.Handled = true; FocusSearch(); break;
                case Key.Enter:
                    if (disclosureToggles.TryGetValue(sense.Key + ":examples", out var examples))
                    { e.Handled = true; examples.IsChecked = examples.IsChecked != true; }
                    break;
                case Key.Space:
                    e.Handled = true;
                    var english = !string.IsNullOrWhiteSpace(sense.English);
                    await ReadAloudAsync(sense.Key + (english ? ":english" : ":chinese"), english ? sense.English : sense.Chinese, "");
                    break;
            }
        };
        keyboardSenses.Add((sense, row));
    }

    // Streaming/merging rebuilds rows. Keep the keyboard on the same sense, including when that
    // wording becomes an alternative, without taking it back from an edit or another control.
    private void RestoreSenseKeyboard(string? key, bool hadFocus)
    {
        if (!hadFocus || key == null) return;
        var request = shown.Request;
        var index = keyboardSenses.FindIndex(s => s.Sense.Key == key || s.Sense.Alternatives.Any(a => a.Key == key));
        if (index < 0) { focusedSense = null; if (IsActive) FocusSearch(); return; }
        var focused = Keyboard.FocusedElement;
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            if (!focusClosed && IsActive && request == shown.Request && focusedSense == key && Keyboard.FocusedElement == focused)
            {
                var currentIndex = keyboardSenses.FindIndex(s => s.Sense.Key == key || s.Sense.Alternatives.Any(a => a.Key == key));
                FocusSense(currentIndex);
            }
        }));
    }

    internal string SummaryText => shown.Kind switch
    {
        QueryViewKind.Dictionary when shown.QuickRead.Visible => shown.QuickRead.Text,
        QueryViewKind.Translation => string.Join("\n\n", shown.Translation?.Paragraphs ?? []),
        _ => ""
    };

    private void CopySummary()
    {
        if (SummaryText.Length == 0) return;
        if (CopyLookupText(SummaryText)) LookupStatus.Text = shown.Kind == QueryViewKind.Translation ? "已复制译文" : "已复制 TL;DR";
    }

    private static bool CopyLookupText(string text)
    {
        if (text.Length == 0) return false;
        try { Clipboard.SetText(text); return true; }
        catch (ExternalException) { return false; }
    }
}
