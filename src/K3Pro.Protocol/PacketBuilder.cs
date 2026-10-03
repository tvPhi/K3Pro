namespace K3Pro.Protocol;

/// <summary>
/// Builds 520-byte packets (header + data + zero padding). Only has methods for commands seen in a capture;
/// every packet goes through <see cref="CommandGuard"/> before it is returned.
/// </summary>
public static class PacketBuilder
{
    public static byte[] ReadDeviceInfo() => Build(Command.ReadDeviceInfo, page: 0x01, DeviceInfo.Length, []);

    public static byte[] ReadSettings() => Build(Command.ReadSettings, page: 0x00, Settings.Length, []);

    public static byte[] WriteSettings(Settings settings) =>
        Build(Command.WriteSettings, page: 0x00, Settings.Length, settings.Data);

    public static byte[] WriteKeymap(KeymapPage page) =>
        Build(Command.WriteKeymap, page.Page, KeymapPage.ByteLength, page.ToBytes());

    public static byte[] WriteColorTable(ColorTable table) =>
        Build(Command.WriteColorTable, page: 0x00, ColorTable.ByteLength, table.ToBytes());

    private static byte[] Build(Command command, byte page, int dataLength, ReadOnlySpan<byte> data)
    {
        var spec = CommandSpecs.All[command];
        if (data.Length > dataLength)
            throw new ArgumentException($"Data {data.Length} byte > len {dataLength}.", nameof(data));

        var packet = new byte[K3ProConstants.ReportLength];
        new PacketHeader(K3ProConstants.ReportId, (byte)command, page,
            PacketHeader.Unknown3Value, PacketHeader.Unknown45Value, (ushort)dataLength).WriteTo(packet);
        data.CopyTo(packet.AsSpan(K3ProConstants.HeaderLength));

        CommandGuard.EnsureAllowed(packet, spec.IsRead);
        return packet;
    }
}
