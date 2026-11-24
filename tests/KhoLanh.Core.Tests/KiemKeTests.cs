using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.Entry;
using KhoLanh.Core.KiemKe;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.SoDo;
using KhoLanh.Core.ViTri;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.KiemKe.KiemKeCore;

namespace KhoLanh.Core.Tests;

public class KiemKeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-kk-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    private const string A = "VRHLBTNXECR003140IME1", B = "VRHLBTNXXXR002630IME1", C = "VRHLBTNXECR002130IME1", D = "VRHLBTNXECR004150IME1";

    public KiemKeTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }
    private KiemKeService Kk => _s.KiemKe;

    private static Page P(string id, string name, string[] cols, params string[][] rows) => new() { Id = id, Name = name, Ngay = NgayOf(name, ""), Columns = cols.ToList(), Rows = rows.ToList() };
    private static readonly string[] Tpl = { "Vị trí", "Mã hàng", "Tên hàng", "Size", "Số thùng", "Kg", "Ngày SX", "Ghi chú" };

    [Fact]
    public void Columns_dates_and_extraction()
    {
        Assert.Equal("sothung", Hk("Số thùng"));
        Assert.Equal(A, NormKey("tpc-" + A.ToLowerInvariant()));
        Assert.Equal("2026-11-21", NgayOf("Kho 1 A dãy 01 21-11-2026", "2026-11-25T00:00"));
        Assert.Equal("2026-11-25", NgayOf("Kiểm kê", "2026-11-25T08:00"));
        var c = PageCols(Tpl);
        Assert.Equal((0, 1, 4, 5), (c["vt"], c["ma"], c["sk"], c["tl"]));
        Assert.True(IsKiemKe(new[] { "Mã hàng", "Net" }));
        Assert.False(IsKiemKe(new[] { "Mã hàng", "Tên hàng" }));

        var ex = ExtractRows(new[]
        {
            P("p1", "A 21-11-2026", Tpl,
                new[] { "TG1.A.01.1.1", A, "Tôm", "31/40", "10", "10,8", "", "lô L1" },
                new[] { "", "", "", "", "5", "", "", "" },          // có số, thiếu mã
                new[] { "", "", "", "", "", "", "", "" },           // dòng trống
                new[] { "TG1.A.01.1.2", B, "", "", "abc", "10", "", "" }),
            P("p2", "Net 22-11-2026", new[] { "Mã hàng", "Số kiện", "Net", "Lô" }, new[] { C, "3", "30,5", "L9" }),
            P("p3", "Không số", new[] { "Mã hàng", "Ghi chú" }),
        });
        Assert.Single(ex.Skipped);
        Assert.Equal(3, ex.Rows.Count);
        Assert.Equal((1, 1, 1), (ex.KhongMa, ex.SoLoi, ex.ThieuKien));
        var r0 = ex.Rows[0];
        Assert.Equal((10.0, 108.0, "L1", "tinh", "2026-11-21"), (r0.Kien, r0.Kg!.Value, r0.Lo, r0.KgSrc, r0.Ngay));
        Assert.Equal((30.5, "net"), (ex.Rows[2].Kg!.Value, ex.Rows[2].KgSrc));

        // đếm lại ở 2 ngày → chỉ giữ lần mới nhất
        var rows = ExtractRows(new[]
        {
            P("a", "x 20-11-2026", Tpl, new[] { "TG1.A.01.1.1", A, "", "", "10", "10", "", "" }),
            P("b", "y 21-11-2026", Tpl, new[] { "TG1.A.01.1.1", A, "", "", "12", "10", "", "" }),
        }).Rows;
        Assert.Single(FindRecount(rows));
        var kept = LatestOnly(rows, out var dropped);
        Assert.Equal(12, Assert.Single(kept).Kien);
        Assert.Single(dropped);
    }

    [Fact]
    public void Totals_statuses_kế_toán_and_causes()
    {
        var ex = new List<Line>
        {
            new() { Key = A, Ma = A, Kien = 10, Kg = 100 },                 // khớp
            new() { Key = B, Ma = B, Kien = 12, Kg = 120 },                 // thừa 2
            new() { Key = C, Ma = C, Kien = 5, Kg = 52 },                   // lệch kg
            new() { Key = "VRHLBTNXECR004150IME9", Ma = "VRHLBTNXECR004150IME9", Kien = 3, Kg = 30 }, // gõ sai của D
        };
        var stock = new List<Stock>
        {
            new() { Key = A, Ma = A, Ton = 10, Tl = 10, Ten = "Tôm A" }, new() { Key = B, Ma = B, Ton = 10, Tl = 10 },
            new() { Key = C, Ma = C, Ton = 5, Tl = 10 }, new() { Key = D, Ma = D, Ton = 3, Tl = 10 },
        };
        var kt = MakeKeToan(new[] { new KtRow(A, "", "", "", "100"), new KtRow(B, "", "10", "10", ""), new KtRow("VRXX", "Hàng KT", "", "", "7"), new KtRow("", "", "", "", "5") });
        Assert.Equal((3, 1), (kt.N, kt.KhongMa));
        var t = BuildTotals(ex, stock, kt);
        string St(string k) => t.Rows.Single(r => r.Key == k).Status;
        Assert.Equal(Khop, St(A)); Assert.Equal(Thua, St(B)); Assert.Equal(LechKg, St(C));
        Assert.Equal(ChiKK, St("VRHLBTNXECR004150IME9")); Assert.Equal(ChuaKK, St(D)); Assert.Equal(ChiKT, St("VRXX"));
        var b = t.Rows.Single(r => r.Key == B);
        Assert.Equal((2.0, 20.0, "Lệch", 20.0), (b.DKien!.Value, b.DKg!.Value, b.KtStatus, b.TongKt!.Value));
        Assert.Equal("Khớp", t.Rows.Single(r => r.Key == A).KtStatus);
        Assert.Equal("Tôm A", t.Rows.Single(r => r.Key == A).Ten);

        Analyze(t.Rows, new HashSet<string>());
        var typo = t.Rows.Single(r => r.Key == "VRHLBTNXECR004150IME9").Nn!;
        Assert.Equal(("sai-ma", "kk", D, true), (typo.Code, typo.Side, typo.FixTo, typo.Strong));
        Assert.Equal("lech-tl", t.Rows.Single(r => r.Key == C).Nn!.Code);
        Assert.Equal("lech-kien", t.Rows.Single(r => r.Key == B).Nn!.Code);
        var sum = Summarize(t.Rows);
        Assert.Equal((5, 1, 1, 1, 1, 1, 4, 1), (sum.Total, sum.Khop, sum.Thua, sum.LechKg, sum.ChiKK, sum.ChuaKK, sum.Open, sum.Auto));
        Assert.Equal(2, Hamming("ABCD", "AXCY"));
    }

    // ------------------------------------------------------------------ qua service (dữ liệu thật trong database)

    private void SeedStock(params (string ma, double ton, double tl)[] rows)
    {
        var headers = new[] { "Item ID", "Mã hàng", "Tên hàng", "Đặc tính", "Hợp đồng", "ĐVT", "Tồn cuối", "Trọng lượng" };
        var arr = new JsonArray(rows.Select((r, i) => (JsonNode)new JsonObject
        {
            ["c0"] = (100 + i).ToString(), ["c1"] = r.ma, ["c2"] = "Tôm " + r.ma[^6..], ["c3"] = "MKS", ["c4"] = "XXX", ["c5"] = "Ctn", ["c6"] = r.ton.ToString(System.Globalization.CultureInfo.InvariantCulture), ["c7"] = r.tl.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).ToArray());
        var meta = new JsonObject { ["headers"] = new JsonArray(headers.Select(h => (JsonNode)h).ToArray()), ["fileName"] = "TON KHO 21.11.xlsx", ["dataDate"] = "21/11/2026" };
        _s.Lan.Write("ton-kho-an-an", new Dictionary<string, JsonArray?> { ["LoadData"] = arr, ["LoadDataMeta"] = new JsonArray(new JsonObject { ["data"] = "__JSON__" + meta.ToJsonString() }) });
    }
    private EntryService.PageInfo Page(string name, params string[][] rows)
    {
        // dòng viết theo mẫu cũ (Vị trí, Mã, Tên, Size, Số thùng, Kg / thùng, Ngày SX, Ghi chú) → mẫu Kiểm kê 1.62
        // (Vị trí, Mã hàng, Size, Trọng lượng, Số lượng, Net(kg) = công thức, Ghi chú, Ngày nhập)
        var p = _s.Entry.Create(name, EntryService.BuiltIn.First(t => t.Id == "kiem-ke"));
        return _s.Entry.Save(p, p.Columns, rows.Select(r => (IReadOnlyList<string>)new[] { r[0], r[1], r[3], r[5], r[4], "", r[7], r[6] }).ToList());
    }
    private void SeedPositions()
    {
        _s.SoDo.Load();
        _s.SoDo.SaveConfig(new SoDoCore.Cfg { ["TG1"] = new List<SoDoCore.KhuCfg> { new() { Khu = "A", D = 2, K = 2, T = 1 } } });
        var arr = new JsonArray(new[]
        {
            ("TG1.A.01.1.1", A, 10.0, "L1"), ("TG1.A.01.1.2", B, 10.0, "L2"), ("TG1.A.02.1.1", C, 5.0, "L3"),
        }.Select(x => (JsonNode)new JsonObject { ["Mã hàng"] = x.Item2, ["Tên hàng"] = "Tôm", ["Lô (Batch)"] = x.Item4, ["Vị trí"] = x.Item1, ["SL Tồn"] = x.Item3, ["T.Lượng"] = 10 }).ToArray());
        _s.Lan.Write(ViTriService.Db, new Dictionary<string, JsonArray?> { [ViTriService.SheetRows] = arr });
    }

    [Fact]
    public void Service_choose_pages_compare_fix_and_lock()
    {
        SeedStock((A, 10, 10), (B, 10, 10), (D, 3, 10));
        var p1 = Page("Kho 1 A dãy 01 21-11-2026",
            new[] { "TG1.A.01.1.1", A, "", "", "10", "10", "", "lô L1" },
            new[] { "TG1.A.01.1.2", B, "", "", "12", "10", "", "lô L2" },
            new[] { "TG1.A.02.1.2", "VRHLBTNXECR004150IME9", "", "", "3", "10", "", "" });
        _s.Entry.Create("Trang khác", EntryService.BuiltIn.First(t => t.Id == "nhap-kho"));
        Kk.Load();
        Assert.Single(Kk.Pages);                       // trang mẫu Nhập kho không tính là trang kiểm kê
        Assert.Empty(Kk.Lines);
        Kk.SetChosen(new[] { p1.Id }, latestOnly: false);
        Assert.Equal(3, Kk.Lines.Count);
        Assert.Equal((1, 1, 1, 1), (Kk.Summary.Khop, Kk.Summary.Thua, Kk.Summary.ChiKK, Kk.Summary.ChuaKK));
        Assert.Equal(1, Kk.Summary.Auto);

        // lựa chọn được lưu (máy khác đọc lại thấy)
        var again = new KiemKeService(() => _s.Lan, _s.Session, _s.Audit, _s.Entry, new SoDoService(() => _s.Lan, _s.Session, _s.Audit, _s.Entry)); again.Load();
        Assert.Contains(p1.Id, again.Chosen);

        // sửa mã gõ sai → ghi vào trang Nhập liệu
        Assert.Equal(1, Kk.FixMa(new[] { ("VRHLBTNXECR004150IME9", D) }));
        Assert.Equal(D, _s.Entry.Load(_s.Entry.Pages().First(p => p.Id == p1.Id))[2][1]);
        Assert.Equal((2, 1), (Kk.Summary.Khop, Kk.Summary.Thua));

        // kế toán
        Kk.SaveKeToan(new[] { new KiemKeCore.KtRow(A, "", "", "", "100"), new KiemKeCore.KtRow(B, "", "", "", "100") }, "Dán tay");
        Assert.Equal(2, Kk.Summary.KtLech);   // B lệch 20 kg, D kế toán không có

        // chụp tồn rồi tồn hiện tại đổi: đối chiếu vẫn theo bản chụp
        Assert.Equal(3, Kk.SnapStock());
        SeedStock((A, 9, 10), (B, 10, 10), (D, 3, 10));
        Kk.Load();
        Assert.Equal("snap", Kk.StockSrc);
        Assert.Equal(Khop, Kk.T.Rows.Single(r => r.Key == A).Status);
        Kk.SetStockSrc("live");
        Assert.Equal(Thua, Kk.T.Rows.Single(r => r.Key == A).Status);

        // chốt: khóa thay đổi, lưu bản chốt, hủy chốt cần lý do
        Assert.Throws<BusinessException>(() => Kk.DoChot(" "));
        var c = Kk.DoChot("chốt cuối tháng");
        Assert.True(Kk.Locked);
        Assert.Throws<BusinessException>(() => Kk.SetChosen(Array.Empty<string>(), false));
        Assert.Equal(Kk.T.Rows.Count, Kk.ChotBan(c.Ver).Count);
        Assert.Contains(Kk.Readiness(), x => x.Ok == "block");
        Kk.MoChot("đếm lại dãy 1");
        Assert.False(Kk.Locked);
        Assert.Equal(2, Kk.DoChot("lần 2").Ver);

        var path = Path.Combine(_dir, "bc.xlsx");
        Kk.ExportReport(path);
        using var wb = new XLWorkbook(path);
        Assert.Equal(new[] { "Doi chieu An An", "Doi chieu ke toan", "Chi tiet kiem ke", "Theo vi tri" }, wb.Worksheets.Select(w => w.Name));
        Assert.Equal("m9", ModuleBridge.PermKey(KiemKeService.Db));
        Assert.Contains(Perm.Items, x => x.Key == "m9");
    }

    [Fact]
    public void By_position_and_count_orders()
    {
        SeedStock((A, 10, 10), (B, 10, 10), (C, 5, 10));
        SeedPositions();
        var p = Page("Kho 1 A dãy 01 21-11-2026",
            new[] { "TG1.A.01.1.1", A, "", "", "10", "10", "", "lô L1" },   // khớp
            new[] { "TG1.A.01.1.2", B, "", "", "8", "10", "", "" },          // lệch (không ghi lô → so theo mã)
            new[] { "TG1.A.02.1.2", C, "", "", "1", "10", "", "" },          // thừa (sổ sách trống)
            new[] { "A-01", C, "", "", "1", "10", "", "" });                 // vị trí sai dạng
        Kk.Load(); Kk.SetChosen(new[] { p.Id }, false);
        var v = Kk.Vt!;
        Assert.Equal(VtKhop, v.Cells["TG1.A.01.1.1"].St);
        var lech = v.Cells["TG1.A.01.1.2"];
        Assert.Equal(VtLech, lech.St);
        Assert.Equal((-2.0, "kiểm kê không ghi lô — so theo mã"), (lech.Items.Single().Diff, lech.Items.Single().Note));
        Assert.Equal(VtChua, v.Cells["TG1.A.02.1.1"].St);
        Assert.Equal(VtThua, v.Cells["TG1.A.02.1.2"].St);
        Assert.Single(v.Bad);
        var day1 = v.Days[DayKey("TG1", "A", 1)];
        Assert.Equal((2, 2, 1), (day1.NExp, day1.NDone, day1.NLech));

        var orders = MakeOrders(Kk.SoDo.B, "TG1", "ALL", false, new[] { "An", "Bình", " " });
        Assert.Equal(2, orders.Count);
        Assert.Equal(new[] { "An", "Bình" }, orders.Select(o => o.Nguoi).OrderBy(x => x));
        Assert.Equal("xong-lech", Progress(orders[0], v).State);
        Assert.Equal((1, 0, "chua"), (Progress(orders[1], v).NExp, Progress(orders[1], v).NDone, Progress(orders[1], v).State));
        Kk.SetPeople(new[] { "An", "Bình", "An" });
        Assert.Equal(2, Kk.People.Count);

        var path = Path.Combine(_dir, "lenh.xlsx");
        Kk.ExportOrders(path, orders, withBook: true);
        using var wb = new XLWorkbook(path);
        Assert.Equal(2, wb.Worksheets.Count);
        Assert.StartsWith("PHIẾU ĐẾM", wb.Worksheet(1).Cell(1, 1).GetString());
        Assert.Equal("TG1.A.01.1.1", wb.Worksheet(1).Cell(4, 1).GetString());
    }

    [Fact]
    public void Reads_accounting_file()
    {
        var path = Path.Combine(_dir, "kt.xlsx");
        using (var wb = new XLWorkbook())
        {
            var ws = wb.AddWorksheet("KT");
            ws.Cell(1, 1).Value = "SỐ LIỆU KẾ TOÁN";
            ws.Cell(3, 1).Value = "Mã hàng"; ws.Cell(3, 2).Value = "Tên hàng"; ws.Cell(3, 3).Value = "Số lượng"; ws.Cell(3, 4).Value = "Net (kg)";
            ws.Cell(4, 1).Value = A; ws.Cell(4, 2).Value = "Tôm"; ws.Cell(4, 3).Value = 10; ws.Cell(4, 4).Value = 108.5;
            ws.Cell(5, 1).Value = "Tổng cộng"; ws.Cell(5, 4).Value = 108.5;
            wb.SaveAs(path);
        }
        var rows = KiemKeService.ReadKeToanFile(path);
        var r = Assert.Single(rows);
        Assert.Equal((A, "10", "108.5"), (r.Ma, r.Sl, r.Net));
    }
}
