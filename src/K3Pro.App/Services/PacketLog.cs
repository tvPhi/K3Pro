using K3Pro.Protocol;
using K3Pro.Protocol.Transport;

namespace K3Pro.App.Services;

public enum LogKind { Set, Get, Out, In, Info, Warning, Error }

public sealed record LogEntry(DateTimeOffset Time, LogKind Kind, string Summary, byte[]? Data);

/// <summary>Log of every packet sent/received + events. Thread-safe; also written to a daily file (full hex).</summary>
public sealed class PacketLog(string? logDir)
{
    private readonly Lock _gate = new();

    public event Action<LogEntry>? Added;

    public void Info(string text) => Add(LogKind.Info, text);
    public void Warning(string text) => Add(LogKind.Warning, text);
    public void Error(string text) => Add(LogKind.Error, text);

    public void Add(LogKind kind, string summary, byte[]? data = null) => Add(kind, summary, data, fileOnly: false);

    private void Add(LogKind kind, string summary, byte[]? data, bool fileOnly)
    {
        var entry = new LogEntry(DateTimeOffset.Now, kind, summary, data);
        lock (_gate) AppendToFile(entry);
        if (!fileOnly) Added?.Invoke(entry);
    }

    public void Add(TransferRecord r) => Add(r, fileOnly: false);

    /// <summary>Frames from the periodic connection probe: log file only, not the panel (every 3 s would flood it).</summary>
    public void AddFileOnly(TransferRecord r) => Add(r, fileOnly: true);

    private void Add(TransferRecord r, bool fileOnly)
    {
        var dir = r.Direction switch
        {
            TransferDirection.Set => LogKind.Set,
            TransferDirection.Get => LogKind.Get,
            TransferDirection.Out => LogKind.Out,
            _ => LogKind.In,
        };
        var summary = r.Direction is TransferDirection.Out or TransferDirection.In
            ? Protocol.Wireless.ReceiverFrame.Describe(r.Data)
            : r.Data.Length >= K3ProConstants.HeaderLength ? PacketHeader.Parse(r.Data).ToString() : "";
        if (r.Error is not null) summary += $" — {K3Pro.Protocol.Lang.T("packetlog.error")}: {r.Error.Message}";
        Add(r.Error is null ? dir : LogKind.Error, $"{dir.ToString().ToUpperInvariant()} {summary}".TrimEnd(), r.Data, fileOnly);
    }

    /// <summary>Compact hex: drops trailing zeros and states how many zero bytes were dropped.</summary>
    public static string TrimmedHex(byte[] data)
    {
        int end = data.Length;
        while (end > 1 && data[end - 1] == 0) end--;
        var hex = Hex.Format(data.AsSpan(0, end));
        return end < data.Length ? $"{hex}  …(+{data.Length - end} × 00, {K3Pro.Protocol.Lang.T("packetlog.bytes_total", data.Length)})" : hex;
    }

    private void AppendToFile(LogEntry e)
    {
        if (logDir is null) return;
        try
        {
            Directory.CreateDirectory(logDir);
            var line = $"{e.Time:yyyy-MM-dd HH:mm:ss.fff} {e.Kind,-7} {e.Summary}" +
                       (e.Data is null ? "" : $"\n    {Hex.Format(e.Data)}");
            File.AppendAllText(Path.Combine(logDir, $"k3pro-{e.Time:yyyyMMdd}.log"), line + Environment.NewLine);
        }
        catch (IOException)
        {
            // The log file is only a copy; the in-app log panel is enough.
        }
    }
}
