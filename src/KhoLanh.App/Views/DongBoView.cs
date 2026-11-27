using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using KhoLanh.Core.Models;

namespace KhoLanh.App.Views;

/// <summary>
/// ĐỒNG BỘ &amp; SAO LƯU — 1.121 (người dùng duyệt mẫu): 3 tab <b>Máy chủ · Điện thoại · Sao lưu</b>.
///  • Máy chủ (<see cref="MayChuView"/>, MỌI tài khoản; nội dung theo quyền): tình trạng nối máy chủ của máy này, máy đang nối, trùng sửa.
///  • Điện thoại: <see cref="PhoneSyncView"/> (quyền "phone"; kết nối đổi ở Hệ thống › Kết nối).
///  • Sao lưu (Quản trị / quyền "backup"): Trên máy này · Google Drive cá nhân (Gmail).
/// Bỏ tab "Tình trạng" và 3 thẻ chọn "Chỉ máy này / Mạng LAN / Thư mục - Drive dùng chung" (bỏ hẳn Drive dùng chung — <see cref="KhoLanh.Core.Lan.DriveRetire"/>).
/// Khóa mở cũ: "lan" / "drive" / "dulieu" → Máy chủ · "phone" → Điện thoại · "saoluu" / "dcn" → Sao lưu.
/// </summary>
public sealed class DongBoView : UserControl, IPage, IKeepFresh
{
    public string Title => "Đồng bộ & sao lưu";
    public string Subtitle => "Máy chủ: máy này nối máy chủ thế nào (trong kho / qua Internet), máy đang nối, trùng sửa. Điện thoại. Sao lưu trên máy + Google Drive cá nhân.";

    private static KhoLanh.Core.AppServices S => App.Services;
    private static bool Admin => Roles.IsAdmin(S.Session.Role);
    private static bool CanBackup => Admin || S.Session.Can("backup");
    private static bool CanPhone => Admin || S.Session.Can("phone");

    internal TabControl Tabs { get; } = new();
    internal MayChuView MayChu { get; } = new();
    private int _tabMayChu = -1, _tabPhone = -1, _tabSaoLuu = -1;

    /// <summary>Mở thẳng: "lan" / "drive" / "dulieu" → Máy chủ · "phone" → Điện thoại · "saoluu" / "dcn" → Sao lưu.</summary>
    public DongBoView(string start = "")
    {
        int n = 0;
        Tabs.Items.Add(Tab("Máy chủ", MayChu)); _tabMayChu = n++;
        if (CanPhone) { Tabs.Items.Add(Tab("Điện thoại", new PhoneSyncView())); _tabPhone = n++; }
        if (CanBackup) { Tabs.Items.Add(Tab("Sao lưu", BuildBackup(start))); _tabSaoLuu = n++; }
        var pick = start switch { "phone" => _tabPhone, "saoluu" or "dcn" => _tabSaoLuu, _ => _tabMayChu };
        Tabs.SelectedIndex = Math.Max(0, pick);
        Content = Tabs;
    }

    private static TabItem Tab(string h, Control c) => new() { Header = h, Content = new Border { Padding = new Thickness(0, 6, 0, 0), Child = c } };

    private static Control BuildBackup(string start)
    {
        // 1.179: sao lưu Google Drive là việc của máy chủ — máy khác chỉ xem tình trạng (bản máy chủ báo), không đăng nhập / đổi lịch / khôi phục từ đây.
        Control drive = new DriveCaNhanPanel();
        if (KhoLanh.App.Controls.HeThongUi.ChiXem)
        {
            var box = new StackPanel { Spacing = 10, Margin = new Thickness(0, 0, 8, 12) };
            if (KhoLanh.App.Controls.HeThongUi.Banner() is { } bn) box.Children.Add(bn);
            var t = S.HeThong.Read();
            box.Children.Add(KhoLanh.App.Controls.HeThongUi.ItemCard(KhoLanh.App.Controls.HeThongUi.Drive(t)));
            box.Children.Add(new TextBlock { Text = KhoLanh.Core.Lan.HeThongService.MayChuText(t, DateTime.UtcNow).Text, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, Tag = "keep", FontSize = 12.5 });
            drive = new ScrollViewer { Content = box };
        }
        var sub = Ui.SubTabs(("Trên máy này", new LocalBackupPanel()), ("Google Drive cá nhân (Gmail)", drive));
        if (start == "dcn") sub.SelectedIndex = 1;
        return sub;
    }
}
