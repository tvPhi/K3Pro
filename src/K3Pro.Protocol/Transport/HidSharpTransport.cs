using HidSharp;

namespace K3Pro.Protocol.Transport;

public sealed class HidSharpTransport : IHidTransport
{
    private readonly HidStream _stream;

    private HidSharpTransport(HidDevice device, HidStream stream)
    {
        Device = device;
        _stream = stream;
    }

    public HidDevice Device { get; }

    public string Description => $"{Device.VendorID:X4}:{Device.ProductID:X4} {Device.DevicePath}";

    /// <summary>The 258A:010C interface that has the 520-byte feature report 0x06.</summary>
    public static IReadOnlyList<HidDevice> FindCandidates() =>
        DeviceList.Local.GetHidDevices(K3ProConstants.VendorId, K3ProConstants.ProductId)
            .Where(HasCommandReport)
            .ToList();

    /// <summary>Enumerates only (does not open): whether the 2.4G receiver is plugged in.</summary>
    public static bool IsWirelessReceiverPresent() =>
        DeviceList.Local.GetHidDevices(K3ProConstants.WirelessVendorId, K3ProConstants.WirelessProductId).Any();

    public static string NotFoundMessage() => IsWirelessReceiverPresent()
        ? Lang.T(
            $"Không thấy numpad qua dây ({K3ProConstants.VendorId:X4}:{K3ProConstants.ProductId:X4}), chỉ thấy receiver 2.4G " +
            $"({K3ProConstants.WirelessVendorId:X4}:{K3ProConstants.WirelessProductId:X4}). Nếu numpad đang ở chế độ 2.4G: chưa hỗ trợ — " +
            "cắm dây và chuyển về chế độ có dây để cấu hình.",
            $"Numpad not found over the cable ({K3ProConstants.VendorId:X4}:{K3ProConstants.ProductId:X4}), only the 2.4G receiver " +
            $"({K3ProConstants.WirelessVendorId:X4}:{K3ProConstants.WirelessProductId:X4}). If the numpad is in 2.4G mode: not supported — " +
            "plug in the cable and switch to wired mode to configure.")
        : Lang.T(
            $"Không thấy interface {K3ProConstants.VendorId:X4}:{K3ProConstants.ProductId:X4} " +
            $"có feature report 0x{K3ProConstants.ReportId:X2} ({K3ProConstants.ReportLength} byte). Đã cắm dây chưa?",
            $"No {K3ProConstants.VendorId:X4}:{K3ProConstants.ProductId:X4} interface " +
            $"with feature report 0x{K3ProConstants.ReportId:X2} ({K3ProConstants.ReportLength} bytes) found. Is the cable plugged in?");

    public static HidSharpTransport Open()
    {
        var candidates = FindCandidates();
        if (candidates.Count == 0)
            throw new IOException(NotFoundMessage());
        if (candidates.Count > 1)
            throw new IOException(Lang.T($"Có {candidates.Count} interface khớp — không chắc chắn chọn đúng, dừng lại:\n  ",
                                         $"{candidates.Count} matching interfaces — not sure which one is right, stopping:\n  ") +
                                  string.Join("\n  ", candidates.Select(d => d.DevicePath)));

        var device = candidates[0];
        if (!device.TryOpen(out var stream))
            throw new IOException(Lang.T($"Không mở được {device.DevicePath} (app hãng đang chạy?).",
                $"Could not open {device.DevicePath} (is the vendor app running?)."));
        return new HidSharpTransport(device, stream);
    }

    public void SetFeature(byte[] report)
    {
        if (report.Length != K3ProConstants.ReportLength)
            throw new ArgumentException(Lang.T($"Report phải dài {K3ProConstants.ReportLength} byte.",
                $"Report must be {K3ProConstants.ReportLength} bytes."), nameof(report));
        _stream.SetFeature(report);
    }

    public byte[] GetFeature(byte reportId)
    {
        var buf = new byte[K3ProConstants.ReportLength];
        buf[0] = reportId;
        _stream.GetFeature(buf);
        return buf;
    }

    public void Dispose() => _stream.Dispose();

    private static bool HasCommandReport(HidDevice d)
    {
        try
        {
            return d.GetMaxFeatureReportLength() == K3ProConstants.ReportLength &&
                   d.GetReportDescriptor().FeatureReports.Any(r => r.ReportID == K3ProConstants.ReportId);
        }
        catch
        {
            return false;
        }
    }
}
