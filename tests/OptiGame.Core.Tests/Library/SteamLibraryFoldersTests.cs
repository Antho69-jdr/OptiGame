using OptiGame.Core.Library;

namespace OptiGame.Core.Tests.Library;

public class SteamLibraryFoldersTests
{
    [Fact]
    public void Current_format_lists_each_path()
    {
        // Extrait du fichier de la machine de dev (identifiants et tailles retirés), plus une 2e bibliothèque.
        var root = Vdf.Parse("""
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            		"label"		""
            		"apps"
            		{
            			"228980"		"0"
            		}
            	}
            	"1"
            	{
            		"path"		"D:\\SteamLibrary"
            	}
            }
            """);

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], SteamLibraryFolders.Paths(root));
    }

    [Fact]
    public void Old_format_reads_numbered_values_and_skips_the_others()
    {
        var root = Vdf.Parse("""
            "LibraryFolders"
            {
            	"TimeNextStatsReport"		"1612345678"
            	"ContentStatsID"		"-123456789"
            	"1"		"D:\\Jeux\\Steam"
            	"2"		"E:/SteamLibrary"
            }
            """);

        Assert.Equal([@"D:\Jeux\Steam", @"E:\SteamLibrary"], SteamLibraryFolders.Paths(root));
    }

    [Fact]
    public void Missing_block_gives_nothing()
    {
        Assert.Empty(SteamLibraryFolders.Paths(Vdf.Parse("\"other\" { }")));
    }
}
