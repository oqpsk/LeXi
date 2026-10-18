using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Lexi;
using Lexi.Core;

internal static partial class Program
{
    private static void CheckInputPreview(MainWindow main)
    {
        void Check(bool ok,string name) { if (!ok) throw new Exception("FAIL: "+name); Console.WriteLine("PASS: "+name); }
        var input=(TextBox)main.FindName("SearchBox");
        var card=(Button)main.FindName("InputPreviewCard");
        var text=(TextBlock)main.FindName("InputPreviewText");
        main.DismissSuggestions(); input.Text="ABS";
        main.ShowInputPreview(new("ABS","abs","腹肌","本地词库 · 词典摘录"));
        Check(card.Visibility==Visibility.Visible && text.Text=="ABS → abs：腹肌" && AutomationProperties.GetName(card).StartsWith("TL;DR"),
            "input preview identifies a case-different dictionary match under the permanent TLDR badge");
        Measure((FrameworkElement)main.Content,558,398);
        Check(text.MaxHeight==52 && !card.Focusable && card.Tag is "ABS" && card.ToolTip.ToString()!.Contains("词典摘录"),
            "the compact preview limits visible lines, exposes its source and leaves input focus alone");
        input.Text="ABI"; main.ShowInputPreview(new("ABS","ABS","stale","saved"));
        Check(card.Visibility==Visibility.Collapsed,"an old preview cannot appear for a newly edited query");
        main.ShowInputPreview(new("ABI","ABI","应用二进制接口","已保存结果"));
        var composition = new TextComposition(InputManager.Current,input,"拼");
        input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,composition)
            { RoutedEvent=TextCompositionManager.PreviewTextInputStartEvent,Handled=true });
        main.ShowInputPreview(new("ABI","ABI","应用二进制接口","已保存结果"));
        Check(card.Visibility==Visibility.Collapsed,"marked input hides previews even when the control handles composition events");
        input.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice,composition)
            { RoutedEvent=TextCompositionManager.TextInputEvent,Handled=true });
        main.ShowInputPreview(new("ABI","ABI","应用二进制接口","已保存结果"));
        Check(card.Visibility==Visibility.Visible,"handled text-commit events end composition and allow the next preview");
        main.DismissSuggestions();
        Check(card.Visibility==Visibility.Collapsed && card.Tag==null,"dismissal clears a standalone preview even without suggestions");
    }
}
