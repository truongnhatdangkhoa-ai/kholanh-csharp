using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using KhoLanh.App;
using KhoLanh.App.Views;
using KhoLanh.Core.Models;

// Công cụ nội bộ: tạo dữ liệu mẫu và chụp ảnh các màn hình chính (không cần màn hình thật).
// Chạy: dotnet run --project tools/Screenshots -- <thư mục ảnh>
var outDir = Path.GetFullPath(args.Length > 0 ? args[0] : "screenshots");
Directory.CreateDirectory(outDir);
var culture = System.Globalization.CultureInfo.GetCultureInfo("vi-VN");
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = culture;
System.Globalization.CultureInfo.CurrentCulture = culture;

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

var data = Path.Combine(Path.GetTempPath(), "kholanh-demo-" + Guid.NewGuid().ToString("N")[..6]);
App.InitServices(data);
var s = App.Services;

void Pump() { for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(30); } }
void Snap(Window w, string name)
{
    Pump();
    using var frame = w.CaptureRenderedFrame();
    frame!.Save(Path.Combine(outDir, name + ".png"));
    Console.WriteLine("  " + name);
}

// Màn đăng nhập lần đầu
var first = new LoginWindow();
first.Show();
Snap(first, "01-thiet-lap-lan-dau");
first.Close();

// Dữ liệu mẫu
s.Auth.CreateFirstAdmin("admin", "Nguyễn Văn An", "matkhau1");
s.Auth.Login("admin", "matkhau1");
s.Settings.Set("company_name", "CÔNG TY TNHH KHO LẠNH MẪU");
s.Settings.Set("warehouse_name", "Kho lạnh số 1");
s.Auth.CreateUser("nv1", "Trần Thị Bình", "matkhau2", Roles.Staff);

var login = new LoginWindow();
login.Show();
login.UserBox.Text = "admin";
Snap(login, "02-dang-nhap");
login.Close();

var main = new MainWindow { Width = 1440, Height = 880, WindowState = WindowState.Normal };
main.Show();
Snap(main, "03-man-hinh-chinh");
var small = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal };
small.Show();
Snap(small, "03c-man-hinh-1366");
small.Close();

foreach (var (key, name) in new[] { ("users", "12-tai-khoan"), ("backup", "13-sao-luu"), ("audit", "14-nhat-ky") })
{
    main.Navigate(key);
    Snap(main, name);
    if (key == "backup" && main.PageHost.Content is BackupView bv)
    {
        bv.AcctStatus.BringIntoView();
        Pump();
        bv.AcctInput.BringIntoView();
        Snap(main, "13b-tai-khoan-dung-chung");
    }
}
Console.WriteLine("Xong: " + outDir);
