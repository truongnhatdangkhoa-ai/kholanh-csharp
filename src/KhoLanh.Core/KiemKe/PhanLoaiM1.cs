using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.KiemKe;

/// <summary>
/// PHÂN LOẠI TỒN KHO THEO MODULE TỒN KHO AN AN (› Phân Loại) — CHỈ ĐỌC (port từ dc-phanloai-m1.js, bản sao quy tắc FILTER_MODULES của module 1).
/// Cho Đối chiếu kiểm kê biết 1 dòng tồn thuộc nhóm nào mà không phải mở / sửa module Tồn kho An An.
/// Nhóm: tpc = Thành Phẩm Chính · tptam = Thành Phẩm Tạm · bm = Tôm BM · hlso = Tôm HLSO · tomgc = Tôm Gia Công · tomnk = Tôm Nhập Khẩu · tomnl = Tôm Nguyên Liệu · tomgs = Tôm Giám Sát.
/// </summary>
public static class PhanLoaiM1
{
    public static readonly string[] Order = { "tpc", "tptam", "bm", "hlso", "tomgc", "tomnk", "tomnl", "tomgs" };
    public static readonly Dictionary<string, string> Label = new()
    {
        ["tpc"] = "Thành Phẩm Chính", ["tptam"] = "Thành Phẩm Tạm", ["bm"] = "Tôm BM", ["hlso"] = "Tôm HLSO", ["tomgc"] = "Tôm Gia Công", ["tomnk"] = "Tôm Nhập Khẩu", ["tomnl"] = "Tôm Nguyên Liệu", ["tomgs"] = "Tôm Giám Sát",
    };
    public static string LabelOf(string g) => g.Length == 0 ? "Chưa phân loại" : Label.GetValueOrDefault(g, g);

    /// <summary>Điều kiện lọc tự thêm của người dùng ở Tồn kho An An (sheet DieuKienTuThem): { nhóm: [ {colLabel, op, value, from, to} ] }.</summary>
    public sealed class CustomFilter { public string ColLabel = "", Op = "", Value = ""; public int From, To; }

    public sealed class Result
    {
        public List<string>[] Hit = Array.Empty<List<string>>();
        public string[] Primary = Array.Empty<string>();
    }

    /// <summary>1.128: "manualAssign" trong LoadDataMeta của Tồn kho An An (chỉ số dòng → nhóm).</summary>
    public static Dictionary<int, string> ManualOf(System.Text.Json.Nodes.JsonObject? meta)
    {
        var d = new Dictionary<int, string>();
        if (meta?["manualAssign"] is System.Text.Json.Nodes.JsonObject mo)
            foreach (var kv in mo) { var v = kv.Value?.ToString() ?? ""; if (int.TryParse(kv.Key, out var i) && v.Length > 0) d[i] = v; }
        return d;
    }
    /// <summary>1.128: điều kiện tự thêm (sheet DieuKienTuThem của ton-kho-an-an: 1 dòng, data = "__JSON__{ nhóm: [ … ] }").</summary>
    public static Dictionary<string, List<CustomFilter>> CustomOf(System.Text.Json.Nodes.JsonArray? rows)
    {
        var d = new Dictionary<string, List<CustomFilter>>();
        if (rows is null || rows.Count == 0 || rows[0] is not System.Text.Json.Nodes.JsonObject ro) return d;
        System.Text.Json.Nodes.JsonObject? cj;
        try { cj = System.Text.Json.Nodes.JsonNode.Parse((ro["data"]?.ToString() ?? "").Replace("__JSON__", "")) as System.Text.Json.Nodes.JsonObject; } catch { return d; }
        if (cj is null) return d;
        static int I(System.Text.Json.Nodes.JsonNode? n) => double.TryParse(n?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (int)v : 0;
        foreach (var kv in cj)
            if (kv.Value is System.Text.Json.Nodes.JsonArray arr)
                d[kv.Key] = arr.OfType<System.Text.Json.Nodes.JsonObject>().Select(o => new CustomFilter { ColLabel = o["colLabel"]?.ToString() ?? "", Op = o["op"]?.ToString() ?? "", Value = o["value"]?.ToString() ?? "", From = I(o["from"]), To = I(o["to"]) }).ToList();
        return d;
    }

    public static string Fuzzy(string? s)
    {
        var d = new string((s ?? "").Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Replace('đ', 'd').Replace('Đ', 'd').ToLowerInvariant();
        return Regex.Replace(Regex.Replace(d, @"[.,()/\\\-_:;]", " "), @"\s+", " ").Trim();
    }
    private static string Norm(string? s) => Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
    private static string NoSpace(string? s) => Regex.Replace(s ?? "", @"\s+", "");
    private static bool HeadersMatch(string a, string b) { var fa = Fuzzy(a); var fb = Fuzzy(b); return fa.Length > 0 && fb.Length > 0 && (fa == fb || fa.Contains(fb) || fb.Contains(fa)); }

    private static double LooseNum(string? v)
    {
        var s = Regex.Replace((v ?? "").Trim(), @"[ \s]", ""); if (s.Length == 0) return double.NaN;
        var hasComma = s.Contains(','); var hasDot = s.Contains('.');
        if (hasComma && hasDot) s = s.LastIndexOf(',') > s.LastIndexOf('.') ? s.Replace(".", "").Replace(',', '.') : s.Replace(",", "");
        else if (hasComma) s = Regex.IsMatch(s, @"^-?\d+,\d{1,2}$") ? s.Replace(',', '.') : s.Replace(",", "");
        var m = Regex.Match(s, @"^-?\d+(\.\d+)?");
        return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
    }
    private static List<string> CfList(string? v) => Regex.Split(v ?? "", @"[;|\n]").Select(x => x.Trim()).Where(x => x.Length > 0).ToList();

    public static bool TestCustom(string? cell, CustomFilter cf)
    {
        double a, b;
        switch (cf.Op)
        {
            case "eq": return Norm(cell) == Norm(cf.Value);
            case "neq": return Norm(cell) != Norm(cf.Value);
            case "contains": return NoSpace(Fuzzy(cell)).Contains(NoSpace(Fuzzy(cf.Value)));
            case "notcontains": return !NoSpace(Fuzzy(cell)).Contains(NoSpace(Fuzzy(cf.Value)));
            case "gt": a = LooseNum(cell); b = LooseNum(cf.Value); return !double.IsNaN(a) && !double.IsNaN(b) && a > b;
            case "lt": a = LooseNum(cell); b = LooseNum(cf.Value); return !double.IsNaN(a) && !double.IsNaN(b) && a < b;
            case "gte": a = LooseNum(cell); b = LooseNum(cf.Value); return !double.IsNaN(a) && !double.IsNaN(b) && a >= b;
            case "lte": a = LooseNum(cell); b = LooseNum(cf.Value); return !double.IsNaN(a) && !double.IsNaN(b) && a <= b;
            case "empty": return (cell ?? "").Trim().Length == 0;
            case "notempty": return (cell ?? "").Trim().Length > 0;
            case "startswith": { var q = NoSpace(Fuzzy(cf.Value)); return q.Length > 0 && NoSpace(Fuzzy(cell)).StartsWith(q, StringComparison.Ordinal); }
            case "endswith": { var q = NoSpace(Fuzzy(cf.Value)); return q.Length > 0 && NoSpace(Fuzzy(cell)).EndsWith(q, StringComparison.Ordinal); }
            case "in": return CfList(cf.Value).Any(v => Norm(cell) == Norm(v));
            case "notin": return !CfList(cf.Value).Any(v => Norm(cell) == Norm(v));
            case "pos_in": case "pos_notin":
            {
                var from = Math.Max(1, cf.From == 0 ? 1 : cf.From); var to = Math.Max(from, cf.To == 0 ? from : cf.To);
                var t = (cell ?? "").Trim(); var seg = from - 1 >= t.Length ? "" : t.Substring(from - 1, Math.Min(to - from + 1, t.Length - from + 1)).ToUpperInvariant();
                var hit = CfList(cf.Value).Any(v => v.Trim().ToUpperInvariant() == seg);
                return cf.Op == "pos_in" ? hit : !hit;
            }
            default: return true;
        }
    }

    private static bool TamOrT(string? v) { var f = Fuzzy(v); return f.Contains("tam") || f == "t"; }
    private static string MaPos(string? v) { var t = (v ?? "").Trim(); return t.Length <= 1 ? "" : t.Substring(1, Math.Min(3, t.Length - 1)).ToUpperInvariant(); }
    private sealed record Extra(string Col, Func<string, bool> Test);
    private static readonly Extra Rhl = new("Mã hàng", v => MaPos(v) == "RHL");
    private static readonly Extra NoKh = new("Khách hàng", v => (v ?? "").Trim().Length == 0);

    private sealed record Cfg(string? PrimaryCol, Func<string, bool>? Primary, Extra[] Extras, bool ExcludeAllOthers = false);
    private static readonly Dictionary<string, Cfg> C = new()
    {
        ["tpc"] = new(null, null, new[] { new Extra("Mã hàng", v => { var x = MaPos(v); return x != "RBR" && x != "CBR"; }) }),
        ["tptam"] = new(null, null, Array.Empty<Extra>(), true),
        ["hlso"] = new("Tình trạng", TamOrT, new[] { Rhl, NoKh, new Extra("Mạ băng", v => !Fuzzy(v).Contains(Fuzzy("Tôm giám sát"))) }),
        ["tomgc"] = new("Tình trạng", TamOrT, new[] { new Extra("Khách hàng", v => Fuzzy(v) == Fuzzy("TÂN PHONG PHÚ")), Rhl }),
        ["tomnk"] = new("Tình trạng", TamOrT, new[] { new Extra("Khách hàng", v => { var f = Fuzzy(v); return f == Fuzzy("ECUADOR") || f == Fuzzy("ẤN ĐỘ"); }) }),
        ["tomnl"] = new("Tình trạng", TamOrT, new[] { new Extra("Mã hàng", v => MaPos(v) == "RHO"), NoKh }),
        ["tomgs"] = new("Tình trạng", TamOrT, new[] { new Extra("Mạ băng", v => Fuzzy(v).Contains(Fuzzy("Tôm giám sát"))) }),
        ["bm"] = new("Tình trạng", TamOrT, new[] { new Extra("Mã hàng", v => { var x = MaPos(v); return x == "RBR" || x == "CBR"; }), NoKh }),
    };
    private static bool HasKh(string id) => C[id].Extras.Any(e => e.Col == "Khách hàng");

    /// <summary>Phân loại từng dòng Load Data (headers + rows theo thứ tự cột). manual = chuyển thủ công của module 1 { chỉ số dòng → nhóm }.</summary>
    public static Result Classify(IReadOnlyList<string> headers, IReadOnlyList<string[]> rows, IReadOnlyDictionary<int, string>? manual = null, IReadOnlyDictionary<string, List<CustomFilter>>? custom = null)
    {
        manual ??= new Dictionary<int, string>(); custom ??= new Dictionary<string, List<CustomFilter>>();
        int ColOf(string label) { for (var i = 0; i < headers.Count; i++) if (HeadersMatch(headers[i], label)) return i; return -1; }
        string Cell(string[] r, int i) => i >= 0 && i < r.Length ? r[i] ?? "" : "";
        var tpcCi = ColOf("Tình trạng"); if (tpcCi == -1) tpcCi = headers.Count > 0 ? 0 : -1;
        var tpcVal = "";
        if (tpcCi >= 0)
        {
            var vals = rows.Select(r => Cell(r, tpcCi).Trim()).Where(v => v.Length > 0).Distinct().OrderBy(v => v, StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), false)).ToList();
            tpcVal = vals.FirstOrDefault(v => { var f = Fuzzy(v); return f.Contains("thanh pham") && !f.Contains("tam"); }) ?? vals.FirstOrDefault(v => Fuzzy(v).Contains("thanh pham")) ?? vals.FirstOrDefault() ?? "";
        }
        List<(int Ci, Func<string, bool> Test)> Extras(string id) => C[id].Extras.Select(e => (Ci: ColOf(e.Col), e.Test)).Where(e => e.Ci != -1).ToList();
        Func<string[], bool> Matcher(string id)
        {
            var c = C[id]; var ex = Extras(id); var pci = c.Primary is not null ? ColOf(c.PrimaryCol!) : -1;
            return row =>
            {
                if (c.Primary is not null) { if (pci != -1 && !c.Primary(Cell(row, pci))) return false; }
                else if (!c.ExcludeAllOthers && tpcCi >= 0 && tpcVal.Length > 0 && Norm(Cell(row, tpcCi)) != Norm(tpcVal)) return false;
                foreach (var e in ex) if (!e.Test(Cell(row, e.Ci))) return false;
                return true;
            };
        }
        var M = Order.Where(id => !C[id].ExcludeAllOthers).ToDictionary(id => id, Matcher);
        var sets = new Dictionary<string, HashSet<int>>();
        foreach (var id in Order)
        {
            var c = C[id]; List<int> bas = new();
            if (c.ExcludeAllOthers)
            {
                var others = Order.Where(x => x != id).Select(x => M[x]).ToList();
                for (var i = 0; i < rows.Count; i++) if (!others.Any(fn => fn(rows[i]))) bas.Add(i);
            }
            else if (c.Primary is not null)
            {
                var ci = ColOf(c.PrimaryCol!);
                for (var i = 0; i < rows.Count; i++) if (ci == -1 || c.Primary(Cell(rows[i], ci))) bas.Add(i);
                foreach (var e in Extras(id)) bas = bas.Where(i => e.Test(Cell(rows[i], e.Ci))).ToList();
            }
            else
            {
                for (var i = 0; i < rows.Count; i++) if (tpcCi < 0 || tpcVal.Length == 0 || Norm(Cell(rows[i], tpcCi)) == Norm(tpcVal)) bas.Add(i);
                foreach (var e in Extras(id)) bas = bas.Where(i => e.Test(Cell(rows[i], e.Ci))).ToList();
            }
            if (!c.ExcludeAllOthers && !HasKh(id))   // ưu tiên nhóm có điều kiện Khách hàng
            {
                var pri = Order.Where(x => x != id && HasKh(x)).Select(x => M[x]).ToList();
                if (pri.Count > 0) bas = bas.Where(i => !pri.Any(fn => fn(rows[i]))).ToList();
            }
            foreach (var cf in custom.GetValueOrDefault(id) ?? new()) { var ci = ColOf(cf.ColLabel); if (ci != -1) bas = bas.Where(i => TestCustom(Cell(rows[i], ci), cf)).ToList(); }
            bas = bas.Where(i => !manual.TryGetValue(i, out var g) || g == id).ToList();
            var keep = bas.ToHashSet();
            foreach (var (i, g) in manual) if (g == id && !keep.Contains(i) && i >= 0 && i < rows.Count) { bas.Add(i); keep.Add(i); }
            sets[id] = bas.ToHashSet();
        }
        var res = new Result { Hit = new List<string>[rows.Count], Primary = new string[rows.Count] };
        for (var i = 0; i < rows.Count; i++) { res.Hit[i] = Order.Where(id => sets[id].Contains(i)).ToList(); res.Primary[i] = res.Hit[i].FirstOrDefault() ?? ""; }
        return res;
    }
}
