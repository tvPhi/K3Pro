using System.Globalization;
using System.Text.Json;

namespace K3Pro.Protocol;

/// <summary>Codes of the built-in translations (any other code can be added with a <c>&lt;code&gt;.json</c> file).</summary>
public static class UiLanguage
{
    public const string En = "en";
    public const string Vi = "vi";
}

/// <summary>
/// Display-string translations loaded from JSON files: <c>{ "language.name": "English", "keymap.base_layer": "Base layer (page 0)", … }</c>.
/// Built-in files are embedded from <c>src/K3Pro.Protocol/Localization/*.json</c>; extra or overriding files are read from the folders
/// passed to <see cref="AddSearchDirectory"/> (the app adds <c>&lt;exe dir&gt;/lang</c> and <c>&lt;app data&gt;/lang</c>), so a new
/// language is just a new <c>xx.json</c>. Missing keys fall back to English, then to the key itself.
/// Values with placeholders use <see cref="string.Format(IFormatProvider, string, object[])"/> syntax (<c>{0}</c>, <c>{0:X2}</c>);
/// values without arguments are returned verbatim. The CLI keeps the default (Vietnamese).
/// </summary>
public static class Lang
{
    private const string NameKey = "language.name";
    private const string ResourcePrefix = "K3Pro.Lang.";

    private static readonly Lock Gate = new();
    private static readonly List<string> SearchDirs = [];
    private static volatile Dictionary<string, Dictionary<string, string>>? _tables;
    private static volatile string _current = UiLanguage.Vi;

    /// <summary>Language changed → the UI refreshes the strings currently shown.</summary>
    public static event Action? Changed;

    /// <summary>Current language code (e.g. "en", "vi"). Unknown codes fall back to English.</summary>
    public static string Current
    {
        get => _current;
        set
        {
            var code = Parse(value);
            if (_current == code) return;
            _current = code;
            Changed?.Invoke();
        }
    }

    public static bool IsEnglish => _current == UiLanguage.En;

    /// <summary>Translated text for <paramref name="key"/> in the current language, formatted with <paramref name="args"/>.</summary>
    public static string T(string key, params object?[] args)
    {
        var tables = Tables;
        var text = Lookup(tables, _current, key) ?? Lookup(tables, UiLanguage.En, key) ?? key;
        if (args.Length == 0) return text;
        try
        {
            return string.Format(CultureInfo.CurrentCulture, text, args);
        }
        catch (FormatException)
        {
            // A broken translation must not crash the UI: fall back to English.
            var en = Lookup(tables, UiLanguage.En, key);
            return en is null || en == text ? text : string.Format(CultureInfo.CurrentCulture, en, args);
        }
    }

    /// <summary>All available languages (code + native name), English first.</summary>
    public static IReadOnlyList<(string Code, string Name)> Available =>
        Tables.Select(t => (t.Key, t.Value.GetValueOrDefault(NameKey, t.Key)))
            .OrderBy(l => l.Key == UiLanguage.En ? 0 : 1).ThenBy(l => l.Item2, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public static bool IsAvailable(string? code) => code is not null && Tables.ContainsKey(code.ToLowerInvariant());

    /// <summary>OS UI language if a translation exists for it (first run, nothing chosen yet), otherwise English.</summary>
    public static string FromCulture(CultureInfo culture) =>
        IsAvailable(culture.TwoLetterISOLanguageName) ? culture.TwoLetterISOLanguageName.ToLowerInvariant() : UiLanguage.En;

    /// <summary>"en", "EN", "en-US" → "en"; an available code → itself; anything else (including null) → English.</summary>
    public static string Parse(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return UiLanguage.En;
        var c = code.Trim().ToLowerInvariant();
        if (IsAvailable(c)) return c;
        var dash = c.IndexOfAny(['-', '_']);
        return dash > 0 && IsAvailable(c[..dash]) ? c[..dash] : UiLanguage.En;
    }

    /// <summary>Adds a folder of <c>&lt;code&gt;.json</c> files (new languages, or overrides of built-in keys) and reloads.</summary>
    public static void AddSearchDirectory(string directory)
    {
        lock (Gate)
        {
            if (SearchDirs.Contains(directory, StringComparer.OrdinalIgnoreCase)) return;
            SearchDirs.Add(directory);
            _tables = null;
        }
    }

    public static void RemoveSearchDirectory(string directory)
    {
        lock (Gate)
        {
            if (SearchDirs.RemoveAll(d => string.Equals(d, directory, StringComparison.OrdinalIgnoreCase)) > 0) _tables = null;
        }
    }

    /// <summary>Raw table of one language (for tests / tooling); null if not available.</summary>
    public static IReadOnlyDictionary<string, string>? TableOf(string code) => Tables.GetValueOrDefault(code);

    private static string? Lookup(Dictionary<string, Dictionary<string, string>> tables, string code, string key) =>
        tables.TryGetValue(code, out var t) && t.TryGetValue(key, out var v) ? v : null;

    private static Dictionary<string, Dictionary<string, string>> Tables
    {
        get
        {
            var t = _tables;
            if (t is not null) return t;
            lock (Gate)
            {
                return _tables ??= Load();
            }
        }
    }

    private static Dictionary<string, Dictionary<string, string>> Load()
    {
        var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var asm = typeof(Lang).Assembly;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.Ordinal)))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            Merge(tables, name[ResourcePrefix.Length..^".json".Length], stream);
        }
        foreach (var dir in SearchDirs.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.json"))
            {
                try
                {
                    using var stream = File.OpenRead(file);
                    Merge(tables, Path.GetFileNameWithoutExtension(file), stream);
                }
                catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
                {
                    // An unreadable / broken user file is skipped; the built-in languages still work.
                }
            }
        }
        return tables;
    }

    private static void Merge(Dictionary<string, Dictionary<string, string>> tables, string code, Stream json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
        code = code.ToLowerInvariant();
        if (!tables.TryGetValue(code, out var table)) tables[code] = table = new(StringComparer.Ordinal);
        foreach (var p in doc.RootElement.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.String))
            table[p.Name] = p.Value.GetString()!;
    }
}
