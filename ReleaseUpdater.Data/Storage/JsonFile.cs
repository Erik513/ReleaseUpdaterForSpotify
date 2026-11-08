using System.Text.Json;

namespace ReleaseUpdater.Data.Storage;

/// <summary>
/// What the three JSON stores (settings, token, tracked songs) have in common: one set of
/// serializer options (indented, snake_case - the format of the files users already have)
/// and one way of reading ("nothing usable there" is not an error) and writing (a temp file,
/// then a replace, see <see cref="AtomicFile"/>).
/// </summary>
internal static class JsonFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Reads and deserializes the file. A missing, empty, unreadable or damaged file - or one that
    /// holds JSON of another shape, or "null" - gives <c>null</c>: the caller falls back to its
    /// defaults, so one bad file never keeps the app from starting.
    /// </summary>
    public static T? TryRead<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);

            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Serializes and writes the value atomically; a failure is thrown to the caller.</summary>
    public static void Write<T>(string path, T value)
    {
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(value, Options));
    }
}
