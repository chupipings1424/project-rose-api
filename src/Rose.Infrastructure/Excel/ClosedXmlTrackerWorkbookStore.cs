using ClosedXML.Excel;
using Microsoft.Extensions.Options;
using Rose.Application.Abstractions;

namespace Rose.Infrastructure.Excel;

/// <summary>
/// payments.xlsx through ClosedXML. One session at a time (a semaphore), so concurrent
/// requests queue up instead of overwriting each other's changes.
/// </summary>
public sealed class ClosedXmlTrackerWorkbookStore(IOptions<RoseOptions> options, TimeProvider clock)
    : ITrackerWorkbookStore, IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public string WorkbookPath { get; } = Path.GetFullPath(options.Value.ResolvedWorkbookPath);

    public async Task<ITrackerWorkbook> OpenAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(WorkbookPath)) CreateEmpty();
            var bytes = await ReadSharedAsync(cancellationToken);
            var workbook = new XLWorkbook(new MemoryStream(bytes));
            return new ClosedXmlTrackerWorkbook(workbook, WorkbookPath, clock, () => gate.Release());
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    public async Task<byte[]> ReadAllBytesAsync(CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(WorkbookPath)) CreateEmpty();
            return await ReadSharedAsync(cancellationToken);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose() => gate.Dispose();

    /// <summary>Reads even while Excel has the file open.</summary>
    private async Task<byte[]> ReadSharedAsync(CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            WorkbookPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>An empty workbook with both sheets and styled headers.</summary>
    private void CreateEmpty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(WorkbookPath)!);
        using var workbook = new XLWorkbook();
        WorkbookLayout.StyleHeader(
            workbook.Worksheets.Add(WorkbookLayout.AccountsSheet), WorkbookLayout.AccountHeaders, WorkbookLayout.AccountWidths, 0);
        WorkbookLayout.StyleHeader(
            workbook.Worksheets.Add(WorkbookLayout.PaymentsSheet), WorkbookLayout.PaymentHeaders, WorkbookLayout.PaymentWidths, 0);
        AtomicSave(workbook, WorkbookPath);
    }

    /// <summary>
    /// Save to a sibling temp file, then replace. Direct repeated saves can fail on synced
    /// folders (OneDrive) that hold a short lock on the file.
    /// </summary>
    internal static void AtomicSave(XLWorkbook workbook, string path)
    {
        var temp = Path.Combine(Path.GetDirectoryName(path)!, $"tmp{Guid.NewGuid():N}.xlsx");
        try
        {
            workbook.SaveAs(temp);
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(temp, path, overwrite: true);
                    return;
                }
                catch (Exception error) when (attempt < 5 && error is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(100 * attempt);
                }
            }
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}