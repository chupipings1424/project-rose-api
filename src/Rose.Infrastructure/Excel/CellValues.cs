using ClosedXML.Excel;

namespace Rose.Infrastructure.Excel;

internal static class CellValues
{
    /// <summary>A cell as a plain .NET value (like openpyxl's values_only): string, double, bool, DateTime, TimeSpan or null.</summary>
    public static object? Read(IXLCell cell)
    {
        var value = cell.HasFormula ? cell.CachedValue : cell.Value;
        return value.Type switch
        {
            XLDataType.Blank => null,
            XLDataType.Boolean => value.GetBoolean(),
            XLDataType.Number => value.GetNumber(),
            XLDataType.Text => value.GetText(),
            XLDataType.DateTime => value.GetDateTime(),
            XLDataType.TimeSpan => value.GetTimeSpan(),
            XLDataType.Error => "#" + value.GetError(),
            _ => null,
        };
    }

    /// <summary>Python truthiness of a cell: blank, "", 0 and False are all "empty".</summary>
    public static bool IsFalsy(object? value) => value switch
    {
        null => true,
        string s => s.Length == 0,
        double d => d == 0,
        bool b => !b,
        _ => false,
    };

    /// <summary><c>str(value or "")</c>.</summary>
    public static string Text(object? value) => IsFalsy(value) ? "" : Rose.Domain.Common.Coerce.ToText(value);
}