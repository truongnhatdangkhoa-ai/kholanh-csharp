using Avalonia.Controls;

namespace KhoLanh.App.Views;

/// <summary>Khởi động cùng Windows + tự đăng nhập — dùng ở Sao lưu và cài đặt và ở menu tài khoản (mọi tài khoản, cài theo máy).</summary>
public static class BootSettings
{
    // ---------------- Khởi động cùng Windows + tự đăng nhập (mọi tài khoản, cài theo máy) ----------------

    public static StackPanel Build(Control owner, bool compact = false)
    {
        var BootPanel = new StackPanel { Spacing = 6 };
        var s = App.Services; var user = s.Session.Username;
        var boot = new CheckBox { Content = "Tự mở phần mềm khi bật máy (khi đăng nhập Windows)", IsChecked = WindowsStartup.IsOn(), IsEnabled = WindowsStartup.Supported };
        var auto = new CheckBox { Content = $"Tự đăng nhập tài khoản “{user}” khi mở phần mềm", IsChecked = s.Remember.Auto && string.Equals(s.Remember.Username, user, StringComparison.OrdinalIgnoreCase) };
        var msg = new TextBlock { Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 };
        var save = new Button { Content = "Lưu", Classes = { "primary" } };
        BootPanel.Children.Add(new TextBlock { Text = "Khởi động cùng Windows", Classes = { "section" } });
        ToolTip.SetTip(boot, "Cài riêng cho máy này và tài khoản Windows đang dùng — hợp cho máy chủ LAN.");
        ToolTip.SetTip(auto, "Mật khẩu được mã hóa theo tài khoản Windows của máy này. Máy chủ không cần gõ mật khẩu Windows: Win + R › netplwiz, tắt chế độ ngủ.");
        if (!compact) BootPanel.Children.Add(new TextBlock
        {
            Text = "Cài riêng cho máy này và tài khoản Windows đang dùng. Bật cả hai thì bật máy là phần mềm tự mở và vào thẳng — hợp cho máy chủ (máy chủ LAN tự phục vụ các máy con, tự đồng bộ Drive, tự sao lưu). Mật khẩu được mã hóa theo tài khoản Windows của máy này.",
            Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
        });
        BootPanel.Children.Add(boot); BootPanel.Children.Add(auto);
        if (!compact) BootPanel.Children.Add(new TextBlock
        {
            Text = "Máy chủ muốn bật máy là chạy mà không cần ai gõ mật khẩu Windows: bật tự đăng nhập Windows (Win + R › netplwiz) và tắt chế độ ngủ (Cài đặt › Nguồn › Không bao giờ).",
            Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12,
        });
        if (!WindowsStartup.Supported) msg.Text = "Chỉ dùng được với bản KhoLanh.exe trên Windows.";
        save.Click += async (_, _) =>
        {
            var wantAuto = auto.IsChecked == true; var wantBoot = boot.IsChecked == true;
            if (wantAuto && !(s.Remember.Auto && string.Equals(s.Remember.Username, user, StringComparison.OrdinalIgnoreCase)))
            {
                var pw = await Ui.Prompt(owner, $"Nhập mật khẩu của tài khoản “{user}” để lưu tự đăng nhập trên máy này:", "Tự đăng nhập", password: true);
                if (pw is null) return;
                if (!s.Auth.CheckPassword(user, pw)) { msg.Text = "Mật khẩu không đúng — chưa bật tự đăng nhập."; return; }
                s.Remember.Save(user, pw, remember: true, auto: true);
            }
            else if (!wantAuto && s.Remember.Auto) s.Remember.Save(s.Remember.Username, s.Remember.Password, remember: true, auto: false);
            if (WindowsStartup.Supported && !await Ui.Run(owner, () => WindowsStartup.Set(wantBoot))) return;
            msg.Text = (wantBoot ? "Đã bật tự mở phần mềm khi bật máy" : "Đã tắt tự mở khi bật máy") + " · " + (wantAuto ? $"tự đăng nhập “{user}”." : "không tự đăng nhập.")
                       + (wantBoot && !wantAuto ? " Phần mềm sẽ mở tới màn hình đăng nhập." : "");
        };
        BootPanel.Children.Add(save); BootPanel.Children.Add(msg);
        return BootPanel;
    }

    public static async Task ShowDialog(Control owner)
    {
        var close = new Button { Content = "Đóng", IsCancel = true };
        var body = Build(owner); body.Children.Add(Ui.Buttons(close));
        var w = Ui.Dialog("Khởi động cùng Windows", body, 560);
        close.Click += (_, _) => w.Close();
        await Ui.Show(owner, w);
    }
}
