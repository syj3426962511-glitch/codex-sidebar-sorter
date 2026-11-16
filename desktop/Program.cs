using Avalonia;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace SidebarSorter;

internal static class DesktopProgram
{
    public static string[] Arguments { get; private set; } = [];
    private static CancellationTokenSource? ipcStopping;
    internal static TaskCompletionSource<DesktopApp> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [STAThread]
    private static int Main(string[] arguments)
    {
        if (OperatingSystem.IsWindows())
        {
            bool relaunched = arguments.Contains(IndependentLaunch.RelaunchArgument);
            arguments = arguments.Where(argument => argument != IndependentLaunch.RelaunchArgument).ToArray();
            int? handoff = IndependentLaunch.HandoffIfNeeded(arguments, relaunched);
            if (handoff != null) return handoff.Value;
        }
        Arguments = arguments;
        string identity = Path.GetFullPath(NativeBridge.DataRoot);
        if (OperatingSystem.IsWindows()) identity = identity.ToUpperInvariant();
        string name = "sst-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..12];
        using var mutex = new Mutex(true, name, out bool first);
        if (!first)
        {
            try
            {
                SendAsync(name, arguments.Contains("--exit") ? "exit" : arguments.Contains("--background") ? "status" : "show").GetAwaiter().GetResult();
                if (arguments.Contains("--exit"))
                {
                    try { if (!mutex.WaitOne(TimeSpan.FromSeconds(60))) return 1; }
                    catch (AbandonedMutexException) { }
                    mutex.ReleaseMutex();
                }
                return 0;
            }
            catch (Exception error) when (error is IOException or TimeoutException) { return 1; }
        }
        if (arguments.Contains("--exit")) { mutex.ReleaseMutex(); return 0; }
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

    private static async Task SendAsync(string name, string command)
    {
        using var stream = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await stream.ConnectAsync(3000);
        using var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync(command);
        using var reader = new StreamReader(stream, leaveOpen: true);
        await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60));
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
                    DesktopApp app = await Ready.Task.WaitAsync(cancellation);
                    if (command == "show") Avalonia.Threading.Dispatcher.UIThread.Post(app.ShowWindow);
                    if (command == "exit") Avalonia.Threading.Dispatcher.UIThread.Post(async () => await app.ExitAsync());
                    using var writer = new StreamWriter(server, leaveOpen: true) { AutoFlush = true };
                    await writer.WriteLineAsync("ok");
                }
                catch (IOException) when (!cancellation.IsCancellationRequested) { }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }
}
