using KhoLanh.Core.Grid;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>Dòng tổng cuối bảng: chọn cột cộng theo tên cột + tổng theo dòng đang hiện / dòng đã tích.</summary>
public class GridTotalsTests
{
    [Theory]
    [InlineData("SL tồn", true)] [InlineData("SL xuất ✎", true)] [InlineData("Net tồn (kg)", true)] [InlineData("Tồn cuối", true)]
    [InlineData("Số thùng", true)] [InlineData("Kg", true)] [InlineData("Tổng (kg)", true)] [InlineData("Kiện/Bao", true)] [InlineData("Số kiện", true)]
    [InlineData("Trọng Lượng (KG)", true)] [InlineData("Đã xuất", true)] [InlineData("Tấn", true)]
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
}
