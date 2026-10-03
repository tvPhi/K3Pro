using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using K3Pro.App.Services;
using static K3Pro.Protocol.Lang;

namespace K3Pro.App.ViewModels;

internal static class Ui
{
    public static void Post(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(action);
    }
}

/// <summary>Connection state shared by all tabs.</summary>
public partial class SessionViewModel : ObservableObject
{
    private ConnectionState _state = ConnectionState.Disconnected;

    public SessionViewModel()
    {
        ConnectionText = T("Chưa kết nối", "Not connected");
        InfoText = "";
    }

    [ObservableProperty]
    public partial bool IsConnected { get; set; }

    /// <summary>Via the 2.4G receiver: settings, keymap page 0 and color table are writable; FN1 / FN2 / Tap need the cable.</summary>
    [ObservableProperty]
    public partial bool IsWireless { get; set; }

    [ObservableProperty]
    public partial string ConnectionText { get; set; }

    [ObservableProperty]
    public partial string? DevicePath { get; set; }

    [ObservableProperty]
    public partial string InfoText { get; set; }

    /// <summary>Language switch: rebuilds the status text from the current connection.</summary>
    public void RefreshLanguage() => Apply(_state);

    public void Apply(ConnectionState state)
    {
        _state = state;
        IsConnected = state.IsConnected;
        DevicePath = state.DevicePath;
        IsWireless = state.Kind == K3Pro.Protocol.ConnectionKind.Wireless;
        ConnectionText = state.IsConnected
            ? IsWireless ? T("Đã kết nối qua 2.4G (receiver 3554:FA09)", "Connected via 2.4G (receiver 3554:FA09)")
                         : T("Đã kết nối có dây 258A:010C", "Connected by cable 258A:010C")
            : state.Error is { } e ? T($"Chưa kết nối — {e}", $"Not connected — {e}") : T("Chưa kết nối (cắm dây numpad)", "Not connected (plug in the numpad)");
        InfoText = state.Info is { } info
            ? $"{(IsWireless ? "0x05" : "0x82")}: {info} {(info.MatchesCapture ? "✅" : T("⚠ khác capture", "⚠ differs from capture"))}"
            : "";
    }
}
