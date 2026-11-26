using KhoLanh.Core.Entry;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

public class EntryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-entry-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;

    public EntryTests()
    {
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }

    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Pages_save_load_trash_and_restore()
    {
        var e = _s.Entry;
        var tpl = EntryService.BuiltIn[1];
        var p = e.Create("Nhập 1", tpl);
        var rows = new List<string[]> { new[] { "01/11/2026", "A1", "Tôm", "16/20", "10", "20", "200", "K1", "", "" }, new string[10].Select(_ => "").ToArray() };
        p = e.Save(p, tpl.Columns, rows);
        Assert.Equal(1, p.Rows); // dòng trống cuối bị bỏ
        var back = e.Load(e.Pages().Single());
        Assert.Equal("Tôm", back[0][2]);
        e.Rename(p.Id, "Nhập kho sáng");
        Assert.Equal("Nhập kho sáng", e.Pages().Single().Name);
        e.Delete(p.Id);
        Assert.Empty(e.Pages());
        Assert.Single(e.Pages(deleted: true));
        e.Restore(p.Id);
        Assert.Single(e.Pages());
        e.Delete(p.Id); e.Delete(p.Id, forever: true);
        Assert.Empty(e.Pages(true));
        Assert.DoesNotContain("T-" + p.Id, _s.Modules.ListSheets(EntryService.Db));
    }

    [Fact]
    public void Templates_and_excel_round_trip()
    {
        var e = _s.Entry;
        var cols = new List<GridColumn> { new("c0", "Mã", 100), new("c1", "Số lượng", 90, "number"), new("c2", "Ngày", 90, "date") };
        e.SaveTemplate("Mẫu A", cols);
        Assert.Contains(e.Templates(), t => t.Name == "Mẫu A" && !t.BuiltIn && t.Columns.Count == 3);
        var path = Path.Combine(_dir, "x.xlsx");
        e.ExportExcel(path, "Trang 1", cols, new List<string[]> { new[] { "0123", "1.234,5", "05/11/2026" }, new[] { "B", "7", "" } });
        var (c, r, sheets) = EntryService.ReadExcel(path);
        Assert.Equal(new[] { "Trang 1" }, sheets);
        Assert.Equal(new[] { "Mã", "Số lượng", "Ngày" }, c.Select(x => x.Header));
        Assert.Equal("0123", r[0][0]);
        Assert.Equal("1234.5", r[0][1]);
        Assert.Equal("05/11/2026", r[0][2]);
        Assert.True(EntryService.TryNumber("1.234.567", out var d) && d == 1234567);
        Assert.True(EntryService.TryNumber("1.234,5", out d) && d == 1234.5);
        Assert.True(EntryService.TryNumber("1,234", out d) && d == 1234);
        Assert.True(EntryService.TryNumber("1,5", out d) && d == 1.5);
    }

    [Fact]
    public void Edit_builtin_template_colors_and_formulas_persist()
    {
        var e = _s.Entry;
        var kk = e.Templates().First(t => t.Id == "kiem-ke");
        Assert.False(kk.Edited);
        var cols = kk.Columns.Select(c => c.Clone()).ToList();
        cols.Add(new GridColumn("c20", "Tổng 2", 90, "number") { Formula = "=[Tổng Net(kg)]*2" });
        e.SaveTemplate("Kiểm kê", cols, "kiem-ke");
        var edited = e.Templates().Single(t => t.Id == "kiem-ke");
        Assert.True(edited.BuiltIn && edited.Edited);
        Assert.Equal("=[Tổng Net(kg)]*2", edited.Columns[^1].Formula);
        Assert.Equal(EntryRules.Today, edited.Columns.Single(c => c.Header == "Ngày nhập").Auto);
        Assert.Throws<BusinessException>(() => e.SaveTemplate("X", new[] { new GridColumn("c0", "A") { Formula = "=SUM(" } }));

        // trang tạo từ mẫu: lưu tính lại công thức, giữ màu ô (bỏ màu của cột không còn)
        var p = e.Create("KK", edited);
        var row = new[] { "A1", "MA", "", "10", "3", "", "", "", "", "", "", "", "", "", "", "c1:y,c5:r,c99:g" };   // 1.115: + Lô, Đặc tính
        p = e.Save(p, p.Columns, new List<string[]> { row });
        var back = e.LoadWithColors(p)[0];
        Assert.Equal("30", back[5]);
        Assert.Equal("3", back[11]);      // số lượng còn lại
        Assert.Equal("30", back[12]);     // net còn lại
        Assert.Equal("60", back[14]);
        Assert.Equal("c1:y,c5:r", back[15]);
        Assert.Equal(15, e.Load(p)[0].Length);

        e.DeleteTemplate("kiem-ke");                    // về mặc định
        Assert.False(e.Templates().Single(t => t.Id == "kiem-ke").Edited);
        Assert.Equal(14, e.Templates().Single(t => t.Id == "kiem-ke").Columns.Count);
    }

    [Fact]
    public void Staff_without_edit_cannot_change_pages()
    {
        _s.Auth.CreateUser("nv01", "NV", "matkhau1", Roles.Staff, Perm.Serialize(new Dictionary<string, string> { ["m6"] = Perm.View }));
        var p = _s.Entry.Create("P", EntryService.BuiltIn[0]);
        _s.Auth.Login("nv01", "matkhau1");
        Assert.Single(_s.Entry.Pages());
        Assert.Throws<BusinessException>(() => _s.Entry.Save(p, p.Columns, new List<string[]>()));
        Assert.Throws<BusinessException>(() => _s.Entry.Create("Q", EntryService.BuiltIn[0]));
    }
}
