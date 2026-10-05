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

    public string CurrentVersionText => T("updates.current_version", CurrentVersion);

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

    public string UpdateButtonText => T("updates.update", NewVersion);

    public string UpdateButtonTip => T("updates.open_download_page_github");

    public string StatusText => _phase switch
    {
        Phase.Checking => T("updates.checking"),
        Phase.UpToDate => T("updates.you_re_up_date"),
        Phase.Available => T("updates.version_available_click_download", NewVersion),
        Phase.Failed => T("updates.couldn_t_check", _error),
        _ => _checker is null ? T("updates.update_checks_are_disabled_this") : "",
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
                _log.Info(T("updates.new_version_available", n.Version, n.Release.Url));
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
            _log.Error(T("device.could_not_open", url, ex.Message));
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
