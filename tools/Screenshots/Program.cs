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
        return new[] { x.Vt, ma, x.Ten, "", sl.ToString(), tl.ToString(ic), "10/2026", "lô " + x.Lo };
    }
    var all = vtDemo.Select((x, i) => (x, i)).ToList();
    bool Part1(string vt) => vt.StartsWith("TG1.A.") || vt.StartsWith("TG1.B.");
    bool Skip(string vt) => vt.StartsWith("TG1.D.14.6") || vt.StartsWith("TG1.D.14.7");
    var kp1 = s.Entry.Create($"Kiểm kê TG1 khu A-B {DateTime.Today.AddDays(-1):dd/MM/yyyy}", kkTpl);
    s.Entry.Save(kp1, kp1.Columns, all.Where(t => Part1(t.x.Vt)).Select(t => (IReadOnlyList<string>)Line(t.x, t.i)).ToList());
    var rest = all.Where(t => !Part1(t.x.Vt) && !Skip(t.x.Vt)).Select(t => (IReadOnlyList<string>)Line(t.x, t.i)).ToList();
    rest.Add(new[] { "TG1.C.05.6.4", "TPC-VRHLBTNXEC0006070IMA1", "Tôm PD BTP 60/70", "60/70", "18", "10", "10/2026", "hàng lạ, chưa có mã" });
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
    foreach (var q in new[] { mk, other })
        rowsPk.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = q.Id, ["loai"] = q.Loai, ["ngay"] = q.Ngay, ["soPhieu"] = q.SoPhieu, ["lsx"] = q.Lsx, ["khach"] = q.Khach, ["capNhat"] = q.CapNhat, ["data"] = "__JSON__" + KhoLanh.Core.PhieuKho.PhieuKhoCore.ToJson(q) });
    s.Modules.WriteSheets("phieu-nhap-xuat", new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { ["PNX_Phieu"] = rowsPk });
}

var main = new MainWindow { Width = 1440, Height = 880, WindowState = WindowState.Normal };
main.Show();
Snap(main, "03-man-hinh-chinh");
var small = new MainWindow { Width = 1366, Height = 768, WindowState = WindowState.Normal };
small.Show();
Snap(small, "03c-man-hinh-1366");
{
    var gb = small.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string)?.StartsWith("Báo cáo") == true);
    gb.Flyout!.ShowAt(gb); Pump();
    Snap(small, "03d-menu-nhom");
    gb.Flyout.Hide(); Pump();
}
small.Close();

foreach (var (key, name) in new[] { ("users", "12-tai-khoan"), ("backup", "13-sao-luu"), ("lan", "15-mang-lan"), ("audit", "14-nhat-ky"), ("m3", "21-tong-hop"), ("m4", "22-ra-dong"), ("m5", "23-doi-chieu"), ("m6", "20-nhap-lieu"), ("m7", "24-vi-tri"), ("m8", "25-so-do-kho"), ("m9", "26-doi-chieu-kiem-ke"), ("m10", "27-khang-sinh"), ("m11", "28-phieu-xuat-he-thong"), ("m12", "29-in-chung-tu-kho"), ("m13", "30-phieu-xuat-kho") })
{
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
    if (key == "m7")
    {
        var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
        var g = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First();
        g.Model.SetCells(new[] { (0, 7, "6"), (1, 7, "x"), (2, 7, "10"), (4, 7, "4"), (6, 7, "3") });
        Pump(); g.Select(2, 7); Snap(main, "24-vi-tri");
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
        tc.SelectedIndex = 0;
    }
    if (key == "m9")
    {
        var tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
        var g = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
        g.Select(0, 1); Pump(); Snap(main, "26-doi-chieu-kiem-ke");
        tc.SelectedIndex = 1; Pump(); Snap(main, "26b-kiem-ke-so-lieu");
        tc.SelectedIndex = 2; Pump(); Snap(main, "26c-kiem-ke-ke-toan");
        tc.SelectedIndex = 3; Pump(); Snap(main, "26d-kiem-ke-vi-tri");
        tc.SelectedIndex = 4; Pump(); Snap(main, "26e-kiem-ke-lenh-dem");
        tc.SelectedIndex = 5; Pump(); Snap(main, "26f-kiem-ke-chua-chot");
        s.KiemKe.DoChot("Kiểm kê cuối tháng 11 — còn lệch đã báo kế toán");
        main.Navigate("m3"); main.Navigate("m9"); for (var i = 0; i < 30; i++) { Thread.Sleep(50); Pump(); }
        tc = main.PageHost.GetVisualDescendants().OfType<TabControl>().First();
        tc.SelectedIndex = 5; Pump();
        var hg = main.PageHost.GetVisualDescendants().OfType<KhoLanh.App.Controls.SheetGrid>().First(x => x.IsEffectivelyVisible);
        hg.Select(0, 0); Pump(); Snap(main, "26g-kiem-ke-da-chot");
        s.KiemKe.MoChot("demo");
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
        main.PageHost.GetVisualDescendants().OfType<Button>().First(b => b.IsEffectivelyVisible && (b.Content as string) == "Tra cứu kết quả").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
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
    if (key == "m13")
    {
        var lb = main.PageHost.GetVisualDescendants().OfType<ListBox>().First(x => x.IsEffectivelyVisible);
        lb.SelectedIndex = 0; for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
        Snap(main, "30-phieu-xuat-kho");
        var tcPk = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible);
        tcPk.SelectedIndex = 1; Pump(); Snap(main, "30b-phieu-xuat-kho-email");
        tcPk.SelectedIndex = 0; Pump();
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

static string FindRepo()
{
    var d = new DirectoryInfo(AppContext.BaseDirectory);
    while (d is not null && !File.Exists(Path.Combine(d.FullName, "Directory.Build.props"))) d = d.Parent;
    return d?.FullName ?? Directory.GetCurrentDirectory();
}
