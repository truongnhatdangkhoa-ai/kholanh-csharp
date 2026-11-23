using KhoLanh.Core.PhieuKho;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.PhieuKho.ListSx;

namespace KhoLanh.Core.Tests;

public class ListSxTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-lsx-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    public ListSxTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    /// <summary>Bố cục bảng ngày × size (CGC): mỗi dòng = 1 ngày sản xuất, mỗi cột = 1 size, cột TỔNG + dấu MKS.</summary>
    private static List<Item> Matrix() => new()
    {
        new("LIST SẢN XUẤT TẨM BỘT CỦA CGC", 50, 800), new("LSX 26.09.057", 50, 785), new("Contract No: 123/2026-LSX", 50, 770), new("AN AN SẢN XUẤT", 50, 755),
        new("1KG NET/TÚI x 5 TÚI/KIỆN x 2/BDL", 50, 740), new("SỐ LƯỢNG (KIỆN)", 50, 725),
        new("51/60", 200, 700), new("41/50", 260, 700), new("31/40", 320, 700), new("TỔNG", 400, 700),
        new("2026.09.10", 50, 680), new("10", 200, 680), new("20", 260, 680), new("30", 320, 680), new("60", 400, 680),
        new("2026.09.11", 50, 660), new("5", 200, 660), new("15", 320, 660), new("20", 400, 660), new("MKS", 450, 660),
        new("TỔNG", 50, 640), new("15", 200, 640), new("20", 260, 640), new("45", 320, 640),
    };

    [Fact]
    public void Matrix_layout_reads_dates_sizes_mks_and_cross_checks()
    {
        var r = Parse(Matrix());
        Assert.True(r.Ok); Assert.Equal("", r.Layout);
        Assert.Equal(new[] { "51/60", "41/50", "31/40" }, r.Sizes.Select(z => z.Size));
        Assert.Equal("26.09.057", r.Lsx); Assert.StartsWith("123/2026", r.HopDong); Assert.Equal("AN AN", r.NhaSanXuat);
        Assert.Equal("TẨM BỘT", r.TieuDe); Assert.Equal("CGC", r.Khach); Assert.Equal("bdl", r.DonVi);
        Assert.Equal("1.0 kg x 5 x 2", r.Packing);
        Assert.Equal(2, r.Rows.Count);
        Assert.Equal("10.09.2026", r.Rows[0].Date);
        Assert.Equal(new Dictionary<string, double> { ["51/60"] = 10, ["41/50"] = 20, ["31/40"] = 30 }, r.Rows[0].Qty);
        Assert.Equal(60, r.Rows[0].Total); Assert.False(r.Rows[0].Mks);
        Assert.Equal(new Dictionary<string, double> { ["51/60"] = 5, ["31/40"] = 15 }, r.Rows[1].Qty);   // dòng chỉ có 2 size vẫn đúng cột theo toạ độ X
        Assert.True(r.Rows[1].Mks);
        Assert.Equal(15, r.Tong["51/60"]); Assert.Equal(45, r.Tong["31/40"]);
        Assert.Empty(r.Warnings);                                                                         // dòng TỔNG dưới bảng khớp
    }

    [Fact]
    public void Matrix_warns_instead_of_guessing()
    {
        var items = Matrix(); items.RemoveAll(i => i.S == "45" && i.Y == 640); items.Add(new("46", 320, 640));
        items.RemoveAll(i => i.S == "60"); items.Add(new("61", 400, 680));
        var r = Parse(items);
        Assert.Contains(r.Warnings, w => w.Contains("cộng các size = 60") && w.Contains("TỔNG ghi 61"));
        Assert.Contains(r.Warnings, w => w.Contains("Size 31/40") && w.Contains("TỔNG ghi 46"));
        var none = Parse(new List<Item> { new("Hóa đơn", 10, 10) });
        Assert.False(none.Ok); Assert.Contains(none.Warnings, w => w.Contains("Không thấy dòng ngày sản xuất"));
    }

    /// <summary>Bố cục sản phẩm theo cột (ISHIMITSU): dòng SIZE, mỗi LÔ = 1 cặp hàng THƯỜNG / MKS, ngày ở cột trái, mã truy xuất.</summary>
    private static List<Item> Product() => new()
    {
        new("LIST SẢN XUẤT TÔM CỦA ISHIMITSU", 50, 800), new("LSX 26.09.061", 50, 785), new("THUẬN PHƯỚC SẢN XUẤT", 50, 770), new("KIỆN", 50, 755),
        new("2026.09.23", 50, 600), new("THƯỜNG", 100, 600), new("10", 200, 600), new("20", 260, 600), new("30", 400, 600), new("23090466014", 600, 598),
        new("MKS", 100, 585), new("3", 200, 585), new("0", 260, 585), new("3", 400, 585),
        new("SIZE", 100, 700), new("200G", 200, 700), new("300G", 260, 700),
        new("TỔNG", 100, 560), new("13", 200, 560), new("20", 260, 560),
        new("NET/KHAY", 100, 480), new("100G", 200, 480), new("150G", 260, 480),
        new("NET/THÙNG", 100, 470), new("100G x 20", 200, 470), new("150G x 10", 260, 470),
    };

    [Fact]
    public void Product_layout_sums_regular_plus_mks_and_reads_packing_table()
    {
        var r = Parse(Product());
        Assert.True(r.Ok, string.Join("; ", r.Warnings)); Assert.Equal("product", r.Layout);
        Assert.Equal("26.09.061", r.Lsx); Assert.Equal("THUẬN PHƯỚC", r.NhaSanXuat); Assert.Equal("ISHIMITSU", r.Khach);
        Assert.Single(r.Rows);
        var row = r.Rows[0];
        Assert.Equal("23.09.2026", row.Date); Assert.Equal("23090466014", row.TruyXuat);
        Assert.Equal(13, row.Qty["200G"]); Assert.Equal(20, row.Qty["300G"]);                              // THƯỜNG + MKS
        Assert.Equal(3, row.MksQty!["200G"]); Assert.Equal(0, row.MksQty["300G"]);
        Assert.True(row.Mks);
        Assert.Equal("100g x 20", r.PackingBy["200G"]); Assert.Equal("150g x 10", r.PackingBy["300G"]);
        Assert.Equal(2, r.NetPdf["200G"]); Assert.Equal(1.5, r.NetPdf["300G"]);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Apply_builds_blocks_groups_by_name_and_uses_mks_qty()
    {
        var r = Parse(Matrix());
        r.Sizes[0].Ten = "Tôm sú tẩm bột"; r.Sizes[0].Ma = "VRHLBTNXECR003140IME1"; r.Sizes[0].Lo = "L1";
        r.Sizes[1].Ten = "Tôm sú tẩm bột"; r.Sizes[1].Ma = "VRHLBTNXECR003140IME1";            // cùng tên + cùng bên (V…) → chung 1 mặt hàng
        r.Sizes[2].Ten = "Tôm sú PD"; r.Sizes[2].Ma = "PBTO0001";                               // mã P… → Thuận Phước, mặt hàng riêng
        var p = PhieuKhoCore.NewPhieu();
        var res = ListSxApply.Apply(p, r);
        Assert.True(res.Ok);
        Assert.Equal(2, res.SoMatHang); Assert.Equal(5, res.SoDong);
        Assert.Equal("26.09.057", p.Lsx); Assert.Equal("CGC", p.Khach); Assert.Equal("bdl", p.DonVi);
        var a = p.Mh[0];
        Assert.Equal("Tôm sú tẩm bột", a.Ten); Assert.Equal("VRHLBTNXECR003140IME1", a.Ma); Assert.Equal("L1", a.Lo);
        Assert.Equal(new[] { "51/60", "51/60", "41/50" }, a.DongList.Select(d => d.Size));       // size trước (đúng thứ tự trong List), rồi DATE tăng dần
        Assert.Equal(new[] { "10.09.2026", "11.09.2026", "10.09.2026" }, a.DongList.Select(d => d.Date));
        Assert.Equal("1.0 kg x 5 x 2", a.DongList[0].Packing);
        Assert.Equal("x", a.DongList[1].Mks); Assert.Equal("", a.DongList[0].Mks);   // MKS chung của dòng ngày 11 áp cho size có số
        Assert.Equal("Tôm sú PD", p.Mh[1].Ten); Assert.Equal(2, p.Mh[1].DongList.Count);
        Assert.Equal("TP", PhieuKhoCore.SideOf(p.Mh[1]));
        Assert.Empty(PhieuKhoCore.Validate(p).Where(e => !e.Contains("Số phiếu")));

        // thêm LSX thứ hai vào cùng phiếu: giữ mặt hàng LSX cũ, xếp theo thứ tự LSX gõ ở ô LSX
        var r2 = Parse(Product()); r2.Sizes[0].Ten = "Tôm X"; r2.Sizes[0].Ma = "VX1"; r2.Sizes[1].Ten = "Tôm X"; r2.Sizes[1].Ma = "VX1";
        var res2 = ListSxApply.Apply(p, r2, null, append: true);
        Assert.True(res2.Ok); Assert.Equal(3, p.Mh.Count);
        Assert.Equal("26.09.061", p.Mh[2].Lsx);
        var mk = p.Mh[2].DongList.First(d => d.Size == "200G");
        Assert.Equal("3", mk.Mks);                                                                // số kiện MKS riêng từng size
    }

    [Fact]
    public void Packing_from_code_uses_weight_and_pack_names()
    {
        string? L(int seg, string c) => (seg, c) switch { (9, "IM") => "0.312 kg/gói", (10, "E1") => "12túi/ctn", (9, "G1") => "24 oz/gói", (10, "E2") => "5túi/ctn, 2ctn/bdl", (9, "KG") => "9.0 kg/gói", _ => null };
        Assert.Equal("0.312 kg x 12 gói", ListSxApply.PackingFromMa("TPC-VRHLBTNXECR0031403IME1".Replace("VRHLBTNXECR0031403IME1", "VRHLBTNXECR003140IME1"), L).Packing);
        Assert.Equal("24 oz x 12 gói", ListSxApply.PackingFromMa("VRHLBTNXECR003140G1E1", L).Packing);
        Assert.Equal("9.0 kg x 5 gói x 2", ListSxApply.PackingFromMa("VRHLBTNXECR003140KGE2", L).Packing);
        var miss = ListSxApply.PackingFromMa("VRHLBTNXECR003140ZZE1", L);
        Assert.Equal("", miss.Packing); Assert.Contains("12túi", miss.Hint);              // chỉ gợi ý phần tra được, không đoán
        Assert.Equal("", ListSxApply.PackingFromMa("SHORT", L).Packing);
    }

    [Fact]
    public void List_store_roundtrip_and_reuse_by_lsx()
    {
        var sv = _s.PhieuKho; sv.Load();
        var r = Parse(Matrix()); r.Sizes[0].Ten = "Tôm A"; r.Sizes[0].Ma = "VA1";
        sv.SaveListData(r, "list.pdf");
        var again = new PhieuKhoService(() => _s.Lan, _s.Session, _s.Audit); again.Load();
        var rec = again.FindList(" 26.09.057 ");
        Assert.NotNull(rec); Assert.Equal("list.pdf", rec!.File);
        Assert.Equal("Tôm A", rec.Data.Sizes[0].Ten); Assert.Equal(2, rec.Data.Rows.Count);
        Assert.Equal(new[] { 10.0, 20, 30 }, rec.Data.Rows[0].Qty.Values.ToArray());
        var noLsx = Parse(Matrix()); noLsx.Lsx = "";
        Assert.Throws<BusinessException>(() => sv.SaveListData(noLsx, "x"));
        again.DeleteList("26.09.057"); Assert.Null(again.FindList("26.09.057"));
    }

    [Fact]
    public void Excel_list_is_read_through_the_same_parser()
    {
        var path = Path.Combine(_dir, "list.xlsx");
        using (var wb = new ClosedXML.Excel.XLWorkbook())
        {
            var ws = wb.AddWorksheet("List");
            ws.Cell(1, 1).Value = "LIST SẢN XUẤT TẨM BỘT CỦA CGC"; ws.Cell(2, 1).Value = "LSX 26.09.057"; ws.Cell(3, 1).Value = "1KG NET/TÚI x 5 TÚI/KIỆN x 2/BDL"; ws.Cell(3, 9).Value = "KIỆN";
            ws.Cell(5, 3).Value = "51/60"; ws.Cell(5, 4).Value = "41/50"; ws.Cell(5, 5).Value = "TỔNG";
            ws.Cell(6, 1).Value = new DateTime(2026, 9, 10); ws.Cell(6, 3).Value = 10; ws.Cell(6, 4).Value = 20; ws.Cell(6, 5).Value = 30;
            ws.Cell(7, 1).Value = "11/09/2026"; ws.Cell(7, 3).Value = 5; ws.Cell(7, 5).Value = 5;
            wb.SaveAs(path);
        }
        var r = ReadFile(path);
        Assert.True(r.Ok, string.Join("; ", r.Warnings));
        Assert.Equal(2, r.Rows.Count); Assert.Equal("10.09.2026", r.Rows[0].Date); Assert.Equal("11.09.2026", r.Rows[1].Date);
        Assert.Equal(20, r.Rows[0].Qty["41/50"]); Assert.False(r.Rows[1].Qty.ContainsKey("41/50"));
    }

    [Fact]
    public void Reads_a_real_pdf_through_text_runs()
    {
        var font = new[] { "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", @"C:\Windows\Fonts\arial.ttf" }.FirstOrDefault(File.Exists);
        if (font is null) { Console.WriteLine("SKIP: không có font"); return; }
        var b = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var f = b.AddTrueTypeFont(File.ReadAllBytes(font));
        var pg = b.AddPage(612, 842);
        foreach (var i in Matrix()) pg.AddText(i.S, 9, new UglyToad.PdfPig.Core.PdfPoint(i.X, i.Y), f);
        var path = Path.Combine(_dir, "list.pdf");
        File.WriteAllBytes(path, b.Build());
        var r = ReadFile(path);
        Assert.True(r.Ok, string.Join("; ", r.Warnings));
        Assert.Equal(2, r.Rows.Count);
        Assert.Equal(new[] { "51/60", "41/50", "31/40" }, r.Sizes.Select(z => z.Size));
        Assert.Equal(30, r.Rows[0].Qty["31/40"]);
        Assert.Equal("26.09.057", r.Lsx); Assert.Equal("CGC", r.Khach);
        Assert.Empty(r.Warnings);
    }
}
