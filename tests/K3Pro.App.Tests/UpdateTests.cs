using Avalonia.Headless.XUnit;
using K3Pro.App.Services;
using K3Pro.Protocol;

namespace K3Pro.App.Tests;

/// <summary>Fake release source — no network in tests.</summary>
internal sealed class FakeUpdates(params ReleaseInfo[] releases) : IUpdateChecker
{
    public int Calls { get; private set; }
    public Exception? Throw { get; set; }

    public Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(CancellationToken ct)
    {
        Calls++;
        return Throw is { } ex ? Task.FromException<IReadOnlyList<ReleaseInfo>>(ex) : Task.FromResult<IReadOnlyList<ReleaseInfo>>(releases);
    }
}

public class UpdateTests
{
    private static ReleaseInfo Release(string tag, bool pre = false, bool draft = false) => new(tag, $"https://example/{tag}", pre, draft);

    [Theory]
    [InlineData("0.1.0-beta.2", "0.1.0-beta.10")]
    [InlineData("0.1.0-beta.10", "0.1.0-rc.1")]
    [InlineData("0.1.0-rc.1", "0.1.0")]
    [InlineData("0.1.0", "0.1.1")]
    [InlineData("0.9.9", "0.10.0")]
    [InlineData("v1.2.3-beta", "1.2.3-beta.1")]
    public void Semver_ordering(string lower, string higher)
    {
        Assert.True(SemVer.TryParse(lower, out var a));
        Assert.True(SemVer.TryParse(higher, out var b));
        Assert.True(a < b, $"{lower} < {higher}");
        Assert.True(b > a);
    }

    [Fact]
    public void Semver_parse_ignores_v_prefix_and_build_metadata()
    {
        Assert.True(SemVer.TryParse("v0.1.0-beta.2+5d1ad30", out var v));
        Assert.Equal("0.1.0-beta.2", v.ToString());
        Assert.False(SemVer.TryParse("latest", out _));
        Assert.False(SemVer.TryParse("1.2", out _));
    }

    [Fact]
    public void Beta_users_get_betas_stable_users_only_stable()
    {
        ReleaseInfo[] releases = [Release("v0.1.0-beta.3", pre: true), Release("v0.1.0-beta.2", pre: true), Release("v0.2.0", draft: true)];

        Assert.Equal("0.1.0-beta.3", UpdatePolicy.PickNewer("0.1.0-beta.2", releases)?.Version.ToString());
        Assert.Null(UpdatePolicy.PickNewer("0.1.0-beta.3", releases));          // already newest
        Assert.Null(UpdatePolicy.PickNewer("0.1.0", releases));                 // stable user: pre-releases and drafts ignored
        Assert.Equal("0.1.1", UpdatePolicy.PickNewer("0.1.0", [Release("v0.1.1"), Release("v0.2.0-beta.1", pre: true)])?.Version.ToString());
        Assert.Null(UpdatePolicy.PickNewer("not-a-version", releases));
    }

    [Fact]
    public void Parses_github_releases_atom_feed()
    {
        const string feed = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns="http://www.w3.org/2005/Atom" xml:lang="en-US">
              <id>tag:github.com,2008:https://github.com/tvPhi/K3Pro/releases</id>
              <link type="text/html" rel="alternate" href="https://github.com/tvPhi/K3Pro/releases"/>
              <entry>
                <id>tag:github.com,2008:Repository/1/v0.2.0</id>
                <link rel="alternate" type="text/html" href="https://github.com/tvPhi/K3Pro/releases/tag/v0.2.0"/>
                <title>K3Pro 0.2.0</title>
              </entry>
              <entry>
                <id>tag:github.com,2008:Repository/1/v0.1.0-beta.2</id>
                <link rel="alternate" type="text/html" href="https://github.com/tvPhi/K3Pro/releases/tag/v0.1.0-beta.2"/>
                <title>K3Pro 0.1.0 beta 2</title>
              </entry>
            </feed>
            """;

        var releases = GitHubUpdateChecker.ParseAtom(feed);

        Assert.Equal(["v0.2.0", "v0.1.0-beta.2"], releases.Select(r => r.Tag));
        Assert.Equal([false, true], releases.Select(r => r.Prerelease));
        Assert.Equal("https://github.com/tvPhi/K3Pro/releases/tag/v0.2.0", releases[0].Url);
    }

    [AvaloniaFact]
    public async Task Startup_check_shows_newer_release_and_the_setting_turns_it_off()
    {
        using var h = new TestHarness();
        var fake = new FakeUpdates(Release("v0.1.0-beta.3", pre: true), Release("v0.1.0-beta.2", pre: true));
        var vm = h.CreateViewModel(UiLanguage.En, fake, "0.1.0-beta.2");
        await vm.StartupUpdateCheck;

        Assert.True(vm.Updates.IsUpdateAvailable);
        Assert.Equal("⬆ Update 0.1.0-beta.3", vm.Updates.UpdateButtonText);
        Assert.Equal(1, fake.Calls);

        vm.Updates.AutoCheck = false;
        vm.SelectedLanguage = vm.Languages.Single(l => l.Language == UiLanguage.Vi); // both settings live in the same file
        var json = File.ReadAllText(h.SettingsStore.Path);
        Assert.Contains("\"checkUpdates\": false", json);
        Assert.Contains("\"vi\"", json);

        var next = h.CreateViewModel(UiLanguage.En, fake, "0.1.0-beta.2");
        await next.StartupUpdateCheck;
        Assert.Equal(1, fake.Calls); // auto-check off → no request
        Assert.False(next.Updates.IsUpdateAvailable);

        await next.Updates.CheckCommand.ExecuteAsync(null); // the manual button still works
        Assert.True(next.Updates.IsUpdateAvailable);
    }

    [AvaloniaFact]
    public async Task Dev_builds_never_auto_check_and_failures_are_reported()
    {
        using var h = new TestHarness();
        var fake = new FakeUpdates(Release("v0.1.0-beta.3", pre: true)) { Throw = new HttpRequestException("offline") };

        var dev = h.CreateViewModel(UiLanguage.En, fake, "0.0.0-dev");
        await dev.StartupUpdateCheck;
        Assert.Equal(0, fake.Calls);

        await dev.Updates.CheckCommand.ExecuteAsync(null);
        Assert.Equal("Couldn't check: offline", dev.Updates.StatusText);
        Assert.False(dev.Updates.IsUpdateAvailable);
    }
}
