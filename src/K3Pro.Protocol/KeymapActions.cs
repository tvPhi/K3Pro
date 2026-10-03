namespace K3Pro.Protocol;

public enum KeymapActionKind { Key, Modifier, Combo, Media, Mouse, Command }

/// <summary>A key-assignment choice with a fixed entry (modifier / media / mouse / command).</summary>
/// <remarks>
/// Parameter <c>Name</c> = Vietnamese name, <c>NameEn</c> = English name (null → <c>Name</c> is used for both).
/// The <c>Name</c> property picks by <see cref="Lang.Current"/> when read; equality (record) does not depend on the language.
/// </remarks>
public sealed record KeymapAction(KeymapActionKind Kind, string Name, string Short, KeymapEntry Entry, string? NameEn = null)
{
    private readonly string _nameVi = Name;

    public string Name
    {
        get => NameEn is null ? _nameVi : Lang.T(_nameVi, NameEn);
        init => _nameVi = value;
    }

    public override string ToString() => Name;
}

/// <summary>
/// Key-assignment kinds beyond regular keys — ONLY entries present in capture batch 2 (2.4G, 2026-10-02) or derived from the
/// confirmed modifier bitmask. Entry: <c>[type][p1][p2][code]</c>.
/// ❓ Not yet: right modifiers (bits 0x10–0x80), other media (Vol−, Play…), right / middle mouse button / wheel,
/// FN2 (the vendor app writes 3 pages), macro.
/// </summary>
public static class KeymapActions
{
    public const byte ModCtrl = 0x01;   // capture 09, 10
    public const byte ModShift = 0x02;  // capture 11
    public const byte ModAlt = 0x04;    // capture 11
    public const byte ModWin = 0x08;    // capture 15
    public const byte LeftModifierMask = ModCtrl | ModShift | ModAlt | ModWin;

    /// <summary>Standalone modifier: <c>00 mask 00 00</c> (capture 09: LCtrl = 00 01 00 00).</summary>
    public static readonly IReadOnlyList<KeymapAction> Modifiers =
    [
        new(KeymapActionKind.Modifier, "Left Ctrl", "LCtrl", new(0x00, ModCtrl, 0x00, 0x00)),
        new(KeymapActionKind.Modifier, "Left Shift", "LShift", new(0x00, ModShift, 0x00, 0x00)),
        new(KeymapActionKind.Modifier, "Left Alt", "LAlt", new(0x00, ModAlt, 0x00, 0x00)),
        new(KeymapActionKind.Modifier, "Left Win", "LWin", new(0x00, ModWin, 0x00, 0x00)),
    ];

    /// <summary>Multimedia = consumer usage, type 02 (capture 12: Vol+ E9; FN1 baseline: Mute E2).</summary>
    public static readonly IReadOnlyList<KeymapAction> Media =
    [
        new(KeymapActionKind.Media, "Volume +", "Vol+", new(0x02, 0x00, 0x00, 0xE9)),
        new(KeymapActionKind.Media, "Mute", "Mute", new(0x02, 0x00, 0x00, 0xE2)),
    ];

    /// <summary>Mouse, type 01 (capture 13: left button = 01 01 01 00; ❓ meaning of p2).</summary>
    public static readonly IReadOnlyList<KeymapAction> Mouse =
    [
        new(KeymapActionKind.Mouse, "Chuột: nút trái", "🖱 L", new(0x01, 0x01, 0x01, 0x00), NameEn: "Mouse: left button"),
    ];

    /// <summary>The vendor app's Commands tab (capture 15: lock PC = Win + L, key in p2).</summary>
    public static readonly IReadOnlyList<KeymapAction> Commands =
    [
        new(KeymapActionKind.Command, "Khóa máy (Win + L)", "🔒 Lock", new(0x00, ModWin, 0x0F, 0x00), NameEn: "Lock PC (Win + L)"),
    ];

    public static IEnumerable<KeymapAction> FixedActions => Modifiers.Concat(Media).Concat(Mouse).Concat(Commands);

    /// <summary>Key combo: <c>00 mask 00 key</c> (capture 10: Ctrl+C = 00 01 00 06; 11: Shift+Alt+A = 00 06 00 04).</summary>
    public static KeymapEntry Combo(byte modifiers, byte key)
    {
        var e = new KeymapEntry(0x00, modifiers, 0x00, key);
        if (!IsCombo(e))
            throw new UnsafeCommandException(Lang.T(
                $"Tổ hợp không hợp lệ: modifier 0x{modifiers:X2} (chỉ Ctrl/Shift/Alt/Win trái), phím 0x{key:X2}.",
                $"Invalid combo: modifier 0x{modifiers:X2} (left Ctrl/Shift/Alt/Win only), key 0x{key:X2}."));
        return e;
    }

    public static bool IsCombo(KeymapEntry e) =>
        e.Type == 0x00 && e.P1 != 0 && (e.P1 & ~LeftModifierMask) == 0 && e.P2 == 0 && KeymapRules.IsAllowedHidUsage(e.Code);

    public static bool IsKey(KeymapEntry e) => e.IsHidUsage && KeymapRules.IsAllowedHidUsage(e.Code);

    /// <summary>Entries the tool may write to a key (besides restoring that key's exact default entry).</summary>
    public static bool IsAllowed(KeymapEntry e) => IsKey(e) || IsCombo(e) || FixedActions.Any(a => a.Entry == e);

    public static KeymapActionKind? Classify(KeymapEntry e) =>
        IsKey(e) ? KeymapActionKind.Key
        : IsCombo(e) ? KeymapActionKind.Combo
        : FixedActions.FirstOrDefault(a => a.Entry == e)?.Kind;

    public static string ModifierText(byte mask) => string.Join("+", new[]
    {
        (mask & ModCtrl) != 0 ? "Ctrl" : null,
        (mask & ModShift) != 0 ? "Shift" : null,
        (mask & ModAlt) != 0 ? "Alt" : null,
        (mask & ModWin) != 0 ? "Win" : null,
    }.Where(x => x is not null));

    /// <summary>Short label drawn on the key; null if the entry is not of a known kind.</summary>
    public static string? ShortLabel(KeymapEntry e) =>
        IsKey(e) ? HidUsages.Name(e.Code)
        : IsCombo(e) ? $"{ModifierText(e.P1)}+{HidUsages.Name(e.Code)}"
        : FixedActions.FirstOrDefault(a => a.Entry == e)?.Short;

    /// <summary>Full description (log, write-plan notes).</summary>
    public static string Describe(KeymapEntry e) =>
        IsKey(e) ? $"{HidUsages.Name(e.Code)} (0x{e.Code:X2})"
        : IsCombo(e) ? $"{ModifierText(e.P1)} + {HidUsages.Name(e.Code)} ({e})"
        : FixedActions.FirstOrDefault(a => a.Entry == e) is { } a ? $"{a.Name} ({e})"
        : e.IsEmpty ? Lang.T("(trống — mặc định firmware)", "(empty — firmware default)") : Lang.T($"mặc định {e}", $"default {e}");
}
