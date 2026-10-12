namespace Rose.Application.Abstractions;

/// <summary>Reads an uploaded .csv / .xlsx into rows keyed by normalised header ("due date").</summary>
public interface IUploadReader
{
    /// <summary>Sheets in file order. A CSV gives one sheet named "".</summary>
    IReadOnlyList<UploadSheet> Read(string fileName, byte[] content);
}

/// <param name="Name">The sheet name ("" for CSV).</param>
/// <param name="Rows">Non-empty data rows. Values are string, double, bool, DateTime, TimeSpan or null.</param>
public sealed record UploadSheet(string Name, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows);