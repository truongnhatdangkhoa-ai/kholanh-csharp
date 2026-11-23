using ClosedXML.Excel;
using KhoLanh.Core.TonKho;

namespace KhoLanh.Core.TonKhoGui;

/// <summary>Đọc sheet Excel thành giá trị GỐC của ô (số = double, ngày = DateTime, chữ) — như sheet_to_json({header:1, defval:''}) của app cũ (khác SheetText: không làm tròn theo định dạng).</summary>
public static class GuiRaw
{
    public static List<object?[]> ReadRaw(string path, string sheet, SheetText.Book book)
    {
        if (Path.GetExtension(path).ToLowerInvariant() == ".xlsx" || Path.GetExtension(path).ToLowerInvariant() == ".xlsm")
        {
            try
            {
                using var wb = new XLWorkbook(path);
                if (wb.TryGetWorksheet(sheet, out var ws))
                {
                    var used = ws.RangeUsed(XLCellsUsedOptions.Contents); var rows = new List<object?[]>();
                    if (used is null) return rows;
                    int r1 = used.FirstRow().RowNumber(), r2 = used.LastRow().RowNumber(), c1 = used.FirstColumn().ColumnNumber(), c2 = used.LastColumn().ColumnNumber();
                    for (var r = r1; r <= r2; r++)
                    {
                        var arr = new object?[c2 - c1 + 1];
                        for (var c = c1; c <= c2; c++)
                        {
                            var cell = ws.Cell(r, c); XLCellValue v; try { v = cell.HasFormula ? cell.CachedValue : cell.Value; } catch { arr[c - c1] = cell.GetFormattedString(); continue; }
                            arr[c - c1] = v.Type switch
                            {
                                XLDataType.Blank => "", XLDataType.Number => v.GetNumber(), XLDataType.DateTime => v.GetDateTime().ToOADate(), XLDataType.Boolean => v.GetBoolean() ? "TRUE" : "FALSE",
                                XLDataType.TimeSpan => v.GetTimeSpan().TotalDays, XLDataType.Text => v.GetText(), _ => cell.GetFormattedString(),
                            };
                        }
                        rows.Add(arr);
                    }
                    return rows;
                }
            }
            catch { /* rơi về bản chữ */ }
        }
        return (book.Sheets.TryGetValue(sheet, out var t) ? t : new()).Select(r => r.Select(x => (object?)x).ToArray()).ToList();
    }
}
