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
    public void Os_language_vietnamese_or_else_english(string culture, string expected) =>
        Assert.Equal(expected, Lang.FromCulture(CultureInfo.GetCultureInfo(culture)));

    [Theory]
    [InlineData(@"\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&023554_pid&fa07_rev&6701_eb968a1cc4a9&col02#a&d0d6800&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}", "EB968A1CC4A9")]
    [InlineData(@"\\?\hid#{00001812-0000-1000-8000-00805f9b34fb}_dev_vid&023554_pid&fa07_rev&6701_eb968a1cc4a9#a&d0d6800&0&0000#{4d1e55b2}", "EB968A1CC4A9")]
    [InlineData(@"\\?\hid#vid_258a&pid_010c&mi_01&col06#9&28edd87a&0&0005#{4d1e55b2-f16f-11cf-88cb-001111000030}", null)]
    public void Bluetooth_address_from_ble_hid_path(string path, string? expected) =>
        Assert.Equal(expected, K3Pro.Protocol.Bluetooth.BluetoothBattery.AddressFromHidPath(path));

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
