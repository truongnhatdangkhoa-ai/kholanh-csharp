using System.IO.Compression;
using System.Text.Json.Nodes;
using KhoLanh.Core.PhieuKho;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.PhieuKho.PhieuKhoCore;

namespace KhoLanh.Core.Tests;

public class PhieuKhoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-pk-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;

    public PhieuKhoTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static Phieu Sample()
    {
        var p = NewPhieu();
        p.Ngay = "2026-11-20"; p.SoPhieu = "05.11 XKAA 03"; p.Lsx = "26.09.057"; p.Khach = "WISMETTAC"; p.HopDong = "HD-01";
        p.Xe.TaiXe = "Nguyễn Văn A"; p.Xe.Sdt = "0901234567"; p.Xe.SoXe = "51C-12345"; p.Xe.Cont = "MSKU1234567"; p.Xe.Seal = "SL001";
        p.Mh = new()
        {
            new MatHang
            {
                Ten = "Tôm TCT cooked PDTO", Ma = "VRHLBTNXECR003140IME1", Lo = "L1", Lsx = "26.09.057",
                DongList = new() { new Dong { Date = "10.11.2026", Size = "31/40", Packing = "1.0 kg x 5 x 2", Kien = "100", Mks = "40" }, new Dong { Date = "11.11.2026", Size = "41/50", Packing = "1.0 kg x 5 x 2", Kien = "60" } },
            },
            new MatHang { Ten = "Tôm sú", Ma = "PBTO0001", DongList = new() { new Dong { Date = "10.11.2026", Size = "U10", Packing = "24 oz x 12", Kien = "5", NetTay = "6.8" } } },
            new MatHang(),
        };
        return p;
    }

    [Fact]
    public void Net_total_and_words()
    {
        Assert.Equal(10, NetOf("1.0 kg x 5 x 2"));
        Assert.Equal(3.744, NetOf("0.312kg x 12"));
        Assert.Equal(10, NetOf("10kg"));
        Assert.Equal(8.16, Math.Round(NetOf("24 oz x 12")!.Value, 2));
        Assert.Null(NetOf("10"));
        Assert.Null(NetOf("abc"));
        Assert.Equal(1.2, NetOf("0.1 kg x 12 gói"));
        var p = Sample();
        var t = TotalsOf(p);
        Assert.Equal(165, t.Kien);
        Assert.Equal(160 * 10 + 5 * 6.8, t.Kg);                     // NET sửa tay (6.8) thắng NET tính từ quy cách
        Assert.Equal(160, t.Aa); Assert.Equal(5, t.Tp);             // mã bắt đầu bằng P → Thuận Phước
        Assert.Equal("Một trăm sáu mươi lăm", DocSo(165));
        Assert.Equal("Một nghìn chín trăm sáu mươi", DocSo(1960));
        Assert.Equal("Hai nghìn không trăm lẻ năm", DocSo(2005));
        Assert.Equal("Không", DocSo(0));
        Assert.Equal("(40MKS )", MksTxt("40")); Assert.Equal("( MKS )", MksTxt("x")); Assert.Equal("", MksTxt(""));
    }

    [Fact]
    public void Validate_reports_missing_fields()
    {
        Assert.Empty(Validate(Sample()));
        var p = Sample(); p.SoPhieu = ""; p.Mh[0].DongList[0].Date = "10/11"; p.Mh[0].DongList[1].Kien = "0"; p.Mh[1].DongList[0].Packing = "abc"; p.Mh[1].DongList[0].NetTay = "";
        var e = Validate(p);
        Assert.Contains("Chưa nhập Số phiếu.", e);
        Assert.Contains(e, x => x.Contains("DATE phải dạng"));
        Assert.Contains(e, x => x.Contains("Số kiện phải là số lớn hơn 0"));
        Assert.Contains(e, x => x.Contains("không tính được NET"));
        Assert.Equal("29.05.2026", NormDate("29/5/2026")); Assert.Equal("29.05.2026", NormDate("2026-05-29"));
    }

    [Fact]
    public void Numbering_by_month_and_side()
    {
        var a = Sample(); a.Id = "1"; a.SoPhieu = "03.06 XKAA 03"; a.Ngay = "2026-06-10"; a.O40 = "XK130";
        var b = Sample(); b.Id = "2"; b.SoPhieu = "04.06 XKTP 01"; b.Ngay = "2026-06-12";
        var list = new[] { a, b };
        var p = NewPhieu(); p.Ngay = "2026-06-20"; p.Mh = Sample().Mh;
        Assert.Equal("05.06 XKAA 04", NextNumbers(list, p).SoPhieu);
        Assert.Equal("XK131", NextNumbers(list, p).O40);
        p.Ngay = "2026-07-02";
        Assert.Equal("01.07 XKAA 01", NextNumbers(list, p).SoPhieu);
        Assert.True(RefreshNumbers(list, p));
        Assert.Equal("01.07 XKAA 01", p.SoPhieu);
        p.Id = "x"; p.Ngay = "2026-06-20";
        Assert.False(RefreshNumbers(list, p));                      // phiếu đã lưu có số → giữ
        Assert.Equal("AA160 05.06XKAA LSX 26.09.057 (PHIẾU XUẤT KHO).xlsm".Replace("AA160", "AA04"), FileName(new Phieu { SoPhieu = "05.06 XKAA 04", Lsx = "26.09.057" }));
    }

    [Fact]
    public void Parses_vehicle_email()
    {
        var r = ParseXeEmail("Dear anh chị,\nHọ tên tài xế: Trần B\nSố điện thoại: 0912 345 678\nSố xe: 60C-99999\nSố cont: TEMU 7654321\nSố seal: AB123\nSố booking: BK99\nNhiệt độ cài đặt cont: -18°C\nThanks");
        Assert.Equal(7, r.Found.Count); Assert.Empty(r.Missing);
        Assert.Equal("Trần B", r.Xe["taiXe"]); Assert.Equal("60C-99999", r.Xe["soXe"]); Assert.Equal("-18℃", r.Xe["nhietDo"]);
        var r2 = ParseXeEmail("Số xe: 51C-1\nSố cont: C1");
        Assert.Equal(2, r2.Found.Count); Assert.Contains("Số seal", r2.Missing);
        var flat = ParseXeEmail("Họ tên tài xế: Lê C Số điện thoại: 090 Số xe: 51D-2 Số cont: C2 Số seal: S2 Số booking: B2 Nhiệt độ cài đặt cont: -18℃ Cảm ơn");
        Assert.Equal("Lê C", flat.Xe["taiXe"]); Assert.Equal("-18℃", flat.Xe["nhietDo"]);
        var p = Sample(); ApplyXeEmail(p, "Số seal: ZZ9"); Assert.Equal("ZZ9", p.Xe.Seal); Assert.Equal("MSKU1234567", p.Xe.Cont);
    }

    [Fact]
    public void Email_text_lists_used_blocks_only()
    {
        var t = EmailText(Sample());
        Assert.StartsWith("Dear Anh Chị,", t);
        Assert.Contains("BP Kho An An gửi List xuất hàng ngày 20.11 Xuất hàng thành phẩm LSX 26.09.057 WISMETTAC", t);
        Assert.Contains("Người nhận hàng : Nguyễn Văn A ( 0901234567 )", t);
        Assert.Contains("Cont/Xe : MSKU1234567 / 51C-12345", t);
        Assert.Contains("1/ TÔM TCT COOKED PDTO - An An sản xuất ( Mã hàng: VRHLBTNXECR003140IME1 )", t);
        Assert.Contains("- DATE: 10.11.2026 - 31/40: 100 kiện(40MKS )", t.Replace("100 kiện ", "100 kiện"));
        Assert.Contains("2/ TÔM SÚ - Thuận Phước sản xuất", t);
        Assert.DoesNotContain("3/", t);
        Assert.Contains("Tổng xuất: 165 kiện", t);
    }

    [Fact]
    public void Json_roundtrip_keeps_unknown_fields_and_old_shapes()
    {
        var old = """
          {"id":"pxabc","loai":"xuat_khau","ngay":"2026-11-02","soPhieu":"01.11 XKAA 01","futureField":{"a":1},"xe":{"taiXe":"A","cont":"C1","newXe":"y"},
          "mh":[{"ten":"Tôm","ma":"V1","hint":{"codeMaHoa":"X"},"dong":[{"date":"02.11.2026","size":"16/20","packing":"1 kg x 10","kien":50,"netTon":8.16,"netPk":"1 kg x 10","extra":1}]}],"gc":{"sole":"co"}}
          """;
        var p = FromJson(old)!;
        Assert.Equal("50", p.Mh[0].DongList[0].Kien);
        Assert.Equal(8.16, p.Mh[0].DongList[0].NetTon);
        Assert.Equal(8.16, RowNet(p.Mh[0].DongList[0]));            // NET từ tồn kho (netPk khớp packing)
        p.Mh[0].DongList[0].Packing = "2 kg x 5";
        Assert.Equal(10, RowNet(p.Mh[0].DongList[0]));              // sửa packing → quay về công thức
        var json = ToJson(p);
        Assert.Contains("\"futureField\"", json); Assert.Contains("\"newXe\":\"y\"", json); Assert.Contains("\"extra\":1", json); Assert.Contains("\"dong\":[", json);
        Assert.Equal("co", FromJson(json)!.Gc.Sole);
    }

    [Fact]
    public void Save_load_delete_and_export_fills_template()
    {
        var sv = _s.PhieuKho;
        sv.Load();
        var p = Sample(); p.SoPhieu = "";
        var n = sv.NewPhieu(); Assert.Equal("01." + DateTime.Today.ToString("MM") + " XKAA 01", n.SoPhieu);
        p = sv.Save(p);
        Assert.StartsWith("px", p.Id);
        var again = new PhieuKhoService(() => _s.Lan, _s.Session, _s.Audit); again.Load();
        Assert.Single(again.List);
        Assert.Equal("Tôm sú", again.List[0].Mh[1].Ten);
        var sheet = _s.Modules.ReadSheet(PhieuKhoService.Db, PhieuKhoService.SheetPhieu);
        Assert.StartsWith("__JSON__", sheet[0]!["data"]!.GetValue<string>());
        Assert.Equal("26.09.057", sheet[0]!["lsx"]!.GetValue<string>());

        p.SoPhieu = "05.11 XKAA 03";
        var path = Path.Combine(_dir, FileName(p));
        sv.Export(p, path);
        Assert.EndsWith("(PHIẾU XUẤT KHO).xlsm", path);
        using var z = ZipFile.OpenRead(path);
        Assert.NotNull(z.GetEntry("xl/vbaProject.bin"));            // macro được giữ
        var sheetXml = new StreamReader(z.GetEntry("xl/worksheets/sheet1.xml")!.Open()).ReadToEnd();
        Assert.Contains("Số : 05.11 XKAA 03", sheetXml);
        Assert.Contains("Ngày 20 tháng 11 năm 2026", sheetXml);
        Assert.Contains("TÔM TCT COOKED PDTO", sheetXml);
        Assert.Contains("DATE: 10.11.2026", sheetXml);
        Assert.Contains("Họ tên tài xế: Nguyễn Văn A", sheetXml);
        Assert.Contains("Bằng chữ: Một trăm sáu mươi lăm kiện", sheetXml);
        Assert.Contains("Mã số lô hàng:  L1", sheetXml);
        Assert.Matches(@"<row\b[^>]*\sr=""20""[^>]*\shidden=""1""", sheetXml);                  // dòng DATE không dùng của mặt hàng 1 bị ẩn
        Assert.DoesNotMatch(@"<row\b[^>]*\sr=""41""[^>]*\shidden=""1""", sheetXml);               // dòng đầu mặt hàng 2 đang dùng
        Assert.Contains("fullCalcOnLoad", new StreamReader(z.GetEntry("xl/workbook.xml")!.Open()).ReadToEnd());

        // mở lại bằng thư viện Excel độc lập (ClosedXML) để chắc file hợp lệ và các ô đúng giá trị
        var copyPath = Path.Combine(_dir, "doc-lai.xlsx");
        File.Copy(path, copyPath);
        using (var wb = new ClosedXML.Excel.XLWorkbook(copyPath))
        {
            var ws = wb.Worksheet("01");
            Assert.Equal("Số : 05.11 XKAA 03", ws.Cell("G8").GetString());
            Assert.Equal("TÔM TCT COOKED PDTO", ws.Cell("B16").GetString());
            Assert.Equal("DATE: 10.11.2026", ws.Cell("B17").GetString());
            Assert.Equal("31/40", ws.Cell("C17").GetString());
            Assert.Equal(100, ws.Cell("G17").GetDouble());
            Assert.Equal(10, ws.Cell("K17").GetDouble());
            Assert.Equal(40, ws.Cell("L17").GetDouble());           // MKS
            Assert.Equal("x", ws.Cell("H40").GetString());          // mặt hàng 2 là Thuận Phước (mã bắt đầu bằng P)
            Assert.True(ws.Row(20).IsHidden);
            Assert.False(ws.Row(41).IsHidden);
        }

        sv.Delete(p.Id);
        Assert.Empty(sv.List);
        var bad = Sample(); bad.SoPhieu = "";
        Assert.Throws<BusinessException>(() => sv.Export(bad, Path.Combine(_dir, "x.xlsm")));
    }
}
