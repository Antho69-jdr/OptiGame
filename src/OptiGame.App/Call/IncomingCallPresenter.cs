using System.ComponentModel;
using System.Media;
using System.Windows;
using OptiGame.App.ViewModels;

namespace OptiGame.App.Call;

/// <summary>
/// Appel entrant d'un ami Steam : petite fenêtre « … vous appelle » (Répondre / Refuser) au premier plan, en bas à droite, et sonnerie,
/// tant que <see cref="CallViewModel.IncomingCall"/> existe. Visible même pendant une partie en fenêtre ou sans bordure (pas
/// par-dessus un jeu en plein écran exclusif : la sonnerie et la notification restent). Ne prend jamais le clavier au jeu.
/// </summary>
public sealed class IncomingCallPresenter(CallViewModel call)
{
    private IncomingCallWindow? _window;
    private SoundPlayer? _ring;

    public void Start() => call.PropertyChanged += OnCallChanged;

    private void OnCallChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CallViewModel.IncomingCall)) return;
        if (call.IncomingCall is not null) Show();
        else Hide();
    }

    private void Show()
    {
        if (_window is null)
        {
            var main = Application.Current.MainWindow;
            _window = new IncomingCallWindow { DataContext = call };
            _window.Show();
            // WPF fait de la première fenêtre ouverte la fenêtre principale : celle-ci ne doit jamais l'être (propriétaire des dialogues).
            if (ReferenceEquals(Application.Current.MainWindow, _window)) Application.Current.MainWindow = main;
            _window.PlaceAtCorner();
        }
        _ring ??= new SoundPlayer(RingTone());
        _ring.PlayLooping();
    }

    private void Hide()
    {
        _ring?.Stop();
        if (_window is null) return;
        if (ReferenceEquals(Application.Current?.MainWindow, _window)) Application.Current!.MainWindow = null;
        _window.Close();
        _window = null;
    }

    /// <summary>Sonnerie : deux notes douces, puis un silence (WAV 16 bits mono fabriqué en mémoire, rien sur le disque).</summary>
    private static MemoryStream RingTone()
    {
        const int rate = 22050;
        var samples = new List<short>();
        void Tone(double frequency, double seconds)
        {
            var count = (int)(rate * seconds);
            for (var i = 0; i < count; i++)
            {
                var envelope = Math.Min(1, Math.Min(i, count - i) / (rate * 0.02)); // montée et descente de 20 ms : pas de clic
                samples.Add((short)(Math.Sin(2 * Math.PI * frequency * i / rate) * envelope * short.MaxValue * 0.22));
            }
        }
        Tone(784, 0.35);
        Tone(988, 0.45);
        samples.AddRange(Enumerable.Repeat((short)0, (int)(rate * 1.4)));

        var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, leaveOpen: true))
        {
            var data = samples.Count * 2;
            writer.Write("RIFF"u8);
            writer.Write(36 + data);
            writer.Write("WAVEfmt "u8);
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(rate);
            writer.Write(rate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write("data"u8);
            writer.Write(data);
            foreach (var sample in samples) writer.Write(sample);
        }
        stream.Position = 0;
        return stream;
    }
}
