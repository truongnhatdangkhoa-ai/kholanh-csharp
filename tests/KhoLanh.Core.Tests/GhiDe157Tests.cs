using ClosedXML.Excel;
using KhoLanh.Core;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.157: xuất Excel đè file cũ phải ghi TẠI CHỖ (cùng một file) — ClosedXML SaveAs(đường dẫn) xóa rồi tạo lại làm biểu tượng trên Desktop nhảy chỗ.</summary>
public class GhiDe157Tests
{
    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)] private static extern int link(string oldPath, string newPath);

    private static XLWorkbook Wb(string v) { var wb = new XLWorkbook(); wb.AddWorksheet("A").Cell(1, 1).Value = v; return wb; }

    [Fact]
    public void Xuat_Excel_de_file_cu_van_la_cung_mot_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kl157_" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, "bao-cao.xlsx");
        using (var wb = Wb("cu dai hon nhieu nhieu nhieu nhieu")) wb.SaveInPlace(p);
        using (var wb = Wb("moi")) wb.SaveInPlace(p);
        using (var back = new XLWorkbook(p)) Assert.Equal("moi", back.Worksheet(1).Cell(1, 1).GetString());   // nội dung mới, không dính đuôi file cũ

        if (!OperatingSystem.IsLinux()) return;   // kiểm "cùng một file" bằng liên kết cứng (CI chạy test trên Linux)
        var l = p + ".lnk";
        Assert.Equal(0, link(p, l));
        using (var wb = Wb("lan-3")) wb.SaveInPlace(p);
        Assert.Equal(File.ReadAllBytes(p), File.ReadAllBytes(l));                    // liên kết cứng thấy nội dung mới = không tạo file khác
        using (var wb = Wb("lan-4")) wb.SaveAs(p);                                    // đối chứng: SaveAs(đường dẫn) tạo file khác
        Assert.NotEqual(File.ReadAllBytes(p), File.ReadAllBytes(l));
        Directory.Delete(dir, true);
    }
}
