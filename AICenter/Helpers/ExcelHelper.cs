using ClosedXML.Excel;
using System.Data;

namespace BuildWorkflow.AICenter.Helpers;

public static class ExcelHelper
{
    public static byte[] CreateWorkbook(IEnumerable<(string SheetName, IEnumerable<IEnumerable<object?>> Rows)> sheets)
    {
        using var workbook = new XLWorkbook();

        foreach (var sheet in sheets)
        {
            var worksheet = workbook.Worksheets.Add(sheet.SheetName);
            var rowNumber = 1;

            foreach (var row in sheet.Rows)
            {
                var columnNumber = 1;
                foreach (var value in row)
                {
                    worksheet.Cell(rowNumber, columnNumber).Value = XLCellValue.FromObject(value);
                    columnNumber++;
                }
                rowNumber++;
            }

            worksheet.Columns().AdjustToContents();
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public static void WriteWorkbook(string path, IEnumerable<(string SheetName, IEnumerable<IEnumerable<object?>> Rows)> sheets)
    {
        var bytes = CreateWorkbook(sheets);
        File.WriteAllBytes(path, bytes);
    }

    public static DataTable ReadSheet(string path, string? sheetName = null)
    {
        using var workbook = new XLWorkbook(path);
        var worksheet = sheetName == null
            ? workbook.Worksheet(1)
            : workbook.Worksheet(sheetName);

        var table = new DataTable(worksheet.Name);
        var range = worksheet.RangeUsed();
        if (range == null)
        {
            return table;
        }

        var firstRow = true;
        foreach (var row in range.Rows())
        {
            if (firstRow)
            {
                foreach (var cell in row.Cells())
                {
                    var header = cell.GetValue<string>();
                    table.Columns.Add(string.IsNullOrWhiteSpace(header) ? $"Column{cell.Address.ColumnNumber}" : header);
                }
                firstRow = false;
                continue;
            }

            var dataRow = table.NewRow();
            var index = 0;
            foreach (var cell in row.Cells())
            {
                if (index < table.Columns.Count)
                {
                    dataRow[index] = cell.IsEmpty() ? DBNull.Value : cell.GetValue<object>();
                }
                index++;
            }
            table.Rows.Add(dataRow);
        }

        return table;
    }
}
