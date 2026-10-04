using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OptiGame.App.Converters;

/// <summary>
/// Images des jaquettes, décodées une seule fois puis gardées en mémoire (les moins récemment affichées sont oubliées au-delà
/// de <see cref="BudgetBytes"/>). Images figées : utilisables depuis n'importe quel thread, donc décodables hors du thread UI
/// (liaisons <c>IsAsync</c> des grilles de « Mes jeux »). Une image modifiée sur le disque est relue.
/// </summary>
public static class ImageLoader
{
    private const long BudgetBytes = 96L * 1024 * 1024;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, LinkedListNode<Entry>> Index = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<Entry> Recent = new();
    private static long _bytes;
    private static int _decoded;
    private static double _displayScale = 1;

    /// <summary>Largeur d'une jaquette des grilles de « Mes jeux », en unités WPF (Width de la carte dans LibraryView).</summary>
    public const double GridCoverWidth = 198;

    /// <summary>
    /// Échelle d'affichage de l'écran de la fenêtre principale (1 = 100 %, 1,5 = 150 %), donnée par MainWindow. Les jaquettes
    /// sont décodées à la taille qu'elles occupent réellement à l'écran : sur un écran à 100 %, 4 fois moins de pixels qu'à 200 %.
    /// </summary>
    public static double DisplayScale
    {
        get => Volatile.Read(ref _displayScale);
        set => Volatile.Write(ref _displayScale, Math.Clamp(value, 1, 4));
    }

    /// <summary>Pixels à décoder pour une image affichée sur <paramref name="width"/> unités WPF.</summary>
    public static int PixelsFor(double width) => (int)Math.Ceiling(width * DisplayScale);

    /// <summary>Oublie les images gardées (fenêtre masquée) : seules restent celles encore affichées.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            Index.Clear();
            Recent.Clear();
            _bytes = 0;
        }
    }

    /// <summary>Pour la mesure de la mémoire (journal) : images décodées depuis le lancement, puis contenu du cache.</summary>
    public static string Describe()
    {
        lock (Gate)
        {
            return $"jaquettes décodées depuis le lancement : {Volatile.Read(ref _decoded)}, en cache : {Index.Count} (≈ {_bytes / (1024 * 1024)} Mo de pixels), " +
                   $"décodées sur {PixelsFor(GridCoverWidth)} px de large (échelle d'affichage {DisplayScale * 100:0} %)";
        }
    }

    private sealed record Entry(string Key, BitmapSource Image, DateTime WriteTimeUtc, long Bytes);

    /// <summary>Image décodée à la largeur donnée (en pixels), en niveaux de gris si demandé ; null si absente ou illisible.</summary>
    public static BitmapSource? Load(string? path, int decodeWidth, bool gray = false)
    {
        if (string.IsNullOrEmpty(path)) return null;
        DateTime writeTime;
        try
        {
            if (!File.Exists(path)) return null;
            writeTime = File.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }

        var key = $"{path}|{decodeWidth}|{gray}";
        lock (Gate)
        {
            if (Index.TryGetValue(key, out var node) && node.Value.WriteTimeUtc == writeTime)
            {
                Recent.Remove(node);
                Recent.AddFirst(node);
                return node.Value.Image;
            }
        }

        var image = Decode(path, decodeWidth, gray);
        if (image is null) return null;
        Interlocked.Increment(ref _decoded);

        lock (Gate)
        {
            if (Index.Remove(key, out var stale))
            {
                Recent.Remove(stale);
                _bytes -= stale.Value.Bytes;
            }
            var bytes = (long)image.PixelWidth * image.PixelHeight * Math.Max(1, image.Format.BitsPerPixel / 8);
            Index[key] = Recent.AddFirst(new Entry(key, image, writeTime, bytes));
            _bytes += bytes;
            while (_bytes > BudgetBytes && Recent.Last is { } oldest && oldest != Recent.First)
            {
                Recent.RemoveLast();
                Index.Remove(oldest.Value.Key);
                _bytes -= oldest.Value.Bytes;
            }
        }
        return image;
    }

    /// <summary>Chargée entièrement en mémoire (le fichier n'est pas verrouillé), sans profil de couleur, puis figée.</summary>
    private static BitmapSource? Decode(string path, int decodeWidth, bool gray)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
            image.EndInit();
            image.Freeze();
            if (!gray) return image;

            // Pixels gris COPIÉS dans une image autonome : un FormatConvertedBitmap garderait l'image couleur d'origine
            // attachée (mesuré : 733 Ko par jaquette au lieu de 214).
            var converted = new FormatConvertedBitmap(image, PixelFormats.Gray8, null, 0);
            var stride = (converted.PixelWidth + 3) & ~3;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            // 96 ppp : la jaquette remplit sa carte (UniformToFill), la résolution déclarée par le fichier ne compte pas.
            var grayImage = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Gray8, null, pixels, stride);
            grayImage.Freeze();
            return grayImage;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException or ArgumentException
                                       or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            return null; // Image corrompue : la jaquette de remplacement s'affiche.
        }
    }
}
