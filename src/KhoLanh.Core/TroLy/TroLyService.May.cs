using KhoLanh.Core.LichXuat;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// Giai đoạn 5 – Nhiều máy + lịch sử: sau mỗi lần quét + mỗi 15 phút ghi ẢNH CHỤP TÌNH TRẠNG của máy này vào database dùng chung
/// (<see cref="TroLyMayStore"/>, đi qua LAN / Drive); lịch sử tóm tắt mỗi lần quét (<see cref="TroLyLichSu"/>, riêng máy, 30 ngày);
/// phần "Cài đặt liên quan" (đã che) + log quanh lỗi cho báo cáo gửi Claude.
/// </summary>
public sealed partial class TroLyService
{
    private TroLyLichSu? _lichSu;
    private DateTime _lsPruned;

    public TroLyMayStore MayStore => new(_src);
    public TroLyLichSu LichSu => _lichSu ??= new TroLyLichSu(Path.Combine(_dataDir, PerfStore.FileName));

    private PerfStore? PerfSt()
    {
        var path = Path.Combine(_dataDir, PerfStore.FileName);
        return PerfMonitor.Current?.Store ?? (File.Exists(path) ? new PerfStore(path) : null);
    }

    /// <summary>Gọi ở cuối mỗi lần quét: thêm 1 dòng lịch sử + ghi ảnh chụp tình trạng. Không bao giờ ném lỗi.</summary>
    private void AfterScan(ScanResult r)
    {
        try
        {
            LichSu.Add(new TroLyLichSu.Scan(r.At, r.Deep, r.Do, r.Vang, r.Xanh, r.Changes.Moi, r.Changes.TaiPhat, r.Changes.Dong));
            if ((r.At - _lsPruned).TotalHours >= 12) { LichSu.Prune(r.At); _lsPruned = r.At; }
        }
        catch { /* không ghi được lịch sử: bỏ qua */ }
        PublishStatus(r);
    }

    /// <summary>
    /// Dựng ảnh chụp tình trạng của MÁY NÀY: cảnh báo từ lần quét <paramref name="r"/> (hoặc lần quét gần nhất trong bộ nhớ; chưa có thì từ hàng đợi
    /// đã lưu — máy đang đăng nhập tài khoản không phải Quản trị vẫn báo được lần quét trước), số hiệu năng mới nhất + trung bình 24 giờ.
    /// </summary>
    public MayTinhTrang BuildStatus(ScanResult? r = null)
    {
        var now = Now(); var lan = _lan();
        r ??= Last;
        DateTime? scanAt; int nDo, nVang, nXanh; List<(string, string, string)> canh;
        if (r is not null)
        {
            scanAt = r.At; nDo = r.Do; nVang = r.Vang; nXanh = r.Xanh;
            canh = r.Findings.Where(x => x.Q is not ("boQua" or "loiNguon")).Select(x => (x.Sev, x.Mod, x.Title)).ToList();
        }
        else
        {
            var st = LoadState(); scanAt = st.LastScanAt;
            var live = st.Items.Values.Where(x => !x.Hidden && x.St is not ("dong" or "boQua" or "loiNguon") && st.LastScanAt is { } la && x.LastSeenAt >= la).ToList();
            nDo = live.Count(x => x.Sev == "do"); nVang = live.Count(x => x.Sev == "vang");
            nXanh = 0;
            try { if (scanAt is { } sa && LichSu.Range(sa.AddSeconds(-1), sa.AddSeconds(1)).LastOrDefault() is { } h) nXanh = h.Xanh; } catch { }
            canh = live.Select(x => (x.Sev, x.Mod, x.Title)).ToList();
        }
        PerfStore.Sample? perf = null, net = null; double? cpu24 = null, ram24 = null;
        try
        {
            if (PerfSt() is { } ps)
            {
                perf = ps.Latest();
                if (perf is not null && now - perf.At > TimeSpan.FromHours(2)) perf = null;   // mẫu quá cũ (app vừa mở lại) — không coi là "hiện tại"
                net = ps.Range(now.AddHours(-1), now, 120).LastOrDefault(s => s.GoogleMs is not null || s.LanMs is not null);
                var day = ps.Range(now.AddHours(-24), now, 48);
                if (day.Count > 0) { cpu24 = day.Average(s => s.CpuSys); ram24 = day.Average(s => s.RamSys); }
            }
        }
        catch { /* file số liệu hỏng: bỏ phần hiệu năng */ }
        return TroLyMay.Build(lan.MachineId, lan.MachineName, Version, TroLyMay.RoleLabel(lan.Role, false), now, scanAt,
            nDo, nVang, nXanh, canh, perf, cpu24, ram24, net);
    }

    /// <summary>Ghi ảnh chụp tình trạng của máy này nếu nội dung đổi / quá nhịp 30 phút. Trả về true khi đã ghi. Không bao giờ ném lỗi.</summary>
    public bool PublishStatus(ScanResult? r = null)
    {
        try { return MayStore.Write(BuildStatus(r)); }
        catch { return false; }
    }

    /// <summary>Tình trạng mọi máy (chỉ Quản trị).</summary>
    public List<MayTinhTrang> Machines() { Require(); return MayStore.ReadAll(); }

    /// <summary>Lịch sử quét của máy này theo ngày (chỉ Quản trị).</summary>
    public List<TroLyLichSu.Day?> History(int days)
    {
        Require();
        var now = Now();
        return TroLyLichSu.ByDay(LichSu.Range(now.Date.AddDays(-days + 1), now.AddMinutes(1)), now, days);
    }

    private CancellationTokenSource? _statusCts;

    /// <summary>Ghi ảnh chụp tình trạng ngầm: <paramref name="first"/> sau khi mở rồi mỗi <paramref name="every"/> (mọi tài khoản — không cần Quản trị).</summary>
    public void StartStatus(TimeSpan first, TimeSpan every)
    {
        StopStatus();
        var cts = _statusCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(first, cts.Token);
                while (!cts.IsCancellationRequested) { PublishStatus(); await Task.Delay(every, cts.Token); }
            }
            catch (OperationCanceledException) { }
        });
    }
    public void StopStatus() { _statusCts?.Cancel(); _statusCts = null; }

    // ------------------------------------------------------------------ báo cáo gửi Claude

    private string ReadLogTail()
    {
        try
        {
            if (!File.Exists(ErrorLogFile)) return "";
            using var fs = new FileStream(ErrorLogFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            const long Tail = 2 * 1024 * 1024;
            if (fs.Length > Tail) fs.Seek(-Tail, SeekOrigin.End);
            using var sr = new StreamReader(fs);
            return sr.ReadToEnd();
        }
        catch { return ""; }
    }

    /// <summary>
    /// Phần thêm của báo cáo: chỉ cài đặt LIÊN QUAN tới loại cảnh báo đã chọn, mọi giá trị đã che (có / không, đuôi đường dẫn, email a***@…);
    /// log quanh lỗi phần mềm; danh sách giá trị bí mật để che lần cuối.
    /// </summary>
    internal TroLyBaoCao.Extra ReportExtra(IReadOnlyList<Finding> pick)
    {
        var x = new TroLyBaoCao.Extra();
        var cats = pick.Select(f => f.Cat).ToHashSet();
        if (cats.Contains("loi-app")) x.LogText = ReadLogTail();
        var st = Settings; var lan = _lan();
        void Add(string k, string v) => x.CaiDat.Add((k, v));
        bool net = cats.Overlaps(new[] { "dong-bo", "ket-noi", "sao-luu", "csdl", "trung-khoa" });
        if (net)
        {
            Add("Vai máy (LAN)", TroLyMay.RoleLabel(lan.Role, false));   // 1.121: bỏ Thư mục / Drive dùng chung
            Add("Mạng LAN", lan.Enabled ? "bật" : "tắt");
            if (lan.Enabled) Add("Địa chỉ máy chủ LAN đã biết", TroLyBaoCao.CoKhong(lan.ServerAddress));
        }
        if (st is not null && cats.Overlaps(new[] { "ket-noi", "dong-bo" }))
        {
            Add("Tài khoản dùng chung — đường dẫn", TroLyBaoCao.MaskUrl(st.Get("acct_url")));
            Add("Tài khoản dùng chung — mã truy cập", TroLyBaoCao.CoKhong(st.Get("acct_token")));
            var phone = new PhoneWebApp(st);
            Add("Web App điện thoại — đường dẫn", phone.CustomUrl ? "riêng " + TroLyBaoCao.MaskUrl(phone.Url) : "mặc định cài sẵn");
            Add("Web App điện thoại — PC_TOKEN", TroLyBaoCao.CoKhong(phone.Token));
            var mail = MailConfig.Load(st);
            Add("Gmail lịch xuất", mail.Email.Length == 0 ? "chưa khai báo" : TroLyBaoCao.MaskEmail(mail.Email) + " · mật khẩu ứng dụng: " + TroLyBaoCao.CoKhong(mail.PassEnc));
            if (st.Get("lx_mail_status") is { Length: > 0 } ms) Add("Gmail lịch xuất — trạng thái", TroLyChecks.Short(ms.Trim(), 160));
        }
        if (cats.Contains("sao-luu"))
        {
            int n = 0; try { n = _backup.ListAutoBackups().Count(); } catch { }
            Add("Bản sao lưu tự động trên máy", n.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (cats.Contains(PerfChecks.Cat) || cats.Contains("cham")) Add("Bộ đo hiệu năng", PerfMonitor.Current is null ? "không chạy" : "đang chạy (1 mẫu / phút)");

        // giá trị bí mật thô của máy: lọt vào chữ báo cáo (bằng chứng, log…) thì bị che
        if (st is not null)
        {
            foreach (var k in new[] { "acct_url", "acct_token", "acct_admin_key", PhoneWebApp.KeyUrl, PhoneWebApp.KeyToken, "lan_key", MailConfig.K + "pass", MailConfig.K + "email", "lt_google_secret", "lt_google_id" })
            {
                var v = st.Get(k); if (v.Length == 0) continue;
                x.Secrets.Add(v);
                try { if (Secret.Unprotect(v) is { Length: > 0 } u && u != v) x.Secrets.Add(u); } catch { }
            }
            try { if (new PhoneWebApp(st).Token is { Length: > 0 } t) x.Secrets.Add(t); } catch { }
        }
        return x;
    }
}
