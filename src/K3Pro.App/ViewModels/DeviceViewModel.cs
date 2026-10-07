using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using K3Pro.App.Services;
using K3Pro.Protocol;
using static K3Pro.Protocol.Lang;

namespace K3Pro.App.ViewModels;

/// <summary>Device tab: connection, sleep, settings block (read-only), app data paths.</summary>
public partial class DeviceViewModel : ObservableObject
{
    private readonly IDeviceService _device;
    private readonly WriteCoordinator _writer;
    private readonly PacketLog _log;

    public DeviceViewModel(SessionViewModel session, IDeviceService device, WriteCoordinator writer, PacketLog log,
        string keymapStatePath, string layoutPath, string dataDir, UpdatesViewModel? updates = null)
    {
        Session = session;
        Updates = updates;
        _device = device;
        _writer = writer;
        _log = log;
        SleepStopIndex = SleepTimes.NearestStopIndex(10);
        KeymapStatePath = keymapStatePath;
        LayoutPath = layoutPath;
        DataDir = dataDir;
        RenderSettings();
    }

    // Latest 0x84 read (null = not read yet) and the sleep value currently on the device — display text is rebuilt on language switch.
    private byte[]? _raw;
    private byte? _deviceSleep;

    public SessionViewModel Session { get; }
    public UpdatesViewModel? Updates { get; }
    public string KeymapStatePath { get; }
    public string LayoutPath { get; }
    public string DataDir { get; }
    public string DelayText => T("device.ms_between_packets", K3ProConstants.DefaultInterPacketDelay.TotalMilliseconds);

    [ObservableProperty]
    public partial string SettingsDump { get; private set; } = "";

    [ObservableProperty]
    public partial string FieldsText { get; private set; } = "";

    [ObservableProperty]
    public partial string ReferenceText { get; private set; } = "";

    /// <summary>Sleep slider as the vendor app's step index (0 … 9 = 30 s … 20 Min).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SleepText), nameof(SleepUnits))]
    public partial int SleepStopIndex { get; set; }

    /// <summary>Byte 0x18 value (unit 30 s) of the selected step; setting a value that isn't a step → nearest step.</summary>
    public int SleepUnits
    {
        get => SleepTimes.VendorStops[Math.Clamp(SleepStopIndex, 0, SleepMax)];
        set => SleepStopIndex = SleepTimes.NearestStopIndex(value);
    }

    public string DeviceSleepText => _deviceSleep is { } u ? $"0x{u:X2} = {SleepTimes.Describe(u)}" : T("device.not_read");

    public string DeviceSleepLine => T("device.device_offset_0x18", DeviceSleepText);

    /// <summary>Language switch: rebuilds all text from the data already read.</summary>
    public void RefreshLanguage()
    {
        RenderSettings();
        OnPropertyChanged(string.Empty);
    }

    public int SleepMin => 0;
    public int SleepMax => SleepTimes.VendorStops.Count - 1;
    public string SleepText => SleepTimes.Describe((byte)SleepUnits);

    [RelayCommand]
    private async Task ApplySleepAsync()
    {
        Settings current;
        try
        {
            current = await _device.ReadSettingsAsync();
        }
        catch (Exception ex)
        {
            _log.Error(T("device.reading_0x84_failed", ex.Message));
            return;
        }

        ShowSleep(current);
        WritePlan plan;
        try
        {
            plan = WritePlanner.SleepTime(current, (byte)SleepUnits);
        }
        catch (UnsafeCommandException ex)
        {
            _log.Error(ex.Message);
            return;
        }

        if (await _writer.ApplyAsync(plan) == ApplyResult.Applied) await ReadSettingsAsync();
    }

    private void ShowSleep(Settings s)
    {
        _deviceSleep = s.SleepUnits;
        OnPropertyChanged(nameof(DeviceSleepText));
        OnPropertyChanged(nameof(DeviceSleepLine));
    }

    [RelayCommand]
    private async Task ReadSettingsAsync()
    {
        byte[] raw;
        try
        {
            raw = await _device.ReadSettingsRawAsync();
        }
        catch (Exception ex)
        {
            _log.Error(T("device.reading_0x84_failed", ex.Message));
            return;
        }

        ShowSettings(raw);
    }

    /// <summary>Shows a settings block read from the device (also used by the automatic read on connect).</summary>
    public void ShowSettings(byte[] raw)
    {
        _raw = raw;
        if (RenderSettings() is { } s)
        {
            ShowSleep(s);
            SleepUnits = s.SleepUnits; // slider jumps to the value stored on the numpad
        }
    }

    /// <summary>Dumps + decodes the block read, in the current language; returns the block if valid.</summary>
    private Settings? RenderSettings()
    {
        if (_raw is not { } raw)
        {
            SettingsDump = T("device.not_read_press_read_0x84");
            FieldsText = "";
            ReferenceText = "";
            return null;
        }

        SettingsDump = Hex.Dump(raw, "");
        try
        {
            var s = Settings.Parse(raw);
            FieldsText = $"0x{Settings.LightingModeOffset:X2}  LightingMode = 0x{s.LightingMode:X2} ({LightingModes.Describe(s.LightingMode)})\n" +
                         $"0x{Settings.SleepOffset:X2}  Sleep = 0x{s.SleepUnits:X2} ({SleepTimes.Describe(s.SleepUnits)}, {T("device.2_4g_mode")})\n" +
                         $"0x{Settings.MagicOffset:X2}  magic = {Hex.Format(raw.AsSpan(Settings.MagicOffset))} ✅\n" +
                         T("device.other_bytes_meaning_unknown_kept");
            var reference = CaptureBaseline.ReferenceSettings();
            var diff = Hex.DiffRanges(reference.Data, s.Data);
            ReferenceText = diff.Count == 0
                ? T("device.identical_block_read", CaptureBaseline.SettingsSource)
                : T("device.differs_from_at", CaptureBaseline.SettingsSource) + string.Join(", ", diff.Select(r =>
                    $"0x{r.Start:X2} ({Hex.Format(reference.Data[r.Start..r.End])} → {Hex.Format(s.Data[r.Start..r.End])})"));
            return s;
        }
        catch (InvalidDataException ex)
        {
            FieldsText = $"⚠ {ex.Message}";
            ReferenceText = "";
            return null;
        }
    }

    [RelayCommand]
    private Task ReconnectAsync() => _device.RefreshAsync();

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            Process.Start(new ProcessStartInfo(DataDir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error(T("device.could_not_open", DataDir, ex.Message));
        }
    }
}
