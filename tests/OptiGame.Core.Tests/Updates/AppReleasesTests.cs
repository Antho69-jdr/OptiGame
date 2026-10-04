using System.Text.Json;
using OptiGame.Core.Updates;

namespace OptiGame.Core.Tests.Updates;

public sealed class AppReleasesTests
{
    private const string RealDigest = "6ab83f84d04f962a160470453e431be569e1e3aba1e7e21037716b26fb19ad68";

    /// <summary>Vraie réponse de l'API (2026-10-04) : le brouillon « v1.1.0 », qui contenait par erreur l'installeur 1.0.0.</summary>
    private static string RealDraft()
    {
        using var list = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Updates", "Samples", "github-releases-2026-10-04.json")));
        return list.RootElement[0].GetRawText();
    }

    /// <summary>La même, publiée : adresses avec le tag (au lieu de « untagged-… ») et, si demandé, l'installeur renommé.</summary>
    private static string Published(string installerVersion = "1.1.0") => RealDraft()
        .Replace("\"draft\":true", "\"draft\":false")
        .Replace("\"published_at\":null", "\"published_at\":\"2026-10-05T09:30:00Z\"")
        .Replace("untagged-3dbcd5450613df12d8fb", "v1.1.0")
        .Replace("OptiGame-Setup-1.0.0.exe", $"OptiGame-Setup-{installerVersion}.exe");

    [Fact]
    public void The_real_response_of_a_published_release_gives_its_installer_and_github_digest()
    {
        var result = AppReleases.Evaluate(Published(), new Version(1, 0, 0));

        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        var package = Assert.IsType<UpdatePackage>(result.Package);
        Assert.Equal(new Version(1, 1, 0), package.Version);
        Assert.Equal("v1.1.0", package.Tag);
        Assert.Equal("OptiGame-Setup-1.1.0.exe", package.InstallerName);
        Assert.Equal("https://github.com/Antho69-jdr/OptiGame/releases/download/v1.1.0/OptiGame-Setup-1.1.0.exe", package.InstallerUrl.ToString());
        Assert.Equal(48657360, package.InstallerSize);
        Assert.Equal(RealDigest, package.Sha256);
        Assert.Equal("https://github.com/Antho69-jdr/OptiGame/releases/tag/v1.1.0", package.PageUrl.ToString());
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 9, 30, 0, TimeSpan.Zero), package.PublishedAt);
        Assert.Equal("OptiGame 1.1.0 est disponible (vous avez la 1.0.0).", result.Message);
    }

    [Fact]
    public void A_draft_is_never_installed() =>
        Assert.Equal(UpdateCheckStatus.Unusable, AppReleases.Evaluate(RealDraft(), new Version(1, 0, 0)).Status);

    [Fact]
    public void A_release_whose_installer_has_another_version_is_refused()
    {
        // Le vrai incident : tag v1.1.0 posé sur le commit de la 1.0.0, installeur « OptiGame-Setup-1.0.0.exe ».
        var result = AppReleases.Evaluate(Published(installerVersion: "1.0.0"), new Version(1, 0, 0));

        Assert.Equal(UpdateCheckStatus.Unusable, result.Status);
        Assert.Null(result.Package);
    }

    [Theory]
    [InlineData("1.1.0")]
    [InlineData("1.2.0")]
    public void The_same_or_an_older_published_version_is_up_to_date(string current) =>
        Assert.Equal(UpdateCheckStatus.UpToDate, AppReleases.Evaluate(Published(), Version.Parse(current)).Status);

    [Theory]
    [InlineData("[]")]
    [InlineData("\"v9.0.0\"")]
    public void An_unexpected_response_is_ignored(string json) =>
        Assert.Equal(UpdateCheckStatus.Unusable, AppReleases.Evaluate(json, new Version(1, 0, 0)).Status);

    [Fact]
    public void No_published_release_is_up_to_date() =>
        Assert.Equal(UpdateCheckStatus.UpToDate, AppReleases.Evaluate(null, new Version(1, 2, 0)).Status);

    [Theory]
    [InlineData("\"digest\":\"sha256:" + RealDigest + "\"", "\"digest\":null")]
    [InlineData("\"digest\":\"sha256:" + RealDigest + "\"", "\"digest\":\"sha512:" + RealDigest + "\"")]
    [InlineData("\"state\":\"uploaded\"", "\"state\":\"starter\"")]
    [InlineData("\"size\":48657360", "\"size\":0")]
    [InlineData("\"size\":48657360", "\"size\":400000000")]
    [InlineData("\"size\":48657360", "\"size\":\"48657360\"")]
    [InlineData("\"size\":48657360", "\"size\":null")]
    [InlineData("https://github.com/Antho69-jdr/OptiGame/releases/download/", "https://example.com/Antho69-jdr/OptiGame/releases/download/")]
    [InlineData("https://github.com/Antho69-jdr/OptiGame/releases/download/", "http://github.com/Antho69-jdr/OptiGame/releases/download/")]
    [InlineData("https://github.com/Antho69-jdr/OptiGame/releases/download/", "https://github.com/someone/OptiGame/releases/download/")]
    [InlineData("\"tag_name\":\"v1.1.0\"", "\"tag_name\":\"latest\"")]
    public void An_installer_that_cannot_be_verified_is_refused(string original, string replacement)
    {
        var json = Published();
        Assert.Contains(original, json);

        var result = AppReleases.Evaluate(json.Replace(original, replacement), new Version(1, 0, 0));

        Assert.Equal(UpdateCheckStatus.Unusable, result.Status);
        Assert.Null(result.Package);
    }

    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("v10.0.3", "10.0.3")]
    [InlineData("1.2.0", null)]
    [InlineData("v1.2", null)]
    [InlineData("v1.2.0-beta", null)]
    [InlineData("untagged-3dbcd5450613df12d8fb", null)]
    [InlineData(null, null)]
    public void Only_version_tags_are_read(string? tag, string? expected) =>
        Assert.Equal(expected is null ? null : Version.Parse(expected), AppReleases.ParseTag(tag));

    [Theory]
    [InlineData("https://release-assets.githubusercontent.com/github-production-release-asset/1404259267/0b10a2ff-d25e-4431-ab87-665af1cc7b7f?sp=r", true)]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/1", true)]
    [InlineData("http://release-assets.githubusercontent.com/x", false)]
    [InlineData("https://release-assets.githubusercontent.com.example.com/x", false)]
    [InlineData("https://example.com/OptiGame-Setup-1.2.0.exe", false)]
    public void Downloads_only_end_on_githubs_file_servers(string url, bool allowed) =>
        Assert.Equal(allowed, AppReleases.IsAllowedDownload(new Uri(url)));

    [Fact]
    public void The_github_digest_is_read_in_lower_case()
    {
        Assert.Equal(RealDigest, AppReleases.Sha256Digest("sha256:" + RealDigest.ToUpperInvariant()));
        Assert.Null(AppReleases.Sha256Digest("sha256:" + RealDigest[..63]));
        Assert.Null(AppReleases.Sha256Digest(RealDigest));
    }

    [Theory]
    [InlineData("OptiGame-Setup-1.2.0.exe", true)]
    [InlineData("OptiGame-Setup-10.0.12.exe", true)]
    [InlineData("OptiGame-Setup-1.2.0.exe.part", false)]
    [InlineData("OptiGame-Setup-1.2.0.exe.non-verifie", false)]
    [InlineData("OptiGame-Setup-1.2.exe", false)]
    [InlineData("Other-Setup-1.2.0.exe", false)]
    [InlineData(@"..\OptiGame-Setup-1.2.0.exe", false)]
    public void Only_optigame_installers_can_be_started(string name, bool expected) =>
        Assert.Equal(expected, AppReleases.IsInstallerName(name));

    [Fact]
    public void The_installer_runs_without_any_window_then_relaunches_optigame()
    {
        const string log = @"C:\Users\Jean Dupont\AppData\Local\OptiGame\logs\update.log";

        Assert.Equal(["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/RELAUNCH=minimized", "/LOG=" + log],
            AppReleases.SilentInstallArguments(showWindowAfter: false, log));
        Assert.Contains("/RELAUNCH=window", AppReleases.SilentInstallArguments(showWindowAfter: true, log));
    }

    [Fact]
    public void The_page_of_a_version_is_its_github_release() =>
        Assert.Equal("https://github.com/Antho69-jdr/OptiGame/releases/tag/v1.2.0", AppReleases.PageFor(new Version(1, 2, 0)).ToString());
}
