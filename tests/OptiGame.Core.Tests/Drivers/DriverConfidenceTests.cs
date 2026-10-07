using System.IO.Compression;
using System.Text;
using OptiGame.Core.Drivers;

namespace OptiGame.Core.Tests.Drivers;

public sealed class NvidiaReleaseNotesTests
{
    private static string Sample(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Drivers", "Samples", name));

    [Theory]
    // Texte réel de la section, extrait par PdfText des PDF de NVIDIA (2026-10-07) : pieds de page intercalés, guillemets octaux.
    [InlineData("617.14", "Assassin's Creed Shadows may crash to desktop after extended gameplay after updating to driver 616.56")]
    [InlineData("616.56", "\"Prefer Maximum Performance\" Power Management Mode may not be applied correctly")]
    [InlineData("591.86", "Call of Duty: Modern Warfare: Image corruption after driver update")]
    public void Reads_the_open_issues_of_real_release_notes(string version, string issue)
    {
        Assert.Equal([issue], NvidiaReleaseNotes.OpenIssues(Sample($"nvidia-open-issues-{version}.txt"), version));
    }

    [Fact]
    public void Unknown_format_gives_null_and_an_empty_section_no_issue()
    {
        Assert.Null(NvidiaReleaseNotes.OpenIssues("Release notes without the expected sections", "617.14"));
        Assert.Null(NvidiaReleaseNotes.OpenIssues(Sample("nvidia-open-issues-617.14.txt"), "616.56")); // autre version
        Assert.Empty(NvidiaReleaseNotes.OpenIssues("Open Issues in Version 700.01 WHQL Nothing to report. 3.3 Issues Not Caused by NVIDIA Drivers", "700.01")!);
    }

    [Fact]
    public void Reads_game_ready_fixes_and_pdf_link_of_the_real_lookup()
    {
        var driver = NvidiaDrivers.ParseLookup(Sample("nvidia-lookup-rtx3070.json"))!;

        Assert.Equal("CONTROL Resonant, Gears of War: E-Day, The Witcher 3: Wild Hunt – Remastered & AION 2", driver.GameReadyTitle);
        Assert.Equal(
        [
            "Judgement/Lost Judgement/ Virtua Fighter 5 R.E.V.O. may fail to launch after updating to driver 616.56",
            "No picture on Samsung Odyssey G95NC monitor when connected via HDMI after updating to R615 drivers",
            "Certain monitors may not wake from sleep when connected via DisplayPort after updating to R615 drivers",
        ], driver.FixedIssues);
        Assert.Equal("https://us.download.nvidia.com/Windows/617.14/617.14-win11-win10-release-notes.pdf", driver.ReleaseNotesPdf?.AbsoluteUri);
    }

    [Fact]
    public void Extracts_the_text_of_a_simple_pdf()
    {
        // Flux compressé comme ceux de NVIDIA : TJ avec crénage, Tj, échappement octal WinAnsi (\222 = apostrophe courbe).
        const string content = "BT /F1 12 Tf [(Op)-1 (en)2 ( Issues)]TJ 0 -14 Td (What\\222s new)Tj ET";
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) zlib.Write(Encoding.Latin1.GetBytes(content));
        var pdf = new List<byte>();
        pdf.AddRange(Encoding.Latin1.GetBytes($"%PDF-1.7\n1 0 obj <</Length {compressed.Length}/Filter/FlateDecode>>stream\r\n"));
        pdf.AddRange(compressed.ToArray());
        pdf.AddRange(Encoding.Latin1.GetBytes("\r\nendstream endobj\n%%EOF"));

        Assert.Equal("Open Issues What's new", PdfText.Extract([.. pdf]));
    }
}

public sealed class DriverConfidenceTests
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    private static NvidiaDriver Driver(DateOnly? released = null, string? gameReady = null, params string[] fixes) =>
        new("GeForce Game Ready Driver", "617.14", released ?? new DateOnly(2026, 9, 22), new Uri("https://us.download.nvidia.com/x.exe"),
            null, null, [], gameReady, fixes);

    [Theory]
    [InlineData("PUBG: Battlegrounds may crash", "PUBG: BATTLEGROUNDS", true)]  // casse et ponctuation ignorées
    [InlineData("Overwatch 2 may stutter", "Overwatch", true)]                 // nom entier, en mots entiers
    [InlineData("The Witcher 3: Wild Hunt – Remastered & AION 2", "The Witcher® 3: Wild Hunt", true)]
    [InlineData("Call of Duty: Modern Warfare: corruption", "Call of Duty: Modern Warfare III", false)] // nom plus long : pas lui
    [InlineData("Rustling noise in menus", "Rust", false)]                    // mots entiers seulement
    [InlineData("GTA may crash", "GTA", false)]                               // moins de 4 lettres : ambigu
    public void Recognizes_a_game_in_a_note(string text, string game, bool expected) =>
        Assert.Equal(expected, DriverConfidences.Mentions(text, game));

    [Fact]
    public void An_open_issue_in_one_of_your_games_calls_for_caution()
    {
        var confidence = DriverConfidences.Evaluate(Driver(), ["Assassin's Creed Shadows may crash to desktop"], ["Assassin's Creed Shadows", "PUBG"], Today);

        Assert.Equal(ConfidenceLevel.Caution, confidence.Level);
        Assert.Equal("Prudence : un problème connu touche un de vos jeux", confidence.Headline);
        Assert.Contains("Problème encore ouvert signalé par NVIDIA : « Assassin's Creed Shadows may crash to desktop ».", confidence.Reasons);
        Assert.Contains("Publiée il y a 15 jours.", confidence.Reasons);
    }

    [Fact]
    public void Fixes_or_game_ready_for_your_games_make_it_recommended()
    {
        var fixes = DriverConfidences.Evaluate(Driver(null, null, "Lost Judgement may fail to launch"), [], ["Lost Judgement"], Today);
        Assert.Equal(ConfidenceLevel.Recommended, fixes.Level);
        Assert.Contains("Corrige : « Lost Judgement may fail to launch ».", fixes.Reasons);
        Assert.Contains("NVIDIA ne signale aucun problème encore ouvert dans cette version.", fixes.Reasons);

        var ready = DriverConfidences.Evaluate(Driver(null, "CONTROL Resonant & AION 2"), ["Other game crashes"], ["AION 2", "Portal 2"], Today);
        Assert.Equal(ConfidenceLevel.Recommended, ready.Level);
        Assert.Contains("Optimisé (« Game Ready ») pour AION 2.", ready.Reasons);
        Assert.Contains(ready.Reasons, r => r.StartsWith("NVIDIA signale 1 problème encore ouvert, aucun dans vos jeux"));
    }

    [Fact]
    public void Without_the_open_issues_nothing_is_called_safe()
    {
        var unknown = DriverConfidences.Evaluate(Driver(null, null, "Lost Judgement may fail to launch"), null, ["Lost Judgement"], Today);
        Assert.Equal((ConfidenceLevel.Unknown, "Problèmes connus non vérifiés"), (unknown.Level, unknown.Headline));

        var none = DriverConfidences.Evaluate(Driver(), [], ["PUBG"], Today);
        Assert.Equal(ConfidenceLevel.NoKnownIssue, none.Level);
    }

    [Fact]
    public void A_very_recent_version_is_flagged()
    {
        var confidence = DriverConfidences.Evaluate(Driver(new DateOnly(2026, 10, 6)), [], [], Today);
        Assert.Contains(confidence.Reasons, r => r.StartsWith("Publiée hier : si rien ne presse"));
    }
}
