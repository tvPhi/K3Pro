namespace K3Pro.Protocol;

/// <summary>
/// Keymap editing rules while there is NO read command yet: every page is derived from <see cref="CaptureBaseline"/> plus
/// HID usage overrides on the editable entries. Entries without an override always stay exactly as in the capture.
/// </summary>
public static class KeymapRules
{
    /// <summary>
    /// HID usages allowed for assignment (type 00): exactly the keys in the vendor app's "Keyboard" tab, groups Comm / Adv / Keypad.
    /// ❓ Not allowed yet: the Modify group (LCtrl… — may be encoded via p1 instead of codes E0–E7), FN / FN2 (type 0D),
    /// and every usage the vendor app does not offer (F13–F24, Mute/Vol on the keyboard page…). Enable once captured.
    /// </summary>
    public static readonly IReadOnlySet<byte> AllowedHidUsages = BuildAllowed();

    public static bool IsAllowedHidUsage(int code) => code is >= 0 and <= 0xFF && AllowedHidUsages.Contains((byte)code);

    private static HashSet<byte> BuildAllowed()
    {
        var set = new HashSet<byte>();
        void Range(int from, int to) { for (int c = from; c <= to; c++) set.Add((byte)c); }
        Range(0x04, 0x31); // A–Z, 1–0, Enter, Esc, Backspace, Tab, Space, - = [ ] backslash
        Range(0x33, 0x38); // ; ' ` , . /
        Range(0x39, 0x52); // Caps Lock, F1–F12, Print Screen, Scroll Lock, Pause, Insert, Home, PgUp, Delete, End, PgDn, arrows
        Range(0x53, 0x63); // Num Lock + the whole keypad
        set.Add(0x65);     // Application ("App")
        return set;
    }

    /// <summary>
    /// Special page-0 entries where a capture shows the vendor app writing a HID usage AND writing back the exact default value
    /// (capture 17–17f): "reset to default" = write back the baseline entry (07 00 00 11 / 0D 00 00 00 / 00 00 00 00),
    /// byte-identical to the vendor app.
    /// </summary>
    public static IReadOnlyDictionary<int, string> CapturedSpecialEntries => new Dictionary<int, string>
    {
        [6] = Lang.T("phím Fn (0D 00 00 00) — capture 17e / 17f", "Fn key (0D 00 00 00) — capture 17e / 17f"),
        [11] = Lang.T("xoay núm trái (trống → firmware chỉnh âm lượng) — capture 17 / 17d",
            "knob turn left (empty → firmware adjusts volume) — capture 17 / 17d"),
        [18] = Lang.T("nhấn núm (07 00 00 11 = đổi chức năng núm) — capture 17c / 17d",
            "knob press (07 00 00 11 = switch knob function) — capture 17c / 17d"),
        [23] = Lang.T("xoay núm phải (trống → firmware chỉnh âm lượng) — capture 17b / 17d",
            "knob turn right (empty → firmware adjusts volume) — capture 17b / 17d"),
    };

    /// <summary>
    /// Entries that may be replaced with a HID usage: entries that are a HID usage in the capture, or captured special entries
    /// (<see cref="CapturedSpecialEntries"/>, page 0 only). Other entries (FN1 type 07 / 08 / 02, keyless index 12 / 21) stay unchanged.
    /// </summary>
    public static bool IsEditable(byte page, int index) =>
        index is >= 0 and < KeymapPage.MatrixSize &&
        (CaptureBaseline.KeymapPage(page)[index].IsHidUsage || (page == 0 && CapturedSpecialEntries.ContainsKey(index)));

    /// <summary>Builds page = capture baseline + HID usage overrides (index → key code). Throws if an override is invalid.</summary>
    public static KeymapPage Apply(byte page, IReadOnlyDictionary<int, byte> hidOverrides)
    {
        foreach (var code in hidOverrides.Values) EnsureAllowedHidUsage(code);
        return Apply(page, hidOverrides.ToDictionary(o => o.Key, o => KeymapEntry.HidUsage(o.Value)));
    }

    /// <summary>
    /// Builds page = capture baseline + entry overrides (regular key / modifier / combo / media / mouse / command — see
    /// <see cref="KeymapActions"/>). Throws if the index is not editable or the entry is not seen in any capture.
    /// </summary>
    public static KeymapPage Apply(byte page, IReadOnlyDictionary<int, KeymapEntry> overrides)
    {
        var result = CaptureBaseline.KeymapPage(page);
        foreach (var (index, entry) in overrides.OrderBy(o => o.Key))
        {
            EnsureEditable(page, index);
            EnsureAllowedEntry(entry);
            result = result.With(index, entry);
        }
        return result;
    }

    /// <summary>
    /// Inverse of <see cref="Apply(byte, IReadOnlyDictionary{int, KeymapEntry})"/>: the entries that differ from the capture baseline.
    /// </summary>
    public static IReadOnlyDictionary<int, KeymapEntry> OverridesOf(KeymapPage target)
    {
        var baseline = CaptureBaseline.KeymapPage(target.Page);
        var overrides = new SortedDictionary<int, KeymapEntry>();
        for (int i = 0; i < KeymapPage.EntryCount; i++)
        {
            if (target[i] == baseline[i]) continue;
            EnsureEditable(target.Page, i);
            EnsureAllowedEntry(target[i]);
            overrides[i] = target[i];
        }
        return overrides;
    }

    public static void EnsureAllowedEntry(KeymapEntry entry)
    {
        if (!KeymapActions.IsAllowed(entry))
            throw new UnsafeCommandException(Lang.T(
                $"Entry {entry} chưa có trong capture (chỉ phím thường, modifier / tổ hợp Ctrl·Shift·Alt·Win trái, " +
                "Vol+ / Mute, chuột trái, khóa máy).",
                $"Entry {entry} not seen in any capture (only regular keys, left Ctrl·Shift·Alt·Win modifiers / combos, " +
                "Vol+ / Mute, left mouse button, lock PC)."));
    }

    public static void EnsureEditable(byte page, int index)
    {
        if (index is < 0 or >= KeymapPage.MatrixSize)
            throw new UnsafeCommandException(Lang.T($"Index {index} ngoài matrix 0–{KeymapPage.MatrixSize - 1}.",
                $"Index {index} outside matrix 0–{KeymapPage.MatrixSize - 1}."));
        var entry = CaptureBaseline.KeymapPage(page)[index];
        if (!IsEditable(page, index))
            throw new UnsafeCommandException(entry.IsEmpty
                ? Lang.T($"Entry {index} page {page} trống và chưa có capture app hãng ghi vào — không ghi.",
                    $"Entry {index} page {page} is empty and no capture shows the vendor app writing it — not writing.")
                : Lang.T($"Entry {index} page {page} là entry đặc biệt ❓ ({entry}) chưa có capture — không ghi đè.",
                    $"Entry {index} page {page} is a special entry ❓ ({entry}) with no capture — not overwriting."));
    }

    public static void EnsureAllowedHidUsage(int code)
    {
        if (!IsAllowedHidUsage(code))
            throw new UnsafeCommandException(Lang.T(
                $"HID usage 0x{code:X2} chưa được phép (chỉ các phím tab Keyboard của app hãng; " +
                "modifier / FN / media cần capture trước).",
                $"HID usage 0x{code:X2} not allowed (only the vendor app's Keyboard-tab keys; " +
                "modifier / FN / media need a capture first)."));
    }
}
