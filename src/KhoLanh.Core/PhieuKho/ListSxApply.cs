using System.Globalization;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.PhieuKho;

/// <summary>Đưa "List sản xuất" đã đọc vào Phiếu xuất kho (port từ applyListSX của phieu-nhap-xuat.js).</summary>
public static class ListSxApply
{
    public sealed record ApplyResult(bool Ok, List<string> Notes, int SoMatHang = 0, int SoDong = 0, double TongKien = 0);
    public sealed record PackResult(string Packing, string Hint);

    private static string MaClean(string? v) => Regex.Replace((v ?? "").Trim(), "^TPC-", "", RegexOptions.IgnoreCase);
    private static List<string> Uniq(IEnumerable<string> a)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase); var o = new List<string>();
        foreach (var x in a) if (x.Length > 0 && seen.Add(x)) o.Add(x);
        return o;
    }
    private static string Num(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Tên size ở phiếu: bỏ hậu tố " · Mã hàng" khi cùng size có nhiều mã (mã đã có cột riêng).</summary>
    public static string PureSize(ListSize z)
    {
        if (z.NoSize == true) return "";
        var s = z.Size ?? ""; var ma = z.Ma.Trim(); var suf = " · " + ma;
        return ma.Length > 0 && s.Length > suf.Length && s.EndsWith(suf) ? s[..^suf.Length].Trim() : s;
    }

    /// <summary>
    /// PACKING từ MÃ HÀNG 21 ký tự: ký tự 18–19 = Trọng lượng, 20–21 = Quy cách đóng thùng (Tổng hợp › Mã hóa sản phẩm, đoạn 9 và 10).
    /// Packing = "&lt;kg&gt; kg x &lt;hệ số&gt; …" — đúng dạng NetOf hiểu. Không tra được / không đọc được → rỗng + gợi ý (KHÔNG đoán).
    /// </summary>
    public static PackResult PackingFromMa(string? ma, Func<int, string, string?> nameOf)
    {
        var c = MaClean(ma).ToUpperInvariant();
        if (c.Length < 21) return new("", "");
        var wn = nameOf(9, c.Substring(17, 2)); var qn = nameOf(10, c.Substring(19, 2));
        var hint = string.Join(" · ", new[] { wn, qn }.Where(x => !string.IsNullOrEmpty(x)));
        if (string.IsNullOrEmpty(wn) || string.IsNullOrEmpty(qn)) return new("", hint.Length > 0 ? hint : $"Mã {c.Substring(17, 4)} chưa có trong Mã hóa sản phẩm (menu 9 / 10)");
        var m = Regex.Match(wn, @"(\d+(?:[.,]\d+)?)\s*(kg|g|oz|lbs?)(?![a-z])", RegexOptions.IgnoreCase);
        if (!m.Success) return new("", hint);
        var un = m.Groups[2].Value.ToLowerInvariant(); if (un == "lbs") un = "lb";
        var kg = double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        if (un == "g") kg = Math.Round(kg) / 1000;
        var fac = new List<string>(); var unitD = "";
        foreach (Match f in Regex.Matches(qn, @"(\d+(?:[.,]\d+)?)\s*([^\d/,;+x×*]*?)\s*/\s*[^\d\s,;]+", RegexOptions.IgnoreCase))
        {
            if (fac.Count == 0)
            {
                var w = f.Groups[2].Value.Trim().ToLowerInvariant();
                unitD = Regex.IsMatch(w, "^(tui|túi|goi|gói)$") ? "gói" : Regex.IsMatch(w, @"^\p{L}+$") ? w : "";
            }
            fac.Add(f.Groups[1].Value.Replace(',', '.'));
        }
        if (fac.Count == 0) return new("", hint);
        var facS = string.Join(" x ", fac.Select((f, i) => i == 0 && unitD.Length > 0 ? f + " " + unitD : f));
        if (un is "oz" or "lb") return new(m.Groups[1].Value.Replace(',', '.') + " " + un + " x " + facS, hint);
        return new((un == "kg" ? m.Groups[1].Value.Replace(',', '.') : Num(kg)) + " kg x " + facS, hint);
    }

    /// <summary>
    /// Nạp List vào phiếu. <paramref name="append"/> = THÊM mặt hàng của LSX này, giữ mặt hàng các LSX khác (cont ghép nhiều LSX).
    /// Gộp theo TÊN HÀNG: các size cùng tên (và cùng bên sản xuất AA / TP) nằm chung 1 mặt hàng, mỗi dòng giữ Size riêng.
    /// </summary>
    public static ApplyResult Apply(Phieu p, ListData r, PhieuKhoCore.Layout? lay = null, bool append = false, Func<string, PackResult>? pack = null)
    {
        lay ??= PhieuKhoCore.XuatKhauLayout;
        var notes = new List<string>();
        if (!r.Ok) return new(false, new() { "Không đọc được dữ liệu từ List sản xuất." });
        int cap = lay.C, NB = lay.NB;
        var keep = append ? p.Mh.Where(m => PhieuKhoCore.UsedBlock(m) && PhieuKhoCore.LsxNorm(m.Lsx) != PhieuKhoCore.LsxNorm(r.Lsx)).ToList() : new List<MatHang>();
        var maxB = NB - keep.Count;
        if (maxB <= 0) return new(false, new() { $"Phiếu đã đủ {NB} mặt hàng (mẫu chỉ chứa {NB}) — không còn chỗ cho LSX {r.Lsx}. Làm phiếu riêng cho LSX này." });
        // List lưu ở bản cũ (bố cục 'product') tách THƯỜNG / MKS thành 2 dòng cùng lô → gộp lại
        if (r.Layout == "product" && r.Rows.Any(x => x.MksQty is null))
        {
            var outRows = new List<ListRow>(); var at = new Dictionary<string, ListRow>();
            foreach (var x in r.Rows)
            {
                if (x.MksQty is not null) { outRows.Add(x); continue; }
                var k = x.Date + "|" + (x.TruyXuat.Length > 0 ? x.TruyXuat : "#" + outRows.Count);
                if (!at.TryGetValue(k, out var t)) { at[k] = t = new ListRow { Date = x.Date, Iso = x.Iso, Exp = x.Exp, TruyXuat = x.TruyXuat, MksQty = new() }; outRows.Add(t); }
                foreach (var (z, v) in x.Qty) { t.Qty[z] = t.Qty.GetValueOrDefault(z) + v; if (x.Mks) { t.MksQty![z] = t.MksQty.GetValueOrDefault(z) + v; t.Mks = true; } }
                t.Total = t.Qty.Values.Sum();
            }
            r = ListSx.FromJson(ListSx.ToJson(r))!; r.Rows = outRows;
        }
        if (r.Lsx.Length > 0 && !append) p.Lsx = r.Lsx;
        if (r.Khach.Length > 0 && p.Khach.Length == 0) p.Khach = r.Khach;
        else if (append && r.Khach.Length > 0 && p.Khach.Length > 0 && PhieuKhoCore.LsxNorm(r.Khach) != PhieuKhoCore.LsxNorm(p.Khach)) notes.Add($"⚠ LSX {r.Lsx} của khách {r.Khach} nhưng phiếu đang ghi khách {p.Khach} — kiểm tra lại.");
        if (r.DonVi.Length > 0) p.DonVi = r.DonVi;

        static string DKey(string d) { var m = Regex.Match(d ?? "", @"^(\d{2})\.(\d{2})\.(\d{4})$"); return m.Success ? m.Groups[3].Value + m.Groups[2].Value + m.Groups[1].Value : d ?? ""; }
        var groups = new List<List<(ListSize Z, int Zi)>>(); var gmap = new Dictionary<string, List<(ListSize, int)>>();
        for (var zi = 0; zi < r.Sizes.Count; zi++)
        {
            var z = r.Sizes[zi]; var ten = z.Ten.Trim();
            var key = ten.Length > 0 ? ten.ToLower(CultureInfo.GetCultureInfo("vi-VN")) + "|" + (Regex.IsMatch(z.Ma.Trim(), "^p", RegexOptions.IgnoreCase) ? "TP" : "AA") : "#" + zi;
            if (!gmap.TryGetValue(key, out var g)) { gmap[key] = g = new(); groups.Add(g); }
            g.Add((z, zi));
        }
        double Tot(ListSize z) => r.Rows.Sum(x => x.Qty.GetValueOrDefault(z.Size));
        if (groups.Count > maxB)
        {
            var rest = groups.Skip(maxB).Select(g => string.Join(", ", g.Select(t => $"{t.Z.Size} ({Tot(t.Z):0.###} kiện)")));
            notes.Add($"⚠ {(r.Lsx.Length > 0 ? "LSX " + r.Lsx + " " : "List ")}có {groups.Count} mặt hàng (sau khi gộp theo Tên hàng)" + (keep.Count > 0 ? $" + {keep.Count} mặt hàng của LSX khác đã có" : "") + $" nhưng mẫu phiếu chỉ chứa {NB} — CHƯA nạp: {string.Join("; ", rest)}. Làm thêm phiếu riêng cho phần này.");
        }
        var blocks = new List<MatHang>();
        foreach (var g in groups.Take(maxB))
        {
            var m = new MatHang { Hint = null }; var dong = new List<(Dong D, string K, int Z, int I)>(); var cods = new List<string>(); var packMsgs = new List<string>();
            foreach (var (z, zi) in g)
            {
                var pk = r.PackingBy.TryGetValue(z.Size, out var pb) && pb.Length > 0 ? pb : r.Packing;
                if (pk.Length == 0 && pack is not null)
                {
                    var first = z.Ma.Split(" / ")[0];
                    var gres = pack(first);
                    if (gres.Packing.Length > 0) pk = gres.Packing;
                    else if (z.Ma.Length > 0)
                    {
                        var why = $"Size {z.Size}: chưa tự lấy được Packing từ mã hàng {first} ({(gres.Hint.Length > 0 ? gres.Hint : "không tra được mã trọng lượng / quy cách")})";
                        notes.Add(why + " — nhập tay."); packMsgs.Add(why);
                    }
                }
                if (pk.Length > 0 && r.NetPdf.TryGetValue(z.Size, out var npdf))
                {
                    var n = PhieuKhoCore.NetOf(pk);
                    if (n is null || Math.Abs(n.Value - npdf) > 0.0005) notes.Add($"⚠ Size {z.Size}: quy cách \"{pk}\" cho NET {(n is null ? "?" : Num(n.Value))} kg nhưng PDF ghi {Num(npdf)} kg — kiểm tra Packing.");
                }
                if (z.CodeMaHoa.Length > 0) cods.Add(z.CodeMaHoa);
                foreach (var x in r.Rows.Where(x => x.Qty.GetValueOrDefault(z.Size) > 0))
                {
                    var mq = x.MksQty is null ? 0 : x.MksQty.GetValueOrDefault(z.Size);
                    var mks = x.MksQty is not null ? (mq > 0 ? Num(mq) : "") : (x.Mks && (x.MksSizes is null || x.MksSizes.Contains(z.Size)) ? "x" : "");
                    dong.Add((new Dong { Date = x.Date, Size = PureSize(z), NoSize = z.NoSize == true ? true : null, Ma = z.Ma.Trim(), Packing = pk, Kien = Num(x.Qty[z.Size]), NetPk = pk, Mks = mks }, DKey(x.Date), zi, dong.Count));
                }
                if (z.Ten.Length > 0 && m.Ten.Length == 0) m.Ten = z.Ten;
                if (z.Ma.Length > 0) m.Ma = string.Join(" / ", Uniq((m.Ma.Length > 0 ? m.Ma.Split(" / ") : Array.Empty<string>()).Concat(new[] { z.Ma })));
                if (z.Lo.Length > 0) m.Lo = string.Join(" / ", Uniq((m.Lo.Length > 0 ? m.Lo.Split(" / ") : Array.Empty<string>()).Concat(new[] { z.Lo })));
            }
            m.Hint = new System.Text.Json.Nodes.JsonObject { ["codeMaHoa"] = string.Join(" / ", cods), ["tieuDe"] = r.TieuDe };
            m.PackNote = string.Join("; ", packMsgs);
            // 1 mã duy nhất → m.Ma (ghi ở tiêu đề); nhiều mã khác nhau → m.Ma trống, mỗi dòng giữ mã riêng của size
            var ds = Uniq(dong.Select(t => t.D.Ma).Where(x => x.Length > 0));
            if (ds.Count > 1) m.Ma = ""; else { m.Ma = ds.FirstOrDefault() ?? (m.Ma.Length > 0 ? m.Ma : ""); foreach (var t in dong) t.D.Ma = ""; }
            // thứ tự: SIZE trước (đúng thứ tự size trong List), rồi DATE tăng dần
            var sorted = dong.OrderBy(t => t.Z).ThenBy(t => t.K, StringComparer.Ordinal).ThenBy(t => t.I).Select(t => t.D).ToList();
            if (sorted.Count > cap) notes.Add($"Mặt hàng {(m.Ten.Length > 0 ? m.Ten : string.Join("+", g.Select(t => t.Z.Size)))} có {sorted.Count} dòng DATE — mẫu chỉ chứa {cap}; đã nạp {cap} dòng đầu.");
            m.DongList = sorted.Take(cap).ToList();
            if (m.DongList.Count == 0) continue;
            m.Lsx = r.Lsx.Trim(); blocks.Add(m);
        }
        if (blocks.Count == 0) return new(false, new() { "List không có dòng số kiện nào." });
        p.Mh = keep.Concat(blocks).ToList();
        if (append)
        {
            var ord = PhieuKhoCore.LsxList(p.Lsx).Select(PhieuKhoCore.LsxNorm).ToList();
            int Idx(MatHang m) { var i = ord.IndexOf(PhieuKhoCore.LsxNorm(m.Lsx)); return i < 0 ? 999 : i; }
            p.Mh = p.Mh.Select((m, i) => (m, i)).OrderBy(t => Idx(t.m)).ThenBy(t => t.i).Select(t => t.m).ToList();
        }
        foreach (var w in r.Warnings) notes.Add("⚠ " + w);
        return new(true, notes, blocks.Count, blocks.Sum(b => b.DongList.Count), blocks.Sum(b => b.DongList.Sum(d => double.Parse(d.Kien, CultureInfo.InvariantCulture))));
    }
}
