namespace KhoLanh.Core.TroLy;

/// <summary>
/// 1.102 (giai đoạn 4): "Sửa nhanh" cho lỗi đã biết — gán khóa xử lý vào phát hiện theo loại + tiêu đề; giao diện (TroLyView) chạy thao tác
/// rồi quét lại. Chỉ thao tác an toàn, không mất dữ liệu: sao lưu ngay, đồng bộ Drive ngay, kiểm tra lại Gmail, dọn bộ nhớ.
/// </summary>
public static class TroLyFix
{
    public const string SaoLuuNgay = "sao-luu-ngay", DongBoDrive = "dong-bo-drive", KiemTraGmail = "kiem-tra-gmail", DonBoNho = "don-bo-nho";

    public static void Assign(IEnumerable<Finding> list)
    {
        foreach (var f in list)
        {
            if (f.Fix.Length > 0 || f.Sev == "xanh") continue;
            var (k, l) = Pick(f);
            if (k is not null) { f.Fix = k; f.FixLabel = l!; }
        }
    }

    public static (string? Key, string? Label) Pick(Finding f) => f switch
    {
        { Cat: "sao-luu" } when f.Title.StartsWith("Chưa có bản sao lưu") || f.Title.Contains("cũ") || f.Title.Contains("lâu") => (SaoLuuNgay, "Sao lưu ngay"),
        { Cat: "dong-bo" } when f.Title.Contains("Google Drive") => (DongBoDrive, "Đồng bộ Drive ngay"),
        _ when f.Title.Contains("Gmail") => (KiemTraGmail, "Kiểm tra lại Gmail"),
        { Cat: PerfChecks.Cat } when f.Title.StartsWith("KhoLanh đang giữ") || f.Title.StartsWith("RAM gần đầy") => (DonBoNho, "Dọn bộ nhớ KhoLanh"),
        _ => (null, null),
    };
}
