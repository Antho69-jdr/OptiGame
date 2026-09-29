using System.Text.Json;
using System.Text.Json.Serialization;

namespace OptiGame.Core.State;

public interface IStateStore<T> where T : class
{
    bool Exists { get; }

    /// <summary>Renvoie null si le fichier n'existe pas. Lève <see cref="StateFileCorruptException"/> s'il est illisible.</summary>
    T? Load();

    void Save(T document);

    void Delete();
}

public sealed class StateFileCorruptException(string path, Exception inner)
    : Exception($"Le fichier d'état « {path} » est illisible. Il n'a pas été modifié ; vérifiez-le avant de continuer.", inner)
{
    public string FilePath { get; } = path;
}

/// <summary>
/// Stockage JSON avec écriture atomique : on écrit un fichier temporaire (vidé sur disque), puis on le renomme
/// par-dessus l'original. Un crash pendant l'écriture laisse donc toujours l'ancienne version intacte.
/// </summary>
public sealed class JsonStateStore<T>(string path) : IStateStore<T> where T : class
{
    public static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private string TempPath => path + ".tmp";

    public bool Exists => File.Exists(path);

    public T? Load()
    {
        // Un .tmp résiduel vient d'une écriture interrompue avant le renommage : l'original fait foi,
        // et rien n'a été appliqué avant la fin de cette écriture (write-ahead).
        if (File.Exists(TempPath))
        {
            File.Delete(TempPath);
        }

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, SerializerOptions)
                ?? throw new JsonException("Document vide.");
        }
        catch (JsonException ex)
        {
            throw new StateFileCorruptException(path, ex);
        }
    }

    public void Save(T document)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        using (var stream = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, document, SerializerOptions);
            stream.Flush(flushToDisk: true);
        }

        File.Move(TempPath, path, overwrite: true);
    }

    public void Delete()
    {
        File.Delete(path);
        File.Delete(TempPath);
    }
}
