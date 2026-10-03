using K3Pro.Protocol.Transport;
using static K3Pro.Protocol.Tests.CaptureFixtures;

namespace K3Pro.Protocol.Tests;

/// <summary>Fake transport: records every SET packet, answers GETs from a queue (zero-padded like HidD_GetFeature).</summary>
internal sealed class FakeTransport : IHidTransport
{
    public List<string> Log { get; } = [];
    public List<byte[]> Sent { get; } = [];
    public Queue<byte[]> Responses { get; } = new();

    public string Description => "fake";

    public void SetFeature(byte[] report)
    {
        Log.Add("SET");
        Sent.Add(report.ToArray());
    }

    public byte[] GetFeature(byte reportId)
    {
        Log.Add("GET");
        var buf = new byte[K3ProConstants.ReportLength];
        Responses.Dequeue().CopyTo(buf, 0);
        return buf;
    }

    public void Dispose() { }
}

public class DeviceTests
{
    private readonly FakeTransport _transport = new();
    private readonly List<TimeSpan> _sleeps = [];

    private K3ProDevice CreateDevice() => new(_transport, TimeSpan.FromMilliseconds(50), _sleeps.Add);

    [Fact]
    public void ReadDeviceInfo_replays_capture_exchange()
    {
        _transport.Responses.Enqueue(ResponseTo(OpenClose, Command.ReadDeviceInfo, page: 0x01).Bytes);

        var info = CreateDevice().ReadDeviceInfo();

        Assert.Equal("03 00 00 00 00 17", info.ToString());
        Assert.Equal(["SET", "GET"], _transport.Log);
        Assert.Equal(Set(OpenClose, Command.ReadDeviceInfo, page: 0x01).Bytes, _transport.Sent.Single());
    }

    [Fact]
    public void ReadSettings_replays_capture_exchange_and_paces_packets()
    {
        _transport.Responses.Enqueue(ResponseTo(RgbRed, Command.ReadSettings).Bytes);
        var device = CreateDevice();

        var settings = device.ReadSettings();
        device.SendCommand(PacketBuilder.WriteSettings(settings.WithLightingMode(LightingModes.Static)));

        Assert.Equal(["SET", "GET", "SET"], _transport.Log);
        Assert.Equal(Set(RgbRed, Command.WriteSettings).Bytes, _transport.Sent[1]);
        Assert.Equal([TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50)], _sleeps);
    }

    [Fact]
    public void ReadCommand_rejects_response_with_wrong_header_echo()
    {
        var resp = ResponseTo(RgbRed, Command.ReadSettings).Bytes;
        resp[1] = 0x82;
        _transport.Responses.Enqueue(resp);

        Assert.Throws<InvalidDataException>(() => CreateDevice().ReadSettings());
    }

    [Fact]
    public void ReadSettings_rejects_bad_magic()
    {
        var resp = ResponseTo(RgbRed, Command.ReadSettings).Bytes;
        resp[K3ProConstants.HeaderLength + Settings.MagicOffset] = 0x00;
        _transport.Responses.Enqueue(resp);

        Assert.Throws<InvalidDataException>(() => CreateDevice().ReadSettings());
    }

    [Theory]
    [InlineData(0x83, 0x00, 0x01F8)] // "read keymap" inferred by symmetry — never seen in a capture
    [InlineData(0x05, 0x00, 0x0080)]
    [InlineData(0x0A, 0x01, 0x018F)] // unknown page
    [InlineData(0x03, 0x04, 0x01F8)] // page > 3
    [InlineData(0x04, 0x00, 0x0081)] // unknown len
    public void SendCommand_rejects_packets_not_seen_in_capture(byte cmd, byte page, ushort len)
    {
        var packet = new byte[K3ProConstants.ReportLength];
        new PacketHeader(0x06, cmd, page, 0x00, 0x0001, len).WriteTo(packet);

        Assert.Throws<UnsafeCommandException>(() => CreateDevice().SendCommand(packet));
        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public void SendCommand_rejects_read_commands_and_ReadCommand_rejects_writes()
    {
        var device = CreateDevice();

        Assert.Throws<UnsafeCommandException>(() => device.SendCommand(PacketBuilder.ReadSettings()));
        Assert.Throws<UnsafeCommandException>(() =>
            device.ReadCommand(PacketBuilder.WriteColorTable(CaptureBaseline.ColorTable())));
        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public void Guard_rejects_wrong_length_report_id_and_nonzero_padding()
    {
        var ok = PacketBuilder.WriteColorTable(CaptureBaseline.ColorTable());

        var shortPacket = ok[..519];
        var wrongId = ok.ToArray(); wrongId[0] = 0x05;
        var dirtyPad = ok.ToArray(); dirtyPad[^1] = 0x01;
        var wrongUnknown = ok.ToArray(); wrongUnknown[4] = 0x00;

        Assert.Throws<UnsafeCommandException>(() => CommandGuard.EnsureAllowed(shortPacket, expectRead: false));
        Assert.Throws<UnsafeCommandException>(() => CommandGuard.EnsureAllowed(wrongId, expectRead: false));
        Assert.Throws<UnsafeCommandException>(() => CommandGuard.EnsureAllowed(dirtyPad, expectRead: false));
        Assert.Throws<UnsafeCommandException>(() => CommandGuard.EnsureAllowed(wrongUnknown, expectRead: false));
    }
}
