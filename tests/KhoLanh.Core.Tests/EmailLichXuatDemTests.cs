using KhoLanh.Core.LichXuat;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.121 (người dùng: "vì sao app tải toàn bộ thư" — 813 thư mỗi lượt quét): thư đã đọc nằm trong đệm, lượt sau chỉ tải thư mới.</summary>
public class EmailLichXuatDemTests
{
    [Fact]
    public void Dem_thu_luu_lai_bo_thu_cu_va_chiu_file_hong()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kl-dem-" + Guid.NewGuid().ToString("N")[..8]);
        var path = Path.Combine(dir, "email-lich-xuat-dem.json");
        try
        {
            var today = DateTime.Today;
            var cache = new Dictionary<string, LichXuatEmail.Msg>
            {
                ["g1"] = new("1", "t1", today.AddDays(-2), "A <a@x.vn>", "LSX 26.11.013", "ETD 27/11"),
                ["g2"] = new("2", "t1", today.AddDays(-90), "A <a@x.vn>", "LSX 26.08.001", "cũ"),
            };
            MailCache.Save(path, cache, today.AddDays(-60));
            var back = MailCache.Load(path);
            Assert.True(back.ContainsKey("g1"));
            Assert.False(back.ContainsKey("g2"));                  // quá mốc giữ → bỏ
            Assert.Equal("ETD 27/11", back["g1"].Text);
            Assert.Equal("LSX 26.11.013", back["g1"].Subject);
            File.WriteAllText(path, "{hỏng");
            Assert.Empty(MailCache.Load(path));                    // file hỏng → coi như trống, lượt sau tải lại
            Assert.Empty(MailCache.Load(null));
            MailCache.Save(null, cache, today);                    // không đệm: không lỗi
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
