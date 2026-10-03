// Commands from the old K3ProTool (legacy/K3ProTool), behavior kept unchanged. READ-ONLY: never sends any SET.
using System.Text;
using HidSharp;

namespace K3Pro.Cli;

internal static class LegacyCommands
{
    public static int List(bool vendorOnly)
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

    public static int Info(int vid, int pid)
    {
        var devs = DeviceList.Local.GetHidDevices(vid, pid).ToList();
        if (devs.Count == 0) { Console.WriteLine("Không tìm thấy thiết bị."); return 1; }

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
                foreach (var r in rd.InputReports) Console.WriteLine($"   IN      id=0x{r.ReportID:X2} len={r.Length}");
                foreach (var r in rd.OutputReports) Console.WriteLine($"   OUT     id=0x{r.ReportID:X2} len={r.Length}");
                foreach (var r in rd.FeatureReports) Console.WriteLine($"   FEATURE id=0x{r.ReportID:X2} len={r.Length}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   (không đọc được descriptor: {ex.Message})");
            }
        }
        return 0;
    }

    public static int Listen(int vid, int pid)
    {
        var devs = DeviceList.Local.GetHidDevices(vid, pid)
            .Where(d => SafeLen(d.GetMaxInputReportLength) > 0)
            .ToList();

        foreach (var d in devs)
        {
            var label = Label(d);
            if (!d.TryOpen(out var stream))
            {
                Console.WriteLine($"[skip] {label} (OS khóa interface này hoặc không mở được)");
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

        Console.WriteLine("Đang nghe... bấm phím / đổi chế độ trên numpad. Enter để thoát.");
        Console.ReadLine();
        return 0;
    }

    // GET_REPORT (feature) — read-only, writes nothing to the device.
    public static int GetFeature(int vid, int pid, int reportId)
    {
        var dev = DeviceList.Local.GetHidDevices(vid, pid).FirstOrDefault(d =>
        {
            try { return d.GetReportDescriptor().FeatureReports.Any(r => r.ReportID == reportId); }
            catch { return false; }
        });
        if (dev is null) { Console.WriteLine($"Không có interface nào có feature report 0x{reportId:X2}."); return 1; }
        if (!dev.TryOpen(out var stream)) { Console.WriteLine("Không mở được interface."); return 1; }

        using (stream)
        {
            var buf = new byte[dev.GetMaxFeatureReportLength()];
            buf[0] = (byte)reportId;
            try { stream.GetFeature(buf); }
            catch (Exception ex) { Console.WriteLine($"GET_REPORT lỗi: {ex.Message}"); return 1; }

            Console.WriteLine($"Feature 0x{reportId:X2} ({buf.Length} bytes):");
            Console.WriteLine(HexBlock(buf, "  "));
        }
        return 0;
    }

    private static List<uint> GetUsages(HidDevice d)
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

    private static bool IsVendor(uint usage) => (usage >> 16) >= 0xFF00;
    private static string FormatUsage(uint u) => $"{u >> 16:X4}:{u & 0xFFFF:X4}";
    private static string Label(HidDevice d) => $"[{string.Join(",", GetUsages(d).Select(FormatUsage))}]";

    private static string Safe(Func<string> f) { try { return f(); } catch { return "?"; } }
    private static int SafeLen(Func<int> f) { try { return f(); } catch { return -1; } }

    private static string Hex(ReadOnlySpan<byte> data) => Protocol.Hex.Format(data);

    private static string HexBlock(byte[] data, string indent)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < data.Length; i += 16)
            sb.AppendLine($"{indent}{i:X4}  {Hex(data.AsSpan(i, Math.Min(16, data.Length - i)))}");
        return sb.ToString().TrimEnd();
    }
}
