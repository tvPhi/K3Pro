using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using K3Pro.App.Services;
using K3Pro.Protocol.Bluetooth;
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
    private BluetoothStatus _bluetooth = BluetoothStatus.None;

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

    /// <summary>The numpad is on Bluetooth and there is no cable / 2.4G link: battery is shown, settings can't be changed.</summary>
    [ObservableProperty]
    public partial bool IsBluetoothOnly { get; set; }

    /// <summary>"🔋 87%" while connected over Bluetooth (BLE Battery Service, read by the OS); null = nothing to show.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBattery))]
    public partial string? BatteryText { get; set; }

    public bool HasBattery => BatteryText is not null;

    /// <summary>
    /// Why settings can't be changed right now (null = configurable). Keymap / Lighting / settings writes need the cable or 2.4G:
    /// the numpad can't be configured over Bluetooth (the vendor app can't either — author, 2026-10-04).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWriteBlocked))]
    public partial string? WriteBlockedText { get; set; }

    public bool IsWriteBlocked => WriteBlockedText is not null;

    public string BatteryTip => T("Pin (Bluetooth) — hệ điều hành đọc từ BLE Battery Service, app không gửi gì xuống numpad",
        "Battery (Bluetooth) — read by the OS from the BLE Battery Service; the app sends nothing to the numpad");

    /// <summary>Language switch: rebuilds the status text from the current connection.</summary>
    public void RefreshLanguage()
    {
        Apply(_state);
        OnPropertyChanged(nameof(BatteryTip));
    }

    public void Apply(BluetoothStatus status)
    {
        _bluetooth = status;
        BatteryText = status is { Connected: true, BatteryPercent: { } p } ? $"🔋 {p}%" : null;
        Apply(_state);
    }

    public void Apply(ConnectionState state)
    {
        _state = state;
        IsConnected = state.IsConnected;
        DevicePath = state.DevicePath;
        IsWireless = state.Kind == K3Pro.Protocol.ConnectionKind.Wireless;
        ConnectionText = state.IsConnected
            ? IsWireless ? T("Đã kết nối qua 2.4G (receiver 3554:FA09)", "Connected via 2.4G (receiver 3554:FA09)")
                         : T("Đã kết nối có dây 258A:010C", "Connected by cable 258A:010C")
            : _bluetooth.Connected
                ? T("Đang dùng Bluetooth (K3PRO 5.0) — chỉ chỉnh được khi cắm dây hoặc qua 2.4G",
                    "Using Bluetooth (K3PRO 5.0) — settings can be changed over the cable or 2.4G only")
                : state.Error is { } e ? T($"Chưa kết nối — {e}", $"Not connected — {e}") : T("Chưa kết nối (cắm dây numpad)", "Not connected (plug in the numpad)");
        IsBluetoothOnly = !state.IsConnected && _bluetooth.Connected;
        WriteBlockedText = state.IsConnected ? null
            : IsBluetoothOnly
                ? T("Đang dùng Bluetooth — numpad không cho chỉnh keymap / đèn / sleep qua Bluetooth. Cắm dây hoặc chuyển sang 2.4G để chỉnh.",
                    "On Bluetooth — the numpad can't be configured over Bluetooth (keymap / lighting / sleep). Plug in the cable or switch to 2.4G to change settings.")
                : T("Chưa kết nối numpad — cắm dây hoặc bật 2.4G (bấm một phím cho numpad thức) để chỉnh.",
                    "Numpad not connected — plug in the cable or use 2.4G (press a key to wake the numpad) to change settings.");
        InfoText = state.Info is { } info
            ? $"{(IsWireless ? "0x05" : "0x82")}: {info} {(info.MatchesCapture ? "✅" : T("⚠ khác capture", "⚠ differs from capture"))}"
            : "";
    }
}
