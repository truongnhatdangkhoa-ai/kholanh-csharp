using KhoLanh.Core.Entry;
using KhoLanh.Core.Grid;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.62: công thức Excel, tự điền theo mã hàng (gõ / dán nhiều dòng), ngày tạo dòng, màu ô.</summary>
public class EntryRulesTests
{
    private sealed class Row : CongThuc.IRow
    {
        private readonly Dictionary<string, string> _d; private readonly string[] _cols;
        public Row(params (string h, string v)[] cells) { _d = cells.ToDictionary(c => c.h, c => c.v); _cols = cells.Select(c => c.h).ToArray(); }
        public string? ByName(string header) => _d.FirstOrDefault(kv => string.Equals(kv.Key, header, StringComparison.OrdinalIgnoreCase)).Value;
        public string? ByIndex(int col) => col >= 0 && col < _cols.Length ? _d[_cols[col]] : null;
    }

    [Fact]
    public void Formulas_like_excel()
    {
        var row = new Row(("Số lượng", "12"), ("Trọng lượng", "10,8"), ("Mã", "abc"), ("Trống", ""));
        Assert.Equal("129.6", CongThuc.Eval("=[Số lượng]*[Trọng lượng]", row));
        Assert.Equal("129.6", CongThuc.Eval("=A1*B1", row));                    // chữ cột như Excel, số dòng bỏ qua
        Assert.Equal("22.8", CongThuc.Eval("=SUM(A, B, D)", row));
        Assert.Equal("130", CongThuc.Eval("=ROUND(A*B; 0)", row));              // dấu ; kiểu Excel tiếng Việt
        Assert.Equal("lớn", CongThuc.Eval("=IF([Số lượng]>10,\"lớn\",\"nhỏ\")", row));
        Assert.Equal("ABC-12", CongThuc.Eval("=UPPER(C)&\"-\"&A", row));
        Assert.Equal("#DIV/0!", CongThuc.Eval("=A/0", row));
        Assert.Equal("0", CongThuc.Eval("=IFERROR(A/0,0)", row));
        Assert.Equal("#NAME?", CongThuc.Eval("=FOO(A)", row));
        Assert.Equal("#REF!", CongThuc.Eval("=[Không có]+1", row));
        Assert.Equal("6", CongThuc.Eval("=2+2^2*1-0%", row));
        Assert.Equal("", CongThuc.Eval("=A*D", row));                       // thiếu số → để trống, không ghi 0
        Assert.Equal("12", CongThuc.Eval("=A+D", row));
        Assert.Equal("TRUE", CongThuc.Eval("=AND(A>1, NOT(D<>\"\"))", row));
        Assert.Equal("ab", CongThuc.Eval("=LEFT(C,2)", row));
        Assert.Equal("2", CongThuc.Eval("=MOD(12,5)", row));
        Assert.NotNull(CongThuc.Check("=SUM(A,"));
        Assert.Null(CongThuc.Check("=[Số lượng]*[Trọng lượng]"));
    }

    private static List<GridColumn> KiemKe() => EntryService.BuiltIn.First(t => t.Id == "kiem-ke").Columns.Select(c => c.Clone()).ToList();

    private static readonly Dictionary<string, EntryRules.Info> Cat = new()
    {
        ["MA1"] = new("Tôm 1", "16/20", "10.8"), ["MA2"] = new("Tôm 2", "21/25", "2"),
    };
    private static EntryRules.Info? Look(string m) => Cat.GetValueOrDefault(m.Trim().ToUpperInvariant());

    private static GridModel Model(List<GridColumn> cols, DateTime today)
    {
        var m = new GridModel();
        m.Load(cols, Array.Empty<string[]>());
        foreach (var c in m.Columns) c.ReadOnly = c.Formula.Length > 0;
        m.Derive = ch => EntryRules.Derive(m.Columns, m.Get, ch, Look, today);
        return m;
    }

    [Fact]
    public void Kiem_ke_template_fills_size_weight_net_and_date_once()
    {
        var cols = KiemKe();
        Assert.Equal(new[] { "Vị trí", "Mã hàng", "Size", "Trọng lượng", "Số lượng", "Tổng Net(kg)", "Ghi chú", "Ngày nhập",
            "Số lượng xuất", "Số lượng còn lại", "Tổng Net(kg) còn lại", "Ngày xuất" }, cols.Select(c => c.Header));
        var day1 = new DateTime(2026, 11, 24);
        var m = Model(cols, day1);

        // dán 2 dòng mã hàng + số lượng (dán nhiều dòng cũng tự điền)
        m.Paste(new CellRange(0, 1, 0, 1), "ma1\r\nMA2\r\n");
        m.SetCells(new[] { (0, 4, "5"), (1, 4, "3") });
        Assert.Equal(new[] { "", "ma1", "16/20", "10.8", "5", "54", "", "24/11/2026", "", "5", "54", "" }, m.Row(0));
        Assert.Equal(new[] { "21/25", "2", "6", "24/11/2026" }, new[] { m.Get(1, 2), m.Get(1, 3), m.Get(1, 5), m.Get(1, 7) });

        // 1.71: gõ số lượng xuất → còn lại = SL − SL xuất, Net còn lại = còn lại × TL, ngày xuất = hôm nay (1 lần)
        m.Set(1, 8, "1");
        Assert.Equal(new[] { "2", "4", "24/11/2026" }, new[] { m.Get(1, 9), m.Get(1, 10), m.Get(1, 11) });
        m.Undo();
        Assert.Equal(new[] { "", "3", "" }, new[] { m.Get(1, 8), m.Get(1, 9), m.Get(1, 11) });

        // ngày sau sửa phiếu: ngày nhập không đổi; đổi mã → size / trọng lượng / net cập nhật
        m.Derive = ch => EntryRules.Derive(m.Columns, m.Get, ch, Look, day1.AddDays(3));
        m.Set(0, 1, "MA2");
        Assert.Equal(new[] { "21/25", "2", "10", "24/11/2026" }, new[] { m.Get(0, 2), m.Get(0, 3), m.Get(0, 5), m.Get(0, 7) });
        // sửa tay ngày nhập được
        m.Set(0, 7, "20/11/2026");
        Assert.Equal("20/11/2026", m.Get(0, 7));
        // hoàn tác 1 lần trả cả ô tự điền
        m.Undo(); m.Undo();
        Assert.Equal(new[] { "ma1", "16/20", "10.8", "54", "24/11/2026" }, new[] { m.Get(0, 1), m.Get(0, 2), m.Get(0, 3), m.Get(0, 5), m.Get(0, 7) });
        // dòng mới hôm nay → ngày hôm nay
        m.Set(2, 1, "MA1");
        Assert.Equal("27/11/2026", m.Get(2, 7));
        // xóa hết phần nhập tay → ô tự động của dòng cũng trống
        m.Clear(new CellRange(2, 0, 2, 1));
        Assert.All(m.Row(2), v => Assert.Equal("", v));
    }

    [Fact]
    public void Old_pages_fill_by_column_name_without_overwriting_typed_values()
    {
        // trang cũ: cột chưa đặt tự điền
        var cols = new List<GridColumn> { new("c0", "Mã hàng"), new("c1", "Tên hàng"), new("c2", "Size"), new("c3", "Kg") };
        var m = Model(cols, DateTime.Today);
        m.Set(0, 2, "tự gõ");
        m.Paste(new CellRange(0, 0, 0, 0), "MA1\nMA2\nKHONGCO");
        Assert.Equal(new[] { "MA1", "Tôm 1", "tự gõ", "" }, m.Row(0));       // Size gõ tay giữ nguyên, "Kg" không đoán
        Assert.Equal(new[] { "MA2", "Tôm 2", "21/25", "" }, m.Row(1));
        Assert.Equal(new[] { "KHONGCO", "", "", "" }, m.Row(2));
        m.Set(1, 0, "MA1");                                                 // đổi mã → giá trị tự điền của mã cũ được thay
        Assert.Equal(new[] { "MA1", "Tôm 1", "16/20", "" }, m.Row(1));
        m.Set(1, 0, "KHONGCO");                                             // mã không tra được → bỏ giá trị của mã cũ
        Assert.Equal(new[] { "KHONGCO", "", "", "" }, m.Row(1));
        // dán cả mã lẫn tên cùng lúc → giữ tên đã dán
        m.Paste(new CellRange(3, 0, 3, 0), "MA2\tTên riêng");
        Assert.Equal("Tên riêng", m.Get(3, 1));
        Assert.Equal("21/25", m.Get(3, 2));
    }

    [Fact]
    public void Recompute_all_on_save_and_colors()
    {
        var cols = KiemKe();
        var rows = new List<IReadOnlyList<string>> { new[] { "A", "MA1", "", "10", "4", "", "", "" }, new[] { "", "", "", "", "", "99", "", "" } };
        var fx = EntryRules.RecomputeAll(cols, rows);
        Assert.Contains((0, 5, "40"), fx);
        Assert.Contains((1, 5, ""), fx);

        var bg = CellColors.Set("", new[] { "c1", "c3" }, "y");
        bg = CellColors.Set(bg, new[] { "c3" }, "r");
        Assert.Equal("c1:y,c3:r", bg);
        Assert.Equal("c3:r", CellColors.Clean(bg + ",c99:g", cols.Where(c => c.Key != "c1").ToList()));
        Assert.Equal("", CellColors.Set(bg, new[] { "c1", "c3" }, ""));
        Assert.Contains(("c1", "#FFEB9C"), CellColors.Parse(bg));
    }

    [Fact]
    public void Auto_labels_round_trip()
    {
        foreach (var (k, label) in EntryRules.AutoKinds) Assert.Equal(k, EntryRules.AutoKey(label));
        Assert.Equal(EntryRules.Tl, EntryRules.AutoKey("trọng lượng"));
    }
}
