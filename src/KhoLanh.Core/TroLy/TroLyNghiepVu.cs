using System.Globalization;
using System.Text.RegularExpressions;
using static KhoLanh.Core.TroLy.TroLyChecks;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// KIỂM TRA SỐ LIỆU NGHIỆP VỤ KHO của Trợ lý hệ thống (theo core/tro-ly/nghiep-vu.js của app cũ) + KIỂM TRA CHÉO giữa các module
/// (mới ở bản C#). HÀM THUẦN, chỉ đọc. Mỗi phát hiện nêu rõ module · mã hàng · ngày · con số để lần ra đúng dòng. Chỉ ĐỀ XUẤT, không sửa gì.
/// </summary>
public static class TroLyNghiepVu
{
    private const int MaxEv = 8;

    /// <summary>Ngày về dạng yyyy-mm-dd (nhận yyyy-mm-dd…, dd/mm/yyyy, dd.mm.yyyy); không đọc được → "".</summary>
    public static string IsoDate(string? s)
    {
        s = (s ?? "").Trim();
        var m = Regex.Match(s, @"^(\d{4})-(\d{1,2})-(\d{1,2})");
        if (m.Success) return Ok(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value));
        m = Regex.Match(s, @"^(\d{1,2})[/.](\d{1,2})[/.](\d{4})");
        if (m.Success) return Ok(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value));
        return "";
        static string Ok(int y, int mo, int d) { try { return new DateTime(y, mo, d).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); } catch { return ""; } }
    }

    private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ 1) Sổ kho (NXT Bột/Sốt — SoCai)

    public sealed record SoKhoLine(string Loai, string Ngay, string MaHang, string Size, string ViTri, double SoLuong, double Net, string ChungTu);

    /// <summary>
    /// Sổ kho: tồn âm (theo mã + size, tính từ lần chốt Tồn đầu gần nhất), xuất mã chưa từng nhập, xuất trước ngày nhập, ngày vô lý,
    /// số lượng xuất lớn gấp ≥ 10 lần thường lệ.
    /// </summary>
    public static List<Finding> CheckSoKho(string label, List<SoKhoLine> all, DateTime today)
    {
        var o = new List<Finding>();
        var lines = all.Where(l => l.Ngay.Length > 0).ToList();
        if (lines.Count == 0) return o;
        // a) Tồn âm: lấy mốc chốt gần nhất (ngày có dòng Tồn đầu muộn nhất) — trước mốc đã gộp vào Tồn đầu
        var moc = lines.Where(l => l.Loai == "tondau").Select(l => l.Ngay).DefaultIfEmpty("").Max()!;
        var bal = new Dictionary<string, (double Sl, double Net, string Ma, string Size)>();
        foreach (var l in lines)
        {
            if (l.MaHang.Length == 0 || string.CompareOrdinal(l.Ngay, moc) < 0) continue;
            if (l.Loai == "tondau" && l.Ngay != moc) continue;
            var sign = l.Loai switch { "tondau" or "nhap" => 1, "xuat" => -1, _ => 0 };
            if (sign == 0) continue;
            var k = l.MaHang.ToUpperInvariant() + "|" + l.Size.ToUpperInvariant();
            var c = bal.GetValueOrDefault(k, (0, 0, l.MaHang, l.Size));
            bal[k] = (c.Sl + sign * (double.IsFinite(l.SoLuong) ? l.SoLuong : 0), c.Net + sign * (double.IsFinite(l.Net) ? l.Net : 0), c.Ma, c.Size);
        }
        var neg = bal.Values.Where(b => b.Sl < -1e-6 || b.Net < -1e-6).OrderBy(b => b.Sl).ToList();
        if (neg.Count > 0)
            o.Add(Mk("nghiep-vu", neg.Count >= 5 ? "do" : "vang", label, $"Tồn âm: {neg.Count} mã xuất nhiều hơn đã nhập",
                $"Tổng thiếu {Nf(-neg.Sum(b => Math.Min(0, b.Sl)))} đơn vị trên {neg.Count} mã (tính từ lần chốt tồn {(moc.Length > 0 ? Dmy(moc) : "đầu sổ")}). Thường do thiếu dòng nhập / tồn đầu, xuất nhầm mã, hoặc gõ thừa số lượng lúc xuất.",
                Cut(neg.Select(b => b.Ma + (b.Size.Length > 0 ? " · size " + b.Size : "") + " → SL " + Nf(b.Sl) + (b.Net != 0 ? " · Net " + Nf(b.Net) : "")), MaxEv)));
        // b) Xuất trước khi có nhập / tồn đầu của cùng mã
        var first = new Dictionary<string, string>(); var firstOut = new Dictionary<string, string>();
        foreach (var l in lines)
        {
            if (l.MaHang.Length == 0) continue;
            var d = l.Loai is "nhap" or "tondau" ? first : l.Loai == "xuat" ? firstOut : null;
            if (d is null) continue;
            if (!d.TryGetValue(l.MaHang, out var cur) || string.CompareOrdinal(l.Ngay, cur) < 0) d[l.MaHang] = l.Ngay;
        }
        var never = new List<string>(); var early = new List<string>();
        foreach (var (k, d) in firstOut)
        {
            if (!first.TryGetValue(k, out var f)) never.Add($"{k} (xuất từ {Dmy(d)})");
            else if (string.CompareOrdinal(d, f) < 0) early.Add($"{k}: xuất {Dmy(d)} nhưng nhập đầu tiên {Dmy(f)}");
        }
        if (never.Count > 0) o.Add(Mk("nghiep-vu", "vang", label, $"Xuất mã hàng chưa từng nhập / tồn đầu ({never.Count} mã)", "Sổ có dòng xuất nhưng không có dòng nhập hay tồn đầu cho mã này — có thể gõ sai mã hoặc chưa nhập tồn đầu.", Cut(never, MaxEv), Hash("sk-never|" + label)));
        if (early.Count > 0) o.Add(Mk("nghiep-vu", "vang", label, $"Ngày xuất sớm hơn ngày nhập ({early.Count} mã)", "Ngày xuất đầu tiên của mã nằm TRƯỚC ngày nhập / tồn đầu đầu tiên — thường nhập nhầm ngày.", Cut(early, MaxEv), Hash("sk-early|" + label)));
        // c) Ngày vô lý
        var hi = Iso(today.AddDays(7));
        var badDate = lines.Where(l => string.CompareOrdinal(l.Ngay, hi) > 0 || string.CompareOrdinal(l.Ngay, "2015-01-01") < 0).ToList();
        if (badDate.Count > 0) o.Add(Mk("nghiep-vu", "vang", label, $"Ngày bất thường: {badDate.Count} dòng", "Có dòng ghi ngày quá xa hôm nay (sau hôm nay hơn 7 ngày hoặc trước năm 2015).",
            Cut(badDate.Select(l => Dmy(l.Ngay) + " · " + l.Loai + " · " + l.MaHang + (l.ChungTu.Length > 0 ? " · CT " + l.ChungTu : "")), MaxEv), Hash("sk-date|" + label)));
        // d) Số lượng xuất lệch hẳn thường lệ
        var spikes = Spikes(lines.Where(l => l.Loai == "xuat" && l.SoLuong > 0 && l.MaHang.Length > 0).Select(l => (l.MaHang, l.SoLuong, $"ngày {Dmy(l.Ngay)}")));
        if (spikes.Count > 0) o.Add(Mk("nghiep-vu", "vang", label, $"Số lượng xuất lớn gấp ≥ 10 lần thường lệ ({spikes.Count} dòng)", "So với trung vị các lần xuất của chính mã hàng đó (mã có ≥ 6 lần xuất). Có thể gõ thừa số 0.", Cut(spikes, MaxEv), Hash("sk-spike|" + label)));
        return o;
    }

    /// <summary>Lần xuất ≥ 10 lần trung vị của chính mã đó (mã có ≥ 6 lần xuất, chênh ≥ 10).</summary>
    private static List<string> Spikes(IEnumerable<(string Ma, double Q, string Where)> xs)
    {
        var res = new List<string>();
        foreach (var g in xs.GroupBy(x => x.Ma))
        {
            var arr = g.ToList(); if (arr.Count < 6) continue;
            var v = arr.Select(x => x.Q).OrderBy(x => x).ToList(); var med = v[v.Count / 2];
            if (!(med > 0)) continue;
            foreach (var x in arr) if (x.Q >= med * 10 && x.Q - med >= 10) res.Add($"{g.Key}: xuất {Nf(x.Q)} (thường ~{Nf(med)}) · {x.Where}");
        }
        return res;
    }

    // ------------------------------------------------------------------ 2) Phiếu xuất (dạng chung)

    public static List<Finding> CheckPhieuNghiepVu(PhieuCol? col, DateTime today)
    {
        var o = new List<Finding>(); const string M = "Phiếu xuất";
        var phieu = col?.Phieu ?? new(); var items = col?.Items ?? new();
        var by = items.GroupBy(i => i.PhieuId).ToDictionary(g => g.Key, g => g.ToList());
        // a) Phiếu giống hệt nhau (cùng module · ngày · mã + SL + vị trí), đều đang trừ tồn → nghi lưu 2 lần
        var sig = new Dictionary<string, List<Phieu>>();
        foreach (var p in phieu)
        {
            if (p.Huy || p.Deducted == false || !by.TryGetValue(p.Id, out var its) || its.Count == 0) continue;
            var s = p.Module + "|" + (p.NgayTao.Length >= 10 ? p.NgayTao[..10] : p.NgayTao) + "|" +
                string.Join(";", its.Select(i => i.MaHang + ":" + (double.IsFinite(Num(i.SoLuongXuat)) ? Num(i.SoLuongXuat) : 0).ToString(CultureInfo.InvariantCulture) + ":" + i.ViTri).OrderBy(x => x, StringComparer.Ordinal));
            if (!sig.TryGetValue(s, out var l)) sig[s] = l = new(); l.Add(p);
        }
        var dups = sig.Values.Where(a => a.Count > 1).ToList();
        if (dups.Count > 0) o.Add(Mk("nghiep-vu", "vang", M, $"Phiếu xuất giống hệt nhau: {dups.Count} nhóm",
            "Cùng module, cùng ngày, cùng mã hàng + số lượng + vị trí nhưng là phiếu khác nhau và đều đang trừ tồn — nghi lưu 2 lần (tồn bị trừ gấp đôi).",
            Cut(dups.Select(a => string.Join(" = ", a.Select(p => p.MaPhieu.Length > 0 ? p.MaPhieu : p.Id)) + $" ({a[0].Module}, {Dmy(a[0].NgayTao)})"), MaxEv)));
        // b) Ngày phiếu vô lý
        var hi = Iso(today.AddDays(2));
        var bad = phieu.Where(p => p.NgayTao.Length >= 10 && (string.CompareOrdinal(p.NgayTao[..10], hi) > 0 || string.CompareOrdinal(p.NgayTao[..10], "2015-01-01") < 0)).ToList();
        if (bad.Count > 0) o.Add(Mk("nghiep-vu", "vang", M, $"Phiếu có ngày bất thường: {bad.Count}", "Ngày phiếu nằm ở tương lai (quá 2 ngày) hoặc trước năm 2015.",
            Cut(bad.Select(p => (p.MaPhieu.Length > 0 ? p.MaPhieu : p.Id) + " · " + p.Module + " · " + Dmy(p.NgayTao)), MaxEv)));
        // c) Số lượng xuất lớn gấp ≥ 10 lần thường lệ
        var pById = phieu.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());
        var sp = Spikes(items.Where(i => Num(i.SoLuongXuat) > 0 && i.MaHang.Length > 0).Select(i =>
        {
            var p = pById.GetValueOrDefault(i.PhieuId);
            return (i.MaHang, Num(i.SoLuongXuat), "phiếu " + (p?.MaPhieu is { Length: > 0 } mp ? mp : i.PhieuId) + " · " + Dmy(p?.NgayTao ?? ""));
        }));
        if (sp.Count > 0) o.Add(Mk("nghiep-vu", "vang", M, $"Phiếu xuất số lượng lớn gấp ≥ 10 lần thường lệ ({sp.Count} dòng)", "So với trung vị các lần xuất của chính mã hàng đó. Có thể gõ thừa số 0.", Cut(sp, MaxEv)));
        return o;
    }

    public static List<Finding> CheckNghiepVu(List<(string Label, List<SoKhoLine> Lines)> soKho, PhieuCol? phieu, DateTime today)
    {
        var o = new List<Finding>();
        foreach (var (label, lines) in soKho) o.AddRange(CheckSoKho(label, lines, today));
        o.AddRange(CheckPhieuNghiepVu(phieu, today));
        if (o.Count == 0) o.Add(Mk("nghiep-vu", "xanh", "Số liệu nghiệp vụ", "Số liệu nghiệp vụ ổn", "Không thấy tồn âm, xuất trước nhập, ngày vô lý, phiếu lưu 2 lần hay số lượng xuất bất thường.", id: "nghiep-vu-on"));
        return o;
    }

    // ------------------------------------------------------------------ 3) KIỂM TRA CHÉO giữa các module (mới ở bản C#)

    /// <summary>Một dòng tồn rút gọn của 1 module (để kiểm tra chéo).</summary>
    public sealed record TonRow(string Ma, string Ten, double Sl, double Kg, string Where = "");
    /// <summary>Bảng tồn của một module: nhãn, ngày dữ liệu (yyyy-mm-dd, có thể rỗng), các dòng.</summary>
    public sealed record TonTable(string Label, string DataDate, List<TonRow> Rows);

    public static string NormCode(string? c)
    {
        var t = Regex.Replace(c ?? "", @"\s+", "").ToUpperInvariant();
        return t.StartsWith("TPC-", StringComparison.Ordinal) ? t[4..] : t;
    }

    /// <summary>
    /// Kiểm tra chéo: (a) tồn âm trong bảng tồn từng module; (b) mã có ở Tồn theo vị trí nhưng không có trong Tồn kho An An cùng ngày dữ liệu
    /// (và ngược lại: mã còn tồn ở An An mà không có vị trí); (c) tổng số lượng theo mã lệch giữa 2 bảng cùng ngày.
    /// </summary>
    public static List<Finding> CheckCheo(TonTable? anAn, TonTable? viTri, TonTable? gui)
    {
        var o = new List<Finding>();
        foreach (var t in new[] { anAn, viTri, gui })
        {
            if (t is null || t.Rows.Count == 0) continue;
            var neg = t.Rows.Where(r => r.Sl < -1e-6 || r.Kg < -1e-6).ToList();
            if (neg.Count > 0) o.Add(Mk("cheo", neg.Count >= 5 ? "do" : "vang", t.Label, $"Tồn âm trong bảng tồn: {neg.Count} dòng",
                "Bảng tồn hiện tại có số lượng / trọng lượng âm — thường do phiếu xuất trừ quá số tồn, hoặc file nạp có dòng âm.",
                Cut(neg.OrderBy(r => r.Sl).Select(r => r.Ma + (r.Where.Length > 0 ? " · " + r.Where : "") + " → " + Nf(r.Sl) + (r.Kg != 0 ? " · " + Nf(r.Kg) + " kg" : "")), MaxEv)));
        }
        if (anAn is { Rows.Count: > 0 } && viTri is { Rows.Count: > 0 })
        {
            if (anAn.DataDate.Length == 0 || anAn.DataDate != viTri.DataDate)
                o.Add(Mk("cheo", "xanh", "Tồn An An ⇄ Tồn theo vị trí", "Chưa so chéo Tồn An An với Tồn theo vị trí",
                    $"Hai bảng khác ngày dữ liệu (An An {Show(anAn.DataDate)}, Vị trí {Show(viTri.DataDate)}) — nạp cả hai file cùng ngày thì Trợ lý mới so được.", id: "cheo-khac-ngay"));
            else
            {
                const string M = "Tồn An An ⇄ Tồn theo vị trí";
                var a = anAn.Rows.Where(r => NormCode(r.Ma).Length == 21).GroupBy(r => NormCode(r.Ma)).ToDictionary(g => g.Key, g => g.Sum(r => r.Sl));
                var v = viTri.Rows.Where(r => NormCode(r.Ma).Length == 21).GroupBy(r => NormCode(r.Ma)).ToDictionary(g => g.Key, g => g.Sum(r => r.Sl));
                var onlyV = v.Where(x => x.Value > 0 && !a.ContainsKey(x.Key)).OrderBy(x => x.Key).ToList();
                var onlyA = a.Where(x => x.Value > 0 && !v.ContainsKey(x.Key)).OrderBy(x => x.Key).ToList();
                var diff = a.Where(x => v.TryGetValue(x.Key, out var y) && Math.Abs(x.Value - y) > 0.5).OrderByDescending(x => Math.Abs(x.Value - v[x.Key])).ToList();
                if (onlyV.Count > 0) o.Add(Mk("cheo", "vang", M, $"Mã có ở Tồn theo vị trí nhưng không có ở Tồn An An: {onlyV.Count} mã",
                    $"Cùng ngày dữ liệu {Dmy(anAn.DataDate)}. Thường do gõ sai mã khi xếp vị trí, hoặc mã đã đổi ở An An.", Cut(onlyV.Select(x => x.Key + " · " + Nf(x.Value) + " ở vị trí"), MaxEv)));
                if (onlyA.Count > 0) o.Add(Mk("cheo", "vang", M, $"Mã còn tồn ở An An nhưng không có vị trí: {onlyA.Count} mã",
                    $"Cùng ngày dữ liệu {Dmy(anAn.DataDate)}. Hàng chưa được xếp / ghi vị trí trong kho (hoặc ở kho khác không theo dõi vị trí).", Cut(onlyA.Select(x => x.Key + " · tồn " + Nf(x.Value)), MaxEv)));
                if (diff.Count > 0) o.Add(Mk("cheo", "vang", M, $"Tổng số lượng theo mã lệch giữa An An và Vị trí: {diff.Count} mã",
                    $"Cùng ngày dữ liệu {Dmy(anAn.DataDate)}: cộng các vị trí của mã không bằng tồn ở An An.", Cut(diff.Select(x => $"{x.Key}: An An {Nf(x.Value)} · vị trí {Nf(v[x.Key])} (lệch {Nf(v[x.Key] - x.Value)})"), MaxEv)));
            }
        }
        if (o.Count == 0 && (anAn?.Rows.Count > 0 || viTri?.Rows.Count > 0 || gui?.Rows.Count > 0))
            o.Add(Mk("cheo", "xanh", "Kiểm tra chéo", "Các bảng tồn khớp nhau", "Không có tồn âm; Tồn An An và Tồn theo vị trí cùng ngày khớp theo mã.", id: "cheo-on"));
        return o;
    }

    private static string Show(string iso) => iso.Length > 0 ? Dmy(iso) : "chưa rõ";
}
