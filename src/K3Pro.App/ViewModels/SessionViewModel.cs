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
        ConnectionText = T("session.not_connected");
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

    public string BatteryTip => T("session.battery_bluetooth_read_os_from");

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
            ? IsWireless ? T("session.connected_via_2_4g_receiver")
                         : T("session.connected_cable_258a_010c")
            : _bluetooth.Connected
                ? T("session.using_bluetooth_k3pro_5_0")
                : state.Error is { } e ? T("session.not_connected_2", e) : T("session.not_connected_plug_numpad");
        IsBluetoothOnly = !state.IsConnected && _bluetooth.Connected;
        WriteBlockedText = state.IsConnected ? null
            : IsBluetoothOnly
                ? T("session.bluetooth_numpad_can_t_be")
                : T("session.numpad_not_connected_plug_cable");
        InfoText = state.Info is { } info
            ? $"{(IsWireless ? "0x05" : "0x82")}: {info} {(info.MatchesCapture ? "✅" : T("session.differs_from_capture"))}"
            : "";
    }
}
