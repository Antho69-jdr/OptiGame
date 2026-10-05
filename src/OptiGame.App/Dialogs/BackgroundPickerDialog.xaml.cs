using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OptiGame.Core.Artwork;
using OptiGame.Core.Text;

namespace OptiGame.App.Dialogs;

/// <summary>Fond choisi : image IGDB (à télécharger en 1080p), fichier local (bannière Steam, image du PC) ou fond d'origine.</summary>
public sealed record BackgroundChoice(BackgroundSource Source, string? ImageId = null, string? Path = null);

public enum BackgroundSource
{
    Igdb,
    File,
    Original,
}

/// <summary>
/// « Changer le fond… » de la fiche du jeu : bannière que Steam garde sur le PC, illustrations et captures d'écran d'IGDB,
/// ou une image du PC. Rien n'est enregistré ici : le choix est renvoyé, la fiche le copie dans le dossier de données.
/// </summary>
public partial class BackgroundPickerDialog : DialogWindow
{
    private readonly ObservableCollection<Option> _options = [];

    public BackgroundPickerDialog(string gameName, string? steamHeroPath, Func<Task<IReadOnlyList<IgdbBackground>>>? loadIgdb,
        Func<string, Task<string?>> loadThumbnail, bool hasCustomBackground)
    {
        InitializeComponent();
        Layout.Heading = $"Changer le fond de {gameName}";
        Layout.Description = "Image affichée en haut de la fiche du jeu. Seul le nom du jeu est envoyé à IGDB ; l'image choisie est copiée sur ce PC.";
        Options.ItemsSource = _options;
        OriginalButton.Visibility = hasCustomBackground ? Visibility.Visible : Visibility.Collapsed;
        if (steamHeroPath is not null)
        {
            _options.Add(new Option("Bannière Steam", null, steamHeroPath) { ThumbPath = steamHeroPath, PreviewText = "" });
        }
        InitialFocus = Options;
        Loaded += async (_, _) => await LoadIgdbAsync(loadIgdb, loadThumbnail);
    }

    public BackgroundChoice? Selected { get; private set; }

    private async Task LoadIgdbAsync(Func<Task<IReadOnlyList<IgdbBackground>>>? loadIgdb, Func<string, Task<string?>> loadThumbnail)
    {
        if (loadIgdb is null)
        {
            StatusText.Text = FrenchText.Typeset(_options.Count == 0
                ? "Pour des fonds IGDB, renseignez vos identifiants IGDB dans Paramètres. Vous pouvez aussi choisir une image sur le PC."
                : "Pour plus de fonds (IGDB), renseignez vos identifiants IGDB dans Paramètres.");
            return;
        }

        StatusText.Text = "Recherche des fonds sur IGDB…";
        try
        {
            var backgrounds = await loadIgdb();
            // Numérotées : chaque aperçu a un nom distinct pour les lecteurs d'écran.
            var (artworks, screenshots) = (0, 0);
            foreach (var background in backgrounds)
            {
                var label = background.Kind == BackgroundKind.Artwork ? $"Illustration {++artworks} (IGDB)" : $"Capture d'écran {++screenshots} (IGDB)";
                _options.Add(new Option(label, background.ImageId, null));
            }
            StatusText.Text = FrenchText.Typeset(_options.Count == 0
                ? "Aucun fond trouvé pour ce jeu. Choisissez une image sur le PC."
                : $"{FrenchText.Count(_options.Count, "fond proposé", "fonds proposés")} : choisissez-en un.");

            // Aperçus un par un, dans l'ordre affiché.
            foreach (var option in _options.Where(o => o.ImageId is not null).ToList())
            {
                if (!IsLoaded) return; // dialogue fermé entre-temps
                option.ThumbPath = await loadThumbnail(option.ImageId!);
                option.PreviewText = option.ThumbPath is null ? "Aperçu indisponible" : "";
            }
        }
        catch (Exception)
        {
            StatusText.Text = FrenchText.Typeset("Fonds IGDB indisponibles : vérifiez la connexion à Internet et vos identifiants IGDB dans Paramètres.");
        }
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UseButton.IsEnabled = Options.SelectedItem is not null;

    private void OnDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Options.SelectedItem is not null) OnUse(sender, e);
    }

    private void OnUse(object sender, RoutedEventArgs e)
    {
        if (Options.SelectedItem is not Option option) return;
        Selected = option.ImageId is { } imageId ? new BackgroundChoice(BackgroundSource.Igdb, ImageId: imageId) : new BackgroundChoice(BackgroundSource.File, Path: option.Path);
        DialogResult = true;
    }

    private void OnOriginal(object sender, RoutedEventArgs e)
    {
        Selected = new BackgroundChoice(BackgroundSource.Original);
        DialogResult = true;
    }

    /// <summary>Image du PC : refusée (avec un message, le dialogue reste ouvert) si Windows ne sait pas la lire.</summary>
    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var picker = new OpenFileDialog
        {
            Title = "Choisir une image de fond",
            Filter = "Images (*.jpg, *.jpeg, *.png, *.bmp)|*.jpg;*.jpeg;*.png;*.bmp",
        };
        if (picker.ShowDialog(this) != true) return;
        if (!CanDecode(picker.FileName))
        {
            StatusText.Text = FrenchText.Typeset($"« {Path.GetFileName(picker.FileName)} » n'est pas une image lisible : choisissez un fichier JPEG, PNG ou BMP.");
            return;
        }
        Selected = new BackgroundChoice(BackgroundSource.File, Path: picker.FileName);
        DialogResult = true;
    }

    private static bool CanDecode(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames.Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or FileFormatException or ArgumentException)
        {
            return false;
        }
    }

    private sealed class Option(string label, string? imageId, string? path) : INotifyPropertyChanged
    {
        public string Label { get; } = label;

        public string? ImageId { get; } = imageId;

        public string? Path { get; } = path;

        public string? ThumbPath
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Texte du remplaçant : « … » pendant le chargement, « Aperçu indisponible » en cas d'échec.</summary>
        public string PreviewText
        {
            get;
            set
            {
                field = value;
                OnPropertyChanged();
            }
        } = "…";

        public event PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => Label;

        private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
