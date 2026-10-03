using static K3Pro.Protocol.Tests.CaptureFixtures;

namespace K3Pro.Protocol.Tests;

public class PlannerTests
{
    private static Settings SettingsReadIn(string capture) =>
        Settings.Parse(ResponseTo(capture, Command.ReadSettings).Bytes.AsSpan(K3ProConstants.HeaderLength, Settings.Length));

    [Fact]
    public void Effect_list_follows_vendor_ui_and_only_captured_modes_are_writable()
    {
        var effects = LightingModes.Effects;

        Assert.Equal(18, effects.Count);
        Assert.Equal(("Fixed_on", "OFF"), (effects[0].Name, effects[^1].Name));
        Assert.Equal(LightingModes.Static, effects[0].Mode);
        Assert.Equal(17, LightingModes.Observed.Count); // all except Self-define
        Assert.All(effects.Where(e => e.IsCaptured), e => Assert.Contains(e.Mode!.Value, LightingModes.Observed));
        var modes = effects.Where(e => e.IsCaptured).Select(e => e.Mode).ToList();
        Assert.Equal(modes.Count, modes.Distinct().Count());
    }

    [Fact]
    public void StaticColor_from_mode_03_reproduces_vendor_app_sequence_in_capture_04()
    {
        var plan = WritePlanner.StaticColor(SettingsReadIn(RgbRed), Rgb.Parse("FF0000"));

        Assert.Equal([Command.WriteSettings, Command.WriteColorTable], plan.Writes.Select(w => w.Command));
        Assert.Equal(Set(RgbRed, Command.WriteSettings).Bytes, plan.Writes[0].Packet);
        Assert.Equal(Set(RgbRed, Command.WriteColorTable).Bytes, plan.Writes[1].Packet);
        Assert.NotNull(plan.Writes[0].ExpectedSettings);
    }

    [Fact]
    public void StaticColor_when_already_static_only_writes_color_table()
    {
        var plan = WritePlanner.StaticColor(SettingsReadIn(RgbBlue), Rgb.Parse("0000FF"));

        Assert.Equal(Set(RgbBlue, Command.WriteColorTable).Bytes, Assert.Single(plan.Writes).Packet);
    }

    [Fact]
    public void LightingMode_static_from_capture_04_read_matches_capture_write()
    {
        var plan = WritePlanner.LightingMode(SettingsReadIn(RgbRed), LightingModes.Static);

        var write = Assert.Single(plan.Writes);
        Assert.Equal(Set(RgbRed, Command.WriteSettings).Bytes, write.Packet);
        Assert.Equal([(0x12, 0x13)], write.Changes);
    }

    [Fact]
    public void LightingMode_same_mode_is_empty_and_unknown_mode_is_rejected()
    {
        var current = SettingsReadIn(RgbBlue);

        Assert.True(WritePlanner.LightingMode(current, LightingModes.Static).IsEmpty);
        Assert.Throws<UnsafeCommandException>(() => WritePlanner.LightingMode(current, 0x0E));
    }

    [Theory, InlineData(Num1ToA, 0x04), InlineData(Num1ToB, 0x05)]
    public void Keymap_override_on_baseline_matches_capture(string capture, byte code)
    {
        var current = CaptureBaseline.KeymapPage(0);
        var target = KeymapRules.Apply(0, new Dictionary<int, byte> { [4] = code });

        var plan = WritePlanner.Keymap(current, target, "baseline");

        Assert.Equal(Set(capture, Command.WriteKeymap).Bytes, Assert.Single(plan.Writes).Packet);
        Assert.Equal(new Dictionary<int, KeymapEntry> { [4] = KeymapEntry.HidUsage(code) }, KeymapRules.OverridesOf(target));
    }

    [Fact]
    public void Keymap_reset_to_default_writes_baseline_page()
    {
        var current = KeymapRules.Apply(0, new Dictionary<int, byte> { [4] = 0x04, [0] = 0x29 });

        var plan = WritePlanner.Keymap(current, CaptureBaseline.KeymapPage(0), "state");

        Assert.Equal(PacketBuilder.WriteKeymap(CaptureBaseline.KeymapPage(0)), Assert.Single(plan.Writes).Packet);
        Assert.Equal(2, plan.Writes[0].Changes.Count);
    }

    [Fact]
    public void Keymap_unchanged_is_empty_unless_forced()
    {
        var page = CaptureBaseline.KeymapPage(0);

        Assert.True(WritePlanner.Keymap(page, page, "x").IsEmpty);
        Assert.Equal(PacketBuilder.WriteKeymap(page), Assert.Single(WritePlanner.Keymap(page, page, "x", force: true).Writes).Packet);
    }

    [Theory]
    [InlineData(24, 0x04)] // outside the matrix
    [InlineData(12, 0x04)] // no physical key
    [InlineData(4, 0x03)]  // invalid usage
    [InlineData(4, 0xE0)]  // Left Ctrl — Modify group, encoding unclear ❓
    [InlineData(4, 0x68)]  // F13 — not offered by the vendor app
    [InlineData(4, 0x7F)]  // keyboard-page Mute — the vendor app uses Multimedia (consumer)
    public void Keymap_rules_reject_special_entries_and_bad_usages(int index, byte code)
    {
        Assert.Throws<UnsafeCommandException>(() => KeymapRules.Apply(0, new Dictionary<int, byte> { [index] = code }));
    }

    [Fact]
    public void Keymap_plan_rejects_target_touching_uncaptured_special_entry()
    {
        var target = CaptureBaseline.KeymapPage(1).With(0, KeymapEntry.HidUsage(0x04)); // FN1 07 00 00 04 ❓

        Assert.Throws<UnsafeCommandException>(() => WritePlanner.Keymap(CaptureBaseline.KeymapPage(1), target, "x"));
        Assert.Throws<UnsafeCommandException>(() => KeymapRules.Apply(0, new Dictionary<int, byte> { [21] = 0x04 }));
    }

    /// <summary>
    /// Wired captures 17–17d were overwritten by the 2.4G versions (2026-10-02); those are checked in ReceiverTests. Wired 17e / 17f remain.
    /// </summary>
    public static TheoryData<string, Dictionary<int, byte>> KnobAndFnCaptures => new()
    {
        { "17e-fn-to-D", new() { [6] = 0x07 } },
        { "17f-fn-reset", new() },
    };

    [Theory, MemberData(nameof(KnobAndFnCaptures))]
    public void Knob_and_Fn_writes_match_vendor_app_page0(string capture, Dictionary<int, byte> overrides)
    {
        var expected = Load(capture).Events.First(e => e.IsSet && e.Command == (byte)Command.WriteKeymap && e.Page == 0);

        var packet = PacketBuilder.WriteKeymap(KeymapRules.Apply(0, overrides));

        Assert.Equal(expected.Bytes, packet);
    }

    [Fact]
    public void Execute_rereads_settings_before_and_after_0x04_like_rmw()
    {
        var transport = new FakeTransport();
        var red = ResponseTo(RgbRed, Command.ReadSettings).Bytes;
        var afterWrite = Set(RgbRed, Command.WriteSettings).Bytes.ToArray();
        afterWrite[1] = 0x84; // the device echoes header 0x84 on reads
        transport.Responses.Enqueue(red);        // check before writing
        transport.Responses.Enqueue(afterWrite); // read back after writing
        var plan = WritePlanner.StaticColor(SettingsReadIn(RgbRed), Rgb.Parse("FF0000"));

        new K3ProDevice(transport, TimeSpan.Zero).Execute(plan);

        Assert.Equal(["SET", "GET", "SET", "SET", "GET", "SET"], transport.Log);
        Assert.Equal(new byte[] { 0x84, 0x04, 0x84, 0x0A }, transport.Sent.Select(p => p[1]));
    }

    [Fact]
    public void Execute_aborts_when_settings_changed_since_planning()
    {
        var transport = new FakeTransport();
        var changed = ResponseTo(RgbRed, Command.ReadSettings).Bytes;
        changed[K3ProConstants.HeaderLength + 0x3A] ^= 0x01;
        transport.Responses.Enqueue(changed);
        var plan = WritePlanner.StaticColor(SettingsReadIn(RgbRed), Rgb.Parse("FF0000"));

        Assert.Throws<InvalidOperationException>(() => new K3ProDevice(transport, TimeSpan.Zero).Execute(plan));
        Assert.Equal(new byte[] { 0x84 }, transport.Sent.Select(p => p[1])); // only the read command, nothing written
    }

    /// <summary>Every (read 0x84 → write 0x04) pair in a capture.</summary>
    private static IEnumerable<(Settings Read, byte[] Write)> ReadWritePairs(string capture)
    {
        Settings? read = null;
        foreach (var e in Load(capture).Events)
        {
            var b = e.Bytes;
            if (!e.IsSet && b[1] == (byte)Command.ReadSettings) read = Settings.Parse(b.AsSpan(K3ProConstants.HeaderLength, Settings.Length));
            if (e.IsSet && b[1] == (byte)Command.WriteSettings) yield return (read!, b);
        }
    }

    [Theory]
    [InlineData(WiredSleep30s, 1, 0x01)]   // 2nd write: 0A → 01 (the 1st also changed 0x38 01 → FF, see PROTOCOL.md)
    [InlineData(WiredSleep20Min, 0, 0x28)] // 01 → 28
    public void SleepTime_rmw_matches_vendor_app_write(string capture, int pair, byte units)
    {
        var (read, write) = ReadWritePairs(capture).ElementAt(pair);

        var plan = WritePlanner.SleepTime(read, units);

        Assert.Equal(write, Assert.Single(plan.Writes).Packet);
    }

    [Fact]
    public void Sleep_units_are_30_seconds()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), SleepTimes.ToTimeSpan(0x0A)); // original value in captures 04/05 = 5 Min in the UI
        Assert.Equal(TimeSpan.FromMinutes(20), SleepTimes.ToTimeSpan(0x28));
        Assert.Equal(0x0A, CaptureBaseline.ReferenceSettings().SleepUnits);
        Assert.Equal((byte)1, SleepTimes.Parse("30s"));
        Assert.Equal((byte)10, SleepTimes.Parse("5m"));
        Assert.Equal((byte)40, SleepTimes.Parse("20min"));
        Assert.Equal((byte)3, SleepTimes.Parse("1.5m"));
        Assert.Equal("5 Min 30 s", SleepTimes.Describe(11));
        Assert.Throws<FormatException>(() => SleepTimes.Parse("45s"));
        Assert.Throws<UnsafeCommandException>(() => SleepTimes.Parse("2.5m")); // multiple of 30 s but not a vendor app step
        Assert.Equal([1, 2, 3, 4, 6, 8, 10, 20, 30, 40], SleepTimes.VendorStops.Select(u => (int)u));
        Assert.All([0x01, 0x0A, 0x28], u => Assert.True(SleepTimes.IsAllowed(u))); // 3 captured steps
        Assert.Throws<UnsafeCommandException>(() => SleepTimes.Parse("30m"));  // > 20 Min: not allowed by the vendor app
        Assert.Throws<UnsafeCommandException>(() => WritePlanner.SleepTime(CaptureBaseline.ReferenceSettings(), 0));
    }

    [Fact]
    public void Allowed_usages_cover_vendor_keyboard_tab_and_default_keymap()
    {
        Assert.Equal(96, KeymapRules.AllowedHidUsages.Count); // 26 letters + 10 digits + 11 symbols + 12 F + 20 Adv + 17 Keypad
        Assert.All(CaptureBaseline.KeymapPage(0).Entries.Where(e => e.IsHidUsage), e => Assert.True(KeymapRules.IsAllowedHidUsage(e.Code)));
        Assert.True(KeymapRules.IsAllowedHidUsage(0x65)); // App
        Assert.False(KeymapRules.IsAllowedHidUsage(0x32)); // Non-US #
        Assert.False(KeymapRules.IsAllowedHidUsage(0x64)); // Non-US backslash
    }

    [Fact]
    public void Hid_usage_names()
    {
        Assert.Equal("A", HidUsages.Name(0x04));
        Assert.Equal("Num 1", HidUsages.Name(0x59));
        Assert.Equal("Backspace", HidUsages.Name(0x2A));
        Assert.Equal("Num .", HidUsages.Name(0x63));
        Assert.Equal("F24", HidUsages.Name(0x73));
        Assert.Equal("Volume Down", HidUsages.Name(0x81));
        Assert.Equal("Right GUI", HidUsages.Name(0xE7));
        Assert.All(CaptureBaseline.KeymapPage(0).Entries.Where(e => e.IsHidUsage), e => Assert.DoesNotContain("0x", HidUsages.Name(e.Code)));
    }
}
