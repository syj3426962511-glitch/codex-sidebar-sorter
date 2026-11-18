using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SidebarSorter;

internal static class WindowsUiTests
{
    [STAThread]
    private static int Main(string[] arguments)
    {
        string output = Path.GetFullPath(arguments[0]);
        Directory.CreateDirectory(output);
        using var server = new FixtureNativeServer();
        Environment.SetEnvironmentVariable("CODEX_HOME", server.Root);
        new ToolSettingsStore().Save(new ToolSettings { Language = "zh-CN" });
        ApplicationConfiguration.Initialize();
        Application.SetDefaultFont(new Font("SimSun", 12));
        using var form = new MainForm(null) { Opacity = 0, ShowInTaskbar = false };
        int exitCode = 1;
        form.Shown += async (_, _) =>
        {
            var report = new JsonObject { ["dataKind"] = "fictional native-protocol fixture", ["projectCount"] = 30, ["chatCount"] = 360 };
            try
            {
                await form.VerifyMultilingualUiAsync(server, report, output);
                report["success"] = true;
                exitCode = 0;
            }
            catch (Exception error) { report["success"] = false; report["error"] = error.ToString(); }
            report["checkedAt"] = DateTimeOffset.UtcNow.ToString("O");
            File.WriteAllText(Path.Combine(output, "ui-results.json"), report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            form.Close();
        };
        Application.Run(form);
        return exitCode;
    }
}

internal sealed partial class MainForm
{
    public async Task VerifyMultilingualUiAsync(FixtureNativeServer server, JsonObject report, string output)
    {
        while (busy) await Task.Delay(30);
        Size normalClientSize = new(Math.Min(1008, ClientSize.Width), Math.Min(730, ClientSize.Height));
        ClientSize = normalClientSize;
        PerformLayout();
        RequireUi(workspace != null && workspace.Problems.Count == 0, "The UI must load the fixture through the production native bridge.");
        RequireUi(workspace!.Groups.Count == 30 && workspace.ChatCount == 360, "Complete project/chat counts with attached records excluded.");
        RequireUi(projectOrder.SelectedIndex == 0 && chatOrder.SelectedIndex == 0 && applyChatOrder.Checked, "Both layers default to ascending names.");
        report["bothLayersDefaultNameAscending"] = true;
        RequireUi(Localization.IsChinese && refresh.Text == "Refresh", "Initial Chinese UI.");
        SaveCloudUi(Path.Combine(output, "ui-chinese-before-apply.png"));
        string[] titles = workspace.Groups.SelectMany(group => group.Chats).Select(row => row.Title).ToArray();
        language.SelectedIndex = 2;
        while (busy) await Task.Delay(30);
        RequireUi(!Localization.IsChinese && refresh.Text == "Refresh" && apply.Text == "Apply to sidebar", "Immediate English switch.");
        RequireUi(workspace.Groups.SelectMany(group => group.Chats).Select(row => row.Title).SequenceEqual(titles), "Original project/chat labels survive language switching.");
        report["immediateEnglishSwitch"] = true;
        SaveCloudUi(Path.Combine(output, "ui-english-before-apply.png"));
        await RunAsync(ApplyPreviewAsync);
        RequireUi(status.Text == "Applied to the Codex sidebar" && server.IsSortedByName(), "Normal Apply entry point and native request order.");
        report["thirtyProjectsAnd360ChatsApplied"] = true;
        int writes = server.WriteCount;
        await RunAsync(ApplyPreviewAsync);
        RequireUi(server.WriteCount == writes && status.Text == "The requested order is already applied", "A repeated application makes zero writes.");
        report["unchangedApplyWritesNothing"] = true;
        SaveCloudUi(Path.Combine(output, "ui-english-after-apply.png"));
        ClientSize = new Size(1008, 730);
        PerformLayout();
        RequireUi(tree.Columns().Time < tree.ClientSize.Width - 120, "The compact view retains the last-updated column.");
        RequireUi(TextRenderer.MeasureText(bottom.Text, bottom.Font).Width < bottom.ClientSize.Width, "The compact move button caption fits.");
        SaveCloudUi(Path.Combine(output, "ui-english-compact.png"));
        report["compactEnglishLayout"] = true;
        ClientSize = normalClientSize;
        PerformLayout();
        projectOrder.SelectedIndex = 2;
        chatOrder.SelectedIndex = 5;
        language.SelectedIndex = 1;
        while (busy) await Task.Delay(30);
        RequireUi(Localization.IsChinese && projectOrder.SelectedIndex == 2 && chatOrder.SelectedIndex == 5, "Chinese switch retains current sort choices.");
        RequireUi(new ToolSettingsStore().Load().Language == "zh-CN", "Language selection is saved in tool-owned settings.");
        report["chineseSwitchRetainsSortChoices"] = true;
        SaveCloudUi(Path.Combine(output, "ui-chinese-switched-back.png"));
        report["nativeWriteOperations"] = writes;
    }

    private static void RequireUi(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private void SaveCloudUi(string path)
    {
        PerformLayout();
        using var image = new Bitmap(Width, Height);
        DrawToBitmap(image, new Rectangle(Point.Empty, Size));
        image.Save(path, ImageFormat.Png);
    }
}
