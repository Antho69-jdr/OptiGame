using System.Globalization;

namespace OptiGame.Core.Logging;

/// <summary>
/// Journal texte minimal (logs\optigame.log), pour diagnostiquer la détection des jeux et les sessions.
/// Rotation simple à 1 Mo (un seul fichier .old conservé).
/// </summary>
public sealed class FileLog(string directory)
{
    private const long MaxBytes = 1024 * 1024;
    private readonly Lock _lock = new();

    public string FilePath { get; } = Path.Combine(directory, "optigame.log");

    public void Info(string message) => Write("INFO ", message);

    public void Warn(string message) => Write("WARN ", message);

    public void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} — {ex.GetType().Name}: {ex.Message}");

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} {level} {message}{Environment.NewLine}";
        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(directory);
                if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                {
                    File.Move(FilePath, FilePath + ".old", overwrite: true);
                }
                File.AppendAllText(FilePath, line);
            }
            catch (IOException)
            {
                // Le journal ne doit jamais faire échouer l'appli.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
