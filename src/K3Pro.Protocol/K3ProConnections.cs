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

    public static string VendorAppWarning => Lang.T(
        "⚠ App hãng (OemDrv.exe) đang chạy — nó cũng nói chuyện với receiver và có thể làm mất phản hồi. Tắt hẳn app hãng (kể cả ở khay hệ thống).",
        "⚠ The vendor app (OemDrv.exe) is running — it also talks to the receiver and may swallow responses. Quit it completely (including from the system tray).");

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
                throw new IOException(Lang.T(
                    $"Receiver 2.4G có mặt nhưng numpad chưa nối (trạng thái {status}) — numpad đang ngủ / tắt / đang ở chế độ khác? " +
                    "Bấm một phím trên numpad rồi thử lại.",
                    $"2.4G receiver present but the numpad is not linked (status {status}) — numpad asleep / off / in another mode? " +
                    "Press a key on the numpad and try again."));
            return device;
        }
        catch
        {
            device.Dispose();
            throw;
        }
    }
}
