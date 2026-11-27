using ClosedXML.Excel;
using KhoLanh.Core.DoiChieu;
using Xunit;
using static KhoLanh.Core.DoiChieu.XuatKiem;

namespace KhoLanh.Core.Tests;

/// <summary>Ca kiểm thử lấy từ dcx-xuat-kiem.test.js của app cũ: dữ liệu mô phỏng đúng cấu trúc file mẫu thật (tiêu đề "Size" lệch 1 cột, dòng tổng trống cuối file).</summary>
public class XuatKiemTests
{
    private static readonly string[] Hdr = { "Đối tác", "Ngày chứng từ", "Số lệnh", "Mã hàng", "Tên hàng", "TLg Net", "Số lượng tồn", "Net", "Size", "Qui cách đông", "Loại chứng từ", "Đặc tính", "Hợp đồng" };
    private const string Kiem = "Xuất kiểm hàng", M1 = "VNTOVCPEMGR00G125EAN1", M2 = "VTOTICEGIDR00G150CAO1", M3 = "VCPDICEEFXF205160HIH1";
    private static object D(int day, int m = 10) => new DateTime(2026, m, day).ToOADate();
    private static object?[] Row(object ngay, string ma, string ten, double tlg, double sl, string size, string loai, string dt, string hd) =>
        new object?[] { "TPC", ngay, "5DO-1", "TPC-" + ma, ten, tlg, sl, Math.Round(tlg * sl * 1e6) / 1e6, "R00", size, loai, dt, hd };
    private static List<object?[]> A(params object?[][] rows) => new List<object?[]> { new object?[] { "Báo cáo - Kho - 07-Nhập xuất chi tiết" }, Array.Empty<object?>(), Hdr.Cast<object?>().ToArray() }.Concat(rows).ToList();

    [Fact]
    public void Takes_only_xuat_kiem_merges_same_day_and_code()
    {
        var r = Build(A(
            Row(D(5), M2, "Tên A - MVSMKS - 26.9.028A-16/09", 3.6, 1, "15gr", Kiem, "MVSMKS", "26.9.028A-16/09"),
            Row(D(5), M2, "Tên A - MVS-ND - 26.9.028A-15/09", 3.6, 1, "15gr", Kiem, "MVS-ND", "26.9.028A-15/09"),
            Row(D(3), M1, "Tên B - MVS - 26.10.044-21/09", 5, 1, "12.5gr", Kiem, "MVS", "26.10.044-21/09"),
            Row(D(3), M3, "Tên C", 8, 1, "51/60", "Xuất xả", "MVS", "26.10.078-22/09"),
            new object?[] { null, null, null, null, null, null, 2.0, 9.0, null, null, null, null, null },    // dòng tổng cuối file
            Row(D(8), M3, "Tên D", 10, 1, "", Kiem, "ISHI", "ISHI-15/09/26")));
        Assert.True(r.Ok);
        Assert.Equal(new[] { 4, 3, 1, 0 }, new[] { r.NKiem, r.Groups.Count, r.NGop, r.BoQua });
        Assert.Equal(new[] { new Other("Xuất xả", 1) }, r.Khac);
        Assert.Equal(new[] { "2026-10-03 " + M1, "2026-10-05 " + M2, "2026-10-08 " + M3 }, r.Groups.Select(g => g.Ngay + " " + g.Ma));   // sắp theo ngày rồi mã
        var g = r.Groups[1];
        Assert.Equal(2, g.Sl); Assert.Equal(7.2, g.Net); Assert.Equal(3.6, g.Tlg); Assert.Equal("15gr", g.Size); Assert.Equal("26.9.028A", g.Hopdong);
        Assert.Equal("Tên A - MVSMKS - 26.9.028A-16/09", g.Ten); Assert.Equal(2, g.NDong);
        Assert.Equal("ISHI", r.Groups[2].Hopdong); Assert.Equal("", r.Groups[2].Size);
        Assert.Equal(4, r.TotSl); Assert.Equal(22.2, r.TotNet);
        Assert.DoesNotContain(r.Groups, x => x.NetLech);
    }

    [Fact]
    public void Size_comes_from_qui_cach_dong_not_the_shifted_size_column()
    {
        Assert.Equal("12.5gr", Build(A(Row(D(3), M1, "x", 5, 1, "12.5gr", Kiem, "MVS", "26.10.044-21/09"))).Groups[0].Size);
        var h2 = new object?[] { "Ngày chứng từ", "Mã hàng", "Tên hàng", "TLg Net", "Số lượng tồn", "Net", "Size", "Loại chứng từ", "Hợp đồng" };
        var r2 = Build(new List<object?[]> { h2, new object?[] { D(3), "TPC-" + M1, "x", 5.0, 1.0, 5.0, "12.5gr", Kiem, "A-1" } });
        Assert.Equal("12.5gr", r2.Groups[0].Size); Assert.Equal("A", r2.Groups[0].Hopdong);
    }

    [Fact]
    public void Net_mismatch_keeps_original_net_and_missing_columns_are_reported()
    {
        var rw = Row(D(3), M1, "x", 5, 2, "12.5gr", Kiem, "MVS", "A-1"); rw[7] = 11.0;
        var r = Build(A(rw));
        Assert.True(r.Groups[0].NetLech); Assert.Equal(11, r.Groups[0].Net); Assert.Single(r.Warn);
        var bad = Build(new List<object?[]> { new object?[] { "Ngày chứng từ", "Mã hàng", "Tên hàng" }, new object?[] { D(3), M1, "x" } });
        Assert.False(bad.Ok); Assert.Contains("Số lượng tồn", bad.Err); Assert.Contains("Loại chứng từ", bad.Err);
        Assert.False(Build(new List<object?[]> { new object?[] { "a", "b" }, new object?[] { 1.0, 2.0 } }).Ok);
    }

    [Fact]
    public void Name_plan_replaces_whole_name_by_code_and_lists_only_differences()
    {
        var g1 = new Group { Ngay = "2026-10-06", Ma = "A", Ten = "Tôm PD luộc (0.800 - MVSMKS - 26.10.078-22/09" };
        var g2 = new Group { Ngay = "2026-10-06", Ma = "B", Ten = "Tên B đủ" }; var g3 = new Group { Ngay = "2026-10-06", Ma = "C", Ten = "C" };
        var p = PlanTen(new[] { g1, g2, g3 }, ma => ma == "A" ? ("Tôm PD luộc đủ tên", "Tồn kho An An") : ma == "B" ? ("Tên B đủ", "Danh sách mã hàng") : null);
        Assert.Equal(new[] { 1, 1, 1 }, new[] { p.List.Count, p.Same, p.NoName });
        Assert.Equal("2026-10-06|A", p.List[0].Key); Assert.Equal("Tôm PD luộc đủ tên", p.List[0].Moi);   // bỏ đuôi " - đặc tính - hợp đồng-lô"
    }

    [Fact]
    public void Reads_a_real_workbook_and_exports_the_short_list()
    {
        var dir = Path.Combine(Path.GetTempPath(), "klcs-xk-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
        try
        {
            var src = Path.Combine(dir, "XK.T10.xlsx");
            using (var wb = new XLWorkbook())
            {
                var ws = wb.AddWorksheet("Sheet1"); ws.Cell(1, 1).Value = "Báo cáo - Kho";
                for (var c = 0; c < Hdr.Length; c++) ws.Cell(3, c + 1).Value = Hdr[c];
                var rows = new[] { Row(D(5), M2, "Tên A", 3.6, 1, "15gr", Kiem, "MVS", "26.9.028A-16/09"), Row(D(5), M2, "Tên A", 3.6, 1, "15gr", Kiem, "MVS", "26.9.028A-15/09"), Row(D(3), M1, "Tên B", 5, 2, "12.5gr", Kiem, "MVS", "26.10.044-21/09"), Row(D(3), M3, "Tên C", 8, 1, "51/60", "Xuất xả", "MVS", "x-1") };
                for (var r = 0; r < rows.Length; r++)
                    for (var c = 0; c < rows[r].Length; c++)
                    {
                        var v = rows[r][c]; var cell = ws.Cell(r + 4, c + 1);
                        if (c == 1) { cell.Value = DateTime.FromOADate((double)v!); cell.Style.DateFormat.Format = "dd/mm/yyyy"; }
                        else if (v is double d) cell.Value = d; else cell.Value = v?.ToString();
                    }
                wb.SaveAs(src);
            }
            var res = BuildFromBook(ReadBook(src));
            Assert.True(res.Ok, res.Err); Assert.Equal(2, res.Groups.Count); Assert.Equal(17.2, res.TotNet);
            var outp = Path.Combine(dir, "rut-gon.xlsx"); Export(res, outp);
            using var wb2 = new XLWorkbook(outp); var w = wb2.Worksheet(1);
            Assert.Equal(Head, Enumerable.Range(1, 8).Select(c => w.Cell(1, c).GetString()));
            Assert.Equal(M1, w.Cell(2, 2).GetString()); Assert.Equal(new DateTime(2026, 10, 3), w.Cell(2, 1).GetDateTime());
            Assert.Equal("D2*E2", w.Cell(2, 6).FormulaA1);
            Assert.Equal("Tổng", w.Cell(4, 1).GetString()); Assert.Equal("SUBTOTAL(9,E2:E3)", w.Cell(4, 5).FormulaA1);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void File_rut_gon_xuat_ra_nap_lai_duoc_va_ra_dung_so_lieu()
    {
        // 1.169: file rút gọn (8 cột, KHÔNG có "Loại chứng từ") nạp lại được — mọi dòng có mã + ngày là Xuất kiểm hàng
        var src = Build(A(
            Row(D(5), M2, "Tên A - MVSMKS - 26.9.028A-16/09", 3.6, 1, "15gr", Kiem, "MVSMKS", "26.9.028A-16/09"),
            Row(D(5), M2, "Tên A - MVS-ND - 26.9.028A-15/09", 3.6, 1, "15gr", Kiem, "MVS-ND", "26.9.028A-15/09"),
            Row(D(3), M1, "Tên B - MVS - 26.10.044-21/09", 5, 1, "12.5gr", Kiem, "MVS", "26.10.044-21/09"),
            Row(D(3), M3, "Tên C", 8, 1, "51/60", "Xuất xả", "MVS", "26.10.078-22/09")));
        var path = Path.Combine(Path.GetTempPath(), "xk169_" + Guid.NewGuid().ToString("N") + ".xlsx");
        Export(src, path);
        var back = BuildFromBook(ReadBook(path));
        Assert.True(back.Ok, back.Err);
        Assert.Equal(src.Groups.Count, back.Groups.Count);
        for (var i = 0; i < src.Groups.Count; i++)
        {
            var a = src.Groups[i]; var b = back.Groups[i];
            Assert.Equal((a.Ngay, a.Ma, a.Ten, a.Size, a.Hopdong), (b.Ngay, b.Ma, b.Ten, b.Size, b.Hopdong));
            Assert.Equal((a.Tlg, a.Sl, a.Net), (b.Tlg, b.Sl, b.Net));
        }
        Assert.Equal((src.TotSl, src.TotNet), (back.TotSl, back.TotNet));
        Assert.True(back.Rutgon); Assert.False(src.Rutgon);
        Assert.Equal(src.Groups.Count, back.NKiem);   // dòng "Tổng" cuối file (không mã, không ngày) được bỏ qua, không lẫn vào dữ liệu
        // xuất lại từ bản đọc về → cùng nội dung (vòng gốc → rút gọn → rút gọn)
        var path2 = Path.Combine(Path.GetTempPath(), "xk169b_" + Guid.NewGuid().ToString("N") + ".xlsx");
        Export(back, path2);
        Assert.Equal(src.Groups.Count, BuildFromBook(ReadBook(path2)).Groups.Count);
        // file thiếu cột khác vẫn báo lỗi rõ (không nhận nhầm là file rút gọn)
        Assert.False(Build(new List<object?[]> { new object?[] { "Ngày chứng từ", "Mã hàng", "Tên hàng", "Số lượng tồn" }, new object?[] { D(3), M1, "x", 1.0 } }).Ok);
    }
}
