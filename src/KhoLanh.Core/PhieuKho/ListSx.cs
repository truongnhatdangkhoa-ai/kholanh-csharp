using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using KhoLanh.Core.KhangSinh;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.PhieuKho;

public sealed class ListSize
{
    public string Size { get; set; } = "";
    public string CodeMaHoa { get; set; } = "";
    public double X { get; set; }
    /// <summary>Tên hàng / mã hàng / mã lô gán sẵn cho size (người dùng điền hoặc lấy từ tồn kho).</summary>
    public string Ten { get; set; } = "";
    public string Ma { get; set; } = "";
    public string Lo { get; set; } = "";
    public bool? NoSize { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class ListRow
{
    public string Date { get; set; } = "";
    public string Iso { get; set; } = "";
    public string Exp { get; set; } = "";
    public string TruyXuat { get; set; } = "";
    public Dictionary<string, double> Qty { get; set; } = new();
    public double? Total { get; set; }
    public bool Mks { get; set; }
    public List<string>? MksSizes { get; set; }
    public Dictionary<string, double>? MksQty { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Kết quả đọc "List sản xuất" của khách (PDF hoặc Excel): các dòng DATE × SIZE × số kiện.</summary>
public sealed class ListData
{
    public bool Ok { get; set; }
    public string Layout { get; set; } = "";     // "" = bảng ngày × size (CGC, MATSUOKA) · "product" = sản phẩm theo cột (ISHIMITSU…)
    public List<ListSize> Sizes { get; set; } = new();
    public List<ListRow> Rows { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string Lsx { get; set; } = "";
    public string HopDong { get; set; } = "";
    public string NhaSanXuat { get; set; } = "";
    public string TieuDe { get; set; } = "";
    public string Khach { get; set; } = "";
    public string DonVi { get; set; } = "";
    public string Packing { get; set; } = "";
    public Dictionary<string, string> PackingBy { get; set; } = new();
    public Dictionary<string, double> NetPdf { get; set; } = new();
    public Dictionary<string, double> Tong { get; set; } = new();
    public Dictionary<string, double> CoCau { get; set; } = new();
    public Dictionary<string, double>? TongPdf { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// ĐỌC "LIST SẢN XUẤT" CỦA KHÁCH (port từ pnx-listsx.js). Nhận các mục chữ có toạ độ (x, y) — từ PDF (PdfText) hoặc từ ô Excel — nên cột size được gán theo
/// TOẠ ĐỘ X, không theo thứ tự. Điều gì không đọc chắc chắn thì BÁO trong Warnings, không đoán. Hai bố cục: bảng ngày × size (CGC, MATSUOKA) và sản phẩm theo cột
/// với từng lô = 1 cặp hàng THƯỜNG / MKS (ISHIMITSU, NISHIMOTO…). Bố cục khác vẫn có thể lỗi — gửi mẫu để thêm quy tắc.
/// </summary>
public static class ListSx
{
    public sealed record Item(string S, double X, double Y);
    private sealed class Line { public double Y; public List<Item> Items = new(); }

    private static readonly Regex SizeRx = new(@"^(\d{2}/\d{2}|\d{1,2}\s?PCS)$", RegexOptions.IgnoreCase), DateRx = new(@"^(\d{4})\.(\d{2})\.(\d{2})$"), NumRx = new(@"^\d{1,5}$"), CodeRx = new(@"^[A-Z][A-Z0-9]{17,23}$");
    private static string SizeKey(string s) => Regex.Replace(s.ToUpperInvariant(), @"\s+", "");
    private static string Dmy(Match m) => $"{m.Groups[3].Value}.{m.Groups[2].Value}.{m.Groups[1].Value}";
    private static double D(string s) => double.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture);
    private static string Join(Line l) => string.Join(" ", l.Items.Select(i => i.S));

    private static List<Line> Lines(IEnumerable<Item> items)
    {
        var s = items.Where(i => i.S.Trim().Length > 0).Select(i => i with { S = i.S.Trim() }).OrderByDescending(i => i.Y).ThenBy(i => i.X).ToList();
        var outL = new List<Line>();
        foreach (var it in s)
        {
            var l = outL.FirstOrDefault(L => Math.Abs(L.Y - it.Y) <= 2);
            if (l is not null) l.Items.Add(it); else outL.Add(new Line { Y = it.Y, Items = { it } });
        }
        foreach (var l in outL) l.Items = l.Items.OrderBy(i => i.X).ToList();
        return outL.OrderByDescending(l => l.Y).ToList();
    }

    private static void Common(ListData res, string text, bool product)
    {
        var lsx = Regex.Match(text, @"LSX\s*([0-9]{2}\.[0-9]{2}\.[0-9]{3})", RegexOptions.IgnoreCase); res.Lsx = lsx.Success ? lsx.Groups[1].Value : "";
        var hd = Regex.Match(text, @"Contract\s*No\.?\s*:?\s*([^\s(]+)", RegexOptions.IgnoreCase);
        if (!hd.Success) hd = Regex.Match(text, @"(\d+/\d{4}[\w-]*)\s*-\s*LSX", RegexOptions.IgnoreCase);
        res.HopDong = hd.Success ? hd.Groups[1].Value : "";
        var nx = Regex.Match(text, @"(AN AN|THUẬN PHƯỚC)\s+SẢN XUẤT", RegexOptions.IgnoreCase); res.NhaSanXuat = nx.Success ? nx.Groups[1].Value.ToUpperInvariant() : "";
        var tt = Regex.Match(text, product ? @"LIST SẢN XUẤT\s+(.+?)\s+CỦA\s+(?:KHÁCH\s+)?(.+?)(?:\s+CODE\b|\n|$)" : @"LIST SẢN XUẤT\s+(.+?)\s+CỦA\s+(?:KHÁCH\s+)?(\S+)", RegexOptions.IgnoreCase);
        res.TieuDe = tt.Success ? tt.Groups[1].Value : ""; res.Khach = tt.Success ? tt.Groups[2].Value.Trim() : "";
        res.DonVi = Regex.IsMatch(text, "BDL", RegexOptions.IgnoreCase) ? "bdl" : Regex.IsMatch(text, product ? "KIỆN|CARTON" : "KIỆN", RegexOptions.IgnoreCase) ? "kiện" : "";
    }

    // ================================================================== bố cục "bảng ngày × size"

    public static ListData ParseMatrix(IEnumerable<Item> items)
    {
        var L = Lines(items); var warn = new List<string>(); var res = new ListData { Warnings = warn };
        var text = string.Join("\n", L.Select(Join));
        var dateLines = L.Where(l => l.Items.Any(i => DateRx.IsMatch(i.S))).ToList();
        if (dateLines.Count == 0) { warn.Add("Không thấy dòng ngày sản xuất (dạng 2026.05.29) — đây có phải List sản xuất không?"); return res; }
        double firstDateY = dateLines.Max(l => l.Y), lastDateY = dateLines.Min(l => l.Y);
        List<Item>? best = null;
        foreach (var l in L)
        {
            if (l.Y <= firstDateY) continue;
            var z = l.Items.Where(i => SizeRx.IsMatch(i.S)).ToList();
            if (z.Count > 0 && (best is null || z.Count > best.Count)) best = z;
        }
        if (best is null) { warn.Add("Không thấy tiêu đề size (VD 51/60 hoặc 4PCS) trên bảng ngày."); return res; }
        var hdr = best.Select(i => (Size: Regex.Replace(i.S, @"\s+", ""), X: i.X)).OrderBy(h => h.X).ToList();
        res.Sizes = hdr.Select(h => new ListSize { Size = h.Size, X = h.X }).ToList();
        var lastX = hdr[^1].X; var mid = hdr.Skip(1).Select((h, i) => (hdr[i].X + h.X) / 2).ToList();
        int ColOf(double x) { if (x - lastX > 50) return -1; var k = 0; foreach (var m in mid) if (x > m) k++; return k; }
        string NearSize(double x) { var b = 0; for (var i = 0; i < hdr.Count; i++) if (Math.Abs(hdr[i].X - x) < Math.Abs(hdr[b].X - x)) b = i; return hdr[b].Size; }
        // code mã hoá của khách → size
        foreach (var l in L)
        {
            if (l.Y <= firstDateY) continue;
            var code = l.Items.FirstOrDefault(i => CodeRx.IsMatch(i.S)); if (code is null) continue;
            var lbl = Regex.Replace(string.Join(" ", l.Items.Select(i => i.S)).ToUpperInvariant(), @"\s+", "");
            foreach (var z in res.Sizes)
            {
                var k = SizeKey(z.Size); var at = lbl.IndexOf(k, StringComparison.Ordinal);
                if (at >= 0 && at < lbl.IndexOf(code.S, StringComparison.Ordinal)) z.CodeMaHoa = code.S;
                else if (Regex.IsMatch(z.Size, @"^\d{2}/\d{2}$") && code.S.Contains(z.Size.Replace("/", ""))) z.CodeMaHoa = code.S;
            }
        }
        Common(res, text, false);
        var pk = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*KG\s*NET\s*/\s*TÚI\s*[xX]\s*(\d+)\s*TÚI\s*/\s*KIỆN(?:\s*[xX]\s*(\d+)\s*/\s*BDL)?", RegexOptions.IgnoreCase);
        if (pk.Success)
        {
            var kg = D(pk.Groups[1].Value);
            res.Packing = (kg == Math.Floor(kg) ? kg.ToString("0.0", CultureInfo.InvariantCulture) : kg.ToString("0.###", CultureInfo.InvariantCulture)) + " kg x " + pk.Groups[2].Value + (pk.Groups[3].Success ? " x " + pk.Groups[3].Value : "");
        }
        foreach (var l in L)
        {
            if (l.Y <= firstDateY) continue;
            foreach (var i in l.Items)
            {
                var m = Regex.Match(i.S, @"([\d.,]+)\s*KG\s*\(\s*(\d+)\s*G\s*[xX]\s*(\d+)\s*\)", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var sz = NearSize(i.X); res.PackingBy[sz] = m.Groups[2].Value + "g x " + m.Groups[3].Value; res.NetPdf[sz] = D(m.Groups[1].Value);
            }
        }
        foreach (var l in dateLines)
        {
            var ds = l.Items.Where(i => DateRx.IsMatch(i.S)).ToList(); if (ds.Count == 0) continue;
            var m = DateRx.Match(ds[0].S);
            var row = new ListRow { Date = Dmy(m), Iso = $"{m.Groups[1].Value}-{m.Groups[2].Value}-{m.Groups[3].Value}", Exp = ds.Count > 1 ? Dmy(DateRx.Match(ds[1].S)) : "", TruyXuat = l.Items.FirstOrDefault(i => Regex.IsMatch(i.S, @"^\d{9,13}$"))?.S ?? "" };
            foreach (var i in l.Items)
                if (NumRx.IsMatch(i.S) && i.X > hdr[0].X - 30)
                {
                    var c = ColOf(i.X);
                    if (c < 0) row.Total = double.Parse(i.S); else row.Qty[hdr[c].Size] = row.Qty.GetValueOrDefault(hdr[c].Size) + double.Parse(i.S);
                }
            var near = L.Where(o => Math.Abs(o.Y - l.Y) <= 4 && (o == l || !o.Items.Any(i => DateRx.IsMatch(i.S)))).ToList();
            foreach (var o in near) foreach (var i in o.Items)
            {
                var mk = Regex.Match(i.S, @"^MKS\b\s*(.*)$", RegexOptions.IgnoreCase); if (!mk.Success) continue;
                row.Mks = true;
                var key = mk.Groups[1].Value;
                var sz = key.Length > 0 ? hdr.Where(h => SizeKey(h.Size) == SizeKey(key)).Select(h => h.Size).FirstOrDefault() : null;
                if (sz is not null) (row.MksSizes ??= new()).Add(sz);
                else if (key.Length > 0) warn.Add($"Ngày {row.Date}: không hiểu dấu \"{i.S}\" — kiểm tra MKS.");
            }
            var sum = row.Qty.Values.Sum();
            if (row.Total is { } t && t != sum) warn.Add($"Ngày {row.Date}: cộng các size = {sum:0.###} nhưng cột TỔNG ghi {t:0.###} — kiểm tra lại PDF.");
            if (row.Qty.Count == 0) warn.Add($"Ngày {row.Date}: không đọc được số kiện.");
            res.Rows.Add(row);
        }
        foreach (var h in hdr) res.Tong[h.Size] = res.Rows.Sum(r => r.Qty.GetValueOrDefault(h.Size));
        foreach (var l in L)
        {
            if (l.Y >= lastDateY) continue;
            var head = Join(l);
            Dictionary<string, double>? Pick()
            {
                var o = new Dictionary<string, double>();
                foreach (var i in l.Items) if (NumRx.IsMatch(i.S) && i.X > hdr[0].X - 30) { var c = ColOf(i.X); if (c >= 0) o[hdr[c].Size] = double.Parse(i.S); }
                return o.Count > 0 ? o : null;
            }
            if (Regex.IsMatch(head, @"^TỔNG\b", RegexOptions.IgnoreCase) && res.CoCau.Count == 0)
            {
                var o = Pick();
                if (o is not null && res.TongPdf is null)
                {
                    res.TongPdf = o;
                    foreach (var h in hdr) if (o.TryGetValue(h.Size, out var v) && v != res.Tong[h.Size]) warn.Add($"Size {h.Size}: các dòng ngày cộng {res.Tong[h.Size]:0.###} nhưng dòng TỔNG ghi {v:0.###}.");
                }
            }
            if (Regex.IsMatch(head, "CƠ CẤU HỢP ĐỒNG", RegexOptions.IgnoreCase)) { var o = Pick(); if (o is not null) res.CoCau = o; }
        }
        res.Ok = res.Rows.Count > 0;
        return res;
    }

    // ================================================================== bố cục "sản phẩm theo cột"

    private static readonly Regex DateAny = new(@"^(?:(\d{4})[./-](\d{2})[./-](\d{2})|(\d{1,2})[./-](\d{2})[./-](\d{4}))$");
    private static readonly Regex SizeAny = new(@"^(\d{1,2}L|\d{2,3}\s?G|\d{2}/\d{2}|\d{1,2}\s?PCS|\d+\s?[+±]-?\s?\d+\s?G|\d+(?:[.,]\d+)?\s?KG)$", RegexOptions.IgnoreCase);
    private static readonly Regex Qual = new(@"^\(\s*(?:\d+\s*/\s*\d+|\d+(?:[.,]\d+)?\s*(?:G|KG))\s*\)$", RegexOptions.IgnoreCase);
    private sealed record DateEnt(string Date, string Iso, string Ddmm, double Y, string ExpD);
    private static (string Date, string Iso, string Ddmm)? DateOf(string s)
    {
        var m = DateAny.Match(s); if (!m.Success) return null;
        var y = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[6].Value; var mo = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[5].Value;
        var dd = (m.Groups[3].Success ? m.Groups[3].Value : m.Groups[4].Value).PadLeft(2, '0');
        return ($"{dd}.{mo}.{y}", $"{y}-{mo}-{dd}", dd + mo);
    }

    private sealed class Lot { public (Line L, Item Lb)? Th, Mk; public double Hi, Lo, Mid; public string Code = ""; public DateEnt? D; }

    public static ListData? ParseProduct(IEnumerable<Item> items)
    {
        var L = Lines(items); var warn = new List<string>();
        var sizeLine = L.FirstOrDefault(l => l.Items.Any(i => Regex.IsMatch(i.S, "^SIZE$", RegexOptions.IgnoreCase)) && l.Items.Count >= 2);
        if (sizeLine is null) return null;
        var qtyLines = L.Where(l => l.Y < sizeLine.Y && l.Items.Any(i => Regex.IsMatch(i.S, "^(THƯỜNG|MKS)$", RegexOptions.IgnoreCase) && l.Items.Any(q => NumRx.IsMatch(q.S) && q.X > i.X))).ToList();
        if (qtyLines.Count == 0) return null;
        var res = new ListData { Warnings = warn, Layout = "product" };
        var lab = sizeLine.Items.First(i => Regex.IsMatch(i.S, "^SIZE$", RegexOptions.IgnoreCase));
        var hdr = sizeLine.Items.Where(i => i != lab && i.X > lab.X).Select(i => (Size: Regex.Replace(i.S, @"\s+", ""), X: i.X)).ToList();
        var band = L.Where(l => l != sizeLine && Math.Abs(l.Y - sizeLine.Y) <= 12).ToList();
        foreach (var l in band) foreach (var i in l.Items)
            if (i.X > lab.X && SizeAny.IsMatch(i.S) && !hdr.Any(h => Math.Abs(h.X - i.X) <= 8)) hdr.Add((Regex.Replace(i.S, @"\s+", ""), i.X));
        foreach (var l in band) foreach (var i in l.Items)
        {
            if (!Qual.IsMatch(i.S)) continue;
            var bi = -1;
            for (var k = 0; k < hdr.Count; k++) if (Math.Abs(hdr[k].X - i.X) <= 15 && (bi < 0 || Math.Abs(hdr[k].X - i.X) < Math.Abs(hdr[bi].X - i.X))) bi = k;
            if (bi >= 0 && !hdr[bi].Size.Contains('(')) hdr[bi] = (hdr[bi].Size + " " + Regex.Replace(i.S, @"\s+", ""), hdr[bi].X);
        }
        hdr = hdr.OrderBy(h => h.X).ToList();
        if (hdr.Count == 0) { warn.Add("Có chữ SIZE nhưng không thấy giá trị size (VD 200G, 3L) bên phải."); return res; }
        var seen = new Dictionary<string, int>();
        for (var k = 0; k < hdr.Count; k++)
        {
            var sz = hdr[k].Size; seen[sz] = seen.GetValueOrDefault(sz) + 1;
            if (seen[sz] > 1) { warn.Add($"Size \"{sz}\" xuất hiện nhiều lần — đã đánh số #{seen[sz]}."); hdr[k] = (sz + "#" + seen[sz], hdr[k].X); }
        }
        res.Sizes = hdr.Select(h => new ListSize { Size = h.Size, X = h.X }).ToList();
        int S = hdr.Count; var lastX = hdr[S - 1].X;
        var gaps = hdr.Skip(1).Select((h, i) => h.X - hdr[i].X).OrderBy(g => g).ToList();
        var thr = S > 1 ? Math.Max(25, 0.8 * gaps[gaps.Count / 2]) : 40;
        int Nearest(double x) { var b = 0; for (var k = 0; k < S; k++) if (Math.Abs(hdr[k].X - x) < Math.Abs(hdr[b].X - x)) b = k; return b; }
        var text = string.Join("\n", L.Select(Join));
        Common(res, text, true);

        (Dictionary<string, double> Q, double? Total, bool Ok) ReadQty(Line l, Item lb)
        {
            var nums = l.Items.Where(i => NumRx.IsMatch(i.S) && i.X > lb.X).OrderBy(i => i.X).ToList(); double? total = null; var body = nums; var ok = nums.Count > 0 && nums.Count <= S + 1;
            if (nums.Count == S + 1) { total = double.Parse(nums[S].S); body = nums.Take(S).ToList(); }
            else if (nums.Count > 0 && nums[^1].X - lastX > thr) { total = double.Parse(nums[^1].S); body = nums.Take(nums.Count - 1).ToList(); }
            var q = new Dictionary<string, double>();
            if (body.Count == S) for (var k = 0; k < S; k++) q[hdr[k].Size] = double.Parse(body[k].S);
            else { var used = new HashSet<int>(); foreach (var n in body) { var b = Nearest(n.X); if (!used.Add(b)) ok = false; q[hdr[b].Size] = double.Parse(n.S); } }
            return (q, total, ok);
        }

        double topY = qtyLines.Max(l => l.Y), botY = qtyLines.Min(l => l.Y);
        var dEnt = new List<DateEnt>();
        foreach (var l in L)
        {
            if (l.Y >= sizeLine.Y || l.Y < botY - 4) continue;
            var ds = l.Items.Where(i => DateOf(i.S) is not null).ToList();
            if (ds.Count > 0) { var d0 = DateOf(ds[0].S)!.Value; dEnt.Add(new DateEnt(d0.Date, d0.Iso, d0.Ddmm, l.Y, ds.Count > 1 ? DateOf(ds[1].S)!.Value.Date : "")); }
        }
        if (dEnt.Count == 0) { warn.Add("Không thấy ngày sản xuất (dạng 2026.09.23 hoặc 23.09.2026)."); return res; }

        var lots = new List<Lot>();
        foreach (var l in qtyLines)
        {
            var lb = l.Items.First(i => Regex.IsMatch(i.S, "^(THƯỜNG|MKS)$", RegexOptions.IgnoreCase)); var mk = Regex.IsMatch(lb.S, "^MKS$", RegexOptions.IgnoreCase); var cur = lots.LastOrDefault();
            if (mk && cur is not null && cur.Mk is null) { cur.Mk = (l, lb); cur.Lo = l.Y; }
            else lots.Add(mk ? new Lot { Mk = (l, lb), Hi = l.Y, Lo = l.Y } : new Lot { Th = (l, lb), Hi = l.Y, Lo = l.Y });
        }
        var codes = new List<(string S, double Y, bool[] Used)>();
        foreach (var l in L) foreach (var i in l.Items) if (Regex.IsMatch(i.S, @"^\d{9,13}$") && l.Y < sizeLine.Y && l.Y <= topY + 6 && l.Y >= botY - 6) codes.Add((i.S, l.Y, new[] { false }));
        foreach (var lot in lots)
        {
            lot.Mid = (lot.Hi + lot.Lo) / 2; int b = -1;
            for (var k = 0; k < codes.Count; k++) if (!codes[k].Used[0] && codes[k].Y <= lot.Hi + 6 && codes[k].Y >= lot.Lo - 6 && (b < 0 || Math.Abs(codes[k].Y - lot.Mid) < Math.Abs(codes[b].Y - lot.Mid))) b = k;
            if (b >= 0) { codes[b].Used[0] = true; lot.Code = codes[b].S; }
        }
        var sortedD = dEnt.OrderByDescending(d => d.Y).ToList(); var guessed = 0;
        foreach (var lot in lots)
        {
            if (dEnt.Count == 1) { lot.D = dEnt[0]; continue; }
            var byCode = lot.Code.Length >= 4 ? dEnt.Where(d => lot.Code[..4] == d.Ddmm).ToList() : new();
            if (byCode.Count == 1) { lot.D = byCode[0]; continue; }
            var k = 0; for (var i = 0; i < sortedD.Count; i++) if (i > 0 && lot.Mid <= (sortedD[i - 1].Y + sortedD[i].Y) / 2) k = i;
            lot.D = sortedD[k]; guessed++;
        }
        if (guessed > 0) warn.Add($"{guessed} lô không có mã truy xuất khớp ngày — ngày được suy theo vị trí trên bảng, kiểm tra lại.");
        var sums = new Dictionary<string, double>();
        foreach (var lot in lots)
        {
            var qty = new Dictionary<string, double>(); var mq = new Dictionary<string, double>();
            foreach (var part in new[] { lot.Th, lot.Mk })
            {
                if (part is null) continue;
                var (pl, plb) = part.Value; var r = ReadQty(pl, plb); var nm = plb.S + (lot.Code.Length > 0 ? $" (lô {lot.Code})" : "");
                if (!r.Ok) { warn.Add($"Hàng {nm} ngày {lot.D!.Date}: không đọc chắc chắn số kiện ({Join(pl)}) — kiểm tra lại."); continue; }
                var sum = r.Q.Values.Sum();
                if (r.Total is { } t && t != sum) warn.Add($"Hàng {nm}: cộng các cột = {sum:0.###} nhưng cột TỔNG ghi {t:0.###}.");
                var isMk = lot.Mk is { } mkp && ReferenceEquals(mkp.L, pl);
                foreach (var (k, v) in r.Q) { qty[k] = qty.GetValueOrDefault(k) + v; sums[k] = sums.GetValueOrDefault(k) + v; if (isMk) mq[k] = mq.GetValueOrDefault(k) + v; }
            }
            var tot = qty.Values.Sum(); if (tot == 0) continue;
            res.Rows.Add(new ListRow { Date = lot.D!.Date, Iso = lot.D.Iso, Exp = lot.D.ExpD, TruyXuat = lot.Code, Qty = qty, Total = tot, Mks = mq.Values.Any(v => v > 0), MksQty = mq });
        }
        var below = L.Where(l => l.Y < botY).ToList();
        Dictionary<string, double>? PickRow(Regex re)
        {
            foreach (var l in below) { var lb = l.Items.FirstOrDefault(i => re.IsMatch(i.S)); if (lb is not null) { var r = ReadQty(l, lb); if (r.Ok) return r.Q; } }
            return null;
        }
        foreach (var h in hdr) res.Tong[h.Size] = sums.GetValueOrDefault(h.Size);
        var tp = PickRow(new Regex(@"^TỔNG(\s*CARTON)?$", RegexOptions.IgnoreCase));
        if (tp is not null) { res.TongPdf = tp; foreach (var h in hdr) if (tp.TryGetValue(h.Size, out var v) && v != res.Tong[h.Size]) warn.Add($"Size {h.Size}: các lô cộng {res.Tong[h.Size]:0.###} nhưng hàng TỔNG ghi {v:0.###}."); }
        var cc = PickRow(new Regex(@"^CƠ CẤU", RegexOptions.IgnoreCase)); if (cc is not null) res.CoCau = cc;

        // code mã hoá của khách
        var cds = new List<string>();
        foreach (var l in L) foreach (var i in l.Items) if (CodeRx.IsMatch(i.S) && l.Y < sizeLine.Y && l.Y > botY) cds.Add(i.S);
        var hit = 0;
        static string Norm(string t) => Regex.Replace(t.ToUpperInvariant(), @"\s+", "");
        foreach (var l in L)
        {
            if (l.Y >= sizeLine.Y || l.Y <= botY) continue;
            foreach (var i in l.Items)
            {
                if (!CodeRx.IsMatch(i.S)) continue;
                Item? lb = null;
                foreach (var o in l.Items) if (o.X < i.X && Regex.IsMatch(o.S, @"^(\d{1,2}L|\d{2,3}\s?G)$", RegexOptions.IgnoreCase) && (lb is null || o.X > lb.X)) lb = o;
                if (lb is null) continue;
                var key = Norm(lb.S);
                var cand = res.Sizes.Where(z => z.CodeMaHoa.Length == 0 && (Norm(Regex.Replace(z.Size, @"#\d+$", "")) == key || (Regex.Match(z.Size, @"\(([^)]*)\)") is { Success: true } pm && Norm(pm.Groups[1].Value) == key))).ToList();
                if (cand.Count == 1) { cand[0].CodeMaHoa = i.S; hit++; }
            }
        }
        foreach (var z in res.Sizes)
        {
            if (z.CodeMaHoa.Length > 0) continue;
            var q = Regex.Match(z.Size, @"\((\d+)/(\d+)\)"); var bas = Regex.Replace(Regex.Replace(z.Size, @"\s*\(.*$", ""), @"#\d+$", "");
            var c = cds.Where(s => q.Success ? s.Contains(q.Groups[1].Value + q.Groups[2].Value) : Regex.IsMatch(bas, @"^\d{1,2}L$", RegexOptions.IgnoreCase) && s.ToUpperInvariant().Contains("0" + bas.ToUpperInvariant())).ToList();
            if (c.Count == 1) { z.CodeMaHoa = c[0]; hit++; }
        }
        if (hit == 0 && cds.Count == S) for (var k = 0; k < S; k++) res.Sizes[k].CodeMaHoa = cds[k];

        // quy cách: bảng NET/KHAY + NET/THÙNG (đọc thẳng) rồi mục ĐÓNG GÓI dạng chữ (suy, có nhắc kiểm tra)
        Dictionary<string, string>? RowVals(Regex re)
        {
            var l = L.FirstOrDefault(x => x.Y < sizeLine.Y && x.Items.Any(i => re.IsMatch(i.S))); if (l is null) return null;
            var lb = l.Items.First(i => re.IsMatch(i.S)); var o = new Dictionary<string, string>();
            foreach (var i in l.Items.Where(i => i.X > lb.X)) o[hdr[Nearest(i.X)].Size] = i.S;
            return o;
        }
        var khay = RowVals(new Regex(@"NET\s*/\s*KHAY", RegexOptions.IgnoreCase)); var thung = RowVals(new Regex(@"NET\s*/\s*THÙNG", RegexOptions.IgnoreCase));
        if (thung is not null)
            foreach (var h in hdr)
            {
                var m = Regex.Match((thung.GetValueOrDefault(h.Size) ?? "").Trim(), @"^(\d+(?:[.,]\d+)?)\s*G\s*[xX]\s*(\d+)$", RegexOptions.IgnoreCase); if (!m.Success) continue;
                double gg = D(m.Groups[1].Value), n = double.Parse(m.Groups[2].Value), kg = gg * n / 1000;
                res.PackingBy[h.Size] = Num(gg) + "g x " + Num(n); res.NetPdf[h.Size] = Math.Round(kg * 1000) / 1000;
                var g2 = Regex.Match((khay?.GetValueOrDefault(h.Size) ?? "").Trim(), @"^(\d+(?:[.,]\d+)?)\s*G$", RegexOptions.IgnoreCase);
                if (g2.Success && D(g2.Groups[1].Value) != gg) warn.Add($"Size {h.Size}: NET/KHAY {g2.Value} khác NET/THÙNG {thung[h.Size]} — kiểm tra quy cách.");
            }
        if (khay is not null && thung is not null)
            foreach (var h in hdr)
            {
                if (res.PackingBy.ContainsKey(h.Size)) continue;
                var g = Regex.Match((khay.GetValueOrDefault(h.Size) ?? "").Trim(), @"^(\d+(?:[.,]\d+)?)\s*G$", RegexOptions.IgnoreCase);
                var k = Regex.Match((thung.GetValueOrDefault(h.Size) ?? "").Trim(), @"^(\d+(?:[.,]\d+)?)\s*KG$", RegexOptions.IgnoreCase);
                if (!g.Success || !k.Success) continue;
                double gg = D(g.Groups[1].Value), kg = D(k.Groups[1].Value), n = kg * 1000 / gg;
                if (Math.Abs(n - Math.Round(n)) < 1e-6) { res.PackingBy[h.Size] = Num(gg) + "g x " + Num(Math.Round(n)); res.NetPdf[h.Size] = kg; }
                else warn.Add($"Size {h.Size}: NET/KHAY {g.Value} và NET/THÙNG {k.Value} không chia hết — nhập Packing tay.");
            }
        var flat = Regex.Replace(text, @"\s+", " "); var inferred = new List<string>();
        var a = Regex.Match(flat, @"(\d+)\s*(?:\(\s*[+\-±]+\s*\d+\s*\))?\s*CON\s*/\s*KHAY\s*[xX]\s*(\d+)\s*KHAY\s*/\s*TÚI(?:\s*PE)?\s*[xX]\s*(\d+)\s*TÚI\s*/\s*KIỆN", RegexOptions.IgnoreCase);
        if (a.Success) foreach (var z in res.Sizes) { var g = Regex.Match(z.Size, @"^(\d+)G$", RegexOptions.IgnoreCase); if (g.Success && !res.PackingBy.ContainsKey(z.Size)) { res.PackingBy[z.Size] = g.Groups[1].Value + "g x " + (double.Parse(a.Groups[2].Value) * double.Parse(a.Groups[3].Value)).ToString("0", CultureInfo.InvariantCulture); inferred.Add(z.Size); } }
        var b2 = Regex.Match(flat, @"(\d+(?:[.,]\d+)?)\s*KG\s*/\s*TÚI\s*[xX]\s*(\d+)\s*/\s*KIỆN", RegexOptions.IgnoreCase);
        if (b2.Success) foreach (var z in res.Sizes) { var k = Regex.Match(z.Size, @"^(\d+(?:[.,]\d+)?)KG$", RegexOptions.IgnoreCase); if (k.Success && !res.PackingBy.ContainsKey(z.Size) && D(k.Groups[1].Value) == D(b2.Groups[1].Value)) { res.PackingBy[z.Size] = D(b2.Groups[1].Value).ToString("0.0", CultureInfo.InvariantCulture) + " kg x " + b2.Groups[2].Value; inferred.Add(z.Size); } }
        if (inferred.Count > 0) warn.Add("Quy cách (Packing) tự suy từ mục ĐÓNG GÓI: " + string.Join(" · ", inferred.Select(k => k + " = " + res.PackingBy[k])) + " — kiểm tra lại.");
        res.Ok = res.Rows.Count > 0; if (!res.Ok) warn.Add("Không đọc được số kiện ở hàng THƯỜNG / MKS.");
        return res;
    }
    private static string Num(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Thử bố cục "bảng ngày × size" trước; không ra thì thử "sản phẩm theo cột".</summary>
    public static ListData Parse(IEnumerable<Item> items)
    {
        var list = items.ToList();
        var a = ParseMatrix(list); if (a.Ok) return a;
        var b = ParseProduct(list); if (b is { Ok: true }) return b;
        return b is not null && b.Warnings.Count > 0 ? b : a;
    }

    // ================================================================== nguồn: PDF / Excel

    public static ListData ReadPdf(byte[] pdf)
    {
        List<PdfText.Page> pages;
        try { pages = PdfText.Read(pdf); } catch (Exception e) { throw new BusinessException("Không đọc được file PDF: " + e.Message); }
        var items = new List<Item>();
        foreach (var p in pages) foreach (var t in p.Items) items.Add(new Item(t.Text, Math.Round(t.X), Math.Round(t.Y) - (p.No - 1) * 2000));
        var r = Parse(items);
        if (r.Rows.Count == 0 && r.Warnings.Count == 0) r.Warnings.Add("PDF không có chữ đọc được (có thể là ảnh scan) — nạp bản Excel hoặc PDF dạng chữ.");
        return r;
    }

    /// <summary>Ô Excel → mục chữ (cột × 100, hàng × −10; ô gộp lấy TÂM) rồi dùng lại <see cref="Parse"/>. Thử lần lượt từng sheet, lấy sheet đọc được.</summary>
    public static ListData ReadXlsx(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(fs);
            ListData? first = null;
            foreach (var ws in wb.Worksheets)
            {
                var r = Parse(GridItems(ws));
                if (r.Ok) return r;
                first ??= r;
            }
            return first ?? new ListData { Warnings = { "File Excel không có sheet nào có dữ liệu." } };
        }
        catch (IOException e) { throw new BusinessException("Không mở được file (có thể đang mở trong Excel): " + e.Message); }
        catch (Exception e) when (e is not BusinessException) { throw new BusinessException("Không đọc được file Excel: " + e.Message); }
    }

    internal static List<Item> GridItems(IXLWorksheet ws)
    {
        var res = new List<Item>(); var used = ws.RangeUsed(); if (used is null) return res;
        var mergeAt = new Dictionary<(int, int), IXLRange>();
        foreach (var m in ws.MergedRanges) mergeAt[(m.FirstRow().RowNumber() - 1, m.FirstColumn().ColumnNumber() - 1)] = m;
        for (var r = used.FirstRow().RowNumber(); r <= used.LastRow().RowNumber(); r++)
            for (var c = used.FirstColumn().ColumnNumber(); c <= used.LastColumn().ColumnNumber(); c++)
            {
                var cell = ws.Cell(r, c); if (cell.IsEmpty()) continue;
                string s = cell.DataType switch
                {
                    XLDataType.DateTime => cell.GetDateTime().ToString("yyyy.MM.dd"),
                    XLDataType.Number => cell.GetDouble() is var v && Math.Abs(v - Math.Round(v)) < 1e-9 ? ((long)Math.Round(v)).ToString(CultureInfo.InvariantCulture) : cell.GetDouble().ToString("0.############", CultureInfo.InvariantCulture),
                    _ => cell.GetFormattedString(),
                };
                s = Regex.Replace(s, @"\s+", " ").Trim(); if (s.Length == 0) continue;
                var dm = Regex.Match(s, @"^(\d{1,2})[./\-](\d{1,2})[./\-](\d{4})$");
                if (dm.Success) s = $"{dm.Groups[3].Value}.{dm.Groups[2].Value.PadLeft(2, '0')}.{dm.Groups[1].Value.PadLeft(2, '0')}";
                double c2 = c - 1, r2 = r - 1;
                if (mergeAt.TryGetValue((r - 1, c - 1), out var mg)) { c2 = (mg.FirstColumn().ColumnNumber() - 1 + mg.LastColumn().ColumnNumber() - 1) / 2.0; r2 = (mg.FirstRow().RowNumber() - 1 + mg.LastRow().RowNumber() - 1) / 2.0; }
                res.Add(new Item(s, Math.Round(c2 * 100), Math.Round(-r2 * 10)));
            }
        return res;
    }

    public static ListData ReadFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".xlsx" or ".xlsm" ? ReadXlsx(path) : ReadPdf(File.ReadAllBytes(path));
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, PropertyNameCaseInsensitive = true,
    };
    public static string ToJson(ListData d) => JsonSerializer.Serialize(d, Json);
    public static ListData? FromJson(string json) { try { return JsonSerializer.Deserialize<ListData>(json, Json); } catch { return null; } }
}
