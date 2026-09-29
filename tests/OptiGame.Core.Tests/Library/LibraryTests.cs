using OptiGame.Core.Library;

namespace OptiGame.Core.Tests.Library;

public sealed class LibraryTests
{
    // Extrait réel de libraryfolders.vdf (machine de dev).
    private const string LibraryFolders = """
        "libraryfolders"
        {
        	"0"
        	{
        		"path"		"C:\\program files (x86)\\Steam"
        		"apps"
        		{
        			"228980"		"469693989"
        		}
        	}
        	"1"
        	{
        		"path"		"A:\\SteamLibrary"
        		"label"		""
        		"apps"
        		{
        			"387990"		"20596856244"
        			"578080"		"52094668820"
        		}
        	}
        }
        """;

    [Fact]
    public void Vdf_parses_nested_sections_and_escaped_backslashes()
    {
        var root = Vdf.Parse(LibraryFolders)["libraryfolders"]!;

        Assert.Equal(@"C:\program files (x86)\Steam", root["0"]!.GetString("path"));
        Assert.Equal(@"A:\SteamLibrary", root["1"]!.GetString("path"));
        Assert.Equal("", root["1"]!.GetString("label"));
        Assert.Equal(2, root["1"]!["apps"]!.Children.Count);
    }

    [Fact]
    public void Vdf_reads_app_manifest_fields()
    {
        var app = Vdf.Parse("""
            "AppState"
            {
            	"appid"		"1063420"
            	"name"		"Void Crew"
            	// commentaire
            	"installdir"		"Void Crew"
            }
            """)["AppState"]!;

        Assert.Equal("1063420", app.GetString("appid"));
        Assert.Equal("Void Crew", app.GetString("installdir"));
    }

    [Theory]
    [InlineData("\"a\" {")]
    [InlineData("\"a\" \"b")]
    [InlineData("}")]
    public void Vdf_rejects_malformed_input(string text)
    {
        Assert.Throws<FormatException>(() => Vdf.Parse(text));
    }

    private static ExeFile Exe(string path, double megabytes) => new(path, (long)(megabytes * 1024 * 1024));

    // Cas réels relevés sur la machine de dev.
    public static TheoryData<string, string, ExeFile[], string> RealGames() => new()
    {
        {
            "Overwatch", "Overwatch",
            [Exe(@"O\Overwatch.exe", 62), Exe(@"O\BlizzardBrowser\BlizzardBrowser.exe", 2.2),
             Exe(@"O\ErrorReporting\x86\BlizzardError.exe", 0.9), Exe(@"O\ErrorReporting\x64\CrashMailer_64.exe", 0.4)],
            "Overwatch.exe"
        },
        {
            "PUBG: BATTLEGROUNDS", "PUBG",
            [Exe(@"P\TslGame\Binaries\Win64\TslGame.exe", 222.6), Exe(@"P\TslGame\Binaries\Win64\TslGame_ZK.exe", 46.8),
             Exe(@"P\TslGame\Binaries\Win64\BattlEye\BEService_x64.exe", 18.1), Exe(@"P\TslGame\Binaries\Win64\ExecPubg.exe", 8.3)],
            "TslGame.exe"
        },
        {
            "Scrap Mechanic", "Scrap Mechanic",
            [Exe(@"S\Release\ScrapMechanic.exe", 27.6), Exe(@"S\Release\TileEditor.exe", 17.9),
             Exe(@"S\Release\ContentCompiler.exe", 17.5), Exe(@"S\Release\BsSndRpt64.exe", 0.5)],
            "ScrapMechanic.exe"
        },
        {
            "Void Crew", "Void Crew",
            [Exe(@"V\UnityCrashHandler64.exe", 1.1), Exe(@"V\Void Crew.exe", 0.9)],
            "Void Crew.exe"
        },
        {
            "Roberts Space Industries", "Roberts Space Industries",
            [Exe(@"R\RSI Launcher\RSI Launcher.exe", 212.7), Exe(@"R\StarCitizen\LIVE\Bin64\StarCitizen.exe", 167.2),
             Exe(@"R\StarCitizen\LIVE\Tools\Public\CrashHandler.exe", 68.5), Exe(@"R\RSI Launcher\resources\VC_redist.x64.exe", 24),
             Exe(@"R\StarCitizen\LIVE\StarCitizen_Launcher.exe", 3.8)],
            "StarCitizen.exe"
        },
    };

    [Theory]
    [MemberData(nameof(RealGames))]
    public void Main_exe_is_ranked_first(string gameName, string folder, ExeFile[] exes, string expected)
    {
        var ranked = ExeRanking.Rank(gameName, folder, exes);

        Assert.Equal(expected, Path.GetFileName(ranked[0].Path));
    }

    [Fact]
    public void Utilities_are_excluded()
    {
        var ranked = ExeRanking.Rank("Jeu", "Jeu",
            [Exe(@"J\unins000.exe", 3), Exe(@"J\UnityCrashHandler64.exe", 1), Exe(@"J\vc_redist.x64.exe", 24), Exe(@"J\Game.exe", 0.5)]);

        Assert.Equal(@"J\Game.exe", Assert.Single(ranked).Path);
    }

    [Fact]
    public void Exact_name_is_never_excluded()
    {
        var ranked = ExeRanking.Rank("Kingdom Builder", "Kingdom Builder", [Exe(@"K\KingdomBuilder.exe", 10)]);

        Assert.Single(ranked);
    }

    [Fact]
    public void No_candidate_when_only_utilities()
    {
        Assert.Empty(ExeRanking.Rank("Redist", "Redist", [Exe(@"R\DXSETUP.exe", 1)]));
    }
}
