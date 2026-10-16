using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Windows;

namespace Lexi;

public partial class App : Application
{
    private Mutex? mutex;
    private readonly CancellationTokenSource lifetime = new();
    public static string Root => AppContext.BaseDirectory;
    public static string UserRoot => Path.Combine(Root, "userdata");
    private const string Pipe = "Lexi.Dictionary.v1";
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SetCurrentProcessExplicitAppUserModelID(string appID);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SetCurrentProcessExplicitAppUserModelID("Lexi.Dictionary");
        mutex = new Mutex(true, "Local\\Lexi.Dictionary.v1", out var first);
        if (!first)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName("Lexi")) { if (p.Id != Environment.ProcessId) AllowSetForegroundWindow(p.Id); p.Dispose(); }
                using var client = new NamedPipeClientStream(".", Pipe, PipeDirection.Out);
                client.Connect(1800);
                using var writer = new StreamWriter(client); writer.WriteLine("activate");
            }
            catch (Exception ex) when (ex is IOException or TimeoutException) { MessageBox.Show("词典正在启动，请稍后再试。", "Lexi"); }
            Shutdown(); return;
        }
        try { Directory.CreateDirectory(UserRoot); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { MessageBox.Show("请把 Lexi 文件夹放到可以写入的位置。", "Lexi"); Shutdown(); return; }
        var window = new MainWindow(); MainWindow = window; window.Show();
        _ = Task.Run(async () =>
        {
            while (!lifetime.IsCancellationRequested)
            {
                try
                {
                    using var server = new NamedPipeServerStream(Pipe, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(lifetime.Token);
                    using var reader = new StreamReader(server);
                    if (await reader.ReadLineAsync(lifetime.Token) == "activate") await Dispatcher.InvokeAsync(window.Restore);
                }
                catch (OperationCanceledException) { break; }
                catch (IOException) { await Task.Delay(250); }
            }
        });
    }
    protected override void OnExit(ExitEventArgs e) { lifetime.Cancel(); mutex?.Dispose(); base.OnExit(e); }
}
