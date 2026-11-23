using System.Globalization;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.PhieuKho;

/// <summary>Gợi ý quy cách mới (port suggestQc): từ nhãn "1.8Kg x 3" tìm Mã gói (ký tự 18–21) trong Mã hóa sản phẩm (menu 9 = trọng lượng, 10 = quy cách đóng thùng) và tiền tố mã hàng từ Danh sách mã hàng / quy cách cùng khối lượng.</summary>
public static class CargoSuggest
{
    public sealed class Dict { public Dictionary<string, string> W = new(), Q = new(); }
    public sealed class Suggestion
    {
        public string Pack = "", PackSrc = ""; public List<string> Alts = new(), Notes = new();
        public Dictionary<string, string> Pre = new(), PreSrc = new();
    }
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static double? KgOfName(string? name)
    {
        var m = Regex.Match(name ?? "", @"(\d+(?:[.,]\d+)?)\s*(kg|g)\b", RegexOptions.IgnoreCase); if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, Inv, out var v)) return null;
        if (m.Groups[2].Value.Equals("g", StringComparison.OrdinalIgnoreCase)) v /= 1000; return Math.Round(v, 6);
    }
    private static List<double> FactorsOfName(string? name)
    {
        var f = new List<double>();
        foreach (Match m in Regex.Matches(name ?? "", @"(\d+(?:[.,]\d+)?)\s*[^\d/,;+x×*]*?/\s*[^\d\s,;]+", RegexOptions.IgnoreCase))
            if (double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, Inv, out var n)) f.Add(n);
        return f;
    }
    public static (double Kg, int N)? ParseLabel(string? label)
    {
        var m = Regex.Match(label ?? "", @"^\s*([\d.,]+)\s*(kg|g)\s*[x×*]\s*(\d+)\s*$", RegexOptions.IgnoreCase); if (!m.Success) return null;
        if (!double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, Inv, out var a)) return null;
        if (m.Groups[2].Value.Equals("g", StringComparison.OrdinalIgnoreCase)) a /= 1000; return (Math.Round(a, 6), int.Parse(m.Groups[3].Value));
    }
    private static string Norm(string? s) => Regex.Replace((s ?? "").ToUpperInvariant(), @"\s+", "");

    public static Suggestion Suggest(string label, Dict dict, CargoCfg cfg, IEnumerable<string> maList, string digit)
    {
        var o = new Suggestion(); var q = ParseLabel(label);
        if (q is null) { o.Notes.Add($"Nhãn \"{(label ?? "").Trim()}\" chưa đúng dạng 1.8Kg x 3 / 100G x 60 nên chưa gợi ý được."); return o; }
        var (kg, n) = q.Value;
        var wc = dict.W.Keys.Where(c => KgOfName(dict.W[c]) == kg).ToList();
        var qc = dict.Q.Keys.Where(c => { var f = FactorsOfName(dict.Q[c]); return f.Count > 0 && f[0] == n; }).ToList();
        var qc1 = qc.Where(c => FactorsOfName(dict.Q[c]).Count == 1).ToList(); var qpick = qc1.Count > 0 ? qc1 : qc;
        if (dict.W.Count == 0 && dict.Q.Count == 0) o.Notes.Add("Chưa đọc được Tổng hợp › Mã hóa sản phẩm (menu 9 / 10) nên chưa tìm được mã gói.");
        else if (wc.Count == 0) o.Notes.Add($"Mã hóa sản phẩm (menu 9) chưa có trọng lượng {kg.ToString(Inv)} kg/túi.");
        else if (qpick.Count == 0) o.Notes.Add($"Mã hóa sản phẩm (menu 10) chưa có quy cách đóng thùng \"{n} túi/ctn\".");
        else
        {
            var pairs = wc.SelectMany(a => qpick.Select(b => a + b)).ToList();
            o.Pack = pairs[0]; o.Alts = pairs.Skip(1).ToList(); o.PackSrc = $"Mã hóa sản phẩm: {wc[0]} = {dict.W[wc[0]]} · {qpick[0]} = {dict.Q[qpick[0]]}";
            if (pairs.Count > 1) o.Notes.Add($"Có {pairs.Count} mã gói cùng khớp ({string.Join(", ", pairs)}) — đã chọn {pairs[0]}, kiểm tra lại.");
        }
        var fam = cfg.Loai.ToDictionary(l => l.Code, l => cfg.Qc.Select(x => x.Pre.TryGetValue(l.Code, out var p) && p.Length > 0 ? (p.Length > 4 ? p[..4] : p).ToUpperInvariant() : "").Where(x => x.Length > 0).ToHashSet());
        if (o.Pack.Length > 0)
        {
            var cnt = new Dictionary<string, int>();
            foreach (var x in maList) { var m = Norm(x); if (m.Length >= 21 && m.Substring(17, 4) == o.Pack) { var p = m[..13]; cnt[p] = cnt.GetValueOrDefault(p) + 1; } }
            foreach (var (L, set) in fam)
            {
                var cand = cnt.Keys.Where(p => set.Contains(p[..4])).OrderByDescending(p => cnt[p]).ToList();
                if (cand.Count > 0) { o.Pre[L] = cand[0]; o.PreSrc[L] = $"Danh sách mã hàng ({cnt[cand[0]]} mã dùng mã gói {o.Pack})"; }
            }
        }
        var sib = cfg.Qc.Select(x => (x, p: ParseLabel(x.Label))).Where(t => t.p is { } p && p.Kg == kg && t.x.D != digit).OrderBy(t => Math.Abs(t.p!.Value.N - n)).FirstOrDefault();
        if (sib.x is not null)
            foreach (var L in fam.Keys) if (!o.Pre.ContainsKey(L) && sib.x.Pre.TryGetValue(L, out var pv) && pv.Length > 0) { o.Pre[L] = pv; o.PreSrc[L] = $"chép theo quy cách {sib.x.Label} (cùng {kg.ToString(Inv)} kg/túi)"; }
        if (o.Pre.Count == 0) o.Notes.Add("Chưa gợi ý được tiền tố mã hàng (chưa có mã nào dùng mã gói này và không có quy cách cùng khối lượng để chép) — gõ tay.");
        return o;
    }
}
