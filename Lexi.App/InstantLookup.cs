using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using Lexi.Core;

namespace Lexi;

internal sealed record CapturedLookup(string Text, string? Surrounding = null, int Start = -1);

// No keyboard hook and no background text reading. Watch native clipboard sequence numbers and
// foreground HWNDs only; read text once, when the reader brings the query window forward.
internal sealed class InstantLookup : IDisposable
{
    private readonly IntPtr window;
    private readonly HwndSource source;
    private readonly ForegroundEvent callback;
    private readonly IntPtr eventHook;
    private readonly RecentCopy copy = new();
    private IntPtr external, focused;
    private bool pending, disposed;
    private const uint ForegroundChanged = 3;

    public InstantLookup(IntPtr window)
    {
        this.window = window;
        source = HwndSource.FromHwnd(window);
        source.AddHook(Message);
        copy.Observe(GetClipboardSequenceNumber(), true);
        AddClipboardFormatListener(window);
        callback = (_, _, handle, _, _, _, _) => Remember(handle);
        eventHook = SetWinEventHook(ForegroundChanged, ForegroundChanged, IntPtr.Zero, callback, 0, 0, 0);
        Remember(GetForegroundWindow());
    }

    private void Remember(IntPtr handle)
    {
        if (disposed || handle == IntPtr.Zero) return;
        var thread = GetWindowThreadProcessId(handle, out var pid);
        if (pid == Environment.ProcessId) return;
        var name = new StringBuilder(128); GetClassName(handle, name, name.Capacity);
        if (name.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "Progman" or "WorkerW") return;
        external = handle;
        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        focused = GetGUIThreadInfo(thread, ref info) ? info.Focus : IntPtr.Zero;
        pending = true;
    }

    private IntPtr Message(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x031D) ObserveCopy(); // WM_CLIPBOARDUPDATE; never read clipboard contents here.
        return IntPtr.Zero;
    }

    private void ObserveCopy()
    {
        GetWindowThreadProcessId(GetClipboardOwner(), out var owner);
        GetWindowThreadProcessId(GetForegroundWindow(), out var front);
        copy.Observe(GetClipboardSequenceNumber(), owner == Environment.ProcessId || owner == 0 && front == Environment.ProcessId);
    }

    public void Discard()
    {
        pending = false;
        ObserveCopy();
        _ = copy.Take();
    }

    public async Task<CapturedLookup?> TakeAsync(bool selected, bool copied, CancellationToken token)
    {
        ObserveCopy(); // Also see a copy whose posted notification has not been dispatched yet.
        var handle = pending ? external : IntPtr.Zero;
        var focus = focused;
        if (handle != IntPtr.Zero)
        {
            var thread = GetWindowThreadProcessId(handle, out _);
            var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
            if (thread != 0 && GetGUIThreadInfo(thread, ref info) && info.Focus != IntPtr.Zero) focus = info.Focus;
        }
        pending = false;
        // Resolve freshness before starting selection work. Its fallback copy must still be the
        // same copy when the worker reads it, and may only be consumed once.
        var sequence = copied ? copy.Take() : null;
        if (selected && handle != IntPtr.Zero &&
            await Task.Run(() => ReadAsync("--read-selection", [handle.ToInt64().ToString(), focus.ToInt64().ToString()], token), token) is { } selection)
            return selection;
        token.ThrowIfCancellationRequested();
        return sequence is { } count ? await Task.Run(() => ReadAsync("--read-clipboard", [count.ToString()], token), token) : null;
    }

    // UI Automation and delayed clipboard rendering belong to an expendable process: a stuck
    // provider cannot block the dispatcher or accumulate abandoned worker threads. No text is
    // put on the command line, on disk, or into diagnostics. The process never opens a window.
    private static async Task<CapturedLookup?> ReadAsync(string mode, string[] args, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(900));
        using var process = new Process();
        try
        {
            process.StartInfo = new ProcessStartInfo(Environment.ProcessPath!)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
              StandardOutputEncoding = Encoding.UTF8 };
            process.StartInfo.ArgumentList.Add(mode);
            foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
            if (!process.Start()) return null;
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var json = await output;
            return process.ExitCode == 0 && json.Length < 40000 ? JsonSerializer.Deserialize<CapturedLookup>(json, Json.Options) : null;
        }
        catch (Exception e) when (e is OperationCanceledException or IOException or System.ComponentModel.Win32Exception or JsonException or InvalidOperationException)
        { token.ThrowIfCancellationRequested(); return null; }
        finally
        {
            try { if (process.Id != 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (eventHook != IntPtr.Zero) UnhookWinEvent(eventHook);
        RemoveClipboardFormatListener(window); source.RemoveHook(Message);
    }

    private delegate void ForegroundEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    {
        public int Size; public uint Flags;
        public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public int Left, Top, Right, Bottom;
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern bool AddClipboardFormatListener(IntPtr window);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, ForegroundEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
}

// Testable freshness/ownership rule. A sequence notification carries no clipboard text.
internal sealed class RecentCopy(TimeProvider? time = null)
{
    private readonly TimeProvider clock = time ?? TimeProvider.System;
    private uint seen, consumed;
    private long? changed;
    internal void Observe(uint sequence, bool own)
    {
        if (seen == sequence) return;
        seen = sequence;
        changed = own ? null : clock.GetTimestamp();
    }
    internal uint? Take()
    {
        if (changed is not { } at || seen == consumed || clock.GetElapsedTime(at) > TimeSpan.FromSeconds(8)) return null;
        consumed = seen;
        return seen;
    }
}
