namespace KhoLanh.Core.TroLy;

/// <summary>
/// 1.101 (giai đoạn 3): cảnh báo HIỆU NĂNG của Trợ lý — hàm thuần trên số liệu <see cref="PerfStore"/> / logs/cham.log:
/// CPU / RAM cao kéo dài, KhoLanh giữ RAM bất thường, ổ chứa dữ liệu sắp đầy, thao tác chậm dần sau cập nhật, mở app / đăng nhập chậm dần.
/// </summary>
public static class PerfChecks
{
    public const string Cat = "hieunang", Mod = "Hiệu năng";
    /// <summary>Cửa sổ xét "kéo dài" (phút) và số mẫu tối thiểu trong cửa sổ đó.</summary>
    public const int SustainMin = 30, MinSamples = 20;
    public const double AppRamMb = 1500;

    /// <summary>Số liệu máy: <paramref name="recent"/> = các mẫu 1 giờ gần nhất (mỗi phút 1 mẫu), <paramref name="latest"/> = mẫu mới nhất.</summary>
    public static List<Finding> CheckMachine(IReadOnlyList<PerfStore.Sample> recent, PerfStore.Sample? latest, DateTime now)
    {
        var res = new List<Finding>();
        var win = recent.Where(s => s.At >= now.AddMinutes(-SustainMin)).ToList();
        if (win.Count >= MinSamples)
        {
            var cpu = win.Average(s => s.CpuSys); var app = win.Average(s => s.CpuApp);
            if (cpu >= 85)
                res.Add(TroLyChecks.Mk(Cat, cpu >= 95 ? "do" : "vang", Mod, $"CPU cao kéo dài: trung bình {cpu:0}% trong {SustainMin} phút",
                    app >= cpu * 0.5 ? $"KhoLanh chiếm phần lớn ({app:0}%) — thường do thao tác nặng đang chạy (nạp file lớn, đồng bộ). Nếu kéo dài, đóng mở lại app."
                        : $"KhoLanh chỉ dùng {app:0}% — chương trình KHÁC trên máy đang chiếm CPU (Task Manager › Processes). App sẽ chậm theo.",
                    new[] { $"{win.Count} mẫu · thấp nhất {win.Min(s => s.CpuSys):0}% · cao nhất {win.Max(s => s.CpuSys):0}%" }, TroLyChecks.Hash("hn|cpu")));
            var ram = win.Average(s => s.RamSys);
            if (ram >= 90)
                res.Add(TroLyChecks.Mk(Cat, ram >= 95 ? "do" : "vang", Mod, $"RAM gần đầy kéo dài: trung bình {ram:0}% trong {SustainMin} phút",
                    $"Máy thiếu bộ nhớ → Windows dùng ổ đĩa thay RAM, mọi thứ chậm hẳn. KhoLanh đang giữ {win[^1].RamAppMb:#,0} MB; đóng bớt chương trình / tab trình duyệt.",
                    null, TroLyChecks.Hash("hn|ram")));
        }
        if (latest is not null && (now - latest.At).TotalMinutes <= 15)
        {
            if (latest.RamAppMb >= AppRamMb)
                res.Add(TroLyChecks.Mk(Cat, "vang", Mod, $"KhoLanh đang giữ {latest.RamAppMb:#,0} MB RAM",
                    "Cao bất thường (thường 300–800 MB) — có thể do mở nhiều màn hình / file rất lớn. Đóng mở lại app để giải phóng; lặp lại thì gửi báo cáo.",
                    null, TroLyChecks.Hash("hn|ramapp")));
            var lv = PerfLevel.Disk(latest.DiskFreeGb, latest.DiskTotalGb);
            if (lv > 0)
                res.Add(TroLyChecks.Mk(Cat, lv == 2 ? "do" : "vang", Mod, $"Ổ chứa dữ liệu sắp đầy: còn {latest.DiskFreeGb:0.#} GB / {latest.DiskTotalGb:0} GB",
                    "Hết chỗ thì không ghi được dữ liệu / sao lưu. Dọn thư mục Tải về, Thùng rác, bản sao lưu cũ (Dữ liệu & sao lưu › Sao lưu) hoặc chuyển dữ liệu sang ổ khác (Cài đặt máy › Đổi nơi lưu dữ liệu).",
                    null, TroLyChecks.Hash("hn|disk")));
        }
        return res;
    }

    /// <summary>
    /// Thao tác chậm dần SAU CẬP NHẬT: cùng thao tác, bản mới nhất có ≥ 2 lần đo với trung bình ≥ 1,5 lần (và chậm hơn ≥ 2 giây) so với bản trước đó.
    /// </summary>
    public static List<Finding> CheckSlowAfterUpdate(IEnumerable<Perf.Entry> entries)
    {
        var res = new List<Finding>();
        foreach (var g in entries.Where(e => e.Version.Length > 0).GroupBy(e => e.Op))
        {
            var byVer = g.GroupBy(e => e.Version).Select(v => (Ver: Version.TryParse(v.Key, out var x) ? x : null, Avg: v.Average(e => (double)e.Ms), N: v.Count()))
                .Where(v => v.Ver is not null && v.N >= 2).OrderByDescending(v => v.Ver).ToList();
            if (byVer.Count < 2) continue;
            var (cur, prev) = (byVer[0], byVer[1]);
            if (cur.Avg >= prev.Avg * 1.5 && cur.Avg - prev.Avg >= 2000)
                res.Add(TroLyChecks.Mk(Cat, "vang", g.Key, $"Chậm hơn sau cập nhật: {g.Key}",
                    $"Bản {cur.Ver!.ToString(3)} trung bình {cur.Avg / 1000:0.0} giây ({cur.N} lần) — bản {prev.Ver!.ToString(3)} chỉ {prev.Avg / 1000:0.0} giây ({prev.N} lần). Gửi báo cáo cho Claude để xem lại thay đổi của bản mới.",
                    null, TroLyChecks.Hash("hn|upd|" + g.Key + "|" + cur.Ver)));
        }
        return res;
    }

    /// <summary>Mở app / đăng nhập chậm dần: trung vị 3 ngày gần nhất ≥ 1,5 lần trung vị 4–30 ngày trước và &gt; 4 giây.</summary>
    public static List<Finding> CheckStartupTrend(IEnumerable<PerfStore.Mark> marks, DateTime now)
    {
        var res = new List<Finding>();
        foreach (var k in new[] { PerfMonitor.MarkMoApp, PerfMonitor.MarkDangNhap })
        {
            var l = marks.Where(m => m.Kind == k).ToList();
            var a = l.Where(m => m.At >= now.AddDays(-3)).Select(m => m.Ms).ToList();
            var b = l.Where(m => m.At < now.AddDays(-3) && m.At >= now.AddDays(-30)).Select(m => m.Ms).ToList();
            if (a.Count < 2 || b.Count < 3) continue;
            double Med(List<double> x) { x.Sort(); return x.Count % 2 == 1 ? x[x.Count / 2] : (x[x.Count / 2 - 1] + x[x.Count / 2]) / 2; }
            var (ma, mb) = (Med(a), Med(b));
            if (ma >= mb * 1.5 && ma > 4000)
                res.Add(TroLyChecks.Mk(Cat, "vang", Mod, $"{PerfMonitor.MarkLabel(k)} chậm dần: {ma / 1000:0.0} giây (trước {mb / 1000:0.0} giây)",
                    k == PerfMonitor.MarkMoApp ? "Thường do database lớn dần, máy khởi động nhiều chương trình cùng lúc, hoặc ổ đĩa chậm / đầy. Xem tab Hiệu năng › Dữ liệu."
                        : "Thường do tải danh sách tài khoản (Google Sheet) chậm / lỗi — xem Cài đặt máy › Tài khoản dùng chung và mạng.",
                    new[] { $"3 ngày gần nhất: {a.Count} lần · trước đó: {b.Count} lần" }, TroLyChecks.Hash("hn|trend|" + k)));
        }
        return res;
    }
}
