using K3Pro.Protocol;

namespace K3Pro.Cli;

/// <summary>
/// Commands on the real device — the connection is picked automatically: wired (258A:010C) if present, else the 2.4G receiver (3554:FA09).
/// Reads run directly. Writes default to dry-run: print every packet / frame to be sent; only sent with --send — exactly the bytes printed.
/// </summary>
internal static class DeviceCommands
{
    private static IK3ProConnection Open()
    {
        var c = K3ProConnections.Open();
        Console.WriteLine($"Kết nối: {(c.Kind == ConnectionKind.Wired ? "có dây" : "2.4G qua receiver")} — {c.Description}");
        if (K3ProConnections.IsVendorAppRunning()) Console.WriteLine(K3ProConnections.VendorAppWarning);
        return c;
    }

    private static string ReadInfoName(IK3ProConnection c) => c.Kind == ConnectionKind.Wired ? "0x82" : "0x05 (2.4G)";

    private static string ReadSettingsName(IK3ProConnection c) => c.Kind == ConnectionKind.Wired ? "0x84" : "0x44 (2.4G)";

    public static int DeviceInfoCmd()
    {
        using var c = Open();
        var info = c.ReadDeviceInfo();
        Console.WriteLine($"{ReadInfoName(c)} → {info}  {(info.MatchesCapture ? "✅ khớp capture" : $"⚠ KHÁC capture ({Hex.Format(DeviceInfo.CaptureValue)})")}");
        return info.MatchesCapture ? 0 : 1;
    }

    public static int ReadSettingsCmd()
    {
        using var c = Open();
        var raw = c.ReadSettingsRaw();
        Console.WriteLine($"{ReadSettingsName(c)} → {raw.Length} byte:");
        Console.WriteLine(Hex.Dump(raw));

        Settings settings;
        try { settings = Settings.Parse(raw); }
        catch (InvalidDataException ex)
        {
            Console.WriteLine($"⚠ {ex.Message}");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine($"  0x{Settings.LightingModeOffset:X2}       LightingMode = 0x{settings.LightingMode:X2} ({LightingModes.Describe(settings.LightingMode)})");
        Console.WriteLine($"  0x{Settings.SleepOffset:X2}       Sleep        = 0x{settings.SleepUnits:X2} ({SleepTimes.Describe(settings.SleepUnits)}, chế độ 2.4G)");
        Console.WriteLine($"  0x{Settings.MagicOffset:X2}..0x{Settings.Length - 1:X2} magic        = {Hex.Format(raw.AsSpan(Settings.MagicOffset))} ✅");

        var reference = CaptureBaseline.ReferenceSettings();
        var diff = Hex.DiffRanges(reference.Data, settings.Data);
        Console.WriteLine(diff.Count == 0
            ? $"  Giống hệt block đọc trong {CaptureBaseline.SettingsSource}."
            : $"  Khác {CaptureBaseline.SettingsSource} ở: " + string.Join(", ", diff.Select(r =>
                $"0x{r.Start:X2}{(r.End - r.Start > 1 ? $"..0x{r.End - 1:X2}" : "")} " +
                $"({Hex.Format(reference.Data[r.Start..r.End])} → {Hex.Format(settings.Data[r.Start..r.End])})")));
        return 0;
    }

    public static int SetMode(CliArgs a)
    {
        var mode = CliArgs.ParseNumber(a.Arg(0, "n"), "n");
        if (mode is < 0 or > 0xFF) throw new UsageException("<n> phải là 1 byte.");

        using var c = Open();
        var current = c.ReadSettings(); // read-modify-write: the base is always the block just read, magic already checked
        Console.WriteLine($"Hiện tại: LightingMode = 0x{current.LightingMode:X2} ({LightingModes.Describe(current.LightingMode)})");
        return Run(a, c, WritePlanner.LightingMode(current, (byte)mode));
    }

    public static int SetSleep(CliArgs a)
    {
        var units = SleepTimes.Parse(a.Arg(0, "thời gian"));

        using var c = Open();
        var current = c.ReadSettings(); // read-modify-write, only offset 0x18 changes
        Console.WriteLine($"Hiện tại: Sleep = 0x{current.SleepUnits:X2} ({SleepTimes.Describe(current.SleepUnits)})");
        return Run(a, c, WritePlanner.SleepTime(current, units));
    }

    public static int SetKey(CliArgs a)
    {
        var index = CliArgs.ParseNumber(a.Arg(0, "index"), "index");
        var code = CliArgs.ParseHex(a.Arg(1, "hidcode"), "hidcode");
        var page = a.Option("--page") is { } p ? CliArgs.ParseNumber(p, "page") : 0;
        if (page is < 0 or > KeymapPage.MaxPage) throw new UsageException($"--page phải 0–{KeymapPage.MaxPage}.");
        if (code is < 0 or > 0xFF) throw new UsageException("<hidcode> phải là 1 byte.");

        // The CLI keeps no state: base = keymap from the capture, only this exact entry changes.
        var basePage = CaptureBaseline.KeymapPage((byte)page);
        var target = KeymapRules.Apply((byte)page, new Dictionary<int, byte> { [index] = (byte)code });
        Console.WriteLine($"⚠ Base = keymap trong capture ({CaptureBaseline.KeymapSource}): mọi entry khác của page {page} " +
                          "sẽ về đúng giá trị đó (vd Num1 = 0x59 dù thiết bị đang là A/B).");

        using var c = Open();
        return Run(a, c, WritePlanner.Keymap(basePage, target, CaptureBaseline.KeymapSource),
            "(Chưa có lệnh đọc keymap — kiểm tra bằng cách bấm phím.)");
    }

    public static int SetStaticColor(CliArgs a)
    {
        var color = Rgb.Parse(a.Arg(0, "RRGGBB"));

        using var c = Open();
        var current = c.ReadSettings(); // to decide whether 0x04 (mode = 01) is needed
        Console.WriteLine($"Hiện tại: LightingMode = 0x{current.LightingMode:X2} ({LightingModes.Describe(current.LightingMode)})");
        return Run(a, c, WritePlanner.StaticColor(current, color));
    }

    private static int Run(CliArgs a, IK3ProConnection c, WritePlan plan, string? after = null)
    {
        if (WireEncoding.WhyUnsupported(c.Kind, plan) is { } why) throw new UnsafeCommandException(why);
        if (!Print(a, plan, c.Kind)) return 0;
        c.Execute(plan, w => Console.WriteLine($"Đã gửi: {w.Title}" +
            (w.Command == Command.WriteSettings ? $" — đọc lại {ReadSettingsName(c)} khớp block đã ghi ✅" : "")));
        if (after is not null) Console.WriteLine(after);
        return 0;
    }

    /// <summary>Prints the write plan: every packet / frame on the wire + diff vs. base. true = allowed to send (--send).</summary>
    private static bool Print(CliArgs a, WritePlan plan, ConnectionKind kind)
    {
        foreach (var note in plan.Notes) Console.WriteLine($"• {note}");
        if (plan.IsEmpty) return false;

        var send = a.Has("--send");
        Console.WriteLine();
        Console.WriteLine(send
            ? $"=== SEND — {plan.Writes.Count} lệnh dưới đây SẼ được gửi xuống thiết bị ({(kind == ConnectionKind.Wired ? "có dây" : "qua receiver 2.4G")}) ==="
            : "=== DRY-RUN — KHÔNG gửi (thêm --send để gửi thật) ===");
        for (int i = 0; i < plan.Writes.Count; i++)
        {
            var w = plan.Writes[i];
            var h = w.Header;
            Console.WriteLine();
            Console.WriteLine($"[{i + 1}/{plan.Writes.Count}] {w.Title}");
            Console.WriteLine($"Lệnh : 0x{h.Command:X2} {w.Command}, page 0x{h.Page:X2}, len 0x{h.DataLength:X4} ({h.DataLength} byte data)");
            Console.WriteLine($"Base : {w.BaseLabel}");
            var changes = w.DescribeChanges().ToList();
            Console.WriteLine(changes.Count == 0 ? "Thay đổi so với base: (không có)" : "Thay đổi so với base:");
            foreach (var c in changes) Console.WriteLine($"  {c}");
            if (w.ExpectedSettings is not null)
                Console.WriteLine("RMW  : đọc lại settings trước khi gửi (phải khớp base) và sau khi gửi (phải khớp gói).");

            var wire = WireEncoding.Encode(kind, w);
            if (kind == ConnectionKind.Wired)
            {
                Console.WriteLine($"Gói {w.Packet.Length} byte:");
                Console.WriteLine(Hex.Dump(w.Packet));
            }
            else
            {
                Console.WriteLine($"{wire.Count} khung × 20 byte qua receiver (mỗi khung chờ receiver echo lại):");
                foreach (var f in wire) Console.WriteLine($"  {Hex.Format(f)}");
            }
        }
        return send;
    }
}
