using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KhoLanh.Core.TonKho;

namespace KhoLanh.Core.TonKhoGui;

/// <summary>Một dòng tồn kho gửi (10 cột cố định TARGET_COLS của app cũ).</summary>
public sealed class GuiRow
{
    public string MaHang = "", TenHang = "", DacTinh = "", SoLo = "", Size = "", QuyCach = "", NgayNhapKho = "", PhieuNhap = "";
    public double SoKien, TrongLuong;
    /// <summary>Giá trị chữ gốc của ô khi vừa đọc từ file (để kiểm tra "không phải số"); null nếu dòng đọc từ database.</summary>
    public string? SoKienRaw, TrongLuongRaw;
    /// <summary>Đã bị trừ theo phiếu xuất (tô đỏ).</summary>
    public bool PxDaXuat;
    public GuiRow Clone() => (GuiRow)MemberwiseClone();
}

public sealed class CustomFilter { public string Id = "", ColLabel = "", Op = "eq", Value = ""; public int From = 2, To = 4; }

/// <summary>
/// Lõi Tồn kho gửi (port ton-kho-gui.js, bản C# thay WebView2): đọc file → "Dữ Liệu" (DuLieuChinh), phân loại 6 nhóm theo Số lô / Size,
/// điều kiện lọc tự thêm, Đặc Tính (suy từ Tên hàng + bảng nhập tay + gộp nhóm), Bảng Tổng Hợp (Báo Cáo), Đối Soát, phiếu xuất.
/// </summary>
public sealed class GuiData
{
    // ------------------------------------------------------------------ cột
    public static readonly (string Key, string Label)[] TargetCols =
    {
        ("maHang", "Mã hàng"), ("tenHang", "Tên hàng"), ("dacTinh", "Đặc tính"), ("soLo", "Số lô"), ("size", "Size"), ("quyCach", "Quy cách"),
        ("ngayNhapKho", "Ngày nhập kho"), ("phieuNhap", "Phiếu nhập/Voucher"), ("soKien", "Số kiện"), ("trongLuong", "Trọng Lượng (KG)"),
    };
    public const string PxDaXuatLabel = "Đã trừ theo phiếu xuất";

    public static string Get(GuiRow r, string key) => key switch
    {
        "maHang" => r.MaHang, "tenHang" => r.TenHang, "dacTinh" => r.DacTinh, "soLo" => r.SoLo, "size" => r.Size, "quyCach" => r.QuyCach,
        "ngayNhapKho" => r.NgayNhapKho, "phieuNhap" => r.PhieuNhap, "soKien" => JsText.JsStr(r.SoKien), "trongLuong" => JsText.JsStr(r.TrongLuong), _ => "",
    };

    // ------------------------------------------------------------------ chữ / số kiểu JS
    /// <summary>normalize(): bỏ dấu (NFD) + hạ chữ thường + trim — KHÔNG đổi đ → d (như app cũ).</summary>
    public static string Nz(string? v)
    {
        var d = (v ?? "").Normalize(NormalizationForm.FormD); var sb = new StringBuilder();
        foreach (var c in d) if (c < '̀' || c > 'ͯ') sb.Append(c);
        return JsText.JsTrim(sb.ToString().ToLowerInvariant());
    }
    /// <summary>toNumber(): trống / không đọc được = 0.</summary>
    public static double ToNum(string? v) { var n = NapExcel.Num(v); return n is double x && !double.IsNaN(x) ? x : 0; }
    public static string FormatWeight(double n) => JsText.En(n, 2, 2);
    public static string FormatNumber(double n) => JsText.En(n, 0, 2);

    /// <summary>Số ngày Excel (serial) → dd/mm/yyyy (công thức SheetJS).</summary>
    public static string FormatExcelDate(object? v)
    {
        if (v is null) return "";
        if (v is DateTime dt) return $"{dt.Day:00}/{dt.Month:00}/{dt.Year}";
        var s = Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
        if (s.Trim().Length == 0) return "";
        if (v is double or int or long or float || (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _) && s.Trim().Length > 0))
        {
            var n = v is double dd ? dd : double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
            if (n > 0)
            {
                var d = new DateTime(1970, 1, 1).AddDays(Math.Floor(n - 25569));
                return $"{d.Day:00}/{d.Month:00}/{d.Year}";
            }
        }
        return s;
    }

    // ------------------------------------------------------------------ đọc file → dòng tồn
    public static int FindCol(IReadOnlyList<string> row, string keyword) { for (var c = 0; c < row.Count; c++) if (Nz(row[c]).Contains(keyword, StringComparison.Ordinal)) return c; return -1; }

    public sealed record Headers(int MainRow, int SubRow, string Mode, int FirstSoKien);
    public static Headers? LocateHeaders(IReadOnlyList<string[]> json)
    {
        int mainRow = -1;
        for (var r = 0; r < Math.Min(json.Count, 30); r++) if (FindCol(json[r], "ma hang") != -1) { mainRow = r; break; }
        if (mainRow != -1)
        {
            int subRow = -1;
            for (var r = mainRow; r <= mainRow + 2 && r < json.Count; r++) if (FindCol(json[r], "so kien") != -1) { subRow = r; break; }
            if (subRow != -1) return new Headers(mainRow, subRow, "text", -1);
        }
        for (var r = 0; r < Math.Min(json.Count, 30); r++)
        {
            var row = json[r]; var first = -1;
            for (var c = 0; c < row.Length; c++) if (Nz(row[c]).Contains("so kien", StringComparison.Ordinal)) { first = c; break; }
            if (first >= 5) return new Headers(r, r, "position", first);
        }
        return null;
    }

    public sealed class Extracted { public List<GuiRow> Rows = new(); public string ReadInfo = ""; }

    /// <summary>extractInventoryData: null = file không khớp mẫu tồn kho gửi. raw[r][c] = giá trị gốc của ô (số / chữ / ngày) để lấy đúng số kiện + ngày.</summary>
    public static Extracted? Extract(IReadOnlyList<object?[]> raw)
    {
        var json = raw.Select(r => r.Select(c => c is double d ? JsText.JsStr(d) : Convert.ToString(c, CultureInfo.InvariantCulture) ?? "").ToArray()).ToList();
        var pos = LocateHeaders(json); if (pos is null) return null;
        var row1 = json[pos.MainRow]; var subRow = json[pos.SubRow];
        int cMa, cTen, cLo, cSize, cQc, cDt, cNgay, cPhieu;
        if (pos.Mode == "position")
        {
            cQc = pos.FirstSoKien - 1; cSize = pos.FirstSoKien - 2; cLo = pos.FirstSoKien - 3; cTen = pos.FirstSoKien - 4; cMa = pos.FirstSoKien - 5; cDt = -1; cNgay = -1; cPhieu = -1;
        }
        else
        {
            cMa = FindCol(row1, "ma hang"); cTen = FindCol(row1, "ten hang"); cLo = FindCol(row1, "so lo"); cSize = FindCol(row1, "size"); cQc = FindCol(row1, "quy cach");
            cDt = FindCol(row1, "dac tinh"); cNgay = FindCol(row1, "ngay nhap kho"); cPhieu = FindCol(row1, "phieu nhap"); if (cPhieu == -1) cPhieu = FindCol(row1, "voucher");
        }
        if (new[] { cMa, cTen, cLo, cSize, cQc }.Any(c => c < 0)) return null;
        var soKien = new List<int>(); for (var c = 0; c < subRow.Length; c++) if (Nz(subRow[c]).Contains("so kien", StringComparison.Ordinal)) soKien.Add(c);
        if (soKien.Count == 0) return null;
        var o = new Extracted();
        var cSoKien = soKien[^1];
        o.ReadInfo = $"Không có nhãn nhóm \"TỒN CUỐI KỲ\" — lấy cặp Số kiện / Trọng lượng cuối cùng (cột {cSoKien + 1}).";
        for (var r = Math.Max(0, pos.MainRow - 1); r <= Math.Max(pos.MainRow, pos.SubRow) && r < json.Count; r++)
        {
            var hr = json[r]; var g = -1;
            for (var c = 0; c < hr.Length; c++) if (Nz(hr[c]).Contains("ton cuoi", StringComparison.Ordinal)) { g = c; break; }
            if (g != -1) { cSoKien = g; o.ReadInfo = $"Đang lấy nhóm cột \"TỒN CUỐI KỲ\" (cột {g + 1} – {g + 2})."; break; }
        }
        var cTl = cSoKien + 1;
        if (!(cTl < subRow.Length && Nz(subRow[cTl]).Contains("trong luong", StringComparison.Ordinal)))
            for (var c = cSoKien + 1; c < subRow.Length; c++) if (Nz(subRow[c]).Contains("trong luong", StringComparison.Ordinal)) { cTl = c; break; }
        var start = Math.Max(pos.MainRow, pos.SubRow) + 1;
        string Cell(int r, int c) => c >= 0 && c < json[r].Length ? json[r][c] : "";
        object? Raw(int r, int c) => c >= 0 && r < raw.Count && c < raw[r].Length ? raw[r][c] : null;
        for (var r = start; r < json.Count; r++)
        {
            if (json[r].All(x => JsText.JsTrim(x).Length == 0)) break;
            if (JsText.JsTrim(Cell(r, cMa)).Length == 0) continue;
            o.Rows.Add(new GuiRow
            {
                MaHang = Cell(r, cMa), TenHang = Cell(r, cTen), SoLo = Cell(r, cLo), Size = Cell(r, cSize), QuyCach = Cell(r, cQc),
                NgayNhapKho = cNgay != -1 ? FormatExcelDate(Raw(r, cNgay)) : "", PhieuNhap = cPhieu != -1 ? Cell(r, cPhieu) : "",
                SoKien = ToNum(Cell(r, cSoKien)), TrongLuong = ToNum(Cell(r, cTl)), SoKienRaw = Cell(r, cSoKien), TrongLuongRaw = Cell(r, cTl), DacTinh = cDt != -1 ? Cell(r, cDt) : "",
            });
        }
        return o;
    }

    /// <summary>extractDacTinhRaw: file không khớp mẫu tồn kho nhưng có cột "Đặc tính" — lấy các giá trị đặc tính.</summary>
    public static List<string> ExtractDacTinhRaw(IReadOnlyList<string[]> json)
    {
        var o = new List<string>(); int main = -1;
        for (var r = 0; r < Math.Min(json.Count, 30); r++) if (FindCol(json[r], "ma hang") != -1) { main = r; break; }
        if (main == -1) return o;
        var c = FindCol(json[main], "dac tinh"); if (c == -1) return o;
        for (var r = main + 1; r < json.Count; r++)
        {
            if (json[r].All(x => JsText.JsTrim(x).Length == 0)) break;
            var v = c < json[r].Length ? json[r][c] : ""; if (JsText.JsTrim(v).Length == 0) continue;
            o.Add(v);
        }
        return o;
    }

    // ------------------------------------------------------------------ dữ liệu đang lưu
    public List<GuiRow> Rows = new();
    public string DataDate = ""; public double ImportedAt; public string NapId = "";
    public Dictionary<string, List<CustomFilter>> Custom = new();
    public static readonly string[] CustomTabs = { "nl", "5", "4", "3", "ad", "2" };
    // Đặc tính
    public List<(string MaHang, string DacTinh)> DtManual = new();     // [{Diễn giải đặc tính, Đặc tính}]
    public Dictionary<string, (string Lo, string DacTinh)> DtLoMap = new();
    public Dictionary<string, string> DtGroupMap = new();
    public HashSet<string> DtSeen = new();

    public List<CustomFilter> CustomFor(string tab) { if (!Custom.TryGetValue(tab, out var l)) Custom[tab] = l = new(); return l; }
    public bool HasData => Rows.Count > 0;

    // ------------------------------------------------------------------ phân loại 6 nhóm
    public static bool IsEcuador(string? lo) => Nz(lo).Contains("ecuador", StringComparison.Ordinal);
    public static bool IsAnDoLot(string? lo) => Nz(lo).Contains("ad", StringComparison.Ordinal);
    public static bool IsTgc(string? lo) => Nz(lo).Contains("tgc", StringComparison.Ordinal);
    public static bool IsTgs(string? lo) => Nz(lo).Contains("tgs", StringComparison.Ordinal);
    public static bool IsNguyenLieu(string? size) => Nz(size).Contains("nl", StringComparison.Ordinal);
    public static bool IsAnDoOnly(GuiRow r) => IsAnDoLot(r.SoLo) && !IsEcuador(r.SoLo) && !IsTgc(r.SoLo) && !IsTgs(r.SoLo) && !IsNguyenLieu(r.Size);
    public static bool IsHlso(GuiRow r) => !IsEcuador(r.SoLo) && !IsAnDoLot(r.SoLo) && !IsTgc(r.SoLo) && !IsTgs(r.SoLo) && !IsNguyenLieu(r.Size);

    public sealed record Group(string Id, string Label, string Cat, string Prefix, Func<GuiRow, bool> Test);
    /// <summary>6 nhóm theo thứ tự menu: id tab Phân Loại giữ như app cũ (nl · 5 · 4 · 3 · ad · 2).</summary>
    public static readonly Group[] Groups =
    {
        new("nl", "Tôm Nguyên Liệu", "TÔM NGUYÊN LIỆU", "NL", r => IsNguyenLieu(r.Size)),
        new("5", "Tôm HLSO", "TÔM HLSO", "BTP", IsHlso),
        new("4", "Tôm Giám Sát", "TÔM GIÁM SÁT", "TGS", r => IsTgs(r.SoLo)),
        new("3", "Tôm Gia Công", "TÔM GIA CÔNG", "TGC", r => IsTgc(r.SoLo)),
        new("ad", "Tôm Ấn Độ", "TÔM ẤN ĐỘ", "AĐ", IsAnDoOnly),
        new("2", "Tôm Ecuador", "TÔM ECUADOR", "ECU", r => IsEcuador(r.SoLo)),
    };
    public static Group GroupOf(string id) => Groups.First(g => g.Id == id);

    /// <summary>Dòng của 1 nhóm sau điều kiện tự thêm.</summary>
    public List<GuiRow> GroupRows(string id) => ApplyCustom(Rows.Where(GroupOf(id).Test).ToList(), id);
    /// <summary>Dòng hiển thị: DacTinh đã suy ra từ Tên hàng.</summary>
    public string DacTinhOf(GuiRow r) => DeriveDacTinh(r.TenHang, r.SoLo);

    // ------------------------------------------------------------------ điều kiện lọc tự thêm
    public static readonly (string Op, string Label)[] CustomOps =
    {
        ("eq", "bằng"), ("neq", "khác"), ("contains", "chứa"), ("notcontains", "không chứa"), ("startswith", "bắt đầu bằng"), ("endswith", "kết thúc bằng"),
        ("in", "thuộc danh sách"), ("notin", "không thuộc danh sách"), ("pos_in", "ký tự vị trí … thuộc"), ("pos_notin", "ký tự vị trí … không thuộc"),
        ("gt", "lớn hơn"), ("gte", "lớn hơn hoặc bằng"), ("lt", "nhỏ hơn"), ("lte", "nhỏ hơn hoặc bằng"), ("empty", "để trống"), ("notempty", "không để trống"),
    };
    private static List<string> CfList(string? v) => Regex.Split(v ?? "", @"[;|\n]").Select(x => JsText.JsTrim(x)).Where(x => x.Length > 0).ToList();
    private static string DkNorm(string? v) => Regex.Replace(Nz(v).Replace('đ', 'd'), @"\s+", "");
    public static bool TestCustom(string? cell, CustomFilter cf)
    {
        double a, b;
        switch (cf.Op)
        {
            case "eq": return Nz(cell) == Nz(cf.Value);
            case "neq": return Nz(cell) != Nz(cf.Value);
            case "contains": return Nz(cell).Contains(Nz(cf.Value), StringComparison.Ordinal);
            case "notcontains": return !Nz(cell).Contains(Nz(cf.Value), StringComparison.Ordinal);
            case "gt": a = JsText.LooseNum(cell); b = JsText.LooseNum(cf.Value); return !double.IsNaN(a) && !double.IsNaN(b) && a > b;
            case "lt": a = JsText.LooseNum(cell); b = JsText.LooseNum(cf.Value); return !double.IsNaN(a) && !double.IsNaN(b) && a < b;
            case "gte": a = JsText.LooseNum(cell); b = JsText.LooseNum(cf.Value); return !double.IsNaN(a) && !double.IsNaN(b) && a >= b;
            case "lte": a = JsText.LooseNum(cell); b = JsText.LooseNum(cf.Value); return !double.IsNaN(a) && !double.IsNaN(b) && a <= b;
            case "empty": return JsText.JsTrim(cell ?? "").Length == 0;
            case "notempty": return JsText.JsTrim(cell ?? "").Length > 0;
            case "startswith": { var q = DkNorm(cf.Value); return q.Length > 0 && DkNorm(cell).StartsWith(q, StringComparison.Ordinal); }
            case "endswith": { var q = DkNorm(cf.Value); return q.Length > 0 && DkNorm(cell).EndsWith(q, StringComparison.Ordinal); }
            case "in": return CfList(cf.Value).Any(v => Nz(cell) == Nz(v));
            case "notin": return !CfList(cf.Value).Any(v => Nz(cell) == Nz(v));
            case "pos_in": case "pos_notin":
            {
                var from = Math.Max(1, cf.From <= 0 ? 1 : cf.From); var to = Math.Max(from, cf.To <= 0 ? from : cf.To);
                var t = JsText.JsTrim(cell ?? ""); var start = from - 1;
                var seg = start >= t.Length ? "" : t.Substring(start, Math.Min(to - start, t.Length - start)).ToUpperInvariant();
                var hit = CfList(cf.Value).Any(v => JsText.JsTrim(v).ToUpperInvariant() == seg);
                return cf.Op == "pos_in" ? hit : !hit;
            }
            default: return true;
        }
    }
    public static string CustomDesc(CustomFilter cf)
    {
        var op = CustomOps.FirstOrDefault(o => o.Op == cf.Op).Label ?? cf.Op;
        var pos = cf.Op is "pos_in" or "pos_notin" ? $" (ký tự {cf.From}–{cf.To})" : "";
        var needsVal = cf.Op is not ("empty" or "notempty");
        return $"{cf.ColLabel}{pos} {op}" + (needsVal ? $" “{cf.Value}”" : "");
    }
    public List<GuiRow> ApplyCustom(List<GuiRow> rows, string tab)
    {
        var cfs = CustomFor(tab); if (cfs.Count == 0) return rows;
        return rows.Where(r => cfs.All(cf =>
        {
            var col = TargetCols.FirstOrDefault(c => c.Label == cf.ColLabel); if (col.Key is null) return true;
            return TestCustom(Get(r, col.Key), cf);
        })).ToList();
    }

    public sealed record FunnelStep(string Kind, string Label, int Remaining, int Removed, string Col, bool Warn);
    public sealed record Funnel(int Total, List<FunnelStep> Steps, int Final, List<GuiRow> Rows);
    private static readonly Dictionary<string, (string Kind, string Label, string Col, Func<GuiRow, bool> Keep)[]> FunnelDef = new()
    {
        ["nl"] = new[] { ("main", "Size chứa \"NL\" ở bất kỳ vị trí nào", "Size", (Func<GuiRow, bool>)(r => IsNguyenLieu(r.Size))) },
        ["5"] = new[]
        {
            ("main", "Loại dòng có Số lô chứa \"ECUADOR\"", "Số lô", (Func<GuiRow, bool>)(r => !IsEcuador(r.SoLo))), ("extra", "Loại dòng có Số lô chứa \"AD\"", "Số lô", r => !IsAnDoLot(r.SoLo)),
            ("extra", "Loại dòng có Số lô chứa \"TGC\"", "Số lô", r => !IsTgc(r.SoLo)), ("extra", "Loại dòng có Số lô chứa \"TGS\"", "Số lô", r => !IsTgs(r.SoLo)),
            ("extra", "Loại dòng có Size chứa \"NL\"", "Size", r => !IsNguyenLieu(r.Size)),
        },
        ["4"] = new[] { ("main", "Số lô chứa \"TGS\" ở bất kỳ vị trí nào", "Số lô", (Func<GuiRow, bool>)(r => IsTgs(r.SoLo))) },
        ["3"] = new[] { ("main", "Số lô chứa \"TGC\" ở bất kỳ vị trí nào", "Số lô", (Func<GuiRow, bool>)(r => IsTgc(r.SoLo))) },
        ["ad"] = new[]
        {
            ("main", "Số lô chứa \"AD\" ở bất kỳ vị trí nào", "Số lô", (Func<GuiRow, bool>)(r => IsAnDoLot(r.SoLo))), ("extra", "Loại dòng đã thuộc Tôm Ecuador (Số lô chứa \"ECUADOR\")", "Số lô", r => !IsEcuador(r.SoLo)),
            ("extra", "Loại dòng đã thuộc Tôm Gia Công (Số lô chứa \"TGC\")", "Số lô", r => !IsTgc(r.SoLo)), ("extra", "Loại dòng đã thuộc Tôm Giám Sát (Số lô chứa \"TGS\")", "Số lô", r => !IsTgs(r.SoLo)),
            ("extra", "Loại dòng đã thuộc Tôm Nguyên Liệu (Size chứa \"NL\")", "Size", r => !IsNguyenLieu(r.Size)),
        },
        ["2"] = new[] { ("main", "Số lô chứa \"ECUADOR\" ở bất kỳ vị trí nào", "Số lô", (Func<GuiRow, bool>)(r => IsEcuador(r.SoLo))) },
    };
    /// <summary>Tab "Điều kiện lọc": từng bước lọc của 1 nhóm kèm số dòng còn lại / bị loại.</summary>
    public Funnel FunnelOf(string tab)
    {
        var rows = Rows; var prev = Rows.Count; var steps = new List<FunnelStep>();
        foreach (var s in FunnelDef[tab]) { rows = rows.Where(s.Keep).ToList(); steps.Add(new(s.Kind, s.Label, rows.Count, prev - rows.Count, s.Col, false)); prev = rows.Count; }
        foreach (var cf in CustomFor(tab))
        {
            var col = TargetCols.FirstOrDefault(c => c.Label == cf.ColLabel);
            if (col.Key is not null) rows = rows.Where(r => TestCustom(Get(r, col.Key), cf)).ToList();
            steps.Add(new("custom", CustomDesc(cf), rows.Count, prev - rows.Count, cf.ColLabel, col.Key is null)); prev = rows.Count;
        }
        return new Funnel(Rows.Count, steps, rows.Count, rows);
    }

    // ------------------------------------------------------------------ Đặc tính
    public static string LastCommaSegment(string? ten)
    {
        var s = JsText.JsTrim(ten ?? ""); if (s.Length == 0) return ""; if (!s.Contains(',')) return s;
        var p = s.Split(','); return JsText.JsTrim(p[^1]);
    }
    /// <summary>matchManualDacTinhPrefix: "83" khớp "83.EU-H-M" trong bảng nhập tay (bỏ số 0 đầu).</summary>
    public string? MatchManual(string? v)
    {
        if (string.IsNullOrEmpty(v)) return null;
        var t = JsText.JsTrim(v); if (t.Length == 0) return null;
        long? vNum = Regex.IsMatch(t, @"^\d+$") && long.TryParse(t, out var n0) ? n0 : null;
        foreach (var (_, dacTinh) in DtManual)
        {
            var dt = JsText.JsTrim(dacTinh); if (dt.Length == 0) continue;
            var m = Regex.Match(dt, @"^([^.]+)\."); if (!m.Success) continue;
            var prefix = JsText.JsTrim(m.Groups[1].Value);
            if (prefix == t) return dt;
            if (vNum is not null && Regex.IsMatch(prefix, @"^\d+$") && long.TryParse(prefix, out var pn) && pn == vNum) return dt;
        }
        return null;
    }
    public static string LoKey(string? s) => JsText.JsTrim(s ?? "").ToLowerInvariant();
    /// <summary>deriveDacTinhFromTenHang: phần sau dấu phẩy cuối của Tên hàng, đối chiếu bảng nhập tay; "CKQ" → dùng Số lô (hoặc đặc tính đã chuyển tay).</summary>
    public string DeriveDacTinh(string? ten, string? soLo)
    {
        var seg = LastCommaSegment(ten); if (seg.Length == 0) return "";
        var result = MatchManual(seg) ?? seg;
        if (result == "CKQ")
        {
            var lo = JsText.JsTrim(soLo ?? "");
            if (lo.Length > 0 && DtLoMap.TryGetValue(LoKey(lo), out var m) && m.DacTinh.Length > 0) return m.DacTinh;
            return lo;
        }
        return result;
    }
    public HashSet<string> CkqLotSet()
    {
        var set = new HashSet<string>();
        foreach (var r in Rows) { var seg = LastCommaSegment(r.TenHang); if (seg.Length == 0) continue; if ((MatchManual(seg) ?? seg) != "CKQ") continue; var lo = JsText.JsTrim(r.SoLo); if (lo.Length > 0) set.Add(lo); }
        return set;
    }
    /// <summary>canonDacTinhValue: "0023" / "0023.MKS" → "23.MKS" nếu có trong bảng nhập tay.</summary>
    public string CanonDacTinh(string? v)
    {
        var s = JsText.JsTrim(v ?? ""); if (s.Length == 0 || DtManual.Count == 0) return s;
        if (Regex.IsMatch(s, @"^\d+$")) return MatchManual(s) ?? s;
        var mm = Regex.Match(s, @"^0*(\d+)\.(.+)$");
        if (mm.Success)
        {
            var cand = (mm.Groups[1].Value + "." + JsText.JsTrim(mm.Groups[2].Value)).ToLowerInvariant();
            foreach (var (_, dt0) in DtManual) { var dt = JsText.JsTrim(dt0); if (dt.Length > 0 && dt.ToLowerInvariant() == cand) return dt; }
        }
        return s;
    }
    /// <summary>Dọn giá trị cũ dạng số thô (đổi sang "23.MKS"); trả true nếu có đổi.</summary>
    public (bool Seen, bool Map) MigrateAliases()
    {
        bool seenCh = false, mapCh = false; if (DtManual.Count == 0) return (false, false);
        foreach (var v in DtSeen.ToList()) { var c = CanonDacTinh(v); if (c != v) { DtSeen.Remove(v); DtSeen.Add(c); seenCh = true; } }
        foreach (var k in DtGroupMap.Keys.ToList()) { var c = CanonDacTinh(k); if (c != k) { if (!DtGroupMap.ContainsKey(c)) DtGroupMap[c] = DtGroupMap[k]; DtGroupMap.Remove(k); mapCh = true; } }
        return (seenCh, mapCh);
    }
    public List<string> RawDacTinhValues = new();
    /// <summary>getUniqueDacTinhValues: mọi đặc tính đang có / từng thấy (đã gộp hay chưa), trừ Số lô đã chuyển tay.</summary>
    public (List<string> Values, bool NewSeen) UniqueDacTinh()
    {
        MigrateAliases();
        var set = new HashSet<string>();
        foreach (var r in Rows) { var v = DeriveDacTinh(r.TenHang, r.SoLo); if (v.Length > 0) set.Add(v); }
        foreach (var raw in RawDacTinhValues) { var v = CanonDacTinh(raw); if (v.Length > 0) set.Add(v); }
        foreach (var (_, dt) in DtManual) { var v = JsText.JsTrim(dt); if (v.Length > 0) set.Add(v); }
        var newSeen = false; foreach (var v in set) if (DtSeen.Add(v)) newSeen = true;
        foreach (var v in DtSeen) set.Add(v);
        foreach (var v in set.ToList()) if (DtLoMap.ContainsKey(LoKey(v))) set.Remove(v);
        var list = set.ToList(); list = AnAnData.StableSort(list, (a, b) => JsText.ViNumericCompare(a, b)); return (list, newSeen);
    }
    public string DienGiai(string? v)
    {
        var key = JsText.JsTrim(v ?? "").ToLowerInvariant(); if (key.Length == 0) return "";
        foreach (var (mh, dt) in DtManual) if (JsText.JsTrim(dt).ToLowerInvariant() == key) return JsText.JsTrim(mh);
        return "";
    }
    public static readonly string[] DtGroups =
    {
        "MKS", "NHẬT, ASC, HQ, MỸ", "NHẬT, ASC-A,B, HQ, MỸ", "NHẬT, ASC-B, HQ, MỸ", "NHẬT, ASC, HQ", "NHẬT, EU, HQ", "NHẬT, ASC-A,B, HQ", "NHẬT, ASC-B, HQ", "NHẬT, ASC", "NHẬT, HQ",
        "NHẬT, EU", "ASC, HQ, MỸ", "ASC-A, B, HQ, MỸ", "ASC-B, HQ, MỸ", "ASC, HQ", "ASC-A,B, HQ", "ASC-B, HQ", "ASC", "EU, HQ, MỸ", "EU, HQ", "EU", "HQ", "KHÔNG ĐẠT",
    };

    /// <summary>parsePastedText: dán từ Excel (Tab) hoặc 2+ khoảng trắng → [(Diễn giải, Đặc tính)]; dòng 1 là tiêu đề thì bỏ.</summary>
    public static List<(string MaHang, string DacTinh)> ParsePasted(string text)
    {
        var lines = Regex.Split(text ?? "", @"\r?\n").Select(l => JsText.JsTrim(l)).Where(l => l.Length > 0).ToList();
        var o = new List<(string, string)>(); if (lines.Count == 0) return o;
        var start = 0; var f = Nz(lines[0]); if (f.Contains("ma hang", StringComparison.Ordinal) || f.Contains("dac tinh", StringComparison.Ordinal)) start = 1;
        for (var i = start; i < lines.Count; i++)
        {
            string ma = "", dt;
            var tab = lines[i].Split('\t');
            if (tab.Length >= 2) { ma = JsText.JsTrim(tab[0]); dt = JsText.JsTrim(string.Join(" ", tab.Skip(1))); }
            else { var sp = Regex.Split(lines[i], @"\s{2,}"); if (sp.Length >= 2) { ma = JsText.JsTrim(sp[0]); dt = JsText.JsTrim(string.Join(" ", sp.Skip(1))); } else dt = JsText.JsTrim(lines[i]); }
            if (dt.Length > 0) o.Add((ma, dt));
        }
        return o;
    }
    /// <summary>resolveTypedDacTinh: khớp không phân biệt hoa thường với đặc tính đã có; chưa có thì viết HOA.</summary>
    public string ResolveTyped(string? typed, IEnumerable<string> existing)
    {
        var t = JsText.JsTrim(typed ?? ""); if (t.Length == 0) return "";
        foreach (var v in existing) if (JsText.JsTrim(v).ToLowerInvariant() == t.ToLowerInvariant()) return JsText.JsTrim(v);
        var canon = CanonDacTinh(t); if (canon != t) return canon;
        return t.ToUpperInvariant();
    }

    // ------------------------------------------------------------------ Báo Cáo (Bảng Tổng Hợp)
    public sealed class Bucket { public Dictionary<string, double> ByGroup = new(); public double Tong; }
    public sealed class PivotCat { public string Label = ""; public Bucket B = new(); public List<(string Size, Bucket B)> Sizes = new(); }
    public sealed class Pivot { public Bucket Grand = new(); public List<PivotCat> Cats = new(); }
    private static readonly Regex SizePrefixRe = new(@"^(TGS|TGC|BTP|ECU|NL|AĐ|AD)\s*", RegexOptions.IgnoreCase);
    public static string StripSizePrefixes(string? raw)
    {
        var s = JsText.JsTrim(raw ?? ""); Match m;
        while ((m = SizePrefixRe.Match(s)).Success && m.Length > 0) s = JsText.JsTrim(s[m.Length..]);
        return s;
    }
    public static (int G, double N, string S) SizeSortKey(string core)
    {
        var s = JsText.JsTrim(core); Match m;
        if ((m = Regex.Match(s, @"^(\d+(?:\.\d+)?)$")).Success) return (0, double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), "");
        if ((m = Regex.Match(s, @"^(\d+(?:\.\d+)?)\s*/\s*\d+(?:\.\d+)?$")).Success) return (1, double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), "");
        if ((m = Regex.Match(s, @"^(\d*)([A-ZÀ-Ỹ]+)XX$", RegexOptions.IgnoreCase)).Success) return (2, m.Groups[1].Value.Length > 0 ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0, m.Groups[2].Value.ToUpperInvariant());
        return (3, double.MaxValue, "");
    }
    private static void AddTo(Bucket b, string? group, double w) { if (string.IsNullOrEmpty(group)) return; b.Tong += w; b.ByGroup[group] = (b.ByGroup.TryGetValue(group, out var x) ? x : 0) + w; }
    public static bool InStock(Bucket b) => Math.Abs(b.Tong) >= 0.005 || b.ByGroup.Values.Any(v => Math.Abs(v) >= 0.005);

    /// <summary>only = id nhóm cần tính (1.126: xuất file HLSO tự chọn); null = cả 6 nhóm.</summary>
    public Pivot ComputePivot(ISet<string>? only = null)
    {
        var p = new Pivot();
        foreach (var g in Groups)
        {
            if (only is not null && !only.Contains(g.Id)) continue;
            var cat = new PivotCat { Label = g.Cat }; var order = new List<string>(); var map = new Dictionary<string, Bucket>();
            foreach (var r in GroupRows(g.Id))
            {
                var dt = DeriveDacTinh(r.TenHang, r.SoLo); if (dt.Length == 0) continue;
                var rawSize = JsText.JsTrim(r.Size);
                var core = rawSize.Length > 0 ? (StripSizePrefixes(rawSize) is { Length: > 0 } c ? c : rawSize) : "(Trống)";
                if (!map.ContainsKey(core)) { map[core] = new Bucket(); order.Add(core); }
                var w = r.TrongLuong; DtGroupMap.TryGetValue(dt, out var grp);
                AddTo(map[core], grp, w); AddTo(cat.B, grp, w); AddTo(p.Grand, grp, w);
            }
            order = AnAnData.StableSort(order, (a, b) =>
            {
                var ka = SizeSortKey(a); var kb = SizeSortKey(b);
                if (ka.G != kb.G) return ka.G.CompareTo(kb.G);
                if (ka.N != kb.N) return ka.N.CompareTo(kb.N);
                return string.CompareOrdinal(a, b);
            });
            foreach (var core in order) cat.Sizes.Add(((core == "(Trống)" || g.Prefix.Length == 0) ? core : $"{g.Prefix} {core}", map[core]));
            cat.Sizes = cat.Sizes.Where(s => InStock(s.B)).ToList();
            if (InStock(cat.B) || cat.Sizes.Count > 0) p.Cats.Add(cat);
        }
        return p;
    }

    // ------------------------------------------------------------------ Đối Soát
    public sealed record DsItem(string MaHang, string TenHang, string SoLo, string Size, double TrongLuong, string Extra);
    public sealed class DoiSoat
    {
        public double LoadTotal, ModuleTotal, DupSum, MissingSum, NoDtSum, UngroupedSum; public int DupCount, MissingCount, NoDtCount, UngroupedCount;
        public List<DsItem> DupList = new(), MissingList = new(), NoDtList = new(), UngroupedList = new();
    }
    public DoiSoat ComputeDoiSoat()
    {
        var o = new DoiSoat();
        foreach (var r in Rows)
        {
            var w = r.TrongLuong; var mods = new List<string>();
            if (IsEcuador(r.SoLo)) mods.Add("Ecuador"); if (IsAnDoOnly(r)) mods.Add("Ấn Độ"); if (IsTgc(r.SoLo)) mods.Add("Gia Công"); if (IsTgs(r.SoLo)) mods.Add("Giám Sát"); if (IsNguyenLieu(r.Size)) mods.Add("Nguyên Liệu");
            var k = mods.Count; if (k == 0) mods.Add("HLSO");
            var total = k + (k == 0 ? 1 : 0);
            o.LoadTotal += w; o.ModuleTotal += w * total;
            if (total >= 2) { o.DupSum += w * (total - 1); o.DupCount++; o.DupList.Add(new(r.MaHang, r.TenHang, r.SoLo, r.Size, w, string.Join(", ", mods))); }
            var dt = DeriveDacTinh(r.TenHang, r.SoLo);
            if (dt.Length == 0) { o.NoDtSum += w * total; o.NoDtCount++; o.NoDtList.Add(new(r.MaHang, r.TenHang, r.SoLo, r.Size, w * total, "")); }
            else if (!DtGroupMap.ContainsKey(dt)) { o.UngroupedSum += w * total; o.UngroupedCount++; o.UngroupedList.Add(new(r.MaHang, r.TenHang, r.SoLo, r.Size, w * total, dt)); }
        }
        int ByW(DsItem a, DsItem b) => Math.Abs(b.TrongLuong).CompareTo(Math.Abs(a.TrongLuong));
        o.DupList = AnAnData.StableSort(o.DupList, ByW); o.MissingList = AnAnData.StableSort(o.MissingList, ByW); o.NoDtList = AnAnData.StableSort(o.NoDtList, ByW); o.UngroupedList = AnAnData.StableSort(o.UngroupedList, ByW);
        return o;
    }
}
