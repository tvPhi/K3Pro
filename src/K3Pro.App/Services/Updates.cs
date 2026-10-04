using System.Net.Http.Headers;
using System.Reflection;
using System.Xml.Linq;

namespace K3Pro.App.Services;

/// <summary>Semantic version <c>MAJOR.MINOR.PATCH[-prerelease]</c> (build metadata after '+' is ignored), ordered per semver.org §11.</summary>
public readonly record struct SemVer(int Major, int Minor, int Patch, string Prerelease) : IComparable<SemVer>
{
    public bool IsPrerelease => Prerelease.Length > 0;

    /// <summary>Accepts an optional leading 'v' and '+metadata' (e.g. "v0.1.0-beta.2", "0.1.0-beta.2+5d1ad30").</summary>
    public static bool TryParse(string? text, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimStart('v', 'V');
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        var dash = s.IndexOf('-');
        var core = dash >= 0 ? s[..dash] : s;
        var pre = dash >= 0 ? s[(dash + 1)..] : "";
        var parts = core.Split('.');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor) ||
            !int.TryParse(parts[2], out var patch) || (dash >= 0 && pre.Length == 0))
            return false;
        version = new(major, minor, patch, pre);
        return true;
    }

    public int CompareTo(SemVer other)
    {
        int c = Major.CompareTo(other.Major);
        if (c == 0) c = Minor.CompareTo(other.Minor);
        if (c == 0) c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;
        if (IsPrerelease != other.IsPrerelease) return IsPrerelease ? -1 : 1; // 1.0.0-beta < 1.0.0
        var a = Prerelease.Split('.');
        var b = other.Prerelease.Split('.');
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++)
        {
            bool an = int.TryParse(a[i], out var ai), bn = int.TryParse(b[i], out var bi);
            c = an && bn ? ai.CompareTo(bi) : an ? -1 : bn ? 1 : string.CompareOrdinal(a[i], b[i]);
            if (c != 0) return c;
        }
        return a.Length.CompareTo(b.Length);
    }

    public static bool operator >(SemVer l, SemVer r) => l.CompareTo(r) > 0;
    public static bool operator <(SemVer l, SemVer r) => l.CompareTo(r) < 0;

    public override string ToString() => IsPrerelease ? $"{Major}.{Minor}.{Patch}-{Prerelease}" : $"{Major}.{Minor}.{Patch}";
}

public sealed record ReleaseInfo(string Tag, string Url, bool Prerelease, bool Draft);

public static class AppVersion
{
    /// <summary>Version stamped by the release workflow (<c>-p:Version=…</c>); local builds are <c>0.0.0-dev</c> (Directory.Build.props).</summary>
    public static string Current { get; } = Clean(typeof(AppVersion).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0-dev");

    public static bool IsDevBuild => SemVer.TryParse(Current, out var v) && v.Prerelease == "dev";

    private static string Clean(string informational) => informational.Split('+')[0];
}

public static class UpdatePolicy
{
    /// <summary>
    /// Newest published release that is newer than <paramref name="current"/>; null if none (or the current version can't be parsed).
    /// Pre-releases are only offered to users already running a pre-release (beta testers); stable users only see stable releases.
    /// </summary>
    public static (SemVer Version, ReleaseInfo Release)? PickNewer(string current, IEnumerable<ReleaseInfo> releases)
    {
        if (!SemVer.TryParse(current, out var cur)) return null;
        (SemVer Version, ReleaseInfo Release)? best = null;
        foreach (var r in releases)
        {
            if (r.Draft || (r.Prerelease && !cur.IsPrerelease) || !SemVer.TryParse(r.Tag, out var v)) continue;
            if (v > cur && (best is null || v > best.Value.Version)) best = (v, r);
        }
        return best;
    }
}

/// <summary>Source of published releases (GitHub in the app, a fake in tests).</summary>
public interface IUpdateChecker
{
    Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(CancellationToken ct);
}

/// <summary>
/// Reads the public releases Atom feed (https://github.com/tvPhi/K3Pro/releases.atom) — one anonymous GET, nothing about the user or
/// device is sent. The feed isn't subject to the REST API's 60-requests-per-hour limit (shared / VPN IPs hit it quickly) and lists
/// published releases only (no drafts). Pre-releases are recognised by their semver label (e.g. <c>v0.1.0-beta.2</c>).
/// </summary>
public sealed class GitHubUpdateChecker : IUpdateChecker
{
    public const string Repository = "tvPhi/K3Pro";
    public const string ReleasesPage = $"https://github.com/{Repository}/releases";
    private const string Feed = $"{ReleasesPage}.atom";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("K3Pro", AppVersion.Current));
        return http;
    }

    public async Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(CancellationToken ct) =>
        ParseAtom(await Http.GetStringAsync(Feed, ct));

    /// <summary>Each <c>entry/link[@rel=alternate]</c> points to <c>…/releases/tag/&lt;tag&gt;</c>.</summary>
    public static IReadOnlyList<ReleaseInfo> ParseAtom(string xml)
    {
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var doc = XDocument.Parse(xml);
        return doc.Root?.Elements(atom + "entry")
            .Select(e => e.Elements(atom + "link").FirstOrDefault(l => (string?)l.Attribute("rel") is null or "alternate")?.Attribute("href")?.Value)
            .OfType<string>()
            .Where(href => href.Contains("/releases/tag/", StringComparison.Ordinal))
            .Select(href =>
            {
                var tag = Uri.UnescapeDataString(href[(href.LastIndexOf('/') + 1)..]);
                return new ReleaseInfo(tag, href, SemVer.TryParse(tag, out var v) && v.IsPrerelease, Draft: false);
            })
            .ToList() ?? [];
    }
}
