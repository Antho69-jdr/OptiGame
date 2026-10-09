using OptiGame.Core.Call;

namespace OptiGame.Core.Tests.Call;

/// <summary>Codes d'appel, serveur de mise en relation et mots de contrôle.</summary>
public sealed class CallCodeTests
{
    [Fact]
    public void A_code_has_six_unambiguous_characters()
    {
        for (var i = 0; i < 200; i++)
        {
            var code = CallCode.Create();
            Assert.Equal(6, code.Length);
            Assert.All(code, c => Assert.Contains(c, CallCode.Alphabet));
        }
        foreach (var ambiguous in "0O1IL") Assert.DoesNotContain(ambiguous, CallCode.Alphabet);
        Assert.Equal("OG-K7P2Q9", CallCode.Display("K7P2Q9"));
    }

    [Theory]
    [InlineData("OG-K7P2Q9")]
    [InlineData("og-k7p2q9")]
    [InlineData(" K7P 2Q9 ")]
    [InlineData("OGK7P2Q9")]
    [InlineData("k7p-2q9")]
    public void Pasted_or_typed_codes_are_read(string text) => Assert.Equal("K7P2Q9", CallCode.Parse(text));

    [Theory]
    [InlineData("OG-K7P2Q")]
    [InlineData("bonjour")]
    [InlineData("OG-K7P2Q90")]
    [InlineData("OG-K0P2Q9")]
    public void Other_text_is_refused_with_a_reason(string text) => Assert.NotEmpty(Assert.Throws<FormatException>(() => CallCode.Parse(text)).Message);

    [Fact]
    public void Only_safe_relay_addresses_are_accepted()
    {
        Assert.NotNull(CallRelay.Resolve("wss://optigame-call.exemple.workers.dev/"));
        Assert.NotNull(CallRelay.Resolve("ws://localhost:51234"));
        Assert.Null(CallRelay.Resolve("ws://optigame-call.exemple.workers.dev")); // non chiffré
        Assert.Null(CallRelay.Resolve("wss://exemple.com"));
        Assert.Null(CallRelay.Resolve("ws://192.168.1.10:8080"));
        Assert.Null(CallRelay.Resolve("wss://x.workers.dev:8443"));
        Assert.Equal("ws://localhost:51234/v1/rooms/K7P2Q9?role=guest",
            CallRelay.Room(CallRelay.Resolve("ws://localhost:51234")!, "K7P2Q9", host: false).AbsoluteUri);
        Assert.Equal("wss://a.workers.dev/v1/rooms/K7P2Q9?role=host", CallRelay.Room(new Uri("wss://a.workers.dev/"), "K7P2Q9", host: true).AbsoluteUri);
    }

    [Fact]
    public void Fingerprint_is_read_from_the_description()
    {
        const string sdp = "v=0\r\nm=audio 9 UDP/TLS/RTP/SAVPF 111\r\na=fingerprint:sha-256 12:34:56:78\r\na=setup:actpass\r\n";
        Assert.Equal("sha-256 12:34:56:78", CallCode.Fingerprint(sdp));
        Assert.Null(CallCode.Fingerprint("v=0\r\n"));
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
