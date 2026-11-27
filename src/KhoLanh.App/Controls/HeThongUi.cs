using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.Core.Lan;
using KhoLanh.Core.Models;

namespace KhoLanh.App.Controls;

/// <summary>
/// 1.179 — phần giao diện chung của "mục Hệ thống chỉ máy chủ sửa": nhãn "Chỉ xem", thẻ tình trạng lấy từ bản chụp dùng chung của máy chủ
/// (<see cref="HtTrangThai"/>), và các dòng mục (Điện thoại / Gmail sao lưu / Gmail lịch xuất / Sheet tài khoản / GitHub).
/// </summary>
public static class HeThongUi
{
    private static KhoLanh.Core.AppServices S => App.Services;

    /// <summary>Máy này chỉ xem các mục chỉ-máy-chủ-sửa.</summary>
    public static bool ChiXem => S.HeThong.ChiXem;

    /// <summary>Dải nhãn đầu trang ở máy khác; null khi máy này là máy chủ / máy đơn.</summary>
    public static Border? Banner()
    {
        if (!ChiXem) return null;
        return new Border
        {
            Background = AppTheme.Brush("#EEEBFC"), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 9),
            Child = new TextBlock { Text = "🔒 " + S.HeThong.Nhan(), Foreground = AppTheme.Brush("#5B4BC4"), FontWeight = FontWeight.SemiBold, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Tag = "keep" },
        };
    }

    /// <summary>Bản chụp để hiện: máy chủ / máy đơn = tính tại chỗ; máy khác = đọc bản máy chủ đã ghi (null = máy chủ chưa báo).</summary>
    public static HtTrangThai? Snapshot() => ChiXem ? S.HeThong.Read() : S.HeThong.Build();

    public static Border Pill(string kind, string text)
    {
        var b = new Border { Classes = { "pill" }, VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = text } };
        if (kind is "ok" or "warn" or "bad" or "info") b.Classes.Add(kind);
        else { b.Background = AppTheme.Brush("#EEF1F5"); ((TextBlock)b.Child).Foreground = AppTheme.Brush("#5D6B78"); }
        return b;
    }

    public sealed record Item(string Name, string Kind, string Pill, string Detail);

    private static string Time(string iso) => HtTrangThai.Parse(iso) is { } d ? d.ToLocalTime().ToString("HH:mm dd/MM", CultureInfo.InvariantCulture) : "";
    private static Item Off(string name) => new(name, "off", "○ Chưa có thông tin", "Máy chủ chưa báo.");

    public static Item Phone(HtTrangThai? t)
    {
        const string n = "Đồng bộ điện thoại";
        if (t is null) return Off(n);
        if (!t.DtNoi) return new(n, "warn", "● Chưa nối", "Máy chủ chưa nối Google Sheet điện thoại (Hệ thống › Kết nối trên máy chủ).");
        var d = "Tự đồng bộ: " + (t.DtAuto.Length > 0 ? t.DtAuto : "Tắt") + $" · {t.DtBang} bảng đẩy lên" + (t.DtLan.Length > 0 ? " · " + t.DtLan : "");
        return t.DtLoi.Length > 0 ? new(n, "bad", "● Lỗi kết nối", d + " · lỗi: " + t.DtLoi) : new(n, "ok", "● Đã nối", d);
    }

    public static Item Drive(HtTrangThai? t)
    {
        const string n = "Sao lưu Google Drive (Gmail)";
        if (t is null) return Off(n);
        if (!t.DriveDn) return new(n, "warn", "● Chưa đăng nhập", "Máy chủ chưa đăng nhập Gmail sao lưu.");
        var d = $"Gmail {t.DriveMail} · mỗi {t.DriveGio} giờ" + (t.DriveLan.Length > 0 ? $" · sao lưu lần cuối {Time(t.DriveLan)}" : " · chưa sao lưu lần nào");
        return t.DriveLoi.Length > 0 ? new(n, "bad", "● Lỗi", d + " · lỗi lần trước: " + t.DriveLoi) : new(n, "ok", "● Đã kết nối", d);
    }

    public static Item Mail(HtTrangThai? t)
    {
        const string n = "Gmail nhận lịch xuất";
        if (t is null) return Off(n);
        if (!t.MailLx) return new(n, "warn", "● Chưa kết nối", "Máy chủ chưa khai báo Gmail nhận lịch xuất.");
        var st = t.MailLxTt.TrimStart('✓', '✖', ' ');
        return t.MailLxTt.StartsWith("✖") ? new(n, "bad", "● Lỗi", st) : new(n, "ok", "● Đã kết nối", st);
    }

    public static Item Sheet(HtTrangThai? t)
    {
        const string n = "Tài khoản dùng chung (Google Sheet)";
        if (t is null) return Off(n);
        return t.SheetChung ? new(n, "ok", "● Đã kết nối", "Máy chủ đang dùng Sheet tài khoản chung.") : new(n, "warn", "● Chưa kết nối", "Máy chủ chưa bật tài khoản dùng chung.");
    }

    public static Item Git(HtTrangThai? t)
    {
        const string n = "Cập nhật phần mềm (GitHub)";
        if (t is null) return Off(n);
        return t.GitHub ? new(n, "ok", "● Đã kết nối", "Máy chủ có mã cập nhật.") : new(n, "warn", "● Chưa có mã", "Máy chủ chưa có mã GitHub.");
    }

    private static bool CanSee => Roles.IsAdmin(S.Session.Role) || S.Session.Can("backup") || S.Session.Can("phone");

    /// <summary>Thẻ "Tình trạng hệ thống": dòng máy chủ (chạy / tắt theo lịch / chưa báo) + các mục. Null khi máy đơn hoặc tài khoản không có quyền xem.</summary>
    public static Border? StatusCard()
    {
        if (!S.Lan.Enabled || !CanSee) return null;
        var t = Snapshot();
        var (kind, text) = ChiXem ? HeThongService.MayChuText(t, DateTime.UtcNow) : ("ok", "Máy này là máy chủ — tình trạng dưới đây là của chính máy này, máy khác thấy bản chỉ xem.");
        var box = new StackPanel { Spacing = 8 };
        box.Children.Add(new TextBlock { Text = ChiXem ? "Tình trạng hệ thống (theo máy chủ, chỉ xem)" : "Tình trạng hệ thống", Classes = { "section" } });
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(Pill(kind, kind == "ok" ? "● Đang chạy" : kind == "info" ? "○ Đang tắt" : "● Chưa báo"));
        head.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Tag = "keep", FontSize = 13 });
        box.Children.Add(head);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("260,150,*"), RowSpacing = 5, ColumnSpacing = 8 };
        var r = 0;
        foreach (var it in new[] { Phone(t), Drive(t), Mail(t), Sheet(t), Git(t) })
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var a = new TextBlock { Text = it.Name, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var p = Pill(it.Kind, it.Pill); p.HorizontalAlignment = HorizontalAlignment.Left;
            var d = new TextBlock { Text = it.Detail, Classes = { "muted" }, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Tag = "keep", VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(a, r); Grid.SetRow(p, r); Grid.SetRow(d, r); Grid.SetColumn(p, 1); Grid.SetColumn(d, 2);
            g.Children.Add(a); g.Children.Add(p); g.Children.Add(d); r++;
        }
        box.Children.Add(g);
        return new Border { Classes = { "card" }, Padding = new Thickness(16, 12), Child = box };
    }

    /// <summary>Thẻ đơn cho một mục (dùng ở tab Điện thoại / Sao lưu của máy khác).</summary>
    public static Border ItemCard(Item it)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(new TextBlock { Text = it.Name, FontSize = 14.5, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center });
        head.Children.Add(Pill(it.Kind, it.Pill));
        var box = new StackPanel { Spacing = 4 };
        box.Children.Add(head);
        box.Children.Add(new TextBlock { Text = it.Detail, TextWrapping = TextWrapping.Wrap, Classes = { "muted" }, Tag = "keep", FontSize = 12.5 });
        return new Border { Classes = { "card" }, Padding = new Thickness(16, 12), Child = box };
    }
}
