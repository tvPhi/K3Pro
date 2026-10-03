using CommunityToolkit.Mvvm.ComponentModel;
using K3Pro.Protocol;
using static K3Pro.Protocol.Lang;

namespace K3Pro.App.ViewModels;

/// <summary>A key picker button. <see cref="Entry"/> null = in the vendor app but not captured yet → dimmed, not writable.</summary>
public partial class PickerItem(string label, KeymapEntry? entry, string? tooltip = null) : ObservableObject
{
    public string Label { get; } = label;
    public KeymapEntry? Entry { get; } = entry;
    public bool IsEnabled => Entry is not null;
    public string Tooltip { get; } = tooltip ?? (entry is { } e ? KeymapActions.Describe(e)
        : T("🔒 Chưa có capture — sẽ mở khi có dữ liệu", "🔒 No capture yet — unlocks once captured"));

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;
}

public partial class PickerSection(string title, IReadOnlyList<PickerItem> items) : ObservableObject
{
    public string Title { get; } = title;
    public IReadOnlyList<PickerItem> Items { get; } = items;

    /// <summary>false when the search box filters out every button in the group.</summary>
    [ObservableProperty]
    public partial bool IsVisible { get; set; } = true;
}

public sealed record PickerTab(string Header, IReadOnlyList<PickerSection> Sections, string? Note = null)
{
    public bool HasNote => Note is not null;

    public IEnumerable<PickerItem> AllItems => Sections.SelectMany(s => s.Items);
}

/// <summary>
/// Picker laid out like the vendor app (Key assignment). Items with an entry = captured (see <see cref="KeymapActions"/>);
/// items without an entry are just labels so the user sees the vendor app's full list — never write guessed values.
/// Labels follow <see cref="Lang.Current"/> at build time — rebuild on language switch.
/// </summary>
public static class KeyPickerCatalog
{
    public const int KeyboardTab = 0, MouseTab = 1, MediaTab = 2, MacroTab = 3, CommandTab = 4, ComboTab = 5;

    private static PickerItem Key(string label, byte code) => new(label, KeymapEntry.HidUsage(code));

    private static PickerItem Locked(string label) => new(label, null);

    private static PickerItem Action(KeymapAction action, string label) => new(label, action.Entry, action.Name);

    public static IReadOnlyList<PickerTab> Build()
    {
        var comm = new List<PickerItem>();
        for (int i = 0; i < 26; i++) comm.Add(Key(((char)('A' + i)).ToString(), (byte)(0x04 + i)));
        for (int i = 1; i <= 9; i++) comm.Add(Key(i.ToString(), (byte)(0x1D + i)));
        comm.Add(Key("0", 0x27));
        (string, byte)[] symbols = [("- _", 0x2D), ("= +", 0x2E), ("[ {", 0x2F), ("] }", 0x30), ("\\ |", 0x31), ("; :", 0x33),
            ("' \"", 0x34), ("` ~", 0x35), (", <", 0x36), (". >", 0x37), ("/ ?", 0x38)];
        comm.AddRange(symbols.Select(s => Key(s.Item1, s.Item2)));

        var adv = new List<PickerItem>();
        for (int i = 1; i <= 12; i++) adv.Add(Key($"F{i}", (byte)(0x39 + i)));
        (string, byte)[] advKeys = [("Esc", 0x29), ("Tab", 0x2B), ("Caps", 0x39), ("Enter", 0x28), ("Space", 0x2C), ("Back", 0x2A),
            ("Ins", 0x49), ("Del", 0x4C), ("Home", 0x4A), ("End", 0x4D), ("PgUp", 0x4B), ("PgDn", 0x4E),
            ("PrtSc", 0x46), ("ScrLk", 0x47), ("Pause", 0x48), ("App", 0x65), ("←", 0x50), ("→", 0x4F), ("↑", 0x52), ("↓", 0x51)];
        adv.AddRange(advKeys.Select(k => Key(k.Item1, k.Item2)));

        var keypad = new List<PickerItem>();
        for (int i = 0; i <= 9; i++) keypad.Add(Key($"Num{i}", (byte)(i == 0 ? 0x62 : 0x58 + i)));
        (string, byte)[] padKeys = [("Num +", 0x57), ("Num −", 0x56), ("Num *", 0x55), ("Num /", 0x54), ("Num .", 0x63), ("Num Enter", 0x58), ("NumLock", 0x53)];
        keypad.AddRange(padKeys.Select(k => Key(k.Item1, k.Item2)));

        var modify = KeymapActions.Modifiers.Select(m => Action(m, m.Short)).ToList();
        modify.AddRange(new[] { "RCtrl", "RShift", "RAlt", "RWin" }.Select(Locked));

        var layer = new List<PickerItem> { Locked("FN"), Locked("FN2") };

        var mouse = new List<PickerItem> { Action(KeymapActions.Mouse[0], T("🖱 Chuột trái", "🖱 Left click")) };
        mouse.AddRange(new[]
        {
            T("🖱 Chuột phải", "🖱 Right click"), T("🖱 Chuột giữa", "🖱 Middle click"), "⏪ Back", "⏩ Forward",
            T("⬆ Cuộn lên", "⬆ Scroll up"), T("⬇ Cuộn xuống", "⬇ Scroll down"), "⏸ Double click",
        }.Select(Locked));

        var media = new List<PickerItem>
        {
            Locked("⏯ Play/Pause"), Action(KeymapActions.Media[1], "🔇 Mute"), Locked("⏹ Stop"), Locked("⏮ Previous"), Locked("⏭ Next"),
            Action(KeymapActions.Media[0], "🔊 Volume +"), Locked("🔉 Volume −"),
        };
        var mediaApps = new List<PickerItem> { Locked("🏠 Home page"), Locked("🧮 Calculator"), Locked("✉ Email"), Locked("💻 My computer"),
            Locked("⭐ Favorites"), Locked("🔆 Screen +"), Locked("🔅 Screen −") };

        var commands = new List<PickerItem>
        {
            Locked(T("✖ Đóng cửa sổ", "✖ Close window")), Action(KeymapActions.Commands[0], T("🔒 Khóa máy", "🔒 Lock PC")), Locked("▶ Run"), Locked("🖥 Show desktop"),
            Locked("⤢ Zoom in"), Locked("⤡ Zoom out"), Locked("📋 Task manager"), Locked("↶ Undo"), Locked("💾 Save"),
            Locked("⊞ Lock Win"), Locked("⌨ Lock Keyboard"),
        };

        return
        [
            new(T("⌨ Bàn phím", "⌨ Keyboard"),
                [new(T("Chữ & số", "Letters & digits"), comm), new(T("Chức năng", "Function"), adv), new("Keypad", keypad), new("Modifier", modify), new("Layer", layer)]),
            new(T("🖱 Chuột", "🖱 Mouse"), [new(T("Chuột", "Mouse"), mouse)],
                T("Mới có capture nút trái. Các nút khác mở khi có capture.", "Only the left button has a capture so far. Other buttons unlock once captured.")),
            new("🎵 Media", [new(T("Phát nhạc / âm lượng", "Playback / volume"), media), new(T("Ứng dụng / màn hình", "Apps / screen"), mediaApps)],
                T("Mới có capture Mute và Volume +.", "Only Mute and Volume + have captures so far.")),
            new("Macro", [], T("Macro: đã giải mã một phần (capture 16) — chưa hỗ trợ ghi.", "Macro: partially decoded (capture 16) — writing is not supported yet.")),
            new(T("⚡ Lệnh", "⚡ Commands"), [new(T("Lệnh hệ thống", "System commands"), commands)],
                T("Mới có capture Khóa máy (Win + L).", "Only Lock PC (Win + L) has a capture so far.")),
        ];
    }
}
