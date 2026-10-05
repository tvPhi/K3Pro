namespace K3Pro.Protocol;

/// <summary>4-byte keymap entry:<c>[type][p1][p2][code]</c>.</summary>
public readonly record struct KeymapEntry(byte Type, byte P1, byte P2, byte Code)
{
    public const int Size = 4;

    /// <summary>✅ type 00 + code = HID keyboard usage.</summary>
    public const byte TypeHidUsage = 0x00;

    // ❓ Other types seen in captures, not yet decoded: 0x02 (+E2 = consumer Mute?), 0x07 / 0x08 (special functions),
    //    0x0D (Fn key?). p1, p2 meaning unknown — always 00 with type 00.

    public static KeymapEntry HidUsage(byte usage) => new(TypeHidUsage, 0x00, 0x00, usage);

    public bool IsEmpty => Type == 0 && P1 == 0 && P2 == 0 && Code == 0;

    public bool IsHidUsage => Type == TypeHidUsage && P1 == 0 && P2 == 0 && Code != 0;

    public static KeymapEntry Read(ReadOnlySpan<byte> b) => new(b[0], b[1], b[2], b[3]);

    public void WriteTo(Span<byte> b)
    {
        b[0] = Type; b[1] = P1; b[2] = P2; b[3] = Code;
    }

    public override string ToString() => $"{Type:X2} {P1:X2} {P2:X2} {Code:X2}";
}

/// <summary>Keymap page (command 0x03): 126 entries × 4 bytes = 0x1F8. Page 0 = base layer; page 1 = Fn layer ❓; pages 2–3 empty.</summary>
public sealed class KeymapPage
{
    public const int EntryCount = 126;
    public const int ByteLength = EntryCount * KeymapEntry.Size;
    public const byte MaxPage = 3;

    /// <summary>Number of indices seen in the matrix (PROTOCOL.md): 0–23.</summary>
    public const int MatrixSize = 24;

    private readonly KeymapEntry[] _entries;

    private KeymapPage(byte page, KeymapEntry[] entries)
    {
        Page = page;
        _entries = entries;
    }

    public byte Page { get; }

    public KeymapEntry this[int index] => _entries[index];

    public IReadOnlyList<KeymapEntry> Entries => _entries;

    public static KeymapPage Parse(byte page, ReadOnlySpan<byte> data)
    {
        if (page > MaxPage) throw new ArgumentOutOfRangeException(nameof(page), page, $"Page 0–{MaxPage}.");
        if (data.Length != ByteLength)
            throw new InvalidDataException(Lang.T("keymapmodel.keymap_page_must_be_bytes", ByteLength, data.Length));
        var entries = new KeymapEntry[EntryCount];
        for (int i = 0; i < EntryCount; i++)
            entries[i] = KeymapEntry.Read(data[(i * KeymapEntry.Size)..]);
        return new(page, entries);
    }

    public KeymapPage With(int index, KeymapEntry entry)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, EntryCount);
        var copy = (KeymapEntry[])_entries.Clone();
        copy[index] = entry;
        return new(Page, copy);
    }

    public byte[] ToBytes()
    {
        var data = new byte[ByteLength];
        for (int i = 0; i < EntryCount; i++)
            _entries[i].WriteTo(data.AsSpan(i * KeymapEntry.Size));
        return data;
    }
}
