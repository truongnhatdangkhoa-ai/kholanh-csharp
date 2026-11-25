using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using KhoLanh.Core.TonKho;

namespace KhoLanh.App.Controls;

/// <summary>
/// 1.73 (N3): khung "So với mẫu file lần nạp trước" trong hộp xem trước khi nạp (Tồn kho An An / Tồn kho gửi / Tồn theo vị trí).
/// Đỏ = lỗi (thiếu cột bắt buộc, ngày số liệu cũ hơn) · vàng = cần kiểm tra · tím nhạt = thông tin; không có gì → nhãn xanh "đúng mẫu".
/// Chữ không mờ để <c>HintTrim</c> không ẩn.
/// </summary>
public static class MauFileBox
{
    public static Control? Build(MauFile.Mau? truoc, IReadOnlyList<MauFile.CanhBao> list)
    {
        if (list.Count == 0)
        {
            if (truoc is null) return null;
            return new Border
            {
                Classes = { "pill", "ok" }, Margin = new Thickness(0, 2), Padding = new Thickness(10, 3), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                Child = new TextBlock { Text = $"✓ Đúng mẫu file lần nạp trước ({truoc.Headers.Count} cột · {truoc.FileName}{(truoc.SavedAt.Length > 0 ? " · " + truoc.SavedAt : "")})", FontSize = 12.5 },
            };
        }
        var loi = MauFile.CoLoi(list);
        var vang = list.Any(x => x.Muc == MauFile.Muc.CanhBao);
        var (bg, fg) = loi ? ("#FDE8E6", "#B42318") : vang ? ("#FFF2D6", "#9A5B00") : ("#EEEBFC", "#5B4BC4");
        var sp = new StackPanel { Spacing = 3 };
        sp.Children.Add(new TextBlock
        {
            Text = (loi ? "⚠ File KHÁC MẪU lần nạp trước — kiểm tra trước khi nạp" : vang ? "⚠ File có thay đổi so với lần nạp trước" : "So với lần nạp trước")
                   + (truoc is not null ? $" (“{truoc.FileName}”{(truoc.SavedAt.Length > 0 ? ", " + truoc.SavedAt : "")})" : ""),
            FontWeight = FontWeight.Bold, FontSize = 13.5, Foreground = AppTheme.Brush(fg), TextWrapping = TextWrapping.Wrap,
        });
        foreach (var c in list)
        {
            var col = c.Muc switch { MauFile.Muc.Loi => "#B42318", MauFile.Muc.CanhBao => "#9A5B00", _ => "#5B4BC4" };
            sp.Children.Add(new TextBlock
            {
                Text = "• " + c.Text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = AppTheme.Brush(col),
                FontWeight = c.Muc == MauFile.Muc.Loi ? FontWeight.SemiBold : FontWeight.Normal,
            });
        }
        return new Border
        {
            Background = AppTheme.Brush(bg), BorderBrush = AppTheme.Brush(fg), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 7), Child = sp,
        };
    }

    /// <summary>So file đang xem trước với mẫu lần nạp trước của module (đọc qua LanService — máy LAN / Drive dùng chung cùng thấy).
    /// <paramref name="warns"/> = cảnh báo có sẵn của hộp xem trước: đã báo trùng ngày / đã nạp thì bỏ dòng "ngày trùng" của mẫu.</summary>
    public static (MauFile.Mau? Truoc, List<MauFile.CanhBao> List) So(string db, MauFile.Mau moi, IReadOnlyList<MauFile.Cot> batBuoc, string? ngayDangCo, IEnumerable<string>? warns = null)
    {
        var (truoc, list) = MauFile.SoVoiLanTruoc(App.Services.Lan, db, moi, batBuoc, ngayDangCo);
        if (warns?.Any(w => w.Contains("Ngày số liệu trùng") || w.Contains("ĐÃ ĐƯỢC NẠP") || w.Contains("đã được nạp lúc")) == true) list.RemoveAll(x => x.Key == "ngay-bang");
        return (truoc, list);
    }

    /// <summary>Câu hỏi xác nhận khi bấm "Vẫn nạp" lúc còn lỗi đỏ.</summary>
    public static string ConfirmText(IEnumerable<MauFile.CanhBao> list) =>
        "File khác mẫu lần nạp trước:\n• " + string.Join("\n• ", list.Where(x => x.Muc == MauFile.Muc.Loi).Select(x => x.Text)) + "\n\nVẫn nạp file này?";
}
