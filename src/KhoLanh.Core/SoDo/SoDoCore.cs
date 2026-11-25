using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.SoDo;

/// <summary>
/// Sơ đồ kho — lõi tính toán thuần dữ liệu (viết lại theo vitri-core.js của app cũ).
/// Tên vị trí = Kho.Khu.Dãy.Tầng.Khoang, VD TG3.J.10.6.1. Khu Z = hàng CHƯA NHẬP VỊ TRÍ (chỉ xem).
/// 1 tầng chứa tối đa bằng SỐ KHOANG pallet; 1 pallet = 1 nhóm (cùng loại hàng + cùng size) trong 1 vị trí.
/// </summary>
public static class SoDoCore
{
    public const int NT = 7, NT_MAX = 7;
    private static readonly Regex ReVt = new(@"^(TG\d+)\.([A-Z]+)\.(\d{1,3})\.(\d)\.(\d)$", RegexOptions.IgnoreCase);
    public static double R2(double n) => Math.Round(n * 100, MidpointRounding.AwayFromZero) / 100;
    public static string Two(int n) => n.ToString("00", CultureInfo.InvariantCulture);
    /// <summary>Mã kho trong dữ liệu là TG1…TG4, màn hình gọi «Kho 1…Kho 4».</summary>
    public static string KhoLabel(string kho) => Regex.Replace(kho ?? "", @"^TG(\d+)$", "Kho $1", RegexOptions.IgnoreCase);

    // ------------------------------------------------------------------ cấu hình kho

    public sealed class KhuCfg
    {
        public string Khu = ""; public int D, K; public int? T; public SortedDictionary<int, int> Td = new(); public bool Auto;
        public KhuCfg Clone() => new() { Khu = Khu, D = D, K = K, T = T, Td = new(Td), Auto = Auto };
    }
    public sealed class Cfg : SortedDictionary<string, List<KhuCfg>>
    {
        public Cfg() : base(StringComparer.Ordinal) { }
        public Cfg Clone() { var c = new Cfg(); foreach (var (k, v) in this) c[k] = v.Select(x => x.Clone()).ToList(); return c; }
    }

    /// <summary>Cấu hình mặc định (người dùng xác nhận 16/11/2026).</summary>
    public static Cfg DefaultCfg()
    {
        static KhuCfg M(string khu, int d, int k) => new() { Khu = khu, D = d, K = k };
        return new Cfg
        {
            ["TG1"] = new() { M("A", 9, 4), M("B", 12, 4), M("C", 12, 4), M("D", 14, 4) },
            ["TG2"] = new() { M("E", 14, 4), M("F", 12, 4), M("G", 12, 4), M("H", 14, 4) },
            ["TG3"] = new() { M("I", 14, 3), M("J", 14, 4) },
            ["TG4"] = new() { M("K", 12, 3), M("L", 14, 3) },
        };
    }
    private static int? Int(JsonNode? n)
    {
        if (n is null) return null;
        if (n is JsonValue v)
        {
            if (v.TryGetValue<int>(out var i)) return i;
            if (v.TryGetValue<double>(out var d) && double.IsFinite(d)) return (int)Math.Truncate(d);
            if (v.TryGetValue<string>(out var s) && Regex.Match(s.Trim(), @"^-?\d+") is { Success: true } m) return int.Parse(m.Value, CultureInfo.InvariantCulture);
        }
        return null;
    }
    /// <summary>Chuẩn hóa cấu hình đọc từ database (sai kiểu → bỏ dòng). Rỗng → mặc định.</summary>
    public static Cfg CleanCfg(JsonNode? c)
    {
        var o = new Cfg();
        if (c is not JsonObject obj) return DefaultCfg();
        foreach (var (kho, arrN) in obj)
        {
            var list = new List<KhuCfg>(); var seen = new HashSet<string>();
            foreach (var e in arrN as JsonArray ?? new JsonArray())
            {
                if (e is not JsonObject eo) continue;
                var khu = Regex.Replace((eo["khu"]?.ToString() ?? "").ToUpperInvariant(), "[^A-Z]", ""); if (khu.Length > 2) khu = khu[..2];
                var d = Int(eo["d"]); var k = Int(eo["k"]);
                if (khu.Length == 0 || khu == "Z" || seen.Contains(khu) || d is not (>= 1 and <= 99) || k is not (>= 1 and <= 9)) continue;
                seen.Add(khu);
                var x = new KhuCfg { Khu = khu, D = d.Value, K = k.Value };
                var t = Int(eo["t"]); if (t is >= 1 and <= NT_MAX && t != NT) x.T = t;
                if (eo["td"] is JsonObject td)
                    foreach (var (dd, vv) in td)
                        if (int.TryParse(dd, out var n) && n is >= 1 and <= 99 && Int(vv) is int v2 && v2 is >= 1 and <= NT_MAX) x.Td[n] = v2;
                list.Add(x);
            }
            if (list.Count > 0) o[kho.ToUpperInvariant()] = list;
        }
        return o.Count > 0 ? o : DefaultCfg();
    }
    public static JsonObject CfgJson(Cfg c)
    {
        var o = new JsonObject();
        foreach (var (kho, list) in c)
        {
            var a = new JsonArray();
            foreach (var e in list)
            {
                var x = new JsonObject { ["khu"] = e.Khu, ["d"] = e.D, ["k"] = e.K };
                if (e.T is int t && t != NT) x["t"] = t;
                if (e.Td.Count > 0) { var td = new JsonObject(); foreach (var (dd, v) in e.Td) td[dd.ToString(CultureInfo.InvariantCulture)] = v; x["td"] = td; }
                a.Add(x);
            }
            o[kho] = a;
        }
        return o;
    }
    /// <summary>Số tầng của 1 dãy: riêng dãy (td) → của khu (t) → 7.</summary>
    public static int TangOf(KhuCfg? e, int d) => e is null ? NT : e.Td.TryGetValue(d, out var v) && v > 0 ? v : e.T ?? NT;
    /// <summary>«3:6, 10-12:6» → {3:6, 10:6, 11:6, 12:6}.</summary>
    public static SortedDictionary<int, int> ParseTd(string? s)
    {
        var o = new SortedDictionary<int, int>();
        foreach (var part in Regex.Split(s ?? "", @"[,;\s]+"))
        {
            var m = Regex.Match(part.Trim(), @"^(\d{1,2})(?:-(\d{1,2}))?:(\d)$"); if (!m.Success) continue;
            int a = int.Parse(m.Groups[1].Value), b = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : a, v = int.Parse(m.Groups[3].Value);
            if (v is < 1 or > NT_MAX || a < 1 || b < a || b > 99) continue;
            for (var i = a; i <= b; i++) o[i] = v;
        }
        return o;
    }
    public static string TdText(IDictionary<int, int>? td)
    {
        if (td is null || td.Count == 0) return "";
        var ks = td.Keys.OrderBy(x => x).ToList(); var o = new List<string>();
        for (var i = 0; i < ks.Count;)
        {
            var j = i; while (j + 1 < ks.Count && ks[j + 1] == ks[j] + 1 && td[ks[j + 1]] == td[ks[i]]) j++;
            o.Add((j > i ? $"{ks[i]}-{ks[j]}" : $"{ks[i]}") + ":" + td[ks[i]]); i = j + 1;
        }
        return string.Join(", ", o);
    }

    // ------------------------------------------------------------------ tên vị trí

    public sealed record VtParts(string Kho, string Khu, int D, int T, int K, bool Z, string Code);
    public static VtParts? Parse(string? vt)
    {
        var m = ReVt.Match((vt ?? "").Trim()); if (!m.Success) return null;
        var kho = m.Groups[1].Value.ToUpperInvariant(); var khu = m.Groups[2].Value.ToUpperInvariant();
        int d = int.Parse(m.Groups[3].Value), t = int.Parse(m.Groups[4].Value), k = int.Parse(m.Groups[5].Value);
        if (t is < 1 or > NT_MAX || k < 1 || d < 1) return null;
        return new VtParts(kho, khu, d, t, k, khu == "Z", CodeOf(kho, khu, d, t, k));
    }
    public static string CodeOf(string kho, string khu, int d, int t, int k) => $"{kho}.{khu}.{Two(d)}.{t}.{k}";
    /// <summary>Dữ liệu vượt cấu hình → nới cấu hình theo dữ liệu để không bỏ sót vị trí.</summary>
    public static (Cfg Cfg, List<string> Notes) FitCfg(Cfg cfg, IEnumerable<VtParts> parsed)
    {
        var o = cfg.Clone(); var notes = new List<string>();
        foreach (var p in parsed)
        {
            if (p.Z) continue;
            if (!o.TryGetValue(p.Kho, out var list)) o[p.Kho] = list = new();
            var e = list.FirstOrDefault(x => x.Khu == p.Khu);
            if (e is null) { e = new KhuCfg { Khu = p.Khu, D = p.D, K = p.K, Auto = true }; list.Add(e); notes.Add($"Khu {p.Kho}.{p.Khu} chưa có trong cấu hình — tự thêm theo dữ liệu"); }
            if (p.D > e.D) { notes.Add($"{p.Kho}.{p.Khu}: số dãy {e.D} → {p.D} (theo dữ liệu)"); e.D = p.D; }
            if (p.K > e.K) { notes.Add($"{p.Kho}.{p.Khu}: số khoang {e.K} → {p.K} (theo dữ liệu)"); e.K = p.K; }
            if (p.T > TangOf(e, p.D)) { notes.Add($"{p.Kho}.{p.Khu} dãy {p.D}: số tầng {TangOf(e, p.D)} → {p.T} (theo dữ liệu)"); e.Td[p.D] = p.T; }
        }
        foreach (var k in o.Keys.ToList()) o[k] = o[k].OrderBy(x => x.Khu, StringComparer.Ordinal).ToList();
        return (o, notes);
    }

    // ------------------------------------------------------------------ quy cách

    public sealed class Qc { public string Dv = "kiện"; public int Gk; public bool Saved; public string Ts = ""; }
    public static bool HasQc(Qc? q) => q is { Saved: true } && (q.Dv != "gói" || q.Gk >= 1);
    public static double KienOf(double sl, Qc? q) => q is { Dv: "gói", Gk: >= 1 } ? sl / q.Gk : sl;
    /// <summary>Gợi ý quy cách từ tên: "(1.8 kg x 6 gói)" → (1.8, 6, "1.8kg x6").</summary>
    public static (double Kg, int Gk, string Text)? GoiY(string? ten)
    {
        var m = Regex.Match(ten ?? "", @"\(\s*([\d.,]+)\s*kg\s*x\s*(\d+)", RegexOptions.IgnoreCase); if (!m.Success) return null;
        var s = m.Groups[1].Value.Replace(',', '.');
        if (!double.TryParse(Regex.Match(s, @"^\d+(\.\d+)?").Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var kg)) return null;
        var gk = int.Parse(m.Groups[2].Value);
        return kg > 0 && gk >= 1 ? (kg, gk, s + "kg x" + gk) : null;
    }
    public static string KeyMa(string? ma)
    {
        var s = Regex.Replace((ma ?? "").Trim(), "^TPC-", "", RegexOptions.IgnoreCase);
        if (s.Length > 21 && s.Contains('-')) s = s[(s.LastIndexOf('-') + 1)..];
        return s.ToUpperInvariant();
    }

    // ------------------------------------------------------------------ dựng danh sách vị trí

    public sealed class StockRow { public string Ma = "", Ten = "", Lo = "", Vt = ""; public double Sl, Tl; public StockRow Clone() => (StockRow)MemberwiseClone(); }
    public sealed class Item { public string Ma = "", Ten = "", Lo = "", Dv = "kiện"; public double Sl, Tl, Net; }
    public sealed record Grp(string G, int N, int Pal);
    public sealed class Pos
    {
        public string Kho = "", Khu = "", Code = ""; public int D, T, K, Nk; public bool Z;
        public List<Item> It = new(); public int Pal; public List<Grp> Grps = new(); public List<(string Dv, double Sl)> Tot = new(); public double Net;
        public string TangKey => $"{Kho}.{Khu}.{Two(D)}.{T}";
    }
    public sealed class Tang { public int Used, Nk; public List<Pos> Pos = new(); }
    public sealed class Built
    {
        public Cfg Cfg = new(); public List<string> Notes = new(); public List<Pos> Pos = new(); public Dictionary<string, Pos> ByCode = new();
        public Dictionary<string, Tang> Tang = new(); public List<StockRow> Unparsed = new();
    }

    /// <summary>Dựng toàn bộ vị trí theo cấu hình (vị trí trống cũng có), gắn hàng, tính pallet / tầng.
    /// grpOf(ma21, ten) → nhóm dùng chung pallet ("" = mỗi mã 1 nhóm).</summary>
    public static Built Build(IEnumerable<StockRow> rows, Cfg cfg, Func<string, Qc?> qcOf, Func<string, string, string> grpOf)
    {
        var parsed = new List<VtParts>(); var unparsed = new List<StockRow>(); var map = new Dictionary<string, (VtParts p, List<Item> it)>();
        foreach (var r in rows)
        {
            var p = Parse(r.Vt);
            if (p is null) { unparsed.Add(r); continue; }
            parsed.Add(p);
            if (!map.TryGetValue(p.Code, out var slot)) map[p.Code] = slot = (p, new List<Item>());
            slot.it.Add(new Item { Ma = KeyMa(r.Ma), Ten = r.Ten, Lo = r.Lo, Sl = r.Sl, Tl = r.Tl, Net = R2(r.Sl * r.Tl) });
        }
        var (fc, notes) = FitCfg(cfg, parsed);
        var b = new Built { Cfg = fc, Notes = notes, Unparsed = unparsed };
        void Add(string kho, string khu, int d, int t, int k, int nk, bool z)
        {
            var code = CodeOf(kho, khu, d, t, k);
            var p = new Pos { Kho = kho, Khu = khu, D = d, T = t, K = k, Nk = nk, Code = code, Z = z, It = map.TryGetValue(code, out var s) ? s.it : new() };
            b.Pos.Add(p); b.ByCode[code] = p;
        }
        foreach (var (kho, list) in fc)
            foreach (var e in list)
                for (var d = 1; d <= e.D; d++) for (int t = 1, nt = TangOf(e, d); t <= nt; t++) for (var k = 1; k <= e.K; k++) Add(kho, e.Khu, d, t, k, e.K, false);
        foreach (var s in map.Values.Where(s => s.p.Z).OrderBy(s => s.p.Code, StringComparer.Ordinal)) Add(s.p.Kho, "Z", s.p.D, s.p.T, s.p.K, 1, true);
        foreach (var p in b.Pos)
        {
            var tot = new Dictionary<string, double>(); var order = new List<string>(); double net = 0;
            foreach (var x in p.It)
            {
                var q = qcOf(x.Ma); x.Dv = q is { Dv: "gói" } ? "gói" : "kiện";
                if (!tot.ContainsKey(x.Dv)) order.Add(x.Dv);
                tot[x.Dv] = tot.GetValueOrDefault(x.Dv) + x.Sl; net += x.Net;
            }
            p.Tot = order.Select(dv => (dv, R2(tot[dv]))).ToList(); p.Net = R2(net);
            p.Grps = PalGroups(p.It.Select(x => (x.Ma, x.Ten)), grpOf); p.Pal = p.Z ? 0 : p.Grps.Count;
            if (p.Z) continue;
            if (!b.Tang.TryGetValue(p.TangKey, out var m)) b.Tang[p.TangKey] = m = new Tang { Nk = p.Nk };
            m.Used += p.Pal; m.Pos.Add(p);
        }
        return b;
    }
    /// <summary>Pallet của 1 vị trí: mỗi nhóm (cùng loại + cùng size) có hàng = 1 pallet.</summary>
    public static List<Grp> PalGroups(IEnumerable<(string Ma, string Ten)> items, Func<string, string, string> grpOf)
    {
        var n = new Dictionary<string, int>(); var order = new List<string>();
        foreach (var (ma, ten) in items) { var g = grpOf(ma, ten); if (g.Length == 0) g = ma; if (!n.ContainsKey(g)) order.Add(g); n[g] = n.GetValueOrDefault(g) + 1; }
        return order.Select(g => new Grp(g, n[g], 1)).ToList();
    }
    public static bool IsOver(Pos p, Built b) => !p.Z && b.Tang.TryGetValue(p.TangKey, out var m) && m.Used > m.Nk;
    /// <summary>'e' trống · 'u' có hàng 1 nhóm · 'mm' nhiều mã CẦN CẢNH BÁO.
    /// 1.89: nhóm = cùng mặt hàng + cùng size (như pallet, <see cref="Pos.Grps"/>): 2 mã cùng size cùng mặt hàng → KHÔNG cảnh báo ('u');
    /// từ 2 size, hoặc cùng size khác mặt hàng → 'mm'. Mã không quy ra size → mỗi mã 1 nhóm (như trước).</summary>
    public static string Status(Pos p) => p.It.Count == 0 ? "e" : GroupCount(p) > 1 ? "mm" : "u";
    /// <summary>Số nhóm (mặt hàng + size) trong vị trí; chưa tính nhóm (Grps rỗng) → đếm mã.</summary>
    public static int GroupCount(Pos p) => p.Grps.Count > 0 ? p.Grps.Count : p.It.Select(x => x.Ma).Distinct().Count();
    /// <summary>Số mã khác nhau trong vị trí.</summary>
    public static int CodeCount(Pos p) => p.It.Select(x => x.Ma).Distinct().Count();
    public static string StatusLabel(string s) => s == "e" ? "Trống" : s == "u" ? "Có hàng" : "Nhiều mã";
    public sealed record Counts(int All, int E, int U, int Mm, int Ov, int Fill);
    public static Counts CountsOf(IEnumerable<Pos> list, Built b)
    {
        int all = 0, e = 0, u = 0, mm = 0; var ov = new HashSet<string>();
        foreach (var p in list)
        {
            if (p.Z) continue; all++;
            switch (Status(p)) { case "e": e++; break; case "u": u++; break; default: mm++; break; }
            if (IsOver(p, b)) ov.Add(p.TangKey);
        }
        return new Counts(all, e, u, mm, ov.Count, all > 0 ? (int)Math.Round((u + mm) * 100.0 / all, MidpointRounding.AwayFromZero) : 0);
    }
    public sealed record Sug(string Code, string Tang, int Free, bool Same, int Dist);
    /// <summary>Gợi ý chuyển khi tầng vượt mức: tối đa 3 tầng khác cùng khu còn chỗ, ưu tiên tầng có cùng mã, rồi gần nhất.</summary>
    public static List<Sug> Suggest(Pos p, Built b)
    {
        if (!b.Tang.TryGetValue(p.TangKey, out var me)) return new();
        var mine = me.Pos.SelectMany(z => z.It.Select(x => x.Ma)).ToHashSet();
        var o = new List<(Sug s, int score)>();
        foreach (var (k, m) in b.Tang)
        {
            var z0 = m.Pos[0];
            if (k == p.TangKey || z0.Kho != p.Kho || z0.Khu != p.Khu) continue;
            var free = m.Nk - m.Used; if (free < 1) continue;
            bool Has(Pos z) => z.It.Any(x => mine.Contains(x.Ma));
            var same = m.Pos.Any(Has);
            var best = m.Pos.OrderBy(a => (Has(a) ? -9 : 0) + a.Pal).First();
            var dist = Math.Abs(z0.D - p.D) + Math.Abs(z0.T - p.T);
            o.Add((new Sug(best.Code, k, free, same, dist), dist - (same ? 6 : 0)));
        }
        return o.OrderBy(x => x.score).ThenByDescending(x => x.s.Free).Take(3).Select(x => x.s).ToList();
    }
    /// <summary>Mức đầy của tầng chứa p: ok (&lt;75%) · near (75–99%) · full (100%) · over (&gt;100%) · "" (khu Z).</summary>
    public static (int Pct, string Lv) FillLevel(Pos p, Built b)
    {
        if (p.Z || !b.Tang.TryGetValue(p.TangKey, out var m) || m.Nk == 0) return (0, "");
        var pct = (int)Math.Round(m.Used * 100.0 / m.Nk, MidpointRounding.AwayFromZero);
        if (m.Used > m.Nk) return (pct, "over");
        if (m.Used == m.Nk) return (pct, "full");
        return (pct, pct >= 75 ? "near" : "ok");
    }
    public static bool ItemHit(Item x, string q, Func<string, string, string>? label)
    {
        if (string.IsNullOrEmpty(q)) return false;
        var s = string.Join(" ", x.Ma, x.Ten, x.Lo, label?.Invoke(x.Ma, x.Ten) ?? "").ToLowerInvariant(); var k = q.ToLowerInvariant();
        return s.Contains(k) || s.Replace(" ", "").Contains(Regex.Replace(k, @"\s+", ""));
    }
    public static string TextOf(Pos p, Func<string, string, string>? label)
    {
        var s = (p.Code + " " + string.Join(" ", p.It.Select(x => string.Join(" ", x.Ma, x.Ten, x.Lo, label?.Invoke(x.Ma, x.Ten) ?? "")))).ToLowerInvariant();
        return s + " " + Regex.Replace(s, @"\s+", "");
    }
    public sealed class Filter { public string St = "", Q = ""; public int D, T, K; }
    /// <summary>Lọc vị trí: St ("", e, u, mm, ov, near, full), D / T / K (0 = tất cả), Q (chữ tìm).</summary>
    public static bool Pass(Pos p, Filter f, Built b, Func<Pos, string> text)
    {
        if (f.St is "near" or "full")
        {
            var lv = FillLevel(p, b).Lv;
            if (f.St == "full" ? lv is not ("full" or "over") : lv is not ("near" or "full" or "over")) return false;
        }
        else if (f.St == "ov") { if (!IsOver(p, b)) return false; }
        else if (f.St.Length > 0 && f.St != "all" && Status(p) != f.St) return false;
        if (f.D > 0 && p.D != f.D) return false; if (f.T > 0 && p.T != f.T) return false; if (f.K > 0 && p.K != f.K) return false;
        if (f.Q.Length > 0)
        {
            var s = text(p); var q = f.Q.ToLowerInvariant();
            if (!s.Contains(q) && !s.Contains(Regex.Replace(q, @"\s+", ""))) return false;
        }
        return true;
    }
    public static HashSet<string> MissingQc(IEnumerable<Pos> pos, Func<string, Qc?> qcOf)
    {
        var o = new HashSet<string>();
        foreach (var p in pos) { if (p.Z) continue; foreach (var x in p.It) if (!HasQc(qcOf(x.Ma))) o.Add(x.Ma); }
        return o;
    }

    // ------------------------------------------------------------------ phiếu kiểm kê / rà soát

    public sealed class KkRow { public string Code = "", Ma = "", Ten = "", Lo = ""; public int T, K; public bool Known, First; public double Sl0, Tl; public double? Sl; }
    /// <summary>Các dòng của 1 DÃY (sắp theo tầng rồi khoang). Vị trí trống = 1 dòng trống.</summary>
    public static List<KkRow> KkRows(IEnumerable<Pos> pos, string kho, string khu, int d)
    {
        var o = new List<KkRow>();
        foreach (var p in pos.Where(p => !p.Z && p.Kho == kho && p.Khu == khu && p.D == d).OrderBy(p => p.T).ThenBy(p => p.K))
        {
            if (p.It.Count == 0) o.Add(new KkRow { Code = p.Code, T = p.T, K = p.K, First = true });
            else for (var j = 0; j < p.It.Count; j++) { var x = p.It[j]; o.Add(new KkRow { Code = p.Code, T = p.T, K = p.K, Known = true, Ma = x.Ma, Ten = x.Ten, Lo = x.Lo, Sl0 = x.Sl, Tl = x.Tl, First = j == 0 }); }
        }
        return o;
    }
    /// <summary>Vị trí có ≥ 2 nhóm → nhóm ngoài nhóm nhiều hàng nhất được đề xuất sang 1 vị trí TRỐNG cùng tầng (khoang gần nhất). Trả nhóm → mã vị trí đích.</summary>
    public static Dictionary<string, string> MoveSug(Pos? p, List<Pos> pos, HashSet<string> taken, Func<string, string, string> grpOf)
    {
        var o = new Dictionary<string, string>();
        if (p is null || p.Z || p.It.Count < 2) return o;
        var gr = new Dictionary<string, double>(); var order = new List<string>();
        foreach (var x in p.It) { var g = grpOf(x.Ma, x.Ten); if (g.Length == 0) g = x.Ma; if (!gr.ContainsKey(g)) order.Add(g); gr[g] = gr.GetValueOrDefault(g) + x.Sl; }
        if (gr.Count < 2) return o;
        string? keep = null; foreach (var g in order) if (keep is null || gr[g] > gr[keep]) keep = g;
        foreach (var g in order)
        {
            if (g == keep) continue;
            var c = pos.Where(q => !q.Z && q.Kho == p.Kho && q.Khu == p.Khu && q.D == p.D && q.T == p.T && q.It.Count == 0 && !taken.Contains(q.Code))
                .OrderBy(a => Math.Abs(a.K - p.K)).FirstOrDefault();
            if (c is not null) { o[g] = c.Code; taken.Add(c.Code); }
        }
        return o;
    }
    public sealed class Review { public int N, Total, Gone, Extra, NoMa, Neg; public double Dsl, Dnet; public List<string> OverT = new(); public List<(string Code, string Ma, double Sl0, double Sl, double D)> Top = new(); }
    /// <summary>Rà soát chênh lệch: rows = KkRows + Sl thực (null = giữ số sổ). nk(tangKey) → số khoang.</summary>
    public static Review ReviewOf(List<KkRow> rows, Func<string, int> nk, Func<string, string, string> grpOf)
    {
        var o = new Review { Total = rows.Count }; var big = new List<(KkRow r, double d, double eff)>(); var byCode = new Dictionary<string, List<(string, string)>>();
        foreach (var r in rows)
        {
            var eff = r.Sl ?? (r.Known ? r.Sl0 : 0); var d = eff - r.Sl0;
            if (eff < 0) o.Neg++;
            if (Math.Abs(d) > 1e-9) { o.N++; o.Dsl += d; big.Add((r, d, eff)); }
            if (r.Known) o.Dnet += (eff - r.Sl0) * r.Tl;
            if (r.Known && eff == 0) o.Gone++;
            if (!r.Known && eff > 0) { o.Extra++; if (r.Ma.Trim().Length == 0) o.NoMa++; }
            if (eff > 0 && r.Ma.Length > 0) { if (!byCode.TryGetValue(r.Code, out var l)) byCode[r.Code] = l = new(); l.Add((KeyMa(r.Ma), r.Ten)); }
        }
        var tm = new Dictionary<string, int>();
        foreach (var (code, items) in byCode) { var k = string.Join(".", code.Split('.').Take(4)); tm[k] = tm.GetValueOrDefault(k) + PalGroups(items, grpOf).Count; }
        foreach (var (k, v) in tm) if (v > nk(k)) o.OverT.Add(k);
        o.Dsl = R2(o.Dsl); o.Dnet = R2(o.Dnet);
        o.Top = big.OrderByDescending(x => Math.Abs(x.d)).Take(5).Select(x => (x.r.Code, x.r.Ma, x.r.Sl0, x.eff, x.d)).ToList();
        return o;
    }

    // ------------------------------------------------------------------ khóa kiểm kê

    public sealed class Lock { public string Id = "", Kho = "", Khu = "", Ts = "", By = "", Note = ""; public int D; }
    public sealed record Diff(string Vt, string Ma, string Ten, string Lo, double Was, double Now, double D, string Kind, string LockId);
    private static string SumKey(StockRow r) { var p = Parse(r.Vt); return (p?.Code ?? r.Vt.Trim().ToUpperInvariant()) + "|" + KeyMa(r.Ma) + "|" + r.Lo.Trim(); }
    /// <summary>Phần đã khóa dùng số liệu lúc khóa; phần còn lại theo tồn hiện tại. Kèm thay đổi của phần khóa.</summary>
    public static (List<StockRow> Rows, List<Diff> Diff) ApplyLocks(List<StockRow> live, List<Lock> locks, Dictionary<string, List<StockRow>> snaps)
    {
        if (locks.Count == 0) return (live.ToList(), new());
        var byL = new Dictionary<string, List<StockRow>>(); var keep = new List<StockRow>();
        foreach (var r in live)
        {
            var p = Parse(r.Vt);
            var l = p is null ? null : locks.FirstOrDefault(x => x.D > 0 && x.Kho == p.Kho && x.Khu == p.Khu && x.D == p.D) ?? locks.FirstOrDefault(x => x.D == 0 && x.Kho == p.Kho && x.Khu == p.Khu);
            if (l is null) keep.Add(r); else { if (!byL.TryGetValue(l.Id, out var a)) byL[l.Id] = a = new(); a.Add(r); }
        }
        var rows = keep.ToList(); var diff = new List<Diff>();
        foreach (var l in locks)
        {
            var snap = snaps.GetValueOrDefault(l.Id) ?? new();
            rows.AddRange(snap);
            diff.AddRange(DiffRows(snap, byL.GetValueOrDefault(l.Id) ?? new(), l.Id));
        }
        return (rows, diff.OrderBy(x => x.Vt, StringComparer.Ordinal).ThenBy(x => x.Ma, StringComparer.Ordinal).ToList());
    }
    public static List<Diff> DiffRows(List<StockRow> snap, List<StockRow> now, string lockId)
    {
        var A = new Dictionary<string, double>(); var B = new Dictionary<string, double>(); var meta = new Dictionary<string, (string vt, string ma, string ten, string lo)>(); var order = new List<string>();
        void Add(Dictionary<string, double> m, StockRow r)
        {
            var k = SumKey(r); m[k] = m.GetValueOrDefault(k) + r.Sl;
            if (!meta.ContainsKey(k)) { var p = Parse(r.Vt); meta[k] = (p?.Code ?? r.Vt, KeyMa(r.Ma), r.Ten, r.Lo); order.Add(k); }
        }
        foreach (var r in snap) Add(A, r); foreach (var r in now) Add(B, r);
        var o = new List<Diff>();
        foreach (var k in order)
        {
            double was = A.GetValueOrDefault(k), cur = B.GetValueOrDefault(k), d = Math.Round((cur - was) * 1e6) / 1e6;
            if (Math.Abs(d) < 1e-9) continue;
            var m = meta[k];
            o.Add(new Diff(m.vt, m.ma, m.ten, m.lo, was, cur, d, cur == 0 ? "Hết hàng" : was == 0 ? "Hàng mới" : d < 0 ? "Xuất" : "Nhập", lockId));
        }
        return o;
    }
    public static List<StockRow> RowsOf(IEnumerable<StockRow> live, string kho, string khu, int d) =>
        live.Where(r => Parse(r.Vt) is { } p && p.Kho == kho && p.Khu == khu && (d == 0 || p.D == d)).ToList();
}
