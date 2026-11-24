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
    small.SetMini(true, save: false); Pump();
    var nb = small.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("side") && ToolTip.GetTip(b) is string t && t.StartsWith("Đối chiếu kiểm kê"));
    ToolTip.SetIsOpen(nb, true); Pump();
    Snap(small, "03d-menu-thu-gon");
    ToolTip.SetIsOpen(nb, false); small.SetMini(false, save: false); Pump();
}
small.Close();
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
    if (key == "m13")
    {
        var lb = main.PageHost.GetVisualDescendants().OfType<ListBox>().First(x => x.IsEffectivelyVisible);
        lb.SelectedIndex = 0; for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); }
        Snap(main, "30-phieu-xuat-kho");
        var tcPk = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible);
        tcPk.SelectedIndex = 1; Pump(); Snap(main, "30b-phieu-xuat-kho-email");
        tcPk.SelectedIndex = 0; Pump();
        // tab Cargo list
        var top = main.PageHost.GetVisualDescendants().OfType<TabControl>().First(x => x.IsEffectivelyVisible);
        top.SelectedIndex = 2; for (var i = 0; i < 12; i++) { Thread.Sleep(30); Pump(); }
        var clb = main.PageHost.GetVisualDescendants().OfType<ListBox>().First(x => x.IsEffectivelyVisible);
        clb.SelectedIndex = 0; for (var i = 0; i < 12; i++) { Thread.Sleep(30); Pump(); }
        Snap(main, "31-cargo-nhap");
        var inner = main.PageHost.GetVisualDescendants().OfType<TabControl>().Where(x => x.IsEffectivelyVisible && x != top).First();
        inner.SelectedIndex = 1; for (var i = 0; i < 6; i++) { Thread.Sleep(30); Pump(); }
        Snap(main, "31b-cargo-phieu-xem-truoc");
        inner.SelectedIndex = 2; Pump(); Snap(main, "31c-cargo-danh-muc");
        top.SelectedIndex = 0; Pump();
    }
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
        for (var t = 0; t < 7; t++) { gtabs.SelectedIndex = t; for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); } Snap(main, $"41-gui-tab{t + 1}"); }
        // Phiếu xuất: gõ SL xuất vào dòng đầu rồi tạo phiếu
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
        gtabs.SelectedIndex = 5; for (var i = 0; i < 10; i++) { Thread.Sleep(30); Pump(); } Snap(main, "41-gui-bao-cao-sau-gop");
        Console.WriteLine($"[GUI] bảng tổng hợp: {s.Gui.D.ComputePivot().Grand.Tong} kg");
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
