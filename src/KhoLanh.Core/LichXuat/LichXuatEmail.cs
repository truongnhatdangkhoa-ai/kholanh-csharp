using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.LichXuat;

/// <summary>
/// NHẬN DIỆN EMAIL XUẤT HÀNG → đề xuất cập nhật Lịch xuất (viết lại theo core/mail.js › emBuildRows / emPhanLoai và
/// js/lich-xuat-cont.js › emGuard của app cũ). HÀM THUẦN: nhận thư đã đọc, trả về đề xuất; không đọc hộp thư, không ghi gì.
/// Quy tắc: chuỗi thư có LSX trên tiêu đề + từ khóa xuất hàng. Thư GỐC = tạo lịch (ngày = ETD trên tiêu đề); thư TRẢ LỜI →
/// "Thông tin xe" (số cont / xe) · đổi lịch · hủy lịch; thư chỉ để xem bị bỏ.
/// </summary>
public static class LichXuatEmail
{
    public static readonly string[] DefaultKeywords = { "loading", "keo dong", "keo hang", "cont", "container", "booking", "etd", "eta", "xuat", "nhap", "stuffing", "shipping", "giao hang", "lay hang", "xe " };
    private static readonly Regex EmLsx = new(@"\b\d{2}\.\d{2}\.\d{2,3}[A-Za-z]?\b");

    public sealed record Msg(string Id, string ThreadId, DateTime Date, string From, string Subject, string Text);

    /// <summary>Một đề xuất từ 1 thư. HanhDong: tao · doi · huy · xe.</summary>
    public sealed class Row
    {
        public string Id { get; set; } = "";
        public string ThreadId { get; set; } = "";
        public DateTime NhanLuc { get; set; }
        public string NguoiGui { get; set; } = "";
        public string TieuDe { get; set; } = "";
        public string Lsx { get; set; } = "";
        public string HanhDong { get; set; } = "khac";
        public string Ngay { get; set; } = "";
        public string Gio { get; set; } = "";
        public string GhiChu { get; set; } = "";
        public string CanCu { get; set; } = "";
        /// <summary>"" = chờ duyệt · "Đã duyệt" · "Bỏ qua" · "Lỗi - …".</summary>
        public string TrangThai { get; set; } = "";
        public DateTime? XuLyLuc { get; set; }
        public string KetQua { get; set; } = "";
        /// <summary>Bản chụp các lịch trước khi duyệt (JSON) — để hoàn tác.</summary>
        public string Undo { get; set; } = "";
        public bool TuDong { get; set; }
        public string HanhDongTen => Act.GetValueOrDefault(HanhDong, HanhDong);
    }

    public static readonly Dictionary<string, string> Act = new() { ["tao"] = "Tạo lịch", ["doi"] = "Đổi lịch", ["huy"] = "Hủy lịch", ["xe"] = "Thông tin xe", ["khac"] = "Khác" };

    public static string Norm(string? s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD); var sb = new StringBuilder(d.Length);
        foreach (var c in d) if (c < '̀' || c > 'ͯ') sb.Append(c);
        return sb.ToString().Replace('đ', 'd').Replace('Đ', 'D').ToLowerInvariant();
    }

    private static string Pad(int n) => n.ToString("00");

    /// <summary>dd/mm[/yyyy] → yyyy-mm-dd; thiếu năm → năm của thư (rơi quá 90 ngày TRƯỚC ngày thư thì hiểu là năm sau).</summary>
    public static string EmNgay(string dd, string mm, string? yy, DateTime refDt)
    {
        if (!int.TryParse(dd, out var d) || !int.TryParse(mm, out var m)) return "";
        var y = string.IsNullOrEmpty(yy) ? refDt.Year : int.Parse(yy);
        if (!string.IsNullOrEmpty(yy) && y < 100) y += 2000;
        if (!(d >= 1 && d <= 31 && m >= 1 && m <= 12)) return "";
        try
        {
            if (string.IsNullOrEmpty(yy) && new DateTime(y, m, d) < refDt.AddDays(-90)) y++;
            _ = new DateTime(y, m, d);
        }
        catch { return ""; }
        return $"{y}-{Pad(m)}-{Pad(d)}";
    }

    /// <summary>Chỉ phần chữ MỚI của thư (cắt phần trích dẫn thư cũ).</summary>
    public static string PhanMoi(string? body)
    {
        var t = (body ?? "").Replace("\r", ""); var cut = t.Length;
        foreach (var re in new[]
        {
            new Regex(@"^[ \t]*V[àa]o\b[\s\S]{0,300}?(?:đã viết|viết)\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            new Regex(@"^[ \t]*On\b[\s\S]{0,300}?wrote\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            new Regex(@"^[ \t]*>", RegexOptions.Multiline),
            new Regex(@"^[ \t]*-{2,}\s*(?:Original|Forwarded|Tin nhắn|Thư)", RegexOptions.IgnoreCase | RegexOptions.Multiline),
            new Regex(@"^[ \t]*_{5,}", RegexOptions.Multiline),
            new Regex(@"^[ \t]*(?:From|Từ)\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline),
        })
        { var m = re.Match(t); if (m.Success && m.Index < cut) cut = m.Index; }
        return Regex.Replace(Regex.Replace(t[..cut], @"[ \t]+", " "), @"\n{2,}", "\n").Trim();
    }

    // CHỈ coi là thư đổi / hủy khi CÂU có ngày nói về kéo đóng / xuất hàng / lấy hàng (không tính câu bàn gate-in / hạ cont / phí / cut-off)
    private static readonly Regex CueLich = new(@"keo dong|dong hang|keo cont|keo hang|xuat hang|lay hang|loading|stuffing|doi (lai|sang|qua|ngay|lich|gio|keo)|chuyen (sang|ngay|qua)|\blui (lai|sang|qua|ngay)|doi\s+\d|cont nay");
    private static readonly Regex NotLich = new(@"gate ?-?in|\bha (cont|hang|vo|tau)|\bha truoc|\busd\b|\bphi\b|cuoc|invoice|thanh toan");
    private static readonly Regex Strong = new(@"keo dong|dong hang|xuat hang|lay hang|loading|doi (lai|sang|qua|ngay|lich|gio|keo)");
    public static bool CauLich(string c) => CueLich.IsMatch(c) && !(NotLich.IsMatch(c) && !Strong.IsMatch(c));
    private static IEnumerable<string> Cau(string text) => Regex.Split(text ?? "", @"\n|;|\.\s+").Select(x => x.Trim()).Where(x => x.Length > 0);

    public static (string HanhDong, string Ngay, string Gio) PhanTichTraLoi(string text, DateTime refDt)
    {
        var cau = Cau(text).Select(Norm).Where(CauLich).ToList();
        if (cau.Count == 0) return ("khac", "", "");
        var t = string.Join(" . ", cau); string ngay = "", gio = "";
        var md = Regex.Match(t, @"ngay\s*(\d{1,2})\s*[\/\-.]\s*(\d{1,2})(?:\s*[\/\-.]\s*(\d{2,4}))?");
        if (md.Success) ngay = EmNgay(md.Groups[1].Value, md.Groups[2].Value, md.Groups[3].Success ? md.Groups[3].Value : null, refDt);
        var mg = Regex.Match(t, @"\b(\d{1,2})\s*(?:h|:|gio)\s*(\d{2})?(?![a-z0-9])");
        if (mg.Success && int.Parse(mg.Groups[1].Value) < 24 && (!mg.Groups[2].Success || int.Parse(mg.Groups[2].Value) < 60))
            gio = Pad(int.Parse(mg.Groups[1].Value)) + ":" + (mg.Groups[2].Success ? mg.Groups[2].Value : "00");
        var hd = Regex.IsMatch(t, @"\b(huy|cancel|cancelled)\b") ? "huy" : ngay.Length > 0 ? "doi" : "khac";
        return (hd, ngay, gio);
    }

    public static bool LaThongTinXe(string text)
    {
        var t = Norm(text); var n = 0;
        foreach (var re in new[] { @"(ho ten tai xe|tai xe)\s*:", @"(so dien thoai|sdt|dt)\s*:", @"(so xe|bien so)\s*:", @"(so cont|cont so|so container)\s*:", @"so seal\s*:", @"(so booking|booking)\s*:", @"nhiet do[^:\n]{0,30}:" })
            if (Regex.IsMatch(t, re)) n++;
        return n >= 3;
    }
    public static bool LaThongBaoList(string text) => Regex.IsMatch(Norm(text), @"gui list|list xuat hang|nguoi nhan hang|cont\s*/\s*xe\s*:|seal\s*:");
    public static bool GoiYDoiLich(string text) => Regex.IsMatch(Norm(text), @"cut ?-?off|update|thay doi|doi (lai|ngay|lich|gio|sang|qua|keo)|\blui\b|som hon|tam hoan|hoan (lai|keo|xuat)|delay|postpone|\betd\b|closing|keo dong");
    public static string TomTatLich(string text)
    {
        var o = (text ?? "").Split('\n').Where(l => Regex.IsMatch(l, @"\b(ETA|ETD|closing|cut ?-?off)\b|hrs\b", RegexOptions.IgnoreCase)).Select(l => l.Trim()).ToList();
        var s = Regex.Replace(o.Count > 0 ? string.Join(" | ", o) : (text ?? "").Replace('\n', ' '), @"\s+", " ");
        return s.Length > 400 ? s[..400] : s;
    }

    public sealed record PhanLoaiKq(string HanhDong, string Ngay, string Gio, string GhiChu, string Loai, string CanCu);

    /// <summary>Phân loại 1 thư TRẢ LỜI: xe · list (thông báo gửi list, bỏ) · goiy (gợi ý đổi lịch, chờ người chọn ngày) · traloi (đổi / hủy) · none.</summary>
    public static PhanLoaiKq PhanLoai(string txt, DateTime dt)
    {
        if (LaThongTinXe(txt)) return new("xe", "", "", Cap(Regex.Replace(Regex.Replace(txt, @"[ \t]+", " "), @"\n\s*", "\n"), 900), "xe", "");
        if (LaThongBaoList(txt)) return new("khac", "", "", "", "list", "");
        var r = PhanTichTraLoi(txt, dt);
        if (r.HanhDong == "khac" && GoiYDoiLich(txt)) return new("doi", "", "", TomTatLich(txt), "goiy", "");
        var gc = Cap(Regex.Replace(txt, @"\s*\n\s*", " "), 300);
        return new(r.HanhDong, r.Ngay, r.Gio, gc, r.HanhDong == "khac" ? "none" : "traloi",
            r.HanhDong == "khac" ? "" : Cap(string.Join(" / ", Cau(txt).Where(c => CauLich(Norm(c)))), 260));
    }
    private static string Cap(string s, int n) => s.Length > n ? s[..n] : s;

    /// <summary>Ô «Thử đọc thư»: dán nội dung (và tiêu đề) → app cho biết SẼ LÀM GÌ, không ghi gì.</summary>
    public static PhanLoaiKq ThuDoc(string text, string tieuDe, DateTime refDt)
    {
        var sub = (tieuDe ?? "").Trim();
        if (sub.Length > 0 && !Regex.IsMatch(sub, @"^\s*(re|fw|fwd|tr)\s*:", RegexOptions.IgnoreCase) && EmLsx.IsMatch(sub))
        {
            var e = Regex.Match(sub, @"ETD\s*:?\s*(\d{1,2})\s*[\/.\-]\s*(\d{1,2})(?:\s*[\/.\-]\s*(\d{2,4}))?", RegexOptions.IgnoreCase);
            return new("tao", e.Success ? EmNgay(e.Groups[1].Value, e.Groups[2].Value, e.Groups[3].Success ? e.Groups[3].Value : null, refDt) : "", "", "", "goc", e.Success ? e.Value : "");
        }
        return PhanLoai(PhanMoi(text), refDt);
    }

    /// <summary>Thư → đề xuất (chưa lọc thư đã xử lý). Chuỗi thư gom theo ThreadId; tiêu đề thư đầu phải có LSX + từ khóa.</summary>
    public static List<Row> BuildRows(IEnumerable<Msg> msgs, IReadOnlyList<string>? keywords, IReadOnlyList<string>? allow, int days, DateTime now)
    {
        var kws = (keywords is { Count: > 0 } ? keywords : DefaultKeywords).Select(k => Norm(k).TrimStart()).Where(k => k.Length > 0).ToList();
        var allowL = (allow ?? Array.Empty<string>()).Select(a => a.Trim().ToLowerInvariant()).Where(a => a.Length > 0).ToList();
        var cutoff = now.AddDays(-(days <= 0 ? 7 : days));
        var rows = new List<Row>();
        foreach (var g in msgs.GroupBy(m => m.ThreadId.Length > 0 ? m.ThreadId : m.Id))
        {
            var list = g.OrderBy(m => m.Date).ToList();
            var first = list[0].Subject ?? "";
            var codes = EmLsx.Matches(first).Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();
            if (codes.Count == 0) continue;
            var t = " " + Norm(first) + " ";
            if (!(kws.Count == 1 && kws[0] == "*") && !kws.Any(k => t.Contains(k))) continue;
            var lsx = string.Join(" + ", codes);
            var subj = Regex.Replace(first, @"^\s*((re|fw|fwd|tr)\s*:\s*)+", "", RegexOptions.IgnoreCase).Trim();
            for (var i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m.Date < cutoff) continue;
                if (allowL.Count > 0 && !allowL.Any(a => m.From.ToLowerInvariant().Contains(a))) continue;
                var rec = new Row { Id = m.Id, ThreadId = g.Key, NhanLuc = m.Date, NguoiGui = m.From.Replace("\"", "").Replace("'", "").Trim(), TieuDe = Cap(subj, 250), Lsx = lsx };
                if (i == 0 && !Regex.IsMatch(m.Subject ?? "", @"^\s*(re|fw|fwd|tr)\s*:", RegexOptions.IgnoreCase))
                {
                    rec.HanhDong = "tao";
                    var e = Regex.Match(subj, @"ETD\s*:?\s*(\d{1,2})\s*[\/.\-]\s*(\d{1,2})(?:\s*[\/.\-]\s*(\d{2,4}))?", RegexOptions.IgnoreCase);
                    if (e.Success) { rec.Ngay = EmNgay(e.Groups[1].Value, e.Groups[2].Value, e.Groups[3].Success ? e.Groups[3].Value : null, m.Date); rec.CanCu = e.Value; }
                    rec.GhiChu = Cap(string.Join(" · ", Regex.Split(subj, @"\s+-\s+").Where(p => !Regex.IsMatch(p.Trim(), "^AN AN$", RegexOptions.IgnoreCase) && !Regex.IsMatch(p, @"\bLSX\b", RegexOptions.IgnoreCase))), 300);
                }
                else
                {
                    var c = PhanLoai(PhanMoi(m.Text), m.Date);
                    rec.HanhDong = c.HanhDong; rec.Ngay = c.Ngay; rec.Gio = c.Gio; rec.GhiChu = c.GhiChu; rec.CanCu = c.CanCu;
                }
                if (rec.HanhDong != "khac") rows.Add(rec);
            }
        }
        return rows.OrderBy(r => r.NhanLuc).ToList();
    }

    public static List<string> CodesOf(string lsx) => EmLsx.Matches(lsx ?? "").Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();

    public static bool Trusted(string from, string trust) =>
        Regex.Split(trust ?? "", @"[,;\s]+").Where(a => a.Length > 0).Any(a => (from ?? "").Contains(a, StringComparison.OrdinalIgnoreCase));

    /// <summary>Trả "" nếu thư của người tin cậy được TỰ xử lý, ngược lại lý do giữ lại chờ duyệt (lưới an toàn của app cũ).</summary>
    public static string Guard(Row r, IReadOnlyList<Lich> rows, DateTime now)
    {
        var t = LichXuatCore.Iso(now); var lim = LichXuatCore.Iso(now.AddDays(60));
        if (r.HanhDong is not ("tao" or "doi" or "huy")) return "không phải thư tạo / đổi / hủy";
        if (r.HanhDong != "tao" && !CueLich.IsMatch(Norm(r.GhiChu)) && !Regex.IsMatch(Norm(r.GhiChu), @"\blui |cont nay")) return "thư không nói rõ về lịch kéo đóng / xuất hàng";
        if (!LichXuatCore.IsIso(r.Ngay)) return "thư không ghi rõ ngày";
        if (string.CompareOrdinal(r.Ngay, t) < 0 || string.CompareOrdinal(r.Ngay, lim) > 0) return "ngày ngoài khoảng hôm nay → 60 ngày tới";
        var codes = CodesOf(r.Lsx); if (codes.Count == 0) return "không đọc được LSX";
        if (r.HanhDong == "huy")
        {
            foreach (var c in codes)
                if (!rows.Any(x => LichXuatCore.SharesLsx(x, new[] { LichXuatCore.Sq(c) }) && x.Ngay == r.Ngay)) return "LSX " + c + " không có lịch ngày " + LichXuatCore.Vn(r.Ngay);
            return "";
        }
        foreach (var p in Plan(r, r.Ngay, rows))
        {
            if (p.Kind == PlanKind.Choose) return "LSX " + p.Code + " đã có lịch ngày " + string.Join(", ", p.Cands.Select(x => LichXuatCore.Vn(x.Ngay))) +
                " (có thể đã sửa tay, hoặc nhiều cont) — duyệt tay để chọn chuyển lịch cũ hay giữ cả hai";
            if (r.HanhDong == "doi" && p.Kind == PlanKind.Add) return "LSX " + p.Code + " chưa có lịch";
        }
        return "";
    }

    /// <summary>Cách áp 1 số LSX của thư tạo / đổi lịch: Add = thêm lịch mới · SameDay = đã có lịch đúng ngày (chỉ cập nhật giờ) ·
    /// Move = chuyển lịch cũ (<see cref="PlanItem.Target"/>) sang ngày thư · Choose = không chắc → người dùng chọn (chuyển lịch nào / giữ cả hai / bỏ qua).</summary>
    public enum PlanKind { Add, SameDay, Move, Choose }
    public sealed record PlanItem(string Code, PlanKind Kind, Lich? Target, List<Lich> Cands);

    /// <summary>
    /// 1.90 — khớp thư với lịch đang có THEO SỐ LSX (kể cả lịch người dùng đã sửa tay ngày / giờ, cont ghép "A + B"), không theo ngày:
    /// • đã có lịch đúng ngày thư → giữ (cập nhật giờ) · • chưa có lịch nào → thêm
    /// • thư ĐỔI lịch, LSX có đúng 1 lịch chưa xuất ở ngày khác → chuyển lịch đó (giữ giờ / ghi chú / việc / số cont đã sửa tay)
    /// • còn lại (thư TẠO lịch mà LSX đã có lịch ngày khác; nhiều lịch chưa xuất; chỉ còn lịch đã xuất) → không tự làm, hỏi người dùng.
    /// </summary>
    public static List<PlanItem> Plan(Row r, string ngay, IEnumerable<Lich> rows)
    {
        var all = rows.Where(x => !x.IsNhap).ToList(); var o = new List<PlanItem>();
        foreach (var c in CodesOf(r.Lsx))
        {
            var k = new[] { LichXuatCore.Sq(c) };
            var mine = all.Where(x => LichXuatCore.SharesLsx(x, k)).ToList();
            if (mine.FirstOrDefault(x => x.Ngay == ngay) is { } same) { o.Add(new(c, PlanKind.SameDay, same, new())); continue; }
            var open = mine.Where(x => x.TrangThai != "ok").OrderBy(x => x.Ngay, StringComparer.Ordinal).ToList();
            var cands = open.Count > 0 ? open : mine.OrderByDescending(x => x.Ngay, StringComparer.Ordinal).Take(3).ToList();
            if (cands.Count == 0) o.Add(new(c, PlanKind.Add, null, new()));
            else if (r.HanhDong == "doi" && open.Count == 1) o.Add(new(c, PlanKind.Move, open[0], cands));
            else o.Add(new(c, PlanKind.Choose, null, cands));
        }
        return o;
    }
}
