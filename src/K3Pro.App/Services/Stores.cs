using System.Text.Json;
using System.Text.Json.Serialization;
using K3Pro.Protocol;
using static K3Pro.Protocol.Lang;

namespace K3Pro.App.Services;

internal static class Json
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };
}

/// <summary>app-settings.json: currently only the UI language ("vi" / "en").</summary>
public sealed class AppSettings
{
    /// <summary>null = the user hasn't chosen → follow the OS language.</summary>
    public string? Language { get; set; }

    public UiLanguage ResolveLanguage(UiLanguage systemLanguage) => Language is { } code ? Lang.Parse(code) : systemLanguage;
}

public sealed class AppSettingsStore(string path)
{
    public string Path => path;

    /// <summary>Missing or corrupt file → defaults (OS language). Old fields (e.g. dryRun) are ignored.</summary>
    public AppSettings Load()
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json.Options) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new();
        }
    }

    public void Save(AppSettings settings)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Json.Options));
    }
}

/// <summary>
/// The keymap state the app believes is on the device (there is no keymap read command yet).
/// Only overrides vs. the default keymap from the capture are stored — every other entry is always derived from the capture.
/// </summary>
public sealed class KeymapState
{
    public string Baseline { get; set; } = CaptureBaseline.KeymapSource;
    public byte Page { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>matrix index → 4-byte entry as hex ("00 01 00 06") — plain key, modifier, combo, media, mouse, command.</summary>
    public SortedDictionary<int, string>? Entries { get; set; }

    /// <summary>Old format: index → HID usage. Still readable; the next save converts it to <see cref="Entries"/>.</summary>
    public SortedDictionary<int, byte>? Overrides { get; set; }

    /// <summary>Only for humans reading the file: index → name. Ignored on load.</summary>
    [JsonPropertyName("overridesReadable")]
    public SortedDictionary<int, string>? Readable { get; set; }
}

public sealed class KeymapStateStore(string path)
{
    public string Path => path;

    /// <summary>Reads the overrides; throws <see cref="InvalidDataException"/> if the file is corrupt or has an invalid entry.</summary>
    public IReadOnlyDictionary<int, KeymapEntry> LoadOverrides()
    {
        if (!File.Exists(path)) return new Dictionary<int, KeymapEntry>();
        KeymapState state;
        try
        {
            state = JsonSerializer.Deserialize<KeymapState>(File.ReadAllText(path), Json.Options) ?? new();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(T($"{path}: JSON hỏng ({ex.Message}).", $"{path}: invalid JSON ({ex.Message})."), ex);
        }
        if (state.Page != 0) throw new InvalidDataException(T($"{path}: chỉ hỗ trợ page 0.", $"{path}: only page 0 is supported."));

        var overrides = new Dictionary<int, KeymapEntry>();
        foreach (var (index, code) in state.Overrides ?? []) overrides[index] = KeymapEntry.HidUsage(code);
        foreach (var (index, hex) in state.Entries ?? [])
        {
            byte[] bytes;
            try { bytes = Hex.Parse(hex); }
            catch (FormatException ex) { throw new InvalidDataException(T($"{path}: entry #{index} '{hex}' không phải hex.", $"{path}: entry #{index} '{hex}' is not hex."), ex); }
            if (bytes.Length != KeymapEntry.Size) throw new InvalidDataException(T($"{path}: entry #{index} phải đúng 4 byte.", $"{path}: entry #{index} must be exactly 4 bytes."));
            overrides[index] = KeymapEntry.Read(bytes);
        }
        try
        {
            _ = KeymapRules.Apply(state.Page, overrides);
        }
        catch (UnsafeCommandException ex)
        {
            throw new InvalidDataException($"{path}: {ex.Message}", ex);
        }
        return overrides;
    }

    public void Save(IReadOnlyDictionary<int, byte> hidOverrides) =>
        Save(hidOverrides.ToDictionary(o => o.Key, o => KeymapEntry.HidUsage(o.Value)));

    public void Save(IReadOnlyDictionary<int, KeymapEntry> overrides)
    {
        var state = new KeymapState
        {
            UpdatedAt = DateTimeOffset.Now,
            Entries = new(overrides.ToDictionary(o => o.Key, o => o.Value.ToString())),
            Readable = new(overrides.ToDictionary(o => o.Key, o => KeymapActions.ShortLabel(o.Value) ?? o.Value.ToString())),
        };
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(state, Json.Options));
    }
}
