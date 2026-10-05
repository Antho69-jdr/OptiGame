using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using OptiGame.App.ViewModels;
using OptiGame.App.Views;
using OptiGame.Core.Profiles;

namespace OptiGame.App.Snapshots;

/// <summary>
/// Mode de développement « --snapshot &lt;dossier&gt; » : chaque page de la fenêtre principale est rendue en PNG, à plusieurs
/// tailles de fenêtre, pour comparer l'interface avant / après une modification (scripts\ui-snapshots.ps1). Lecture seule :
/// ni détection des jeux, ni reprise de session, ni dock, ni icône de notification, ni mise à jour. Refusé sans
/// OPTIGAME_DATA_DIR, pour ne jamais tourner sur les vraies données.
/// </summary>
internal static class PageSnapshots
{
    public const string Argument = "--snapshot";
    private const string SizesArgument = "--sizes";
    private const string GameArgument = "--game";
    private const string OnlyArgument = "--only";

    /// <summary>Tailles par défaut (DIP) : défaut actuel, minimum, zone de travail à 150 % d'un écran 1080p, grand écran.</summary>
    private static readonly Size[] DefaultSizes = [new(1240, 860), new(880, 600), new(1280, 680), new(1600, 1000)];

    public static bool IsRequested(string[] args) => args.Contains(Argument, StringComparer.OrdinalIgnoreCase);

    public static async Task RunAsync(IServiceProvider services, string[] args)
    {
        var output = ValueAfter(args, Argument) ?? throw new ArgumentException($"{Argument} <dossier> attendu.");
        Directory.CreateDirectory(output);
        var report = new StringBuilder();
        void Log(string line)
        {
            report.AppendLine($"{DateTime.Now:HH:mm:ss} {line}");
            File.WriteAllText(Path.Combine(output, "snapshot.log"), report.ToString(), Encoding.UTF8);
        }

        try
        {
            var sizes = ParseSizes(ValueAfter(args, SizesArgument)) ?? DefaultSizes;
            // --only 7,0 : seulement les captures dont le nom commence par l'un de ces préfixes (pages, dialogues, galerie).
            var only = ValueAfter(args, OnlyArgument)?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool Wanted(string name) => only is null || only.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            var main = services.GetRequiredService<MainViewModel>();
            var library = services.GetRequiredService<LibraryViewModel>();
            var window = services.GetRequiredService<MainWindow>();
            // Hors des écrans, sans activation ni bouton de barre des tâches : rien ne vient gêner l'utilisateur.
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = -32000;
            window.Top = -32000;
            window.ShowActivated = false;
            window.ShowInTaskbar = false;
            window.Width = sizes[0].Width;
            window.Height = sizes[0].Height;
            window.Show();

            var pages = new List<(string Name, Func<Task> Open)>
            {
                ("1-mes-jeux", async () => { main.Navigate(library); await Settle(4000); }),
                ("2-diagnostic", async () => { main.Navigate(main.Diagnostic); await WaitUntil(() => !main.Diagnostic.IsBusy && main.Diagnostic.HasResults, 60_000); }),
                ("3-pilotes", async () =>
                {
                    var drivers = main.NavItems[2].Page;
                    main.Navigate(drivers);
                    await Settle(500);
                    await WaitUntil(() => drivers is DriversViewModel { IsSearching: false }, 90_000);
                }),
                ("4-mesures", async () => { main.Navigate(main.Measures); await Settle(1500); }),
                ("5-parametres", async () => { main.Navigate(main.Settings); await Settle(1500); }),
            };
            if (PickGame(services.GetRequiredService<ProfileStore>(), ValueAfter(args, GameArgument)) is { } game)
            {
                pages.Add(("6-fiche-du-jeu", async () => { main.Navigate(library); library.ShowGame(game.Id); await Settle(4000); }));
            }

            foreach (var (name, open) in pages.Where(p => Wanted(p.Name)))
            {
                await open();
                foreach (var size in sizes)
                {
                    window.Width = size.Width;
                    window.Height = size.Height;
                    await Settle(700);
                    var file = Path.Combine(output, $"{name}-{size.Width:0}x{size.Height:0}.png");
                    Save(window, file);
                    Log($"{Path.GetFileName(file)}");
                }
            }
            window.CloseForGame();

            foreach (var (name, create) in DialogSnapshots.All().Where(d => Wanted(d.Name)))
            {
                var dialog = create();
                dialog.WindowStartupLocation = WindowStartupLocation.Manual;
                dialog.Left = -32000;
                dialog.Top = -32000;
                dialog.ShowActivated = false;
                dialog.Show();
                await Settle(800);
                Save(dialog, Path.Combine(output, $"{name}.png"));
                Log($"{name}.png");
                dialog.Close();
            }

            // Galerie : mise en page hors fenêtre (sa hauteur dépasse l'écran), sur le fond de la fenêtre.
            if (!Wanted("0-galerie"))
            {
                Log("Terminé.");
                return;
            }
            var gallery = new ComponentGallery { Background = (Brush)Application.Current.FindResource("Brush.Window") };
            gallery.Measure(new Size(1000, double.PositiveInfinity));
            gallery.Arrange(new Rect(new Size(1000, gallery.DesiredSize.Height)));
            gallery.UpdateLayout();
            SaveElement(gallery, Path.Combine(output, "0-galerie.png"));
            Log("0-galerie.png");
            Log("Terminé.");
        }
        catch (Exception ex)
        {
            Log($"ÉCHEC : {ex}");
        }
    }

    private static GameProfile? PickGame(ProfileStore store, string? name)
    {
        var profiles = store.GetAll();
        return name is { Length: > 0 }
            ? profiles.FirstOrDefault(p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase))
            : profiles.Where(p => p.SteamAppId is not null && File.Exists(p.ExePath)).OrderBy(p => p.Name).FirstOrDefault()
              ?? profiles.FirstOrDefault();
    }

    /// <summary>Contenu de la fenêtre (sans le cadre Windows) sur son fond, à 96 ppp : 1 pixel = 1 unité WPF.</summary>
    private static void Save(Window window, string path)
    {
        window.UpdateLayout();
        Render((FrameworkElement)window.Content, window.Background, path);
    }

    private static void SaveElement(System.Windows.Controls.Control element, string path) => Render(element, element.Background, path);

    private static void Render(FrameworkElement root, Brush background, string path)
    {
        var size = new Size(root.ActualWidth, root.ActualHeight);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(background, null, new Rect(size));
            dc.DrawRectangle(new VisualBrush(root) { Stretch = Stretch.None, ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(size) },
                null, new Rect(size));
        }
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width), (int)Math.Ceiling(size.Height), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    /// <summary>Laisse la mise en page, les liaisons et les lectures asynchrones se faire.</summary>
    private static async Task Settle(int milliseconds)
    {
        await Task.Delay(milliseconds);
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMilliseconds)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250);
        }
        await Settle(500);
    }

    private static string? ValueAfter(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>« 1240x860,880x600 » → tailles en unités WPF.</summary>
    private static Size[]? ParseSizes(string? value) =>
        value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Split('x'))
            .Select(p => new Size(double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture)))
            .ToArray();
}
