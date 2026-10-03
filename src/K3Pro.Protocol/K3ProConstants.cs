namespace K3Pro.Protocol;

public static class K3ProConstants
{
    /// <summary>Wired VID:PID (Sinowealth): feature report 0x06. The 2.4G receiver uses its own framing (see <see cref="Wireless.ReceiverFrame"/>).</summary>
    public const int VendorId = 0x258A;
    public const int ProductId = 0x010C;

    /// <summary>
    /// 2.4G receiver (KB.ini VID_Wireless / PID_Wireless): output report 0x13 frames. Only frames seen in the 2.4G captures may be sent
    /// (<see cref="Wireless.ReceiverGuard"/>).
    /// </summary>
    public const int WirelessVendorId = 0x3554;
    public const int WirelessProductId = 0xFA09;

    /// <summary>Every command goes through feature report 0x06.</summary>
    public const byte ReportId = 0x06;

    /// <summary>Feature report length on the wire, including the report ID byte.</summary>
    public const int ReportLength = 520;

    public const int HeaderLength = 8;
    public const int MaxDataLength = ReportLength - HeaderLength;

    /// <summary>The vendor app sends packets ~30–60 ms apart.</summary>
    public static readonly TimeSpan DefaultInterPacketDelay = TimeSpan.FromMilliseconds(50);
}
