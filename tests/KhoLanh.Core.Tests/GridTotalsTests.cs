using KhoLanh.Core.Grid;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Dòng tổng cuối bảng: chọn cột cộng theo tên cột + tổng theo dòng đang hiện / dòng đã tích.</summary>
public class GridTotalsTests
{
    [Theory]
    [InlineData("SL tồn", true)] [InlineData("SL xuất ✎", true)] [InlineData("Net tồn (kg)", true)] [InlineData("Tồn cuối", true)]
    [InlineData("Số thùng", true)] [InlineData("Kg", false)] [InlineData("Kg xuất", true)] [InlineData("Tổng (kg)", true)] [InlineData("Kiện/Bao", true)] [InlineData("Số kiện", true)]
    [InlineData("Trọng Lượng (KG)", true)] [InlineData("Đã xuất", true)] [InlineData("Đã làm phiếu", true)] [InlineData("Tấn", true)]
    [InlineData("T.Lượng", false)] [InlineData("Trọng lượng", false)] [InlineData("Net (kg/đơn vị)", false)] [InlineData("Trọng lượng (kg) (kg/bao)", false)]
    [InlineData("Size", false)] [InlineData("Ngày nhập", false)] [InlineData("Mã hàng", false)] [InlineData("Lô", false)] [InlineData("Vị trí", false)]
    [InlineData("Hợp đồng", false)] [InlineData("Đơn giá", false)] [InlineData("Tỷ lệ %", false)] [InlineData("Tên hàng", false)] [InlineData("Ghi chú kho", false)]
    public void Chon_cot_cong_theo_ten(string header, bool sum) => Assert.Equal(sum, GridTotals.DefaultSum(header));

    [Fact]
    public void Cong_theo_dong_dang_hien_va_dong_da_tich()
    {
        var m = new GridModel();
        m.Load(new[] { new GridColumn("a", "Mã hàng", 100), new GridColumn("b", "SL tồn", 60, "number"), new GridColumn("c", "SL xuất", 60, "number") },
            new[] { new[] { "A", "1,200", "" }, new[] { "B", "50", "10" }, new[] { "C", "x", "5" }, new[] { "D", "7.5", "" } });
        bool Sum(int c) => GridTotals.DefaultSum(m.Columns[c].Header);
        var all = GridTotals.Sum(m, new[] { 0, 1, 2, 3 }, Sum);
        Assert.Null(all[0]); Assert.Equal(1257.5, all[1]); Assert.Equal(15, all[2]);
        var shown = GridTotals.Sum(m, new[] { 1, 2 }, Sum);
        Assert.Equal(50, shown[1]);
        var ticked = GridTotals.Sum(m, new[] { 0, 1, 2, 3 }, Sum, r => m.Get(r, 2).Length > 0);
        Assert.Equal(50, ticked[1]); Assert.Equal(15, ticked[2]);
    }

    [Fact]
    public void Cot_so_kieu_Viet_da_dinh_dang()
    {
        var m = new GridModel();
        m.Load(new[] { new GridColumn("a", "Kiểm kê (kiện)", 80), new GridColumn("b", "Kiểm kê (kg)", 90), new GridColumn("c", "Chênh (kiện)", 70), new GridColumn("d", "SL tồn", 70) },
            new[] { new[] { "3.891", "35.019,00", "-24", "10.8" }, new[] { "462", "4.158,00", "+18", "1263.6" }, new[] { "4.182", "45.165,60", "+10", "5" } });
        var r = GridTotals.Sum(m, new[] { 0, 1, 2 }, _ => true);
        Assert.Equal(8535, r[0]); Assert.Equal(84342.6, r[1]!.Value, 6); Assert.Equal(4, r[2]); Assert.Equal(1279.4, r[3]!.Value, 6);
    }
}
