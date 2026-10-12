using System.Text;
using Rose.Domain.Common;
using Rose.Infrastructure.Importing;

namespace Rose.UnitTests.Infrastructure;

public class UploadReaderTests
{
    private readonly UploadReader _reader = new();

    [Fact]
    public void Parse_handles_quotes_commas_and_newlines()
    {
        var rows = CsvParser.Parse("Item,Notes\r\n\"Rent, flat\",\"said \"\"hi\"\"\"\nGym,\"two\nlines\"\n");

        Assert.Equal(3, rows.Count);
        Assert.Equal("Rent, flat", rows[1][0]);
        Assert.Equal("said \"hi\"", rows[1][1]);
        Assert.Equal("two\nlines", rows[2][1]);
    }

    [Fact]
    public void Parse_detects_semicolon_files()
    {
        var rows = CsvParser.Parse("Item;Amount\nRent;1200,50\n");

        Assert.Equal("1200,50", rows[1][1]);
    }

    [Fact]
    public void Decode_strips_the_bom_and_falls_back_to_windows_1252()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF, .. "Item"u8];
        byte[] cp1252 = [0x43, 0x61, 0x66, 0xE9]; // "Caf\u00e9" in Windows-1252

        Assert.Equal("Item", CsvParser.Decode(bom));
        Assert.Equal("Caf\u00e9", CsvParser.Decode(cp1252));
    }

    [Fact]
    public void Read_keys_rows_by_normalised_header()
    {
        var sheets = _reader.Read("budget.csv", Encoding.UTF8.GetBytes("Item, Due Date ,Amount\nRent,2024-01-01,5\n,,\n"));

        var row = Assert.Single(Assert.Single(sheets).Rows);
        Assert.Equal("Rent", row["item"]);
        Assert.Equal("2024-01-01", row["due date"]);
    }

    [Theory]
    [InlineData("x.csv", new byte[0], "The uploaded file is empty.")]
    [InlineData("x.pdf", new byte[] { 1 }, "Upload a .csv or .xlsx file.")]
    public void Read_rejects_bad_files(string name, byte[] content, string message) =>
        Assert.Equal(message, Assert.Throws<ValidationException>(() => _reader.Read(name, content)).Message);
}