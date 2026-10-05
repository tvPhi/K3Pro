using System.Globalization;

namespace K3Pro.Protocol;

/// <summary>RGB color, on-the-wire byte order R, G, B ✅.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Parse(string s)
    {
        var hex = s.StartsWith('#') ? s[1..] : s;
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
            throw new FormatException(Lang.T("colortable.color_must_be_rrggbb_got", s));
        return new((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public override string ToString() => $"{R:X2}{G:X2}{B:X2}";
}

/// <summary>
/// Color table (command 0x0A): 133 RGB colors = 399 bytes = 0x18F. ❓ Possibly 19 modes × 7 colors.
/// Slots 0–6 are all 000000; slot 7 = static color ✅.
/// </summary>
public sealed class ColorTable
{
    public const int ColorCount = 133;
    public const int ByteLength = ColorCount * 3;
    public const int StaticColorIndex = 7;

    private readonly Rgb[] _colors;

    private ColorTable(Rgb[] colors) => _colors = colors;

    public Rgb this[int index] => _colors[index];

    public IReadOnlyList<Rgb> Colors => _colors;

    public Rgb StaticColor => _colors[StaticColorIndex];

    public static ColorTable Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length != ByteLength)
            throw new InvalidDataException(Lang.T("colortable.color_table_must_be_bytes", ByteLength, data.Length));
        var colors = new Rgb[ColorCount];
        for (int i = 0; i < ColorCount; i++)
            colors[i] = new(data[i * 3], data[i * 3 + 1], data[i * 3 + 2]);
        return new(colors);
    }

    public ColorTable With(int index, Rgb color)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, ColorCount);
        var copy = (Rgb[])_colors.Clone();
        copy[index] = color;
        return new(copy);
    }

    public ColorTable WithStaticColor(Rgb color) => With(StaticColorIndex, color);

    public byte[] ToBytes()
    {
        var data = new byte[ByteLength];
        for (int i = 0; i < ColorCount; i++)
        {
            data[i * 3] = _colors[i].R;
            data[i * 3 + 1] = _colors[i].G;
            data[i * 3 + 2] = _colors[i].B;
        }
        return data;
    }
}
