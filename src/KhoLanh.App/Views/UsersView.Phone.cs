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
    private readonly StackPanel _pRows = new() { Spacing = 8 };
    private readonly TextBlock _pStatus = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Tag = "keep" };
    private readonly ComboBox _pPick = new() { MinWidth = 320 };
    private List<User> _pUsers = new();
    private readonly Dictionary<string, PhoneAccounts.Entry> _pEntries = new();
    private (CheckBox On, ComboBox Role, List<CheckBox> Kho)? _pCur;
    private string _pCurKey = "";
    private bool _pLoading;
    /// <summary>Lần đẩy gần nhất (công cụ chụp màn hình kiểm tra).</summary>
    internal Task? PhonePushTask;

    private static string PKey(User u) => u.Username.Trim().ToLowerInvariant();

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
        var pickBar = new WrapPanel { ItemSpacing = 10, LineSpacing = 4 };
        pickBar.Children.Add(new TextBlock { Text = "Tài khoản:", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        pickBar.Children.Add(_pPick);
        head.Children.Add(pickBar);
        _pPick.SelectionChanged += (_, _) => { if (!_pLoading) { CommitPhoneCur(); ShowPhoneUser(); } };
        var root = new DockPanel();
        DockPanel.SetDock(head, Dock.Top); root.Children.Add(head);
        root.Children.Add(new ScrollViewer { Content = _pRows });
        PaintPhone();
        return new TabItem { Header = "Điện thoại", Content = root };
    }

    private void PaintPhone()
    {
        _pRows.Children.Clear(); _pCur = null; _pEntries.Clear();
        List<PhoneAccounts.Entry> rights;
        try { _pUsers = App.Services.Auth.ListUsers(); rights = App.Services.PhoneAccounts.List(); }
        catch (Exception e) { _pStatus.Text = e.Message; return; }
        var byTen = rights.ToDictionary(r => r.Ten);
        foreach (var u in _pUsers) _pEntries[PKey(u)] = byTen.GetValueOrDefault(PKey(u)) ?? new PhoneAccounts.Entry { Ten = PKey(u) };
        var keep = _pCurKey;
        _pLoading = true;
        _pPick.ItemsSource = _pUsers.Select(u => u.Username + (u.FullName.Length > 0 ? " — " + u.FullName : "")
            + (_pEntries[PKey(u)].Bat ? "  ✓ điện thoại" : "")).ToList();
        _pPick.SelectedIndex = _pUsers.Count == 0 ? -1 : Math.Max(0, _pUsers.FindIndex(u => PKey(u) == keep));
        _pLoading = false;
        ShowPhoneUser();
    }

    /// <summary>Ghi các ô đang hiện về bản nháp của tài khoản đang chọn (chưa lưu).</summary>
    private void CommitPhoneCur()
    {
        if (_pCur is not { } c || _pCurKey.Length == 0 || !_pEntries.TryGetValue(_pCurKey, out var e)) return;
        e.Bat = c.On.IsChecked == true;
        e.VaiTro = PhoneAccounts.Roles[Math.Max(0, c.Role.SelectedIndex)].Key;
        e.Kho = c.Kho.Where(k => k.IsChecked == true).Select(k => (string)k.Tag!).ToList();
    }

    private void ShowPhoneUser()
    {
        _pRows.Children.Clear(); _pCur = null; _pCurKey = "";
        var i = _pPick.SelectedIndex;
        if (i < 0 || i >= _pUsers.Count) return;
        var u = _pUsers[i]; _pCurKey = PKey(u);
        var e = _pEntries[_pCurKey];
        var on = new CheckBox { Content = "Dùng trên điện thoại", IsChecked = e.Bat, IsEnabled = u.IsActive };
        var role = new ComboBox { ItemsSource = PhoneAccounts.Roles.Select(r => r.Label).ToList(), MinWidth = 200, SelectedIndex = Math.Max(0, Array.FindIndex(PhoneAccounts.Roles, r => r.Key == e.VaiTro)) };
        var kho = PhoneAccounts.Kho.Select(k => new CheckBox { Content = k.Label, IsChecked = e.Kho.Contains(k.Key), FontSize = 12.5, Tag = k.Key }).ToList();
        var pass = e.Hash.Length > 0
            ? new TextBlock { Text = "✓ đã có mật khẩu điện thoại" + (e.HashLuc.Length >= 10 ? $" (cập nhật {e.HashLuc[8..10]}/{e.HashLuc[5..7]})" : ""), Foreground = AppTheme.Brush("#1E7B4A"), FontSize = 12 }
            : new TextBlock { Text = "Chưa có mật khẩu điện thoại — người này cần đăng nhập PC 1 lần (hoặc Quản trị đặt lại mật khẩu)", Foreground = AppTheme.Brush("#9A5B00"), FontSize = 12, TextWrapping = TextWrapping.Wrap };
        void Enable() { role.IsEnabled = on.IsChecked == true; foreach (var k in kho) k.IsEnabled = on.IsChecked == true; }
        on.IsCheckedChanged += (_, _) => Enable(); Enable();
        var card = new StackPanel { Spacing = 10 };
        card.Children.Add(new TextBlock { Text = (u.FullName.Length > 0 ? u.FullName + " · " : "") + Roles.Label(u.Role) + " (PC)" + (u.IsActive ? "" : " · đã khóa"), Foreground = AppTheme.Muted });
        card.Children.Add(on);
        var rr = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        rr.Children.Add(new TextBlock { Text = "Vai trò trên điện thoại:", VerticalAlignment = VerticalAlignment.Center }); rr.Children.Add(role);
        card.Children.Add(rr);
        var kp = new WrapPanel { ItemSpacing = 10, LineSpacing = 2 };
        kp.Children.Add(new TextBlock { Text = "Kho được dùng:", FontSize = 12, Foreground = AppTheme.Muted, VerticalAlignment = VerticalAlignment.Center });
        foreach (var k in kho) kp.Children.Add(k);
        card.Children.Add(kp); card.Children.Add(pass);
        _pRows.Children.Add(new Border { Child = card, Padding = new Thickness(12, 10), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), BorderBrush = AppTheme.Brush("#E3E8EF"), Background = AppTheme.Card, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left });
        _pCur = (on, role, kho);
    }

    private async Task SavePhoneAsync()
    {
        CommitPhoneCur();
        var rights = _pUsers.Select(u => { var x = _pEntries[PKey(u)]; return new PhoneAccounts.Entry { Ten = PKey(u), Bat = x.Bat, VaiTro = x.VaiTro, Kho = x.Kho.ToList() }; }).ToList();
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
