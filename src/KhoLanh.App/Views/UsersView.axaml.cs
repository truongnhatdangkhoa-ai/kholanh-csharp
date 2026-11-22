using Avalonia.Controls;
using Avalonia.Interactivity;
using KhoLanh.Core.Models;

namespace KhoLanh.App.Views;

public partial class UsersView : UserControl, IPage
{
    public string Title => "Tài khoản";
    public string Subtitle => "Tạo tài khoản, phân quyền, khóa tài khoản và đặt lại mật khẩu.";
    private User _editing = new();

    public UsersView()
    {
        InitializeComponent();
        Load();
        Edit(new User());
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
    }

    private void OnSelect(object? sender, SelectionChangedEventArgs e)
    {
        if (Grid.SelectedItem is User u) Edit(u);
    }

    private void OnNew(object? sender, RoutedEventArgs e) { Grid.SelectedItem = null; Edit(new User()); UserBox.Focus(); }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var role = (RoleBox.SelectedItem as ComboBoxItem)?.Tag as string ?? Roles.Staff;
        var ok = await Ui.Run(this, () =>
        {
            if (_editing.Id == 0)
            {
                var u = App.Services.Auth.CreateUser(UserBox.Text ?? "", NameBox.Text ?? "", PassBox.Text ?? "", role);
                if (ActiveBox.IsChecked != true) App.Services.Auth.UpdateUser(u.Id, u.FullName, role, false);
            }
            else App.Services.Auth.UpdateUser(_editing.Id, NameBox.Text ?? "", role, ActiveBox.IsChecked == true);
        });
        if (ok) { Load(); Edit(new User()); }
    }

    private async void OnReset(object? sender, RoutedEventArgs e)
    {
        var pw = await Ui.Prompt(this, $"Mật khẩu mới cho {_editing.Username} (ít nhất 6 ký tự):", "Đặt lại mật khẩu", password: true);
        if (pw is null) return;
        if (await Ui.Run(this, () => App.Services.Auth.ResetPassword(_editing.Id, pw)))
            await Ui.Message(this, $"Đã đặt lại mật khẩu cho {_editing.Username}.");
    }
}
