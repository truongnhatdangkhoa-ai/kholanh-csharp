using KhoLanh.Core.Grid;
using Xunit;

namespace KhoLanh.Core.Tests;

public class GridFilterTests
{
    private static readonly string[][] Data =
    {
        new[] { "Tôm thẻ HLSO", "21/11/2026", "1,250.50", "Đông lạnh" },
        new[] { "Tôm sú PD", "22/11/2026", "300", "Đông lạnh" },
        new[] { "Mực ống", "05/10/2026", "0", "" },
        new[] { "Tôm thẻ PTO", "02/01/2025", "-12", "Mát" },
    };
    private static string Cell(int r, int c) => Data[r][c];

    [Fact]
    public void Text_search_ignores_case_and_vietnamese_marks()
    {
        Assert.True(GridFilter.MatchText("Tôm thẻ HLSO", "tom the"));
        Assert.True(GridFilter.MatchText("Đông lạnh", "dong"));
        Assert.True(GridFilter.MatchText("Tôm thẻ HLSO", "hlso   thẻ"));   // nhiều từ, đảo thứ tự
        Assert.False(GridFilter.MatchText("Tôm sú PD", "the"));
        Assert.True(GridFilter.MatchText("bất kỳ", "  "));
    }

    [Fact]
    public void Number_operators()
    {
        Assert.True(GridFilter.MatchText("1,250.50", ">1000"));
        Assert.False(GridFilter.MatchText("300", ">1000"));
        Assert.True(GridFilter.MatchText("0", "<=0"));
        Assert.True(GridFilter.MatchText("-12", "<0"));
        Assert.True(GridFilter.MatchText("300", "=300"));
        Assert.True(GridFilter.MatchText("300", "<>0"));
        Assert.False(GridFilter.MatchText("abc", ">1"));
        Assert.True(GridFilter.MatchText("Mát", "=mat"));                     // = với chữ: bằng đúng
        Assert.False(GridFilter.MatchText("Mát lạnh", "=mat"));
    }

    [Fact]
    public void Numbers_parse_both_styles()
    {
        Assert.True(GridFilter.TryNumber("1,234.5", out var a)); Assert.Equal(1234.5, a);
        Assert.True(GridFilter.TryNumber("1.234,5", out var b)); Assert.Equal(1234.5, b);
        Assert.True(GridFilter.TryNumber("12,5", out var c)); Assert.Equal(12.5, c);
        Assert.True(GridFilter.TryNumber("1,234", out var d)); Assert.Equal(1234, d);
    }

    [Fact]
    public void Rows_combine_columns_and_value_lists()
    {
        var f = new Dictionary<int, ColumnFilter>
        {
            [0] = new() { Text = "tôm" },
            [3] = new() { Allowed = new HashSet<string> { "Đông lạnh" } },
        };
        Assert.Equal(new[] { 0, 1 }, GridFilter.Rows(Data.Length, Cell, f));
        // phân tầng: bỏ qua cột 3 → danh sách của cột 3 chỉ còn giá trị của các dòng "tôm"
        var rows = GridFilter.Rows(Data.Length, Cell, f, skipCol: 3);
        Assert.Equal(new[] { 0, 1, 3 }, rows);
        var tree = GridFilter.BuildTree(rows.Select(r => Cell(r, 3)));
        Assert.Equal(new[] { "Đông lạnh", "Mát" }, tree.Select(n => n.Label));
        Assert.Equal(2, tree[0].Count);
    }

    [Fact]
    public void Blank_values_are_listed_and_filterable()
    {
        var tree = GridFilter.BuildTree(Data.Select(d => d[3]));
        Assert.Equal(GridFilter.Blank, tree[^1].Label);
        var f = new Dictionary<int, ColumnFilter> { [3] = new() { Allowed = new HashSet<string> { GridFilter.Blank } } };
        Assert.Equal(new[] { 2 }, GridFilter.Rows(Data.Length, Cell, f));
    }

    [Fact]
    public void Date_column_is_grouped_year_month_day()
    {
        var tree = GridFilter.BuildTree(Data.Select(d => d[1]));
        Assert.Equal(new[] { "Năm 2026", "Năm 2025" }, tree.Select(n => n.Label));
        Assert.Equal(new[] { "Tháng 10", "Tháng 11" }, tree[0].Children.Select(n => n.Label));
        Assert.Equal(new[] { "21/11/2026", "22/11/2026" }, tree[0].Children[1].Children.Select(n => n.Label));
        Assert.Equal(3, tree[0].Count);
        Assert.Equal(new[] { "05/10/2026", "21/11/2026", "22/11/2026" }, tree[0].Values().OrderBy(x => x));
    }

    [Fact]
    public void Number_column_sorted_by_value()
    {
        var tree = GridFilter.BuildTree(Data.Select(d => d[2]));
        Assert.Equal(new[] { "-12", "0", "300", "1,250.50" }, tree.Select(n => n.Label));
    }
}
