using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KhoLanh.Core.KiemKe;
using static KhoLanh.Core.TonKho.JsText;

namespace KhoLanh.Core.TonKho;

/// <summary>
/// LÕI MODULE TỒN KHO AN AN (C#, port từ ton-kho-an-an.js). Giữ dữ liệu Load Data + phân loại 8 bảng (Phân Loại) +
/// các con số của Báo Cáo / Bảng Tổng Hợp HLSO / Đặc Tính / Theo Dõi Tồn — tính y hệt bản WebView2.
/// </summary>
public sealed class AnAnData
{
    // ------------------------------------------------------------------ cấu hình (giữ nguyên tên / thứ tự app cũ)

    public sealed record Module(string Id, string Label);

    /// <summary>8 bảng Phân Loại theo đúng thứ tự menu / Báo Cáo.</summary>
    public static readonly Module[] FilterModules =
    {
        new("tpc", "Thành Phẩm Chính"), new("tptam", "Thành Phẩm Tạm"), new("bm", "Tôm BM"), new("hlso", "Tôm HLSO"),
        new("tomgc", "Tôm Gia Công"), new("tomnk", "Tôm Nhập Khẩu"), new("tomnl", "Tôm Nguyên Liệu"), new("tomgs", "Tôm Giám Sát"),
    };
    /// <summary>Thứ tự khai báo trong FILTER_MODULES (Object.keys) — dùng cho ưu tiên / đối soát.</summary>
    public static readonly string[] FilterOrder = { "tpc", "tptam", "hlso", "tomgc", "tomnk", "tomnl", "tomgs", "bm" };
    public static string LabelOf(string id) => FilterModules.FirstOrDefault(m => m.Id == id)?.Label ?? id;

    public static readonly string[] RequiredColumns =
    {
        "Item ID", "Mã hàng", "Tên hàng", "Đặc tính", "Hợp đồng", "ĐVT", "Tồn đầu", "Nhập", "Xuất", "Tồn cuối", "Tồn cuối (NET)", "Trọng lượng",
        "Nguyên liệu", "Qui cách đông", "Tình trạng", "Hóa chất", "Thị trường", "Khách hàng", "Mạ băng", "Size", "Qui cách gói",
    };
    private static readonly HashSet<string> NumericCols = new() { "tồn đầu", "nhập", "xuất", "tồn cuối", "tồn cuối (net)", "trọng lượng" };
    public static readonly string[] FilterColumns =
    {
        "Mã hàng", "Tên hàng", "Size", "Đặc tính", "Hợp đồng", "ĐVT", "Tồn đầu", "Nhập", "Xuất", "Tồn cuối", "Trọng lượng", "Khách hàng", "Thị trường", "Mạ băng", "Qui cách gói", "Tình trạng",
    };
    public static readonly string[] ExportColumns = { "Mã hàng", "Tên hàng", "Size", "Đặc tính", "Hợp đồng", "Tồn cuối", "Trọng lượng" };
    public const string ManualLabel = "Bột + Sốt + Bếp", KhoGuiLabel = "Báo Cáo Kho Gửi";
    public const double TpcThresholdDefault = 8;

    public static bool IsNumericCol(string h) => NumericCols.Contains(Norm(h));
    public static bool IsSummableCol(string h) => IsNumericCol(h) && Norm(h) != "trọng lượng";
    public static string LabelKey(string s) => (s ?? "").Trim().ToLowerInvariant();

    /// <summary>Tiền tố Size khi hiện / xuất: Tôm HLSO "BTP …", Tôm Gia Công "TGC …", Tôm Nguyên Liệu "NL …", Tôm Giám Sát "TGS …".</summary>
    public static string SizePrefix(string modId, string label, string val)
    {
        if (val.Length == 0 || label != "Size") return val;
        if (modId == "hlso")
        {
            var s = JsTrim(val);
            if (Regex.IsMatch(s, "XX$", RegexOptions.IgnoreCase) || s.Contains('/')) return s;
            return "BTP " + Regex.Replace(s, @"^0+(?=\d)", "");
        }
        return modId switch { "tomgc" => "TGC " + val, "tomnl" => "NL " + val, "tomgs" => "TGS " + val, _ => val };
    }

    // ------------------------------------------------------------------ dữ liệu

    public List<string> Headers = new();
    /// <summary>Mỗi dòng = chữ từng ô theo thứ tự cột (đúng như sheet LoadData c0, c1…).</summary>
    public List<string[]> Rows = new();
    /// <summary>Ô đang lưu dạng SỐ (sau khi trừ / cộng tồn bằng phiếu Xuất Hàng, app cũ ghi số chứ không ghi chữ): khóa = dòng * 4096 + cột.</summary>
    public HashSet<long> NumCells = new();
    public List<string> Sheets = new();
    public string? CurrentSheet;
    public string FileName = "", DataDate = "", NapId = "";
    public double? ImportedAt;
    public JsonNode? HeaderRowInfo;
    /// <summary>Chuyển dòng thủ công: chỉ số dòng → id bảng.</summary>
    public Dictionary<int, string> ManualAssign = new();
    /// <summary>Điều kiện tự thêm theo bảng (sheet DieuKienTuThem).</summary>
    public Dictionary<string, List<PhanLoaiM1.CustomFilter>> Custom = new();
    /// <summary>Đặc Tính: khóa (chữ thường) → nhãn gốc, theo thứ tự xuất hiện; nhóm đã gộp theo khóa.</summary>
    public List<string> DtKeys = new();
    public Dictionary<string, string> DtLabel = new(), DtGroup = new();
    /// <summary>Báo cáo ngoài sổ sách: khóa nhãn (chữ thường) → kg (null = trống).</summary>
    public Dictionary<string, double?> ManualValues = new();
    public double TpcThreshold = TpcThresholdDefault;
    /// <summary>Thành Phẩm Chính: cột / giá trị lọc người dùng chọn (null = tự đoán như app cũ).</summary>
    public int? TpcFilterCol; public string? TpcFilterVal;

    public bool HasData => Rows.Count > 0 && Headers.Count > 0;
    public static long CellKey(int r, int c) => (long)r * 4096 + c;
    public string Cell(int r, int c) => c >= 0 && r >= 0 && r < Rows.Count && c < Rows[r].Length ? Rows[r][c] ?? "" : "";
    private static string C(string[] row, int c) => c >= 0 && c < row.Length ? row[c] ?? "" : "";

    public int Col(string label) => FindCol(Headers, label);
    public int ColExact(string label) => FindColExact(Headers, label);

    // ------------------------------------------------------------------ nạp file (prepareSheet)

    public sealed class Prepared
    {
        public List<string> Headers = new();
        public List<string[]> Rows = new();
        public int HeaderRowIdx, Score, Dropped;
        public List<string[]> Filtered = new();
        public List<int> RowNums = new();
    }

    /// <summary>prepareSheet: bỏ dòng trống, dò dòng tiêu đề trong 15 dòng đầu, bỏ dấu "." cuối số, bỏ dòng Tồn cuối trống / ".", bỏ tiền tố "TPC-".</summary>
    public static Prepared Prepare(List<string[]> raw)
    {
        var filtered = raw.Where(r => r.Any(c => JsTrim(c ?? "").Length > 0)).Select(r => r.ToArray()).ToList();
        int bestIdx = 0, bestScore = -1;
        for (var i = 0; i < Math.Min(15, filtered.Count); i++)
        {
            var row = filtered[i];
            if (!row.Any(c => JsTrim(c).Length > 0)) continue;
            var score = row.Count(cell => RequiredColumns.Any(rc => HeadersMatch(cell, rc)));
            if (score > bestScore) { bestScore = score; bestIdx = i; }
        }
        var p = new Prepared { HeaderRowIdx = bestIdx, Score = bestScore, Filtered = filtered };
        p.Headers = (bestIdx < filtered.Count ? filtered[bestIdx] : Array.Empty<string>()).Select(h => JsTrim(h ?? "")).ToList();
        var rowsRaw = filtered.Skip(bestIdx + 1).Select(r => r.ToArray()).ToList();
        foreach (var r in rowsRaw)
            for (var k = 0; k < r.Length; k++)
                if (Regex.IsMatch(r[k], @"^\s*\(?-?[\d,]*\d\.\)?\s*$")) r[k] = Regex.Replace(JsTrim(r[k]), @"\.(\)?)$", "$1");
        var tc = FindColExact(p.Headers, "Tồn cuối");
        for (var i = 0; i < rowsRaw.Count; i++)
        {
            var r = rowsRaw[i];
            var v = tc == -1 ? "x" : JsTrim(C(r, tc));
            if (tc == -1 || !(v.Length == 0 || v == ".")) { p.Rows.Add(r); p.RowNums.Add(i + 1); }
        }
        p.Dropped = rowsRaw.Count - p.Rows.Count;
        var ma = FindCol(p.Headers, "Mã hàng");
        if (ma != -1)
            foreach (var r in p.Rows)
                if (ma < r.Length && Regex.IsMatch(r[ma], @"^\s*TPC-", RegexOptions.IgnoreCase)) r[ma] = Regex.Replace(r[ma], @"^\s*TPC-", "", RegexOptions.IgnoreCase);
        return p;
    }

    // ------------------------------------------------------------------ phân loại (filterModuleBaseRows)

    private sealed record Extra(string Col, Func<string, bool> Test);
    private sealed record Cfg(Func<string, bool>? Primary, Extra[] Extras, bool ExcludeAllOthers = false);

    private static bool TamOrT(string? v) { var f = Fuzzy(v); return f.Contains("tam") || f == "t"; }
    private static string MaPos(string v) { var t = JsTrim(v); return t.Length <= 1 ? "" : t.Substring(1, Math.Min(3, t.Length - 1)).ToUpperInvariant(); }
    private static readonly string FzGs = Fuzzy("Tôm giám sát");

    private static readonly Dictionary<string, Cfg> Cfgs = new()
    {
        ["tpc"] = new(null, new[] { new Extra("Mã hàng", v => { var x = MaPos(v); return x != "RBR" && x != "CBR"; }) }),
        ["tptam"] = new(null, Array.Empty<Extra>(), true),
        ["hlso"] = new(TamOrT, new[] { new Extra("Mã hàng", v => MaPos(v) == "RHL"), new Extra("Khách hàng", v => JsTrim(v).Length == 0), new Extra("Mạ băng", v => !Fuzzy(v).Contains(FzGs)) }),
        ["tomgc"] = new(TamOrT, new[] { new Extra("Khách hàng", v => Fuzzy(v) == Fuzzy("TÂN PHONG PHÚ")), new Extra("Mã hàng", v => MaPos(v) == "RHL") }),
        ["tomnk"] = new(TamOrT, new[] { new Extra("Khách hàng", v => { var f = Fuzzy(v); return f == Fuzzy("ECUADOR") || f == Fuzzy("ẤN ĐỘ"); }) }),
        ["tomnl"] = new(TamOrT, new[] { new Extra("Mã hàng", v => MaPos(v) == "RHO"), new Extra("Khách hàng", v => JsTrim(v).Length == 0) }),
        ["tomgs"] = new(TamOrT, new[] { new Extra("Mạ băng", v => Fuzzy(v).Contains(FzGs)) }),
        ["bm"] = new(TamOrT, new[] { new Extra("Mã hàng", v => { var x = MaPos(v); return x == "RBR" || x == "CBR"; }), new Extra("Khách hàng", v => JsTrim(v).Length == 0) }),
    };
    private static bool HasKh(string id) => Cfgs[id].Extras.Any(e => e.Col == "Khách hàng");

    /// <summary>Mô tả điều kiện của 1 bảng (cho tab Điều kiện lọc).</summary>
    public static readonly Dictionary<string, string[]> RuleText = new()
    {
        ["tpc"] = new[] { "Cột Tình trạng = giá trị \"Thành phẩm\" (tự đoán, đổi được)", "Mã hàng: loại trừ ký tự vị trí 2–4 là \"RBR\" hoặc \"CBR\" (tránh trùng với module BM)" },
        ["tptam"] = new[] { "Mọi dòng KHÔNG thuộc bảng nào khác" },
        ["hlso"] = new[] { "Tình trạng chứa \"Tạm\" hoặc bằng \"T\"", "Mã hàng có ký tự vị trí 2–4 là \"RHL\"", "Khách hàng: không có khách hàng (để trống)", "Mạ băng: loại trừ dòng chứa \"Tôm giám sát\" (tránh trùng với module Tôm Giám Sát)" },
        ["tomgc"] = new[] { "Tình trạng chứa \"Tạm\" hoặc bằng \"T\"", "Khách hàng là \"TÂN PHONG PHÚ\"", "Mã hàng có ký tự vị trí 2–4 là \"RHL\"" },
        ["tomnk"] = new[] { "Tình trạng chứa \"Tạm\" hoặc bằng \"T\"", "Khách hàng là \"ECUADOR\" hoặc \"ẤN ĐỘ\"" },
        ["tomnl"] = new[] { "Tình trạng chứa \"Tạm\" hoặc bằng \"T\"", "Mã hàng có ký tự vị trí 2–4 là \"RHO\"", "Khách hàng: không có khách hàng (để trống)" },
        ["tomgs"] = new[] { "Tình trạng chứa \"Tạm\" hoặc bằng \"T\"", "Mạ băng chứa \"Tôm giám sát\"" },
        ["bm"] = new[] { "Tình trạng chứa \"Tạm\" hoặc bằng \"T\"", "Mã hàng có ký tự vị trí 2–4 là \"RBR\" hoặc \"CBR\"", "Khách hàng: không có khách hàng (để trống)" },
    };

    /// <summary>Cột lọc của Thành Phẩm Chính (resolveFilterCol).</summary>
    public int TpcCol()
    {
        if (TpcFilterCol is int c && c < Headers.Count) return c;
        var i = Col("Tình trạng");
        return i != -1 ? i : Headers.Count > 0 ? 0 : -1;
    }

    /// <summary>Giá trị khác nhau của 1 cột (đã trim, bỏ trống), sắp xếp kiểu localeCompare('vi').</summary>
    public List<string> Distinct(int ci)
    {
        if (ci < 0) return new();
        var set = new List<string>(); var seen = new HashSet<string>();
        foreach (var r in Rows) { var v = JsTrim(C(r, ci)); if (v.Length > 0 && seen.Add(v)) set.Add(v); }
        set.Sort(ViCompareDefault);
        return set;
    }

    public string TpcVal(int ci)
    {
        var vals = Distinct(ci);
        if (TpcFilterVal is { Length: > 0 } fv && vals.Contains(fv)) return fv;
        string? Guess(string[] inc, string[] exc) => vals.FirstOrDefault(v => { var fn = Fuzzy(v); return inc.All(fn.Contains) && !exc.Any(fn.Contains); });
        return Guess(new[] { "thanh pham" }, new[] { "tam" }) ?? Guess(new[] { "thanh pham" }, Array.Empty<string>()) ?? vals.FirstOrDefault() ?? "";
    }

    private Func<string[], bool> Matcher(string id)
    {
        var cfg = Cfgs[id];
        var pci = cfg.Primary is not null ? Col("Tình trạng") : -1;
        int dci = -1; var dval = "";
        if (cfg.Primary is null && !cfg.ExcludeAllOthers) { dci = TpcCol(); if (dci != -1) dval = TpcVal(dci); }
        var extras = cfg.Extras.Select(e => (Ci: Col(e.Col), e.Test)).Where(e => e.Ci != -1).ToArray();
        var ndval = Norm(dval);
        return row =>
        {
            if (cfg.Primary is not null) { if (pci != -1 && !cfg.Primary(C(row, pci))) return false; }
            else if (dci != -1 && dval.Length > 0 && Norm(C(row, dci)) != ndval) return false;
            foreach (var e in extras) if (!e.Test(C(row, e.Ci))) return false;
            return true;
        };
    }

    private readonly Dictionary<string, List<int>> _base = new();
    private Dictionary<string, SizeStats>? _sizeStats;
    private Dictionary<string, double>? _tpcTotals;

    /// <summary>Gọi sau mọi thay đổi dữ liệu / điều kiện / chuyển dòng / đặc tính.</summary>
    public void Invalidate() { _filterCols = null; _base.Clear(); _sizeStats = null; _tpcTotals = null; }

    /// <summary>Danh sách chỉ số dòng thuộc 1 bảng (filterModuleBaseRows — đúng thứ tự app cũ: dòng chuyển tay vào nằm cuối).</summary>
    public List<int> BaseRows(string id)
    {
        if (_base.TryGetValue(id, out var cached)) return cached;
        var cfg = Cfgs[id];
        var all = Enumerable.Range(0, Rows.Count);
        List<int> b;
        if (cfg.ExcludeAllOthers)
        {
            var ms = FilterOrder.Where(x => x != id).Select(Matcher).ToArray();
            b = all.Where(i => !ms.Any(fn => fn(Rows[i]))).ToList();
        }
        else if (cfg.Primary is not null)
        {
            var ci = Col("Tình trạng");
            b = ci == -1 ? all.ToList() : all.Where(i => cfg.Primary(C(Rows[i], ci))).ToList();
        }
        else
        {
            var ci = TpcCol();
            if (ci == -1) b = all.ToList();
            else { var val = TpcVal(ci); var nv = Norm(val); b = val.Length == 0 ? all.ToList() : all.Where(i => Norm(C(Rows[i], ci)) == nv).ToList(); }
        }
        foreach (var e in cfg.Extras) { var ci = Col(e.Col); if (ci != -1) b = b.Where(i => e.Test(C(Rows[i], ci))).ToList(); }
        if (!cfg.ExcludeAllOthers && !HasKh(id))
        {
            var pri = FilterOrder.Where(x => x != id && HasKh(x)).Select(Matcher).ToArray();
            if (pri.Length > 0) b = b.Where(i => !pri.Any(fn => fn(Rows[i]))).ToList();
        }
        foreach (var cf in Custom.GetValueOrDefault(id) ?? new())
        {
            var ci = Col(cf.ColLabel);
            if (ci != -1) b = b.Where(i => PhanLoaiM1.TestCustom(C(Rows[i], ci), cf)).ToList();
        }
        b = b.Where(i => !ManualAssign.TryGetValue(i, out var g) || g == id).ToList();
        var set = b.ToHashSet();
        foreach (var (i, g) in ManualAssign.OrderBy(k => k.Key)) // Object.keys: chỉ số nguyên tăng dần
            if (g == id && !set.Contains(i) && i >= 0 && i < Rows.Count) { b.Add(i); set.Add(i); }
        _base[id] = b;
        return b;
    }

    /// <summary>Đếm số dòng còn lại sau từng bước lọc của 1 bảng (tab Điều kiện lọc).</summary>
    public List<(string Step, int Left, int Removed)> Funnel(string id)
    {
        var res = new List<(string, int, int)>();
        var cfg = Cfgs[id];
        var all = Enumerable.Range(0, Rows.Count).ToList();
        List<int> b;
        if (cfg.ExcludeAllOthers)
        {
            var ms = FilterOrder.Where(x => x != id).Select(Matcher).ToArray();
            b = all.Where(i => !ms.Any(fn => fn(Rows[i]))).ToList();
            res.Add(("Không khớp bảng nào khác", b.Count, all.Count - b.Count));
        }
        else if (cfg.Primary is not null)
        {
            var ci = Col("Tình trạng");
            b = ci == -1 ? all : all.Where(i => cfg.Primary(C(Rows[i], ci))).ToList();
            res.Add((ci == -1 ? "Thiếu cột Tình trạng — bỏ qua điều kiện chính" : "Tình trạng chứa \"Tạm\" hoặc bằng \"T\"", b.Count, all.Count - b.Count));
        }
        else
        {
            var ci = TpcCol(); var val = ci == -1 ? "" : TpcVal(ci); var nv = Norm(val);
            b = ci == -1 || val.Length == 0 ? all : all.Where(i => Norm(C(Rows[i], ci)) == nv).ToList();
            res.Add(($"{(ci >= 0 ? Headers[ci] : "?")} = \"{val}\"", b.Count, all.Count - b.Count));
        }
        var k = 1;
        foreach (var e in cfg.Extras)
        {
            var ci = Col(e.Col); var before = b.Count;
            if (ci != -1) b = b.Where(i => e.Test(C(Rows[i], ci))).ToList();
            res.Add((ci == -1 ? $"Thiếu cột {e.Col} — bỏ qua: {RuleText[id][k]}" : RuleText[id][k], b.Count, before - b.Count));
            k++;
        }
        if (!cfg.ExcludeAllOthers && !HasKh(id))
        {
            var pri = FilterOrder.Where(x => x != id && HasKh(x)).ToArray();
            var ms = pri.Select(Matcher).ToArray(); var before = b.Count;
            b = b.Where(i => !ms.Any(fn => fn(Rows[i]))).ToList();
            res.Add(("Ưu tiên bảng có điều kiện Khách hàng (" + string.Join(", ", pri.Select(LabelOf)) + ")", b.Count, before - b.Count));
        }
        foreach (var cf in Custom.GetValueOrDefault(id) ?? new())
        {
            var ci = Col(cf.ColLabel); var before = b.Count;
            if (ci != -1) b = b.Where(i => PhanLoaiM1.TestCustom(C(Rows[i], ci), cf)).ToList();
            res.Add(((ci == -1 ? "Thiếu cột — bỏ qua: " : "Tự thêm: ") + CustomDesc(cf), b.Count, before - b.Count));
        }
        var outN = b.Count(i => ManualAssign.TryGetValue(i, out var g) && g != id);
        var inN = ManualAssign.Count(kv => kv.Value == id && kv.Key < Rows.Count && !b.Contains(kv.Key));
        res.Add(($"Chuyển thủ công: {outN} dòng chuyển đi, {inN} dòng chuyển vào", BaseRows(id).Count, outN - inN));
        return res;
    }

    public static readonly (string Op, string Label)[] CustomOps =
    {
        ("eq", "bằng"), ("neq", "khác"), ("contains", "chứa"), ("notcontains", "không chứa"), ("startswith", "bắt đầu bằng"), ("endswith", "kết thúc bằng"),
        ("in", "thuộc danh sách"), ("notin", "không thuộc danh sách"), ("pos_in", "ký tự vị trí … thuộc"), ("pos_notin", "ký tự vị trí … không thuộc"),
        ("gt", "lớn hơn"), ("gte", "lớn hơn hoặc bằng"), ("lt", "nhỏ hơn"), ("lte", "nhỏ hơn hoặc bằng"), ("empty", "để trống"), ("notempty", "không để trống"),
    };

    public static List<string> CfList(string v) => Regex.Split(v ?? "", @"[;|\n]").Select(x => JsTrim(x)).Where(x => x.Length > 0).ToList();

    /// <summary>customFilterDesc của app cũ.</summary>
    public static string CustomDesc(PhanLoaiM1.CustomFilter cf)
    {
        var op = CustomOps.FirstOrDefault(o => o.Op == cf.Op).Label ?? cf.Op;
        if (cf.Op is "pos_in" or "pos_notin")
        {
            var f = cf.From > 0 ? cf.From : 1; var t = cf.To > 0 ? cf.To : f;
            return $"{cf.ColLabel}: ký tự vị trí {(f == t ? f.ToString() : f + "–" + t)} {(cf.Op == "pos_in" ? "thuộc" : "không thuộc")} [{string.Join(", ", CfList(cf.Value))}]";
        }
        if (cf.Op is "in" or "notin") return $"{cf.ColLabel} {op} [{string.Join(", ", CfList(cf.Value))}]";
        return $"{cf.ColLabel} {op}" + (cf.Op is "empty" or "notempty" ? "" : $" \"{cf.Value}\"");
    }

    /// <summary>Điều kiện tự thêm dạng gốc (giữ nguyên mọi trường như id để ghi lại đúng sheet DieuKienTuThem).</summary>
    public Dictionary<string, List<JsonObject>> CustomRaw = new();

    public void SetCustom(Dictionary<string, List<JsonObject>> raw)
    {
        CustomRaw = raw;
        Custom = raw.ToDictionary(kv => kv.Key, kv => kv.Value.Select(ToCf).ToList());
        Invalidate();
    }

    public static PhanLoaiM1.CustomFilter ToCf(JsonObject o)
    {
        static string S(JsonNode? n) => n is null ? "" : n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n.ToJsonString();
        static int I(JsonNode? n) { var m = Regex.Match(S(n).Trim(), @"^[+-]?\d+"); return m.Success && int.TryParse(m.Value, out var x) ? x : 0; }
        return new PhanLoaiM1.CustomFilter { ColLabel = S(o["colLabel"]), Op = S(o["op"]), Value = S(o["value"]), From = I(o["from"]), To = I(o["to"]) };
    }

    // ------------------------------------------------------------------ cột + trọng lượng

    public sealed record ColRef(string Label, int Ci);

    // 1.153: FilterCols được gọi cho TỪNG dòng (TpcStatus / WeightBasis…) và mỗi lần dò cột chuẩn hóa chữ tiêu đề → 8000 dòng mất ~1 s. Nhớ theo bộ tiêu đề.
    private List<ColRef>? _filterCols; private List<string>? _filterColsFor; private int _filterColsN = -1;
    public List<ColRef> FilterCols()
    {
        if (_filterCols is null || !ReferenceEquals(_filterColsFor, Headers) || _filterColsN != Headers.Count)
        { _filterCols = FilterColumns.Select(l => new ColRef(l, Col(l))).Where(c => c.Ci != -1).ToList(); _filterColsFor = Headers; _filterColsN = Headers.Count; }
        return _filterCols;
    }
    public List<ColRef> ExportCols() => ExportColumns.Select(l => new ColRef(l, Col(l))).Where(c => c.Ci != -1).ToList();

    /// <summary>Cột số lượng × trọng lượng cho "Tổng KL (kg)" của các bảng Phân Loại (getFilterModuleWeightBasis).</summary>
    public (int Qty, int W)? WeightBasis()
    {
        var cols = FilterCols();
        var w = cols.FirstOrDefault(c => c.Label == "Trọng lượng");
        var q = cols.FirstOrDefault(c => c.Label == "Tồn cuối (NET)") ?? cols.FirstOrDefault(c => c.Label == "Tồn cuối");
        return w is null || q is null ? null : (q.Ci, w.Ci);
    }

    public double? RowWeight(int i, (int Qty, int W) b)
    {
        var q = LooseNum(Cell(i, b.Qty)); var w = LooseNum(Cell(i, b.W));
        return double.IsNaN(q) || double.IsNaN(w) ? null : q * w;
    }

    public static string KgText(double? v) => v is null ? "—" : En2(v.Value);

    public double? ModuleWeight(string id)
    {
        var b = WeightBasis(); if (b is null) return null;
        double sum = 0;
        foreach (var i in BaseRows(id)) if (RowWeight(i, b.Value) is double v) sum += v;
        return sum;
    }

    public (int Qty, int W)? WeightBasisAll()
    {
        var w = Col("Trọng lượng"); var q = ColExact("Tồn cuối"); if (q == -1) q = Col("Tồn cuối");
        return w == -1 || q == -1 ? null : (q, w);
    }

    public double? TotalWeightAll()
    {
        var b = WeightBasisAll(); if (b is null) return null;
        double sum = 0;
        for (var i = 0; i < Rows.Count; i++) { var q = LooseNum(Cell(i, b.Value.Qty)); var w = LooseNum(Cell(i, b.Value.W)); if (!double.IsNaN(q) && !double.IsNaN(w)) sum += q * w; }
        return sum;
    }

    public double? SumAll(string label)
    {
        var ci = Col(label); if (ci == -1) return null;
        double s = 0; foreach (var r in Rows) { var v = LooseNum(C(r, ci)); if (!double.IsNaN(v)) s += v; }
        return s;
    }

    public int? DistinctCountAll(string label)
    {
        var ci = Col(label); if (ci == -1) return null;
        return Rows.Select(r => JsTrim(C(r, ci))).Where(v => v.Length > 0).Distinct().Count();
    }

    /// <summary>Tổng các cột cộng được của 1 tập dòng (filterModuleComputeTotals): null = không có số; cột không cộng = NaN.</summary>
    public double? SumCol(IEnumerable<int> rows, int ci)
    {
        double s = 0; var has = false;
        foreach (var i in rows) { var v = LooseNum(Cell(i, ci)); if (!double.IsNaN(v)) { s += v; has = true; } }
        return has ? s : null;
    }

    public double? SumWeight(IEnumerable<int> rows, (int Qty, int W) b)
    {
        double s = 0; var has = false;
        foreach (var i in rows) if (RowWeight(i, b) is double v) { s += v; has = true; }
        return has ? s : null;
    }

    // ------------------------------------------------------------------ Thành Phẩm Chính: trạng thái tồn

    /// <summary>Trạng thái của 1 dòng TPC theo tổng Tồn cuối cùng (Mã hàng + Hợp đồng): "Hết hàng" / "Sắp hết" / "Còn hàng". null = thiếu cột.</summary>
    public string? TpcStatus(int i)
    {
        var cols = FilterCols();
        var ma = cols.FirstOrDefault(c => c.Label == "Mã hàng"); var hd = cols.FirstOrDefault(c => c.Label == "Hợp đồng"); var tc = cols.FirstOrDefault(c => c.Label == "Tồn cuối");
        if (ma is null || hd is null || tc is null) return null;
        if (_tpcTotals is null)
        {
            _tpcTotals = new();
            foreach (var r in BaseRows("tpc"))
            {
                var key = JsTrim(Cell(r, ma.Ci)) + "|" + JsTrim(Cell(r, hd.Ci));
                var q = JsNumber(Cell(r, tc.Ci)); if (double.IsNaN(q)) q = 0;
                _tpcTotals[key] = _tpcTotals.GetValueOrDefault(key) + q;
            }
        }
        var total = _tpcTotals.GetValueOrDefault(JsTrim(Cell(i, ma.Ci)) + "|" + JsTrim(Cell(i, hd.Ci)));
        var th = double.IsFinite(TpcThreshold) && TpcThreshold >= 0 ? TpcThreshold : TpcThresholdDefault;
        return total <= 0 ? "Hết hàng" : total <= th ? "Sắp hết" : "Còn hàng";
    }

    // ------------------------------------------------------------------ Đặc Tính

    public string? GroupOf(string label) { var k = LabelKey(label); return DtGroup.TryGetValue(k, out var g) && g.Length > 0 ? g : null; }

    public sealed record DtRow(string Label, int Count, double Weight, bool HasWeight, bool Present);

    /// <summary>
    /// getDacTinhBreakdown: đặc tính của 5 bảng HLSO / Gia Công / Nhập Khẩu / Nguyên Liệu / Giám Sát + mọi đặc tính đã ghi nhận trước đó.
    /// Đặc tính mới gặp được thêm vào danh sách ghi nhận (<paramref name="knownChanged"/> = true → cần lưu sheet DacTinh).
    /// </summary>
    public List<DtRow> DacTinhBreakdown(out bool knownChanged)
    {
        knownChanged = false;
        var ci = Headers.Count > 0 ? Col("Đặc tính") : -1;
        var basis = WeightBasisAll();
        var live = new List<string>(); var map = new Dictionary<string, (int C, double W, bool H)>(StringComparer.Ordinal);
        if (Rows.Count > 0 && ci != -1)
        {
            var seen = new HashSet<int>();
            foreach (var (mod, _) in HlsoGroups)
                foreach (var i in BaseRows(mod))
                {
                    if (!seen.Add(i)) continue;
                    var label = JsTrim(Cell(i, ci)); if (label.Length == 0) continue;
                    if (!map.TryGetValue(label, out var e)) { e = (0, 0, false); live.Add(label); }
                    e.C++;
                    if (basis is not null && RowWeight(i, basis.Value) is double w) { e.W += w; e.H = true; }
                    map[label] = e;
                }
        }
        foreach (var label in live)
        {
            var key = LabelKey(label);
            if (!DtLabel.ContainsKey(key) || DtLabel[key].Length == 0) { if (!DtLabel.ContainsKey(key)) DtKeys.Add(key); DtLabel[key] = label; knownChanged = true; }
        }
        var result = live.Select(l => new DtRow(l, map[l].C, map[l].W, map[l].H, true)).ToList();
        var liveKeys = live.Select(LabelKey).ToHashSet();
        var names = live.ToHashSet(StringComparer.Ordinal);
        foreach (var key in DtKeys)
        {
            if (liveKeys.Contains(key)) continue;
            var kl = DtLabel[key];
            var row = new DtRow(kl, 0, 0, false, false);
            var at = result.FindIndex(r => r.Label == kl);
            if (at >= 0) result[at] = row; else result.Add(row);
        }
        return StableSort(result, (a, b) => ViNumericCompare(a.Label, b.Label));
    }

    /// <summary>Các đặc tính đã gộp vào 1 nhóm (getDacTinhGroupMembers).</summary>
    public List<(string Key, string Label)> GroupMembers(string group) =>
        StableSort(DtKeys.Where(k => DtGroup.TryGetValue(k, out var g) && g == group).Select(k => (k, DtLabel.GetValueOrDefault(k) ?? k)).ToList(), (a, b) => ViNumericCompare(a.Item2, b.Item2));

    public void AssignGroup(IEnumerable<string> labels, string group)
    {
        foreach (var l in labels)
        {
            var k = LabelKey(l);
            if (!DtLabel.ContainsKey(k)) DtKeys.Add(k);
            DtLabel[k] = l; DtGroup[k] = group;
        }
        Invalidate();
    }

    public void Ungroup(string key) { DtGroup.Remove(key); Invalidate(); }

    public void ClearDacTinh() { DtKeys.Clear(); DtLabel.Clear(); DtGroup.Clear(); Invalidate(); }

    // ------------------------------------------------------------------ Báo Cáo

    public sealed record TotRow(string Label, double? Weight, bool Manual);

    /// <summary>
    /// Bảng "Báo Cáo Tổng Tồn": Bột + Sốt + Bếp (nhập tay) + 8 bảng; bảng chỉ giữ dòng có tồn (|kg| ≥ 0.005).
    /// 1.69: dòng Bột + Sốt + Bếp LUÔN hiện (chưa nhập = 0) để không bị sót khi báo cáo (app cũ ẩn khi trống).
    /// </summary>
    public List<TotRow> TongTonRows()
    {
        var list = new List<TotRow> { new(ManualLabel, ManualValues.GetValueOrDefault(LabelKey(ManualLabel)) is double mv && !double.IsNaN(mv) ? mv : 0, true) };
        foreach (var m in FilterModules) list.Add(new(m.Label, ModuleWeight(m.Id), false));
        return list.Where(r => r.Manual || HasStock(r.Weight)).ToList();
    }

    public static bool HasStock(double? v) => v is double d && !double.IsNaN(d) && Math.Abs(d) >= 0.005;
    public static double? TotalOf(IEnumerable<TotRow> rows) { var l = rows.ToList(); return l.Any(r => r.Weight is not null) ? l.Sum(r => r.Weight ?? 0) : null; }

    public double? KhoGui => ManualValues.GetValueOrDefault(LabelKey(KhoGuiLabel));

    /// <summary>findBaoCaoTotalWeightByLabel.</summary>
    public double? WeightByLabel(string label)
    {
        var t = LabelKey(label);
        if (t == LabelKey(ManualLabel)) return ManualValues.GetValueOrDefault(t);
        var m = FilterModules.FirstOrDefault(x => LabelKey(x.Label) == t);
        return m is null ? null : ModuleWeight(m.Id);
    }

    // ------------------------------------------------------------------ Bảng Tổng Hợp HLSO

    public static readonly string[] HlsoColumns =
    {
        "MKS", "NHẬT, ASC, HQ, MỸ", "NHẬT, ASC-A,B, HQ, MỸ", "NHẬT, ASC-B, HQ, MỸ", "NHẬT, ASC, HQ", "NHẬT, EU, HQ", "NHẬT, ASC-A,B, HQ", "NHẬT, ASC-B, HQ",
        "NHẬT, ASC", "NHẬT, HQ", "NHẬT, EU", "ASC, HQ, MỸ", "ASC-A, B, HQ, MỸ", "ASC-B, HQ, MỸ", "ASC, HQ", "ASC-A,B, HQ", "ASC-B, HQ", "ASC",
        "EU, HQ, MỸ", "EU, HQ", "EU", "HQ", "KHÔNG ĐẠT",
    };
    public static readonly string[] HlsoStaticRows = { "Thành phẩm chính", "Thành phẩm tạm", "Tôm BM", "Bột + Sốt + Bếp" };
    public static readonly (string Mod, string Title)[] HlsoGroups = { ("hlso", "TÔM HLSO"), ("tomgc", "TÔM GIA CÔNG"), ("tomnk", "TÔM NHẬP KHẨU"), ("tomnl", "TÔM NGUYÊN LIỆU"), ("tomgs", "TÔM GIÁM SÁT") };

    public sealed class SizeEntry { public double Total; public bool HasTotal; public Dictionary<string, double> ByGroup = new(); }
    public sealed class SizeStats { public List<string> Order = new(); public Dictionary<string, SizeEntry> BySize = new(); }

    private static int SizeCat(string s) { var t = JsTrim(s); return Regex.IsMatch(t, "XX$", RegexOptions.IgnoreCase) ? 0 : t.Contains('/') ? 1 : 2; }
    public static int CompareSize(string a, string b) { var ca = SizeCat(a); var cb = SizeCat(b); return ca != cb ? ca - cb : ViNumericCompare(a, b); }

    public SizeStats Sizes(string mod)
    {
        _sizeStats ??= new();
        if (_sizeStats.TryGetValue(mod, out var st)) return st;
        st = new SizeStats(); _sizeStats[mod] = st;
        if (!HasData) return st;
        var sci = Col("Size"); if (sci == -1) return st;
        var dci = Col("Đặc tính");
        var basis = WeightBasis(); if (basis is null) return st;
        foreach (var i in BaseRows(mod))
        {
            var size = JsTrim(Cell(i, sci)); if (size.Length == 0) continue;
            if (!st.BySize.TryGetValue(size, out var e)) { e = new SizeEntry(); st.BySize[size] = e; st.Order.Add(size); }
            if (RowWeight(i, basis.Value) is not double v) continue;
            string? grp = null;
            if (dci != -1) { var dt = JsTrim(Cell(i, dci)); if (dt.Length > 0) grp = GroupOf(dt); }
            if (grp is null) continue;
            e.Total += v; e.HasTotal = true;
            e.ByGroup[grp] = e.ByGroup.GetValueOrDefault(grp) + v;
        }
        st.Order = StableSort(st.Order, CompareSize);
        return st;
    }

    public static List<T> StableSort<T>(List<T> l, Comparison<T> cmp) => l.Select((x, k) => (x, k)).OrderBy(t => t, Comparer<(T x, int k)>.Create((a, b) => { var c = cmp(a.x, b.x); return c != 0 ? c : a.k.CompareTo(b.k); })).Select(t => t.x).ToList();

    public sealed record HlsoRow(string Label, string Display, string Kind, string Mod);

    public List<HlsoRow> HlsoRows()
    {
        var rows = HlsoStaticRows.Select(l => new HlsoRow(l, l, "static", "")).ToList();
        foreach (var (mod, title) in HlsoGroups)
        {
            rows.Add(new HlsoRow(title, title, "group-title", mod));
            if (HasData) foreach (var s in Sizes(mod).Order) rows.Add(new HlsoRow(s, SizePrefix(mod, "Size", s), "group-size", mod));
        }
        return rows;
    }

    public double? HlsoTotal(HlsoRow r)
    {
        if (r.Kind == "group-title")
        {
            var st = Sizes(r.Mod); double s = 0; var has = false;
            foreach (var z in st.Order) { var e = st.BySize[z]; if (e.HasTotal) { s += e.Total; has = true; } }
            return has ? s : null;
        }
        if (r.Kind == "group-size") { if (!HasData) return null; var e = Sizes(r.Mod).BySize.GetValueOrDefault(r.Label); return e is { HasTotal: true } ? e.Total : null; }
        return WeightByLabel(r.Label);
    }

    public double? HlsoCell(HlsoRow r, string grp)
    {
        if (r.Kind == "group-title")
        {
            var st = Sizes(r.Mod); double s = 0; var has = false;
            foreach (var z in st.Order) if (st.BySize[z].ByGroup.TryGetValue(grp, out var g)) { s += g; has = true; }
            return has ? s : null;
        }
        if (r.Kind == "group-size") { if (!HasData) return null; var e = Sizes(r.Mod).BySize.GetValueOrDefault(r.Label); return e is not null && e.ByGroup.TryGetValue(grp, out var g) ? g : null; }
        return null;
    }

    /// <summary>getHlsoSummaryRowsInStock: bỏ dòng hết tồn; tiêu đề cụm chỉ giữ khi còn ít nhất 1 Size có tồn.</summary>
    public List<HlsoRow> HlsoRowsInStock()
    {
        bool InStock(HlsoRow r) => HasStock(HlsoTotal(r)) || HlsoColumns.Any(g => HasStock(HlsoCell(r, g)));
        var outL = new List<HlsoRow>(); HlsoRow? pending = null; var used = false;
        foreach (var r in HlsoRows())
        {
            if (r.Kind == "group-title") { pending = r; used = false; continue; }
            if (r.Kind == "group-size")
            {
                if (!InStock(r)) continue;
                if (pending is not null && !used) { outL.Add(pending); used = true; }
                outL.Add(r); continue;
            }
            if (InStock(r)) outL.Add(r);
        }
        return outL;
    }

    public (double Total, bool Has) HlsoGrandTotal()
    {
        double t = 0; var has = false;
        foreach (var r in HlsoRows()) { if (r.Kind == "group-title") continue; if (HlsoTotal(r) is double v && !double.IsNaN(v)) { t += v; has = true; } }
        return (t, has);
    }

    /// <summary>Cột thị trường có ít nhất 1 ô số (để ẩn cột trống).</summary>
    public bool[] HlsoColumnsWithData()
    {
        var sizes = HlsoRows().Where(r => r.Kind == "group-size").ToList();
        return HlsoColumns.Select(g => sizes.Any(r => HlsoCell(r, g) is double v && !double.IsNaN(v))).ToArray();
    }

    // ------------------------------------------------------------------ Đối soát

    public sealed record GapRow(string Ma, string Ten, string Size, string DacTinh, string Module, double? Weight);
    public sealed record OverlapRow(int I, string Ma, string Ten, string Size, List<string> Modules, double? Weight, double? Excess);
    public sealed record OrphanRow(int I, string Ma, string Ten, string Size, double? Weight);
    public sealed class Recon
    {
        public List<OverlapRow> Overlap = new(); public List<OrphanRow> Orphan = new();
        public double OverlapExcess, OrphanWeight;
        public List<(string Label, List<string> Missing)> ColumnWarnings = new();
    }

    public Recon? Reconcile()
    {
        if (!HasData) return null;
        var sets = FilterOrder.ToDictionary(id => id, id => BaseRows(id).ToHashSet());
        var basis = WeightBasisAll();
        int ma = Col("Mã hàng"), ten = Col("Tên hàng"), sz = Col("Size");
        var rc = new Recon();
        for (var i = 0; i < Rows.Count; i++)
        {
            var hit = FilterOrder.Where(id => sets[id].Contains(i)).ToList();
            var w = basis is null ? null : RowWeight(i, basis.Value);
            if (hit.Count >= 2)
            {
                double? ex = w is null ? null : w * (hit.Count - 1);
                if (ex is double e) rc.OverlapExcess += e;
                rc.Overlap.Add(new OverlapRow(i, Cell(i, ma), Cell(i, ten), Cell(i, sz), hit.Select(LabelOf).ToList(), w, ex));
            }
            else if (hit.Count == 0) rc.Orphan.Add(new OrphanRow(i, Cell(i, ma), Cell(i, ten), Cell(i, sz), w));
        }
        rc.Overlap = StableSort(rc.Overlap, (a, b) => Math.Sign((b.Excess ?? 0) - (a.Excess ?? 0)));
        rc.OrphanWeight = rc.Orphan.Sum(r => r.Weight ?? 0);
        foreach (var id in FilterOrder)
        {
            var miss = new List<string>();
            if (Cfgs[id].Primary is not null && Col("Tình trạng") == -1) miss.Add("Tình trạng");
            foreach (var e in Cfgs[id].Extras) if (Col(e.Col) == -1) miss.Add(e.Col);
            if (miss.Count > 0) rc.ColumnWarnings.Add((LabelOf(id), miss));
        }
        return rc;
    }

    /// <summary>Dòng thuộc 5 cụm Size mà Đặc tính trống / chưa gộp nhóm — nguyên nhân lệch giữa Bảng Tổng Hợp HLSO và tổng 8 bảng.</summary>
    public (List<GapRow> Rows, double Weight)? HlsoGap()
    {
        if (!HasData) return null;
        int dci = Col("Đặc tính"), ma = Col("Mã hàng"), ten = Col("Tên hàng"), sz = Col("Size");
        var list = new List<GapRow>(); double gw = 0;
        foreach (var (mod, title) in HlsoGroups)
        {
            var b = WeightBasis(); if (b is null) continue;
            foreach (var i in BaseRows(mod))
            {
                var dt = dci != -1 ? JsTrim(Cell(i, dci)) : "";
                if (dt.Length > 0 && GroupOf(dt) is not null) continue;
                var w = RowWeight(i, b.Value); if (w is double x) gw += x;
                list.Add(new GapRow(Cell(i, ma), Cell(i, ten), Cell(i, sz), dt.Length == 0 ? "(trống)" : dt, title, w));
            }
        }
        return (StableSort(list, (a, b) => Math.Sign((b.Weight ?? 0) - (a.Weight ?? 0))), gw);
    }

    /// <summary>Tỉ lệ % theo Size (các dòng Size của Bảng Tổng Hợp HLSO).</summary>
    public (List<(string Label, double Weight)> Rows, double Total)? SizeBreakdown()
    {
        var rows = HlsoRows().Where(r => r.Kind == "group-size").Select(r => (r.Display, W: HlsoTotal(r))).Where(x => x.W is not null).Select(x => (x.Display, x.W!.Value)).ToList();
        if (rows.Count == 0) return null;
        return (rows, rows.Sum(r => r.Item2));
    }
}
