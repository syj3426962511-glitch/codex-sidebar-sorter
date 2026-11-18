using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SidebarSorter;

internal static class InstalledDesktopAcceptance
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref int size);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    private static async Task<int> Main(string[] arguments)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" || !OperatingSystem.IsWindows())
            throw new InvalidOperationException("Use a disposable cloud Windows installation.");
        string installer = Path.GetFullPath(arguments[0]);
        string output = Path.GetFullPath(arguments[1]);
        string directory = Path.Combine(Path.GetTempPath(), "\u6392\u5e8f\u5b89\u88c5\u9a8c\u6536", "Application With Spaces");
        string program = Path.Combine(directory, "SidebarSorter.exe");
        Directory.CreateDirectory(output);
        using var server = new FixtureNativeServer();
        Environment.SetEnvironmentVariable("CODEX_HOME", server.Root);
        var settingsStore = new ToolSettingsStore();
        Directory.CreateDirectory(Path.GetDirectoryName(settingsStore.PathName)!);
        File.WriteAllText(settingsStore.PathName, "{\"language\":\"en\",\"projectSortMode\":2,\"chatSortMode\":5}");
        var report = new JsonObject
        {
            ["success"] = false,
            ["dataKind"] = "installed production executable with fictional native-protocol fixture",
            ["installDirectory"] = directory
        };
        try
        {
            int? previousVersionWorker = null;
            if (arguments.Length == 3)
            {
                await RunAsync(Path.GetFullPath(arguments[2]), "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/TASKS=", "/DIR=" + directory);
                report["previousVersion"] = FileVersionInfo.GetVersionInfo(program).ProductVersion;
                await RunAsync(program, "--capture", Path.Combine(output, "01-previous-version.png"));
                previousVersionWorker = (await ReadStatusAsync(server.Root))["ProcessId"]!.GetValue<int>();
                Require(IsRunning(previousVersionWorker.Value), "Run the preceding background before the upgrade.");
            }
            await RunAsync(installer, "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/TASKS=", "/DIR=" + directory);
            if (previousVersionWorker is int previousVersionWorkerId)
            {
                Require(!IsRunning(previousVersionWorkerId), "Stop the preceding version's background before replacement.");
                report["publishedVersionRunningUpgradePassed"] = true;
            }
            string expectedVersion = Path.GetFileName(installer).Split('-')[1];
            string? installedVersion = FileVersionInfo.GetVersionInfo(program).ProductVersion;
            Require(installedVersion == expectedVersion || installedVersion?.StartsWith(expectedVersion + "+", StringComparison.Ordinal) == true, "Install the current desktop version.");
            report["installedVersion"] = installedVersion;
            Require(File.Exists(Path.Combine(directory, "coreclr.dll")), "Install the bundled runtime.");
            Require(File.Exists(Path.Combine(directory, "Source", "desktop", "SidebarSorter.Desktop.csproj")), "Install the complete current source tree.");
            Require(File.Exists(Path.Combine(directory, "Source", "src", "NativeBridge.cs")), "Include shared source dependencies.");
            Require(File.Exists(Path.Combine(directory, "Source", "README.md")) && File.Exists(Path.Combine(directory, "Source", "LICENSE")), "Include the root files required to rebuild the installer.");
            Require(File.Exists(Path.Combine(directory, "Source", ".github", "workflows", "windows-update.yml")), "Include the source build and upgrade acceptance workflow.");
            report["customUnicodeInstallPath"] = true;
            report["bundledRuntimeAndSource"] = true;
            Environment.SetEnvironmentVariable("DOTNET_ROOT", Path.Combine(Path.GetTempPath(), "no-system-dotnet"));
            Environment.SetEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", "0");
            await RunAsync(program, "--capture", Path.Combine(output, "02-installed-english.png"));
            await WaitAsync(server.IsSortedByName, "The installed background must adopt automatic ascending name order.");
            await WaitAsync(server.AreSectionsSortedByName, "Sort pinned and custom sections through the installed background.");
            report["normalEntryPointAutomaticSorting"] = true;
            report["olderSettingsMigration"] = true;
            JsonObject status = await ReadStatusAsync(server.Root);
            int workerId = status["ProcessId"]!.GetValue<int>();
            using Process worker = Process.GetProcessById(workerId);
            string project = server.Projects[0].Id;
            server.AddChat(project, "000 installed background chat");
            server.RenameProject(project, "000 installed renamed project");
            await WaitAsync(server.IsSortedByName, "Keep sorting additions and renames after closing the installed window.");
            report["closeKeepsBackgroundAndAutomaticChanges"] = true;
            await RunAsync(program, "--background");
            Require((await ReadStatusAsync(server.Root))["ProcessId"]!.GetValue<int>() == workerId, "Keep one installed background instance.");
            int writes = server.WriteCount;
            worker.Refresh();
            TimeSpan cpu = worker.TotalProcessorTime;
            await Task.Delay(10000);
            worker.Refresh();
            Require(writes == server.WriteCount, "Idle monitoring performs zero sorting writes.");
            report["idleCpuMilliseconds"] = (worker.TotalProcessorTime - cpu).TotalMilliseconds;
            report["idleWorkingSetBytes"] = worker.WorkingSet64;
            report["idleSortingWrites"] = 0;
            await RunAsync(program, "--exit");
            settingsStore.Save(new ToolSettings { Language = "en", AutomaticNameSorting = false, ProjectSortMode = 2, ChatSortMode = 5 });
            await RunAsync(program, "--capture", Path.Combine(output, "03-manual-preferences.png"));
            int previousWorker = (await ReadStatusAsync(server.Root))["ProcessId"]!.GetValue<int>();
            await RunAsync(installer, "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/SP-", "/TASKS=", "/DIR=" + directory);
            Require(!IsRunning(previousWorker), "Finish the old background before replacing its files.");
            ToolSettings retained = settingsStore.Load();
            Require(retained.Language == "en" && !retained.AutomaticNameSorting && retained.ProjectSortMode == 2 && retained.ChatSortMode == 5, "Keep existing tool preferences across upgrade.");
            report["runningUpgradeAndSettingsRetention"] = true;
            await RunAsync(program, "--background");
            using (var registration = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{57DA4787-4BE0-43CB-9BFC-FE83679E370F}_is1"))
            {
                string uninstaller = (registration?.GetValue("UninstallString") as string)?.Trim('"')
                    ?? throw new InvalidOperationException("The installed uninstaller registration is missing.");
                await RunAsync(uninstaller, "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART");
            }
            Require(!File.Exists(program), "Remove the installed program during uninstall.");
            Require(File.Exists(settingsStore.PathName), "Retain user preferences for a later installation.");
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

    private static bool IsRunning(int processId)
    {
        try { using Process process = Process.GetProcessById(processId); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static bool IsProgramRunning(string program)
    {
        foreach (Process process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(program)))
        {
            using (process)
            {
                IntPtr handle = OpenProcess(0x1000, false, process.Id);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    int size = 32768;
                    var image = new StringBuilder(size);
                    if (QueryFullProcessImageName(handle, 0, image, ref size)
                        && string.Equals(image.ToString(), program, StringComparison.OrdinalIgnoreCase)) return true;
                }
                finally { CloseHandle(handle); }
            }
        }
        return false;
    }

    private static async Task<JsonObject> ReadStatusAsync(string dataRoot)
    {
        string identity = "sst-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataRoot).ToUpperInvariant())))[..12];
        await using var pipe = new NamedPipeClientStream(".", identity + "-bg", PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipe.ConnectAsync(10000);
        using var writer = new StreamWriter(pipe, leaveOpen:true) { AutoFlush = true };
        await writer.WriteLineAsync("status");
        using var reader = new StreamReader(pipe, leaveOpen:true);
        return JsonNode.Parse((await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)))!)!.AsObject();
    }

    private static async Task RunAsync(string executable, params string[] arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        Task<string> errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(120));
        Require(process.ExitCode == 0, Path.GetFileName(executable) + " " + string.Join(" ", arguments) + " returned " + process.ExitCode + " " + await errors);
    }

    private static async Task WaitAsync(Func<bool> condition, string failure)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(40);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(100);
        Require(condition(), failure);
    }

    private static void Require(bool condition, string failure)
    {
        if (!condition) throw new InvalidOperationException(failure);
    }
}

