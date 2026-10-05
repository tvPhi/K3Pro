using HidSharp;
using K3Pro.Protocol.Transport;

namespace K3Pro.Protocol.Wireless;

/// <summary>Raw HID layer to the receiver: writes output report 0x13, reads input report 0x13 (interrupt IN).</summary>
public interface IReceiverTransport : IDisposable
{
    string Description { get; }

    void Write(byte[] frame);

    /// <summary>Reads one input report; null on timeout.</summary>
    byte[]? Read(TimeSpan timeout);
}

/// <summary>
/// Vendor interface of receiver 3554:FA09 (usage FF02:0002, 20-byte in/out, mi_01&amp;col01).
/// This interface only has IN endpoint 0x82 → <c>Write</c> goes via a control SET_REPORT (output), exactly like the vendor app.
/// </summary>
public sealed class HidSharpReceiverTransport : IReceiverTransport
{
    private readonly HidStream _stream;

    private HidSharpReceiverTransport(HidDevice device, HidStream stream)
    {
        Device = device;
        _stream = stream;
    }

    public HidDevice Device { get; }

    public string Description => $"{Device.VendorID:X4}:{Device.ProductID:X4} (receiver 2.4G) {Device.DevicePath}";

    public static IReadOnlyList<HidDevice> FindCandidates() =>
        DeviceList.Local.GetHidDevices(K3ProConstants.WirelessVendorId, K3ProConstants.WirelessProductId)
            .Where(IsCommandInterface)
            .ToList();

    public static HidSharpReceiverTransport Open()
    {
        var candidates = FindCandidates();
        if (candidates.Count != 1)
            throw new IOException(candidates.Count == 0
                ? Lang.T("receivertransport.command_interface_receiver_not_found", K3ProConstants.WirelessVendorId, K3ProConstants.WirelessProductId)
                : Lang.T("receivertransport.matching_receiver_interfaces_not_sure", candidates.Count));
        var device = candidates[0];
        if (!device.TryOpen(out var stream))
            throw new IOException(Lang.T("transport.could_not_open_vendor_app", device.DevicePath));
        return new HidSharpReceiverTransport(device, stream);
    }

    public void Write(byte[] frame)
    {
        if (frame.Length != ReceiverFrame.Length) throw new ArgumentException(Lang.T("receivertransport.frame_must_be_20_bytes"), nameof(frame));
        _stream.Write(frame);
    }

    public byte[]? Read(TimeSpan timeout)
    {
        _stream.ReadTimeout = Math.Max(1, (int)timeout.TotalMilliseconds);
        var buf = new byte[ReceiverFrame.Length];
        try
        {
            int n = _stream.Read(buf, 0, buf.Length);
            return n > 0 ? buf[..n] : null;
        }
        catch (TimeoutException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();

    private static bool IsCommandInterface(HidDevice d)
    {
        try
        {
            if (d.GetMaxOutputReportLength() != ReceiverFrame.Length || d.GetMaxInputReportLength() != ReceiverFrame.Length) return false;
            var rd = d.GetReportDescriptor();
            return rd.OutputReports.Any(r => r.ReportID == ReceiverFrame.ReportId) &&
                   rd.InputReports.Any(r => r.ReportID == ReceiverFrame.ReportId);
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Wraps a receiver transport and reports every OUT / IN frame to the log.</summary>
public sealed class LoggingReceiverTransport(IReceiverTransport inner, Action<TransferRecord> sink) : IReceiverTransport
{
    public string Description => inner.Description;

    public void Write(byte[] frame)
    {
        try
        {
            inner.Write(frame);
            sink(new(DateTimeOffset.Now, TransferDirection.Out, frame.ToArray()));
        }
        catch (Exception ex)
        {
            sink(new(DateTimeOffset.Now, TransferDirection.Out, frame.ToArray(), ex));
            throw;
        }
    }

    public byte[]? Read(TimeSpan timeout)
    {
        var data = inner.Read(timeout);
        if (data is not null) sink(new(DateTimeOffset.Now, TransferDirection.In, data.ToArray()));
        return data;
    }

    public void Dispose() => inner.Dispose();
}
