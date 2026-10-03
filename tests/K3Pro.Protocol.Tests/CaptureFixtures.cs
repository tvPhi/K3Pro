using System.Text.Json;

namespace K3Pro.Protocol.Tests;

/// <summary>One report 0x06 feature transfer extracted from a capture (tools/make_fixtures.py).</summary>
public sealed record CaptureEvent(int Frame, double T, string Dir, int Iface, int WLength, long Status, int Length, string Hex)
{
    public byte[] Bytes => Convert.FromHexString(Hex);
    public byte Command => Bytes[1];
    public byte Page => Bytes[2];
    public bool IsSet => Dir == "SET";

    public override string ToString() => $"f{Frame} {Dir} cmd 0x{Command:X2} page {Page}";
}

public sealed record CapturedFrame(int Frame, string Dir, string Hex)
{
    public byte[] Bytes => Convert.FromHexString(Hex);
}

public sealed record CaptureFixture(string Source, int ReportId, List<CaptureEvent> Events, List<CapturedFrame>? Receiver);

public static class CaptureFixtures
{
    public const string OpenClose = "01-open-close";
    public const string Num1ToA = "02-num1-to-A";
    public const string Num1ToB = "03-num1-to-B";
    public const string RgbRed = "04-rgb-red";
    public const string RgbBlue = "05-rgb-blue";

    public const string WiredSleep30s = "31-wired-sleep-30s";
    public const string WiredSleep20Min = "32-wired-sleep-20min";
    public const string WirelessSleep30s = "33-24g-sleep-30s";
    public const string WirelessSleep20Min = "34-24g-sleep-20min";

    public static readonly string[] All = [OpenClose, Num1ToA, Num1ToB, RgbRed, RgbBlue];

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static CaptureFixture Load(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".json");
        return JsonSerializer.Deserialize<CaptureFixture>(File.ReadAllText(path), Options)
               ?? throw new InvalidDataException(path);
    }

    /// <summary>The single SET of (cmd, page) in the capture.</summary>
    public static CaptureEvent Set(string name, Command cmd, byte page = 0) =>
        Load(name).Events.Single(e => e.IsSet && e.Command == (byte)cmd && e.Page == page);

    /// <summary>The GET right after the SET of (cmd, page).</summary>
    public static CaptureEvent ResponseTo(string name, Command cmd, byte page = 0)
    {
        var events = Load(name).Events;
        var i = events.FindIndex(e => e.IsSet && e.Command == (byte)cmd && e.Page == page);
        Assert.True(i >= 0 && i + 1 < events.Count, $"{name}: no SET 0x{(byte)cmd:X2} found");
        var get = events[i + 1];
        Assert.Equal("GET", get.Dir);
        return get;
    }
}
