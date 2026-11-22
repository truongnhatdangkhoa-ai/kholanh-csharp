using KhoLanh.Core.Grid;
using Xunit;

namespace KhoLanh.Core.Tests;

public class GridModelTests
{
    private static GridModel Make(int cols = 3, int rows = 2)
    {
        var g = new GridModel();
        g.Load(Enumerable.Range(0, cols).Select(i => new GridColumn("c" + i, GridModel.ColumnName(i))),
            Enumerable.Range(0, rows).Select(r => Enumerable.Range(0, cols).Select(c => $"{r}{c}")));
        return g;
    }

    [Fact]
    public void Parses_excel_clipboard_with_quotes_tabs_and_newlines()
    {
        var t = GridModel.ParseTsv("a\tb\r\n\"x\ty\"\t\"line1\nline2\"\r\n\"he said \"\"hi\"\"\"\t5\"\r\n");
        Assert.Equal(3, t.Count);
        Assert.Equal(new[] { "a", "b" }, t[0]);
        Assert.Equal(new[] { "x\ty", "line1\nline2" }, t[1]);
        Assert.Equal(new[] { "he said \"hi\"", "5\"" }, t[2]);
        Assert.Equal(new[] { "1", "", "3" }, GridModel.ParseTsv("1\t\t3")[0]);
    }

    [Fact]
    public void Copy_then_paste_round_trips()
    {
        var g = Make();
        g.Set(0, 0, "có\ttab");
        var text = g.CopyText(new CellRange(0, 0, 1, 2));
        var h = Make(3, 0);
        var r = h.Paste(new CellRange(0, 0, 0, 0), text);
        Assert.Equal(new CellRange(0, 0, 1, 2), r);
        Assert.Equal("có\ttab", h.Get(0, 0));
        Assert.Equal("12", h.Get(1, 2));
    }

    [Fact]
    public void Paste_adds_rows_fills_selection_and_undoes()
    {
        var g = Make();
        g.Paste(new CellRange(1, 1, 1, 1), "p\tq\r\nr\ts\r\nt\tu\r\n");
        Assert.Equal(4, g.RowCount);
        Assert.Equal("u", g.Get(3, 2));
        g.Paste(new CellRange(0, 0, 3, 0), "z");
        Assert.All(Enumerable.Range(0, 4), r => Assert.Equal("z", g.Get(r, 0)));
        Assert.True(g.Undo());
        Assert.Equal("00", g.Get(0, 0));
        Assert.True(g.Undo());
        Assert.Equal(2, g.RowCount);
        Assert.Equal("11", g.Get(1, 1));
        Assert.True(g.Redo());
        Assert.Equal(4, g.RowCount);
        Assert.Equal("p", g.Get(1, 1));
    }

    [Fact]
    public void Wide_paste_adds_columns_when_allowed()
    {
        var g = Make(2, 1);
        g.AllowAddColumns = true;
        g.Paste(new CellRange(0, 1, 0, 1), "a\tb\tc");
        Assert.Equal(4, g.ColCount);
        Assert.Equal("D", g.Columns[3].Header);
        Assert.Equal("c", g.Get(0, 3));
        g.Undo();
        Assert.Equal(2, g.ColCount);
        Assert.Equal("01", g.Get(0, 1));
    }

    [Fact]
    public void Delete_insert_rows_and_fill_down()
    {
        var g = Make(2, 4);
        g.DeleteRows(1, 2);
        Assert.Equal(2, g.RowCount);
        Assert.Equal("30", g.Get(1, 0));
        g.Undo();
        Assert.Equal("10", g.Get(1, 0));
        Assert.Equal("30", g.Get(3, 0));
        g.InsertRows(1, 2);
        Assert.Equal(6, g.RowCount);
        Assert.Equal("", g.Get(1, 0));
        g.Undo();
        Assert.Equal("10", g.Get(1, 0));
        g.FillDown(new CellRange(0, 0, 3, 1));
        Assert.Equal("01", g.Get(3, 1));
        g.Clear(new CellRange(0, 0, 0, 1));
        Assert.Equal("", g.Get(0, 0));
        Assert.Equal("BA", GridModel.ColumnName(52));
    }
}
