using Avalonia.Input;

namespace K3Pro.App.Services;

/// <summary>Avalonia <see cref="PhysicalKey"/> (key position, per the W3C "code") → HID keyboard usage.</summary>
public static class PhysicalKeyMap
{
    internal static readonly IReadOnlyDictionary<string, byte> ByName = Build();

    public static byte? ToHidUsage(PhysicalKey key) =>
        Enum.GetName(key) is { } name && ByName.TryGetValue(name, out var code) ? code : null;

    private static Dictionary<string, byte> Build()
    {
        var map = new Dictionary<string, byte>();
        void Add(string name, int code) => map[name] = (byte)code;

        for (int i = 0; i < 26; i++) Add(((char)('A' + i)).ToString(), 0x04 + i);
        for (int i = 1; i <= 9; i++) Add($"Digit{i}", 0x1D + i);
        Add("Digit0", 0x27);
        string[] block1 =
        [
            "Enter", "Escape", "Backspace", "Tab", "Space", "Minus", "Equal", "BracketLeft", "BracketRight", "Backslash",
            "", "Semicolon", "Quote", "Backquote", "Comma", "Period", "Slash", "CapsLock",
        ];
        for (int i = 0; i < block1.Length; i++)
            if (block1[i].Length > 0) Add(block1[i], 0x28 + i);
        for (int i = 1; i <= 12; i++) Add($"F{i}", 0x39 + i);
        for (int i = 13; i <= 24; i++) Add($"F{i}", 0x68 + i - 13);
        string[] block2 =
        [
            "PrintScreen", "ScrollLock", "Pause", "Insert", "Home", "PageUp", "Delete", "End", "PageDown",
            "ArrowRight", "ArrowLeft", "ArrowDown", "ArrowUp", "NumLock", "NumPadDivide", "NumPadMultiply",
            "NumPadSubtract", "NumPadAdd", "NumPadEnter",
        ];
        for (int i = 0; i < block2.Length; i++) Add(block2[i], 0x46 + i);
        for (int i = 1; i <= 9; i++) Add($"NumPad{i}", 0x58 + i);
        Add("NumPad0", 0x62);
        Add("NumPadDecimal", 0x63);
        Add("IntlBackslash", 0x64);
        Add("ContextMenu", 0x65);
        Add("NumPadEqual", 0x67);
        string[] modifiers = ["ControlLeft", "ShiftLeft", "AltLeft", "MetaLeft", "ControlRight", "ShiftRight", "AltRight", "MetaRight"];
        for (int i = 0; i < modifiers.Length; i++) Add(modifiers[i], 0xE0 + i);
        return map;
    }
}
