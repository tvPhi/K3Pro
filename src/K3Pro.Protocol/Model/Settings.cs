namespace K3Pro.Protocol;

/// <summary>
/// 128-byte settings block (read 0x84 / write 0x04). Immutable: every change returns a copy, modifying only the exact byte needed.
/// Apart from <see cref="LightingMode"/> and the magic, every other byte is ❓ — keep the value read from the device unchanged.
/// </summary>
public sealed class Settings
{
    public const int Length = 128;

    /// <summary>✅ Lighting mode. 0x01 = static.</summary>
    public const int LightingModeOffset = 0x0A;

    /// <summary>
    /// ❓ 01 while in Self-define, 00 after leaving it (capture 57 / 58). Never changed here — if it is 01, effect changes are refused.
    /// </summary>
    public const int SelfDefineFlagOffset = 0x09;

    /// <summary>✅ Per-mode parameters from 0x38: <c>[0x38 + 2·mode]</c> = brightness 0..4, <c>[0x39 + 2·mode]</c> high nibble = speed 0..4,
    /// low nibble ❓ kept unchanged (capture 61–64: Respire 0x3C / 0x3D; capture 42: 0x4E = brightness of mode 0B).</summary>
    public const int EffectParamsOffset = 0x38;

    public static int BrightnessOffset(byte mode) => EffectParamsOffset + 2 * mode;

    public static int SpeedOffset(byte mode) => EffectParamsOffset + 2 * mode + 1;

    /// <summary>✅ Sleep time in 2.4G mode, unit 30 s (capture 31/32/33/34: 01 = 30 s, 0A = 5 Min, 28 = 20 Min).</summary>
    public const int SleepOffset = 0x18;

    public const int MagicOffset = Length - 2;
    public static ReadOnlySpan<byte> Magic => [0x5A, 0xA5];

    // Device brightness can be > 4 (07, 09 — set by the knob?) ❓: display only; the app writes only 0..4, like the vendor app.

    private readonly byte[] _data;

    private Settings(byte[] data) => _data = data;

    public ReadOnlySpan<byte> Data => _data;

    public byte LightingMode => _data[LightingModeOffset];

    public byte SleepUnits => _data[SleepOffset];

    public byte SelfDefineFlag => _data[SelfDefineFlagOffset];

    public byte Brightness(byte mode) => _data[BrightnessOffset(CheckMode(mode))];

    /// <summary>High nibble of the speed byte.</summary>
    public byte Speed(byte mode) => (byte)(_data[SpeedOffset(CheckMode(mode))] >> 4);

    public TimeSpan SleepTime => SleepTimes.ToTimeSpan(SleepUnits);

    public static Settings Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length != Length)
            throw new InvalidDataException(Lang.T($"Settings phải dài {Length} byte (nhận {data.Length}).",
                $"Settings must be {Length} bytes (got {data.Length})."));
        if (!data[MagicOffset..].SequenceEqual(Magic))
            throw new InvalidDataException(Lang.T($"Settings sai magic: {Hex.Format(data[MagicOffset..])} (cần 5A A5).",
                $"Settings has wrong magic: {Hex.Format(data[MagicOffset..])} (expected 5A A5)."));
        return new(data.ToArray());
    }

    public Settings WithLightingMode(byte mode) => WithByte(LightingModeOffset, mode);

    public Settings WithSleepUnits(byte units) => WithByte(SleepOffset, units);

    public Settings WithBrightness(byte mode, byte level) => WithByte(BrightnessOffset(CheckMode(mode)), level);

    /// <summary>Changes the high nibble (speed), keeps the low nibble ❓ unchanged.</summary>
    public Settings WithSpeed(byte mode, byte level) =>
        WithByte(SpeedOffset(CheckMode(mode)), (byte)((level << 4) | (_data[SpeedOffset(mode)] & 0x0F)));

    private static byte CheckMode(byte mode) => SpeedOffset(mode) < MagicOffset
        ? mode
        : throw new ArgumentOutOfRangeException(nameof(mode), Lang.T($"Mode 0x{mode:X2} ngoài vùng tham số.", $"Mode 0x{mode:X2} is outside the parameter area."));

    private Settings WithByte(int offset, byte value)
    {
        var copy = (byte[])_data.Clone();
        copy[offset] = value;
        return Parse(copy);
    }

    public override string ToString() => Hex.Format(_data);
}

/// <summary>
/// Sleep (offset 0x18): unit 30 s. Only the vendor app's exact 10 slider steps are allowed (read from its UI by the user, 2026-10-02):
/// 30 s, 1, 1.5, 2, 3, 4, 5, 10, 15, 20 Min. Captures confirm 3 steps: 01, 0A, 28.
/// ❓ The vendor app never sends values off the steps / &gt; 40; 0 may be OFF, but the OFF switch is not captured yet → all forbidden.
/// </summary>
public static class SleepTimes
{
    public static readonly TimeSpan Unit = TimeSpan.FromSeconds(30);

    /// <summary>The vendor app's steps, in 30 s units.</summary>
    public static readonly IReadOnlyList<byte> VendorStops = [1, 2, 3, 4, 6, 8, 10, 20, 30, 40];

    public const byte MinUnits = 1;
    public const byte MaxUnits = 40;

    public static bool IsAllowed(int units) => units is >= 0 and <= 0xFF && VendorStops.Contains((byte)units);

    /// <summary>Nearest step (to show an unusual value read from the device on the slider).</summary>
    public static int NearestStopIndex(int units) =>
        Enumerable.Range(0, VendorStops.Count).MinBy(i => Math.Abs(VendorStops[i] - units));

    public static TimeSpan ToTimeSpan(byte units) => Unit * units;

    public static string StopsText => string.Join(", ", VendorStops.Select(Describe));

    public static string Describe(byte units) => units switch
    {
        0 => Lang.T("00 (❓ có thể OFF)", "00 (❓ possibly OFF)"),
        1 => "30 s",
        _ when units % 2 == 1 => $"{units / 2} Min 30 s",
        _ => $"{units / 2} Min",
    };

    /// <summary>"30s", "90s", "5m", "5min", "1.5m", "20" (min) → 30 s units. Throws if not a multiple of 30 s or out of range.</summary>
    public static byte Parse(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        double seconds = t.EndsWith("min") ? double.Parse(t[..^3], ci) * 60
            : t.EndsWith('m') ? double.Parse(t[..^1], ci) * 60
            : t.EndsWith('s') ? double.Parse(t[..^1], ci)
            : double.Parse(t, ci) * 60;
        var units = seconds / Unit.TotalSeconds;
        if (units != Math.Floor(units))
            throw new FormatException(Lang.T($"Sleep phải là bội số của 30 s (nhận '{text}').", $"Sleep must be a multiple of 30 s (got '{text}')."));
        if (!IsAllowed((int)units))
            throw new UnsafeCommandException(Lang.T($"Sleep '{text}' không phải nấc của app hãng ({StopsText}).",
                $"Sleep '{text}' is not one of the vendor app's steps ({StopsText})."));
        return (byte)units;
    }
}

/// <summary>
/// An effect in the vendor app's Light effect list (names kept exactly as in the vendor app).
/// <see cref="HasBrightness"/> / <see cref="HasSpeed"/> follow the <c>light</c> / <c>speed</c> flags of KB.ini <c>LedOptN</c>.
/// </summary>
public sealed record LightingEffect(string Name, byte? Mode, string? Source = null, bool HasBrightness = false, bool HasSpeed = false,
    bool IsWritable = true)
{
    /// <summary>Has a mode value from a capture AND is writable with 0x04 alone (Self-define also needs command 02 → no).</summary>
    public bool IsCaptured => Mode is not null && IsWritable;
}

/// <summary>Lighting mode (offset 0x0A) — capture batch 6 (41–58, 2.4G, 2026-10-02): the vendor app changes only this byte.</summary>
public static class LightingModes
{
    public const byte Off = 0x00;

    /// <summary>✅ Fixed_on (static).</summary>
    public const byte Static = 0x01;

    public const byte Respire = 0x02;

    /// <summary>✅ Rainbow (capture 43). Also the mode the device was in before capture 04.</summary>
    public const byte Rainbow = 0x03;

    /// <summary>
    /// Self-define: the vendor app writes 0x0A = 15 + 0x09 = 01 + receiver command 02 (per-key colors) — capture 57. Not supported.
    /// </summary>
    public const byte SelfDefine = 0x15;

    /// <summary>✅ Brightness / speed: 0..4 (KB.ini <c>LightHW</c> / <c>SpeedHW</c> = 0..4; capture 61–64 confirm 00 and 04).</summary>
    public const byte MaxLevel = 4;

    /// <summary>
    /// The vendor app's Light effect list, in UI order, with the capture confirming each 0x0A value.
    /// 0x0E and 0x12 exist in KB.ini (LedOpt14 / 18) but are hidden from the UI by <c>LedMask=0x22000</c> → not used.
    /// </summary>
    public static IReadOnlyList<LightingEffect> Effects { get; } =
    [
        new("Fixed_on", Static, "41 / 04", HasBrightness: true),
        new("Respire", Respire, "42, 61–64", true, true),
        new("Rainbow", Rainbow, "43", true, true),
        new("Flash_away", 0x04, "44", true, true),
        new("Raindrops", 0x05, "45", true, true),
        new("Rainbow_wheel", 0x06, "46", true, true),
        new("Ripples_shining", 0x07, "47", true, true),
        new("Stars_twinkle", 0x08, "48", true, true),
        new("Shadow_disappear", 0x09, "49", true, true),
        new("Retro_snake", 0x0A, "50", true, true),
        new("Neon_stream", 0x0B, "51", true, true),
        new("Reaction", 0x0C, "52", true, true),
        new("Sine_wave", 0x0D, "53", true, true),
        new("Rotating windmill", 0x0F, "54", true, true),
        new("Colorful waterfall", 0x10, "55", true, true),
        new("Blossoming", 0x11, "56", true, true),
        new("Self-define", SelfDefine, "57", IsWritable: false),
        new("OFF", Off, "58"),
    ];

    /// <summary>Modes allowed to be written (each has a capture of the vendor app writing exactly this value).</summary>
    public static IReadOnlyList<byte> Observed { get; } = Effects.Where(e => e.IsCaptured).Select(e => e.Mode!.Value).ToList();

    public static LightingEffect? Find(byte mode) => Effects.FirstOrDefault(e => e.Mode == mode);

    public static string Describe(byte mode) => mode switch
    {
        Static => "static",
        _ when Find(mode) is { } e => e.Name,
        _ => Lang.T("❓ chưa thấy trong capture", "❓ not seen in any capture"),
    };
}
