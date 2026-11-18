using System.Globalization;
using System.Text.Json;

namespace SidebarSorter;

internal static class Localization
{
    private static readonly Dictionary<string, string> English = ReadCatalog("SidebarSorter.English");
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = ReadCatalogs();
    public static IReadOnlyList<LanguageChoice> Choices { get; } = new LanguageChoice[]
    {
        new("system", ""), new("zh-CN", "\u7b80\u4f53\u4e2d\u6587"), new("en", "English"), new("zh-TW", "\u7e41\u9ad4\u4e2d\u6587"),
        new("ja", "\u65e5\u672c\u8a9e"), new("ko", "한국어"), new("fr", "Français"), new("de", "Deutsch"),
        new("es", "Español"), new("pt", "Português"), new("it", "Italiano"), new("ru", "Русский"),
        new("ar", "العربية")
    };
    public static string Language { get; private set; } = "en";
    public static string CurrentLanguage => Language == "system" ? ResolveCulture(CultureInfo.CurrentUICulture.Name) : Language;
    public static bool IsChinese => CurrentLanguage.StartsWith("zh", StringComparison.Ordinal);
    public static bool IsRightToLeft => CurrentLanguage == "ar";

    public static void SetLanguage(string language)
    {
        Language = NormalizeLanguage(language);
    }

    public static string NormalizeLanguage(string language) => Choices.Any(choice => choice.Code == language) ? language : "en";

    internal static string ResolveCulture(string culture)
    {
        string name = culture.Replace('_', '-').ToLowerInvariant();
        if (name.StartsWith("zh", StringComparison.Ordinal))
            return name.Contains("hant", StringComparison.Ordinal) || name is "zh-tw" or "zh-hk" or "zh-mo" ? "zh-TW" : "zh-CN";
        string language = name.Split('-')[0];
        return Catalogs.ContainsKey(language) ? language : "en";
    }

    public static string Text(string source)
    {
        string language = CurrentLanguage;
        return Catalogs[language].GetValueOrDefault(source, English.GetValueOrDefault(source, source));
    }

    public static string SourceText(string text)
    {
        foreach (Dictionary<string, string> catalog in Catalogs.Values)
            foreach (var pair in catalog)
                if (pair.Value == text) return pair.Key;
        return text;
    }

    public static bool HasEnglish(string source) => English.ContainsKey(source);
    internal static bool HasTranslation(string language, string source) => Catalogs[language].ContainsKey(source);

    private static Dictionary<string, string> ReadCatalog(string resource)
    {
        using Stream stream = typeof(Localization).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("A language resource is missing.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }

    private static Dictionary<string, Dictionary<string, string>> ReadCatalogs()
    {
        using Stream stream = typeof(Localization).Assembly.GetManifestResourceStream("SidebarSorter.Languages")
            ?? throw new InvalidOperationException("The language resources are missing.");
        var catalogs = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream)!;
        catalogs.Add("en", English);
        return catalogs;
    }
}

internal sealed record LanguageChoice(string Code, string Name)
{
    public override string ToString() => Code == "system" ? Localization.Text("System language") : Name;
}
