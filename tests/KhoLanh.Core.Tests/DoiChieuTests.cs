using ClosedXML.Excel;
using KhoLanh.Core.DoiChieu;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.DoiChieu.DcxEngine;

namespace KhoLanh.Core.Tests;

public class DoiChieuTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-dc-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    private const string A = "VRHLBTNXECR003140IME1", B = "VRHLBTNXXXR000914IME1", C = "VRHLBTNXXXTGS0010IME1", D = "PRHLBTNXXXTGS0010IME1";
    private static readonly DateTime D1 = new(2026, 11, 1), D2 = new(2026, 11, 2), D3 = new(2026, 11, 3);

    public DoiChieuTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>File Kho kiểu thật: khối dòng chi tiết từng ngày + dòng TỔNG NGÀY (công thức SUM) bên dưới.</summary>
    private string KhoFile(string name, params (DateTime d, (string ma, double sl, double tl)[] rows)[] days)
    {
        var path = Path.Combine(_dir, name);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Xuat");
        ws.Cell(1, 1).Value = "BẢNG KÊ XUẤT";
        var heads = new[] { "Ngày", "Mã hàng", "Tên hàng", "Số lượng", "Trọng lượng", "Net" };
        for (var i = 0; i < heads.Length; i++) ws.Cell(3, i + 1).Value = heads[i];
        var r = 4;
        foreach (var (d, rows) in days)
        {
            var first = r;
            foreach (var (ma, sl, tl) in rows)
            {
                ws.Cell(r, 1).Value = d; ws.Cell(r, 2).Value = ma; ws.Cell(r, 3).Value = "Tôm " + ma[^6..];
                ws.Cell(r, 4).Value = sl; ws.Cell(r, 5).Value = tl; ws.Cell(r, 6).Value = sl * tl; r++;
            }
            ws.Cell(r, 1).Value = d;
            ws.Cell(r, 4).Value = rows.Sum(x => x.sl); ws.Cell(r, 6).Value = rows.Sum(x => x.sl * x.tl);
            ws.Cell(r, 4).FormulaA1 = $"SUM(D{first}:D{r - 1})"; ws.Cell(r, 6).FormulaA1 = $"SUM(F{first}:F{r - 1})";
            r++;
        }
        wb.SaveAs(path);
        return path;
    }
    private string XuongFile(string name, params (string sheet, (DateTime d, string ma, double sl, double net)[] rows)[] sheets)
    {
        var path = Path.Combine(_dir, name);
        using var wb = new XLWorkbook();
        foreach (var (sheet, rows) in sheets)
        {
            var ws = wb.AddWorksheet(sheet);
            var heads = new[] { "Ngày", "Mã hàng", "Số lượng", "Net" };
            for (var i = 0; i < heads.Length; i++) ws.Cell(1, i + 1).Value = heads[i];
            var r = 2;
            foreach (var x in rows) { ws.Cell(r, 1).Value = x.d; ws.Cell(r, 2).Value = x.ma; ws.Cell(r, 3).Value = x.sl; ws.Cell(r, 4).Value = x.net; r++; }
        }
        wb.SaveAs(path);
        return path;
    }

    private static List<object?[]> Aoa(params object?[][] rows) => rows.ToList();

    [Fact]
    public void Parse_and_compare_statuses()
    {
        var kho = ParseSheet(Aoa(
            new object?[] { "Ngày", "Mã hàng", "Số lượng", "Net" },
            new object?[] { "01/11/2026", A, 10.0, 100.0 },
            new object?[] { "01/11/2026", B, 5.0, 45.0 },
            new object?[] { "01/11/2026", C, 2.0, 24.0 },
            new object?[] { "01/11/2026", null, 17.0, 169.0 }));
        Assert.True(kho.Ok);
        Assert.Equal(3, kho.Rows.Count);
        Assert.Single(kho.Totals);
        var xuong = ParseSheet(Aoa(
            new object?[] { "Ngày", "Mã hàng", "Số lượng", "Net" },
            new object?[] { null, A, 10.0, 100.0 },
            new object?[] { null, B, 4.0, 36.0 },
            new object?[] { null, D, 3.0, 36.0 },
            new object?[] { "1/11/26", null, 17.0, 172.0 }), new ParseOptions { DateFromTotals = true });
        Assert.True(xuong.Ok);
        Assert.All(xuong.Rows, r => Assert.Equal("2026-11-01", r.Ngay));   // ngày lấy từ dòng tổng bên dưới
        var res = Compare(kho.Rows, xuong.Rows, new CompareOptions { KhoTotals = kho.Totals, XuongTotals = xuong.Totals });
        string St(string ma) => res.Items.First(i => i.Ma == ma).Status;
        Assert.Equal("khop", St(A));
        Assert.Equal("lech", St(B));
        Assert.Equal("chi_kho", St(C));
        Assert.Equal("chi_xuong", St(D));
        Assert.Single(res.Days);
        Assert.True(res.Days[0].CheckKho!.Khop);
    }

    [Fact]
    public void Missing_columns_ask_for_mapping()
    {
        var r = ParseSheet(Aoa(new object?[] { "Ngày", "Mã hàng", "Số lượng", "Ghi chú" }, new object?[] { "01/11/2026", A, 1.0, "x" }));
        Assert.False(r.Ok);
        Assert.Contains("net", r.Need!.Fields);
    }

    [Fact]
    public void Transform_moves_quantity_and_rewrites_totals()
    {
        var path = KhoFile("k1.xlsx", (D1, new[] { (A, 10.0, 10.0), (B, 5.0, 9.0) }), (D2, new[] { (A, 3.0, 10.0) }));
        var bytes = File.ReadAllBytes(path);
        var (outB, rep) = DcxXlsx.Transform(bytes, "Xuat", new DcxXlsx.Spec
        {
            Removes = new() { new DcxXlsx.Remove(5, 5) },                              // B ngày 1: hết → xóa dòng
            Adds = new() { new DcxXlsx.Add("2026-11-03", C, "Tôm mới", 2, 12, 24, false) }, // ngày 3 chưa có → khối mới
            Green = true,
        });
        Assert.Contains(rep.Removed, s => s.Contains("5") || s.Length > 0);
        Assert.NotEmpty(rep.NewBlocks);
        var wb = DcxXlsx.ReadWorkbook(outB);
        var p = ParseSheet(wb.Aoa["Xuat"]);
        Assert.True(p.Ok);
        Assert.DoesNotContain(p.Rows, r => r.Ma == B);
        Assert.Contains(p.Rows, r => r.Ma == C && r.Ngay == "2026-11-03" && r.Sl == 2 && r.Net == 24);
        // dòng tổng có giá trị (đã ghi sẵn kết quả) cho cả khối cũ và khối mới
        Assert.Contains(p.Totals, t => t.Ngay == "2026-11-01" && t.Sl == 10 && t.Net == 100);
        Assert.Contains(p.Totals, t => t.Ngay == "2026-11-03" && t.Sl == 2 && t.Net == 24);
        using var x = new XLWorkbook(new MemoryStream(outB));   // file mở lại được bằng thư viện Excel khác
        Assert.Contains("SUM", x.Worksheet("Xuat").CellsUsed(c => c.HasFormula).First().FormulaA1);
    }

    [Fact]
    public void AddSizeColumn_adds_column_and_fills_only_empty()
    {
        var path = KhoFile("k2.xlsx", (D1, new[] { (A, 10.0, 10.0), (B, 5.0, 9.0) }), (D2, new[] { (A, 3.0, 10.0) }));
        var bytes = File.ReadAllBytes(path);
        var (outB, rep) = DcxXlsx.AddSizeColumn(bytes, "Xuat", (ma, _) => ma == A ? "31/40" : "", true);
        Assert.NotNull(outB);
        Assert.Equal("add", rep.Mode);
        Assert.True(rep.Filled >= 2); Assert.True(rep.Empty >= 1);
        using var x = new XLWorkbook(new MemoryStream(outB!));
        var ws = x.Worksheet("Xuat");
        var hc = ws.Row(3).CellsUsed().First(c => c.GetString() == "Size");
        var col = hc.Address.ColumnNumber;
        Assert.Equal("31/40", ws.Cell(4, col).GetString());
        // chạy lại: đã có cột Size → không điền đè, không còn gì để điền
        var (b2, r2) = DcxXlsx.AddSizeColumn(outB!, "Xuat", (ma, _) => ma == A ? "XX" : "", false);
        Assert.Equal("fill", r2.Mode); Assert.True(r2.Nothing); Assert.Null(b2);
        // ô đã có không bị đổi; dòng trống được điền khi từ điển có size
        var (b3, r3) = DcxXlsx.AddSizeColumn(outB!, "Xuat", (ma, _) => "21/25", false);
        Assert.NotNull(b3);
        using var y = new XLWorkbook(new MemoryStream(b3!));
        Assert.Equal("31/40", y.Worksheet("Xuat").Cell(4, col).GetString());
    }

    // mã thường (không phải BTP/NL) → so theo mã, không gộp theo size
    private const string MA = "SPA00000000000000000A", MB = "SPB00000000000000000B", MC = "SPC00000000000000000C", MD = "SPD00000000000000000D", ME = "SPE00000000000000000E";
    private DoiChieuService Linked()
    {
        var k1 = KhoFile("thay.xlsx", (D1, new[] { (MA, 10.0, 10.0), (MC, 2.0, 12.0) }), (D2, new[] { (MB, 4.0, 9.0) }));
        var k2 = KhoFile("radong.xlsx", (D1, new[] { (MD, 6.0, 9.0) }), (D2, new[] { (ME, 1.0, 10.0) }));
        var xu = XuongFile("xuong.xlsx",
            ("Thay thung", new[] { (D1, MA, 10.0, 100.0), (D2, MB, 4.0, 36.0) }),
            ("Ra dong", new[] { (D1, MD, 6.0, 54.0), (D1, MC, 2.0, 24.0), (D2, ME, 1.0, 10.0) }));
        var dc = _s.DoiChieu; dc.Load();
        dc.LinkFile("kho1", k1); dc.LinkFile("kho2", k2); dc.LinkFile("xuong", xu);
        return dc;
    }

    [Fact]
    public void Link_files_auto_pairs_sheets()
    {
        var dc = Linked();
        Assert.Empty(dc.Needs);
        Assert.Equal("Thay thung", dc.P1.XuongSheet);
        Assert.Equal("Ra dong", dc.P2.XuongSheet);
        Assert.True(dc.IsLinked(dc.P1));
        Assert.Equal("chi_kho", dc.P1.Result!.Items.First(i => i.Ma == MC).Status);
        Assert.Equal("chi_xuong", dc.P2.Result!.Items.First(i => i.Ma == MC).Status);
        // trạng thái liên kết lưu theo tài khoản → nạp lại vẫn còn
        var again = new DoiChieuService(() => _s.Lan, _s.Session, _s.Audit, _s.Settings, _dir); again.Load();
        Assert.True(again.IsLinked(again.P2));
    }

    [Fact]
    public void Move_writes_both_files_backs_up_and_undoes()
    {
        var dc = Linked();
        var r = dc.ApplyOps(new() { new Op { Type = "move", From = 1, To = 2, Ngay = "2026-11-01", Ma = MC, Sl = 2 } }, "Chuyển C sang Rã đông");
        Assert.StartsWith("✔", r.Message);
        Assert.Equal("khop", dc.P2.Result!.Items.First(i => i.Ma == MC).Status);
        Assert.DoesNotContain(dc.P1.Result!.Items, i => i.Ma == MC);
        Assert.Equal(2, Directory.GetFiles(dc.BackupDir).Length);     // sao lưu 1 bản / file / ngày
        Assert.Single(dc.St.Log);
        dc.UndoLog(dc.St.Log[0].Id);
        Assert.Equal("chi_kho", dc.P1.Result!.Items.First(i => i.Ma == MC).Status);
        Assert.Empty(dc.St.Log);
        Assert.Equal(2, Directory.GetFiles(dc.BackupDir).Length);
    }

    [Fact]
    public void Setsl_and_add_row_new_day()
    {
        var dc = Linked();
        dc.ApplyOps(new() { new Op { Type = "setsl", Slot = 1, Ngay = "2026-11-02", Ma = MB, Target = 3 } }, "SL B = 3");
        Assert.Equal(3, dc.Movable(dc.P1, "2026-11-02", MB));
        dc.ApplyOps(new() { new Op { Type = "add", Slot = 1, Ngay = "2026-11-03", Ma = MD, Ten = "Tôm D", Sl = 5, Tl = 2 } }, "Thêm D");
        Assert.Contains(dc.P1.Kho!.Res.Rows, x => x.Ma == MD && x.Ngay == "2026-11-03" && x.Net == 10);
        Assert.Contains(dc.P1.Kho!.Res.Totals, t => t.Ngay == "2026-11-03" && t.Sl == 5);
    }

    [Fact]
    public void Commit_refuses_when_file_changed_outside()
    {
        var dc = Linked();
        var lk = dc.LinkOf("kho1")!;
        var ex = Assert.Throws<DoiChieuService.CommitException>(() => dc.Commit(new() { ("kho1", File.ReadAllBytes(lk.Path), "khongdung") }));
        Assert.Equal("CHANGED", ex.Code);
    }

    [Fact]
    public void Manual_edit_on_xuong_side()
    {
        var dc = Linked();
        dc.ApplyEdit(1, "xuong", "2026-11-01", MC, "sl", 2);
        dc.ApplyEdit(1, "xuong", "2026-11-01", MC, "net", 24);
        Assert.Equal("khop", dc.P1.Result!.Items.First(i => i.Ma == MC).Status);
        dc.UndoEdit(dc.St.Edits[0].Id);
        dc.UndoEdit(dc.St.Edits[0].Id);
        Assert.Equal("chi_kho", dc.P1.Result!.Items.First(i => i.Ma == MC).Status);
    }

    [Fact]
    public void Viewer_without_edit_cannot_write()
    {
        var dc = Linked();
        _s.Auth.CreateUser("xem", "Người xem", "matkhau1", Roles.Staff, Perm.Serialize(new Dictionary<string, string> { ["m5"] = Perm.View }));
        _s.Auth.Logout(); _s.Auth.Login("xem", "matkhau1");
        Assert.ThrowsAny<Exception>(() => dc.ApplyOps(new() { new Op { Type = "delete", Slot = 1, Ngay = "2026-11-01", Ma = MC } }, "x"));
    }
}
