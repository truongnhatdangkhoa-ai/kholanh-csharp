using ClosedXML.Excel;
using KhoLanh.Core.TonKhoGui;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

public class TonKhoGuiTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-gui-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    public TonKhoGuiTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1"); _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    // báo cáo NXT: tiêu đề nhãn chữ; nhóm số liệu "Số kiện / Trọng lượng" lặp lại (đầu kỳ, nhập, xuất, cuối kỳ) — lấy cặp cuối
    private string Xlsx(string name, params (string ma, string ten, string lo, string size, double sk, double tl)[] rows)
    {
        var path = Path.Combine(_dir, name);
        using var wb = new XLWorkbook(); var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = "BÁO CÁO TỒN KHO GỬI";
        var h = new[] { "Mã hàng", "Tên hàng", "Số lô", "Size", "Quy cách", "Ngày nhập kho", "Phiếu nhập", "Số kiện", "Trọng lượng", "Số kiện", "Trọng lượng" };
        for (var i = 0; i < h.Length; i++) ws.Cell(2, i + 1).Value = h[i];
        var r = 3;
        foreach (var x in rows)
        {
            ws.Cell(r, 1).Value = x.ma; ws.Cell(r, 2).Value = x.ten; ws.Cell(r, 3).Value = x.lo; ws.Cell(r, 4).Value = x.size; ws.Cell(r, 5).Value = "1.8Kg x 6";
            ws.Cell(r, 6).Value = new DateTime(2026, 11, 5); ws.Cell(r, 7).Value = "PN" + r;
            ws.Cell(r, 8).Value = 99; ws.Cell(r, 9).Value = 99; ws.Cell(r, 10).Value = x.sk; ws.Cell(r, 11).Value = x.tl; r++;
        }
        wb.SaveAs(path); return path;
    }

    private static readonly (string, string, string, string, double, double)[] Sample =
    {
        ("A1", "TÔM TCT BTP50, BLOCK, 1.8Kg x 6, 0083", "L1", "BTP50", 10, 108),
        ("A2", "TÔM TCT BTP35, IQF, 9Kg x 1, CKQ", "04.04.26 L12 NÂU", "BTP35", 5, 45),
        ("A3", "TÔM ECU 41/50, BLOCK, 1.8Kg x 7, ASCN", "ECUADOR-01", "ECU41/50", 8, 100.8),
        ("A4", "TÔM TGS 45, BLOCK, 1.8Kg x 6, 0083", "TGS-9", "TGS 45", 3, 32.4),
        ("A5", "NL 105, BLOCK, 10kg x 1, 0083", "L5", "NL 105", 4, 40),
        ("A6", "TÔM GC 45, BLOCK, 1.8Kg x 6, ASCN", "TGC-2", "BTP45", 2, 21.6),
        ("A7", "TÔM AD 50, BLOCK, 1.8Kg x 6, ASCN", "AD-7", "AĐ 50", 6, 64.8),
    };

    [Fact]
    public void Extract_reads_last_so_kien_pair_and_date()
    {
        var pv = _s.Gui.Preview(Xlsx("a.xlsx", Sample));
        Assert.NotNull(pv.Extracted);
        var rows = pv.Extracted!.Rows;
        Assert.Equal(7, rows.Count);
        Assert.Equal((10.0, 108.0), (rows[0].SoKien, rows[0].TrongLuong));           // cặp CUỐI (cột 10-11), không phải cột 8-9
        Assert.Equal("05/11/2026", rows[0].NgayNhapKho);
        Assert.Equal("PN3", rows[0].PhieuNhap);
        Assert.False(pv.Blocking);
    }

    [Fact]
    public void Classification_into_six_groups_has_no_overlap()
    {
        _s.Gui.Commit(_s.Gui.Preview(Xlsx("b.xlsx", Sample)));
        var d = _s.Gui.D;
        string Ma(string id) => string.Join(",", d.GroupRows(id).Select(r => r.MaHang));
        Assert.Equal("A5", Ma("nl")); Assert.Equal("A4", Ma("4")); Assert.Equal("A6", Ma("3")); Assert.Equal("A3", Ma("2")); Assert.Equal("A7", Ma("ad"));
        Assert.Equal("A1,A2", Ma("5"));
        var ds = d.ComputeDoiSoat();
        Assert.Equal(0, ds.DupCount);                                                 // mỗi dòng đúng 1 nhóm
        Assert.Equal(ds.LoadTotal, ds.ModuleTotal, 6);
    }

    [Fact]
    public void Dac_tinh_derivation_manual_table_ckq_and_pivot()
    {
        _s.Gui.Commit(_s.Gui.Preview(Xlsx("c.xlsx", Sample)));
        var d = _s.Gui.D;
        Assert.Equal("0083", d.DeriveDacTinh("TÔM TCT BTP50, BLOCK, 1.8Kg x 6, 0083", "L1"));
        _s.Gui.SaveManual(new[] { ("Hàng EU", "83.EU-H-M") });
        Assert.Equal("83.EU-H-M", d.DeriveDacTinh("TÔM TCT BTP50, BLOCK, 1.8Kg x 6, 0083", "L1"));   // bỏ số 0 đầu
        Assert.Equal("04.04.26 L12 NÂU", d.DeriveDacTinh("TÔM TCT BTP35, IQF, 9Kg x 1, CKQ", "04.04.26 L12 NÂU"));
        _s.Gui.MapLot("04.04.26 L12 NÂU", "23.MKS");                                              // CKQ chuyển tay
        Assert.Equal("23.MKS", d.DeriveDacTinh("TÔM TCT BTP35, IQF, 9Kg x 1, CKQ", "04.04.26 L12 NÂU"));
        _s.Gui.MergeGroup(new[] { "83.EU-H-M", "23.MKS" }, "EU");
        var p = d.ComputePivot();
        var hl = p.Cats.First(c => c.Label == "TÔM HLSO");
        Assert.Equal(108 + 45, hl.B.Tong, 6);
        Assert.Contains(hl.Sizes, s => s.Size == "BTP 35"); Assert.Contains(hl.Sizes, s => s.Size == "BTP 50");
        Assert.Equal(new[] { "BTP 35", "BTP 50" }, hl.Sizes.Select(s => s.Size).ToArray());      // sắp tăng dần theo số
        // đặc tính chưa gộp (ASCN, 0083 cũ…) không cộng vào Tổng
        Assert.Equal(108 + 45 + 40 + 32.4, p.Grand.Tong, 6);   // A1 + A2 (23.MKS) + A5 + A4 đều đặc tính 83.EU-H-M / đã gộp; ASCN chưa gộp thì không tính
    }

    [Fact]
    public void Slip_create_update_delete_restores_stock_and_locks_after_new_load()
    {
        var g = _s.Gui; g.Commit(g.Preview(Xlsx("d.xlsx", Sample)));
        var iA1 = g.D.Rows.FindIndex(r => r.MaHang == "A1");
        var slip = g.CreateSlip(new[] { (iA1, 4.0) });
        Assert.Equal("PX0001", slip.MaPhieu);
        Assert.Equal(6, g.D.Rows[iA1].SoKien); Assert.Equal(108 * 6 / 10.0, g.D.Rows[iA1].TrongLuong, 6);
        Assert.True(g.D.Rows[iA1].PxDaXuat);
        g.UpdateSlip(slip.Id, "PX-SUA", new[] { 7.0 });
        Assert.Equal(3, g.D.Rows[iA1].SoKien);
        Assert.Throws<KhoLanh.Core.Services.BusinessException>(() => g.UpdateSlip(slip.Id, "PX-SUA", new[] { 11.0 }));   // vượt tồn (10)
        g.Load();                                                                                                      // đọc lại từ database
        Assert.Equal(3, g.D.Rows.First(r => r.MaHang == "A1").SoKien);
        Assert.Single(g.Slips); Assert.Equal("PX-SUA", g.Slips[0].MaPhieu);
        g.DeleteSlip(slip.Id);
        Assert.Equal(10, g.D.Rows.First(r => r.MaHang == "A1").SoKien); Assert.Equal(108, g.D.Rows.First(r => r.MaHang == "A1").TrongLuong, 6);
        // phiếu của đợt cũ bị khóa khi nạp số liệu mới
        var s2 = g.CreateSlip(new[] { (iA1, 2.0) });
        System.Threading.Thread.Sleep(5);
        g.Commit(g.Preview(Xlsx("d2.xlsx", Sample)));
        Assert.False(g.IsActive(g.Slips[0]));
        Assert.Throws<KhoLanh.Core.Services.BusinessException>(() => g.UpdateSlip(s2.Id, "X", new[] { 1.0 }));
        g.DeleteSlip(s2.Id);                                                                                          // xóa phiếu khóa KHÔNG hoàn tồn
        Assert.Equal(10, g.D.Rows.First(r => r.MaHang == "A1").SoKien);
    }

    [Fact]
    public void Qty_problems_and_custom_filters_and_funnel()
    {
        Assert.Contains("TRỐNG", GuiService.QtyProblems(new[] { ("x", "", (double?)5) }));
        Assert.Contains("VƯỢT", GuiService.QtyProblems(new[] { ("x", "9", (double?)5) }));
        Assert.Equal("", GuiService.QtyProblems(new[] { ("x", "5", (double?)5) }));
        var g = _s.Gui; g.Commit(g.Preview(Xlsx("e.xlsx", Sample)));
        Assert.Equal(2, g.D.FunnelOf("5").Final);
        g.AddCustomFilter("5", new CustomFilter { ColLabel = "Size", Op = "eq", Value = "btp50" });
        Assert.Equal(1, g.D.FunnelOf("5").Final);
        Assert.Single(g.D.GroupRows("5"));
        g.Load();                                                                  // điều kiện lưu bền
        Assert.Single(g.D.CustomFor("5"));
    }

    [Fact]
    public void Exports_open_as_xlsx()
    {
        var g = _s.Gui; g.Commit(g.Preview(Xlsx("f.xlsx", Sample)));
        g.SaveManual(new[] { ("x", "83.EU") }); g.MergeGroup(new[] { "0083" }, "EU");
        var slip = g.CreateSlip(new[] { (0, 2.0) });
        var p1 = Path.Combine(_dir, "o1.xlsx"); GuiExport.Rows(g.D, g.D.Rows, "Du lieu").Save(p1);
        var p2 = Path.Combine(_dir, "o2.xlsx"); GuiExport.BangTongHop(g.D.ComputePivot(), "23/11/2026").Save(p2);
        var p3 = Path.Combine(_dir, "o3.xlsx"); File.WriteAllBytes(p3, GuiExport.SlipWorkbook(slip));
        foreach (var p in new[] { p1, p2, p3 }) { using var wb = new XLWorkbook(p); Assert.NotEmpty(wb.Worksheets); }
        using var w3 = new XLWorkbook(p3); Assert.Equal("TỔNG", w3.Worksheet(1).Cell(9, 3).GetString());
    }
    [Fact]
    public void Tong_kg_xuat_theo_kien_hoac_theo_kg_va_logo_Long_An()
    {
        Assert.Equal(60.0, GuiService.KgXuat(new GuiRow { SoKien = 10, TrongLuong = 120 }, 5), 6);
        Assert.Equal(37.5, GuiService.KgXuat(new GuiRow { SoKien = 0, TrongLuong = 100 }, 37.5), 6);
        Assert.True(KhoLanh.Core.Grid.GridTotals.DefaultSum("Tổng kg"));
        Assert.True(KhoLanh.Core.Grid.GridTotals.DefaultSum("Tổng kg xuất"));
        var g = _s.Gui; g.Commit(g.Preview(Xlsx("lg.xlsx", Sample)));
        var slip = g.CreateSlip(new[] { (0, 2.0) });
        var p = Path.Combine(_dir, "logo.xlsx"); File.WriteAllBytes(p, GuiExport.SlipWorkbook(slip));
        using var wb = new XLWorkbook(p); var pic = Assert.Single(wb.Worksheet(1).Pictures);
        Assert.Equal(375.0 / 119, (double)pic.Width / pic.Height, 1);
    }
    [Fact]
    public void Xuat_file_HLSO_tu_chon_chi_gom_nhom_da_chon()
    {
        var g = _s.Gui; g.Commit(g.Preview(Xlsx("tc.xlsx", Sample)));
        g.SaveManual(new[] { ("x", "83.EU") }); g.MergeGroup(new[] { "83.EU" }, "EU");
        var only = g.D.ComputePivot(new HashSet<string> { "5" });
        Assert.Equal(new[] { "TÔM HLSO" }, only.Cats.Select(c => c.Label).ToArray());
        Assert.Equal(only.Cats[0].B.Tong, only.Grand.Tong, 6);
        var p = Path.Combine(_dir, "tc-out.xlsx"); GuiExport.TuChon(g.D, "23/11/2026", true, new[] { "5" })!.Save(p);
        using (var wb = new XLWorkbook(p)) { Assert.Equal(2, wb.Worksheets.Count); Assert.Equal("Tom HLSO", wb.Worksheet(2).Name); }
        var p2 = Path.Combine(_dir, "tc-out2.xlsx"); GuiExport.TuChon(g.D, "", false, new[] { "5", "nl" })!.Save(p2);
        using (var wb = new XLWorkbook(p2)) Assert.Equal(new[] { "Tom Nguyen Lieu", "Tom HLSO" }, wb.Worksheets.Select(w => w.Name).ToArray());
        Assert.Null(GuiExport.TuChon(g.D, "", true, Array.Empty<string>()));
    }

    [Fact]
    public void Custom_summary_filters_sizes_and_columns()
    {
        var g = _s.Gui; g.Commit(g.Preview(Xlsx("cs.xlsx", Sample)));
        g.SaveManual(new[] { ("x", "83.EU") }); g.MergeGroup(new[] { "83.EU" }, "EU"); g.MergeGroup(new[] { "ASCN" }, "ASC");
        var d = CustomSummary.FromPivot(g.D.ComputePivot());
        var all = CustomSummary.Compute(d, d.Rows.Select(r => r.Key).ToHashSet(), d.Cols.ToHashSet(), true);
        Assert.Equal(g.D.ComputePivot().Grand.Tong, all.Grand, 6);                 // đủ size + đủ cột = tổng bảng gốc
        Assert.Equal(new[] { "ASC", "EU" }, all.ColIdx.Select(i => d.Cols[i]).OrderBy(x => x).ToArray());   // ẩn cột trống
        var onlyEu = CustomSummary.Compute(d, d.Rows.Select(r => r.Key).ToHashSet(), new HashSet<string> { "EU" }, true);
        Assert.Equal(108 + 32.4 + 40, onlyEu.Grand, 6);                           // A1 + A4 + A5 đặc tính 83.EU
        var hl = d.Rows.Where(r => r.Key.Contains("TÔM HLSO")).Select(r => r.Key).ToHashSet();
        var r2 = CustomSummary.Compute(d, hl, d.Cols.ToHashSet(), true);
        Assert.Equal(108, r2.Grand, 6);                                            // dòng tiêu đề nhóm không cộng
        var st = new CustomSummary.State { Sizes = hl.ToList(), HideEmpty = false }; st.Views["HLSO"] = (hl.ToList(), null);
        var back = CustomSummary.State.From(st.ToJson().ToJsonString());
        Assert.False(back.HideEmpty); Assert.Equal(hl.Count, back.Sizes!.Count); Assert.True(back.Views.ContainsKey("HLSO"));
        var p = Path.Combine(_dir, "cs-out.xlsx"); CustomSummary.Export(d, all).Save(p);
        using var wb = new XLWorkbook(p); Assert.Equal("TỔNG CHỌN", wb.Worksheet(1).Cell(all.Shown.Count + 2, 1).GetString());
    }
}
