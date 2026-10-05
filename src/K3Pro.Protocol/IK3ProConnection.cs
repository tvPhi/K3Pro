using K3Pro.Protocol.Wireless;

namespace K3Pro.Protocol;

public enum ConnectionKind { Wired, Wireless }

/// <summary>A numpad connection: wired (<see cref="K3ProDevice"/>) or via the 2.4G receiver (<see cref="K3ProReceiverDevice"/>).</summary>
public interface IK3ProConnection : IDisposable
{
    ConnectionKind Kind { get; }

    string Description { get; }

    DeviceInfo ReadDeviceInfo();

    /// <summary>Raw 128-byte settings block (magic not checked).</summary>
    byte[] ReadSettingsRaw();

    /// <summary>Reads settings; throws if the 5A A5 magic is not intact.</summary>
    Settings ReadSettings();

    /// <summary>Executes a write plan; for 0x04 always reads back before (must match the base) and after (must match the packet).</summary>
    void Execute(WritePlan plan, Action<PlannedWrite>? onSent = null);
}

/// <summary>On-the-wire packets for each connection kind — shared by the CLI dry-run and real sends.</summary>
public static class WireEncoding
{
    /// <summary>
    /// Wired write command → receiver command seen in the 2.4G captures: settings 04 → 04, keymap 03 pages 0–2 → 01 with the page
    /// in the high nibble of byte 4 (capture 22/23, 14/15), color table 0A → 09 (capture 24/25). Keymap page 3 (Tap) over 2.4G:
    /// not captured yet.
    /// </summary>
    public static ReceiverCommand? ReceiverCommandFor(PlannedWrite write) => write.Command switch
    {
        Command.WriteSettings => ReceiverCommand.WriteSettings,
        Command.WriteKeymap when write.Header.Page <= ReceiverFrame.MaxWirelessKeymapPage => ReceiverCommand.WriteKeymap,
        Command.WriteColorTable => ReceiverCommand.WriteColorTable,
        _ => null,
    };

    public static bool Supports(ConnectionKind kind, PlannedWrite write) =>
        kind == ConnectionKind.Wired || ReceiverCommandFor(write) is not null;

    public static string? WhyUnsupported(ConnectionKind kind, WritePlan plan) =>
        plan.Writes.FirstOrDefault(w => !Supports(kind, w)) is { } w
            ? Lang.T("connection.command_0x_page_has_no", w.Title, (byte)w.Command, w.Header.Page)
            : null;

    /// <summary>Packets to send for a <see cref="PlannedWrite"/>: wired = one 520-byte packet; 2.4G = 20-byte frames.</summary>
    public static IReadOnlyList<byte[]> Encode(ConnectionKind kind, PlannedWrite write)
    {
        if (kind == ConnectionKind.Wired) return [write.Packet];
        if (ReceiverCommandFor(write) is not { } cmd)
            throw new UnsafeCommandException(WhyUnsupported(kind, new WritePlan(write.Title, [write], []))!);
        return ReceiverFrame.Chunked(cmd, write.Packet.AsSpan(K3ProConstants.HeaderLength, ReceiverFrame.DataLength(cmd)), write.Header.Page);
    }
}
