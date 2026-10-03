namespace K3Pro.Protocol.Transport;

/// <summary>Set / Get: wired feature report. Out / In: 2.4G receiver frame.</summary>
public enum TransferDirection { Set, Get, Out, In }

public sealed record TransferRecord(DateTimeOffset Time, TransferDirection Direction, byte[] Data, Exception? Error = null);

/// <summary>Wraps a transport and reports every SET/GET (all bytes + timestamp) to <paramref name="sink"/>.</summary>
public sealed class LoggingTransport(IHidTransport inner, Action<TransferRecord> sink) : IHidTransport
{
    public string Description => inner.Description;

    public void SetFeature(byte[] report)
    {
        try
        {
            inner.SetFeature(report);
            sink(new(DateTimeOffset.Now, TransferDirection.Set, report.ToArray()));
        }
        catch (Exception ex)
        {
            sink(new(DateTimeOffset.Now, TransferDirection.Set, report.ToArray(), ex));
            throw;
        }
    }

    public byte[] GetFeature(byte reportId)
    {
        try
        {
            var data = inner.GetFeature(reportId);
            sink(new(DateTimeOffset.Now, TransferDirection.Get, data.ToArray()));
            return data;
        }
        catch (Exception ex)
        {
            sink(new(DateTimeOffset.Now, TransferDirection.Get, [reportId], ex));
            throw;
        }
    }

    public void Dispose() => inner.Dispose();
}
