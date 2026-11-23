using System.Text;
using ExcelDataReader;

namespace KhoLanh.Core.TonKho;

/// <summary>Đọc file Excel 97-2003 (.xls) thành chữ đang hiện (giống SheetJS raw:false) — dùng ExcelDataReader + định dạng số của ô.</summary>
internal static class XlsBiff
{
    private static bool _reg;

    public static SheetText.Book Read(string path)
    {
        if (!_reg) { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); _reg = true; }
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var rd = ExcelReaderFactory.CreateReader(fs);
        var book = new SheetText.Book();
        do
        {
            var name = rd.Name;
            var rows = new List<string[]>();
            while (rd.Read())
            {
                var arr = new string[rd.FieldCount];
                for (var c = 0; c < rd.FieldCount; c++) arr[c] = Text(rd, c);
                rows.Add(arr);
            }
            // SheetJS lấy vùng !ref: bỏ dòng / cột trống ở đầu và cuối vùng dữ liệu
            var last = rows.FindLastIndex(r => r.Any(x => x.Length > 0));
            rows = last < 0 ? new() : rows.Take(last + 1).ToList();
            var first = rows.FindIndex(r => r.Any(x => x.Length > 0));
            if (first > 0) rows = rows.Skip(first).ToList();
            if (rows.Count > 0)
            {
                var w = rows.Max(r => r.Length);
                var lastCol = -1; var firstCol = w;
                foreach (var r in rows) for (var c = 0; c < r.Length; c++) if (r[c].Length > 0) { lastCol = Math.Max(lastCol, c); firstCol = Math.Min(firstCol, c); }
                rows = rows.Select(r => Enumerable.Range(firstCol, lastCol - firstCol + 1).Select(c => c < r.Length ? r[c] : "").ToArray()).ToList();
            }
            book.SheetNames.Add(name);
            book.Sheets[name] = rows;
        } while (rd.NextResult());
        return book;
    }

    private static string Text(IExcelDataReader rd, int c)
    {
        var v = rd.GetValue(c);
        switch (v)
        {
            case null: return "";
            case string s: return s;
            case bool b: return b ? "TRUE" : "FALSE";
            case double d: return SheetText.FormatNumber(d, rd.GetNumberFormatString(c), rd.GetNumberFormatIndex(c));
            case int i: return SheetText.FormatNumber(i, rd.GetNumberFormatString(c), rd.GetNumberFormatIndex(c));
            case DateTime dt: return SheetText.FormatNumber(dt.ToOADate(), rd.GetNumberFormatString(c), rd.GetNumberFormatIndex(c));
            default: return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        }
    }
}
