namespace K3Pro.Protocol;

/// <summary>
/// Base data extracted verbatim from captures/. There is no read command for the keymap / color table yet (sending 0x83 etc. is FORBIDDEN),
/// so every 0x03 / 0x0A write must take its base from here and modify only the exact entry / slot being changed.
/// The golden tests (tests/K3Pro.Protocol.Tests) check every byte of this data against the capture.
/// </summary>
public static class CaptureBaseline
{
    public const string KeymapSource = "captures/02-num1-to-A.pcapng f9128..f9161 (entry 4 page 0 restored to 0x59 Num1)";
    public const string ColorTableSource = "captures/05-rgb-blue.pcapng f10324";
    public const string SettingsSource = "captures/05-rgb-blue.pcapng f10284";

    /// <summary>
    /// Page 0 = default keymap. Captures 02/03 differ only in entry 4 (Num1 → A / B); every other entry is identical;
    /// entry 4 defaults to 0x59 (Num1) per PROTOCOL.md.
    /// </summary>
    private static readonly Dictionary<int, KeymapEntry> Page0 = new()
    {
        [0] = new(0x00, 0x00, 0x00, 0x2A),  // Backspace
        [1] = new(0x00, 0x00, 0x00, 0x53),  // NumLock
        [2] = new(0x00, 0x00, 0x00, 0x5F),  // Num 7
        [3] = new(0x00, 0x00, 0x00, 0x5C),  // Num 4
        [4] = new(0x00, 0x00, 0x00, 0x59),  // Num 1 (capture 02: 0x04 A, capture 03: 0x05 B)
        [5] = new(0x00, 0x00, 0x00, 0x62),  // Num 0
        [6] = new(0x0D, 0x00, 0x00, 0x00),  // ❓ Fn
        [7] = new(0x00, 0x00, 0x00, 0x54),  // Num /
        [8] = new(0x00, 0x00, 0x00, 0x60),  // Num 8
        [9] = new(0x00, 0x00, 0x00, 0x5D),  // Num 5
        [10] = new(0x00, 0x00, 0x00, 0x5A), // Num 2
        [13] = new(0x00, 0x00, 0x00, 0x55), // Num *
        [14] = new(0x00, 0x00, 0x00, 0x61), // Num 9
        [15] = new(0x00, 0x00, 0x00, 0x5E), // Num 6
        [16] = new(0x00, 0x00, 0x00, 0x5B), // Num 3
        [17] = new(0x00, 0x00, 0x00, 0x63), // Num .
        [18] = new(0x07, 0x00, 0x00, 0x11), // ❓ knob press — KB.ini: "切模式" (switch mode)
        [19] = new(0x00, 0x00, 0x00, 0x56), // Num -
        [20] = new(0x00, 0x00, 0x00, 0x57), // Num +
        [22] = new(0x00, 0x00, 0x00, 0x58), // Num Enter
        // 11 / 23 empty = knob rotate left / right (KB.ini 左旋 / 右旋); 12 / 21 empty = no key.
    };

    /// <summary>❓ Page 1 (possibly the Fn layer). Every byte kept unchanged as in capture 02 f9138.</summary>
    private static readonly Dictionary<int, KeymapEntry> Page1 = new()
    {
        [0] = new(0x07, 0x00, 0x00, 0x04),
        [2] = new(0x07, 0x00, 0x00, 0x05),
        [5] = new(0x07, 0x00, 0x00, 0x0A),
        [7] = new(0x08, 0x00, 0x00, 0x00),
        [8] = new(0x07, 0x00, 0x00, 0x06),
        [13] = new(0x08, 0x02, 0x00, 0x00),
        [14] = new(0x07, 0x00, 0x00, 0x07),
        [18] = new(0x02, 0x00, 0x00, 0xE2),
        [19] = new(0x07, 0x00, 0x00, 0x08),
        [20] = new(0x08, 0x04, 0x00, 0x00),
    };

    /// <summary>Pages 2–3: all zeros in capture 02 (f9150, f9161).</summary>
    public static KeymapPage KeymapPage(byte page)
    {
        var entries = page switch
        {
            0 => Page0,
            1 => Page1,
            2 or 3 => [],
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, $"Page 0–{Protocol.KeymapPage.MaxPage}."),
        };
        var data = new byte[Protocol.KeymapPage.ByteLength];
        foreach (var (index, entry) in entries)
            entry.WriteTo(data.AsSpan(index * KeymapEntry.Size));
        return Protocol.KeymapPage.Parse(page, data);
    }

    /// <summary>
    /// ❓ 19 modes × 7 colors. Slot 7 (start of row 1) = static color: 0000FF in capture 05, FF0000 in capture 04 — only this slot differs.
    /// </summary>
    private static readonly string[] ColorRows =
    [
        "000000 000000 000000 000000 000000 000000 000000",
        "0000FF 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
        "FF0000 0000FF 00FF00 FFFF00 FF00FF 00FFFF FFFFFF",
    ];

    public static ColorTable ColorTable() => Protocol.ColorTable.Parse(Hex.Parse(string.Concat(ColorRows)));

    /// <summary>
    /// Settings block read in capture 05. ONLY for comparison when reading the device — NEVER used as a write base
    /// (settings are always read-modify-write from the device).
    /// </summary>
    public static Settings ReferenceSettings() => Settings.Parse(Hex.Parse(
        "00 03 03 00 00 00 04 04 07 00 01 20 01 00 00 00" +
        "00 00 01 00 04 04 00 FF 0A 00 00 00 00 00 00 00" +
        "00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00" +
        "00 00 00 00 00 00 00 00 FF FF 04 40 04 47 04 47" +
        "04 47 04 47 04 47 04 47 04 47 04 47 04 47 04 47" +
        "04 47 04 47 04 47 04 47 04 47 04 47 04 47 09 47" +
        "09 47 07 47 07 47 07 44 07 44 07 44 07 44 07 44" +
        "07 44 07 44 09 09 09 09 09 09 09 09 09 09 5A A5"));
}
