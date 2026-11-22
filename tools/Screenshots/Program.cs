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
var kho = s.Catalog.ListOwners().Single();
var kh = s.Catalog.SaveOwner(new Owner { Code = "MINHPHAT", Name = "Cty Thủy sản Minh Phát", Phone = "0909 123 456" });
var p1 = s.Catalog.SaveProduct(new Product { Code = "CB-PL", Name = "Cá basa phi lê", Category = "Cá", KgPerUnit = 10, Sizes = "120-170, 170-220, 220 UP", MinStock = 50 });
var p2 = s.Catalog.SaveProduct(new Product { Code = "TS-NB", Name = "Tôm sú nguyên con", Category = "Tôm", KgPerUnit = 12, Sizes = "16-20, 21-25, 26-30", MinStock = 40 });
var p3 = s.Catalog.SaveProduct(new Product { Code = "MO-LS", Name = "Mực ống làm sạch", Category = "Mực", KgPerUnit = 10, Sizes = "U5, U10", MinStock = 30 });
var p4 = s.Catalog.SaveProduct(new Product { Code = "BT-C", Name = "Bạch tuộc cắt khúc", Category = "Mực", KgPerUnit = 8, Sizes = "S, M, L" });
var p5 = s.Catalog.SaveProduct(new Product { Code = "CN-LOIN", Name = "Cá ngừ loin", Category = "Cá", KgPerUnit = 15, Sizes = "3-5, 5 UP", MinStock = 60 });
Voucher V(string t, int day, long owner, string partner, params (Product p, string size, string lot, string loc, double q)[] lines) =>
    s.Vouchers.Create(new Voucher
    {
        Type = t, Date = DateTime.Today.AddDays(day), OwnerId = owner, Partner = partner, Vehicle = t == "N" ? "51C-123.45" : "",
        Lines = lines.Select(l => new VoucherLine { ProductId = l.p.Id, Size = l.size, Lot = l.lot, Location = l.loc, Quantity = l.q, Weight = l.q * l.p.KgPerUnit }).ToList(),
    });
V("N", -12, kho.Id, "Xưởng chế biến", (p1, "120-170", "L2611-01", "A1-01", 120), (p1, "170-220", "L2611-01", "A1-02", 80), (p2, "21-25", "T2611-03", "B2-01", 60));
V("N", -9, kh.Id, "Minh Phát", (p5, "3-5", "MP-0911", "C1-04", 45), (p3, "U10", "MP-0911", "C1-05", 70));
V("X", -6, kho.Id, "Đại lý Hồng Phúc", (p1, "120-170", "L2611-01", "A1-01", 40));
V("N", -3, kho.Id, "Xưởng chế biến", (p2, "16-20", "T2611-07", "B2-02", 50), (p4, "M", "BT-1907", "B3-01", 35));
var cancel = V("X", -2, kh.Id, "Minh Phát", (p3, "U10", "MP-0911", "C1-05", 20));
s.Vouchers.Cancel(cancel.Id, "Nhập sai số lượng");
V("X", -2, kh.Id, "Minh Phát", (p3, "U10", "MP-0911", "C1-05", 25));
V("N", 0, kho.Id, "Xưởng chế biến", (p1, "220 UP", "L2611-22", "A2-01", 55), (p2, "21-25", "T2611-22", "B2-03", 30));
V("X", 0, kho.Id, "Siêu thị Bình An", (p2, "21-25", "T2611-03", "B2-01", 35), (p1, "170-220", "L2611-01", "A1-02", 20));
s.Catalog.SaveProduct(new Product { Code = "GH-XL", Name = "Ghẹ xanh nguyên con", Category = "Ghẹ", KgPerUnit = 5 });

var login = new LoginWindow();
login.Show();
login.UserBox.Text = "admin";
Snap(login, "02-dang-nhap");
login.Close();

var main = new MainWindow { Width = 1440, Height = 880, WindowState = WindowState.Normal };
main.Show();
Snap(main, "03-tong-quan");
var catButton = main.NavPanel.Children.OfType<Button>().First(b => (b.Content as string ?? "").StartsWith("Danh mục"));
catButton.Flyout!.ShowAt(catButton);
Snap(main, "03b-menu-danh-muc");
catButton.Flyout.Hide();
var small = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal };
small.Show();
Snap(small, "03c-man-hinh-1366");
small.Close();

var vin = new VoucherEditView(VoucherTypes.In);
main.Show("in", vin);
vin.PartnerBox.Text = "Xưởng chế biến"; vin.VehicleBox.Text = "51C-678.90";
vin.Prefill(new Voucher { OwnerId = kho.Id, Partner = "Xưởng chế biến", Vehicle = "51C-678.90" }, new[]
{
    new VoucherLineView { ProductId = p1.Id, ProductCode = p1.Code, ProductName = p1.Name, Size = "120-170", Lot = "L2611-23", Location = "A1-03", Quantity = 100, Weight = 1000 },
    new VoucherLineView { ProductId = p5.Id, ProductCode = p5.Code, ProductName = p5.Name, Size = "5 UP", Lot = "L2611-23", Location = "C1-06", Quantity = 24, Weight = 360 },
});
vin.ProductBox.SelectedItem = p2;
vin.SizeBox.Text = "26-30"; vin.LotBox.Text = "T2611-23"; vin.LocationBox.Text = "B2-04"; vin.QtyBox.Text = "40";
Snap(main, "04-lap-phieu-nhap");

var vout = new VoucherEditView(VoucherTypes.Out);
main.Show("out", vout);
vout.PartnerBox.Text = "Đại lý Hồng Phúc";
vout.ProductBox.SelectedItem = p1;
Pump();
vout.StockGrid.SelectedIndex = 0;
vout.SizeBox.Text = "120-170"; vout.LotBox.Text = "L2611-01"; vout.LocationBox.Text = "A1-01"; vout.QtyBox.Text = "30";
vout.Prefill(new Voucher { OwnerId = kho.Id, Partner = "Đại lý Hồng Phúc" }, new[]
{
    new VoucherLineView { ProductId = p2.Id, ProductCode = p2.Code, ProductName = p2.Name, Size = "16-20", Lot = "T2611-07", Location = "B2-02", Quantity = 20, Weight = 240 },
});
Snap(main, "05-lap-phieu-xuat");

foreach (var (key, name) in new[] { ("list", "06-danh-sach-phieu"), ("stock", "07-ton-kho"), ("ledger", "08-so-kho"), ("nxt", "09-bao-cao-nxt"),
                                     ("products", "10-ma-hang"), ("owners", "11-chu-hang"), ("users", "12-tai-khoan"), ("backup", "13-sao-luu"), ("audit", "14-nhat-ky") })
{
    main.Navigate(key);
    if (key == "nxt" && main.PageHost.Content is ReportView rv) rv.OnMonth(null, new Avalonia.Interactivity.RoutedEventArgs());
    if (key == "products" && main.PageHost.Content is ProductsView pv) pv.Grid.SelectedIndex = 0;
    Snap(main, name);
}

var detail = new VoucherDetailWindow(cancel.Id) { Width = 1000, Height = 560 };
detail.Show();
Snap(detail, "15-chi-tiet-phieu-da-huy");

// Trang in phiếu
var (v, o, ls) = s.Vouchers.Get(s.Vouchers.List(DateTime.Today, DateTime.Today, "N").First().Id);
File.WriteAllText(Path.Combine(outDir, "16-mau-in-phieu.html"),
    s.Export.BuildVoucherHtml(v, o, ls, s.Settings.Get("company_name"), "123 Đường Số 1, KCN Tân Tạo, TP.HCM", s.Settings.Get("warehouse_name")));
Console.WriteLine("Xong: " + outDir);
