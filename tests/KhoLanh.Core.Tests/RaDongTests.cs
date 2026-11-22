using ClosedXML.Excel;
using KhoLanh.Core.Models;
using KhoLanh.Core.RaDong;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

public class RaDongTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-rd-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    // mã 21 ký tự: SP(1–4) · 5–7 · TT(8–10) · MB(11–13) · Size(14–17) · 18–21
    private const string A = "VRHLBTNXECR003140IME1", B = "VRHLBTNXXXR000914IME1", C = "VRHLBTNXXXTGS0010IME1";

    public RaDongTests()
    {
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private string MakeFile(string name, params (DateTime d, string code, double sl, double tl, object net)[] rows)
    {
        var path = Path.Combine(_dir, name);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = "BÁO CÁO NHẬP XUẤT TRONG NGÀY";
        var heads = new[] { "Loại chứng từ", "Mã hàng", "Tên hàng", "Số lượng", "Ngày chứng từ", "Trọng lượng", "Net", "Ca" };
        for (var i = 0; i < heads.Length; i++) ws.Cell(3, i + 1).Value = heads[i];
        var r = 4;
        foreach (var x in rows)
        {
            ws.Cell(r, 1).Value = "Xuất xả";
            ws.Cell(r, 2).Value = "TPC-" + x.code;
            ws.Cell(r, 3).Value = x.code == B ? "Tôm HLSO, 10%, 91/120 (9 kg x 1 gói)" : "Tôm";
            ws.Cell(r, 4).Value = x.sl; ws.Cell(r, 5).Value = x.d; ws.Cell(r, 6).Value = x.tl;
            if (x.net is double n) ws.Cell(r, 7).Value = n;
            ws.Cell(r, 8).Value = 1;
            r++;
        }
        ws.Cell(r, 1).Value = "Sum:"; ws.Cell(r, 7).Value = 999;
        wb.SaveAs(path);
        return path;
    }

    private static readonly DateTime D1 = new(2026, 11, 1), D2 = new(2026, 11, 2);

    [Fact]
    public void Read_group_auto_net_and_size_labels()
    {
        var rd = _s.RaDong; rd.Load();
        var d = RaDongService.ReadExcel(MakeFile("a.xlsx", (D1, A, 10, 10.8, 108.0), (D1, A, 5, 10.8, ""), (D1, B, 2, 9, 18.0), (D1, C, 3, 12, 36.0), (D2, A, 1, 10.8, 10.8)));
        Assert.Equal(5, d.Rows.Count);            // dòng tiêu đề báo cáo + dòng Sum bị bỏ
        Assert.Equal("Mã hàng", d.CodeCol);
        rd.SetBang1(d);
        Assert.Equal("91/120", rd.SizeLabels["0914"]);
        var g = rd.Compute();
        Assert.Equal("Net", g.NetCol);
        Assert.Equal(new[] { "Số lượng", "Net" }, g.NumericCols);
        var day1 = g.Order.Select(k => g.ByKey[k]).Where(x => x.Date == "01/11/2026").ToList();
        Assert.Equal(new[] { "BTP", "ECUADOR", "TGS" }, day1.Select(x => x.Product));
        var ec = day1[1];
        Assert.Equal(2, ec.Count); Assert.Equal(15, ec.Sums["Số lượng"]); Assert.Equal(162, ec.Sums["Net"]); Assert.Equal(1, ec.AutoNet);
        Assert.Equal("31/40", rd.FormatSize(ec.Size));
        Assert.Equal("91/120", rd.FormatSize(day1[0].Size));
        Assert.Equal("10", rd.FormatSize(day1[2].Size));
        Assert.Equal(new[] { "10.8" }, ec.WeightValues);
        // đọc lại từ database: giữ nguyên dữ liệu
        rd.Load();
        Assert.Equal(5, rd.Bang1.Rows.Count);
        Assert.Equal(162, rd.Compute().ByKey[ec.Key].Sums["Net"]);
    }

    [Fact]
    public void Edit_groups_raw_rows_and_undo()
    {
        var rd = _s.RaDong; rd.Load();
        rd.SetBang1(RaDongService.ReadExcel(MakeFile("a.xlsx", (D1, A, 10, 10.8, 108.0), (D1, C, 3, 12, 36.0))));
        var key = rd.Compute().Order.First(k => k.Contains("ECUADOR"));
        rd.EditGroup(key, "Net", "1.234,5");
        rd.EditGroup(key, "__p", "EC MỚI");
        rd.Load(); // sửa tay được lưu (meta.edits)
        var g = rd.Compute().ByKey[key];
        Assert.Equal(1234.5, g.Sums["Net"]); Assert.Equal("EC MỚI", g.Product); Assert.True(g.Edited);
        Assert.Contains(rd.DaysFrom()["2026-11-01"], x => x.Ma == "EC MỚI" && x.Net == 1234.5);
        rd.EditGroup(key, "Net", "108"); // trùng số gốc → bỏ sửa tay của ô đó
        Assert.Null(rd.Bang1.Edits[key].V);
        Assert.Throws<BusinessException>(() => rd.EditGroup(key, "Net", "abc"));
        Assert.Equal(1, rd.DeleteGroups(new[] { key }));
        Assert.Single(rd.Bang1.Rows);
        Assert.Equal("Xóa 1 nhóm", rd.UndoBang2());
        Assert.Equal(2, rd.Bang1.Rows.Count);
        var row = rd.Bang1.Rows[0];
        rd.EditRaw(row, "Net", "50", numeric: true);
        Assert.Equal(50, rd.Compute(noEdits: true).ByKey[key].Sums["Net"]);
    }

    [Fact]
    public void Save_to_bang3_update_keeps_manual_edits_and_undo()
    {
        var rd = _s.RaDong; rd.Load();
        var f1 = RaDongService.ReadExcel(MakeFile("a.xlsx", (D1, A, 10, 10.8, 108.0), (D1, C, 3, 12, 36.0), (D2, A, 1, 10.8, 10.8)));
        rd.SetBang1(f1);
        var plan = rd.DaysFrom().Select(p => new RaDongService.DayPlan { Date = p.Key, Rows = p.Value }).ToList();
        var res = rd.Apply(plan, f1.FileName, f1.FileHash, f1.FileSize, "replace");
        Assert.Equal((2, 3), (res.Days, res.NewRows));
        Assert.Equal(3, rd.SavedRows.Count);
        Assert.NotNull(rd.SeenBefore(f1.FileHash));
        var ec = rd.SavedRows.First(r => r.Date == "2026-11-01" && r.Ma == "ECUADOR");
        Assert.Equal(10, ec.Sl);
        // sửa tay: sửa SL, giữ nguyên Net → Net tự tính theo kg/đơn vị
        rd.EditRow(ec.Id, ec.Ma, ec.Size, "20", "108");
        Assert.Equal(216, ec.Net); Assert.True(ec.Edited);
        var tgs = rd.SavedRows.First(r => r.Ma == "TGS");
        // file cập nhật: ECUADOR khác số (đã sửa tay → mặc định giữ), TGS khác số → lấy mới, thêm BTP
        var f2 = RaDongService.ReadExcel(MakeFile("b.xlsx", (D1, A, 10, 10.8, 110.0), (D1, C, 3, 12, 40.0), (D1, B, 1, 9, 9.0)));
        var p2 = rd.DaysFrom(f2).Select(p => new RaDongService.DayPlan { Date = p.Key, Rows = p.Value }).ToList();
        var calc = rd.Calc(p2[0]);
        Assert.Equal("update", calc.Mode);
        Assert.Contains(calc.Items, x => x.Kind == RaDongService.Kind.Manual && x.Ma == "ECUADOR");
        Assert.Contains(calc.Items, x => x.Kind == RaDongService.Kind.Chg && x.Ma == "TGS");
        Assert.Contains(calc.Items, x => x.Kind == RaDongService.Kind.New && x.Ma == "BTP");
        var r2 = rd.Apply(p2, f2.FileName, f2.FileHash, f2.FileSize);
        Assert.Equal((1, 1), (r2.NewRows, r2.Changed));
        Assert.Equal(216, rd.SavedRows.First(r => r.Id == ec.Id).Net);       // giữ số đã sửa
        Assert.Equal(40, rd.SavedRows.First(r => r.Id == tgs.Id).Net);
        Assert.Equal(4, rd.SavedRows.Count);
        rd.Load();
        Assert.NotNull(rd.Undo);
        rd.UndoUpdate();
        Assert.Equal(3, rd.SavedRows.Count);
        Assert.Equal(36, rd.SavedRows.First(r => r.Ma == "TGS").Net);
        // nạp lại đúng file đó: không có gì đổi
        var p3 = rd.DaysFrom(f1).Where(p => p.Key == "2026-11-02").Select(p => new RaDongService.DayPlan { Date = p.Key, Rows = p.Value }).ToList();
        Assert.True(rd.Apply(p3, f1.FileName, f1.FileHash, f1.FileSize).NoChange);
    }

    [Fact]
    public void Add_delete_and_export_year()
    {
        var rd = _s.RaDong; rd.Load();
        rd.AddRow("2026-10-05", "BTP", "31/40", "10", "120");
        rd.AddRow("2026-10-06", "BTP", "31/40", "5", "");   // Net tự tính = 5 × 12
        Assert.Equal(60, rd.SavedRows.Last().Net);
        rd.AddRow("2026-11-01", "NL", "90", "", "80,5");
        Assert.Throws<BusinessException>(() => rd.AddRow("2026-11-01", "NL", "", "", "1"));
        var path = Path.Combine(_dir, "y.xlsx");
        rd.ExportYear(2026, path, new DateTime(2026, 11, 22));
        using (var wb = new XLWorkbook(path))
        {
            Assert.Equal(new[] { "Tổng 2026", "tháng 11", "tháng 10" }, wb.Worksheets.Select(w => w.Name));
            var y = wb.Worksheet("Tổng 2026");
            Assert.Equal("'tháng 10'!D4", y.Cell(8, 11).FormulaA1);  // ngày 5/10 → ô Tổng cộng của khối ngày
            Assert.Equal("X", y.Cell(7, 11).GetString());             // ngày 4/10 không xả
            Assert.Equal("X", y.Cell(34, 12).GetString());            // 31/11 không tồn tại
            var m10 = wb.Worksheet("tháng 10");
            Assert.Equal("NGÀY : 05.10.2026", m10.Cell(1, 1).GetString());
            Assert.Equal("NGÀY : 06.10.2026", m10.Cell(1, 6).GetString());
            Assert.Equal("SUM(D3:D3)", m10.Cell(4, 4).FormulaA1);
        }
        Assert.Equal(new[] { 11, 10, 9, 12 }, RaDongService.MonthOrder(new[] { 9, 10, 11, 12 }, 2026, new DateTime(2026, 11, 22)));
        rd.DeleteMonth(2026, 10);
        Assert.Single(rd.SavedRows);
        rd.DeleteDays(new[] { "2026-11-01" });
        Assert.Empty(rd.SavedRows);
        Assert.Equal("o345gr:3", RaDongService.Fingerprint(new byte[] { 1, 2, 3 })); // giống t3sFingerprint của app cũ
    }

    [Fact]
    public void Staff_with_export_only_cannot_write()
    {
        _s.Auth.CreateUser("nv01", "NV", "matkhau1", Roles.Staff, Perm.Serialize(new Dictionary<string, string> { ["m4"] = Perm.Export }));
        _s.Auth.Login("nv01", "matkhau1");
        var rd = _s.RaDong; rd.Load();
        Assert.False(rd.CanEdit);
        Assert.Throws<BusinessException>(() => rd.AddRow("2026-11-01", "A", "1", "", "1"));
    }
}
