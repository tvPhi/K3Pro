using K3Pro.Protocol.Transport;
using K3Pro.Protocol.Wireless;

namespace K3Pro.Protocol;

/// <summary>Opens a connection to the numpad: wired (258A:010C) first, otherwise via the 2.4G receiver (3554:FA09).</summary>
public static class K3ProConnections
{
    /// <summary>The vendor app (OemDrv.exe) is running — it may steal the receiver's responses (seen 2026-10-02).</summary>
    public static bool IsVendorAppRunning()
    {
        try
        {
            return System.Diagnostics.Process.GetProcessesByName("OemDrv").Length > 0;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public static string VendorAppWarning => Lang.T("connections.vendor_app_oemdrv_exe_running");

    public static IK3ProConnection Open(Action<TransferRecord>? log = null)
    {
        if (HidSharpTransport.FindCandidates().Count > 0)
        {
            IHidTransport t = HidSharpTransport.Open();
            return new K3ProDevice(log is null ? t : new LoggingTransport(t, log));
        }

        if (HidSharpReceiverTransport.FindCandidates().Count == 0)
            throw new IOException(HidSharpTransport.NotFoundMessage());

        IReceiverTransport r = HidSharpReceiverTransport.Open();
        var device = new K3ProReceiverDevice(log is null ? r : new LoggingReceiverTransport(r, log));
        try
        {
            var status = device.ReadStatus();
            if (!status.NumpadLinked)
                throw new IOException(Lang.T("connections.2_4g_receiver_present_but", status));
            return device;
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }
}
