using Avalonia.Controls;
using Avalonia.Interactivity;
using KhoLanh.Core.Models;

namespace KhoLanh.App.Views;

public partial class UsersView : UserControl, IPage
{
    public string Title => "Tài khoản";
    public string Subtitle => App.Services.Accounts.Enabled
        ? "Tài khoản dùng chung cho mọi máy qua Google Sheet: sửa ở đây là lưu lên Sheet, máy khác nhận ở lần đăng nhập sau."
          + (App.Services.Accounts.CanWrite ? "" : " Máy này chỉ đọc — sửa ở máy quản trị.")
        : "Tạo tài khoản, phân quyền tới từng màn hình, khóa tài khoản và đặt lại mật khẩu. Quyền mới có hiệu lực từ lần đăng nhập sau.";
    private User _editing = new();
    private readonly Dictionary<string, ComboBox> _permBoxes = new();
    private bool _loading;

    public UsersView()
    {
        InitializeComponent();
        BuildPermGrid();
        Load();
        Edit(new User());
        if (App.Services.Accounts.Enabled) _ = Refresh();
    }

    /// <summary>Lấy danh sách mới nhất từ Google Sheet (lỗi mạng: giữ danh sách trong máy).</summary>
    private async Task Refresh()
    {
        try { await App.Services.Accounts.PullAsync(); Load(); }
        catch { /* mất mạng: dùng bản lưu trong máy */ }
    }

    private void Load() => Grid.ItemsSource = App.Services.Auth.ListUsers();

    private void Edit(User u)
    {
        _editing = u;
        var isNew = u.Id == 0;
        FormTitle.Text = isNew ? "Tài khoản mới" : $"Sửa tài khoản {u.Username}";
        UserBox.Text = u.Username; UserBox.IsReadOnly = !isNew;
        NameBox.Text = u.FullName;
        RoleBox.SelectedIndex = u.Role switch { Roles.Admin => 2, Roles.Manager => 1, _ => 0 };
        PassPanel.IsVisible = isNew; PassBox.Text = "";
        ActiveBox.IsChecked = u.IsActive;
        ResetButton.IsVisible = !isNew;
        _loading = true;
        CustomBox.IsChecked = u.Role != Roles.Admin && u.Permissions.Length > 0;
        _loading = false;
        ShowPerms();
    }

    private string SelectedRole => (RoleBox.SelectedItem as ComboBoxItem)?.Tag as string ?? Roles.Staff;

    /// <summary>Bảng mục / mức quyền: mỗi màn hình một ô chọn Không / Xem / Xem + xuất file / Sửa.</summary>
    private void BuildPermGrid()
    {
        var row = 0;
        string? group = null;
        foreach (var item in Perm.Items)
        {
            if (item.Group != group)
            {
                group = item.Group;
                PermGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var g = new TextBlock { Text = group.ToUpper(), Classes = { "label" }, Margin = new Avalonia.Thickness(0, row == 0 ? 0 : 8, 0, 2) };
                Avalonia.Controls.Grid.SetRow(g, row++); Avalonia.Controls.Grid.SetColumnSpan(g, 2);
                PermGrid.Children.Add(g);
            }
            PermGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = item.Label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            if (item.Hint.Length > 0) ToolTip.SetTip(label, item.Hint);
            var levels = new[] { Perm.None }.Concat(item.Levels).ToList();
            var box = new ComboBox
            {
                ItemsSource = levels.Select(l => new ComboBoxItem { Content = Perm.LevelLabel(l), Tag = l }).ToList(),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Margin = new Avalonia.Thickness(0, 2),
            };
            Avalonia.Controls.Grid.SetRow(label, row); Avalonia.Controls.Grid.SetRow(box, row); Avalonia.Controls.Grid.SetColumn(box, 1);
            row++;
            PermGrid.Children.Add(label); PermGrid.Children.Add(box);
            _permBoxes[item.Key] = box;
        }
    }

    /// <summary>Hiện mức quyền: quyền riêng (nếu có) hoặc mặc định theo vai trò đang chọn.</summary>
    private void ShowPerms()
    {
        var role = SelectedRole;
        var custom = CustomBox.IsChecked == true && role != Roles.Admin;
        CustomBox.IsEnabled = role != Roles.Admin;
        var map = role == Roles.Admin
            ? Perm.Effective(new User { Role = Roles.Admin })
            : custom && _editing.Permissions.Length > 0 && _editing.Role == role
                ? Perm.Effective(_editing)
                : custom ? CurrentBoxes() ?? Perm.Defaults(role) : Perm.Defaults(role);
        foreach (var (key, box) in _permBoxes)
        {
            var lv = map.GetValueOrDefault(key, Perm.None);
            box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag! == lv);
            box.IsEnabled = custom;
        }
        PermHint.Text = role == Roles.Admin ? "Quản trị luôn có toàn quyền."
            : custom ? "Đang dùng quyền riêng — chọn mức cho từng màn hình rồi bấm Lưu."
            : $"Đang theo quyền mặc định của vai trò {Roles.Label(role)}. Đánh dấu ô trên để chỉnh riêng.";
    }

    private Dictionary<string, string>? CurrentBoxes()
    {
        if (_permBoxes.Values.Any(b => b.SelectedItem is null)) return null;
        return _permBoxes.ToDictionary(p => p.Key, p => (string)((ComboBoxItem)p.Value.SelectedItem!).Tag!);
    }

    private void OnCustomChanged(object? sender, RoutedEventArgs e) { if (!_loading) ShowPerms(); }
    private void OnRoleChanged(object? sender, SelectionChangedEventArgs e) { if (!_loading && _permBoxes.Count > 0) ShowPerms(); }

    private string PermissionsToSave() =>
        CustomBox.IsChecked == true && SelectedRole != Roles.Admin && CurrentBoxes() is { } m ? Perm.Serialize(m) : "";

    private void OnSelect(object? sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is User u) Edit(u);
    }

    private void OnNew(object? sender, RoutedEventArgs e) { Grid.SelectedItem = null; Edit(new User()); UserBox.Focus(); }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var role = SelectedRole;
        var perms = PermissionsToSave();
        var editing = _editing; var user = UserBox.Text ?? ""; var name = NameBox.Text ?? ""; var pass = PassBox.Text ?? ""; var active = ActiveBox.IsChecked == true;
        // Chạy nền: khi dùng chung tài khoản, mỗi thay đổi được ghi lên Google Sheet (vài giây) — không làm đơ cửa sổ.
        var ok = await Ui.RunAsync(this, () => Task.Run(() =>
        {
            if (editing.Id == 0)
            {
                var u = App.Services.Auth.CreateUser(user, name, pass, role, perms);
                if (!active) App.Services.Auth.UpdateUser(u.Id, u.FullName, role, false);
            }
            else
            {
                App.Services.Auth.UpdateUser(editing.Id, name, role, active);
                App.Services.Auth.SetPermissions(editing.Id, perms);
            }
        }));
        if (ok) { Load(); Edit(new User()); }
    }

    private async void OnReset(object? sender, RoutedEventArgs e)
    {
        var pw = await Ui.Prompt(this, $"Mật khẩu mới cho {_editing.Username} (ít nhất 6 ký tự):", "Đặt lại mật khẩu", password: true);
        if (pw is null) return;
        var id = _editing.Id;
        if (await Ui.RunAsync(this, () => Task.Run(() => App.Services.Auth.ResetPassword(id, pw))))
            await Ui.Message(this, $"Đã đặt lại mật khẩu cho {_editing.Username}.");
    }
}
