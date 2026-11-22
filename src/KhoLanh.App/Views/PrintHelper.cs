using Avalonia;
using KhoLanh.Core.Services;

namespace KhoLanh.App.Views;

public static class PrintHelper
{
    /// <summary>Tạo trang in phiếu (HTML) và mở bằng trình duyệt để in hoặc lưu PDF.</summary>
    public static async Task Print(Visual v, long voucherId)
    {
        await Ui.Run(v, () =>
        {
            var s = App.Services;
            var (voucher, owner, lines) = s.Vouchers.Get(voucherId);
            var html = s.Export.BuildVoucherHtml(voucher, owner, lines,
                s.Settings.Get(SettingsService.CompanyName, "CÔNG TY"),
                s.Settings.Get(SettingsService.CompanyAddress),
                s.Settings.Get(SettingsService.WarehouseName));
            var dir = Path.Combine(App.DataFolder, "In");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, Ui.SafeFileName(voucher.No) + ".html");
            File.WriteAllText(path, html, System.Text.Encoding.UTF8);
            Ui.OpenExternal(path);
        });
    }
}
