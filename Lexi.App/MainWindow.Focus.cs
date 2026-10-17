using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Lexi;

public partial class MainWindow
{
    private DispatcherOperation? searchFocusRetry;
    private bool focusClosed;

    internal void FocusSearch() => FocusSearch(selectAll: true);
    private void FocusSearch(bool selectAll)
    {
        // Never activate a background window or redirect input from its modal dialog.
        if (focusClosed || !IsActive || !IsVisible || !IsEnabled || WindowState == WindowState.Minimized) return;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || GetForegroundWindow() != handle) return;
        var alreadyFocused = SearchBox.IsKeyboardFocused;
        // Logical focus alone can survive activation while native keyboard focus is
        // elsewhere. Bring the foreground window's input host back into agreement.
        if (GetFocus() != handle) SetFocus(handle);
        FocusManager.SetFocusedElement(this, SearchBox);
        Keyboard.Focus(SearchBox);
        if (SearchBox.IsKeyboardFocused && (selectAll || !alreadyFocused)) SearchBox.SelectAll();
    }

    private void QueueSearchFocus()
    {
        CancelSearchFocus();
        if (focusClosed || Dispatcher.HasShutdownStarted) return;
        // Run after loaded/layout and activation focus restoration. Do not reselect
        // text if the immediate attempt succeeded and the user has started typing.
        searchFocusRetry = Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            searchFocusRetry = null;
            FocusSearch(selectAll: false);
        }));
    }

    private void CancelSearchFocus()
    {
        searchFocusRetry?.Abort(); searchFocusRetry = null;
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr handle);
}
