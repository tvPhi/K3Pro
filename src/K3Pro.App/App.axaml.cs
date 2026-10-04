using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using K3Pro.App.Services;
using K3Pro.App.ViewModels;
using K3Pro.App.Views;

namespace K3Pro.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Headless tests have no desktop lifetime → don't build the real services (never touch HID).
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var log = new PacketLog(AppPaths.LogDir);
            var device = new HidDeviceService(log);

            // Apply the saved language before reading the layout (so error messages use the right language)
            var systemLanguage = K3Pro.Protocol.Lang.FromCulture(System.Globalization.CultureInfo.CurrentUICulture);
            K3Pro.Protocol.Lang.Current = new AppSettingsStore(AppPaths.SettingsFile).Load().ResolveLanguage(systemLanguage);
            LayoutConfig? layout = null;
            string? layoutError = null;
            try
            {
                layout = LayoutConfig.Load(AppPaths.LayoutFile);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
            {
                layoutError = K3Pro.Protocol.Lang.T($"Không đọc được {AppPaths.LayoutFile}: {ex.Message}", $"Could not read {AppPaths.LayoutFile}: {ex.Message}");
            }

            var vm = new MainWindowViewModel(new AppServices(
                device, log,
                new AppSettingsStore(AppPaths.SettingsFile), new KeymapStateStore(AppPaths.KeymapStateFile),
                layout, layoutError, AppPaths.LayoutFile, AppPaths.DataDir, AppPaths.LogDir, systemLanguage, new GitHubUpdateChecker()));

            desktop.MainWindow = new MainWindow { DataContext = vm };
            desktop.ShutdownRequested += (_, _) => device.Dispose();
            device.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
