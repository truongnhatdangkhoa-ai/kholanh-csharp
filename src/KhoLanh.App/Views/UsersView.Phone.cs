using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using KhoLanh.Core.Models;
using KhoLanh.Core.PhoneSync;

namespace KhoLanh.App.Views;

/// <summary>
/// Tab "Điện thoại" (1.76): tài khoản PC dùng chung trên app điện thoại mới (cùng tên + mật khẩu) — quyền điện thoại RIÊNG
/// cho từng tài khoản: bật / tắt, vai trò (Chỉ xem · Tạo phiếu · Quản lý · Quản trị), kho được dùng. Lưu dùng chung mọi máy PC,
/// đẩy lên tab PhanQuyen của Google Sheet điện thoại (gộp với tài khoản tạo trên điện thoại).
/// </summary>
public partial class UsersView
{
    private readonly StackPanel _pRows = new() { Spacing = 6 };
    private readonly TextBlock _pStatus = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Tag = "keep" };
    private readonly List<(User U, CheckBox On, ComboBox Role, List<CheckBox> Kho)> _pEdit = new();
    /// <summary>Lần đẩy gần nhất (công cụ chụp màn hình kiểm tra).</summary>
    internal Task? PhonePushTask;

    private TabItem BuildPhoneTab()
    {
        var save = new Button { Content = "Lưu và đẩy lên điện thoại", Classes = { "primary" } };
        save.Click += async (_, _) => { PhonePushTask = SavePhoneAsync(); await PhonePushTask; };
        var head = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 8) };
        head.Children.Add(new TextBlock
        {
            Text = "Đăng nhập app điện thoại bằng ĐÚNG tên đăng nhập + mật khẩu PC. Quyền điện thoại đặt riêng ở đây, không theo quyền PC. "
                   + "Mật khẩu điện thoại tự cập nhật khi người đó đăng nhập PC hoặc đổi mật khẩu (PC chỉ cất mật khẩu dạng mã hóa, nên ai chưa đăng nhập PC từ bản này cần đăng nhập PC 1 lần).",
            TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Tag = "keep",
        });
        var bar = new WrapPanel { ItemSpacing = 10, LineSpacing = 6 };
        bar.Children.Add(save); bar.Children.Add(_pStatus);
        head.Children.Add(bar);
        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        root.Children.Add(new ScrollViewer { Content = _pRows });
        PaintPhone();
        return new TabItem { Header = "Điện thoại", Content = root };
    }

    private void PaintPhone()
    {
        _pRows.Children.Clear(); _pEdit.Clear();
        List<User> users; List<PhoneAccounts.Entry> rights;
        try { users = App.Services.Auth.ListUsers(); rights = App.Services.PhoneAccounts.List(); }
        catch (Exception e) { _pStatus.Text = e.Message; return; }
        var byTen = rights.ToDictionary(r => r.Ten);
        foreach (var u in users)
        {
            var e = byTen.GetValueOrDefault(u.Username.Trim().ToLowerInvariant()) ?? new PhoneAccounts.Entry();
            var on = new CheckBox { Content = "Dùng trên điện thoại", IsChecked = e.Bat, IsEnabled = u.IsActive, MinWidth = 170 };
            var role = new ComboBox { ItemsSource = PhoneAccounts.Roles.Select(r => r.Label).ToList(), MinWidth = 170, SelectedIndex = Math.Max(0, Array.FindIndex(PhoneAccounts.Roles, r => r.Key == e.VaiTro)) };
            var kho = PhoneAccounts.Kho.Select(k => new CheckBox { Content = k.Label, IsChecked = e.Kho.Contains(k.Key), FontSize = 12.5, Tag = k.Key }).ToList();
            var pass = e.Hash.Length > 0
                ? new TextBlock { Text = "✓ đã có mật khẩu điện thoại" + (e.HashLuc.Length >= 10 ? $" (cập nhật {e.HashLuc[8..10]}/{e.HashLuc[5..7]})" : ""), Foreground = AppTheme.Brush("#1E7B4A"), FontSize = 12 }
                : new TextBlock { Text = "Chưa có mật khẩu điện thoại — người này cần đăng nhập PC 1 lần (hoặc Quản trị đặt lại mật khẩu)", Foreground = AppTheme.Brush("#9A5B00"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
            void Enable() { role.IsEnabled = on.IsChecked == true; foreach (var k in kho) k.IsEnabled = on.IsChecked == true; }
            on.IsCheckedChanged += (_, _) => Enable(); Enable();
            var top = new WrapPanel { ItemSpacing = 12, LineSpacing = 4 };
            top.Children.Add(new TextBlock { Text = u.Username, FontWeight = FontWeight.Bold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 110 });
            top.Children.Add(new TextBlock { Text = (u.FullName.Length > 0 ? u.FullName + " · " : "") + Roles.Label(u.Role) + " (PC)" + (u.IsActive ? "" : " · đã khóa"), Foreground = AppTheme.Muted, VerticalAlignment = VerticalAlignment.Center, MinWidth = 180 });
            top.Children.Add(on); top.Children.Add(role); top.Children.Add(pass);
            var kp = new WrapPanel { ItemSpacing = 10, LineSpacing = 2, Margin = new Thickness(0, 4, 0, 0) };
            kp.Children.Add(new TextBlock { Text = "Kho được dùng:", FontSize = 12, Foreground = AppTheme.Muted, VerticalAlignment = VerticalAlignment.Center });
            foreach (var k in kho) kp.Children.Add(k);
            var card = new StackPanel(); card.Children.Add(top); card.Children.Add(kp);
            _pRows.Children.Add(new Border { Child = card, Padding = new Thickness(10, 8), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = AppTheme.Brush("#E3E8EF"), Background = AppTheme.Card });
            _pEdit.Add((u, on, role, kho));
        }
    }

    private async Task SavePhoneAsync()
    {
        var rights = _pEdit.Select(x => new PhoneAccounts.Entry
        {
            Ten = x.U.Username.Trim().ToLowerInvariant(), Bat = x.On.IsChecked == true,
            VaiTro = PhoneAccounts.Roles[Math.Max(0, x.Role.SelectedIndex)].Key,
            Kho = x.Kho.Where(k => k.IsChecked == true).Select(k => (string)k.Tag!).ToList(),
        }).ToList();
        if (!await Ui.Run(this, () => App.Services.PhoneAccounts.SaveRights(rights))) return;
        _pStatus.Text = "Đã lưu. Đang đẩy lên điện thoại…";
        try
        {
            var plan = await App.Services.PhoneSync.PushAccountsAsync();
            var on = plan.Info.Count(i => i.FromPc && i.Note == "dùng mật khẩu PC");
            _pStatus.Text = $"✓ Đã đẩy: {on} tài khoản PC dùng được trên điện thoại · {plan.Info.Count(i => !i.FromPc)} tài khoản tạo riêng trên điện thoại giữ nguyên."
                            + (plan.Warnings.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", plan.Warnings) : "");
        }
        catch (Exception e)
        {
            _pStatus.Text = "Đã lưu trên PC nhưng chưa đẩy được lên điện thoại: " + e.Message
                            + " — kiểm tra Hệ thống › Đồng bộ điện thoại (đường dẫn Web App + mã PC). Lần đồng bộ điện thoại sau sẽ tự đẩy.";
        }
        PaintPhone();
    }
}
