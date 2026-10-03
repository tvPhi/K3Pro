namespace K3Pro.Protocol;

/// <summary>A prebuilt write packet, plus the base packet for showing the diff.</summary>
public sealed record PlannedWrite(string Title, byte[] Packet, byte[] BasePacket, string BaseLabel)
{
    /// <summary>For 0x04: the device block must be unchanged since the plan was made, otherwise cancel (read-modify-write).</summary>
    public Settings? ExpectedSettings { get; init; }

    public Command Command => (Command)Packet[1];

    public PacketHeader Header => PacketHeader.Parse(Packet);

    public IReadOnlyList<(int Start, int End)> Changes => Hex.DiffRanges(BasePacket, Packet);

    /// <summary>One line per changed range: offset in the packet / in the data, old bytes → new.</summary>
    public IEnumerable<string> DescribeChanges() => Changes.Select(r =>
    {
        int ds = r.Start - K3ProConstants.HeaderLength, de = r.End - 1 - K3ProConstants.HeaderLength;
        var where = r.End - r.Start == 1
            ? Lang.T($"gói 0x{r.Start:X4} (data 0x{ds:X4})", $"packet 0x{r.Start:X4} (data 0x{ds:X4})")
            : Lang.T($"gói 0x{r.Start:X4}..0x{r.End - 1:X4} (data 0x{ds:X4}..0x{de:X4})",
                $"packet 0x{r.Start:X4}..0x{r.End - 1:X4} (data 0x{ds:X4}..0x{de:X4})");
        return $"{where}: {Hex.Format(BasePacket.AsSpan(r.Start, r.End - r.Start))} → {Hex.Format(Packet.AsSpan(r.Start, r.End - r.Start))}";
    });
}

/// <summary>Write packets in the exact order they will be sent. Empty = nothing to write.</summary>
public sealed record WritePlan(string Title, IReadOnlyList<PlannedWrite> Writes, IReadOnlyList<string> Notes)
{
    public bool IsEmpty => Writes.Count == 0;
}

/// <summary>
/// Plans writes for the CLI and the UI. Every packet is built via <see cref="PacketBuilder"/> (checked by <see cref="CommandGuard"/>);
/// the base is always data read from the device (settings) or extracted from a capture (keymap, color table).
/// </summary>
public static class WritePlanner
{
    public static string DeviceSettingsLabel => Lang.T("block settings vừa đọc từ thiết bị", "settings block just read from the device");

    public static WritePlan LightingMode(Settings current, byte mode) => LightingEffect(current, mode);

    /// <summary>
    /// Lighting effect (capture batch 6): 0x04 read-modify-write that changes only 0x0A and — if given — that mode's own
    /// brightness / speed (0x38 + 2·mode, high nibble of 0x39 + 2·mode). The vendor app also writes the 0x0A color table, but its
    /// content is identical to the previous write → not written.
    /// </summary>
    public static WritePlan LightingEffect(Settings current, byte mode, byte? brightness = null, byte? speed = null)
    {
        if (!LightingModes.Observed.Contains(mode))
            throw new UnsafeCommandException(Lang.T($"Mode 0x{mode:X2} chưa thấy trong capture. Chỉ cho phép: ",
                                                    $"Mode 0x{mode:X2} not seen in any capture. Allowed only: ") +
                                             string.Join(", ", LightingModes.Observed.Select(m => $"0x{m:X2} ({LightingModes.Describe(m)})")));
        if (current.SelfDefineFlag != 0)
            throw new UnsafeCommandException(Lang.T(
                $"Thiết bị đang ở Self-define (offset 0x09 = {current.SelfDefineFlag:X2} ❓) — chuyển hiệu ứng bằng app hãng một lần trước.",
                $"Device is in Self-define (offset 0x09 = {current.SelfDefineFlag:X2} ❓) — switch effect once with the vendor app first."));
        var effect = LightingModes.Find(mode)!;
        if (brightness is { } b && (!effect.HasBrightness || b > LightingModes.MaxLevel))
            throw new UnsafeCommandException(Lang.T($"{effect.Name}: độ sáng {b} không hợp lệ (0..{LightingModes.MaxLevel}, chỉ hiệu ứng có chỉnh độ sáng).",
                $"{effect.Name}: brightness {b} is invalid (0..{LightingModes.MaxLevel}, only for effects with brightness)."));
        if (speed is { } sp && (!effect.HasSpeed || sp > LightingModes.MaxLevel))
            throw new UnsafeCommandException(Lang.T($"{effect.Name}: tốc độ {sp} không hợp lệ (0..{LightingModes.MaxLevel}, chỉ hiệu ứng có chỉnh tốc độ).",
                $"{effect.Name}: speed {sp} is invalid (0..{LightingModes.MaxLevel}, only for effects with speed)."));

        var updated = current.WithLightingMode(mode);
        if (brightness is { } nb) updated = updated.WithBrightness(mode, nb);
        if (speed is { } ns) updated = updated.WithSpeed(mode, ns);

        int[] allowed = [Settings.LightingModeOffset, Settings.BrightnessOffset(mode), Settings.SpeedOffset(mode)];
        var changes = Hex.DiffRanges(current.Data, updated.Data);
        if (changes.Any(r => Enumerable.Range(r.Start, r.End - r.Start).Any(o => !allowed.Contains(o))))
            throw new InvalidOperationException(Lang.T("Read-modify-write đổi ngoài 0x0A / tham số của mode — dừng.",
                "Read-modify-write changed bytes outside 0x0A / the mode's parameters — aborting."));

        var parts = new List<string> { $"{LightingModes.Describe(current.LightingMode)} → {effect.Name}" };
        if (brightness is { } pb) parts.Add(Lang.T($"độ sáng {current.Brightness(mode)} → {pb}", $"brightness {current.Brightness(mode)} → {pb}"));
        if (speed is { } ps) parts.Add(Lang.T($"tốc độ {current.Speed(mode)} → {ps}", $"speed {current.Speed(mode)} → {ps}"));
        var title = Lang.T("Hiệu ứng ", "Effect ") + string.Join(", ", parts);
        if (changes.Count == 0)
            return new(title, [], [Lang.T("Thiết bị đã đúng hiệu ứng / thông số yêu cầu — không có gì để ghi.",
                "Device already has the requested effect / parameters — nothing to write.")]);

        return new(title, [new($"0x04 settings: mode 0x{current.LightingMode:X2} → 0x{mode:X2}" +
                               (brightness is not null || speed is not null ? $" + 0x{Settings.BrightnessOffset(mode):X2}..0x{Settings.SpeedOffset(mode):X2}" : ""),
            PacketBuilder.WriteSettings(updated), PacketBuilder.WriteSettings(current), DeviceSettingsLabel)
        {
            ExpectedSettings = current,
        }], []);
    }

    /// <summary>2.4G sleep (offset 0x18, unit 30 s). The block is shared by both modes → writing over the cable is enough.</summary>
    public static WritePlan SleepTime(Settings current, byte units)
    {
        if (!SleepTimes.IsAllowed(units))
            throw new UnsafeCommandException(Lang.T($"Sleep {units} × 30 s không phải nấc của app hãng ({SleepTimes.StopsText}).",
                $"Sleep {units} × 30 s is not one of the vendor app's steps ({SleepTimes.StopsText})."));
        var title = $"Sleep {SleepTimes.Describe(current.SleepUnits)} → {SleepTimes.Describe(units)}";
        if (current.SleepUnits == units)
            return new(title, [], [Lang.T("Sleep đã đúng giá trị yêu cầu — không có gì để ghi.",
                "Sleep is already at the requested value — nothing to write.")]);

        var updated = current.WithSleepUnits(units);
        if (Hex.DiffRanges(current.Data, updated.Data) is not [(Settings.SleepOffset, Settings.SleepOffset + 1)])
            throw new InvalidOperationException(Lang.T("Read-modify-write đổi ngoài offset 0x18 — dừng.",
                "Read-modify-write changed bytes outside offset 0x18 — aborting."));
        return new(title, [new($"0x04 settings: sleep 0x{current.SleepUnits:X2} → 0x{units:X2}",
            PacketBuilder.WriteSettings(updated), PacketBuilder.WriteSettings(current), DeviceSettingsLabel)
        {
            ExpectedSettings = current,
        }], [Lang.T("Block settings dùng chung cho chế độ có dây và 2.4G (capture 32 → 33): ghi qua dây có tác dụng ở 2.4G.",
            "The settings block is shared by wired and 2.4G modes (capture 32 → 33): writing over the cable also applies to 2.4G.")]);
    }

    /// <summary>
    /// Writes one keymap page. <paramref name="current"/> = the state the app believes is on the device;
    /// <paramref name="target"/> may differ from the capture baseline only in allowed entries (see <see cref="KeymapRules"/>).
    /// </summary>
    /// <param name="force">Write even if no entry changed (e.g. "Reset to default" to resync when the state may have drifted).</param>
    public static WritePlan Keymap(KeymapPage current, KeymapPage target, string currentLabel, bool force = false)
    {
        if (current.Page != target.Page)
            throw new ArgumentException(Lang.T("current và target phải cùng page.", "current and target must be the same page."));
        _ = KeymapRules.OverridesOf(target); // throws if target touches a special entry or is not a HID usage

        var changed = Enumerable.Range(0, KeymapPage.EntryCount).Where(i => current[i] != target[i]).ToList();
        var title = $"Keymap page {target.Page}";
        if (changed.Count == 0 && !force)
            return new(title, [], [Lang.T("Keymap không đổi — không có gì để ghi.", "Keymap unchanged — nothing to write.")]);

        var notes = changed
            .Select(i => $"Entry {i}: {Describe(current[i])} → {Describe(target[i])}")
            .Append(changed.Count == 0
                ? Lang.T("Không entry nào đổi so với trạng thái app — ghi lại cả page để đồng bộ thiết bị.",
                    "No entry differs from the app's state — rewriting the whole page to resync the device.")
                : Lang.T($"Entry không đổi lấy từ {CaptureBaseline.KeymapSource} (+ override đã áp dụng trước đó).",
                    $"Unchanged entries come from {CaptureBaseline.KeymapSource} (+ previously applied overrides)."))
            .ToList();
        return new(title, [new($"0x03 keymap page {target.Page}", PacketBuilder.WriteKeymap(target),
            PacketBuilder.WriteKeymap(current), currentLabel)], notes);
    }

    /// <summary>
    /// Static color: if not in static mode yet, first write 0x04 (mode = 01, read-modify-write), then 0x0A slot 7 —
    /// the same order as the vendor app in capture 04.
    /// </summary>
    public static WritePlan StaticColor(Settings current, Rgb color)
    {
        var writes = new List<PlannedWrite>();
        var notes = new List<string>();
        if (current.LightingMode != LightingModes.Static)
            writes.Add(SettingsWrite(current, LightingModes.Static));
        else
            notes.Add(Lang.T("Thiết bị đã ở mode static (0x01) — bỏ qua 0x04.", "Device is already in static mode (0x01) — skipping 0x04."));

        var baseTable = CaptureBaseline.ColorTable();
        writes.Add(new(Lang.T($"0x0A bảng màu: slot {ColorTable.StaticColorIndex} {baseTable.StaticColor} → {color}",
                $"0x0A color table: slot {ColorTable.StaticColorIndex} {baseTable.StaticColor} → {color}"),
            PacketBuilder.WriteColorTable(baseTable.WithStaticColor(color)),
            PacketBuilder.WriteColorTable(baseTable), CaptureBaseline.ColorTableSource));
        notes.Add(Lang.T("Chưa có lệnh đọc bảng màu: các slot khác lấy nguyên từ capture.",
            "No color-table read command yet: all other slots are taken unchanged from the capture."));
        return new(Lang.T($"Màu static {color}", $"Static color {color}"), writes, notes);
    }

    private static PlannedWrite SettingsWrite(Settings current, byte mode)
    {
        var updated = current.WithLightingMode(mode);
        if (Hex.DiffRanges(current.Data, updated.Data) is not [(Settings.LightingModeOffset, Settings.LightingModeOffset + 1)])
            throw new InvalidOperationException(Lang.T("Read-modify-write đổi ngoài offset 0x0A — dừng.",
                "Read-modify-write changed bytes outside offset 0x0A — aborting."));
        return new($"0x04 settings: mode 0x{current.LightingMode:X2} → 0x{mode:X2}",
            PacketBuilder.WriteSettings(updated), PacketBuilder.WriteSettings(current), DeviceSettingsLabel)
        {
            ExpectedSettings = current,
        };
    }

    private static string Describe(KeymapEntry e) => KeymapActions.Describe(e);
}
