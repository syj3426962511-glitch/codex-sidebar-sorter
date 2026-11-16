using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SidebarSorter;

internal static class DesktopSmokeTests
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
            throw new InvalidOperationException("Desktop acceptance runs on disposable cloud hosts.");
        string output = Path.GetFullPath(arguments[0]);
        Directory.CreateDirectory(output);
        using var server = new FixtureNativeServer();
        Environment.SetEnvironmentVariable("CODEX_HOME", server.Root);
        new ToolSettingsStore().Save(new ToolSettings { Language = "zh-CN" });
        var report = new JsonObject
        {
            ["platform"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ["dataKind"] = "fictional native-protocol fixture",
            ["initialProjectCount"] = 30, ["initialChatCount"] = 360
        };
        int result = 1;
        AppBuilder.Configure<DesktopApp>().UsePlatformDetect().AfterSetup(_ =>
            Dispatcher.UIThread.Post(async () =>
            {
                DesktopApp app = DesktopApp.Instance!;
                try
                {
                    await app.VerifyCloudAsync(server, report, output);
                    report["success"] = true;
                    result = 0;
                }
                catch (Exception error) { report["success"] = false; report["error"] = error.ToString(); }
                finally
                {
                    report["checkedAt"] = DateTimeOffset.UtcNow.ToString("O");
                    File.WriteAllText(Path.Combine(output, "desktop-results.json"), report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    await app.ExitAsync();
                }
            })).StartWithClassicDesktopLifetime([]);
        Console.WriteLine(report.ToJsonString());
        return result;
    }

    internal static async Task WaitForAsync(Func<bool> condition, string message)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(35);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(40);
        Require(condition(), message);
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed partial class DesktopApp
{
    public async Task VerifyCloudAsync(FixtureNativeServer server, JsonObject report, string output)
    {
        await DesktopSmokeTests.WaitForAsync(() => window?.CloudLoaded == true && server.IsSortedByName(), "Startup must automatically sort projects and their chats by name.");
        report["initialAutomaticNameOrder"] = true;
        await window!.VerifyCloudUiAsync(server, report, output);

        Settings = Settings with { AutomaticNameSorting = true, ProjectSortMode = 0, ChatSortMode = 0 };
        SaveSettings(Settings);
        string project = server.Projects[0].Id;
        server.RenameProject(project, "AAAA renamed project");
        server.RenameChat(project, server.Chats[project][0].Id, "AAAA renamed chat");
        await DesktopSmokeTests.WaitForAsync(server.IsSortedByName, "Renaming must automatically update the native order.");
        report["renamedItemsAutomaticallyReordered"] = true;
        string addedProject = server.AddProject("000 new project");
        server.AddChat(addedProject, "Zulu new chat");
        server.AddChat(addedProject, "Alpha new chat");
        server.AddChat(project, "000 new chat");
        await DesktopSmokeTests.WaitForAsync(server.IsSortedByName, "New projects and chats must automatically join name order.");
        report["newItemsAutomaticallyReordered"] = true;
        await Task.Delay(2000);
        int writes = server.WriteCount;
        using Process current = Process.GetCurrentProcess();
        TimeSpan cpu = current.TotalProcessorTime;
        var elapsed = Stopwatch.StartNew();
        window.Close();
        await DesktopSmokeTests.WaitForAsync(() => window == null, "Closing releases the desktop window.");
        await Task.Delay(10000);
        current.Refresh();
        report["idleSeconds"] = elapsed.Elapsed.TotalSeconds;
        report["idleCpuMilliseconds"] = (current.TotalProcessorTime - cpu).TotalMilliseconds;
        report["backgroundWorkingSetBytes"] = current.WorkingSet64;
        DesktopSmokeTests.Require(server.WriteCount == writes, "Idle watching must perform zero native writes.");
        DesktopSmokeTests.Require(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime, "The application stays running after closing its window.");
        report["closeKeepsBackgroundRunning"] = true;
        report["idleNativeWrites"] = 0;
        server.AddChat(project, "0000 background new chat");
        await DesktopSmokeTests.WaitForAsync(server.IsSortedByName, "Automatic sorting continues with the window closed.");
        report["automaticSortingWhileWindowClosed"] = true;
        ShowWindow();
        await DesktopSmokeTests.WaitForAsync(() => window?.CloudLoaded == true, "The desktop window can reopen.");
        window!.SaveScreenshot(Path.Combine(output, "desktop-reopened.png"));
        report["windowReopened"] = true;
    }
}

internal sealed partial class DesktopWindow
{
    internal bool CloudLoaded => workspace != null && !busy;

    public async Task VerifyCloudUiAsync(FixtureNativeServer server, JsonObject report, string output)
    {
        DesktopSmokeTests.Require(workspace?.Groups.Count == 30 && workspace.ChatCount == 360, "Load all standalone chats and exclude attached children.");
        DesktopSmokeTests.Require(ProjectMode == 0 && ChatMode == 0 && app.Settings.AutomaticNameSorting, "The initial settings must automatically sort both layers by name.");
        await Task.Delay(500);
        SaveScreenshot(Path.Combine(output, "desktop-chinese.png"));
        Box("LanguageBox").SelectedIndex = 2;
        DesktopSmokeTests.Require(!Localization.IsChinese && Button("ApplyButton").Content?.ToString() == "Apply to sidebar", "The English switch updates captions immediately.");
        await Task.Delay(200);
        SaveScreenshot(Path.Combine(output, "desktop-english.png"));
        Width = 1000;
        Height = 720;
        await Task.Delay(200);
        SaveScreenshot(Path.Combine(output, "desktop-english-compact.png"));
        Width = 1240;
        Height = 860;
        report["bilingualUiAndCompactRendering"] = true;

        Box("ProjectModeBox").SelectedIndex = 1;
        Box("ChatModeBox").SelectedIndex = 1;
        DesktopSmokeTests.Require(!app.Settings.AutomaticNameSorting, "A custom rule pauses automatic name sorting.");
        await RunAsync(ApplyAsync);
        DesktopSmokeTests.Require(Label("StatusText").Text == "Applied to the Codex sidebar" && !server.IsSortedByName(), "The normal application handler applies the chosen descending order.");
        int writes = server.WriteCount;
        await RunAsync(ApplyAsync);
        DesktopSmokeTests.Require(server.WriteCount == writes, "An unchanged application makes no native writes.");
        report["manualRulePausesAutomaticSorting"] = true;
        report["repeatedApplyMakesZeroWrites"] = true;
        RowList.SelectedIndex = 0;
        MoveSelected(1, false);
        DesktopSmokeTests.Require(ProjectMode == 6, "Manual movement selects manual mode.");
        Restore();
        report["manualMoveAndRestore"] = true;
        Box("LanguageBox").SelectedIndex = 1;
        Check("AutomaticBox").IsChecked = true;
        await DesktopSmokeTests.WaitForAsync(server.IsSortedByName, "Re-enabling automatic sorting restores ascending names.");
        await Task.Delay(1000);
        await RefreshAsync();
        SaveScreenshot(Path.Combine(output, "desktop-automatic-restored.png"));
        report["automaticSortingResumes"] = true;
    }
}
