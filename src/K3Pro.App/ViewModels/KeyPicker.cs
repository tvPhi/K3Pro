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
        : T("picker.no_capture_yet_unlocks_once"));

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

        var mouse = new List<PickerItem> { Action(KeymapActions.Mouse[0], T("picker.left_click")) };
        mouse.AddRange(new[]
        {
            T("picker.right_click"), T("picker.middle_click"), "⏪ Back", "⏩ Forward",
            T("picker.scroll_up"), T("picker.scroll_down"), "⏸ Double click",
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
            Locked(T("picker.close_window")), Action(KeymapActions.Commands[0], T("picker.lock_pc")), Locked("▶ Run"), Locked("🖥 Show desktop"),
            Locked("⤢ Zoom in"), Locked("⤡ Zoom out"), Locked("📋 Task manager"), Locked("↶ Undo"), Locked("💾 Save"),
            Locked("⊞ Lock Win"), Locked("⌨ Lock Keyboard"),
        };

        return
        [
            new(T("picker.keyboard"),
                [new(T("picker.letters_digits"), comm), new(T("picker.function"), adv), new("Keypad", keypad), new("Modifier", modify), new("Layer", layer)]),
            new(T("picker.mouse"), [new(T("picker.mouse_2"), mouse)],
                T("picker.only_left_button_has_capture")),
            new("🎵 Media", [new(T("picker.playback_volume"), media), new(T("picker.apps_screen"), mediaApps)],
                T("picker.only_mute_volume_have_captures")),
            new("Macro", [], T("picker.macro_partially_decoded_capture_16")),
            new(T("picker.commands"), [new(T("picker.system_commands"), commands)],
                T("picker.only_lock_pc_win_l")),
        ];
    }
}
