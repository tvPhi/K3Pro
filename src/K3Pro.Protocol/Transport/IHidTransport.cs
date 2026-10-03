namespace K3Pro.Protocol.Transport;

/// <summary>Raw HID layer. Kept separate from the logic so tests do not need a device.</summary>
public interface IHidTransport : IDisposable
{
    string Description { get; }

    /// <summary>SET_REPORT (feature). <paramref name="report"/>[0] = report ID, length = <see cref="K3ProConstants.ReportLength"/>.</summary>
    void SetFeature(byte[] report);

    /// <summary>GET_REPORT (feature). Returns a buffer of length <see cref="K3ProConstants.ReportLength"/>, byte 0 = report ID.</summary>
    byte[] GetFeature(byte reportId);
}
