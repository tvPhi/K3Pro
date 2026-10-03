using K3Pro.Protocol.Transport;

namespace K3Pro.Protocol;

/// <summary>
/// Command logic on top of an <see cref="IHidTransport"/>. Every packet goes through <see cref="CommandGuard"/>,
/// and <see cref="InterPacketDelay"/> is always awaited between two transfers.
/// This class knows NOTHING about dry-run: the caller (CLI) is responsible for calling write methods only once the user has allowed it.
/// </summary>
public sealed class K3ProDevice(IHidTransport transport, TimeSpan? interPacketDelay = null, Action<TimeSpan>? sleep = null)
    : IK3ProConnection
{
    private readonly Action<TimeSpan> _sleep = sleep ?? Thread.Sleep;
    private bool _anyTransfer;

    public TimeSpan InterPacketDelay { get; } = interPacketDelay ?? K3ProConstants.DefaultInterPacketDelay;

    public IHidTransport Transport => transport;

    public ConnectionKind Kind => ConnectionKind.Wired;

    public string Description => transport.Description;

    public byte[] ReadSettingsRaw() => ReadCommand(PacketBuilder.ReadSettings());

    public DeviceInfo ReadDeviceInfo() => DeviceInfo.Parse(ReadCommand(PacketBuilder.ReadDeviceInfo()));

    /// <summary>Reads 0x84; throws if the 5A A5 magic is not intact.</summary>
    public Settings ReadSettings() => Settings.Parse(ReadCommand(PacketBuilder.ReadSettings()));

    /// <summary>Sends a prebuilt write packet (0x04 / 0x03 / 0x0A) — byte-for-byte what the dry-run printed.</summary>
    public void SendCommand(byte[] packet)
    {
        CommandGuard.EnsureAllowed(packet, expectRead: false);
        Transfer(() => transport.SetFeature(packet));
    }

    /// <summary>
    /// Sends the packets of <paramref name="plan"/> in order. For 0x04: reads 0x84 back before sending (must be unchanged since
    /// the plan was made) and after sending (must match the block written). Stops at the first error.
    /// </summary>
    public void Execute(WritePlan plan, Action<PlannedWrite>? onSent = null)
    {
        foreach (var write in plan.Writes)
        {
            if (write.ExpectedSettings is { } expected && !ReadSettings().Data.SequenceEqual(expected.Data))
                throw new InvalidOperationException(Lang.T("Block settings trên thiết bị đã đổi từ lúc lập kế hoạch — hủy, hãy làm lại.",
                    "The device's settings block changed since the plan was made — cancelled, please try again."));

            SendCommand(write.Packet);
            onSent?.Invoke(write);

            if (write.Command == Command.WriteSettings)
            {
                var written = write.Packet.AsSpan(K3ProConstants.HeaderLength, Settings.Length);
                if (!ReadSettings().Data.SequenceEqual(written))
                    throw new InvalidDataException(Lang.T("Đọc lại 0x84 khác block vừa ghi.", "0x84 read-back differs from the block just written."));
            }
        }
    }

    /// <summary>SET header (data = 0) → GET → check the header is echoed → return data.</summary>
    public byte[] ReadCommand(byte[] request)
    {
        var spec = CommandGuard.EnsureAllowed(request, expectRead: true);
        Transfer(() => transport.SetFeature(request));

        byte[] response = [];
        Transfer(() => response = transport.GetFeature(K3ProConstants.ReportId));

        if (response.Length < K3ProConstants.HeaderLength + spec.DataLength)
            throw new InvalidDataException(Lang.T($"Phản hồi quá ngắn ({response.Length} byte).", $"Response too short ({response.Length} bytes)."));
        var expected = request.AsSpan(0, K3ProConstants.HeaderLength);
        var echoed = response.AsSpan(0, K3ProConstants.HeaderLength);
        if (!echoed.SequenceEqual(expected))
            throw new InvalidDataException(Lang.T($"Header phản hồi {Hex.Format(echoed)} khác yêu cầu {Hex.Format(expected)}.",
                $"Response header {Hex.Format(echoed)} differs from request {Hex.Format(expected)}."));

        return response.AsSpan(K3ProConstants.HeaderLength, spec.DataLength).ToArray();
    }

    public void Dispose() => transport.Dispose();

    private void Transfer(Action io)
    {
        if (_anyTransfer && InterPacketDelay > TimeSpan.Zero) _sleep(InterPacketDelay);
        io();
        _anyTransfer = true;
    }
}
