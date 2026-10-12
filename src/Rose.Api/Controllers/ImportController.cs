using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Rose.Application.Importing;

namespace Rose.Api.Controllers;

/// <summary>The multipart form both import endpoints accept.</summary>
public sealed class ImportForm
{
    /// <summary>A .csv or .xlsx file (max 10 MB).</summary>
    public IFormFile? File { get; set; }

    /// <summary>skip (default) or replace.</summary>
    public string? Mode { get; set; }

    /// <summary>"1" (default) only previews; "0", "false" or an empty value writes.</summary>
    [FromForm(Name = "dry_run")]
    [DisplayFormat(ConvertEmptyStringToNull = false)]
    public string? DryRun { get; set; }

    /// <summary>Budget only: the year for rows without one.</summary>
    public string? Year { get; set; }
}

[Tags("Import")]
public sealed class ImportController(ImportService imports) : TrackerControllerBase
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;
    private const long MaxRequestBytes = MaxUploadBytes + 1024 * 1024;

    private static readonly Dictionary<string, string> Samples = new()
    {
        ["budget"] = "budget-2024.csv",
        ["payments"] = "payments-2024.csv",
    };

    /// <summary>Preview or apply a budget import.</summary>
    [HttpPost("import/budget")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ImportPlan> Budget([FromForm] ImportForm form, CancellationToken ct)
    {
        var (name, bytes) = await ReadAsync(form, ct);
        return await imports.BudgetAsync(name, bytes, form.Mode ?? "skip", form.Year, IsDryRun(form), ct);
    }

    /// <summary>Preview or apply a payment-history import (optionally with an Accounts sheet).</summary>
    [HttpPost("import/payments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    public async Task<ImportPlan> Payments([FromForm] ImportForm form, CancellationToken ct)
    {
        var (name, bytes) = await ReadAsync(form, ct);
        return await imports.PaymentsAsync(name, bytes, form.Mode ?? "skip", IsDryRun(form), ct);
    }

    /// <summary>Download a sample file to fill in (budget or payments).</summary>
    /// <response code="404">Unknown sample.</response>
    [HttpGet("import/sample/{kind}")]
    [Produces("text/csv")]
    [ProducesResponseType<FileStreamResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public IActionResult Sample(string kind)
    {
        if (!Samples.TryGetValue(kind, out var fileName))
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: "Unknown sample.");
        var stream = typeof(ImportController).Assembly.GetManifestResourceStream($"Rose.Api.Samples.{fileName}");
        return stream is null
            ? Problem(statusCode: StatusCodes.Status404NotFound, detail: "Sample file is missing.")
            : File(stream, "text/csv", fileName);
    }

    private static bool IsDryRun(ImportForm form) => (form.DryRun ?? "1") is not ("0" or "false" or "False" or "");

    private static async Task<(string Name, byte[] Bytes)> ReadAsync(ImportForm form, CancellationToken ct)
    {
        if (form.File is not { FileName.Length: > 0 } file)
            throw new Domain.Common.ValidationException("Choose a .csv or .xlsx file to import.");
        if (file.Length > MaxUploadBytes) throw new Domain.Common.ValidationException("That file is larger than 10 MB.");
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return (file.FileName, buffer.ToArray());
    }
}