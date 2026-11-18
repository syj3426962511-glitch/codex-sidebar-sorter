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
        if (arguments[0].StartsWith("--", StringComparison.Ordinal)) return DesktopProgram.Run(arguments);
        return RunDesktopTests(arguments);
    }

    private static int RunDesktopTests(string[] arguments)
    {
        string output = Path.GetFullPath(arguments[0]);
        Directory.CreateDirectory(output);
        using var server = new FixtureNativeServer();
        Environment.SetEnvironmentVariable("CODEX_HOME", server.Root);
        new ToolSettingsStore().Save(new ToolSettings { Language = "zh-CN" });
        File.WriteAllText(new ToolSettingsStore().PathName, "{\"language\":\"zh-CN\",\"projectSortMode\":2,\"chatSortMode\":5}");
        BackgroundClient.EnsureRunningAsync().GetAwaiter().GetResult();
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
                    File.WriteAllText(Path.Combine(output, "desktop-results.json"), report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                    await app.ExitAsync();
                }
            })).StartWithClassicDesktopLifetime([]);
        SynchronizationContext.SetSynchronizationContext(null);
        Console.WriteLine("Desktop closed. Verifying the independent background process.");
        try
        {
            if (result == 0) VerifyBackgroundAsync(server, report, output).GetAwaiter().GetResult();
        }
        catch (Exception error) { report["success"] = false; report["error"] = error.ToString(); result = 1; }
        finally
        {
            BackgroundClient.NotifyAsync("exit").GetAwaiter().GetResult();
            using var worker = new Mutex(false, BackgroundClient.Identity + "-bg");
            try { Require(worker.WaitOne(TimeSpan.FromSeconds(60)), "The independent worker exits safely after its current operation."); }
            catch (AbandonedMutexException) { }
            worker.ReleaseMutex();
            report["backgroundExitWaited"] = true;
            report["checkedAt"] = DateTimeOffset.UtcNow.ToString("O");
            File.WriteAllText(Path.Combine(output, "desktop-results.json"), report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
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

    private static async Task VerifyBackgroundAsync(FixtureNativeServer server, JsonObject report, string output)
    {
        BackgroundStatus status = await BackgroundClient.ReadStatusAsync();
        await BackgroundClient.NotifyAsync("compact");
        using Process background = Process.GetProcessById(status.ProcessId);
        BackgroundClient.Launch("--background");
        await Task.Delay(1500);
        Require((await BackgroundClient.ReadStatusAsync()).ProcessId == background.Id, "Background startup preserves a single worker.");
        int writes = server.WriteCount;
        TimeSpan cpu = background.TotalProcessorTime;
        var elapsed = Stopwatch.StartNew();
        await Task.Delay(10000);
        background.Refresh();
        report["idleSeconds"] = elapsed.Elapsed.TotalSeconds;
        report["idleCpuMilliseconds"] = (background.TotalProcessorTime - cpu).TotalMilliseconds;
        report["backgroundWorkingSetBytes"] = background.WorkingSet64;
        Require(server.WriteCount == writes, "Idle watching must perform zero native writes.");
        Require(!background.HasExited, "Closing the UI leaves its independent worker running.");
        report["closeKeepsBackgroundRunning"] = true;
        report["idleNativeWrites"] = 0;
        string project = server.Projects[0].Id;
        server.AddChat(project, "0000 background new chat");
        await WaitForAsync(server.IsSortedByName, "Automatic sorting continues with the UI process closed.");
        report["automaticSortingWhileWindowClosed"] = true;
        var capture = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        capture.ArgumentList.Add("--capture");
        capture.ArgumentList.Add(Path.Combine(output, "desktop-reopened.png"));
        using Process gui = Process.Start(capture)!;
        await gui.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(90));
        Require(gui.ExitCode == 0 && File.Exists(Path.Combine(output, "desktop-reopened.png")), "A separate UI process can reopen, render, and close normally.");
        Require((await BackgroundClient.ReadStatusAsync()).ProcessId == background.Id, "Reopening the UI retains the same independent worker.");
        report["windowReopenedInSeparateProcess"] = true;
        report["backgroundSingleInstance"] = true;
    }
}

internal sealed partial class DesktopApp
{
    public async Task VerifyCloudAsync(FixtureNativeServer server, JsonObject report, string output)
    {
        await DesktopSmokeTests.WaitForAsync(() => window?.CloudLoaded == true && server.IsSortedByName(), "Startup must automatically sort projects and their chats by name.");
        report["initialAutomaticNameOrder"] = true;
        report["olderSettingsAdoptAutomaticNameOrder"] = true;
        await DesktopSmokeTests.WaitForAsync(server.AreSectionsSortedByName, "Pinned and custom-section chats must also keep ascending names.");
        report["pinnedAndCustomSectionsSortedByName"] = true;
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
        var languages = new JsonArray();
        foreach (LanguageChoice choice in Localization.Choices.Where(choice => choice.Code != "system"))
        {
            Box("LanguageBox").SelectedItem = choice;
            DesktopSmokeTests.Require(app.Settings.Language == choice.Code && app.SettingsStore.Load().Language == choice.Code, "Language selection is persisted.");
            DesktopSmokeTests.Require(Button("ApplyButton").Content?.ToString() == Localization.Text("Apply to sidebar"), "Captions update in " + choice.Code);
            DesktopSmokeTests.Require(FlowDirection == (choice.Code == "ar" ? Avalonia.Media.FlowDirection.RightToLeft : Avalonia.Media.FlowDirection.LeftToRight), "Text direction follows the selected language.");
            DesktopSmokeTests.Require(ProjectMode == 0 && ChatMode == 0 && app.Settings.AutomaticNameSorting, "Language changes retain automatic name sorting.");
            await Task.Delay(100);
            languages.Add(choice.Code);
        }
        report["languagesRendered"] = languages;
        Box("LanguageBox").SelectedIndex = 2;

        ComboBox systemBox = Box("SystemBox");
        DesktopSmokeTests.Require(systemBox.SelectedIndex == 0 &&
            systemBox.SelectedItem?.ToString() == $"Auto ({NativeBridge.SystemName(NativeBridge.CurrentSystem)})",
            "The desktop opens with the detected native system.");
        var selectedSystems = new JsonArray();
        for (int index = 1; index < Systems.Length; index++)
        {
            ClientSystem candidate = Systems[index]!.Value;
            if (candidate == NativeBridge.CurrentSystem) continue;
            systemBox.SelectedIndex = index;
            DesktopSmokeTests.Require(SelectedSystem == candidate && workspace == null && RowList.Items.Count == 0
                && !Button("ApplyButton").IsEnabled && !Button("RefreshButton").IsEnabled
                && Label("StatusText").Text == NativeBridge.LocalSystemPrompt(), "Switching away clears the preview and disables native writes.");
            selectedSystems.Add(new JsonObject { ["system"] = NativeBridge.SystemName(candidate), ["connected"] = false });
        }
        await Task.Delay(200);
        SaveScreenshot(Path.Combine(output, "desktop-other-system.png"));
        systemBox.SelectedIndex = Array.FindIndex(Systems, system => system == NativeBridge.CurrentSystem);
        await DesktopSmokeTests.WaitForAsync(() => !busy && workspace?.ChatCount == 360, "The matching system reloads the native projects and chats.");
        selectedSystems.Add(new JsonObject { ["system"] = NativeBridge.SystemName(SelectedSystem), ["connected"] = true });
        report["selectedSystems"] = selectedSystems;
        report["explicitSystemReconnected"] = true;
        await Task.Delay(200);
        DesktopSmokeTests.Require(Label("StatusText").Text is "Connected" or "Keep names sorted A to Z",
            "The selected system keeps the current English status after switching from other languages.");
        report["selectedSystemStatusInEnglish"] = true;
        SaveScreenshot(Path.Combine(output, "desktop-system-selected.png"));

        Box("ProjectModeBox").SelectedIndex = 1;
        Box("ChatModeBox").SelectedIndex = 1;
        DesktopSmokeTests.Require(!app.Settings.AutomaticNameSorting, "A custom rule pauses automatic name sorting.");
        await RunAsync(ApplyAsync);
        DesktopSmokeTests.Require(Label("StatusText").Text == "Applied to the Codex sidebar" && !server.IsSortedByName(), "The normal application handler applies the chosen descending order.");
        report["selectedSystemSorted"] = true;
        int writes = server.WriteCount;
        await RunAsync(ApplyAsync);
        DesktopSmokeTests.Require(server.WriteCount == writes, "An unchanged application makes no native writes.");
        server.UnavailableTool = "move_thread_to_project";
        try
        {
            var service = new SorterService();
            List<SortScope> scopes = await service.RefreshAsync();
            SortScope projectScope = scopes.First(scope => scope.Kind == "project");
            List<SortRow> projectRows = await service.GetRowsAsync(scopes[0]);
            List<SortRow> chatRows = (await service.GetRowsAsync(projectScope)).Where(row => !row.IsPinned).ToList();
            bool rejected = false;
            try { await service.ApplyWorkspaceAsync(projectRows, true, [new(projectScope, chatRows)], false); }
            catch (InvalidOperationException error) when (error.Message.Contains("move_thread_to_project", StringComparison.Ordinal)) { rejected = true; }
            DesktopSmokeTests.Require(rejected && server.WriteCount == writes, "A missing chat operation must be detected before any project write.");
            report["missingOperationPreflightPreservesOrder"] = true;
        }
        finally { server.UnavailableTool = null; }
        {
            var service = new SorterService();
            List<SortScope> scopes = await service.RefreshAsync();
            SortScope scope = scopes.First(item => item.Kind == "project" && item.Id != server.Projects[0].Id);
            List<SortRow> projectRows = await service.GetRowsAsync(scopes[0]);
            List<SortRow> staleRows = await service.GetRowsAsync(scope);
            server.AddChat(scope.Id, "New review item");
            bool rejected = false;
            try { await service.ApplyWorkspaceAsync(projectRows, true, [new(scope, staleRows)], false); }
            catch (InvalidOperationException error) when (error.Message == "Chat membership changed. Refresh the list and apply again.") { rejected = true; }
            DesktopSmokeTests.Require(rejected && server.WriteCount == writes, "Adding a chat invalidates the whole pending write plan.");
            report["stalePreviewPreflightPreservesOrder"] = true;
            await RefreshAsync();
            Box("ScopeBox").SelectedItem = this.scopes.First(item => item.Id == scope.Id);
            await DesktopSmokeTests.WaitForAsync(() => loadedScope?.Id == scope.Id && !busy, "Load the project scope.");
            Box("ChatModeBox").SelectedIndex = 4;
            string[] expected = Groups().Single().Chats.Select(SorterService.RowKey).ToArray();
            await RunAsync(ApplyAsync);
            await service.RefreshAsync();
            var saved = await service.GetRowsAsync(scope);
            DesktopSmokeTests.Require(saved.Select(SorterService.RowKey).SequenceEqual(expected), "Most recent mode persists the displayed preview.");
            report["newestOrderMatchesSavedPreview"] = true;
            Box("ScopeBox").SelectedIndex = 0;
            await DesktopSmokeTests.WaitForAsync(() => loadedScope?.Kind == "projects" && !busy, "Return to the project list.");
        }
        report["manualRulePausesAutomaticSorting"] = true;
        report["repeatedApplyMakesZeroWrites"] = true;
        RowList.SelectedIndex = 0;
        MoveSelected(1, false);
        DesktopSmokeTests.Require(ProjectMode == 6, "Manual movement selects manual mode.");
        Restore();
        report["manualMoveAndRestore"] = true;
        Box("LanguageBox").SelectedIndex = 2;
        Check("AutomaticBox").IsChecked = true;
        await DesktopSmokeTests.WaitForAsync(server.IsSortedByName, "Re-enabling automatic sorting restores ascending names.");
        await Task.Delay(1000);
        await RefreshAsync();
        SaveScreenshot(Path.Combine(output, "desktop-automatic-restored.png"));
        report["automaticSortingResumes"] = true;
        systemBox.SelectedIndex = 0;
        await DesktopSmokeTests.WaitForAsync(() => !busy && workspace != null, "Selecting automatic detection reconnects to the native system.");
        report["returnedToAutomaticSystem"] = true;

        server.IncludePagedHistory = true;
        try
        {
            await RefreshAsync();
            ProjectGroup? history = workspace?.Groups.SingleOrDefault(group => group.Scope.Id == FixtureNativeServer.HistoryProjectId);
            IEnumerable<string> expected = Enumerable.Range(1, FixtureNativeServer.HistoryChatCount).Select(index => $"history-{index}");
            DesktopSmokeTests.Require(history?.IsLoaded == true && history.Chats.Select(row => row.Id).SequenceEqual(expected),
                "Refresh must use the server page size and load every active history page.");
            report["pagedHistoryChatsRead"] = history!.Chats.Count;
            TextBox search = this.FindControl<TextBox>("Search")!;
            search.Text = history.Row.Title;
            try
            {
                await Task.Delay(200);
                SaveScreenshot(Path.Combine(output, "desktop-paged-history.png"));
            }
            finally { search.Text = ""; }
        }
        finally { server.IncludePagedHistory = false; }
    }
}
