using OptiGame.Core.Call;

namespace OptiGame.Core.Tests.Call;

/// <summary>Forme du bloc « friends » relevée le 2026-10-09 sur la machine de dev (noms remplacés).</summary>
public sealed class SteamPersonasTests
{
    private const string LocalConfig = """
        "UserLocalConfigStore"
        {
        	"friends"
        	{
        		"82627317"
        		{
        			"NameHistory"
        			{
        				"0"		"Moi"
        			}
        			"avatar"		"60a08004ab4caf17dee67aeabda1d156d08dff11"
        			"name"		"Moi"
        		}
        		"PersonaName"		"Moi"
        		"communitypreferences"		"18a5debdc1062000280330013800"
        		"191567988"
        		{
        			"name"		"Élodie"
        			"avatar"		"232fe086bb1457615bebd0c81c9034897e5f8046"
        		}
        		"842469044"
        		{
        			"name"		"bob"
        		}
        		"48469177"
        		{
        			"avatar"		"1fc4024be62da8b0618131f53eb6a736d06de63a"
        		}
        	}
        }
        """;

    [Fact]
    public void Known_people_are_listed_without_myself_or_nameless_entries()
    {
        var personas = SteamPersonas.Read(LocalConfig, 82627317);
        Assert.Equal(["bob", "Élodie"], personas.Select(p => p.Name));
        Assert.Equal(SteamFriendsLink.FromAccountId(191567988), personas[1].SteamId);
    }

    [Fact]
    public void Search_ignores_case_and_accents()
    {
        var personas = SteamPersonas.Read(LocalConfig, 82627317);
        Assert.Equal("Élodie", Assert.Single(SteamPersonas.Search(personas, "elo")).Name);
        Assert.Equal(2, SteamPersonas.Search(personas, " ").Count);
        Assert.Empty(SteamPersonas.Read("\"x\" { }", 1));
    }
}
