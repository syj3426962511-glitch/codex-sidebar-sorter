using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SidebarSorter;

internal sealed record BackgroundStatus(int ProcessId, bool Automatic, string Message, bool Error);

internal static class BackgroundClient
{
    public static string Identity
    {
        get
        {
            string root = Path.GetFullPath(NativeBridge.DataRoot);
            if (OperatingSystem.IsWindows()) root = root.ToUpperInvariant();
            return "sst-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..12];
        }
    }

    public static void Launch(string argument)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(argument);
        using Process? process = Process.Start(start);
        if (OperatingSystem.IsWindows() && argument == "--show" && process != null)
            IndependentLaunch.AllowSetForegroundWindow((uint)process.Id);
    }

    public static async Task EnsureRunningAsync()
    {
        try { await ReadStatusAsync(300); return; }
        catch (TimeoutException) { }
        Launch("--background");
        await ReadStatusAsync(10000);
    }

    private static async Task<NamedPipeClientStream> ConnectAsync(string command, int timeout)
    {
        var pipe = new NamedPipeClientStream(".", Identity + "-bg", PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(timeout);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
            await writer.WriteLineAsync(command);
            return pipe;
        }
        catch { pipe.Dispose(); throw; }
    }

    public static async Task<BackgroundStatus> ReadStatusAsync(int timeout = 3000)
    {
        await using var pipe = await ConnectAsync("status", timeout);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        string response = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20))
            ?? throw new IOException("The background process closed its status connection.");
        return JsonSerializer.Deserialize<BackgroundStatus>(response)!;
    }

    public static async Task<NamedPipeClientStream> AcquireAsync()
    {
        NamedPipeClientStream pipe;
        try { pipe = await ConnectAsync("lease", 300); }
        catch (TimeoutException)
        {
            await EnsureRunningAsync();
            pipe = await ConnectAsync("lease", 3000);
        }
        try
        {
            using var reader = new StreamReader(pipe, leaveOpen: true);
            if (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(60)) != "ok")
                throw new IOException("The background process could not coordinate this operation.");
            return pipe;
        }
        catch { pipe.Dispose(); throw; }
    }

    public static async Task NotifyAsync(string command)
    {
        await using var pipe = await ConnectAsync(command, 3000);
        using var reader = new StreamReader(pipe, leaveOpen: true);
        if (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)) != "ok")
            throw new IOException("The background process did not acknowledge the command.");
    }
}

internal sealed class BackgroundHost : IDisposable
{
    private readonly CancellationTokenSource stopping = new();
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly ToolSettingsStore settings = new();
    private readonly AutomaticSorter automatic;
    private readonly Task listener;
    private readonly NativeTray.Command trayCommand;
    private string? lastError;

    private BackgroundHost()
    {
        automatic = new AutomaticSorter(NativeBridge.DataRoot, async () =>
        {
            await gate.WaitAsync(stopping.Token);
            try
            {
                ToolSettings current = settings.Load();
                Localization.SetLanguage(current.Language);
                if (current.AutomaticNameSorting) await AutomaticSorter.SynchronizeByNameAsync(new SorterService(stopping.Token));
            }
            finally { gate.Release(); }
        }, NativeBridge.FindEndpoints);
        automatic.Completed += error => lastError = error?.Message;
        trayCommand = command =>
        {
            if (command == 1) BackgroundClient.Launch("--show");
            if (command == 2) _ = ExitAllAsync();
        };
        listener = ListenAsync();
    }

    public static int Run()
    {
        using var mutex = new Mutex(false, BackgroundClient.Identity + "-bg");
        bool first;
        try { first = mutex.WaitOne(TimeSpan.Zero); }
        catch (AbandonedMutexException) { first = true; }
        if (!first) return 0;
        try
        {
            using var host = new BackgroundHost();
            ToolSettings current = host.settings.Load();
            Localization.SetLanguage(current.Language);
            string icon = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "tray.ico" : "tray.png");
            int result = NativeTray.Run(icon, Localization.Text("Codex Sidebar Sorter"), Localization.Text("Open Sidebar Sorter"), Localization.Text("Exit"), host.trayCommand);
            host.stopping.Cancel();
            return result;
        }
        finally { mutex.ReleaseMutex(); }
    }

    private void Stop()
    {
        stopping.Cancel();
        NativeTray.Stop();
    }

    private async Task ExitAllAsync()
    {
        try { await DesktopProgram.CloseWindowAsync(); }
        finally { Stop(); }
    }

    private async Task ListenAsync()
    {
        var clients = new List<Task>();
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var server = new NamedPipeServerStream(BackgroundClient.Identity + "-bg", PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                try { await server.WaitForConnectionAsync(stopping.Token); }
                catch { server.Dispose(); throw; }
                clients.RemoveAll(task => task.IsCompleted);
                clients.Add(HandleAsync(server));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        finally { await Task.WhenAll(clients); }
    }

    private async Task HandleAsync(NamedPipeServerStream pipe)
    {
        await using (pipe)
        {
            try
            {
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                string? command = await reader.ReadLineAsync(stopping.Token);
                if (command == "lease")
                {
                    await gate.WaitAsync(stopping.Token);
                    try { await writer.WriteLineAsync("ok"); await reader.ReadLineAsync(stopping.Token); }
                    finally { gate.Release(); }
                    return;
                }
                ToolSettings current = settings.Load();
                Localization.SetLanguage(current.Language);
                if (command == "status")
                {
                    var status = new BackgroundStatus(Environment.ProcessId, current.AutomaticNameSorting,
                        lastError ?? Localization.Text(current.AutomaticNameSorting ? "Keep names sorted A to Z" : "Automatic sorting is paused"), lastError != null);
                    await writer.WriteLineAsync(JsonSerializer.Serialize(status));
                    return;
                }
                if (command == "compact") GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                await writer.WriteLineAsync("ok");
                if (command == "settings")
                {
                    NativeTray.SetCaptions(Localization.Text("Codex Sidebar Sorter"), Localization.Text("Open Sidebar Sorter"), Localization.Text("Exit"));
                    automatic.Request();
                }
                if (command == "exit") Stop();
            }
            catch (Exception error) when (error is IOException or OperationCanceledException) { }
        }
    }

    public void Dispose()
    {
        stopping.Cancel();
        listener.GetAwaiter().GetResult();
        automatic.DisposeAsync().AsTask().GetAwaiter().GetResult();
        gate.Dispose();
        stopping.Dispose();
    }
}

internal static class NativeTray
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void Command(int command);

    [DllImport("sorter_tray", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sorter_tray_run")]
    internal static extern int Run([MarshalAs(UnmanagedType.LPUTF8Str)] string icon,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string title, [MarshalAs(UnmanagedType.LPUTF8Str)] string open,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string exit, Command command);

    [DllImport("sorter_tray", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sorter_tray_stop")]
    internal static extern void Stop();

    [DllImport("sorter_tray", CallingConvention = CallingConvention.Cdecl, EntryPoint = "sorter_tray_set_captions")]
    internal static extern void SetCaptions([MarshalAs(UnmanagedType.LPUTF8Str)] string title,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string open, [MarshalAs(UnmanagedType.LPUTF8Str)] string exit);
}
