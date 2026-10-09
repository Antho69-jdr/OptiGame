using OptiGame.Core.Call;

namespace OptiGame.Core.Tests.Call;

/// <summary>Codes d'appel et mots de contrôle. L'offre ci-dessous a la forme d'une vraie offre de WebView2 (relevée le 2026-10-09).</summary>
public sealed class CallCodeTests
{
    private const string Sdp = "v=0\r\no=- 4611731400430051336 2 IN IP4 127.0.0.1\r\ns=-\r\nt=0 0\r\na=group:BUNDLE 0\r\n" +
        "m=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\nc=IN IP4 0.0.0.0\r\n" +
        "a=candidate:1 1 udp 2113937151 0a1b2c3d-1111-2222-3333-444455556666.local 54321 typ host generation 0\r\n" +
        "a=ice-ufrag:abcd\r\na=ice-pwd:0123456789abcdef01234567\r\n" +
        "a=fingerprint:sha-256 12:34:56:78:9A:BC:DE:F0:12:34:56:78:9A:BC:DE:F0:12:34:56:78:9A:BC:DE:F0:12:34:56:78:9A:BC:DE:F0\r\n" +
        "a=setup:actpass\r\na=mid:0\r\na=sctp-port:5000\r\n";

    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_code_round_trips_and_is_shorter_than_the_description()
    {
        var code = InviteCode.Create(InviteKind.Invitation, Sdp, Now);
        Assert.StartsWith("OG1I", code);
        Assert.True(code.Length < Sdp.Length);
        // Collé avec des retours à la ligne (messagerie qui coupe les longues lignes) : relu quand même.
        var pasted = string.Join("\n", code.Chunk(60).Select(c => new string(c)));
        Assert.Equal(Sdp, InviteCode.Read(pasted, InviteKind.Invitation, Now.AddMinutes(5)));
    }

    [Fact]
    public void Wrong_kind_expired_or_damaged_codes_are_refused_with_a_reason()
    {
        var invitation = InviteCode.Create(InviteKind.Invitation, Sdp, Now);
        Assert.Contains("RÉPONSE", Assert.Throws<FormatException>(() => InviteCode.Read(invitation, InviteKind.Answer, Now)).Message);
        Assert.Contains("expiré", Assert.Throws<FormatException>(() => InviteCode.Read(invitation, InviteKind.Invitation, Now.AddMinutes(16))).Message);
        Assert.Contains("abîmé", Assert.Throws<FormatException>(() => InviteCode.Read(invitation[..^20], InviteKind.Invitation, Now)).Message);
        Assert.Contains("OG1", Assert.Throws<FormatException>(() => InviteCode.Read("bonjour", InviteKind.Invitation, Now)).Message);
        Assert.Throws<FormatException>(() => InviteCode.Read("OG1I" + new string('A', InviteCode.MaxLength), InviteKind.Invitation, Now));
    }

    [Fact]
    public void A_code_from_a_clock_far_in_the_future_is_refused()
    {
        var future = InviteCode.Create(InviteKind.Answer, Sdp, Now.AddHours(2));
        Assert.Contains("date impossible", Assert.Throws<FormatException>(() => InviteCode.Read(future, InviteKind.Answer, Now)).Message);
    }

    [Fact]
    public void Fingerprint_is_read_from_the_description()
    {
        Assert.StartsWith("sha-256 12:34:56", InviteCode.Fingerprint(Sdp));
        Assert.Null(InviteCode.Fingerprint("v=0\r\n"));
    }

    [Fact]
    public void Both_sides_get_the_same_four_words()
    {
        const string a = "sha-256 AA:BB:CC";
        const string b = "sha-256 11:22:33";
        var words = SafetyWords.For(a, b);
        Assert.Equal(words, SafetyWords.For(b, a)); // l'hôte et l'invité ont leurs empreintes dans l'ordre inverse
        Assert.Equal(4, words.Split(" · ").Length);
        Assert.NotEqual(words, SafetyWords.For(a, "sha-256 11:22:34")); // une autre empreinte (intrus) = d'autres mots
        Assert.Equal("", SafetyWords.For(a, null));
    }

    [Fact]
    public void The_word_list_has_256_distinct_words()
    {
        Assert.Equal(256, SafetyWords.Words.Count);
        Assert.Equal(256, SafetyWords.Words.Distinct().Count());
    }
}
