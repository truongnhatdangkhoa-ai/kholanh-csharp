using System.Text.Json.Nodes;
using KhoLanh.Core.TroLy;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.100 — Hiệu năng giai đoạn 1: kho số liệu SQLite, gộp điểm biểu đồ, xóa dữ liệu cũ, đọc /proc, bộ đo ngầm, dung lượng database.</summary>
public class HieuNangTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-hn-" + Guid.NewGuid().ToString("N"));
    public HieuNangTests() { Directory.CreateDirectory(_dir); }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static PerfStore.Sample S(DateTime at, double cpu, double ram = 50) => new(at, cpu, cpu / 4, ram, 300, 120, 500, null, null);

    [Fact]
    public void Luu_mau_gop_diem_bieu_do_va_xoa_du_lieu_cu_hon_30_ngay()
    {
        var st = new PerfStore(Path.Combine(_dir, PerfStore.FileName));
        var now = new DateTime(2026, 11, 25, 12, 0, 0);
        for (var i = 0; i < 120; i++) st.Add(S(now.AddMinutes(-i), i % 2 == 0 ? 10 : 30));   // 2 giờ, mỗi phút 1 mẫu
        st.Add(S(now.AddDays(-31), 99));                                                       // mẫu cũ
        Assert.Equal(121, st.Count());
        Assert.Equal(now, st.Latest()!.At);
        var hour = st.Range(now.AddHours(-1), now, 60);
        Assert.InRange(hour.Count, 55, 61);
        var four = st.Range(now.AddHours(-2), now, 4);                                           // gộp 120 mẫu → ≤ 5 điểm, trung bình 10 / 30 = 20
        Assert.InRange(four.Count, 4, 5);
        Assert.All(four.Take(4), p => Assert.InRange(p.CpuSys, 19, 21));
        Assert.Equal(1, st.Prune(now));                                                          // chỉ mẫu 31 ngày trước bị xóa
        Assert.Equal(120, st.Count());
        Assert.Empty(st.Range(now.AddDays(-40), now.AddDays(-30)));
    }

    [Fact]
    public void Moc_thoi_gian_mo_app_dang_nhap_trung_vi()
    {
        var st = new PerfStore(Path.Combine(_dir, PerfStore.FileName));
        var t = new DateTime(2026, 11, 25, 8, 0, 0);
        foreach (var (ms, i) in new[] { 3000.0, 1200, 1800 }.Select((x, i) => (x, i))) st.AddMark(PerfMonitor.MarkMoApp, ms, t.AddHours(i));
        st.AddMark(PerfMonitor.MarkDangNhap, 900, t);
        var m = st.Marks(null, t.AddDays(-1));
        var s = PerfStore.Stat(m, PerfMonitor.MarkMoApp)!;
        Assert.Equal((3, 1800d, 3000d, 1800d), (s.Count, s.Median, s.Max, s.Last));
        Assert.Single(st.Marks(PerfMonitor.MarkDangNhap, t.AddDays(-1)));
        Assert.Null(PerfStore.Stat(m, PerfMonitor.MarkDongBoLan));
    }

    [Fact]
    public void Doc_proc_stat_meminfo_va_tinh_cpu()
    {
        var a = PerfProbe.ParseProcStat("cpu  100 0 100 700 100 0 0 0 0 0")!.Value;   // idle = 700 + iowait 100
        Assert.Equal((800UL, 1000UL), (a.Idle, a.Total));
        var b = PerfProbe.ParseProcStat("cpu  300 0 100 900 100 0 0 0 0 0")!.Value;   // +200 bận, +200 rảnh
        Assert.Equal(50, PerfProbe.CpuPct(a, b), 3);
        Assert.Equal(0, PerfProbe.CpuPct(b, a));
        Assert.Null(PerfProbe.ParseProcStat("intr 1 2 3"));
        Assert.Equal(75, PerfProbe.ParseMemInfo("MemTotal:       16000000 kB\nMemFree:  100 kB\nMemAvailable:    4000000 kB\n")!.Value, 3);
        Assert.Null(PerfProbe.ParseMemInfo("abc"));
        Assert.Null(PerfProbe.Latency("khong-co-cong"));
    }

    [Fact]
    public void Bo_do_ghi_mau_moi_phut_do_tre_moi_5_luot_va_moc_dong_bo()
    {
        using var mon = new PerfMonitor(_dir, () => "10.0.0.2:47820");
        var now = new DateTime(2026, 11, 25, 9, 0, 0);
        mon.Now = () => now; var pinged = new List<string?>();
        mon.Ping = h => { pinged.Add(h); return h is null ? null : 12.5; };
        mon.Start();
        for (var i = 0; i < 6; i++) { mon.Tick(); now = now.AddMinutes(1); }
        Assert.Equal(6, mon.Store.Count());
        var all = mon.Store.Range(now.AddHours(-1), now, 100);
        Assert.Equal(2, all.Count(x => x.LanMs is not null));                                    // lượt 1 và lượt 6
        Assert.Equal(new[] { "10.0.0.2:47820", PerfMonitor.GoogleHost, "10.0.0.2:47820", PerfMonitor.GoogleHost }, pinged);
        Assert.All(all, x => Assert.True(x.DiskTotalGb > 0 && x.RamAppMb > 0));
        using (Perf.Measure("Đồng bộ LAN", "nhận thay đổi")) { }                                  // lượt nhanh vẫn vào mốc
        PerfMonitor.MarkOp("Mở module", 5000);                                                    // thao tác khác không phải mốc
        var kinds = mon.Store.Marks(null, now.AddDays(-1)).Select(m => m.Kind).ToList();   // (test khác chạy song song cũng có thể ghi mốc đồng bộ)
        Assert.Contains(PerfMonitor.MarkDongBoLan, kinds);
        Assert.DoesNotContain("Mở module", kinds);
        mon.Dispose();
        Assert.Null(PerfMonitor.Current);
        PerfMonitor.Mark(PerfMonitor.MarkMoApp, 1);                                               // chưa bật bộ đo: không lỗi
    }

    [Fact]
    public void Dung_luong_database_theo_module_va_bang()
    {
        var s = new AppServices(Path.Combine(_dir, "kho.db"));
        try
        {
            s.Auth.CreateFirstAdmin("admin", "QT", "matkhau1"); s.Auth.Login("admin", "matkhau1");
            var big = new JsonArray(Enumerable.Range(0, 300).Select(i => (JsonNode)new JsonObject { ["Mã"] = "VRHLBTNXECR00" + i.ToString("0000") + "IMA1", ["Tên"] = "Tôm " + i }).ToArray());
            s.Lan.Write("ton-kho-an-an", new Dictionary<string, JsonArray?> { ["LoadData"] = big, ["Meta"] = new JsonArray(new JsonObject { ["a"] = 1 }) });
            s.Lan.Write("tong-hop", new Dictionary<string, JsonArray?> { ["MaDaTao"] = new JsonArray(new JsonObject { ["Mã"] = "X" }) });
            var r = DbStats.Read(s.Db);
            Assert.True(r.FileBytes > 0);
            Assert.Equal("ton-kho-an-an", r.Modules[0].Module);
            Assert.Equal(("ton-kho-an-an", "LoadData", 300L), (r.Tables[0].Module, r.Tables[0].Sheet, r.Tables[0].Rows));
            Assert.Equal(r.Tables.Sum(t => t.Bytes), r.DataBytes);
            Assert.Equal("1.5 KB", DbStats.Human(1536)); Assert.Equal("2.0 MB", DbStats.Human(2 * 1048576));
        }
        finally { s.Lan.Dispose(); }
    }

    [Fact]
    public void Nguong_vang_do()
    {
        Assert.Equal((0, 1, 2), (PerfLevel.Cpu(40), PerfLevel.Cpu(75), PerfLevel.Cpu(95)));
        Assert.Equal((0, 1, 2), (PerfLevel.Ram(60), PerfLevel.Ram(85), PerfLevel.Ram(95)));
        Assert.Equal((0, 1, 2, 2), (PerfLevel.Disk(200, 500), PerfLevel.Disk(50, 500), PerfLevel.Disk(20, 500), PerfLevel.Disk(1.5, 20)));
        Assert.Equal((0, 1, 2, 0), (PerfLevel.Net(20, true), PerfLevel.Net(300, true), PerfLevel.Net(null, true), PerfLevel.Net(null, true, expected: false)));
        Assert.Equal((0, 1), (PerfLevel.Net(300, false), PerfLevel.Net(900, false)));
    }

    [Fact]
    public void Canh_bao_hieu_nang_cpu_ram_o_day_cham_sau_cap_nhat()
    {
        var now = new DateTime(2026, 11, 25, 10, 0, 0);
        var hot = Enumerable.Range(0, 40).Select(i => new PerfStore.Sample(now.AddMinutes(-i), 92, 10, 96, 1700, 20, 240, null, null)).ToList();
        var f = PerfChecks.CheckMachine(hot, hot[0], now);
        Assert.Contains(f, x => x.Title.StartsWith("CPU cao kéo dài") && x.Sev == "vang" && x.Detail.Contains("chương trình KHÁC"));
        Assert.Contains(f, x => x.Title.StartsWith("RAM gần đầy") && x.Sev == "do");
        Assert.Contains(f, x => x.Title.StartsWith("KhoLanh đang giữ"));
        Assert.Contains(f, x => x.Title.StartsWith("Ổ chứa dữ liệu sắp đầy") && x.Sev == "vang");
        var few = hot.Take(5).ToList();                                                            // 5 phút cao → chưa "kéo dài"
        Assert.DoesNotContain(PerfChecks.CheckMachine(few, few[0], now), x => x.Title.StartsWith("CPU"));
        var calm = Enumerable.Range(0, 40).Select(i => new PerfStore.Sample(now.AddMinutes(-i), 30, 5, 60, 400, 200, 500, null, null)).ToList();
        Assert.Empty(PerfChecks.CheckMachine(calm, calm[0], now));

        Perf.Entry E(string op, long ms, string ver) => new(now, op, "", ms, 0, "PC", ver);
        var slow = new[] { E("Mở module", 3000, "1.99.0"), E("Mở module", 3200, "1.99.0"), E("Mở module", 7000, "1.100.0"), E("Mở module", 8000, "1.100.0"),
            E("Đọc file Excel", 5000, "1.99.0"), E("Đọc file Excel", 5000, "1.99.0"), E("Đọc file Excel", 5500, "1.100.0"), E("Đọc file Excel", 5600, "1.100.0") };
        var u = PerfChecks.CheckSlowAfterUpdate(slow);
        Assert.Single(u);
        Assert.Contains("1.100.0", u[0].Detail); Assert.Equal("Mở module", u[0].Mod);

        var marks = new List<PerfStore.Mark>();
        foreach (var d in new[] { 10, 12, 15, 20 }) marks.Add(new(now.AddDays(-d), PerfMonitor.MarkMoApp, 2500));
        marks.Add(new(now.AddDays(-1), PerfMonitor.MarkMoApp, 6000)); marks.Add(new(now.AddHours(-2), PerfMonitor.MarkMoApp, 6500));
        var t = PerfChecks.CheckStartupTrend(marks, now);
        Assert.Single(t); Assert.StartsWith("Mở app chậm dần", t[0].Title);
    }
}
