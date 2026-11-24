using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using static KhoLanh.Core.CuoiKy.Ck;

namespace KhoLanh.Core.CuoiKy;

public sealed record CkPeriod(string From, string To, string Source = "");

/// <summary>1 dòng tồn của 1 nguồn (An An / Kế toán / Kho gửi / Kiểm kê) — tên trường JSON giống app cũ.</summary>
public sealed class CkRow
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("row")] public int Row { get; set; }
    [JsonPropertyName("ma")] public string Ma { get; set; } = "";
    [JsonPropertyName("maGui")] public string? MaGui { get; set; }
    [JsonPropertyName("ten")] public string Ten { get; set; } = "";
    [JsonPropertyName("kg")] public double Kg { get; set; }
    [JsonPropertyName("kgSrc")] public string? KgSrc { get; set; }
    [JsonPropertyName("sl")] public double? Sl { get; set; }
    [JsonPropertyName("w")] public double? W { get; set; }
    [JsonPropertyName("dvt")] public string? Dvt { get; set; }
    [JsonPropertyName("dauKg")] public double? DauKg { get; set; }
    [JsonPropertyName("nhapKg")] public double? NhapKg { get; set; }
    [JsonPropertyName("xuatKg")] public double? XuatKg { get; set; }
    [JsonPropertyName("dauKien")] public double? DauKien { get; set; }
    [JsonPropertyName("nhapKien")] public double? NhapKien { get; set; }
    [JsonPropertyName("xuatKien")] public double? XuatKien { get; set; }
    [JsonPropertyName("cuoiKien")] public double? CuoiKien { get; set; }
    [JsonPropertyName("nhetLech")] public double? NhetLech { get; set; }
    [JsonPropertyName("selfLech")] public double? SelfLech { get; set; }
    [JsonPropertyName("selfUnit")] public string? SelfUnit { get; set; }
    [JsonPropertyName("dup")] public int? Dup { get; set; }
    [JsonPropertyName("lo")] public string? Lo { get; set; }
    [JsonPropertyName("size")] public string? Size { get; set; }
    [JsonPropertyName("quyCach")] public string? QuyCach { get; set; }
    [JsonPropertyName("ngayNhap")] public string? NgayNhap { get; set; }
    [JsonPropertyName("phieu")] public string? Phieu { get; set; }
    [JsonPropertyName("via")] public string? Via { get; set; }
    [JsonPropertyName("rule")] public string? Rule { get; set; }
    [JsonPropertyName("why")] public string? Why { get; set; }
    [JsonPropertyName("nDong")] public int? NDong { get; set; }
    [JsonPropertyName("ngay")] public string? Ngay { get; set; }
    public CkRow Clone() => (CkRow)MemberwiseClone();
}

/// <summary>1 chứng từ (kế toán / kho An An).</summary>
public sealed class CkVoucher
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("src")] public string Src { get; set; } = "ketoan";
    [JsonPropertyName("row")] public int Row { get; set; }
    [JsonPropertyName("phieu")] public string Phieu { get; set; } = "";
    [JsonPropertyName("ngay")] public string Ngay { get; set; } = "";
    [JsonPropertyName("kho")] public string Kho { get; set; } = "";
    [JsonPropertyName("ma")] public string Ma { get; set; } = "";
    [JsonPropertyName("ten")] public string Ten { get; set; } = "";
    [JsonPropertyName("lsx")] public string Lsx { get; set; } = "";
    [JsonPropertyName("loai")] public string Loai { get; set; } = "";
    [JsonPropertyName("dir")] public string Dir { get; set; } = "";
    [JsonPropertyName("kg")] public double Kg { get; set; }
    [JsonPropertyName("kien")] public double? Kien { get; set; }
    [JsonPropertyName("netPer")] public double? NetPer { get; set; }
    [JsonPropertyName("netLech")] public double? NetLech { get; set; }
}

public sealed record CkIssue(string Type, string Sev, int Row, string Ma, string Msg);

public sealed class CkStats
{
    public int NRows, NCodes, NZero, Skipped, Totals, Issues, NetLech;
    public double TotalKg, TotalDau, TotalNhap, TotalXuat;
    public string From = "", To = "";
    public Dictionary<string, int> Counts = new();
}

/// <summary>Kết quả đọc 1 nguồn.</summary>
public sealed class CkSource
{
    public bool Ok;
    public string Kind = "", Error = "", FileName = "", Sheet = "", LoadedAt = "";
    /// <summary>Lấy từ module (vd "module Tồn kho An An") — rỗng = nạp file.</summary>
    public string FromModule = "";
    public bool NeedMap;
    public int HeaderRow;
    public Dictionary<string, int> Mapping = new();
    public Dictionary<string, string> MappingLabels = new();
    public List<string> Labels = new();
    public Dictionary<string, List<int>> Ambiguous = new();
    public List<string> Missing = new();
    public List<CkRow> Rows = new();
    public List<CkVoucher> Vouchers = new();
    public List<string> KnownCodes = new();
    public CkPeriod? Period, PeriodGuess;
    public (string State, string Text)? PeriodCheck;
    public CkStats Stats = new();
    public List<CkIssue> Issues = new();
    public List<string> Sheets = new();
    // kế toán: sheet chứng từ + danh mục kèm theo
    public CkSource? Ct;
    public string CtError = "";
    public List<string>? DanhMuc;
    public string DanhMucSheet = "";
}

/// <summary>Đọc & kiểm tra file Excel các nguồn (giống dc2-import.js): KHÔNG ĐOÁN — thiếu cột / nhập nhằng → NeedMap để ánh xạ tay.</summary>
public static class CkImport
{
    public const int MaxIssues = 400;
    private static readonly Regex TotalRow = new(@"^(tong cong|tong so|tong|cong|total|grand total|sum)\b");

    // ---------- kỳ báo cáo (đọc từ nội dung) ----------
    private static string IsoFromText(string s) => IsoDay(ParseDate(s));
    private static string FirstDateAfter(IReadOnlyList<object?> row, int c)
    {
        for (var k = c + 1; k < Math.Min(row.Count, c + 14); k++) { var v = row[k]; if (!NonEmpty(v)) continue; var iso = ToIso(v); if (iso.Length > 0) return iso; }
        return "";
    }
    public static CkPeriod? DetectPeriod(IReadOnlyList<IReadOnlyList<object?>> aoa)
    {
        const string DATE = @"(\d{1,2}[/.\-]\d{1,2}[/.\-]\d{2,4})";
        var re = new Regex(DATE + @"[^\d]+" + DATE);
        for (var r = 0; r < Math.Min(aoa.Count, 20); r++)
        {
            var row = aoa[r];
            foreach (var cell in row)
            {
                if (cell is not string s) continue;
                if (!Regex.IsMatch(FoldClean(s), "(tu ngay|den ngay|from|to date|ky bao cao)")) continue;
                var m = re.Match(s);
                if (m.Success) { var a = IsoFromText(m.Groups[1].Value); var b = IsoFromText(m.Groups[2].Value); if (a.Length > 0 && b.Length > 0) return new CkPeriod(a, b, "tiêu đề báo cáo"); }
            }
            string from = "", to = "";
            for (var c = 0; c < row.Count; c++)
            {
                if (row[c] is not string s) continue;
                var f = FoldClean(s);
                if (Regex.IsMatch(f, @"^(tu ngay|from)\b")) from = from.Length > 0 ? from : FirstDateAfter(row, c);
                else if (Regex.IsMatch(f, @"^(den ngay|to)\b")) to = to.Length > 0 ? to : FirstDateAfter(row, c);
            }
            if (from.Length > 0 && to.Length > 0) return new CkPeriod(from, to, "tiêu đề báo cáo");
        }
        return null;
    }

    public static CkPeriod? PeriodFromFileName(string? name, int? yearHint = null)
    {
        var m = Regex.Match(name ?? "", @"(\d{1,2})[._\-/](\d{1,2})(?:[._\-/](\d{2,4}))?");
        if (!m.Success) return null;
        int d = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value), y = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : yearHint ?? DateTime.Now.Year;
        if (y < 100) y += 2000;
        if (mo < 1 || mo > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo)) return null;
        return new CkPeriod("", $"{y:0000}-{mo:00}-{d:00}", "đoán theo tên file");
    }

    // ---------- tiêu đề 1–2 tầng ----------
    public sealed record HeaderInfo(List<string> Labels, List<List<string>> Vars, int DataStart, int Row);

    private static List<string> VariantsOf(object? v)
    {
        var s = Str(v);
        return new[] { Fold(s), Fold(Regex.Split(s, @"\r?\n|\r")[0]) }.Where(x => x.Length > 0).Distinct().ToList();
    }

    public static HeaderInfo Header(IReadOnlyList<IReadOnlyList<object?>> aoa, int r)
    {
        IReadOnlyList<object?> top = r < aoa.Count ? aoa[r] : Array.Empty<object?>(), next = r + 1 < aoa.Count ? aoa[r + 1] : Array.Empty<object?>();
        var width = Math.Max(top.Count, next.Count);
        static bool Filled(object? v) => v is not null && !(v is string s && s.Length == 0) && Str(v).Trim().Length > 0;
        object? At(IReadOnlyList<object?> row, int c) => c < row.Count ? row[c] : null;
        var isSub = false;
        var ne = next.Where(Filled).ToList();
        if (ne.Count >= 2 && ne.All(c => c is string s && !Regex.IsMatch(s.Trim(), @"^sum\s*:", RegexOptions.IgnoreCase)))
            for (var c = 0; c < width; c++) if (!Filled(At(top, c)) && Filled(At(next, c))) { isSub = true; break; }
        var labels = new List<string>(); var variants = new List<List<string>>(); var parent = "";
        for (var c = 0; c < width; c++)
        {
            var t = Str(At(top, c)).Trim();
            if (t.Length > 0) parent = t;
            var sub = isSub ? Str(At(next, c)).Trim() : "";
            var v = VariantsOf(t);
            var label = Regex.Split(t, @"\r?\n|\r")[0];
            if (sub.Length > 0)
            {
                var head = Regex.Split(t.Length > 0 ? t : parent, @"\r?\n|\r")[0];
                label = head + " › " + Regex.Split(sub, @"\r?\n|\r")[0];
                foreach (var sv in VariantsOf(sub)) v.Add(Fold(head) + " " + sv);
            }
            labels.Add(label);
            variants.Add(v.Distinct().Select(CleanFold).Where(x => x.Length > 0).Distinct().ToList());
        }
        return new HeaderInfo(labels, variants, r + (isSub ? 2 : 1), r);
    }

    private static List<string> Matched(List<List<string>> vars, CkSourceSpec spec) =>
        spec.Fields.Where(f => vars.Any(v => v.Any(x => f.Names.Contains(x)))).Select(f => f.Key).ToList();
    private static bool MustOk(List<string> m, CkSourceSpec spec) =>
        spec.Must.All(m.Contains) && (spec.MustAny is null || spec.MustAny.Any(g => g.All(m.Contains)));

    public static (HeaderInfo H, int Score)? FindHeader(IReadOnlyList<IReadOnlyList<object?>> aoa, CkSourceSpec spec, int maxRows = 30)
    {
        (HeaderInfo H, int Score)? best = null;
        for (var r = 0; r < Math.Min(aoa.Count, maxRows); r++)
        {
            if (!aoa[r].Any(NonEmpty)) continue;
            var h = Header(aoa, r); var m = Matched(h.Vars, spec);
            if (!MustOk(m, spec)) continue;
            if (best is null || m.Count > best.Value.Score) best = (h, m.Count);
        }
        return best;
    }

    private static bool SameContent(List<int> idxs, IReadOnlyList<IReadOnlyList<object?>> aoa, int dataStart)
    {
        if (idxs.Count < 2) return false;
        var seen = 0;
        for (var r = dataStart; r < Math.Min(aoa.Count, dataStart + 3000); r++)
        {
            var line = aoa[r]; string Cell(int c) => Txt(c < line.Count ? line[c] : null);
            var a = Cell(idxs[0]); if (a.Length > 0) seen++;
            for (var k = 1; k < idxs.Count; k++) if (Cell(idxs[k]) != a) return false;
        }
        return seen > 0;
    }

    public sealed record MapResult(Dictionary<string, int> Mapping, Dictionary<string, List<int>> Ambiguous, List<string> Missing, List<string> Labels);

    public static MapResult ResolveMapping(HeaderInfo hd, CkSourceSpec spec, IReadOnlyList<IReadOnlyList<object?>> aoa, Dictionary<string, int>? manual = null, Dictionary<string, string>? prior = null)
    {
        var vars = hd.Vars; var labels = hd.Labels; var mapping = new Dictionary<string, int>(); var amb = new Dictionary<string, List<int>>(); var used = new HashSet<int>();
        foreach (var (f, c) in manual ?? new()) if (spec.F(f) is not null) { mapping[f] = c; used.Add(c); }
        foreach (var (f, lab) in prior ?? new())
        {
            if (mapping.ContainsKey(f) || spec.F(f) is null) continue;
            var want = CleanFold(lab); if (want.Length == 0) continue;
            var hit = labels.Select((l, i) => (CleanFold(l), i)).Where(x => x.Item1 == want && !used.Contains(x.i)).Select(x => x.i).ToList();
            if (hit.Count == 1) { mapping[f] = hit[0]; used.Add(hit[0]); }
        }
        foreach (var fd in spec.Fields)
        {
            var f = fd.Key; if (mapping.ContainsKey(f)) continue;
            var hits = new List<int>(); for (var i = 0; i < vars.Count; i++) if (!used.Contains(i) && vars[i].Any(x => fd.Names.Contains(x))) hits.Add(i);
            if (hits.Count == 0) continue;
            if (hits.Count == 1) { mapping[f] = hits[0]; used.Add(hits[0]); continue; }
            var done = false;
            foreach (var p in fd.Names)
            {
                var same = hits.Where(i => vars[i].Contains(p)).ToList();
                if (same.Count == 1) { mapping[f] = same[0]; used.Add(same[0]); done = true; break; }
                if (same.Count > 1) { if (SameContent(same, aoa, hd.DataStart)) { mapping[f] = same[0]; used.Add(same[0]); } else amb[f] = same; done = true; break; }
            }
            if (done) continue;
            if (SameContent(hits, aoa, hd.DataStart)) { mapping[f] = hits[0]; used.Add(hits[0]); } else amb[f] = hits;
        }
        var missing = spec.Must.Where(f => !mapping.ContainsKey(f) && !amb.ContainsKey(f)).ToList();
        if (spec.MustAny is not null && !spec.MustAny.Any(g => g.All(mapping.ContainsKey)))
        {
            var grp = spec.MustAny.FirstOrDefault(g => g.All(f => mapping.ContainsKey(f) || amb.ContainsKey(f))) ?? spec.MustAny[0];
            foreach (var f in grp) if (!mapping.ContainsKey(f) && !amb.ContainsKey(f) && !missing.Contains(f)) missing.Add(f);
        }
        return new MapResult(mapping, amb, missing, labels);
    }

    public sealed class ReadOpts { public Dictionary<string, int>? Manual; public Dictionary<string, string>? Prior; public int? HeaderRow; }

    private static (CkSourceSpec spec, HeaderInfo hd, MapResult res)? Ctx(string kind, IReadOnlyList<IReadOnlyList<object?>> aoa, ReadOpts? o, out string err)
    {
        var spec = CkConfig.Sources[kind]; err = "";
        HeaderInfo? hd;
        if (o?.HeaderRow is { } hr) hd = Header(aoa, hr);
        else hd = FindHeader(aoa, spec)?.H;
        if (hd is null) { err = $"Không thấy dòng tiêu đề phù hợp \"{spec.Title}\" trong 30 dòng đầu (cần các cột: {string.Join(", ", spec.Must.Select(f => spec.F(f)!.Label))})."; return null; }
        return (spec, hd, ResolveMapping(hd, spec, aoa, o?.Manual, o?.Prior));
    }

    private static CkSource NeedMap(string kind, CkSourceSpec spec, HeaderInfo hd, MapResult res) => new()
    {
        Ok = false, NeedMap = true, Kind = kind, HeaderRow = hd.Row + 1, Labels = res.Labels, Mapping = res.Mapping, Ambiguous = res.Ambiguous, Missing = res.Missing,
        Error = "Không nhận diện chắc chắn cột: " + string.Join("; ", res.Missing.Select(f => spec.F(f)!.Label + " (thiếu)").Concat(res.Ambiguous.Keys.Select(f => spec.F(f)!.Label + " (nhiều cột cùng khớp)"))) + " — hãy ánh xạ cột thủ công.",
    };

    private static Dictionary<string, string> MapLabels(CkSourceSpec spec, Dictionary<string, int> m, List<string> labels) =>
        m.Where(kv => spec.F(kv.Key) is not null).ToDictionary(kv => kv.Key, kv => kv.Value < labels.Count ? labels[kv.Value] : "");

    private sealed class Pusher
    {
        public readonly List<CkIssue> Issues = new(); public readonly Dictionary<string, int> Counts = new(); public int All;
        public void Add(string type, string sev, int row, string ma, string msg) { Counts[type] = Counts.GetValueOrDefault(type) + 1; All++; if (Issues.Count < MaxIssues) Issues.Add(new CkIssue(type, sev, row, ma, msg)); }
    }

    private static object? Cell(IReadOnlyList<object?> line, int? c) => c is { } i && i >= 0 && i < line.Count ? line[i] : null;
    private static bool IsCodeLike(string s) => Regex.IsMatch(Regex.Replace(s.ToUpperInvariant(), @"\s+", ""), "^[A-Z0-9]{15,}$");

    // ============================================================ AN AN
    public static CkSource ReadAnAn(IReadOnlyList<IReadOnlyList<object?>> aoa, ReadOpts? opts = null)
    {
        var c = Ctx("anan", aoa, opts, out var err); if (c is null) return new CkSource { Kind = "anan", Error = err };
        var (spec, hd, res) = c.Value;
        if (res.Missing.Count > 0 || res.Ambiguous.Count > 0) return NeedMap("anan", spec, hd, res);
        var m = res.Mapping; int? M(string f) => m.TryGetValue(f, out var x) ? x : null;
        var rows = new List<CkRow>(); var P = new Pusher(); var byMa = new Dictionary<string, CkRow>();
        int skipped = 0, totals = 0, dvtBad = 0;
        for (var r = hd.DataStart; r < aoa.Count; r++)
        {
            var line = aoa[r]; var raw = Cell(line, M("ma")); var ma = NormMa(raw);
            if (ma.Length == 0) { if (line.Any(NonEmpty)) skipped++; continue; }
            if (TotalRow.IsMatch(FoldClean(raw))) { totals++; continue; }
            var row = r + 1;
            double? Rd(string f, string label)
            {
                if (M(f) is not { } col) return null;
                var v = Num(Cell(line, col)); if (v is { } d && double.IsNaN(d)) { P.Add("so-loi", "loi", row, ma, label + ": không đọc được \"" + Txt(Cell(line, col)) + "\""); return double.NaN; }
                return v;
            }
            var kg = Rd("tonCuoiKg", "Tồn cuối (NET)"); var sl = Rd("tonCuoi", "Tồn cuối (số lượng)"); var w = Rd("trongLuong", "Trọng lượng");
            var dau = Rd("tonDau", "Tồn đầu"); var nhap = Rd("nhap", "Nhập"); var xuat = Rd("xuat", "Xuất");
            var kgSrc = "file";
            static bool Ok(double? x) => x is { } v && !double.IsNaN(v);
            if (kg is null && Ok(sl) && Ok(w)) { kg = R6(sl!.Value * w!.Value); kgSrc = "quy-doi"; }
            if (kg is null) { P.Add("thieu-so", "loi", row, ma, "Không có số tồn cuối (kg) — dòng không được tính"); continue; }
            if (double.IsNaN(kg.Value)) continue;
            var okW = Ok(w) && w!.Value > 0;
            var o = new CkRow { Row = row, Ma = ma, Ten = Txt(Cell(line, M("ten"))), Kg = kg.Value, KgSrc = kgSrc, Sl = Ok(sl) ? sl : null, W = okW ? w : null };
            if (M("dvt") is { } dc) { var d = Txt(Cell(line, dc)); if (IsCodeLike(d)) dvtBad++; else o.Dvt = d; }
            if (okW)
            {
                if (Ok(dau)) o.DauKg = R6(dau!.Value * w!.Value);
                if (Ok(nhap)) o.NhapKg = R6(nhap!.Value * w!.Value);
                if (Ok(xuat)) o.XuatKg = R6(xuat!.Value * w!.Value);
                if (kgSrc == "file" && Ok(sl) && Math.Abs(kg.Value - sl!.Value * w!.Value) > Math.Max(0.01, Math.Abs(kg.Value) * 1e-4))
                {
                    o.NhetLech = R6(kg.Value - sl.Value * w.Value);
                    P.Add("trong-luong", "canh-bao", row, ma, $"Tồn cuối (NET) {JsNum(kg.Value)} ≠ {JsNum(sl.Value)} × {JsNum(w.Value)} = {JsNum(R6(sl.Value * w.Value))} (sai trọng lượng Net?)");
                }
            }
            if (Ok(dau) && Ok(nhap) && Ok(xuat) && Ok(sl))
            {
                var d = R6(dau!.Value + nhap!.Value - xuat!.Value - sl!.Value);
                if (Math.Abs(d) > 0.001) { o.SelfLech = okW ? R6(d * w!.Value) : d; o.SelfUnit = okW ? "kg" : "đv"; P.Add("tu-khong-khop", "canh-bao", row, ma, $"Tồn đầu + Nhập − Xuất ≠ Tồn cuối (lệch {JsNum(d)} đơn vị)"); }
            }
            if (kg.Value < -1e-9) P.Add("ton-am", "canh-bao", row, ma, $"Tồn cuối âm ({JsNum(kg.Value)} kg)");
            if (byMa.TryGetValue(ma, out var prev))
            {
                P.Add("ma-trung", "canh-bao", row, ma, $"Mã hàng xuất hiện nhiều dòng (dòng {prev.Row} và {row}) — đã cộng dồn");
                prev.Kg = R6(prev.Kg + kg.Value); if (o.Sl is { } s2) prev.Sl = R6((prev.Sl ?? 0) + s2);
                if (o.DauKg is { } a) prev.DauKg = R6((prev.DauKg ?? 0) + a); if (o.NhapKg is { } b) prev.NhapKg = R6((prev.NhapKg ?? 0) + b); if (o.XuatKg is { } x) prev.XuatKg = R6((prev.XuatKg ?? 0) + x);
                prev.Dup = (prev.Dup ?? 1) + 1; continue;
            }
            byMa[ma] = o; rows.Add(o);
        }
        if (dvtBad > 0) P.Add("dvt-la", "thong-tin", 0, "", $"Cột \"ĐVT\" chứa mã hàng thay vì đơn vị tính ({dvtBad} dòng) — bỏ qua cột này, coi đơn vị là thùng / kiện, đối chiếu theo kg.");
        return new CkSource
        {
            Ok = true, Kind = "anan", HeaderRow = hd.Row + 1, Mapping = m, MappingLabels = MapLabels(spec, m, res.Labels), Labels = res.Labels, Rows = rows, Period = DetectPeriod(aoa),
            Stats = new CkStats { NRows = rows.Count, NCodes = rows.Count, TotalKg = R6(rows.Sum(x => x.Kg)), Skipped = skipped, Totals = totals, Issues = P.All, Counts = P.Counts }, Issues = P.Issues,
        };
    }

    // ============================================================ KẾ TOÁN (NXT)
    public static CkSource ReadKetoan(IReadOnlyList<IReadOnlyList<object?>> aoa, ReadOpts? opts = null)
    {
        var c = Ctx("ketoan", aoa, opts, out var err); if (c is null) return new CkSource { Kind = "ketoan", Error = err };
        var (spec, hd, res) = c.Value;
        if (res.Missing.Count > 0 || res.Ambiguous.Count > 0) return NeedMap("ketoan", spec, hd, res);
        var m = res.Mapping; var labels = res.Labels; int? M(string f) => m.TryGetValue(f, out var x) ? x : null;
        var mappedCols = m.Values.OrderBy(x => x).ToList();
        List<int> RangeOf(string f) { if (M(f) is not { } s) return new(); var next = mappedCols.FirstOrDefault(x => x > s, -1); var e = next < 0 ? s : next - 1; return Enumerable.Range(s, e - s + 1).ToList(); }
        var nhapCols = RangeOf("nhap"); var xuatCols = RangeOf("xuat");
        string SubLabel(int k) { var l = k < labels.Count ? labels[k] : ""; var p = l.Split('›'); var last = p[^1]; return (last.Length > 0 ? last : l).Trim(); }
        var rows = new List<CkRow>(); var P = new Pusher(); var known = new List<string>(); var byMa = new Dictionary<string, CkRow>();
        int skipped = 0, totals = 0, nZero = 0, dvtOther = 0;
        for (var r = hd.DataStart; r < aoa.Count; r++)
        {
            var line = aoa[r]; var raw = Cell(line, M("ma")); var ma = NormMa(raw);
            if (ma.Length == 0) { if (line.Select((v, i) => (v, i)).Any(x => x.i != M("ma") && NonEmpty(x.v) && x.v is string)) skipped++; continue; }
            if (TotalRow.IsMatch(FoldClean(raw))) { totals++; continue; }
            var row = r + 1;
            double? Rd(int col, string label) { var v = Num(Cell(line, col)); if (v is { } d && double.IsNaN(d)) { P.Add("so-loi", "loi", row, ma, label + ": không đọc được \"" + Txt(Cell(line, col)) + "\""); return double.NaN; } return v; }
            var cuoi = Rd(M("tonCuoi")!.Value, "Tồn cuối kỳ");
            if (cuoi is null) { known.Add(ma); nZero++; continue; }
            if (double.IsNaN(cuoi.Value)) continue;
            var dau = M("tonDau") is { } dcol ? Rd(dcol, "Tồn đầu kỳ") : null;
            double nhap = 0, xuat = 0; var bad = false;
            foreach (var k in nhapCols) { var v = Rd(k, "Nhập (" + SubLabel(k) + ")"); if (v is null) continue; if (double.IsNaN(v.Value)) { bad = true; continue; } nhap += v.Value; }
            foreach (var k in xuatCols) { var v = Rd(k, "Xuất (" + SubLabel(k) + ")"); if (v is null) continue; if (double.IsNaN(v.Value)) { bad = true; continue; } xuat += v.Value; }
            if (bad || (dau is { } dd && double.IsNaN(dd))) continue;
            nhap = R6(nhap); xuat = R6(xuat);
            known.Add(ma);
            var dauV = dau ?? 0;
            if (Math.Abs(cuoi.Value) < 1e-9 && Math.Abs(dauV) < 1e-9 && Math.Abs(nhap) < 1e-9 && Math.Abs(xuat) < 1e-9) { nZero++; continue; }
            var o = new CkRow { Row = row, Ma = ma, Ten = Txt(Cell(line, M("ten"))), Kg = cuoi.Value, DauKg = dau, NhapKg = nhap, XuatKg = xuat };
            if (M("dvt") is { } dvc) { var d = Txt(Cell(line, dvc)); o.Dvt = d; if (d.Length > 0 && FoldClean(d) != "kg") dvtOther++; }
            if (dau is { } dv) { var d = R6(dv + nhap - xuat - cuoi.Value); if (Math.Abs(d) > 0.01) { o.SelfLech = d; o.SelfUnit = "kg"; P.Add("tu-khong-khop", "canh-bao", row, ma, $"Tồn đầu + Nhập − Xuất ≠ Tồn CK (lệch {JsNum(d)} kg)"); } }
            if (cuoi.Value < -1e-9) P.Add("ton-am", "canh-bao", row, ma, $"Tồn CK âm ({JsNum(cuoi.Value)} kg)");
            if (byMa.TryGetValue(ma, out var prev))
            {
                P.Add("ma-trung", "canh-bao", row, ma, $"Mã thành phẩm xuất hiện nhiều dòng (dòng {prev.Row} và {row}) — đã cộng dồn");
                prev.Kg = R6(prev.Kg + cuoi.Value); prev.NhapKg = R6((prev.NhapKg ?? 0) + nhap); prev.XuatKg = R6((prev.XuatKg ?? 0) + xuat); if (dau is { } d2) prev.DauKg = R6((prev.DauKg ?? 0) + d2); prev.Dup = (prev.Dup ?? 1) + 1; continue;
            }
            byMa[ma] = o; rows.Add(o);
        }
        if (dvtOther > 0) P.Add("don-vi", "canh-bao", 0, "", $"{dvtOther} dòng có đơn vị tính khác \"kg\" — đối chiếu giả định đơn vị là kg.");
        return new CkSource
        {
            Ok = true, Kind = "ketoan", HeaderRow = hd.Row + 1, Mapping = m, MappingLabels = MapLabels(spec, m, labels), Labels = labels, Rows = rows, KnownCodes = known.Distinct().ToList(), Period = DetectPeriod(aoa),
            Stats = new CkStats { NRows = rows.Count, NCodes = known.Count, NZero = nZero, TotalKg = R6(rows.Sum(x => x.Kg)), TotalDau = R6(rows.Sum(x => x.DauKg ?? 0)), TotalNhap = R6(rows.Sum(x => x.NhapKg ?? 0)), TotalXuat = R6(rows.Sum(x => x.XuatKg ?? 0)), Skipped = skipped, Totals = totals, Issues = P.All, Counts = P.Counts },
            Issues = P.Issues,
        };
    }

    // ============================================================ CHỨNG TỪ KẾ TOÁN
    public static CkSource ReadCT(IReadOnlyList<IReadOnlyList<object?>> aoa, ReadOpts? opts = null)
    {
        var c = Ctx("ct", aoa, opts, out var err); if (c is null) return new CkSource { Kind = "ct", Error = err };
        var (spec, hd, res) = c.Value;
        if (res.Missing.Count > 0 || res.Ambiguous.Count > 0) return NeedMap("ct", spec, hd, res);
        var m = res.Mapping; int? M(string f) => m.TryGetValue(f, out var x) ? x : null;
        var rows = new List<CkVoucher>(); var P = new Pusher(); int skipped = 0, netLech = 0;
        for (var r = hd.DataStart; r < aoa.Count; r++)
        {
            var line = aoa[r]; var ma = NormMa(Cell(line, M("ma"))); var phieu = Txt(Cell(line, M("phieu")));
            if (ma.Length == 0 && phieu.Length == 0) continue;
            var row = r + 1;
            if (ma.Length == 0 || phieu.Length == 0) { skipped++; continue; }
            var kg = Num(Cell(line, M("kg")));
            if (kg is null) { skipped++; continue; }
            if (double.IsNaN(kg.Value)) { P.Add("so-loi", "loi", row, ma, "Số lượng kg không đọc được: \"" + Txt(Cell(line, M("kg"))) + "\""); continue; }
            var ngay = ToIso(Cell(line, M("ngay")));
            if (ngay.Length == 0) P.Add("ngay-loi", "loi", row, ma, "Ngày chứng từ không đọc được: \"" + Txt(Cell(line, M("ngay"))) + "\"");
            var loai = M("loai") is { } lc ? Txt(Cell(line, lc)).ToUpperInvariant() : "";
            var dirRaw = M("dir") is { } xc ? Txt(Cell(line, xc)).ToUpperInvariant() : "";
            var dir = dirRaw.Length > 0 ? dirRaw[..1] : "";
            if (dir is not ("N" or "X")) dir = loai.StartsWith('N') ? "N" : loai.StartsWith('X') ? "X" : "";
            if (dir.Length == 0) { P.Add("chieu-loi", "loi", row, ma, $"Không xác định được nhập hay xuất (cột X / Loại: \"{Txt(Cell(line, M("dir")))}\" / \"{loai}\")"); continue; }
            var o = new CkVoucher { Id = "kt:" + row, Src = "ketoan", Row = row, Phieu = phieu, Ngay = ngay, Kho = M("kho") is { } kc ? Txt(Cell(line, kc)) : "", Ma = ma, Ten = Txt(Cell(line, M("ten"))), Lsx = M("lsx") is { } lsc ? Txt(Cell(line, lsc)) : "", Loai = loai, Dir = dir, Kg = kg.Value };
            if (M("kien") is { } kic && Num(Cell(line, kic)) is { } k && !double.IsNaN(k)) o.Kien = k;
            if (M("netPer") is { } npc && Num(Cell(line, npc)) is { } np && !double.IsNaN(np)) o.NetPer = np;
            if (o.Kien is { } kk && o.NetPer is { } nn && Math.Abs(kg.Value - kk * nn) > Math.Max(0.01, Math.Abs(kg.Value) * 1e-4))
            { o.NetLech = R6(kg.Value - kk * nn); netLech++; P.Add("trong-luong", "canh-bao", row, ma, $"Số lượng kg {JsNum(kg.Value)} ≠ {JsNum(kk)} kiện × {JsNum(nn)}"); }
            rows.Add(o);
        }
        double Sum(string d) => R6(rows.Where(x => x.Dir == d).Sum(x => x.Kg));
        var dates = rows.Select(x => x.Ngay).Where(x => x.Length > 0).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return new CkSource
        {
            Ok = true, Kind = "ct", HeaderRow = hd.Row + 1, Mapping = m, MappingLabels = MapLabels(spec, m, res.Labels), Labels = res.Labels, Vouchers = rows,
            Stats = new CkStats { NRows = rows.Count, TotalNhap = Sum("N"), TotalXuat = Sum("X"), Skipped = skipped, NetLech = netLech, From = dates.FirstOrDefault() ?? "", To = dates.LastOrDefault() ?? "", Issues = P.All, Counts = P.Counts },
            Issues = P.Issues,
        };
    }

    // ============================================================ TỒN KHO GỬI
    private static int? FindMa21Column(IReadOnlyList<IReadOnlyList<object?>> aoa, int dataStart, Dictionary<string, int> mapping)
    {
        var used = mapping.Values.ToHashSet();
        var width = aoa.Skip(dataStart).Take(50).Select(r => r.Count).DefaultIfEmpty(0).Max();
        (int col, int ok)? best = null;
        for (var col = 0; col < width; col++)
        {
            if (used.Contains(col)) continue;
            int n = 0, ok = 0, pre = 0;
            for (var r = dataStart; r < Math.Min(aoa.Count, dataStart + 400); r++)
            {
                var line = aoa[r]; var bas = NormMa(Cell(line, mapping["ma"])); if (bas.Length == 0) continue;
                var v = NormMa(Cell(line, col)); if (v.Length == 0) continue;
                n++; if (Regex.IsMatch(v, "^[A-Z0-9]{21}$")) { ok++; if (bas.Length >= 7 && v[..7] == bas[..7]) pre++; }
            }
            if (n >= 5 && (double)ok / n >= 0.9 && (double)pre / n >= 0.9 && (best is null || ok > best.Value.ok)) best = (col, ok);
        }
        return best?.col;
    }

    public static CkSource ReadGui(IReadOnlyList<IReadOnlyList<object?>> aoa, ReadOpts? opts = null)
    {
        var c = Ctx("gui", aoa, opts, out var err); if (c is null) return new CkSource { Kind = "gui", Error = err };
        var (spec, hd, res) = c.Value;
        if (res.Missing.Count > 0 || res.Ambiguous.Count > 0) return NeedMap("gui", spec, hd, res);
        var m = res.Mapping; int? M(string f) => m.TryGetValue(f, out var x) ? x : null;
        var h21 = FindMa21Column(aoa, hd.DataStart, m);
        var rows = new List<CkRow>(); var P = new Pusher(); var skipped = 0;
        for (var r = hd.DataStart; r < aoa.Count; r++)
        {
            var line = aoa[r]; var maGui = NormMa(Cell(line, M("ma")));
            if (maGui.Length == 0) { if (line.Select((v, i) => (v, i)).Any(x => x.i != h21 && NonEmpty(x.v) && !Regex.IsMatch(Str(x.v), "^#N/A$"))) skipped++; continue; }
            if (TotalRow.IsMatch(FoldClean(Cell(line, M("ma"))))) continue;
            var row = r + 1;
            double? Rd(string f, string label) { if (M(f) is not { } col) return null; var v = Num(Cell(line, col)); if (v is { } d && double.IsNaN(d)) { P.Add("so-loi", "loi", row, maGui, label + ": không đọc được \"" + Txt(Cell(line, col)) + "\""); return double.NaN; } return v; }
            var cuoi = Rd("cuoiKg", "Tồn cuối (kg)");
            if (cuoi is null) { P.Add("thieu-so", "loi", row, maGui, "Không có số tồn cuối (kg)"); continue; }
            if (double.IsNaN(cuoi.Value)) continue;
            var o = new CkRow
            {
                Id = "gui:" + row, Row = row, MaGui = maGui, Ten = Txt(Cell(line, M("ten"))), Lo = M("lo") is { } a ? Txt(Cell(line, a)) : "", Size = M("size") is { } b ? Txt(Cell(line, b)) : "",
                QuyCach = M("quyCach") is { } q ? Txt(Cell(line, q)) : "", NgayNhap = M("ngayNhap") is { } nn ? ToIso(Cell(line, nn)) : "", Phieu = M("phieu") is { } pp ? Txt(Cell(line, pp)) : "", Kg = cuoi.Value,
            };
            double? V(string f, string l) { var v = Rd(f, l); return v is { } d && !double.IsNaN(d) ? d : null; }
            o.DauKg = V("dauKg", "dauKg"); o.NhapKg = V("nhapKg", "nhapKg"); o.XuatKg = V("xuatKg", "xuatKg"); o.DauKien = V("dauKien", "dauKien"); o.NhapKien = V("nhapKien", "nhapKien"); o.XuatKien = V("xuatKien", "xuatKien"); o.CuoiKien = V("cuoiKien", "cuoiKien");
            string ma = "", how = "";
            if (maGui.Length == 21) { ma = maGui; how = "goc"; }
            else if (h21 is { } hc && NormMa(Cell(line, hc)) is var h && h.Length == 21 && Regex.IsMatch(h, "^[A-Z0-9]{21}$")) { ma = h; how = "cot"; }
            else if (KiemKe.KhoGui.AutoMa21(maGui, o.Lo ?? "", o.Size ?? "") is { } au) { if (au.Ma.Length > 0) { ma = au.Ma; how = "auto"; o.Rule = au.Rule; } else o.Why = au.Why; }
            o.Ma = ma.Length > 0 ? ma : maGui; o.Via = how.Length > 0 ? how : "khong";
            if (ma.Length == 0) P.Add("ma-khong-chuyen", "canh-bao", row, maGui, "Không chuyển được mã kho gửi sang 21 ký tự" + (!string.IsNullOrEmpty(o.Why) ? ": " + o.Why : ""));
            if (cuoi.Value < -1e-9) P.Add("ton-am", "canh-bao", row, o.Ma, $"Tồn cuối âm ({JsNum(cuoi.Value)} kg)");
            if (o.DauKg is { } d0 && o.NhapKg is { } n0 && o.XuatKg is { } x0) { var d = R6(d0 + n0 - x0 - cuoi.Value); if (Math.Abs(d) > 0.01) { o.SelfLech = d; P.Add("tu-khong-khop", "canh-bao", row, o.Ma, $"Tồn đầu + Nhập − Xuất ≠ Tồn cuối (lệch {JsNum(d)} kg)"); } }
            rows.Add(o);
        }
        return new CkSource
        {
            Ok = true, Kind = "gui", HeaderRow = hd.Row + 1, Mapping = m, MappingLabels = MapLabels(spec, m, res.Labels), Labels = res.Labels, Rows = rows, Period = DetectPeriod(aoa),
            Stats = new CkStats { NRows = rows.Count, NCodes = rows.Select(x => x.Ma).Distinct().Count(), TotalKg = R6(rows.Sum(x => x.Kg)), TotalDau = R6(rows.Sum(x => x.DauKg ?? 0)), TotalNhap = R6(rows.Sum(x => x.NhapKg ?? 0)), TotalXuat = R6(rows.Sum(x => x.XuatKg ?? 0)), Skipped = skipped, Issues = P.All, Counts = P.Counts },
            Issues = P.Issues,
        };
    }

    // ============================================================ DANH MỤC THÀNH PHẨM (sheet DMVT)
    public static List<string>? ReadDanhMuc(IReadOnlyList<IReadOnlyList<object?>> aoa)
    {
        int colMa = -1, first = -1;
        for (var r = 0; r < Math.Min(aoa.Count, 12) && colMa < 0; r++)
            for (var c = 0; c < Math.Min(aoa[r].Count, 6); c++) if (Regex.IsMatch(NormMa(aoa[r][c]), "^[A-Z0-9]{21}$")) { colMa = c; first = r; break; }
        if (colMa < 0) return null;
        var codes = new List<string>(); var seen = new HashSet<string>(); var n = 0;
        for (var r = first; r < aoa.Count; r++) { var ma = NormMa(Cell(aoa[r], colMa)); if (!Regex.IsMatch(ma, "^[A-Z0-9]{21}$")) continue; if (seen.Add(ma)) codes.Add(ma); n++; }
        return n >= 20 ? codes : null;
    }

    // ============================================================ FILE ĐIỀU CHỈNH
    public sealed record AdjRow(int Row, string Ma, double Qty, string Reason, string Side);
    public static (bool Ok, string Error, List<AdjRow> Rows, List<(int Row, string Msg)> Errors) ReadAdjustments(IReadOnlyList<IReadOnlyList<object?>> aoa)
    {
        string[] QTY = { "dieu chinh kg", "dieu chinh", "so luong dieu chinh", "dieu chinh kg", "so luong kg", "so luong", "kg" }, WHY = { "ly do", "ly do xu ly", "ghi chu" }, SIDE = { "sua o", "ben sua", "nguon sua" };
        int hr = -1, cm = -1, cq = -1, cw = -1, cs = -1;
        for (var r = 0; r < Math.Min(aoa.Count, 15) && hr < 0; r++)
        {
            var f = aoa[r].Select(FoldClean).ToList();
            var mm = f.FindIndex(x => x is "ma hang" or "ma" or "ma thanh pham"); if (mm < 0) continue;
            var q = -1; foreach (var n in QTY) { q = f.IndexOf(n); if (q >= 0) break; }
            if (q < 0) continue; hr = r; cm = mm; cq = q; cw = f.FindIndex(x => WHY.Contains(x)); cs = f.FindIndex(x => SIDE.Contains(x));
        }
        if (hr < 0) return (false, "Không thấy dòng tiêu đề có cột \"Mã hàng\" và \"Điều chỉnh (kg)\" (hoặc \"Số lượng\") trong 15 dòng đầu.", new(), new());
        var rows = new List<AdjRow>(); var errors = new List<(int, string)>();
        for (var r = hr + 1; r < aoa.Count; r++)
        {
            var line = aoa[r]; var ma = NormMa(Cell(line, cm)); if (ma.Length == 0 && !NonEmpty(Cell(line, cq))) continue;
            var row = r + 1; if (ma.Length == 0) { errors.Add((row, "Thiếu mã hàng")); continue; }
            var q = Num(Cell(line, cq)); if (q is null || double.IsNaN(q.Value) || q.Value == 0) { errors.Add((row, q == 0 ? "Số lượng điều chỉnh bằng 0" : "Số lượng điều chỉnh không đọc được: \"" + Txt(Cell(line, cq)) + "\"")); continue; }
            var sd = cs >= 0 ? FoldClean(Cell(line, cs)) : "";
            rows.Add(new AdjRow(row, ma, R6(q.Value), cw >= 0 ? Txt(Cell(line, cw)) : "", Regex.IsMatch(sd, @"ke toan|^kt\b") ? "kt" : "kho"));
        }
        return (true, "", rows, errors);
    }

    // ============================================================ chọn sheet
    public static int ScoreSheet(IReadOnlyList<IReadOnlyList<object?>> small, string kind) => FindHeader(small, CkConfig.Sources[kind]) is { } h ? h.Score : -1;

    public static string? PickSheet(IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<object?>>> book, IList<string> names, string kind, string? prefer = null)
    {
        if (prefer is not null && names.Contains(prefer)) return prefer;
        (string sn, int s)? best = null;
        foreach (var sn in names) { var s = ScoreSheet(book[sn].Take(40).ToList(), kind); if (s >= 0 && (best is null || s > best.Value.s)) best = (sn, s); }
        return best?.sn;
    }

    /// <summary>Đọc 1 workbook (đã chuyển thành mảng dòng từng sheet) cho nguồn <paramref name="kind"/>; kế toán kèm sheet chứng từ + danh mục.</summary>
    public static CkSource ReadWorkbook(IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<object?>>> book, IList<string> names, string kind, string fileName, ReadOpts? o = null, string? sheet = null)
    {
        if (CkService.IsMove(kind))
        {
            var msn = sheet is not null && names.Contains(sheet) ? sheet : names.FirstOrDefault();
            if (msn is null) return new CkSource { Kind = kind, FileName = fileName, Error = "File không có sheet nào." };
            var mr = CkMovement.Read(book[msn], kind); mr.FileName = fileName; mr.Sheets = names.ToList(); if (mr.Ok) mr.Sheet = msn;
            return mr;
        }
        var sn = PickSheet(book, names, kind, sheet);
        if (sn is null)
        {
            var spec0 = CkConfig.Sources[kind];
            var alt = new[] { "anan", "ketoan", "gui", "kiemke", "ct" }.Where(k => k != kind).Select(k => (k, s: names.Select(n => ScoreSheet(book[n].Take(40).ToList(), k)).DefaultIfEmpty(-1).Max())).Where(x => x.s >= 0).OrderByDescending(x => x.s).Select(x => x.k).FirstOrDefault();
            return new CkSource { Kind = kind, FileName = fileName, Sheets = names.ToList(), Error = $"Không sheet nào có cấu trúc \"{spec0.Title}\" (cần cột: {string.Join(", ", spec0.Must.Select(f => spec0.F(f)!.Label))})." + (alt is not null ? $" File này giống \"{CkConfig.SourceTitle[alt]}\" — kiểm tra lại đã chọn đúng ô chưa." : "") };
        }
        var aoa = book[sn];
        var res = kind switch { "anan" => ReadAnAn(aoa, o), "ketoan" => ReadKetoan(aoa, o), "gui" => ReadGui(aoa, o), "ct" => ReadCT(aoa, o), _ => throw new ArgumentException(kind) };
        res.FileName = fileName; res.Sheet = sn; res.Sheets = names.ToList();
        if (res.Ok && res.Period is null && fileName.Length > 0 && PeriodFromFileName(fileName) is { } g) res.PeriodGuess = g;
        if (!res.Ok) return res;
        if (kind == "ketoan")
        {
            var ctName = PickSheet(book, names, "ct");
            if (ctName is not null) { var r2 = ReadCT(book[ctName]); if (r2.Ok) { r2.Sheet = ctName; res.Ct = r2; } else res.CtError = r2.Error; }
            var dmName = names.FirstOrDefault(s => Regex.IsMatch(FoldClean(s), "dmvt|danh ?muc", RegexOptions.IgnoreCase));
            if (dmName is not null && ReadDanhMuc(book[dmName].Take(8000).ToList()) is { } dm) { res.DanhMuc = dm; res.DanhMucSheet = dmName; }
        }
        return res;
    }

    private static readonly string[] Snapshot = { "anan", "gui", "kiemke" };
    /// <summary>Kỳ của 1 nguồn so với kỳ đối chiếu → (ok | khac | khong-ro, lời nhắn).</summary>
    public static (string State, string Text) CheckPeriod(CkPeriod? src, CkPeriod? period, string kind)
    {
        if (src is null || (src.From.Length == 0 && src.To.Length == 0)) return ("khong-ro", "Chưa đọc được kỳ báo cáo trong file");
        var f = src.From.Length > 0 ? src.From : src.To; var t = src.To.Length > 0 ? src.To : src.From;
        if (period is null || period.From.Length == 0 || period.To.Length == 0) return ("khong-ro", "Chưa chọn kỳ đối chiếu");
        if (f == period.From && t == period.To) return ("ok", $"Đúng kỳ {Dmy(period.From)} – {Dmy(period.To)}");
        if (Snapshot.Contains(kind) && t == period.To && src.Source != "đoán theo tên file") return ("ok", $"Số dư tại ngày {Dmy(t)} = ngày cuối kỳ");
        if (src.Source == "đoán theo tên file") return t == period.To ? ("ok", $"Ngày cuối {Dmy(t)} (đoán theo tên file)") : ("khac", $"Tên file gợi ý ngày {Dmy(t)} (đoán theo tên file) — chỉ để tham khảo");
        return ("khac", $"Báo cáo ghi ngày {Dmy(f)}" + (f != t ? " – " + Dmy(t) : "") + $" (kỳ đối chiếu {Dmy(period.From)} – {Dmy(period.To)}) — chỉ để tham khảo, số liệu vẫn được dùng");
    }
}
