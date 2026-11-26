using KhoLanh.Core.Entry;
using KhoLanh.Core.Grid;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.117: Nhập liệu › Mẫu — cột hệ thống của mẫu có sẵn khóa cứng (chỉ đổi tên / độ rộng), đổi tên cột thì công thức đổi theo.</summary>
public class NhapLieuMau117Tests
{
    private static EntryService.Template KiemKe => EntryService.BuiltIn.First(b => b.Id == "kiem-ke");

    [Fact]
    public void Cot_he_thong_cua_mau_co_san_la_cot_tu_dien_va_cong_thuc()
    {
        var t = KiemKe;
        var names = EntryService.SystemKeys(t).Select(k => t.Columns.First(c => c.Key == k).Header).ToList();
        Assert.Contains("Size", names);
        Assert.Contains("Trọng lượng", names);
        Assert.Contains("Đặc tính", names);
        Assert.Contains("Tổng Net(kg)", names);
        Assert.Contains("Ngày nhập", names);
        Assert.DoesNotContain("Mã hàng", names);
        Assert.DoesNotContain("Số lượng", names);
        Assert.DoesNotContain("Lô", names);
        // mẫu tự tạo / bản sao: không khóa
        Assert.Empty(EntryService.SystemKeys(t with { Id = "", BuiltIn = false }));
        Assert.Empty(EntryService.SystemKeys(EntryService.BuiltIn.First(b => b.Id == "trong")));
    }

    [Fact]
    public void Sua_cot_he_thong_chi_nhan_ten_va_do_rong_khong_xoa_duoc()
    {
        var t = KiemKe;
        var size = t.Columns.First(c => c.Header == "Size");
        var cols = t.Columns.Select(c => c.Key == size.Key
            ? new GridColumn(c.Key, "Cỡ", 120, "number") { Auto = "", Formula = "=1" } : c.Clone()).ToList();
        var res = EntryService.KeepSystemCols(t, cols, out var err);
        Assert.Null(err);
        var s = res.First(c => c.Key == size.Key);
        Assert.Equal("Cỡ", s.Header);
        Assert.Equal(120, s.Width);
        Assert.Equal(size.Type, s.Type);
        Assert.Equal(EntryRules.Size, s.Auto);
        Assert.Equal("", s.Formula);

        var dt = t.Columns.First(c => c.Header == "Đặc tính");
        EntryService.KeepSystemCols(t, t.Columns.Where(c => c.Key != dt.Key).Select(c => c.Clone()).ToList(), out err);
        Assert.NotNull(err);
        Assert.Contains("Đặc tính", err);
        // xóa cột thường (Ghi chú) thì được
        EntryService.KeepSystemCols(t, t.Columns.Where(c => c.Header != "Ghi chú").Select(c => c.Clone()).ToList(), out err);
        Assert.Null(err);
    }

    [Fact]
    public void Doi_ten_cot_thi_cong_thuc_goi_theo_ten_moi()
    {
        var t = KiemKe;
        var before = t.Columns;
        var after = before.Select(c => c.Header switch
        {
            "Số lượng" => new GridColumn(c.Key, "SL", c.Width, c.Type) { Auto = c.Auto, Formula = c.Formula },
            "Trọng lượng" => new GridColumn(c.Key, "Kg/thùng", c.Width, c.Type) { Auto = c.Auto, Formula = c.Formula },
            _ => c.Clone(),
        }).ToList();
        var res = EntryService.RenameRefs(before, after);
        Assert.Equal("=[SL]*[Kg/thùng]", res.First(c => c.Header == "Tổng Net(kg)").Formula);
        Assert.Equal("=[SL]-[Số lượng xuất]", res.First(c => c.Header == "Số lượng còn lại").Formula);
        Assert.Equal("=[Số lượng còn lại]*[Kg/thùng]", res.First(c => c.Header == "Tổng Net(kg) còn lại").Formula);
        // không đổi tên → giữ nguyên
        Assert.Same(after, EntryService.RenameRefs(after, after));
    }

    [Fact]
    public void O_khoa_cung_khong_go_dan_xoa_duoc_dong_khoa_khong_xoa_duoc()
    {
        var m = new GridModel();
        m.Load(new[] { new GridColumn("a", "A", 80), new GridColumn("b", "B", 80) }, new[] { new[] { "x", "1" }, new[] { "y", "2" }, new[] { "z", "3" } });
        m.CellLocked = (r, c) => c == 1 && m.Get(r, 0) == "y";
        m.SetCells(new[] { (1, 1, "9"), (1, 0, "yy"), (0, 1, "8") });
        Assert.Equal("2", m.Get(1, 1));
        Assert.Equal("yy", m.Get(1, 0));   // cột A của dòng đó vẫn sửa được (đổi tên) → hết khóa theo nội dung
        Assert.Equal("8", m.Get(0, 1));
        Assert.True(m.LockedHit);

        m.Set(1, 0, "y"); m.LockedHit = false;
        m.Paste(new CellRange(0, 1, 2, 1), "5");
        Assert.Equal(new[] { "5", "2", "5" }, new[] { m.Get(0, 1), m.Get(1, 1), m.Get(2, 1) });
        Assert.True(m.LockedHit);

        m.DeleteRows(0, 2);
        Assert.Equal(1, m.RowCount);
        Assert.Equal("y", m.Get(0, 0));
        Assert.True(m.Undo());
        Assert.Equal(new[] { "x", "y", "z" }, Enumerable.Range(0, 3).Select(r => m.Get(r, 0)));
        m.DeleteRows(1, 1);   // chỉ dòng khóa → không làm gì, không thêm lần hoàn tác
        Assert.Equal(3, m.RowCount);
    }
}
