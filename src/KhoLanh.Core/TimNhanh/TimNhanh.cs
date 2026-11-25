using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KhoLanh.Core.Models;
using KhoLanh.Core.ViTri;

namespace KhoLanh.Core.TimNhanh;

/// <summary>1 dòng tồn hiện tại (cùng khuôn cho mọi nguồn) — như buildGlobalSearchData() của app cũ.</summary>
public sealed class GsRow
{
    public string Src = "", Vt = "", Ma = "", Ten = "", Size = "", Tag = "", Hd = "", Dt = "", Lo = "", Extra = "";
    public double? Ton, Kg;
    internal string S = "", T = "", K = "", Sc = "";
    public string Key => K;
}

/// <summary>
/// TÌM NHANH TOÀN APP (Ctrl + K) — lõi so khớp, chép từ global-search.js (matcher / wordsHit / maKey) của app cũ:
/// không phân biệt dấu, không tính khoảng trắng ("btp50" = "BTP 50"); các từ tìm riêng (VÀ), mỗi từ có ở 1 cột bất kỳ;
/// phần đầu là đúng 1 nhãn Size ("BTP 50 MKS") → chỉ dòng có nhãn đó; cả ô dạng nhãn ("BTP 13") → nhãn bắt đầu bằng BTP13,
/// có dấu cách cuối ("BTP 13 ") → đúng nhãn.
/// </summary>
public static class Gs
{
    /// <summary>Nguồn: mã · tên hiển thị · quyền xem (thứ tự = thứ tự hiển thị).</summary>
    public static readonly (string Src, string Name, string Perm, string Menu)[] Sources =
    {
        ("M01", "Tồn kho An An", "m1", "m1"), ("M01VT", "Tồn vị trí", "m7", "m7"), ("M01VTB", "Tồn bột", "m1vtb", "m1vtb"),
        ("M02", "Tồn kho gửi", "m2", "m2"), ("M03", "NXT Bột/Sốt", "m3sk", "m3sk"),
    };
    public static string SrcName(string src) => Sources.FirstOrDefault(s => s.Src == src).Name ?? src;
    public static int SrcOrder(string src) { var i = Array.FindIndex(Sources, s => s.Src == src); return i < 0 ? 99 : i; }

    public static string Norm(string? v)
    {
        var d = (v ?? "").Normalize(NormalizationForm.FormD); var sb = new StringBuilder(d.Length);
        foreach (var ch in d) if (ch < '\u0300' || ch > '\u036f') sb.Append(ch);
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant();
    }
    public static string Cmp(string? v) => Regex.Replace(v ?? "", @"\s+", "");
    public static string MaKey(string? v) => Regex.Replace(Regex.Replace((v ?? "").Normalize(NormalizationForm.FormC).ToUpperInvariant(), @"\s+", ""), "^TPC-", "");
    /// <summary>Vị trí gọn: bỏ tiền tố kho "TG1." (vẫn tìm được theo tên đầy đủ).</summary>
    public static string ShortPlace(string? v) => Regex.Replace(v ?? "", @"^TG\d*\.", "", RegexOptions.IgnoreCase);

    public sealed class Data
    {
        public List<GsRow> Rows = new();
        public HashSet<string> Tags = new(), Prefixes = new();
        public List<string> Errors = new();
        /// <summary>Mã hóa sản phẩm (Tổng hợp): tổ hợp → mã → tên.</summary>
        public Dictionary<int, Dictionary<string, string>> Seg = new();
        /// <summary>1.89: MỌI dòng còn tồn của Tồn kho An An (Load Data), kể cả mã đã có ở Tồn vị trí (khác <see cref="Rows"/>: M01 chỉ giữ mã chưa có ở vị trí)
        /// — nguồn gợi ý mã hàng của Nhập liệu.</summary>
        public List<GsRow> AnAn = new();
    }

    /// <summary>10 đoạn của mã 21 ký tự (giống segmentDefs của Tổng hợp): tổ hợp, vị trí, độ dài, tên.</summary>
    public static readonly (int Seg, int Start, int Len, string Label)[] SegDefs =
    {
        (1, 0, 4, "Tên sản phẩm"), (2, 4, 1, "Cấp đông"), (3, 5, 1, "Đóng gói"), (4, 6, 1, "Hóa chất / phụ gia"), (5, 7, 1, "Thị trường"),
        (6, 8, 2, "Khách hàng"), (7, 10, 3, "Mạ băng"), (8, 13, 4, "Size"), (9, 17, 2, "Trọng lượng"), (10, 19, 2, "Quy cách đóng thùng"),
    };
    /// <summary>Giải mã mã 21 ký tự (bỏ "TPC-"); mã khác độ dài → rỗng. Tên = null khi chưa khai báo.</summary>
    public static List<(string Label, string Code, string? Name)> Decode(Data d, string ma)
    {
        var m = Regex.Replace((ma ?? "").Trim().ToUpperInvariant(), "^TPC-", "");
        if (m.Length != 21) return new();
        return SegDefs.Select(x => { var c = m.Substring(x.Start, x.Len); return (x.Label, c, d.Seg.TryGetValue(x.Seg, out var t) && t.TryGetValue(c, out var n) ? n : null); }).ToList();
    }

    public static Data Prepare(IEnumerable<GsRow> rows, IEnumerable<string>? errors = null)
    {
        var d = new Data { Rows = rows.ToList() }; if (errors is not null) d.Errors.AddRange(errors);
        foreach (var r in d.Rows)
        {
            r.S = Norm(string.Join(" ", r.Vt, r.Ma, r.Ten, r.Size, r.Tag, r.Hd, r.Dt, r.Lo, r.Extra));
            r.T = r.Tag.Length > 0 ? Cmp(Norm(r.Tag)) : ""; r.K = MaKey(r.Ma); r.Sc = Cmp(r.S);
            if (r.T.Length > 0) { d.Tags.Add(r.T); var m = Regex.Match(r.T, "^[a-z]+"); if (m.Success) d.Prefixes.Add(m.Value); }
        }
        return d;
    }

    public static bool WordsHit(GsRow r, IReadOnlyList<string> qs)
    {
        if (qs.Count == 0) return true;
        if (qs.All(w => r.S.Contains(w, StringComparison.Ordinal))) return true;
        return r.Sc.Contains(string.Concat(qs), StringComparison.Ordinal);
    }

    public sealed record Matcher(List<string> Terms, Func<GsRow, bool> Fn);

    public static Matcher? Match(Data d, string raw)
    {
        var qs = Norm(raw).Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        var trail = Regex.IsMatch(raw ?? "", @"\s$");
        if (qs.Count == 0) return null;
        for (var k = qs.Count - 1; k >= 1; k--)
        {
            var tag = string.Concat(qs.Take(k)); if (!d.Tags.Contains(tag)) continue;
            var rest = qs.Skip(k).ToList();
            return new Matcher(rest, r => r.T == tag && WordsHit(r, rest));
        }
        var all = string.Concat(qs); var m = Regex.Match(all, @"^([a-z]+)\d");
        if (m.Success && d.Prefixes.Contains(m.Groups[1].Value))
            return new Matcher(new(), trail ? r => r.T == all : r => r.T.Length > 0 && r.T.StartsWith(all, StringComparison.Ordinal));
        return new Matcher(qs, r => WordsHit(r, qs));
    }

    /// <summary>Kết quả xếp theo mã, rồi theo thứ tự nguồn, rồi tồn giảm dần (như app cũ).</summary>
    public static List<GsRow> Search(Data d, string q, out List<string> terms)
    {
        terms = new(); var m = Match(d, q); if (m is null) return new();
        terms = m.Terms;
        return d.Rows.Where(m.Fn).OrderBy(r => r.K, StringComparer.Ordinal).ThenBy(r => SrcOrder(r.Src)).ThenByDescending(r => r.Ton ?? 0).ToList();
    }

    /// <summary>Các đoạn [đầu, cuối) của <paramref name="text"/> cần tô sáng (so khớp bỏ dấu + bỏ khoảng trắng).</summary>
    public static bool[] Highlight(string text, IReadOnlyList<string> terms)
    {
        var mk = new bool[text.Length]; if (text.Length == 0 || terms.Count == 0) return mk;
        var flat = new StringBuilder(); var map = new List<int>();
        for (var i = 0; i < text.Length; i++) { var c = Norm(text[i].ToString()); if (string.IsNullOrWhiteSpace(c)) continue; foreach (var ch in c) { flat.Append(ch); map.Add(i); } }
        var f = flat.ToString();
        foreach (var t in terms)
        {
            if (t.Length == 0) continue; var from = 0; int at;
            while ((at = f.IndexOf(t, from, StringComparison.Ordinal)) >= 0) { for (var k = map[at]; k <= map[at + t.Length - 1]; k++) mk[k] = true; from = at + t.Length; }
        }
        return mk;
    }
}

/// <summary>Gom tồn HIỆN TẠI của các module thành danh sách <see cref="GsRow"/> (chỉ đọc; chỉ module tài khoản được xem).</summary>
public sealed class GsService
{
    private readonly AppServices _s;
    public GsService(AppServices s) { _s = s; }

    public Gs.Data Build()
    {
        var rows = new List<GsRow>(); var errors = new List<string>(); var sess = _s.Session;
        bool Can(string perm) => sess.Can(perm, Perm.View);
        var vt = _s.ViTri;
        try { if (!vt.Loaded && Can(ViTriService.PermKey)) vt.Load(); else vt.LoadDictionaries(); } catch (Exception e) { errors.Add("Từ điển size: " + e.Message); }
        (string Size, string Tag) SizeOf(string ma, string ten)
        {
            if (ma.Length == 0) return ("", "");
            try { var r = vt.Build(ma, ten, "", "", "", 0, 0); return (vt.SizeOf(r), vt.GopLabel(r) ?? ""); } catch { return ("", ""); }
        }
        void Push(GsRow r) { var (sz, tg) = SizeOf(r.Ma, r.Ten); if (r.Size.Length == 0) r.Size = sz; if (r.Tag.Length == 0) r.Tag = tg; rows.Add(r); }

        // ---- Tồn theo vị trí (M01VT) + Load Data An An (M01: mã chưa có ở vị trí) + Vị Trí Bột (M01VTB)
        var vtSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var anAn = new List<GsRow>();
        try
        {
            if (Can(ViTriService.PermKey) || Can("m1"))
                foreach (var r in vt.Rows.Where(r => r.Ma.Length > 0 && r.Sl > 0))
                {
                    vtSeen.Add(r.Ma);
                    if (Can(ViTriService.PermKey)) Push(new GsRow { Src = "M01VT", Vt = r.Vt, Ma = r.Ma, Ten = r.Ten, Hd = r.Hd, Dt = r.Dt, Ton = r.Sl, Kg = r.Tl > 0 ? r.Sl * r.Tl : null, Lo = r.Lo });
                }
        }
        catch (Exception e) { errors.Add("Tồn vị trí: " + e.Message); }
        try
        {
            if (Can("m1"))
            {
                var an = _s.AnAn; if (!an.Loaded) an.Load(); var D = an.D;
                int cMa = D.Col("Mã hàng"), cTen = D.Col("Tên hàng"), cHd = D.Col("Hợp đồng"), cSz = D.Col("Size"), cDt = D.Col("Đặc tính"), cTon = D.Col("Tồn cuối"), cTl = D.Col("Trọng lượng");
                if (cMa >= 0)
                {
                    var seenLd = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < D.Rows.Count; i++)
                    {
                        var ma = ViTriService.ShortCode(D.Cell(i, cMa)); var ton = Num(D.Cell(i, cTon)); var tl = Num(D.Cell(i, cTl));
                        if (ma.Length == 0 || !(ton > 0)) continue;
                        var row = new GsRow { Src = "M01", Ma = ma, Ten = D.Cell(i, cTen).Trim(), Hd = D.Cell(i, cHd).Trim(), Dt = D.Cell(i, cDt).Trim(), Size = D.Cell(i, cSz).Trim(), Ton = ton, Kg = tl > 0 ? ton * tl : null };
                        if (vtSeen.Contains(ma)) { var (sz, tg) = SizeOf(row.Ma, row.Ten); if (row.Size.Length == 0) row.Size = sz; if (row.Tag.Length == 0) row.Tag = tg; }
                        else Push(row);
                        anAn.Add(row);
                    }
                }
            }
        }
        catch (Exception e) { errors.Add("Tồn kho An An: " + e.Message); }
        try
        {
            if (Can(KhoLanh.Core.SoKho.VtbService.PermKey))
            {
                var vb = _s.Vtb; vb.Load();
                foreach (var r in vb.Rows.Where(r => r.Ma.Trim().Length > 0 && r.Sl > 0))
                    Push(new GsRow { Src = "M01VTB", Vt = r.Vt, Ma = r.Ma.Trim(), Ten = r.GhiChu, Ton = r.Sl, Extra = string.Join(" · ", new[] { r.Nsx.Trim().Length > 0 ? "NSX " + r.Nsx.Trim() : "", r.Hsd.Trim().Length > 0 ? "HSD " + r.Hsd.Trim() : "" }.Where(x => x.Length > 0)) });
            }
        }
        catch (Exception e) { errors.Add("Vị Trí Bột: " + e.Message); }

        // ---- Tồn kho gửi (M02)
        try
        {
            if (Can("m2"))
            {
                var g = _s.Gui; if (!g.Loaded) g.Load();
                foreach (var r in g.D.Rows)
                {
                    if (!(r.SoKien > 0) && !(r.TrongLuong > 0)) continue;
                    var codeTag = SizeOf(r.MaHang.Trim(), r.TenHang).Tag; var szCol = r.Size.Trim();
                    var tag = codeTag.Length > 0 ? codeTag : Regex.IsMatch(szCol, @"^[A-Za-zÀ-ỹĐđ]+\s*\d") ? szCol : "";
                    rows.Add(new GsRow { Src = "M02", Ma = r.MaHang.Trim(), Ten = r.TenHang.Trim(), Dt = g.D.DacTinhOf(r), Size = szCol.Length > 0 ? szCol : SizeOf(r.MaHang.Trim(), r.TenHang).Size, Tag = tag, Ton = r.SoKien, Kg = r.TrongLuong, Lo = r.SoLo.Trim(), Extra = r.PhieuNhap.Trim().Length > 0 ? "PN " + r.PhieuNhap.Trim() : "" });
                }
            }
        }
        catch (Exception e) { errors.Add("Tồn kho gửi: " + e.Message); }

        // ---- NXT Bột/Sốt (M03): lô còn tồn (FIFO)
        try
        {
            if (Can(KhoLanh.Core.SoKho.SoKhoService.PermKey))
            {
                var sk = _s.SoKho; sk.Load();
                foreach (var l in KhoLanh.Core.SoKho.SoKhoCore.Compute(sk.Lines).Lots.Where(l => l.Net > 1e-9 || l.Sl > 1e-9))
                    rows.Add(new GsRow { Src = "M03", Vt = l.ViTri, Ma = l.MaHang, Size = l.Size, Hd = l.HopDong, Ton = l.Sl, Kg = l.Net, Lo = l.NgayNhap.Length > 0 ? "Nhập " + KhoLanh.Core.SoKho.SoKhoCore.Dmy(l.NgayNhap) : "", Extra = l.HanDung.Length > 0 ? "HSD " + KhoLanh.Core.SoKho.SoKhoCore.Dmy(l.HanDung) : "" });
            }
        }
        catch (Exception e) { errors.Add("NXT Bột/Sốt: " + e.Message); }

        var data = Gs.Prepare(rows, errors);
        data.AnAn = anAn;
        try { data.Seg = ReadMaHoa(); } catch (Exception e) { data.Errors.Add("Mã hóa sản phẩm: " + e.Message); }
        return data;
    }

    /// <summary>Mã hóa sản phẩm: danh mục cố định + sheet MaHoa của Tổng hợp (Tổ hợp | Mã | Tên).</summary>
    private Dictionary<int, Dictionary<string, string>> ReadMaHoa()
    {
        var seg = new Dictionary<int, Dictionary<string, string>>();
        void Add(int k, string code, string name) { code = code.Trim().ToUpperInvariant(); if (code.Length == 0) return; if (!seg.TryGetValue(k, out var t)) seg[k] = t = new(); t[code] = name.Trim(); }
        foreach (var (k, list) in KhoLanh.Core.TongHop.TongHopService.FixedCatalog()) foreach (var e in list) Add(k, e.Code, e.Name);
        var arr = JsonNode.Parse(_s.Lan.Read(KhoLanh.Core.TongHop.TongHopService.Db, "MaHoa")) as JsonArray;
        foreach (var r in arr?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            if (int.TryParse(r["Tổ hợp"]?.ToString(), out var k)) Add(k, r["Mã"]?.ToString() ?? "", r["Tên"]?.ToString() ?? "");
        return seg;
    }

    private static double Num(string? s) => KhoLanh.Core.DoiChieu.DcxEngine.ParseNum(s) ?? 0;
}

/// <summary>1 phiếu tìm được (Phiếu xuất kho / phiếu vị trí) — như core/tim-nhanh.js của app cũ.</summary>
public sealed record GsDoc(string Kind, string Id, string Menu, string Title, string Sub, string Hay);

/// <summary>TÌM PHIẾU / LSX / SỐ CONT (chép core/tim-nhanh.js): các từ đều phải có; trùng khít một trường +2, khớp đầu trường +1.</summary>
public static class GsDocs
{
    private static string S(string? v) => (v ?? "").Trim();
    private static string Dmy(string iso) => Regex.Match(iso, @"^(\d{4})-(\d{2})-(\d{2})") is { Success: true } m ? $"{m.Groups[3]}/{m.Groups[2]}/{m.Groups[1]}" : iso;
    public static string Fold(string? v) => Regex.Replace(Gs.Norm(S(v)), @"\s+", " ");

    public static List<GsDoc> Build(AppServices s, int max = 6000)
    {
        var outp = new List<GsDoc>();
        try
        {
            if (s.Session.Can(KhoLanh.Core.PhieuKho.PhieuKhoService.PermKey, Perm.View))
            {
                var pk = s.PhieuKho; if (!pk.Loaded) pk.Load();
                foreach (var p in pk.List.Take(max))
                {
                    if (S(p.Id).Length == 0) continue; var x = p.Xe;
                    var parts = new[] { S(p.SoPhieu), S(p.Lsx), S(p.Khach), S(x.Cont), S(x.Seal), S(x.Booking), S(x.SoXe), S(p.Ngay) };
                    outp.Add(new GsDoc("pnx", p.Id, "m13", "Phiếu " + (S(p.SoPhieu).Length > 0 ? S(p.SoPhieu) : p.Id),
                        string.Join(" · ", new[] { Dmy(S(p.Ngay)), S(p.Khach), S(p.Lsx).Length > 0 ? "LSX " + S(p.Lsx) : "", S(x.Cont).Length > 0 ? "Cont " + S(x.Cont) : "" }.Where(t => t.Length > 0)),
                        string.Join(" | ", parts.Select(Fold))));
                }
            }
        }
        catch { /* bỏ qua nguồn lỗi */ }
        try
        {
            if (s.Session.Can(ViTriService.PermKey, Perm.View))
            {
                var vt = s.ViTri; if (!vt.Loaded) vt.Load();
                foreach (var sl in vt.Slips.Take(max))
                {
                    if (S(sl.Id).Length == 0) continue;
                    outp.Add(new GsDoc("vt", sl.Id, "m7", "Phiếu vị trí " + sl.Id + (S(sl.MaPhieu).Length > 0 ? " · " + S(sl.MaPhieu) : ""),
                        string.Join(" · ", new[] { S(sl.CreatedAt).Length >= 10 ? S(sl.CreatedAt)[..10] : S(sl.CreatedAt), S(sl.NguoiTao), S(sl.GhiChu), sl.Lines.Count + " dòng" }.Where(t => t.Length > 0)),
                        string.Join(" | ", new[] { sl.Id, sl.MaPhieu, sl.GhiChu, sl.NguoiTao, sl.TieuDe ?? "" }.Select(Fold))));
                }
            }
        }
        catch { /* bỏ qua nguồn lỗi */ }
        return outp;
    }

    public static List<GsDoc> Search(IEnumerable<GsDoc> docs, string q, int lim = 8)
    {
        var ws = Fold(q).Split(' ', StringSplitOptions.RemoveEmptyEntries); if (ws.Length == 0) return new();
        int Score(GsDoc d) { var f = d.Hay.Split(" | "); return ws.Sum(w => f.Any(x => x == w) ? 2 : f.Any(x => x.StartsWith(w, StringComparison.Ordinal)) ? 1 : 0); }
        return docs.Where(d => ws.All(w => d.Hay.Contains(w, StringComparison.Ordinal))).Select(d => (d, sc: Score(d))).OrderByDescending(x => x.sc).Take(lim).Select(x => x.d).ToList();
    }
}
