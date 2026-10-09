using OptiGame.Core.Call;

namespace OptiGame.Core.Tests.Call;

public sealed class MicHotkeyTests
{
    private const int M = 0x4D, F13 = 0x7C, LeftCtrl = 0xA2, LeftAlt = 0xA4, LeftShift = 0xA0;

    [Fact]
    public void Labels_are_in_french()
    {
        Assert.Equal("Ctrl + Alt + M", new MicHotkey(M, Ctrl: true, Alt: true).Label());
        Assert.Equal("F13", new MicHotkey(F13).Label());
        Assert.Equal("Maj + Pavé num. 5", new MicHotkey(0x65, Shift: true).Label());
        Assert.Equal("ù", new MicHotkey(0xC0).Label(_ => "ù")); // touche propre à la disposition : nommée par Windows
        Assert.Equal("Touche C0", new MicHotkey(0xC0).Label());
    }

    [Fact]
    public void Settings_text_round_trips_and_bad_text_is_dropped()
    {
        var hotkey = new MicHotkey(M, Ctrl: true, Shift: true);
        Assert.Equal("ctrl+shift+0x4D", hotkey.Serialize());
        Assert.Equal(hotkey, MicHotkey.Parse(hotkey.Serialize()));
        Assert.Null(MicHotkey.Parse(null));
        Assert.Null(MicHotkey.Parse("ctrl+0x11")); // un modificateur seul
        Assert.Null(MicHotkey.Parse("0x1B")); // Échap annule la saisie
        Assert.Null(MicHotkey.Parse("win+0x4D"));
        Assert.Null(MicHotkey.Parse("bonjour"));
    }

    [Fact]
    public void A_single_key_fires_on_press_and_release_and_ignores_repeats()
    {
        var matcher = new MicHotkeyMatcher(new MicHotkey(F13));
        Assert.Equal(1, matcher.OnKey(F13, true));
        Assert.Equal(0, matcher.OnKey(F13, true)); // répétition automatique
        Assert.Equal(-1, matcher.OnKey(F13, false));
        Assert.Equal(0, matcher.OnKey(0x41, true));
    }

    [Fact]
    public void Extra_held_keys_do_not_block_a_single_key()
    {
        var matcher = new MicHotkeyMatcher(new MicHotkey(F13));
        matcher.OnKey(LeftShift, true); // course
        matcher.OnKey(0x57, true); // W
        Assert.Equal(1, matcher.OnKey(F13, true));
        Assert.Equal(-1, matcher.OnKey(F13, false));
    }

    [Fact]
    public void Required_modifiers_must_be_held_left_or_right()
    {
        var matcher = new MicHotkeyMatcher(new MicHotkey(M, Ctrl: true, Alt: true));
        Assert.Equal(0, matcher.OnKey(M, true));
        matcher.OnKey(M, false);
        matcher.OnKey(LeftCtrl, true);
        matcher.OnKey(0xA5, true); // Alt droit
        Assert.Equal(1, matcher.OnKey(M, true));
        matcher.OnKey(LeftCtrl, false); // modificateur relâché avant : le micro reste ouvert jusqu'à la touche
        Assert.Equal(-1, matcher.OnKey(M, false));
        Assert.Equal(0, matcher.OnKey(LeftAlt, false));
    }
}
