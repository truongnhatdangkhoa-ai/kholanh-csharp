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
App.InitServices(data); KhoLanh.App.MainWindow.CacheEnabled = false;   // ảnh chụp cần màn hình mới mỗi lần mở
var s = App.Services;

// 1.85.1: không phần nào của trang (thanh công cụ, chữ…) được nằm trên mép dưới thanh menu 44px (lỗi Container 3D: trang bị dời lên −38px)
var navBad = new List<string>();
void CheckUnderNav(MainWindow w, string name)
{
    if (w.PageHost.Content is not Control root) return;
    var top = root.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.Bounds.Height > 0 && c is TextBlock or Button or TextBox or ComboBox)
        .Select(c => c.TranslatePoint(new Point(0, 0), w)?.Y ?? 999).DefaultIfEmpty(999).Min();
    if (top < 43.5) { navBad.Add($"{name} (y={top:0})"); Console.WriteLine($"  [HỎNG] {name}: nội dung ở y={top:0} < 44 (thanh menu)"); }
}
void Pump() { for (var i = 0; i < 5; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(30); } }
void SnapFlyout(Window w, Button anchor, string name)
{
    Pump();
    using var frame = w.CaptureRenderedFrame()!;
    var pres = ((MenuFlyout)anchor.Flyout!).Items.OfType<MenuItem>().First().GetVisualAncestors().OfType<MenuFlyoutPresenter>().First();
    var pt = anchor.TranslatePoint(new Point(0, anchor.Bounds.Height), w) ?? new Point(0, 0);
    var ps = new PixelSize((int)Math.Ceiling(pres.Bounds.Width), (int)Math.Ceiling(pres.Bounds.Height));
    using var pop = new Avalonia.Media.Imaging.RenderTargetBitmap(ps); pop.Render(pres);
    using var all = new Avalonia.Media.Imaging.RenderTargetBitmap(frame.PixelSize);
    using (var ctx = all.CreateDrawingContext())
    {
        ctx.DrawImage(frame, new Rect(0, 0, frame.Size.Width, frame.Size.Height));
        ctx.DrawImage(pop, new Rect(pt.X, pt.Y + 2, ps.Width, ps.Height));
    }
    all.Save(Path.Combine(outDir, name + ".png"));
    Console.WriteLine("  " + name);
}

void SnapPopup(Window w, Control child, Control anchor, string name)
{
    Pump();
    using var frame = w.CaptureRenderedFrame()!;
    var pt = anchor.TranslatePoint(new Point(0, anchor.Bounds.Height), w) ?? new Point(0, 0);
    var ps = new PixelSize((int)Math.Ceiling(child.Bounds.Right), (int)Math.Ceiling(child.Bounds.Bottom));
    using var pop = new Avalonia.Media.Imaging.RenderTargetBitmap(ps); pop.Render(child);
    using var all = new Avalonia.Media.Imaging.RenderTargetBitmap(frame.PixelSize);
    using (var ctx = all.CreateDrawingContext())
    {
        ctx.DrawImage(frame, new Rect(0, 0, frame.Size.Width, frame.Size.Height));
        ctx.DrawImage(pop, new Rect(child.Bounds.X, child.Bounds.Y, child.Bounds.Width, child.Bounds.Height), new Rect(pt.X, pt.Y, child.Bounds.Width, child.Bounds.Height));
    }
    all.Save(Path.Combine(outDir, name + ".png"));
    Console.WriteLine("  " + name);
}

/// <summary>Nhập liệu: gõ vào cột Mã hàng → ô gợi ý (popup ghép vào ảnh).</summary>
void SnapSuggest(Window w, string name)
{
    var g = w.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
    for (var i = 0; i < 60 && typeof(EntryView).GetField("_sugs", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null) is null; i++) { Thread.Sleep(50); Pump(); }
    var mc = Enumerable.Range(0, g.Model.ColCount).First(c => g.Model.Columns[c].Header == "Mã hàng");
    g.Select(3, mc); g.BeginEdit("1620");   // 1.92: gợi ý chỉ từ Tồn kho An An, 4 số = đoạn Size
    for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
    var pop = (Avalonia.Controls.Primitives.Popup)typeof(KhoLanh.App.Controls.SheetGrid).GetField("_sug", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(g)!;
    var ed = (TextBox)typeof(KhoLanh.App.Controls.SheetGrid).GetField("_editor", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(g)!;
    if (pop.IsOpen && pop.Child is Control ch) SnapPopup(w, ch, ed, name); else { Console.WriteLine("  (không mở được gợi ý)"); Snap(w, name); }
    var esc = new Avalonia.Input.KeyEventArgs { Key = Avalonia.Input.Key.Escape, RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent };
    ed.RaiseEvent(esc); ed.RaiseEvent(new Avalonia.Input.KeyEventArgs { Key = Avalonia.Input.Key.Escape, RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent }); Pump();
}

void Snap(Window w, string name)
{
    Pump();
    Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(2); Pump();   // headless: vẽ lại theo bố cục mới nhất (không thì ảnh giữ khung cũ)
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
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v206")
{
    // 1.106: máy mới = màn Đăng nhập thường (không mã); nút "Máy đầu tiên" mở form tạo Quản trị.
    var okL = first.Heading.Text == "Đăng nhập" && !first.FullNamePanel.IsVisible && first.SetupButton.IsVisible;
    Console.WriteLine($"[v206] máy mới: tiêu đề \"{first.Heading.Text}\", ô họ tên {(first.FullNamePanel.IsVisible ? "hiện" : "ẩn")}, nút máy đầu tiên {(first.SetupButton.IsVisible ? "có" : "không")}");
    Snap(first, "v206-may-moi-dang-nhap");
    first.SetupButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); Pump();
    okL &= first.FullNamePanel.IsVisible && first.Heading.Text == "Thiết lập lần đầu";
    Snap(first, "v206-may-dau-tien");
    if (!okL) { Console.WriteLine("        HỎNG: màn đăng nhập máy mới"); Environment.ExitCode = 1; }
}
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
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v222")
{
    // 1.122: bỏ "Đăng nhập bằng Google" — màn đăng nhập không còn nút / chữ Google nào (kể cả khi máy có mã OAuth).
    var hasG = login.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Any(b => b.IsVisible && (b.Content?.ToString() ?? "").Contains("Google"));
    Console.WriteLine($"[v222] nút Đăng nhập bằng Google: {(hasG ? "CÒN" : "không có")}");
    if (hasG) { Console.WriteLine("        HỎNG: còn nút đăng nhập Google"); Environment.ExitCode = 1; }
    login.Close(); return;
}
login.Close();

// Chế độ LAN: máy này làm máy chủ chính (cổng ngẫu nhiên để không đụng máy thật) + một mục trùng sửa mẫu.
s.Settings.Set("lan_addrs", "192.168.1.10:47820");
s.Settings.Set("lan_name", "MAY-CHU-KHO");
KhoLanh.Core.Lan.LanService.Configure(s.Settings, s.Session, KhoLanh.Core.Lan.LanService.RoleServer, null, 47000 + Random.Shared.Next(900));
App.InitServices(data); KhoLanh.App.MainWindow.CacheEnabled = false;
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

var vtDemo = new List<(string Vt, string Ma, string Ten, string Lo, int Sl, double Tl)>();
// Tồn kho theo vị trí mẫu: file vị trí 3 khu, phiếu hôm qua (đã vào lưu trữ), phiếu hôm nay, 1 phiếu chưa chọn loại
{
    var rnd = new Random(11);
    string Ma(int n) => "TPC-VRHLBTN" + new[] { "XXX", "XEC", "XTC" }[n % 3] + "R00" + new[] { "3140", "4150", "5160", "2630", "1620" }[n % 5] + "IM" + (char)('A' + n % 4) + "1";
    string[] dts = { "IQF", "Semi", "Block" }; string[] hds = { "HD-2611", "HD-2614", "HD-2620" };
    var path = Path.Combine(data, "TON VI TRI 23.11.xlsx");
    using (var wb = new ClosedXML.Excel.XLWorkbook())
    {
        var ws = wb.AddWorksheet("TonViTri");
        ws.Cell(1, 1).Value = "BÁO CÁO TỒN KHO THEO VỊ TRÍ";
        var heads = KhoLanh.Core.ViTri.ViTriService.TemplateColumns;
        for (var i = 0; i < heads.Length; i++) ws.Cell(3, i + 1).Value = heads[i];
        var r = 4; var no = 0;
        void Put(string vt, int m)
        {
            ws.Cell(r, 1).Value = 1000 + no; ws.Cell(r, 2).Value = Ma(m);
            ws.Cell(r, 3).Value = $"Tôm PD BTP ({new[] { "1.8", "2", "0.9" }[m % 3]} kg x {new[] { 6, 5, 10 }[m % 3]} gói) - {dts[m % 3]} - {hds[m % 3]}";
            ws.Cell(r, 4).Value = "Thùng"; ws.Cell(r, 5).Value = "L" + (2600 + m % 7);
            ws.Cell(r, 6).Value = vt;
            var sl = rnd.Next(40, 160); var tl = new[] { 10.8, 10.0, 9.0 }[m % 3];
            if (Ma(m).EndsWith("A1")) sl = Math.Max(1, sl / 10) * new[] { 6, 5, 10 }[m % 3];   // mã tính theo gói: số gói chẵn kiện
            ws.Cell(r, 7).Value = sl; ws.Cell(r, 8).Value = tl; ws.Cell(r, 9).Value = sl * tl; r++; no++;
            vtDemo.Add((vt, Ma(m), ws.Cell(r - 1, 3).GetString(), "L" + (2600 + m % 7), sl, tl));
        }
        // Kho 1 (sơ đồ mặc định A9×4, B12×4, C12×4, D14×4, 7 tầng): ~45% ô có hàng, vài ô lẫn 2 size, 1 tầng vượt mức
        foreach (var (khu, nd) in new[] { ("A", 9), ("B", 12), ("C", 12), ("D", 14) })
            for (var d = 1; d <= nd; d++)
                for (var t = 1; t <= 7; t++)
                    for (var k = 1; k <= 4; k++)
                    {
                        var fill = khu == "A" ? 0.6 : khu == "D" ? 0.25 : 0.45;
                        if (rnd.NextDouble() > fill * (t <= 2 ? 1.3 : 1)) continue;
                        var m = (d * 3 + t + "ABCD".IndexOf(khu) * 5) % 14;
                        Put($"TG1.{khu}.{d:00}.{t}.{k}", m);
                        if (rnd.NextDouble() < 0.05) Put($"TG1.{khu}.{d:00}.{t}.{k}", (m + 1) % 14);
                    }
        Put("TG1.A.01.1.1", 9); Put("TG1.A.01.1.2", 11); Put("TG1.A.01.1.3", 12);   // tầng 1 dãy 1 khu A: 4 khoang nhưng > 4 pallet
        for (var j = 0; j < 40; j++) Put($"TG2.{"EFGH"[j % 4]}.{1 + j % 12:00}.{1 + j % 5}.{1 + j % 4}", j % 14);
        for (var j = 0; j < 5; j++) Put($"TG1.Z.01.1.{j + 1}", j + 2);
        wb.SaveAs(path);
    }
    var vt = s.ViTri; vt.Load(); vt.Import(vt.ReadFile(path));
    List<KhoLanh.Core.ViTri.ViTriService.Pick> P(params (int i, double q)[] x) => x.Select(t => new KhoLanh.Core.ViTri.ViTriService.Pick(t.i, t.q)).ToList();
    var y = DateTime.Today.AddDays(-1);
    vt.CreateSlip(P((0, 12), (3, 8), (9, 20)), true, "xuatxa", y, "");
    vt.CreateSlip(P((1, 15), (4, 6)), false, "thaythung", y, "");
    vt.CreateSlip(P((12, 10), (15, 5)), true, "xuatxa", y, "");
    vt.CreateSlip(P((20, 4)), false, "", y, "");
    vt.CreateSlip(P((2, 30), (5, 12), (8, 9), (11, 16)), true, "xuatxa", DateTime.Today, "Phiếu xả khu C");
    vt.CreateSlip(P((7, 10), (10, 6)), false, "thaythung", DateTime.Today, "");

    // Sơ đồ kho: quy cách cho phần lớn mã, khóa kiểm kê khu D dãy 3
    var sd = s.SoDo; sd.Load();
    var mas = sd.B.Pos.SelectMany(p => p.It).Select(x => (x.Ma, x.Ten)).Distinct().ToList();
    sd.SaveQc(mas.Take(mas.Count - 3).Select(x => (x.Ma, KhoLanh.Core.SoDo.SoDoCore.GoiY(x.Ten) is { } g && x.Ma.EndsWith("A1") ? "gói" : "kiện", KhoLanh.Core.SoDo.SoDoCore.GoiY(x.Ten)?.Gk ?? 0)));
    sd.AddLocks(new[] { ("TG1", "D", 3) }, "kiểm kê tháng 11");

    // Đối chiếu kiểm kê: tồn An An = tổng theo vị trí (+ vài chỗ lệch), 2 trang kiểm kê (TG1 A–B hôm qua, phần còn lại hôm nay),
    // vài ô đếm lệch, 1 mã gõ sai, 1 mã lạ, khu D dãy 13–14 chưa đếm; số kế toán dán tay.
    var ic = System.Globalization.CultureInfo.InvariantCulture;
    // tồn hiện tại theo vị trí (sau các phiếu xả), tính theo kiện
    vtDemo = sd.B.Pos.Where(p => !p.Z).SelectMany(p => p.It.Select(x => (p.Code, x.Ma.StartsWith("TPC-") ? x.Ma : "TPC-" + x.Ma, x.Ten, x.Lo, (int)Math.Round(KhoLanh.Core.SoDo.SoDoCore.KienOf(x.Sl, sd.QcOf(x.Ma))), x.Tl))).ToList();
    var stock = vtDemo.GroupBy(x => x.Ma).Select((g, i) => (Ma: g.Key, Ten: g.First().Ten, Ton: g.Sum(x => x.Sl) + (i == 2 ? 24 : i == 5 ? -10 : 0), Tl: g.First().Tl)).ToList();
    var headers = new[] { "Item ID", "Mã hàng", "Tên hàng", "Đặc tính", "Hợp đồng", "ĐVT", "Tồn cuối", "Trọng lượng" };
    var arr = new System.Text.Json.Nodes.JsonArray(stock.Select((r, i) => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
    {
        ["c0"] = (100 + i).ToString(), ["c1"] = r.Ma, ["c2"] = r.Ten, ["c3"] = "IQF", ["c4"] = "HD-2611", ["c5"] = "Thùng", ["c6"] = r.Ton.ToString(ic), ["c7"] = r.Tl.ToString(ic),
    }).ToArray());
    var meta = new System.Text.Json.Nodes.JsonObject { ["headers"] = new System.Text.Json.Nodes.JsonArray(headers.Select(h => (System.Text.Json.Nodes.JsonNode)h).ToArray()), ["fileName"] = "TON KHO AN AN 22.11.xlsx", ["dataDate"] = DateTime.Today.AddDays(-1).ToString("dd/MM/yyyy") };
    s.Lan.Write("ton-kho-an-an", new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { ["LoadData"] = arr, ["LoadDataMeta"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["data"] = "__JSON__" + meta.ToJsonString() }) });

    var kkTpl = KhoLanh.Core.Entry.EntryService.BuiltIn.First(t => t.Id == "kiem-ke");
    var typoMa = stock[7].Ma; var typoTo = typoMa[..^3] + "B" + typoMa[^2..];
    if (typoTo == typoMa) typoTo = typoMa[..^3] + "C" + typoMa[^2..];
    var typoDone = false;
    string[] Line((string Vt, string Ma, string Ten, string Lo, int Sl, double Tl) x, int idx)
    {
        var sl = x.Sl; var ma = x.Ma; var tl = x.Tl;
        if (idx % 97 == 5) sl -= 3; else if (idx % 131 == 11) sl += 2;
        if (!typoDone && ma == typoMa) { ma = typoTo; typoDone = true; }
        if (ma == stock[10].Ma && idx % 2 == 0) tl += 0.5;
        // mẫu Kiểm kê 1.62: Vị trí, Mã hàng, Size, Trọng lượng, Số lượng, Net(kg), Ghi chú, Ngày nhập
        return new[] { x.Vt, ma, "", tl.ToString(ic), sl.ToString(), "", "lô " + x.Lo, DateTime.Today.ToString("dd/MM/yyyy") };
    }
    var all = vtDemo.Select((x, i) => (x, i)).ToList();
    bool Part1(string vt) => vt.StartsWith("TG1.A.") || vt.StartsWith("TG1.B.");
    bool Skip(string vt) => vt.StartsWith("TG1.D.14.6") || vt.StartsWith("TG1.D.14.7");
    var kp1 = s.Entry.Create($"Kiểm kê TG1 khu A-B {DateTime.Today.AddDays(-1):dd/MM/yyyy}", kkTpl);
    s.Entry.Save(kp1, kp1.Columns, all.Where(t => Part1(t.x.Vt)).Select(t => (IReadOnlyList<string>)Line(t.x, t.i)).ToList());
    var rest = all.Where(t => !Part1(t.x.Vt) && !Skip(t.x.Vt)).Select(t => (IReadOnlyList<string>)Line(t.x, t.i)).ToList();
    rest.Add(new[] { "TG1.C.05.6.4", "TPC-VRHLBTNXEC0006070IMA1", "60/70", "10", "18", "", "hàng lạ, chưa có mã", DateTime.Today.ToString("dd/MM/yyyy") });
    var kp2 = s.Entry.Create($"Kiểm kê TG1 C-D, TG2 {DateTime.Today:dd/MM/yyyy}", kkTpl);
    s.Entry.Save(kp2, kp2.Columns, rest);

    var kk = s.KiemKe; kk.Load();
    kk.SetChosen(new[] { kp1.Id, kp2.Id }, latestOnly: true);
    kk.SaveKeToan(stock.Select((r, i) => new KhoLanh.Core.KiemKe.KiemKeCore.KtRow(r.Ma, r.Ten, r.Tl.ToString(ic), r.Ton.ToString(ic), ((r.Ton + (i == 3 ? 15 : 0)) * r.Tl).ToString(ic))), "Dán từ file kế toán");
    kk.SetPeople(new[] { "Anh Tuấn", "Chị Hoa", "Anh Phúc" });
}

// Kết quả kháng sinh mẫu (giả lập): 3 ngày kiểm lần 1 (có phiếu PDF), vài lô kiểm lại "MKS chờ", vài lô có Oxy / Cipro / AOZ
{
    var rnd = new Random(5);
    var rules = KhoLanh.Core.KhangSinh.KhangSinhCore.DefaultRules();
    var rows = new System.Text.Json.Nodes.JsonArray();
    string[] ncc = { "PHI X2L1", "PHI X2L2", "THUẬN PHƯỚC", "MINH HẢI", "CÀ MAU 3" };
    var n = 0;
    for (var day = 3; day >= 1; day--)
    {
        var d = DateTime.Today.AddDays(-day); var iso = d.ToString("yyyy-MM-dd");
        for (var lo = 1; lo <= 16; lo++)
        {
            var inp = KhoLanh.Core.KhangSinh.KhangSinhCore.FieldKeys.ToDictionary(k => k, _ => "ND");
            var x = rnd.NextDouble();
            if (x < 0.18) inp["oxy"] = (rnd.Next(5, 40)).ToString();
            else if (x < 0.3) inp["cipro"] = (rnd.Next(2, 9) + 0.4).ToString(System.Globalization.CultureInfo.InvariantCulture);
            else if (x < 0.34) inp["enro"] = "3.1";
            if (day == 2 && lo == 7) inp["aoz"] = "0.4";
            if (day == 1 && lo == 11) inp["oxy"] = "95";
            var rule = KhoLanh.Core.KhangSinh.KhangSinhCore.PickBest(rules, inp);
            var rec = new System.Text.Json.Nodes.JsonObject
            {
                ["date"] = iso, ["batch"] = lo.ToString(), ["stt"] = null, ["savedAt"] = d.AddHours(15).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
                ["source"] = "lan1-import", ["dat"] = rule?.Dat ?? "", ["kyhieu"] = rule?.KyHieu ?? "", ["inputs"] = KhoLanh.Core.KhangSinh.KhangSinhCore.InputsJson(inp),
                ["note"] = ncc[(lo + day) % ncc.Length], ["mota"] = "",
                ["lan1Src"] = new System.Text.Json.Nodes.JsonObject { ["file"] = $"KQKS KHO ngày {d:dd.MM.yy}.pdf", ["ngayKiem"] = iso, ["stt"] = lo, ["trang"] = 1 + lo / 9, ["canhBao"] = new System.Text.Json.Nodes.JsonArray(), ["xacNhanTay"] = false, ["suaTay"] = false },
            };
            if (!inp.Values.All(v => v == "ND") && day >= 2 && inp["aoz"] == "ND")
            {
                var d2 = d.AddDays(1); var ok = KhoLanh.Core.KhangSinh.KhangSinhCore.FieldKeys.ToDictionary(k => k, _ => "ND");
                var r2 = KhoLanh.Core.KhangSinh.KhangSinhCore.PickBest(rules, ok);
                var l = new System.Text.Json.Nodes.JsonObject
                {
                    ["dat"] = r2?.Dat ?? "", ["kyhieu"] = r2?.KyHieu ?? "", ["kq1"] = rule?.KyHieu ?? "", ["inputs"] = KhoLanh.Core.KhangSinh.KhangSinhCore.InputsJson(ok), ["ghichu"] = "",
                    ["mau"] = $"MKS chờ {++n} ngày {d2:dd.MM} · LSX: 0{50 + n}/11 · Lô: {lo} ({d:dd.MM.yy})", ["group"] = "", ["date"] = d2.ToString("yyyy-MM-dd"),
                    ["so"] = $"{d2:dd}/KS {d2:MM}", ["file"] = $"KQKS MKS chờ ngày {d2:dd.MM.yy}.pdf", ["stt"] = n, ["trang"] = 1, ["xacNhanTay"] = false, ["suaTay"] = false,
                    ["savedAt"] = d2.AddHours(16).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"),
                };
                rec["lan2"] = l.DeepClone(); rec["lan2All"] = new System.Text.Json.Nodes.JsonArray(l);
            }
            rows.Add(new System.Text.Json.Nodes.JsonObject { ["key"] = $"s:batch:{iso}:{lo}:demo{day}{lo:00}", ["value"] = rec.ToJsonString() });
        }
    }
    s.Lan.Write(KhoLanh.Core.KhangSinh.KhangSinhService.Db, new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { [KhoLanh.Core.KhangSinh.KhangSinhService.Sheet] = rows });
}

// Phiếu xuất mẫu cho "Phiếu xuất hệ thống" (Tồn kho An An, Tồn kho gửi; Tồn theo vị trí đã có phiếu mẫu ở trên)
{
    var t0 = DateTime.Today;
    var slips = new System.Text.Json.Nodes.JsonArray();
    string[] tens = { "Tôm sú BTP IQF", "Tôm thẻ PD Semi", "Mực ống cắt khoanh", "Cá basa fillet" };
    for (var i = 0; i < 9; i++)
    {
        var lines = new System.Text.Json.Nodes.JsonArray();
        for (var k = 0; k <= i % 4; k++)
            lines.Add(new System.Text.Json.Nodes.JsonObject { ["maHang"] = $"TPC-VRHLBTNX{k}CR00{314 + i}IME1", ["tenHang"] = tens[k], ["size"] = new[] { "16/20", "21/25", "31/40", "U10" }[k], ["hopDong"] = $"HD{20 + i}", ["qty"] = 10 + i * 3 + k });
        var o = new System.Text.Json.Nodes.JsonObject { ["id"] = $"PX-{118 + i:0000}", ["createdAt"] = t0.AddDays(-i / 2.0).AddHours(8 + i).ToString("yyyy-MM-dd'T'HH:mm:ss"), ["nguoiTao"] = i % 3 == 0 ? "Lan" : "Nguyễn Văn An", ["ghiChu"] = i % 2 == 0 ? "Xuất container 40ft" : "", ["deducted"] = true, ["lines"] = lines };
        if (i % 3 == 0) { o["phoneId"] = "dt" + i; o["phoneMaPhieu"] = $"DT{t0:yyMMdd}-0{i}30"; }
        slips.Add(o);
    }
    s.Modules.WriteSheets("ton-kho-an-an", new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { ["PhieuXuat"] = new() { new System.Text.Json.Nodes.JsonObject { ["data"] = "__JSON__" + new System.Text.Json.Nodes.JsonObject { ["slips"] = slips, ["seq"] = 130 }.ToJsonString() } } });
    var heads = new System.Text.Json.Nodes.JsonArray(); var items = new System.Text.Json.Nodes.JsonArray();
    for (var i = 0; i < 5; i++)
    {
        heads.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = (19 + i).ToString(), ["maPhieu"] = $"PX00{19 + i}", ["ngayTao"] = t0.AddDays(-i).AddHours(9).ToString("yyyy-MM-dd'T'HH:mm:ss"), ["nguon"] = i == 1 ? "Điện thoại" : "PC", ["nguoiTao"] = "Thắng", ["ghiChu"] = i == 0 ? "Xuất cho khách Bình Dương" : "" });
        for (var k = 0; k < 2 + i; k++)
            items.Add(new System.Text.Json.Nodes.JsonObject { ["phieuId"] = (19 + i).ToString(), ["maHang"] = $"G{k + 1}0{i}", ["tenHang"] = tens[k % 4] + " (gửi)", ["phieuNhap"] = $"PN{i}{k}", ["soLo"] = $"L{k}", ["size"] = "21/25", ["soKien"] = 100, ["soLuongXuat"] = 5 + k, ["trongLuongTru"] = (5 + k) * 10.8 });
    }
    s.Modules.WriteSheets("ton-kho-gui", new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { ["PhieuXuat"] = heads, ["PhieuXuatItems"] = items });
}

// Dữ liệu mẫu In chứng từ kho (đúng định dạng app cũ lưu trong database "in-chung-tu-kho")
{
    var arr = new System.Text.Json.Nodes.JsonArray();
    string[] ten = { "Tôm sú, IQF, EU, Culinar, 10%, 31/40 (9 kg x 1 gói)", "Tôm thẻ PD, Semi, 21/25 (1.8 kg x 6 gói)", "Mực ống cắt khoanh, 2L30 (0.140 kg x 1 gói)", "Cá basa fillet, 5-7 oz (4.5 kg x 1 gói)" };
    for (var i = 0; i < 14; i++)
    {
        var d = DateTime.Today.AddDays(-(i / 5));
        arr.Add(new System.Text.Json.Nodes.JsonObject
        {
            ["maHang"] = $"TPC-VRHLBTNXECR00314{i % 4}IME1", ["tenHang"] = i % 5 == 3 ? ten[i % 4][..Math.Min(40, ten[i % 4].Length)] : ten[i % 4], ["size"] = "", ["soLuong"] = 5 + i * 2,
            ["ngayChungTu"] = d.ToString("dd/MM/yyyy"), ["ngayChungTuSort"] = new DateTimeOffset(d).ToUnixTimeMilliseconds(), ["trongLuong"] = new[] { 9, 10.8, 0.84, 4.5 }[i % 4],
            ["net"] = Math.Round((5 + i * 2) * new[] { 9, 10.8, 0.84, 4.5 }[i % 4], 2), ["tach"] = i == 6 ? 1 : "", ["goiY"] = i is 6 or 7 ? "markfood" : "",
        });
    }
    s.Modules.WriteSheets("in-chung-tu-kho", new Dictionary<string, System.Text.Json.Nodes.JsonArray?>
    {
        ["DuLieu_TBB"] = arr,
        ["TTB"] = new() { new System.Text.Json.Nodes.JsonObject { ["pxNhan"] = "Lê Văn Hùng", ["thuKhoXuat"] = "Trần Thị Bình", ["keToan"] = "Phạm Thu Hà", ["quanLyKho"] = "Nguyễn Văn An", ["giamDoc"] = "Võ Minh Tâm" } },
        ["CT_Meta"] = new() { new System.Text.Json.Nodes.JsonObject { ["data"] = "__JSON__" + new System.Text.Json.Nodes.JsonObject { ["v"] = 1, ["mod"] = "tbb", ["files"] = new System.Text.Json.Nodes.JsonObject { ["tbb"] = "XUAT-KHO-T11.xlsx" }, ["form"] = new System.Text.Json.Nodes.JsonObject { ["fSoPhieu"] = "TBB-1123", ["fNgayLap"] = DateTime.Today.ToString("yyyy-MM-dd"), ["fLoaiChungTu"] = "Xuất Thay Thùng", ["fKho"] = "Kho Lạnh - An An", ["fMaBieuMau"] = "BM02/QT23", ["fSoBanMoiNgay"] = "2" } }.ToJsonString() } },
    });
}

// Phiếu xuất kho mẫu (đúng định dạng app cũ: database "phieu-nhap-xuat", sheet PNX_Phieu)
{
    var mk = new KhoLanh.Core.PhieuKho.Phieu { Id = "pxdemo1", Ngay = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd"), SoPhieu = $"03.{DateTime.Today:MM} XKAA 02", Lsx = "26.09.057", Khach = "WISMETTAC", HopDong = "WM-2611", O40 = "XK199", XuatLuc = "" };
    mk.Xe.TaiXe = "Nguyễn Văn Hùng"; mk.Xe.Sdt = "0903 456 789"; mk.Xe.SoXe = "51C-123.45"; mk.Xe.Cont = "MSKU1234567"; mk.Xe.Seal = "SL-889900"; mk.Xe.Booking = "BK2611";
    mk.Mh = new()
    {
        new KhoLanh.Core.PhieuKho.MatHang { Ten = "Tôm TCT cooked PDTO", Ma = "VRHLBTNXECR003140IME1", Lo = "L2611", Lsx = "26.09.057", DongList = new()
        {
            new() { Date = "10.11.2026", Size = "31/40", Packing = "1.0 kg x 5 x 2", Kien = "120", Mks = "40" }, new() { Date = "10.11.2026", Size = "41/50", Packing = "1.0 kg x 5 x 2", Kien = "80" },
            new() { Date = "11.11.2026", Size = "31/40", Packing = "1.0 kg x 5 x 2", Kien = "60" },
        } },
        new KhoLanh.Core.PhieuKho.MatHang { Ten = "Tôm sú tẩm bột", Ma = "PBTO0001ECR0031403IM", DongList = new() { new() { Date = "12.11.2026", Size = "U10", Packing = "24 oz x 12", Kien = "25" } } },
    };
    var other = KhoLanh.Core.PhieuKho.PhieuKhoCore.Clone(mk); other.Id = "pxdemo0"; other.SoPhieu = $"02.{DateTime.Today:MM} XKTP 01"; other.Ngay = DateTime.Today.AddDays(-5).ToString("yyyy-MM-dd"); other.Lsx = "26.08.071"; other.Khach = "SYSCO"; other.XuatLuc = "2026-11-18T01:00:00.000Z";
    mk.CapNhat = "2026-11-22T01:00:00.000Z"; other.CapNhat = "2026-11-18T01:00:00.000Z";
    var rowsPk = new System.Text.Json.Nodes.JsonArray();
    var olds = Enumerable.Range(1, 3).Select(k =>   // 1.82: thêm phiếu các tháng trước để thấy danh sách nhóm theo tháng
    {
        var o = KhoLanh.Core.PhieuKho.PhieuKhoCore.Clone(other); var d = DateTime.Today.AddMonths(-k).AddDays(-k);
        o.Id = "pxdemo-old" + k; o.Ngay = d.ToString("yyyy-MM-dd"); o.SoPhieu = $"{10 + k:00}.{d:MM} XKAA {20 + k}"; o.Khach = new[] { "UMIOS FOOD UK", "PACIFIC AMERICAN", "TOYO REIZO" }[k - 1]; o.Lsx = $"26.{d:MM}.0{40 + k}";
        return o;
    }).ToArray();
    foreach (var q in new[] { mk, other }.Concat(olds))
        rowsPk.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = q.Id, ["loai"] = q.Loai, ["ngay"] = q.Ngay, ["soPhieu"] = q.SoPhieu, ["lsx"] = q.Lsx, ["khach"] = q.Khach, ["capNhat"] = q.CapNhat, ["data"] = "__JSON__" + KhoLanh.Core.PhieuKho.PhieuKhoCore.ToJson(q) });
    s.Modules.WriteSheets("phieu-nhap-xuat", new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { ["PNX_Phieu"] = rowsPk });
}

// Kết quả kháng sinh + Cargo mẫu (Thuận Phước)
{
    var ksRows = new System.Text.Json.Nodes.JsonArray();
    void Ks(string date, string b, string ky, string dat) => ksRows.Add(new System.Text.Json.Nodes.JsonObject { ["key"] = $"s:batch:{date}:{b}:x{b}", ["value"] = new System.Text.Json.Nodes.JsonObject { ["date"] = date, ["batch"] = b, ["kyhieu"] = ky, ["dat"] = dat, ["savedAt"] = "2026-11-20T01:00:00.000Z", ["inputs"] = new System.Text.Json.Nodes.JsonObject() }.ToJsonString() });
    var nl = DateTime.Today.AddDays(-6).ToString("yyyy-MM-dd");
    Ks(nl, "16", "03.N-EU-H", "ĐẠT NHẬT, EU, HQ"); Ks(nl, "17", "07.N-AS1-H", "ĐẠT NHẬT, ASC-B, HQ");
    s.Modules.WriteSheets("ket-qua-khang-sinh", new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { ["KhangSinh"] = ksRows });
    var cg = new KhoLanh.Core.PhieuKho.CargoRec { Id = "cgdemo1", Ngay = DateTime.Today.ToString("yyyy-MM-dd"), SoPhieu = $"05.{DateTime.Today:MM} XTP 46", Kho = "An An + Long An", NguoiNhan = "Nguyễn Văn Hùng ( 0903456789 )", ContXe = "51C-123.45 / MSKU1234567", Seal = "SL-889900" };
    var nlTxt = DateTime.Today.AddDays(-6).ToString("dd.MM.yy");
    cg.Blocks = new()
    {
        new KhoLanh.Core.PhieuKho.CargoBlock { Id = "b1", Loai = "ks", Ten = "HLSO", Lines = new()
        {
            new() { Loai = "BTP", Sn = "0040-6", Lo = nlTxt + " L16", Dai = new() { ["1"] = "62", ["2"] = "62", ["3"] = "30" } },
            new() { Loai = "BTP", Sn = "0030-6", Lo = nlTxt + " L17", Dai = new() { ["3"] = "32", ["4"] = "54" } },
            new() { Loai = "BTP", Sn = "0050-6", Lo = nlTxt + " L9", Dai = new() { ["5"] = "20" } },
        } },
        new KhoLanh.Core.PhieuKho.CargoBlock { Id = "b2", Loai = "khac", Ten = "TÔM VỤN", Lines = new() { new() { Size = "VỤN", Pack = "10.0 kg x 1", Dai = new() { ["5"] = "40" } } } },
    };
    cg.CapNhat = "2026-11-22T01:00:00.000Z";
    s.Modules.WriteSheets("phieu-nhap-xuat", new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { ["PNX_Cargo"] = new() { new System.Text.Json.Nodes.JsonObject { ["id"] = cg.Id, ["ngay"] = cg.Ngay, ["soPhieu"] = cg.SoPhieu, ["kho"] = cg.Kho, ["kien"] = 300, ["capNhat"] = cg.CapNhat, ["data"] = "__JSON__" + KhoLanh.Core.PhieuKho.CargoCore.ToJson(cg) } } });
}

var main = new MainWindow { Width = 1440, Height = 880, WindowState = WindowState.Normal };
main.Show();
Snap(main, "03-man-hinh-chinh");
var small = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal };
small.Show();
Snap(small, "03c-man-hinh-1366");
{
    // menu ngang: mở nhóm "Tồn kho" (popup vẽ riêng rồi ghép vào ảnh cửa sổ)
    var gb = small.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("navtab") && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Tồn kho"));
    gb.Flyout!.ShowAt(gb); for (var i = 0; i < 10; i++) Pump();
    SnapFlyout(small, gb, "03d-menu-tha-xuong");
    gb.Flyout.Hide(); Pump();
}
small.Close();
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "small")
{
    // rà màn hình nhỏ: mỗi module × mỗi tab cấp 1 (và tab cấp 2 trong tab đang mở)
    var sw = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal }; sw.Show();
    var minBad = 0;
    var keys = new[] { "lx", "m6", "m13", "m1", "m2", "m7", "m3sk", "m1vtb", "m8", "m4", "m5", "m9", "m7ck", "m3", "m3dm", "m10", "m12", "m11", "m14", "m15", "m21", "m22" };
    if (Environment.GetEnvironmentVariable("SMALL_KEYS") is { Length: > 0 } sk) keys = sk.Split(',');
    foreach (var k in keys)
    {
        try
        {
            sw.Navigate(k); for (var i = 0; i < 40; i++) { Thread.Sleep(30); Pump(); }
            var tc = sw.PageHost.GetVisualDescendants().OfType<TabControl>().FirstOrDefault();
            var n = tc?.ItemCount ?? 1;
            for (var t = 0; t < n; t++)
            {
                if (tc is not null) { tc.SelectedIndex = t; for (var i = 0; i < 25; i++) { Thread.Sleep(30); Pump(); } }
                CheckUnderNav(sw, $"s-{k}-{t}"); Snap(sw, $"s-{k}-{t}"); CheckMinRows($"s-{k}-{t}");
                var inner = sw.PageHost.GetVisualDescendants().OfType<TabControl>().Where(x => x != tc && x.IsEffectivelyVisible).FirstOrDefault();
                if (inner is not null)
                    for (var u = 1; u < inner.ItemCount; u++) { inner.SelectedIndex = u; for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); } CheckUnderNav(sw, $"s-{k}-{t}-{u}"); Snap(sw, $"s-{k}-{t}-{u}"); CheckMinRows($"s-{k}-{t}-{u}"); }
            }
        }
        catch (Exception e) { Console.WriteLine($"  [{k}] lỗi: {e.Message}"); }
    }
    sw.Close(); s.Lan.Dispose(); Console.WriteLine("Xong (small): " + outDir);
    Console.WriteLine(minBad == 0 ? "[MIN8] OK: mọi bảng đang hiện đủ chiều cao tối thiểu" : $"[MIN8] {minBad} bảng thấp hơn tối thiểu");
    if (navBad.Count > 0) Console.WriteLine("HỎNG: nội dung trang chui lên thanh menu: " + string.Join(", ", navBad));
    if (minBad > 0 || navBad.Count > 0) Environment.ExitCode = 1;
    return;

    // 1.83: mọi bảng đang hiện phải cao ≥ chiều cao tối thiểu (8 dòng, bảng chỉ đọc ít dòng thì đủ dòng của nó)
    void CheckMinRows(string name)
    {
        foreach (var g in sw.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().Where(x => x.IsEffectivelyVisible && x.Bounds.Width > 0))
        {
            var need = g.MinHeightFor(g.MinRowsEffective);
            var c = Avalonia.Layout.LayoutInformation.GetPreviousMeasureConstraint(g);
            var got = c is { } cs && !double.IsInfinity(cs.Height) ? Math.Min(g.Bounds.Height, cs.Height) : g.Bounds.Height;
            if (Environment.GetEnvironmentVariable("MIN8_DEBUG") == "1") Console.WriteLine($"  [dbg] {name}: min {g.MinHeight:0} bounds {g.Bounds.Height:0} constraint {c} deficit {sw.GetVisualDescendants().OfType<KhoLanh.App.Controls.FillScroll>().First().Deficit():0}");
            if (Environment.GetEnvironmentVariable("MIN8_DEBUG") == "1")
                for (Avalonia.Visual? a = g; a is not null && a is not KhoLanh.App.Controls.FillScroll; a = a.GetVisualParent())
                    if (a is Avalonia.Layout.Layoutable l) Console.WriteLine($"     {a.GetType().Name} y={a.TranslatePoint(new Point(0,0), sw)?.Y:0} h={a.Bounds.Height:0} des={l.DesiredSize.Height:0} min={l.MinHeight:0} hgt={l.Height} mc={Avalonia.Layout.LayoutInformation.GetPreviousMeasureConstraint(l)?.Height:0}");
            if (got + 1 < need) { minBad++; Console.WriteLine($"  [MIN8] {name}: bảng được chia {got:0} < {need:0} ({g.MinRowsEffective} dòng)"); }
        }
    }
}
// Lưu trữ cá nhân (1.60): SHOTS_ONLY=lt
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "lt")
{
    void WaitLt(int ms) { for (var i = 0; i < ms / 30; i++) { Thread.Sleep(30); Pump(); } }
    var lt = s.LuuTru.Store;
    var tmpDir = Path.Combine(Path.GetTempPath(), "kl-lt-demo"); Directory.CreateDirectory(tmpDir);
    var ngHd = lt.AddNgan("Hợp đồng & báo giá"); var ngSl = lt.AddNgan("Số liệu kho"); var ngCn = lt.AddNgan("Cá nhân");
    lt.AddNote(ngSl.Id, "Tồn cuối tháng 10 — đối chiếu kế toán", "HLSO 31/40: 1.240 kiện\nPD 41/50: 860 kiện\nChênh lệch: 12 kiện (đã báo kế toán 02/11)", new[] { "tồn kho", "tháng 10" });
    lt.AddNote(ngSl.Id, "Nhiệt độ kho mát tuần 47", "Kho 1: -18,5 °C · Kho 2: -19,2 °C · Kho 3: -20,1 °C\nĐã kiểm máy nén kho 2.", new[] { "nhiệt độ" });
    var pin = lt.AddNote(LtStoreDefault(), "Số tài khoản nhận tiền cước xe", "Vietcombank 0071 000 123 456 — CN Cần Thơ", new[] { "quan trọng" });
    lt.Edit(pin.Id, new KhoLanh.Core.LuuTru.LtStore.Patch(Pinned: true));
    lt.AddNote(ngHd.Id, "Bảng giá cước lạnh 2026", "Bảng giá mới: https://example.com/bang-gia-cuoc-lanh-2026", new[] { "báo giá" });
    var img = Path.Combine(tmpDir, "anh-pallet-kho-2.png");
    using (var bmp = new SkiaSharp.SKBitmap(640, 420))
    {
        using var cnv = new SkiaSharp.SKCanvas(bmp); cnv.Clear(new SkiaSharp.SKColor(214, 226, 238));
        using var p1 = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(31, 111, 214) }; using var p2 = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(150, 110, 70) };
        for (var i = 0; i < 4; i++) { cnv.DrawRect(40 + i * 150, 120, 120, 200, p1); cnv.DrawRect(30 + i * 150, 320, 140, 22, p2); }
        using var im = SkiaSharp.SKImage.FromBitmap(bmp); using var d = im.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90); File.WriteAllBytes(img, d.ToArray());
    }
    var pdf = Path.Combine(tmpDir, "Hop dong thue kho 2026.pdf"); File.WriteAllBytes(pdf, new byte[183_000]);
    var xls = Path.Combine(tmpDir, "Bao gia cuoc thang 11.xlsx"); File.WriteAllBytes(xls, new byte[42_000]);
    lt.AddFile(ngHd.Id, pdf); lt.AddFile(ngHd.Id, xls);
    var imgRec = lt.AddFile(ngSl.Id, img, "Pallet kho 2 sau khi xếp lại");
    lt.AddNote(ngCn.Id, "Việc cần nhớ", "- Gia hạn bảo hiểm xe nâng trước 30/11\n- Hẹn thợ kiểm tra cửa kho 3", new[] { "việc" });
    var cfg = s.LuuTru.Config; cfg.Email = "kholanh.anan@gmail.com"; cfg.Refresh = KhoLanh.Core.Services.Secret.Protect("demo"); cfg.LastOk = DateTime.Now.AddMinutes(-25);
    cfg.LastSeq = lt.D.Seq - 2;
    cfg.AddRun(DateTime.Now.AddDays(-1).AddHours(-3), true, "Google", "Đăng nhập kholanh.anan@gmail.com");
    cfg.AddRun(DateTime.Now.AddDays(-1).AddHours(-3), true, "Sao lưu", "Sao lưu: tải lên 3 tệp (225 KB)");
    cfg.AddRun(DateTime.Now.AddMinutes(-25), true, "Sao lưu", "Tự sao lưu: danh sách mục");
    s.LuuTru.SaveConfig(cfg);
    s.LuuTru.ClientOverride = new("123-demo.apps.googleusercontent.com", "demo");
    main.Navigate("m19"); WaitLt(800); Snap(main, "60-luu-tru-kho");
    var ltv = main.PageHost.GetVisualDescendants().OfType<LuuTruView>().First();
    var g = ltv.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First();
    var rows = lt.Query();
    g.Select(rows.FindIndex(r => r.Id == imgRec.Id), 2); WaitLt(500); Snap(main, "60b-luu-tru-anh");
    var tcl = ltv.GetVisualDescendants().OfType<TabControl>().First();
    tcl.SelectedIndex = 1; WaitLt(600); Snap(main, "60c-luu-tru-sao-luu");
    s.Lan.Dispose(); Console.WriteLine("Xong (lt): " + outDir); return;
}
static string LtStoreDefault() => KhoLanh.Core.LuuTru.LtStore.DefaultNgan;
// Kiểm tra lỗi "ComboBox already has a visual parent WrapPanel" (Trợ lý 24/11): Tồn kho An An › bảng lọc — vẽ lại khung điều kiện
// nhiều lần (đổi bảng lọc + gọi lại RenderPl) phải không ném lỗi. SHOTS_ONLY=an-loc; thoát mã 1 nếu lỗi.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "an-loc")
{
    main.Navigate("m1"); for (var i = 0; i < 40; i++) { Thread.Sleep(30); Pump(); }
    var anv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.AnAnView>().First();
    var plMod = (ComboBox)typeof(KhoLanh.App.Views.AnAnView).GetField("_plMod", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(anv)!;
    try
    {
        anv.RenderPl(); anv.RenderPl();
        var n = plMod.ItemCount;
        for (var k = 0; k < Math.Max(2, n) * 2; k++) { plMod.SelectedIndex = k % Math.Max(1, n); Pump(); anv.RenderPl(); }
        var bar = anv.CfBar();
        Console.WriteLine($"[AN-LOC] đạt: vẽ lại {Math.Max(2, n) * 2 + 2} lần, thanh Thêm có {bar.Children.Count} ô, trong cây: {bar.GetVisualParent() is not null}");
        Snap(main, "an-loc");
    }
    catch (Exception ex) { Console.WriteLine("[AN-LOC] LỖI: " + ex.GetType().Name + ": " + ex.Message); s.Lan.Dispose(); Environment.Exit(1); }
    s.Lan.Dispose(); Console.WriteLine("Xong (an-loc): " + outDir); return;
}
// 1.69: chuột phải ở bảng có lọc báo "Specified cast is not valid" (menu tìm vạch ngăn theo vị trí). SHOTS_ONLY=menu-phai: mở menu chuột phải
// ở mọi bảng của Tồn kho gửi + Tồn kho An An (mọi tab); thoát mã 1 nếu lỗi.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "menu-phai")
{
    var n = 0;
    try { s.AnAn.AssignGroup(new[] { "IQF" }, KhoLanh.Core.TonKho.AnAnData.HlsoColumns[0]); } catch (Exception ex) { Console.WriteLine("[MENU-PHAI] gộp thử: " + ex.Message); }
    foreach (var key in new[] { "m2", "m1" })
    {
        main.Navigate(key); for (var i = 0; i < 40; i++) { Thread.Sleep(30); Pump(); }
        var tabs = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
        for (var t = 0; t < tabs.ItemCount; t++)
        {
            tabs.SelectedIndex = t; for (var i = 0; i < 15; i++) { Thread.Sleep(20); Pump(); }
            foreach (var sub in main.PageHost.GetVisualDescendants().OfType<TabControl>().Where(x => x != tabs && x.IsEffectivelyVisible).ToList())
                for (var u = 0; u < sub.ItemCount; u++)
                {
                    sub.SelectedIndex = u; for (var i = 0; i < 10; i++) { Thread.Sleep(20); Pump(); }
                    if (u == 0 && (tabs.Items[t] as TabItem)?.Header?.ToString() is { } th && th.Contains("Đặc", StringComparison.OrdinalIgnoreCase))
                        Snap(main, $"dac-tinh-khoa-{key}");   // 1.69: đặc tính đã gộp hiện mờ (khóa)
                    foreach (var g in main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().Where(x => x.IsEffectivelyVisible).ToList())
                    {
                        var surf = g.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.ContextMenu is not null);
                        if (surf?.ContextMenu is not { } cm) continue;
                        try
                        {
                            var op = false; cm.Opened += (_, _) => op = true;
                            surf.RaiseEvent(new ContextRequestedEventArgs());   // như bấm chuột phải
                            Pump(); if (!op) cm.Open(surf); Pump(); cm.Close(); Pump(); n++;
                        }
                        catch (Exception ex) { Console.WriteLine($"[MENU-PHAI] LỖI ({key} tab {t}.{u}): {ex.GetType().Name}: {ex.Message}"); s.Lan.Dispose(); Environment.Exit(1); }
                    }
                }
        }
    }
    Console.WriteLine($"[MENU-PHAI] đạt: mở menu chuột phải {n} lần không lỗi");
    s.Lan.Dispose(); Console.WriteLine("Xong (menu-phai): " + outDir); return;
}
// 1.69: tab Báo Cáo (tab 2) của Tồn kho An An (Tổng Tồn + Tồn Kho Gửi + Đối Soát chung 1 tab, cảnh báo lệch) và Tồn kho gửi
// (Báo Cáo tab 2, Đối Soát có cảnh báo). SHOTS_ONLY=bc
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "bc")
{
    void W(int n) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    {   // Tồn kho gửi: 7 dòng mẫu (CKQ / ASCN chưa gộp đặc tính → cảnh báo)
        var fp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ton-kho-gui-bc 23.11.xlsx");
        using (var wbk = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wbk.AddWorksheet("Sheet1"); ws.Cell(1, 1).Value = "BÁO CÁO TỒN KHO GỬI";
            var hh = new[] { "Mã hàng", "Tên hàng", "Số lô", "Size", "Quy cách", "Ngày nhập kho", "Phiếu nhập", "Số kiện", "Trọng lượng", "Số kiện", "Trọng lượng" };
            for (var c = 0; c < hh.Length; c++) ws.Cell(2, c + 1).Value = hh[c];
            var gdata = new (string, string, string, string, double, double)[]
            {
                ("VRHLBTNXECR003140IME1", "TÔM TCT BTP50, BLOCK, 1.8Kg x 6, 0083", "L1", "BTP50", 10, 108), ("VRHLBTNXECR003141IME1", "TÔM TCT BTP35, IQF, 9Kg x 1, CKQ", "04.04.26 L12 NÂU", "BTP35", 5, 45),
                ("VRHLBTNXECR003142IME1", "TÔM ECU 41/50, BLOCK, 1.8Kg x 7, ASCN", "ECUADOR-01", "ECU41/50", 8, 100.8), ("VRHLBTNXECR003144IME1", "NL 105, BLOCK, 10kg x 1, 0083", "L5", "NL 105", 4, 40),
            };
            var rr = 3; foreach (var x in gdata) { ws.Cell(rr, 1).Value = x.Item1; ws.Cell(rr, 2).Value = x.Item2; ws.Cell(rr, 3).Value = x.Item3; ws.Cell(rr, 4).Value = x.Item4; ws.Cell(rr, 5).Value = "1.8Kg x 6"; ws.Cell(rr, 6).Value = new DateTime(2026, 11, 5); ws.Cell(rr, 7).Value = "PN" + rr; ws.Cell(rr, 8).Value = 99; ws.Cell(rr, 9).Value = 99; ws.Cell(rr, 10).Value = x.Item5; ws.Cell(rr, 11).Value = x.Item6; rr++; }
            wbk.SaveAs(fp);
        }
        s.Gui.Commit(s.Gui.Preview(fp));
        s.Gui.SaveManual(new[] { ("Tôm EU Nhật", "83.EU-H-M") }); s.Gui.MergeGroup(new[] { "83.EU-H-M" }, "EU");
    }
    {   // Tồn kho An An: 12 dòng Thành phẩm + 2 dòng không thuộc bảng nào (→ lệch Load Data); chưa nhập Bột + Sốt + Bếp
        var hs = new[] { "Tình trạng", "Mã hàng", "Tên hàng", "Đặc tính", "Hợp đồng", "ĐVT", "Tồn cuối", "Trọng lượng", "Size" };
        var rowsA = new System.Text.Json.Nodes.JsonArray(Enumerable.Range(0, 14).Select(i => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
        { ["c0"] = i < 12 ? "Thành phẩm" : "", ["c1"] = i < 12 ? $"VRHLBTNXECR00{3140 + i}IME1" : $"ZZ-LA-{i}", ["c2"] = $"Tôm thẻ HLSO 31/40 mẫu {i}", ["c3"] = "IQF", ["c4"] = "HD-2611", ["c5"] = "Thùng", ["c6"] = (40 + i).ToString(), ["c7"] = "10", ["c8"] = "31/40" }).ToArray());
        var mt = new System.Text.Json.Nodes.JsonObject { ["headers"] = new System.Text.Json.Nodes.JsonArray(hs.Select(h => (System.Text.Json.Nodes.JsonNode)h).ToArray()), ["fileName"] = "TON KHO AN AN 23.11.xlsx", ["dataDate"] = DateTime.Today.ToString("yyyy-MM-dd") };
        s.Lan.Write("ton-kho-an-an", new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { ["LoadData"] = rowsA, ["LoadDataMeta"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["data"] = "__JSON__" + mt.ToJsonString() }) });
    }
    foreach (var (win, tag) in new[] { (main, ""), (new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal }, "-1366") })
    {
        if (win != main) win.Show();
        win.Navigate("audit"); win.Navigate("m1"); W(50);
        var at = win.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible);
        Console.WriteLine($"[BC] An An tab 2 = {(at.Items[1] as TabItem)?.Header} · tab 3 = {(at.Items[2] as TabItem)?.Header}");
        at.SelectedIndex = 1; W(30); Snap(win, "40b-an-an-bao-cao-doi-soat" + tag);
        win.Navigate("m2"); W(50);
        var gt = win.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible);
        Console.WriteLine($"[BC] Gửi tab 2 = {(gt.Items[1] as TabItem)?.Header} · tab 6 = {(gt.Items[5] as TabItem)?.Header}");
        gt.SelectedIndex = 1; W(30); Snap(win, "41b-gui-bao-cao" + tag);
        gt.SelectedIndex = 5; W(30); Snap(win, "41c-gui-doi-soat" + tag);
        if (win != main) win.Close();
    }
    var bd = KhoLanh.Core.TonKho.BaoCaoTongTon.CanhBaoXuat(s.AnAn.D);
    Console.WriteLine($"[BC] cảnh báo xuất Excel: {bd.Count} · " + string.Join(" | ", bd));
    s.Lan.Dispose(); Console.WriteLine("Xong (bc): " + outDir); return;
}
// 1.72 (A3): đo thời gian mở module — lần đầu (dựng mới) và lần mở lại (giữ màn hình), không đổi dữ liệu giữa 2 lần
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "perf")
{
    KhoLanh.App.MainWindow.CacheEnabled = true; main.ClearPageCache();
    var keysP = new[] { "lx", "m1", "m2", "m7", "m8", "m6", "m3sk", "m13", "m9", "m3dm", "m4" };
    var firstMs = new Dictionary<string, double>();
    foreach (var k in keysP) { var sw = System.Diagnostics.Stopwatch.StartNew(); main.Navigate(k); Pump(); firstMs[k] = sw.Elapsed.TotalMilliseconds; for (var i = 0; i < 10; i++) { Thread.Sleep(20); Pump(); } }
    foreach (var k in keysP)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew(); main.Navigate(k); Pump(); var again = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[PERF] {k,-6} mở lần đầu {firstMs[k],7:0} ms · mở lại {again,6:0} ms");
        for (var i = 0; i < 5; i++) { Thread.Sleep(20); Pump(); }
    }
    s.Lan.Dispose(); Console.WriteLine("Xong (perf)"); return;
}
// 1.90: SHOTS_ONLY=lxtrung — LSX trùng lịch: cảnh báo ở tab Việc cần làm, hộp giữ 1 lịch / trùng hợp lệ; gộp xong hết báo. Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "lxtrung")
{
    var ok = true;
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var lx = s.LichXuat; lx.Load();
    string D(int d) => KhoLanh.Core.LichXuat.LichXuatCore.Iso(DateTime.Today.AddDays(d));
    var a = lx.Add(new() { Ngay = D(1), Lsx = "26.09.020", Gio = "10:00", GhiChu = "sửa tay" });
    lx.Add(new() { Ngay = D(2), Lsx = "26.09.020" });
    lx.Load();
    var groups = lx.DupGroups();
    Console.WriteLine($"[Trùng] nhóm: {string.Join(" | ", groups.Select(g => g.Lsx + " ×" + g.Rows.Count))}");
    if (!groups.Any(g => g.Lsx == "26.09.020")) ok = false;
    main.Navigate("lx"); P(60);
    var tqv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.TongQuanView>().First();
    var todo = tqv.GetVisualDescendants().OfType<TabItem>().FirstOrDefault(t => (t.Header as string ?? "").StartsWith("Việc cần làm"));
    Console.WriteLine($"[Trùng] tab: {todo?.Header}");
    if (todo is not null) { todo.IsSelected = true; P(20); }
    Snap(main, "lx-trung-viec-can-lam");
    var t = tqv.OpenDupForTest(groups.First(g => g.Lsx == "26.09.020")); P(30);
    if (tqv.DupWindow is { } dw) { Snap(dw, "lx-trung-hop-giu-lich"); dw.Close(); P(10); } else ok = false;
    lx.ResolveDup(a.Id, groups.First(g => g.Lsx == "26.09.020").Rows.Select(r => r.Id)); lx.Load();
    if (lx.DupGroups().Any(g => g.Lsx == "26.09.020")) { Console.WriteLine("[Trùng] LỖI: gộp xong vẫn báo"); ok = false; }
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (lxtrung) — ĐẠT: " : "Xong (lxtrung) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.74: SHOTS_ONLY=v174 — hộp Email mở được (trước lỗi NullReference), đổi giao diện Tối → Sáng bằng hộp "Giao diện & cỡ chữ",
// màu thanh tiêu đề Windows theo giao diện. Thoát mã 1 nếu kiểm tra hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v174")
{
    var ok = true;
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    main.Navigate("lx"); P(60);
    var tqv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.TongQuanView>().First();
    Exception? emailErr = null;
    var et = tqv.OpenEmailForTest(); P(30);
    if (et.IsFaulted) emailErr = et.Exception?.GetBaseException();
    var ew = tqv.EmailWindow;
    Console.WriteLine($"[Email] mở hộp: {(ew is not null && emailErr is null ? "được" : "LỖI " + emailErr?.Message)}");
    if (ew is null || emailErr is not null) ok = false;
    else
    {
        Snap(ew, "v174-email");
        var tabs = ew.GetVisualDescendants().OfType<TabControl>().First(); tabs.SelectedIndex = 2; P(); Snap(ew, "v174-email-cai-dat");
        ew.Close(); P(10);
    }
    // Giao diện: Tối → Sáng qua chính hộp thoại
    void ThemeTo(bool dark)
    {
        main.OpenThemeForTest(); P(20);
        var tw = main.ThemeWindow!;
        var rb = tw.GetVisualDescendants().OfType<RadioButton>().ToList();
        (dark ? rb[1] : rb[0]).IsChecked = true; P(10);
        Console.WriteLine($"[Giao diện] chọn {(dark ? "Tối" : "Sáng")} → đang {AppTheme.Current} · nền thẻ {AppTheme.Brush("#FFFFFF").Color} · thanh tiêu đề {KhoLanh.App.TitleBar.ColorsFor(main).Caption}");
        if (AppTheme.Current != (dark ? AppTheme.Mode.Dark : AppTheme.Mode.Light)) ok = false;
        // 1.78: thanh tiêu đề cửa sổ CHÍNH theo giao diện (Sáng = không bật chế độ tối → thanh trắng; Tối = thanh tối)
        if (KhoLanh.App.TitleBar.ColorsFor(main).Dark != dark) { Console.WriteLine("[Thanh tiêu đề] LỖI: cửa sổ chính không theo giao diện"); ok = false; }
        tw.GetVisualDescendants().OfType<Button>().First(b => KhoLanh.App.Controls.Icon.TextOf(b.Content) == "Lưu").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); P(20);
    }
    ThemeTo(true); Snap(main, "v174-toi");
    ThemeTo(false); Snap(main, "v174-sang");
    if (AppTheme.Brush("#FFFFFF").Color.R < 200) { Console.WriteLine("[Giao diện] LỖI: về Sáng mà nền vẫn tối"); ok = false; }
    var dlg = new Window(); var (cap, txt, dk) = KhoLanh.App.TitleBar.ColorsFor(dlg);
    Console.WriteLine($"[Thanh tiêu đề] hộp thoại (Sáng): nền {cap} chữ {txt} tối={dk} · COLORREF chính = 0x{KhoLanh.App.TitleBar.ColorRef(KhoLanh.App.TitleBar.ColorsFor(main).Caption):X6}");
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v174) — ĐẠT: " : "Xong (v174) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.76: SHOTS_ONLY=v176 — Hệ thống › Tài khoản › tab Điện thoại (tài khoản PC dùng chung trên điện thoại, quyền điện thoại riêng)
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v176")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    s.Auth.CreateUser("thang", "Thắng", "thang123", Roles.Staff);
    s.Auth.CreateUser("lan", "Lan", "lan12345", Roles.Manager);
    s.PhoneAccounts.SaveRights(new[]
    {
        new KhoLanh.Core.PhoneSync.PhoneAccounts.Entry { Ten = "thang", Bat = true, VaiTro = "xuat", Kho = new() { "M01", "M02", "LX" } },
        new KhoLanh.Core.PhoneSync.PhoneAccounts.Entry { Ten = "admin", Bat = true, VaiTro = "quantri" },
    });
    var pl = s.PhoneAccounts.List(); var ln = pl.FirstOrDefault(e => e.Ten == "lan"); if (ln is not null) { ln.Hash = ""; }
    main.Navigate("users"); P(40);
    var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
    tc.SelectedIndex = tc.ItemCount - 1; P(20);
    Snap(main, "v176-tai-khoan-dien-thoai");
    Console.WriteLine($"[Điện thoại] tab: {((TabItem)tc.Items[tc.ItemCount - 1]!).Header} · {s.PhoneAccounts.List().Count(e => e.Bat)} tài khoản bật");
    s.Lan.Dispose(); Console.WriteLine("Xong (v176): " + outDir); return;
}
// 1.80: SHOTS_ONLY=v180 — Dữ liệu & sao lưu › Máy chủ LAN ở 1366×768: bảng "Trùng sửa / tự gộp" không bị cắt, cuộn được. Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v180")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var w = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal }; w.Show(); P(20);
    // 1.121: LanView không còn nằm trong Đồng bộ & sao lưu (tab Máy chủ = MayChuView; LanView mở bằng «Mở danh sách đầy đủ») → hiện thẳng
    var lv = new KhoLanh.App.Views.LanView();
    w.Show("lan", lv); P(40);
    var cg = lv.FindControl<DataGrid>("ConflictsGrid")!;
    var sv = cg.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
    Console.WriteLine($"[LAN] bảng Trùng sửa cao {cg.Bounds.Height:0} px · trong vùng cuộn: {sv is not null} · vùng cuộn {sv?.Extent.Height:0}/{sv?.Viewport.Height:0}");
    Snap(w, "v180-lan-trung-sua");
    sv?.ScrollToEnd(); P(10); Snap(w, "v180-lan-trung-sua-cuon");
    var ok = cg.Bounds.Height >= 250 && sv is not null;
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v180) — ĐẠT: " : "Xong (v180) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.81: SHOTS_ONLY=v181 — phím ESC quay lại màn hình trước (tối đa 3), hết thì về Tổng quan; đang gõ ô chữ thì ESC đầu chỉ rời ô. Thoát mã 1 nếu hỏng.
// 1.85: SHOTS_ONLY=v184 — mỗi bảng tối thiểu 8 dòng: Phiếu xuất nội địa › Danh mục ở 1366×768 (2 bảng xếp dọc) → trang cuộn dọc,
// bảng Size-quy cách đủ 8 dòng, không đè lên chữ phía trên. Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v184")
{
    void P(int n = 15) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var w = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal }; w.Show();
    w.Navigate("m13"); P(40);
    var tc = w.PageHost.GetVisualDescendants().OfType<TabControl>().First(); tc.SelectedIndex = 1;   // 1.96: Phiếu xuất nội địa là tab thứ 2 P(25);
    var inner = w.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x != tc && x.IsEffectivelyVisible); inner.SelectedIndex = 1; P(30);
    Snap(w, "47-bang-toi-thieu-8-dong");
    var fs = w.GetVisualDescendants().OfType<KhoLanh.App.Controls.FillScroll>().First();
    var ok = true;
    var grids = w.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().Where(g => g.IsEffectivelyVisible).ToList();
    foreach (var g in grids)
    {
        var need = g.MinHeightFor(8);
        Console.WriteLine($"[v184] bảng cao {g.Bounds.Height:0} (cần {need:0})");
        if (g.Bounds.Height + 1 < need) ok = false;
    }
    if (grids.Count == 2)
    {
        var y0 = grids[0].TranslatePoint(new Point(0, 0), w)!.Value.Y; var y1 = grids[1].TranslatePoint(new Point(0, 0), w)!.Value.Y;
        Console.WriteLine($"[v184] bảng 1 y={y0:0}..{y0 + grids[0].Bounds.Height:0} · bảng 2 y={y1:0}");
        if (y1 < y0 + grids[0].Bounds.Height) ok = false;   // đè nhau
    }
    Console.WriteLine($"[v184] trang cuộn: cao nội dung {fs.Extent.Height:0} / khung {fs.Viewport.Height:0} · thiếu còn {fs.Deficit():0}");
    if (fs.Extent.Height <= fs.Viewport.Height + 1 || fs.Deficit() > 1) ok = false;
    fs.Offset = new Vector(0, fs.Extent.Height); P(15);
    Snap(w, "47b-bang-toi-thieu-8-dong-cuon");
    w.Close(); s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v184) — ĐẠT: " : "Xong (v184) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v181")
{
    void P(int n = 15) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    void Esc() { main.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null); main.KeyRelease(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null); P(); }
    var ok = true;
    foreach (var k in new[] { "lx", "m1", "m2", "m7", "m8" }) { main.Navigate(k); P(25); }
    Console.WriteLine($"[ESC] đang {main.CurrentKey} · lịch sử {string.Join(" > ", main.BackHistory)}");
    var seen = new List<string>();
    for (var i = 0; i < 5; i++) { main.PageHost.Focus(); P(3); Esc(); seen.Add(main.CurrentKey); }
    Console.WriteLine($"[ESC] 5 lần ESC → {string.Join(" > ", seen)}");
    if (string.Join(",", seen) != "m7,m2,m1,lx,lx") ok = false;
    main.Navigate("m1"); P(25);
    var tb = main.PageHost.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsEffectivelyVisible);
    if (tb is not null)
    {
        tb.Focus(); P(3); Esc();
        Console.WriteLine($"[ESC] đang gõ ô chữ → ESC đầu: vẫn ở {main.CurrentKey}");
        if (main.CurrentKey != "m1") ok = false;
        Esc(); Console.WriteLine($"[ESC] ESC lần 2 → {main.CurrentKey}");
        if (main.CurrentKey != "lx") ok = false;
    }
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v181) — ĐẠT: " : "Xong (v181) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// Giai đoạn 5: SHOTS_ONLY=v203 — Trợ lý › Các máy (dữ liệu mẫu 3 máy, 1 máy mất liên lạc) + Lịch sử 7 / 30 ngày, 1440×880 và 1366×768.
// Tự kiểm: bảng đủ 3 máy, nhãn "Mất liên lạc", khung phải có cảnh báo, biểu đồ đủ cao, không control nào tràn ngang trang. Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v203")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var now = DateTime.Now; var ok = true;
    // máy này: quét thật + ghi tình trạng thật; 2 máy khác: ghi thẳng vào database dùng chung như khi nhận qua LAN / Drive
    s.TroLy.Scan(false); s.TroLy.PublishStatus();
    var store = s.TroLy.MayStore;
    KhoLanh.Core.TroLy.PerfStore.Sample Smp(double cpu, double ram, double free) => new(now.AddMinutes(-3), cpu, 4, ram, 512, free, 237.9, 9, 120);
    store.Write(KhoLanh.Core.TroLy.TroLyMay.Build("may-kho-02", "KHO-LANH-02", "1.101.0", "Máy con LAN", now.AddMinutes(-7), now.AddHours(-2),
        2, 3, 11, new[] { ("do", "Đồng bộ", "Máy con mất kết nối máy chủ LAN 25 phút"), ("do", "Phần mềm", "Lỗi phần mềm: Object reference not set to an instance of an object."),
            ("vang", "Hiệu năng", "RAM gần đầy kéo dài: trung bình 88% trong 30 phút"), ("vang", "Sao lưu", "Bản sao lưu cũ hơn 2 ngày"), ("vang", "Tồn kho An An", "Mã hợp lệ nhưng chưa có trong Danh sách mã hàng") },
        Smp(76, 91, 8.4), 54, 83));
    store.Write(KhoLanh.Core.TroLy.TroLyMay.Build("may-vp-03", "VAN-PHONG", "1.99.0", "Xem từ xa (Drive)", now.AddHours(-5), now.AddDays(-1),
        0, 1, 14, new[] { ("vang", "Kết nối", "Gmail lịch xuất mất kết nối") }, Smp(18, 52, 120), 22, 48));
    // lịch sử máy này 30 ngày
    var rnd = new Random(5);
    for (var d = 29; d >= 1; d--)
        if (d % 6 != 3)
            for (var k = 0; k < 1 + rnd.Next(3); k++)
                s.TroLy.LichSu.Add(new KhoLanh.Core.TroLy.TroLyLichSu.Scan(now.Date.AddDays(-d).AddHours(8 + 3 * k), k == 0, Math.Max(0, (d % 9 == 0 ? 3 : 1) - rnd.Next(2)),
                    2 + rnd.Next(6), 8 + rnd.Next(4), rnd.Next(3), d % 11 == 0 ? 1 : 0, rnd.Next(2)));
    KhoLanh.App.Views.TroLyMayView Open(MainWindow w)
    {
        w.Navigate("troly"); P(40);
        var tc = w.PageHost.GetVisualDescendants().OfType<TabControl>().First(t => t.Items.OfType<TabItem>().Any(i => KhoLanh.App.Controls.Icon.TextOf(i.Header) == "Các máy"));
        tc.SelectedItem = tc.Items.OfType<TabItem>().First(i => KhoLanh.App.Controls.Icon.TextOf(i.Header) == "Các máy"); P(40);
        var v = w.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.TroLyMayView>().First();
        v.Refresh(); P(20);
        return v;
    }
    bool Check(MainWindow w, KhoLanh.App.Views.TroLyMayView v, string tag)
    {
        var good = true;
        var g = v.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
        var rows = g.Model.RowCount;
        var lost = Enumerable.Range(0, rows).Count(r => g.Model.Get(r, 4).StartsWith("Mất liên lạc", StringComparison.Ordinal));
        var det = v.GetVisualDescendants().OfType<TextBlock>().Count(t => t.IsEffectivelyVisible && t.Text is "NẶNG" or "CẦN XEM");
        var pageW = w.PageHost.Bounds.Width;
        var over = v.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0)
            .Count(c => c.TranslatePoint(new Point(c.Bounds.Width, 0), w.PageHost) is { } pt && pt.X > pageW + 2 && c is not TextBlock);
        Console.WriteLine($"[v203 {tag}] máy {rows} · mất liên lạc {lost} · cảnh báo ở khung phải {det} · lưới {g.Bounds.Width:0}×{g.Bounds.Height:0} · trang rộng {pageW:0} · tràn ngang {over}");
        if (rows != 3 || lost != 1 || det < 1 || g.Bounds.Height < 200 || over > 0) good = false;
        return good;
    }
    var mv = Open(main);
    ok &= Check(main, mv, "1440");
    Snap(main, "v203-cac-may");
    // chọn máy có cảnh báo Nặng
    var grid = mv.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
    var r2 = Enumerable.Range(0, grid.Model.RowCount).First(r => grid.Model.Get(r, 0).StartsWith("KHO-LANH-02", StringComparison.Ordinal));
    grid.Select(r2, 0); P(10);
    var nang = mv.GetVisualDescendants().OfType<TextBlock>().Count(t => t.IsEffectivelyVisible && t.Text == "NẶNG");
    Console.WriteLine($"[v203] chọn KHO-LANH-02 → {nang} cảnh báo Nặng ở khung phải");
    if (nang != 2) ok = false;
    Snap(main, "v203-cac-may-chon");
    mv.Tabs.SelectedIndex = 1; P(30);
    Control Chart(KhoLanh.App.Views.TroLyMayView v) => v.GetVisualDescendants().OfType<Control>().First(c => c.GetType().Name == "DayChart");
    var chart = Chart(mv);
    Console.WriteLine($"[v203] biểu đồ lịch sử {chart.Bounds.Width:0}×{chart.Bounds.Height:0}");
    if (chart.Bounds.Height < 200 || chart.Bounds.Width < 400) ok = false;
    Snap(main, "v203-lich-su-7");
    var seg = mv.GetVisualDescendants().OfType<KhoLanh.App.Controls.Seg>().First(); seg.Selected = 1;
    mv.PaintHistory(); P(15);
    Snap(main, "v203-lich-su-30");
    // 1366×768: dùng cửa sổ nhỏ `small` đã mở sẵn ở đầu công cụ (headless không vẽ lại khi đổi cỡ cửa sổ / mở cửa sổ mới về sau)
    var sw2 = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal }; sw2.Show(); P(20);
    var mv2 = Open(sw2);
    ok &= Check(sw2, mv2, "1366");
    Snap(sw2, "v203-cac-may-1366");
    mv2.Tabs.SelectedIndex = 1; P(30);
    var c2 = mv2.GetVisualDescendants().OfType<Control>().First(c => c.GetType().Name == "DayChart");
    Console.WriteLine($"[v203 1366] biểu đồ lịch sử {c2.Bounds.Width:0}×{c2.Bounds.Height:0}");
    if (c2.Bounds.Width < 400 || c2.Bounds.Height < 200) ok = false;
    // đổi tab con bằng SelectedIndex (như bấm) phải tự vẽ lịch sử
    var hInfo = mv2.GetVisualDescendants().OfType<TextBlock>().Any(t => t.IsEffectivelyVisible && t.Text?.Contains("lần quét trong") == true);
    Console.WriteLine($"[v203 1366] dòng tóm tắt lịch sử: {(hInfo ? "có" : "KHÔNG")}");
    if (!hInfo) ok = false;
    Snap(sw2, "v203-lich-su-1366");
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v203) — ĐẠT: " : "Xong (v203) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.100: SHOTS_ONLY=v200 — Trợ lý › Hiệu năng (dữ liệu mẫu 24 giờ + mốc + thao tác chậm), Sáng và Tối. Thoát mã 1 nếu hỏng.
// 1.121: SHOTS_ONLY=v213 — Đồng bộ & sao lưu: 3 tab Máy chủ · Điện thoại · Sao lưu; tab Máy chủ ở các tình huống dựng được: máy chủ có máy nối
// trong kho + qua Internet + 1 mục trùng sửa, lọc Qua Internet; máy con mất máy chủ có thay đổi chờ gửi; máy đơn dùng bản Drive; máy đơn chưa bật LAN.
// Khóa cũ lan / drive / dulieu / phone / saoluu / dcn mở đúng tab. Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v213")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var ok = true;
    void Bad(string m) { Console.WriteLine("        HỎNG: " + m); ok = false; }
    var code = s.Lan.ConnectionCode();
    System.Text.Json.Nodes.JsonObject Beat(string id, string name, string user, int pend) => new()
    { ["k"] = KhoLanh.Core.Lan.LanService.ParseCode(code).Item2, ["op"] = "beat", ["m"] = id, ["mn"] = name, ["r"] = KhoLanh.Core.Lan.LanService.RoleClient, ["u"] = user, ["pend"] = pend,
      ["a"] = new System.Text.Json.Nodes.JsonArray(id == "m-kho1" ? "192.168.1.20:47820" : "192.168.1.21:47820") };
    s.Lan.Handle(Beat("m-kho1", "MAY-KHO-1", "thang", 0));
    s.Lan.Handle(Beat("m-kho2", "MAY-KETOAN", "lan", 2));
    s.Lan.Handle(Beat("m-nha", "LAPTOP-NHA", "khoa", 0), "", netIp: "113.161.40.12");
    // giả lập đã nối Google Sheet chung + kế hoạch LAN (máy dự phòng) — không gọi mạng khi chỉ mở trang
    var sec = (Func<string, string>)KhoLanh.Core.Services.Secret.Protect;
    s.Settings.Set("acct_url", "https://script.google.com/macros/s/DEMO/exec"); s.Settings.Set("acct_token", sec("demo-token"));
    var lp = new KhoLanh.Core.Lan.LanPlan { On = true, Key = "demo", Port = s.Lan.Port };
    lp.Chu = new KhoLanh.Core.Lan.LanPlan.Slot(s.Lan.MachineId, "MAY-CHU-KHO", new() { "192.168.1.10:47820", "https://kho.kcenter.xyz" }, "", "admin");
    lp.DuPhong = new KhoLanh.Core.Lan.LanPlan.Slot("x", "MAY-VAN-PHONG", new() { "192.168.1.11:47820" }, "", "quanly");
    s.Settings.Set("lan_plan", lp.ToJson());
    KhoLanh.Core.Lan.LanService.SetPublicUrl(s.Settings, "kho.kcenter.xyz");
    KhoLanh.App.Views.MayChuView? Mc() => (main.PageHost.Content as DongBoView)?.MayChu;

    // --- a) máy chủ
    main.Navigate("audit"); P(5); main.Navigate("dongbo"); P(40);
    if (main.PageHost.Content is not DongBoView dv) { Bad("dongbo không mở DongBoView"); s.Lan.Dispose(); Environment.Exit(1); return; }
    var heads = string.Join("|", dv.Tabs.Items.OfType<TabItem>().Select(t => KhoLanh.App.Controls.Icon.TextOf(t.Header)));
    var mc = dv.MayChu;
    Console.WriteLine($"[v213] tab: {heads} · tình huống: {mc.State} · máy đang nối {mc.MachineRows} · trùng sửa {mc.ConflictRows}");
    if (heads != "Máy chủ|Điện thoại|Sao lưu") Bad("cần đúng 3 tab Máy chủ · Điện thoại · Sao lưu");
    if (mc.State != "may-chu") Bad("máy chủ phải hiện tình huống may-chu");
    if (mc.MachineRows != 3) Bad("bảng Máy đang nối cần 3 máy (2 trong kho + 1 Internet)");
    if (mc.ConflictRows < 1) Bad("cần khung Trùng sửa cần xem (có 1 mục chờ)");
    var allTxt = string.Join("\n", mc.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible).Select(t => t.Inlines is { Count: > 0 } il ? string.Concat(il.OfType<Avalonia.Controls.Documents.Run>().Select(r => r.Text)) : t.Text ?? ""));
    if (System.Text.RegularExpressions.Regex.IsMatch(allTxt, @"47\d\d\d|Cổng|cổng|mã LAN")) Bad("tab Máy chủ không được hiện cổng / mã LAN");
    if (!allTxt.Contains("MAY-VAN-PHONG")) Bad("thiếu dòng Máy dự phòng");
    CheckUnderNav(main, "v213-may-chu"); Snap(main, "v213-may-chu");
    mc.MachineFilter.Selected = 2; mc.Render(); P(10);
    Console.WriteLine($"[v213] lọc Qua Internet: {mc.MachineRows} máy");
    if (mc.MachineRows != 1) Bad("lọc Qua Internet cần 1 máy");
    Snap(main, "v213-may-chu-loc-internet");
    mc.MachineFilter.Selected = 0; mc.Render(); P(5);
    var more = mc.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => KhoLanh.App.Controls.Icon.TextOf(b.Content) == "⋯ Thêm" && b.Flyout is MenuFlyout);
    if (more is null) Bad("thiếu nút ⋯ Thêm (Quản trị)");
    else
    {
        var items = ((MenuFlyout)more.Flyout!).Items.OfType<MenuItem>().Select(i => i.Header?.ToString()).ToList();
        Console.WriteLine($"[v213] ⋯ Thêm: {string.Join(" · ", items)}");
        if (!items.Contains("Địa chỉ Internet của máy chủ…") || !items.Contains("Nhật ký nối máy")) Bad("⋯ Thêm thiếu mục");
        more.Flyout!.ShowAt(more); P(10); SnapFlyout(main, more, "v213-may-chu-them"); more.Flyout.Hide(); P(5);
    }
    if (!mc.GetVisualDescendants().OfType<Button>().Any(b => KhoLanh.App.Controls.Icon.TextOf(b.Content) == "Tắt máy chủ" && b.Classes.Contains("danger"))) Bad("thiếu nút Tắt máy chủ (danger)");
    foreach (var (key, tab) in new[] { ("lan", "Máy chủ"), ("drive", "Máy chủ"), ("dulieu", "Máy chủ"), ("phone", "Điện thoại"), ("saoluu", "Sao lưu"), ("dcn", "Sao lưu") })
    {
        main.Navigate("audit"); P(5); main.Navigate(key); P(15);
        var h = (main.PageHost.Content as DongBoView)?.Tabs.SelectedItem is TabItem ti ? KhoLanh.App.Controls.Icon.TextOf(ti.Header) : null;
        Console.WriteLine($"[v213] khóa cũ \"{key}\" → {h}");
        if (h != tab) Bad($"khóa {key} mở {h}, cần {tab}");
    }

    // --- c) máy con mất máy chủ, có thay đổi chờ gửi
    KhoLanh.Core.Lan.LanService.Configure(s.Settings, s.Session, KhoLanh.Core.Lan.LanService.RoleClient, code);
    s.Settings.Set("lan_synced_at", DateTime.Now.AddMinutes(-47).ToString("s", System.Globalization.CultureInfo.InvariantCulture));
    s.ReloadLan(start: false);
    s.Lan.Write("ton-kho-an-an", new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { ["ThuMayChu"] = new() { new System.Text.Json.Nodes.JsonObject { ["ma"] = "a" } } });
    main.Navigate("audit"); P(5); main.Navigate("dongbo"); P(30);
    Console.WriteLine($"[v213] máy con mất máy chủ: {Mc()?.State} · chờ gửi {s.Lan.PendingCount()}");
    if (Mc()?.State != "may-don-cho") Bad("máy con mất máy chủ có chờ gửi phải hiện may-don-cho");
    if (Mc()?.GetVisualDescendants().OfType<Button>().Any(b => KhoLanh.App.Controls.Icon.TextOf(b.Content) == "Nối lại ngay" && b.Classes.Contains("primary")) != true) Bad("thiếu nút Nối lại ngay");
    CheckUnderNav(main, "v213-may-don-cho"); Snap(main, "v213-may-don-cho");

    // --- d) máy đơn dùng bản Drive cá nhân của máy chủ
    s.Settings.Set("lan_offline_src", "MAY-CHU-KHO · sao lưu 26/11 22:00");
    main.Navigate("audit"); P(5); main.Navigate("dongbo"); P(30);
    Console.WriteLine($"[v213] máy đơn dùng bản Drive: {Mc()?.State}");
    if (Mc()?.State != "may-don-drive") Bad("máy đơn dùng bản Drive phải hiện may-don-drive");
    Snap(main, "v213-may-don-drive");
    s.Settings.Set("lan_offline_src", "");

    // --- e) máy đơn chưa bật LAN
    s.Settings.Set("lan_role", ""); s.Settings.Set("lan_serving", ""); s.ReloadLan(start: false);
    main.Navigate("audit"); P(5); main.Navigate("dongbo"); P(30);
    Console.WriteLine($"[v213] máy đơn: {Mc()?.State}");
    if (Mc()?.State != "may-don") Bad("máy đơn phải hiện may-don");
    if (Mc()?.GetVisualDescendants().OfType<Button>().Any(b => KhoLanh.App.Controls.Icon.TextOf(b.Content) == "Bật máy chủ trên máy này") != true) Bad("Quản trị cần nút Bật máy chủ trên máy này");
    CheckUnderNav(main, "v213-may-don"); Snap(main, "v213-may-don");
    if (navBad.Count > 0) ok = false;
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v213) — ĐẠT: " : "Xong (v213) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.119: SHOTS_ONLY=v212 — chép chữ: chuột phải vào chữ thường → menu "Chép chữ này / Chép cả khung"; chữ trong nút không bật menu; hộp thông báo bôi đen được. Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v212")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var ok = true;
    var lv = new KhoLanh.App.Views.LanView();
    main.Show("lan", lv); P(40);
    bool Right(TextBlock t) { var e = new ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent }; t.RaiseEvent(e); P(5); return e.Handled; }
    var plain = main.PageHost.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t is not SelectableTextBlock && (t.Text?.Length ?? 0) > 3 && KhoLanh.App.Controls.CopyText.Eligible(t));
    var inBtn = main.PageHost.GetVisualDescendants().OfType<Button>().SelectMany(b => b.GetVisualDescendants().OfType<TextBlock>()).First(t => (t.Text?.Length ?? 0) > 0);
    var h1 = Right(plain); var h2 = Right(inBtn);
    Console.WriteLine($"[v212] chuột phải chữ «{plain.Text}» → menu chép: {h1} · chữ trong nút «{inBtn.Text}» → không menu: {!h2}");
    if (!h1 || h2) ok = false;
    Snap(main, "v212-chep-chu");
    Window? dlg = null;
    Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => { if (w.Title == "Thông báo thử") dlg = w; }, Avalonia.Interactivity.RoutingStrategies.Direct);
    _ = KhoLanh.App.Ui.Message(main, "Câu báo thử: mã LSX 26.09.020 — bôi đen để chép.", "Thông báo thử"); P(30);
    var sel = dlg?.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t => t.Text?.Contains("26.09.020") == true) == true;
    Console.WriteLine($"[v212] hộp thông báo có chữ bôi đen được: {sel}");
    if (!sel) ok = false;
    dlg?.Close();
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v212) — ĐẠT: " : "Xong (v212) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.118: SHOTS_ONLY=v211 — Máy chủ LAN › Máy đang nối: 2 bảng (trong mạng LAN / qua Internet), có cột Tài khoản. Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v211")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var ok = true;
    System.Text.Json.Nodes.JsonObject Beat(string id, string name, string user) => new()
    { ["k"] = KhoLanh.Core.Lan.LanService.ParseCode(s.Lan.ConnectionCode()).Item2, ["op"] = "beat", ["m"] = id, ["mn"] = name, ["r"] = KhoLanh.Core.Lan.LanService.RoleClient, ["u"] = user, ["pend"] = 0,
      ["a"] = new System.Text.Json.Nodes.JsonArray("192.168.1.20:47820") };
    s.Lan.Handle(Beat("m-kho1", "MAY-KHO-1", "thang"));
    s.Lan.Handle(Beat("m-kho2", "MAY-KETOAN", "lan"));
    s.Lan.Handle(Beat("m-nha", "LAPTOP-NHA", "khoa"), "", netIp: "113.161.40.12");
    var lv = new KhoLanh.App.Views.LanView();
    main.Show("lan", lv); P(40);
    var g1 = lv.FindControl<DataGrid>("MachinesGrid")!; var g2 = lv.FindControl<DataGrid>("NetMachinesGrid")!;
    int N(DataGrid g) => (g.ItemsSource as System.Collections.IEnumerable)?.Cast<object>().Count() ?? 0;
    var t2 = lv.FindControl<TextBlock>("NetMachinesTitle")!.Text;
    Console.WriteLine($"[v211] LAN: {N(g1)} máy · Internet: {N(g2)} máy · {t2}");
    if (N(g1) != 2 || N(g2) != 1 || t2?.Contains("1 máy") != true) ok = false;
    Snap(main, "v211-may-dang-noi-2-loai");
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v211) — ĐẠT: " : "Xong (v211) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.117: SHOTS_ONLY=v210 — Nhập liệu › Danh sách trang: viền từng dòng + ô tích / chọn tất cả; Mẫu: cột hệ thống (Size, Đặc tính…) khóa cứng, chỉ đổi tên. Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v210")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var ok = true;
    var kk = KhoLanh.Core.Entry.EntryService.BuiltIn.First(t => t.Id == "kiem-ke");
    foreach (var n in new[] { "Kho 2 E dãy 01 25-11-2026", "Kho 2 E dãy 02 25-11-2026", "Kho 2 E dãy 03 25-11-2026", "Kiểm kê 25/11/2026" }) s.Entry.Create(n, kk);
    main.Show("m6", new KhoLanh.App.Views.EntryView()); P(40);
    var ev = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.EntryView>().First();
    var tabs = ev.GetVisualDescendants().OfType<TabControl>().First(t => t.Name == "Tabs");
    tabs.SelectedIndex = 1; P(30);
    var list = ev.GetVisualDescendants().OfType<ListBox>().First(l => l.Name == "PageList");
    var items = list.ItemsSource!.Cast<KhoLanh.App.Views.EntryView.Item>().ToList();
    items[1].Checked = true; items[2].Checked = true; P(5);
    var rowChecks = list.GetVisualDescendants().OfType<CheckBox>().Where(c => c.IsEffectivelyVisible).ToList();
    rowChecks.First(c => c.IsChecked == true).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); P(5);
    var all = ev.GetVisualDescendants().OfType<CheckBox>().First(c => c.Name == "AllCheck");
    var del = ev.GetVisualDescendants().OfType<Button>().First(b => b.Name == "DeleteButton");
    Console.WriteLine($"[v210] ô tích dòng: {rowChecks.Count} · chọn tất cả = {all.IsChecked?.ToString() ?? "một phần"} · nút: {del.Content}");
    if (rowChecks.Count < items.Count || all.IsChecked is not null || del.Content?.ToString() != "Xóa 2 trang") ok = false;
    var lbi = list.GetVisualDescendants().OfType<ListBoxItem>().First();
    Console.WriteLine($"[v210] viền dòng: {lbi.BorderThickness}");
    if (lbi.BorderThickness.Bottom < 1) ok = false;
    Snap(main, "v210-danh-sach-tich");
    all.IsChecked = true; all.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); P(5);
    Console.WriteLine($"[v210] chọn tất cả: {items.Count(i => i.Checked)}/{items.Count} · nút: {del.Content}");
    if (items.Any(i => !i.Checked)) ok = false;

    tabs.SelectedIndex = 2; P(30);
    var mauList = ev.GetVisualDescendants().OfType<ListBox>().First(l => l.IsEffectivelyVisible && l.Name != "PageList");
    mauList.SelectedItem = mauList.Items.OfType<ListBoxItem>().First(i => i.Tag is KhoLanh.Core.Entry.EntryService.Template { Id: "kiem-ke" }); P(20);
    var mg = ev.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(g => g.IsEffectivelyVisible);
    var mm = mg.Model;
    int Row(string h) => Enumerable.Range(0, mm.RowCount).First(r => mm.Get(r, 0) == h);
    var size = Row("Size");
    mm.SetCells(new[] { (size, 3, ""), (size, 1, "Số") }); P(3);
    mm.DeleteRows(Row("Đặc tính"), Row("Đặc tính")); P(3);
    mm.Set(size, 0, "Size hàng"); P(3);
    var kept = mm.Get(size, 3); var stillDt = Enumerable.Range(0, mm.RowCount).Any(r => mm.Get(r, 0) == "Đặc tính");
    Console.WriteLine($"[v210] Size: tự điền = {kept} · kiểu = {mm.Get(size, 1)} · tên = {mm.Get(size, 0)} · Đặc tính còn = {stillDt}");
    if (kept.Length == 0 || mm.Get(size, 1) != "Chữ" || mm.Get(size, 0) != "Size hàng" || !stillDt) ok = false;
    mg.Select(size, 3); P(3); mg.BeginEdit(null, force: true); P(5);
    var ed = mg.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsVisible);
    Console.WriteLine($"[v210] nhấn đúp ô Tự điền hệ thống: mở sửa = {ed is not null}");
    if (ed is not null) ok = false;
    Snap(main, "v210-mau-cot-he-thong");
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v210) — ĐẠT: " : "Xong (v210) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.116: SHOTS_ONLY=v209 — Nhập liệu kiểm kê: gõ lô "22.05 L16" → "22.05.<năm> L16"; ô tự điền (Ngày nhập) khóa tạm: gõ không sửa, nhấn đúp mới sửa. Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v209")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var ok = true;
    s.Entry.Create("Kiểm kê khóa tạm", KhoLanh.Core.Entry.EntryService.BuiltIn.First(t => t.Id == "kiem-ke"));
    main.Show("m6", new KhoLanh.App.Views.EntryView()); P(40);
    var ev = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.EntryView>().First();
    var combo = ev.GetVisualDescendants().OfType<ComboBox>().First(c => c.Name == "PageCombo");
    combo.SelectedItem = combo.ItemsSource!.Cast<object>().First(i => i.ToString()!.Contains("khóa tạm")); P(30);
    var grid = ev.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First();
    var m = grid.Model;
    int Col(string h) => Enumerable.Range(0, m.ColCount).First(c => m.Columns[c].Header == h);
    m.SetCells(new[] { (0, Col("Vị trí"), "TG1.A.01.1.1"), (0, Col("Mã hàng"), "VRHLBTNXECR00162IMA1"), (0, Col("Số lượng"), "5") }); P(5);
    m.Set(0, Col("Lô"), "22.05 L16"); P(5);
    var lo = m.Get(0, Col("Lô")); var ngay = m.Get(0, Col("Ngày nhập"));
    Console.WriteLine($"[v209] lô: {lo} · ngày nhập tự điền: {ngay}");
    if (lo != $"22.05.{(new DateTime(DateTime.Today.Year, 5, 22) > DateTime.Today ? DateTime.Today.Year - 1 : DateTime.Today.Year)} L16" || ngay.Length == 0) ok = false;
    grid.Select(0, Col("Ngày nhập")); P(5);
    grid.BeginEdit("x"); P(5);                       // như gõ phím
    var editor = grid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsVisible);
    var status = ev.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "StatusText").Text ?? "";
    Console.WriteLine($"[v209] gõ vào ô tự điền: mở sửa = {editor is not null} · {status}");
    if (editor is not null || !status.Contains("nhấn đúp")) ok = false;
    Snap(main, "v209-o-tu-dien-khoa-tam");
    grid.BeginEdit(null, force: true); P(5);           // như nhấn đúp
    editor = grid.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.IsVisible);
    Console.WriteLine($"[v209] nhấn đúp: mở sửa = {editor is not null} · nội dung {editor?.Text}");
    if (editor is null || editor.Text != ngay) ok = false;
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v209) — ĐẠT: " : "Xong (v209) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.115: SHOTS_ONLY=v208 — Nhập liệu: mẫu Kiểm kê có Lô · Đặc tính trước Ghi chú; nút "Đóng trang" đóng trang đang mở (không tự mở trang khác). Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v208")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var ok = true;
    var pg = s.Entry.Create("Kiểm kê thử 26-11-2026", KhoLanh.Core.Entry.EntryService.BuiltIn.First(t => t.Id == "kiem-ke"));
    var heads = string.Join(" | ", pg.Columns.Select(c => c.Header));
    Console.WriteLine("[v208] cột: " + heads);
    if (!heads.Contains("Tổng Net(kg) | Lô | Đặc tính | Ghi chú")) ok = false;
    main.Show("m6", new KhoLanh.App.Views.EntryView()); P(40);
    var ev = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.EntryView>().First();
    var combo = ev.GetVisualDescendants().OfType<ComboBox>().First(c => c.Name == "PageCombo");
    var item = combo.ItemsSource!.Cast<object>().FirstOrDefault(i => i.ToString()!.Contains("Kiểm kê thử"));
    if (item is not null) { combo.SelectedItem = item; P(30); }
    var close = ev.GetVisualDescendants().OfType<Button>().First(b => b.Name == "CloseButton");
    var grid = ev.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First();
    Console.WriteLine($"[v208] trang mở: lưới hiện {grid.IsVisible} · nút Đóng trang hiện {close.IsVisible}");
    if (!grid.IsVisible || !close.IsVisible) ok = false;
    Snap(main, "v208-kiem-ke-lo-dac-tinh");
    close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); P(30);
    var empty = ev.GetVisualDescendants().OfType<TextBlock>().First(t => t.Name == "EmptyText");
    Console.WriteLine($"[v208] sau Đóng trang: lưới hiện {grid.IsVisible} · ô trang = {combo.SelectedItem?.ToString() ?? "(trống)"} · {empty.Text}");
    if (grid.IsVisible || combo.SelectedItem is not null || !empty.IsVisible || close.IsVisible) ok = false;
    Snap(main, "v208-dong-trang");
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v208) — ĐẠT: " : "Xong (v208) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.114: SHOTS_ONLY=v207 — khôi phục 2 chế độ: máy đã tách riêng thấy nút "Nối lại mạng LAN" (Giữa các máy › Chỉ máy này); máy chủ LAN bị khóa khôi phục. Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v207")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var ok = true;
    s.Settings.Set("lan_role", ""); s.Settings.Set("lan_serving", ""); s.ReloadLan(start: false);   // như máy vừa khôi phục ngoài LAN: tách riêng
    s.Settings.Set(KhoLanh.Core.Services.BackupService.DetachedKey, "1");
    main.Navigate("lan"); P(40);
    var cards = main.PageHost.GetVisualDescendants().OfType<Button>().ToList();
    var choose = main.PageHost.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "Chỉ máy này");
    if (choose?.GetVisualAncestors().OfType<Button>().FirstOrDefault() is { } cb) { cb.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); P(30); }
    else if (choose?.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Cursor is not null) is { } bb)
    {
        var pt = bb.TranslatePoint(new Avalonia.Point(20, 10), main)!.Value;
        main.MouseDown(pt, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None); main.MouseUp(pt, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None); P(30);
    }
    var rejoin = main.PageHost.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => KhoLanh.App.Controls.Icon.TextOf(b.Content) == "Nối lại mạng LAN" && b.IsEffectivelyVisible);
    Console.WriteLine($"[v207] nút Nối lại mạng LAN: {rejoin is not null}");
    if (rejoin is null) ok = false;
    Snap(main, "v207-noi-lai-lan");
    s.Settings.Set(KhoLanh.Core.Services.BackupService.DetachedKey, "");
    int Free() { var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); l.Start(); var p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
    KhoLanh.Core.Lan.LanService.Configure(s.Settings, s.Session, KhoLanh.Core.Lan.LanService.RoleServer, null, Free());
    s.ReloadLan(start: true);
    for (var i = 0; i < 100 && !s.Lan.Serving; i++) P(5);
    var lockMsg = KhoLanh.Core.AppServices.RestoreLock(s.Lan) ?? "";
    Console.WriteLine($"[v207] máy chủ: {s.Lan.Serving} · khóa: {lockMsg}");
    if (!lockMsg.Contains("MÁY CHỦ")) ok = false;
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v207) — ĐẠT: " : "Xong (v207) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.113: SHOTS_ONLY=v204 — Máy chủ từ xa (Cloudflare Tunnel): ô "Địa chỉ từ xa" ở Đồng bộ & sao lưu › Giữa các máy › Máy chủ LAN, mã LAN chứa địa chỉ,
// "Kiểm tra nối từ xa" qua HTTP thật (127.0.0.1 thay cho Cloudflare). Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v204")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var ok = true;
    int Free() { var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0); l.Start(); var p = ((System.Net.IPEndPoint)l.LocalEndpoint).Port; l.Stop(); return p; }
    var tcp = Free(); var hp = Free();
    s.Settings.Set("lan_http_port", hp.ToString());
    KhoLanh.Core.Lan.LanService.Configure(s.Settings, s.Session, KhoLanh.Core.Lan.LanService.RoleServer, null, tcp);
    KhoLanh.Core.Lan.LanService.SetPublicUrl(s.Settings, $"http://127.0.0.1:{hp}");
    s.ReloadLan(start: true);
    for (var i = 0; i < 100 && !s.Lan.Serving; i++) P(5);
    var test = "";
    // chặn luồng giao diện như Trợ lý quét nền: máy chủ đường hầm phải trả lời được (không bám luồng giao diện)
    try { test = Task.Run(() => s.Lan.TestPublicAsync()).GetAwaiter().GetResult(); } catch (Exception ex) { test = "LỖI " + ex.Message; }
    var (addrs, _) = KhoLanh.Core.Lan.LanService.ParseCode(s.Lan.ConnectionCode());
    Console.WriteLine($"[v204] máy chủ: {s.Lan.Serving} · mã LAN: {string.Join(" ; ", addrs)} · tự thử: {test}");
    if (!test.Contains("đang làm máy chủ") || !addrs.Contains($"http://127.0.0.1:{hp}")) ok = false;
    KhoLanh.Core.Lan.LanService.SetPublicUrl(s.Settings, "kho.kcenter.xyz");
    main.Show("lan", new KhoLanh.App.Views.LanView()); P(40);   // 1.121: khung cũ chỉ còn trong cửa sổ «Mở danh sách đầy đủ» (tab Máy chủ có hộp Địa chỉ Internet)
    var inner = main.PageHost.GetVisualDescendants().OfType<TabControl>().FirstOrDefault(t => t.Items.OfType<TabItem>().Any(i => KhoLanh.App.Controls.Icon.TextOf(i.Header) == "Máy chủ LAN"));
    if (inner is not null) { inner.SelectedItem = inner.Items.OfType<TabItem>().First(i => KhoLanh.App.Controls.Icon.TextOf(i.Header) == "Máy chủ LAN"); P(30); }
    var lv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.LanView>().FirstOrDefault();
    var box = lv?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "RemoteUrlBox");
    var card = lv?.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "RemoteCard");
    Console.WriteLine($"[v204] khung Máy chủ từ xa: {card?.IsEffectivelyVisible} · ô địa chỉ: {box?.Text}");
    if (card?.IsEffectivelyVisible != true || box?.Text != "https://kho.kcenter.xyz") ok = false;
    Snap(main, "v204-may-chu-tu-xa");
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v204) — ĐẠT: " : "Xong (v204) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.102: SHOTS_ONLY=v202 — Trợ lý giai đoạn 4: nút "Sửa nhanh" ở khung chi tiết (sao lưu ngay → quét lại hết cảnh báo), thông báo góc màn hình cho cảnh báo Nặng mới. Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v202")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var ok = true;
    var sl = Path.Combine(KhoLanh.App.App.DataFolder, "SaoLuu"); Directory.CreateDirectory(sl); foreach (var f in Directory.GetFiles(sl)) File.Delete(f);
    var r0 = s.TroLy.Scan(false);
    var fx = r0.Findings.Where(f => f.Fix.Length > 0).ToList();
    Console.WriteLine("[v202] có Sửa nhanh: " + string.Join(" · ", fx.Select(f => f.Title + " → " + f.FixLabel)));
    main.Navigate("troly"); P(40);
    var tv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.TroLyView>().First();
    var row = tv.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text == "Chưa có bản sao lưu");
    var hit = row?.GetVisualAncestors().OfType<Border>().FirstOrDefault(b => b.Cursor is not null) as Control ?? row;
    if (hit is not null)
    {
        var pt = hit.TranslatePoint(new Avalonia.Point(hit.Bounds.Width - 20, hit.Bounds.Height / 2), main)!.Value;
        main.MouseMove(pt, Avalonia.Input.RawInputModifiers.None); main.MouseDown(pt, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None); main.MouseUp(pt, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None); P(20);
    }
    var btn = tv.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => KhoLanh.App.Controls.Icon.TextOf(b.Content)?.StartsWith("⚡ Sửa nhanh") == true || (KhoLanh.App.Controls.Icon.TextOf(b.Content) ?? "").Contains("Sửa nhanh"));
    Console.WriteLine($"[v202] dòng Chưa có bản sao lưu: {row is not null} · nút: {(btn is null ? "(không)" : KhoLanh.App.Controls.Icon.TextOf(btn.Content))}");
    Snap(main, "v202-sua-nhanh");
    if (btn is null) ok = false;
    else
    {
        btn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); P(120);
        var still = s.TroLy.Last!.Findings.Any(f => f.Title == "Chưa có bản sao lưu");
        var n = Directory.GetFiles(Path.Combine(KhoLanh.App.App.DataFolder, "SaoLuu")).Length;
        Console.WriteLine($"[v202] sau Sửa nhanh: {n} bản sao lưu · còn cảnh báo: {still} · thông báo: {KhoLanh.App.Ui.LastNotify}");
        if (n == 0 || still) ok = false;
        Snap(main, "v202-sau-sua-nhanh");
    }
    // thông báo cảnh báo Nặng mới
    var res = new KhoLanh.Core.TroLy.TroLyService.ScanResult { At = DateTime.Now, Do = 1 };
    var hot = KhoLanh.Core.TroLy.TroLyChecks.Mk("csdl", "do", "CSDL", "Kiểm tra toàn vẹn database THẤT BẠI", "x"); hot.Q = "moi";
    res.Findings.Add(hot);
    typeof(MainWindow).GetMethod("NotifyFindings", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(main, new object[] { res }); P(30);
    var cards = main.GetVisualDescendants().OfType<Avalonia.Controls.Notifications.NotificationCard>().Where(c => c.IsVisible).ToList();
    var txt = string.Join(" | ", cards.SelectMany(c => c.GetVisualDescendants().OfType<TextBlock>()).Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)));
    Console.WriteLine($"[v202] thông báo Nặng: {cards.Count} · {txt}");
    if (!txt.Contains("Kiểm tra toàn vẹn database")) ok = false;
    Snap(main, "v202-thong-bao-nang");
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v202) — ĐẠT: " : "Xong (v202) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v200")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var st = new KhoLanh.Core.TroLy.PerfStore(Path.Combine(KhoLanh.App.App.DataFolder, KhoLanh.Core.TroLy.PerfStore.FileName));
    var now = DateTime.Now; var rnd = new Random(7);
    for (var i = 24 * 60; i >= 0; i -= 2)
    {
        var t = now.AddMinutes(-i); var h = t.Hour + t.Minute / 60.0;
        var work = h is >= 7.5 and <= 17 ? 1.0 : 0.25;
        var cpu = Math.Clamp(12 + 30 * work + 18 * Math.Sin(i / 37.0) * work + rnd.NextDouble() * 10 + (i is > 300 and < 330 ? 45 : 0), 2, 99);
        st.Add(new KhoLanh.Core.TroLy.PerfStore.Sample(t, Math.Round(cpu, 1), Math.Round(cpu * 0.22 + rnd.NextDouble() * 2, 1), Math.Round(58 + 14 * work + rnd.NextDouble() * 4, 1),
            Math.Round(380 + 120 * work, 1), 41.3, 237.9, i % 10 == 0 ? 8 + rnd.NextDouble() * 6 : null, i % 10 == 0 ? 85 + rnd.NextDouble() * 60 : null));
    }
    foreach (var (k, ms) in new[] { ("mo-app", 3400.0), ("mo-app", 2900), ("mo-app", 3100), ("dang-nhap", 1450), ("dang-nhap", 980), ("dong-bo-lan", 420), ("dong-bo-lan", 610), ("dong-bo-drive", 5200), ("dong-bo-dien-thoai", 2700) })
        st.AddMark(k, ms, now.AddMinutes(-rnd.Next(30, 3000)));
    KhoLanh.Core.TroLy.Perf.Record("Mở module", "Tồn kho An An", 4800, 0); KhoLanh.Core.TroLy.Perf.Record("Mở module", "Tồn kho An An", 3600, 0);
    KhoLanh.Core.TroLy.Perf.Record("Đọc file Excel", "Tồn theo vị trí (chi tiết)", 9200, 55000);
    KhoLanh.Core.TroLy.Perf.Record("Đồng bộ thư mục / Drive", "", 12600, 0);
    KhoLanh.Core.TroLy.Perf.Record("Lưu trang Nhập liệu", "Kiểm kê TG1", 2300, 327);
    main.Navigate("troly"); P(40);
    var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(t => t.Items.OfType<TabItem>().Any(i => KhoLanh.App.Controls.Icon.TextOf(i.Header) == "Hiệu năng"));
    tc.SelectedItem = tc.Items.OfType<TabItem>().First(i => KhoLanh.App.Controls.Icon.TextOf(i.Header) == "Hiệu năng"); P(40);
    var hv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.HieuNangView>().First();
    hv.Refresh(); P(20);
    var kpis = hv.GetVisualDescendants().OfType<Border>().Count(b => b.Width == 250);
    var chart = hv.GetVisualDescendants().OfType<KhoLanh.App.Controls.PerfChart>().First();
    var bars = hv.GetVisualDescendants().OfType<TextBlock>().Count(t => t.Text?.Contains(" lần") == true);
    Console.WriteLine($"[v200] thẻ số {kpis} · biểu đồ {chart.Bounds.Width:0}×{chart.Bounds.Height:0} · dòng có số lần {bars} · mẫu {st.Count()}");
    Snap(main, "v200-hieu-nang");
    KhoLanh.App.AppTheme.Apply(KhoLanh.App.AppTheme.Mode.Dark, 1.0); hv.Refresh(); P(30); Snap(main, "v200-hieu-nang-toi");
    KhoLanh.App.AppTheme.Apply(KhoLanh.App.AppTheme.Mode.Light, 1.0); P(10);
    var ok = kpis == 4 && chart.Bounds.Height > 200 && bars >= 4;
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v200) — ĐẠT: " : "Xong (v200) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.99: SHOTS_ONLY=v199 — xuất file xong không tự mở: chỉ hiện thông báo "Đã lưu file" góc phải dưới (bấm = mở thư mục). Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v199")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    main.Navigate("m1"); P(30);
    var f = Path.Combine(outDir, "Bao-cao-tong-ton.xlsx"); File.WriteAllText(f, "x");
    KhoLanh.App.Ui.Exported(main, f); P(20);
    var mgrs = main.GetVisualDescendants().OfType<Avalonia.Controls.Notifications.WindowNotificationManager>().ToList();
    Console.WriteLine($"[v199] manager: {mgrs.Count} · {string.Join(",", mgrs.Select(m => m.Bounds.ToString()))}");
    var cards = main.GetVisualDescendants().OfType<Avalonia.Controls.Notifications.NotificationCard>().Where(c => c.IsVisible).ToList();
    var txt = string.Join(" | ", cards.SelectMany(c => c.GetVisualDescendants().OfType<TextBlock>()).Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)));
    Console.WriteLine($"[v199] thông báo: {cards.Count} · {txt}");
    Snap(main, "v199-da-luu-file");
    var ok = cards.Count == 1 && txt.Contains("Bao-cao-tong-ton.xlsx");
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v199) — ĐẠT: " : "Xong (v199) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.96: SHOTS_ONLY=v196 — Phiếu xuất kho: tab Phiếu xuất nội địa đứng thứ 2 (mở theo số tab cũ vẫn đúng); Tồn kho An An › Báo Cáo: bấm dòng Bột + Sốt + Bếp / Tồn Kho Gửi → Tổng Hợp › Báo cáo ngoài sổ sách. Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v196")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var ok = true;
    string Heads(TabControl t) => string.Join(" | ", t.Items.OfType<TabItem>().Select(i => KhoLanh.App.Controls.Icon.TextOf(i.Header)));
    main.Show("m13", new PhieuKhoView(PhieuKhoView.TabNd)); P(40);
    var pt = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
    Console.WriteLine($"[v196] Phiếu xuất kho: {Heads(pt)} · mở tab Nội địa → đang ở “{KhoLanh.App.Controls.Icon.TextOf(((TabItem)pt.SelectedItem!).Header)}”");
    if (!Heads(pt).StartsWith("Phiếu xuất khẩu (theo LSX) | Phiếu xuất nội địa | List sản xuất") || pt.SelectedIndex != 1) ok = false;
    Snap(main, "30d-phieu-xuat-tab-noi-dia-thu-2");
    main.Show("m13", new PhieuKhoView(PhieuKhoView.TabLsx)); P(30);
    pt = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
    if (pt.SelectedIndex != 2) { ok = false; Console.WriteLine("[v196] mở tab List sản xuất sai: " + pt.SelectedIndex); }
    main.Navigate("m1"); P(40);
    var at = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(); at.SelectedIndex = 1; P(30);
    var bg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible && x.Model.ColCount == 2 && x.Model.Columns[0].Header == "Tên hàng");
    var rows = Enumerable.Range(0, bg.Model.RowCount).Select(r => bg.Model.Get(r, 0)).ToList();
    Console.WriteLine("[v196] Báo cáo tổng tồn: " + string.Join(" · ", rows.Where(x => x.Contains('↗'))));
    Snap(main, "40e-an-an-bao-cao-lien-ket");
    var rKg = rows.FindIndex(x => x.StartsWith("Tồn Kho Gửi"));
    bg.RowClicked!(rKg); P(20);
    var inner = main.PageHost.GetVisualDescendants().OfType<TabControl>().FirstOrDefault(t => t != at && t.IsEffectivelyVisible && t.Items.OfType<TabItem>().Any(i => KhoLanh.App.Controls.Icon.TextOf(i.Header) == "Báo cáo ngoài sổ sách"));
    var foc = main.FocusManager?.GetFocusedElement() as TextBox;
    Console.WriteLine($"[v196] bấm dòng Tồn Kho Gửi → tab {at.SelectedIndex} / {at.ItemCount - 1}, tab con {(inner is null ? "?" : KhoLanh.App.Controls.Icon.TextOf(((TabItem)inner.SelectedItem!).Header))}, ô đang chọn = {foc?.Text ?? "(không)"}");
    if (at.SelectedIndex != at.ItemCount - 1 || inner is null || inner.SelectedIndex != 2 || rows.Count(x => x.Contains('↗')) != 2) ok = false;
    Snap(main, "40f-an-an-ngoai-so-sach");
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v196) — ĐẠT: " : "Xong (v196) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.94: SHOTS_ONLY=v194 — bấm chuột thật ở Nhập liệu: tiêu đề cột = bôi đen cả cột (không sắp xếp), số dòng = bôi đen cả dòng, Ctrl+C chép cả dòng; bảng Nhập liệu không ô tích. Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v194")
{
    void P(int n = 10) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var T = typeof(KhoLanh.App.Controls.SheetGrid);
    var ok = true;
    main.Navigate("m6"); P(40);
    var g = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible && x.Model.RowCount > 3);
    var tick = (bool)T.GetProperty("HasCheck", bf)!.GetValue(g)!;
    double topH = (double)T.GetProperty("TopH", bf)!.GetValue(g)!, rowH = (double)T.GetProperty("RowH", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
    double numW = (double)T.GetProperty("NumW", bf)!.GetValue(g)!;
    var surf = g.GetVisualDescendants().OfType<Control>().First(x => x.GetType().Name == "GridSurface");
    Avalonia.Point At(double x, double y) => surf.TranslatePoint(new Avalonia.Point(x, y), main)!.Value;
    void Click(Avalonia.Point p) { main.MouseMove(p, Avalonia.Input.RawInputModifiers.None); main.MouseDown(p, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None); main.MouseUp(p, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None); P(5); }
    // 1) tiêu đề cột 2 (Mã hàng)
    var c1 = g.Model.Columns[0].Width; var before0 = g.Model.Get(0, 1);
    Click(At(numW + c1 + 30, 12));
    var sel = g.Selection; var sortCol = (int)T.GetProperty("SortCol", bf)!.GetValue(g)!;
    Console.WriteLine($"[v194] ô tích ở Nhập liệu: {tick} · bấm tiêu đề cột 2 → chọn dòng {sel.R1}..{sel.R2} cột {sel.C1}..{sel.C2} / {g.Model.RowCount} dòng · sắp xếp cột {sortCol}");
    if (tick || sel.C1 != 1 || sel.C2 != 1 || sel.R1 != 0 || sel.R2 < g.Model.RowCount - 1 || sortCol >= 0) ok = false;
    Snap(main, "20f-nhap-lieu-chon-cot");
    // 2) số dòng 3
    Click(At(10, topH + rowH * 2.5));
    sel = g.Selection;
    Console.WriteLine($"[v194] bấm số dòng 3 → chọn dòng {sel.R1}..{sel.R2} cột {sel.C1}..{sel.C2} / {g.Model.ColCount} cột");
    if (sel.R1 != 2 || sel.R2 != 2 || sel.C1 != 0 || sel.C2 != g.Model.ColCount - 1) ok = false;
    var txt = (string)T.GetMethod("CopyTextOf", bf)!.Invoke(g, new object[] { T.GetProperty("VSel", bf)!.GetValue(g)! })!;
    Console.WriteLine($"[v194] chép: “{txt.TrimEnd()}”");
    if (!txt.Contains(g.Model.Get(2, 0)) || !txt.Contains(g.Model.Get(2, 1)) || txt.Split('\t').Length < 3) ok = false;
    Snap(main, "20g-nhap-lieu-chon-dong");
    // 4) bấm đúp viền phải tiêu đề cột → vừa dữ liệu (Mã hàng đang cắt chữ → rộng ra; Ghi chú thừa chỗ → hẹp lại)
    void DblBorder(int col)
    {
        double x = numW; for (var i = 0; i <= col; i++) x += g.Model.Columns[i].Width;
        var p = At(x, 12); main.MouseMove(p, Avalonia.Input.RawInputModifiers.None);
        for (var k = 0; k < 2; k++) { main.MouseDown(p, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None); main.MouseUp(p, Avalonia.Input.MouseButton.Left, Avalonia.Input.RawInputModifiers.None); }
        P(5);
    }
    int ColOf(string h) => Enumerable.Range(0, g.Model.ColCount).First(i => g.Model.Columns[i].Header == h);
    int cMa = ColOf("Mã hàng"), cGc = ColOf("Ghi chú");
    double ma0 = g.Model.Columns[cMa].Width, gc0 = g.Model.Columns[cGc].Width;
    DblBorder(cMa); DblBorder(cGc);
    double ma1 = g.Model.Columns[cMa].Width, gc1 = g.Model.Columns[cGc].Width;
    Console.WriteLine($"[v194] bấm đúp viền cột: Mã hàng {ma0:0} → {ma1:0} px · Ghi chú {gc0:0} → {gc1:0} px");
    if (ma1 <= ma0 || gc1 >= gc0) ok = false;
    g.Select(0, 0); Snap(main, "20h-nhap-lieu-co-gian-cot");
    // 3) sắp xếp vẫn được qua SetSort (nút ▾ / chuột phải)
    g.SetSort(1, true); P(3);
    Console.WriteLine($"[v194] sắp xếp Z→A cột Mã hàng: dòng đầu đang hiện = {g.Model.Get((int)T.GetMethod("ToModel", bf)!.Invoke(g, new object[] { 0 })!, 1)}");
    g.ClearSort();
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v194) — ĐẠT: " : "Xong (v194) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.93: SHOTS_ONLY=v193 — ô tích chọn dòng: In chứng từ tích ngày in (ô tiêu đề = tích hết), bảng sửa được có ô tích, bảng chỉ xem không. Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v193")
{
    void P(int n = 20) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    bool Tick(KhoLanh.App.Controls.SheetGrid g) => (bool)typeof(KhoLanh.App.Controls.SheetGrid).GetProperty("HasCheck", bf)!.GetValue(g)!;
    var ok = true;
    main.Navigate("m12"); P(40);
    var gg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible && x.Model.ColCount > 0 && x.Model.Columns[0].Header == "Số phiếu");
    var view = main.PageHost.Content!;
    int Sel() => ((System.Collections.IEnumerable)view.GetType().GetMethod("Selected", bf)!.Invoke(view, null)!).Cast<object>().Count();
    gg.SetChecked(new[] { 0, 2 }, true); P(5);
    var info = main.PageHost.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.Text?.StartsWith("Đã tích") == true)?.Text ?? "";
    Console.WriteLine($"[v193] In chứng từ: ô tích = {Tick(gg)}, {gg.Model.RowCount} ngày, tích 2 → in / gộp {Sel()} phiếu · “{info}”");
    if (!Tick(gg) || Sel() != 2 || !info.Contains("Đã tích 2 ngày")) ok = false;
    Snap(main, "29b-in-chung-tu-tich-ngay");
    gg.SetChecked(Enumerable.Range(0, gg.Model.RowCount), true); P(3);
    Console.WriteLine($"[v193] tích hết → {Sel()} / {gg.Model.RowCount}");
    if (Sel() != gg.Model.RowCount) ok = false;
    gg.SetChecked(gg.CheckedRows, false); P(3);
    Console.WriteLine($"[v193] bỏ tích → theo dòng đang chọn: {Sel()}");
    if (Sel() != 1) ok = false;
    foreach (var (k, wantEdit) in new[] { ("m6", false), ("m4", true), ("m13", false) })   // 1.94: Nhập liệu tắt ô tích (RowChecks = false)
    {
        main.Navigate(k); P(40);
        var grids = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().Where(x => x.IsEffectivelyVisible && x.Model.ColCount > 0).ToList();
        var bad = grids.Where(x => Tick(x) == x.Model.ReadOnly && x.CheckCol < 0 && x.RowChecks is null).ToList();
        Console.WriteLine($"[v193] {k}: {grids.Count} bảng · " + string.Join(" · ", grids.Select(x => $"{(x.Model.ReadOnly ? "chỉ xem" : "sửa được")} ô tích={Tick(x)}")));
        if (bad.Count > 0 || (wantEdit && !grids.Any(x => !x.Model.ReadOnly && Tick(x)))) ok = false;
        if (k == "m6") Snap(main, "20d-nhap-lieu-o-tich");
    }
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v193) — ĐẠT: " : "Xong (v193) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.83: SHOTS_ONLY=v183 — header gọn: 1 thanh 44px + hàng tên trang 36px có tab cấp 1 (1366×768). Thoát mã 1 nếu hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v183")
{
    void P(int n = 30) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var hw = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal }; hw.Show(); P();
    var ok = true;
    var navSv = hw.NavPanel.Parent as Control;
    Console.WriteLine($"[v183] menu rộng {hw.NavPanel.Bounds.Width:0} / chỗ {navSv?.Bounds.Width:0}");
    if (navSv is not null && hw.NavPanel.Bounds.Width > navSv.Bounds.Width + 1) ok = false;
    foreach (var (k, n, tabs) in new[] { ("m4", "v183-ra-dong", true), ("m13", "v183-phieu-xuat", true), ("m1", "v183-an-an", true), ("m2", "v183-gui", true),
        ("m7", "v183-vi-tri", false), ("m8", "v183-so-do", false), ("lx", "v183-tong-quan", false), ("m6", "v183-nhap-lieu", false), ("m9", "v183-kiem-ke", false), ("m10", "v183-khang-sinh", false), ("users", "v183-tai-khoan", false), ("m3sk", "v183-nxt", false) })
    {
        hw.Navigate(k); P(40);
        var tc = hw.PageTabs;
        var y = tc is null ? -1 : tc.TranslatePoint(new Avalonia.Point(0, 0), hw)?.Y ?? -1;
        var content = hw.PageHost.TranslatePoint(new Avalonia.Point(0, 0), hw)?.Y ?? -1;
        Console.WriteLine($"[v183] {k}: tab chung hàng = {(tc is null ? "không" : $"có ({tc.ItemCount} tab, y={y:0})")} · nội dung từ y={content:0}");
        if (tc is not null) Console.WriteLine("        tab: " + string.Join(" ", tc.Items.OfType<TabItem>().Select(t => $"{t.Bounds.Height:0}@{t.TranslatePoint(new Avalonia.Point(0, 0), hw)?.Y:0}")) + $" · cls={string.Join(",", tc.Classes)} · host margin={hw.PageHost.Margin}");
        if (tabs && (tc is null || y > 45)) ok = false;
        if (tc is not null && tc.Items.OfType<TabItem>().Any(t => t.IsVisible && (t.TranslatePoint(new Avalonia.Point(0, 0), hw)?.Y ?? 99) > 46)) { Console.WriteLine("        HỎNG: tab không nằm trên hàng tên trang"); ok = false; }
        P(20); hw.InvalidateMeasure(); hw.InvalidateVisual(); P(10);
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); P(5);
        Snap(hw, n);
    }
    hw.Close(); s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v183) — ĐẠT: " : "Xong (v183) — HỎNG: ") + outDir); if (!ok) Environment.Exit(1); return;
}
// 1.84: SHOTS_ONLY=v184 — mẫu chuẩn module (thanh công cụ 1 hàng, tab con = nút gạt kéo vào thanh, bảng ≥ 8 dòng) ở 1366×768.
// V184_KEYS=m4,m12 để chụp riêng. In số dòng bảng lớn nhất thấy được; dưới 8 dòng → thoát mã 1.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v184")
{
    void P(int n = 30) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var hw = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal }; hw.Show(); P();
    var ok = true;
    var only = (Environment.GetEnvironmentVariable("V184_KEYS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet();
    var scenes = new List<(string Key, string Name, int[] Path)>
    {
        ("m4", "v184-ra-dong", new int[0]), ("m4", "v184-ra-dong-da-luu", new[] { 1 }),
        ("m12", "v184-chung-tu", new int[0]), ("m12", "v184-chung-tu-bang", new[] { -1, 1 }),
        ("m9", "v184-kiem-ke", new[] { 1 }), ("m9", "v184-kiem-ke-ke-toan", new[] { 3 }),
        ("m1", "v184-an-an", new int[0]), ("m1", "v184-an-an-bao-cao", new[] { 1 }), ("m1", "v184-an-an-tong-hop", new[] { 5 }),
        ("m2", "v184-gui", new int[0]), ("m2", "v184-gui-phieu", new[] { 2 }), ("m2", "v184-gui-dac-tinh", new[] { 4 }),
        ("m7", "v184-vi-tri", new int[0]), ("m3sk", "v184-nxt", new int[0]), ("m3sk", "v184-nxt-so-cai", new[] { 6 }),
        ("m8", "v184-so-do", new int[0]), ("m8", "v184-so-do-phieu-in", new[] { 4 }),
        ("m5", "v184-doi-chieu", new int[0]), ("m7ck", "v184-cuoi-ky", new int[0]), ("m3", "v184-tong-hop", new[] { 1 }), ("m3", "v184-tong-hop-anh-xa", new[] { 2 }),
        ("m3dm", "v184-danh-muc", new int[0]), ("m10", "v184-khang-sinh", new int[0]), ("m10", "v184-khang-sinh-excel", new[] { 3 }),
        ("m11", "v184-pxht", new int[0]), ("m1vtb", "v184-vtb", new int[0]), ("m6", "v184-nhap-lieu", new int[0]), ("m13", "v184-phieu-xuat", new int[0]),
        ("m14", "v184-c3d", new int[0]), ("m22", "v184-so-sanh", new int[0]), ("audit", "v184-nhat-ky", new int[0]), ("dulieu", "v184-du-lieu", new int[0]),
    };
    foreach (var (k, n, path) in scenes)
    {
        if (only.Count > 0 && !only.Contains(k)) continue;
        hw.Navigate(k); P(40);
        // path: chỉ số tab cấp 1, rồi cấp 2… (-1 = giữ nguyên cấp đó)
        var level = 0; Control scope = hw.PageHost;
        foreach (var idx in path)
        {
            var tcs = scope.GetVisualDescendants().OfType<TabControl>().Where(t => t.IsEffectivelyVisible).ToList();
            var tc = tcs.FirstOrDefault();
            if (tc is null) break;
            if (idx >= 0 && idx < tc.ItemCount) { tc.SelectedIndex = idx; P(30); }
            scope = tc.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().FirstOrDefault(p => p.Name == "PART_SelectedContentHost" && p.TemplatedParent == tc) ?? (Control)tc;
            level++;
        }
        P(20); hw.InvalidateMeasure(); hw.InvalidateVisual(); P(10);
        Avalonia.Headless.AvaloniaHeadlessPlatform.ForceRenderTimerTick(3); P(5);
        var grids = hw.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().Where(g => g.IsEffectivelyVisible && g.Bounds.Height > 0).ToList();
        var big = grids.OrderByDescending(g => g.Bounds.Height * g.Bounds.Width).FirstOrDefault();
        var rows = big is null ? -1 : (int)((big.Bounds.Height - KhoLanh.App.Controls.SheetGrid.HeadH - 26 - 30) / KhoLanh.App.Controls.SheetGrid.RowH);
        var gy = big?.TranslatePoint(new Avalonia.Point(0, 0), hw)?.Y ?? -1;
        var trs = hw.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.ToolRow>().Where(t => t.IsEffectivelyVisible).Select(t => $"{t.Bounds.Height:0}@{t.TranslatePoint(new Avalonia.Point(0, 0), hw)?.Y:0}").ToList();
        Console.WriteLine($"[v184] {n}: bảng lớn y={gy:0} cao={big?.Bounds.Height:0} ≈ {rows} dòng · thanh công cụ: {string.Join(" ", trs)}");
        if (big is not null && rows < 8 && big.Model.RowCount > rows) { Console.WriteLine("        HỎNG: bảng dưới 8 dòng"); ok = false; }
        Snap(hw, n);
    }
    hw.Close(); s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v184) — ĐẠT: " : "Xong (v184) — CHƯA ĐẠT: ") + outDir); if (!ok) Environment.Exit(1); return;
}
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v206")
{
    // 1.106: LAN theo tài khoản — Giữa các máy › Mạng LAN: không cài tay IP / mã, Quản trị bật / tắt máy chủ.
    void Wait6(int n = 40) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    s.Settings.Set("acct_url", KhoLanh.Core.Services.PhoneWebApp.DefaultUrl); s.Settings.Set("acct_token", KhoLanh.Core.Services.Secret.Protect("mau"));
    var w = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal }; w.Show();
    w.Navigate("lan"); Wait6(60);
    var lv = w.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.LanView>().FirstOrDefault();
    var ok6 = lv is not null && lv.ClaimButton.IsVisible;
    Console.WriteLine($"[v206] Mạng LAN: nút \"{lv?.ClaimButton.Content}\" {(lv?.ClaimButton.IsVisible == true ? "hiện" : "ẩn")}");
    if (lv is not null) { lv.LanTabs.SelectedIndex = 1; Wait6(30); }
    CheckUnderNav(w, "v206-lan-theo-tai-khoan"); Snap(w, "v206-lan-theo-tai-khoan");
    s.Lan.Dispose();
    if (!ok6 || Environment.ExitCode != 0) { Console.WriteLine("Xong (v206) — CHƯA ĐẠT"); Environment.Exit(1); }
    Console.WriteLine("Xong (v206) — ĐẠT: " + outDir); return;
}
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "v205")
{
    // 1.105 (phương án A): Hệ thống › Kết nối + Đồng bộ & sao lưu (4 tab) + Cài đặt máy gọn, khóa menu cũ mở đúng tab. Tự kiểm, thoát mã 1 nếu hỏng.
    var ok = true;
    void Bad(string m) { Console.WriteLine("        HỎNG: " + m); ok = false; }
    void Wait(int n = 40) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    void Connected(bool on)
    {
        s.Settings.Set(KhoLanh.Core.Services.PhoneWebApp.KeyToken, on ? KhoLanh.Core.Services.Secret.Protect("mau") : "");
        s.Settings.Set("acct_url", on ? KhoLanh.Core.Services.PhoneWebApp.DefaultUrl : ""); s.Settings.Set("acct_token", on ? KhoLanh.Core.Services.Secret.Protect("mau") : "");
        s.Settings.Set("acct_last_pull", on ? DateTime.Now.AddMinutes(-12).ToString("yyyy-MM-dd HH:mm:ss") : "");
    }
    Directory.CreateDirectory(s.Backup.AutoBackupFolder);
    s.Backup.ManualBackup(Path.Combine(s.Backup.AutoBackupFolder, $"tu-dong_{DateTime.Today:yyyy-MM-dd}.db"));
    s.Audit.Log("admin", "Đồng bộ điện thoại: Đẩy lên", "m1 1284 dòng, m2 312 dòng, m7 2410 dòng");
    s.Audit.Log("admin", "Đồng bộ điện thoại: Nhận phiếu", "m1 1 phiếu, m7 1 phiếu");
    s.Audit.Log("admin", "Kết nối tài khoản dùng chung", "tải về 6 tài khoản");
    s.Audit.Log("admin", "Tự nhận vai LAN", "máy chủ chính");
    var w = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal }; w.Show();
    foreach (var on in new[] { true, false })
    {
        Connected(on);
        w.Navigate("audit"); Wait(10); w.Navigate("ketnoi"); Wait();
        if (w.PageHost.Content is not KetNoiView kv) { Bad("ketnoi không mở KetNoiView"); continue; }
        var sheet = kv.Shown.FirstOrDefault(x => x.Name == "Google Sheet chung");
        Console.WriteLine($"[v205] Kết nối ({(on ? "đã nối" : "chưa nối")}): {string.Join(" | ", kv.Shown.Select(x => x.Name + "=" + x.State))}");
        if (sheet.State != (on ? "ok" : "off")) Bad($"Google Sheet chung = {sheet.State}");
        CheckUnderNav(w, "v205-ket-noi");
        Snap(w, on ? "v205-ket-noi" : "v205-ket-noi-chua");
        if (!on)
        {
            _ = kv.WizardForTest(); Wait(20);
            if (KetNoiView.LastWizard is { } wz) { Snap(wz, "v205-ket-noi-lan-dau"); wz.Close(); Wait(10); } else Bad("không mở được hộp Kết nối lần đầu");
        }
    }
    Connected(true);
    w.Navigate("dongbo"); Wait();
    if (w.PageHost.Content is DongBoView dv)
    {
        var heads = dv.Tabs.Items.OfType<TabItem>().Select(t => t.Header?.ToString()).ToList();
        Console.WriteLine($"[v205] Đồng bộ & sao lưu: tab {string.Join(" · ", heads)} · Máy chủ: {dv.MayChu.State}");
        if (string.Join("|", heads) != "Máy chủ|Điện thoại|Sao lưu") Bad("cần 3 tab Máy chủ · Điện thoại · Sao lưu (1.121)");
        string[] names = { "v205-may-chu", "v205-dien-thoai", "v205-sao-luu" };
        for (var t = 0; t < dv.Tabs.ItemCount; t++)
        {
            dv.Tabs.SelectedIndex = t; Wait(30);
            CheckUnderNav(w, names[t]); Snap(w, names[t]);
            foreach (var g in w.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().Where(g => g.IsEffectivelyVisible))
                Console.WriteLine($"   [{names[t]}] bảng cao {g.Bounds.Height:0}");
        }
    }
    else Bad("dongbo không mở DongBoView");
    foreach (var (key, tab) in new[] { ("phone", "Điện thoại"), ("dcn", "Sao lưu"), ("lan", "Máy chủ"), ("drive", "Máy chủ"), ("dulieu", "Máy chủ"), ("saoluu", "Sao lưu") })
    {
        w.Navigate("audit"); Wait(10); w.Navigate(key); Wait(20);
        var h = (w.PageHost.Content as DongBoView)?.Tabs.SelectedItem is TabItem ti ? ti.Header?.ToString() : null;
        Console.WriteLine($"[v205] khóa cũ \"{key}\" → {h}");
        if (h != tab) Bad($"khóa {key} mở {h}, cần {tab}");
    }
    w.Navigate("backup"); Wait();
    CheckUnderNav(w, "v205-cai-dat-may"); Snap(w, "v205-cai-dat-may");
    if (navBad.Count > 0) ok = false;
    w.Close();
    s.Lan.Dispose(); Console.WriteLine((ok ? "Xong (v205) — ĐẠT: " : "Xong (v205) — CHƯA ĐẠT: ") + outDir); if (!ok) Environment.Exit(1); return;
}
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "he")
{
    // 1.68: "dulieu" = Dữ liệu & sao lưu (tab Đồng bộ: chỉ máy này / Drive dùng chung / LAN · tab Sao lưu: trên máy / Drive cá nhân Gmail)
    foreach (var (k, n) in new[] { ("users", "he-tai-khoan"), ("backup", "he-cai-dat"), ("ketnoi", "he-ket-noi"), ("dongbo", "he-dong-bo"), ("lan", "he-mang-lan"), ("drive", "he-drive"), ("saoluu", "he-sao-luu"), ("dcn", "he-drive-ca-nhan"), ("phone", "he-dien-thoai"), ("audit", "he-nhat-ky"), ("troly", "he-tro-ly") })
    {
        if (k == "troly")
        {
            // Giai đoạn 3: dữ liệu mẫu cho tab Cảnh báo — cấu hình kết nối hỏng (Web App trỏ nhầm, PC_TOKEN sai, Gmail ✖) + lỗi cùng nguyên nhân ở nhiều nơi
            s.Settings.Set("acct_url", "https://script.google.com/macros/s/MAU-TAI-KHOAN/exec"); s.Settings.Set("acct_token", KhoLanh.Core.Services.Secret.Protect("mau"));
            KhoLanh.Core.Services.ConnErrorLog.Note(s.Settings, KhoLanh.Core.Services.ConnErrorLog.Acct, "Đường dẫn ở Cài đặt máy › Tài khoản dùng chung (Google Sheet) đang trỏ tới Web App của APP ĐIỆN THOẠI, không phải Web App tài khoản.", KhoLanh.Core.Services.ConnErrorLog.KindPhoneScript);
            s.Settings.Set(KhoLanh.Core.Services.PhoneWebApp.KeyToken, KhoLanh.Core.Services.Secret.Protect("mau"));
            KhoLanh.Core.Services.ConnErrorLog.Note(s.Settings, KhoLanh.Core.Services.ConnErrorLog.Phone, "Mã truy cập không đúng. → Mã PC_TOKEN ở Đồng bộ điện thoại › Kết nối không khớp mã PC_TOKEN trong Apps Script.", KhoLanh.Core.Services.ConnErrorLog.KindAuth);
            s.Settings.Set("lx_mail_email", "kho.anan@gmail.com"); s.Settings.Set("lx_mail_pass", KhoLanh.Core.Services.Secret.Protect("mau"));
            s.Settings.Set("lx_mail_status", $"✖ {DateTime.Now:HH:mm dd/MM} · Gmail từ chối đăng nhập (mật khẩu ứng dụng sai hoặc đã bị thu hồi).");
            Directory.CreateDirectory(Path.GetDirectoryName(s.TroLy.ErrorLogFile)!);
            const string Root = "Tên người dùng hoặc mã truy cập không đúng, hoặc tài khoản đã bị khóa. Liên hệ người quản lý app PC.";
            string Unobs(string fn) => $"System.AggregateException: A Task's exception(s) were not observed either by Waiting on the Task or accessing its Exception property. ({Root})\n ---> KhoLanh.Core.Services.BusinessException: {Root}\n   at KhoLanh.Core.Services.AccountSyncService.{fn}()\n   --- End of inner exception stack trace ---\n\n";
            File.AppendAllText(s.TroLy.ErrorLogFile,
                $"{DateTime.Now.AddHours(-5):yyyy-MM-dd HH:mm:ss} bản 1.98.0 [Tác vụ nền]\n" + Unobs("CallAsync") +
                $"{DateTime.Now.AddHours(-3):yyyy-MM-dd HH:mm:ss} bản 1.98.0 [Tác vụ nền]\n" + Unobs("PullAsync") +
                $"{DateTime.Now.AddHours(-1):yyyy-MM-dd HH:mm:ss} bản 1.98.0\nKhoLanh.Core.Services.BusinessException: {Root}\n   at KhoLanh.App.Views.UsersView.OnSave()\n\n");
            try { s.TroLy.Scan(false); } catch (Exception e) { Console.WriteLine("[troly] quét lỗi: " + e.Message); }
            var cfg = s.TroLy.Last?.Findings.Where(f => f.Cat == "ket-noi" && f.Sev != "xanh").ToList() ?? new();
            var grp = s.TroLy.Last?.Findings.FirstOrDefault(f => f.Cat == "loi-app" && f.Title.Contains("Tên người dùng"));
            Console.WriteLine($"[troly] cấu hình kết nối: {cfg.Count} cảnh báo ({string.Join(" | ", cfg.Select(f => f.Sev + " " + f.Title + " → " + f.Go))}) · lỗi gộp: {grp?.Detail}");
            var tw = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal }; tw.Show();
            tw.Navigate(k); for (var i = 0; i < 60; i++) { Thread.Sleep(30); Pump(); } Snap(tw, n + "-1366");
            tw.Close();
        }
        main.Navigate(k); for (var i = 0; i < 40; i++) { Thread.Sleep(30); Pump(); } Snap(main, n);
        if (k is "users")
        {
            var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
            if (k == "users") main.PageHost.GetVisualDescendants().OfType<DataGrid>().First().SelectedIndex = 1;
            tc.SelectedIndex = 1; for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); } Snap(main, n + "-2");
        }
    }
    s.Lan.Dispose(); Console.WriteLine("Xong (he): " + outDir); return;
}
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "ui")
{
    foreach (var (k, n) in new[] { ("m6", "ui-nhap-lieu"), ("m7", "ui-vi-tri"), ("m1", "ui-an-an"), ("m2", "ui-gui"), ("m3sk", "ui-nxt") })
    {
        main.Navigate(k); for (var i = 0; i < 40; i++) { Thread.Sleep(30); Pump(); } Snap(main, n);
        if (k == "m6") SnapSuggest(main, "ui-nhap-lieu-goi-y");
        if (k == "m7" && main.PageHost.Content is ViTriView vv)
        {
            _ = vv.OpenPickerForTest(); for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); }
            var pw = vv.PickerWindow!; var g = pw.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.Model.ColCount == 12);
            g.Model.SetCells(new[] { (0, 8, "6"), (1, 8, "x"), (2, 8, "10"), (4, 8, "4"), (6, 8, "3") });
            for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); } Snap(pw, "ui-vi-tri-chon");
            pw.Close(); for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); } Snap(main, "ui-vi-tri-phieu");
        }
        foreach (var sg in main.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>())
        {
            var tb = typeof(KhoLanh.App.Controls.SheetGrid).GetField("_totals", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(sg) as Control;
            var sa = (double?[])typeof(KhoLanh.App.Controls.SheetGrid).GetField("_sumAll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(sg)!;
            Console.WriteLine($"   [{n}] grid h={sg.Bounds.Height:0} cols={sg.Model.ColCount} totals vis={tb!.IsVisible} h={tb.Bounds.Height:0} sums={string.Join(",", sa.Select(x => x?.ToString() ?? "-"))}");
        }
    }
    s.Lan.Dispose(); Console.WriteLine("Xong (ui): " + outDir); return;
}
// 1.72 (luồng 2): SHOTS_ONLY=l2 — B4 số "Cần xử lý" Danh mục trên menu + chuột phải "Thêm vào danh mục"; B1 Sơ đồ kho › kết quả kiểm kê
// (+ mở Nhập liệu đúng dòng); B2 Phiếu xuất nội địa › "Chọn từ tồn" theo size + đặc tính. Thoát mã 1 nếu kiểm tra hỏng.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "l2")
{
    void W(int ms) { for (var i = 0; i < ms / 30; i++) { Thread.Sleep(30); Pump(); } }
    void Fail(string m) { Console.WriteLine("[L2] LỖI: " + m); s.Lan.Dispose(); Environment.Exit(1); }
    var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var sf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
    // ---------- B4: mã lạ ở Nhập liệu → chuột phải "Thêm vào danh mục mã hàng"
    var known = s.ViTri.Rows.Select(r => r.Ma).Where(m => m.Length > 0).Distinct().Take(3).ToList();
    var maLa = "VRHLBTNXXXR00" + "0099" + "IME1";
    var pg = s.Entry.Create("Kiểm kê thử mã lạ " + DateTime.Today.ToString("dd/MM/yyyy"), KhoLanh.Core.Entry.EntryService.BuiltIn.First(t => t.Id == "kiem-ke"));
    s.Entry.Save(pg, pg.Columns, known.Select((m, i) => (IReadOnlyList<string>)new[] { $"TG1.A.0{i + 1}.1.1", m, "", "", "10", "", "", "" }).Append(new[] { "TG1.A.04.1.1", maLa, "", "", "5", "", "", "" }).ToList());
    main.RefreshDmBadge(); W(300);
    var badgeN = (Dictionary<string, int>)typeof(MainWindow).GetField("_badgeN", bf)!.GetValue(main)!;
    for (var i = 0; i < 200 && !badgeN.ContainsKey("m3dm"); i++) W(60);
    var n0 = badgeN.GetValueOrDefault("m3dm", -1);
    Console.WriteLine($"[L2] số Cần xử lý Danh mục trên menu: {n0}");
    if (n0 < 0) Fail("chưa tính số Cần xử lý");
    var gb = main.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("navtab") && b.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Chức năng"));
    gb.Flyout!.ShowAt(gb); W(300);
    SnapFlyout(main, gb, "47-danh-muc-can-xu-ly-tren-menu");
    gb.Flyout.Hide(); W(100);

    main.Navigate("m6"); W(600);
    var ev = main.PageHost.GetVisualDescendants().OfType<EntryView>().First();
    for (var i = 0; i < 150 && typeof(EntryView).GetField("_lookup", sf)!.GetValue(null) is null; i++) W(60);
    ev.OpenAt(pg.Id, 3); W(500);
    var g = ev.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
    var mc = Enumerable.Range(0, g.Model.ColCount).First(c => g.Model.Columns[c].Header == "Mã hàng");
    g.Select(3, mc); W(200);
    main.Height = 1020; W(300);   // menu chuột phải dài — cửa sổ cao hơn để ảnh không cắt
    var surf = g.GetVisualDescendants().OfType<Control>().First(c => c.ContextMenu is not null);
    var cm = surf.ContextMenu!;
    surf.RaiseEvent(new ContextRequestedEventArgs()); W(300);
    if (!cm.IsOpen) cm.Open(surf); W(300);
    var dmItem = cm.Items.OfType<MenuItem>().FirstOrDefault(x => x.IsVisible && (x.Header as string ?? "").Contains("danh mục mã hàng"));
    var mapItem = cm.Items.OfType<MenuItem>().FirstOrDefault(x => x.IsVisible && (x.Header as string ?? "").Contains("sơ đồ kho"));
    Console.WriteLine($"[L2] chuột phải ô mã lạ: “{dmItem?.Header}” · “{mapItem?.Header}”");
    if (dmItem is null) Fail("không có mục Thêm vào danh mục");
    Snap(main, "47b-nhap-lieu-them-vao-danh-muc");   // ảnh headless có sẵn lớp popup (menu chuột phải)
    cm.Close(); main.Height = 880; W(200);
    var tAdd = (Task)typeof(EntryView).GetMethod("AddToDanhMuc", bf)!.Invoke(ev, null)!;
    for (var i = 0; i < 300 && !tAdd.IsCompleted; i++) W(30);
    W(1500);
    s.DanhMuc.Load(); var inDm = s.DanhMuc.Rows.Any(r => r.Code == maLa);
    Console.WriteLine($"[L2] sau khi thêm: mã có trong danh mục = {inDm}; số Cần xử lý = {badgeN.GetValueOrDefault("m3dm")}");
    if (!inDm) Fail("mã chưa vào danh mục");
    g.Select(3, mc); W(200);
    Snap(main, "47c-nhap-lieu-da-them-danh-muc");

    // ---------- B1: Sơ đồ kho › kết quả kiểm kê
    s.SoDo.Load();
    var kp0 = s.SoDo.KkPages().OrderByDescending(p => p.Rows).First(p => p.Name.StartsWith("Kiểm kê TG1 khu A-B"));
    // trang thử 2 dãy (A 01–02): bỏ 2 vị trí có hàng (→ thiếu), thêm hàng vào 1 ô trống (→ thừa); các dãy khác = chưa đếm
    var kRows = s.Entry.Load(kp0).Where(r => r[0].StartsWith("TG1.A.01.") || r[0].StartsWith("TG1.A.02.")).ToList();
    var drop = kRows.Select(r => r[0]).Distinct().Skip(3).Take(2).ToHashSet();
    var emptyPos = s.SoDo.B.Pos.First(p => p.Kho == "TG1" && p.Khu == "A" && p.D == 2 && p.It.Count == 0).Code;
    var kNew = kRows.Where(r => !drop.Contains(r[0])).Select(r => (IReadOnlyList<string>)r.Take(kp0.Columns.Count).ToArray()).ToList();
    var extra = new string[kp0.Columns.Count]; Array.Fill(extra, ""); extra[0] = emptyPos; extra[1] = kRows[0][1]; extra[4] = "12"; kNew.Add(extra);
    var kkp = s.Entry.Create("Kho 1 A dãy 01-02 kiểm kê " + DateTime.Today.ToString("dd-MM-yyyy"), KhoLanh.Core.Entry.EntryService.BuiltIn.First(t => t.Id == "kiem-ke"));
    kkp = s.Entry.Save(kkp, kp0.Columns, kNew);
    var mapRes = s.SoDo.KkMapOf(kkp.Id);
    Console.WriteLine("[L2] kiểm kê trên sơ đồ: " + string.Join(", ", mapRes.N.Select(kv => $"{kv.Key} {kv.Value}")) + $" · {mapRes.Kho} khu {mapRes.Khu} (1 khu: {mapRes.OneKhu})");
    var lechCode = mapRes.St.FirstOrDefault(kv => kv.Value == KhoLanh.Core.KiemKe.KiemKeCore.KkLech).Key ?? mapRes.St.First(kv => kv.Value != "chua").Key;
    main.Show("m8", new SoDoView(kkp.Id, lechCode)); W(2500);
    Snap(main, "25h-so-do-ket-qua-kiem-ke");
    var sd = main.PageHost.GetVisualDescendants().OfType<SoDoView>().First();
    var tOpen = (Task)typeof(SoDoView).GetMethod("KkOpenEntry", bf)!.Invoke(sd, new object[] { s.SoDo.B.ByCode[lechCode] })!;
    for (var i = 0; i < 300 && !tOpen.IsCompleted; i++) W(30);
    W(1200);
    var ev2 = main.PageHost.GetVisualDescendants().OfType<EntryView>().FirstOrDefault();
    var g2 = ev2?.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
    Console.WriteLine($"[L2] bấm ô {lechCode} → Nhập liệu dòng {(g2?.Row ?? -1) + 1}: vị trí “{(g2 is null ? "" : g2.Model.Get(g2.Row, 0))}”");
    if (g2 is null || KhoLanh.Core.SoDo.SoDoCore.Parse(g2.Model.Get(g2.Row, 0))?.Code != lechCode) Fail("không mở đúng dòng ở Nhập liệu");
    Snap(main, "25i-so-do-mo-dong-nhap-lieu");

    // ---------- B2: Phiếu xuất nội địa › Chọn từ tồn
    main.Navigate("m13"); W(1200);
    var top = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible);
    top.SelectedIndex = 2; W(500);
    var cg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.CargoView>().First(x => x.IsEffectivelyVisible);
    var lg = cg.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
    lg.Select(1, 0); lg.RaiseEvent(new Avalonia.Input.TappedEventArgs(Avalonia.Input.Gestures.DoubleTappedEvent, null!)); W(800);   // dòng 0 = dòng nhóm tháng (1.82)
    var cur = (KhoLanh.Core.PhieuKho.CargoRec)typeof(KhoLanh.App.Views.CargoView).GetField("_cur", bf)!.GetValue(cg)!;
    cur.DayCap = Enumerable.Range(1, 20).ToDictionary(d => d.ToString(), _ => "63");   // KIỆN / DÃY 20 × 63
    typeof(KhoLanh.App.Views.CargoView).GetMethod("ShowEditor", bf)!.Invoke(cg, null); W(400);
    var task = (Task)typeof(KhoLanh.App.Views.CargoView).GetMethod("PickFromStock", bf)!.Invoke(cg, new object[] { cur, 0 })!;
    W(900);
    var pw = cg.PickerWindow;
    if (pw is null) Fail("không mở được cửa sổ Chọn từ tồn");
    var pg2 = pw!.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First();
    Console.WriteLine($"[L2] Chọn từ tồn: {pg2.Model.RowCount} nhóm size + đặc tính; 3 nhóm đầu: " + string.Join(" | ", Enumerable.Range(0, Math.Min(3, pg2.Model.RowCount)).Select(r => string.Join(" · ", pg2.Model.Row(r).Take(8)))));
    var okRows = Enumerable.Range(0, pg2.Model.RowCount).Where(r => pg2.Model.Get(r, 3).Length > 0).Take(2).ToList();
    pg2.SetChecked(okRows, true); W(200);
    double LeftOf(int r) => double.Parse(pg2.Model.Get(r, 7).Replace(",", ""), System.Globalization.CultureInfo.InvariantCulture);
    var r3 = Enumerable.Range(0, pg2.Model.RowCount).Where(r => !okRows.Contains(r) && pg2.Model.Get(r, 3).Length > 0).OrderBy(LeftOf).First();
    pg2.Model.SetCells(okRows.Select(r => (r, 8, "40")).ToList()); W(100);
    pg2.Model.Set(r3, 8, (LeftOf(r3) + 20).ToString(System.Globalization.CultureInfo.InvariantCulture)); W(300);
    Snap(pw, "31e-phieu-noi-dia-chon-tu-ton");
    var add = pw.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Thêm vào phiếu");
    add.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); W(300);
    for (var i = 0; i < 300 && !task.IsCompleted; i++) W(30);
    W(800);
    var b0 = cur.Blocks[0];
    Console.WriteLine($"[L2] sau Thêm vào phiếu: mục 1 có {b0.Lines.Count} dòng; dòng cuối: Loại {b0.Lines[^1].Loai}, Sn {b0.Lines[^1].Sn}, KQ tay {b0.Lines[^1].KsTay}, Lô “{b0.Lines[^1].Lo}”, dãy {string.Join(",", b0.Lines[^1].Dai.Select(kv => kv.Key + ":" + kv.Value))}");
    if (b0.Lines.Count < 3) Fail("chưa thêm dòng vào mục");
    var blockGrid = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().Where(x => x.IsEffectivelyVisible && x.Model.ColCount > 40).First();
    W(400);
    Snap(main, "31f-phieu-noi-dia-da-them-tu-ton");
    s.Lan.Dispose(); Console.WriteLine("Xong (l2): " + outDir); return;
}
// 1.73 (luồng 2): SHOTS_ONLY=l3 — N3 cảnh báo file đổi mẫu (An An, Tồn theo vị trí) · K2 báo trùng / lỗi khi nhập kiểm kê. Tự kiểm, sai → thoát mã 1.
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "l3")
{
    void W(int ms) { for (var i = 0; i < ms / 30; i++) { Thread.Sleep(30); Pump(); } }
    void Fail(string m) { Console.WriteLine("[L3] LỖI: " + m); s.Lan.Dispose(); Environment.Exit(1); }
    var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    var sf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
    Window? Dlg(Func<Task> start, out Task t)
    {
        t = start(); W(700);
        return main.OwnedWindows.LastOrDefault();
    }
    void CloseDlg(Window w, Task t) { w.Close(); for (var i = 0; i < 40 && !t.IsCompleted; i++) W(30); }
    List<string> Texts(Window w) => w.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text ?? "").ToList();

    // ---------- N3 · Tồn kho An An: nạp file 20/11 (lưu mẫu) → xem trước file 19/11 đổi tên cột, thêm cột, ít dòng hơn
    string AnXlsx(string name, string[] head, int rows)
    {
        var fp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
        using var wbk = new ClosedXML.Excel.XLWorkbook(); var ws = wbk.AddWorksheet("TonKho");
        for (var c = 0; c < head.Length; c++) ws.Cell(1, c + 1).Value = head[c];
        for (var i = 0; i < rows; i++)
            for (var c = 0; c < head.Length; c++)
                ws.Cell(i + 2, c + 1).Value = head[c] switch
                {
                    "Mã hàng" => $"VRHLBTNXECR00{3140 + i}IME1", "Tên hàng" => $"Tôm thẻ mẫu {i}", "Đặc tính" => "IQF", "Size" => "31/40", "Hợp đồng" => "HD-2611", "ĐVT" => "Thùng",
                    "Tồn cuối" => 20 + i * 3, "Tồn cuối (NET)" => (20 + i * 3) * 10, "Tình trạng" => "Thành phẩm", "Ghi chú" => "", _ => (ClosedXML.Excel.XLCellValue)10,
                };
        wbk.SaveAs(fp); return fp;
    }
    var h1 = new[] { "Tình trạng", "Mã hàng", "Tên hàng", "Đặc tính", "Size", "Hợp đồng", "ĐVT", "Tồn cuối", "Tồn cuối (NET)", "Trọng lượng" };
    var h2 = new[] { "Tình trạng", "Mã hàng", "Tên hàng", "Đặc tính", "Size", "Hợp đồng", "ĐVT", "Tồn cuối", "Tồn cuối (NET)", "Trọng lượng (kg)", "Ghi chú" };
    var pv1 = s.AnAn.Preview(AnXlsx("Ton kho An An 20.11.2026.xlsx", h1, 20));
    s.AnAn.Commit(pv1);
    var mau = KhoLanh.Core.TonKho.MauFile.Doc(s.Lan, KhoLanh.Core.TonKho.AnAnStore.Db);
    Console.WriteLine($"[L3] mẫu An An đã lưu: {mau?.Headers.Count} cột · {mau?.Rows} dòng · ngày {mau?.DataDate}");
    if (mau is null || mau.Headers.Count != h1.Length || mau.Rows != 20) Fail("chưa lưu mẫu file An An sau khi nạp");
    main.Navigate("m1"); W(900);
    var anv = main.PageHost.GetVisualDescendants().OfType<AnAnView>().First();
    var pv2 = s.AnAn.Preview(AnXlsx("Ton kho An An 19.11.2026.xlsx", h2, 13));
    var cb = KhoLanh.Core.TonKho.MauFile.SoSanh(mau, KhoLanh.Core.TonKho.MauFile.Of(pv2), KhoLanh.Core.TonKho.MauFile.BatBuocAnAn, s.AnAn.D.DataDate);
    Console.WriteLine("[L3] An An so mẫu: " + string.Join(" | ", cb.Select(x => $"{x.Muc}:{x.Key}")));
    foreach (var k in new[] { "thieu:trongluong", "them", "ngay-cu", "sodong" }) if (!cb.Any(x => x.Key == k)) Fail("thiếu cảnh báo " + k);
    var dA = Dlg(() => (Task)typeof(AnAnView).GetMethod("ReviewDialog", bf)!.Invoke(anv, new object[] { pv2 })!, out var tA);
    if (dA is null) Fail("không mở hộp xem trước An An");
    var goA = dA!.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.Classes.Contains("primary"));
    Console.WriteLine($"[L3] hộp xem trước An An: nút “{goA?.Content}” · có khung khác mẫu: {Texts(dA).Any(t => t.Contains("KHÁC MẪU"))}");
    if (goA?.Content as string != "Vẫn nạp" || !Texts(dA).Any(t => t.Contains("KHÁC MẪU"))) Fail("hộp xem trước An An chưa báo khác mẫu / nút chưa đổi Vẫn nạp");
    Snap(dA, "40d-an-an-file-khac-mau");
    CloseDlg(dA, tA);

    // ---------- N3 · Tồn theo vị trí: nạp file (lưu mẫu) → file cùng tên, đổi thứ tự cột + thêm cột, nội dung khác
    string VtXlsx(string[] head, int rows, int shift)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kholanh-l3-" + shift); System.IO.Directory.CreateDirectory(dir);
        var fp = System.IO.Path.Combine(dir, "Ton theo vi tri 21.11.xlsx");
        using var wbk = new ClosedXML.Excel.XLWorkbook(); var ws = wbk.AddWorksheet("ViTri");
        for (var c = 0; c < head.Length; c++) ws.Cell(1, c + 1).Value = head[c];
        var src = s.ViTri.Rows.Where(r => r.Vt.Length > 0).Take(rows).ToList();
        for (var i = 0; i < src.Count; i++)
            for (var c = 0; c < head.Length; c++)
                ws.Cell(i + 2, c + 1).Value = head[c] switch
                {
                    "Mã hàng" => src[i].MaGoc, "Tên hàng" => src[i].Ten, "Vị trí" => src[i].Vt, "SL Tồn" => src[i].Sl + shift, "T.Lượng" => src[i].Tl, "Lô (Batch)" => src[i].Lo, _ => "",
                };
        wbk.SaveAs(fp); return fp;
    }
    var vtN = Math.Min(40, s.ViTri.Rows.Count);
    var f1 = s.ViTri.ReadFile(VtXlsx(new[] { "Mã hàng", "Tên hàng", "Lô (Batch)", "Vị trí", "SL Tồn", "T.Lượng" }, vtN, 0));
    f1.Sha = "lan-1"; s.ViTri.Import(f1);
    var f2 = s.ViTri.ReadFile(VtXlsx(new[] { "Vị trí", "Mã hàng", "Tên hàng", "Lô (Batch)", "SL Tồn", "T.Lượng", "Kho" }, vtN, 1)); f2.Sha = "lan-2";
    main.Navigate("m7"); W(900);
    var vv = main.PageHost.GetVisualDescendants().OfType<ViTriView>().First();
    var dV = Dlg(() => (Task<bool>)typeof(ViTriView).GetMethod("ConfirmImport", bf)!.Invoke(vv, new object[] { f2, $"Nạp {f2.Rows.Count:#,0} dòng từ “{f2.FileName}” (sheet {f2.Sheet}) thay cho số liệu hiện tại?" })!, out var tV);
    if (dV is null) Fail("không mở hộp nạp Tồn theo vị trí");
    var tv = Texts(dV!);
    Console.WriteLine($"[L3] hộp nạp vị trí: thứ tự cột = {tv.Any(t => t.Contains("Thứ tự cột"))} · cột mới = {tv.Any(t => t.Contains("Cột mới"))} · cùng tên khác nội dung = {tv.Any(t => t.Contains("nội dung đã khác"))}");
    if (!tv.Any(t => t.Contains("Thứ tự cột")) || !tv.Any(t => t.Contains("Cột mới")) || !tv.Any(t => t.Contains("nội dung đã khác"))) Fail("hộp nạp vị trí thiếu cảnh báo mẫu");
    Snap(dV!, "24d-vi-tri-nap-file-doi-mau");
    CloseDlg(dV!, tV);

    // ---------- K2 · Nhập liệu kiểm kê: trùng / vị trí lạ / mã lạ / số lỗi
    var maCol = s.AnAn.D.Headers.FindIndex(h => h == "Mã hàng");
    var known = s.AnAn.D.Rows.Select(r => r[maCol]).Where(m => m.Length > 0).Distinct().Take(4).ToList();
    var codes = s.SoDo.AllCodes().Where(c => c.StartsWith("TG1.A.01.")).OrderBy(c => c).Take(6).ToList();
    if (known.Count < 4 || codes.Count < 6) Fail($"dữ liệu mẫu thiếu (mã {known.Count}, vị trí {codes.Count})");
    var pg = s.Entry.Create("Kiểm kê thử báo lỗi " + DateTime.Today.ToString("dd/MM/yyyy"), KhoLanh.Core.Entry.EntryService.BuiltIn.First(t => t.Id == "kiem-ke"));
    var cot = KhoLanh.Core.KiemKe.KkKiemTra.Cols(pg.Columns.Select(c => c.Header))!;
    string[] Row(string vt, string ma, string sl, string xs = "") { var a = new string[pg.Columns.Count]; Array.Fill(a, ""); a[cot.Vt] = vt; a[cot.Ma] = ma; a[cot.Sl] = sl; if (cot.SlXuat >= 0) a[cot.SlXuat] = xs; return a; }
    var rowsK = new List<IReadOnlyList<string>>
    {
        Row(codes[0], known[0], "12"), Row(codes[1], known[1], "8", "3"), Row(codes[0], known[0], "4"),          // dòng 1 & 3 trùng
        Row("TG1.A.99.1.1", known[2], "6"), Row(codes[2], "VRHLBTNXXXR000099IME1", "5"),                         // vị trí ngoài sơ đồ · mã lạ
        Row(codes[3], known[3], "1O"), Row(codes[4], known[1], "5", "7"), Row(codes[5], known[2], "-2"),           // số lỗi
    };
    pg = s.Entry.Save(pg, pg.Columns, rowsK);
    main.Navigate("m6"); W(600);
    var ev = main.PageHost.GetVisualDescendants().OfType<EntryView>().First();
    for (var i = 0; i < 150 && (typeof(EntryView).GetField("_lookupKk", sf)!.GetValue(null) is null || typeof(EntryView).GetField("_vtCodes", sf)!.GetValue(null) is null); i++) W(60);
    ev.OpenAt(pg.Id, 0); W(800);
    var kk = (KhoLanh.Core.KiemKe.KkKiemTra.KetQua)typeof(EntryView).GetField("_kk", bf)!.GetValue(ev)!;
    Console.WriteLine($"[L3] kiểm kê: {kk.TomTat()} · đỏ = {kk.CoDo}");
    if (kk.TrungDong != 2 || kk.ViTriLa != 1 || kk.MaLa != 1 || kk.SoLoi != 3) Fail("đếm sai (cần 2 trùng · 1 vị trí lạ · 1 mã lạ · 3 số lỗi)");
    var btn = ev.GetVisualDescendants().OfType<Button>().First(b => b.Name == "KkCheckButton");
    if (!btn.IsVisible || !(KhoLanh.App.Controls.Icon.TextOf(btn.Content) ?? "").Contains("2 dòng trùng")) Fail("dòng tóm tắt dưới lưới chưa hiện");
    var g = ev.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
    g.Select(1, 0);
    btn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); W(200);
    Console.WriteLine($"[L3] bấm tóm tắt từ dòng 2 → ô dòng {g.Row + 1}, cột {g.Col + 1}");
    if (g.Row != 2) Fail("bấm tóm tắt không nhảy tới ô lỗi kế tiếp (dòng 3)");
    btn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); W(100);
    btn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); W(300);   // → dòng 4 vị trí lạ
    Snap(main, "20e-nhap-lieu-kiem-tra-kiem-ke");
    // gõ sửa → tính lại sau khi ngừng gõ
    g.Model.Set(5, cot.Sl, "10"); W(700);
    kk = (KhoLanh.Core.KiemKe.KkKiemTra.KetQua)typeof(EntryView).GetField("_kk", bf)!.GetValue(ev)!;
    Console.WriteLine($"[L3] sửa “1O” → 10: {kk.TomTat()}");
    if (kk.SoLoi != 2) Fail("không tính lại sau khi sửa ô");
    s.Lan.Dispose(); Console.WriteLine("Xong (l3): " + outDir); return;
}
// Nhập liệu 1.62: SHOTS_ONLY=nl — mẫu Kiểm kê mới (dán mã → tự điền), tô màu ô, tab Mẫu, hộp Cột…
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "nl")
{
    void WaitNl(int ms) { for (var i = 0; i < ms / 30; i++) { Thread.Sleep(30); Pump(); } }
    var codes = s.ViTri.Rows.Select(r => r.Ma).Where(m => m.Length > 0).Distinct().Take(6).ToList();
    var pg = s.Entry.Create("Kiểm kê kho 1 " + DateTime.Today.ToString("dd/MM/yyyy"), KhoLanh.Core.Entry.EntryService.BuiltIn.First(t => t.Id == "kiem-ke"));
    main.Navigate("m6"); WaitNl(600);
    var ev = main.PageHost.GetVisualDescendants().OfType<EntryView>().First();
    for (var i = 0; i < 100 && typeof(EntryView).GetField("_lookup", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null) is null; i++) WaitNl(60);
    var combo = ev.GetVisualDescendants().OfType<ComboBox>().First();
    combo.SelectedItem = combo.Items.Cast<object>().First(o => o.ToString() == pg.Name); WaitNl(400);
    var g = ev.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
    var m = g.Model;
    m.Paste(new KhoLanh.Core.Grid.CellRange(0, 1, 0, 1), string.Join("\r\n", codes));          // dán cột Mã hàng → Size / Trọng lượng / Ngày nhập tự điền
    m.Paste(new KhoLanh.Core.Grid.CellRange(0, 0, 0, 0), string.Join("\r\n", codes.Select((_, i) => $"TG1.A.0{i + 1}.1.1")));
    m.Paste(new KhoLanh.Core.Grid.CellRange(0, 4, 0, 4), string.Join("\r\n", codes.Select((_, i) => (12 + i * 7).ToString())));
    g.Select(1, 0); g.Select(2, 5, extend: true);
    var paint = typeof(EntryView).GetMethod("PaintSelection", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    paint.Invoke(ev, new object[] { "y" });
    g.Select(4, 4); g.Select(4, 5, extend: true); paint.Invoke(ev, new object[] { "r" });
    g.Select(0, 1); WaitNl(300);
    Snap(main, "20-nhap-lieu");
    Console.WriteLine("   dòng 1: " + string.Join(" | ", m.Row(0)));
    var tabs = ev.GetVisualDescendants().OfType<TabControl>().First();
    tabs.SelectedIndex = 2; WaitNl(500);
    var ml = ev.GetVisualDescendants().OfType<ListBox>().First(l => l.IsEffectivelyVisible);
    ml.SelectedIndex = 3; WaitNl(400);
    Snap(main, "20d-nhap-lieu-mau");
    s.Lan.Dispose(); Console.WriteLine("Xong (nl): " + outDir); return;
}
// Tồn theo vị trí — tìm đúng nhãn size + lọc vị trí đang chọn; Sơ đồ kho — Phiếu In 2 tab (1.63): SHOTS_ONLY=vt
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "vt")
{
    void W(int ms) { for (var i = 0; i < ms / 30; i++) { Thread.Sleep(30); Pump(); } }
    main.Navigate("m7"); W(900);
    var vv = (ViTriView)main.PageHost.Content!;
    _ = vv.OpenPickerForTest(); W(600);
    var pw = vv.PickerWindow!;
    var g = pw.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.Model.ColCount == 12);
    // vị trí có ≥ 2 mã, lấy nhãn size của 1 mã trong đó
    var vtRows = s.ViTri.Rows;
    var multi = vtRows.Where(r => r.Vt.Length > 0 && r.Sl > 0 && s.ViTri.GopLabel(r) is not null).GroupBy(r => r.Vt).First(gr => gr.Select(r => r.Ma).Distinct().Count() >= 2);
    var label = s.ViTri.GopLabel(multi.First())!;
    var q = pw.GetVisualDescendants().OfType<TextBox>().First(t => t.Watermark?.StartsWith("Tìm: BTP") == true);
    q.Text = label; W(400);
    var r0 = Enumerable.Range(0, g.Model.RowCount).First(i => g.Model.Row(i)[0] == multi.Key);
    g.Model.SetCells(new[] { (r0, 8, "x") }); W(300); g.Select(r0, 8);
    Console.WriteLine($"   tìm “{label}”: {g.Model.RowCount} dòng, các size: {string.Join(", ", Enumerable.Range(0, g.Model.RowCount).Select(i => g.Model.Row(i)[3]).Distinct())}");
    Snap(pw, "24b1-vi-tri-tim-dung-size");
    var pos = pw.GetVisualDescendants().OfType<CheckBox>().First(c => c.Content as string == "Lọc vị trí đang chọn");
    pos.IsChecked = true; W(400);
    Snap(pw, "24b2-vi-tri-loc-vi-tri-dang-chon");
    pw.Close(); W(300);
    main.Navigate("m8"); W(900);
    var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
    tc.SelectedIndex = 4; W(600);
    Snap(main, "25f-so-do-phieu-in");
    var sub = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(t => t != tc && t.Items.Count == 2 && t.IsEffectivelyVisible);
    sub.SelectedIndex = 1; W(600);
    Snap(main, "25g-so-do-phieu-in-vi-tri-trong");
    s.Lan.Dispose(); Console.WriteLine("Xong (vt): " + outDir); return;
}
// 1.89: Phiếu In vị trí — chọn Khu / Dãy ở CẢ 2 tab con phải giữ đúng lựa chọn (trước luôn về Khu A · Dãy 01): SHOTS_ONLY=kkchon (tự kiểm, thoát mã 1)
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "kkchon")
{
    void W(int ms) { for (var i = 0; i < ms / 30; i++) { Thread.Sleep(30); Pump(); } }
    var bfk = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
    main.Navigate("m8"); W(900);
    var sdk = (SoDoView)main.PageHost.Content!;
    var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
    tc.SelectedIndex = 4; W(600);
    var cKhu = (ComboBox)typeof(SoDoView).GetField("_kKhu", bfk)!.GetValue(sdk)!;
    var cDay = (ComboBox)typeof(SoDoView).GetField("_kDay", bfk)!.GetValue(sdk)!;
    var sub = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(t => t != tc && t.Items.Count == 2 && t.IsEffectivelyVisible);
    (string Khu, int D) Cur() { var k = typeof(SoDoView).GetField("_k", bfk)!.GetValue(sdk); return k is null ? ("", -1) : ((string)k.GetType().GetField("Khu")!.GetValue(k)!, (int)k.GetType().GetField("D")!.GetValue(k)!); }
    var ok = true;
    foreach (var tab in new[] { 0, 1 })
    {
        sub.SelectedIndex = tab; W(500);
        cKhu.SelectedIndex = 1; W(400);
        var khu = (cKhu.SelectedItem as string ?? "").Replace("Khu ", "");
        cDay.SelectedIndex = 3; W(400);
        var day = cDay.SelectedItem as string ?? "";
        var cur = Cur();
        var pass = cur.Khu == khu && cur.D == 3 && cKhu.SelectedIndex == 1 && cDay.SelectedIndex == 3 && day == "Dãy 03";
        Console.WriteLine($"[KKCHON] tab {tab}: chọn Khu {khu} · {day} → phiếu Khu {cur.Khu} · Dãy {cur.D:00} · ô {cKhu.SelectedItem} / {cDay.SelectedItem} {(pass ? "ĐẠT" : "HỎNG")}");
        ok &= pass;
        cDay.SelectedIndex = 0; W(400);
        var all = Cur(); var pAll = all.Khu == khu && all.D == 0 && cDay.SelectedIndex == 0;
        Console.WriteLine($"[KKCHON] tab {tab}: Tất cả dãy → Khu {all.Khu} · D={all.D} {(pAll ? "ĐẠT" : "HỎNG")}");
        ok &= pAll;
        cDay.SelectedIndex = 2; W(400);
        Snap(main, tab == 0 ? "25j-phieu-in-chon-khu-day" : "25k-phieu-in-trong-chon-khu-day");
    }
    s.Lan.Dispose(); Console.WriteLine("Xong (kkchon): " + outDir); if (!ok) Environment.Exit(1); return;
}
// Phiếu xuất kho (1.61, danh sách / nhập liệu tách tab): SHOTS_ONLY=px
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "px")
{
    main.Navigate("m13"); for (var i = 0; i < 60; i++) { Thread.Sleep(30); Pump(); }
    PxScene(); s.Lan.Dispose(); Console.WriteLine("Xong (px): " + outDir); return;
}
// 1.62: popup kéo giãn / phóng to, Tồn kho gửi 1 tab Load Data, Báo cáo rã đông tích ngày gộp ảnh: SHOTS_ONLY=bt
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "bt")
{
    BtScene(); s.Lan.Dispose(); Console.WriteLine("Xong (bt): " + outDir); return;
}
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "menu") { s.Lan.Dispose(); Console.WriteLine("Xong (chỉ khung menu): " + outDir); return; }
if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "dm") { DmScene(); s.Lan.Dispose(); Console.WriteLine("Xong (chỉ Danh mục mã hàng): " + outDir); return; }

// Số liệu cuối kỳ (dữ liệu mẫu = bộ test đối chiếu lõi JS)
{
    void WaitCk(int ms) { for (var i = 0; i < ms / 30; i++) { Thread.Sleep(30); Pump(); } }
    var g = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(FindRepo(), "tests", "KhoLanh.Core.Tests", "Data", "ck-golden.json")))!;
    string Xl(string name, params (string sheet, string key)[] sheets)
    {
        var path = Path.Combine(Path.GetTempPath(), "kl-ck-" + name + ".xlsx");
        using var wb = new ClosedXML.Excel.XLWorkbook();
        foreach (var (sn, key) in sheets)
        {
            var ws = wb.AddWorksheet(sn); var rows = g["aoa"]![key]!.AsArray();
            for (var r = 0; r < rows.Count; r++) { var row = rows[r]!.AsArray(); for (var c = 0; c < row.Count; c++) { var v = row[c] as System.Text.Json.Nodes.JsonValue; if (v is null) continue; if (v.TryGetValue<double>(out var d)) ws.Cell(r + 1, c + 1).Value = d; else { var t = v.GetValue<string>(); if (t.Length > 0) ws.Cell(r + 1, c + 1).Value = t; } } }
        }
        wb.SaveAs(path); return path;
    }
    var ck = s.CuoiKy;
    ck.LoadFile("anan", Xl("an", ("NXT", "anan")));
    ck.LoadFile("ketoan", Xl("kt", ("NXT", "nxt"), ("Nhap lieu", "ct"), ("DMVT", "dm")));
    ck.LoadFile("gui", Xl("gui", ("TonGui", "gui")));
    var it = ck.Run().Items.First(i => i.Cands.Count > 0 && i.Cands[0].Conf == "cao");
    ck.Transfer(it.Diff > 0 ? it.Ma : it.Cands[0].Ma, it.Diff > 0 ? it.Cands[0].Ma : it.Ma, 50, "Phiếu XK ghi nhầm mã size", "kho");
    main.Navigate("m7ck"); WaitCk(1500);
    Snap(main, "36-so-lieu-cuoi-ky");
    var grid = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
    var idx = Enumerable.Range(0, grid.Model.RowCount).FirstOrDefault(r => grid.Model.Get(r, 0).Contains("VRCOBTNXXXR00009010IM"));
    grid.Select(idx, 0); WaitCk(500);
    Snap(main, "36b-so-lieu-cuoi-ky-chi-tiet");
    // tab Dữ liệu nguồn: sửa 1 ô số lượng của An An (tô vàng)
    { var r0 = ck.Files["anan"].Rows[1]; ck.EditCell("anan", r0, "kg", (r0.Kg + 5).ToString(System.Globalization.CultureInfo.InvariantCulture), "kiểm lại sổ"); }
    main.Navigate("m7ck"); WaitCk(1200);
    var ckTabs = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible); ckTabs.SelectedIndex = 1; WaitCk(500);
    Snap(main, "36c-so-lieu-cuoi-ky-du-lieu-nguon");
    Console.WriteLine($"[CK] {ck.Result!.Kpi.Tong} mã, lệch {ck.Result.Kpi.Lech}, đã xử lý {ck.Result.Kpi.DaXuLy}");
    if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "ck") { s.Lan.Dispose(); Console.WriteLine("Xong (chỉ Số liệu cuối kỳ): " + outDir); return; }
}

// Container 3D + Scan tài liệu
{
    void Wait(int ms) { for (var i = 0; i < ms / 30; i++) { Thread.Sleep(30); Pump(); } }
    main.Navigate("m14"); Wait(300);
    s.C3d.St.Skus = KhoLanh.Core.Container3D.C3dService.DemoSkus();
    main.Navigate("m3"); main.Navigate("m14"); Wait(3000);
    var tabs = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
    Snap(main, "50-c3d-hang-hoa");
    tabs.SelectedIndex = 1; Wait(300); Snap(main, "50-c3d-container");
    tabs.SelectedIndex = 2; Wait(600); Snap(main, "50-c3d-3d");
    var cv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.C3dCanvas>().First();
    Console.WriteLine($"[C3D] thùng: {cv.Plan?.Boxes.Count} / {cv.Plan?.Totals.Requested}, verify {cv.Plan?.Verify.Ok}");
    var c = cv.TranslatePoint(new Point(cv.Bounds.Width * 0.5, cv.Bounds.Height * 0.55), main)!.Value;
    main.MouseMove(c); Pump(); main.MouseDown(c, Avalonia.Input.MouseButton.Left); main.MouseUp(c, Avalonia.Input.MouseButton.Left); Wait(400);
    Snap(main, "50-c3d-chon-thung");
    cv.SetPreset("door"); Wait(200); Snap(main, "50-c3d-tu-cua");
    cv.SetPreset("top"); Wait(200); Snap(main, "50-c3d-tu-tren");
    cv.SetPreset("side"); Wait(200); Snap(main, "50-c3d-nhin-ngang");
    var sv = main.PageHost.GetVisualDescendants().OfType<ScrollViewer>().First(x => x.IsEffectivelyVisible && x.Content is StackPanel);
    sv.Offset = new Vector(0, 520); Wait(300); Snap(main, "50-c3d-bao-cao");

    main.Navigate("m15"); Wait(300); Snap(main, "51-scan-trong");
    var photo = Path.Combine(Path.GetTempPath(), "kl-scan-demo.png");
    {
        int W = 1200, H = 1600; using var bmp = new SkiaSharp.SKBitmap(W, H);
        using (var cnv = new SkiaSharp.SKCanvas(bmp))
        {
            cnv.Clear(new SkiaSharp.SKColor(120, 80, 45));
            using var path = new SkiaSharp.SKPath(); path.MoveTo(220, 180); path.LineTo(1010, 250); path.LineTo(960, 1440); path.LineTo(170, 1380); path.Close();
            using var paper = new SkiaSharp.SKPaint { Shader = SkiaSharp.SKShader.CreateLinearGradient(new SkiaSharp.SKPoint(200, 0), new SkiaSharp.SKPoint(1000, 0), new[] { new SkiaSharp.SKColor(238, 236, 230), new SkiaSharp.SKColor(150, 148, 142) }, SkiaSharp.SKShaderTileMode.Clamp), IsAntialias = true };
            cnv.DrawPath(path, paper);
            using var ink = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(25, 25, 30), IsAntialias = true, TextSize = 34 };
            using var rule = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(150, 160, 190), StrokeWidth = 2 };
            cnv.Save(); cnv.RotateDegrees(4, 600, 800);
            for (var i = 0; i < 18; i++) { var y = 330 + i * 60; cnv.DrawLine(250, y + 8, 930, y + 8, rule); cnv.DrawText("PHIẾU NHẬP KHO số " + (100 + i) + " — Tôm HLSO 31/40: " + (i * 37 % 400) + " kiện", 260, y, ink); }
            cnv.Restore();
        }
        using var img = SkiaSharp.SKImage.FromBitmap(bmp); using var d = img.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90); File.WriteAllBytes(photo, d.ToArray());
    }
    var scan = main.PageHost.GetVisualDescendants().OfType<ScanView>().First();
    var t = scan.AddFiles(new List<string> { photo, photo }); for (var i = 0; i < 300 && !t.IsCompleted; i++) { Thread.Sleep(30); Pump(); }
    Wait(2500); Snap(main, "51-scan-ket-qua");
    var cropBtn = main.PageHost.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && KhoLanh.App.Controls.Icon.TextOf(b.Content) == "Chỉnh khung cắt");
    cropBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Wait(800); Snap(main, "51-scan-khung-cat");
    var modeBtn = main.PageHost.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(b => b.IsEffectivelyVisible && KhoLanh.App.Controls.Icon.TextOf(b.Content) == "Màu");
    modeBtn.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Wait(300);
    var done = main.PageHost.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && KhoLanh.App.Controls.Icon.TextOf(b.Content) == "Xong chỉnh khung");
    done.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); Wait(2500); Snap(main, "51-scan-mau");
    if (Environment.GetEnvironmentVariable("SHOTS_ONLY") == "tools") { s.Lan.Dispose(); Console.WriteLine("Xong (chỉ công cụ): " + outDir); return; }
}

// Trợ lý hệ thống: quét chạy nền khi mở trang → đợi xong rồi chụp
main.Navigate("troly"); for (var i = 0; i < 130; i++) { Thread.Sleep(30); Pump(); }
Snap(main, "16-tro-ly-he-thong");

foreach (var (key, name) in new[] { ("users", "12-tai-khoan"), ("backup", "13-sao-luu"), ("lan", "15-mang-lan"), ("audit", "14-nhat-ky"), ("m3", "21-tong-hop"), ("m4", "22-ra-dong"), ("m5", "23-doi-chieu"), ("m6", "20-nhap-lieu"), ("m7", "24-vi-tri"), ("m8", "25-so-do-kho"), ("m9", "26-doi-chieu-kiem-ke"), ("m10", "27-khang-sinh"), ("m11", "28-phieu-xuat-he-thong"), ("m12", "29-in-chung-tu-kho"), ("m13", "30-phieu-xuat-kho"), ("m1", "40-ton-kho-an-an"), ("m2", "41-ton-kho-gui") })
{
    // SHOTS_KEYS=m8,m13 → chỉ chụp các module đó (kiểm nhanh một màn hình)
    if (Environment.GetEnvironmentVariable("SHOTS_KEYS") is { Length: > 0 } onlyKeys && !onlyKeys.Split(',').Contains(key)) continue;
    main.Navigate(key);
    Snap(main, name);
    if (key == "lan")
    {
        // Phân vai theo tài khoản (giả lập đã nối Google Sheet ở máy quản trị; không gọi mạng khi chỉ mở trang).
        s.Auth.CreateUser("quanly", "Lê Văn Cường", "matkhau3", Roles.Manager);
        var sec = (Func<string, string>)KhoLanh.Core.Services.Secret.Protect;
        s.Settings.Set("acct_url", "https://script.google.com/macros/s/DEMO/exec");
        s.Settings.Set("acct_token", sec("demo-token"));
        s.Settings.Set("acct_admin_key", sec("demo-admin"));
        var lp = new KhoLanh.Core.Lan.LanPlan { On = true, Key = "demo", Port = s.Lan.Port };
        lp.Vai["admin"] = "chu"; lp.Vai["quanly"] = "duphong";
        lp.Chu = new KhoLanh.Core.Lan.LanPlan.Slot(s.Lan.MachineId, "MAY-CHU-KHO", new() { "192.168.1.10:47820" }, "", "admin");
        lp.DuPhong = new KhoLanh.Core.Lan.LanPlan.Slot("x", "MAY-VAN-PHONG", new() { "192.168.1.11:47820" }, "", "quanly");
        s.Settings.Set("lan_plan", lp.ToJson());
        main.Navigate("audit"); main.Navigate("lan");
        Snap(main, "15b-mang-lan-theo-tai-khoan");
        foreach (var k in new[] { "acct_url", "acct_token", "acct_admin_key", "lan_plan" }) s.Settings.Set(k, "");
    }
    if (key == "backup")
    {
        main.Navigate("ketnoi"); for (var i = 0; i < 40; i++) { Thread.Sleep(30); Pump(); }
        Snap(main, "13b-ket-noi");
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
        var tg = main.PageHost.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(t => KhoLanh.App.Controls.Icon.TextOf(t.Content)?.StartsWith("Bảng chưa gộp") == true);
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
    if (key == "m7")
    {
        var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
        var vtv = (ViTriView)main.PageHost.Content!;
        _ = vtv.OpenPickerForTest(); for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); }
        var pw = vtv.PickerWindow!;
        var g = pw.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.Model.ColCount == 12);
        g.Model.SetCells(new[] { (0, 8, "6"), (1, 8, "x"), (2, 8, "10"), (4, 8, "4"), (6, 8, "3") });
        for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); } g.Select(2, 8); Snap(pw, "24b0-vi-tri-chon-hang");
        pw.Close(); for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
        var dg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
        dg.Select(0, 8); Snap(main, "24-vi-tri");
        tc.SelectedIndex = 1; Pump();
        var sg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible && x.Model.ColCount == 9);
        sg.Select(0, 0); Snap(main, "24b-vi-tri-phieu");
        tc.SelectedIndex = 2; Pump();
        var ag = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible && x.Model.ColCount == 6 && x.Model.Columns[0].Header == "Ngày");
        ag.Select(0, 0); Snap(main, "24c-vi-tri-luu-tru");
        tc.SelectedIndex = 3; Snap(main, "24d-vi-tri-size");
        tc.SelectedIndex = 4; Snap(main, "24e-vi-tri-nap-file");
        var arc = s.ViTri.Archive().Groups.First();
        KhoLanh.App.Views.SlipImageRenderer.Render(s.ViTri.ArchiveItems(arc), $"{KhoLanh.Core.ViTri.ViTriService.Loai.First(l => l.Key == arc.Loai).Title} : {DateTime.Today.AddDays(-1):dd/MM/yyyy}").Save(Path.Combine(outDir, "24f-vi-tri-anh-phieu.png"));
        tc.SelectedIndex = 0;
    }
    if (key == "m8" && main.PageHost.Content is SoDoView sdv)
    {
        var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
        var map = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.WarehouseMap>().First();
        var over = s.SoDo.B.ByCode["TG1.A.01.1.1"];
        sdv.PickForDemo(over); Snap(main, "25-so-do-kho");
        var mq = main.PageHost.GetVisualDescendants().OfType<TextBox>().First(t => t.Watermark?.StartsWith("Tìm trên sơ đồ") == true);
        mq.Text = "31/40"; Snap(main, "25b-so-do-tim"); mq.Text = "";
        tc.SelectedIndex = 1; Pump(); Snap(main, "25c-so-do-danh-sach");
        tc.SelectedIndex = 2; Snap(main, "25d-so-do-chua-co-vi-tri");
        tc.SelectedIndex = 3; Snap(main, "25e-so-do-quy-cach");
        tc.SelectedIndex = 4; Pump();
        var kg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible && x.Model.ColCount == 7);
        kg.Select(1, 2); Snap(main, "25f-so-do-phieu-in");
        var ksub = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(t => t != tc && t.Items.Count == 2 && t.IsEffectivelyVisible);
        ksub.SelectedIndex = 1; for (var i = 0; i < 30; i++) { Thread.Sleep(30); Pump(); }
        var kg2 = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible && x.Model.ColCount == 7);
        Console.WriteLine($"[SĐ] Vị trí trống (ghi tay): {kg2.Model.RowCount} dòng (mọi vị trí theo sơ đồ)");
        Snap(main, "25g-so-do-phieu-in-vi-tri-trong");
        ksub.SelectedIndex = 0; Pump();
        tc.SelectedIndex = 0;
    }
    if (key == "m9")
    {
        var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
        for (var w9 = 0; w9 < 10; w9++) { Thread.Sleep(30); Pump(); } Snap(main, "45-kiem-ke-nhanh");
        tc.SelectedIndex = 1; Pump();
        var g = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
        g.Select(0, 1); Pump(); Snap(main, "26-doi-chieu-kiem-ke");
        tc.SelectedIndex = 2; Pump(); Snap(main, "26b-kiem-ke-so-lieu");
        tc.SelectedIndex = 3; Pump(); Snap(main, "26c-kiem-ke-ke-toan");
        {
            var guiRows = new System.Text.Json.Nodes.JsonArray(new[] { ("VRHLBTN0040IME10046", "L1", "BTP40", 120.0, 12.0), ("VRHLBTN0050IME10023", "TGS-9", "TGS 50", 48.0, 4.0), ("ABC", "", "", 7.0, 1.0) }
                .Select(x => (System.Text.Json.Nodes.JsonNode)KhoLanh.Core.TonKhoGui.GuiStore.RowToDb(new KhoLanh.Core.TonKhoGui.GuiRow { MaHang = x.Item1, TenHang = "TÔM TCT " + x.Item3, SoLo = x.Item2, Size = x.Item3, TrongLuong = x.Item4, SoKien = x.Item5 })).ToArray());
            s.Lan.Write("ton-kho-gui", new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { ["DuLieuChinh"] = guiRows });
            s.KiemKe.SetGuiOn(true);
        }
        tc.SelectedIndex = 4; for (var i = 0; i < 8; i++) { Thread.Sleep(30); Pump(); } Snap(main, "26h-kiem-ke-ton-kho-gui");
        Console.WriteLine($"[KK-GUI] {s.KiemKe.GuiInfo} · auto {s.KiemKe.Gui.NAuto} · bad {s.KiemKe.Gui.Bad.Count} · kg {s.KiemKe.Gui.KgAll}");
        var xp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "kk-bao-cao.xlsx"); s.KiemKe.ExportReport(xp);
        using (var xb = new ClosedXML.Excel.XLWorkbook(xp)) Console.WriteLine("[KK-GUI] sheet Excel: " + string.Join(", ", xb.Worksheets.Select(w => w.Name)));
        tc.SelectedIndex = 5; Pump(); Snap(main, "26d-kiem-ke-vi-tri");
        tc.SelectedIndex = 6; Pump(); Snap(main, "26e-kiem-ke-lenh-dem");
        tc.SelectedIndex = 7; Pump(); Snap(main, "26f-kiem-ke-chua-chot");
        s.KiemKe.DoChot("Kiểm kê cuối tháng 11 — còn lệch đã báo kế toán");
        main.Navigate("m3"); main.Navigate("m9"); for (var i = 0; i < 30; i++) { Thread.Sleep(50); Pump(); }
        tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
        tc.SelectedIndex = 7; Pump();
        var hg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
        hg.Select(0, 0); Pump(); Snap(main, "26g-kiem-ke-da-chot");
        s.KiemKe.MoChot("demo");
    }
    if (key == "m10")
    {   // 1.51: ghi chú dài thu vào nút Hướng dẫn
        var kt = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible);
        kt.SelectedIndex = 4; for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
        var hints = KhoLanh.App.Controls.HintTrim.Collect(main.PageHost);
        Console.WriteLine($"[Ghi chú] Kết quả kháng sinh › Nạp PDF: {hints.Count} ghi chú thu gọn");
        var hf = main.ShowHelp(); for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
        Snap(main, "27z-huong-dan");
        hf.Hide(); kt.SelectedIndex = 0; for (var i = 0; i < 6; i++) { Thread.Sleep(30); Pump(); }
    }
    if (key == "m10")
    {
        for (var i = 0; i < 20; i++) { Thread.Sleep(40); Pump(); }
        var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
        var g = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
        var dayBox = main.PageHost.GetVisualDescendants().OfType<ComboBox>().First(x => x.IsEffectivelyVisible);
        dayBox.SelectedIndex = 1; Pump();
        g.Select(4, 3); Pump(); Snap(main, "27-khang-sinh");
        var q = main.PageHost.GetVisualDescendants().OfType<TextBox>().First(t => t.Watermark?.StartsWith("Tìm nhanh") == true);
        q.Text = "MKS2"; for (var i = 0; i < 10; i++) { Thread.Sleep(40); Pump(); } Snap(main, "27b-khang-sinh-tim-mau-cho"); q.Text = "";
        tc.SelectedIndex = 1; Pump();
        var ins = main.PageHost.GetVisualDescendants().OfType<TextBox>().Where(t => t.IsEffectivelyVisible && t.Watermark == "ND").ToList();
        ins[1].Text = "4.83"; ins[2].Text = "10";
        main.PageHost.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && KhoLanh.App.Controls.Icon.TextOf(b.Content) == "Tra cứu kết quả").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Pump(); Snap(main, "27c-khang-sinh-tra-cuu");
        tc.SelectedIndex = 2; Pump();
        var bg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
        var dd = DateTime.Today.ToString("dd/MM/yyyy");
        bg.Model.SetCells(new[] { (0, 0, dd), (0, 1, "17"), (0, 2, "PHI X2L1"), (1, 0, dd), (1, 1, "18"), (1, 2, "MINH HẢI"), (1, 4, "4.83"), (1, 5, "10"),
            (2, 0, dd), (2, 1, "19"), (2, 5, "30"), (3, 0, dd), (3, 1, "20"), (3, 8, "0.3"), (4, 0, dd), (4, 1, "21"), (4, 7, "12") });
        Pump(); bg.Select(1, 5); Snap(main, "27d-khang-sinh-nhap-lo");
        tc.SelectedIndex = 5; Pump(); Snap(main, "27e-khang-sinh-quy-dinh");
        // Nạp PDF phiếu: phiếu mẫu tự tạo (tests/.../Data/ks-pdf), không phải phiếu thật
        tc.SelectedIndex = 4; Pump();
        var ksv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.KhangSinhView>().First();
        var pdfDir = Path.Combine(FindRepo(), "tests", "KhoLanh.Core.Tests", "Data", "ks-pdf");
        void WaitPdf() { for (var i = 0; i < 60; i++) { Thread.Sleep(50); Pump(); } }
        var tsk = ksv.LoadPdfFiles(new[] { Path.Combine(pdfDir, "lan2-a.pdf") }); while (!tsk.IsCompleted) { Thread.Sleep(30); Pump(); } WaitPdf();
        Snap(main, "27f-khang-sinh-pdf-kiem-lai");
        var qg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
        qg.Select(5, 2); WaitPdf(); Snap(main, "27g-khang-sinh-pdf-mau");
        tsk = ksv.LoadPdfFiles(new[] { Path.Combine(pdfDir, "lan1-a.pdf") }); while (!tsk.IsCompleted) { Thread.Sleep(30); Pump(); } WaitPdf();
        qg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
        qg.Select(1, 2); WaitPdf(); Snap(main, "27h-khang-sinh-pdf-lan1");
        tc.SelectedIndex = 0;
    }
    if (key == "m5")
    {
        var dcv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.DoiChieuView>().First();
        var tcx = dcv.GetVisualDescendants().OfType<TabControl>().First();
        object?[] R(int day, string ma, string ten, double tlg, double sl, string size, string loai, string hd) => new object?[] { "TPC", new DateTime(2026, 10, day).ToOADate(), "5DO-1", "TPC-" + ma, ten, tlg, sl, Math.Round(tlg * sl, 6), "R00", size, loai, "MVS", hd };
        var aoa = new List<object?[]>
        {
            new object?[] { "Báo cáo - Kho" }, new object?[0],
            new object?[] { "Đối tác", "Ngày chứng từ", "Số lệnh", "Mã hàng", "Tên hàng", "TLg Net", "Số lượng tồn", "Net", "Size", "Qui cách đông", "Loại chứng từ", "Đặc tính", "Hợp đồng" },
            R(5, "VTOTICEGIDR00G150CAO1", "Tôm PD luộc đông IQF (0.800 kg - MVSMKS - 26.9.028A-16/09", 3.6, 1, "15gr", "Xuất kiểm hàng", "26.9.028A-16/09"),
            R(5, "VTOTICEGIDR00G150CAO1", "Tôm PD luộc đông IQF (0.800 kg - MVS-ND - 26.9.028A-15/09", 3.6, 1, "15gr", "Xuất kiểm hàng", "26.9.028A-15/09"),
            R(3, "VNTOVCPEMGR00G125EAN1", "Mực ống cắt khoanh IQF", 5, 12, "12.5gr", "Xuất kiểm hàng", "26.10.044-21/09"),
            R(8, "VCPDICEEFXF205160HIH1", "Cá basa fillet", 10, 20, "51/60", "Xuất kiểm hàng", "ISHI-15/09/26"),
            R(3, "VCPDICEEFXF205160HIH1", "Tên C", 8, 1, "51/60", "Xuất xả", "x-1"),
        };
        var xr = KhoLanh.Core.DoiChieu.XuatKiem.Build(aoa);
        var f = typeof(KhoLanh.App.Views.DoiChieuView).GetField("_xk", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!; f.SetValue(dcv, xr);
        typeof(KhoLanh.App.Views.DoiChieuView).GetField("_xkFile", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(dcv, "XK.T10.xlsx");
        typeof(KhoLanh.App.Views.DoiChieuView).GetMethod("XkRender", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(dcv, null);
        tcx.SelectedIndex = 1; for (var i = 0; i < 6; i++) { Thread.Sleep(30); Pump(); }
        Snap(main, "23b-xuat-kiem");
        tcx.SelectedIndex = 0; Pump();
    }
    if (key == "m13") PxScene();
    if (key == "m1")
    {
        // Tồn kho An An (C#): duyệt 6 tab, rồi thử thao tác tạo phiếu xuất + ghi chú Theo Dõi Tồn bằng giao diện thật.
        {   // dữ liệu mẫu có cột "Tình trạng" để có dòng Thành Phẩm Chính (Theo Dõi Tồn / Xuất Hàng)
            var J = System.Text.Json.Nodes.JsonNode.Parse("[]")!.AsArray();
            var hs = new[] { "Tình trạng", "Mã hàng", "Tên hàng", "Đặc tính", "Hợp đồng", "ĐVT", "Tồn cuối", "Trọng lượng" };
            var rowsA = new System.Text.Json.Nodes.JsonArray(Enumerable.Range(0, 12).Select(i => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject
            { ["c0"] = "Thành phẩm", ["c1"] = $"VRHLBTNXECR00{3140 + i}IME1", ["c2"] = $"Tôm thẻ HLSO 31/40 mẫu {i}", ["c3"] = "IQF", ["c4"] = "HD-2611", ["c5"] = "Thùng", ["c6"] = (i < 3 ? 2 + i : 40 + i).ToString(), ["c7"] = "10" }).ToArray());
            var mt = new System.Text.Json.Nodes.JsonObject { ["headers"] = new System.Text.Json.Nodes.JsonArray(hs.Select(h => (System.Text.Json.Nodes.JsonNode)h).ToArray()), ["fileName"] = "TON KHO AN AN 23.11.xlsx", ["dataDate"] = DateTime.Today.ToString("yyyy-MM-dd") };
            s.Lan.Write("ton-kho-an-an", new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { ["LoadData"] = rowsA, ["LoadDataMeta"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["data"] = "__JSON__" + mt.ToJsonString() }) });
            main.Navigate("audit"); main.Navigate("m1"); for (var i = 0; i < 12; i++) { Thread.Sleep(30); Pump(); }
        }
        for (var i = 0; i < 12; i++) { Thread.Sleep(30); Pump(); }
        var tabs = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible);
        for (var t = 0; t < 6; t++) { tabs.SelectedIndex = t; for (var i = 0; i < 8; i++) { Thread.Sleep(30); Pump(); } Snap(main, $"40-an-an-tab{t + 1}"); }
        tabs.SelectedIndex = 5; for (var i = 0; i < 6; i++) { Thread.Sleep(30); Pump(); }
        var inner2 = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible && x != tabs);
        inner2.SelectedIndex = 0; for (var i = 0; i < 6; i++) { Thread.Sleep(30); Pump(); }
        var before = s.AnAn.Slips.Count;
        var grids = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().Where(x => x.IsEffectivelyVisible).ToList();
        var xg = grids.First();
        var srcCb = main.PageHost.GetVisualDescendants().OfType<ComboBox>().First(x => x.IsEffectivelyVisible);
        for (var k = 0; k < 8 && xg.Model.RowCount == 0; k++) { srcCb.SelectedIndex = k; for (var i = 0; i < 6; i++) { Thread.Sleep(30); Pump(); } }
        Console.WriteLine($"[AN-AN] module nguồn có dữ liệu: #{srcCb.SelectedIndex} · {xg.Model.RowCount} dòng");
        var qtyCol = xg.Model.ColCount - 1;
        xg.Select(0, qtyCol); xg.Focus(); Pump();
        main.KeyTextInput("2"); Pump(); main.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null); main.KeyRelease(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        for (var i = 0; i < 8; i++) { Thread.Sleep(30); Pump(); }
        Snap(main, "40-an-an-xuat-hang-go-SL");
        {   // 1.50: tích ô đầu dòng = chọn xuất hết tồn cuối
            var tonCol = Enumerable.Range(0, xg.Model.ColCount).First(c => xg.Model.Columns[c].Header == "Tồn cuối");
            xg.SetChecked(new[] { 1, 2 }, true); for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
            Console.WriteLine($"[AN-AN] tích chọn 2 dòng: SL = {xg.Model.Get(1, qtyCol)} / {xg.Model.Get(2, qtyCol)} (tồn {xg.Model.Get(1, tonCol)} / {xg.Model.Get(2, tonCol)}) · đúng={xg.Model.Get(1, qtyCol) == xg.Model.Get(1, tonCol) && xg.IsChecked(2)}");
            Snap(main, "40-an-an-xuat-hang-tich-chon");
            xg.SetChecked(new[] { 1, 2 }, false); for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
            Console.WriteLine($"[AN-AN] bỏ tích: SL dòng 2 = \"{xg.Model.Get(1, qtyCol)}\" · ô tiêu đề = {xg.MasterState()}");
        }
        var tao = main.PageHost.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && KhoLanh.App.Controls.Icon.TextOf(b.Content) == "Tạo phiếu xuất");
        tao.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
        Console.WriteLine($"[AN-AN] tạo phiếu xuất: {before} -> {s.AnAn.Slips.Count} phiếu");
        Snap(main, "40-an-an-sau-tao-phieu");
        // Theo Dõi Tồn: gõ ghi chú vào cột cuối của dòng đầu → phải lưu
        tabs.SelectedIndex = 3; for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
        var tg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
        var notesBefore = s.AnAn.Tdt.Notes.Count;
        Console.WriteLine($"[AN-AN] Theo Dõi Tồn: {tg.Model.RowCount} dòng hiện");
        if (tg.Model.RowCount > 0)
        {
            tg.Select(0, 9); tg.Focus(); Pump();
            main.KeyTextInput("Cho xuat lo Nhat"); Pump();
            main.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null); main.KeyRelease(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
            for (var i = 0; i < 14; i++) { Thread.Sleep(30); Pump(); }
            Console.WriteLine($"[AN-AN] ghi chú Theo Dõi Tồn: {notesBefore} -> {s.AnAn.Tdt.Notes.Count}");
            Snap(main, "40-an-an-theo-doi-ton-ghi-chu");
        }
        // Hộp thoại: xem trước khi nạp → nạp 2 lần → lịch sử nạp → so sánh 2 lần nạp
        {
            var anv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.AnAnView>().First();
            string MkXlsx(string name, int shift, bool bad)
            {
                var fp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
                using var wbk = new ClosedXML.Excel.XLWorkbook(); var ws = wbk.AddWorksheet("Sheet1");
                var hh = new[] { "Tình trạng", "Mã hàng", "Tên hàng", "Đặc tính", "Hợp đồng", "ĐVT", "Tồn cuối", "Trọng lượng" };
                for (var c = 0; c < hh.Length; c++) ws.Cell(1, c + 1).Value = hh[c];
                for (var i = 0; i < 14; i++)
                {
                    ws.Cell(i + 2, 1).Value = "Thành phẩm"; ws.Cell(i + 2, 2).Value = $"VRHLBTNXECR00{3140 + i + shift}IME1"; ws.Cell(i + 2, 3).Value = $"Tôm thẻ mẫu {i + shift}";
                    ws.Cell(i + 2, 4).Value = "IQF"; ws.Cell(i + 2, 5).Value = "HD-2611"; ws.Cell(i + 2, 6).Value = "Thùng";
                    ws.Cell(i + 2, 7).Value = bad && i == 4 ? "abc" : 20 + i * 3 + shift; ws.Cell(i + 2, 8).Value = 10;
                }
                wbk.SaveAs(fp); return fp;
            }
            async System.Threading.Tasks.Task<bool> Dlg(string name, Func<System.Threading.Tasks.Task> start)
            {
                var t = start(); for (var i = 0; i < 25; i++) { Thread.Sleep(30); Pump(); }
                var w = main.OwnedWindows.FirstOrDefault();
                if (w is null) { Console.WriteLine($"[AN-AN] hộp thoại {name}: KHÔNG mở"); return false; }
                Snap(w, name); Console.WriteLine($"[AN-AN] hộp thoại {name}: mở được ({w.Title})");
                w.Close(); for (var i = 0; i < 25 && !t.IsCompleted; i++) { Thread.Sleep(30); Pump(); }
                await System.Threading.Tasks.Task.CompletedTask; return true;
            }
            var f1 = MkXlsx("an-an-nap1.xlsx", 0, false); var f2 = MkXlsx("an-an-nap2.xlsx", 2, true);
            var pv1 = s.AnAn.Preview(f1);
            var mi = typeof(KhoLanh.App.Views.AnAnView).GetMethod("ReviewDialog", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Dlg("40-an-an-hop-xem-truoc", () => (System.Threading.Tasks.Task)mi.Invoke(anv, new object[] { pv1 })!).GetAwaiter().GetResult();
            s.AnAn.Commit(pv1);
            var pv2 = s.AnAn.Preview(f2);
            Console.WriteLine($"[AN-AN] xem trước file 2: hợp lệ {pv2.Check.Ok} · kiểm tra {pv2.Check.Warn} · lỗi {pv2.Check.Err} · chặn {pv2.Blocking}");
            Dlg("40-an-an-hop-xem-truoc-co-loi", () => (System.Threading.Tasks.Task)mi.Invoke(anv, new object[] { pv2 })!).GetAwaiter().GetResult();
            s.AnAn.Commit(pv2);
            var bt = main.PageHost.GetVisualDescendants().OfType<Button>().ToList();
            tabs.SelectedIndex = 0; for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
            Button B(string pre) => main.PageHost.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && KhoLanh.App.Controls.Icon.TextOf(b.Content)?.Contains(pre) == true);
            Dlg("40-an-an-lich-su-nap", () => { B("Lịch sử nạp").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); return System.Threading.Tasks.Task.CompletedTask; }).GetAwaiter().GetResult();
            Dlg("40-an-an-so-sanh", () => { B("So sánh 2 lần nạp").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); return System.Threading.Tasks.Task.CompletedTask; }).GetAwaiter().GetResult();
            Console.WriteLine($"[AN-AN] lịch sử nạp: {s.AnAn.History().Count} mục · hoàn tác được: {s.AnAn.CanUndo}");
        }
    }
    if (key == "m2")
    {
        // Tồn kho gửi (C#): nạp file mẫu qua dịch vụ rồi duyệt 7 tab + thao tác chính bằng giao diện thật.
        var fp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ton-kho-gui-mau 23.11.xlsx");
        {
            using var wbk = new ClosedXML.Excel.XLWorkbook(); var ws = wbk.AddWorksheet("Sheet1");
            ws.Cell(1, 1).Value = "BÁO CÁO TỒN KHO GỬI";
            var hh = new[] { "Mã hàng", "Tên hàng", "Số lô", "Size", "Quy cách", "Ngày nhập kho", "Phiếu nhập", "Số kiện", "Trọng lượng", "Số kiện", "Trọng lượng" };
            for (var c = 0; c < hh.Length; c++) ws.Cell(2, c + 1).Value = hh[c];
            var gdata = new (string, string, string, string, double, double)[]
            {
                ("VRHLBTNXECR003140IME1", "TÔM TCT BTP50, BLOCK, 1.8Kg x 6, 0083", "L1", "BTP50", 10, 108), ("VRHLBTNXECR003141IME1", "TÔM TCT BTP35, IQF, 9Kg x 1, CKQ", "04.04.26 L12 NÂU", "BTP35", 5, 45),
                ("VRHLBTNXECR003142IME1", "TÔM ECU 41/50, BLOCK, 1.8Kg x 7, ASCN", "ECUADOR-01", "ECU41/50", 8, 100.8), ("VRHLBTNXECR003143IME1", "TÔM TGS 45, BLOCK, 1.8Kg x 6, 0083", "TGS-9", "TGS 45", 3, 32.4),
                ("VRHLBTNXECR003144IME1", "NL 105, BLOCK, 10kg x 1, 0083", "L5", "NL 105", 4, 40), ("VRHLBTNXECR003145IME1", "TÔM GC 45, BLOCK, 1.8Kg x 6, ASCN", "TGC-2", "BTP45", 2, 21.6),
                ("VRHLBTNXECR003146IME1", "TÔM AD 50, BLOCK, 1.8Kg x 6, ASCN", "AD-7", "AĐ 50", 6, 64.8),
            };
            var rr = 3; foreach (var x in gdata) { ws.Cell(rr, 1).Value = x.Item1; ws.Cell(rr, 2).Value = x.Item2; ws.Cell(rr, 3).Value = x.Item3; ws.Cell(rr, 4).Value = x.Item4; ws.Cell(rr, 5).Value = "1.8Kg x 6"; ws.Cell(rr, 6).Value = new DateTime(2026, 11, 5); ws.Cell(rr, 7).Value = "PN" + rr; ws.Cell(rr, 8).Value = 99; ws.Cell(rr, 9).Value = 99; ws.Cell(rr, 10).Value = x.Item5; ws.Cell(rr, 11).Value = x.Item6; rr++; }
            wbk.SaveAs(fp);
        }
        var pvg = s.Gui.Preview(fp); s.Gui.Commit(pvg);
        s.Gui.SaveManual(new[] { ("Tôm EU Nhật", "83.EU-H-M") }); s.Gui.MergeGroup(new[] { "83.EU-H-M" }, "EU");
        main.Navigate("audit"); main.Navigate("m2"); for (var i = 0; i < 14; i++) { Thread.Sleep(30); Pump(); }
        var gtabs = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible);
        for (var t = 0; t < gtabs.ItemCount; t++) { gtabs.SelectedIndex = t; for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); } Snap(main, $"41-gui-tab{t + 1}"); }
        // Phiếu xuất (1.69: tab 3 — Báo Cáo lên tab 2): gõ SL xuất vào dòng đầu rồi tạo phiếu
        gtabs.SelectedIndex = 2; for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
        var pg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
        var before = s.Gui.Slips.Count; var kien0 = s.Gui.D.Rows[0].SoKien;
        Console.WriteLine($"[GUI] phiếu xuất: {pg.Model.RowCount} dòng còn tồn");
        pg.Select(0, pg.Model.ColCount - 1); pg.Focus(); Pump();
        main.KeyTextInput("4"); Pump(); main.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null); main.KeyRelease(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.None, null);
        for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
        Snap(main, "41-gui-px-go-SL");
        pg.SetChecked(new[] { 2 }, true); for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
        Console.WriteLine($"[GUI] tích chọn dòng 3: SL = {pg.Model.Get(2, pg.Model.ColCount - 1)} (số kiện {pg.Model.Get(2, 6)})");
        Snap(main, "41-gui-px-tich-chon");
        pg.SetChecked(new[] { 2 }, false); for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
        var tao = main.PageHost.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && KhoLanh.App.Controls.Icon.TextOf(b.Content) == "Tạo phiếu xuất");
        tao.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); for (var i = 0; i < 25; i++) { Thread.Sleep(30); Pump(); }
        var dlgW = main.OwnedWindows.FirstOrDefault();
        if (dlgW is not null) { Snap(dlgW, "41-gui-px-xac-nhan"); var okb = dlgW.GetVisualDescendants().OfType<Button>().First(b => KhoLanh.App.Controls.Icon.TextOf(b.Content) == "Tạo phiếu"); okb.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); for (var i = 0; i < 30; i++) { Thread.Sleep(30); Pump(); } }
        Console.WriteLine($"[GUI] tạo phiếu: {before} -> {s.Gui.Slips.Count} phiếu · số kiện dòng 1: {kien0} -> {s.Gui.D.Rows[0].SoKien} · hộp xác nhận mở: {(dlgW is not null)}");
        Snap(main, "41-gui-px-ds");
        // Đặc tính: bôi đen dòng chưa gộp rồi gộp
        gtabs.SelectedIndex = 4; for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
        var dgrid = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
        Console.WriteLine($"[GUI] đặc tính: {dgrid.Model.RowCount} dòng");
        var gb = main.PageHost.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.IsEffectivelyVisible && KhoLanh.App.Controls.Icon.TextOf(b.Content) == "Gộp dòng chọn");
        var nGroupedBefore = s.Gui.D.DtGroupMap.Count;
        if (gb is not null && dgrid.Model.RowCount > 0)
        {
            var rowAscn = Enumerable.Range(0, dgrid.Model.RowCount).First(r => dgrid.Model.Get(r, 0) == "ASCN"); dgrid.Select(rowAscn, 0); Pump();
            gb.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); for (var i = 0; i < 12; i++) { Thread.Sleep(30); Pump(); }
        }
        Console.WriteLine($"[GUI] gộp đặc tính: {nGroupedBefore} -> {s.Gui.D.DtGroupMap.Count} đặc tính đã gộp");
        Snap(main, "41-gui-dac-tinh-sau-gop");
        gtabs.SelectedIndex = 1; for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); } Snap(main, "41-gui-bao-cao-sau-gop");
        Console.WriteLine($"[GUI] bảng tổng hợp: {s.Gui.D.ComputePivot().Grand.Tong} kg");
    }
    if (key == "m6")
    {
        var g = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First();
        g.Select(3, 4); g.Select(9, 6, extend: true);
        Snap(main, "20b-nhap-lieu-chon-vung");
        SnapSuggest(main, "20c-nhap-lieu-goi-y");
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
// ---------------- Tổng quan + Lịch xuất + List sản xuất ----------------
{
    var J = System.Text.Json.Nodes.JsonNode.Parse("[]")!.AsArray();
    var hs = new[] { "Tình trạng", "Mã hàng", "Tên hàng", "Size", "Đặc tính", "Hợp đồng", "ĐVT", "Tồn cuối", "Trọng lượng", "Khách hàng", "Thị trường" };
    var t0 = DateTime.Today; string Dd(int d) => t0.AddDays(d).ToString("dd/MM");
    var lines = new List<string[]>
    {
        new[] { "Thành phẩm", "VRHLBTNXEC0016201ME1", "Tôm sú PTO (1kg x 10)", "16/20", "MKS", $"LSX 26.11.020-{Dd(-12)}", "Kiện", "120", "10", "CGC", "Nhật" },
        new[] { "Thành phẩm", "VRHLBTNXEC0021301ME1", "Tôm sú PTO (1kg x 10)", "21/30", "", $"LSX 26.11.020-{Dd(-11)}", "Kiện", "80", "10", "CGC", "Nhật" },
        new[] { "Thành phẩm", "VRHLBTNXEC0031401ME1", "Tôm thẻ HLSO (1.8kg x 6)", "31/40", "", $"LSX 26.11.031-{Dd(-9)}", "Kiện", "650", "10.8", "MATSUOKA", "Nhật" },
        new[] { "Thành phẩm", "PRHLBTNXEC0041501ME1", "Tôm thẻ PD (1kg x 10)", "41/50", "", $"LSX 26.11.044-{Dd(-8)}", "Kiện", "1100", "10", "THUẬN PHƯỚC - ISHI", "Nhật" },
        new[] { "Thành phẩm", "VRHLBTNXEC0026301ME1", "Tôm thẻ PTO (1kg x 10)", "26/30", "", $"LSX 26.11.052-{Dd(-6)}", "Kiện", "900", "10", "YOKOREI", "Nhật" },
        new[] { "Thành phẩm", "VRHLBTNXEC0013151ME1", "Tôm sú HOSO (1kg x 6)", "13/15", "", $"LSX 26.11.060-{Dd(-5)}", "Kiện", "700", "6", "CGC", "Hàn Quốc" },
        new[] { "Thành phẩm", "VRHLBTNXEC0051601ME1", "Tôm thẻ PD (1kg x 10)", "51/60", "", $"LSX 26.11.071-{Dd(-4)}", "Kiện", "1000", "10", "MARUHA", "Nhật" },
    };
    var rowsA = new System.Text.Json.Nodes.JsonArray(lines.Select(r => (System.Text.Json.Nodes.JsonNode)new System.Text.Json.Nodes.JsonObject(r.Select((v, i) => KeyValuePair.Create("c" + i, (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(v))))).ToArray());
    var mt = new System.Text.Json.Nodes.JsonObject { ["headers"] = new System.Text.Json.Nodes.JsonArray(hs.Select(h => (System.Text.Json.Nodes.JsonNode)h).ToArray()), ["fileName"] = "TON KHO AN AN.xlsx", ["dataDate"] = t0.ToString("dd/MM/yyyy") };
    s.Lan.Write("ton-kho-an-an", new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { ["LoadData"] = rowsA, ["LoadDataMeta"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["data"] = "__JSON__" + mt.ToJsonString() }) });
    var lx = s.LichXuat; string D(int d) => KhoLanh.Core.LichXuat.LichXuatCore.Iso(t0.AddDays(d));
    lx.Add(new() { Ngay = D(0), Lsx = "26.11.020", Gio = "08:00", TrangThai = "run", ContSo = "TGHU8812345" });
    var b = lx.Add(new() { Ngay = D(0), Lsx = "26.11.031", Gio = "13:30" });
    lx.Add(new() { Ngay = D(0), Lsx = "26.11.044", Gio = "15:00" });
    lx.Add(new() { Ngay = D(1), Lsx = "26.11.052" });
    lx.Add(new() { Ngay = D(1), Lsx = "26.11.060", ChungVoi = lx.Rows.First(r => r.Lsx.Contains("052")).Id });
    lx.Add(new() { Ngay = D(3), Lsx = "26.11.071" });
    foreach (var (d, l) in new[] { (-1, "26.11.003"), (-1, "26.11.008"), (-2, "26.10.099"), (-4, "26.10.090"), (-6, "26.10.081") }) lx.Add(new() { Ngay = D(d), Lsx = l, TrangThai = "ok" });
    lx.ViecAdd(b.Id, "Chờ chứng thư kiểm dịch");
    lx.ViecAdd(lx.Rows.First(r => r.Lsx.Contains("052")).Id, "Dán nhãn phụ tiếng Nhật");
    lx.PendAdd("26.12.004", "Khách đổi quy cách thùng — xác nhận trước khi làm List");
    lx.CreatePhieu(lx.Rows.Where(r => r.Lsx.Contains("020")).Select(r => r.Id));
    lx.CreateLists(lx.Rows.Where(r => r.Lsx.Contains("031")).Select(r => r.Id));
    main.Navigate("audit"); main.Navigate("lx"); for (var i = 0; i < 100; i++) { Thread.Sleep(30); Pump(); }
    Snap(main, "05-tong-quan");
    var tq = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible);
    tq.SelectedIndex = 1; for (var i = 0; i < 15; i++) { Thread.Sleep(30); Pump(); } Snap(main, "05b-lich-xuat-thang");
    tq.SelectedIndex = 2; for (var i = 0; i < 15; i++) { Thread.Sleep(30); Pump(); } Snap(main, "05c-thong-ke-xuat");
    main.Show("m13", new PhieuKhoView(1, null, "26.11.031")); for (var i = 0; i < 80; i++) { Thread.Sleep(30); Pump(); }
    Snap(main, "30c-list-san-xuat");
}
// ---------------- NXT Bột/Sốt (Sổ kho) ----------------
{
    var sk = s.SoKho; sk.Load(); var t0 = DateTime.Today; string D(int d) => KhoLanh.Core.SoKho.SoKhoCore.Iso(t0.AddDays(d));
    System.Text.Json.Nodes.JsonObject L(string loai, int d, string vt, string ma, double sl, double netDv, string hd = "", string han = "", string ct = "") =>
        new() { ["loai"] = loai, ["ngay"] = D(d), ["viTri"] = vt, ["maHang"] = ma, ["size"] = "", ["soLuong"] = sl, ["netDv"] = netDv, ["hopDong"] = hd, ["hanDung"] = han, ["chungTu"] = ct };
    var add = new[]
    {
        L("nhap", -20, "B1", "BOT-CHIEN-XU", 40, 25, han: D(60), ct: "NK-0311-01"), L("nhap", -20, "B2", "BOT-NHUNG", 30, 20, han: D(12), ct: "NK-0311-01"),
        L("nhap", -15, "S1", "SOT-TERIYAKI", 50, 10, han: D(150), ct: "NK-0811-01"), L("nhap", -10, "B1", "BOT-CHIEN-XU", 20, 25, han: D(90), ct: "NK-1311-01"),
        L("xuat", -8, "B1", "BOT-CHIEN-XU", 12, 25, "LSX 26.11.020", ct: "BS-1511-01"), L("xuat", -8, "S1", "SOT-TERIYAKI", 8, 10, "LSX 26.11.020", ct: "BS-1511-01"),
        L("xuat", -4, "B2", "BOT-NHUNG", 6, 20, "LSX 26.11.031", ct: "BS-1911-01"), L("xuat", -1, "B1", "BOT-CHIEN-XU", 15, 25, "LSX 26.11.044", ct: "BS-2211-01"),
    }.Select(KhoLanh.Core.SoKho.SoKhoCore.NormLine).ToList();
    sk.Commit(add, Array.Empty<string>(), Array.Empty<KhoLanh.Core.SoKho.SkLine>(), "nhap", "Dữ liệu mẫu");
    sk.SaveNorms(new[] { new KhoLanh.Core.SoKho.SoKhoCore.Norm("", "", "BOT-CHIEN-XU", "", 3, "kg/100kg", "") });
    sk.SaveOutputs(new[] { new KhoLanh.Core.SoKho.SoKhoCore.Output("", "LSX 26.11.020", 9000, "CGC", ""), new KhoLanh.Core.SoKho.SoKhoCore.Output("", "LSX 26.11.044", 12000, "MARUHA", "") });
    main.Navigate("m3sk"); for (var i = 0; i < 80; i++) { Thread.Sleep(30); Pump(); }
    var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible);
    tc.SelectedIndex = 1; for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); } Snap(main, "35-nxt-bot-sot-phieu-xuat");
    tc.SelectedIndex = 3; for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); } Snap(main, "35b-nxt-bot-sot-ton-kho");
    tc.SelectedIndex = 5; for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); } Snap(main, "35c-nxt-bot-sot-dinh-muc");
    tc.SelectedIndex = 6; for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); } Snap(main, "35d-nxt-bot-sot-so-cai");
    // mẫu cột: đổi tên Ghi chú, ẩn Hạn dùng, thêm cột phụ "Người giao" cho phiếu nhập
    {
        var tp0 = KhoLanh.Core.SoKho.SkTemplate.Default();
        var cols = tp0.Columns.Select(c => new System.Text.Json.Nodes.JsonObject { ["key"] = c.Key, ["label"] = c.Key == "ghiChu" ? "Ghi chú kho" : c.Label, ["hidden"] = c.Key == "hanDung", ["w"] = c.W }).ToList();
        cols.Add(new System.Text.Json.Nodes.JsonObject { ["key"] = "x_nguoi_giao", ["label"] = "Người giao", ["type"] = "text", ["w"] = 140 });
        sk.SaveTemplates(new[] { tp0, KhoLanh.Core.SoKho.SkTemplate.Norm("", "Mẫu phiếu nhập", "nhap", false, cols) });
    }
    main.Navigate("lx"); main.Navigate("m3sk"); for (var i = 0; i < 60; i++) { Thread.Sleep(30); Pump(); }
    tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible);
    tc.SelectedIndex = 7; for (var i = 0; i < 30; i++) { Thread.Sleep(30); Pump(); }
    { var cb = tc.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault(c => c.IsEffectivelyVisible && c.ItemCount == 4); if (cb is not null) cb.SelectedIndex = 1; for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); } }
    Snap(main, "35e-nxt-bot-sot-mau-cot");
    tc.SelectedIndex = 0; for (var i = 0; i < 30; i++) { Thread.Sleep(30); Pump(); } Snap(main, "35f-nxt-bot-sot-phieu-theo-mau");
    // Vị Trí Bột: đưa tồn theo vị trí lên, gõ SL xuất, tạo 1 phiếu
    s.Vtb.Push(KhoLanh.Core.SoKho.VtbService.FromLedger(sk.Lines, D(0)), D(0));
    s.Vtb.Load(); s.Vtb.CreateSlip(new[] { new KhoLanh.Core.SoKho.VtbService.Pick(0, 2) }, "Xuất cho line chiên");
    s.Vtb.ApplyPhone(new[] { new KhoLanh.Core.SoKho.VtbService.PhonePhieu("dt-1", "DT-2411-01", DateTime.UtcNow.ToString("o"), "kho1", "Xuất từ điện thoại", new() { new KhoLanh.Core.SoKho.VtbService.PhoneItem(KhoLanh.Core.SoKho.VtbService.Keys(s.Vtb.Rows)[1], 3) }) });
    main.Navigate("m1vtb"); for (var i = 0; i < 40; i++) { Thread.Sleep(30); Pump(); }
    var vg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
    vg.Model.Set(1, 3, "x"); for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); }
    Snap(main, "37-vi-tri-bot");
    Console.WriteLine($"[VTB] {s.Vtb.Rows.Count} dòng, {s.Vtb.Slips.Count} phiếu");
}
// ---------------- Xử lý PDF & Excel ----------------
{
    // chạy thử thao tác PDF / Excel thật
    {
        var img = new KhoLanh.Core.Scan.RgbaImage(400, 560, Enumerable.Repeat((byte)255, 400 * 560 * 4).ToArray());
        for (var y = 100; y < 140; y++) for (var xx = 50; xx < 350; xx++) { var p = (y * 400 + xx) * 4; img.Px[p] = img.Px[p + 1] = img.Px[p + 2] = 0; }
        var pg = new KhoLanh.Core.Scan.JpegPdf.Page(KhoLanh.App.Controls.ScanImaging.EncodeJpeg(img, 80), 400, 560, false);
        var blank = new KhoLanh.Core.Scan.JpegPdf.Page(KhoLanh.App.Controls.ScanImaging.EncodeJpeg(new KhoLanh.Core.Scan.RgbaImage(400, 560, Enumerable.Repeat((byte)255, 400 * 560 * 4).ToArray()), 80), 400, 560, false);
        var a = KhoLanh.Core.Scan.JpegPdf.Build(new[] { pg, blank, pg }.ToList(), true, "A");
        var m = KhoLanh.App.Controls.PdfRender.Merge(new List<(byte[], string)> { (a, "a"), (a, "b") });
        var ex = KhoLanh.App.Controls.PdfRender.Extract(m, "m", new List<int> { 0, 2 });
        var rt = KhoLanh.App.Controls.PdfRender.Rotate(m, "m", new List<int> { 0 }, 90);
        var st = KhoLanh.App.Controls.PdfRender.Stamp(m, "m", new KhoLanh.App.Controls.PdfRender.StampOpts { Number = true, NumFmt = "Trang {n}/{N}", Watermark = true, WmText = "BẢN SAO" });
        var bl = KhoLanh.App.Controls.PdfRender.BlankPages(m, "m", 0.003);
        var dl = KhoLanh.App.Controls.PdfRender.Delete(m, "m", bl);
        var (rw, rh, _) = KhoLanh.App.Controls.PdfRender.RenderRgbaDpi(st, 0, 72);
        Console.WriteLine($"[PDF] gộp {KhoLanh.App.Controls.PdfRender.Count(m, "m")} trang, tách {KhoLanh.App.Controls.PdfRender.Count(ex, "e")}, xoay {KhoLanh.App.Controls.PdfRender.Count(rt, "r")}, dấu {KhoLanh.App.Controls.PdfRender.Count(st, "s")} ({rw}x{rh}), trang trắng [{string.Join(",", bl)}] → còn {KhoLanh.App.Controls.PdfRender.Count(dl, "d")}");
        File.WriteAllBytes(Path.Combine(outDir, "smoke-stamp.pdf"), st);
        var xf = Path.Combine(outDir, "smoke.xlsx");
        KhoLanh.Core.Bang.ExcelOps.Write(xf, new[] { ("A", new KhoLanh.Core.Bang.CcTable(new[] { "Mã", "SL" }, new[] { new object?[] { " a1 ", "1,234" }, new object?[] { "b2", 5.0 } })) });
        var rd = KhoLanh.Core.Bang.ExcelOps.Read(xf); var tb = KhoLanh.Core.Bang.ExcelOps.ToTable(rd[0]);
        var cl = KhoLanh.Core.Bang.CcBang.CleanTable(tb, new KhoLanh.Core.Bang.CcBang.CleanOpts { Trim = true, NumAll = true });
        Console.WriteLine($"[XLS] {rd.Count} sheet, {tb.Rows.Count} dòng, sửa {cl.Log.Count} ô: {string.Join(" | ", cl.Table.Rows.Select(r => string.Join(";", r)))}");
    }
    main.Navigate("m21"); for (var i = 0; i < 40; i++) { Thread.Sleep(30); Pump(); }
    var lb = main.PageHost.GetVisualDescendants().OfType<ListBox>().First(x => x.IsEffectivelyVisible);
    lb.SelectedIndex = 3; for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); } Snap(main, "38-xu-ly-pdf");
    lb.SelectedIndex = 2; for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); }
    { var xv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.XuLyTepView>().First(); var t = xv.LoadPdfForTest(Path.Combine(outDir, "smoke-stamp.pdf")); while (!t.IsCompleted) { Thread.Sleep(30); Pump(); } for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); } }
    Snap(main, "38c-xu-ly-pdf-xem-truoc");
    var tcx = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible);
    tcx.SelectedIndex = 1; for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); }
    var lb2 = main.PageHost.GetVisualDescendants().OfType<ListBox>().First(x => x.IsEffectivelyVisible);
    lb2.SelectedIndex = 2; for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); } Snap(main, "38b-xu-ly-excel");
}
// ---------------- So sánh & Tra cứu bảng ----------------
{
    var T = (string[] h, object?[][] r) => new KhoLanh.Core.Bang.CcTable(h, r);
    var fa = Path.Combine(outDir, "_ton-kho-an-an.xlsx"); var fb = Path.Combine(outDir, "_ton-ke-toan.xlsx");
    KhoLanh.Core.Bang.ExcelOps.Write(fa, new[] { ("Tồn", T(new[] { "Mã hàng", "Tên hàng", "Size", "Tồn (kg)" }, new[] {
        new object?[] { "CA-BASA-FL", "Cá basa fillet", "120-170", 18250.0 }, new object?[] { "TOM-SU-HL", "Tôm sú HLSO", "16-20", 4200.0 },
        new object?[] { "MUC-ONG", "Mực ống làm sạch", "U10", 3100.0 }, new object?[] { "CA-TRA-CAT", "Cá tra cắt khúc", "", 9650.5 },
        new object?[] { "BACHTUOC", "Bạch tuộc cắt", "40-60", 760.0 }, new object?[] { "TOM-THE-PD", "Tôm thẻ PD", "31-40", 5520.0 } })) });
    KhoLanh.Core.Bang.ExcelOps.Write(fb, new[] { ("KT", T(new[] { "Mã hàng", "Tên hàng", "Tồn (kg)", "Đơn giá" }, new[] {
        new object?[] { "CA-BASA-FL", "Cá basa fillet", 18250.0, 52000.0 }, new object?[] { "TOM-SU-HL", "Tôm sú HLSO", 4150.0, 215000.0 },
        new object?[] { "CA-TRA-CAT", "Cá tra cắt khúc", 9650.5, 38000.0 }, new object?[] { "MUC-ONG", "Mực ống làm sạch", 3100.0, 98000.0 },
        new object?[] { "GHE-XANH", "Ghẹ xanh nguyên con", 410.0, 260000.0 }, new object?[] { "TOM-THE-PD", "Tôm thẻ PD", 5502.0, 168000.0 } })) });
    main.Navigate("m22"); for (var i = 0; i < 40; i++) { Thread.Sleep(30); Pump(); }
    var sv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.SoSanhBangView>().First();
    sv.SetSide('A', fa, KhoLanh.Core.Bang.ExcelOps.Read(fa)); sv.SetSide('B', fb, KhoLanh.Core.Bang.ExcelOps.Read(fb));
    for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
    sv.RunCompare(); for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); }
    sv.ShowCmp("all");
    for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); } Snap(main, "39-so-sanh-bang");
    var tcs = sv.GetVisualDescendants().OfType<TabControl>().First(); tcs.SelectedIndex = 1; for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
    sv.RunLookup(new[] { 3 }); for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); } Snap(main, "39b-tra-cuu-bang");
    File.Delete(fa); File.Delete(fb);
}
// ---------------- Giao diện Tối + cỡ chữ ----------------
{
    KhoLanh.App.AppTheme.Apply(KhoLanh.App.AppTheme.Mode.Dark, 1.0);
    foreach (var (key, name) in new[] { ("lx", "40-giao-dien-toi-tong-quan"), ("m3sk", "40b-giao-dien-toi-nxt"), ("m7", "40c-giao-dien-toi-vi-tri"), ("m22", "40d-giao-dien-toi-so-sanh") })
    { main.Navigate(key); for (var i = 0; i < 60; i++) { Thread.Sleep(30); Pump(); } Snap(main, name); }
    KhoLanh.App.AppTheme.Apply(KhoLanh.App.AppTheme.Mode.Light, 1.0);
    main.Navigate("lx"); for (var i = 0; i < 60; i++) { Thread.Sleep(30); Pump(); } Snap(main, "40e-giao-dien-sang-diu");
}
// ---------------- Tìm nhanh toàn app (Ctrl + K) ----------------
{
    var sw = main.OpenSearch(); sw.LoadNow();
    sw.SetQuery("VRHLBTNXECR003140"); for (var i = 0; i < 30; i++) { Thread.Sleep(30); Pump(); }
    sw.SelectRow(0); for (var i = 0; i < 30; i++) { Thread.Sleep(30); Pump(); } Snap(sw, "41-tim-nhanh");
    sw.SetQuery("bot"); for (var i = 0; i < 30; i++) { Thread.Sleep(30); Pump(); } Snap(sw, "41b-tim-nhanh-module");
    sw.SetQuery("26.11"); for (var i = 0; i < 30; i++) { Thread.Sleep(30); Pump(); } Snap(sw, "41c-tim-nhanh-phieu");
    sw.Close(); for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
}
// ---------------- Đồng bộ điện thoại ----------------
{
    s.PhoneSync.Pick = new HashSet<string> { "M01", "M01VT", "M01VTB", "M02", "M03" };
    main.Navigate("phone"); for (var i = 0; i < 40; i++) { Thread.Sleep(30); Pump(); } Snap(main, "42-dong-bo-dien-thoai");
}
// ---------------- Tự tạo List / Phiếu theo Lịch xuất ----------------
{
    s.LichXuat.Auto = true; s.LichXuat.AutoMode = "phieu";
    try
    {
        s.LichXuat.Load();
        var d1 = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd"); var d2 = DateTime.Today.AddDays(3).ToString("yyyy-MM-dd");
        s.LichXuat.Add(new KhoLanh.Core.LichXuat.LichXuatService.Input { Ngay = d1, Lsx = "26.11.088", ContSo = "TGHU 889120-4" });
        s.LichXuat.Add(new KhoLanh.Core.LichXuat.LichXuatService.Input { Ngay = d2, Lsx = "26.12.140" });
        Console.WriteLine("[AUTO] " + s.PhieuKho.Lists.Count + " List");
    }
    catch (Exception e) { Console.WriteLine("[AUTO] lỗi: " + e.Message); }
    s.LichXuat.AutoMode = "list";
    main.Show("m13", new PhieuKhoView(3)); for (var i = 0; i < 60; i++) { Thread.Sleep(30); Pump(); } Snap(main, "43-tu-dong-da-tao");
}
// ---------------- Danh mục mã hàng ----------------
DmScene();
s.Lan.Dispose();
Console.WriteLine("Xong: " + outDir);

void PxScene()
{
    void W(int n) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    var bfx = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
    var pkv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.PhieuKhoView>().First();
    var top = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible);
    TabControl Sub() => main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible && x != top);
    KhoLanh.App.Controls.SheetGrid Grid() => main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
    void Fail(string m) { Console.WriteLine("[PX] LỖI: " + m); Environment.Exit(1); }
    // 1.88: khung soạn phiếu không được nằm chồng lên hàng tên trang ("Phiếu xuất kho" đè nút ← Danh sách)
    void NoOverlap(string what)
    {
        W(8);
        var band = main.FindControl<Border>("PageBand")!; var full = pkv.FullContent!;
        var bandBottom = band.TranslatePoint(new Point(0, band.Bounds.Height), main)!.Value.Y;
        var y = full.TranslatePoint(new Point(0, 0), main)!.Value.Y;
        Console.WriteLine($"[PX] {what}: khung soạn y = {y:0}, đáy hàng tên trang = {bandBottom:0}");
        if (y < bandBottom - 0.5) Fail(what + ": khung soạn phiếu chồng lên tên trang");
    }
    double ow = main.Width, oh = main.Height; main.Width = 1366; main.Height = 768; W(10);   // màn hình nhỏ như máy người dùng
    // 1.82: danh sách nhóm theo tháng (dòng 0 = dòng tháng) → bấm đúp phiếu → khung soạn phiếu chiếm cả trang (phương án B)
    top.SelectedIndex = 0; W(10);
    var g = Grid(); W(4);
    Console.WriteLine($"[PX] danh sách xuất khẩu: dòng 0 = “{g.Model.Get(0, 0)}” · {g.Model.Get(0, 1)}");
    if (!g.Model.Get(0, 0).Contains("Tháng")) Fail("dòng đầu không phải dòng nhóm tháng");
    g.Select(1, 0); W(4); Snap(main, "30-phieu-xuat-khau-danh-sach");
    var rows0 = g.Model.RowCount; g.RowClicked!(0); W(6);
    Console.WriteLine($"[PX] bấm dòng tháng: {rows0} → {g.Model.RowCount} dòng (thu lại)");
    if (g.Model.RowCount >= rows0) Fail("bấm dòng tháng không thu lại");
    Snap(main, "30a-phieu-xuat-khau-thu-thang");
    g.RowClicked!(0); W(6);
    if (g.Model.RowCount != rows0) Fail("bấm dòng tháng lần 2 không mở lại");
    g.Select(1, 0); g.RaiseEvent(new Avalonia.Input.TappedEventArgs(Avalonia.Input.Gestures.DoubleTappedEvent, null!)); W(14);
    if (!pkv.EditorOpen) Fail("bấm đúp phiếu không mở khung soạn phiếu");
    var eg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible && x.Model.ColCount == 9);
    Console.WriteLine($"[PX] soạn phiếu xuất khẩu: lưới mặt hàng cao {eg.Bounds.Height:0} px, rộng {eg.Bounds.Width:0} px (cửa sổ {main.Bounds.Width:0}×{main.Bounds.Height:0})");
    if (eg.Bounds.Height < 0.38 * main.Bounds.Height) Fail("lưới mặt hàng còn thấp");
    NoOverlap("soạn phiếu xuất khẩu");
    Snap(main, "30b-phieu-xuat-khau-nhap-lieu");
    var closeX = (Func<Task>)Delegate.CreateDelegate(typeof(Func<Task>), pkv, pkv.GetType().GetMethod("CloseEditor", bfx)!);
    _ = closeX(); W(10);
    if (pkv.EditorOpen) Fail("← Danh sách không đóng phiếu");
    W(10); if (main.PageTabs is null) Fail("đóng phiếu: tab đầu trang không kéo lên lại hàng tên trang");
    Console.WriteLine("[PX] ← Danh sách: đã về danh sách");
    // Phiếu xuất nội địa
    top.SelectedIndex = 1; W(14);   // 1.96: Phiếu xuất nội địa là tab thứ 2
    var sub2 = Sub(); sub2.SelectedIndex = 0; W(8);
    var cgv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.CargoView>().First(x => x.IsEffectivelyVisible);
    var g2 = Grid(); g2.Select(1, 0); W(4); Snap(main, "31-phieu-noi-dia-danh-sach");
    g2.RaiseEvent(new Avalonia.Input.TappedEventArgs(Avalonia.Input.Gestures.DoubleTappedEvent, null!)); W(14);
    if (!pkv.EditorOpen) Fail("bấm đúp cont không mở khung soạn phiếu");
    Snap(main, "31b-phieu-noi-dia-nhap-lieu");
    NoOverlap("soạn phiếu nội địa");
    {   // lưới mục hàng Cargo luôn có dòng trống; gõ dòng trống cuối thì thêm dòng · KIỆN / DÃY + gõ Tổng đổ đầy
        var cgg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible && x.Model.ColCount > 12 && x.Model.Columns[0].Header != "");
        int n0 = cgg.Model.RowCount, t0 = cgg.TotalRows; var h0 = cgg.Bounds.Height;
        Console.WriteLine($"[CARGO] lưới mục hàng cao {h0:0} px (cửa sổ {main.Bounds.Height:0})");
        if (h0 < 0.3 * main.Bounds.Height) Fail("lưới mục hàng còn thấp");
        cgg.Model.Set(t0 - 1, 2, "01.08 L9"); W(4);   // gõ vào dòng trống cuối cùng
        Console.WriteLine($"[CARGO] dòng: {n0} có số liệu, hiện {t0}; gõ dòng {t0} → {cgg.Model.RowCount} dòng, hiện {cgg.TotalRows}");
        if (t0 < KhoLanh.App.Views.CargoView.MinRows || cgg.TotalRows < cgg.Model.RowCount + KhoLanh.App.Views.CargoView.MinSpare) Fail("thiếu dòng trống");
        Snap(main, "31b2-phieu-noi-dia-them-dong");
        cgg.Model.Undo(); W(2);
        var cap = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible && x.Model.Columns[0].Header == "" && x.Model.ColCount > 12);
        cap.Model.SetCells(Enumerable.Range(1, 20).Select(d => (0, 1 + d, "63")).ToList()); W(3);
        cgg.Model.Set(0, 9, "150"); W(4);
        Console.WriteLine($"[CARGO] KIỆN/DÃY 20×63, gõ Tổng 150 → dãy 1–4: {string.Join(", ", Enumerable.Range(10, 4).Select(c => cgg.Model.Get(0, c)))}");
        Snap(main, "31b3-phieu-noi-dia-kien-day");
        cgg.Model.Undo(); cap.Model.Undo(); W(2);
        // 1.88: Ctrl+D ở hàng KIỆN / DÃY (1 dòng) = chép ô đầu vùng chọn sang các ô còn lại; Ctrl+R như Excel
        cap.Model.Set(0, 2, "55"); cap.SelectRange(new KhoLanh.Core.Grid.CellRange(0, 2, 0, 8)); cap.Focus(); W(2);
        cap.OnKey(new Avalonia.Input.KeyEventArgs { Key = Avalonia.Input.Key.D, KeyModifiers = Avalonia.Input.KeyModifiers.Control, RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent }); W(3);
        var capVals = string.Join(",", Enumerable.Range(2, 8).Select(c => cap.Model.Get(0, c)));
        Console.WriteLine($"[CARGO] Ctrl+D dãy 1–7: {capVals}");
        if (capVals != "55,55,55,55,55,55,55,") Fail("Ctrl+D hàng KIỆN / DÃY không chép sang");
        Snap(main, "31b4-kien-day-ctrl-d");
        cap.Model.Undo(); cap.Model.Set(0, 9, "40"); cap.SelectRange(new KhoLanh.Core.Grid.CellRange(0, 9, 0, 11)); W(2);
        cap.OnKey(new Avalonia.Input.KeyEventArgs { Key = Avalonia.Input.Key.R, KeyModifiers = Avalonia.Input.KeyModifiers.Control, RoutedEvent = Avalonia.Input.InputElement.KeyDownEvent }); W(3);
        if (cap.Model.Get(0, 11) != "40") Fail("Ctrl+R không chép sang phải");
        cap.Model.Undo(); cap.Model.Undo(); cap.Model.Undo(); W(2);
        // 1.88: gõ Size-quy cách sai / không có tồn → chỉ cảnh báo (ô Loại + Size-quy cách tô màu, khung phải "Cần xử lý")
        var r0 = cgg.Model.RowCount;
        cgg.Model.SetCells(new List<(int, int, string)> { (r0, 0, "BTP"), (r0, 1, "0390-6"), (r0 + 1, 0, "BTP"), (r0 + 1, 1, "12x") }); W(6);
        var warn = (System.Collections.IDictionary)typeof(KhoLanh.App.Views.CargoView).GetField("_warn", bfx)!.GetValue(cgv)!;
        var info = ((TextBlock)typeof(KhoLanh.App.Views.CargoView).GetField("_stockInfo", bfx)!.GetValue(cgv)!).Text ?? "";
        Console.WriteLine($"[CARGO] cảnh báo {warn.Count} dòng: {info.Replace("\n", " | ")}");
        if (warn.Count < 2 || cgg.CellBackground!(r0, 1) is null || cgg.CellTip!(r0 + 1, 1) is null) Fail("không cảnh báo Size-quy cách sai / không có tồn");
        Snap(main, "31b5-canh-bao-size-quy-cach");
        cgg.Model.Undo(); W(2);
    }
    var pvb = main.PageHost.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>().First(x => x.IsEffectivelyVisible && x.Content as string == "Xem trước");
    pvb.IsChecked = true; W(6); Snap(main, "31c-phieu-noi-dia-xem-truoc"); pvb.IsChecked = false; W(2);
    typeof(KhoLanh.App.Views.CargoView).GetField("_dirty", bfx)!.SetValue(cgv, false);
    var closeC = (Func<Task>)Delegate.CreateDelegate(typeof(Func<Task>), cgv, typeof(KhoLanh.App.Views.CargoView).GetMethod("CloseEditor", bfx)!);
    _ = closeC(); W(10);
    if (pkv.EditorOpen) Fail("← Danh sách không đóng cont");
    sub2 = Sub(); sub2.SelectedIndex = 1; W(6); Snap(main, "31d-phieu-noi-dia-danh-muc");
    sub2.SelectedIndex = 0; top.SelectedIndex = 0; Pump();
    main.Width = ow; main.Height = oh; W(6);
}

void DmScene()
{
    s.TongHop.Load();
    var th = System.Text.Json.Nodes.JsonNode.Parse(s.Lan.Read("tong-hop", "MaDaTao")) as System.Text.Json.Nodes.JsonArray ?? new System.Text.Json.Nodes.JsonArray();
    var codes = th.OfType<System.Text.Json.Nodes.JsonObject>().Select(o => o["Mã"]?.ToString() ?? "").Where(c => c.Length == 21).Take(3).ToList();
    if (codes.Count > 0)
    {
        var typo = codes[0][..13] + "9" + codes[0][14..];   // 1 ký tự size gõ sai
        th.Add(new System.Text.Json.Nodes.JsonObject { ["STT"] = th.Count + 1, ["Mã"] = codes[0], ["Ghi chú"] = "dòng trùng" });
        th.Add(new System.Text.Json.Nodes.JsonObject { ["STT"] = th.Count + 1, ["Mã"] = typo, ["Ghi chú"] = "" });
        th.Add(new System.Text.Json.Nodes.JsonObject { ["STT"] = th.Count + 1, ["Mã"] = "ABC123", ["Ghi chú"] = "mã thiếu ký tự" });
        s.Lan.Write("tong-hop", new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { ["MaDaTao"] = th });
    }
    main.Navigate("m3dm"); for (var i = 0; i < 80; i++) { Thread.Sleep(30); Pump(); }
    var dv = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Views.DanhMucMaView>().First();
    var ds = s.DanhMuc;
    Console.WriteLine($"  [DM] {ds.Rows.Count} mã · cần xử lý {ds.XlNow().Count} (thiếu {ds.XlNow().Missing.Count}, trùng {ds.XlNow().Dups.Count}, gõ sai {ds.XlNow().Near.Count})");
    var dq = dv.GetVisualDescendants().OfType<TextBox>().First(); dq.Text = codes.Count > 0 ? codes[0][..6] : "";
    var dcb = dv.GetVisualDescendants().OfType<ComboBox>().First(); dcb.SelectedIndex = 1; dcb.SelectedIndex = 0;
    for (var i = 0; i < 30; i++) { Thread.Sleep(30); Pump(); } Snap(main, "44-danh-muc-ma-hang");
    var dt = dv.GetVisualDescendants().OfType<TabControl>().First(); dt.SelectedIndex = 1; for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); } Snap(main, "44b-danh-muc-can-xu-ly");
    dt.SelectedIndex = 0; dq.Text = ""; for (var i = 0; i < 120; i++) { Thread.Sleep(30); Pump(); }
    FilterScene();
}

// Lọc theo cột (1.49): dòng tìm kiếm riêng từng cột + danh sách lọc phân tầng ở nút ▾ tiêu đề — kiểm trên bảng thật (Danh mục mã hàng).
void FilterScene()
{
    var g = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().Where(x => x.IsEffectivelyVisible).OrderByDescending(x => x.Model.RowCount).First();
    var total = g.Model.RowCount;
    var tc = Enumerable.Range(0, g.Model.ColCount).FirstOrDefault(c => g.Model.Columns[c].Type != "number" && Enumerable.Range(0, Math.Min(total, 50)).Count(r => g.Model.Get(r, c).Length > 3) > 10);
    var word = Enumerable.Range(0, total).Select(r => g.Model.Get(r, tc)).Where(v => v.Length > 3).Select(v => v.Split(' ')[0]).GroupBy(v => v).OrderByDescending(x => x.Count()).First().Key;
    g.SetFilter(tc, word[..Math.Min(4, word.Length)].ToLowerInvariant());
    for (var i = 0; i < 20; i++) { Thread.Sleep(30); Pump(); }
    var shown = g.ShownRows.Count;
    var okRows = g.ShownRows.All(r => KhoLanh.Core.Grid.GridFilter.MatchText(g.CellText(r, tc), word[..Math.Min(4, word.Length)]));
    g.Select(g.ShownRows.Count > 1 ? g.ShownRows[1] : 0, tc);
    var selOk = g.Row == (g.ShownRows.Count > 1 ? g.ShownRows[1] : 0);
    Console.WriteLine($"  [Lọc] cột \"{g.Model.Columns[tc].Header}\" chứa \"{word[..Math.Min(4, word.Length)]}\": {shown}/{total} dòng · đúng={okRows} · chọn dòng dữ liệu={selOk}");
    Snap(main, "45-loc-theo-cot");
    var lc = Enumerable.Range(0, g.Model.ColCount).Where(c => c != tc && g.Model.Columns[c].Width >= 46)
        .OrderBy(c => g.ShownRows.Select(r => g.CellText(r, c)).Distinct().Count() is var n && n > 1 ? n : 9999).First();
    g.Col = lc; g.OpenFilterMenu(lc);
    for (var i = 0; i < 30; i++) { Thread.Sleep(30); Pump(); }
    Snap(main, "45b-loc-danh-sach-phan-tang");
    foreach (var w in Avalonia.Controls.Primitives.OverlayLayer.GetOverlayLayer(main)?.GetVisualDescendants().OfType<Button>() ?? Enumerable.Empty<Button>())
        if (KhoLanh.App.Controls.Icon.TextOf(w.Content) == "Hủy") { w.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); break; }
    g.ClearFilters(); for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
    Console.WriteLine($"  [Lọc] bỏ lọc: {g.ShownRows.Count}/{total} dòng");
    // 1.71: bấm tiêu đề cột → A→Z, bấm lại → Z→A
    g.ToggleSort(tc); for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
    var asc = g.ShownRows.Take(5).Select(r => g.CellText(r, tc)).ToList();
    Snap(main, "45d-sap-xep-a-z");
    g.ToggleSort(tc); for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
    Console.WriteLine($"  [Sắp xếp] A→Z: {string.Join(" | ", asc)} · Z→A đầu: {g.CellText(g.ShownRows[0], tc)}");
    Snap(main, "45e-sap-xep-z-a");
    g.ClearSort();
    // 1.52: ⤢ mở bảng ra cửa sổ lớn (cùng dữ liệu)
    _ = g.OpenLarge(); for (var i = 0; i < 30; i++) { Thread.Sleep(30); Pump(); }
    if (KhoLanh.App.Controls.SheetGrid.LastLarge is { } big)
    {
        var bg = big.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First();
        Console.WriteLine($"  [Cửa sổ lớn] {big.Width:0}×{big.Height:0} · {bg.Model.RowCount} dòng · cùng dữ liệu={ReferenceEquals(bg.Model, g.Model)}");
        Snap(big, "45c-bang-cua-so-lon");
        big.Close(); for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
    }
}

static string FindRepo()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d is not null && !File.Exists(Path.Combine(d.FullName, "Directory.Build.props"))) d = d.Parent;
    return d?.FullName ?? Directory.GetCurrentDirectory();
}

void BtScene()
{
    void W(int n) { for (var i = 0; i < n; i++) { Thread.Sleep(30); Pump(); } }
    Window Dlg() { for (var i = 0; i < 60 && main.OwnedWindows.Count == 0; i++) W(1); return main.OwnedWindows.Last(); }
    object? Call(object o, string m) => o.GetType().GetMethod(m, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(o, null);
    main.Navigate("m2"); W(40); Snap(main, "41-ton-kho-gui");
    {   // hộp thoại có bảng (cùng khuôn "Lịch sử nạp Excel"): dựng bằng Ui.Dialog + Ui.Show như các màn hình
        var gm = new KhoLanh.Core.Grid.GridModel { ReadOnly = true };
        gm.Load(new[] { new KhoLanh.Core.Grid.GridColumn("t", "Lúc nạp", 140), new KhoLanh.Core.Grid.GridColumn("f", "File", 260), new KhoLanh.Core.Grid.GridColumn("r", "Dòng", 80, "number") },
            Enumerable.Range(1, 30).Select(i => new[] { $"{i:00}/11/2026 08:00", $"TON KHO {i:00}.11.xlsx", (3000 + i * 17).ToString() }));
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new Border { BorderThickness = new Thickness(1), BorderBrush = Avalonia.Media.Brushes.Gray, Child = new KhoLanh.App.Controls.SheetGrid(gm) { SpareRows = 0, Height = 340 } });
        var cl = new Button { Content = "Đóng" }; body.Children.Add(Ui.Buttons(cl));
        _ = Ui.Show(main, Ui.Dialog("Lịch sử nạp Excel · Tồn kho An An", body, 1000));
    }
    var h = Dlg(); W(10);
    Console.WriteLine($"[BT] lịch sử nạp: CanResize={h.CanResize} {h.Bounds.Width:0}x{h.Bounds.Height:0}");
    Snap(h, "bt-lich-su-nap");
    h.Width = 1300; h.Height = 760; W(10);
    var hg = h.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First();
    Console.WriteLine($"[BT] sau kéo giãn: {h.Bounds.Width:0}x{h.Bounds.Height:0} bảng cao {hg.Bounds.Height:0}");
    Snap(h, "bt-lich-su-nap-gian"); h.Close(); W(4);
    main.Navigate("m4"); W(40);
    var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(); tc.SelectedIndex = 1; W(10);
    var g = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
    {   // 1.92: chỉ ngày mới nhất mở, các ngày cũ thu gọn; bấm dòng tổng ngày = mở / thu; dòng Tổng không đổi
        bool Head(int r) => g.Model.Get(r, 0).StartsWith("▾") || g.Model.Get(r, 0).StartsWith("▸");
        double? Tot() { g.InvalidateVisual(); W(4); return ((double?[])typeof(KhoLanh.App.Controls.SheetGrid).GetField("_sumAll", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(g)!)[4]; }
        var open0 = Enumerable.Range(0, g.Model.RowCount).Count(r => g.Model.Get(r, 0).StartsWith("▾"));
        var n0 = g.Model.RowCount; var t0 = Tot();
        Snap(main, "22e-ra-dong-thu-gon");
        var h2 = Enumerable.Range(0, g.Model.RowCount).First(r => g.Model.Get(r, 0).StartsWith("▸"));
        var oldTxt = g.Model.Get(h2, 0); g.RowClicked!(h2); W(10); var n1 = g.Model.RowCount; var t1 = Tot();
        var h1 = Enumerable.Range(0, g.Model.RowCount).First(r => g.Model.Get(r, 0).StartsWith("▾"));
        g.RowClicked!(h1); W(10); var n2 = g.Model.RowCount; var t2 = Tot();
        Console.WriteLine($"[THUGON] ngày mở lúc đầu {open0}, dòng {n0} → mở 1 ngày cũ {n1} → thu ngày mới {n2}; Net tổng {t0} / {t1} / {t2}");
        if (open0 != 1 || n1 <= n0 || n2 >= n1 || t0 is null || Math.Abs((t0 ?? 0) - (t1 ?? 0)) > 0.01 || Math.Abs((t1 ?? 0) - (t2 ?? 0)) > 0.01) { Console.WriteLine("[THUGON] LỖI"); s.Lan.Dispose(); Environment.Exit(1); }
        var old = oldTxt[2..];   // trả về như lúc mở trang: ngày mới nhất mở, ngày cũ thu
        g.RowClicked!(0); W(10); g.RowClicked!(Enumerable.Range(0, g.Model.RowCount).First(r => g.Model.Get(r, 0) == "▾ " + old)); W(10);
    }
    var heads = Enumerable.Range(0, g.Model.RowCount).Where(r => g.Model.Get(r, 0).StartsWith("▾") || g.Model.Get(r, 0).StartsWith("▸")).Take(2).ToList();
    g.SetChecked(heads, true); W(10); Snap(main, "22c-ra-dong-tich-ngay");
    _ = Call(main.PageHost.Content!, "DayImage"); var d = Dlg(); W(10);
    Console.WriteLine($"[BT] ảnh gộp: {d.Title} CanResize={d.CanResize} {d.Bounds.Width:0}x{d.Bounds.Height:0}");
    Snap(d, "22d-ra-dong-anh-gop"); d.Close(); W(4);
}
