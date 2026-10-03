using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using K3Pro.App.Services;
using static K3Pro.Protocol.Lang;

namespace K3Pro.App.ViewModels;

public sealed class LogEntryViewModel(LogEntry entry)
{
    public string Time { get; } = entry.Time.ToString("HH:mm:ss.fff");
    public LogKind Kind { get; } = entry.Kind;
    public string Summary { get; } = entry.Summary;
    public string? Hex { get; } = entry.Data is null ? null : PacketLog.TrimmedHex(entry.Data);
    public bool HasHex => Hex is not null;

    public string KindText => Kind switch
    {
        LogKind.Set => "SET",
        LogKind.Get => "GET",
        LogKind.Out => "OUT",
        LogKind.In => "IN",
        LogKind.Warning => "WARN",
        LogKind.Error => "ERR",
        _ => "INFO",
    };

    public bool IsSet => Kind is LogKind.Set or LogKind.Out;
    public bool IsGet => Kind is LogKind.Get or LogKind.In;
    public bool IsWarning => Kind == LogKind.Warning;
    public bool IsError => Kind == LogKind.Error;
}

public partial class LogViewModel : ObservableObject
{
    private const int MaxEntries = 2000;

    public LogViewModel(PacketLog log, string logDir)
    {
        LogDir = logDir;
        log.Added += e => Ui.Post(() =>
        {
            Entries.Add(new LogEntryViewModel(e));
            while (Entries.Count > MaxEntries) Entries.RemoveAt(0);
        });
    }

    public ObservableCollection<LogEntryViewModel> Entries { get; } = [];

    public string LogDir { get; }

    public string Header => T($"Log gói SET / GET / OUT / IN (bản đầy đủ: {LogDir})", $"Packet log SET / GET / OUT / IN (full log: {LogDir})");

    public void RefreshLanguage() => OnPropertyChanged(nameof(Header));

    [RelayCommand]
    private void Clear() => Entries.Clear();
}
