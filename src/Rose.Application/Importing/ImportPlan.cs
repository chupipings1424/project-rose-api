using System.Text.Json.Serialization;

namespace Rose.Application.Importing;

public static class ImportAction
{
    public const string Add = "add";
    public const string Replace = "replace";
    public const string Skip = "skip";
    public const string Error = "error";
}

/// <summary>What an import did (or would do, for a preview) to every row of the file.</summary>
public sealed record ImportPlan(
    string Kind,
    string Mode,
    bool Applied,
    IReadOnlyList<ImportEntry> Entries,
    ImportCounts Counts,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<int>? Years = null)
{
    public static ImportPlan Summarize(string kind, IReadOnlyList<ImportEntry> entries, string mode) => new(
        kind,
        mode,
        false,
        entries,
        new ImportCounts(
            entries.Count(e => e.Action == ImportAction.Add),
            entries.Count(e => e.Action == ImportAction.Replace),
            entries.Count(e => e.Action == ImportAction.Skip),
            entries.Count(e => e.Action == ImportAction.Error),
            entries.Count));
}

public sealed record ImportCounts(int Add, int Replace, int Skip, int Error, int Total);

/// <summary>One row of the preview. Only the fields that apply to the row are sent.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Skip)]
public sealed class ImportEntry
{
    public int Row { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Kind { get; set; }
    public string Item { get; set; } = "";
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ExistingId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Derived { get; set; }

    // budget lines
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Section { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Group { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Year { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double[]? Amounts { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Total { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Notes { get; set; }

    // accounts (titles)
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? TotalAmount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? StartDate { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? DueDate { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Frequency { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Instalments { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? PaymentAmount { get; set; }

    // payments
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Date { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public double? Amount { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Status { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Time { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; set; }

    internal ImportEntry Fail(string reason)
    {
        Action = ImportAction.Error;
        Reason = reason;
        return this;
    }
}