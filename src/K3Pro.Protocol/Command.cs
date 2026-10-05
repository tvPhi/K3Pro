namespace K3Pro.Protocol;

/// <summary>
/// Only command codes SEEN in a capture. Do not add any other code (not even 0x83 "read keymap", inferred by symmetry)
/// until a capture confirms it — see the SAFETY RULES in CLAUDE.md.
/// </summary>
public enum Command : byte
{
    WriteKeymap = 0x03,
    WriteSettings = 0x04,
    WriteColorTable = 0x0A,
    ReadDeviceInfo = 0x82,
    ReadSettings = 0x84,
}

/// <summary>Header shape of each command, exactly as in the capture.</summary>
public sealed record CommandSpec(Command Command, IReadOnlyList<byte> Pages, ushort DataLength, bool IsRead, string Source);

public static class CommandSpecs
{
    public static readonly IReadOnlyDictionary<Command, CommandSpec> All = new Dictionary<Command, CommandSpec>
    {
        // 0x82 uses page 01 (not 00) — kept unchanged as in the capture.
        [Command.ReadDeviceInfo] = new(Command.ReadDeviceInfo, [0x01], DeviceInfo.Length, IsRead: true, "01..05 (vendor app startup)"),
        [Command.ReadSettings] = new(Command.ReadSettings, [0x00], Settings.Length, IsRead: true, "04-rgb-red, 05-rgb-blue"),
        [Command.WriteSettings] = new(Command.WriteSettings, [0x00], Settings.Length, IsRead: false, "04-rgb-red, 05-rgb-blue"),
        [Command.WriteKeymap] = new(Command.WriteKeymap, [0x00, 0x01, 0x02, 0x03], KeymapPage.ByteLength, IsRead: false, "02-num1-to-A, 03-num1-to-B"),
        [Command.WriteColorTable] = new(Command.WriteColorTable, [0x00], ColorTable.ByteLength, IsRead: false, "04-rgb-red, 05-rgb-blue"),
    };
}

/// <summary>Last line of defense before any packet reaches the device.</summary>
public static class CommandGuard
{
    /// <summary>Throws <see cref="UnsafeCommandException"/> unless the packet exactly matches a command seen in a capture.</summary>
    public static CommandSpec EnsureAllowed(ReadOnlySpan<byte> packet, bool expectRead)
    {
        if (packet.Length != K3ProConstants.ReportLength)
            throw new UnsafeCommandException(Lang.T("command.packet_must_be_bytes_got", K3ProConstants.ReportLength, packet.Length));

        var h = PacketHeader.Parse(packet);
        if (h.ReportId != K3ProConstants.ReportId)
            throw new UnsafeCommandException(Lang.T("command.report_id_0x_not_allowed", h.ReportId));
        if (!Enum.IsDefined((Command)h.Command) || !CommandSpecs.All.TryGetValue((Command)h.Command, out var spec))
            throw new UnsafeCommandException(Lang.T("command.command_0x_not_seen_any", h.Command));
        if (spec.IsRead != expectRead)
            throw new UnsafeCommandException(Lang.T("command.command_0x_command_called_through", h.Command, Lang.T(spec.IsRead ? "command.kind_read" : "command.kind_write")));
        if (!spec.Pages.Contains(h.Page))
            throw new UnsafeCommandException(Lang.T("command.command_0x_page_0x_not", h.Command, h.Page));
        if (h.DataLength != spec.DataLength)
            throw new UnsafeCommandException(Lang.T("command.command_0x_len_0x_differs", h.Command, h.DataLength, spec.DataLength));
        if (h.Unknown3 != PacketHeader.Unknown3Value || h.Unknown45 != PacketHeader.Unknown45Value)
            throw new UnsafeCommandException(Lang.T("command.header_bytes_3_5_differ"));

        var tail = packet[(K3ProConstants.HeaderLength + spec.DataLength)..];
        if (tail.ContainsAnyExcept((byte)0))
            throw new UnsafeCommandException(Lang.T("command.padding_after_data_must_be"));
        if (spec.IsRead && packet[K3ProConstants.HeaderLength..].ContainsAnyExcept((byte)0))
            throw new UnsafeCommandException(Lang.T("command.read_packet_data_must_be"));

        return spec;
    }
}

public sealed class UnsafeCommandException(string message) : InvalidOperationException(message);
