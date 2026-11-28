using System.Globalization;
using System.Text;
using static KhoLanh.Core.TroLy.TroLyChecks;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// 1.186 — Trợ lý hệ thống nâng cấp (6 hướng, HÀM THUẦN, test bằng dữ liệu giả; TroLyService.MoRong.cs lo thu thập):
/// (1) kiểm tra nghiệp vụ lịch xuất / phiếu xuất khẩu · (2) chẩn đoán đồng bộ giữa các máy · (3) giám sát email lịch xuất ·
/// (4) khóa Sửa nhanh mới (<see cref="TroLyFix"/>) · (5) tự kiểm sau cập nhật · (6) báo cáo tuần cho Quản trị.
/// </summary>
public static class TroLyMoRong
{
    public const string CatXk = "nghiep-vu", CatMail = "email", CatCapNhat = "sau-cap-nhat";

    private static Finding Go(Finding f, string key, string label) { f.Go = key; f.GoLabel = label; return f; }
    private static string D(string iso) => iso.Length >= 10 ? Dmy(iso[..10]) : iso;

    // ------------------------------------------------------------------ 1) Lịch xuất / phiếu xuất khẩu

    /// <summary>Lịch xuất rút gọn: ngày yyyy-mm-dd, số LSX, đã xuất chưa, đã có phiếu xuất khẩu chưa.</summary>
    public sealed record XkLich(string Ngay, string Lsx, bool Xuat, bool HasPhieu);
    /// <summary>Phiếu xuất khẩu rút gọn.</summary>
    public sealed record XkPhieu(string SoPhieu, string Ngay, string Lsx, string Cont, string SoXe, string TaiXe, string Seal);

    public static List<Finding> CheckXuatKhau(IReadOnlyList<XkLich> lich, IReadOnlyList<XkPhieu> phieu, DateTime today)
    {
        var o = new List<Finding>(); const string M = "Lịch xuất · Phiếu xuất khẩu";
        string Iso(DateTime d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var t0 = Iso(today); var t1 = Iso(today.AddDays(1)); var back = Iso(today.AddDays(-7));
        var ls = lich.Select(l => l with { Ngay = TroLyNghiepVu.IsoDate(l.Ngay) }).Where(l => l.Ngay.Length > 0).ToList();
        var ps = phieu.Select(p => p with { Ngay = TroLyNghiepVu.IsoDate(p.Ngay) }).ToList();

        var chua = ls.Where(l => !l.Xuat && !l.HasPhieu && (l.Ngay == t0 || l.Ngay == t1)).OrderBy(l => l.Ngay).ToList();
        if (chua.Count > 0) o.Add(Go(Mk(CatXk, "vang", M, $"Lịch xuất hôm nay / ngày mai chưa có phiếu xuất: {chua.Count} lịch",
            "Lịch chưa xuất và chưa có Phiếu xuất khẩu. Bấm «Tạo phiếu xuất» ở Tổng quan (app tự quét lại email để điền thông tin xe).",
            Cut(chua.Select(l => $"{l.Lsx} · {D(l.Ngay)}")), id: "xk-chua-phieu"), "lx", "Tổng quan"));

        var qua = ls.Where(l => !l.Xuat && string.CompareOrdinal(l.Ngay, t0) < 0 && string.CompareOrdinal(l.Ngay, back) >= 0).OrderBy(l => l.Ngay).ToList();
        if (qua.Count > 0) o.Add(Go(Mk(CatXk, "vang", M, $"Lịch đã qua ngày (7 ngày gần đây) vẫn chưa đánh dấu đã xuất: {qua.Count} lịch",
            "Hàng đã đi mà lịch còn «Dự kiến / Đang đóng hàng» — hoặc lịch cần dời ngày / hủy.", Cut(qua.Select(l => $"{l.Lsx} · {D(l.Ngay)}")), id: "xk-qua-ngay"), "lx", "Tổng quan"));

        var thieu = ps.Where(p => p.Ngay == t0 || p.Ngay == t1 || p.Ngay == Iso(today.AddDays(-1)))
            .Select(p => (P: p, Miss: new[] { (p.SoXe, "số xe"), (p.Cont, "số cont"), (p.TaiXe, "tài xế") }.Where(x => string.IsNullOrWhiteSpace(x.Item1)).Select(x => x.Item2).ToList()))
            .Where(x => x.Miss.Count > 0).ToList();
        if (thieu.Count > 0) o.Add(Go(Mk(CatXk, "vang", M, $"Phiếu xuất khẩu hôm qua / nay / mai thiếu thông tin xe: {thieu.Count} phiếu",
            "Phiếu chưa có số xe, số cont hoặc tài xế. Xe thường báo qua email «Thông tin xe» — kiểm tra mục Email (Tổng quan) hoặc nhập tay.",
            Cut(thieu.Select(x => $"{(x.P.SoPhieu.Length > 0 ? x.P.SoPhieu : x.P.Lsx)} ({D(x.P.Ngay)}): thiếu {string.Join(", ", x.Miss)}")), id: "xk-thieu-xe"), "m13", "Phiếu xuất kho"));

        var trung = ps.Where(p => p.Lsx.Trim().Length > 0 && p.Ngay.Length > 0).GroupBy(p => p.Lsx.Trim().ToUpperInvariant() + "|" + p.Ngay).Where(g => g.Count() > 1).ToList();
        if (trung.Count > 0) o.Add(Go(Mk(CatXk, "vang", M, $"Hai phiếu xuất khẩu cùng LSX cùng ngày: {trung.Count} nhóm",
            "Nghi tạo phiếu 2 lần (hoặc phiếu cũ chưa xóa) — kiểm tra rồi xóa bản thừa.", Cut(trung.Select(g => $"{g.First().Lsx} · {D(g.First().Ngay)}: " + string.Join(" = ", g.Select(p => p.SoPhieu.Length > 0 ? p.SoPhieu : "(chưa có số)")))), id: "xk-trung-phieu"), "m13", "Phiếu xuất kho"));

        var recent = ps.Where(p => p.Cont.Trim().Length >= 8 && string.CompareOrdinal(p.Ngay, Iso(today.AddDays(-14))) >= 0).ToList();
        var contTrung = recent.GroupBy(p => p.Cont.Trim().ToUpperInvariant()).Where(g => g.Select(p => (p.SoPhieu, p.Lsx)).Distinct().Count() > 1 && g.Select(p => p.Lsx.Trim().ToUpperInvariant()).Distinct().Count() > 1).ToList();
        if (contTrung.Count > 0) o.Add(Go(Mk(CatXk, "vang", M, $"Một số cont gắn cho nhiều LSX khác nhau trong 14 ngày: {contTrung.Count} cont",
            "Cùng số cont ở phiếu của các LSX khác nhau — có thể gõ nhầm số cont (cont ghép nhiều LSX thì bỏ qua).",
            Cut(contTrung.Select(g => g.Key + ": " + string.Join(" / ", g.Select(p => $"{p.Lsx} ({D(p.Ngay)})").Distinct()))), id: "xk-trung-cont"), "m13", "Phiếu xuất kho"));

        if (o.Count == 0 && (ls.Count > 0 || ps.Count > 0))
            o.Add(Mk(CatXk, "xanh", M, "Lịch xuất & phiếu xuất khẩu ổn", "Lịch hôm nay / ngày mai đều có phiếu, phiếu gần đây đủ thông tin xe, không trùng phiếu / cont.", id: "xk-on"));
        return o;
    }

    // ------------------------------------------------------------------ 2) Đồng bộ giữa các máy

    public sealed class DongBoInfo
    {
        public bool Enabled;
        public string Role = "";            // chu · con · duphong · "" 
        public int Health;                  // như LanService.Health (0 tắt · 1 tốt · 2 mất · 3 ngoại tuyến)
        public bool Away;
        public double? LastSyncedMin;       // phút từ lần đồng bộ thành công cuối (máy con)
        public int Pending;
        public double PendingAgeMin = double.NaN;
        /// <summary>Trên máy chủ: các máy đang nối — tên, số bảng chờ gửi, qua đường nào.</summary>
        public List<(string Name, int Pending, string Via)> Machines = new();
    }

    public static List<Finding> CheckDongBo(DongBoInfo? i)
    {
        var o = new List<Finding>(); if (i is null || !i.Enabled) return o; const string M = "Đồng bộ giữa các máy";
        bool con = i.Role is "con" or "duphong";
        if (con && i.Health == 1 && i.LastSyncedMin is > 30)
            o.Add(Go(Mk("dong-bo", "vang", M, "Máy này lâu chưa đồng bộ với máy chủ", $"Lần đồng bộ thành công cuối cách đây {Math.Round(i.LastSyncedMin!.Value)} phút dù đang báo nối — dữ liệu máy này có thể cũ.", id: "db-lau-chua-sync"), "dongbo", "Đồng bộ & sao lưu"));
        if (i.Health == 1 && i.Pending > 0 && i.PendingAgeMin is > 15 and < 60)
            o.Add(Go(Mk("dong-bo", "vang", M, "Sổ chờ gửi không giảm dù đang nối máy chủ", $"{i.Pending} bảng chờ gửi, lâu nhất {Math.Round(i.PendingAgeMin)} phút — thử nối lại.", id: "db-so-cho-dung"), "dongbo", "Đồng bộ & sao lưu"));
        if (i.Away && i.Pending > 0 && i.PendingAgeMin > 24 * 60)
            o.Add(Go(Mk("dong-bo", "vang", M, "Máy ngoại tuyến tích thay đổi hơn 1 ngày chưa gộp", $"{i.Pending} bảng chờ, lâu nhất {Math.Round(i.PendingAgeMin / 60)} giờ. Về mạng kho sẽ tự gộp — nhớ mang máy về / nối lại.", id: "db-ngoai-tuyen-lau"), "dongbo", "Đồng bộ & sao lưu"));
        var cham = i.Machines.Where(m => m.Pending > 0).OrderByDescending(m => m.Pending).ToList();
        if (cham.Count > 0)
            o.Add(Mk("dong-bo", "vang", M, $"{cham.Count} máy con đang nối nhưng còn bảng chờ gửi", "Máy con đã nối máy chủ mà sổ chờ chưa hết — gửi chậm hoặc bị chặn; bảo người dùng máy đó bấm «Làm mới».",
                Cut(cham.Select(m => $"{m.Name}: {m.Pending} bảng chờ ({m.Via})")), id: "db-may-con-cho"));
        return o;
    }

    // ------------------------------------------------------------------ 3) Email lịch xuất

    public sealed record XeCase(string Lsx, DateTime At, string State);   // State: no_phieu · phieu_trong
    public sealed class EmailInfo
    {
        public bool Ready, Auto;
        public int IntervalMin = 10;
        public DateTime? LastScan;
        public int Waiting;
        public DateTime? OldestWaiting;
        public List<XeCase> Xe = new();
        public DateTime Now;
    }

    public static List<Finding> CheckEmail(EmailInfo? i)
    {
        var o = new List<Finding>(); if (i is null || !i.Ready) return o; const string M = "Email lịch xuất";
        var maxQuiet = TimeSpan.FromMinutes(Math.Max(30, 4 * Math.Max(2, i.IntervalMin)));
        if (i.Auto && i.LastScan is { } ls && i.Now - ls > maxQuiet)
            o.Add(Go(Mk(CatMail, "vang", M, "Email lịch xuất lâu chưa quét", $"Lần quét cuối {ls:HH:mm dd/MM} (cách {Math.Round((i.Now - ls).TotalMinutes)} phút) dù đã bật quét tự động — máy tắt / mất mạng / Gmail lỗi.", id: "mail-lau-chua-quet"), "lx", "Tổng quan"));
        if (i.Waiting >= 10 || (i.OldestWaiting is { } ow && i.Now - ow > TimeSpan.FromDays(2) && i.Waiting > 0))
            o.Add(Go(Mk(CatMail, "vang", M, $"Thư lịch xuất chờ duyệt tồn đọng: {i.Waiting} thư", i.OldestWaiting is { } w ? $"Thư cũ nhất nhận {w:dd/MM HH:mm}. Vào Email (Tổng quan) duyệt / bỏ qua." : "Vào Email (Tổng quan) duyệt / bỏ qua.", id: "mail-ton-doong"), "lx", "Tổng quan"));
        var trong = i.Xe.Where(x => x.State == "phieu_trong" && i.Now - x.At > TimeSpan.FromMinutes(30)).ToList();
        if (trong.Count > 0)
            o.Add(Go(Mk(CatMail, "vang", M, $"Phiếu xuất khẩu chưa nhận thông tin xe dù thư đã đến: {trong.Count} LSX", "Có thư «Thông tin xe» đã duyệt nhưng phiếu của LSX vẫn trống thông tin xe — quét lại email sẽ tự điền.",
                Cut(trong.Select(x => $"{x.Lsx} · thư {x.At:dd/MM HH:mm}")), id: "mail-xe-chua-dien"), "m13", "Phiếu xuất kho"));
        var np = i.Xe.Where(x => x.State == "no_phieu" && i.Now - x.At > TimeSpan.FromHours(3)).ToList();
        if (np.Count > 0)
            o.Add(Go(Mk(CatMail, "vang", M, $"Có thư thông tin xe nhưng chưa có phiếu xuất khẩu: {np.Count} LSX", "Lịch hôm nay / ngày mai đã có thông tin xe qua email mà chưa tạo phiếu — bấm «Tạo phiếu xuất» ở Tổng quan.",
                Cut(np.Select(x => $"{x.Lsx} · thư {x.At:dd/MM HH:mm}")), id: "mail-xe-chua-phieu"), "lx", "Tổng quan"));
        return o;
    }

    // ------------------------------------------------------------------ 5) Tự kiểm sau cập nhật

    /// <summary>
    /// So số dòng từng bảng với lần quét cuối của bản trước (<paramref name="truoc"/>), kèm toàn vẹn CSDL + chỉ mục.
    /// Chỉ báo bảng mất ≥ 20 % và ≥ 20 dòng; <paramref name="verTruoc"/> = bản app lúc chụp. Chỉ gọi ở lần quét ĐẦU SAU khi đổi bản.
    /// </summary>
    public static List<Finding> CheckSauCapNhat(IReadOnlyDictionary<string, long>? truoc, IReadOnlyDictionary<string, long> nay, string verTruoc, string verNay, string? quickCheck, bool indexOk)
    {
        var o = new List<Finding>(); string M = $"Sau cập nhật {verNay}";
        bool doi = verTruoc.Length > 0 && verTruoc != verNay;
        if (quickCheck is { Length: > 0 } q && !q.Equals("ok", StringComparison.OrdinalIgnoreCase))
            o.Add(Go(Mk(CatCapNhat, "do", M, "Kiểm tra toàn vẹn CSDL báo lỗi", "PRAGMA quick_check: " + (q.Length > 300 ? q[..300] : q) + " — sao lưu ngay, khôi phục từ bản sao lưu gần nhất nếu app báo lỗi đọc dữ liệu.", id: "capnhat-toanven"), "dongbo", "Đồng bộ & sao lưu"));
        if (!indexOk)
            o.Add(Mk(CatCapNhat, "vang", M, "Thiếu chỉ mục CSDL (mở trang có thể chậm)", "Chỉ mục ix_module_sheets_ver chưa có (CSDL chỉ đọc / đang khóa lúc mở app). «Sửa nhanh» tạo lại.", id: "capnhat-chimuc"));
        if (doi && truoc is not null)
        {
            var mat = new List<(string K, long A, long B)>();
            foreach (var (k, a) in truoc) if (a >= 20 && nay.TryGetValue(k, out var b) && b < a * 0.8) mat.Add((k, a, b));
            if (mat.Count > 0)
            {
                var nang = mat.Any(m => m.B < m.A * 0.5);
                o.Add(Go(Mk(CatCapNhat, nang ? "do" : "vang", M, $"Sau cập nhật {verTruoc} → {verNay}: {mat.Count} bảng ít dòng hơn hẳn",
                    "So với lần quét cuối của bản trước. Nếu bạn KHÔNG vừa nạp lại file / xóa dữ liệu thì có thể bản mới đọc sai — bấm «Quay lại bản trước» và gửi báo cáo cho Claude; dữ liệu cũ xem ở Sao lưu.",
                    Cut(mat.OrderBy(m => m.B - m.A).Select(m => $"{m.K}: {m.A:#,0} → {m.B:#,0} dòng")), id: "capnhat-mat-dong"), "dongbo", "Đồng bộ & sao lưu"));
            }
        }
        if (o.Count == 0 && doi)
            o.Add(Mk(CatCapNhat, "xanh", M, $"Tự kiểm sau cập nhật {verNay}: ổn", $"Từ {verTruoc}: toàn vẹn CSDL đạt, chỉ mục đủ, không bảng nào mất dòng.", id: "capnhat-on"));
        return o;
    }

    // ------------------------------------------------------------------ 6) Báo cáo tuần

    public sealed class TuanData
    {
        public DateTime From, To;
        public string Version = "", Machine = "";
        public List<TroLyLichSu.Day?> Days = new();
        public List<(string Sev, string Mod, string Title)> Open = new();
        public int ErrorKinds, ErrorTotal;
        public List<string> TopErrors = new();
        public double? CpuAvg, RamAvg, DiskFreeGb;
        public long DbBytes, WalBytes;
        public string LastBackup = "";
        public List<(string Name, string Ver, bool Lost, int Do, int Vang)> Machines = new();
        public int SlowOps;
    }

    public static string BuildTuan(TuanData d)
    {
        var s = new StringBuilder();
        s.AppendLine($"BÁO CÁO TUẦN — Trợ lý hệ thống Kho Lạnh");
        s.AppendLine($"Tuần {d.From:dd/MM} – {d.To:dd/MM/yyyy} · máy {d.Machine} · bản {d.Version}");
        s.AppendLine();
        var doNay = d.Open.Count(x => x.Sev == "do"); var vangNay = d.Open.Count(x => x.Sev == "vang");
        s.AppendLine($"1) Tình trạng hiện tại: {doNay} Nặng · {vangNay} Cần xem");
        foreach (var x in d.Open.OrderBy(x => x.Sev == "do" ? 0 : 1).Take(10)) s.AppendLine($"   - [{(x.Sev == "do" ? "Nặng" : "Cần xem")}] {x.Mod}: {x.Title}");
        if (d.Open.Count > 10) s.AppendLine($"   … và {d.Open.Count - 10} mục nữa");
        s.AppendLine();
        var have = d.Days.Where(x => x is not null).Select(x => x!).ToList();
        if (have.Count > 0)
        {
            s.AppendLine($"2) Diễn biến 7 ngày (Nặng / Cần xem cao nhất mỗi ngày): " + string.Join(" · ", have.Select(x => $"{x.Date:dd/MM} {x.Do}/{x.Vang}")));
            s.AppendLine($"   Cảnh báo mới {have.Sum(x => x.Moi)} · tái phát {have.Sum(x => x.TaiPhat)} · đã hết {have.Sum(x => x.Dong)} · số lần quét {have.Sum(x => x.Scans)}");
        }
        else s.AppendLine("2) Chưa có lịch sử quét trong tuần.");
        s.AppendLine();
        s.AppendLine($"3) Lỗi phần mềm: {d.ErrorKinds} loại / {d.ErrorTotal} lần" + (d.TopErrors.Count > 0 ? ":" : " — không có."));
        foreach (var e in d.TopErrors.Take(5)) s.AppendLine("   - " + e);
        if (d.SlowOps > 0) s.AppendLine($"   Thao tác chậm (> 2 giây): {d.SlowOps} lần");
        s.AppendLine();
        s.AppendLine("4) Máy & dữ liệu: " +
            (d.CpuAvg is { } c ? $"CPU TB {c:0}% · " : "") + (d.RamAvg is { } r ? $"RAM TB {r:0}% · " : "") + (d.DiskFreeGb is { } g ? $"ổ trống {g:0.0} GB · " : "") +
            $"database {DbStats.Human(d.DbBytes)}" + (d.WalBytes > 0 ? $" (+WAL {DbStats.Human(d.WalBytes)})" : "") + $" · sao lưu cuối: {(d.LastBackup.Length > 0 ? d.LastBackup : "CHƯA CÓ")}");
        if (d.Machines.Count > 0)
        {
            s.AppendLine();
            s.AppendLine($"5) Các máy ({d.Machines.Count}):");
            foreach (var m in d.Machines) s.AppendLine($"   - {m.Name} · bản {m.Ver}{(m.Lost ? " · MẤT LIÊN LẠC" : "")} · {m.Do} Nặng / {m.Vang} Cần xem");
            var vers = d.Machines.Select(m => m.Ver).Where(v => v.Length > 0).Distinct().ToList();
            if (vers.Count > 1) s.AppendLine($"   ⚠ Các máy đang chạy {vers.Count} bản khác nhau ({string.Join(", ", vers)}) — nên cập nhật cho đồng đều.");
        }
        return s.ToString().TrimEnd();
    }

    /// <summary>Tóm tắt 1–2 dòng cho thông báo.</summary>
    public static string TuanShort(TuanData d)
    {
        var n = d.Open.Count(x => x.Sev == "do"); var v = d.Open.Count(x => x.Sev == "vang");
        return $"Hiện {n} Nặng · {v} Cần xem · lỗi phần mềm {d.ErrorTotal} lần/tuần · sao lưu cuối {(d.LastBackup.Length > 0 ? d.LastBackup : "chưa có")}.";
    }
}
