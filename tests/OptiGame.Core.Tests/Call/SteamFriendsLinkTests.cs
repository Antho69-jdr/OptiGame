using OptiGame.Core.Call;

namespace OptiGame.Core.Tests.Call;

public sealed class SteamFriendsLinkTests
{
    [Fact]
    public void Account_ids_become_steam_ids()
    {
        Assert.Equal("76561198042893045", SteamFriendsLink.FromAccountId(82627317)); // compte de la machine de dev (avatarcache)
        Assert.True(SteamFriendsLink.IsSteamId("76561198042893045"));
        Assert.False(SteamFriendsLink.IsSteamId("82627317"));
        Assert.False(SteamFriendsLink.IsSteamId(null));
    }

    [Fact]
    public void Login_states_are_random_and_url_safe()
    {
        var a = SteamFriendsLink.NewLoginState();
        Assert.Matches("^[A-Za-z0-9_-]{32}$", a);
        Assert.NotEqual(a, SteamFriendsLink.NewLoginState());
    }

    [Fact]
    public void Addresses_follow_the_relay()
    {
        var relay = new Uri("wss://optigame-call.exemple.workers.dev");
        Assert.Equal("https://optigame-call.exemple.workers.dev/v1/auth/steam?state=abc", SteamFriendsLink.LoginPage(relay, "abc").AbsoluteUri);
        Assert.Equal("wss://optigame-call.exemple.workers.dev/v1/auth/wait?state=abc", SteamFriendsLink.LoginWait(relay, "abc").AbsoluteUri);
        Assert.Equal("wss://optigame-call.exemple.workers.dev/v1/presence", SteamFriendsLink.Presence(relay).AbsoluteUri);
        Assert.Equal("http://localhost:5123/", SteamFriendsLink.HttpOrigin(new Uri("ws://localhost:5123")).AbsoluteUri);
    }

    [Fact]
    public void Token_contents_are_read_without_trusting_them()
    {
        var token = SteamFriendsLink.ReadToken("76561198042893045.1791548787484.c2lnbmF0dXJl");
        Assert.Equal("76561198042893045", token!.Value.SteamId);
        Assert.Equal(1791548787484, token.Value.Expires.ToUnixTimeMilliseconds());
        Assert.Null(SteamFriendsLink.ReadToken("abc.def.ghi"));
        Assert.Null(SteamFriendsLink.ReadToken(null));
    }
}
