using System.Text.Json;
using K3Pro.Protocol;
using static K3Pro.Protocol.Lang;

namespace K3Pro.App.Services;

/// <param name="LabelEn">Label for the English UI; null = use <paramref name="Label"/>.</param>
public sealed record KeyLayout(int Index, string? Label, double X, double Y, double W = 1, double H = 1, string? Shape = null, string? LabelEn = null)
{
    public bool IsKnob => string.Equals(Shape, "knob", StringComparison.OrdinalIgnoreCase);
}

/// <summary>layout.json: physical position of each matrix index (unit = 1 key).</summary>
public sealed record LayoutConfig(double Unit, double Gap, IReadOnlyList<KeyLayout> Keys)
{
    public static LayoutConfig Load(string path)
    {
        var config = JsonSerializer.Deserialize<LayoutConfig>(File.ReadAllText(path), Json.Options)
                     ?? throw new InvalidDataException(T($"{path}: rỗng.", $"{path}: empty."));
        config.Validate(path);
        return config;
    }

    private void Validate(string source)
    {
        if (Unit <= 0 || Gap < 0 || Gap >= Unit) throw new InvalidDataException(T($"{source}: unit/gap không hợp lệ.", $"{source}: invalid unit/gap."));
        var dup = Keys.GroupBy(k => k.Index).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null) throw new InvalidDataException(T($"{source}: index {dup.Key} lặp lại.", $"{source}: index {dup.Key} is duplicated."));
        var bad = Keys.FirstOrDefault(k => k.Index is < 0 or >= KeymapPage.MatrixSize || k.W <= 0 || k.H <= 0);
        if (bad is not null) throw new InvalidDataException(T($"{source}: phím index {bad.Index} không hợp lệ.", $"{source}: key index {bad.Index} is invalid."));
    }
}
