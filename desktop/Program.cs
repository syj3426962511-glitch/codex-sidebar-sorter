using Avalonia;
using Microsoft.Win32.SafeHandles;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace SidebarSorter;

internal static class DesktopProgram
{
    public static string[] Arguments { get; private set; } = [];
    private static CancellationTokenSource? ipcStopping;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);

    [STAThread]
    private static int Main(string[] arguments) => Run(arguments);

    internal static int Run(string[] arguments)
    {
        if (OperatingSystem.IsWindows())
        {
            bool relaunched = arguments.Contains(IndependentLaunch.RelaunchArgument);
            arguments = arguments.Where(argument => argument != IndependentLaunch.RelaunchArgument).ToArray();
            int? handoff = IndependentLaunch.HandoffIfNeeded(arguments, relaunched);
            if (handoff != null) return handoff.Value;
        }
        Arguments = arguments;
        if (arguments.Contains("--background")) return BackgroundHost.Run();
        if (arguments.Contains("--exit"))
        {
            CloseWindowAsync().GetAwaiter().GetResult();
            try { BackgroundClient.NotifyAsync("exit").GetAwaiter().GetResult(); }
            catch (TimeoutException) { }
            using var worker = new Mutex(false, BackgroundClient.Identity + "-bg");
            try { if (!worker.WaitOne(TimeSpan.FromSeconds(60))) return 1; }
            catch (AbandonedMutexException) { }
            worker.ReleaseMutex();
            return 0;
        }
        return RunWindow(arguments);
    }

    private static int RunWindow(string[] arguments)
    {
        string name = BackgroundClient.Identity;
        using var mutex = new Mutex(false, name);
        bool first;
        try { first = mutex.WaitOne(TimeSpan.Zero); }
        catch (AbandonedMutexException) { first = true; }
        if (!first)
        {
            try
            {
                string? response = SendAsync(name, "show").GetAwaiter().GetResult();
                if (response == "closing")
                {
                    try { if (!mutex.WaitOne(TimeSpan.FromSeconds(60))) return 1; }
                    catch (AbandonedMutexException) { }
                }
                else return 0;
            }
            catch (Exception error) when (error is IOException or TimeoutException) { return 1; }
        }
        ipcStopping = new CancellationTokenSource();
        Task listener = ListenAsync(name, ipcStopping.Token);
        try
        {
            Localization.SetLanguage(new ToolSettingsStore().Load().Language);
            AppBuilder.Configure<DesktopApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(arguments);
            return 0;
        }
        finally
        {
            ipcStopping.Cancel();
            listener.GetAwaiter().GetResult();
            ipcStopping.Dispose();
            mutex.ReleaseMutex();
        }
    }

    internal static async Task CloseWindowAsync()
    {
        try { await SendAsync(BackgroundClient.Identity, "exit"); }
        catch (TimeoutException) { return; }
        using var mutex = new Mutex(false, BackgroundClient.Identity);
        try { if (!mutex.WaitOne(TimeSpan.FromSeconds(60))) throw new TimeoutException("The desktop operation is still finishing."); }
        catch (AbandonedMutexException) { }
        mutex.ReleaseMutex();
    }

    private static async Task<string?> SendAsync(string name, string command)
    {
        using var stream = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await stream.ConnectAsync(3000);
        if (command == "show" && OperatingSystem.IsWindows() && GetNamedPipeServerProcessId(stream.SafePipeHandle, out uint processId))
            IndependentLaunch.AllowSetForegroundWindow(processId);
        using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(command);
        using var reader = new StreamReader(stream, leaveOpen: true);
        return await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60));
    }

    private static async Task ListenAsync(string name, CancellationToken cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try
                {
                    await server.WaitForConnectionAsync(cancellation);
                    using var reader = new StreamReader(server, leaveOpen: true);
                    string? command = await reader.ReadLineAsync(cancellation);
                    DesktopApp app = await DesktopApp.Ready.Task.WaitAsync(cancellation);
                    if (command == "show") Avalonia.Threading.Dispatcher.UIThread.Post(app.ShowWindow);
                    if (command == "exit") Avalonia.Threading.Dispatcher.UIThread.Post(async () => await app.ExitAsync());
                    using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
                    await writer.WriteLineAsync(app.IsClosing ? "closing" : "ok");
                }
                catch (IOException) when (!cancellation.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }
}
