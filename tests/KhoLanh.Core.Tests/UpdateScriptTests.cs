using KhoLanh.Core.Services;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Script tiến trình phụ thay KhoLanh.exe: chờ cửa sổ hiện tại + MỌI cửa sổ khác cùng exe thoát rồi mới đổi file (lỗi FileNotFoundException 1.41.0).</summary>
public class UpdateScriptTests
{
    [Fact]
    public void Cho_moi_cua_so_cung_exe_roi_moi_doi_file()
    {
        var s = UpdateScript.Build(@"C:\Kho Lanh's\KhoLanh.exe", 4321, rollback: false, @"C:\Kho Lanh's\loi-cap-nhat.log");
        Assert.Contains(@"$exe = 'C:\Kho Lanh''s\KhoLanh.exe'", s);                       // dấu ' trong đường dẫn được nhân đôi
        Assert.Contains("$_.Id -ne 4321", s);                                              // đếm cửa sổ KHÁC cùng file exe
        Assert.Contains("$_.Path -eq $exe", s);
        Assert.Contains("$p.WaitForExit(60000)", s);
        var wait = s.IndexOf("while ((Others).Count -gt 0)", StringComparison.Ordinal);
        var move = s.IndexOf("Move-Item -LiteralPath $exe -Destination $cu", StringComparison.Ordinal);
        Assert.True(wait > 0 && move > wait, "phải chờ các cửa sổ khác thoát TRƯỚC khi đổi tên exe");
        Assert.Contains("'--da-cap-nhat'", s);
        Assert.Contains("Add-Content -LiteralPath $log", s);                               // lỗi → ghi log + mở lại bản đang có
        var rb = UpdateScript.Build(@"C:\K\KhoLanh.exe", 1, rollback: true, @"C:\K\l.log");
        Assert.Contains("'--da-quay-lai'", rb);
        Assert.True(rb.IndexOf("while ((Others).Count -gt 0)", StringComparison.Ordinal) < rb.IndexOf("Move-Item -LiteralPath $exe -Destination $doi", StringComparison.Ordinal));
    }
}
