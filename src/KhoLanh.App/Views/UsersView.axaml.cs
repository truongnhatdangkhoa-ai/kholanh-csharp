using Avalonia.Controls;
using Avalonia.Interactivity;
using KhoLanh.Core.Models;

namespace KhoLanh.App.Views;

public partial class UsersView : UserControl, IPage
{
    public string Title => "Tài khoản";
    public string Subtitle => App.Services.Accounts.Enabled ? "Dùng chung cho mọi máy qua Google Sheet." : "Quyền mới có hiệu lực từ lần đăng nhập sau.";
    private User _editing = new();
    private readonly Dictionary<string, ComboBox> _permBoxes = new();
    private bool _loading;

    public UsersView()
    {
        InitializeComponent();
        // 1.97: cột Vai trò = biểu tượng tài khoản theo quyền + nhãn (như thanh menu)
        var ri = List.Columns.ToList().FindIndex(c => c.Header as string == "Vai trò");
        if (ri >= 0)
        {
            List.Columns[ri] = new DataGridTemplateColumn
            {
                Header = "Vai trò", Width = new DataGridLength(190),
                CellTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Row>((row, _) => row is null ? new TextBlock() : new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 7, Margin = new Avalonia.Thickness(8, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    Children = { KhoLanh.App.Controls.RoleBadge.Avatar(row.U.Role, 22), new TextBlock { Text = row.RoleLabel, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center } },
                }),
            };
        }
        BuildPermGrid();
        Tabs.Items.Add(BuildPhoneTab());   // 1.76: quyền điện thoại riêng
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

    /// <summary>1 dòng bảng danh sách.</summary>
    public sealed record Row(User U)
    {
        public string Username => U.Username; public string FullName => U.FullName; public string RoleLabel => U.RoleLabel; public string StatusLabel => U.StatusLabel; public string GoogleEmail => U.GoogleEmail ?? "";
    }

    private void Load()
    {
        var list = App.Services.Auth.ListUsers();
        List.ItemsSource = list.Select(u => new Row(u)).ToList();
        CountText.Text = $"{list.Count} tài khoản";
    }

    private void Edit(User u)
    {
        _editing = u;
        var isNew = u.Id == 0;
        FormTitle.Text = isNew ? "Tài khoản mới" : $"Sửa tài khoản {u.Username}";
        UserBox.Text = u.Username; UserBox.IsReadOnly = !isNew;
        NameBox.Text = u.FullName;
        GoogleBox.Text = u.GoogleEmail ?? "";
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

    /// <summary>Bảng phân quyền: mỗi nhóm 1 khung (tên màn hình + ô chọn Không / Xem / Xem + xuất file / Sửa), các khung xếp nhiều cột theo bề rộng.</summary>
    private void BuildPermGrid()
    {
        foreach (var grp in Perm.Items.GroupBy(i => i.Group))
        {
            var g = new Avalonia.Controls.Grid { ColumnDefinitions = new ColumnDefinitions("*,150"), RowSpacing = 4 };
            var r = 0;
            foreach (var item in grp)
            {
                g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                var label = new TextBlock { Text = item.Label, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 0, 8, 0) };
                if (item.Hint.Length > 0) ToolTip.SetTip(label, item.Hint);
                var levels = new[] { Perm.None }.Concat(item.Levels).ToList();
                var box = new ComboBox
                {
                    ItemsSource = levels.Select(l => new ComboBoxItem { Content = Perm.LevelLabel(l), Tag = l }).ToList(),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                };
                Avalonia.Controls.Grid.SetRow(label, r); Avalonia.Controls.Grid.SetRow(box, r); Avalonia.Controls.Grid.SetColumn(box, 1);
                g.Children.Add(label); g.Children.Add(box);
                _permBoxes[item.Key] = box; r++;
            }
            var card = new StackPanel { Spacing = 6 };
            card.Children.Add(new TextBlock { Text = grp.Key.ToUpper(), Classes = { "label" }, FontWeight = Avalonia.Media.FontWeight.SemiBold });
            card.Children.Add(g);
            PermPanel.Children.Add(new Border
            {
                Width = 345, Padding = new Avalonia.Thickness(12, 10), CornerRadius = new Avalonia.CornerRadius(8),
                BorderBrush = AppTheme.Brush("#E3E8EF"), BorderThickness = new Avalonia.Thickness(1), Background = AppTheme.Brush("#F8FAFC"), Child = card,
            });
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
            : custom ? "Đang dùng quyền riêng — chọn mức từng màn hình rồi bấm Lưu."
            : $"Theo quyền mặc định của vai trò {Roles.Label(role)}.";
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

    private void OpenSelected() { if (List.SelectedItem is Row r) { Edit(r.U); Tabs.SelectedIndex = 1; } }
    private void OnEdit(object? sender, RoutedEventArgs e) => OpenSelected();
    private void OnListDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e) => OpenSelected();
    private void OnBack(object? sender, RoutedEventArgs e) => Tabs.SelectedIndex = 0;

    private void OnNew(object? sender, RoutedEventArgs e) { List.SelectedItem = null; Edit(new User()); Tabs.SelectedIndex = 1; UserBox.Focus(); }

    /// <summary>
    /// Dùng chung tài khoản mà máy chưa mở khóa ghi → hỏi mật khẩu Quản trị (hoặc mã cho máy quản trị) để mở ngay, thay vì chỉ báo lỗi.
    /// </summary>
    private async Task<bool> EnsureWrite()
    {
        var a = App.Services.Accounts;
        if (!a.Enabled || a.CanWrite) return true;
        while (true)
        {
            var input = await Ui.Prompt(this,
                "Máy này chưa mở khóa ghi tài khoản lên Google Sheet. Nhập lại MẬT KHẨU QUẢN TRỊ để mở khóa (hoặc dán \"mã cho máy quản trị\" KLKN1-… chép từ máy đã thiết lập Google Sheet).",
                "Mở khóa ghi tài khoản", password: true);
            if (string.IsNullOrWhiteSpace(input)) return false;
            if (await Ui.RunAsync(this, () => a.UnlockAsync(input))) return true;
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (!await EnsureWrite()) return;
        var role = SelectedRole;
        var perms = PermissionsToSave();
        var editing = _editing; var user = UserBox.Text ?? ""; var name = NameBox.Text ?? ""; var pass = PassBox.Text ?? ""; var active = ActiveBox.IsChecked == true; var gmail = GoogleBox.Text ?? "";
        // Chạy nền: khi dùng chung tài khoản, mỗi thay đổi được ghi lên Google Sheet (vài giây) — không làm đơ cửa sổ.
        var ok = await Ui.RunAsync(this, () => Task.Run(() =>
        {
            if (editing.Id == 0)
            {
                var u = App.Services.Auth.CreateUser(user, name, pass, role, perms);
                if (!active) App.Services.Auth.UpdateUser(u.Id, u.FullName, role, false);
                App.Services.Auth.SetGoogleEmail(u.Id, gmail);
            }
            else
            {
                App.Services.Auth.UpdateUser(editing.Id, name, role, active);
                App.Services.Auth.SetPermissions(editing.Id, perms);
                App.Services.Auth.SetGoogleEmail(editing.Id, gmail);
            }
        }));
        if (ok) { Load(); Tabs.SelectedIndex = 0; Edit(new User()); }
    }

    /// <summary>Mã OAuth cho "Đăng nhập bằng Google" trên máy này (Client ID cài sẵn = mã Desktop của dự án kholanhcloud; Client secret quản trị dán 1 lần).</summary>
    private async void OnGoogleConfig(object? sender, RoutedEventArgs e) => await GoogleConfigDialog(this);

    /// <summary>1.105: dùng chung với Hệ thống › Kết nối › Đăng nhập bằng Google.</summary>
    internal static async Task GoogleConfigDialog(Control owner)
    {
        var g = App.Services.Google;
        var id = new TextBox { Text = g.ClientId };
        var sec = new TextBox { PasswordChar = '•', Watermark = g.ClientSecret.Length > 0 ? "(đã lưu — để trống = giữ nguyên)" : "GOCSPX-…" };
        var msg = new TextBlock { FontSize = 12, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Tag = "keep" };
        var body = new StackPanel { Spacing = 6 };
        body.Children.Add(new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12.5, Tag = "keep",
            Text = "Mã OAuth loại Desktop trong Google Cloud (dự án kholanhcloud › APIs & Services › Credentials › kholanhanan). Client ID đã cài sẵn; chỉ cần dán Client secret một lần trên mỗi máy. Sau đó gán Email Google cho từng tài khoản." });
        body.Children.Add(new TextBlock { Text = "Client ID", Classes = { "label" } }); body.Children.Add(id);
        body.Children.Add(new TextBlock { Text = "Client secret", Classes = { "label" } }); body.Children.Add(sec);
        body.Children.Add(msg);
        var test = new Button { Content = "Thử đăng nhập Google" }; var ok = new Button { Content = "Lưu", Classes = { "primary" }, IsDefault = true }; var close = new Button { Content = "Đóng", IsCancel = true };
        body.Children.Add(Ui.Buttons(test, close, ok));
        var w = Ui.Dialog("Đăng nhập Google", body, 540);
        bool Apply() { try { g.SaveConfig(id.Text ?? "", sec.Text ?? ""); sec.Text = ""; return true; } catch (Exception ex) { msg.Foreground = AppTheme.Danger; msg.Text = ex.Message; return false; } }
        ok.Click += (_, _) => { if (Apply()) w.Close(); };
        close.Click += (_, _) => w.Close();
        test.Click += async (_, _) =>
        {
            if (!Apply()) return;
            msg.Foreground = AppTheme.Muted; msg.Text = "Đang mở trình duyệt — chọn tài khoản Google…";
            try { var r = await g.SignInAsync(Ui.OpenExternal); msg.Foreground = AppTheme.Brush("#1E7B4A"); msg.Text = $"✔ Đăng nhập được: {r.Email}" + (r.Name.Length > 0 ? $" ({r.Name})" : ""); }
            catch (Exception ex) { msg.Foreground = AppTheme.Danger; msg.Text = ex.Message; }
        };
        await Ui.Show(owner, w);
    }

    private async void OnReset(object? sender, RoutedEventArgs e)
    {
        if (!await EnsureWrite()) return;
        var pw = await Ui.Prompt(this, $"Mật khẩu mới cho {_editing.Username} (ít nhất 6 ký tự):", "Đặt lại mật khẩu", password: true);
        if (pw is null) return;
        var id = _editing.Id;
        if (await Ui.RunAsync(this, () => Task.Run(() => App.Services.Auth.ResetPassword(id, pw))))
            await Ui.Message(this, $"Đã đặt lại mật khẩu cho {_editing.Username}.");
    }
}
