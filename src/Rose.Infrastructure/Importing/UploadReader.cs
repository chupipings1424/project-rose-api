using System.Text;
using ClosedXML.Excel;
using Rose.Application.Abstractions;
using Rose.Application.Importing;
using Rose.Domain.Common;
using Rose.Infrastructure.Excel;

namespace Rose.Infrastructure.Importing;

/// <summary>Reads an uploaded .csv or .xlsx into rows keyed by normalised header.</summary>
public sealed class UploadReader : IUploadReader
{
    public IReadOnlyList<UploadSheet> Read(string fileName, byte[] content)
    {
        if (content.Length == 0) throw new ValidationException("The uploaded file is empty.");
        var name = (fileName ?? "").ToLowerInvariant();

        if (name.EndsWith(".csv", StringComparison.Ordinal) || name.EndsWith(".txt", StringComparison.Ordinal))
            return [new UploadSheet("", ToRows(CsvParser.Parse(CsvParser.Decode(content))))];

        if (name.EndsWith(".xlsx", StringComparison.Ordinal) || name.EndsWith(".xlsm", StringComparison.Ordinal))
            return ReadWorkbook(content);

        throw new ValidationException("Upload a .csv or .xlsx file.");
    }

    private static List<UploadSheet> ReadWorkbook(byte[] content)
    {
        XLWorkbook workbook;
        try
        {
            workbook = new XLWorkbook(new MemoryStream(content));
        }
#pragma warning disable CA1031 // ClosedXML throws a grab-bag of exception types for bad files
        catch (Exception error)
#pragma warning restore CA1031
        {
            throw new ValidationException($"That file could not be read as an Excel workbook ({error.Message}).");
        }

        using (workbook)
        {
            var sheets = new List<UploadSheet>();
            foreach (var ws in workbook.Worksheets)
            {
                var lastRow = ws.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 0;
                var lastColumn = ws.LastColumnUsed(XLCellsUsedOptions.Contents)?.ColumnNumber() ?? 0;
                var table = new List<object?[]>();
                for (var r = 1; r <= lastRow; r++)
                {
                    var cells = new object?[lastColumn];
                    for (var c = 1; c <= lastColumn; c++) cells[c - 1] = CellValues.Read(ws.Cell(r, c));
                    // The header row is kept as-is; data rows that are completely empty are dropped.
                    if (r == 1 || cells.Any(v => !Coerce.IsBlank(v))) table.Add(cells);
                }
                sheets.Add(new UploadSheet(ws.Name, ToRows(table)));
            }
            return sheets;
        }
    }

    /// <summary>First row = headers; each later row becomes {header: value} (like Python's dict(zip(...))).</summary>
    private static List<IReadOnlyDictionary<string, object?>> ToRows(List<object?[]> table)
    {
        var rows = new List<IReadOnlyDictionary<string, object?>>();
        if (table.Count == 0) return rows;
        var headers = table[0].Select(ImportPlanner.Key).ToArray();
        foreach (var cells in table.Skip(1))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < Math.Min(headers.Length, cells.Length); i++) row[headers[i]] = cells[i];
            rows.Add(row);
        }
        return rows;
    }
}

/// <summary>A small, quote-aware CSV reader with delimiter sniffing (, ; tab |).</summary>
internal static class CsvParser
{
    private static readonly char[] Delimiters = [',', '\t', ';', '|'];

    /// <summary>UTF-8 (with or without BOM), then Windows-1252, then Latin-1 - the first that decodes cleanly.</summary>
    public static string Decode(byte[] bytes)
    {
        var start = bytes is [0xEF, 0xBB, 0xBF, ..] ? 3 : 0;
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
        }
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <summary>Rows with at least one non-blank cell.</summary>
    public static List<object?[]> Parse(string text)
    {
        var delimiter = Sniff(text.Length > 4096 ? text[..4096] : text);
        return [.. Split(text, delimiter)
            .Where(row => row.Any(cell => cell.Trim().Length > 0))
            .Select(row => row.Cast<object?>().ToArray())];
    }

    /// <summary>The delimiter that splits the sample's lines into the most consistent, non-trivial column count.</summary>
    internal static char Sniff(string sample)
    {
        var best = ',';
        var bestScore = 0;
        foreach (var delimiter in Delimiters)
        {
            var counts = Split(sample, delimiter).Where(r => r.Count > 0 && !(r.Count == 1 && r[0].Length == 0)).Select(r => r.Count - 1).ToList();
            if (counts.Count == 0 || counts[0] == 0) continue;
            var score = counts.Count(c => c == counts[0]) * 1000 + counts[0];
            if (score > bestScore)
            {
                best = delimiter;
                bestScore = score;
            }
        }
        return best;
    }

    private static List<List<string>> Split(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var fieldStarted = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(ch);
                }
                continue;
            }

            if (ch == '"' && field.Length == 0 && !fieldStarted)
            {
                quoted = true;
                fieldStarted = true;
            }
            else if (ch == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString());
                rows.Add(row);
                row = [];
                field.Clear();
                fieldStarted = false;
            }
            else
            {
                field.Append(ch);
                fieldStarted = true;
            }
        }

        if (field.Length > 0 || fieldStarted || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}