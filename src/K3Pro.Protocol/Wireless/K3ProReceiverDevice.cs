namespace K3Pro.Protocol.Wireless;

/// <summary>Response to command 0x07. ❓ byte 5 = 01 while the numpad is linked over 2.4G (00 while wired) — 2 samples.</summary>
public sealed class ReceiverStatus(byte[] frame)
{
    public ReadOnlySpan<byte> Frame => frame;

    public bool NumpadLinked => frame[5] == 0x01;

    public override string ToString() => Hex.Format(frame);
}

/// <summary>
/// Command logic via the 2.4G receiver. Every frame goes through <see cref="ReceiverGuard"/>; between two OUT frames
/// it waits <see cref="InterFrameDelay"/> (vendor app ~23 ms). Every write command (settings / keymap page 0 / color table):
/// the receiver must echo back each frame exactly.
/// </summary>
public sealed class K3ProReceiverDevice(
    IReceiverTransport transport,
    TimeSpan? interFrameDelay = null,
    Action<TimeSpan>? sleep = null,
    TimeSpan? responseTimeout = null) : IK3ProConnection
{
    public static readonly TimeSpan DefaultInterFrameDelay = TimeSpan.FromMilliseconds(25);
    /// <summary>The vendor app waits ~1 s for the echo before resending (capture 22/24/25); a normal echo arrives after ~8 ms.</summary>
    public static readonly TimeSpan DefaultResponseTimeout = TimeSpan.FromMilliseconds(1000);

    private readonly Action<TimeSpan> _sleep = sleep ?? Thread.Sleep;
    private readonly TimeSpan _timeout = responseTimeout ?? DefaultResponseTimeout;
    private bool _anyWrite;

    public TimeSpan InterFrameDelay { get; } = interFrameDelay ?? DefaultInterFrameDelay;

    public ConnectionKind Kind => ConnectionKind.Wireless;

    public string Description => transport.Description;

    public ReceiverStatus ReadStatus() => new(Query(ReceiverCommand.Status));

    /// <summary>
    /// Like <see cref="ReadStatus"/>, but returns null on timeout instead of throwing (for periodic polling — keeps the debugger quiet).
    /// </summary>
    public ReceiverStatus? TryReadStatus() => TryQuery(ReceiverCommand.Status) is { } r ? new ReceiverStatus(r) : null;

    /// <summary>Reads info 0x05; numpad not answering (asleep) → null.</summary>
    public DeviceInfo? TryReadDeviceInfo() =>
        TryQuery(ReceiverCommand.ReadInfo) is { } r && ReceiverFrame.LengthOf(r) >= DeviceInfo.Length
            ? DeviceInfo.Parse(r.AsSpan(ReceiverFrame.PayloadOffset, DeviceInfo.Length))
            : null;

    public DeviceInfo ReadDeviceInfo()
    {
        var r = Query(ReceiverCommand.ReadInfo);
        if (ReceiverFrame.LengthOf(r) < DeviceInfo.Length) throw new InvalidDataException(Lang.T($"Phản hồi 0x05 quá ngắn: {Hex.Format(r)}", $"0x05 response too short: {Hex.Format(r)}"));
        return DeviceInfo.Parse(r.AsSpan(ReceiverFrame.PayloadOffset, DeviceInfo.Length));
    }

    /// <summary>Reads the block via 0x44; on timeout, asks again exactly once (read command, changes nothing on the device).</summary>
    public byte[] ReadSettingsRaw()
    {
        try
        {
            return ReadSettingsOnce();
        }
        catch (TimeoutException)
        {
            DrainInput();
            return ReadSettingsOnce();
        }
    }

    private byte[] ReadSettingsOnce()
    {
        Send(ReceiverFrame.Query(ReceiverCommand.ReadSettings));
        var frames = new List<byte[]>(ReceiverFrame.SettingsFrameCount);
        for (int i = 0; i < ReceiverFrame.SettingsFrameCount; i++)
            frames.Add(Receive(ReceiverCommand.ReadSettings));
        return ReceiverFrame.AssembleSettings(frames);
    }

    private void DrainInput()
    {
        while (transport.Read(TimeSpan.FromMilliseconds(20)) is not null) { }
    }

    public Settings ReadSettings() => Settings.Parse(ReadSettingsRaw());

    /// <summary>How many times a frame is resent when no echo is seen (the vendor app resends after ~1 s, capture 22/24/25).</summary>
    public const int MaxResends = 2;

    public void Execute(WritePlan plan, Action<PlannedWrite>? onSent = null)
    {
        if (WireEncoding.WhyUnsupported(Kind, plan) is { } why) throw new UnsafeCommandException(why);
        foreach (var write in plan.Writes)
        {
            bool isSettings = write.Command == Command.WriteSettings;
            if (isSettings)
            {
                Settings now;
                try
                {
                    now = ReadSettings();
                }
                catch (Exception ex) when (ex is TimeoutException or InvalidDataException)
                {
                    throw new IOException(Lang.T($"{ex.Message} (chưa gửi khung ghi nào — thiết bị không đổi).",
                        $"{ex.Message} (no write frame sent yet — device unchanged)."), ex);
                }
                if (write.ExpectedSettings is { } expected && !now.Data.SequenceEqual(expected.Data))
                    throw new InvalidOperationException(Lang.T("Block settings trên thiết bị đã đổi từ lúc lập kế hoạch — hủy, hãy làm lại.",
                        "The device's settings block changed since the plan was made — cancelled, please try again."));
            }

            var frames = WireEncoding.Encode(Kind, write);
            int sent = 0;
            try
            {
                foreach (var frame in frames)
                {
                    SendWithEcho(frame);
                    sent++;
                }
            }
            catch (Exception ex) when (ex is TimeoutException or InvalidDataException)
            {
                throw new IOException(Lang.T(
                    $"{ex.Message} — đã gửi {sent}/{frames.Count} khung của \"{write.Title}\": dữ liệu trên thiết bị " +
                    "có thể dở dang, hãy Apply lại.",
                    $"{ex.Message} — sent {sent}/{frames.Count} frames of \"{write.Title}\": data on the device " +
                    "may be incomplete, please Apply again."), ex);
            }
            onSent?.Invoke(write);

            // Only settings have a read command (0x44) to verify against; keymap / color table have no read command yet.
            if (isSettings && !ReadSettings().Data.SequenceEqual(write.Packet.AsSpan(K3ProConstants.HeaderLength, Settings.Length)))
                throw new InvalidDataException(Lang.T("Đọc lại settings qua 2.4G khác block vừa ghi.",
                    "Settings read back over 2.4G differ from the block just written."));
        }
    }

    /// <summary>
    /// Sends one write frame and waits for the receiver to echo it unchanged. No echo → resend that exact frame (like the
    /// vendor app), at most <see cref="MaxResends"/> times. Echoes of other frames (e.g. a late echo of a previous send) are ignored.
    /// </summary>
    private void SendWithEcho(byte[] frame)
    {
        var cmd = (ReceiverCommand)frame[1];
        for (int attempt = 0; ; attempt++)
        {
            Send(frame);
            try
            {
                var echo = Receive(cmd, r => r[3] == frame[3]);
                if (!echo.AsSpan().SequenceEqual(frame))
                    throw new InvalidDataException(Lang.T($"Receiver không echo đúng khung {frame[3] + 1}/{frame[2]}: {Hex.Format(echo)}",
                        $"Receiver did not echo frame {frame[3] + 1}/{frame[2]} correctly: {Hex.Format(echo)}"));
                return;
            }
            catch (TimeoutException) when (attempt < MaxResends)
            {
                // resend the same frame
            }
        }
    }

    public void Dispose() => transport.Dispose();

    private byte[] Query(ReceiverCommand command)
    {
        Send(ReceiverFrame.Query(command));
        return Receive(command);
    }

    private byte[]? TryQuery(ReceiverCommand command)
    {
        Send(ReceiverFrame.Query(command));
        return TryReceive(command, null);
    }

    private void Send(byte[] frame)
    {
        ReceiverGuard.EnsureAllowed(frame);
        if (_anyWrite && InterFrameDelay > TimeSpan.Zero) _sleep(InterFrameDelay);
        transport.Write(frame);
        _anyWrite = true;
    }

    /// <summary>
    /// Waits for a response frame of the given command (that also satisfies <paramref name="match"/>); other input reports are skipped.
    /// Timeout → the numpad may be asleep.
    /// </summary>
    private byte[] Receive(ReceiverCommand command, Func<byte[], bool>? match = null) =>
        TryReceive(command, match) ?? throw new TimeoutException(Lang.T(
            $"Receiver không trả lời lệnh 0x{(byte)command:X2} — numpad đang ngủ / tắt? Bấm một phím trên numpad rồi thử lại.",
            $"Receiver did not answer command 0x{(byte)command:X2} — is the numpad asleep / off? Press a key on the numpad and try again."));

    private byte[]? TryReceive(ReceiverCommand command, Func<byte[], bool>? match)
    {
        var deadline = DateTime.UtcNow + _timeout;
        while (true)
        {
            var left = deadline - DateTime.UtcNow;
            var r = left > TimeSpan.Zero ? transport.Read(left) : null;
            if (r is null) return null;
            if (r.Length != ReceiverFrame.Length || r[0] != ReceiverFrame.ReportId || r[1] != (byte)command) continue;
            if (match is not null && !match(r)) continue;
            if (!ReceiverFrame.HasValidChecksum(r)) throw new InvalidDataException(Lang.T($"Checksum phản hồi sai: {Hex.Format(r)}", $"Bad response checksum: {Hex.Format(r)}"));
            return r;
        }
    }
}
