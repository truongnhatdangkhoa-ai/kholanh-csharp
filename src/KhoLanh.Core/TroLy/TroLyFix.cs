namespace KhoLanh.Core.TroLy;

/// <summary>
/// 1.102 (giai đoạn 4): "Sửa nhanh" cho lỗi đã biết — gán khóa xử lý vào phát hiện theo loại + tiêu đề; giao diện (TroLyView) chạy thao tác
/// rồi quét lại. Chỉ thao tác an toàn, không mất dữ liệu: sao lưu ngay, kiểm tra lại Gmail, dọn bộ nhớ (1.121: bỏ "Đồng bộ Drive ngay" cùng Drive dùng chung).
/// </summary>
public static class TroLyFix
{
    public const string SaoLuuNgay = "sao-luu-ngay", KiemTraGmail = "kiem-tra-gmail", DonBoNho = "don-bo-nho";
    /// <summary>1.186: nối lại máy chủ LAN · quét email lịch xuất ngay · nạp lại cài đặt từ bản dự phòng · tạo lại chỉ mục CSDL (đều an toàn, không mất dữ liệu).</summary>
    public const string NoiLaiLan = "noi-lai-lan", QuetEmail = "quet-email", NapLaiCaiDat = "nap-lai-cai-dat", TaoLaiChiMuc = "tao-lai-chi-muc";

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
        _ when f.Title.Contains("Gmail") => (KiemTraGmail, "Kiểm tra lại Gmail"),
        { Cat: TroLyMoRong.CatMail } => (QuetEmail, "Quét email ngay"),
        { Cat: TroLyMoRong.CatCapNhat } when f.Title.StartsWith("Thiếu chỉ mục") => (TaoLaiChiMuc, "Tạo lại chỉ mục"),
        { Cat: "dong-bo" } when f.Title.StartsWith("Không nối được máy chủ") || f.Title.StartsWith("Sổ chờ gửi") || f.Title.StartsWith("Máy này lâu chưa đồng bộ") || f.Title.StartsWith("Thay đổi chờ gửi") => (NoiLaiLan, "Nối lại máy chủ"),
        { Cat: TroLyMoRong.CatXk } when f.Title.StartsWith("Phiếu xuất khẩu hôm qua") => (QuetEmail, "Quét email ngay"),
        _ when f.Title.Contains("Cài đặt máy bị mất") => (NapLaiCaiDat, "Nạp lại cài đặt từ bản dự phòng"),
        { Cat: PerfChecks.Cat } when f.Title.StartsWith("KhoLanh đang giữ") || f.Title.StartsWith("RAM gần đầy") => (DonBoNho, "Dọn bộ nhớ KhoLanh"),
        _ => (null, null),
    };
}
