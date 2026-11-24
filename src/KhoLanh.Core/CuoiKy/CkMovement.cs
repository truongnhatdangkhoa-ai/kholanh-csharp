using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.CuoiKy;

/// <summary>
/// CHỨNG TỪ NHẬP / XUẤT AN AN (nguồn tuỳ chọn "anan_nhap" / "anan_xuat" của Số liệu cuối kỳ) — chép readMovementAnAn (dc2-import.js)
/// cùng phần đọc báo cáo nhập / xuất của dc-engine.js (tiêu đề 1–2 tầng, ánh xạ cột theo từ đồng nghĩa — không đoán, dòng nhóm "Loại chứng từ",
/// số / ngày không đọc được = lỗi dòng, không coi là 0). Kết quả là danh sách chứng từ (src "anan", chiều N / X) đưa vào phân tích chứng từ.
/// </summary>
public static class CkMovement
{
    public static string Fold(object? s)
    {
        var d = Str(s).Normalize(NormalizationForm.FormD); var sb = new StringBuilder(d.Length);
        foreach (var ch in d) if (ch < '̀' || ch > 'ͯ') sb.Append(ch);
        var t = sb.ToString().Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant();
        t = Regex.Replace(t, @"[._\-/\\():#*]+", " ");
        return Regex.Replace(t, @"\s+", " ").Trim();
    }
    private static string Str(object? v) => v switch { null => "", string s => s, double d => JsNum(d), DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "" };
    private static string JsNum(double d) => d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture);
    private static bool Filled(object? v) => v is not null && !(v is string s && s.Trim().Length == 0);

    private sealed record Kind(Dictionary<string, string[]> Fields, string[] Required, Dictionary<string, string[]> Extras, Dictionary<string, string[]> Prefer);
    private static readonly string[] MaNames = { "ma hang", "ma sp", "ma san pham", "ma vat tu", "ma hh", "ma hang hoa", "ma vt", "item code", "item id", "sku", "ma thanh pham" };
    private static readonly string[] TenNames = { "ten hang", "ten san pham", "ten vat tu", "ten sp", "ten hang hoa", "ten vt", "dien giai hang", "item name", "ten thanh pham" };
    private static readonly string[] KgMove = { "net", "kl net", "net kg", "khoi luong", "khoi luong kg", "khoi luong net", "so kg", "slkg", "sl kg", "trong luong net" };
    private static readonly string[] Phieu = { "ma phieu", "so phieu", "so chung tu", "so ct", "chung tu", "so lenh" };
    private static Kind Make(string dir)
    {
        var (pn, ng, ex) = dir == "nhap"
            ? (new[] { "ma phieu nhap", "so phieu nhap", "phieu nhap" }, new[] { "ngay nhap", "ngay", "ngay chung tu", "ngay ct" }, new Dictionary<string, string[]> { ["loai"] = new[] { "loai nhap", "loai chung tu" }, ["doiTac"] = new[] { "nha cung cap", "ncc" }, ["kho"] = new[] { "kho" }, ["nguoi"] = new[] { "nguoi nhap", "nguoi tao" } })
            : (new[] { "ma phieu xuat", "so phieu xuat", "phieu xuat" }, new[] { "ngay xuat", "ngay", "ngay chung tu", "ngay ct" }, new Dictionary<string, string[]> { ["loai"] = new[] { "loai xuat", "loai chung tu" }, ["doiTac"] = new[] { "khach hang", "kh" }, ["kho"] = new[] { "kho" }, ["nguoi"] = new[] { "nguoi xuat", "nguoi tao" } });
        var phieu = pn.Concat(Phieu).ToArray();
        return new Kind(new() { ["phieu"] = phieu, ["ngay"] = ng, ["ma"] = MaNames, ["ten"] = TenNames, ["sl"] = KgMove }, new[] { "ngay", "ma", "sl" }, ex,
            new() { ["sl"] = KgMove, ["ngay"] = new[] { ng[0], "ngay chung tu", "ngay ct", "ngay" }, ["phieu"] = phieu });
    }
    private static readonly Dictionary<string, Kind> Kinds = new() { ["nhap"] = Make("nhap"), ["xuat"] = Make("xuat") };
    public static readonly Dictionary<string, string> FieldLabel = new()
    {
        ["ma"] = "Mã hàng", ["ten"] = "Tên hàng", ["sl"] = "Khối lượng (kg)", ["phieu"] = "Mã phiếu", ["ngay"] = "Ngày", ["loai"] = "Loại chứng từ", ["doiTac"] = "Đối tác", ["kho"] = "Kho", ["nguoi"] = "Người lập",
    };
    private static readonly Regex KgHint = new(@"(^| )(net|kg|trong luong|khoi luong|ton ck|slkg)( |$)");

    // ---------------------------------------------------------------- tiêu đề 1 / 2 tầng
    public sealed record HeaderInfo(List<string> Labels, List<List<string>> Variants, int DataStart);
    private static List<string> VariantsOf(object? v)
    {
        var s = Str(v);
        return new[] { Fold(s), Fold(Regex.Split(s, @"\r?\n|\r")[0]) }.Where(x => x.Length > 0).Distinct().ToList();
    }
    private static object? Cell(IReadOnlyList<IReadOnlyList<object?>> aoa, int r, int c) => r < aoa.Count && c < aoa[r].Count ? aoa[r][c] : null;
    public static HeaderInfo Header(IReadOnlyList<IReadOnlyList<object?>> aoa, int r)
    {
        int wTop = r < aoa.Count ? aoa[r].Count : 0, wNext = r + 1 < aoa.Count ? aoa[r + 1].Count : 0, width = Math.Max(wTop, wNext);
        var ne = Enumerable.Range(0, wNext).Select(c => Cell(aoa, r + 1, c)).Where(Filled).ToList();
        var isSub = false;
        if (ne.Count >= 2 && ne.All(c => c is string s && !Regex.IsMatch(s.Trim(), @"^sum\s*:", RegexOptions.IgnoreCase)))
            for (var c = 0; c < width; c++) if (!Filled(Cell(aoa, r, c)) && Filled(Cell(aoa, r + 1, c))) { isSub = true; break; }
        var labels = new List<string>(); var variants = new List<List<string>>(); var parent = "";
        for (var c = 0; c < width; c++)
        {
            var t = Str(Cell(aoa, r, c)).Trim(); if (t.Length > 0) parent = t;
            var sub = isSub ? Str(Cell(aoa, r + 1, c)).Trim() : "";
            var v = VariantsOf(t); var label = Regex.Split(t, @"\r?\n|\r")[0];
            if (sub.Length > 0)
            {
                var head = Regex.Split(t.Length > 0 ? t : parent, @"\r?\n|\r")[0];
                label = head + " › " + Regex.Split(sub, @"\r?\n|\r")[0];
                foreach (var sv in VariantsOf(sub)) v.Add(Fold(head) + " " + sv);
            }
            labels.Add(label); variants.Add(v.Distinct().ToList());
        }
        return new HeaderInfo(labels, variants, r + (isSub ? 2 : 1));
    }

    public static int FindHeaderRow(IReadOnlyList<IReadOnlyList<object?>> aoa, string kind)
    {
        var K = Kinds[kind]; int best = -1, bestScore = 0;
        for (var r = 0; r < Math.Min(aoa.Count, 25); r++)
        {
            var vs = Header(aoa, r).Variants;
            bool Has(string[] names) => vs.Any(v => v.Any(names.Contains));
            if (!Has(K.Fields["ma"])) continue;
            var score = K.Fields.Values.Count(Has);
            if (score > bestScore) { bestScore = score; best = r; }
        }
        return best;
    }

    public sealed class MapResult { public Dictionary<string, int> Mapping = new(); public Dictionary<string, List<int>> Ambiguous = new(); public List<string> Missing = new(); }
    private static bool SameContent(List<int> idxs, IReadOnlyList<IReadOnlyList<object?>> aoa, int dataStart)
    {
        if (idxs.Count < 2) return false;
        for (var r = dataStart; r < aoa.Count; r++)
        {
            var a = Str(Cell(aoa, r, idxs[0])).Trim();
            for (var k = 1; k < idxs.Count; k++) if (Str(Cell(aoa, r, idxs[k])).Trim() != a) return false;
        }
        return true;
    }
    public static MapResult AutoMap(HeaderInfo info, string kind, IReadOnlyList<IReadOnlyList<object?>> aoa)
    {
        var K = Kinds[kind]; var vars = info.Variants; var res = new MapResult();
        var all = K.Fields.Concat(K.Extras);
        foreach (var (field, names) in all)
        {
            var hits = Enumerable.Range(0, vars.Count).Where(i => vars[i].Any(names.Contains)).ToList();
            if (hits.Count == 0) { if (K.Required.Contains(field)) res.Missing.Add(field); continue; }
            if (hits.Count == 1) { res.Mapping[field] = hits[0]; continue; }
            var pref = K.Prefer.GetValueOrDefault(field) ?? names; var done = false;
            foreach (var p in pref)
            {
                var same = hits.Where(i => vars[i].Contains(p)).ToList();
                if (same.Count == 1) { res.Mapping[field] = same[0]; done = true; break; }
                if (same.Count > 1) { if (SameContent(same, aoa, info.DataStart)) res.Mapping[field] = same[0]; else res.Ambiguous[field] = same; done = true; break; }
            }
            if (done) continue;
            if (SameContent(hits, aoa, info.DataStart)) res.Mapping[field] = hits[0]; else res.Ambiguous[field] = hits;
        }
        if (res.Mapping.TryGetValue("sl", out var sl) && !vars[sl].Any(x => KgHint.IsMatch(x))) { res.Ambiguous["sl"] = new() { sl }; res.Mapping.Remove("sl"); }
        // 1 cột không được dùng cho 2 trường
        foreach (var g in res.Mapping.ToList().GroupBy(kv => kv.Value).Where(g => g.Count() > 1))
            foreach (var kv in g.Skip(1)) { res.Ambiguous[kv.Key] = new() { kv.Value }; res.Mapping.Remove(kv.Key); }
        return res;
    }

    // ---------------------------------------------------------------- số / ngày (phần dự phòng của dc-engine parseNum / parseDate)
    public static double? ParseNum(object? v)
    {
        if (v is null || v is string { Length: 0 }) return null;
        if (v is double d) return double.IsFinite(d) ? d : double.NaN;
        var s = Regex.Replace(Str(v).Trim(), @"\s+", "");
        if (s.Length == 0 || s == "-") return null;
        var neg = false;
        if (Regex.IsMatch(s, @"^\(.*\)$")) { neg = true; s = s[1..^1]; }
        if (Regex.IsMatch(s, @"[^\d.,+\-]")) return double.NaN;
        bool hasDot = s.Contains('.'), hasComma = s.Contains(',');
        if (hasDot && hasComma) s = s.LastIndexOf(',') > s.LastIndexOf('.') ? ReplaceFirst(s.Replace(".", ""), ",", ".") : s.Replace(",", "");
        else if (hasComma) s = Regex.IsMatch(s, @"^[+-]?\d{1,3}(,\d{3})+$") ? s.Replace(",", "") : ReplaceFirst(s, ",", ".");
        else if (hasDot && Regex.IsMatch(s, @"^[+-]?\d{1,3}(\.\d{3})+$")) s = s.Replace(".", "");
        if (s.Length == 0) return 0;
        if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n)) return double.NaN;
        return neg ? -n : n;
    }
    private static string ReplaceFirst(string s, string a, string b) { var i = s.IndexOf(a, StringComparison.Ordinal); return i < 0 ? s : s[..i] + b + s[(i + a.Length)..]; }
    public static string? ParseDate(object? v)
    {
        if (v is null || v is string { Length: 0 }) return null;
        if (v is DateTime dt) return dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (v is double d) return d > 20000 && d < 80000 ? DateTime.UnixEpoch.AddMilliseconds(Math.Round((d - 25569) * 86400000)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        var s = Str(v).Trim();
        static string? Mk(int y, int m, int day) => m >= 1 && m <= 12 && day >= 1 && day <= DateTime.DaysInMonth(y, m) ? new DateTime(y, m, day).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
        var m1 = Regex.Match(s, @"^(\d{1,2})[\/\-.](\d{1,2})[\/\-.](\d{4}|\d{2})(?!\d)");
        if (m1.Success) { var y = int.Parse(m1.Groups[3].Value); if (y < 100) y += 2000; return y is >= 1 and <= 9999 ? Mk(y, int.Parse(m1.Groups[2].Value), int.Parse(m1.Groups[1].Value)) : null; }
        var m2 = Regex.Match(s, @"^(\d{4})[\/\-.](\d{1,2})[\/\-.](\d{1,2})");
        if (m2.Success) { var y = int.Parse(m2.Groups[1].Value); return y >= 1 ? Mk(y, int.Parse(m2.Groups[2].Value), int.Parse(m2.Groups[3].Value)) : null; }
        return null;
    }
    private static string NormMa(object? v) => Regex.Replace(Regex.Replace(Str(v).Normalize(NormalizationForm.FormC).ToUpperInvariant(), @"\s+", ""), "^TPC-", "");
    private static string CleanTxt(object? v) => Regex.Replace(Regex.Replace(Str(v), "_x000d_", " ", RegexOptions.IgnoreCase), @"\s+", " ").Trim();

    public sealed class MoveRow { public int Row; public string Ma = "", Ten = "", Phieu = "", Ngay = "", Loai = "", Kho = "", Nhom = ""; public double Sl; public bool HasLoai; }
    public sealed record RowError(int Row, string Ma, string Msg);

    /// <summary>Đọc dòng báo cáo nhập / xuất theo ánh xạ (readRows của dc-engine, phần nhap / xuat).</summary>
    public static (List<MoveRow> Rows, List<RowError> Errors, int Skipped, int Totals) ReadRows(IReadOnlyList<IReadOnlyList<object?>> aoa, Dictionary<string, int> mapping, int dataStart)
    {
        var rows = new List<MoveRow>(); var errors = new List<RowError>(); int skipped = 0, totals = 0;
        string? curGroup = null; var extraKeys = new[] { "loai", "doiTac", "kho", "nguoi" };
        bool IsSum(object? x) => x is string s && Regex.IsMatch(s.Trim(), @"^sum\s*:", RegexOptions.IgnoreCase);
        for (var r = dataStart; r < aoa.Count; r++)
        {
            var line = aoa[r];
            object? Get(string f) => mapping.TryGetValue(f, out var c) && c < line.Count ? line[c] : (mapping.ContainsKey(f) ? null : "");
            var cells = Enumerable.Range(0, line.Count).Where(i => Filled(line[i])).ToList();
            if (cells.Count > 0)
            {
                var first = cells[0]; var restSum = cells.Skip(1).All(i => IsSum(line[i]));
                if (line[first] is string fs && CleanTxt(fs).Length > 0 && restSum && !IsSum(fs)
                    && (!mapping.TryGetValue("ma", out var cm) || first != cm || Regex.IsMatch(CleanTxt(fs), @"\s"))
                    && (!mapping.TryGetValue("ma", out var cm2) || NormMa(cm2 < line.Count ? line[cm2] : null).Length == 0 || first == cm2))
                { curGroup = CleanTxt(fs); continue; }
            }
            var ma = NormMa(Get("ma"));
            if (ma.Length == 0) { if (line.Any(c => c is not null && !(c is string { Length: 0 }))) skipped++; continue; }
            if (Regex.IsMatch(Fold(Get("ma")), @"^(tong cong|tong so|tong|cong|total|grand total|sum)\b")) { totals++; continue; }
            var sl = ParseNum(Get("sl")); var excelRow = r + 1;
            if (sl is null) { errors.Add(new(excelRow, ma, "Ô khối lượng trống")); continue; }
            if (double.IsNaN(sl.Value)) { errors.Add(new(excelRow, ma, $"Khối lượng không đọc được: \"{Str(Get("sl"))}\"")); continue; }
            var o = new MoveRow { Row = excelRow, Ma = ma, Ten = CleanTxt(Get("ten")), Sl = sl.Value };
            o.Phieu = mapping.ContainsKey("phieu") ? CleanTxt(Get("phieu")) : "";
            if (mapping.ContainsKey("phieu") && o.Phieu.Length == 0) { errors.Add(new(excelRow, ma, "Thiếu mã phiếu")); continue; }
            var d = ParseDate(Get("ngay"));
            if (d is null) { errors.Add(new(excelRow, ma, $"Ngày không đọc được: \"{Str(Get("ngay"))}\"")); continue; }
            o.Ngay = d;
            if (mapping.ContainsKey("loai")) { o.Loai = CleanTxt(Get("loai")); o.HasLoai = true; }
            if (mapping.ContainsKey("kho")) o.Kho = CleanTxt(Get("kho"));
            if (curGroup is not null) { o.Nhom = curGroup; if (!o.HasLoai || o.Loai.Length == 0) { o.Loai = curGroup; o.HasLoai = true; } }
            rows.Add(o);
        }
        return (rows, errors, skipped, totals);
    }

    /// <summary>Đọc báo cáo nhập ("anan_nhap") / xuất ("anan_xuat") của kho An An → chứng từ (src "anan", dir N / X).</summary>
    public static CkSource Read(IReadOnlyList<IReadOnlyList<object?>> aoa, string kind)
    {
        var ek = kind == "anan_nhap" ? "nhap" : "xuat"; var title = CkConfig.SourceTitle[kind];
        var hr = FindHeaderRow(aoa, ek);
        if (hr < 0) return new CkSource { Kind = kind, Error = $"Không thấy dòng tiêu đề (Mã hàng, Ngày, Khối lượng/Net) trong {title}." };
        var info = Header(aoa, hr); var am = AutoMap(info, ek, aoa);
        if (am.Missing.Count > 0 || am.Ambiguous.Count > 0)
            return new CkSource { Kind = kind, Error = $"Chưa nhận diện chắc chắn cột ({string.Join(", ", am.Missing.Concat(am.Ambiguous.Keys).Select(f => FieldLabel.GetValueOrDefault(f, f)))}). Sửa tiêu đề cột trong file (VD: Ngày, Số phiếu, Mã hàng, Net) rồi nạp lại." };
        var (rows, errors, skipped, totals) = ReadRows(aoa, am.Mapping, info.DataStart);
        var res = new CkSource
        {
            Ok = true, Kind = kind, HeaderRow = hr + 1, Mapping = am.Mapping, Labels = info.Labels,
            MappingLabels = am.Mapping.ToDictionary(kv => kv.Key, kv => info.Labels[kv.Value]),
            Vouchers = rows.Select(x => new CkVoucher { Id = $"aa:{kind}:{x.Row}", Src = "anan", Row = x.Row, Phieu = x.Phieu, Ngay = x.Ngay, Kho = x.Kho, Ma = x.Ma, Ten = x.Ten, Lsx = "", Loai = x.Loai, Dir = ek == "nhap" ? "N" : "X", Kg = x.Sl }).ToList(),
            Period = CkImport.DetectPeriod(aoa),
        };
        res.Stats.NRows = rows.Count; res.Stats.TotalKg = Math.Round(rows.Sum(x => x.Sl) * 1e6) / 1e6; res.Stats.Skipped = skipped; res.Stats.Totals = totals; res.Stats.Issues = errors.Count;
        res.Issues.AddRange(errors.Take(CkImport.MaxIssues).Select(e => new CkIssue("so-loi", "loi", e.Row, e.Ma, e.Msg)));
        return res;
    }
}
