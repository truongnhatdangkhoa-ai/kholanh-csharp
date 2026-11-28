using Dapper;
using KhoLanh.Core.Data;
using System.Globalization;
using KhoLanh.Core.LichXuat;
using KhoLanh.Core.PhieuKho;
using KhoLanh.Core.Services;
using static KhoLanh.Core.TroLy.TroLyChecks;

namespace KhoLanh.Core.TroLy;

/// <summary>1.186: thu thập số liệu cho các kiểm tra mới (lịch xuất / email / đồng bộ / sau cập nhật) + Sửa nhanh mới + báo cáo tuần. Chỉ ĐỌC (trừ các Sửa nhanh).</summary>
public sealed partial class TroLyService
{
    /// <summary>Lịch xuất (gán ở AppServices) — để kiểm tra lịch / phiếu xuất khẩu / email.</summary>
    public Func<LichXuatService>? LichXuat { get; set; }

    // ------------------------------------------------------------------ bước quét

    internal List<Finding> CheckLichXuat(DateTime now)
    {
        var res = new List<Finding>();
        if (LichXuat?.Invoke() is not { } lx) return res;
        var (lich, phieu) = lx.PeekForCheck();
        var pn = phieu.Select(p => PhieuKhoCore.LsxList(p.Lsx).Select(PhieuKhoCore.LsxNorm).ToHashSet()).ToList();
        bool Has(Lich l) { var c = LichXuatCore.Codes(l.Lsx).Select(PhieuKhoCore.LsxNorm).ToHashSet(); return pn.Any(s => s.Overlaps(c)); }
        res.AddRange(TroLyMoRong.CheckXuatKhau(
            lich.Select(l => new TroLyMoRong.XkLich(l.Ngay, LichXuatCore.Label(l.Lsx), l.TrangThai == "ok", Has(l))).ToList(),
            phieu.Select(p => new TroLyMoRong.XkPhieu(p.SoPhieu, p.Ngay, p.Lsx, p.Xe.Cont, p.Xe.SoXe, p.Xe.TaiXe, p.Xe.Seal)).ToList(), now.Date));
        // email
        var cfg = lx.MailCfg();
        var rows = lx.EmailRows();
        var info = new TroLyMoRong.EmailInfo { Ready = cfg.Ready, Auto = cfg.Auto, IntervalMin = cfg.IntervalMin, LastScan = lx.LastScan, Now = now };
        var wait = rows.Where(r => r.TrangThai.Length == 0).ToList();
        info.Waiting = wait.Count; info.OldestWaiting = wait.Count > 0 ? wait.Min(r => r.NhanLuc) : null;
        var iso = now.Date.AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        foreach (var r in rows.Where(r => r.HanhDong == "xe" && r.TrangThai == "Đã duyệt" && r.NhanLuc >= now.AddDays(-3)).GroupBy(r => r.Id).Select(g => g.First()))
        {
            var codes = LichXuatEmail.CodesOf(r.Lsx).Select(PhieuKhoCore.LsxNorm).ToHashSet(); if (codes.Count == 0) continue;
            var p = phieu.Where((x, i) => pn[i].Overlaps(codes)).OrderByDescending(x => x.Ngay, StringComparer.Ordinal).FirstOrDefault();
            if (p is null)
            {
                var near = lich.Any(l => l.TrangThai != "ok" && string.CompareOrdinal(l.Ngay, iso) <= 0 && LichXuatCore.Codes(l.Lsx).Select(PhieuKhoCore.LsxNorm).Any(codes.Contains));
                if (near) info.Xe.Add(new TroLyMoRong.XeCase(string.Join("+", LichXuatEmail.CodesOf(r.Lsx)), r.NhanLuc, "no_phieu"));
            }
            else if (new[] { p.Xe.TaiXe, p.Xe.Sdt, p.Xe.SoXe, p.Xe.Seal, p.Xe.Booking }.All(v => string.IsNullOrWhiteSpace(v)))
                info.Xe.Add(new TroLyMoRong.XeCase(string.Join("+", LichXuatEmail.CodesOf(r.Lsx)), r.NhanLuc, "phieu_trong"));
        }
        res.AddRange(TroLyMoRong.CheckEmail(info));
        return res;
    }

    internal List<Finding> CheckDongBoMay(DateTime now)
    {
        var lan = _lan();
        var i = new TroLyMoRong.DongBoInfo { Enabled = lan.Enabled, Role = lan.Role, Health = lan.Health, Away = lan.AwayMode };
        if (lan.LastSyncedAt is { } ls) i.LastSyncedMin = (now - ls).TotalMinutes;
        try
        {
            using var c = _db.Open();
            i.Pending = (int)c.ExecuteScalar<long>("SELECT COUNT(*) FROM lan_pending");
            var oldest = c.ExecuteScalar<string?>("SELECT MIN(created_at) FROM lan_pending");
            if (oldest is not null && DateTime.TryParse(oldest, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var o)) i.PendingAgeMin = (now.ToUniversalTime() - o).TotalMinutes;
        }
        catch { }
        try { if (lan.Serving) i.Machines = lan.Machines().Select(m => (m.Name, m.Pending, m.ViaNet ? "Internet" : "LAN")).ToList(); } catch { }
        var res = TroLyMoRong.CheckDongBo(i);
        // cài đặt máy bị xóa nhưng còn bản dự phòng → nạp lại được
        try
        {
            if (Settings?.MissingFromMirror() is { } miss && SettingsService.Meaningful(miss))
                res.Add(Mk("cai-dat", "vang", "Cài đặt máy", $"Cài đặt máy bị mất {miss.Count} khóa so với bản dự phòng",
                    "Bảng cài đặt thiếu các khóa quan trọng (vai máy chủ, kết nối, Gmail…) — thường do phần mềm dọn rác. «Sửa nhanh» nạp lại từ file dự phòng cạnh database (khóa còn thì giữ nguyên).",
                    Cut(miss.Take(8)), id: "cai-dat-mat"));
        }
        catch { }
        return res;
    }

    /// <summary>Số dòng từng bảng module (để so sau cập nhật).</summary>
    private Dictionary<string, long> RowSnapshot()
    {
        var d = new Dictionary<string, long>();
        foreach (var t in DbStats.Read(_db).Tables) if (t.Module != "tro-ly") d[t.Module + "/" + t.Sheet] = t.Rows;
        return d;
    }

    private sealed class SnapRec { public string Ver { get; set; } = ""; public Dictionary<string, long> Rows { get; set; } = new(); }

    internal List<Finding> CheckSauCapNhatMay(bool deep)
    {
        if (Settings is not { } st) return new();
        var nay = RowSnapshot();
        SnapRec? old = null;
        try { var raw = st.Get("troly_snap"); if (raw.Length > 0) old = System.Text.Json.JsonSerializer.Deserialize<SnapRec>(raw); } catch { }
        var vNay = Version; var vTruoc = old?.Ver ?? "";
        var firstAfter = vTruoc.Length > 0 && vTruoc != vNay;
        string? quick = null; var indexOk = true;
        try
        {
            using var c = _db.Open();
            indexOk = c.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='ix_module_sheets_ver'") > 0;
            if (firstAfter || deep) quick = c.ExecuteScalar<string?>("PRAGMA quick_check(1)") ?? "ok";
        }
        catch (Exception e) { quick = e.Message; }
        var res = TroLyMoRong.CheckSauCapNhat(firstAfter ? old!.Rows : null, nay, vTruoc, vNay, quick, indexOk);
        // chụp mới: lần quét ở bản hiện tại — khi bản đổi lần sau sẽ so với đây. Lần đầu sau cập nhật không ghi đè ngay nếu có cảnh báo mất dòng (giữ mốc cũ để so lại)
        var lost = res.Any(f => f.Id == "capnhat-mat-dong");
        if (!lost) try { st.Set("troly_snap", System.Text.Json.JsonSerializer.Serialize(new SnapRec { Ver = vNay, Rows = nay })); } catch { }
        return res;
    }

    // ------------------------------------------------------------------ Sửa nhanh mới

    /// <summary>Tạo lại chỉ mục CSDL (an toàn: chỉ thêm / dựng lại chỉ mục, không đụng dữ liệu).</summary>
    public string RebuildIndexes()
    {
        Require();
        using var c = _db.Open();
        c.Execute("CREATE INDEX IF NOT EXISTS ix_module_sheets_ver ON module_sheets(db_key, version);");
        c.Execute("REINDEX;"); c.Execute("ANALYZE;");
        return "Đã tạo lại chỉ mục và cập nhật thống kê CSDL.";
    }

    public string RestoreSettingsFromMirror()
    {
        Require();
        var r = Settings?.RestoreFromMirror() ?? new();
        return r.Count > 0 ? $"Đã nạp lại {r.Count} khóa cài đặt từ bản dự phòng (cần mở lại app để vai máy chủ / kết nối có hiệu lực)." : "Không có khóa nào cần nạp lại.";
    }

    public string ReconnectLan() { Require(); _lan().RetryNow(); return "Đã bảo máy nối lại máy chủ ngay."; }

    // ------------------------------------------------------------------ báo cáo tuần

    public TroLyMoRong.TuanData WeeklyData()
    {
        Require();
        var now = Now();
        var d = new TroLyMoRong.TuanData { From = now.Date.AddDays(-6), To = now.Date, Version = Version, Machine = Environment.MachineName };
        try { d.Days = TroLyLichSu.ByDay(LichSu.Range(now.Date.AddDays(-6), now.AddDays(1)), now.Date, 7); } catch { }
        var open = (Last?.Findings ?? Array.Empty<Finding>().ToList()).Where(x => x.Sev != "xanh" && x.Q is not ("boQua" or "loiNguon" or "dong")).ToList();
        if (Last is null)
        {
            var st = LoadState();
            open = st.Items.Values.Where(x => !x.Hidden && x.St is not ("dong" or "boQua" or "loiNguon") && st.LastScanAt is { } la && x.LastSeenAt >= la)
                .Select(x => new Finding { Sev = x.Sev, Mod = x.Mod, Title = x.Title }).ToList();
        }
        d.Open = open.Select(x => (x.Sev, x.Mod, x.Title)).ToList();
        try
        {
            var errs = ReadErrors(now).Where(e => e.Last >= now.AddDays(-7)).OrderByDescending(e => e.Count).ToList();
            d.ErrorKinds = errs.Count; d.ErrorTotal = errs.Sum(e => e.Count);
            d.TopErrors = errs.Take(5).Select(e => $"{e.Type}: {e.Msg} ×{e.Count} (lần cuối {e.Last:dd/MM HH:mm})").ToList();
        }
        catch { }
        try { d.SlowOps = Perf.Read(_dataDir, now, 7).Count; } catch { }
        try
        {
            if (PerfSt() is { } ps)
            {
                var sm = ps.Range(now.AddDays(-7), now, 200);
                if (sm.Count > 0) { d.CpuAvg = sm.Average(x => x.CpuSys); d.RamAvg = sm.Average(x => x.RamSys); d.DiskFreeGb = sm[^1].DiskFreeGb; }
            }
        }
        catch { }
        try { var s = DbStats.Read(_db); d.DbBytes = s.FileBytes; d.WalBytes = s.WalBytes; } catch { }
        try { var b = _backup.ListAutoBackups().OrderByDescending(x => x.LastWriteTime).FirstOrDefault(); if (b is not null) d.LastBackup = $"{b.LastWriteTime:dd/MM HH:mm}"; } catch { }
        try { d.Machines = MayStore.ReadAll().Select(m => (m.Name, m.Ver, TroLyMay.MatLienLac(m, now), m.Do, m.Vang)).ToList(); } catch { }
        return d;
    }

    public string WeeklyReport() => TroLyMoRong.BuildTuan(WeeklyData());

    /// <summary>Đến hạn báo cáo tuần (≥ 7 ngày từ lần báo trước; lần đầu chỉ đặt mốc, 7 ngày sau mới báo).</summary>
    public bool WeeklyDue()
    {
        if (Settings is not { } st || !CanUse) return false;
        var raw = st.Get("troly_tuan_at");
        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)) { st.Set("troly_tuan_at", Now().ToString("o", CultureInfo.InvariantCulture)); return false; }
        return Now() - at >= TimeSpan.FromDays(7);
    }
    public void WeeklyDone() => Settings?.Set("troly_tuan_at", Now().ToString("o", CultureInfo.InvariantCulture));
}
