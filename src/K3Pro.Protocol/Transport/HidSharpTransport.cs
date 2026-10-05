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
        ? Lang.T("transport.numpad_not_found_over_cable", K3ProConstants.VendorId, K3ProConstants.ProductId, K3ProConstants.WirelessVendorId, K3ProConstants.WirelessProductId)
        : Lang.T("transport.no_interface_with_feature_report", K3ProConstants.VendorId, K3ProConstants.ProductId, K3ProConstants.ReportId, K3ProConstants.ReportLength);

    public static HidSharpTransport Open()
    {
        var candidates = FindCandidates();
        if (candidates.Count == 0)
            throw new IOException(NotFoundMessage());
        if (candidates.Count > 1)
            throw new IOException(Lang.T("transport.matching_interfaces_not_sure_which", candidates.Count) +
                                  string.Join("\n  ", candidates.Select(d => d.DevicePath)));

        var device = candidates[0];
        if (!device.TryOpen(out var stream))
            throw new IOException(Lang.T("transport.could_not_open_vendor_app", device.DevicePath));
        return new HidSharpTransport(device, stream);
    }

    public void SetFeature(byte[] report)
    {
        if (report.Length != K3ProConstants.ReportLength)
            throw new ArgumentException(Lang.T("transport.report_must_be_bytes", K3ProConstants.ReportLength), nameof(report));
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
