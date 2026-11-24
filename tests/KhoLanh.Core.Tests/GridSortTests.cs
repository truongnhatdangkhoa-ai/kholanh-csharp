using KhoLanh.Core.Grid;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.71: sắp xếp theo cột (A→Z / Z→A) và định dạng số thống nhất 1,500.00.</summary>
public class GridSortTests
{
    private static List<string> Sorted(string[] v, bool desc) => GridSort.Order(Enumerable.Range(0, v.Length), r => v[r], desc).Select(r => v[r]).ToList();

    [Fact]
    public void Xep_so_theo_gia_tri_o_trong_cuoi()
    {
        var v = new[] { "1,500.00", "", "20", "3.5", "100" };
        Assert.Equal(new[] { "3.5", "20", "100", "1,500.00", "" }, Sorted(v, false));
        Assert.Equal(new[] { "1,500.00", "100", "20", "3.5", "" }, Sorted(v, true));
    }

    [Fact]
    public void Xep_ngay_va_chu_tieng_viet()
    {
        Assert.Equal(new[] { "20/11/2025", "05/01/2026", "02/12/2026" }, Sorted(new[] { "02/12/2026", "20/11/2025", "05/01/2026" }, false));
        Assert.Equal(new[] { "an", "Bình", "đức", "Gấm" }, Sorted(new[] { "Gấm", "đức", "an", "Bình" }, false));
        Assert.Equal(new[] { "Gấm", "đức", "Bình", "an" }, Sorted(new[] { "Gấm", "đức", "an", "Bình" }, true));
    }

    [Fact]
    public void Sap_xep_on_dinh()
    {
        var v = new[] { "B", "A", "B", "A" };
        Assert.Equal(new[] { 1, 3, 0, 2 }, GridSort.Order(Enumerable.Range(0, 4), r => v[r], false));
    }

    [Fact]
    public void Dinh_dang_so_mot_kieu()
    {
        Assert.Equal("1,500.00", NumFmt.Kg(1500));
        Assert.Equal("1.50", NumFmt.Kg(1.5));
        Assert.Equal("1,500", NumFmt.Count(1500));
        Assert.Equal("1", NumFmt.Count(1));
        Assert.True(NumFmt.TryParse("1,500.00", out var a)); Assert.Equal(1500, a);
        Assert.True(NumFmt.TryParse("1.5", out var b)); Assert.Equal(1.5, b);
        Assert.True(NumFmt.TryParse("1.500", out var c)); Assert.Equal(1.5, c);          // kiểu mới: 1.500 = một phẩy năm
        Assert.False(NumFmt.TryParse("1.500,5", out _));                                // kiểu cũ không còn đọc khi gõ
        Assert.False(NumFmt.TryParse("1,5", out _));
        Assert.Equal("1,500.00", NumFmt.Show("1500", NumFmt.Cls.Kg, false));
        Assert.Equal("35,019.00", NumFmt.Show("35.019,00", NumFmt.Cls.Kg, true));       // dữ liệu cũ kiểu Việt trong bảng chỉ đọc
        Assert.Equal("63", NumFmt.Show("63", NumFmt.Cls.Count, false));
        Assert.Equal("VRHL0123", NumFmt.Show("VRHL0123", NumFmt.Cls.Kg, false));
        Assert.Equal("1,500.00", 1500.0.ToString("N2", NumFmt.Culture));
        Assert.Equal("24/11/2026", new DateTime(2026, 11, 24).ToString("d", NumFmt.Culture));
    }

    [Fact]
    public void Loai_cot_theo_ten()
    {
        Assert.Equal(NumFmt.Cls.Kg, NumFmt.ClassOf("Trọng lượng"));
        Assert.Equal(NumFmt.Cls.Kg, NumFmt.ClassOf("Tổng Net(kg)"));
        Assert.Equal(NumFmt.Cls.Kg, NumFmt.ClassOf("Kg / thùng"));
        Assert.Equal(NumFmt.Cls.Count, NumFmt.ClassOf("Số lượng"));
        Assert.Equal(NumFmt.Cls.Count, NumFmt.ClassOf("Số kiện"));
        Assert.Equal(NumFmt.Cls.None, NumFmt.ClassOf("Mã hàng"));
        Assert.Equal(NumFmt.Cls.None, NumFmt.ClassOf("Ngày nhập"));
        Assert.Equal(NumFmt.Cls.None, NumFmt.ClassOf("Size"));
    }

    [Fact]
    public void Trang_kiem_ke_cu_tu_them_cot_moi()
    {
        var old = new List<GridColumn>
        {
            new("c0", "Vị trí"), new("c1", "Mã hàng"), new("c2", "Size"), new("c3", "Trọng lượng", 100, "number"), new("c4", "Số lượng", 90, "number"),
            new("c5", "Net(kg)", 100, "number") { Formula = "=[Số lượng]*[Trọng lượng]" }, new("c6", "Ghi chú"), new("c7", "Ngày nhập", 100, "date"),
        };
        var up = KhoLanh.Core.Entry.EntryService.UpgradeKiemKe(old);
        Assert.Equal(new[] { "Vị trí", "Mã hàng", "Size", "Trọng lượng", "Số lượng", "Tổng Net(kg)", "Ghi chú", "Ngày nhập", "Số lượng xuất", "Số lượng còn lại", "Tổng Net(kg) còn lại", "Ngày xuất" }, up.Select(c => c.Header));
        Assert.Equal(new[] { "c0", "c1", "c2", "c3", "c4", "c5", "c6", "c7", "c8", "c9", "c10", "c11" }, up.Select(c => c.Key));
        Assert.Equal(12, KhoLanh.Core.Entry.EntryService.UpgradeKiemKe(up).Count);   // chạy lại không thêm nữa
    }

    [Fact]
    public void Doi_chieu_kiem_ke_dung_so_luong_con_lai()
    {
        var cols = new List<string> { "Vị trí", "Mã hàng", "Trọng lượng", "Số lượng", "Tổng Net(kg)", "Số lượng xuất", "Số lượng còn lại", "Tổng Net(kg) còn lại", "Ngày xuất" };
        var page = new KhoLanh.Core.KiemKe.KiemKeCore.Page { Id = "p", Name = "KK 24-11-2026", Ngay = "2026-11-24", Columns = cols,
            Rows = new() { new[] { "A1", "MA1", "10", "5", "50", "2", "3", "30", "24/11/2026" }, new[] { "A2", "MA2", "2", "4", "8", "", "", "", "" } } };
        var x = KhoLanh.Core.KiemKe.KiemKeCore.ExtractRows(new[] { page });
        Assert.Equal(new[] { 3.0, 4.0 }, x.Rows.Select(r => r.Kien));
        Assert.Equal(new double?[] { 30, 8 }, x.Rows.Select(r => r.Kg));
        Assert.Empty(x.Mv);                                                    // SL xuất đã trừ sẵn — không thành phát sinh
    }
}
