namespace K3Pro.Protocol.Wireless;

/// <summary>Commands via the 2.4G receiver — only commands SEEN in the 2.4G captures (21–25, 33/34).</summary>
public enum ReceiverCommand : byte
{
    /// <summary>Writes keymap page 0 (= wired <see cref="Command.WriteKeymap"/> page 0), 36 frames × 14 bytes. Capture 22/23.</summary>
    WriteKeymap = 0x01,

    /// <summary>Writes the settings block (= wired <see cref="Command.WriteSettings"/>), 10 frames. Capture 33/34, 24/25.</summary>
    WriteSettings = 0x04,

    /// <summary>Reads info (first 6 payload bytes = the wired <see cref="Command.ReadDeviceInfo"/> response).</summary>
    ReadInfo = 0x05,

    /// <summary>❓ Queries receiver / numpad status. The vendor app sends it on every startup.</summary>
    Status = 0x07,

    /// <summary>Writes the color table (= wired <see cref="Command.WriteColorTable"/>), 29 frames (28 × 14 + 7). Capture 24/25.</summary>
    WriteColorTable = 0x09,

    /// <summary>Reads the settings block (= wired <see cref="Command.ReadSettings"/>), 10 response frames.</summary>
    ReadSettings = 0x44,
}

/// <summary>
/// 20-byte frame via the receiver: <c>[13][cmd][frame count][index][page·length][14-byte payload][checksum]</c>.
/// Byte 4: high nibble = page (keymap: 0 Default, 1 FN1, 2 FN2 — capture 14/15), low nibble = payload length (≤ 14).
/// Checksum = 8-bit sum of bytes 0–18 (matches every frame in the captures). Longer data is split into 14-byte frames.
/// </summary>
public static class ReceiverFrame
{
    public const byte ReportId = 0x13;
    public const int Length = 20;
    public const int PayloadOffset = 5;
    public const int PayloadLength = 14;

    /// <summary>128-byte block = 9 frames × 14 bytes + 1 frame of 2 bytes (5A A5).</summary>
    public const int SettingsFrameCount = 10;

    /// <summary>Keymap pages seen over 2.4G (0x0E / 0x1E / 0x2E). Page 3 (Tap): not captured yet.</summary>
    public const byte MaxWirelessKeymapPage = 2;

    public static int PageOf(ReadOnlySpan<byte> frame) => frame[4] >> 4;

    public static int LengthOf(ReadOnlySpan<byte> frame) => frame[4] & 0x0F;

    /// <summary>Data length of each framed write command (same as the corresponding wired command).</summary>
    public static int DataLength(ReceiverCommand command) => command switch
    {
        ReceiverCommand.WriteSettings => Settings.Length,         // 128 → 10 frames
        ReceiverCommand.WriteKeymap => KeymapPage.ByteLength,      // 504 → 36 frames
        ReceiverCommand.WriteColorTable => ColorTable.ByteLength,  // 399 → 29 frames
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, Lang.T("receiver.not_framed_write_command")),
    };

    public static int FrameCount(int dataLength) => (dataLength + PayloadLength - 1) / PayloadLength;

    public static byte Checksum(ReadOnlySpan<byte> frame)
    {
        byte sum = 0;
        foreach (var b in frame[..(Length - 1)]) sum += b;
        return sum;
    }

    public static bool HasValidChecksum(ReadOnlySpan<byte> frame) =>
        frame.Length == Length && frame[^1] == Checksum(frame);

    public static byte[] Build(ReceiverCommand command, byte count, byte index, ReadOnlySpan<byte> payload, byte page = 0)
    {
        if (payload.Length > PayloadLength) throw new ArgumentException(Lang.T("receiver.payload_at_most_14_bytes"), nameof(payload));
        if (page > 0x0F) throw new ArgumentOutOfRangeException(nameof(page));
        var frame = new byte[Length];
        frame[0] = ReportId;
        frame[1] = (byte)command;
        frame[2] = count;
        frame[3] = index;
        frame[4] = (byte)(page << 4 | payload.Length);
        payload.CopyTo(frame.AsSpan(PayloadOffset));
        frame[^1] = Checksum(frame);
        return frame;
    }

    /// <summary>Query frame (07 / 05 / 44): <c>13 cmd 01 00 00 … cks</c> as in the capture.</summary>
    public static byte[] Query(ReceiverCommand command) => ReceiverGuard.Checked(Build(command, 1, 0, []));

    /// <summary>Splits a write command's data into frames, exactly as the vendor app sends them.</summary>
    public static IReadOnlyList<byte[]> Chunked(ReceiverCommand command, ReadOnlySpan<byte> data, byte page = 0)
    {
        if (data.Length != DataLength(command))
            throw new ArgumentException(Lang.T("receiver.command_0x_needs_bytes_got", (byte)command, DataLength(command), data.Length));
        int count = FrameCount(data.Length);
        var frames = new List<byte[]>(count);
        for (int i = 0; i < count; i++)
        {
            int start = i * PayloadLength;
            frames.Add(ReceiverGuard.Checked(Build(command, (byte)count, (byte)i, data.Slice(start, Math.Min(PayloadLength, data.Length - start)), page)));
        }
        return frames;
    }

    public static IReadOnlyList<byte[]> WriteSettings(ReadOnlySpan<byte> settings) => Chunked(ReceiverCommand.WriteSettings, settings);

    /// <summary>Assembles the 10 0x44 response frames into the 128-byte block (drops junk past each frame's length).</summary>
    public static byte[] AssembleSettings(IReadOnlyList<byte[]> frames)
    {
        if (frames.Count != SettingsFrameCount)
            throw new InvalidDataException(Lang.T("receiver.expected_frames_got", SettingsFrameCount, frames.Count));
        var data = new byte[Settings.Length];
        for (int i = 0; i < SettingsFrameCount; i++)
        {
            var f = frames[i];
            int expectedLen = Math.Min(PayloadLength, Settings.Length - i * PayloadLength);
            if (!HasValidChecksum(f) || f[0] != ReportId || f[1] != (byte)ReceiverCommand.ReadSettings ||
                f[2] != SettingsFrameCount || f[3] != i || f[4] != expectedLen)
                throw new InvalidDataException(Lang.T("receiver.invalid_settings_frame", i, Hex.Format(f)));
            f.AsSpan(PayloadOffset, expectedLen).CopyTo(data.AsSpan(i * PayloadLength));
        }
        return data;
    }

    public static string Describe(ReadOnlySpan<byte> frame) =>
        frame.Length < PayloadOffset ? Hex.Format(frame)
        : $"0x13 cmd 0x{frame[1]:X2} ({(Enum.IsDefined((ReceiverCommand)frame[1]) ? ((ReceiverCommand)frame[1]).ToString() : "?")}) " +
          Lang.T("receiver.frame", frame[3] + 1, frame[2]) +
          $"{(PageOf(frame) > 0 ? $" page {PageOf(frame)}" : "")} len {LengthOf(frame)}";
}

/// <summary>Last line of defense before any frame reaches the receiver: only the exact frame shapes seen in a capture.</summary>
public static class ReceiverGuard
{
    public static byte[] Checked(byte[] frame)
    {
        EnsureAllowed(frame);
        return frame;
    }

    public static void EnsureAllowed(ReadOnlySpan<byte> frame)
    {
        if (frame.Length != ReceiverFrame.Length)
            throw new UnsafeCommandException(Lang.T("receiver.receiver_frame_must_be_bytes", ReceiverFrame.Length, frame.Length));
        if (frame[0] != ReceiverFrame.ReportId)
            throw new UnsafeCommandException(Lang.T("receiver.report_id_0x_not_allowed", frame[0]));
        if (!ReceiverFrame.HasValidChecksum(frame))
            throw new UnsafeCommandException(Lang.T("receiver.bad_receiver_frame_checksum"));

        var cmd = (ReceiverCommand)frame[1];
        byte count = frame[2], index = frame[3];
        int page = ReceiverFrame.PageOf(frame), len = ReceiverFrame.LengthOf(frame);
        var payload = frame.Slice(ReceiverFrame.PayloadOffset, ReceiverFrame.PayloadLength);
        switch (cmd)
        {
            case ReceiverCommand.Status or ReceiverCommand.ReadInfo or ReceiverCommand.ReadSettings:
                if (count != 1 || index != 0 || frame[4] != 0 || payload.ContainsAnyExcept((byte)0))
                    throw new UnsafeCommandException(Lang.T("receiver.query_frame_0x_differs_from", frame[1]));
                break;
            case ReceiverCommand.WriteSettings or ReceiverCommand.WriteKeymap or ReceiverCommand.WriteColorTable:
                int total = ReceiverFrame.DataLength(cmd);
                int expectedCount = ReceiverFrame.FrameCount(total);
                int expectedLen = Math.Min(ReceiverFrame.PayloadLength, total - index * ReceiverFrame.PayloadLength);
                if (count != expectedCount || index >= expectedCount || len != expectedLen)
                    throw new UnsafeCommandException(Lang.T("receiver.write_frame_0x_differs_from", frame[1], index));
                int maxPage = cmd == ReceiverCommand.WriteKeymap ? ReceiverFrame.MaxWirelessKeymapPage : 0;
                if (page > maxPage)
                    throw new UnsafeCommandException(Lang.T("receiver.write_frame_0x_page_not", frame[1], page));
                if (payload[len..].ContainsAnyExcept((byte)0))
                    throw new UnsafeCommandException(Lang.T("receiver.padding_after_payload_must_be"));
                if (cmd == ReceiverCommand.WriteSettings && index == expectedCount - 1 && !payload[..2].SequenceEqual(Settings.Magic))
                    throw new UnsafeCommandException(Lang.T("receiver.last_settings_write_frame_must"));
                break;
            default:
                throw new UnsafeCommandException(Lang.T("receiver.receiver_command_0x_not_seen", frame[1]));
        }
    }
}
