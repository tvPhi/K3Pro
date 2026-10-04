using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using K3Pro.App.Localization;
using K3Pro.App.Services;
using K3Pro.Protocol;
using static K3Pro.Protocol.Lang;

namespace K3Pro.App.ViewModels;

public sealed record AppServices(
    IDeviceService Device,
    PacketLog Log,
    AppSettingsStore SettingsStore,
    KeymapStateStore KeymapStore,
    LayoutConfig? Layout,
    string? LayoutError,
    string LayoutPath,
    string DataDir,
    string LogDir,
    UiLanguage SystemLanguage = UiLanguage.Vi,
    IUpdateChecker? Updates = null,
    string? CurrentVersion = null);

public partial class MainWindowViewModel : ObservableObject
{
    private readonly AppSettingsStore _settings;
    private readonly AppSettings _appSettings;
    private readonly PacketLog _log;
    private readonly bool _ready;

    public MainWindowViewModel(AppServices s)
    {
        _settings = s.SettingsStore;
        _log = s.Log;
        // Before building child VMs: right language from the start. Never chosen → follow the OS (vi → Vietnamese, else → English).
        _appSettings = s.SettingsStore.Load();
        Lang.Current = _appSettings.ResolveLanguage(s.SystemLanguage);
        SelectedLanguage = LanguageOption.Of(Lang.Current);
        Log = new LogViewModel(s.Log, s.LogDir); // created first so no log entries from the other VMs are lost
        Session = new SessionViewModel();
        Updates = new UpdatesViewModel(s.Updates, s.Log, s.CurrentVersion ?? AppVersion.Current, _appSettings.CheckUpdates ?? true, value =>
        {
            _appSettings.CheckUpdates = value;
            SaveSettings();
        });
        var writer = new WriteCoordinator(s.Device, s.Log, Notify);
        Keymap = new KeymapViewModel(s.Layout, s.LayoutError, s.KeymapStore, writer, s.Log);
        Lighting = new LightingViewModel(s.Device, writer, s.Log);
        Device = new DeviceViewModel(Session, s.Device, writer, s.Log, s.KeymapStore.Path, s.LayoutPath, s.DataDir, Updates);

        if (s.LayoutError is not null) s.Log.Error(s.LayoutError);
        s.Device.ConnectionChanged += state => Ui.Post(() => Session.Apply(state));
        Session.Apply(s.Device.State);
        s.Device.BluetoothChanged += status => Ui.Post(() => Session.Apply(status));
        Session.Apply(s.Device.Bluetooth);
        _ready = true;
        StartupUpdateCheck = Updates.StartupCheckAsync();
    }

    /// <summary>The startup update check (completes immediately when disabled) — awaited by tests.</summary>
    public Task StartupUpdateCheck { get; }

    private void SaveSettings()
    {
        try
        {
            _settings.Save(_appSettings);
        }
        catch (IOException ex)
        {
            _log.Error(T($"Không lưu được {_settings.Path}: {ex.Message}", $"Could not save {_settings.Path}: {ex.Message}"));
        }
    }

    public IReadOnlyList<LanguageOption> Languages => LanguageOption.All;

    /// <summary>Language switch: takes effect immediately (every tab + XAML via <see cref="Tr"/>), saved to app-settings.json.</summary>
    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (!_ready || value is null) return;
        Lang.Current = value.Language;
        _appSettings.Language = Lang.Code(value.Language);
        SaveSettings();
        Session.RefreshLanguage();
        Updates.RefreshLanguage();
        Keymap.RefreshLanguage();
        Lighting.RefreshLanguage();
        Device.RefreshLanguage();
        Log.RefreshLanguage();
    }

    public SessionViewModel Session { get; }
    public UpdatesViewModel Updates { get; }
    public KeymapViewModel Keymap { get; }
    public LightingViewModel Lighting { get; }
    public DeviceViewModel Device { get; }
    public LogViewModel Log { get; }

    [ObservableProperty]
    public partial int SelectedTab { get; set; }

    /// <summary>Notification after Apply ("Saved …" / error), hides itself after a few seconds.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNotification))]
    public partial string? Notification { get; set; }

    [ObservableProperty]
    public partial bool NotificationIsError { get; set; }

    public bool HasNotification => Notification is not null;

    private DispatcherTimer? _notificationTimer;

    public void Notify(string message, bool isError) => Ui.Post(() =>
    {
        NotificationIsError = isError; // before Notification: the UI has the right color as soon as the text appears
        Notification = message;
        _notificationTimer?.Stop();
        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(isError ? 8 : 4) };
        _notificationTimer.Tick += (_, _) =>
        {
            _notificationTimer?.Stop();
            Notification = null;
        };
        _notificationTimer.Start();
    });
}
