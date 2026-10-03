using K3Pro.Protocol.Wireless;
using static K3Pro.Protocol.Tests.CaptureFixtures;

namespace K3Pro.Protocol.Tests;

/// <summary>Fake receiver: records OUT frames, returns IN frames from a queue, or echoes write frames like the real receiver.</summary>
internal sealed class FakeReceiver : IReceiverTransport
{
    public List<byte[]> Sent { get; } = [];
    public Queue<byte[]> Responses { get; } = new();

    /// <summary>When true: write frames (0x01 / 0x04 / 0x09) are echoed back verbatim — the behavior seen in the 2.4G captures.</summary>
    public bool EchoWrites { get; set; } = true;

    /// <summary>Drops the echo of the first send of the frame with this index (simulates a lost echo → the app must resend).</summary>
    public int? DropFirstEchoOfIndex { get; set; }

    /// <summary>The "device"'s current block; writes via 0x04 update it, reads via 0x44 return it.</summary>
    public byte[]? Block { get; set; }

    private readonly List<byte[]> _pendingWrite = [];

    public string Description => "fake receiver";

    public void Write(byte[] frame)
    {
        Sent.Add(frame.ToArray());
        bool isWrite = frame[1] is (byte)ReceiverCommand.WriteSettings or (byte)ReceiverCommand.WriteKeymap or (byte)ReceiverCommand.WriteColorTable;
        if (isWrite && DropFirstEchoOfIndex == frame[3])
        {
            DropFirstEchoOfIndex = null;
            return;
        }
        if (isWrite && EchoWrites)
            Responses.Enqueue(frame.ToArray());
        if (frame[1] == (byte)ReceiverCommand.WriteSettings && EchoWrites)
        {
            _pendingWrite.Add(frame);
            if (_pendingWrite.Count == ReceiverFrame.SettingsFrameCount && Block is not null)
            {
                Block = _pendingWrite.SelectMany((f, i) => f.Skip(ReceiverFrame.PayloadOffset).Take(f[4])).ToArray();
                _pendingWrite.Clear();
            }
        }
        if (frame[1] == (byte)ReceiverCommand.ReadSettings && Block is not null)
            foreach (var r in ReadResponse(Block)) Responses.Enqueue(r);
    }

    public byte[]? Read(TimeSpan timeout) => Responses.Count > 0 ? Responses.Dequeue() : null;

    public void Dispose() { }

    /// <summary>The 10 0x44 response frames like the real receiver sends (without stale buffer data).</summary>
    public static IEnumerable<byte[]> ReadResponse(byte[] block) =>
        Enumerable.Range(0, ReceiverFrame.SettingsFrameCount).Select(i =>
            ReceiverFrame.Build(ReceiverCommand.ReadSettings, ReceiverFrame.SettingsFrameCount, (byte)i,
                block.AsSpan(i * ReceiverFrame.PayloadLength, Math.Min(ReceiverFrame.PayloadLength, Settings.Length - i * ReceiverFrame.PayloadLength))));
}

public class ReceiverTests
{
    private static List<CapturedFrame> Frames(string capture) => Load(capture).Receiver!;

    private static IEnumerable<byte[]> Out(string capture) => Frames(capture).Where(f => f.Dir == "OUT").Select(f => f.Bytes);

    private static IEnumerable<byte[]> In(string capture) => Frames(capture).Where(f => f.Dir == "IN").Select(f => f.Bytes);

    public static TheoryData<string> AllWithReceiver => new(All.Append(WiredSleep30s).Append(WiredSleep20Min)
        .Append(WirelessSleep30s).Append(WirelessSleep20Min)
        .Append("21-24g-open-close").Append("22-24g-num1-to-A").Append("23-24g-num1-to-B").Append("24-24g-rgb-red").Append("25-24g-rgb-blue")
        .Append("09-num1-lctrl").Append("10-num1-ctrl-c").Append("11-num1-shift-alt-a").Append("12-num1-volup").Append("13-num1-mouse-left")
        .Append("14-num1-fn2").Append("15-num1-cmd-lock").Append("17-knob-left-A").Append("17b-knob-right-B").Append("17c-knob-press-C")
        .Append("17d-knob-reset"));

    [Theory, MemberData(nameof(AllWithReceiver))]
    public void Every_captured_frame_has_valid_checksum_and_every_OUT_frame_passes_the_guard(string capture)
    {
        Assert.All(Frames(capture), f => Assert.True(ReceiverFrame.HasValidChecksum(f.Bytes), Hex.Format(f.Bytes)));
        Assert.All(Out(capture), f => ReceiverGuard.EnsureAllowed(f));
    }

    [Theory, InlineData(ReceiverCommand.Status), InlineData(ReceiverCommand.ReadInfo), InlineData(ReceiverCommand.ReadSettings)]
    public void Query_frames_match_capture(ReceiverCommand command)
    {
        Assert.Contains(Out(WirelessSleep30s), f => f.SequenceEqual(ReceiverFrame.Query(command)));
    }

    [Theory, InlineData(WirelessSleep30s, 0x01), InlineData(WirelessSleep20Min, 0x28)]
    public void Sleep_write_frames_match_vendor_app_byte_for_byte(string capture, byte units)
    {
        var read = Settings.Parse(ReceiverFrame.AssembleSettings(In(capture).Where(f => f[1] == 0x44).ToList()));

        var plan = WritePlanner.SleepTime(read, units);
        var frames = WireEncoding.Encode(ConnectionKind.Wireless, Assert.Single(plan.Writes));

        var expected = Out(capture).Where(f => f[1] == (byte)ReceiverCommand.WriteSettings).ToList();
        Assert.Equal(expected.Count, frames.Count);
        for (int i = 0; i < frames.Count; i++) Assert.Equal(expected[i], frames[i]);
    }

    [Fact]
    public void Block_read_over_2_4G_shares_sleep_with_wired_and_differs_only_at_0x0E()
    {
        var wireless = ReceiverFrame.AssembleSettings(In(WirelessSleep30s).Where(f => f[1] == 0x44).ToList());
        var wiredWrite = Load(WiredSleep20Min).Events.Last(e => e.IsSet && e.Command == (byte)Command.WriteSettings).Bytes;
        var wired = wiredWrite.AsSpan(K3ProConstants.HeaderLength, Settings.Length).ToArray();

        Assert.Equal(0x28, wireless[Settings.SleepOffset]); // written over wire in capture 32, read over 2.4G in capture 33
        Assert.Equal([(0x0E, 0x0F)], Hex.DiffRanges(wired, wireless));
    }

    [Fact]
    public void ReadDeviceInfo_and_status_replay_capture()
    {
        var fake = new FakeReceiver();
        var responses = In(WirelessSleep30s).ToList();
        fake.Responses.Enqueue(responses.First(f => f[1] == 0x07));
        fake.Responses.Enqueue(responses.First(f => f[1] == 0x05));
        var device = new K3ProReceiverDevice(fake, TimeSpan.Zero);

        Assert.True(device.ReadStatus().NumpadLinked);
        var info = device.ReadDeviceInfo();

        Assert.True(info.MatchesCapture);
        Assert.Equal([ReceiverFrame.Query(ReceiverCommand.Status), ReceiverFrame.Query(ReceiverCommand.ReadInfo)], fake.Sent);
    }

    [Fact]
    public void Execute_sleep_over_2_4G_rereads_writes_10_frames_with_echo_and_verifies()
    {
        var block = ReceiverFrame.AssembleSettings(In(WirelessSleep20Min).Where(f => f[1] == 0x44).ToList());
        var fake = new FakeReceiver { Block = block };
        var device = new K3ProReceiverDevice(fake, TimeSpan.Zero);
        var current = device.ReadSettings();

        device.Execute(WritePlanner.SleepTime(current, 0x28));

        var cmds = fake.Sent.Select(f => f[1]).ToList();
        Assert.Equal([0x44, 0x44, .. Enumerable.Repeat((byte)0x04, 10), 0x44], cmds);
        Assert.Equal(Out(WirelessSleep20Min).Where(f => f[1] == 0x04), fake.Sent.Where(f => f[1] == 0x04));
        Assert.Equal(0x28, fake.Block![Settings.SleepOffset]);
    }

    [Fact]
    public void Execute_refuses_keymap_page_3_over_2_4G()
    {
        var fake = new FakeReceiver();
        var device = new K3ProReceiverDevice(fake, TimeSpan.Zero);
        var page3 = CaptureBaseline.KeymapPage(3);
        var plan = WritePlanner.Keymap(page3, page3, "x", force: true);

        Assert.Throws<UnsafeCommandException>(() => device.Execute(plan));
        Assert.Throws<UnsafeCommandException>(() => WireEncoding.Encode(ConnectionKind.Wireless, plan.Writes[0]));
        Assert.Empty(fake.Sent);
    }

    /// <summary>OUT frames of one command in a capture, without the vendor app's resends (index order kept).</summary>
    private static List<byte[]> UniqueOut(string capture, ReceiverCommand cmd)
    {
        var seen = new HashSet<int>();
        return Out(capture).Where(f => f[1] == (byte)cmd && seen.Add(f[3])).ToList();
    }

    [Theory, InlineData("22-24g-num1-to-A", 0x04), InlineData("23-24g-num1-to-B", 0x05)]
    public void Keymap_page0_over_2_4G_matches_vendor_app_byte_for_byte(string capture, byte num1)
    {
        var plan = WritePlanner.Keymap(CaptureBaseline.KeymapPage(0), KeymapRules.Apply(0, new Dictionary<int, byte> { [4] = num1 }), "x");

        var frames = WireEncoding.Encode(ConnectionKind.Wireless, Assert.Single(plan.Writes));

        Assert.Equal(36, frames.Count);
        Assert.Equal(UniqueOut(capture, ReceiverCommand.WriteKeymap), frames);
    }

    [Theory, InlineData("24-24g-rgb-red", "FF0000"), InlineData("25-24g-rgb-blue", "0000FF")]
    public void Static_color_over_2_4G_reproduces_vendor_sequence_settings_then_color_table(string capture, string color)
    {
        var read = Settings.Parse(ReceiverFrame.AssembleSettings(In(capture).Where(f => f[1] == 0x44).TakeLast(10).ToList()))
            .WithLightingMode(0x03); // before the vendor app picked static (the read block is already 01 → still writes 04 like the capture)
        var plan = WritePlanner.StaticColor(read, Rgb.Parse(color));

        var settingsFrames = WireEncoding.Encode(ConnectionKind.Wireless, plan.Writes[0]);
        var colorFrames = WireEncoding.Encode(ConnectionKind.Wireless, plan.Writes[1]);

        Assert.Equal(UniqueOut(capture, ReceiverCommand.WriteSettings), settingsFrames);
        Assert.Equal(29, colorFrames.Count);
        Assert.Equal(UniqueOut(capture, ReceiverCommand.WriteColorTable), colorFrames);
    }

    /// <summary>The settings block the vendor app writes (OUT frames 0x04) in a capture.</summary>
    private static Settings WrittenSettings(string capture) => Settings.Parse(UniqueOut(capture, ReceiverCommand.WriteSettings)
        .SelectMany(f => f.Skip(ReceiverFrame.PayloadOffset).Take(ReceiverFrame.LengthOf(f))).ToArray());

    /// <summary>
    /// Batch 6 (run back to back, vendor app kept open): base = block written by the previous step → the tool builds every 0x04 frame the vendor app sent.
    /// </summary>
    [Theory]
    [InlineData("43-led-rainbow", "42-led-respire", 0x03, null, null)]
    [InlineData("44-led-flash-away", "43-led-rainbow", 0x04, null, null)]
    [InlineData("45-led-raindrops", "44-led-flash-away", 0x05, null, null)]
    [InlineData("46-led-rainbow-wheel", "45-led-raindrops", 0x06, null, null)]
    [InlineData("47-led-ripples-shining", "46-led-rainbow-wheel", 0x07, null, null)]
    [InlineData("48-led-stars-twinkle", "47-led-ripples-shining", 0x08, null, null)]
    [InlineData("49-led-shadow-disappear", "48-led-stars-twinkle", 0x09, null, null)]
    [InlineData("50-led-retro-snake", "49-led-shadow-disappear", 0x0A, null, null)]
    [InlineData("51-led-neon-stream", "50-led-retro-snake", 0x0B, null, null)]
    [InlineData("52-led-reaction", "51-led-neon-stream", 0x0C, null, null)]
    [InlineData("53-led-sine-wave", "52-led-reaction", 0x0D, null, null)]
    [InlineData("54-led-rotating-windmill", "53-led-sine-wave", 0x0F, null, null)]
    [InlineData("55-led-colorful-waterfall", "54-led-rotating-windmill", 0x10, null, null)]
    [InlineData("56-led-blossoming", "55-led-colorful-waterfall", 0x11, null, null)]
    [InlineData("61-led-respire-bright-min", "58-led-off", 0x02, 0, null)]
    [InlineData("62-led-respire-bright-max", "61-led-respire-bright-min", 0x02, 4, null)]
    [InlineData("63-led-respire-speed-min", "62-led-respire-bright-max", 0x02, null, 0)]
    [InlineData("64-led-respire-speed-max", "63-led-respire-speed-min", 0x02, null, 4)]
    [InlineData("41-led-fixed-on", "64-led-respire-speed-max", 0x01, null, null)]
    public void Light_effect_over_2_4G_matches_vendor_app_byte_for_byte(string capture, string previous, byte mode, int? brightness, int? speed)
    {
        var plan = WritePlanner.LightingEffect(WrittenSettings(previous), mode, (byte?)brightness, (byte?)speed);

        var frames = WireEncoding.Encode(ConnectionKind.Wireless, Assert.Single(plan.Writes));

        Assert.Equal(UniqueOut(capture, ReceiverCommand.WriteSettings), frames);
    }

    [Fact]
    public void Self_define_needs_command_02_and_is_not_writable()
    {
        var selfDefine = WrittenSettings("57-led-self-define");

        Assert.Equal(LightingModes.SelfDefine, selfDefine.LightingMode);
        Assert.Equal(1, selfDefine.SelfDefineFlag);
        Assert.Contains(Out("57-led-self-define"), f => f[1] == 0x02); // per-key color command — not enabled yet
        Assert.DoesNotContain(LightingModes.SelfDefine, LightingModes.Observed);
        Assert.Throws<UnsafeCommandException>(() => WritePlanner.LightingEffect(WrittenSettings("56-led-blossoming"), LightingModes.SelfDefine));
        // In Self-define (0x09 = 01) → don't change the effect (the vendor app also clears 0x09 when leaving it, capture 58)
        Assert.Throws<UnsafeCommandException>(() => WritePlanner.LightingEffect(selfDefine, LightingModes.Off));
        Assert.Equal([(Settings.SelfDefineFlagOffset, Settings.LightingModeOffset + 1)],
            Hex.DiffRanges(selfDefine.Data, WrittenSettings("58-led-off").Data));
    }

    [Fact]
    public void Vendor_rewrites_identical_color_table_on_every_effect_change()
    {
        string[] captures = ["42-led-respire", "43-led-rainbow", "51-led-neon-stream", "58-led-off", "64-led-respire-speed-max", "41-led-fixed-on"];
        var tables = captures.Select(c => UniqueOut(c, ReceiverCommand.WriteColorTable)).ToList();

        Assert.All(tables, t => Assert.Equal(tables[0], t)); // → the tool only writes 0x04 when changing effects
    }

    [Fact]
    public void Effect_parameters_reject_out_of_range_and_unsupported()
    {
        var current = WrittenSettings("41-led-fixed-on");

        Assert.Throws<UnsafeCommandException>(() => WritePlanner.LightingEffect(current, LightingModes.Respire, brightness: 5));
        Assert.Throws<UnsafeCommandException>(() => WritePlanner.LightingEffect(current, LightingModes.Static, speed: 2)); // Fixed_on has no speed
        Assert.Throws<UnsafeCommandException>(() => WritePlanner.LightingEffect(current, LightingModes.Off, brightness: 1));
        Assert.Throws<UnsafeCommandException>(() => WritePlanner.LightingEffect(current, 0x0E)); // hidden in the vendor app UI, not captured yet
        Assert.True(WritePlanner.LightingEffect(current, LightingModes.Static).IsEmpty);
    }

    /// <summary>Assembles one keymap page from the OUT 0x01 frames in a capture (page = high nibble of byte 4).</summary>
    private static byte[] KeymapPageFrom(string capture, int page) =>
        UniqueOutPage(capture, page).SelectMany(f => f.Skip(ReceiverFrame.PayloadOffset).Take(ReceiverFrame.LengthOf(f))).ToArray();

    private static List<byte[]> UniqueOutPage(string capture, int page)
    {
        var seen = new HashSet<int>();
        return Out(capture).Where(f => f[1] == (byte)ReceiverCommand.WriteKeymap && ReceiverFrame.PageOf(f) == page && seen.Add(f[3])).ToList();
    }

    public static TheoryData<string, Dictionary<int, byte>> Knob24gCaptures => new()
    {
        // Num1 (#4) still holds Win+L (00 08 0F 00) from step 15 in the vendor app profile
        { "17-knob-left-A", new() { [11] = 0x04 } },
        { "17b-knob-right-B", new() { [11] = 0x04, [23] = 0x05 } },
        { "17c-knob-press-C", new() { [11] = 0x04, [18] = 0x06, [23] = 0x05 } },
    };

    [Theory, MemberData(nameof(Knob24gCaptures))]
    public void Knob_writes_over_2_4G_match_baseline_plus_overrides_and_reencode_byte_for_byte(string capture, Dictionary<int, byte> overrides)
    {
        var expected = KeymapRules.Apply(0, overrides).With(4, new KeymapEntry(0x00, 0x08, 0x0F, 0x00));
        var data = KeymapPageFrom(capture, 0);

        Assert.Equal(expected.ToBytes(), data);
        var packet = PacketBuilder.WriteKeymap(KeymapPage.Parse(0, data)); // the planner doesn't allow #4 = Win+L yet → build directly to test the encoder
        Assert.Equal(UniqueOutPage(capture, 0), WireEncoding.Encode(ConnectionKind.Wireless, new PlannedWrite("x", packet, packet, "x")));
    }

    [Theory, InlineData("14-num1-fn2", 1), InlineData("14-num1-fn2", 2), InlineData("15-num1-cmd-lock", 1), InlineData("15-num1-cmd-lock", 2)]
    public void Keymap_pages_1_2_over_2_4G_put_page_in_high_nibble_of_byte4(string capture, int page)
    {
        var frames = UniqueOutPage(capture, page);
        Assert.Equal(36, frames.Count);
        Assert.All(frames, f => Assert.Equal(page << 4 | 0x0E, f[4]));

        var packet = new byte[K3ProConstants.ReportLength];
        new PacketHeader(0x06, (byte)Command.WriteKeymap, (byte)page, 0x00, 0x0001, KeymapPage.ByteLength).WriteTo(packet);
        KeymapPageFrom(capture, page).CopyTo(packet, K3ProConstants.HeaderLength);
        var write = new PlannedWrite("x", packet, packet, "x");

        Assert.Equal(frames, WireEncoding.Encode(ConnectionKind.Wireless, write));
    }

    [Fact]
    public void Modifier_combo_consumer_and_mouse_entries_from_2_4G_captures()
    {
        static KeymapEntry Num1(string capture) => KeymapEntry.Read(KeymapPageFrom(capture, 0).AsSpan(4 * KeymapEntry.Size));

        Assert.Equal(new KeymapEntry(0x00, 0x01, 0x00, 0x00), Num1("09-num1-lctrl"));        // LCtrl on its own
        Assert.Equal(new KeymapEntry(0x00, 0x01, 0x00, 0x06), Num1("10-num1-ctrl-c"));       // Ctrl + C
        Assert.Equal(new KeymapEntry(0x00, 0x06, 0x00, 0x04), Num1("11-num1-shift-alt-a"));  // Shift | Alt + A
        Assert.Equal(new KeymapEntry(0x02, 0x00, 0x00, 0xE9), Num1("12-num1-volup"));        // consumer Volume +
        Assert.Equal(new KeymapEntry(0x01, 0x01, 0x01, 0x00), Num1("13-num1-mouse-left"));   // left mouse button ❓ p2
        Assert.Equal(new KeymapEntry(0x0D, 0x01, 0x00, 0x00), Num1("14-num1-fn2"));          // FN2
        Assert.Equal(new KeymapEntry(0x00, 0x08, 0x0F, 0x00), Num1("15-num1-cmd-lock"));     // Commands: lock PC = Win + L
    }

    /// <summary>Page 0 built by the tool (baseline + 1 override) must match, byte for byte, the page 0 the vendor app writes over 2.4G.</summary>
    [Theory]
    [InlineData("09-num1-lctrl", "00 01 00 00")]
    [InlineData("10-num1-ctrl-c", "00 01 00 06")]
    [InlineData("11-num1-shift-alt-a", "00 06 00 04")]
    [InlineData("12-num1-volup", "02 00 00 E9")]
    [InlineData("13-num1-mouse-left", "01 01 01 00")]
    [InlineData("15-num1-cmd-lock", "00 08 0F 00")]
    public void Captured_actions_build_vendor_page0_byte_for_byte(string capture, string entryHex)
    {
        var entry = KeymapEntry.Read(Hex.Parse(entryHex));
        Assert.True(KeymapActions.IsAllowed(entry));

        var page = KeymapRules.Apply(0, new Dictionary<int, KeymapEntry> { [4] = entry });

        Assert.Equal(KeymapPageFrom(capture, 0), page.ToBytes());
        var plan = WritePlanner.Keymap(CaptureBaseline.KeymapPage(0), page, "x");
        Assert.Equal(UniqueOutPage(capture, 0), WireEncoding.Encode(ConnectionKind.Wireless, Assert.Single(plan.Writes)));
    }

    [Theory]
    [InlineData("02 00 00 EA")] // Vol− — not captured yet
    [InlineData("01 02 01 00")] // right mouse button — not captured yet
    [InlineData("00 10 00 00")] // RCtrl — not captured yet
    [InlineData("00 11 00 04")] // combo with a right-side modifier
    [InlineData("0D 01 00 00")] // FN2 — the vendor app writes 3 pages, not supported yet
    [InlineData("03 01 01 00")] // macro — command 03 not enabled yet
    [InlineData("00 01 00 E0")] // combo with a key outside the list
    public void Uncaptured_entries_are_rejected(string entryHex)
    {
        var entry = KeymapEntry.Read(Hex.Parse(entryHex));

        Assert.False(KeymapActions.IsAllowed(entry));
        Assert.Throws<UnsafeCommandException>(() => KeymapRules.Apply(0, new Dictionary<int, KeymapEntry> { [4] = entry }));
    }

    [Fact]
    public void Action_labels()
    {
        Assert.Equal("Ctrl+C", KeymapActions.ShortLabel(KeymapActions.Combo(KeymapActions.ModCtrl, 0x06)));
        Assert.Equal("Shift+Alt+A", KeymapActions.ShortLabel(new KeymapEntry(0x00, 0x06, 0x00, 0x04)));
        Assert.Equal("Vol+", KeymapActions.ShortLabel(new KeymapEntry(0x02, 0x00, 0x00, 0xE9)));
        Assert.Equal(KeymapActionKind.Command, KeymapActions.Classify(new KeymapEntry(0x00, 0x08, 0x0F, 0x00)));
        Assert.Throws<UnsafeCommandException>(() => KeymapActions.Combo(0x00, 0x06));
    }

    [Fact]
    public void Guard_rejects_keymap_page_3_over_2_4G()
    {
        var frame = ReceiverFrame.Build(ReceiverCommand.WriteKeymap, 0x24, 0, new byte[14], page: 3);

        Assert.Throws<UnsafeCommandException>(() => ReceiverGuard.EnsureAllowed(frame));
    }

    [Fact]
    public void Missing_echo_is_resent_like_vendor_app_then_succeeds()
    {
        var fake = new FakeReceiver { DropFirstEchoOfIndex = 3 };
        var device = new K3ProReceiverDevice(fake, TimeSpan.Zero, responseTimeout: TimeSpan.FromMilliseconds(5));
        var plan = WritePlanner.Keymap(CaptureBaseline.KeymapPage(0), KeymapRules.Apply(0, new Dictionary<int, byte> { [4] = 4 }), "x");

        device.Execute(plan);

        var keymapFrames = fake.Sent.Where(f => f[1] == (byte)ReceiverCommand.WriteKeymap).ToList();
        Assert.Equal(37, keymapFrames.Count);              // 36 + 1 resend, like capture 22
        Assert.Equal(2, keymapFrames.Count(f => f[3] == 3));
        Assert.Equal(UniqueOut("22-24g-num1-to-A", ReceiverCommand.WriteKeymap), keymapFrames.DistinctBy(f => f[3]));
    }

    [Fact]
    public void Execute_reports_that_nothing_was_sent_when_precheck_read_times_out()
    {
        var fake = new FakeReceiver(); // no Block → nobody answers 0x44
        var device = new K3ProReceiverDevice(fake, TimeSpan.Zero, responseTimeout: TimeSpan.FromMilliseconds(5));
        var plan = WritePlanner.SleepTime(CaptureBaseline.ReferenceSettings(), 0x28);

        var ex = Assert.Throws<IOException>(() => device.Execute(plan));

        Assert.Contains("chưa gửi khung ghi nào", ex.Message);
        Assert.DoesNotContain(fake.Sent, f => f[1] == (byte)ReceiverCommand.WriteSettings);
        Assert.Equal(2, fake.Sent.Count(f => f[1] == (byte)ReceiverCommand.ReadSettings)); // retried exactly once
    }

    [Fact]
    public void Execute_reports_how_many_frames_were_sent_when_echo_stops()
    {
        var block = CaptureBaseline.ReferenceSettings().Data.ToArray();
        var fake = new FakeReceiver { Block = block, EchoWrites = false };
        var device = new K3ProReceiverDevice(fake, TimeSpan.Zero, responseTimeout: TimeSpan.FromMilliseconds(5));

        var ex = Assert.Throws<IOException>(() => device.Execute(WritePlanner.SleepTime(Settings.Parse(block), 0x28)));

        Assert.Contains("đã gửi 0/10 khung", ex.Message);
        Assert.Equal(1 + K3ProReceiverDevice.MaxResends, fake.Sent.Count(f => f[1] == (byte)ReceiverCommand.WriteSettings)); // resends before giving up
    }

    [Fact]
    public void Try_queries_return_null_instead_of_throwing_when_numpad_sleeps()
    {
        var fake = new FakeReceiver(); // nobody answers
        var device = new K3ProReceiverDevice(fake, TimeSpan.Zero, responseTimeout: TimeSpan.FromMilliseconds(5));

        Assert.Null(device.TryReadStatus());
        Assert.Null(device.TryReadDeviceInfo());

        var asleep = Hex.Parse("13 07 01 00 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 1C"); // byte 5 = 00 (log 2026-10-02)
        fake.Responses.Enqueue(asleep);
        Assert.False(device.TryReadStatus()!.NumpadLinked);
    }

    [Fact]
    public void Missing_echo_or_no_response_raises_timeout_with_wake_hint()
    {
        var fake = new FakeReceiver();
        var device = new K3ProReceiverDevice(fake, TimeSpan.Zero, responseTimeout: TimeSpan.FromMilliseconds(5));

        var ex = Assert.Throws<TimeoutException>(() => device.ReadStatus());
        Assert.Contains("ngủ", ex.Message);
    }

    [Theory]
    [InlineData("13 03 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 17")] // 0x03 (keymap) never seen via the receiver
    [InlineData("13 0a 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 1e")] // 0x0A (colors)
    [InlineData("13 07 01 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 1c")] // bad checksum
    [InlineData("13 44 02 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 59")] // count differs from capture
    [InlineData("13 04 0a 09 02 00 00 00 00 00 00 00 00 00 00 00 00 00 00 2c")] // last frame missing magic
    public void Guard_rejects_frames_not_seen_in_capture(string hex)
    {
        Assert.Throws<UnsafeCommandException>(() => ReceiverGuard.EnsureAllowed(Hex.Parse(hex)));
    }
}
