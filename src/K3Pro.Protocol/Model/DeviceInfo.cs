namespace K3Pro.Protocol;

/// <summary>Response to command 0x82. ❓ Meaning of each byte unknown (possibly version / ID).</summary>
public sealed class DeviceInfo
{
    public const int Length = 6;

    /// <summary>Value seen in all 5 captures.</summary>
    public static ReadOnlySpan<byte> CaptureValue => [0x03, 0x00, 0x00, 0x00, 0x00, 0x17];

    private readonly byte[] _raw;

    private DeviceInfo(byte[] raw) => _raw = raw;

    public ReadOnlySpan<byte> Raw => _raw;

    public bool MatchesCapture => Raw.SequenceEqual(CaptureValue);

    public static DeviceInfo Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length != Length)
            throw new InvalidDataException(Lang.T($"DeviceInfo phải dài {Length} byte (nhận {data.Length}).",
                $"DeviceInfo must be {Length} bytes (got {data.Length})."));
        return new(data.ToArray());
    }

    public override string ToString() => Hex.Format(_raw);
}
