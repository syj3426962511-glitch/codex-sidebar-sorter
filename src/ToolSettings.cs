using System.Text;
using System.Text.Json;

namespace SidebarSorter;

internal sealed record ToolSettings
{
    public string Language { get; init; } = "system";
    public bool AutomaticNameSorting { get; init; } = true;
    public int ProjectSortMode { get; init; }
    public int ChatSortMode { get; init; }
}

internal sealed class ToolSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexSidebarSorter");
    private readonly string directory;
    public ToolSettingsStore(string? directory = null) => this.directory = directory ?? DefaultDirectory;
    public string PathName => Path.Combine(directory, "settings.json");

    public ToolSettings Load()
    {
        if (!File.Exists(PathName)) return new ToolSettings();
        using var input = new FileStream(PathName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        ToolSettings settings = JsonSerializer.Deserialize<ToolSettings>(input, Options)
            ?? throw new InvalidDataException("The tool settings file is empty.");
        return settings with
        {
            Language = Localization.NormalizeLanguage(settings.Language),
            ProjectSortMode = Math.Clamp(settings.ProjectSortMode, 0, 6),
            ChatSortMode = Math.Clamp(settings.ChatSortMode, 0, 6)
        };
    }

    public void Save(ToolSettings settings)
    {
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options), new UTF8Encoding(false));
            File.Move(temporary, PathName, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
