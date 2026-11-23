using System.Text.RegularExpressions;
using static KhoLanh.Core.CuoiKy.Ck;

namespace KhoLanh.Core.CuoiKy;

public sealed record CkSegDiff(int K, string Label, string A, string B);
public sealed record CkEdit(string Type, string Text);

public sealed class CkCand
{
    public string Ma = "", Ten = "", Conf = "", Kind = "";
    public double Diff, Score, CodeSim, SegEq, Qty;
    public double? NameSim;
    public bool Exact;
    public CkEdit Edit = new("same", "");
    public List<CkSegDiff> DiffSegs = new();
    public List<string> Reasons = new();
}

public sealed class CkCombo
{
    public List<string> Codes = new(), Names = new(), Reasons = new();
    public List<double> Diffs = new();
    public double Sum, AvgSim;
    public string Conf = "";
}

public sealed record CkOffset(int Seg, string Label, List<string> Codes, double Sum);
public sealed record CkPoolItem(string Ma, string Ten, double Diff);

/// <summary>GỢI Ý MÃ HÀNG SAI / NHẦM (chỉ gợi ý, không tự gộp / sửa mã) — giống dc2-suggest.js.</summary>
public static class CkSuggest
{
    private static readonly string[] Confusable = { "0O", "0D", "0Q", "1I", "1L", "IL", "5S", "8B", "2Z", "6G", "UV", "MN", "EF", "CG" };
    private static bool IsConf(char a, char b) => Confusable.Contains($"{a}{b}") || Confusable.Contains($"{b}{a}");

    public static int Dam(string a, string b)
    {
        int n = a.Length, m = b.Length; if (n == 0) return m; if (m == 0) return n;
        var d = new int[n + 1, m + 1];
        for (var i = 0; i <= n; i++) d[i, 0] = i; for (var j = 0; j <= m; j++) d[0, j] = j;
        for (var i = 1; i <= n; i++) for (var j = 1; j <= m; j++)
        {
            var cost = a[i - 1] == b[j - 1] ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
        }
        return d[n, m];
    }
    public static double Sim(string a, string b) { var L = Math.Max(a.Length, b.Length); return L > 0 ? 1 - (double)Dam(a, b) / L : 1; }

    public static CkEdit EditKind(string a, string b)
    {
        if (a == b) return new("same", "giống hệt");
        if (a.Length == b.Length)
        {
            var pos = Enumerable.Range(0, a.Length).Where(i => a[i] != b[i]).ToList();
            if (pos.Count == 2 && pos[1] == pos[0] + 1 && a[pos[0]] == b[pos[1]] && a[pos[1]] == b[pos[0]]) return new("swap", $"đảo vị trí 2 ký tự {pos[0] + 1}–{pos[1] + 1} (\"{a[pos[0]]}{a[pos[1]]}\" ↔ \"{b[pos[0]]}{b[pos[1]]}\")");
            if (pos.Count > 0 && pos.All(i => IsConf(a[i], b[i]))) return new("confusable", "nhầm chữ ↔ số tại ký tự " + string.Join(", ", pos.Select(i => $"{i + 1} (\"{a[i]}\" ↔ \"{b[i]}\")")));
            if (pos.Count == 1) return new("sub1", $"sai 1 ký tự ở vị trí {pos[0] + 1} (\"{a[pos[0]]}\" ↔ \"{b[pos[0]]}\")");
            return new("subN", $"sai {pos.Count} ký tự (vị trí {string.Join(", ", pos.Select(i => i + 1))})");
        }
        var (lo, hi) = a.Length < b.Length ? (a, b) : (b, a);
        if (Math.Abs(a.Length - b.Length) == 1)
            for (var i = 0; i < hi.Length; i++) if (hi[..i] + hi[(i + 1)..] == lo)
                return a.Length < b.Length ? new("missing", $"thiếu 1 ký tự (tại vị trí {i + 1}: \"{hi[i]}\")") : new("extra", $"thừa 1 ký tự (tại vị trí {i + 1}: \"{hi[i]}\")");
        return new("len", $"khác độ dài ({a.Length} ↔ {b.Length} ký tự)");
    }

    public static string[]? SegsOf(string ma) => ma.Length == 21 ? CkConfig.Seg.Select(s => ma[s.a..s.b]).ToArray() : null;

    public static List<CkSegDiff>? SegDiff(string a, string b)
    {
        var sa = SegsOf(a); var sb = SegsOf(b); if (sa is null || sb is null) return null;
        return Enumerable.Range(0, 10).Where(k => sa[k] != sb[k]).Select(k => new CkSegDiff(k, CkConfig.SegLabel[k], sa[k], sb[k])).ToList();
    }
    public static string SegText(CkSegDiff d) => $"{d.Label} \"{d.A}\" ↔ \"{d.B}\"";

    public static string KindOfDiff(List<CkSegDiff>? diffs)
    {
        if (diffs is null || diffs.Count == 0) return "ma";
        if (diffs.All(d => d.K >= 7)) return "quy-cach";
        if (diffs.Any(d => d.K == 0)) return "san-pham";
        if (diffs.All(d => d.K >= 1 && d.K <= 6)) return "dac-tinh";
        return "ma";
    }

    private static List<string> Tokens(string name) =>
        Regex.Split(Regex.Replace(Fold(name), @"[^a-z0-9.\s]", " "), @"\s+").Where(t => t.Length > 1 || Regex.IsMatch(t, @"\d")).ToList();
    public static double? NameSim(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return null;
        var ta = Tokens(a); var tb = Tokens(b); if (ta.Count == 0 || tb.Count == 0) return null;
        var cnt = new Dictionary<string, int>(); foreach (var t in ta) cnt[t] = cnt.GetValueOrDefault(t) + 1;
        var inter = 0; foreach (var t in tb) if (cnt.GetValueOrDefault(t) > 0) { inter++; cnt[t]--; }
        return 2.0 * inter / (ta.Count + tb.Count);
    }

    public static CkCand Compare(CkPoolItem A, CkPoolItem B, CkOptions o)
    {
        var pairTol = o.PairTol; string a = A.Ma, b = B.Ma;
        var codeSim = Sim(a, b); var ek = EditKind(a, b); var dsegs = SegDiff(a, b);
        var segEq = dsegs is not null ? (10 - dsegs.Count) / 10.0 : codeSim;
        var nSim = NameSim(A.Ten, B.Ten);
        double dA = A.Diff, dB = B.Diff; var opp = dA * dB < 0;
        var gap = Math.Abs(dA + dB); var big = Math.Max(Math.Abs(dA), Math.Abs(dB));
        var exact = opp && gap <= pairTol && big >= Math.Max(1, 2 * pairTol);
        var qty = exact ? 1 : (opp && big > 0 ? Math.Max(0, 1 - gap / big) * 0.6 : 0);
        var score = 100 * (0.30 * codeSim + 0.20 * segEq + 0.15 * (nSim ?? segEq) + 0.35 * qty);
        var conf = "";
        if (exact && (codeSim >= 0.8 || segEq >= 0.7) && (nSim is null || nSim >= 0.5)) conf = "cao";
        else if (exact && (codeSim >= 0.6 || segEq >= 0.5)) conf = "trung binh";
        else if (opp && qty >= 0.54 && codeSim >= 0.9) conf = "trung binh";
        else if (opp && big > 0 && Math.Min(Math.Abs(dA), Math.Abs(dB)) / big >= 0.5 && codeSim >= 0.75 && score >= o.MinScore) conf = "thap";
        var reasons = new List<string>();
        if (ek.Type != "same") reasons.Add("Hai mã " + (a.Length == b.Length ? "cùng độ dài, " : "") + ek.Text + (dsegs is { Count: > 0 } ? $" — khác ở {dsegs.Count} đoạn: " + string.Join("; ", dsegs.Select(SegText)) : ""));
        if (exact) reasons.Add($"Chênh lệch đối ứng đúng {Vn(Math.Abs(dA))} kg (mã nghi sai {(dA > 0 ? "+" : "")}{Vn(dA)} kg, mã gợi ý {(dB > 0 ? "+" : "")}{Vn(dB)} kg)");
        else if (opp) reasons.Add($"Lệch ngược chiều nhưng không bằng nhau ({(dA > 0 ? "+" : "")}{Vn(dA)} ↔ {(dB > 0 ? "+" : "")}{Vn(dB)} kg, hụt {Vn(gap)} kg)");
        if (nSim is { } ns) reasons.Add("Tên hàng giống " + Pct(ns));
        reasons.Add("Mã giống " + Pct(codeSim) + (dsegs is not null ? $", {10 - dsegs.Count}/10 đoạn trùng" : ""));
        return new CkCand { Ma = b, Ten = B.Ten, Diff = dB, Score = Rnd(score * 10) / 10, Conf = conf, Kind = KindOfDiff(dsegs), CodeSim = codeSim, SegEq = segEq, NameSim = nSim, Qty = qty, Exact = exact, Edit = ek, DiffSegs = dsegs ?? new(), Reasons = reasons };
    }

    private static int Rank(string c) => c switch { "cao" => 3, "trung binh" => 2, "thap" => 1, _ => 0 };

    public static (List<CkCand> Cands, List<CkCombo> Combos) SuggestFor(CkPoolItem item, List<CkPoolItem> pool, CkOptions o)
    {
        var cands = new List<CkCand>();
        foreach (var p in pool)
        {
            if (p.Ma == item.Ma) continue;
            if (!(item.Diff * p.Diff < 0)) continue;
            var L = Math.Max(item.Ma.Length, p.Ma.Length);
            if (L - Math.Min(item.Ma.Length, p.Ma.Length) > 3) continue;
            if (Sim(item.Ma, p.Ma) < 0.6) continue;
            var c = Compare(item, p, o);
            if (c.Conf.Length > 0 && c.Score >= o.MinScore) cands.Add(c);
        }
        cands = cands.OrderByDescending(x => Rank(x.Conf)).ThenByDescending(x => x.Score).ToList();
        return (cands.Take(o.MaxCand).ToList(), CombosFor(item, pool, o));
    }

    public static List<CkCombo> CombosFor(CkPoolItem item, List<CkPoolItem> pool, CkOptions o)
    {
        var pairTol = o.PairTol;
        if (Math.Abs(item.Diff) < Math.Max(1, 2 * pairTol)) return new();
        var near = new List<(CkPoolItem p, double s)>();
        foreach (var p in pool)
        {
            if (p.Ma == item.Ma || !(item.Diff * p.Diff < 0)) continue;
            var s = Sim(item.Ma, p.Ma); if (s < 0.8) continue;
            if (Math.Abs(p.Diff) >= Math.Abs(item.Diff) - pairTol) continue;
            near.Add((p, s));
        }
        var top = near.OrderByDescending(x => x.s).Take(8).ToList();
        var outp = new List<CkCombo>(); var want = -item.Diff;
        void Try(params (CkPoolItem p, double s)[] grp)
        {
            var sum = grp.Sum(x => x.p.Diff);
            if (Math.Abs(sum - want) <= pairTol) outp.Add(new CkCombo { Codes = grp.Select(x => x.p.Ma).ToList(), Names = grp.Select(x => x.p.Ten).ToList(), Diffs = grp.Select(x => x.p.Diff).ToList(), Sum = R6(sum), AvgSim = grp.Sum(x => x.s) / grp.Length });
        }
        for (var i = 0; i < top.Count; i++) for (var j = i + 1; j < top.Count; j++) { Try(top[i], top[j]); for (var k = j + 1; k < top.Count; k++) Try(top[i], top[j], top[k]); }
        return outp.OrderBy(g => g.Codes.Count).ThenByDescending(g => g.AvgSim).Take(3).Select(g =>
        {
            g.Conf = g.AvgSim >= 0.92 ? "trung binh" : "thap";
            g.Reasons = new()
            {
                $"Tổng lệch của {g.Codes.Count} mã giống nhau ({string.Join(", ", g.Diffs.Select(d => (d > 0 ? "+" : "") + Vn(d)))} = {(g.Sum > 0 ? "+" : "")}{Vn(g.Sum)} kg) bằng đúng mức lệch ngược của mã này ({(item.Diff > 0 ? "+" : "")}{Vn(item.Diff)} kg)",
                "Độ giống mã trung bình " + Pct(g.AvgSim),
            };
            return g;
        }).ToList();
    }

    public static List<CkOffset> OffsettingGroups(IEnumerable<CkPoolItem> items, CkOptions o)
    {
        var tol = o.PairTol; var outp = new List<CkOffset>(); var list = items.ToList();
        for (var skip = 1; skip <= 9; skip++)
        {
            var g = new Dictionary<string, List<CkPoolItem>>(); var order = new List<string>();
            foreach (var it in list)
            {
                var s = SegsOf(it.Ma); if (s is null) continue;
                var key = string.Join("|", s.Select((x, k) => k == skip ? "*" : x));
                if (!g.TryGetValue(key, out var l)) { g[key] = l = new(); order.Add(key); }
                l.Add(it);
            }
            foreach (var key in order)
            {
                var arr = g[key]; if (arr.Count < 2) continue;
                var diffs = arr.Where(x => Math.Abs(x.Diff) > o.Tol).ToList(); if (diffs.Count < 2) continue;
                var sum = diffs.Sum(x => x.Diff); if (Math.Abs(sum) <= tol) outp.Add(new CkOffset(skip, CkConfig.SegLabel[skip], diffs.Select(x => x.Ma).ToList(), R6(sum)));
            }
        }
        return outp;
    }
}

public sealed record CkFlag(string Code, string Text);
public sealed class CkLine
{
    public CkVoucher V = null!;
    public List<CkFlag> Flags = new();
}
public sealed class CkItemCt
{
    public List<CkLine> Lines = new();
    public int NFlag;
    public (double Nhap, double Xuat, int N) Ketoan, Anan;
}

/// <summary>TRUY XUẤT CHỨNG TỪ GÂY LỆCH — mỗi cờ chỉ là dấu hiệu nghi ngờ có bằng chứng (giống dc2-trace.js).</summary>
public static class CkTrace
{
    public static readonly Dictionary<string, string> FlagLabel = new()
    {
        ["trung"] = "Trùng", ["ngoai-ky"] = "Ngoài kỳ", ["sau-chot"] = "Sau cuối kỳ", ["sau-kiem-ke"] = "Sau kiểm kê", ["khong-doi-ung"] = "Không đối ứng",
        ["lech-so"] = "Lệch số", ["net-lech"] = "Sai Net", ["bat-thuong"] = "Bất thường", ["bang-chenh"] = "= chênh lệch",
    };
    private static string DirName(string d) => d == "N" ? "Nhập" : "Xuất";

    public sealed class Result
    {
        public Dictionary<string, List<CkVoucher>> ByMa = new();
        public Dictionary<string, List<CkFlag>> Flags = new();
        public int Trung, NgoaiKy, KhongDoiUng, LechSo, BatThuong, NetLech;
        public bool HasAn, HasKt;
    }

    public static Result Analyze(List<CkVoucher> vouchers, CkPeriod? period, string kiemKeNgay, double tol)
    {
        var res = new Result();
        foreach (var v in vouchers) { if (!res.ByMa.TryGetValue(v.Ma, out var l)) res.ByMa[v.Ma] = l = new(); l.Add(v); }
        void AddFlag(CkVoucher v, string code, string text) { if (!res.Flags.TryGetValue(v.Id, out var a)) res.Flags[v.Id] = a = new(); if (!a.Any(f => f.Code == code)) a.Add(new CkFlag(code, text)); }
        res.HasAn = vouchers.Any(v => v.Src == "anan"); res.HasKt = vouchers.Any(v => v.Src == "ketoan");
        string pFrom = period?.From ?? "", pTo = period?.To ?? "";
        foreach (var (ma, list) in res.ByMa)
        {
            foreach (var g in list.GroupBy(v => string.Join("|", v.Src, v.Phieu, v.Dir, JsNum(R6(v.Kg)))))
                if (g.Count() > 1) foreach (var v in g) { AddFlag(v, "trung", $"Trùng {g.Count()} dòng: cùng chứng từ {v.Phieu}, cùng {DirName(v.Dir).ToLowerInvariant()} {Vn(v.Kg)} kg"); res.Trung++; }
            foreach (var v in list)
            {
                if (v.Ngay.Length == 0) continue;
                if (pFrom.Length > 0 && string.CompareOrdinal(v.Ngay, pFrom) < 0) { AddFlag(v, "ngoai-ky", $"Ngày {Dmy(v.Ngay)} trước đầu kỳ {Dmy(pFrom)}"); res.NgoaiKy++; }
                else if (pTo.Length > 0 && string.CompareOrdinal(v.Ngay, pTo) > 0) { AddFlag(v, "sau-chot", $"Ngày {Dmy(v.Ngay)} sau cuối kỳ {Dmy(pTo)}"); res.NgoaiKy++; }
                if (kiemKeNgay.Length > 0 && string.CompareOrdinal(v.Ngay, kiemKeNgay) > 0 && !(pTo.Length > 0 && string.CompareOrdinal(v.Ngay, pTo) > 0)) AddFlag(v, "sau-kiem-ke", $"Ngày {Dmy(v.Ngay)} sau ngày kiểm kê {Dmy(kiemKeNgay)}");
            }
            foreach (var d in new[] { "N", "X" })
            {
                var L = list.Where(v => v.Dir == d).ToList(); if (L.Count < 5) continue;
                var xs = L.Select(v => Math.Abs(v.Kg)).OrderBy(x => x).ToList(); var med = xs[xs.Count / 2];
                var mad = L.Select(v => Math.Abs(Math.Abs(v.Kg) - med)).OrderBy(x => x).ToList()[L.Count / 2];
                foreach (var v in L) { var k = Math.Abs(v.Kg); if (med > 0 && k > med * 5 && k > med + 6 * mad) { AddFlag(v, "bat-thuong", $"Số lượng {Vn(k)} kg lớn bất thường so với các chứng từ cùng mã (trung vị {Vn(med)} kg)"); res.BatThuong++; } }
            }
            foreach (var v in list) if (v.NetLech is { } nl && nl != 0) { AddFlag(v, "net-lech", $"Số kg ≠ số kiện × Net/kiện (lệch {Vn(nl)} kg)"); res.NetLech++; }
            if (res.HasAn && res.HasKt)
                foreach (var d in new[] { "N", "X" })
                {
                    var A = list.Where(v => v.Src == "anan" && v.Dir == d).ToList(); var K = list.Where(v => v.Src == "ketoan" && v.Dir == d).ToList();
                    if (A.Count == 0 && K.Count == 0) continue;
                    var leftK = K.ToList();
                    var byPhieu = new Dictionary<string, CkVoucher>(); foreach (var k in K) byPhieu[k.Phieu] = k;
                    var leftA = A.Where(a =>
                    {
                        if (!byPhieu.TryGetValue(a.Phieu, out var k)) return true;
                        if (Math.Abs(a.Kg - k.Kg) > tol) { AddFlag(a, "lech-so", $"Cùng chứng từ {a.Phieu}: kho ghi {Vn(a.Kg)} kg, kế toán ghi {Vn(k.Kg)} kg"); AddFlag(k, "lech-so", $"Cùng chứng từ {k.Phieu}: kế toán ghi {Vn(k.Kg)} kg, kho ghi {Vn(a.Kg)} kg"); res.LechSo++; }
                        leftK.Remove(k); return false;
                    }).ToList();
                    leftA = leftA.Where(a => { var i = leftK.FindIndex(k => Math.Abs(k.Kg - a.Kg) <= tol); if (i < 0) return true; leftK.RemoveAt(i); return false; }).ToList();
                    foreach (var a in leftA) { AddFlag(a, "khong-doi-ung", $"Phiếu {DirName(d).ToLowerInvariant()} {Vn(a.Kg)} kg có ở dữ liệu kho nhưng chưa tìm thấy chứng từ tương ứng (cùng mã, cùng số lượng) trong kế toán"); res.KhongDoiUng++; }
                    foreach (var k in leftK) { AddFlag(k, "khong-doi-ung", $"Chứng từ {DirName(d).ToLowerInvariant()} {Vn(k.Kg)} kg có ở kế toán nhưng chưa tìm thấy phiếu tương ứng (cùng mã, cùng số lượng) trong dữ liệu kho"); res.KhongDoiUng++; }
                }
        }
        return res;
    }

    public static CkItemCt ForItem(Result tr, string ma, double diff, double pairTol)
    {
        var list = tr.ByMa.TryGetValue(ma, out var l) ? l : new List<CkVoucher>(); var abs = Math.Abs(diff);
        var lines = list.Select(v =>
        {
            var f = (tr.Flags.TryGetValue(v.Id, out var fl) ? fl : new List<CkFlag>()).ToList();
            if (abs >= Math.Max(1, 2 * pairTol) && Math.Abs(Math.Abs(v.Kg) - abs) <= pairTol) f.Add(new CkFlag("bang-chenh", $"Số lượng {Vn(v.Kg)} kg bằng đúng mức chênh lệch {Vn(abs)} kg"));
            return new CkLine { V = v, Flags = f };
        }).OrderBy(x => x.V.Ngay, StringComparer.Ordinal).ThenBy(x => x.V.Phieu, StringComparer.Ordinal).ToList();
        double Sum(string? src, string d) => R6(list.Where(v => (src is null || v.Src == src) && v.Dir == d).Sum(v => v.Kg));
        return new CkItemCt
        {
            Lines = lines, NFlag = lines.Count(x => x.Flags.Count > 0),
            Ketoan = (Sum("ketoan", "N"), Sum("ketoan", "X"), list.Count(v => v.Src == "ketoan")), Anan = (Sum("anan", "N"), Sum("anan", "X"), list.Count(v => v.Src == "anan")),
        };
    }
}
