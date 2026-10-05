using System.Buffers.Binary;

namespace K3Pro.Protocol;

/// <summary>8-byte header:<c>[06][cmd][page][00][01 00][len u16 LE]</c>.</summary>
public readonly record struct PacketHeader(byte ReportId, byte Command, byte Page, byte Unknown3, ushort Unknown45, ushort DataLength)
{
    /// <summary>❓ Byte 3: always 0x00 in the captures, meaning unknown.</summary>
    public const byte Unknown3Value = 0x00;

    /// <summary>❓ Bytes 4–5: always 0x0001 (LE) in the captures, meaning unknown.</summary>
    public const ushort Unknown45Value = 0x0001;

    public static PacketHeader Parse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length < K3ProConstants.HeaderLength)
            throw new InvalidDataException(Lang.T("header.packet_shorter_than_header_bytes", packet.Length));
        return new(packet[0], packet[1], packet[2], packet[3],
            BinaryPrimitives.ReadUInt16LittleEndian(packet[4..]),
            BinaryPrimitives.ReadUInt16LittleEndian(packet[6..]));
    }

    public void WriteTo(Span<byte> packet)
    {
        packet[0] = ReportId;
        packet[1] = Command;
        packet[2] = Page;
        packet[3] = Unknown3;
        BinaryPrimitives.WriteUInt16LittleEndian(packet[4..], Unknown45);
        BinaryPrimitives.WriteUInt16LittleEndian(packet[6..], DataLength);
    }

    public override string ToString() => $"cmd 0x{Command:X2} page 0x{Page:X2} len 0x{DataLength:X4}";
}
