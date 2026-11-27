using ClosedXML.Excel;

namespace KhoLanh.Core;

/// <summary>
/// 1.157 — Ghi đè file TẠI CHỖ (người dùng: "xuất file ra Desktop đè lên file cũ thì file mới bị chạy chỗ").
/// <c>XLWorkbook.SaveAs(đường dẫn)</c> của ClosedXML XÓA file cũ rồi tạo file mới → Windows coi là file khác, biểu tượng trên Desktop
/// nhảy sang ô trống. Ở đây dựng xong file trong bộ nhớ rồi mới mở file cũ ghi đè nội dung (cùng một file, giữ chỗ trên Desktop);
/// lỗi lúc dựng file thì file cũ còn nguyên.
/// </summary>
public static class GhiDe
{
    public static void SaveInPlace(this IXLWorkbook wb, string path)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        Write(path, ms.GetBuffer().AsSpan(0, (int)ms.Length));
    }

    /// <summary>Ghi nội dung vào file: có rồi thì ghi đè đúng file đó (không xóa / tạo lại), chưa có thì tạo.</summary>
    public static void Write(string path, ReadOnlySpan<byte> data)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        using var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read);
        fs.Write(data);
        fs.SetLength(data.Length);
    }
}
