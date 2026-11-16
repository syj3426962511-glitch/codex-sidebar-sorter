using System.Globalization;
using System.Text.Json;

namespace SidebarSorter;

internal static class Localization
{
    private static readonly Dictionary<string, string> English = ReadEnglish();
    public static string Language { get; private set; } = "system";
    public static bool IsChinese => Language == "zh-CN"
        || Language == "system" && CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    public static void SetLanguage(string language)
    {
        Language = language is "zh-CN" or "en" ? language : "system";
    }

    public static string Text(string source) => IsChinese ? source : English.GetValueOrDefault(source, source);

    public static string SourceText(string text)
    {
        foreach (var pair in English)
            if (pair.Value == text) return pair.Key;
        return text;
    }

    public static bool HasEnglish(string source) => English.ContainsKey(source);

    private static Dictionary<string, string> ReadEnglish()
    {
        using Stream stream = typeof(Localization).Assembly.GetManifestResourceStream("SidebarSorter.English")
            ?? throw new InvalidOperationException("The English language resource is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
}

internal sealed record LanguageChoice(string Code, string Name)
{
    public override string ToString() => Code == "system" ? Localization.Text("跟随系统") : Name;
}
