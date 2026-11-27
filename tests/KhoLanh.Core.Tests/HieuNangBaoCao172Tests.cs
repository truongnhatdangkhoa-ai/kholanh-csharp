using KhoLanh.Core.TroLy;
using Xunit;

namespace KhoLanh.Core.Tests;

public class HieuNangBaoCao172Tests
{
    [Fact]
    public void Bao_cao_hieu_nang_co_prompt_so_lieu_va_che_thong_tin_ca_nhan()
    {
        var now = new DateTime(2026, 11, 27, 10, 0, 0);
        var pts = Enumerable.Range(0, 100).Select(k => new PerfStore.Sample(now.AddMinutes(-k), 10 + k % 50, 2, 60 + k % 30, 300, 120, 500, k % 2 == 0 ? 3 : null, 40)).ToList();
        var marks = new List<PerfStore.Mark> { new(now.AddHours(-2), PerfMonitor.MarkMoApp, 12000), new(now.AddHours(-1), PerfMonitor.MarkMoApp, 6000) };
        var slow = new List<Perf.Entry> { new(now.AddMinutes(-5), "Đọc file Excel", "Tồn An An, khach=abc@x.vn", 7000, 8000, "M", "1.172"), new(now.AddMinutes(-4), "Đọc file Excel", "Tồn An An", 9000, 8000, "M", "1.172") };
        var f = new Finding { Id = "h1", Sev = "vang", Mod = PerfChecks.Mod, Title = "CPU cao kéo dài", Detail = "liên hệ a@b.com", Evidence = { "CPU 92 % trong 30 phút" } };
        var t = TroLyHieuNangBaoCao.Build(new TroLyHieuNangBaoCao.Input
        {
            Version = "1.172.0", At = now, RangeLabel = "24 giờ", Os = "Windows 11", Cores = 8, Last = pts[0], Points = pts, Marks = marks, Slow = slow, Findings = { f }, Note = "chuyển tab giật 3 giây, email a@b.com",
        });
        Assert.Contains("Yêu cầu cho Claude", t); Assert.Contains("WINDOWS", t);
        Assert.Contains("1.172.0", t); Assert.Contains("100 mẫu", t);
        Assert.Contains("CPU cả máy: trung vị", t); Assert.Contains("p95", t);
        Assert.Contains("Đọc file Excel", t); Assert.Contains("2 |", t);                  // gộp 2 lần chậm cùng thao tác
        Assert.Contains("CPU cao kéo dài", t); Assert.Contains("chuyển tab giật 3 giây", t);
        Assert.DoesNotContain("a@b.com", t); Assert.DoesNotContain("abc@x.vn", t);       // email bị che
        Assert.Contains("Ổ chứa dữ liệu: còn 120", t);
        // không có số liệu nào vẫn soạn được (không ném lỗi)
        Assert.Contains("chưa có mẫu", TroLyHieuNangBaoCao.Build(new TroLyHieuNangBaoCao.Input { Version = "x", At = now }));
    }
}
