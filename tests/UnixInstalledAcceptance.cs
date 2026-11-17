using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SidebarSorter;

internal static class UnixInstalledAcceptance
{
    private static async Task<int> Main(string[] arguments)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || OperatingSystem.IsWindows())
            throw new InvalidOperationException("Use a disposable Unix cloud host.");
        string installer = Path.GetFullPath(arguments[0]);
        string output = Path.GetFullPath(arguments[1]);
        string prefix = Path.Combine(Path.GetTempPath(), "排序安装验收", OperatingSystem.IsMacOS() ? "Application With Spaces.app" : "Application With Spaces");
        string program = Path.Combine(prefix, OperatingSystem.IsMacOS() ? "Contents/MacOS/SidebarSorter" : "Start Sidebar Sorter");
        Directory.CreateDirectory(output);
        using var server = new FixtureNativeServer();
        Environment.SetEnvironmentVariable("CODEX_HOME", server.Root);
        var settings = new ToolSettingsStore();
        settings.Save(new ToolSettings { Language = "zh-CN", AutomaticNameSorting = true });
        var report = new JsonObject
        {
            ["success"] = false,
            ["dataKind"] = "installed production executable with fictional native-protocol fixture",
            ["authenticatedOfficialClientSortingVerified"] = false,
            ["installDirectory"] = prefix
        };
        try
        {
            await RunAsync("bash", installer, "--prefix", prefix, "--no-launch");
            string resources = OperatingSystem.IsMacOS() ? Path.Combine(prefix, "Contents/Resources") : prefix;
            Require(File.Exists(Path.Combine(resources, "Source/README.md")), "Include the current source.");
            Require(File.Exists(Path.Combine(Path.GetDirectoryName(program)!, OperatingSystem.IsMacOS() ? "libcoreclr.dylib" : "libcoreclr.so")), "Bundle the runtime.");
            Environment.SetEnvironmentVariable("DOTNET_ROOT", Path.Combine(Path.GetTempPath(), "absent-system-dotnet"));
            Environment.SetEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", "0");
            await RunAsync(program, "--capture", Path.Combine(output, "01-installed-chinese.png"));
            await WaitAsync(() => server.IsSortedByName() && server.AreSectionsSortedByName(), "Sort projects, chats and sections through the installed executable.");
            report["bundledRuntimeAndSource"] = true;
            report["customUnicodeInstallPath"] = true;
            report["normalEntryPointAutomaticSorting"] = true;
            int workerId = (await ReadStatusAsync(server.Root))["ProcessId"]!.GetValue<int>();
            using Process worker = Process.GetProcessById(workerId);
            server.AddChat(server.Projects[0].Id, "000 installed background chat");
            server.RenameProject(server.Projects[0].Id, "000 installed project");
            await WaitAsync(server.IsSortedByName, "Sort new and renamed items after the window closes.");
            report["closeKeepsBackgroundAndAutomaticChanges"] = true;
            worker.Refresh();
            TimeSpan cpu = worker.TotalProcessorTime;
            int writes = server.WriteCount;
            await Task.Delay(10000);
            worker.Refresh();
            Require(server.WriteCount == writes, "Perform zero idle sorting writes.");
            report["idleCpuMilliseconds"] = (worker.TotalProcessorTime - cpu).TotalMilliseconds;
            report["idleWorkingSetBytes"] = worker.WorkingSet64;
            report["idleSortingWrites"] = 0;
            await RunAsync(program, "--exit");
            settings.Save(new ToolSettings { Language = "en", AutomaticNameSorting = false, ProjectSortMode = 2, ChatSortMode = 5 });
            await RunAsync(program, "--capture", Path.Combine(output, "02-installed-english.png"));
            int previousWorker = (await ReadStatusAsync(server.Root))["ProcessId"]!.GetValue<int>();
            await RunAsync("bash", installer, "--prefix", prefix, "--no-launch");
            Require(!IsRunning(previousWorker), "Finish the previous worker before replacing its files.");
            ToolSettings retained = settings.Load();
            Require(retained.Language == "en" && !retained.AutomaticNameSorting && retained.ProjectSortMode == 2 && retained.ChatSortMode == 5,
                "Retain language and ordering preferences during replacement.");
            report["runningUpgradeAndSettingsRetention"] = true;
            await RunAsync(program, "--capture", Path.Combine(output, "03-upgraded-reopened.png"));
            report["fullExitAndReopening"] = true;
            await RunAsync("bash", installer, "--uninstall", "--prefix", prefix, "--no-launch");
            Require(!Directory.Exists(prefix) && File.Exists(settings.PathName), "Uninstall while retaining user preferences.");
            report["runningUninstallAndPreferenceRetention"] = true;
            report["success"] = true;
        }
        catch (Exception error) { report["error"] = error.ToString(); }
        finally
        {
            if (File.Exists(program))
            {
                try { await RunAsync(program, "--exit"); }
                catch (Exception error) { report["cleanupError"] = error.Message; }
            }
            report["checkedAt"] = DateTimeOffset.UtcNow.ToString("O");
            File.WriteAllText(Path.Combine(output, "installed-results.json"), report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine(report.ToJsonString());
        return report["success"]!.GetValue<bool>() ? 0 : 1;
    }

    private static bool IsRunning(int id)
    {
        try { using Process process = Process.GetProcessById(id); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task<JsonObject> ReadStatusAsync(string root)
    {
        string identity = "sst-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root))))[..12];
        await using var pipe = new NamedPipeClientStream(".", identity + "-bg", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(10000);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        await writer.WriteLineAsync("status");
        using var reader = new StreamReader(pipe, leaveOpen: true);
        return JsonNode.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)))!)!.AsObject();
    }

    private static async Task RunAsync(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> standard = process.StandardOutput.ReadToEndAsync();
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120));
        Require(process.ExitCode == 0, executable + " exited " + process.ExitCode + " " + await errors);
        await standard;
    }

    private static async Task WaitAsync(Func<bool> condition, string message)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(40);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(100);
        Require(condition(), message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
