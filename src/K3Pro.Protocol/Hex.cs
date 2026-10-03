using System.Text;

namespace K3Pro.Protocol;

public static class Hex
{
    public static string Format(ReadOnlySpan<byte> data, string separator = " ")
    {
        var sb = new StringBuilder(data.Length * (2 + separator.Length));
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0) sb.Append(separator);
            sb.Append(data[i].ToString("X2"));
        }
        return sb.ToString();
    }

    /// <summary>Full hexdump, 16 bytes per line, duplicate lines not collapsed.</summary>
    public static string Dump(ReadOnlySpan<byte> data, string indent = "  ")
    {
        var sb = new StringBuilder();
        for (int off = 0; off < data.Length; off += 16)
        {
            var chunk = data.Slice(off, Math.Min(16, data.Length - off));
            sb.Append(indent).Append(off.ToString("X4")).Append("  ").AppendLine(Format(chunk));
        }
        return sb.ToString().TrimEnd();
    }

    public static byte[] Parse(string hex) =>
        Convert.FromHexString(hex.Replace(" ", "").Replace("-", "").Replace(":", "").Replace("\r", "").Replace("\n", ""));

    /// <summary>Differing byte ranges [start, end) between two buffers of equal length.</summary>
    public static IReadOnlyList<(int Start, int End)> DiffRanges(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length) throw new ArgumentException(Lang.T("Hai buffer phải cùng độ dài.", "Both buffers must have the same length."));
        var ranges = new List<(int, int)>();
        int i = 0;
        while (i < a.Length)
        {
            if (a[i] == b[i]) { i++; continue; }
            int start = i;
            while (i < a.Length && a[i] != b[i]) i++;
            ranges.Add((start, i));
        }
        return ranges;
    }
}
