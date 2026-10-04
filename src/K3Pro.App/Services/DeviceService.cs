using HidSharp;
using K3Pro.Protocol;
using K3Pro.Protocol.Bluetooth;
using K3Pro.Protocol.Transport;
using K3Pro.Protocol.Wireless;

namespace K3Pro.App.Services;

public sealed record ConnectionState(bool IsConnected, string? DevicePath, DeviceInfo? Info, string? Error, ConnectionKind? Kind = null)
{
    public static readonly ConnectionState Disconnected = new(false, null, null, null);
}

/// <summary>Device access for the UI. Only reads (0x82, 0x84) and executes a <see cref="WritePlan"/> — no packets are built here.</summary>
public interface IDeviceService : IDisposable
{
    /// <summary>May be raised from a background thread.</summary>
    event Action<ConnectionState>? ConnectionChanged;

    ConnectionState State { get; }

    /// <summary>Bluetooth mode (battery level read by the OS — nothing is sent). May be raised from a background thread.</summary>
    event Action<BluetoothStatus>? BluetoothChanged;

    BluetoothStatus Bluetooth { get; }

    void Start();

    Task RefreshAsync();

    Task<Settings> ReadSettingsAsync();

    /// <summary>Raw 0x84 (magic not checked) — so it can be shown even when the block is corrupt.</summary>
    Task<byte[]> ReadSettingsRawAsync();

    /// <summary>Runs over the connection kind the plan was made for; cancels if it changed (cable ↔ 2.4G) in the meantime.</summary>
    Task ExecuteAsync(WritePlan plan, ConnectionKind expectedKind);
}

/// <summary>
/// HidSharp: auto-detect / reconnect — wired 258A:010C is preferred, otherwise the 2.4G receiver 3554:FA09.
/// Plugging / unplugging the cable → <see cref="DeviceList.Changed"/>. In 2.4G the receiver stays plugged in, so the numpad going asleep /
/// waking up doesn't change DeviceList → poll with status frame 0x07 (query command from the capture, answered by the receiver itself):
/// every 3 s while not linked, 10 s while linked.
/// Each operation opens a new connection (<see cref="K3ProConnections"/>); every packet / frame goes to <see cref="PacketLog"/>.
/// Operations are serialized.
/// </summary>
public sealed class HidDeviceService(PacketLog log) : IDeviceService
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(400);
    public static readonly TimeSpan WakePollInterval = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan LinkPollInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan BluetoothPollInterval = TimeSpan.FromSeconds(15);
    private static string SleepingMessage => Lang.T("numpad 2.4G không phản hồi (đang ngủ / tắt) — bấm một phím, app tự kết nối lại",
        "2.4G numpad not responding (asleep / off) — press a key and the app reconnects automatically");

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _debounce;
    private Timer? _pollTimer;
    private DateTime _nextPoll = DateTime.MinValue;
    private DateTime _nextBluetoothPoll = DateTime.MinValue;
    private int _bluetoothBusy;
    private bool _started;

    public event Action<ConnectionState>? ConnectionChanged;

    public event Action<BluetoothStatus>? BluetoothChanged;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public BluetoothStatus Bluetooth { get; private set; } = BluetoothStatus.None;

    public void Start()
    {
        if (_started) return;
        _started = true;
        DeviceList.Local.Changed += OnDeviceListChanged;
        _pollTimer = new Timer(_ =>
        {
            _ = PollAsync();
            PollBluetooth(force: false);
        }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _ = RefreshAsync();
    }

    /// <summary>Probe now (startup, "Rescan" pressed, cable plugged / unplugged) — the query frames show in the log panel.</summary>
    public async Task RefreshAsync()
    {
        await _gate.WaitAsync();
        try
        {
            await ProbeAsync(verbose: true);
        }
        finally
        {
            ScheduleNextPoll();
            _gate.Release();
        }
    }

    /// <summary>Periodic probe in 2.4G; skipped while another operation runs or when wired (DeviceList handles that).</summary>
    private async Task PollAsync()
    {
        if (DateTime.UtcNow < _nextPoll || State is { IsConnected: true, Kind: ConnectionKind.Wired }) return;
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            await ProbeAsync(verbose: false);
        }
        catch (Exception ex)
        {
            log.Error(Lang.T($"Dò kết nối lỗi: {ex.Message}", $"Connection probe failed: {ex.Message}"));
        }
        finally
        {
            ScheduleNextPoll();
            _gate.Release();
        }
    }

    /// <summary>
    /// Bluetooth status from the OS (<see cref="BluetoothBattery"/>): no device I/O, so it doesn't take the operation gate.
    /// Every 15 s, and right away when DeviceList changes (BLE HID collections appear / disappear).
    /// </summary>
    private void PollBluetooth(bool force)
    {
        if (!force && DateTime.UtcNow < _nextBluetoothPoll) return;
        if (Interlocked.Exchange(ref _bluetoothBusy, 1) == 1) return;
        try
        {
            _nextBluetoothPoll = DateTime.UtcNow + BluetoothPollInterval;
            var status = BluetoothBattery.Read();
            if (status == Bluetooth) return;
            if (status.Connected != Bluetooth.Connected)
                log.Info(status.Connected
                    ? Lang.T($"Bluetooth: K3PRO 5.0 đang kết nối ({status.Address}), pin {status.BatteryPercent?.ToString() ?? "?"}%",
                        $"Bluetooth: K3PRO 5.0 connected ({status.Address}), battery {status.BatteryPercent?.ToString() ?? "?"}%")
                    : Lang.T("Bluetooth: K3PRO 5.0 đã ngắt kết nối.", "Bluetooth: K3PRO 5.0 disconnected."));
            Bluetooth = status;
            BluetoothChanged?.Invoke(status);
        }
        finally
        {
            Volatile.Write(ref _bluetoothBusy, 0);
        }
    }

    private void ScheduleNextPoll() =>
        _nextPoll = DateTime.UtcNow + (State is { IsConnected: true, Kind: ConnectionKind.Wireless } ? LinkPollInterval : WakePollInterval);

    private async Task ProbeAsync(bool verbose)
    {
        try
        {
            var wired = await Task.Run(() => HidSharpTransport.FindCandidates().Select(d => d.DevicePath).ToList());
            if (wired.Count > 1)
            {
                Disconnect(Lang.T($"{wired.Count} interface khớp — không chắc chọn đúng.", $"{wired.Count} matching interfaces — cannot tell which one is right."), LogKind.Error);
                return;
            }
            if (wired.Count == 1)
            {
                if (State is { IsConnected: true, Kind: ConnectionKind.Wired } && State.DevicePath == wired[0]) return;
                var (path, info) = await Task.Run(() =>
                {
                    using var c = new K3ProDevice(new LoggingTransport(HidSharpTransport.Open(), log.Add));
                    return (c.Description, c.ReadDeviceInfo());
                });
                Connect(ConnectionKind.Wired, path, info);
                return;
            }

            if (HidSharpReceiverTransport.FindCandidates().Count == 0)
            {
                Disconnect(null, LogKind.Info);
                return;
            }

            bool alreadyWireless = State is { IsConnected: true, Kind: ConnectionKind.Wireless };
            var probe = await Task.Run(() => ProbeReceiver(verbose, readInfo: !alreadyWireless));
            if (probe.Linked)
            {
                if (!alreadyWireless) Connect(ConnectionKind.Wireless, probe.Path, probe.Info!);
                return;
            }
            Disconnect(SleepingMessage, LogKind.Warning);
        }
        catch (Exception ex)
        {
            Disconnect(ex.Message, LogKind.Info);
        }
    }

    private (bool Linked, string Path, DeviceInfo? Info) ProbeReceiver(bool verbose, bool readInfo)
    {
        Action<TransferRecord> sink = verbose ? log.Add : log.AddFileOnly;
        using var device = new K3ProReceiverDevice(new LoggingReceiverTransport(HidSharpReceiverTransport.Open(), sink));
        if (device.TryReadStatus() is not { NumpadLinked: true }) return (false, device.Description, null);
        if (!readInfo) return (true, device.Description, State.Info);
        return device.TryReadDeviceInfo() is { } info ? (true, device.Description, info) : (false, device.Description, null);
    }

    private void Connect(ConnectionKind kind, string path, DeviceInfo info)
    {
        log.Info(kind == ConnectionKind.Wired ? Lang.T($"Kết nối có dây: {path}", $"Connected by cable: {path}") : Lang.T($"Kết nối 2.4G qua receiver: {path}", $"Connected via 2.4G receiver: {path}"));
        log.Info($"Info → {info} {(info.MatchesCapture ? Lang.T("✅ khớp capture", "✅ matches capture") : Lang.T("⚠ KHÁC capture", "⚠ DIFFERS from capture"))}");
        if (K3ProConnections.IsVendorAppRunning()) log.Warning(K3ProConnections.VendorAppWarning);
        SetState(new(true, path, info, null, kind));
    }

    /// <summary>Only logs + updates the state on an actual change (so periodic probing doesn't flood the log).</summary>
    private void Disconnect(string? error, LogKind kind)
    {
        if (!State.IsConnected && State.Error == error) return;
        if (State.IsConnected) log.Warning(Lang.T("Mất kết nối thiết bị.", "Device disconnected."));
        if (error is not null) log.Add(kind, Lang.T($"Chưa kết nối: {error}", $"Not connected: {error}"));
        SetState(ConnectionState.Disconnected with { Error = error });
    }

    public Task<Settings> ReadSettingsAsync() => RunAsync(d => d.ReadSettings());

    public Task<byte[]> ReadSettingsRawAsync() => RunAsync(d => d.ReadSettingsRaw());

    public Task ExecuteAsync(WritePlan plan, ConnectionKind expectedKind) =>
        RunAsync(d =>
        {
            if (d.Kind != expectedKind)
                throw new InvalidOperationException(Lang.T("Kết nối đã đổi (dây ↔ 2.4G) từ lúc bấm Apply — hủy, hãy Apply lại.",
                    "The connection changed (cable ↔ 2.4G) since Apply was pressed — cancelled, please Apply again."));
            d.Execute(plan, w => log.Info(Lang.T($"Đã gửi: {w.Title}", $"Sent: {w.Title}")));
            return true;
        });

    public void Dispose()
    {
        if (_started) DeviceList.Local.Changed -= OnDeviceListChanged;
        _debounce?.Cancel();
        _pollTimer?.Dispose();
    }

    private async Task<T> RunAsync<T>(Func<IK3ProConnection, T> operation)
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                using var device = Open();
                return operation(device);
            });
        }
        finally
        {
            _gate.Release();
        }
    }

    private IK3ProConnection Open() => K3ProConnections.Open(log.Add);

    private void SetState(ConnectionState state)
    {
        State = state;
        ConnectionChanged?.Invoke(state);
    }

    private void OnDeviceListChanged(object? sender, DeviceListChangedEventArgs e)
    {
        _debounce?.Cancel();
        var cts = _debounce = new CancellationTokenSource();
        _ = Task.Delay(Debounce, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            PollBluetooth(force: true);
            _ = RefreshAsync();
        }, TaskScheduler.Default);
    }
}
