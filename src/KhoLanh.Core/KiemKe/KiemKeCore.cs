using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using KhoLanh.Core.Entry;
using KhoLanh.Core.SoDo;

namespace KhoLanh.Core.KiemKe;

/// <summary>
/// Lõi tính của Đối chiếu kiểm kê (hàm thuần, theo "Số liệu kiểm kê" của app cũ: dc-gop-engine.js, dc-nguyen-nhan.js, vt-kiemke-core.js).
/// · Rút dòng kiểm kê từ các trang Nhập liệu (cột Mã hàng + Số kiện / Số thùng / Net; "Kg" của mẫu Kiểm kê = kg mỗi thùng).
/// · Đối chiếu theo mã với Tồn kho An An (kiện + kg): Khớp / Thừa / Thiếu / Lệch kg / Chỉ có ở kiểm kê / Chưa kiểm kê; với Kế toán theo kg.
/// · Gợi ý nguyên nhân (không đoán: chỉ gợi ý, không tự sửa số liệu).
/// · Đối chiếu theo vị trí (Tồn theo vị trí + Sơ đồ kho) và lệnh đếm theo dãy.
/// </summary>
public static class KiemKeCore
{
    public const double KgTol = 0.05, VtTol = 0.0005;
    public const string Khop = "Khớp", Thua = "Thừa", Thieu = "Thiếu", LechKg = "Lệch kg", ChiKK = "Chỉ có ở kiểm kê", ChuaKK = "Chưa kiểm kê",
        ChiKT = "Chỉ có ở kế toán", ChuaDC = "Chưa đối chiếu tồn kho", ChiGui = "Chỉ có ở kho gửi";
    public static readonly HashSet<string> Lech = new() { Thua, Thieu, LechKg, ChiKK, ChuaKK };

    public static double R4(double n) => Math.Round(n * 10000, MidpointRounding.AwayFromZero) / 10000;
    public static double R2(double n) => Math.Round(n * 100, MidpointRounding.AwayFromZero) / 100;

    /// <summary>Bỏ dấu, đ → d, chữ thường, chỉ giữ a-z 0-9 (so tên cột).</summary>
    public static string Hk(string? s)
    {
        var t = (s ?? "").Normalize(NormalizationForm.FormD);
        var b = new StringBuilder();
        foreach (var ch in t)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = ch is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(ch);
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') b.Append(c);
        }
        return b.ToString();
    }
    /// <summary>Khóa so mã: chữ hoa, bỏ khoảng trắng, bỏ tiền tố TPC-.</summary>
    public static string NormKey(string? s) => Regex.Replace(Regex.Replace((s ?? "").Normalize(NormalizationForm.FormC).ToUpperInvariant(), @"\s+", ""), "^TPC-", "");
    public static double? Num(string? s) => string.IsNullOrWhiteSpace(s) ? null : EntryService.TryNumber(s, out var d) && double.IsFinite(d) ? d : double.NaN;

    // ================================================================== trang Nhập liệu → dòng kiểm kê

    public sealed class Page
    {
        public string Id = "", Name = "", Ngay = "", Template = "", UpdatedAt = "";
        public List<string> Columns = new(); public List<string[]> Rows = new();
    }
    private static readonly Dictionary<string, string[]> Alias = new()
    {
        ["ma"] = new[] { "mahang", "mahh", "ma" }, ["ten"] = new[] { "tenhang", "ten" }, ["vt"] = new[] { "vitri" },
        ["tl"] = new[] { "trongluong", "kgthung", "kgkien", "tlthung", "kg" }, ["sk"] = new[] { "sokien", "sothung", "soluong", "kien", "thung", "sl" },
        ["net"] = new[] { "net", "nettl", "netkg", "tongkg", "tongnetkg", "tongnet" }, ["hd"] = new[] { "hopdong" }, ["lo"] = new[] { "lo", "lobatch", "solo" },
        ["dt"] = new[] { "dactinh" }, ["gc"] = new[] { "ghichu" },
        ["xd"] = new[] { "ngayxuat" }, ["xs"] = new[] { "slxuat", "soluongxuat", "sokienxuat" }, ["nd"] = new[] { "ngaynhap" }, ["ns"] = new[] { "slnhap", "soluongnhap", "sokiennhap" },
        // 1.71 mẫu kiểm kê mới: Số lượng còn lại (= Số lượng − SL xuất) là SỐ ĐỐI CHIẾU; Tổng Net(kg) còn lại
        ["sc"] = new[] { "soluongconlai", "slconlai", "sokienconlai" }, ["nc"] = new[] { "tongnetkgconlai", "netconlai", "netkgconlai", "tongnetconlai" },
    };
    public static Dictionary<string, int> PageCols(IEnumerable<string> columns)
    {
        var idx = new Dictionary<string, int>(); var i = 0;
        foreach (var c in columns)
        {
            var k = Hk(c);
            foreach (var (f, names) in Alias) if (!idx.ContainsKey(f) && names.Contains(k)) idx[f] = i;
            i++;
        }
        return idx;
    }
    public static bool IsKiemKe(IEnumerable<string> columns) { var c = PageCols(columns); return c.ContainsKey("ma") && (c.ContainsKey("sk") || c.ContainsKey("net")); }

    /// <summary>Ngày của trang: trong tên ("… 21-11-2026", "21/11/2026") → yyyy-MM-dd; không có thì ngày tạo.</summary>
    public static string NgayOf(string name, string createdAt)
    {
        var m = Regex.Match(name ?? "", @"(\d{1,2})[-/.](\d{1,2})[-/.](\d{4})");
        if (m.Success && DateTime.TryParseExact($"{m.Groups[3].Value}-{int.Parse(m.Groups[2].Value):00}-{int.Parse(m.Groups[1].Value):00}", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Regex.IsMatch(createdAt ?? "", @"^\d{4}-\d{2}-\d{2}") ? createdAt![..10] : "";
    }

    public sealed class Line
    {
        public string PageId = "", Page = "", Ngay = "", Ma = "", Key = "", Ten = "", Vt = "", Hd = "", Lo = "", Dt = "", Gc = "", KgSrc = "";
        public int Row; public double Kien; public double? Kg, Tl;
    }
    public sealed class Extracted
    {
        public List<Line> Rows = new(); public List<(string Id, string Name, string Why)> Skipped = new();
        public int KhongMa, ThieuKien, ThieuKg, SoLoi; public List<string> Samples = new(); public List<PhatSinh.Mv> Mv = new();
    }
    private static readonly Regex ReLoGc = new(@"(?:^|·|\s)lô\s+(\S+)", RegexOptions.IgnoreCase);

    public static Extracted ExtractRows(IEnumerable<Page> pages, string? chotDate = null)
    {
        var nearYear = PhatSinh.IsoOk(chotDate) ? int.Parse(chotDate![..4]) : DateTime.Now.Year;
        var o = new Extracted();
        foreach (var p in pages.OrderBy(p => p.Ngay.Length > 0 ? p.Ngay : "9999", StringComparer.Ordinal).ThenBy(p => p.UpdatedAt, StringComparer.Ordinal))
        {
            var C = PageCols(p.Columns);
            if (!C.ContainsKey("ma") || (!C.ContainsKey("sk") && !C.ContainsKey("net")))
            {
                o.Skipped.Add((p.Id, p.Name, !C.ContainsKey("ma") ? "không có cột “Mã hàng”" : "không có cột “Số kiện” / “Số thùng” / “Net”")); continue;
            }
            string Cell(string[] row, string f) => C.TryGetValue(f, out var i) && i < row.Length ? (row[i] ?? "").Trim() : "";
            for (var r = 0; r < p.Rows.Count; r++)
            {
                var row = p.Rows[r];
                var ma = Cell(row, "ma"); string skR = Cell(row, "sk"), netR = Cell(row, "net"), tlR = Cell(row, "tl");
                var hasNum = skR.Length > 0 || netR.Length > 0;
                string xdR = Cell(row, "xd"), xsR = Cell(row, "xs"), ndR = Cell(row, "nd"), nsR = Cell(row, "ns");
                var hasMv = xsR.Length > 0 || nsR.Length > 0;
                if (ma.Length == 0 && !hasNum && !hasMv) continue;
                var where = $"{p.Name} · dòng {r + 1}";
                if (ma.Length == 0) { o.KhongMa++; if (o.Samples.Count < 12) o.Samples.Add(where + ": có số nhưng không có mã"); continue; }
                double? kien = Num(skR), net = Num(netR), tl = Num(tlR);
                var conLai = C.ContainsKey("sc");   // 1.71: trang có "Số lượng còn lại" → đối chiếu theo số còn lại, SL xuất đã trừ sẵn (không tính phát sinh xuất nữa)
                if (conLai && hasNum)
                {
                    var xq = Num(xsR) is { } xv && !double.IsNaN(xv) ? xv : 0;
                    var sc = Num(Cell(row, "sc"));
                    var k0 = kien is { } kk0 && !double.IsNaN(kk0) ? kk0 : (double?)null;
                    kien = sc is { } scv && !double.IsNaN(scv) ? scv : k0 is { } kv ? kv - xq : null;
                    var nc = Num(Cell(row, "nc"));
                    net = nc is { } ncv && !double.IsNaN(ncv) ? ncv : tl is { } tv && !double.IsNaN(tv) && kien is { } kn ? kn * tv : (xq == 0 ? net : null);
                }
                if ((kien is { } a && double.IsNaN(a)) || (net is { } b && double.IsNaN(b)) || (tl is { } c && double.IsNaN(c))) { o.SoLoi++; if (o.Samples.Count < 12) o.Samples.Add(where + ": số không đọc được"); }
                if (kien is { } k1 && double.IsNaN(k1)) kien = null; if (net is { } n1 && double.IsNaN(n1)) net = null; if (tl is { } t1 && double.IsNaN(t1)) tl = null;
                if (hasMv)
                {
                    var uw = tl is > 0 ? tl : (kien is > 0 && net is >= 0 ? net / kien : null);
                    foreach (var (type, dRaw, sRaw) in new[] { ("x", xdR, xsR), ("n", ndR, nsR) })
                    {
                        if (sRaw.Length == 0 || (conLai && type == "x")) continue; var q = Num(sRaw);
                        if (q is null || double.IsNaN(q.Value) || q <= 0) { o.SoLoi++; if (o.Samples.Count < 12) o.Samples.Add(where + ": số phát sinh không đọc được"); continue; }
                        var d = PhatSinh.ParseNgay(dRaw, nearYear, chotDate);
                        o.Mv.Add(new PhatSinh.Mv { Type = type, PageId = p.Id, Page = p.Name, PageNgay = PhatSinh.IsoOk(p.Ngay) ? p.Ngay : "", Dong = r + 1, Ma = Regex.Replace(ma.ToUpperInvariant(), @"\s+", ""), Key = NormKey(ma), Ten = Cell(row, "ten"), Vt = Cell(row, "vt"), Hd = Cell(row, "hd"), Gc = Cell(row, "gc"), NgayRaw = dRaw, Ngay = d.Iso, DateBlank = d.Blank, DateBad = d.Bad, Kien = q.Value, Kg = uw is { } w ? q.Value * w : null });
                    }
                }
                if (!hasNum) continue;
                double? kg = null; var src = "";
                if (net is >= 0) { kg = net; src = "net"; } else if (kien is not null && tl is not null) { kg = kien * tl; src = "tinh"; }
                if (kien is null) { o.ThieuKien++; if (o.Samples.Count < 12) o.Samples.Add(where + ": thiếu số kiện"); }
                if (kg is null && (kien ?? 0) != 0) o.ThieuKg++;
                var gc = Cell(row, "gc"); var lo = Cell(row, "lo");
                if (lo.Length == 0 && ReLoGc.Match(gc) is { Success: true } lm) lo = lm.Groups[1].Value;
                o.Rows.Add(new Line
                {
                    PageId = p.Id, Page = p.Name, Ngay = p.Ngay, Row = r + 1, Ma = Regex.Replace(ma.ToUpperInvariant(), @"\s+", ""), Key = NormKey(ma),
                    Ten = Cell(row, "ten"), Vt = Cell(row, "vt"), Hd = Cell(row, "hd"), Lo = lo, Dt = Cell(row, "dt"), Gc = gc,
                    Kien = kien ?? 0, Kg = kg, Tl = tl, KgSrc = src,
                });
            }
        }
        return o;
    }

    public sealed record Recount(string Vt, string Ma, string Hd, string Lo, string Dt, List<Line> Items);
    private static string VtNorm(string s) => Regex.Replace((s ?? "").ToUpperInvariant(), @"\s+", "");
    /// <summary>Cùng Vị trí · Mã · Hợp đồng · Lô · Đặc tính được đếm ở ≥ 2 ngày → có thể đếm lại (cộng trùng).</summary>
    public static List<Recount> FindRecount(IEnumerable<Line> rows)
    {
        var g = new Dictionary<string, Recount>(); var order = new List<string>();
        foreach (var r in rows)
        {
            if (r.Vt.Length == 0) continue;
            var k = string.Join("|", VtNorm(r.Vt), r.Key, r.Hd.ToUpperInvariant(), r.Lo.ToUpperInvariant(), r.Dt.ToUpperInvariant());
            if (!g.TryGetValue(k, out var a)) { g[k] = a = new Recount(r.Vt, r.Ma, r.Hd, r.Lo, r.Dt, new()); order.Add(k); }
            a.Items.Add(r);
        }
        return order.Select(k => g[k]).Where(a => a.Items.Select(x => x.Ngay).Distinct().Count() >= 2).ToList();
    }
    /// <summary>Bỏ các dòng của lần đếm cũ (cùng định danh, ngày cũ hơn).</summary>
    public static List<Line> LatestOnly(List<Line> rows, out List<Line> dropped)
    {
        var drop = new HashSet<Line>();
        foreach (var a in FindRecount(rows)) { var mx = a.Items.Max(x => x.Ngay) ?? ""; foreach (var x in a.Items) if (string.CompareOrdinal(x.Ngay, mx) < 0) drop.Add(x); }
        dropped = rows.Where(drop.Contains).ToList();
        return rows.Where(x => !drop.Contains(x)).ToList();
    }

    // ================================================================== Tồn kho An An

    public sealed class Stock { public string Ma = "", Key = "", Ten = "", Dt = "", Hd = "", Nhom = ""; public double Ton; public double? Tl; }
    /// <summary>Dòng Load Data của Tồn kho An An → bản ghi (Mã hàng, Tên, Đặc tính, Hợp đồng, Tồn cuối, Trọng lượng).</summary>
    public static List<Stock> MakeStock(IReadOnlyList<string> headers, IEnumerable<string[]> rows, IReadOnlyList<string>? groups = null)
    {
        var H = headers.Select(Hk).ToList();
        int At(params string[] ks) { foreach (var k in ks) { var i = H.IndexOf(k); if (i >= 0) return i; } return -1; }
        int ma = At("mahang"), ten = At("tenhang"), dt = At("dactinh"), hd = At("hopdong"), ton = At("toncuoi"), tl = At("trongluong");
        var o = new List<Stock>(); if (ma < 0) return o;
        string G(string[] r, int i) => i >= 0 && i < r.Length ? (r[i] ?? "").Trim() : "";
        var idx = -1;
        foreach (var r in rows)
        {
            idx++;
            var m = G(r, ma); if (m.Length == 0) continue;
            var t = Num(G(r, ton)); var w = Num(G(r, tl));
            o.Add(new Stock { Ma = m, Key = NormKey(m), Ten = G(r, ten), Dt = G(r, dt), Hd = G(r, hd), Nhom = groups is not null && idx < groups.Count ? groups[idx] : "", Ton = t is { } x && !double.IsNaN(x) ? x : 0, Tl = w is { } y && !double.IsNaN(y) ? y : null });
        }
        return o;
    }

    // ================================================================== Kế toán (nạp / dán: Mã hàng · Trọng lượng · Số lượng · Net)

    public sealed class Kt { public string Key = "", Ma = "", Ten = ""; public double? Kg, Kien, Tl; public int Rows; }
    public sealed class KtSet { public Dictionary<string, Kt> Map = new(); public int N, KhongMa, ThieuNet, SoLoi; }
    public sealed record KtRow(string Ma, string Ten, string Tl, string Sl, string Net);
    public static KtSet MakeKeToan(IEnumerable<KtRow> list)
    {
        var o = new KtSet(); var tls = new Dictionary<string, HashSet<double>>();
        foreach (var x in list)
        {
            var ma = (x.Ma ?? "").Trim();
            if (ma.Length == 0) { if ((x.Tl + x.Sl + x.Net).Trim().Length > 0) o.KhongMa++; continue; }
            double? tl = Num(x.Tl), sl = Num(x.Sl), net = Num(x.Net);
            if ((tl is { } a && double.IsNaN(a)) || (sl is { } b && double.IsNaN(b)) || (net is { } c && double.IsNaN(c))) o.SoLoi++;
            if (tl is { } a1 && double.IsNaN(a1)) tl = null; if (sl is { } b1 && double.IsNaN(b1)) sl = null; if (net is { } c1 && double.IsNaN(c1)) net = null;
            double? kg = net ?? (sl is not null && tl is not null ? sl * tl : null);
            if (kg is null && (sl ?? 0) != 0) o.ThieuNet++;
            var key = NormKey(ma);
            if (!o.Map.TryGetValue(key, out var e)) { o.Map[key] = e = new Kt { Key = key, Ma = Regex.Replace(ma.ToUpperInvariant(), @"\s+", ""), Ten = (x.Ten ?? "").Trim() }; tls[key] = new(); }
            e.Rows++; o.N++;
            if (kg is not null) e.Kg = (e.Kg ?? 0) + kg;
            if (sl is not null) e.Kien = (e.Kien ?? 0) + sl;
            if (tl is not null) tls[key].Add(tl.Value);
        }
        foreach (var e in o.Map.Values) { e.Tl = tls[e.Key].Count == 1 ? tls[e.Key].First() : null; if (e.Kg is { } k) e.Kg = R4(k); if (e.Kien is { } n) e.Kien = R4(n); }
        return o;
    }

    // ================================================================== bảng đối chiếu theo mã

    public sealed class Nn
    {
        public string Code = "", Level = "", Label = ""; public List<string> Evidence = new();
        public string? PartnerKey, PartnerMa, Side, FixTo; public bool Strong; public List<(string Ma, int Dist)> Sug = new();
    }
    public sealed class Row
    {
        public string Key = "", Ma = "", Ten = "", Status = "", KtStatus = "";
        public double? KkKien, KkKg, TonKien, TonKg, DKien, DKg, KtKg, KtKien, ChotKg, TongKt;
        /// <summary>Tồn kho gửi (đã đổi sang mã 21) + Tổng = tồn chốt + kho gửi (dùng để so Kế toán).</summary>
        public double? GuiKg, GuiKien, TongKg;
        public string Nhom = ""; public List<string> NhomAll = new();
        /// <summary>Phát sinh nhập / xuất đã quy về ngày chốt (KkKien / KkKg đã gồm phần này).</summary>
        public double? NhapKien, XuatKien, NhapKg, XuatKg;
        /// <summary>So theo LSX (chỉ mã nhóm Thành Phẩm Chính có ≥2 LSX): "", Khớp, Lệch, Thiếu thông tin.</summary>
        public string LsxStatus = ""; public List<LsxItem> Lsx = new();
        public int KkRows, KkDays, TonRows; public List<string> Notes = new(); public Nn? Nn;
    }
    public sealed record LsxItem(string Lsx, double KkKien, double TonKien, double KkKg, double TonKg, bool Lech);
    /// <summary>LSX = phần Hợp đồng trước hậu tố ngày "-dd/mm[/yy]", viết hoa, bỏ khoảng trắng.</summary>
    public static string LsxOf(string? hd)
    {
        var t = Regex.Replace((hd ?? "").Trim(), @"\s*-\s*\d{1,2}/\d{1,2}(/\d{2,4})?\s*$", "");
        return Regex.Replace(t, @"\s+", "").ToUpperInvariant();
    }
    /// <summary>Đối chiếu theo LSX cho các mã Thành Phẩm Chính vừa kiểm kê vừa có tồn mà có từ 2 LSX trở lên.</summary>
    public static void AddLsx(IEnumerable<Row> rows, IEnumerable<Line> lines, IEnumerable<Stock> stock)
    {
        var kk = lines.GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.ToList());
        var st = stock.GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.ToList());
        foreach (var r in rows)
        {
            r.LsxStatus = ""; r.Lsx = new();
            if (r.Nhom != "tpc" || r.KkRows == 0 || r.TonRows == 0 || !kk.TryGetValue(r.Key, out var kl) || !st.TryGetValue(r.Key, out var sl)) continue;
            var kLsx = kl.Select(x => LsxOf(x.Hd)).Where(x => x.Length > 0).Distinct().ToList();
            var sLsx = sl.Where(x => x.Nhom == "tpc").Select(x => LsxOf(x.Hd)).Where(x => x.Length > 0).Distinct().ToList();
            if (kLsx.Union(sLsx).Count() < 2 && sLsx.Count < 2) continue;
            if (kl.Any(x => LsxOf(x.Hd).Length == 0)) { r.LsxStatus = "Thiếu thông tin"; continue; }
            var keys = kLsx.Union(sLsx).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var items = new List<LsxItem>(); var any = false;
            foreach (var k in keys)
            {
                var kk1 = kl.Where(x => LsxOf(x.Hd) == k).ToList(); var ss1 = sl.Where(x => x.Nhom == "tpc" && LsxOf(x.Hd) == k).ToList();
                double kKien = kk1.Sum(x => x.Kien), tKien = ss1.Sum(x => x.Ton);
                double kKg = kk1.Sum(x => x.Kg ?? 0), tKg = ss1.Sum(x => x.Tl is { } w ? x.Ton * w : 0);
                var lech = Math.Abs(kKien - tKien) > 1e-9 || Math.Abs(kKg - tKg) > KgTol;
                any |= lech; items.Add(new LsxItem(k, R4(kKien), R4(tKien), R4(kKg), R4(tKg), lech));
            }
            r.Lsx = items; r.LsxStatus = any ? "Lệch" : "Khớp";
        }
    }
    public sealed class Totals { public List<Row> Rows = new(); public bool HasStock, HasKT; }

    public static Totals BuildTotals(IEnumerable<Line> ex, IReadOnlyCollection<Stock> stock, KtSet? kt, PhatSinh.Adj? adj = null, IReadOnlyDictionary<string, KhoGui.Agg>? gui = null)
    {
        var hasStock = stock.Count > 0; var hasKT = kt is { Map.Count: > 0 };
        var M = new Dictionary<string, (Row r, double kkKien, double kkKg, int kkKgN, int kgThieu, HashSet<string> days, double tonKien, double tonKg, int tonKgThieu, bool ktHas)>();
        var order = new List<string>(); var gm = new Dictionary<string, Dictionary<string, (double Ton, int N)>>();
        Row Get(string key, string ma) { if (!M.ContainsKey(key)) { M[key] = (new Row { Key = key, Ma = ma }, 0, 0, 0, 0, new HashSet<string>(), 0, 0, 0, false); order.Add(key); } return M[key].r; }
        foreach (var x in ex)
        {
            var r = Get(x.Key, x.Ma); var e = M[x.Key];
            e.kkKien += x.Kien; r.KkRows++; if (x.Ngay.Length > 0) e.days.Add(x.Ngay);
            if (x.Kg is { } kg) { e.kkKg += kg; e.kkKgN++; } else if (x.Kien != 0) e.kgThieu++;
            if (r.Ten.Length == 0 && x.Ten.Length > 0) r.Ten = x.Ten;
            M[x.Key] = e;
        }
        foreach (var s in stock)
        {
            var r = Get(s.Key, s.Ma); var e = M[s.Key];
            r.TonRows++; e.tonKien += s.Ton;
            if (s.Nhom.Length > 0) { if (!gm.TryGetValue(s.Key, out var gd)) gm[s.Key] = gd = new(); gd.TryGetValue(s.Nhom, out var gv); gd[s.Nhom] = (gv.Ton + s.Ton, gv.N + 1); }
            if (s.Tl is { } tl) e.tonKg += s.Ton * tl; else if (s.Ton != 0) e.tonKgThieu++;
            if (s.Ten.Length > 0) r.Ten = s.Ten;
            M[s.Key] = e;
        }
        if (hasKT)
            foreach (var (key, v) in kt!.Map)
            {
                var r = Get(key, v.Ma); var e = M[key];
                if (v.Kg is { } kg) r.KtKg = (r.KtKg ?? 0) + kg;
                if (v.Kien is { } kn) r.KtKien = (r.KtKien ?? 0) + kn;
                e.ktHas = true; if (r.Ten.Length == 0 && v.Ten.Length > 0) r.Ten = v.Ten;
                M[key] = e;
            }
        var guiKeys = new HashSet<string>();
        if (gui is not null)
            foreach (var (key, g) in gui) { var r = Get(key, g.Ma); r.GuiKg = (r.GuiKg ?? 0) + g.Kg; if (g.Kien is double k) r.GuiKien = (r.GuiKien ?? 0) + k; if (r.Ten.Length == 0 && g.Ten.Length > 0) r.Ten = g.Ten; guiKeys.Add(key); }
        if (adj is not null)
            foreach (var (key, a) in adj.ByKey) if ((a.NKien != 0 || a.XKien != 0) && !M.ContainsKey(key)) Get(key, key);
        var o = new Totals { HasStock = hasStock, HasKT = hasKT };
        foreach (var key in order)
        {
            var e = M[key]; var r = e.r;
            var ad = adj is not null && adj.ByKey.TryGetValue(key, out var aa) && (aa.NKien != 0 || aa.XKien != 0) ? aa : null;
            double adK = ad is null ? 0 : ad.NKien - ad.XKien, adG = ad is null ? 0 : ad.NKg - ad.XKg;
            e.kkKien += adK; e.kkKg += adG;
            if (ad is not null) { r.NhapKien = ad.NKien; r.XuatKien = ad.XKien; r.NhapKg = ad.NKg; r.XuatKg = ad.XKg; }
            bool counted = r.KkRows > 0 || ad is not null, inStock = r.TonRows > 0;
            var kgKnown = e.kkKgN > 0 || e.kkKien - adK == 0 || ad is not null;
            r.KkDays = e.days.Count;
            if (gm.TryGetValue(key, out var gdm))
            {
                var ord = PhanLoaiM1.Order.ToList();
                r.NhomAll = gdm.Keys.OrderBy(g => ord.IndexOf(g)).ToList();
                r.Nhom = gdm.OrderByDescending(kv => Math.Abs(kv.Value.Ton)).ThenByDescending(kv => kv.Value.N).ThenBy(kv => ord.IndexOf(kv.Key)).First().Key;
            }
            double? dKien = counted || inStock ? R4(e.kkKien - e.tonKien) : null;
            double? dKg = (counted || inStock) && kgKnown ? R4(e.kkKg - e.tonKg) : null;
            string? st;
            var guiHas = guiKeys.Contains(key);
            if (!counted && !inStock) st = guiHas ? ChiGui : e.ktHas ? ChiKT : null;
            else if (!counted) st = Math.Abs(e.tonKien) > 1e-9 ? ChuaKK : guiHas ? ChiGui : (e.ktHas && (r.KtKg ?? 0) != 0 ? ChiKT : null);
            else if (!inStock) st = hasStock ? ChiKK : ChuaDC;
            else if (Math.Abs(dKien!.Value) < 1e-9 && (dKg is null || Math.Abs(dKg.Value) <= KgTol)) st = Khop;
            else if (Math.Abs(dKien.Value) < 1e-9) st = LechKg;
            else st = dKien > 0 ? Thua : Thieu;
            if (st is null) continue;
            r.Status = st;
            r.KkKien = counted ? R4(e.kkKien) : null; r.KkKg = counted && kgKnown ? R4(e.kkKg) : null;
            r.TonKien = inStock ? R4(e.tonKien) : null; r.TonKg = inStock ? R4(e.tonKg) : null;
            r.DKien = st is ChiKT or ChiGui ? null : dKien; r.DKg = st is ChiKT or ChiGui ? null : dKg;
            if (inStock && e.tonKien < -1e-9) r.Notes.Add($"Tồn kho An An đang ÂM ({R4(e.tonKien)} kiện) — kiểm tra lại số liệu tồn");
            if (ad is not null) r.Notes.Add($"Đã quy về ngày chốt: +{ad.NKien:#,0.##} nhập − {ad.XKien:#,0.##} xuất kiện sau ngày đếm");
            if (adj is not null && adj.ByKey.TryGetValue(key, out var fl) && fl.Flags.Overlaps(new[] { "truoc", "thieu_ngay", "loi_ngay", "xuat_lon" })) r.Notes.Add("Có phát sinh nhập / xuất cần rà (ngày hoặc số lượng chưa hợp lý) — xem mục Phát sinh");
            if (e.kgThieu > 0) r.Notes.Add($"{e.kgThieu} dòng kiểm kê thiếu Net / Trọng lượng (chưa tính kg)");
            if (e.tonKgThieu > 0) r.Notes.Add($"{e.tonKgThieu} dòng tồn kho thiếu Trọng lượng");
            if (!inStock && counted && hasStock) r.Notes.Add("Mã không có trong Tồn kho An An");
            if (r.NhomAll.Count > 1) r.Notes.Add("Mã có tồn ở nhiều nhóm của module Tồn kho An An: " + string.Join(", ", r.NhomAll.Select(PhanLoaiM1.LabelOf)) + " — nhóm chính: " + PhanLoaiM1.LabelOf(r.Nhom));
            // Kế toán: so "tồn chốt" (= số kiểm kê kg; mã không kiểm kê = 0) với Net kế toán
            r.ChotKg = counted ? (kgKnown ? R4(e.kkKg) : null) : 0;
            if (r.GuiKg is double gk) r.GuiKg = R4(gk); if (r.GuiKien is double gn) r.GuiKien = R4(gn);
            r.TongKg = r.ChotKg is double ck ? R4(ck + (r.GuiKg ?? 0)) : null;
            if (hasKT)
            {
                if (r.KtKg is { } k) r.KtKg = R4(k); if (r.KtKien is { } kn) r.KtKien = R4(kn);
                if (r.KtKg is null) { if (r.TongKg is { } c && Math.Abs(c) > KgTol) { r.KtStatus = "Lệch"; r.Notes.Add("Kế toán không có mã này"); } }
                else if (r.TongKg is null) r.KtStatus = "Chưa tính được";
                else r.KtStatus = Math.Abs(r.TongKg.Value - r.KtKg.Value) <= KgTol ? "Khớp" : "Lệch";
                r.TongKt = r.KtKg is null || r.TongKg is null ? null : R4(r.TongKg.Value - r.KtKg.Value);
                if (e.ktHas && r.KtKg is null) r.Notes.Add("Kế toán chưa có Net (và không tính được từ Số lượng × Trọng lượng)");
            }
            o.Rows.Add(r);
        }
        o.Rows.Sort((a, b) => string.CompareOrdinal(a.Ma, b.Ma));
        return o;
    }

    public sealed record Sum(int Total, int Khop, int Thua, int Thieu, int LechKg, int ChiKK, int ChuaKK, int Open, double KgLech, int Auto, int KtLech, double KtKgLech);
    public static Sum Summarize(IEnumerable<Row> rows)
    {
        int t = 0, k = 0, th = 0, ti = 0, lk = 0, ck = 0, ch = 0, op = 0, au = 0, ktl = 0; double kg = 0, ktkg = 0;
        foreach (var r in rows)
        {
            if (r.Status is ChiKT or ChiGui) { if (r.KtStatus == "Lệch") { ktl++; ktkg += Math.Abs(r.TongKt ?? r.KtKg ?? 0); } continue; }
            t++;
            switch (r.Status) { case Khop: k++; break; case Thua: th++; break; case Thieu: ti++; break; case LechKg: lk++; break; case ChiKK: ck++; break; case ChuaKK: ch++; break; }
            if (Lech.Contains(r.Status)) { op++; kg += Math.Abs(r.DKg ?? 0); if (r.Nn is { Code: "sai-ma", Side: "kk", Strong: true, FixTo: not null }) au++; }
            if (r.KtStatus == "Lệch") { ktl++; ktkg += Math.Abs(r.TongKt ?? r.ChotKg ?? 0); }
        }
        return new Sum(t, k, th, ti, lk, ck, ch, op, R2(kg), au, ktl, R2(ktkg));
    }

    // ================================================================== nguyên nhân chênh lệch (chỉ gợi ý)

    public static int Hamming(string a, string b)
    {
        if (a.Length == 0 || a.Length != b.Length) return int.MaxValue;
        var d = 0; for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) d++;
        return d;
    }
    private static string Nf(double v) => R4(v).ToString("#,0.##", CultureInfo.InvariantCulture);
    private static List<(Row r, int dist, double dKg)> Nearest(Row row, IEnumerable<Row> pool, int maxD, Func<Row, double> kgOf, HashSet<string>? used) =>
        pool.Where(c => !ReferenceEquals(c, row) && (used is null || !used.Contains(c.Key)))
            .Select(c => (c, d: Hamming(row.Ma, c.Ma))).Where(x => x.d <= maxD)
            .Select(x => (x.c, x.d, Math.Abs(kgOf(row) - kgOf(x.c)))).OrderBy(x => x.d).ThenBy(x => x.Item3).Take(5).ToList();

    public static void Analyze(List<Row> rows, HashSet<string> recountKeys)
    {
        foreach (var r in rows) r.Nn = null;
        var used = new HashSet<string>();
        var onlyKK = rows.Where(r => r.Status == ChiKK).ToList(); var onlyTon = rows.Where(r => r.Status == ChuaKK).ToList();
        double KkKg(Row r) => r.KkKg ?? 0; double TonKg(Row r) => r.TonKg ?? 0; double SideKg(Row r) => r.Status == ChiKK ? KkKg(r) : TonKg(r);
        foreach (var a in onlyKK)
        {
            var c = Nearest(a, onlyTon, 2, SideKg, used).FirstOrDefault(); if (c.r is null) continue;
            var b = c.r; used.Add(a.Key); used.Add(b.Key);
            var sameKg = Math.Abs(KkKg(a) - TonKg(b)) <= KgTol; var sameKien = Math.Abs((a.KkKien ?? 0) - (b.TonKien ?? 0)) < 1e-9;
            var ev = new List<string>
            {
                $"Mã kiểm kê “{a.Ma}” chỉ có ở kiểm kê ({Nf(KkKg(a))} kg); mã “{b.Ma}” chỉ có ở Tồn kho An An ({Nf(TonKg(b))} kg).",
                $"Hai mã chỉ khác {c.dist} ký tự" + (sameKg ? " và cùng số kg" : $" (số kg khác nhau {Nf(Math.Abs(KkKg(a) - TonKg(b)))} kg)") + (sameKien ? ", cùng số kiện" : "") + ".",
            };
            a.Nn = new Nn { Code = "sai-ma", Level = "ng", Label = "Nghi gõ sai mã", Evidence = ev, PartnerKey = b.Key, PartnerMa = b.Ma, Side = "kk", FixTo = b.Ma, Strong = sameKg };
            b.Nn = new Nn { Code = "sai-ma", Level = "ng", Label = "Nghi gõ sai mã", Evidence = ev.Concat(new[] { $"Mã “{b.Ma}” nằm ở Tồn kho An An (chỉ đọc) nên sửa ở dòng kiểm kê." }).ToList(), PartnerKey = a.Key, PartnerMa = a.Ma, Side = "ton", Strong = sameKg };
        }
        foreach (var r in rows)
        {
            if (r.Status == Khop || r.Nn is not null || !Lech.Contains(r.Status)) continue;
            switch (r.Status)
            {
                case ChiKK:
                    r.Nn = new Nn { Code = "chi-kiem-ke", Level = "ng", Label = "Mã chỉ có ở kiểm kê", Evidence = { "Mã không có trong Tồn kho An An. Có thể gõ sai mã, hoặc hàng chưa được nhập vào hệ thống / nằm ở kho gửi." },
                        Sug = Nearest(r, rows.Where(x => (x.TonKg ?? 0) > 0 || (x.TonKien ?? 0) > 0), 3, SideKg, null).Select(x => (x.r.Ma, x.dist)).ToList() };
                    break;
                case ChuaKK:
                    r.Nn = new Nn { Code = "chi-ton", Level = "ng", Label = "Có ở An An, chưa có dòng kiểm kê", Evidence = { $"Tồn kho An An có {Nf(r.TonKien ?? 0)} kiện / {Nf(TonKg(r))} kg nhưng không có dòng kiểm kê nào. Có thể chưa đếm vị trí này, hoặc đếm nhưng gõ sai mã." },
                        Sug = Nearest(r, onlyKK, 3, SideKg, null).Select(x => (x.r.Ma, x.dist)).ToList() };
                    break;
                case LechKg:
                {
                    var k = r.KkKien ?? 0; double? a = k != 0 ? KkKg(r) / k : null, b = (r.TonKien ?? 0) != 0 ? TonKg(r) / r.TonKien!.Value : null;
                    r.Nn = new Nn { Code = "lech-tl", Level = "xd", Label = "Lệch trọng lượng trên mỗi kiện", Evidence = { $"Số kiện bằng nhau ({Nf(k)}) nhưng kg lệch {Nf(r.DKg ?? 0)} kg." + (a is not null && b is not null ? $" Trọng lượng/kiện: kiểm kê {Nf(a.Value)} · An An {Nf(b.Value)}." : "") } };
                    break;
                }
                default:
                {
                    if (recountKeys.Contains(r.Key)) { r.Nn = new Nn { Code = "dem-trung", Level = "ng", Label = "Nghi đếm trùng hoặc đếm lại", Evidence = { "Cùng vị trí / hợp đồng / lô / đặc tính của mã này được đếm ở từ 2 ngày khác nhau." } }; break; }
                    double k = r.KkKien ?? 0, t = r.TonKien ?? 0; double? a = k != 0 ? KkKg(r) / k : null, b = t != 0 ? TonKg(r) / t : null;
                    r.Nn = a is not null && b is not null && Math.Abs(a.Value - b.Value) <= KgTol
                        ? new Nn { Code = "lech-kien", Level = "xd", Label = (r.Status == Thua ? "Thừa " : "Thiếu ") + Nf(Math.Abs(r.DKien ?? 0)) + " kiện", Evidence = { $"Trọng lượng/kiện hai bên giống nhau ({Nf(a.Value)} kg), chỉ lệch số kiện: kiểm kê {Nf(k)} · An An {Nf(t)}. Chưa biết nguyên nhân của số kiện lệch." } }
                        : new Nn { Code = "cx", Level = "cx", Label = "Chưa xác định nguyên nhân", Evidence = { "Số kiện và kg cùng lệch, không có dấu hiệu đếm trùng để giải thích." } };
                    break;
                }
            }
        }
    }

    public sealed record HdLine(string Hd, double Kk, double Ton, double KkKg, double TonKg, double D, double DKg);
    /// <summary>Chi tiết 1 mã theo Hợp đồng: kiểm kê ↔ tồn — để tìm chênh lệch nằm ở đâu.</summary>
    public static List<HdLine> DrillHd(string key, IEnumerable<Line> ex, IEnumerable<Stock> stock)
    {
        var m = new Dictionary<string, (string hd, double kk, double ton, double kkKg, double tonKg)>(); var order = new List<string>();
        void Add(string hd, double kk, double ton, double kkKg, double tonKg)
        {
            var k = hd.Trim().ToUpperInvariant(); if (!m.ContainsKey(k)) { m[k] = (hd.Trim().Length > 0 ? hd.Trim() : "(không có hợp đồng)", 0, 0, 0, 0); order.Add(k); }
            var e = m[k]; m[k] = (e.hd, e.kk + kk, e.ton + ton, e.kkKg + kkKg, e.tonKg + tonKg);
        }
        foreach (var x in ex.Where(x => x.Key == key)) Add(x.Hd, x.Kien, 0, x.Kg ?? 0, 0);
        foreach (var s in stock.Where(s => s.Key == key)) Add(s.Hd, 0, s.Ton, 0, s.Tl is { } tl ? s.Ton * tl : 0);
        return order.Select(k => m[k]).Select(e => new HdLine(e.hd, R4(e.kk), R4(e.ton), R4(e.kkKg), R4(e.tonKg), R4(e.kk - e.ton), R4(e.kkKg - e.tonKg)))
            .OrderByDescending(x => Math.Abs(x.D)).ThenBy(x => x.Hd, StringComparer.Ordinal).ToList();
    }

    // ================================================================== theo vị trí (Tồn theo vị trí + Sơ đồ kho)

    public const string VtChua = "chua", VtKhop = "khop", VtLech = "lech", VtThua = "thua", VtTrong = "trong";
    public static string VtText(string st) => st switch { VtChua => "Chưa đếm", VtKhop => "Khớp", VtLech => "Lệch", VtThua => "Thừa hàng (sổ sách trống)", _ => "Trống đúng" };
    public sealed class VtItem { public string Ma = "", Lo = "", Note = ""; public double Exp, Cnt, Diff; }
    public sealed class VtCell { public SoDoCore.Pos P = null!; public string St = ""; public List<VtItem> Items = new(); public double Cnt, Exp; public int NExp, NCnt; public List<Line> Lines = new(); }
    public sealed class VtDay { public string Key = "", Kho = "", Khu = ""; public int D, NExp, NDone, NLech, NThua, NAll; }
    public sealed class VtCmp
    {
        public Dictionary<string, VtCell> Cells = new(); public List<(Line R, string Why)> Bad = new();
        public Dictionary<string, int> S = new() { [VtChua] = 0, [VtKhop] = 0, [VtLech] = 0, [VtThua] = 0, [VtTrong] = 0, ["coHang"] = 0 };
        public Dictionary<string, VtDay> Days = new();
    }
    public static string DayKey(string kho, string khu, int d) => $"{kho}.{khu}.{SoDoCore.Two(d)}";

    /// <summary>So từng vị trí: dòng kiểm kê có cột Vị trí ↔ Tồn theo vị trí (sổ sách, đổi gói → kiện theo quy cách). So theo mã · lô; kiểm kê không ghi lô thì so theo mã.</summary>
    public static VtCmp CompareVt(SoDoCore.Built b, IEnumerable<Line> ex, Func<string, SoDoCore.Qc?> qcOf)
    {
        var o = new VtCmp(); var cnt = new Dictionary<string, List<Line>>();
        foreach (var r in ex)
        {
            if (r.Vt.Trim().Length == 0) { o.Bad.Add((r, "không có vị trí")); continue; }
            var p = SoDoCore.Parse(r.Vt);
            if (p is null) { o.Bad.Add((r, $"vị trí “{r.Vt}” sai dạng Kho.Khu.Dãy.Tầng.Khoang")); continue; }
            if (!b.ByCode.ContainsKey(p.Code)) { o.Bad.Add((r, $"vị trí {p.Code} không có trong Sơ đồ kho")); continue; }
            if (!cnt.TryGetValue(p.Code, out var l)) cnt[p.Code] = l = new();
            l.Add(r);
        }
        foreach (var p in b.Pos)
        {
            if (p.Z) continue;
            var dk = DayKey(p.Kho, p.Khu, p.D);
            if (!o.Days.TryGetValue(dk, out var d)) o.Days[dk] = d = new VtDay { Key = dk, Kho = p.Kho, Khu = p.Khu, D = p.D };
            d.NAll++;
            var counted = cnt.GetValueOrDefault(p.Code) ?? new(); var exp = p.It;
            var map = new Dictionary<string, VtItem>(); var order = new List<string>();
            VtItem Slot(string ma, string lo) { var k = ma + "|" + lo; if (!map.TryGetValue(k, out var s)) { map[k] = s = new VtItem { Ma = ma, Lo = lo }; order.Add(k); } return s; }
            foreach (var x in exp) Slot(SoDoCore.KeyMa(x.Ma), x.Lo.Trim().ToUpperInvariant()).Exp += SoDoCore.KienOf(x.Sl, qcOf(x.Ma));
            foreach (var r in counted) Slot(SoDoCore.KeyMa(r.Ma), r.Lo.Trim().ToUpperInvariant()).Cnt += r.Kien;
            foreach (var g in map.Values.GroupBy(s => s.Ma).ToList())
            {
                var noLo = g.FirstOrDefault(s => s.Lo.Length == 0 && s.Cnt > 0 && s.Exp == 0); var withExp = g.Where(s => s.Exp > 0 && s.Cnt == 0).ToList();
                if (noLo is null || withExp.Count == 0) continue;
                var left = noLo.Cnt;
                foreach (var s in withExp) { var t = Math.Min(left, s.Exp); s.Cnt += t; left -= t; s.Note = "kiểm kê không ghi lô — so theo mã"; }
                noLo.Cnt = left; if (noLo.Cnt <= VtTol) { map.Remove(noLo.Ma + "|"); order.Remove(noLo.Ma + "|"); }
            }
            var items = order.Where(map.ContainsKey).Select(k => map[k]).ToList();
            foreach (var s in items) { s.Exp = R2(s.Exp); s.Cnt = R2(s.Cnt); s.Diff = R2(s.Cnt - s.Exp); }
            items.Sort((a, c) => string.CompareOrdinal(a.Ma + "|" + a.Lo, c.Ma + "|" + c.Lo));
            bool hasExp = exp.Count > 0, hasCnt = counted.Count > 0;
            var st = !hasExp && !hasCnt ? VtTrong : hasExp && !hasCnt ? VtChua : !hasExp ? VtThua : items.Any(s => Math.Abs(s.Diff) > VtTol) ? VtLech : VtKhop;
            o.S[st]++; if (hasExp) { o.S["coHang"]++; d.NExp++; if (hasCnt) d.NDone++; }
            if (st == VtLech) d.NLech++; if (st == VtThua) d.NThua++;
            o.Cells[p.Code] = new VtCell
            {
                P = p, St = st, Items = items, Lines = counted, Cnt = R2(counted.Sum(r => r.Kien)), Exp = R2(exp.Sum(x => SoDoCore.KienOf(x.Sl, qcOf(x.Ma)))), NExp = exp.Count, NCnt = counted.Count,
            };
        }
        return o;
    }

    // ================================================================== 1 trang Nhập liệu kiểm kê ↔ sơ đồ (1.72, Sơ đồ kho › chế độ "Kiểm kê")

    /// <summary>Trạng thái 1 ô vị trí so với 1 trang kiểm kê: khớp · lệch · thiếu (sổ có, đếm không / = 0) · thừa (đếm có, sổ trống) · chưa đếm (dãy không có trong trang).</summary>
    public const string KkKhop = "khop", KkLech = "lech", KkThieu = "thieu", KkThua = "thua", KkChuaDem = "chua";
    public static readonly string[] KkStates = { KkKhop, KkLech, KkThieu, KkThua, KkChuaDem };
    public static string KkText(string st) => st switch { KkKhop => "Khớp", KkLech => "Lệch", KkThieu => "Thiếu (sổ có, đếm không)", KkThua => "Thừa (đếm có, sổ không)", _ => "Chưa đếm" };
    public sealed class KkMap
    {
        public Dictionary<string, string> St = new();           // mã vị trí → trạng thái
        public Dictionary<string, VtCell> Cells = new();        // mã vị trí → chi tiết so (sổ / đếm theo mã · lô)
        public Dictionary<string, int> RowOf = new();           // mã vị trí → dòng ĐẦU TIÊN của vị trí trên trang (0 = dòng 1)
        public HashSet<string> Days = new();                    // các dãy có trong trang (Kho.Khu.Dãy)
        public Dictionary<string, int> N = KkStates.ToDictionary(x => x, _ => 0);
        public List<(Line R, string Why)> Bad = new();
        public string Kho = "", Khu = ""; public int D;         // dãy / khu / kho xuất hiện nhiều nhất trong trang
        public int Empty;                                       // ô trống đúng (sổ trống, đếm trống) — đã tính trong "Khớp"
        /// <summary>Trang chỉ có 1 khu (sơ đồ mở đúng khu đó); nhiều khu thì mở "Tất cả".</summary>
        public bool OneKhu => Days.Select(k => k[..k.LastIndexOf('.')]).Distinct().Count() == 1;
    }

    /// <summary>
    /// So 1 trang Nhập liệu kiểm kê (cột Vị trí, Mã hàng, Số lượng / Số lượng còn lại…) với sổ sách theo VỊ TRÍ của Sơ đồ kho.
    /// Mọi ô của các dãy có trong trang được xét (ô không ghi dòng nào = đếm trống); ô ở dãy khác = "chưa đếm".
    /// <paramref name="qcOf"/> đổi gói → kiện cho sổ sách (null = so số lượng như ghi, như trang tạo từ Sơ đồ kho).
    /// </summary>
    public static KkMap MapPage(SoDoCore.Built b, Page page, Func<string, SoDoCore.Qc?>? qcOf = null)
    {
        var o = new KkMap();
        var ex = ExtractRows(new[] { page });
        var cmp = CompareVt(b, ex.Rows, qcOf ?? (_ => null));
        o.Bad = cmp.Bad;
        var C = PageCols(page.Columns);
        var dayN = new Dictionary<string, int>();
        if (C.TryGetValue("vt", out var cv))
            for (var r = 0; r < page.Rows.Count; r++)
            {
                var row = page.Rows[r]; var vt = cv < row.Length ? (row[cv] ?? "").Trim() : "";
                if (SoDoCore.Parse(vt) is not { } p || p.Z || !b.ByCode.ContainsKey(p.Code)) continue;
                var dk = DayKey(p.Kho, p.Khu, p.D); o.Days.Add(dk); dayN[dk] = dayN.GetValueOrDefault(dk) + 1;
                o.RowOf.TryAdd(p.Code, r);
            }
        foreach (var (code, c) in cmp.Cells)
        {
            var covered = o.Days.Contains(DayKey(c.P.Kho, c.P.Khu, c.P.D));
            var st = c.St switch
            {
                VtKhop => KkKhop,
                VtTrong => covered ? KkKhop : KkChuaDem,
                VtChua => covered ? KkThieu : KkChuaDem,
                VtThua => KkThua,
                _ => c.Cnt <= VtTol ? KkThieu : KkLech,
            };
            if (st == KkKhop && c.St == VtTrong) o.Empty++;
            o.St[code] = st; o.Cells[code] = c; o.N[st]++;
        }
        if (dayN.Count > 0)
        {
            var top = dayN.OrderByDescending(x => x.Value).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key;
            var any = b.Pos.First(p => !p.Z && DayKey(p.Kho, p.Khu, p.D) == top);
            o.Kho = any.Kho; o.Khu = any.Khu; o.D = any.D;
        }
        return o;
    }

    public sealed class Order { public string Id = "", Kho = "", Khu = "", Nguoi = ""; public int D, NPos, NExp, NItems; public double Sl; }
    /// <summary>Lệnh đếm: mỗi dãy 1 lệnh; chia đều cho người đếm theo số vị trí có hàng.</summary>
    public static List<Order> MakeOrders(SoDoCore.Built b, string kho, string khu, bool onlyWithStock, IList<string> people)
    {
        var by = new Dictionary<string, Order>();
        foreach (var p in b.Pos)
        {
            if (p.Z || (kho.Length > 0 && p.Kho != kho) || (khu.Length > 0 && khu != "ALL" && p.Khu != khu)) continue;
            var k = DayKey(p.Kho, p.Khu, p.D);
            if (!by.TryGetValue(k, out var o)) by[k] = o = new Order { Id = k, Kho = p.Kho, Khu = p.Khu, D = p.D };
            o.NPos++;
            if (p.It.Count > 0) { o.NExp++; o.NItems += p.It.Count; o.Sl = R2(o.Sl + p.It.Sum(x => x.Sl)); }
        }
        var list = by.Values.OrderBy(o => o.Kho, StringComparer.Ordinal).ThenBy(o => o.Khu, StringComparer.Ordinal).ThenBy(o => o.D).ToList();
        if (onlyWithStock) list = list.Where(o => o.NExp > 0).ToList();
        var ppl = people.Select(s => s.Trim()).Where(s => s.Length > 0).Distinct().ToList();
        if (ppl.Count > 0)
        {
            var load = new int[ppl.Count];
            foreach (var o in list.OrderByDescending(o => o.NExp).ThenBy(o => o.Id, StringComparer.Ordinal))
            {
                var m = 0; for (var i = 1; i < ppl.Count; i++) if (load[i] < load[m]) m = i;
                o.Nguoi = ppl[m]; load[m] += Math.Max(1, o.NExp);
            }
        }
        return list;
    }
    public sealed record Prog(int NExp, int NDone, int Pct, int Lech, int Thua, string State);
    public static Prog Progress(Order o, VtCmp? c)
    {
        if (c is null || !c.Days.TryGetValue(o.Id, out var d)) return new Prog(o.NExp, 0, 0, 0, 0, "chua");
        var pct = d.NExp > 0 ? (int)Math.Round(d.NDone * 100.0 / d.NExp) : d.NThua > 0 ? 100 : 0;
        var st = d.NExp == 0 ? "khong-hang" : d.NDone == 0 ? "chua" : d.NDone < d.NExp ? "dang" : d.NLech > 0 || d.NThua > 0 ? "xong-lech" : "xong";
        return new Prog(d.NExp, d.NDone, pct, d.NLech, d.NThua, st);
    }
    public static string ProgText(string st) => st switch { "khong-hang" => "Không có hàng", "chua" => "Chưa đếm", "dang" => "Đang đếm", "xong-lech" => "Xong — còn lệch", "xong" => "Xong — khớp", _ => st };
}
