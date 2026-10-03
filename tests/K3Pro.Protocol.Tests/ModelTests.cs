using System.Globalization;

namespace K3Pro.Protocol.Tests;

public class ModelTests
{
    [Theory]
    [InlineData("vi-VN", UiLanguage.Vi)]
    [InlineData("vi", UiLanguage.Vi)]
    [InlineData("en-US", UiLanguage.En)]
    [InlineData("nb-NO", UiLanguage.En)]
    [InlineData("", UiLanguage.En)] // invariant
    public void Os_language_vietnamese_or_else_english(string culture, UiLanguage expected) =>
        Assert.Equal(expected, Lang.FromCulture(CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void Settings_rejects_bad_magic_and_length()
    {
        var data = CaptureBaseline.ReferenceSettings().Data.ToArray();
        data[^1] = 0x00;

        Assert.Throws<InvalidDataException>(() => Settings.Parse(data));
        Assert.Throws<InvalidDataException>(() => Settings.Parse(new byte[127]));
    }

    [Fact]
    public void Settings_WithLightingMode_changes_only_offset_0x0A()
    {
        var before = CaptureBaseline.ReferenceSettings();

        var after = before.WithLightingMode(LightingModes.Rainbow);

        Assert.Equal([(0x0A, 0x0B)], Hex.DiffRanges(before.Data, after.Data));
        Assert.Equal(LightingModes.Static, before.LightingMode); // original is unchanged
    }

    [Fact]
    public void KeymapPage_With_changes_only_one_entry()
    {
        var before = CaptureBaseline.KeymapPage(0);

        var after = before.With(4, KeymapEntry.HidUsage(0x04));

        Assert.Equal([(4 * 4 + 3, 4 * 4 + 4)], Hex.DiffRanges(before.ToBytes(), after.ToBytes()));
    }

    [Fact]
    public void ColorTable_WithStaticColor_changes_only_slot_7()
    {
        var before = CaptureBaseline.ColorTable();

        var after = before.WithStaticColor(new Rgb(0x12, 0x34, 0x56));

        Assert.Equal([(21, 24)], Hex.DiffRanges(before.ToBytes(), after.ToBytes()));
        Assert.Equal("123456", after.StaticColor.ToString());
    }

    [Theory, InlineData("FF0000", 0xFF, 0, 0), InlineData("#00ff7f", 0, 0xFF, 0x7F)]
    public void Rgb_parses(string s, byte r, byte g, byte b) => Assert.Equal(new Rgb(r, g, b), Rgb.Parse(s));

    [Theory, InlineData("FF00"), InlineData("GG0000"), InlineData("FF00000")]
    public void Rgb_rejects_invalid(string s) => Assert.Throws<FormatException>(() => Rgb.Parse(s));

    [Fact]
    public void Baseline_color_table_layout()
    {
        var t = CaptureBaseline.ColorTable();

        Assert.All(Enumerable.Range(0, 7), i => Assert.Equal(new Rgb(0, 0, 0), t[i]));
        Assert.Equal(ColorTable.ColorCount, t.Colors.Count);
    }
}
