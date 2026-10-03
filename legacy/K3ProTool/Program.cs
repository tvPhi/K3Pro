// K3ProTool — Phase 0: exploring the HID interfaces of the Darmoshark K3 Pro.
// READ ONLY: this tool never sends any report to the device.
using System.Text;
using HidSharp;

Console.OutputEncoding = Encoding.UTF8;

return args.FirstOrDefault()?.ToLowerInvariant() switch
{
    "list" => CmdList(vendorOnly: args.Contains("--vendor")),
    "info" when args.Length >= 3 => CmdInfo(ParseHex(args[1]), ParseHex(args[2])),
    "listen" when args.Length >= 3 => CmdListen(ParseHex(args[1]), ParseHex(args[2])),
    "getfeature" when args.Length >= 4 => CmdGetFeature(ParseHex(args[1]), ParseHex(args[2]), ParseHex(args[3])),
    _ => Help()
};

// ---------------------------------------------------------------- commands

static int CmdList(bool vendorOnly)
{
    var groups = DeviceList.Local.GetHidDevices()
        .GroupBy(d => (d.VendorID, d.ProductID))
        .OrderBy(g => g.Key.VendorID).ThenBy(g => g.Key.ProductID);

    foreach (var g in groups)
    {
        var ifaces = g.Select(d => (Dev: d, Usages: GetUsages(d))).ToList();
        if (vendorOnly && !ifaces.Any(i => i.Usages.Any(IsVendor))) continue;

        var first = g.First();
        Console.WriteLine($"\n== {g.Key.VendorID:X4}:{g.Key.ProductID:X4}  " +
                          $"{Safe(first.GetManufacturer)} / {Safe(first.GetProductName)}");

        foreach (var (dev, usages) in ifaces)
        {
            var u = usages.Count == 0 ? "?" : string.Join(", ", usages.Select(FormatUsage));
            var tag = usages.Any(IsVendor) ? "  <-- VENDOR" : "";
            Console.WriteLine($"   usage [{u}]  in={SafeLen(dev.GetMaxInputReportLength)} " +
                              $"out={SafeLen(dev.GetMaxOutputReportLength)} " +
                              $"feat={SafeLen(dev.GetMaxFeatureReportLength)}{tag}");
            Console.WriteLine($"     {dev.DevicePath}");
        }
    }
    return 0;
}

static int CmdInfo(int vid, int pid)
{
    var devs = DeviceList.Local.GetHidDevices(vid, pid).ToList();
    if (devs.Count == 0) { Console.WriteLine("Device not found."); return 1; }

    foreach (var d in devs)
    {
        Console.WriteLine($"\n== {Label(d)}");
        Console.WriteLine($"   {d.DevicePath}");
        try
        {
            var raw = d.GetRawReportDescriptor();
            Console.WriteLine($"   Report descriptor ({raw.Length} bytes):");
            Console.WriteLine(HexBlock(raw, "     "));

            var rd = d.GetReportDescriptor();
            foreach (var r in rd.InputReports)   Console.WriteLine($"   IN      id=0x{r.ReportID:X2} len={r.Length}");
            foreach (var r in rd.OutputReports)  Console.WriteLine($"   OUT     id=0x{r.ReportID:X2} len={r.Length}");
            foreach (var r in rd.FeatureReports) Console.WriteLine($"   FEATURE id=0x{r.ReportID:X2} len={r.Length}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   (could not read descriptor: {ex.Message})");
        }
    }
    return 0;
}

static int CmdListen(int vid, int pid)
{
    var devs = DeviceList.Local.GetHidDevices(vid, pid)
        .Where(d => SafeLen(d.GetMaxInputReportLength) > 0)
        .ToList();

    foreach (var d in devs)
    {
        var label = Label(d);
        if (!d.TryOpen(out var stream))
        {
            Console.WriteLine($"[skip] {label} (interface locked by the OS or could not be opened)");
            continue;
        }

        Console.WriteLine($"[open] {label}");
        stream.ReadTimeout = 1000;
        var buf = new byte[d.GetMaxInputReportLength()];

        new Thread(() =>
        {
            try
            {
                while (true)
                {
                    int n;
                    try { n = stream.Read(buf, 0, buf.Length); }
                    catch (TimeoutException) { continue; }

                    if (n > 0)
                        lock (Console.Out)
                            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {label}  {Hex(buf.AsSpan(0, n))}");
                }
            }
            catch (Exception ex)
            {
                lock (Console.Out) Console.WriteLine($"[closed] {label}: {ex.Message}");
            }
        }) { IsBackground = true }.Start();
    }

    Console.WriteLine("Listening... press keys / switch modes on the numpad. Enter to quit.");
    Console.ReadLine();
    return 0;
}

// GET_REPORT (feature) — read only, nothing is written to the device.
static int CmdGetFeature(int vid, int pid, int reportId)
{
    var dev = DeviceList.Local.GetHidDevices(vid, pid).FirstOrDefault(d =>
    {
        try { return d.GetReportDescriptor().FeatureReports.Any(r => r.ReportID == reportId); }
        catch { return false; }
    });
    if (dev is null) { Console.WriteLine($"No interface has feature report 0x{reportId:X2}."); return 1; }
    if (!dev.TryOpen(out var stream)) { Console.WriteLine("Could not open the interface."); return 1; }

    using (stream)
    {
        var buf = new byte[dev.GetMaxFeatureReportLength()];
        buf[0] = (byte)reportId;
        try { stream.GetFeature(buf); }
        catch (Exception ex) { Console.WriteLine($"GET_REPORT failed: {ex.Message}"); return 1; }

        Console.WriteLine($"Feature 0x{reportId:X2} ({buf.Length} bytes):");
        Console.WriteLine(HexBlock(buf, "  "));
    }
    return 0;
}

static int Help()
{
    Console.WriteLine("""
        K3ProTool (read-only)
          list [--vendor]                List HID devices (--vendor: only devices with a vendor usage page)
          info <vid> <pid>               Report descriptor + list of reports for each interface
          listen <vid> <pid>             Print input reports received from the interfaces that can be opened
          getfeature <vid> <pid> <id>    Read (GET_REPORT) one feature report

        Example: dotnet run -- info 0x1234 0xABCD
        """);
    return 1;
}

// ---------------------------------------------------------------- helpers

static List<uint> GetUsages(HidDevice d)
{
    try
    {
        return d.GetReportDescriptor().DeviceItems
            .SelectMany(i => i.Usages.GetAllValues())
            .Distinct()
            .ToList();
    }
    catch { return []; }
}

static bool IsVendor(uint usage) => (usage >> 16) >= 0xFF00;
static string FormatUsage(uint u) => $"{u >> 16:X4}:{u & 0xFFFF:X4}";
static string Label(HidDevice d) => $"[{string.Join(",", GetUsages(d).Select(FormatUsage))}]";

static string Safe(Func<string> f) { try { return f(); } catch { return "?"; } }
static int SafeLen(Func<int> f) { try { return f(); } catch { return -1; } }

static int ParseHex(string s) =>
    Convert.ToInt32(s.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? s[2..] : s, 16);

static string Hex(ReadOnlySpan<byte> data) => Convert.ToHexString(data) is var h
    ? string.Join(" ", Enumerable.Range(0, h.Length / 2).Select(i => h.Substring(i * 2, 2)))
    : "";

static string HexBlock(byte[] data, string indent)
{
    var sb = new StringBuilder();
    for (int i = 0; i < data.Length; i += 16)
        sb.AppendLine($"{indent}{i:X4}  {Hex(data.AsSpan(i, Math.Min(16, data.Length - i)))}");
    return sb.ToString().TrimEnd();
}
