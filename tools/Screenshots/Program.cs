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

// Máy mới cài lần đầu: chọn nơi lưu dữ liệu
var pickWin = new DataFolderWindow(new KhoLanh.Core.Data.DataLocation(Path.Combine(Path.GetTempPath(), "kholanh-demo-macdinh")));
pickWin.Show();
Snap(pickWin, "00-chon-noi-luu-du-lieu");
pickWin.Close();

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

// Tổng hợp mẫu: Bảng 1 lấy từ danh sách mã hàng mặc định
{
    var th = s.TongHop; th.Load();
    var rnd = new Random(3);
    var codes = th.Results.Where(r => r.Code.Length == 21).Take(400).Select(r => r.Code).ToList();
    th.Tab2 = new KhoLanh.Core.TongHop.TongHopService.Table
    {
        Headers = new() { "Ngày", "Mã hàng", "Số thùng", "Số kg" }, CodeCol = "Mã hàng", DateCol = "Ngày",
        Rows = Enumerable.Range(0, 80).Select(i => new Dictionary<string, System.Text.Json.Nodes.JsonNode?>
        {
            ["Ngày"] = $"{20 + i / 30}/11/2026", ["Mã hàng"] = codes[rnd.Next(12)], ["Số thùng"] = rnd.Next(5, 200), ["Số kg"] = rnd.Next(50, 4000),
        }).ToList(),
    };
    th.Save();
}

// Báo cáo rã đông mẫu: file báo cáo kho giả lập → Bảng 2 → lưu Bảng 3
{
    var rnd = new Random(5);
    string[] sp = { "VRHL", "VRHO", "VRPD" }, tt = { "XXX", "XEC", "XTC", "XAI" }, sz = { "3140", "4150", "5160", "0914", "0030", "0045", "4LXX" };
    var xlsx = Path.Combine(data, "rd-mau.xlsx");
    using (var wb = new ClosedXML.Excel.XLWorkbook())
    {
        var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = "BÁO CÁO NHẬP XUẤT TRONG NGÀY";
        var heads = new[] { "Loại chứng từ", "Mã hàng", "Tên hàng", "Số lượng", "Ngày chứng từ", "Trọng lượng", "Net" };
        for (var i = 0; i < heads.Length; i++) ws.Cell(3, i + 1).Value = heads[i];
        var r = 4;
        for (var d = 14; d <= 21; d++)
            for (var k = 0; k < 14; k++)
            {
                var size = sz[rnd.Next(sz.Length)];
                var code = sp[rnd.Next(sp.Length)] + "BTN" + tt[rnd.Next(tt.Length)] + (rnd.Next(6) == 0 ? "TGS" : "R00") + size + "IME1";
                var sl = rnd.Next(20, 500); var tl = new[] { 9, 10.8, 12.6 }[rnd.Next(3)];
                ws.Cell(r, 1).Value = "Xuất xả"; ws.Cell(r, 2).Value = "TPC-" + code;
                ws.Cell(r, 3).Value = size == "0914" ? "Tôm HLSO, 10%, 91/120 (9 kg x 1 gói)" : "Tôm";
                ws.Cell(r, 4).Value = sl; ws.Cell(r, 5).Value = new DateTime(2026, 11, d); ws.Cell(r, 6).Value = tl;
                if (k % 5 != 0) ws.Cell(r, 7).Value = Math.Round(sl * tl, 2);
                r++;
            }
        wb.SaveAs(xlsx);
    }
    var rd = s.RaDong; rd.Load();
    var dt = KhoLanh.Core.RaDong.RaDongService.ReadExcel(xlsx);
    rd.SetBang1(dt);
    var plan = rd.DaysFrom().Where(p => p.Key != "2026-11-21").Select(p => new KhoLanh.Core.RaDong.RaDongService.DayPlan { Date = p.Key, Rows = p.Value }).ToList();
    rd.Apply(plan, dt.FileName, "", 0, "replace");
    var k0 = rd.Compute().Order.Last();
    rd.EditGroup(k0, rd.Compute().NetCol, "1234.5");
    rd.AddRow("2026-11-20", "BTP", "31/40", "12", "");
}

// Đối chiếu hàng ngày mẫu: 2 file Kho (thay thùng / rã đông) + 1 file Xưởng 2 sheet, có lệch / chỉ 1 bên / nằm nhầm file
{
    var rnd = new Random(11);
    string Code(int i) => "VRPDBTN" + new[] { "XXX", "XEC", "XTC", "XAI" }[i % 4] + "R00" + new[] { "3140", "4150", "5160", "2630", "1620", "6170" }[i % 6] + "IM" + (char)('A' + i % 7) + "1";
    var days = Enumerable.Range(18, 4).Select(d => new DateTime(2026, 11, d)).ToList();
    var thay = new List<(DateTime d, string ma, double sl, double tl)>(); var rad = new List<(DateTime d, string ma, double sl, double tl)>();
    for (var di = 0; di < days.Count; di++)
        for (var k = 0; k < 7; k++)
        {
            var row = (days[di], Code(di * 7 + k), (double)rnd.Next(10, 300), new[] { 9.0, 10.8, 12.6 }[rnd.Next(3)]);
            if (k < 4) thay.Add(row); else rad.Add(row);
        }
    string KhoFile(string name, List<(DateTime d, string ma, double sl, double tl)> rows)
    {
        var path = Path.Combine(data, name);
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.AddWorksheet("Xuat");
        ws.Cell(1, 1).Value = "BẢNG KÊ " + name.ToUpperInvariant();
        var heads = new[] { "Ngày", "Mã hàng", "Tên hàng", "Số lượng", "Trọng lượng", "Net" };
        for (var i = 0; i < heads.Length; i++) ws.Cell(3, i + 1).Value = heads[i];
        var r = 4;
        foreach (var g in rows.GroupBy(x => x.d))
        {
            var first = r;
            foreach (var x in g) { ws.Cell(r, 1).Value = x.d; ws.Cell(r, 2).Value = "TPC-" + x.ma; ws.Cell(r, 3).Value = "Tôm PD size " + x.ma[13..17]; ws.Cell(r, 4).Value = x.sl; ws.Cell(r, 5).Value = x.tl; ws.Cell(r, 6).Value = x.sl * x.tl; r++; }
            ws.Cell(r, 1).Value = g.Key; ws.Cell(r, 4).FormulaA1 = $"SUM(D{first}:D{r - 1})"; ws.Cell(r, 6).FormulaA1 = $"SUM(F{first}:F{r - 1})"; r++;
        }
        wb.RecalculateAllFormulas();   // ghi sẵn kết quả công thức như Excel lưu
        wb.SaveAs(path);
        return path;
    }
    // Xưởng: lệch 1 mã ngày 19, mã cuối ngày 20 của Thay thùng Xưởng ghi ở sheet Rã đông (nằm nhầm file), thêm 1 mã chỉ Xưởng có
    var xThay = thay.Select(x => (x.d, x.ma, x.sl, net: x.sl * x.tl)).ToList(); var xRad = rad.Select(x => (x.d, x.ma, x.sl, net: x.sl * x.tl)).ToList();
    var li = xThay.FindIndex(x => x.d.Day == 19); xThay[li] = (xThay[li].d, xThay[li].ma, xThay[li].sl - 5, xThay[li].net - 5 * 10.8);
    var mv = xThay.FindLastIndex(x => x.d.Day == 20); xRad.Add(xThay[mv]); xThay.RemoveAt(mv);
    xRad.Add((days[3], Code(40), 24, 24 * 12.6));
    var xu = Path.Combine(data, "xuong.xlsx");
    using (var wb = new ClosedXML.Excel.XLWorkbook())
    {
        foreach (var (name, rows) in new[] { ("Thay thùng", xThay), ("Rã đông", xRad) })
        {
            var ws = wb.AddWorksheet(name);
            var heads = new[] { "Ngày", "Mã hàng", "Số lượng", "Net" };
            for (var i = 0; i < heads.Length; i++) ws.Cell(1, i + 1).Value = heads[i];
            var r = 2;
            foreach (var x in rows.OrderBy(x => x.d)) { ws.Cell(r, 1).Value = x.d; ws.Cell(r, 2).Value = x.ma; ws.Cell(r, 3).Value = x.sl; ws.Cell(r, 4).Value = x.net; r++; }
        }
        wb.SaveAs(xu);
    }
    var dc = s.DoiChieu; dc.Load();
    dc.LinkFile("kho1", KhoFile("xuat-thay-thung.xlsx", thay)); dc.LinkFile("kho2", KhoFile("xuat-ra-dong.xlsx", rad)); dc.LinkFile("xuong", xu);
    var fix = rad.First(x => x.d.Day == 18);
    dc.ApplyOps(new() { new KhoLanh.Core.DoiChieu.DcxEngine.Op { Type = "setsl", Slot = 2, Ngay = "2026-11-18", Ma = fix.ma, Target = fix.sl + 2 } }, $"Đổi SL {fix.ma} ngày 18/11/2026 ở Xuất rã đông thành {fix.sl + 2}");
}

var main = new MainWindow { Width = 1440, Height = 880, WindowState = WindowState.Normal };
main.Show();
Snap(main, "03-man-hinh-chinh");
var small = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal };
small.Show();
Snap(small, "03c-man-hinh-1366");
small.Close();

foreach (var (key, name) in new[] { ("users", "12-tai-khoan"), ("backup", "13-sao-luu"), ("lan", "15-mang-lan"), ("audit", "14-nhat-ky"), ("m3", "21-tong-hop"), ("m4", "22-ra-dong"), ("m5", "23-doi-chieu"), ("m6", "20-nhap-lieu") })
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
    if (key == "m3")
    {
        var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
        tc.SelectedIndex = 1; Snap(main, "21b-tong-hop-bang");
        tc.SelectedIndex = 2; Snap(main, "21c-anh-xa-ten");
    }
    if (key == "m4")
    {
        var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
        tc.SelectedIndex = 1; Snap(main, "22b-ra-dong-bang-3");
        var days = s.RaDong.SavedRows.Select(x => x.Date).Distinct().OrderBy(x => x).TakeLast(2).Select(d => (d, s.RaDong.SavedRows.Where(x => x.Date == d).ToList())).ToList();
        KhoLanh.App.Views.DayImageRenderer.Render(days).Save(Path.Combine(outDir, "22c-ra-dong-anh-ngay.png"));
        tc.SelectedIndex = 0;
        var tg = main.PageHost.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(t => (t.Content as string)?.StartsWith("Bảng chưa gộp") == true);
        tg.IsChecked = true; Snap(main, "22d-ra-dong-chua-gop"); tg.IsChecked = false;
    }
    if (key == "m5")
    {
        var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
        var g = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First();
        g.Select(3, 1); Snap(main, "23-doi-chieu");
        tc.SelectedIndex = 1; Snap(main, "23b-doi-chieu-lien-ket");
        tc.SelectedIndex = 2; Snap(main, "23c-doi-chieu-lich-su");
        tc.SelectedIndex = 0;
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
