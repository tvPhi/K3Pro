using System.Globalization;

namespace K3Pro.Cli;

/// <summary>Minimal parser: command + positional arguments + flags (--send, --vendor) + options with a value (--page N).</summary>
internal sealed class CliArgs
{
    private static readonly HashSet<string> ValueOptions = ["--page"];

    private readonly List<string> _positional = [];
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _options = new(StringComparer.OrdinalIgnoreCase);

    public CliArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--")) _positional.Add(a);
            else if (ValueOptions.Contains(a))
                _options[a] = i + 1 < args.Length ? args[++i] : throw new UsageException($"{a} cần giá trị.");
            else _flags.Add(a);
        }
    }

    public string? Command => _positional.FirstOrDefault()?.ToLowerInvariant();

    public int PositionalCount => _positional.Count - 1;

    public string Arg(int i, string name) =>
        i + 1 < _positional.Count ? _positional[i + 1] : throw new UsageException($"Thiếu tham số <{name}>.");

    public bool Has(string flag) => _flags.Contains(flag);

    public string? Option(string name) => _options.GetValueOrDefault(name);

    public void EnsureOnly(params string[] allowedFlags)
    {
        var unknown = _flags.Where(f => !allowedFlags.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
        if (unknown.Count > 0) throw new UsageException($"Cờ không hỗ trợ: {string.Join(", ", unknown)}");
    }

    /// <summary>0x prefix → hex, otherwise decimal.</summary>
    public static int ParseNumber(string s, string name)
    {
        var ok = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            : int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
        return ok ? v : throw new UsageException($"<{name}> không hợp lệ: '{s}'.");
    }

    /// <summary>Always hex, with or without 0x.</summary>
    public static int ParseHex(string s, string name)
    {
        var body = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s;
        return int.TryParse(body, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? v
            : throw new UsageException($"<{name}> phải là hex: '{s}'.");
    }
}

internal sealed class UsageException(string message) : Exception(message);
