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
        string root = Path.Combine(Path.GetTempPath(), "sorter-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "session_index.jsonl"), new JsonObject { ["id"] = Guid.NewGuid().ToString() }.ToJsonString());
        Environment.SetEnvironmentVariable("CODEX_HOME", root);
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
}
