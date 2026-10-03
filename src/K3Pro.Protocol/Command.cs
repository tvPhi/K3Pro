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
            throw new UnsafeCommandException(Lang.T($"Gói phải dài {K3ProConstants.ReportLength} byte (nhận {packet.Length}).",
                $"Packet must be {K3ProConstants.ReportLength} bytes (got {packet.Length})."));

        var h = PacketHeader.Parse(packet);
        if (h.ReportId != K3ProConstants.ReportId)
            throw new UnsafeCommandException(Lang.T($"Report ID 0x{h.ReportId:X2} không được phép (chỉ 0x06).",
                $"Report ID 0x{h.ReportId:X2} not allowed (0x06 only)."));
        if (!Enum.IsDefined((Command)h.Command) || !CommandSpecs.All.TryGetValue((Command)h.Command, out var spec))
            throw new UnsafeCommandException(Lang.T($"Lệnh 0x{h.Command:X2} chưa thấy trong capture — CẤM gửi.",
                $"Command 0x{h.Command:X2} not seen in any capture — sending is FORBIDDEN."));
        if (spec.IsRead != expectRead)
            throw new UnsafeCommandException(Lang.T($"Lệnh 0x{h.Command:X2} là lệnh {(spec.IsRead ? "đọc" : "ghi")}, gọi sai đường.",
                $"Command 0x{h.Command:X2} is a {(spec.IsRead ? "read" : "write")} command, called through the wrong path."));
        if (!spec.Pages.Contains(h.Page))
            throw new UnsafeCommandException(Lang.T($"Lệnh 0x{h.Command:X2} page 0x{h.Page:X2} chưa thấy trong capture.",
                $"Command 0x{h.Command:X2} page 0x{h.Page:X2} not seen in any capture."));
        if (h.DataLength != spec.DataLength)
            throw new UnsafeCommandException(Lang.T($"Lệnh 0x{h.Command:X2} len 0x{h.DataLength:X} khác capture (0x{spec.DataLength:X}).",
                $"Command 0x{h.Command:X2} len 0x{h.DataLength:X} differs from capture (0x{spec.DataLength:X})."));
        if (h.Unknown3 != PacketHeader.Unknown3Value || h.Unknown45 != PacketHeader.Unknown45Value)
            throw new UnsafeCommandException(Lang.T("Byte 3–5 của header khác capture.", "Header bytes 3–5 differ from capture."));

        var tail = packet[(K3ProConstants.HeaderLength + spec.DataLength)..];
        if (tail.ContainsAnyExcept((byte)0))
            throw new UnsafeCommandException(Lang.T("Phần pad sau data phải toàn 0.", "Padding after data must be all 0."));
        if (spec.IsRead && packet[K3ProConstants.HeaderLength..].ContainsAnyExcept((byte)0))
            throw new UnsafeCommandException(Lang.T("Gói đọc phải có data toàn 0 (như capture).", "Read packet data must be all 0 (as in capture)."));

        return spec;
    }
}

public sealed class UnsafeCommandException(string message) : InvalidOperationException(message);
