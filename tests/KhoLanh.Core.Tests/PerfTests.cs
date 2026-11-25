using KhoLanh.Core.TroLy;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.72 (A4): ghi thao tác chậm vào logs/cham.log và Trợ lý báo lại.</summary>
public class PerfTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-perf-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { Perf.DataDir = ""; try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Chi_ghi_thao_tac_qua_2_giay_va_tro_ly_gom_lai()
    {
        Perf.DataDir = _dir;
        Perf.Record("Mở module", "Tồn kho An An", 500);                 // nhanh: không ghi
        Perf.Record("Mở module", "Tồn kho An An", 2500, 0);
        Perf.Record("Mở module", "Tồn kho An An", 12_000, 0);
        Perf.Record("Nạp file Excel", "Tồn theo vị trí", 3000, 8344);
        var list = Perf.Read(_dir, DateTime.Now);
        Assert.Equal(3, list.Count);
        Assert.Equal(8344, list.Single(x => x.Op == "Nạp file Excel").Rows);
        var g = Perf.Summary(list);
        Assert.Equal("Mở module", g[0].Op);
        Assert.Equal(12_000, g[0].MaxMs); Assert.Equal(2, g[0].Count);
        var f = TroLyChecks.CheckSlow(list);
        Assert.Equal("vang", f.Single(x => x.Mod == "Mở module").Sev);       // ≥ 10 giây
        Assert.Equal("xanh", f.Single(x => x.Mod == "Nạp file Excel").Sev);
        Assert.Empty(Perf.Read(_dir, DateTime.Now.AddDays(8)));                 // quá 7 ngày không tính
    }

    [Fact]
    public void Measure_do_thoi_gian_thuc()
    {
        // Perf là tĩnh: test khác chạy song song (Nhập liệu, đồng bộ điện thoại… cũng đo qua Perf) có thể ghi vào `seen`
        // trong lúc ngưỡng đang hạ xuống 50 ms → chỉ xét lượt đo của chính test này (trước đây thỉnh thoảng đỏ khi chạy cả bộ).
        Perf.DataDir = _dir; var seen = new System.Collections.Concurrent.ConcurrentQueue<Perf.Entry>();
        Perf.Recorded = seen.Enqueue; Perf.SlowMs = 50;
        try { using (Perf.Measure("Thử đo thời gian thực", "a")) Thread.Sleep(120); using (Perf.Measure("Thử đo thời gian thực", "b")) { } }
        finally { Perf.Recorded = null; Perf.SlowMs = 2000; }
        var mine = seen.Where(e => e.Op == "Thử đo thời gian thực").ToList();
        Assert.Single(mine); Assert.Equal("a", mine[0].Detail);
    }
}
