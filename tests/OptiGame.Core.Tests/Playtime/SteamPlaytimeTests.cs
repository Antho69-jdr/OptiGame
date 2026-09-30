using OptiGame.Core.Playtime;

namespace OptiGame.Core.Tests.Playtime;

public sealed class SteamPlaytimeTests
{
    // Même structure que le vrai localconfig.vdf (machine de dev, 2026-09-30), valeurs fictives. Le second bloc « apps »
    // (WebStorage) ne doit pas être lu.
    private const string LocalConfig = """
        "UserLocalConfigStore"
        {
        	"Software"
        	{
        		"Valve"
        		{
        			"Steam"
        			{
        				"apps"
        				{
        					"7"
        					{
        						"cloud"
        						{
        							"last_sync_state"		"synchronized"
        						}
        					}
        					"578080"
        					{
        						"LastPlayed"		"1790790974"
        						"Playtime"		"33378"
        						"Playtime2wks"		"915"
        					}
        					"440"
        					{
        						"LastPlayed"		"1359014400"
        						"Playtime"		"530"
        					}
        					"999"
        					{
        						"LastPlayed"		"1700000000"
        					}
        				}
        			}
        		}
        	}
        	"WebStorage"
        	{
        		"apps"
        		{
        			"123"
        			{
        				"Playtime"		"99999"
        			}
        		}
        	}
        }
        """;

    [Fact]
    public void Reads_minutes_and_last_played_per_appid()
    {
        var apps = SteamPlaytime.Parse(LocalConfig);

        Assert.Equal(TimeSpan.FromMinutes(33378), apps["578080"].Total); // 556 h 18
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1790790974), apps["578080"].LastPlayed);
        Assert.Equal(TimeSpan.FromMinutes(530), apps["440"].Total);
        Assert.Equal((TimeSpan.Zero, DateTimeOffset.FromUnixTimeSeconds(1700000000)), (apps["999"].Total, apps["999"].LastPlayed));
        Assert.False(apps.ContainsKey("7"));   // pas de temps enregistré
        Assert.False(apps.ContainsKey("123")); // bloc WebStorage : pas un temps de jeu
    }

    [Fact]
    public void Missing_or_foreign_structure_gives_nothing()
    {
        Assert.Empty(SteamPlaytime.Parse("\"UserLocalConfigStore\" { }"));
        Assert.Empty(SteamPlaytime.Parse("\"Other\" { \"apps\" { \"1\" { \"Playtime\" \"5\" } } }"));
    }

    [Fact]
    public void Account_is_the_last_one_logged_in()
    {
        // Steam actuel : pas de MostRecent, on prend le plus grand Timestamp.
        const string current = """
            "users"
            {
            	"76561198042893045"
            	{
            		"AccountName"		"a"
            		"Timestamp"		"1790788733"
            	}
            	"76561197960265829"
            	{
            		"AccountName"		"b"
            		"Timestamp"		"1600000000"
            	}
            }
            """;
        Assert.Equal("82627317", SteamPlaytime.MostRecentAccountId(current));

        // Ancien Steam : MostRecent prime sur Timestamp.
        const string older = """
            "users"
            {
            	"76561198042893045" { "MostRecent" "0" "Timestamp" "1790788733" }
            	"76561197960265829" { "MostRecent" "1" "Timestamp" "1600000000" }
            }
            """;
        Assert.Equal("101", SteamPlaytime.MostRecentAccountId(older));
        Assert.Null(SteamPlaytime.MostRecentAccountId("\"users\" { }"));
    }

    private static readonly DateTimeOffset Yesterday = new(2026, 9, 29, 20, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastWeek = new(2026, 9, 23, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Steam_total_wins_when_larger_and_is_never_added_to_optigame()
    {
        var optiGame = new PlaytimeStats(TimeSpan.FromHours(2), 1, LastWeek);
        var steam = new SteamPlaytimeEntry(TimeSpan.FromHours(556), Yesterday);

        var summary = PlaytimeSummary.Combine(optiGame, steam);

        Assert.Equal(new PlaytimeSummary(TimeSpan.FromHours(556), Yesterday, 1, PlaytimeSource.Steam), summary);
    }

    [Fact]
    public void Live_optigame_session_can_exceed_the_not_yet_updated_steam_total()
    {
        var optiGame = new PlaytimeStats(TimeSpan.FromMinutes(90), 2, Yesterday);
        var steam = new SteamPlaytimeEntry(TimeSpan.FromMinutes(60), LastWeek);

        var summary = PlaytimeSummary.Combine(optiGame, steam);

        Assert.Equal((TimeSpan.FromMinutes(90), Yesterday, PlaytimeSource.OptiGame), (summary.Total, summary.LastPlayed, summary.Source));
    }

    [Fact]
    public void Without_steam_data_the_optigame_tracking_is_used_as_is()
    {
        Assert.Equal(PlaytimeSummary.None, PlaytimeSummary.Combine(PlaytimeStats.None, null));
        Assert.Equal(PlaytimeSummary.None, PlaytimeSummary.Combine(PlaytimeStats.None, new SteamPlaytimeEntry(TimeSpan.Zero, null)));
        Assert.Equal(new PlaytimeSummary(TimeSpan.FromMinutes(42), LastWeek, 3, PlaytimeSource.OptiGame),
            PlaytimeSummary.Combine(new PlaytimeStats(TimeSpan.FromMinutes(42), 3, LastWeek), null));
        var steamOnly = PlaytimeSummary.Combine(PlaytimeStats.None, new SteamPlaytimeEntry(TimeSpan.FromMinutes(10), null));
        Assert.Equal((TimeSpan.FromMinutes(10), (DateTimeOffset?)null, 0, PlaytimeSource.Steam, true),
            (steamOnly.Total, steamOnly.LastPlayed, steamOnly.OptiGameSessions, steamOnly.Source, steamOnly.EverPlayed));
    }
}
