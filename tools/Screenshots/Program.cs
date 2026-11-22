using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using KhoLanh.App;
using KhoLanh.App.Views;
using KhoLanh.Core.Models;
using Avalonia.VisualTree;

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

// Chế độ LAN: máy này làm máy chủ chính (cổng ngẫu nhiên để không đụng máy thật) + một mục trùng sửa mẫu.
s.Settings.Set("lan_addrs", "192.168.1.10:47820");
s.Settings.Set("lan_name", "MAY-CHU-KHO");
KhoLanh.Core.Lan.LanService.Configure(s.Settings, s.Session, KhoLanh.Core.Lan.LanService.RoleServer, null, 47000 + Random.Shared.Next(900));
App.InitServices(data);
s = App.Services;
s.Auth.Login("admin", "matkhau1");
s.Lan.Speed = 0.01; s.Lan.TickMs = 20;
s.Lan.Start();
for (var i = 0; i < 100 && !s.Lan.Serving; i++) Thread.Sleep(30);
using (var c = s.Db.Open())
{
    Dapper.SqlMapper.Execute(c, "INSERT INTO lan_conflicts(at, machine, username, db_key, sheet, status, base_data, client_data, server_data, result_data, note) VALUES('22/11/2026 15:42','MAY-KHO-2','nv1','ton-kho-gui','PhieuXuat','cho','[]','[{},{},{}]','[{},{}]',NULL,'hai bên cùng sửa hoặc xóa một dòng')");
    Dapper.SqlMapper.Execute(c, "INSERT INTO lan_conflicts(at, machine, username, db_key, sheet, status, base_data, client_data, server_data, result_data, note) VALUES('22/11/2026 15:42','MAY-KHO-2','nv1','ton-kho-an-an','DacTinh','gop','[]','[{},{}]','[{}]','[{},{},{}]','tự gộp: máy con bỏ 0 dòng, thêm 1 dòng')");
}

// Nhập liệu mẫu
{
    var tpl = KhoLanh.Core.Entry.EntryService.BuiltIn[1];
    var pg = s.Entry.Create("Nhập kho 22/11", tpl);
    var rnd = new Random(7);
    string[] hang = { "HLSO 16/20 BLOCK", "PD 31/40 IQF", "PTO 26/30", "HOSO 20/30 SEMI", "PDTO 41/50 IQF" };
    var rows = Enumerable.Range(0, 60).Select(i =>
    {
        var thung = rnd.Next(10, 400); var kg = new[] { 10, 12, 20 }[rnd.Next(3)];
        return new[] { $"{1 + i / 10:00}/11/2026", $"AN{rnd.Next(1000, 9999)}-{i % 7}", hang[i % 5], new[] { "16/20", "31/40", "26/30" }[i % 3],
            thung.ToString(), kg.ToString(), (thung * kg).ToString(), $"K{rnd.Next(1, 9)}-{rnd.Next(1, 30):00}", i % 3 == 0 ? "AN AN" : "MINH PHÚ", i % 9 == 0 ? "Hàng gửi" : "" };
    }).ToList();
    s.Entry.Save(pg, tpl.Columns, rows);
    s.Entry.Create("Kiểm kê kho 2", KhoLanh.Core.Entry.EntryService.BuiltIn[3]);
}

var main = new MainWindow { Width = 1440, Height = 880, WindowState = WindowState.Normal };
main.Show();
Snap(main, "03-man-hinh-chinh");
var small = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal };
small.Show();
Snap(small, "03c-man-hinh-1366");
small.Close();

foreach (var (key, name) in new[] { ("users", "12-tai-khoan"), ("backup", "13-sao-luu"), ("lan", "15-mang-lan"), ("audit", "14-nhat-ky"), ("m6", "20-nhap-lieu") })
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
    if (key == "m6")
    {
        var g = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First();
        g.Select(3, 4); g.Select(9, 6, extend: true);
        Snap(main, "20b-nhap-lieu-chon-vung");
    }
}
// Kiểm tra nhanh thao tác bàn phím trên lưới (gõ, Enter, Tab, F2, Delete, Ctrl+Z, Ctrl+D)
{
    var m = new KhoLanh.Core.Grid.GridModel();
    m.Load(new[] { new KhoLanh.Core.Grid.GridColumn("a", "A"), new KhoLanh.Core.Grid.GridColumn("b", "B"), new KhoLanh.Core.Grid.GridColumn("c", "C") }, new List<string[]>());
    var g = new KhoLanh.App.Controls.SheetGrid(m);
    var w = new Window { Width = 600, Height = 400, Content = g };
    w.Show(); Pump();
    g.Focus(); Pump();
    void Type(string t) { w.KeyTextInput(t); Pump(); }
    void Key(Avalonia.Input.Key k, Avalonia.Input.RawInputModifiers mods = Avalonia.Input.RawInputModifiers.None) { w.KeyPress(k, mods, Avalonia.Input.PhysicalKey.None, null); w.KeyRelease(k, mods, Avalonia.Input.PhysicalKey.None, null); Pump(); }
    Type("x"); Type("y"); Key(Avalonia.Input.Key.Tab); Type("1"); Type("2"); Key(Avalonia.Input.Key.Enter);
    Type("z"); Key(Avalonia.Input.Key.Enter);
    var ok1 = m.Get(0, 0) == "xy" && m.Get(0, 1) == "12" && m.Get(1, 1) == "z" && g.Row == 2 && g.Col == 1;
    g.Select(0, 0); Key(Avalonia.Input.Key.F2); Type("!"); Key(Avalonia.Input.Key.Enter);
    var ok2 = m.Get(0, 0) == "xy!";
    g.Select(0, 1); g.Select(2, 1, extend: true); Key(Avalonia.Input.Key.D, Avalonia.Input.RawInputModifiers.Control);
    var ok3 = m.Get(2, 1) == "12" && m.Get(1, 1) == "12";
    Key(Avalonia.Input.Key.Z, Avalonia.Input.RawInputModifiers.Control);
    var ok4 = m.Get(1, 1) == "z";
    g.Select(0, 0); Key(Avalonia.Input.Key.Delete);
    var ok5 = m.Get(0, 0) == "";
    g.Select(1, 1); Key(Avalonia.Input.Key.Down, Avalonia.Input.RawInputModifiers.Shift); Key(Avalonia.Input.Key.Right, Avalonia.Input.RawInputModifiers.Shift);
    var ok6 = g.Selection == new KhoLanh.Core.Grid.CellRange(1, 1, 2, 2);
    Console.WriteLine($"  Lưới: gõ/Enter/Tab={ok1} F2={ok2} Ctrl+D={ok3} Ctrl+Z={ok4} Delete={ok5} Shift+mũi tên={ok6}");
    if (!(ok1 && ok2 && ok3 && ok4 && ok5 && ok6)) { Console.WriteLine("  LỖI lưới: " + string.Join(" | ", Enumerable.Range(0, 3).Select(r => string.Join(",", m.Row(r))))); Environment.ExitCode = 1; }
    w.Close();
}
s.Lan.Dispose();
Console.WriteLine("Xong: " + outDir);
