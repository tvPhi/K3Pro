using static K3Pro.Protocol.Tests.CaptureFixtures;

namespace K3Pro.Protocol.Tests;

/// <summary>Packets built by PacketBuilder must match the real captured packets BYTE FOR BYTE (all 520 bytes).</summary>
public class GoldenPacketTests
{
    public static TheoryData<string> AllCaptures => new(All);

    [Theory, MemberData(nameof(AllCaptures))]
    public void ReadDeviceInfo_request_matches_capture(string capture)
    {
        var expected = Set(capture, Command.ReadDeviceInfo, page: 0x01);

        AssertPacket(expected, PacketBuilder.ReadDeviceInfo());
    }

    [Theory, MemberData(nameof(AllCaptures))]
    public void ReadDeviceInfo_response_is_03_00_00_00_00_17(string capture)
    {
        var resp = ResponseTo(capture, Command.ReadDeviceInfo, page: 0x01).Bytes;

        Assert.Equal(PacketBuilder.ReadDeviceInfo()[..K3ProConstants.HeaderLength], resp[..K3ProConstants.HeaderLength]);
        var info = DeviceInfo.Parse(resp.AsSpan(K3ProConstants.HeaderLength, DeviceInfo.Length));
        Assert.Equal("03 00 00 00 00 17", info.ToString());
        Assert.True(info.MatchesCapture);
    }

    [Theory, InlineData(RgbRed), InlineData(RgbBlue)]
    public void ReadSettings_request_matches_capture(string capture)
    {
        AssertPacket(Set(capture, Command.ReadSettings), PacketBuilder.ReadSettings());
    }

    [Theory, InlineData(RgbRed), InlineData(RgbBlue)]
    public void WriteSettings_static_mode_read_modify_write_matches_capture(string capture)
    {
        var settings = ParseSettingsResponse(capture);

        var packet = PacketBuilder.WriteSettings(settings.WithLightingMode(LightingModes.Static));

        AssertPacket(Set(capture, Command.WriteSettings), packet);
    }

    [Fact]
    public void Settings_read_in_capture_04_was_mode_03_and_05_was_static()
    {
        Assert.Equal(LightingModes.Rainbow, ParseSettingsResponse(RgbRed).LightingMode);
        Assert.Equal(LightingModes.Static, ParseSettingsResponse(RgbBlue).LightingMode);
    }

    [Fact]
    public void Reference_settings_equal_capture_05_read()
    {
        Assert.Equal(ParseSettingsResponse(RgbBlue).Data.ToArray(), CaptureBaseline.ReferenceSettings().Data.ToArray());
    }

    [Theory, InlineData(Num1ToA, 0x04), InlineData(Num1ToB, 0x05)]
    public void WriteKeymap_num1_remap_on_baseline_page0_matches_capture(string capture, byte hidUsage)
    {
        var page = CaptureBaseline.KeymapPage(0).With(4, KeymapEntry.HidUsage(hidUsage));

        AssertPacket(Set(capture, Command.WriteKeymap, page: 0), PacketBuilder.WriteKeymap(page));
    }

    [Theory, InlineData(1), InlineData(2), InlineData(3)]
    public void WriteKeymap_baseline_pages_1_to_3_match_capture_02(byte page)
    {
        AssertPacket(Set(Num1ToA, Command.WriteKeymap, page), PacketBuilder.WriteKeymap(CaptureBaseline.KeymapPage(page)));
    }

    [Fact]
    public void Baseline_page0_entry4_is_default_num1()
    {
        Assert.Equal(KeymapEntry.HidUsage(0x59), CaptureBaseline.KeymapPage(0)[4]);
    }

    [Theory, InlineData(RgbRed, "FF0000"), InlineData(RgbBlue, "0000FF")]
    public void WriteColorTable_static_color_on_baseline_matches_capture(string capture, string color)
    {
        var table = CaptureBaseline.ColorTable().WithStaticColor(Rgb.Parse(color));

        AssertPacket(Set(capture, Command.WriteColorTable), PacketBuilder.WriteColorTable(table));
    }

    [Theory, InlineData(RgbRed, "FF0000"), InlineData(RgbBlue, "0000FF")]
    public void Captured_color_table_slot7_is_static_color(string capture, string color)
    {
        var data = Set(capture, Command.WriteColorTable).Bytes.AsSpan(K3ProConstants.HeaderLength, ColorTable.ByteLength);

        Assert.Equal(color, ColorTable.Parse(data).StaticColor.ToString());
    }

    [Fact]
    public void Every_captured_SET_is_accepted_by_the_guard()
    {
        foreach (var name in All)
        foreach (var e in Load(name).Events.Where(e => e.IsSet))
        {
            Assert.Equal(K3ProConstants.ReportLength, e.Length);
            var spec = CommandGuard.EnsureAllowed(e.Bytes, expectRead: CommandSpecs.All[(Command)e.Command].IsRead);
            Assert.Equal((Command)e.Command, spec.Command);
        }
    }

    private static Settings ParseSettingsResponse(string capture)
    {
        var resp = ResponseTo(capture, Command.ReadSettings).Bytes;
        Assert.Equal(PacketBuilder.ReadSettings()[..K3ProConstants.HeaderLength], resp[..K3ProConstants.HeaderLength]);
        return Settings.Parse(resp.AsSpan(K3ProConstants.HeaderLength, Settings.Length));
    }

    private static void AssertPacket(CaptureEvent expected, byte[] actual)
    {
        Assert.Equal(K3ProConstants.ReportLength, expected.Length);
        Assert.Equal(K3ProConstants.ReportLength, actual.Length);
        var diff = Hex.DiffRanges(expected.Bytes, actual);
        Assert.True(diff.Count == 0,
            $"{expected}: differs at " + string.Join(", ", diff.Select(r => $"0x{r.Start:X3}..0x{r.End - 1:X3}")));
    }
}
