using OptiGame.Core.Call;

namespace OptiGame.Core.Tests.Call;

public sealed class VoiceQualityAdvisorTests
{
    [Theory]
    [InlineData(48000, 20000, 128)] // micro de studio : pleine bande
    [InlineData(48000, 15000, 96)]
    [InlineData(48000, 9000, 64)]   // micro de casque sans fil : coupe vers 9 kHz malgré 48 kHz annoncés
    [InlineData(16000, null, 48)]   // casque Bluetooth en mode appel : 16 kHz dans Windows
    [InlineData(8000, 20000, 32)]   // la mesure ne dépasse jamais ce que le format permet
    [InlineData(null, 7000, 48)]
    public void The_bitrate_follows_what_the_microphone_really_captures(int? sampleRate, int? bandwidth, int kbps) =>
        Assert.Equal(kbps, VoiceQualityAdvisor.Recommend(sampleRate, bandwidth).Kbps);

    [Fact]
    public void Without_information_the_high_bitrate_is_kept_and_said()
    {
        var advice = VoiceQualityAdvisor.Recommend(null, null);
        Assert.Equal(128, advice.Kbps);
        Assert.Contains("pas encore mesuré", advice.Reason);
    }

    [Fact]
    public void The_reason_says_why_in_plain_french()
    {
        Assert.Equal("Votre micro capte la voix jusqu'à 9 kHz : 64 kbit/s, plus de débit n'apporterait rien.",
            VoiceQualityAdvisor.Recommend(48000, 9000).Reason);
        Assert.Equal("Votre micro est réglé à 16 kHz dans Windows : 48 kbit/s, plus de débit n'apporterait rien.",
            VoiceQualityAdvisor.Recommend(16000, null).Reason);
    }

    [Fact]
    public void The_default_microphone_prefix_is_removed()
    {
        Assert.Equal("Microphone (Yeti Nano)", VoiceQualityAdvisor.DeviceName("Par défaut - Microphone (Yeti Nano)"));
        Assert.Equal("Microphone (Yeti Nano)", VoiceQualityAdvisor.DeviceName("Microphone (Yeti Nano)"));
    }
}
