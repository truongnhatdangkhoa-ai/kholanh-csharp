using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.DoiChieu;

/// <summary>
/// Tab "Xuất kiểm" của Đối chiếu hàng ngày (port từ dcx-xuatkiem-core.js). Đầu vào: file GỐC của phần mềm kho (báo cáo "Nhập xuất chi tiết", ~41 cột).
/// Đầu ra: danh sách RÚT GỌN 8 cột của riêng loại chứng từ "Xuất kiểm hàng": Ngày chứng từ · Mã hàng (bỏ "TPC-") · Tên hàng · TLg Net · Số lượng tồn · Net · SIZE · Hợp đồng.
///  · chỉ lấy dòng Loại chứng từ = "Xuất kiểm hàng" (loại khác chỉ liệt kê; dòng tổng cuối file / dòng không mã, không ngày bị bỏ);
///  · GỘP theo (Ngày, Mã hàng): Số lượng tồn và Net cộng dồn; Tên hàng, TLg Net, Size, Hợp đồng lấy theo DÒNG ĐẦU của nhóm;
///  · Net rút gọn = TLg Net × Số lượng; nếu Net gốc cộng lại KHÁC TLg × SL thì giữ Net gốc và báo;
///  · SIZE lấy ở cột "Qui cách đông" (nhãn cột "Size" của file gốc lệch 1 cột: thực chất chứa mạ băng R00 / 20%); không có cột này thì dùng "Size";
///  · Hợp đồng = phần TRƯỚC dấu "-" đầu tiên ("26.10.044-21/09" → "26.10.044"; "ISHI-15/09/26" → "ISHI"); sắp theo Ngày rồi Mã hàng.
/// Tên hàng của file gốc bị cắt ~100 ký tự: <see cref="PlanTen"/> thay hẳn bằng tên chuẩn theo MÃ (bỏ đuôi " - đặc tính - hợp đồng-lô").
/// </summary>
public static class XuatKiem
{
    public sealed class Group
    {
        public string Ngay = "", Ma = "", Ten = "", Size = "", HdFull = "", Hopdong = "", DacTinh = "";
        public double? Tlg;
        public double Sl, Net;
        public int NDong, Row;
        public bool NetLech;
    }
    public sealed record Other(string Loai, int N);
    public sealed class Result
    {
        public bool Ok, Rutgon; public string Err = "";   // Rutgon: nạp lại FILE RÚT GỌN (8 cột, không có "Loại chứng từ") — coi mọi dòng có mã + ngày là Xuất kiểm hàng
        public int Header, NRows, NKiem, BoQua, NGop;
        public List<Other> Khac = new();
        public List<Group> Groups = new();
        public List<string> Warn = new();
        public double TotSl, TotNet;
    }
    public sealed record TenChange(string Key, string Ngay, string Ma, string Cu, string Moi, string Src);
    public sealed record TenPlan(List<TenChange> List, int Same, int NoName);

    public static readonly string[] Head = { "Ngày chứng từ", "Mã hàng", "Tên hàng", "TLg Net", "Số lượng tồn", "Net", "SIZE", "Hợp đồng" };

    private static string Strip(object? s) =>
        Regex.Replace(Regex.Replace(new string(Regex.Replace(Convert.ToString(s, CultureInfo.InvariantCulture) ?? "", "_x000d_", " ", RegexOptions.IgnoreCase).Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('đ', 'd').Replace('Đ', 'd').ToLowerInvariant(), @"\s+", " "), @"^\s+|\s+$", "");
    public static string NormMa(object? v) => Regex.Replace(Regex.Replace((Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").Normalize(NormalizationForm.FormC).ToUpperInvariant(), @"\s+", ""), "^TPC-", "");
    private static string Str(object? v) => v is null ? "" : Regex.Replace(Regex.Replace(Convert.ToString(v, CultureInfo.InvariantCulture) ?? "", "_x000d_", " ", RegexOptions.IgnoreCase), @"[\r\n]+", " ").Trim();
    private static double R6(double x) => Math.Round(x * 1e6) / 1e6;
    private static bool IsXuatKiem(string loai) => Strip(loai).StartsWith("xuat kiem", StringComparison.Ordinal);

    private static readonly (string Key, string[] Names)[] Want =
    {
        ("ngay", new[] { "ngay chung tu" }), ("ma", new[] { "ma hang" }), ("ten", new[] { "ten hang" }), ("tlg", new[] { "tlg net" }), ("sl", new[] { "so luong ton", "so luong" }),
        ("net", new[] { "net" }), ("loai", new[] { "loai chung tu" }), ("size", new[] { "qui cach dong", "quy cach dong", "size" }), ("hd", new[] { "hop dong", "so hop dong" }), ("dt", new[] { "dac tinh" }),
    };
    private static readonly string[] RutgonKeys = { "ngay", "ma", "ten", "tlg", "sl", "net", "size", "hd" };
    private static readonly (string Key, string Label)[] Need = { ("ngay", "Ngày chứng từ"), ("ma", "Mã hàng"), ("sl", "Số lượng tồn"), ("net", "Net"), ("loai", "Loại chứng từ") };

    /// <summary>Tìm dòng tiêu đề (≤ 40 dòng đầu) và vị trí các cột theo TÊN cột.</summary>
    public static (int Header, Dictionary<string, int> Cols)? FindColumns(IReadOnlyList<object?[]> aoa)
    {
        for (var r = 0; r < Math.Min(aoa.Count, 40); r++)
        {
            var h = (aoa[r] ?? Array.Empty<object?>()).Select(Strip).ToList();
            if (!h.Contains("ma hang") || !h.Contains("ngay chung tu")) continue;
            var cols = new Dictionary<string, int>();
            foreach (var (k, names) in Want) foreach (var n in names) { var c = h.IndexOf(n); if (c >= 0) { cols[k] = c; break; } }
            return (r, cols);
        }
        return null;
    }

    private static object? At(object?[] row, int c) => c >= 0 && c < row.Length ? row[c] : null;

    /// <summary>aoa (mảng các dòng; ngày dạng số Excel / DateTime / chữ đều được) → kết quả rút gọn.</summary>
    public static Result Build(IReadOnlyList<object?[]> aoa)
    {
        var f = FindColumns(aoa);
        if (f is null) return new Result { Err = "Không tìm thấy dòng tiêu đề (cần có cột Ngày chứng từ và Mã hàng)." };
        var c = f.Value.Cols;
        // 1.169: file RÚT GỌN do chính tab này xuất ra (Ngày · Mã hàng · Tên hàng · TLg Net · Số lượng tồn · Net · SIZE · Hợp đồng) không có cột "Loại chứng từ"
        // → nhận ra bằng đủ 8 cột đó, mọi dòng có mã + ngày coi là Xuất kiểm hàng. Thiếu cột khác thì vẫn báo lỗi như file gốc.
        var rutgon = !c.ContainsKey("loai") && RutgonKeys.All(c.ContainsKey);
        var miss = Need.Where(n => !(rutgon && n.Key == "loai") && !c.ContainsKey(n.Key)).Select(n => n.Label).ToList();
        if (miss.Count > 0) return new Result { Err = "Thiếu cột: " + string.Join(", ", miss) + ". Hãy dùng file gốc “Nhập xuất chi tiết” của phần mềm kho (hoặc file rút gọn đã xuất từ tab này)." };
        var res = new Result { Ok = true, Header = f.Value.Header, Rutgon = rutgon };
        var other = new Dictionary<string, int>(); var otherOrder = new List<string>(); var byKey = new Dictionary<string, Group>(); var order = new List<Group>();
        for (var r = f.Value.Header + 1; r < aoa.Count; r++)
        {
            var row = aoa[r] ?? Array.Empty<object?>();
            var ma = NormMa(At(row, c["ma"])); var ngay = DcxEngine.ParseDate(At(row, c["ngay"]));
            var loai = rutgon ? (ma.Length > 0 || ngay is not null ? "Xuất kiểm hàng" : "") : Str(At(row, c["loai"]));
            if (loai.Length == 0 && ma.Length == 0 && ngay is null) continue;   // dòng trống
            res.NRows++;
            if (ma.Length == 0 || ngay is null) { res.BoQua++; continue; }       // dòng tổng / dòng thiếu mã hoặc ngày
            if (!IsXuatKiem(loai)) { var k = loai.Length > 0 ? loai : "(không loại)"; if (!other.ContainsKey(k)) { other[k] = 0; otherOrder.Add(k); } other[k]++; continue; }
            res.NKiem++;
            var sl = DcxEngine.ParseNum(At(row, c["sl"])); var net = DcxEngine.ParseNum(At(row, c["net"])); double? tlg = c.TryGetValue("tlg", out var ct) ? DcxEngine.ParseNum(At(row, ct)) : null;
            if (sl is null) { res.Warn.Add($"Dòng {r + 1} ({ma}): Số lượng tồn trống / không đọc được — tính 0."); sl = 0; }
            net ??= tlg is { } tt ? R6(tt * sl.Value) : 0;
            var key = ngay + "|" + ma;
            if (!byKey.TryGetValue(key, out var g))
            {
                var hdFull = c.TryGetValue("hd", out var ch) ? Str(At(row, ch)) : "";
                g = new Group
                {
                    Ngay = ngay, Ma = ma, Ten = c.TryGetValue("ten", out var cn) ? Str(At(row, cn)) : "", Tlg = tlg, Size = c.TryGetValue("size", out var cs) ? Str(At(row, cs)) : "",
                    HdFull = hdFull, Hopdong = hdFull.Split('-')[0].Trim(), DacTinh = c.TryGetValue("dt", out var cd) ? Str(At(row, cd)) : "", Row = r + 1,
                };
                byKey[key] = g; order.Add(g);
            }
            else if (tlg is { } t2 && g.Tlg is { } t1 && Math.Abs(t2 - t1) > 1e-9)
                res.Warn.Add($"Ngày {Dmy(ngay)} · {ma}: các dòng cùng mã có TLg Net khác nhau ({t1:0.###} / {t2:0.###}) — giữ {t1:0.###} của dòng đầu.");
            g.Sl = R6(g.Sl + sl.Value); g.Net = R6(g.Net + net.Value); g.NDong++;
        }
        res.Khac = otherOrder.Select(k => new Other(k, other[k])).ToList();
        order = order.OrderBy(g => g.Ngay, StringComparer.Ordinal).ThenBy(g => g.Ma, StringComparer.Ordinal).ToList();
        foreach (var g in order)
        {
            g.Tlg ??= g.Sl != 0 ? R6(g.Net / g.Sl) : 0;
            var tn = R6(g.Tlg.Value * g.Sl);
            g.NetLech = Math.Abs(tn - g.Net) > 0.0005;   // Net gốc ≠ TLg × SL → ghi Net gốc, không dùng công thức
            if (g.NetLech) res.Warn.Add($"Ngày {Dmy(g.Ngay)} · {g.Ma}: Net gốc {g.Net:0.###} ≠ TLg {g.Tlg:0.###} × SL {g.Sl:0.###} = {tn:0.###} — giữ Net gốc.");
            res.TotSl = R6(res.TotSl + g.Sl); res.TotNet = R6(res.TotNet + g.Net);
        }
        res.Groups = order; res.NGop = res.NKiem - order.Count;
        return res;
    }

    public static string Dmy(string k) { var m = Regex.Match(k ?? "", @"^(\d{4})-(\d{2})-(\d{2})$"); return m.Success ? $"{m.Groups[3].Value}/{m.Groups[2].Value}/{m.Groups[1].Value}" : k ?? ""; }

    /// <summary>Lập danh sách đổi tên: tên mới = tên chuẩn theo mã (bỏ phần đuôi " - đặc tính - hợp đồng-lô"). lookup(ma) → (tên, nguồn) hoặc null.</summary>
    public static TenPlan PlanTen(IEnumerable<Group> groups, Func<string, (string Ten, string Src)?> lookup)
    {
        var list = new List<TenChange>(); int same = 0, noName = 0;
        foreach (var g in groups)
        {
            var t = lookup(g.Ma); var moi = t is { } x ? x.Ten.Trim() : "";
            if (moi.Length == 0) { noName++; continue; }
            if (g.Ten.Trim() == moi) { same++; continue; }
            list.Add(new TenChange(g.Ngay + "|" + g.Ma, g.Ngay, g.Ma, g.Ten.Trim(), moi, t!.Value.Src));
        }
        return new TenPlan(list, same, noName);
    }

    // ------------------------------------------------------------------ đọc file gốc + xuất file rút gọn

    /// <summary>Đọc mọi sheet của file gốc (.xlsx / .xlsm): số → double, ngày → số ngày Excel, còn lại chữ.</summary>
    public static Dictionary<string, List<object?[]>> ReadBook(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(fs);
            var res = new Dictionary<string, List<object?[]>>();
            foreach (var ws in wb.Worksheets)
            {
                var used = ws.RangeUsed(); var aoa = new List<object?[]>(); if (used is null) { res[ws.Name] = aoa; continue; }
                int r2 = used.LastRow().RowNumber(), c2 = used.LastColumn().ColumnNumber();
                for (var r = 1; r <= r2; r++)
                {
                    var row = new object?[c2];
                    for (var c = 1; c <= c2; c++)
                    {
                        var cell = ws.Cell(r, c);
                        row[c - 1] = cell.IsEmpty() ? null : cell.DataType switch
                        {
                            XLDataType.Number => cell.GetDouble(), XLDataType.DateTime => cell.GetDateTime().ToOADate(),
                            XLDataType.Boolean => cell.GetBoolean() ? "TRUE" : "FALSE", _ => cell.GetFormattedString(),
                        };
                    }
                    aoa.Add(row);
                }
                res[ws.Name] = aoa;
            }
            return res;
        }
        catch (IOException e) { throw new BusinessException("Không mở được file (có thể đang mở trong Excel): " + e.Message); }
        catch (Exception e) when (e is not BusinessException) { throw new BusinessException("Không đọc được file (chỉ nhận .xlsx / .xlsm): " + e.Message); }
    }

    /// <summary>Thử lần lượt từng sheet, lấy sheet đầu nhận ra được; không sheet nào thì trả lỗi của sheet đầu.</summary>
    public static Result BuildFromBook(Dictionary<string, List<object?[]>> book)
    {
        Result? first = null;
        foreach (var (_, aoa) in book) { var r = Build(aoa); if (r.Ok) return r; first ??= r; }
        return first ?? new Result { Err = "Không có dữ liệu." };
    }

    /// <summary>Xuất file rút gọn (cùng bố cục file mẫu: 1 sheet, 8 cột, Net = TLg × SL, dòng tổng cuối, bộ lọc ở dòng tiêu đề).</summary>
    public static void Export(Result r, string path)
    {
        if (r.Groups.Count == 0) throw new BusinessException("Không có dòng Xuất kiểm nào để xuất.");
        using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Sheet");
        for (var i = 0; i < Head.Length; i++) ws.Cell(1, i + 1).Value = Head[i];
        var hr = ws.Range(1, 1, 1, 8); hr.Style.Font.SetBold().Font.SetFontName("Times New Roman").Font.SetFontSize(11); hr.Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E1F2");
        hr.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetWrapText(); ws.Row(1).Height = 24;
        var n = r.Groups.Count; var last = n + 1;
        for (var i = 0; i < n; i++)
        {
            var g = r.Groups[i]; var rr = i + 2;
            ws.Cell(rr, 1).Value = DateTime.ParseExact(g.Ngay, "yyyy-MM-dd", CultureInfo.InvariantCulture); ws.Cell(rr, 1).Style.DateFormat.Format = "dd/mm/yyyy";
            ws.Cell(rr, 2).Value = g.Ma; ws.Cell(rr, 3).Value = g.Ten; ws.Cell(rr, 4).Value = g.Tlg ?? 0; ws.Cell(rr, 5).Value = g.Sl;
            if (g.NetLech) ws.Cell(rr, 6).Value = g.Net; else ws.Cell(rr, 6).FormulaA1 = $"D{rr}*E{rr}";
            ws.Cell(rr, 7).Value = g.Size; ws.Cell(rr, 8).Value = g.Hopdong;
            ws.Cell(rr, 4).Style.NumberFormat.Format = "#,##0.###0"; ws.Cell(rr, 5).Style.NumberFormat.Format = "#,##0"; ws.Cell(rr, 6).Style.NumberFormat.Format = "#,##0.###0";
            for (var c = 1; c <= 8; c++) { var cell = ws.Cell(rr, c); cell.Style.Font.FontName = "Times New Roman"; cell.Style.Font.FontSize = 11; cell.Style.Alignment.Horizontal = c is 1 or 7 or 8 ? XLAlignmentHorizontalValues.Center : c is >= 4 and <= 6 ? XLAlignmentHorizontalValues.Right : XLAlignmentHorizontalValues.Left; }
            ws.Cell(rr, 3).Style.Alignment.WrapText = true;
        }
        var tr = last + 1;
        ws.Cell(tr, 1).Value = "Tổng"; ws.Cell(tr, 5).FormulaA1 = $"SUBTOTAL(9,E2:E{last})"; ws.Cell(tr, 6).FormulaA1 = $"SUM(F2:F{last})";
        ws.Cell(tr, 5).Style.NumberFormat.Format = "#,##0"; ws.Cell(tr, 6).Style.NumberFormat.Format = "#,##0.###0";
        var tot = ws.Range(tr, 1, tr, 8); tot.Style.Font.Bold = true; tot.Style.Font.FontName = "Times New Roman"; tot.Style.Fill.BackgroundColor = XLColor.FromHtml("#F2F2F2");
        ws.Range(1, 1, tr, 8).Style.Border.OutsideBorder = XLBorderStyleValues.Thin; ws.Range(1, 1, tr, 8).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        double[] w = { 17.6, 26.4, 63.4, 10.9, 15.4, 12, 9, 15.1 }; for (var i = 0; i < 8; i++) ws.Column(i + 1).Width = w[i];
        ws.Range(1, 1, last, 8).SetAutoFilter(); ws.SheetView.FreezeRows(1);
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape; ws.PageSetup.PaperSize = XLPaperSize.A4Paper; ws.PageSetup.FitToPages(1, 0); ws.PageSetup.SetRowsToRepeatAtTop(1, 1);
        wb.SaveInPlace(path);
    }
}
