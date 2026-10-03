using Avalonia;
using Avalonia.Headless;
using K3Pro.App.Services;
using K3Pro.App.ViewModels;
using K3Pro.Protocol;

[assembly: AvaloniaTestApplication(typeof(K3Pro.App.Tests.TestAppBuilder))]
// Lang.Current is global state (one language for the whole app) → tests don't run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace K3Pro.App.Tests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>Fake device: reads return the capture 05 settings block, writes only record the plan. Never touches HID.</summary>
internal sealed class FakeDevice : IDeviceService
{
    public event Action<ConnectionState>? ConnectionChanged;

    public ConnectionState State { get; set; } = new(true,
        @"\\?\hid#vid_258a&pid_010c&mi_01&col06#9&28edd87a&0&0005#{4d1e55b2-f16f-11cf-88cb-001111000030}",
        DeviceInfo.Parse([0x03, 0x00, 0x00, 0x00, 0x00, 0x17]), null, ConnectionKind.Wired);

    public Settings Settings { get; set; } = CaptureBaseline.ReferenceSettings();
    public List<WritePlan> Executed { get; } = [];
    public int Reads { get; private set; }

    public void Start() { }
    public Task RefreshAsync() => Task.CompletedTask;

    public Task<Settings> ReadSettingsAsync()
    {
        Reads++;
        return Task.FromResult(Settings);
    }

    public Task<byte[]> ReadSettingsRawAsync()
    {
        Reads++;
        return Task.FromResult(Settings.Data.ToArray());
    }

    public List<ConnectionKind> ExecutedKinds { get; } = [];

    public Task ExecuteAsync(WritePlan plan, ConnectionKind expectedKind)
    {
        Executed.Add(plan);
        ExecutedKinds.Add(expectedKind);
        return Task.CompletedTask;
    }

    public void Raise(ConnectionState s)
    {
        State = s;
        ConnectionChanged?.Invoke(s);
    }

    public void Dispose() { }
}

/// <summary>A set of fake services + a private temp directory per test.</summary>
internal sealed class TestHarness : IDisposable
{
    public TestHarness()
    {
        Dir = Path.Combine(Path.GetTempPath(), "k3pro-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        SettingsStore = new AppSettingsStore(Path.Combine(Dir, "app-settings.json"));
        KeymapStore = new KeymapStateStore(Path.Combine(Dir, "keymap-state.json"));
        Log.Added += Entries.Add;
    }

    public string Dir { get; }
    public FakeDevice Device { get; } = new();
    public PacketLog Log { get; } = new(null);
    public List<LogEntry> Entries { get; } = [];
    public AppSettingsStore SettingsStore { get; }
    public KeymapStateStore KeymapStore { get; }

    public static string LayoutPath => Path.Combine(AppContext.BaseDirectory, "layout.json");

    public List<(string Message, bool IsError)> Notifications { get; } = [];

    /// <param name="systemLanguage">Fake "OS" language — defaults to Vi so tests don't depend on the machine they run on.</param>
    public MainWindowViewModel CreateViewModel(UiLanguage systemLanguage = UiLanguage.Vi)
    {
        var vm = new MainWindowViewModel(new AppServices(Device, Log, SettingsStore, KeymapStore,
            LayoutConfig.Load(LayoutPath), null, LayoutPath, Dir, Path.Combine(Dir, "logs"), systemLanguage));
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Notification) && vm.Notification is { } n)
                Notifications.Add((n, vm.NotificationIsError));
        };
        return vm;
    }

    public void Dispose()
    {
        Lang.Current = UiLanguage.Vi; // the next test always starts in Vietnamese
        try { Directory.Delete(Dir, recursive: true); } catch (IOException) { }
    }
}
