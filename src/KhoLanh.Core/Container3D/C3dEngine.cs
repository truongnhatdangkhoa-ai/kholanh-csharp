using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.Container3D;

/// <summary>
/// LÕI THUẬT TOÁN XẾP HÀNG VÀO CONTAINER (chuyển nguyên từ c3d-engine.js của app cũ — cùng đầu vào cho cùng kết quả).
/// Xếp theo "vách" từ trong ra ngoài như công nhân đóng hàng thật:
///  1. Mỗi dòng hàng (mã · size · ngày) là 1 nhóm, sắp mã &gt; size &gt; ngày; nhóm đầu vào SÂU NHẤT (sát vách đầu).
///  2. Mỗi nhóm xếp thành các vách liên tiếp; thử mọi hướng đặt, cột thẳng / trộn cột / so le rồi chọn phương án xếp được nhiều nhất, ít tốn chiều dài nhất.
///  3. Verify() KIỂM LẠI độc lập: trong cont, không chồng, dưới red line, đủ đỡ (thùng đỡ đóng trước), không bị thùng đóng trước chắn đường từ cửa.
///  4. Tải trọng: nhóm quá tải thì giảm số thùng; báo số thùng rớt lại kho + lý do.
/// </summary>
public static class C3dEngine
{
    public const double EPS = 1e-6;
    public const int MinSample = 10;
    public static readonly string[] Perms = { "LWH", "WLH", "LHW", "HLW", "WHL", "HWL" };

    public static readonly C3dContainer[] Presets =
    {
        new() { Id = "rf20", Name = "Cont lạnh 20' RF", Type = "20RF", L = 5450, W = 2290, H = 2270, DoorW = 2290, DoorH = 2260, MaxPayload = 27400, RedLine = 2170, ClearFront = 50, ClearRear = 50, ClearSide = 10, Note = "Thông số tham khảo hãng tàu — kiểm tra lại cont thực tế." },
        new() { Id = "rf40", Name = "Cont lạnh 40' RF (thấp)", Type = "40RF", L = 11560, W = 2290, H = 2250, DoorW = 2290, DoorH = 2180, MaxPayload = 29000, RedLine = 2150, ClearFront = 50, ClearRear = 50, ClearSide = 10, Note = "Thông số tham khảo hãng tàu — kiểm tra lại cont thực tế." },
        new() { Id = "rf40hc", Name = "Cont lạnh 40' HC RF (cao)", Type = "40HC", L = 11560, W = 2290, H = 2550, DoorW = 2290, DoorH = 2500, MaxPayload = 29500, RedLine = 2450, ClearFront = 50, ClearRear = 50, ClearSide = 10, Note = "Thông số tham khảo hãng tàu — kiểm tra lại cont thực tế." },
    };

    // ------------------------------------------------------------ tiện ích
    private static double Rnd(double x) => Math.Floor(x + 0.5);            // Math.round của JS
    internal static string R1(double n) => Js(Rnd(n * 10) / 10);
    internal static string Js(double n) => n.ToString("0.##########", CultureInfo.InvariantCulture);

    private static List<string> NatKey(string? s) =>
        Regex.Matches((s ?? "").ToLowerInvariant(), @"\d+|\D+").Select(m => m.Value).ToList();

    public static int NatCmp(string? a, string? b)
    {
        var A = NatKey(a); var B = NatKey(b);
        for (var i = 0; i < Math.Max(A.Count, B.Count); i++)
        {
            if (i >= A.Count) return -1; if (i >= B.Count) return 1;
            string x = A[i], y = B[i];
            bool nx = x.All(char.IsAsciiDigit), ny = y.All(char.IsAsciiDigit);
            if (nx && ny) { var d = decimal.Parse(x.Length > 27 ? x[..27] : x, CultureInfo.InvariantCulture) - decimal.Parse(y.Length > 27 ? y[..27] : y, CultureInfo.InvariantCulture); if (d != 0) return d < 0 ? -1 : 1; }
            else if (x != y) return string.CompareOrdinal(x, y) < 0 ? -1 : 1;
        }
        return 0;
    }

    /// <summary>Ngày dd/mm/yyyy · dd-mm-yyyy · dd.mm.yyyy · yyyy-mm-dd · dd/mm (không năm → năm nay). Không đọc được → NaN.</summary>
    public static double DateVal(string? s)
    {
        var t = (s ?? "").Trim(); if (t.Length == 0) return double.NaN;
        static double U(int y, int m, int d) { try { return (new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(m - 1).AddDays(d - 1) - DateTime.UnixEpoch).TotalMilliseconds; } catch { return double.NaN; } }
        var m = Regex.Match(t, @"^(\d{4})[-/.](\d{1,2})[-/.](\d{1,2})");
        if (m.Success) return U(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        m = Regex.Match(t, @"^(\d{1,2})[-/.](\d{1,2})[-/.](\d{2,4})");
        if (m.Success) { var y = int.Parse(m.Groups[3].Value); if (y < 100) y += 2000; return U(y, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value)); }
        m = Regex.Match(t, @"^(\d{1,2})[-/.](\d{1,2})$");
        if (m.Success) return U(DateTime.Now.Year, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value));
        return double.NaN;
    }

    // ------------------------------------------------------------ chuẩn hoá đầu vào
    public static C3dSpace MakeSpace(C3dContainer c)
    {
        var red = c.RedLine > 0 ? c.RedLine : c.H;
        var effH = Math.Min(c.H, Math.Min(red, c.DoorH > 0 ? c.DoorH : c.H));
        double x0 = c.ClearFront, x1 = c.L - c.ClearRear, y0 = c.ClearSide, y1 = c.W - c.ClearSide;
        return new C3dSpace(x0, x1, y0, y1, x1 - x0, y1 - y0, effH, red, c.L, c.W, c.H);
    }

    public static C3dSku NormSku(C3dSku s, int i)
    {
        var o = s.Clone();
        if (o.Id.Length == 0) o.Id = "s" + i;
        o.Code = o.Code.Trim(); o.Name = o.Name.Trim(); o.Size = o.Size.Trim(); o.Date = o.Date.Trim(); o.RowPlan = o.RowPlan.Trim();
        o.Qty = Math.Max(0, o.Qty); o.Kg = Math.Max(0, o.Kg); o.MaxLayers = Math.Max(0, o.MaxLayers);
        if (!Perms.Contains(o.Orient)) o.Orient = "";
        o.Idx = i;
        if (!o.AllowStand && !o.AllowLay) o.AllowStand = true; // không chọn gì → mặc định dựng đứng
        return o;
    }

    // ------------------------------------------------------------ hướng đặt hợp lệ
    private static double Dim(C3dSku k, char c) => c == 'L' ? k.L : c == 'W' ? k.W : k.H;

    public static List<C3dOrient> OrientationsOf(C3dSku k, C3dSpace sp)
    {
        if (k.Orient.Length > 0)
        {
            var p = k.Orient;
            var o = new C3dOrient(Dim(k, p[0]), Dim(k, p[1]), Dim(k, p[2]), p[2] == 'H' ? "dung" : "nam", p is "WLH" or "HLW" or "HWL" ? 1 : 0);
            return o.Dx <= sp.Lu + EPS && o.Dy <= sp.Wu + EPS && o.Dz <= sp.EffH + EPS ? new List<C3dOrient> { o } : new List<C3dOrient>();
        }
        double l = k.L, w = k.W, h = k.H; var all = new List<C3dOrient>();
        void Add(double dx, double dy, double dz, string kind, int rot)
        {
            if (!all.Any(o => Math.Abs(o.Dx - dx) < EPS && Math.Abs(o.Dy - dy) < EPS && Math.Abs(o.Dz - dz) < EPS)) all.Add(new C3dOrient(dx, dy, dz, kind, rot));
        }
        if (k.AllowStand) { Add(l, w, h, "dung", 0); if (k.AllowRotate) Add(w, l, h, "dung", 1); }
        if (k.AllowLay)
        {
            Add(l, h, w, "nam", 0); if (k.AllowRotate) Add(h, l, w, "nam", 1);
            Add(w, h, l, "nam", 0); if (k.AllowRotate) Add(h, w, l, "nam", 1);
        }
        return all.Where(o => o.Dx <= sp.Lu + EPS && o.Dy <= sp.Wu + EPS && o.Dz <= sp.EffH + EPS).ToList();
    }

    /// <summary>Thùng có đưa lọt cửa cont không (xoay tay khi khiêng).</summary>
    public static bool PassDoor(C3dSku k, C3dContainer c)
    {
        var s = new[] { k.L, k.W, k.H }; Array.Sort(s);
        var d0 = Math.Min(c.DoorW, c.DoorH); var d1 = Math.Max(c.DoorW, c.DoorH);
        return s[0] <= d0 + EPS && s[1] <= d1 + EPS;
    }

    // ------------------------------------------------------------ các kiểu xếp 1 "vách"
    private static C3dBox Mk(double x, double y, double z, C3dOrient o, int tier) =>
        new() { X = x, Y = y, Z = z, Dx = o.Dx, Dy = o.Dy, Dz = o.Dz, Tier = tier, Ori = o.Kind, Rot = o.Rot };

    internal sealed class Pat
    {
        public string Kind = "col", Label = "";
        public C3dOrient O = null!;
        public double D, Shift, Gap;
        public int Cap, Nc, T;
        public List<(C3dOrient o, int T)> Cols = new();

        public List<C3dBox> Build(int count, string style, double ox, double oy)
        {
            var outp = new List<C3dBox>(); var k = 0;
            switch (Kind)
            {
                case "col":
                    if (style == "tall") { for (var c = 0; c < Nc && k < count; c++) for (var t = 0; t < T && k < count; t++, k++) outp.Add(Mk(ox, oy + c * O.Dy, t * O.Dz, O, t + 1)); }
                    else { for (var t = 0; t < T && k < count; t++) for (var c = 0; c < Nc && k < count; c++, k++) outp.Add(Mk(ox, oy + c * O.Dy, t * O.Dz, O, t + 1)); }
                    break;
                case "sole":
                    if (style == "tall") { for (var c = 0; c < Nc && k < count; c++) for (var t = 0; t < T && k < count; t++, k++) { var b = Mk(ox, oy + (t % 2 == 0 ? Shift : 0) + c * O.Dy, t * O.Dz, O, t + 1); b.Sole = true; outp.Add(b); } }
                    else { for (var t = 0; t < T && k < count; t++) for (var c = 0; c < Nc && k < count; c++, k++) { var b = Mk(ox, oy + (t % 2 == 0 ? Shift : 0) + c * O.Dy, t * O.Dz, O, t + 1); b.Sole = true; outp.Add(b); } }
                    break;
                default: // mix / cols
                {
                    var y = oy; var pos = new double[Cols.Count];
                    for (var i = 0; i < Cols.Count; i++) { pos[i] = y; y += Cols[i].o.Dy; }
                    if (style == "tall") { for (var i = 0; i < Cols.Count; i++) for (var t = 0; t < Cols[i].T && k < count; t++, k++) outp.Add(Mk(ox, pos[i], t * Cols[i].o.Dz, Cols[i].o, t + 1)); }
                    else
                    {
                        var tt = Cols.Max(c => c.T);
                        for (var t = 0; t < tt && k < count; t++) for (var i = 0; i < Cols.Count; i++) if (t < Cols[i].T && k < count) { outp.Add(Mk(ox, pos[i], t * Cols[i].o.Dz, Cols[i].o, t + 1)); k++; }
                    }
                    break;
                }
            }
            return outp;
        }
    }

    private static Pat? ColPattern(C3dOrient o, int T, double Wu)
    {
        var nc = (int)Math.Floor((Wu + EPS) / o.Dy); if (nc < 1 || T < 1) return null;
        return new Pat { Kind = "col", O = o, D = o.Dx, Cap = nc * T, Nc = nc, T = T };
    }

    private static Pat? MixPattern(List<C3dOrient> os, Func<C3dOrient, int> tof, double Wu)
    {
        var types = os.Select(o => (o, T: tof(o))).Where(t => t.T >= 1 && t.o.Dy <= Wu + EPS).ToList();
        if (types.Count < 2) return null;
        var W = (int)Math.Floor(Wu + EPS); var dp = new int[W + 1]; var ch = new int[W + 1]; Array.Fill(ch, -1);
        for (var w = 1; w <= W; w++)
        {
            dp[w] = dp[w - 1]; ch[w] = -1;
            for (var i = 0; i < types.Count; i++) { var d = (int)Math.Ceiling(types[i].o.Dy - EPS); if (w >= d && dp[w - d] + types[i].T > dp[w]) { dp[w] = dp[w - d] + types[i].T; ch[w] = i; } }
        }
        var cols = new List<(C3dOrient o, int T)>(); var ww = W;
        while (ww > 0) { if (ch[ww] == -1) { ww--; continue; } var t = types[ch[ww]]; cols.Add(t); ww -= (int)Math.Ceiling(t.o.Dy - EPS); }
        if (cols.Select(c => c.o).Distinct(ReferenceEqualityComparer.Instance).Count() < 2) return null;
        cols = cols.OrderBy(c => c.o.Dz).ThenBy(c => c.o.Dy).ToList(); // cột thấp-nhiều-tầng gom 1 phía cho gọn
        return new Pat { Kind = "mix", O = cols[0].o, D = cols[0].o.Dx, Cap = cols.Sum(c => c.T), Cols = cols, T = cols.Max(c => c.T) };
    }

    // SOLE (so le trái / phải): tầng lẻ dồn 1 vách, tầng chẵn dồn vách kia → mỗi tầng có đường khí sát vách; lệch d = min(khe dư, nửa thùng).
    private static Pat? SolePattern(C3dOrient o, int T, double Wu, bool allowDrop)
    {
        if (T < 2) return null;
        var n = (int)Math.Floor((Wu + EPS) / o.Dy); if (n < 1) return null;
        var g = Wu - n * o.Dy;
        if (g < 0.1 * o.Dy) { if (!allowDrop) return null; n -= 1; g += o.Dy; }
        if (n < 1) return null;
        var d = Math.Min(g, o.Dy / 2); if (d < 1) return null;
        return new Pat { Kind = "sole", O = o, D = o.Dx, Cap = n * T, Nc = n, T = T, Shift = d, Gap = g };
    }

    private sealed class SoleInfo { public string SoleFail = ""; }

    private static List<Pat> BuildPatterns(C3dSku sku, List<C3dOrient> ors, C3dSpace sp, double Wu, SoleInfo? info)
    {
        int Tof(C3dOrient o) => Math.Min((int)Math.Floor((sp.EffH + EPS) / o.Dz), sku.MaxLayers > 0 ? sku.MaxLayers : int.MaxValue);
        var pats = new List<Pat>();
        if (sku.Sole || sku.Auto)
        {
            foreach (var o in ors) { var p = SolePattern(o, Tof(o), Wu, sku.Sole || sku.Drop); if (p is not null) pats.Add(p); }
            if (pats.Count == 0 && info is not null && sku.Sole) info.SoleFail = ors.All(o => Tof(o) < 2) ? "mot-tang" : "khong-khe";
        }
        if (pats.Count == 0)
        {
            foreach (var o in ors) { var p = ColPattern(o, Tof(o), Wu); if (p is not null) pats.Add(p); }
            var byDx = new Dictionary<double, List<C3dOrient>>(); var order = new List<double>();
            foreach (var o in ors) { var key = Rnd(o.Dx * 2); if (!byDx.TryGetValue(key, out var l)) { byDx[key] = l = new(); order.Add(key); } l.Add(o); }
            foreach (var key in order.OrderBy(k => k))
            {
                var g = byDx[key]; if (g.Count < 2) continue; var p = MixPattern(g, Tof, Wu);
                if (p is not null)
                {
                    var bestCol = pats.Where(q => q.Kind == "col" && Math.Abs(q.D - p.D) < EPS).Select(q => q.Cap).DefaultIfEmpty(0).Max();
                    if (p.Cap > Math.Max(0, bestCol)) pats.Add(p);
                }
            }
        }
        return pats;
    }

    // ---- DÃY MẪU do người dùng chọn: "6 nằm 2 đứng" = trong MỖI LỚP của 1 dãy xếp cạnh nhau 6 thùng NẰM + 2 thùng ĐỨNG.
    public static string StripVn(string? t)
    {
        var s = (t ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in s) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Replace('đ', 'd');
    }

    public sealed record ColSpec(string Tok, int N, int T);

    public static List<ColSpec> ParseColPlan(string? str)
    {
        var outp = new List<ColSpec>(); var t = StripVn(str);
        foreach (Match m in Regex.Matches(t, @"(\d+)\s*(dung\s*cao|dung|nam|cao|[lwh]{3})(?:\s*(?:x|\*)\s*(\d+))?"))
        {
            var tok = Regex.Replace(m.Groups[2].Value, @"\s+", ""); tok = tok == "dungcao" ? "cao" : tok;
            tok = tok is "dung" or "nam" or "cao" ? tok : tok.ToUpperInvariant();
            if (tok is "dung" or "nam" or "cao" || Perms.Contains(tok))
                outp.Add(new ColSpec(tok, (int)Math.Min(60, double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)), m.Groups[3].Success ? int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) : 0));
        }
        return outp;
    }

    public static string TokName(string tok) => tok switch { "nam" => "nằm", "dung" => "đứng", "cao" => "dựng cao", _ => tok };
    private static int TokRank(string tok) => tok switch { "nam" => 0, "dung" => 1, _ => 2 };
    private static double[] SortedDims(C3dSku k) { var v = new[] { k.L, k.W, k.H }; Array.Sort(v); return v; }

    private static C3dOrient? TokOrient(string tok, C3dSku sku, C3dSpace sp, double Wu)
    {
        var v = SortedDims(sku); C3dOrient? best = null;
        foreach (var pm in Perms)
        {
            var o = new C3dOrient(Dim(sku, pm[0]), Dim(sku, pm[1]), Dim(sku, pm[2]), pm[2] == 'H' ? "dung" : "nam", pm is "WLH" or "HLW" or "HWL" ? 1 : 0, pm);
            var ok = Perms.Contains(tok) ? tok == pm : Math.Abs(o.Dz - v[TokRank(tok)]) < 1e-6;
            if (!ok || o.Dx > sp.Lu + EPS || o.Dy > Wu + EPS || o.Dz > sp.EffH + EPS) continue;
            if (best is null || o.Dx > best.Dx + EPS || (Math.Abs(o.Dx - best.Dx) <= EPS && o.Dy < best.Dy - EPS)) best = o;
        }
        return best;
    }

    private static Pat? ColsPattern(List<(C3dOrient o, int T)> cols, string label)
    {
        if (cols.Count == 0) return null;
        return new Pat { Kind = "cols", Label = label, O = cols[0].o, D = cols.Max(c => c.o.Dx), Cap = cols.Sum(c => c.T), Cols = cols, T = cols.Max(c => c.T) };
    }

    public sealed record TplCol(string Tok, int N, int T0, C3dOrient? O, int T, int Used, bool Ok);
    internal sealed record TplInfo(List<TplCol> Cols, double Width, bool Over, int Cap, double Top, double Wu);

    private static (Pat? pat, TplInfo info)? BuildTpl(string str, C3dSku sku, C3dSpace sp, Func<C3dOrient, int> tmax, double Wu)
    {
        var specs = ParseColPlan(str); if (specs.Count == 0) return null;
        var cols = new List<(C3dOrient o, int T)>(); var det = new List<TplCol>(); double width = 0, top = 0; var over = false;
        foreach (var s0 in specs)
        {
            var o = TokOrient(s0.Tok, sku, sp, Wu); var T = o is not null ? Math.Min(tmax(o), s0.T > 0 ? s0.T : int.MaxValue) : 0; var used = 0;
            if (o is not null && T >= 1)
                for (var i = 0; i < s0.N; i++) { if (width + o.Dy > Wu + EPS) { over = true; break; } cols.Add((o, T)); width += o.Dy; used++; top = Math.Max(top, T * o.Dz); }
            det.Add(new TplCol(s0.Tok, s0.N, s0.T, o, T, used, o is not null && T >= 1));
        }
        var label = string.Join(" + ", specs.Select(q => q.N + " " + TokName(q.Tok) + (q.T > 0 ? " x" + q.T : "")));
        return (ColsPattern(cols, label), new TplInfo(det, width, over, cols.Sum(c => c.T), top, Wu));
    }

    internal sealed class WallPatFn
    {
        public Func<int, Pat?> Fn = _ => null;
        public double K;
    }

    private static WallPatFn? MakeWallPat(C3dSku sku, C3dSpace sp, Func<C3dOrient, int> tof, double Wu)
    {
        var set = sku.RowSet; var setKeys = set.Where(kv => !string.IsNullOrEmpty(kv.Value)).Select(kv => kv.Key).ToList();
        var hasTpl = ParseColPlan(sku.RowPlan).Count > 0;
        if (!hasTpl && setKeys.Count == 0) return null;
        var cache = new Dictionary<string, Pat?>();
        Pat? BestFull(string tok)
        {
            var dims = SortedDims(sku);
            var perms = Perms.Contains(tok) ? new[] { tok } : Perms.Where(pm => { var o = TokOrient(pm, sku, sp, Wu); return o is not null && Math.Abs(o.Dz - dims[TokRank(tok)]) < 1e-6; }).ToArray();
            Pat? best = null;
            foreach (var pm in perms)
            {
                var o = TokOrient(pm, sku, sp, Wu); if (o is null) continue;
                var T = tof(o); var p = (sku.Sole && T >= 2 ? SolePattern(o, T, Wu, true) : null) ?? ColPattern(o, T, Wu);
                if (p is not null && (best is null || (double)p.Cap / p.D > (double)best.Cap / best.D + 1e-9)) best = p;
            }
            return best;
        }
        Pat? Resolve(string v)
        {
            if (cache.TryGetValue(v, out var c)) return c; Pat? p;
            if (Regex.IsMatch(v, @"\d")) { var r = BuildTpl(v, sku, sp, tof, Wu); p = r?.pat; }
            else { var t = Regex.Replace(StripVn(v), @"\s+", ""); var tok = t == "dungcao" ? "cao" : t is "dung" or "nam" or "cao" ? t : v.ToUpperInvariant(); p = BestFull(tok); }
            return cache[v] = p;
        }
        var fn = new WallPatFn
        {
            Fn = wi => set.TryGetValue(wi.ToString(CultureInfo.InvariantCulture), out var s) && !string.IsNullOrEmpty(s) ? Resolve(s) : hasTpl ? Resolve(sku.RowPlan) : null,
            K = hasTpl ? 1e9 : Math.Max(0, setKeys.Select(k => double.TryParse(k, NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x + 1 : double.NaN).Where(double.IsFinite).DefaultIfEmpty(0).Max()),
        };
        return fn;
    }

    public sealed record TplPreviewCol(double Dy, double Dz, double Dx, int T, string Name);
    public sealed record TplPreview(List<TplPreviewCol> Cols, List<TplCol> Detail, double Width, double Wu, int Cap, double Top, double EffH, double Red, double H, bool Over, double Depth, double W, double ClearSide);

    /// <summary>Xem trước dãy mẫu (mặt cắt nhìn từ cửa) cho bảng sắp dãy.</summary>
    public static TplPreview PreviewTpl(C3dSku skuRaw, C3dContainer contRaw, string str)
    {
        var c = contRaw.Normalized(); var sp = MakeSpace(c); var sku = NormSku(skuRaw, 0);
        int Tmax(C3dOrient o) => Math.Min((int)Math.Floor((sp.EffH + EPS) / o.Dz), sku.MaxLayers > 0 ? sku.MaxLayers : int.MaxValue);
        var r = BuildTpl(str, sku, sp, Tmax, sp.Wu);
        if (r is null) return new TplPreview(new(), new(), 0, sp.Wu, 0, 0, sp.EffH, sp.Red, c.H, false, 0, c.W, c.ClearSide);
        var (pat, info) = r.Value;
        return new TplPreview(pat is not null ? pat.Cols.Select(q => new TplPreviewCol(q.o.Dy, q.o.Dz, q.o.Dx, q.T, VName(q.o.Dz, sku))).ToList() : new(),
            info.Cols, info.Width, sp.Wu, info.Cap, info.Top, sp.EffH, sp.Red, c.H, info.Over, pat?.D ?? 0, c.W, c.ClearSide);
    }

    internal sealed class Tail
    {
        public double X0, Used; public int Count, Cap; public string Style = "", Kind = ""; public List<C3dBox>? Boxes; public Pat Pat = null!;
    }

    internal sealed class WallCtx
    {
        public WallPatFn? WallPat; public int Wi; public bool Mark; public List<Pat>? Used; public double? MaxWalls;
    }

    private sealed record PlaceRes(List<C3dBox> Boxes, int Placed, double End, Tail? Tail, int Left);

    // xếp liên tiếp các vách của 1 kiểu; vách cuối có thể thiếu thùng
    private static PlaceRes PlaceWalls(Pat p0, int n, double x0, double lEnd, string style, double oy, bool dry, WallCtx? ctx)
    {
        double cur = x0; int left = n, placed = 0; Tail? tail = null; var boxes = new List<C3dBox>();
        while (left > 0)
        {
            if (ctx?.MaxWalls is { } mw && ctx.Wi >= mw) break;
            var p = p0; if (ctx?.WallPat is not null) { var q = ctx.WallPat.Fn(ctx.Wi); if (q is not null) p = q; }
            if (cur + p.D > lEnd + EPS) break;
            var take = Math.Min(left, p.Cap); var part = take < p.Cap; var st = part ? style : "wide";
            var bx = dry && !part ? null : p.Build(take, st, cur, oy);
            var used = p.D;
            if (part) { var mx = cur; foreach (var b in bx!) mx = Math.Max(mx, b.X + b.Dx); used = mx - cur; }
            if (cur + used > lEnd + EPS) break;
            placed += take; left -= take;
            if (!dry && bx is not null) foreach (var b in bx) { if (ctx is { Mark: true }) b.Wall = ctx.Wi; boxes.Add(b); }
            if (ctx is not null) { ctx.Wi++; if (ctx.Used is not null && !ctx.Used.Contains(p)) ctx.Used.Add(p); }
            if (part) tail = new Tail { X0 = cur, Used = used, Count = take, Cap = p.Cap, Style = st, Boxes = dry ? null : bx, Kind = p.Kind, Pat = p };
            cur += used;
        }
        return new PlaceRes(boxes, placed, cur, tail, left);
    }

    private static int PrefOf(C3dOrient o, string kind) => (o.Kind == "dung" ? 0 : 1) * 2 + (kind == "mix" ? 1 : 0);
    private static int PrefOf(Pat p) => PrefOf(p.O, p.Kind);
    // Tên hướng theo CHIỀU CAO THÙNG khi đặt: cao nhỏ nhất = NẰM, cao vừa = ĐỨNG, cao lớn nhất = DỰNG CAO
    public static int VRank(double dz, C3dSku k) { var v = SortedDims(k); return Math.Abs(dz - v[0]) < 1e-6 ? 0 : Math.Abs(dz - v[1]) < 1e-6 ? 1 : 2; }
    public static string VName(double dz, C3dSku k) => new[] { "nằm", "đứng", "dựng cao" }[VRank(dz, k)];
    private static string OrientText(C3dOrient o, C3dSku k) => VName(o.Dz, k) + " (cao " + R1(o.Dz) + ")" + (o.Rot != 0 ? " · xoay 90°" : "");

    internal sealed class Strip { public double X0, Depth, Y0, W, AW; }
    internal sealed class Top { public double X0, Depth, Y0, W, Z; public int Tiers; }
    internal sealed class SState { public C3dSpace Sp = null!; public double Cursor; public Strip? Strip; public Top? Top; }

    private sealed class GroupRun
    {
        public List<C3dBox> Boxes = new(); public int Placed, StripUsed, StackedOn; public string Reason = "", SoleFail = "";
        public List<C3dPatNote> Patterns = new();
    }

    // ------------------------------------------------------------ xếp 1 nhóm vào cont
    private static GroupRun RunGroup(SState S, C3dGroup g, int nWant, C3dOptions opts, bool isLast)
    {
        var sp = S.Sp; var sku = g.Sku; var outp = new GroupRun(); var info = new SoleInfo();
        var ors = OrientationsOf(sku, sp);
        if (ors.Count == 0) { outp.Reason = "khong-vua"; return outp; }
        var left = nWant;
        // QUY TẮC CAO → THẤP: vách THIẾU thùng ở cuối mỗi mặt hàng xếp CỘT ĐẦY tới red line (hẹp hơn, không thấp hơn)
        var tallRule = opts.HeightRule && !isLast;
        var style = (opts.MergeTail && !isLast) || tallRule ? "tall" : "wide";
        string Describe(Pat p) => p.Kind == "cols" ? "dãy mẫu: " + p.Label + " · sâu " + R1(p.D) + " mm"
            : OrientText(p.O, sku) + " · " + R1(p.O.Dx) + "×" + R1(p.O.Dy) + "×" + R1(p.O.Dz)
              + (p.Kind == "sole" ? " · sole (so le trái/phải từng tầng, khe " + R1(p.Gap) + " mm)" : p.Kind == "mix" ? " · trộn hướng cùng chiều sâu" : "");
        void Note(Pat p) { var tx = Describe(p) + (sku.Auto && p.Kind == "sole" ? " [tự động]" : ""); if (!outp.Patterns.Any(q => q.Text == tx)) outp.Patterns.Add(new C3dPatNote(tx, p.Kind, p.O)); }
        Tail? lastTail = null;
        // 1) ƯU TIÊN CHỒNG lên khoảng trống phía trên vách cuối của mặt hàng trước
        if (opts.StackTop && S.Top is not null && !(sku.Sole || sku.Auto) && left > 0)
        {
            var t = S.Top; var sp2 = sp with { Lu = t.Depth, Wu = t.W, EffH = sp.EffH - t.Z };
            var o2 = OrientationsOf(sku, sp2); var pats2 = o2.Count > 0 ? BuildPatterns(sku, o2, sp2, t.W, null).Where(p => p.Kind != "sole").ToList() : new();
            (Pat p, int rows, int placed)? bst = null;
            foreach (var p in pats2)
            {
                var rows = (int)Math.Floor((t.Depth + EPS) / p.D); if (rows < 1) continue;
                var r = PlaceWalls(p, Math.Min(left, rows * p.Cap), t.X0, t.X0 + rows * p.D, "wide", t.Y0, true, null);
                if (r.Placed > 0 && (bst is null || r.Placed > bst.Value.placed || (r.Placed == bst.Value.placed && PrefOf(p) < PrefOf(bst.Value.p)))) bst = (p, rows, r.Placed);
            }
            if (bst is { } b0)
            {
                var res = PlaceWalls(b0.p, Math.Min(left, b0.rows * b0.p.Cap), t.X0, t.X0 + b0.rows * b0.p.D, "wide", t.Y0, false, null);
                foreach (var b in res.Boxes) { b.Z += t.Z; b.Tier += t.Tiers; b.OnPrev = true; }
                outp.Boxes.AddRange(res.Boxes); left -= res.Placed; outp.StackedOn = res.Placed; Note(b0.p);
            }
        }
        // 1b) KHÔNG ĐỦ chỗ chồng mới xếp SANG HAI BÊN trong cùng dãy: lấp phần trống cạnh vách cuối của nhóm trước
        if (opts.MergeTail && S.Strip is not null && !(sku.Sole || sku.Auto) && left > 0)
        {
            var st = S.Strip;
            var pats = BuildPatterns(sku, ors, sp, st.W, null).ToList();
            pats = pats.Select((p, i) => (p, i)).OrderByDescending(x => (double)x.p.Cap / x.p.D).ThenBy(x => PrefOf(x.p)).ThenBy(x => x.i).Select(x => x.p).ToList();
            var ps = pats.FirstOrDefault();
            if (ps is not null)
            {
                var D = ps.D; var fl = (int)Math.Floor((st.Depth + EPS) / D); var ce = (int)Math.Ceiling((st.Depth - EPS) / D); if (ce == 0) ce = 1;
                var vFl = fl >= 1 ? (st.Depth - fl * D) * st.W : double.PositiveInfinity; var vCe = (ce * D - st.Depth) * st.AW;
                var rows = fl >= 1 && vFl <= vCe ? fl : ce;
                while (rows > 0 && st.X0 + rows * D > sp.X1 + EPS) rows--;
                if (rows >= 1)
                {
                    var want = Math.Min(left, rows * ps.Cap); var res = PlaceWalls(ps, want, st.X0, st.X0 + rows * D, "wide", st.Y0, false, null);
                    if (res.Placed > 0) { outp.Boxes.AddRange(res.Boxes); left -= res.Placed; S.Cursor = Math.Max(S.Cursor, res.End); Note(ps); outp.StripUsed = res.Placed; }
                }
            }
        }
        // 2) các vách chính
        if (left > 0)
        {
            var pats = BuildPatterns(sku, ors, sp, sp.Wu, info);
            int TofW(C3dOrient o) => Math.Min((int)Math.Floor((sp.EffH + EPS) / o.Dz), sku.MaxLayers > 0 ? sku.MaxLayers : int.MaxValue);
            var wallPat = MakeWallPat(sku, sp, TofW, sp.Wu);
            var ctx = new WallCtx { WallPat = wallPat, Wi = 0, Mark = true, Used = new(), MaxWalls = null };
            if (pats.Count == 0 && wallPat is not null) { var q = wallPat.Fn(0); if (q is not null) pats.Add(q); }
            if (pats.Count == 0) { if (outp.Boxes.Count == 0) outp.Reason = "khong-vua"; }
            else
            {
                var airy = opts.Airflow;
                (Pat p, int placed, double end)? Pick(WallCtx? cx)
                {
                    (Pat p, int placed, double end)? best = null;
                    foreach (var p in pats)
                    {
                        var r = PlaceWalls(p, left, S.Cursor, sp.X1, style, sp.Y0, true, cx is not null ? new WallCtx { WallPat = cx.WallPat, Wi = 0, MaxWalls = cx.MaxWalls } : null);
                        if (best is not { } b
                            || r.Placed > b.placed
                            || (r.Placed == b.placed && (airy
                                ? PrefOf(p) < PrefOf(b.p) || (PrefOf(p) == PrefOf(b.p) && r.End < b.end - EPS)
                                : r.End < b.end - EPS || (Math.Abs(r.End - b.end) <= EPS && PrefOf(p) < PrefOf(b.p)))))
                            best = (p, r.Placed, r.End);
                    }
                    return best;
                }
                PlaceRes Run(Pat p, int count)
                {
                    var res = PlaceWalls(p, Math.Min(left, count), S.Cursor, sp.X1, style, sp.Y0, false, ctx);
                    outp.Boxes.AddRange(res.Boxes); left -= res.Placed; S.Cursor = res.End; lastTail = res.Tail; return res;
                }
                // A) các dãy đầu do NGƯỜI DÙNG chọn hướng
                if (wallPat is not null && wallPat.K > 0)
                {
                    ctx.MaxWalls = wallPat.K; var bA = Pick(new WallCtx { WallPat = wallPat, MaxWalls = wallPat.K });
                    if (bA is { placed: > 0 } a) Run(a.p, left);
                    ctx.MaxWalls = null; ctx.WallPat = null;
                }
                // B) phần còn lại TỰ ĐỘNG; hàng nhiều: thử trộn dãy kiểu A rồi kiểu B
                if (left > 0)
                {
                    var best = Pick(null); var steps = best is { placed: > 0 } ? new List<(Pat p, int count)> { (best.Value.p, left) } : new();
                    var seq = wallPat is null && sku.TierSeq is { Length: > 0 } ? sku.TierSeq : null;
                    if (seq is not null && best is { placed: > 0 })
                    {
                        var bp = best.Value.p; var cache = new Dictionary<int, Pat>(); var Wu0 = sp.Wu;
                        Pat LimitT(int cap)
                        {
                            cap = Math.Max(1, cap); if (cap >= bp.T) return bp; if (cache.TryGetValue(cap, out var c)) return c; Pat? q;
                            if (bp.Kind == "col") q = ColPattern(bp.O, cap, Wu0);
                            else if (bp.Kind == "sole") q = SolePattern(bp.O, cap, Wu0, true) ?? ColPattern(bp.O, cap, Wu0);
                            else if (bp.Cols.Count > 0) q = ColsPattern(bp.Cols.Select(c2 => (c2.o, Math.Min(c2.T, cap))).ToList(), bp.Label);
                            else q = bp;
                            return cache[cap] = q ?? bp;
                        }
                        ctx.WallPat = new WallPatFn { Fn = wi => LimitT(seq[Math.Min(wi, seq.Length - 1)]) };
                    }
                    if (best is { } bb && seq is null && bb.placed < left && pats.Count > 1)
                    {
                        var R0 = sp.X1 - S.Cursor; var cand = pats.Where(q => q.Kind is "col" or "sole").ToList(); (Pat pi, Pat pj, int a, int b, int total)? bm = null;
                        for (var i = 0; i < cand.Count; i++) for (var j = 0; j < cand.Count; j++)
                        {
                            if (i == j) continue; var A = cand[i]; var B = cand[j];
                            for (var a = 1; a * A.D <= R0 + EPS; a++)
                            {
                                var b = (int)Math.Floor((R0 - a * A.D + EPS) / B.D); var tot = a * A.Cap + b * B.Cap;
                                if (b >= 1 && tot > bb.placed && (bm is null || tot > bm.Value.total)) bm = (A, B, a, b, tot);
                            }
                        }
                        if (bm is { } m) steps = new() { (m.pi, m.a * m.pi.Cap), (m.pj, left) };
                    }
                    foreach (var s0 in steps) if (left > 0) Run(s0.p, s0.count);
                }
                // C) phần còn dư ở cuối cont: thử kiểu khác có chiều sâu nhỏ hơn
                var guard = 0;
                while (left > 0 && guard++ < 4)
                {
                    var R = sp.X1 - S.Cursor; (Pat p, int placed, double end)? b2 = null;
                    foreach (var p in pats)
                    {
                        if (p.D > R + EPS) continue;
                        var r = PlaceWalls(p, left, S.Cursor, sp.X1, tallRule ? style : "wide", sp.Y0, true, null);
                        if (r.Placed > 0 && (b2 is null || r.Placed > b2.Value.placed || (r.Placed == b2.Value.placed && r.End < b2.Value.end))) b2 = (p, r.Placed, r.End);
                    }
                    if (b2 is null) break;
                    var r2 = PlaceWalls(b2.Value.p, left, S.Cursor, sp.X1, tallRule ? style : "wide", sp.Y0, false, ctx);
                    if (r2.Placed == 0) break;
                    outp.Boxes.AddRange(r2.Boxes); left -= r2.Placed; S.Cursor = r2.End; lastTail = r2.Tail; Note(b2.Value.p);
                }
                foreach (var p in ctx.Used!) Note(p);
            }
        }
        outp.Placed = nWant - left;
        // 3) phần trống bên cạnh vách cuối để nhóm sau lấp (chỉ khi ghép + vách cuối đang xếp "cột đầy")
        S.Strip = null;
        if (opts.MergeTail && !isLast && left == 0 && lastTail is { Style: "tall", Boxes: not null, Kind: "col" } && Math.Abs(S.Cursor - (lastTail.X0 + lastTail.Used)) < EPS)
        {
            var yEnd = sp.Y0; foreach (var b in lastTail.Boxes) yEnd = Math.Max(yEnd, b.Y + b.Dy);
            var w = sp.Y1 - yEnd; if (w > 1) S.Strip = new Strip { X0 = lastTail.X0, Depth = lastTail.Used, Y0 = yEnd, W = w, AW = yEnd - sp.Y0 };
        }
        // 4) mặt phẳng trống phía trên vách cuối: cột còn thiếu tầng
        S.Top = null;
        if (opts.StackTop && !isLast && left == 0 && lastTail is { Kind: "col", Boxes: not null } && Math.Abs(S.Cursor - (lastTail.X0 + lastTail.Used)) < EPS)
        {
            var pt = lastTail.Pat; var tops = new double[pt.Nc];
            foreach (var b in lastTail.Boxes) { var c = (int)Rnd((b.Y - sp.Y0) / pt.O.Dy); if (c >= 0 && c < pt.Nc) tops[c] = Math.Max(tops[c], b.Z + b.Dz); }
            if (opts.HeightRule || lastTail.Style == "tall") for (var c = 0; c < tops.Length; c++) if (tops[c] <= EPS) tops[c] = 1e18;
            var mn = tops.Length > 0 ? tops.Min() : double.PositiveInfinity; (int i0, int i1)? bestRun = null; var k0 = 0;
            while (k0 < tops.Length)
            {
                if (Math.Abs(tops[k0] - mn) > EPS) { k0++; continue; }
                var k1 = k0; while (k1 + 1 < tops.Length && Math.Abs(tops[k1 + 1] - mn) <= EPS) k1++;
                if (bestRun is null || k1 - k0 > bestRun.Value.i1 - bestRun.Value.i0) bestRun = (k0, k1); k0 = k1 + 1;
            }
            if (bestRun is { } br && sp.EffH - mn > 1)
                S.Top = new Top { X0 = lastTail.X0, Depth = lastTail.Used, Y0 = sp.Y0 + br.i0 * pt.O.Dy, W = (br.i1 - br.i0 + 1) * pt.O.Dy, Z = mn, Tiers = (int)Rnd(mn / pt.O.Dz) };
        }
        outp.SoleFail = info.SoleFail;
        return outp;
    }

    // ------------------------------------------------------------ HÀNG MẪU HẢI QUAN — các dãy cuối (sát cửa) trộn đủ mọi mặt hàng
    private sealed class SampInfo { public C3dGroup G = null!; public C3dSku K = null!; public C3dOrient O = null!; public int T, Want; }
    private sealed record SampRow(double D, List<List<int>> Cols);
    private sealed record SampRes(List<SampRow> Rows, int[] Placed, double Reserve, double Kg);
    private sealed record SampPlan(List<SampInfo> Infos, List<SampRow> Rows, int[] Placed, double Reserve, double Kg);

    private static SampRes BuildSampleRows(List<SampInfo> infos, C3dSpace sp, int R, double weightLeft)
    {
        var rem = infos.Select(f => f.Want).ToArray(); var placed = new int[infos.Count]; var rows = new List<SampRow>(); var wl = weightLeft;
        for (var r = 0; r < R; r++)
        {
            var wLeft = sp.Wu; var cols = infos.Select(_ => new List<int>()).ToList();
            for (var guard = 0; guard < 5000; guard++)
            {
                var pick = -1;
                for (var i = 0; i < infos.Count; i++) { if (rem[i] <= 0 || infos[i].O.Dy > wLeft + EPS) continue; if (pick < 0 || placed[i] < placed[pick]) pick = i; }
                if (pick < 0) break;
                var f = infos[pick]; var n = Math.Min(f.T, rem[pick]);
                if (double.IsFinite(wl) && f.K.Kg > 0) n = (int)Math.Min(n, Math.Floor((wl + 1e-6) / f.K.Kg));
                if (n <= 0) { rem[pick] = 0; continue; }
                cols[pick].Add(n); wLeft -= f.O.Dy; rem[pick] -= n; if (double.IsFinite(wl)) wl -= n * f.K.Kg;
                placed[pick] += n;
            }
            var used = infos.Select((f, i) => cols[i].Count > 0 ? f.O.Dx : 0).ToList(); if (!used.Any(d => d > 0)) break;
            rows.Add(new SampRow(used.Max(), cols));
        }
        return new SampRes(rows, placed, rows.Sum(r => r.D), double.IsFinite(weightLeft) ? weightLeft - wl : 0);
    }

    private static SampPlan? PlanSampleRows(List<C3dGroup> gs, C3dSpace sp, int minQ, double weightLeft)
    {
        var infos = new List<SampInfo>();
        foreach (var g in gs)
        {
            var k = g.Sku; var ors = OrientationsOf(k, sp); (C3dOrient o, int T)? best = null; double bs = -1;
            foreach (var o in ors)
            {
                var T = (int)Math.Floor((sp.EffH + EPS) / o.Dz); if (k.MaxLayers > 0) T = Math.Min(T, k.MaxLayers);
                if (T < 1 || o.Dy > sp.Wu + EPS) continue;
                var sc = T / (o.Dx * o.Dy);
                if (best is null || sc > bs + 1e-12 || (Math.Abs(sc - bs) <= 1e-12 && PrefOf(o, "col") < PrefOf(best.Value.o, "col"))) { bs = sc; best = (o, T); }
            }
            if (best is { } b) infos.Add(new SampInfo { G = g, K = k, O = b.o, T = b.T, Want = Math.Min(k.Qty, Math.Max(Math.Min(minQ, k.Qty), k.Qty / 2)) });
        }
        if (infos.Count < 2) return null;
        SampRes? res = null;
        for (var R = 2; R <= 4; R++)
        {
            res = BuildSampleRows(infos, sp, R, weightLeft);
            var rr = res; if (infos.Select((f, i) => rr.Placed[i] >= Math.Min(minQ, f.K.Qty)).All(x => x)) break;
        }
        return res is { Rows.Count: > 0 } ? new SampPlan(infos, res.Rows, res.Placed, res.Reserve, res.Kg) : null;
    }

    // ------------------------------------------------------------ KIỂM TRA ĐỘC LẬP phương án
    public static C3dVerify Verify(List<C3dBox> boxes, C3dSpace sp, double minSupport = 0.7)
    {
        var minSup = minSupport > 0 ? minSupport : 0.7; var v = new List<C3dViolation>();
        void Push(string type, int i, string msg) { if (v.Count < 200) v.Add(new C3dViolation(type, i, msg)); }
        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i];
            if (b.X < sp.X0 - 1e-3 || b.X + b.Dx > sp.X1 + 1e-3 || b.Y < sp.Y0 - 1e-3 || b.Y + b.Dy > sp.Y1 + 1e-3) Push("ngoai-cont", i, "Thùng #" + b.Seq + " nằm ngoài vùng chứa hàng của cont.");
            if (b.Z < -1e-3 || b.Z + b.Dz > sp.EffH + 1e-3) Push("vuot-redline", i, "Thùng #" + b.Seq + " vượt chiều cao cho phép (red line).");
        }
        // chồng nhau
        var ix = Enumerable.Range(0, boxes.Count).OrderBy(i => boxes[i].X).ToArray();
        for (var a = 0; a < ix.Length; a++)
        {
            var A = boxes[ix[a]];
            for (var bI = a + 1; bI < ix.Length; bI++)
            {
                var B = boxes[ix[bI]]; if (B.X >= A.X + A.Dx - 1e-3) break;
                if (B.Y < A.Y + A.Dy - 1e-3 && A.Y < B.Y + B.Dy - 1e-3 && B.Z < A.Z + A.Dz - 1e-3 && A.Z < B.Z + B.Dz - 1e-3) Push("chong-nhau", ix[a], "Thùng #" + A.Seq + " chồng lên thùng #" + B.Seq + ".");
            }
        }
        // được đỡ bên dưới + thùng đỡ phải đóng trước
        var byTop = new Dictionary<long, List<int>>();
        for (var i = 0; i < boxes.Count; i++) { var k = (long)Rnd((boxes[i].Z + boxes[i].Dz) * 100); if (!byTop.TryGetValue(k, out var l)) byTop[k] = l = new(); l.Add(i); }
        for (var i = 0; i < boxes.Count; i++)
        {
            var b = boxes[i]; if (b.Z < 1e-3) continue;
            var cand = byTop.TryGetValue((long)Rnd(b.Z * 100), out var cl) ? cl : new List<int>(); double area = 0; var sup = new List<int>();
            foreach (var j in cand)
            {
                if (j == i) continue; var c = boxes[j];
                var ox = Math.Min(b.X + b.Dx, c.X + c.Dx) - Math.Max(b.X, c.X); var oy = Math.Min(b.Y + b.Dy, c.Y + c.Dy) - Math.Max(b.Y, c.Y);
                if (ox > 1e-3 && oy > 1e-3) { area += ox * oy; sup.Add(j); }
            }
            var ratio = area / (b.Dx * b.Dy); var cx = b.X + b.Dx / 2; var cy = b.Y + b.Dy / 2;
            var cover = sup.Any(j => { var c = boxes[j]; return cx >= c.X - 1e-3 && cx <= c.X + c.Dx + 1e-3 && cy >= c.Y - 1e-3 && cy <= c.Y + c.Dy + 1e-3; });
            if (ratio < (b.Sole ? 0.5 : minSup) - 1e-6 || !cover) Push("thieu-do", i, "Thùng #" + b.Seq + " chỉ được đỡ " + Rnd(ratio * 100) + "% bên dưới.");
            if (sup.Any(j => boxes[j].Seq > b.Seq)) Push("thu-tu-do", i, "Thùng #" + b.Seq + " được đóng trước thùng đỡ nó.");
        }
        // thùng đóng trước chắn đường trượt từ cửa vào
        var ord = Enumerable.Range(0, boxes.Count).OrderBy(i => boxes[i].Seq).ToArray();
        for (var a = 0; a < ord.Length; a++)
        {
            var J = boxes[ord[a]];
            for (var bI = 0; bI < a; bI++)
            {
                var I = boxes[ord[bI]];
                if (I.X >= J.X + J.Dx - 1e-3 && I.Y < J.Y + J.Dy - 1e-3 && J.Y < I.Y + I.Dy - 1e-3 && I.Z < J.Z + J.Dz - 1e-3 && J.Z < I.Z + I.Dz - 1e-3)
                { Push("chan-duong", ord[a], "Thùng #" + J.Seq + " không đưa vào được: thùng #" + I.Seq + " (đóng trước) chắn phía cửa."); break; }
            }
        }
        return new C3dVerify(v.Count == 0, v);
    }

    // ------------------------------------------------------------ GIẢI
    private static readonly Dictionary<string, string> SoleWhy = new() { ["mot-tang"] = "chỉ xếp được 1 tầng", ["khong-khe"] = "bề ngang không còn khe để so le" };

    private static C3dPlan SolveOnce(C3dContainer container, List<C3dSku> inSkus, C3dOptions opts)
    {
        var c = container.Normalized(); var sp = MakeSpace(c);
        var warnings = new List<string>(); var errors = new List<string>();
        if (!(c.L > 0 && c.W > 0 && c.H > 0)) errors.Add("Container chưa có kích thước bên trong (dài / rộng / cao).");
        if (sp.Lu <= 0 || sp.Wu <= 0 || sp.EffH <= 0) errors.Add("Khe hở đầu / cửa / hai bên lớn hơn kích thước cont — không còn chỗ chứa hàng.");
        var skus = inSkus.Select(NormSku).Where(k => k.Qty > 0 || k.Code.Length > 0 || k.Size.Length > 0).ToList();
        if (opts.Auto) foreach (var k in skus) if (!k.Sole) { k.Auto = true; k.Drop = opts.Drop; }
        if (opts.TierSeqs is not null) foreach (var k in skus) if (opts.TierSeqs.TryGetValue(k.Id, out var ts)) k.TierSeq = ts;
        if (opts.TierCap is { } cap) foreach (var k in skus) k.MaxLayers = k.MaxLayers > 0 ? Math.Min(k.MaxLayers, cap) : cap;
        var groups = skus.Select(k => new C3dGroup { Sku = k }).ToList();
        // thứ tự đóng: mặt hàng > size > ngày (hoặc đúng thứ tự nhập)
        if (opts.SortMode != "manual")
        {
            var dir = opts.DateDir == "desc" ? -1 : 1;
            int Cmp(C3dGroup a, C3dGroup b)
            {
                var x = a.Sku; var y = b.Sku; var d = NatCmp(x.Code, y.Code); if (d != 0) return d;
                d = NatCmp(x.Size, y.Size); if (d != 0) return d;
                double dx = DateVal(x.Date), dy = DateVal(y.Date); bool nx = double.IsNaN(dx), ny = double.IsNaN(dy);
                if (nx != ny) return nx ? 1 : -1; if (!nx && dx != dy) return Math.Sign(dx - dy) * dir;
                d = NatCmp(x.Date, y.Date); if (d != 0) return d * dir;
                return x.Idx - y.Idx;
            }
            groups.Sort(Cmp);
        }
        var weightLeft = c.MaxPayload > 0 ? c.MaxPayload : double.PositiveInfinity; double loadedKg = 0;
        var sampleQ = opts.SampleOn ? MinSample : 0;
        for (var gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi]; var k = g.Sku; g.Index = gi; g.Requested = k.Qty; g.Loaded = 0; g.Left = k.Qty; g.Sample = 0;
            if (errors.Count > 0 || k.Qty <= 0) continue;
            if (!(k.L > 0 && k.W > 0 && k.H > 0)) { g.Reasons.Add("Thiếu kích thước thùng."); continue; }
            if (!PassDoor(k, c)) { g.Reasons.Add("Thùng quá lớn, không đưa lọt cửa cont (" + R1(c.DoorW) + "×" + R1(c.DoorH) + " mm)."); continue; }
            g.Ok = true;
        }
        var boxes = new List<C3dBox>(); var mainBoxes = new List<C3dBox>(); var sampBoxes = new List<C3dBox>();
        int WLimit(C3dSku k, int n) => k.Kg > 0 && double.IsFinite(weightLeft) ? (int)Math.Min(n, Math.Floor((weightLeft + 1e-6) / k.Kg)) : n;
        void AddWhy(C3dGroup g, string t) { if (!g.Reasons.Contains(t)) g.Reasons.Add(t); }
        // A) DÃY MẪU HẢI QUAN (cần ≥ 2 mặt hàng xếp được) — tính trước để chừa chỗ + trừ số thùng mẫu khỏi hàng chính
        var sampPlan = sampleQ > 0 && errors.Count == 0 ? PlanSampleRows(groups.Where(g => g.Ok).ToList(), sp, sampleQ, weightLeft) : null;
        if (sampPlan is not null) { for (var i = 0; i < sampPlan.Infos.Count; i++) sampPlan.Infos[i].G.Sample = sampPlan.Placed[i]; if (double.IsFinite(weightLeft)) weightLeft -= sampPlan.Kg; }
        // B) HÀNG CHÍNH: từ vách đầu ra tới trước các dãy mẫu
        var spM = sampPlan is not null ? sp with { X1 = sp.X1 - sampPlan.Reserve, Lu = sp.Lu - sampPlan.Reserve } : sp;
        var S = new SState { Sp = spM, Cursor = sp.X0 };
        var lastIdx = -1; for (var i = 0; i < groups.Count; i++) if (groups[i].Ok && groups[i].Requested - groups[i].Sample > 0) lastIdx = i;
        for (var gi = 0; gi < groups.Count; gi++)
        {
            var g = groups[gi]; if (!g.Ok) continue; var k = g.Sku; var n0 = k.Qty - g.Sample; if (n0 <= 0) continue;
            var n = WLimit(k, n0); if (n < n0) AddWhy(g, "Hết tải trọng cho phép (" + Rnd(c.MaxPayload) + " kg)."); if (n <= 0) continue;
            var r = RunGroup(S, g, n, opts, gi == lastIdx);
            if (r.Reason == "khong-vua")
            {
                AddWhy(g, k.Orient.Length > 0 ? "Hướng đặt bạn chọn không vừa cont (so với bề ngang / red line). Đổi hướng hoặc chọn “Tự động”." : "Không có hướng đặt nào vừa trong cont (kích thước thùng so với bề ngang / red line). Thử bật xoay / đặt nằm.");
                continue;
            }
            foreach (var b in r.Boxes) { b.Group = gi; b.SkuId = k.Id; }
            mainBoxes.AddRange(r.Boxes);
            g.Loaded = r.Placed; g.Patterns = r.Patterns; if (r.SoleFail.Length > 0) g.SoleFail = r.SoleFail; g.StripUsed = r.StripUsed; g.StackedOn = r.StackedOn;
            if (r.Placed < n) AddWhy(g, "Hết chỗ trong cont (đã tới dãy mẫu / cửa / red line).");
            if (double.IsFinite(weightLeft)) weightLeft -= r.Placed * k.Kg;
        }
        if (sampPlan is not null)
        {
            var cx = mainBoxes.Count > 0 ? mainBoxes.Max(b => b.X + b.Dx) : sp.X0; foreach (var f in sampPlan.Infos) f.G.Sample = 0;
            foreach (var rw in sampPlan.Rows)
            {
                if (cx + rw.D > sp.X1 + 1e-3) continue; var y = sp.Y0;
                for (var i = 0; i < sampPlan.Infos.Count; i++)
                {
                    var f = sampPlan.Infos[i];
                    foreach (var n in rw.Cols[i])
                    {
                        for (var t = 0; t < n; t++) { var b = Mk(cx, y, t * f.O.Dz, f.O, t + 1); b.Group = f.G.Index; b.SkuId = f.K.Id; b.Sample = true; sampBoxes.Add(b); f.G.Sample++; f.G.Loaded++; }
                        y += f.O.Dy;
                    }
                }
                cx += rw.D;
            }
            foreach (var f in sampPlan.Infos)
            {
                var g = f.G; if (g.Patterns.Count == 0) g.Patterns.Add(new C3dPatNote(OrientText(f.O, f.K) + " · hàng mẫu", "col", f.O));
                if (g.Loaded < g.Requested && g.Reasons.Count == 0) AddWhy(g, "Hết chỗ trong cont (các dãy mẫu sát cửa).");
            }
        }
        var allBoxes = mainBoxes.Concat(sampBoxes).ToList();
        foreach (var g in groups)
        {
            g.Left = g.Requested - g.Loaded; g.Kg = g.Loaded * g.Sku.Kg; loadedKg += g.Kg;
            g.VolUnit = g.Sku.L * g.Sku.W * g.Sku.H; g.VolReq = g.VolUnit * g.Requested; g.VolLoaded = g.VolUnit * g.Loaded;
            var mine = allBoxes.Where(b => b.Group == g.Index).ToList();
            if (mine.Count > 0) { g.XStart = mine.Min(b => b.X); g.XEnd = mine.Max(b => b.X + b.Dx); }
            if (g.SoleFail.Length > 0) AddWhy(g, "Không xếp SOLE được: " + (SoleWhy.TryGetValue(g.SoleFail, out var w) ? w : "") + " → xếp thẳng hàng.");
        }
        boxes.AddRange(allBoxes);
        for (var i = 0; i < boxes.Count; i++) boxes[i].Seq = i + 1;
        // đánh số DÃY: lát cắt liên tiếp theo chiều dài cont; các thùng chồng lấn về chiều dọc thuộc cùng 1 dãy
        var rows = new List<C3dRow>();
        {
            var ix = Enumerable.Range(0, boxes.Count).OrderBy(i => boxes[i].X).ToArray(); var end = -1e18;
            foreach (var i in ix)
            {
                var b = boxes[i];
                if (b.X >= end - 1e-3) { rows.Add(new C3dRow { Row = rows.Count + 1, X0 = b.X, X1 = b.X + b.Dx }); end = b.X + b.Dx; } else end = Math.Max(end, b.X + b.Dx);
                var r = rows[^1]; r.X1 = Math.Max(r.X1, b.X + b.Dx); r.N++; if (b.Sample) r.Sample = true; r.Gs.Add(b.Group); r.Ws.Add(b.Wall ?? -1);
                r.Y0 = Math.Min(r.Y0, b.Y); r.Y1 = Math.Max(r.Y1, b.Y + b.Dy); r.Top = Math.Max(r.Top, b.Z + b.Dz); r.HMin = Math.Min(r.HMin, b.Dz); b.Row = r.Row;
            }
            foreach (var r in rows)
            {
                r.Group = r.Gs.Count == 1 ? r.Gs.First() : -1;
                r.Wall = r.Gs.Count == 1 && r.Ws.Count == 1 && r.Ws.First() >= 0 && !r.Sample ? r.Ws.First() : -1;
                r.Gaps = new C3dGaps { Right = r.Y0, Left = c.W - r.Y1, Top = r.Top, Ceiling = c.H - r.Top, Red = sp.Red - r.Top, Eff = sp.EffH - r.Top };
            }
        }
        // HÀNG MẪU HẢI QUAN — TỰ CHIA ĐỀU (1 mặt hàng): mỗi dòng ≥ MinSample thùng, chia đều các dãy, ưu tiên tầng trên cùng
        var nSample = 0;
        if (sampleQ > 0 && sampPlan is null)
        {
            foreach (var g in groups)
            {
                if (!g.Ok) continue; var mine = boxes.Where(b => b.Group == g.Index).ToList(); if (mine.Count == 0) continue;
                var need = Math.Min(sampleQ, mine.Count); var byRow = new SortedDictionary<int, List<C3dBox>>();
                foreach (var b in mine) { if (!byRow.TryGetValue(b.Row, out var l)) byRow[b.Row] = l = new(); l.Add(b); }
                var ids = byRow.Keys.ToList(); var R = ids.Count;
                var quota = ids.Select((_, i) => (int)(Math.Floor((double)need * (i + 1) / R) - Math.Floor((double)need * i / R))).ToArray();
                var miss = 0; for (var i = 0; i < R; i++) if (quota[i] > byRow[ids[i]].Count) { miss += quota[i] - byRow[ids[i]].Count; quota[i] = byRow[ids[i]].Count; }
                for (var i = 0; miss > 0 && i < 4 * R; i++) { var j = i % R; if (quota[j] < byRow[ids[j]].Count) { quota[j]++; miss--; } }
                for (var i = 0; i < R; i++)
                {
                    var q = quota[i]; if (q <= 0) continue;
                    var cand = byRow[ids[i]].OrderByDescending(b => b.Z + b.Dz).ThenBy(b => b.Y).ToList(); var topZ = cand[0].Z + cand[0].Dz;
                    var nTop = cand.Count(b => b.Z + b.Dz >= topZ - 1e-3); var head = cand.Take(nTop).ToList(); var rest = cand.Skip(nTop);
                    var sh = i * 3 % Math.Max(1, head.Count);
                    var ordered = head.Skip(sh).Concat(head.Take(sh)).Concat(rest);
                    foreach (var b in ordered.Take(q)) { b.Sample = true; nSample++; g.Sample++; }
                }
                if (g.Sample < Math.Min(sampleQ, g.Sku.Qty)) AddWhy(g, "Chỉ xếp được " + g.Sample + " thùng, chưa đủ " + sampleQ + " thùng mẫu hải quan.");
            }
        }
        if (sampPlan is not null) { nSample = sampBoxes.Count; foreach (var g in groups) if (g.Ok && g.Sample < Math.Min(sampleQ, g.Sku.Qty)) AddWhy(g, "Chỉ xếp được " + g.Sample + " thùng, chưa đủ " + sampleQ + " thùng mẫu hải quan."); }
        foreach (var r in rows) r.Samples = 0;
        foreach (var b in boxes) if (b.Sample && b.Row - 1 < rows.Count && b.Row >= 1) rows[b.Row - 1].Samples++;
        // QUY TẮC CAO → THẤP (trong → cửa)
        var heightViol = new List<C3dHeightViol>();
        for (var i = 0; i + 1 < rows.Count; i++)
        {
            var a = rows[i]; var b2 = rows[i + 1]; if (a.Sample || b2.Sample) continue;
            if (sp.EffH - a.Top >= a.HMin - 1e-3 && b2.Top > a.Top + 1e-3) heightViol.Add(new C3dHeightViol(a.Row, b2.Row, a.Top, b2.Top));
        }
        var maxTier = boxes.Count > 0 ? boxes.Max(b => b.Tier) : 0;
        var ver = Verify(boxes, sp, opts.MinSupport);
        // tổng hợp
        var volBox = boxes.Sum(b => b.Dx * b.Dy * b.Dz); var volUse = sp.Lu * sp.Wu * sp.EffH; var volIn = c.L * c.W * c.H;
        double cxS = 0, cyS = 0, kgSum = 0; var skuById = skus.GroupBy(s => s.Id).ToDictionary(x => x.Key, x => x.First());
        foreach (var b in boxes) { var w = skuById.TryGetValue(b.SkuId, out var k) ? k.Kg : 0; cxS += w * (b.X + b.Dx / 2); cyS += w * (b.Y + b.Dy / 2); kgSum += w; }
        var cog = kgSum > 0 ? new C3dCog(cxS / kgSum, cyS / kgSum, cxS / kgSum / c.L * 100, cyS / kgSum / c.W * 100) : null;
        C3dGaps? gaps = null;
        if (boxes.Count > 0)
        {
            double mnx = 1e18, mxx = -1e18, mny = 1e18, mxy = -1e18, top = 0;
            foreach (var b in boxes) { mnx = Math.Min(mnx, b.X); mxx = Math.Max(mxx, b.X + b.Dx); mny = Math.Min(mny, b.Y); mxy = Math.Max(mxy, b.Y + b.Dy); top = Math.Max(top, b.Z + b.Dz); }
            gaps = new C3dGaps { Front = mnx, Door = c.L - mxx, Right = mny, Left = c.W - mxy, Top = top, Ceiling = c.H - top, Red = sp.Red - top, Eff = sp.EffH - top };
        }
        var usedLen = boxes.Count > 0 ? boxes.Max(b => b.X + b.Dx) - sp.X0 : 0;
        var reqTotal = groups.Sum(g => g.Requested); var loadTotal = groups.Sum(g => g.Loaded);
        if (!ver.Ok) errors.Add("Kiểm tra độc lập phát hiện " + ver.Violations.Count + " lỗi trong phương án (xem chi tiết).");
        if (cog is not null && Math.Abs(cog.XPct - 50) > 12) warnings.Add("Trọng tâm hàng lệch dọc cont: " + cog.XPct.ToString("0", CultureInfo.InvariantCulture) + "% chiều dài tính từ vách đầu (nên 40–60%).");
        if (cog is not null && Math.Abs(cog.YPct - 50) > 6) warnings.Add("Trọng tâm hàng lệch ngang cont: " + cog.YPct.ToString("0", CultureInfo.InvariantCulture) + "% bề ngang (nên gần 50%).");
        if (c.MaxPayload > 0 && loadedKg > c.MaxPayload + 1e-6) errors.Add("Vượt tải trọng cho phép.");
        if (!(c.RedLine > 0)) warnings.Add("Chưa nhập red line — đang lấy chiều cao trong của cont làm giới hạn.");
        if (c.RedLine > 0 && c.DoorH > 0 && c.DoorH < c.RedLine) warnings.Add("Cao cửa (" + R1(c.DoorH) + " mm) thấp hơn red line (" + R1(c.RedLine) + " mm) → giới hạn chiều cao xếp lấy theo cao cửa.");
        if (heightViol.Count > 0) warnings.Add("Chưa đúng quy tắc xếp từ cao xuống thấp (trong → cửa): dãy " + string.Join(", ", heightViol.Take(6).Select(v => v.Row + " thấp hơn dãy " + v.Next)) + (heightViol.Count > 6 ? " …" : "") + ". Thường do chỉnh dãy bằng tay / dãy mẫu / trải đều / ghép lấp chỗ trống.");
        return new C3dPlan
        {
            Ok = errors.Count == 0, Errors = errors, Warnings = warnings, Container = c, Space = sp, Groups = groups, Boxes = boxes, Skus = skus, Rows = rows,
            HeightViol = heightViol, Verify = ver, MaxTier = maxTier,
            Totals = new C3dTotals
            {
                VolReqAll = groups.Sum(g => g.VolReq), VolFreeInternal = volIn - volBox, VolFreeUsable = volUse - volBox, Gaps = gaps, Rows = rows.Count, Sample = nSample,
                Requested = reqTotal, Loaded = loadTotal, Left = reqTotal - loadTotal, Kg = loadedKg, Payload = c.MaxPayload, VolBox = volBox, VolUsable = volUse, VolInternal = volIn,
                FillUsablePct = volUse > 0 ? volBox / volUse * 100 : 0, FillInternalPct = volIn > 0 ? volBox / volIn * 100 : 0, UsedLength = usedLen, FreeLength = sp.Lu - usedLen, EffH = sp.EffH, Cog = cog,
            },
            Options = opts.Clone(),
        };
    }

    // TỰ SẮP XẾP HỢP LÝ KHI TRỐNG MỘT BÊN: khe dư bề ngang → tự đóng SO LE trái/phải từng tầng (chỉ nhận khi kiểm tra vẫn đạt, không mất thùng).
    private static C3dPlan SolveBest(C3dContainer c, List<C3dSku> skus, C3dOptions opts)
    {
        var p1 = SolveOnce(c, skus, opts);
        if (!opts.AutoLayout || !(p1.Totals.Requested > 0) || !p1.Groups.Any(g => !g.Sku.Sole && g.Loaded > 0)) return p1;
        var spare = p1.Totals.Left == 0 && p1.Totals.FreeLength >= 0.15 * p1.Space.Lu;
        C3dPlan p2;
        try { var o2 = opts.Clone(); o2.Auto = true; o2.Airflow = spare; o2.Drop = spare; p2 = SolveOnce(c, skus, o2); } catch { return p1; }
        var applied = p2.Groups.Any(g => g.Sku.Auto && g.Patterns.Any(q => q.Kind == "sole"));
        if (!applied || !p2.Verify.Ok || p2.Errors.Count > 0 || p2.Totals.Loaded < p1.Totals.Loaded || (!spare && p2.Totals.UsedLength > p1.Totals.UsedLength + 1)) return p1;
        p2.LayoutNote = spare ? "Cont còn trống nhiều → tự đóng so le (sole) trái/phải từng tầng + ưu tiên dựng đứng để khí lạnh lưu thông hai bên."
            : "Khe dư bề ngang không xếp thêm được thùng → tự đóng so le trái/phải từng tầng (không mất chỗ) để có đường khí hai bên và chống nghiêng.";
        return p2;
    }

    // GỢI Ý ĐÓNG THÊM khi còn hàng rớt (chỉ gợi ý — người dùng bấm "Áp dụng" mới đổi).
    private static List<C3dHint> BuildHints(C3dContainer c0, List<C3dSku> skus, C3dOptions opts, C3dPlan plan)
    {
        var outp = new List<C3dHint>();
        if (!opts.Spread && plan.Totals.Left == 0 && plan.Totals.Requested > 0 && plan.Totals.UsedLength < 0.5 * plan.Space.Lu)
        {
            var o = opts.Clone(); o.Spread = true; o.Compare = false;
            var sp = SolveSpread(c0, skus, o, plan);
            if (!ReferenceEquals(sp, plan) && sp.SpreadNote.Length > 0)
                outp.Add(new C3dHint("spread", 0, sp.Totals.Loaded, 0, "Hàng ít (mới dùng " + Rnd(plan.Totals.UsedLength / plan.Space.Lu * 100) + "% chiều dài): trải đều thành " + sp.Totals.Rows + " dãy thấp thay vì " + plan.Totals.Rows + " dãy cao", sp.SpreadNote, new(), false, true, null));
        }
        if (!(plan.Totals.Left > 0)) return outp;
        var bse = plan.Totals.Loaded; var c = plan.Container;
        if (opts.HeightRule)
        {
            try
            {
                var o = opts.Clone(); o.HeightRule = false; o.Compare = false; o.Spread = false;
                var fr = SolveBest(c0, skus, o);
                if (fr.Verify.Ok && fr.Totals.Loaded > bse) outp.Add(new C3dHint("free", fr.Totals.Loaded - bse, fr.Totals.Loaded, fr.Totals.Left, "Bỏ quy tắc \"cao → thấp (trong → cửa)\" để đóng thêm — có thể có dãy phía trong thấp hơn dãy phía ngoài", "", new(), false, null, false));
            }
            catch { /* bỏ qua */ }
        }
        if (!skus.All(k => k.Id.Length > 0)) return outp;
        C3dPlan? TryRun(C3dContainer cc, List<C3dSku> ks) { try { var o = opts.Clone(); o.Compare = false; o.Auto = false; o.Airflow = false; o.Drop = false; return SolveOnce(cc, ks, o); } catch { return null; } }
        var need = plan.Groups.Where(g => g.Left > 0 && g.Ok && (!g.Sku.AllowLay || g.Sku.Orient.Length > 0 || g.Sku.RowPlan.Length > 0 || g.Sku.RowSet.Count > 0)).Select(g => g.Sku.Id).ToList();
        var hasGap = c.ClearFront > 0 || c.ClearRear > 0;
        string Nm(List<string> ids) => string.Join(", ", plan.Groups.Where(g => ids.Contains(g.Sku.Id)).Select(g => (g.Sku.Code.Length > 0 ? g.Sku.Code : "mặt hàng") + (g.Sku.Size.Length > 0 ? " " + g.Sku.Size : "")));
        var variants = new List<(string id, List<string> skus, bool gap, string text)>();
        if (need.Count > 0) variants.Add(("lay", need, false, "Bỏ chốt hướng + cho phép đặt nằm / xoay cho " + Nm(need)));
        if (hasGap) variants.Add(("gap", new(), true, "Giảm khe vách đầu / khe cửa từ " + Rnd(c.ClearFront) + " / " + Rnd(c.ClearRear) + " mm về 0 (đóng sát)"));
        if (need.Count > 0 && hasGap) variants.Add(("both", need, true, "Cả hai: bỏ chốt hướng + cho đặt nằm cho " + Nm(need) + " và đóng sát đầu / cửa"));
        foreach (var v in variants)
        {
            var ks = skus.Select(k => { var x = k.Clone(); if (v.skus.Contains(x.Id)) ApplyLayPatch(x); return x; }).ToList();
            var cc = c0.Clone(); if (v.gap) { cc.ClearFront = 0; cc.ClearRear = 0; }
            var r = TryRun(cc, ks); if (r is null || !r.Verify.Ok) continue; var gain = r.Totals.Loaded - bse;
            if (gain > 0) outp.Add(new C3dHint(v.id, gain, r.Totals.Loaded, r.Totals.Left, v.text, "", v.skus, v.gap, null, null));
        }
        return outp.OrderByDescending(h => h.Gain).Take(3).ToList();
    }

    /// <summary>Bản vá "bỏ chốt hướng + cho đặt nằm / xoay" của gợi ý.</summary>
    public static void ApplyLayPatch(C3dSku k) { k.AllowLay = true; k.AllowRotate = true; k.AllowStand = true; k.Orient = ""; k.RowPlan = ""; k.RowSet = new(); }

    // TRẢI ĐỀU (hàng ít): hạ số tầng để hàng trải ra tới ~spreadPct % chiều dài cont.
    private static C3dPlan SolveSpread(C3dContainer c0, List<C3dSku> skus, C3dOptions opts, C3dPlan p1)
    {
        var Lu = p1.Space.Lu; var pct = Math.Min(100, Math.Max(20, opts.SpreadPct > 0 ? opts.SpreadPct : 70)); var main = Lu; var target = main * pct / 100;
        if (!(p1.Totals.Requested > 0) || p1.Totals.Left > 0 || p1.MaxTier < 2) return p1;
        if (p1.Totals.UsedLength >= target - 1) return p1;
        string Note(C3dPlan p)
        {
            var rt = new Dictionary<int, int>(); foreach (var b in p.Boxes) rt[b.Row] = Math.Max(rt.TryGetValue(b.Row, out var t) ? t : 0, b.Tier);
            var ts = rt.Values.ToList(); var mn = ts.Min(); var mx = ts.Max(); var rws = ts.Count;
            return "Hàng ít nên trải đều: " + rws + " dãy (dùng " + Rnd(p.Totals.UsedLength / main * 100) + "% chiều dài chứa hàng), mỗi dãy " + (mn == mx ? mx.ToString(CultureInfo.InvariantCulture) : mn + "–" + mx) + " tầng (≈ " + Rnd(p.Totals.Loaded / (double)Math.Max(1, rws)) + " thùng/dãy) — thay vì " + p1.Totals.Rows + " dãy cao " + p1.MaxTier + " tầng.";
        }
        // CÁCH 1: trải ra ĐỦ số dãy vừa chiều dài; số tầng từng dãy chênh nhau tối đa 1 (dãy cao đứng trước)
        try
        {
            var info = new SortedDictionary<int, (int n, Dictionary<int, (double x0, double x1)> walls, Dictionary<int, int> t1)>();
            foreach (var b in p1.Boxes)
            {
                if (b.Wall is not { } w || w < 0) continue;
                if (!info.TryGetValue(b.Group, out var g)) g = (0, new(), new());
                g.n++;
                var cur = g.walls.TryGetValue(w, out var ww) ? ww : (1e18, -1e18); g.walls[w] = (Math.Min(cur.Item1, b.X), Math.Max(cur.Item2, b.X + b.Dx));
                if (b.Tier == 1) g.t1[w] = (g.t1.TryGetValue(w, out var tt) ? tt : 0) + 1; // số cột của vách
                info[b.Group] = g;
            }
            double totalLen = 0; var calc = new Dictionary<int, (int wc, double D, int capT, int Tsum)>();
            foreach (var (k, g) in info)
            {
                var wc = g.walls.Count; var D = g.walls.Values.Max(w => w.x1 - w.x0); var capT = Math.Max(1, g.t1.Values.DefaultIfEmpty(1).Max());
                var Tsum = (int)Math.Ceiling(g.n / (double)capT); calc[k] = (wc, D, capT, Tsum); totalLen += wc * D;
            }
            if (info.Count > 0 && totalLen > 0)
            {
                var sc = target / totalLen; var seqs = new Dictionary<string, int[]>();
                foreach (var (k, g) in calc)
                {
                    var n = Math.Min(g.Tsum, Math.Max(g.wc, (int)Math.Floor(g.wc * sc + 1e-9))); var bse = g.Tsum / n; var extra = g.Tsum - bse * n;
                    seqs[p1.Groups[k].Sku.Id] = Enumerable.Range(0, n).Select(i => bse + (i < extra ? 1 : 0)).ToArray();
                }
                var o = opts.Clone(); o.TierSeqs = seqs; o.Spread = false; o.Compare = false;
                var p2 = SolveBest(c0, skus, o);
                if (p2.Totals.Left == 0 && p2.Verify.Ok && p2.Errors.Count == 0 && p2.Totals.UsedLength <= target + 1 && p2.Totals.Rows > p1.Totals.Rows) { p2.SpreadNote = Note(p2); return p2; }
            }
        }
        catch { /* thử cách 2 */ }
        // CÁCH 2: hạ ĐỀU số tầng của mọi dãy tới mức nhỏ nhất vẫn vừa mục tiêu
        for (var t = 1; t < p1.MaxTier; t++)
        {
            C3dPlan p;
            try { var o = opts.Clone(); o.TierCap = t; o.Spread = false; o.Compare = false; p = SolveBest(c0, skus, o); } catch { continue; }
            if (p.Totals.Left == 0 && p.Verify.Ok && p.Errors.Count == 0 && p.Totals.UsedLength <= target + 1) { p.SpreadNote = Note(p); p.SpreadTier = t; return p; }
        }
        return p1;
    }

    /// <summary>Tính phương án xếp hàng (kèm so sánh "ghép / tách" và gợi ý đóng thêm).</summary>
    public static C3dPlan Solve(C3dContainer container, IEnumerable<C3dSku> skus, C3dOptions? options = null)
    {
        var opts = (options ?? new C3dOptions()).Clone(); var list = skus.ToList();
        var plan = SolveBest(container, list, opts);
        if (opts.Spread) plan = SolveSpread(container, list, opts, plan);
        if (opts.Compare && plan.Totals.Requested > 0 && plan.SpreadNote.Length == 0)
        {
            try
            {
                var o = opts.Clone(); o.MergeTail = !opts.MergeTail;
                var alt = SolveBest(container, list, o);
                plan.Compare = new C3dCompare(!opts.MergeTail, alt.Totals.Loaded, alt.Totals.Kg, alt.Ok, alt.Totals.UsedLength, alt.Totals.Loaded - plan.Totals.Loaded);
            }
            catch { /* bỏ qua */ }
        }
        if (opts.Hints) { try { plan.Hints = BuildHints(container, list, opts, plan); } catch { plan.Hints = new(); } }
        return plan;
    }
}
