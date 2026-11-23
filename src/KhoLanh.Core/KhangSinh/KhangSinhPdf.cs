using System.Text.RegularExpressions;
using Tok = KhoLanh.Core.KhangSinh.PdfText.Tok;

namespace KhoLanh.Core.KhangSinh;

/// <summary>
/// Đọc PDF phiếu kết quả kháng sinh — chép đúng thuật toán của app cũ (tra-cuu-khang-sinh.html, PC 3.4 → 7.78):
///  • Phiếu lần 1 "KẾT QUẢ KIỂM KHÁNG SINH NGUYÊN LIỆU (KHO)": mỗi hàng = 1 lô (STT · Code · Số lô · 7 chỉ tiêu · Kết quả · Đặt tính · Ghi chú).
///  • Phiếu kiểm lại "KẾT QUẢ PHÂN TÍCH DƯ LƯỢNG KHÁNG SINH" (2 cột Kết quả lần 1 / lần 2): mỗi mẫu có tên nhiều dòng, "Lô: N (ngày)".
/// Mỗi phiếu đọc bằng 2 cách độc lập (A = theo toạ độ cột, B = theo thứ tự chữ trong file / theo hàng) rồi đối soát:
/// Lô · Mẫu · Đặt tính khác nhau → phải xem PDF gốc và tick "Đã so" mới nhập được.
/// </summary>
public static class KhangSinhPdf
{
    public const string KindLan1 = "lan1", KindLan2 = "lan2";

    public sealed class Lot
    {
        public string Batch = "";
        public List<string>? Batches;
        public string Date = "";
        public bool Same(Lot? o) => o is not null && o.Batch == Batch && o.Date == Date
            && (Batches is null ? o.Batches is null : o.Batches is not null && Batches.SequenceEqual(o.Batches));
        public static bool Eq(Lot? a, Lot? b) => a is null ? b is null : a.Same(b);
    }
    public sealed record Issue(string Lv, string Msg);
    public sealed record Box(double X1, double X2, double Y1, double Y2);

    /// <summary>Kết quả cách đọc thứ 2.</summary>
    public sealed class Alt
    {
        public double Stt;
        public List<string> Lines = new();
        public List<string> Vals = new();
        public string? Kq1;
        public string Kq2 = "", DatTinh = "", GhiChu = "", Code = "";
        public Lot? Lot;
    }

    public sealed class Sample
    {
        public string Kind = KindLan2;
        public double Stt;
        public bool NoStt;
        public string Group = "";
        public List<string> Lines = new();
        public Dictionary<string, string> Raw = new(), Inputs = new();
        public string Kq1 = "", Kq2 = "", DatTinh = "", GhiChu = "";
        public int Page;
        public List<string> Stray = new();
        public List<Issue> Issues = new();
        public bool Ok, Edited;
        public Box? Box;
        // phiếu lần 1
        public string RowKey = "", Code = "", SoloRaw = "";
        public Lot? Lot;
        // đối soát
        public Alt? Alt;
        public int Ix, Fi;
        public string Sid = "";
        /// <summary>Kết quả lần 1 đã lưu của lô được ghép (để so với cột KQ lần 1 của phiếu).</summary>
        public string? Lan1Saved;
        public string Id => Fi + ":" + Sid;
    }

    public sealed class Phieu
    {
        public string Kind = KindLan2, File = "", Date = "", So = "";
        public List<Sample> Samples = new();
        public List<Issue> Issues = new();
        public List<Issue> FileIssues = new();
        public int PageCount, AltCount;
        public List<double> AltStts = new();
        public List<Alt> AltRows = new();
        public bool FileOk;
        public byte[] Bytes = Array.Empty<byte>();
        public List<PdfText.Page> Pages = new();
    }

    // ================================================================== hằng / hàm nhỏ

    public static readonly (string Key, string Name, string Label)[] AB =
    {
        ("enro", "enro", "Enro"), ("cipro", "cipro", "Cipro"), ("oxy", "oxy", "Oxy"), ("doxy", "doxy", "Doxy"),
        ("sulfo", "sulfonamide", "Sulfo"), ("aoz", "aoz", "AOZ"), ("cap", "cap", "CAP"),
    };
    private static readonly string[] AbKeys = AB.Select(a => a.Key).ToArray();
    // \b của JS chỉ tính chữ ASCII → (?![A-Za-z0-9_])
    private const string WB = "(?![A-Za-z0-9_])";
    public static readonly Regex Code = new(@"^\d{1,2}\.[A-ZĐ][A-Z0-9Đ.\-]*$");
    public static readonly Regex Val = new(@"^(ND|\*\*\*|[<>]?\s*\d+(?:[.,]\d+)?)$");
    private static readonly Regex KqStart = new(@"^(KHÔNG\s+)?ĐẠT" + WB, RegexOptions.IgnoreCase);
    private static readonly Regex Dat = new(@"^ĐẠT" + WB, RegexOptions.IgnoreCase);
    private static readonly Regex Num3 = new(@"^\d{1,3}$");
    private static readonly Regex Lan1Date = new(@"(\d{1,2})[./-](\d{1,2})[./-](\d{4})");
    private static readonly Regex GroupL1 = new(@"^Ng[aà]y nh[aậ]p nguy[eê]n li[eệ]u", RegexOptions.IgnoreCase);
    public static readonly Regex NumVal = new(@"^[<>]?\s*\d+(?:[.,]\d+)?$");

    public static string Fold(string? s) => Regex.Replace(KhangSinhCore.NormText(s), @"\s+", " ").Trim();
    private static bool HeadIs(string text, string n) { var f = Fold(text); return f.StartsWith(n, StringComparison.Ordinal) || (f.Length >= 5 && n.StartsWith(f, StringComparison.Ordinal)); }
    public static double SubStt(double b, int k) => Js.Round((b + Math.Min(k, 9) / 10.0 + Math.Max(0, k - 9) / 1000.0) * 1000) / 1000;
    private static double Cx(Tok t) => t.X + t.W / 2;
    private static string Collapse(string s) => Regex.Replace(s, @"\s+", " ").Trim();
    private static string Iso(Match m) => $"{m.Groups[3].Value}-{m.Groups[2].Value.PadLeft(2, '0')}-{m.Groups[1].Value.PadLeft(2, '0')}";
    public static string Dmy(string? d) => KhangSinhCore.Dmy(d);
    public static string N(double v) => Js.Num(v);
    private static double Min(IEnumerable<double> xs) { var l = xs.ToList(); return l.Count == 0 ? double.PositiveInfinity : l.Min(); }
    private static double Max(IEnumerable<double> xs) { var l = xs.ToList(); return l.Count == 0 ? double.NegativeInfinity : l.Max(); }
    private static int YX(Tok a, Tok b) { var c = Js.Cmp(b.Y - a.Y); return c != 0 ? c : Js.Cmp(a.X - b.X); }

    /// <summary>lan2DtNum: số Đặt tính (1–83) ở đầu ô, bỏ qua phần chữ phía sau.</summary>
    public static int? DtNum(string? v)
    {
        foreach (Match m in Regex.Matches(v ?? "", @"(?:^|[^\d.,])(\d{1,2})(?![\d]|[.,]\d)"))
        { var n = int.Parse(m.Groups[1].Value); if (n >= 1 && n <= 83) return n; }
        return null;
    }

    /// <summary>"Lô: 2 (29.09.26)" / "Lô: 3+5 (08.09.26)" trong tên mẫu → lô + ngày; phiếu lần 1 = Số lô + ngày nhập.</summary>
    public static Lot? LotRef(Sample s) => s.Kind == KindLan1 ? s.Lot : LotOfLines(s.Lines);
    public static Lot? LotOfLines(IEnumerable<string> lines)
    {
        foreach (var l in lines)
        {
            var m = Regex.Match(l, @"L[oôÔ]\s*:?\s*(\d+(?:\s*[+,&]\s*\d+)*)\s*\(\s*(\d{1,2})[./-](\d{1,2})[./-](\d{2,4})\s*\)", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var y = m.Groups[4].Value.Length == 2 ? "20" + m.Groups[4].Value : m.Groups[4].Value;
                var bs = Regex.Split(m.Groups[1].Value, @"\s*[+,&]\s*").Where(x => x.Length > 0).ToList();
                return new Lot { Batch = string.Join("+", bs), Batches = bs, Date = $"{y}-{m.Groups[3].Value.PadLeft(2, '0')}-{m.Groups[2].Value.PadLeft(2, '0')}" };
            }
        }
        return null;
    }

    // ================================================================== nhận dạng phiếu

    public static bool IsLan2(IReadOnlyList<PdfText.PageToks> pages)
    {
        var toks = pages.SelectMany(p => p.Toks).Select(t => Fold(t.Text)).ToList();
        return toks.Any(t => Regex.IsMatch(t, @"(^|ket qua )lan\s*2$")) && toks.Any(t => Regex.IsMatch(t, @"(^|ket qua )lan\s*1$"));
    }
    public static bool IsLan1(IReadOnlyList<PdfText.PageToks> pages)
    {
        var toks = pages.SelectMany(p => p.Toks).Select(t => Fold(t.Text)).ToList();
        return toks.Any(t => t.StartsWith("ngay nhap nguyen lieu", StringComparison.Ordinal)) && toks.Contains("so lo") && toks.Contains("dat tinh") && !IsLan2(pages);
    }

    /// <summary>Đọc 1 file PDF: null nếu không phải phiếu kết quả kháng sinh (lần 1 / kiểm lại).</summary>
    public static Phieu? Read(byte[] bytes, string fileName)
    {
        var raw = PdfText.Read(bytes);
        var pages = PdfText.Pages(raw);
        var kind = IsLan2(pages) ? KindLan2 : IsLan1(pages) ? KindLan1 : "";
        if (kind.Length == 0) return null;
        var r = kind == KindLan2 ? ParseLan2(pages) : ParseLan1(pages);
        r.Kind = kind; r.File = fileName; r.Bytes = bytes; r.Pages = raw;
        if (kind == KindLan2)
        {
            var alt = ParseLan2Stream(pages);
            r.AltCount = alt.Count; r.AltStts = alt.Values.Select(v => v.Stt).ToList(); r.AltRows = alt.Values.ToList();
            foreach (var s in r.Samples) s.Alt = alt.Get(s.Stt);
        }
        else
        {
            var alt = ParseLan1Lines(pages);
            r.AltCount = alt.Count; r.AltStts = alt.Values.Select(v => v.Stt).ToList(); r.AltRows = alt.Values.ToList();
            foreach (var s in r.Samples) s.Alt = alt.Get(s.RowKey);
        }
        var seen = new Dictionary<double, int>();
        for (int i = 0; i < r.Samples.Count; i++)
        {
            var s = r.Samples[i]; s.Ix = i;
            seen[s.Stt] = seen.GetValueOrDefault(s.Stt) + 1;
            s.Sid = seen[s.Stt] == 1 ? N(s.Stt) : N(s.Stt) + "#" + seen[s.Stt];
        }
        return r;
    }

    /// <summary>Đối soát lại cả phiếu (gọi lại sau mỗi lần sửa tay).</summary>
    public static void CheckAll(Phieu r)
    {
        CheckFile(r);
        foreach (var s in r.Samples) CheckSample(s, r);
    }

    /// <summary>Map giữ thứ tự chèn (như Map của JS: ghi đè giữ vị trí cũ).</summary>
    public sealed class OMap<TK, TV> where TK : notnull
    {
        private readonly List<TK> _keys = new();
        private readonly Dictionary<TK, TV> _d = new();
        public void Set(TK k, TV v) { if (!_d.ContainsKey(k)) _keys.Add(k); _d[k] = v; }
        public TV? Get(TK k) => _d.TryGetValue(k, out var v) ? v : default;
        public int Count => _keys.Count;
        public IEnumerable<TV> Values => _keys.Select(k => _d[k]);
    }

    // ================================================================== phiếu KIỂM LẠI — cách A (toạ độ cột)

    private sealed class RowA { public double Y; public double? Stt; public Tok? SttTok; public List<Tok> Toks = new(); public bool NoStt; public List<string>? Stray; }

    public static Phieu ParseLan2(IReadOnlyList<PdfText.PageToks> pages)
    {
        var res = new Phieu { PageCount = pages.Count };
        string group = ""; double lastStt = 0;
        for (int pi = 0; pi < pages.Count; pi++)
        {
            var toks = pages[pi].Toks;
            var all = string.Join(" ", toks.Select(t => t.Text));
            var dm = Regex.Match(all, @"Ng[aà]y ph[aâ]n t[ií]ch[^0-9]*(\d{1,2})[./-](\d{1,2})[./-](\d{4})", RegexOptions.IgnoreCase);
            if (dm.Success && res.Date.Length == 0) res.Date = Iso(dm);
            var sm = Regex.Match(all, @"\(No\)\s*:\s*([0-9A-Za-z/.-]+(?:\s+[0-9]+)?)", RegexOptions.IgnoreCase);
            if (sm.Success && res.So.Length == 0) res.So = sm.Groups[1].Value.Trim();
            Tok? Find(string re) => toks.FirstOrDefault(t => Regex.IsMatch(Fold(t.Text), re));
            var hEnro = Find("^enro$");
            if (hEnro is null) { res.Issues.Add(new("warn", $"Trang {pi + 1}: không thấy dòng tiêu đề cột (Enro…) — trang này không được đọc.")); continue; }
            var heads = new Dictionary<string, Tok?> { ["mau"] = Find("^mau$"), ["ten"] = Find("^ten mau") };
            foreach (var (k, n, _) in AB) heads[k] = toks.FirstOrDefault(t => HeadIs(t.Text, n) && Math.Abs(t.Y - hEnro.Y) < 8);
            var kqs = Js.Sort(toks.Where(t => Regex.IsMatch(Fold(t.Text), "^ket qua$")), (a, b) => Js.Cmp(a.X - b.X));
            heads["kq1"] = kqs.ElementAtOrDefault(0); heads["kq2"] = kqs.ElementAtOrDefault(1);
            heads["dattinh"] = Find("^dat tinh$"); heads["ghichu"] = Find("^ghi chu$");
            var miss = AB.Where(a => heads[a.Key] is null).Select(a => a.Label).ToList();
            if (heads["kq1"] is null || heads["kq2"] is null) miss.Add("Kết quả lần 1/2");
            if (miss.Count > 0) { res.Issues.Add(new("err", $"Trang {pi + 1}: không nhận ra cột {string.Join(", ", miss)} — trang này không được đọc.")); continue; }
            var order = AbKeys.Concat(new[] { "kq1", "kq2", "dattinh", "ghichu" }).Where(k => heads[k] is not null).ToList();
            var centers = order.Select(k => (k, c: Cx(heads[k]!))).ToList();
            var enroLeft = Cx(heads["enro"]!) - (Cx(heads["cipro"]!) - Cx(heads["enro"]!)) / 2;
            double? sttC = null;
            string ColOf(Tok t)
            {
                var x = Cx(t);
                if (x < enroLeft)
                {
                    if (sttC is not null) return Math.Abs(x - sttC.Value) <= 7 ? "mau" : "ten";
                    return x < (heads["mau"] is { } hm ? Cx(hm) + 25 : 0) ? "mau" : "ten";
                }
                string best = ""; double bd = 1e9;
                foreach (var (k, c) in centers) { var d = Math.Abs(c - x); if (d < bd) { bd = d; best = k; } }
                return best;
            }
            var mrplYs = toks.Where(t => Regex.IsMatch(t.Text, "mrpl", RegexOptions.IgnoreCase)).Select(t => t.Y).ToList();
            var mrplLow = mrplYs.Count > 0 ? mrplYs.Min() : hEnro.Y;
            var parenYs = toks.Where(t => Regex.IsMatch(t.Text.Trim(), @"^\)+$") && t.Y < mrplLow && mrplLow - t.Y <= 10).Select(t => t.Y);
            var headBottom = new[] { mrplLow, hEnro.Y }.Concat(parenYs).Min() - 3;
            var footer = toks.FirstOrDefault(t => Regex.IsMatch(t.Text, @"ki[eể]m nghi[eệ]m vi[eê]n|^[ĐD][oồ]ng Th[aá]p, ng[aà]y", RegexOptions.IgnoreCase));
            var body = toks.Where(t => t.Y < headBottom && t.Y > (footer is not null ? footer.Y + 3 : -1e9)).ToList();
            {
                var mc = heads["mau"] is { } hm ? Cx(hm) : 0;
                var cand = body.Where(t => Num3.IsMatch(t.Text.Trim()) && Cx(t) < enroLeft && Cx(t) < mc + 25).Select(Cx).ToList();
                var near = cand.Where(c => Math.Abs(c - mc) <= 12).ToList();
                var pool = Js.Sort(near.Count > 0 ? near : cand, (a, b) => Js.Cmp(a - b));
                if (pool.Count > 0) sttC = pool[pool.Count / 2];
            }
            var stts = Js.Sort(body.Where(t => ColOf(t) == "mau" && Num3.IsMatch(t.Text.Trim())), (a, b) => Js.Cmp(b.Y - a.Y));
            var groupsAt = body.Where(t =>
            {
                var c = ColOf(t); var s = t.Text.Trim();
                bool alone = !body.Any(o => !ReferenceEquals(o, t) && Math.Abs(o.Y - t.Y) < 3);
                if (Regex.IsMatch(s, @"^Ng[aà]y nh[aậ]p", RegexOptions.IgnoreCase) && alone) return true;
                return AbKeys.Contains(c) && s.Length > 4 && s == s.ToUpperInvariant() && Regex.IsMatch(s, "[A-ZĐÀ-Ỹ]{3}") && !Val.IsMatch(s) && !Regex.IsMatch(s, "^(ND|ĐẠT|KHÔNG)") && alone;
            }).Select(t => (t.Y, Text: t.Text.Trim(), Tok: t)).ToList();
            var rows = stts.Select(s => new RowA { Y = s.Y, Stt = double.Parse(s.Text.Trim()), SttTok = s }).ToList();
            {
                var codes = Js.Sort(body.Where(t => ColOf(t) == "dattinh" && Code.IsMatch(t.Text.Trim())), (a, b) => Js.Cmp(b.Y - a.Y));
                if (codes.Count > 0)
                {
                    var cg = codes.Skip(1).Select((c, i) => codes[i].Y - c.Y).Where(g => g > 1).ToList();
                    var tol = cg.Count > 0 ? cg.Min() * 0.5 : 20;
                    var pairs = new List<(double d, int ri, int ci)>();
                    for (int ri = 0; ri < rows.Count; ri++) for (int ci = 0; ci < codes.Count; ci++) { var d = Math.Abs(rows[ri].Y - codes[ci].Y); if (d <= tol) pairs.Add((d, ri, ci)); }
                    pairs = Js.Sort(pairs, (a, b) => Js.Cmp(a.d - b.d));
                    var usedR = new HashSet<int>(); var usedC = new HashSet<int>();
                    foreach (var (_, ri, ci) in pairs) if (!usedR.Contains(ri) && !usedC.Contains(ci)) { usedR.Add(ri); usedC.Add(ci); }
                    for (int ci = 0; ci < codes.Count; ci++) if (!usedC.Contains(ci)) rows.Add(new RowA { Y = codes[ci].Y, NoStt = true });
                    rows = Js.Sort(rows, (a, b) => Js.Cmp(b.Y - a.Y));
                }
            }
            if (rows.Count == 0) continue;
            {
                double bse = lastStt; int k = 0;
                foreach (var r in rows) { if (r.Stt is not null) { bse = r.Stt.Value; k = 0; } else { k++; r.Stt = SubStt(bse, k); } }
                lastStt = bse;
            }
            var gaps = Js.Sort(rows.Skip(1).Select((r, i) => rows[i].Y - r.Y), (a, b) => Js.Cmp(a - b));
            var gap = gaps.Count > 0 ? gaps[gaps.Count / 2] : 40;
            var left = Min(body.Select(t => t.X)); var right = Max(body.Select(t => t.X + t.W));
            var nameToks = new List<Tok>();
            foreach (var t in body)
            {
                if (stts.Contains(t) || groupsAt.Any(g => ReferenceEquals(g.Tok, t))) continue;
                if (ColOf(t) == "ten") { nameToks.Add(t); continue; }
                RowA best = rows[0]; double bd = 1e9;
                foreach (var r in rows) { var d = Math.Abs(r.Y - t.Y); if (d < bd) { bd = d; best = r; } }
                best.Toks.Add(t);
                if (bd > gap * 0.85) (best.Stray ??= new()).Add(t.Text.Trim());
            }
            {
                var ls = new List<(double Y, List<Tok> Toks)>();
                foreach (var t in Js.Sort(nameToks, YX))
                {
                    var li = ls.FindIndex(l => Math.Abs(l.Y - t.Y) < 3);
                    if (li >= 0) ls[li].Toks.Add(t); else ls.Add((t.Y, new List<Tok> { t }));
                }
                int Nn = rows.Count, L = ls.Count; int[]? assign = null;
                if (L >= Nn)
                {
                    const double INF = 1e18; var pre = new double[L + 1];
                    for (int k = 0; k < L; k++) pre[k + 1] = pre[k] + ls[k].Y;
                    var dp = new double[Nn + 1, L + 1]; var bk = new int[Nn + 1, L + 1];
                    for (int i = 0; i <= Nn; i++) for (int j = 0; j <= L; j++) dp[i, j] = INF;
                    dp[0, 0] = 0;
                    for (int i = 1; i <= Nn; i++) for (int j = i; j <= L; j++) for (int a = i - 1; a < j; a++)
                            {
                                if (dp[i - 1, a] >= INF) continue;
                                var d = (pre[j] - pre[a]) / (j - a) - rows[i - 1].Y; var c = dp[i - 1, a] + d * d;
                                if (c < dp[i, j]) { dp[i, j] = c; bk[i, j] = a; }
                            }
                    if (dp[Nn, L] < INF)
                    {
                        assign = new int[L]; int j2 = L;
                        for (int i = Nn; i >= 1; i--) { var a = bk[i, j2]; for (int k = a; k < j2; k++) assign[k] = i - 1; j2 = a; }
                    }
                }
                for (int k = 0; k < ls.Count; k++)
                {
                    var l = ls[k];
                    int ri = assign is not null ? assign[k] : 0;
                    if (assign is null) { double bd = 1e9; for (int q = 0; q < rows.Count; q++) { var d = Math.Abs(rows[q].Y - l.Y); if (d < bd) { bd = d; ri = q; } } }
                    rows[ri].Toks.AddRange(l.Toks);
                }
            }
            foreach (var r in rows)
            {
                var g = groupsAt.Where(x => x.Y > r.Y).OrderBy(x => x.Y).Select(x => (string?)x.Text).FirstOrDefault();
                if (g is not null) group = g;
                var sample = new Sample { Kind = KindLan2, Stt = r.Stt!.Value, NoStt = r.NoStt, Group = group, Page = pi + 1, Stray = r.Stray ?? new() };
                r.Toks = Js.Sort(r.Toks, YX);
                var byCol = new Dictionary<string, List<Tok>>();
                foreach (var t in r.Toks) { var c = ColOf(t); if (!byCol.TryGetValue(c, out var lst)) byCol[c] = lst = new(); lst.Add(t); }
                var lines = new List<(double Y, string Text)>();
                foreach (var t in byCol.GetValueOrDefault("ten") ?? new())
                {
                    var li = lines.FindIndex(l => Math.Abs(l.Y - t.Y) < 3);
                    if (li >= 0) lines[li] = (lines[li].Y, lines[li].Text + " " + t.Text.Trim()); else lines.Add((t.Y, t.Text.Trim()));
                }
                sample.Lines = lines.Select(l => Collapse(l.Text)).Where(x => x.Length > 0).ToList();
                string Txt(string k) => Collapse(string.Join(" ", (byCol.GetValueOrDefault(k) ?? new()).Select(t => t.Text.Trim())));
                foreach (var k in AbKeys) { var v = Txt(k); sample.Raw[k] = v; sample.Inputs[k] = v == "***" ? "" : v; }
                sample.Kq1 = Txt("kq1"); sample.Kq2 = Txt("kq2"); sample.DatTinh = Txt("dattinh"); sample.GhiChu = Txt("ghichu");
                var ys = r.Toks.Concat(r.SttTok is not null ? new[] { r.SttTok } : Array.Empty<Tok>()).ToList();
                sample.Box = new Box(left - 2, right + 2, Min(ys.Select(t => t.Y)) - 3, Max(ys.Select(t => t.Y + (t.H != 0 ? t.H : 7))) + 3);
                res.Samples.Add(sample);
            }
        }
        res.Samples = Js.Sort(res.Samples, (a, b) => Js.Cmp(a.Stt - b.Stt));
        return res;
    }

    // ================================================================== phiếu KIỂM LẠI — cách B (thứ tự chữ trong file)

    private static readonly Regex StopRe = new(@"^(MẪU KIỂM|THÀNH PHẨM|KẾT QUẢ PHÂN TÍCH|Enro$|Kiểm nghiệm viên|Đồng Tháp|Người thẩm tra|CÔNG TY)", RegexOptions.IgnoreCase);
    private static readonly Regex FooterRe = new(@"^(Kiểm nghiệm viên|Đồng Tháp|Người thẩm tra)", RegexOptions.IgnoreCase);
    private static readonly Regex GhKey = new(@"^(enro[A-Za-z0-9_]*|cipro[A-Za-z0-9_]*|oxy[A-Za-z0-9_]*|doxy[A-Za-z0-9_]*|sulfo[A-Za-z0-9_]*|aoz|cap|chloram[A-Za-z0-9_]*|nitrofur[A-Za-z0-9_]*)" + WB, RegexOptions.IgnoreCase);

    public static OMap<double, Alt> ParseLan2Stream(IReadOnlyList<PdfText.PageToks> pages)
    {
        var toks = pages.SelectMany(p => p.Raw).ToList();
        var outm = new OMap<double, Alt>();
        double last = 0; (double Stt, List<string> Toks)? cur = null; bool lastDone = false, ended = false;
        bool HasCode((double Stt, List<string> Toks)? c) => c is not null && c.Value.Toks.Any(x => Code.IsMatch(x));
        List<List<string>> SplitRows(List<string> t)
        {
            var runs = new List<int>();
            for (int q = 0; q < t.Count;)
            {
                if (!Val.IsMatch(t[q])) { q++; continue; }
                int e = q; while (e < t.Count && Val.IsMatch(t[e])) e++;
                if (e - q >= 7) runs.Add(e - 7);
                q = e;
            }
            if (runs.Count < 2) return new() { t };
            var parts = new List<List<string>>(); int from = 0;
            for (int r = 0; r < runs.Count - 1; r++)
            {
                var region = t.GetRange(runs[r] + 7, runs[r + 1] - (runs[r] + 7));
                int lc = -1; for (int i = 0; i < region.Count; i++) if (Code.IsMatch(region[i])) lc = i;
                if (lc < 0) return new() { t };
                int k = lc + 1; bool gh = false;
                while (k < region.Count)
                {
                    var x = region[k];
                    if (GhKey.IsMatch(x)) { gh = true; k++; }
                    else if (gh && Regex.IsMatch(x, @"^[:;,]?\s*[<>]?\d+(?:[.,]\d+)?[;,]?$")) k++;
                    else break;
                }
                var cut = runs[r] + 7 + k;
                parts.Add(t.GetRange(from, cut - from)); from = cut;
            }
            parts.Add(t.GetRange(from, t.Count - from));
            return parts;
        }
        void Emit(double key, List<string> t)
        {
            lastDone = t.Any(x => Code.IsMatch(x));
            int vi = -1;
            for (int q = 0; q + 7 <= t.Count; q++) if (t.Skip(q).Take(7).All(x => Val.IsMatch(x))) { vi = q; break; }
            if (vi < 0) vi = t.FindIndex(x => Val.IsMatch(x));
            var name = vi < 0 ? t : t.Take(vi).ToList();
            var vals = vi < 0 ? new List<string>() : t.Skip(vi).Take(7).ToList();
            var rest = vi < 0 ? new List<string>() : t.Skip(vi + 7).ToList();
            int lastCode = -1; for (int i = 0; i < rest.Count; i++) if (Code.IsMatch(rest[i])) lastCode = i;
            var kqEnd = lastCode >= 0 ? lastCode : rest.Count;
            var kqIdx = new List<int>(); for (int i = 0; i < rest.Count; i++) if (i < kqEnd && KqStart.IsMatch(rest[i])) kqIdx.Add(i);
            int KqFrom(int i) => i > 0 && Regex.IsMatch(rest[i - 1], "^KHÔNG$", RegexOptions.IgnoreCase) && Dat.IsMatch(rest[i]) ? i - 1 : i;
            string JoinTxt(IEnumerable<string> a) => Collapse(string.Join(" ", a));
            string kq1 = "", kq2 = "";
            if (kqIdx.Count > 0) { var k2 = KqFrom(kqIdx[^1]); kq1 = JoinTxt(rest.Take(k2)); kq2 = JoinTxt(rest.Skip(k2).Take(kqEnd - k2)); }
            else kq1 = JoinTxt(rest.Take(kqEnd));
            var s = new Alt
            {
                Stt = key, Lines = name.ToList(), Vals = vals, Kq1 = kq1, Kq2 = kq2,
                DatTinh = lastCode >= 0 ? rest[lastCode] : "", GhiChu = lastCode >= 0 ? string.Join(" ", rest.Skip(lastCode + 1)) : "",
            };
            s.Lot = LotOfLines(name);
            outm.Set(key, s);
        }
        void Flush()
        {
            if (cur is null) return;
            var parts = SplitRows(cur.Value.Toks);
            for (int i = 0; i < parts.Count; i++) Emit(i == 0 ? cur.Value.Stt : SubStt(cur.Value.Stt, i), parts[i]);
            cur = null;
        }
        bool IsStt(string t, int i)
        {
            if (ended) return false;
            var prev = i > 0 ? toks[i - 1] : "";
            if (Regex.IsMatch(prev, @":\s*$")) return false;
            if (t == N(last + 1)) return true;
            if (last < 1 || !Regex.IsMatch(t, @"^[1-9]\d{0,2}$")) return false;
            var n = double.Parse(t);
            if (!(n > last + 1 && n <= last + 40)) return false;
            if (cur is not null ? !HasCode(cur) : !lastDone) return false;
            var nx = i + 1 < toks.Count ? toks[i + 1] : "";
            return Regex.IsMatch(nx, "[A-Za-zÀ-ỹ]") && !Val.IsMatch(nx) && !Code.IsMatch(nx);
        }
        for (int i = 0; i < toks.Count; i++)
        {
            var t = toks[i];
            if (IsStt(t, i)) { Flush(); last = double.Parse(t); cur = (last, new List<string>()); continue; }
            if (StopRe.IsMatch(t)) { Flush(); if (FooterRe.IsMatch(t) && outm.Count > 0) ended = true; continue; }
            cur?.Toks.Add(t);
        }
        Flush();
        return outm;
    }

    // ================================================================== phiếu LẦN 1 (Kho) — cách A

    public static Phieu ParseLan1(IReadOnlyList<PdfText.PageToks> pages)
    {
        var res = new Phieu { Kind = KindLan1, PageCount = pages.Count };
        string carryDate = "";
        for (int pi = 0; pi < pages.Count; pi++)
        {
            var toks = pages[pi].Toks;
            var fk = toks.FirstOrDefault(t => Regex.IsMatch(t.Text.Trim(), @"^Ng[aà]y ki[eể]m", RegexOptions.IgnoreCase));
            if (fk is not null) { var dk = Lan1Date.Match(fk.Text); if (dk.Success && res.Date.Length == 0) res.Date = Iso(dk); }
            Tok? Find(string re) => toks.FirstOrDefault(t => Regex.IsMatch(Fold(t.Text), re));
            var hEnro = Find("^enro$");
            if (hEnro is null) { res.Issues.Add(new("warn", $"Trang {pi + 1}: không thấy dòng tiêu đề cột (Enro…) — trang này không được đọc.")); continue; }
            Tok? Near(string re) => toks.FirstOrDefault(t => Regex.IsMatch(Fold(t.Text), re) && Math.Abs(t.Y - hEnro.Y) < 16);
            var heads = new List<(string K, Tok? T)>
            {
                ("stt", Near("^stt$")), ("code", Near("^code")), ("solo", Near("^so lo$")), ("socon", Near("^so con$")), ("kl", Near("^khoi$")), ("pcs", Near("^pcs")), ("sl", Near("^so$")),
            };
            foreach (var (k, n, _) in AB) heads.Add((k, toks.FirstOrDefault(t => HeadIs(t.Text, n) && Math.Abs(t.Y - hEnro.Y) < 8)));
            heads.Add(("kq2", Near("^ket qua$"))); heads.Add(("dattinh", Near("^dat tinh$"))); heads.Add(("ghichu", Near("^ghi chu$"))); heads.Add(("am", Near("^am$")));
            Tok? H(string k) => heads.First(h => h.K == k).T;
            var labels = new Dictionary<string, string> { ["stt"] = "STT", ["solo"] = "Số lô", ["kq2"] = "Kết Quả", ["dattinh"] = "Đặt Tính" };
            var miss = AB.Where(a => H(a.Key) is null).Select(a => a.Label).Concat(new[] { "stt", "solo", "kq2", "dattinh" }.Where(k => H(k) is null).Select(k => labels[k])).ToList();
            if (miss.Count > 0) { res.Issues.Add(new("err", $"Trang {pi + 1}: không nhận ra cột {string.Join(", ", miss)} — trang này không được đọc.")); continue; }
            var centers = heads.Where(h => h.T is not null).Select(h => (h.K, c: Cx(h.T!))).ToList();
            string ColOf(Tok t) { var x = Cx(t); string best = ""; double bd = 1e9; foreach (var (k, c) in centers) { var d = Math.Abs(c - x); if (d < bd) { bd = d; best = k; } } return best; }
            var lodCand = toks.Where(t => t.Y < hEnro.Y && t.Y > hEnro.Y - 40 && Cx(t) > Cx(H("enro")!) - 20 && Cx(t) < Cx(H("cap")!) + 20 && Regex.IsMatch(t.Text.Trim(), @"^\d+(\.\d+)?$")).ToList();
            var lodTop = lodCand.Count > 0 ? lodCand.Max(t => t.Y) : 0;
            var lodRow = lodCand.Where(t => Math.Abs(t.Y - lodTop) < 3).ToList();
            var headBottom = (lodRow.Count > 0 ? lodRow.Min(t => t.Y) : hEnro.Y - 12) - 3;
            var footer = toks.FirstOrDefault(t => Regex.IsMatch(t.Text.Trim(), @"^Ng[aà]y ki[eể]m|^[ĐD][oồ]ng Th[aá]p", RegexOptions.IgnoreCase));
            var body = toks.Where(t => t.Y < headBottom && t.Y > (footer is not null ? footer.Y + 3 : -1e9)).ToList();
            var groups = body.Where(t => GroupL1.IsMatch(t.Text.Trim())).Select(t => { var m = Lan1Date.Match(t.Text); return (t.Y, Date: m.Success ? Iso(m) : "", Tok: t); }).ToList();
            var stts = Js.Sort(body.Where(t => ColOf(t) == "stt" && Num3.IsMatch(t.Text.Trim())), (a, b) => Js.Cmp(b.Y - a.Y));
            var rows = stts.Select(s => new RowA { Y = s.Y, Stt = double.Parse(s.Text.Trim()), SttTok = s }).ToList();
            if (rows.Count == 0) continue;
            var gaps = Js.Sort(rows.Skip(1).Select((r, i) => rows[i].Y - r.Y), (a, b) => Js.Cmp(a - b));
            var gap = gaps.Count > 0 ? gaps[gaps.Count / 2] : 30;
            var left = Min(body.Select(t => t.X)); var right = Max(body.Select(t => t.X + t.W));
            var noStray = new HashSet<string> { "socon", "kl", "pcs", "sl", "am" };
            foreach (var t in body)
            {
                if (stts.Contains(t) || groups.Any(g => ReferenceEquals(g.Tok, t))) continue;
                RowA best = rows[0]; double bd = 1e9;
                foreach (var r in rows) { var d = Math.Abs(r.Y - t.Y); if (d < bd) { bd = d; best = r; } }
                best.Toks.Add(t);
                var c = ColOf(t);
                if (bd > gap * 0.85 && !noStray.Contains(c)) (best.Stray ??= new()).Add(t.Text.Trim());
            }
            foreach (var r in rows)
            {
                var g = groups.Where(x => x.Y > r.Y).OrderBy(x => x.Y).Select(x => (string?)x.Date).FirstOrDefault();
                var date = g ?? carryDate;
                if (g is not null) carryDate = g;
                r.Toks = Js.Sort(r.Toks, YX);
                var byCol = new Dictionary<string, List<Tok>>();
                foreach (var t in r.Toks) { var c = ColOf(t); if (!byCol.TryGetValue(c, out var lst)) byCol[c] = lst = new(); lst.Add(t); }
                string Txt(string k) => Collapse(string.Join(" ", (byCol.GetValueOrDefault(k) ?? new()).Select(t => t.Text.Trim())));
                var solo = Txt("solo"); var code = Txt("code");
                var s = new Sample
                {
                    Kind = KindLan1, Stt = r.Stt!.Value, RowKey = pi + ":" + N(Js.Round(r.Y * 10)), Group = date.Length > 0 ? "Nhập " + Dmy(date) : "", Code = code,
                    Lines = code.Length > 0 ? new() { code } : new(), Kq2 = Txt("kq2"), DatTinh = Txt("dattinh"), GhiChu = Txt("ghichu"), Page = pi + 1, Stray = r.Stray ?? new(),
                };
                s.Lot = Regex.IsMatch(solo, @"^\d+$") ? new Lot { Batch = solo, Date = date } : null;
                s.SoloRaw = solo;
                foreach (var k in AbKeys) { var v = Txt(k); s.Raw[k] = v; s.Inputs[k] = v == "***" ? "" : v; }
                var ys = r.Toks.Concat(new[] { r.SttTok! }).ToList();
                s.Box = new Box(left - 2, right + 2, Min(ys.Select(t => t.Y)) - 3, Max(ys.Select(t => t.Y + (t.H != 0 ? t.H : 7))) + 3);
                res.Samples.Add(s);
            }
        }
        return res;
    }

    // ================================================================== phiếu LẦN 1 — cách B (theo hàng chứa STT)

    public static OMap<string, Alt> ParseLan1Lines(IReadOnlyList<PdfText.PageToks> pages)
    {
        var outm = new OMap<string, Alt>();
        string carryDate = "";
        for (int pi = 0; pi < pages.Count; pi++)
        {
            var toks = pages[pi].Toks;
            var hCode = toks.FirstOrDefault(t => Fold(t.Text).StartsWith("code", StringComparison.Ordinal));
            var hEnro = toks.FirstOrDefault(t => Fold(t.Text) == "enro");
            if (hCode is null || hEnro is null) continue;
            var foot = toks.FirstOrDefault(t => Regex.IsMatch(t.Text.Trim(), @"^Ng[aà]y ki[eể]m|^[ĐD][oồ]ng Th[aá]p", RegexOptions.IgnoreCase));
            var body = toks.Where(t => t.Y < hEnro.Y - 20 && t.Y > (foot is not null ? foot.Y + 3 : -1e9)).ToList();
            var groups = body.Where(t => GroupL1.IsMatch(t.Text.Trim())).Select(t => { var m = Lan1Date.Match(t.Text); return (t.Y, Date: m.Success ? Iso(m) : ""); }).ToList();
            var stts = Js.Sort(body.Where(t => t.X < hCode.X - 5 && Num3.IsMatch(t.Text.Trim())), (a, b) => Js.Cmp(b.Y - a.Y));
            Tok? NearestStt(Tok t) { Tok? bs = null; double bd = 1e9; foreach (var s2 in stts) { var d = Math.Abs(s2.Y - t.Y); if (d < bd) { bd = d; bs = s2; } } return bs; }
            foreach (var st in stts)
            {
                var g = groups.Where(x => x.Y > st.Y).OrderBy(x => x.Y).Select(x => (string?)x.Date).FirstOrDefault();
                var date = g ?? carryDate; if (g is not null) carryDate = g;
                var line = Js.Sort(body.Where(t => Math.Abs(t.Y - st.Y) < 2.5 && !ReferenceEquals(t, st)), (a, b) => Js.Cmp(a.X - b.X));
                var lotTok = line.FirstOrDefault(t => Regex.IsMatch(t.Text.Trim(), @"^\d+$"));
                int ci = -1;
                for (int i = 0; i < line.Count; i++) if (Code.IsMatch(line[i].Text.Trim())) ci = i;
                if (ci < 0)
                    for (int i = 0; i < line.Count; i++)
                    {
                        var x = line[i].Text.Trim();
                        if (DtNum(x) is not null && Regex.IsMatch(x, @"^\d{1,2}(?:[.\s]|$)") && !Regex.IsMatch(x, @"^\d{1,2}[.,]\d") && line.Take(i).Count(u => Val.IsMatch(u.Text.Trim())) >= 7) ci = i;
                    }
                var before = (ci < 0 ? new List<Tok>() : line.Take(ci).ToList()).Select(t => t.Text.Trim()).Where(x => Val.IsMatch(x)).ToList();
                var vals = before.Skip(Math.Max(0, before.Count - 7)).ToList();
                var codeToks = lotTok is not null
                    ? Js.Sort(body.Where(t => t.X > st.X + st.W && t.X + t.W < lotTok.X && t.Text.Trim().Length > 0 && !GroupL1.IsMatch(t.Text.Trim()) && ReferenceEquals(NearestStt(t), st)), YX)
                    : new List<Tok>();
                var kqTok = Js.Sort(body.Where(t => KqStart.IsMatch(t.Text.Trim()) && Math.Abs(t.Y - st.Y) <= 7), (a, b) => Js.Cmp(Math.Abs(a.Y - st.Y) - Math.Abs(b.Y - st.Y))).FirstOrDefault();
                var kq = "";
                if (kqTok is not null)
                {
                    var cont = body.Where(t => !ReferenceEquals(t, kqTok) && t.Y < kqTok.Y && kqTok.Y - t.Y <= 13 && Math.Abs(Cx(t) - Cx(kqTok)) <= 45
                        && !Regex.IsMatch(t.Text.Trim(), "^(KHÔNG\\s+)?ĐẠT" + WB + "|^ND$", RegexOptions.IgnoreCase) && Regex.IsMatch(t.Text.Trim(), "^[A-ZĐÀ-Ỹ ,]+$")).ToList();
                    kq = Collapse(string.Join(" ", new[] { kqTok }.Concat(Js.Sort(cont, (a, b) => Js.Cmp(b.Y - a.Y))).Select(t => t.Text.Trim())));
                }
                outm.Set(pi + ":" + N(Js.Round(st.Y * 10)), new Alt
                {
                    Stt = double.Parse(st.Text.Trim()), Vals = vals, Kq2 = kq, DatTinh = ci >= 0 ? line[ci].Text.Trim() : "",
                    Code = Collapse(string.Join(" ", codeToks.Select(t => t.Text.Trim()))), Lot = lotTok is not null ? new Lot { Batch = lotTok.Text.Trim(), Date = date } : null,
                });
            }
        }
        return outm;
    }

    // ================================================================== ĐỐI SOÁT

    public static bool Flag(Sample s) => s.Issues.Any(i => i.Lv != "info" && i.Lv != "note");
    public static bool FileFlag(Phieu r) => r.FileIssues.Any(i => i.Lv != "info" && i.Lv != "note");
    public static bool Need(Sample s) => Flag(s) && !s.Ok;

    public static List<Issue> CheckSample(Sample s, Phieu r)
    {
        var iss = new List<Issue>();
        var L1 = r.Kind == KindLan1; var KQL = L1 ? "KẾT QUẢ" : "KẾT QUẢ LẦN 2";
        void Add(string lv, string msg) => iss.Add(new(lv, msg));
        static string Nz(string? v) => Collapse(v ?? "").ToUpperInvariant();
        static string Tight(string? v) => Regex.Replace(Nz(v), @"\s+", "");
        static string Sortc(string? v) { var a = Tight(v).ToCharArray(); Array.Sort(a, (x, y) => x.CompareTo(y)); return new string(a); }
        static string LotTxt(Lot? l) => l is not null ? $"Lô {l.Batch} ({Dmy(l.Date)})" : "không có lô";
        if (s.Lines.Count == 0) Add("err", "Không đọc được tên mẫu.");
        if (s.NoStt) Add("info", $"Dòng này trên phiếu KHÔNG ghi số thứ tự — app tự đánh số {N(s.Stt)} (nằm sau mẫu số {N(Math.Floor(s.Stt))}). Không chặn nhập.");
        if (L1)
        {
            if (s.Lot is null) Add("err", "Không đọc được Số lô" + (s.SoloRaw.Length > 0 ? $" (đọc ra \"{s.SoloRaw}\")" : "") + ".");
            else if (s.Lot.Date.Length == 0) Add("err", "Không xác định được ngày nhập nguyên liệu của lô này.");
            if (s.Code.Length == 0) Add("err", "Không đọc được Code/Mã.");
        }
        var rf = LotRef(s);
        if (rf is not null && r.Date.Length > 0 && string.CompareOrdinal(rf.Date, r.Date) > 0) Add("note", $"Lô ghi ngày {Dmy(rf.Date)} SAU ngày phân tích {Dmy(r.Date)} — xem lại ngày của lô.");
        if (s.DatTinh.Length == 0) Add("err", "Ô Đặt tính trống.");
        else if (DtNum(s.DatTinh) is null) Add("err", $"Ô Đặt tính ghi \"{s.DatTinh}\" — không thấy số từ 1 đến 83.");
        if (s.Kq2.Length == 0) Add("note", $"Ô {KQL} trống.");
        var a = s.Alt;
        if (a is null) Add("err", "Không đọc được mẫu này theo cách thứ 2 (thứ tự chữ trong PDF) — hãy so tay với PDF gốc.");
        else
        {
            Lot? r1 = LotRef(s), r2 = a.Lot;
            if (!Lot.Eq(r1, r2)) Add("err", $"Hai cách đọc ra LÔ khác nhau: {LotTxt(r1)} ≠ {LotTxt(r2)}.");
            if (L1)
            {
                var c1 = Tight(s.Code); var c2 = Tight(a.Code);
                if (c1 != c2 && Sortc(s.Code) != Sortc(a.Code)) Add("err", $"Hai cách đọc ra CODE khác nhau: {(Nz(s.Code) is { Length: > 0 } x ? x : "(trống)")} ≠ {(Nz(a.Code) is { Length: > 0 } y ? y : "(trống)")}.");
            }
            else
            {
                var j1 = string.Join(" ", s.Lines); var j2 = string.Join(" ", a.Lines);
                var n1 = Tight(j1); var n2 = Tight(j2);
                if (n2.Length > 0 && n1 != n2 && Sortc(j1) != Sortc(j2) && !n1.Contains(n2) && !n2.Contains(n1))
                    Add("err", $"Hai cách đọc ra TÊN MẪU khác nhau: {string.Join(" / ", s.Lines)} ≠ {string.Join(" / ", a.Lines)}.");
                else if (n2.Length == 0) Add("note", "Cách đọc 2 không đọc được tên mẫu — hãy so tên mẫu với PDF gốc.");
            }
            int? d1 = DtNum(s.DatTinh), d2 = DtNum(a.DatTinh);
            if (d1 != d2 || (d1 is null && Nz(s.DatTinh) != Nz(a.DatTinh)))
                Add("err", $"Hai cách đọc ra SỐ ĐẶT TÍNH khác nhau ({(d1 is null ? "?" : d1.ToString())} ≠ {(d2 is null ? "?" : d2.ToString())}): {Or(s.DatTinh)} ≠ {Or(a.DatTinh)}.");
            if (!L1 && a.Kq1 is not null && Tight(s.Kq1) != Tight(a.Kq1)) Add("note", $"Hai cách đọc ra KẾT QUẢ LẦN 1 khác nhau: {Or(s.Kq1)} ≠ {Or(a.Kq1)}.");
            if (Tight(s.Kq2) != Tight(a.Kq2)) Add("note", $"Hai cách đọc ra {KQL} khác nhau: {Or(s.Kq2)} ≠ {Or(a.Kq2)}.");
            var v1 = AbKeys.Select(k => Nz(s.Raw.GetValueOrDefault(k))).ToList(); var v2 = a.Vals.Select(Nz).ToList();
            var diff = AB.Where((x, i) => i >= v2.Count || v1[i] != v2[i]).Select(x => x.Label).ToList();
            if (diff.Count > 0) Add("info", $"Tham khảo: 7 ô chỉ tiêu 2 cách đọc khác nhau ở ô {string.Join(", ", diff)} (không chặn nhập).");
        }
        s.Issues = iss;
        return iss;
    }
    private static string Or(string? v) => string.IsNullOrEmpty(v) ? "(trống)" : v;

    public static List<Issue> CheckFile(Phieu r)
    {
        var iss = r.Issues.ToList();
        if (r.Date.Length == 0) iss.Add(r.Kind == KindLan1 ? new("warn", "Không đọc được \"Ngày kiểm\" cuối phiếu.") : new("err", "Không đọc được \"Ngày phân tích\" ở đầu phiếu."));
        if (r.AltCount == 0) iss.Add(new("note", "Không đọc được phiếu theo cách thứ 2 (thứ tự chữ trong PDF) — chỉ còn 1 cách đọc; từng mẫu không đọc được cách 2 sẽ tự báo riêng."));
        else if (r.AltCount != r.Samples.Count) iss.Add(new("note", $"Cách đọc 1 ra {r.Samples.Count} mẫu, cách đọc 2 ra {r.AltCount} mẫu — mẫu nào cách 2 không đọc được đã báo riêng ở từng dòng (không chặn nhập)."));
        var nums = r.Samples.Select(s => s.Stt).ToList();
        var max = nums.Count > 0 ? nums.Max() : 0;
        var missing = new List<int>(); for (int i = 1; i <= max; i++) if (!nums.Contains(i)) missing.Add(i);
        var dup = nums.Where((n, i) => nums.IndexOf(n) != i).ToList();
        var bothSee = r.AltStts.Count == nums.Count && nums.OrderBy(x => x).SequenceEqual(r.AltStts.OrderBy(x => x));
        if (missing.Count > 0)
        {
            if (bothSee) iss.Add(new("info", $"Phiếu nhảy số thứ tự: không có mẫu số {string.Join(", ", missing)} (cả 2 cách đọc đều thấy phiếu không có các số này — thường do dòng ẩn trong Excel)."));
            else
            {
                var msg = $"Thiếu mẫu số {string.Join(", ", missing)} (STT không liền mạch — có thể đọc sót).";
                var seenB = r.AltRows.Where(v => missing.Contains((int)v.Stt) && v.Stt == Math.Floor(v.Stt)).ToList();
                if (seenB.Count > 0)
                {
                    var lo = new[] { 0.0 }.Concat(nums.Where(n => n < missing[0])).Max();
                    var hiL = nums.Where(n => n > missing[0]).ToList();
                    msg += " Cách đọc 2 THẤY: " + string.Join(" ; ", seenB.Select(v => "mẫu " + N(v.Stt) + (v.Lot is not null ? " = Lô " + v.Lot.Batch : "") + (v.Code.Length > 0 ? " · " + v.Code : "")))
                        + " — nằm giữa mẫu " + (lo != 0 ? N(lo) : "?") + " và mẫu " + (hiL.Count == 0 ? "?" : N(hiL.Min())) + " trên phiếu (cách đọc 1 bị sót dòng này).";
                }
                iss.Add(new("err", msg));
            }
        }
        if (dup.Count > 0)
        {
            var dupL = string.Join(", ", dup.Distinct().Select(N));
            if (r.Kind == KindLan1 && r.AltCount == r.Samples.Count && r.Samples.All(s => s.Alt is not null))
                iss.Add(new("info", $"Phiếu ghi trùng số thứ tự {dupL} (STT trên phiếu bị đánh lặp). App đọc từng hàng riêng và nhận dạng lô theo Số lô + Ngày nhập, không theo STT."));
            else iss.Add(new("err", $"Trùng số mẫu {dupL}."));
        }
        r.FileIssues = iss;
        return iss;
    }
    // ================================================================== PDF dạng bảng chung (không phải phiếu) → bảng để khớp cột như Excel

    public sealed record PdfTable(List<string> Headers, List<string[]> Rows);
    private static string TightNorm(string? s) => Regex.Replace(KhangSinhCore.NormText(s), "[^a-z0-9]", "");
    private static readonly string[] DropHeaders = { "soconnl", "khoiluongnl", "pcslb", "soluongctn" };

    /// <summary>extractPdfTable của app cũ: dò dòng tiêu đề (ô "STT"), dòng dữ liệu theo STT 1, 2, 3…, cột theo tâm chữ của dữ liệu.</summary>
    public static PdfTable Table(byte[] pdf) => Table(PdfText.Lines(PdfText.Read(pdf)));
    public static PdfTable Table(List<List<Tok>> lines)
    {
        var empty = new PdfTable(new(), new());
        var nonEmpty = lines.Where(l => l.Count > 0).ToList();
        if (nonEmpty.Count == 0) return empty;
        static double LineY(List<Tok> l) => l.Count == 0 ? 0 : l.Sum(t => t.Y) / l.Count;
        var blocks = new List<List<List<Tok>>>(); var current = new List<List<Tok>>(); double? prevY = null;
        foreach (var line in nonEmpty)
        {
            var y = LineY(line);
            if (prevY is not null && (prevY.Value - y) > 10) { if (current.Count > 0) blocks.Add(current); current = new(); }
            current.Add(line); prevY = y;
        }
        if (current.Count > 0) blocks.Add(current);
        static Tok Leftmost(List<Tok> l) => l.Aggregate((a, b) => a.X < b.X ? a : b);
        var headerBlockIdx = blocks.FindIndex(b => b.Any(l => l.Any(t => TightNorm(t.Text) == "stt")));
        List<List<Tok>> headerLines; List<List<List<Tok>>> dataBlocks;
        if (headerBlockIdx != -1) { headerLines = blocks[headerBlockIdx]; dataBlocks = blocks.Skip(headerBlockIdx + 1).ToList(); }
        else
        {
            var ds = nonEmpty.FindIndex(l => Leftmost(l).Text.Trim() == "1");
            if (ds <= 0) ds = nonEmpty.FindIndex(l => l.Count >= 2) + 1;
            if (ds <= 0) return empty;
            headerLines = nonEmpty.Take(ds).ToList();
            dataBlocks = nonEmpty.Skip(ds).Select(l => new List<List<Tok>> { l }).ToList();
        }
        var headerTokens = headerLines.SelectMany(l => l).ToList();
        if (headerTokens.Count == 0) return empty;
        var headerDup = string.Join("|", Js.Sort(headerTokens, (a, b) => Js.Cmp(a.Y - b.Y) is var c && c != 0 ? c : Js.Cmp(a.X - b.X)).Select(t => t.Text.ToLowerInvariant().Trim()));
        var dateRe = new Regex(@"(\d{1,2})[./\-](\d{1,2})[./\-](\d{2,4})");
        static string BlockText(List<List<Tok>> b) => string.Join(" ", b.Select(l => string.Join(" ", Js.Sort(l, (x, y) => Js.Cmp(x.X - y.X)).Select(t => t.Text))));
        string ToIsoM(Match m) { var y = int.Parse(m.Groups[3].Value); if (y < 100) y += 2000; return $"{y}-{int.Parse(m.Groups[2].Value):00}-{int.Parse(m.Groups[1].Value):00}"; }
        var footerDate = "";
        foreach (var line in lines) { var t = KhangSinhCore.NormText(string.Join(" ", line.Select(x => x.Text))); if (t.Contains("ngay kiem")) { var m = dateRe.Match(t); if (m.Success && footerDate.Length == 0) footerDate = ToIsoM(m); } }
        var matched = new List<List<List<Tok>>>(); var blockDates = new List<string>(); var currentDate = ""; var expected = 1;
        foreach (var block in dataBlocks)
        {
            var bn = string.Join("|", block.Select(l => string.Join("|", l.Select(t => t.Text.ToLowerInvariant().Trim()))));
            if (bn == headerDup) continue;
            var hasCounter = block.Any(l => l.Count > 0 && Leftmost(l).Text.Trim() == expected.ToString());
            if (!hasCounter)
            {
                var t = KhangSinhCore.NormText(BlockText(block)); var m = dateRe.Match(t);
                if (m.Success && t.Contains("ngay") && !t.Contains("ngay kiem")) currentDate = ToIsoM(m);
                continue;
            }
            matched.Add(block); blockDates.Add(currentDate.Length > 0 ? currentDate : footerDate); expected++;
        }
        if (matched.Count == 0) return empty;
        static double CX(Tok t) => t.X + t.W / 2;
        var pool = Js.Sort(matched.SelectMany(b => b.SelectMany(l => l)).Select(CX), (a, b) => Js.Cmp(a - b));
        var clusters = new List<(double RepX, List<double> Xs)>();
        foreach (var x in pool)
        {
            if (clusters.Count > 0 && x - clusters[^1].RepX <= 9) { clusters[^1].Xs.Add(x); clusters[^1] = (Math.Min(clusters[^1].RepX, x), clusters[^1].Xs); }
            else clusters.Add((x, new List<double> { x }));
        }
        var reps = clusters.Select(c => c.Xs.Sum() / c.Xs.Count).ToList();
        var bounds = new List<double>(); for (int i = 0; i < reps.Count - 1; i++) bounds.Add((reps[i] + reps[i + 1]) / 2);
        int ColIndexFor(double x) { int idx = 0; while (idx < bounds.Count && x >= bounds[idx]) idx++; return Math.Min(idx, reps.Count - 1); }
        var buckets = reps.Select(_ => new List<Tok>()).ToList();
        foreach (var tok in headerTokens) buckets[ColIndexFor(CX(tok))].Add(tok);
        var headers = buckets.Select(b => string.Join(" ", Js.Sort(b, YX).Select(t => t.Text.Trim()).Where(x => x.Length > 0))).ToList();
        var rows = matched.Select(block =>
        {
            var row = Enumerable.Repeat("", reps.Count).ToArray();
            foreach (var line in block) foreach (var tok in line)
                {
                    var val = tok.Text.Trim(); if (val.Length == 0) continue;
                    var ci = ColIndexFor(CX(tok));
                    if (row[ci].Length == 0) row[ci] = val; else if (row[ci] != val) row[ci] = row[ci] + " " + val;
                }
            return row;
        }).ToList();
        var keep = headers.Select((h, i) => h.Length > 0 || rows.Any(r => r[i].Length > 0)).ToList();
        var outH = headers.Where((h, i) => keep[i]).ToList();
        var outR = rows.Select(r => r.Where((v, i) => keep[i]).ToArray()).ToList();
        if (blockDates.Any(d => d.Length > 0) && !outH.Any(h => KhangSinhCore.NormText(h).StartsWith("ngay", StringComparison.Ordinal)))
        {
            outH.Insert(0, "Ngày nhập NL");
            outR = outR.Select((r, i) => new[] { blockDates[i] }.Concat(r).ToArray()).ToList();
        }
        var keepIdx = outH.Select((h, i) => (h, i)).Where(x => !DropHeaders.Any(p => TightNorm(x.h).Contains(p))).Select(x => x.i).ToList();
        if (keepIdx.Count == outH.Count) return new PdfTable(outH, outR);
        return new PdfTable(keepIdx.Select(i => outH[i]).ToList(), outR.Select(r => keepIdx.Select(i => r[i]).ToArray()).ToList());
    }

    /// <summary>Gộp bảng của nhiều file PDF theo tên cột (mergeExtractedPdfTables).</summary>
    public static PdfTable Merge(IReadOnlyList<PdfTable> tables)
    {
        var hs = new List<string>(); var norm = new List<string>();
        var maps = tables.Select(t => t.Headers.Select(h => { var n = TightNorm(h); var i = norm.IndexOf(n); if (i == -1) { hs.Add(h); norm.Add(n); i = hs.Count - 1; } return i; }).ToList()).ToList();
        var rows = new List<string[]>();
        for (int ti = 0; ti < tables.Count; ti++)
            foreach (var r in tables[ti].Rows)
            {
                var nr = Enumerable.Repeat("", hs.Count).ToArray();
                for (int ci = 0; ci < r.Length && ci < maps[ti].Count; ci++) nr[maps[ti][ci]] = r[ci];
                rows.Add(nr);
            }
        return new PdfTable(hs, rows);
    }
}
