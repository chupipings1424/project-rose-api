using System.Text.Json;
using Rose.Application.Abstractions;
using Rose.Application.Common;

namespace Rose.Infrastructure.Storage;

/// <summary>
/// A tiny JSON "database": the whole value is written on every change, to a temp file first
/// and then renamed over the real one, so a crash mid-write never leaves half a file behind.
/// </summary>
public sealed class JsonDataFile<T>(string path, Func<T> fallback) : IDataFile<T>
{
    private readonly Lock gate = new();

    public string FilePath { get; } = path;

    public T Read()
    {
        lock (gate)
        {
            if (!File.Exists(FilePath)) return fallback();
            try
            {
                return JsonSerializer.Deserialize<T>(File.ReadAllText(FilePath), RoseJson.Files) ?? fallback();
            }
            catch (JsonException error)
            {
                throw new InvalidOperationException($"{FilePath} is not valid JSON - fix or delete it. ({error.Message})", error);
            }
        }
    }

    public void Write(T value)
    {
        lock (gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, RoseJson.Files));
            File.Move(temp, FilePath, overwrite: true);
        }
    }
}