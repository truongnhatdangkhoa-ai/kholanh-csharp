using Avalonia.Controls;
using Avalonia.Media;

namespace KhoLanh.App.Views;

/// <summary>Hộp thoại "Kết nối điện thoại" dùng chung (Vị Trí Bột, Đồng bộ điện thoại).</summary>
public static class PhoneConnect
{
    private static KhoLanh.Core.Services.PhoneWebApp Phone => App.Services.Phone;
    /// <summary>Đường dẫn Web App + mã PC_TOKEN (giống Cài Đặt › Điện Thoại của app cũ) — lưu trên máy này.</summary>
    public static async Task ShowAsync(Control owner)
    {
        var url = new TextBox { Text = Phone.Url, Watermark = "https://script.google.com/macros/s/…/exec" };
        var tok = new TextBox { Text = Phone.Token, PasswordChar = '•', Watermark = "PC_TOKEN" };
        var msg = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var test = new Button { Content = "Kiểm tra kết nối" }; var ok = new Button { Content = "Lưu", Classes = { "primary" }, IsDefault = true }; var off = new Button { Content = "Ngắt kết nối" }; var cancel = new Button { Content = "Đóng", IsCancel = true };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = "Dùng ĐÚNG Web App (Apps Script) mà app điện thoại đang dùng — đường dẫn và mã PC_TOKEN chép từ app cũ (Cài Đặt › Điện Thoại) hoặc từ người quản trị.", TextWrapping = TextWrapping.Wrap, FontSize = 12.5 });
        body.Children.Add(new TextBlock { Text = "Đường dẫn Web App", FontSize = 12 }); body.Children.Add(url);
        body.Children.Add(new TextBlock { Text = "Mã PC_TOKEN", FontSize = 12 }); body.Children.Add(tok);
        body.Children.Add(msg); body.Children.Add(Ui.Buttons(off, test, cancel, ok));
        var w = Ui.Dialog("Kết nối điện thoại", body, 520);
        async Task<bool> Apply() { try { Phone.Save(url.Text ?? "", tok.Text ?? ""); return true; } catch (Exception e) { msg.Text = e.Message; msg.Foreground = AppTheme.Danger; return false; } }
        test.Click += async (_, _) =>
        {
            if (!await Apply()) return; msg.Foreground = AppTheme.Muted; msg.Text = "Đang kiểm tra…";
            try { var (t, sh) = await Phone.PingAsync(); msg.Foreground = AppTheme.Brush("#1B5E20"); msg.Text = $"✔ Kết nối được Google Sheet \"{t}\" ({sh.Count} tab)."; }
            catch (Exception e) { msg.Foreground = AppTheme.Danger; msg.Text = e.Message; }
        };
        ok.Click += async (_, _) => { if (await Apply()) w.Close(); };
        off.Click += (_, _) => { Phone.Clear(); url.Text = ""; tok.Text = ""; msg.Foreground = AppTheme.Muted; msg.Text = "Đã ngắt kết nối điện thoại trên máy này."; };
        cancel.Click += (_, _) => w.Close();
        await Ui.Show(owner, w);
    }

}
