using KhoLanh.Core.Entry;
using KhoLanh.Core.Grid;
using KhoLanh.Core.Services;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.130 (người dùng duyệt): gõ vị trí nhanh + ô số lượng tính được ở Nhập liệu.</summary>
public class NhapLieu130Tests
{
    private static List<GridColumn> KiemKe() => EntryService.BuiltIn.First(t => t.Id == "kiem-ke").Columns.Select(c => c.Clone()).ToList();

    [Fact]
    public void Go_vi_tri_nhanh()
    {
        Assert.Equal("TG1.A.01.1.2", EntryRules.NormVt("a0112", "TG1.B.03.2.1"));       // kho theo dòng trên
        Assert.Equal("TG1.A.01.1.2", EntryRules.NormVt("a.1.1.2", "TG1.B.03.2.1"));
        Assert.Equal("TG1.A.01.1.2", EntryRules.NormVt("A 01 1 2", "TG1.B.03.2.1"));
        Assert.Equal("TG1.A.12.3.4", EntryRules.NormVt("a1234", "TG1.B.03.2.1"));
        Assert.Equal("TG2.A.01.1.2", EntryRules.NormVt("tg2a0112", "TG1.B.03.2.1"));     // gõ cả kho
        Assert.Equal("TG2.A.01.1.2", EntryRules.NormVt("2a0112", null));
        Assert.Equal("TG1.B.05.1.3", EntryRules.NormVt("0513", "TG1.B.03.2.1"));        // chỉ số → cùng kho + khu dòng trên
        Assert.Equal("TG1.A.01.1.2", EntryRules.NormVt("tg1.a.01.1.2", null));          // đủ dạng → viết hoa đúng dạng
        Assert.Equal("a0112", EntryRules.NormVt("a0112", null));                       // không biết kho → giữ nguyên
        Assert.Equal("0513", EntryRules.NormVt("0513", null));
        Assert.Equal("a0192", EntryRules.NormVt("a0192", "TG1.A.01.1.1"));              // tầng 9 > tối đa → giữ nguyên (Kiểm tra báo vị trí lạ)
        Assert.Equal("kệ ngoài", EntryRules.NormVt("kệ ngoài", "TG1.A.01.1.1"));
    }

    [Fact]
    public void O_so_tinh_duoc()
    {
        Assert.Equal("15", EntryRules.Calc("10+5"));
        Assert.Equal("36", EntryRules.Calc("3x12"));
        Assert.Equal("36", EntryRules.Calc("3 × 12"));
        Assert.Equal("36", EntryRules.Calc("3*12"));
        Assert.Equal("1480", EntryRules.Calc("1,500-20"));
        Assert.Equal("12.5", EntryRules.Calc("25/2"));
        Assert.Equal("26", EntryRules.Calc("(10+3)*2"));
        Assert.Null(EntryRules.Calc("15"));
        Assert.Null(EntryRules.Calc("-5"));
        Assert.Null(EntryRules.Calc("1,500.5"));
        Assert.Null(EntryRules.Calc("abc"));
        Assert.Null(EntryRules.Calc("5/0"));
    }

    [Fact]
    public void Go_phep_tinh_o_bat_ky_o_nao_nhu_Excel()
    {
        // người dùng: "nhập 5+5+5 = 15 ở bất cứ ô nào (file trống và các cột mẫu không có điều kiện sẵn)"
        Assert.Equal("15", EntryRules.CalcCell("5+5+5", numberCol: false));
        Assert.Equal("12", EntryRules.CalcCell("4*3", numberCol: false));
        Assert.Equal("5", EntryRules.CalcCell("=10/2", numberCol: false));
        Assert.Equal("15", EntryRules.CalcCell("= 20 - 5", numberCol: false));
        Assert.Equal("15", EntryRules.CalcCell("=15", numberCol: false));
        Assert.Null(EntryRules.CalcCell("10/12", numberCol: false));       // cột chữ: ngày tháng → giữ nguyên
        Assert.Null(EntryRules.CalcCell("1-2", numberCol: false));         // khoảng số → giữ nguyên
        Assert.Null(EntryRules.CalcCell("3x12", numberCol: false));        // quy cách → giữ nguyên ở cột chữ
        Assert.Null(EntryRules.CalcCell("2+3 kiện", numberCol: false));    // có chữ → giữ nguyên
        Assert.Equal("36", EntryRules.CalcCell("3x12", numberCol: true));
        Assert.Equal("0.83", Math.Round(double.Parse(EntryRules.CalcCell("10/12", numberCol: true)!, System.Globalization.CultureInfo.InvariantCulture), 2).ToString(System.Globalization.CultureInfo.InvariantCulture));

        // trang trống (cột chữ tự đặt): gõ 5+5+5 ở cột bất kỳ → 15; cột Mã hàng / Vị trí / Lô / ngày không tính
        var cols = new List<GridColumn> { new("c0", "Cột A", 100), new("c1", "Mã hàng", 100), new("c2", "Ghi chú", 100), new("c3", "Lô", 100), new("c4", "Ngày", 100, "date") };
        var cells = new Dictionary<(int, int), string>();
        string Get(int r, int c) => cells.TryGetValue((r, c), out var v) ? v : "";
        string Edit(int c, string v)
        {
            var before = Get(0, c); cells[(0, c)] = v;
            foreach (var (rr, cc, vv) in EntryRules.Derive(cols, Get, new[] { (0, c, before, v) }, _ => null, new DateTime(2026, 11, 26))) cells[(rr, cc)] = vv;
            return Get(0, c);
        }
        Assert.Equal("15", Edit(0, "5+5+5"));
        Assert.Equal("7", Edit(2, "=3+4"));
        Assert.Equal("1+2", Edit(1, "1+2"));       // Mã hàng
        Assert.Equal("1+2", Edit(4, "1+2"));       // cột ngày
    }

    [Fact]
    public void Trang_kiem_ke_tu_doi_vi_tri_va_phep_tinh()
    {
        var cols = KiemKe();
        int vt = EntryRules.VtCol(cols), ma = 1, sl = cols.FindIndex(c => c.Header == "Số lượng");
        Assert.Equal(0, vt);
        var cells = new Dictionary<(int, int), string> { [(0, vt)] = "TG1.A.01.1.1", [(0, ma)] = "MA1", [(0, sl)] = "5" };
        string Get(int r, int c) => cells.TryGetValue((r, c), out var v) ? v : "";
        List<(int r, int c, string v)> Edit(int r, int c, string v)
        {
            var before = Get(r, c); cells[(r, c)] = v;
            var res = EntryRules.Derive(cols, Get, new[] { (r, c, before, v) }, _ => null, new DateTime(2026, 11, 26));
            foreach (var (rr, cc, vv) in res) cells[(rr, cc)] = vv;
            return res;
        }
        // gõ tắt ở dòng 2 → đủ dạng theo kho dòng trên
        Edit(1, vt, "a0112");
        Assert.Equal("TG1.A.01.1.2", Get(1, vt));
        // dòng 3 mới: gõ mã, bỏ trống vị trí → lấy vị trí dòng trên
        Edit(2, ma, "MA2");
        Assert.Equal("TG1.A.01.1.2", Get(2, vt));
        // ô số lượng gõ phép tính
        Edit(2, sl, "3x12");
        Assert.Equal("36", Get(2, sl));
        // sửa mã ở dòng đã có dữ liệu mà vị trí trống (người dùng xóa) → KHÔNG tự điền lại
        cells[(0, vt)] = "";
        Edit(0, ma, "MA9");
        Assert.Equal("", Get(0, vt));
    }

    [Fact]
    public void Model_nhap_lieu_ap_dung_qua_GridModel()
    {
        var cols = KiemKe(); var sl = cols.FindIndex(c => c.Header == "Số lượng");
        var m = new GridModel(); m.Load(cols, new[] { new[] { "TG1.A.01.1.1", "MA1" } });
        m.Derive = ch => EntryRules.Derive(m.Columns, m.Get, ch, _ => null, new DateTime(2026, 11, 26));
        m.SetCells(new[] { (1, 1, "MA2"), (1, sl, "10+5") });
        Assert.Equal("TG1.A.01.1.1", m.Get(1, 0));
        Assert.Equal("15", m.Get(1, sl));
    }
}
