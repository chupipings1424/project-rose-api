namespace Rose.Application.Abstractions;

/// <summary>A small JSON "database" file: the whole value is read and written at once.</summary>
public interface IDataFile<T>
{
    /// <summary>The saved value, or the fallback when the file does not exist yet.</summary>
    T Read();

    void Write(T value);
}