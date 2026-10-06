using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OptiGame.Core.Library;
using OptiGame.Platform.Library;

namespace OptiGame.App.Converters;

/// <summary>
/// Icône du magasin d'un jeu (pastille des jaquettes) : celle du lanceur INSTALLÉ sur ce PC (steam.exe, programme du protocole
/// com.epicgames.launcher, GalaxyClient.exe du protocole goggalaxy). Aucun logo n'est embarqué ni téléchargé ; lanceur absent =
/// pas d'icône (la pastille montre l'initiale du magasin). Lue une fois par magasin, figée.
/// </summary>
public static class StoreIcons
{
    private const int Pixels = 48; // nette jusqu'à 200 % pour une pastille de 16 à 24 unités

    private static readonly Lock Gate = new();
    private static readonly Dictionary<GameSource, ImageSource?> Cache = [];

    public static ImageSource? For(GameSource store)
    {
        lock (Gate)
        {
            if (!Cache.TryGetValue(store, out var icon)) Cache[store] = icon = Load(store);
            return icon;
        }
    }

    private static ImageSource? Load(GameSource store)
    {
        var exe = store switch
        {
            GameSource.Steam => GameLibraryScanner.SteamExe(),
            GameSource.Epic => StoreLibraries.ProtocolExe("com.epicgames.launcher"),
            GameSource.Gog => StoreLibraries.ProtocolExe("goggalaxy"),
            _ => null,
        };
        if (exe is null) return null;
        var size = (uint)Pixels | ((uint)16 << 16); // grande icône : 48 px ; petite : 16 px (ignorée)
        if (SHDefExtractIcon(exe, 0, 0, out var large, out var small, size) != 0 || large == IntPtr.Zero) return null;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(large, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(Pixels, Pixels));
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return null;
        }
        finally
        {
            DestroyIcon(large);
            if (small != IntPtr.Zero) DestroyIcon(small);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string iconFile, int index, uint flags, out IntPtr large, out IntPtr small, uint iconSize);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr icon);
}

/// <summary>Magasin (GameSource ou null) → icône de son lanceur, ou null.</summary>
public sealed class StoreToIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is GameSource store and not GameSource.Folder ? StoreIcons.For(store) : null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
