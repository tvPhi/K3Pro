using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using K3Pro.App.Services;
using static K3Pro.Protocol.Lang;

namespace K3Pro.App.ViewModels;

/// <summary>
/// "Is there a newer release?" — reads GitHub Releases (<see cref="IUpdateChecker"/>), shows a top-bar button when one exists.
/// Auto-check on startup can be turned off (app-settings.json); dev builds (0.0.0-dev) never auto-check.
/// </summary>
public partial class UpdatesViewModel : ObservableObject
{
    private enum Phase { Idle, Checking, UpToDate, Available, Failed }

    private readonly IUpdateChecker? _checker;
    private readonly PacketLog _log;
    private readonly Action<bool> _saveAutoCheck;
    private readonly bool _ready;
    private Phase _phase = Phase.Idle;
    private string? _error;
    private (SemVer Version, ReleaseInfo Release)? _newer;

    public UpdatesViewModel(IUpdateChecker? checker, PacketLog log, string currentVersion, bool autoCheck, Action<bool> saveAutoCheck)
    {
        _checker = checker;
        _log = log;
        _saveAutoCheck = saveAutoCheck;
        CurrentVersion = currentVersion;
        AutoCheck = autoCheck;
        _ready = true;
    }

    public string CurrentVersion { get; }

    public string CurrentVersionText => T($"Phiên bản đang dùng: {CurrentVersion}", $"Current version: {CurrentVersion}");

    /// <summary>Check GitHub for a newer release every time the app starts.</summary>
    [ObservableProperty]
    public partial bool AutoCheck { get; set; }

    partial void OnAutoCheckChanged(bool value)
    {
        if (_ready) _saveAutoCheck(value);
    }

    public bool IsUpdateAvailable => _phase == Phase.Available;

    public bool CanCheck => _checker is not null && _phase != Phase.Checking;

    public string? NewVersion => _newer?.Version.ToString();

    public string UpdateButtonText => T($"⬆ Bản mới {NewVersion}", $"⬆ Update {NewVersion}");

    public string UpdateButtonTip => T("Mở trang tải bản mới trên GitHub", "Open the download page on GitHub");

    public string StatusText => _phase switch
    {
        Phase.Checking => T("Đang kiểm tra…", "Checking…"),
        Phase.UpToDate => T("Đang dùng bản mới nhất.", "You're up to date."),
        Phase.Available => T($"Có bản mới {NewVersion} — bấm để tải.", $"Version {NewVersion} is available — click to download."),
        Phase.Failed => T($"Không kiểm tra được: {_error}", $"Couldn't check: {_error}"),
        _ => _checker is null ? T("Kiểm tra cập nhật bị tắt trong bản này.", "Update checks are disabled in this build.") : "",
    };

    /// <summary>Startup: auto-check unless turned off or running a local dev build. The task is exposed for tests.</summary>
    public Task StartupCheckAsync() =>
        AutoCheck && _checker is not null && !(SemVer.TryParse(CurrentVersion, out var v) && v.Prerelease == "dev")
            ? CheckAsync()
            : Task.CompletedTask;

    [RelayCommand]
    private async Task CheckAsync()
    {
        if (_checker is null || _phase == Phase.Checking) return;
        SetPhase(Phase.Checking);
        try
        {
            var releases = await _checker.GetReleasesAsync(CancellationToken.None);
            _newer = UpdatePolicy.PickNewer(CurrentVersion, releases);
            SetPhase(_newer is null ? Phase.UpToDate : Phase.Available);
            if (_newer is { } n)
                _log.Info(T($"Có bản mới {n.Version}: {n.Release.Url}", $"New version {n.Version} available: {n.Release.Url}"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Xml.XmlException or IOException)
        {
            _error = ex.Message;
            SetPhase(Phase.Failed);
        }
    }

    [RelayCommand]
    private void OpenRelease()
    {
        var url = _newer?.Release.Url ?? GitHubUpdateChecker.ReleasesPage;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _log.Error(T($"Không mở được {url}: {ex.Message}", $"Could not open {url}: {ex.Message}"));
        }
    }

    public void RefreshLanguage() => OnPropertyChanged(string.Empty);

    private void SetPhase(Phase phase)
    {
        _phase = phase;
        OnPropertyChanged(nameof(IsUpdateAvailable));
        OnPropertyChanged(nameof(CanCheck));
        OnPropertyChanged(nameof(NewVersion));
        OnPropertyChanged(nameof(UpdateButtonText));
        OnPropertyChanged(nameof(StatusText));
    }
}
