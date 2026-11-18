using System.Diagnostics;
using System.Text.Json.Nodes;

namespace SidebarSorter;

internal static class Program
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        bool relaunched = arguments.Contains(IndependentLaunch.RelaunchArgument);
        arguments = arguments.Where(argument => argument != IndependentLaunch.RelaunchArgument).ToArray();
        try
        {
            int? exitCode = IndependentLaunch.HandoffIfNeeded(arguments, relaunched);
            if (exitCode != null) return exitCode.Value;
        }
        catch (Exception error)
        {
            if (arguments.Length == 2)
                File.WriteAllText(arguments[1], new JsonObject { ["success"] = false, ["error"] = error.Message }.ToJsonString());
            else
                MessageBox.Show(error.Message, Localization.Text("Codex Sidebar Sorter"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }
        ApplicationConfiguration.Initialize();
        Application.SetDefaultFont(new Font("SimSun", 12));
        if (arguments.Length == 2 && arguments[0] is "--verify-live" or "--read-check")
        {
            try { VerifyAsync(arguments[0] == "--verify-live", arguments[1]).GetAwaiter().GetResult(); return 0; }
            catch (Exception error)
            {
                File.WriteAllText(arguments[1], new JsonObject { ["success"] = false, ["error"] = error.Message }.ToJsonString());
                return 1;
            }
        }
        string? capture = arguments.Length == 2 && arguments[0] == "--capture" ? arguments[1] : null;
        if (capture != null)
        {
            Application.Run(new MainForm(capture));
            return 0;
        }
        if (arguments.Length == 1 && arguments[0] == "--ui")
        {
            Application.Run(new MainForm(null, true));
            return 0;
        }
        try
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, "\u804a\u5929\u6392\u5e8f\u540e\u53f0.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory
            };
            if (arguments.Length == 1 && arguments[0] is "--background" or "--exit") start.ArgumentList.Add(arguments[0]);
            using Process? background = Process.Start(start);
        }
        catch (Exception error)
        {
            MessageBox.Show(Localization.Text("The background component could not start. Update the tool using its installer.\n") + error.Message, Localization.Text("Codex Sidebar Sorter"),
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }
        return 0;
    }

    private static async Task VerifyAsync(bool writeCheck, string reportPath)
    {
        var service = new SorterService();
        Stopwatch stopwatch = Stopwatch.StartNew();
        List<SortScope> scopes = await service.RefreshAsync();
        SortScope projects = scopes.First(scope => scope.Kind == "projects");
        List<SortRow> original = await service.GetRowsAsync(projects);
        JsonObject? originalSorting = service.Snapshot["sidebarPreferences"]?["sorting"]?.DeepClone().AsObject();
        long readMilliseconds = stopwatch.ElapsedMilliseconds;
        bool sortedVerified = false;
        bool restoredVerified = false;
        bool preferencesRestored = false;
        if (writeCheck && original.Count > 1)
        {
            var sorted = original.ToList();
            SorterService.Sort(sorted, 0);
            try
            {
                await service.ApplyWorkspaceAsync(sorted, true, [], false);
                await service.RefreshAsync();
                List<SortRow> actual = await service.GetRowsAsync(projects);
                sortedVerified = actual.Select(row => row.Id).SequenceEqual(sorted.Select(row => row.Id));
                if (!sortedVerified) throw new InvalidOperationException(Localization.Text("The saved project order differs from the requested order."));
            }
            finally
            {
                await service.ApplyWorkspaceAsync(original, true, [], false);
                await service.RefreshAsync();
                List<SortRow> restored = await service.GetRowsAsync(projects);
                restoredVerified = restored.Select(row => row.Id).SequenceEqual(original.Select(row => row.Id));
                if (!restoredVerified) throw new InvalidOperationException(Localization.Text("Restoring the original project order failed verification."));
                if (originalSorting != null)
                {
                    using NativeBridge bridge = await NativeBridge.ConnectAsync();
                    await bridge.CallAsync("update_sidebar_preferences", new JsonObject { ["sorting"] = originalSorting.DeepClone() });
                    await service.RefreshAsync();
                    preferencesRestored = JsonNode.DeepEquals(service.Snapshot["sidebarPreferences"]?["sorting"], originalSorting);
                    if (!preferencesRestored) throw new InvalidOperationException(Localization.Text("Restoring the original sort preferences failed verification."));
                }
            }
        }
        SortScope? populated = scopes.FirstOrDefault(scope => scope.Kind == "project");
        int projectChatCount = populated == null ? 0 : (await service.GetRowsAsync(populated)).Count;
        CatalogResult catalog = await service.ReadCatalogAsync();
        var report = new JsonObject
        {
            ["success"] = true,
            ["projectCount"] = original.Count,
            ["scopeCount"] = scopes.Count,
            ["projectChatReadCount"] = projectChatCount,
            ["catalogChatCount"] = catalog.Chats.Count,
            ["catalogProblems"] = System.Text.Json.JsonSerializer.SerializeToNode(catalog.Problems),
            ["localHistoryDatabaseReadOnly"] = service.LocalHistoryReadOnly,
            ["readMilliseconds"] = readMilliseconds,
            ["sortAndRestoreTest"] = writeCheck,
            ["nativeSortedOrderVerified"] = sortedVerified,
            ["originalOrderRestored"] = restoredVerified,
            ["originalPreferencesRestored"] = preferencesRestored,
            ["totalMilliseconds"] = stopwatch.ElapsedMilliseconds,
            ["inheritedPipeEnvironment"] = Environment.GetEnvironmentVariable("CODEX_APP_TOOLS_PIPE_PATH") != null,
            ["inheritedThreadEnvironment"] = Environment.GetEnvironmentVariable("CODEX_THREAD_ID") != null,
            ["processInWindowsJob"] = IndependentLaunch.IsInJob(),
            ["processSessionId"] = Process.GetCurrentProcess().SessionId,
            ["runtimeDirectory"] = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),
            ["applicationDirectory"] = AppContext.BaseDirectory,
            ["checkedAt"] = DateTimeOffset.Now.ToString("O")
        };
        File.WriteAllText(reportPath, report.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
