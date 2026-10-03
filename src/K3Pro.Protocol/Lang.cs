namespace K3Pro.Protocol;

public enum UiLanguage { Vi, En }

/// <summary>
/// Language of display strings (app UI + Protocol messages / errors). Defaults to Vietnamese — the CLI keeps the default.
/// Both translations sit side by side at the point of use: <c>Lang.T("Đã lưu", "Saved")</c>. For display strings only,
/// not for data saved to files / comparisons.
/// </summary>
public static class Lang
{
    private static volatile UiLanguage _current = UiLanguage.Vi;

    /// <summary>Language changed → the UI refreshes the strings currently shown.</summary>
    public static event Action? Changed;

    public static UiLanguage Current
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            Changed?.Invoke();
        }
    }

    public static bool IsEnglish => _current == UiLanguage.En;

    public static string T(string vi, string en) => _current == UiLanguage.En ? en : vi;

    public static string Code(UiLanguage language) => language == UiLanguage.En ? "en" : "vi";

    /// <summary>OS UI language: Vietnamese → Vi, any other language → En (first run, no language chosen yet).</summary>
    public static UiLanguage FromCulture(System.Globalization.CultureInfo culture) =>
        culture.TwoLetterISOLanguageName.Equals("vi", StringComparison.OrdinalIgnoreCase) ? UiLanguage.Vi : UiLanguage.En;

    /// <summary>"en" / "en-US" → En; anything else (including null) → Vi.</summary>
    public static UiLanguage Parse(string? code) =>
        code is not null && code.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? UiLanguage.En : UiLanguage.Vi;
}
