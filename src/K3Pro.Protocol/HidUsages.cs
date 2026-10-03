namespace K3Pro.Protocol;

public sealed record HidUsage(byte Code, string Name)
{
    public override string ToString() => $"{Name} (0x{Code:X2})";
}

/// <summary>HID Keyboard/Keypad usage names (page 0x07), per the HID Usage Tables. Standard data only, not the K3 Pro protocol.</summary>
public static class HidUsages
{
    public static readonly IReadOnlyList<HidUsage> Keyboard = Build();

    private static readonly Dictionary<byte, string> ByCode = Keyboard.ToDictionary(u => u.Code, u => u.Name);

    public static string Name(byte code) => ByCode.TryGetValue(code, out var n) ? n : $"0x{code:X2}";

    private static List<HidUsage> Build()
    {
        var list = new List<HidUsage>();
        void Add(int code, string name) => list.Add(new((byte)code, name));

        for (int i = 0; i < 26; i++) Add(0x04 + i, ((char)('A' + i)).ToString());
        for (int i = 0; i < 9; i++) Add(0x1E + i, (i + 1).ToString());
        Add(0x27, "0");
        string[] block1 =
        [
            "Enter", "Esc", "Backspace", "Tab", "Space", "-", "=", "[", "]", "\\", "Non-US #", ";", "'", "`", ",", ".", "/", "Caps Lock",
        ];
        for (int i = 0; i < block1.Length; i++) Add(0x28 + i, block1[i]);
        for (int i = 0; i < 12; i++) Add(0x3A + i, $"F{i + 1}");
        string[] block2 =
        [
            "Print Screen", "Scroll Lock", "Pause", "Insert", "Home", "Page Up", "Delete", "End", "Page Down",
            "Right", "Left", "Down", "Up", "Num Lock", "Num /", "Num *", "Num -", "Num +", "Num Enter",
            "Num 1", "Num 2", "Num 3", "Num 4", "Num 5", "Num 6", "Num 7", "Num 8", "Num 9", "Num 0", "Num .",
            "Non-US \\", "Application", "Power", "Num =",
        ];
        for (int i = 0; i < block2.Length; i++) Add(0x46 + i, block2[i]);
        for (int i = 0; i < 12; i++) Add(0x68 + i, $"F{i + 13}");
        string[] block3 =
        [
            "Execute", "Help", "Menu", "Select", "Stop", "Again", "Undo", "Cut", "Copy", "Paste", "Find",
            "Mute", "Volume Up", "Volume Down",
        ];
        for (int i = 0; i < block3.Length; i++) Add(0x74 + i, block3[i]);
        string[] modifiers = ["Left Ctrl", "Left Shift", "Left Alt", "Left GUI", "Right Ctrl", "Right Shift", "Right Alt", "Right GUI"];
        for (int i = 0; i < modifiers.Length; i++) Add(0xE0 + i, modifiers[i]);
        return list;
    }
}
