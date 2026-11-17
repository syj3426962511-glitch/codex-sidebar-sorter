using System.Text.Json;
using System.Text.Json.Nodes;

namespace SidebarSorter;

internal static class NativeEndpointProbe
{
    private static async Task<int> Main(string[] arguments)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true")
            throw new InvalidOperationException("Use a disposable cloud machine.");
        string output = Path.GetFullPath(arguments[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (!arguments.Contains("--existing-profile"))
        {
            string root = Path.Combine(Path.GetTempPath(), "sorter-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "session_index.jsonl"), new JsonObject { ["id"] = Guid.NewGuid().ToString() }.ToJsonString());
            Environment.SetEnvironmentVariable("CODEX_HOME", root);
        }
        Localization.SetLanguage("en");
        var report = new JsonObject { ["platform"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription };
        try
        {
            report["endpointCount"] = NativeBridge.FindEndpoints().Length;
            using NativeBridge bridge = await NativeBridge.ConnectAsync();
            report["nativeCatalogAccessible"] = true;
            report["toolCount"] = bridge.Tools.Count;
            if (arguments.Contains("--read-lists"))
            {
                JsonObject projects = await bridge.CallAsync("list_projects", new JsonObject());
                JsonObject threads = await bridge.CallAsync("list_threads", new JsonObject { ["limit"] = 50 });
                report["nativeProjectListAccessible"] = projects["projects"] != null;
                report["nativeThreadListAccessible"] = threads["threads"] != null;
            }
            int fixtureArgument = Array.IndexOf(arguments, "--fixture");
            if (fixtureArgument >= 0)
                await VerifyFixtureAsync(arguments[fixtureArgument + 1], report);
            report["success"] = true;
        }
        catch (Exception error)
        {
            report["success"] = false;
            report["error"] = error.ToString();
        }
        report["checkedAt"] = DateTimeOffset.UtcNow.ToString("O");
        File.WriteAllText(output, report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(report.ToJsonString());
        return report["success"]!.GetValue<bool>() ? 0 : 1;
    }

    private static async Task VerifyFixtureAsync(string path, JsonObject report)
    {
        JsonObject fixture = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        HashSet<string> names = fixture["projectNames"]!.AsArray().Select(node => node!.GetValue<string>()).ToHashSet();
        var service = new SorterService();
        List<SortScope> scopes = await service.RefreshAsync();
        List<SortScope> projects = scopes.Where(scope => scope.Kind == "project" && names.Contains(scope.Label)).ToList();
        if (projects.Count != names.Count) throw new InvalidOperationException("The isolated test projects are missing from the official sidebar.");
        var checkedModes = new JsonArray();
        for (int mode = 0; mode < 7; mode++)
        {
            foreach (SortScope scope in projects)
            {
                List<SortRow> rows = await service.GetRowsAsync(scope);
                if (rows.Count != 3) throw new InvalidOperationException("The official project has not loaded its three test chats.");
                if (mode == 6) rows.Reverse();
                else SorterService.Sort(rows, mode);
                await service.ApplyAsync(scope, rows, recentMode: false);
                await service.RefreshAsync();
                List<SortRow> saved = await service.GetRowsAsync(scope);
                if (!saved.Select(row => row.Id).SequenceEqual(rows.Select(row => row.Id)))
                    throw new InvalidOperationException("The saved project-chat order differs for mode " + mode);
            }
            List<SortRow> projectRows = (await service.GetRowsAsync(scopes[0])).Where(row => names.Contains(row.Title)).ToList();
            await service.FillProjectDatesAsync(projectRows);
            if (mode == 6) projectRows.Reverse();
            else SorterService.Sort(projectRows, mode);
            await service.ApplyAsync(scopes[0], projectRows, recentMode: false);
            await service.RefreshAsync();
            List<SortRow> savedProjects = (await service.GetRowsAsync(scopes[0])).Where(row => names.Contains(row.Title)).ToList();
            if (!savedProjects.Select(row => row.Id).SequenceEqual(projectRows.Select(row => row.Id)))
                throw new InvalidOperationException("The saved project order differs for mode " + mode);
            checkedModes.Add(mode);
        }
        foreach (SortScope scope in projects)
        {
            List<SortRow> rows = await service.GetRowsAsync(scope);
            SorterService.Sort(rows, 0);
            await service.ApplyAsync(scope, rows, recentMode: false);
        }
        List<SortRow> ascending = (await service.GetRowsAsync(scopes[0])).Where(row => names.Contains(row.Title)).ToList();
        SorterService.Sort(ascending, 0);
        await service.ApplyAsync(scopes[0], ascending, recentMode: false);
        report["authenticatedFixtureProjectCount"] = projects.Count;
        report["authenticatedFixtureChatCount"] = projects.Count * 3;
        report["savedOrderModesVerified"] = checkedModes;
    }
}
