using System.Globalization;
using System.Text.RegularExpressions;
using KhoLanh.Core.PhieuKho;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// «VIỆC CẦN CHÚ Ý HÔM NAY» (theo core/tro-ly/chu-y.js của app cũ, mở rộng cho bản C#). HÀM THUẦN, chỉ đọc.
/// Gom việc còn dang dở từ các module để Quản trị nhìn một chỗ — chỉ NÊU việc, không tự sửa gì. Mỗi việc có Go = khóa màn hình để mở nhanh.
/// </summary>
public static class TroLyChuY
{
    public sealed record Viec(string Id, string Sev, string Mod, string Title, string Detail, string Go);

    public sealed record LichRow(string Id, string Ngay, string Loai, string TrangThai, string Cont, string ContSo, string Lsx);
    public sealed record PnxPhieu(string Loai, string Lsx, string Ngay, string Cont);
    public sealed record VtSlip(string Id, string MaPhieu, string Loai, DateTime? CreatedAt, bool Deducted);
    /// <summary>Ngày dữ liệu của một bảng tồn (yyyy-mm-dd, rỗng = chưa rõ).</summary>
    public sealed record DataAge(string Label, string Iso, int Rows, string Go);

    public sealed class Input
    {
        public DateTime Today = DateTime.Today;
        public List<LichRow> Lich = new();
        public List<string> ListLsx = new();
        public List<PnxPhieu> Phieu = new();
        public List<VtSlip> VtSlips = new();
        public bool LanEnabled;
        public int LanHealth;
        public int LanPending;
        public double LanPendingAgeMin = double.NaN;
        public int LanConflicts;
        public bool HasBackupToday = true;
        public List<DataAge> Data = new();
        public int ErrorsToday;
        public int StaleDays = 2;
    }

    // ------------------------------------------------------------------ LSX (giống html/js/phieu-nhap-xuat.js)

    private static readonly Regex LsxTen = new(@"^[A-Z][A-Z0-9]{1,11}$");
    private static readonly HashSet<string> LsxTenBo = new() { "OK", "NA", "TBD", "XK", "NK", "LSX", "CONT", "HUY", "HD" };

    /// <summary>Tách các mã LSX trong ô (26.09.038 / 26.09.038A / tên khách viết hoa như ISHI), bỏ "LSX:" và đuôi ngày "- 22/10".</summary>
    public static List<string> LsxCodes(string? v)
    {
        var o = new List<string>(); var seen = new HashSet<string>();
        foreach (var t in Regex.Split(v ?? "", @"[+,;&\n]"))
        {
            var s = Regex.Replace(t.Trim(), @"^LSX\s*[:\-]?\s*", "", RegexOptions.IgnoreCase).Trim();
            s = Regex.Replace(s, @"\s*-\s*\d{1,2}\s*[\/.]\s*\d{1,2}(?:\s*[\/.]\s*\d{2,4})?\s*$", "");
            var m = Regex.Match(s, @"^\d{2}\.\d{2}\.\d{2,3}(?:[A-Za-z]+|\s[A-Z](?![A-Za-z]))?(?:-[A-Za-z0-9]{1,3})*");
            if (!m.Success)
            {
                if (LsxTen.IsMatch(s) && !LsxTenBo.Contains(s) && seen.Add(s)) o.Add(s);
                continue;
            }
            var c = Regex.Replace(m.Value, @"\s+", "").ToUpperInvariant();
            if (seen.Add(c)) o.Add(c);
        }
        return o;
    }

    /// <summary>So LSX bỏ số 0 đầu: "26.08.068" = "26.8.068" (hậu tố chữ vẫn phân biệt).</summary>
    public static string LsxCmp(string? v)
    {
        var t = Regex.Replace(v ?? "", @"\s+", "").ToUpperInvariant();
        var m = Regex.Match(t, @"^(\d{1,2})\.(\d{1,2})\.(\d{1,3})([A-Z]*)$");
        return m.Success ? $"{int.Parse(m.Groups[1].Value)}.{int.Parse(m.Groups[2].Value)}.{int.Parse(m.Groups[3].Value)}{m.Groups[4].Value}" : t;
    }

    private static string K(string? v) => Regex.Replace(v ?? "", @"\s+", "").ToUpperInvariant();
    private static string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    private static string Dm(string iso) => Regex.Match(iso, @"^\d{4}-(\d{2})-(\d{2})") is { Success: true } m ? m.Groups[2].Value + "/" + m.Groups[1].Value : iso;
    private static string Join(IEnumerable<string> a, int n) { var l = a.ToList(); return string.Join(" · ", l.Take(n)) + (l.Count > n ? " …" : ""); }

    public static List<Viec> Build(Input inp)
    {
        var items = new List<Viec>();
        var today = Iso(inp.Today); var tmr = Iso(inp.Today.AddDays(1));

        // 1) Lịch xuất khẩu hôm nay + ngày mai chưa chuẩn bị đủ: thiếu List / Phiếu xuất kho / Số cont
        if (inp.Lich.Count > 0)
        {
            var lsxSet = inp.ListLsx.Select(K).ToHashSet(); var cmpSet = inp.ListLsx.Select(LsxCmp).ToHashSet();
            var grp = inp.Lich.Where(x => x.Loai != "nhap" && x.TrangThai != "ok" && (x.Ngay == today || x.Ngay == tmr))
                .GroupBy(x => x.Ngay + "|" + (x.Cont.Length > 0 ? x.Cont : x.Id));
            var missToday = new List<string>(); var missTmr = new List<string>();
            foreach (var g in grp)
            {
                var codes = g.SelectMany(z => LsxCodes(z.Lsx)).Distinct().ToList();
                if (codes.Count == 0) continue;
                var ngay = g.First().Ngay;
                var want = codes.Select(PhieuKhoCore.LsxNorm).ToHashSet();
                var ps = inp.Phieu.Where(p => p.Loai == "xuat_khau" && PhieuKhoCore.LsxList(p.Lsx).Any(c => want.Contains(PhieuKhoCore.LsxNorm(c))))
                    .Where(p => !(p.Ngay.Length > 0 && string.CompareOrdinal(p.Ngay, ngay) < 0)).ToList();
                var list = codes.All(c => lsxSet.Contains(K(c)) || cmpSet.Contains(LsxCmp(c)));
                var cont = ps.Any(p => p.Cont.Trim().Length > 0) || g.Any(z => z.ContSo.Trim().Length > 0);
                var thieu = new List<string>();
                if (!list) thieu.Add("List"); if (ps.Count == 0) thieu.Add("Phiếu xuất kho"); if (!cont) thieu.Add("Số cont");
                if (thieu.Count > 0) (ngay == today ? missToday : missTmr).Add("LSX " + string.Join("+", codes) + " thiếu " + string.Join(", ", thieu));
            }
            if (missToday.Count > 0) items.Add(new("lich-hom-nay", "do", "Lịch xuất khẩu", $"Hôm nay có {missToday.Count} cont chưa chuẩn bị đủ", Join(missToday, 6), "m13"));
            if (missTmr.Count > 0) items.Add(new("lich-ngay-mai", "vang", "Lịch xuất khẩu", $"Ngày mai ({Dm(tmr)}) có {missTmr.Count} cont chưa chuẩn bị đủ", Join(missTmr, 6), "m13"));
        }

        // 2) Phiếu xuất vị trí đang trừ tồn nhưng CHƯA chọn loại (Thay thùng / Xuất xả) — 7 ngày gần đây
        var from = inp.Today.AddDays(-7);
        var noLoai = inp.VtSlips.Where(s => s.Deducted && s.Loai.Trim().Length == 0 && s.CreatedAt is { } d && d.Date >= from).ToList();
        if (noLoai.Count > 0) items.Add(new("vt-chua-loai", "vang", "Tồn kho theo vị trí", $"{noLoai.Count} phiếu xuất vị trí chưa chọn loại",
            "Chọn «Phiếu thay thùng» hoặc «Phiếu xuất xả» để phiếu vào đúng bản lưu trữ ngày: " + string.Join(", ", noLoai.Take(8).Select(s => s.MaPhieu.Length > 0 ? s.MaPhieu : s.Id)) + (noLoai.Count > 8 ? " …" : ""), "m7"));

        // 3) Mạng LAN: mất máy chủ, sổ chờ tồn đọng, xung đột chờ Quản trị
        if (inp.LanEnabled)
        {
            if (inp.LanHealth == 2) items.Add(new("lan-mat-may-chu", "do", "Mạng LAN", "Không nối được máy chủ LAN",
                "Máy này đang làm riêng" + (inp.LanPending > 0 ? $", {inp.LanPending} bảng chờ gửi lên" : "") + ". Kiểm tra máy chủ còn bật và cùng mạng.", "lan"));
            else if (inp.LanPending > 0 && inp.LanPendingAgeMin > 30) items.Add(new("lan-so-cho", "vang", "Mạng LAN", $"{inp.LanPending} bảng chờ gửi lên máy chủ quá 30 phút",
                $"Lâu nhất {Math.Round(inp.LanPendingAgeMin)} phút — mở Đồng bộ & sao lưu › Máy chủ xem máy chủ có nhận không.", "lan"));
            if (inp.LanConflicts > 0) items.Add(new("lan-xung-dot", "vang", "Mạng LAN", $"{inp.LanConflicts} xung đột gộp dữ liệu chờ Quản trị xem",
                "Hai máy cùng sửa một bảng: chọn giữ bản máy chủ / dùng bản máy con, hoặc đánh dấu đã xem.", "lan"));
        }

        // 4) Sao lưu tự động hôm nay chưa có
        if (!inp.HasBackupToday) items.Add(new("sao-luu-hom-nay", "vang", "Sao lưu", "Hôm nay chưa có bản sao lưu tự động",
            "App tự sao lưu mỗi ngày lúc mở. Mở Đồng bộ & sao lưu › Sao lưu để sao lưu ngay (hoặc xem vì sao bản tự động lỗi).", "saoluu"));

        // 5) Bảng tồn lâu chưa nạp file mới
        foreach (var d in inp.Data)
        {
            if (d.Rows == 0) continue;
            if (d.Iso.Length == 0) continue;
            var age = (inp.Today - DateTime.ParseExact(d.Iso, "yyyy-MM-dd", CultureInfo.InvariantCulture)).TotalDays;
            if (age > inp.StaleDays) items.Add(new("du-lieu-cu|" + d.Label, age > 7 ? "vang" : "xam", d.Label, $"{d.Label}: dữ liệu ngày {Dm(d.Iso)} (cũ {Math.Round(age)} ngày)",
                "Chưa nạp file tồn mới — số liệu đối chiếu, gợi ý vị trí và Trợ lý đang dùng số cũ.", d.Go));
        }

        // 6) Lỗi phần mềm hôm nay
        if (inp.ErrorsToday > 0) items.Add(new("loi-hom-nay", inp.ErrorsToday >= 5 ? "do" : "vang", "Phần mềm", $"Hôm nay phần mềm gặp {inp.ErrorsToday} lỗi",
            "Xem mục «Lỗi phần mềm» trong kết quả quét, chọn rồi sao chép báo cáo cho Claude.", "troly"));

        var rank = new Dictionary<string, int> { ["do"] = 0, ["vang"] = 1, ["xam"] = 2 };
        return items.OrderBy(i => rank.GetValueOrDefault(i.Sev, 3)).ToList();
    }
}
