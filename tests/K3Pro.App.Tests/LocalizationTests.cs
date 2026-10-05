using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Avalonia.Headless.XUnit;
using K3Pro.App.Localization;
using K3Pro.Protocol;

namespace K3Pro.App.Tests;

/// <summary>Translations live in src/K3Pro.Protocol/Localization/*.json (embedded); extra languages come from lang/ folders.</summary>
public partial class LocalizationTests
{
    private static IReadOnlyDictionary<string, string> Table(string code) => Lang.TableOf(code) ?? throw new InvalidOperationException(code);

    [GeneratedRegex(@"\{(\d+)")]
    private static partial Regex Placeholder();

    private static HashSet<int> Indices(string text) => Placeholder().Matches(text).Select(m => int.Parse(m.Groups[1].Value)).ToHashSet();

    [Fact]
    public void Built_in_languages_have_the_same_keys_and_compatible_placeholders()
    {
        var en = Table(UiLanguage.En);
        var vi = Table(UiLanguage.Vi);

        Assert.Equal(en.Keys.Order(), vi.Keys.Order());
        foreach (var (key, text) in en)
        {
            Assert.True(Indices(vi[key]).IsSubsetOf(Indices(text)), $"{key}: vi uses a placeholder that en doesn't have");
            var args = Enumerable.Range(0, Indices(text).DefaultIfEmpty(-1).Max() + 1).Select(_ => (object)0).ToArray();
            if (args.Length > 0) _ = string.Format(CultureInfo.InvariantCulture, text, args); // throws on a malformed format
        }
    }

    [Fact]
    public void Every_key_used_in_the_source_exists_in_en_json()
    {
        var root = Path.GetDirectoryName(ThisFile());
        while (root is not null && !File.Exists(Path.Combine(root, "K3Pro.slnx"))) root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        var used = Directory.EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"(?:\bT\(|NameKey: )""([a-z0-9_]+\.[a-z0-9_.]+)""").Select(m => m.Groups[1].Value))
            .ToHashSet();

        Assert.True(used.Count > 200, $"only {used.Count} keys found — scan broken?");
        var missing = used.Where(k => !Table(UiLanguage.En).ContainsKey(k)).Order().ToList();
        Assert.True(missing.Count == 0, "missing in en.json: " + string.Join(", ", missing));
    }

    private static string ThisFile([CallerFilePath] string path = "") => path;

    [AvaloniaFact]
    public void Missing_keys_fall_back_to_english_then_to_the_key()
    {
        var dir = Path.Combine(Path.GetTempPath(), "k3pro-lang-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "fr.json"), """
            { "language.name": "Français", "ui.base_layer": "Couche de base (page {0})", "ui.reset_to_default": "Réinitialiser" }
            """);
        try
        {
            Lang.AddSearchDirectory(dir);

            Assert.Contains(LanguageOption.All, o => o is { Language: "fr", Name: "Français" });
            Assert.Equal("fr", Lang.FromCulture(CultureInfo.GetCultureInfo("fr-FR")));
            Lang.Current = "fr";
            Assert.Equal("Réinitialiser", Tr.I.ResetToDefault);
            Assert.Equal("Discard changes", Tr.I.DiscardChanges);           // not translated → English
            Assert.Equal("no.such.key", Lang.T("no.such.key"));             // unknown → the key itself
            Assert.Equal("Couche de base (page {0})", Lang.T("ui.base_layer")); // no args → verbatim

            Lang.Current = "xx";                                             // unknown language → English
            Assert.Equal(UiLanguage.En, Lang.Current);
        }
        finally
        {
            Lang.Current = UiLanguage.Vi;
            Lang.RemoveSearchDirectory(dir);
            Directory.Delete(dir, recursive: true);
        }
        Assert.DoesNotContain(LanguageOption.All, o => o.Language == "fr");
    }
}
