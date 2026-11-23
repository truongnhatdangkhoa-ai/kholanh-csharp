using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.KhangSinh;
using KhoLanh.Core.PhieuKho;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.PhieuKho.CargoCore;

namespace KhoLanh.Core.Tests;

public class CargoTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-cg-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;
    public CargoTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static CargoCfg Cfg => NormCfg(DefaultCfg());

    /// <summary>Kết quả kháng sinh của module Kết quả kháng sinh: lô 16 ngày nguyên liệu 22/05/2026 → ký hiệu 03 (NHẬT, EU, HQ); lô 17 → 07 (NHẬT, ASC-B, HQ).</summary>
    private void SeedKs()
    {
        JsonObject Rec(string date, string batch, string ky, string dat) => new() { ["date"] = date, ["batch"] = batch, ["kyhieu"] = ky, ["dat"] = dat, ["savedAt"] = "2026-05-23T01:00:00.000Z", ["inputs"] = new JsonObject() };
        var rows = new JsonArray
        {
            new JsonObject { ["key"] = "s:batch:2026-05-22:16:aaa", ["value"] = Rec("2026-05-22", "16", "03.N-EU-H", "ĐẠT NHẬT, EU, HQ").ToJsonString() },
            new JsonObject { ["key"] = "s:batch:2026-05-22:17:bbb", ["value"] = Rec("2026-05-22", "17", "07.N-AS1-H", "ĐẠT NHẬT, ASC-B, HQ").ToJsonString() },
            new JsonObject { ["key"] = "s:batch:2026-05-23:5:ccc", ["value"] = Rec("2026-05-23", "05", "11.X", "KHÔNG ĐẠT").ToJsonString() },
        };
        _s.Modules.WriteSheets("ket-qua-khang-sinh", new Dictionary<string, JsonArray?> { ["KhangSinh"] = rows });
    }

    private static CargoRec Sample(string so = "05.10 XTP 46")
    {
        var c = new CargoRec { Ngay = "2026-10-29", SoPhieu = so, Kho = "An An + Long An", NguoiNhan = "Nguyễn Văn A ( 0901234567 )", ContXe = "51C-12345 / MSKU1234567", Seal = "SL001" };
        c.Blocks = new()
        {
            new CargoBlock { Loai = "ks", Ten = "HLSO", Ma = "", Lines = new()
            {
                new CargoLine { Loai = "BTP", Sn = "0040-6", Lo = "22.05.26 L16", Dai = new() { ["1"] = "50", ["2"] = "12" } },
                new CargoLine { Loai = "BTP", Sn = "0030-6", Lo = "22.05.26 L17", Dai = new() { ["1"] = "10", ["3"] = "20" } },
                new CargoLine { Loai = "BTP", Sn = "0040-6", Lo = "22.05.26 L17", Dai = new() { ["2"] = "8" } },
            } },
            new CargoBlock { Loai = "khac", Ten = "TÔM VỤN", Lines = new() { new CargoLine { Size = "VỤN", Pack = "10.0 kg x 1", Dai = new() { ["4"] = "5" } } } },
        };
        return c;
    }

    private static KsIndex Ix()
    {
        JsonObject J(string date, string b, string ky, string dat) => new() { ["date"] = date, ["batch"] = b, ["kyhieu"] = ky, ["dat"] = dat, ["savedAt"] = "x" };
        return KsIndex.From(new[]
        {
            new KhangSinhCore.Rec { Key = "s:batch:a", J = J("2026-05-22", "16", "03.N-EU-H", "ĐẠT NHẬT, EU, HQ") },
            new KhangSinhCore.Rec { Key = "s:batch:b", J = J("2026-05-22", "17", "07.N-AS1-H", "ĐẠT NHẬT, ASC-B, HQ") },
        });
    }

    [Fact]
    public void Lot_markets_and_size_codes()
    {
        var lo = ParseLo("22.05.26 L16"); Assert.True(lo.Ok); Assert.Equal("2026-05-22", lo.Date); Assert.Equal("16", lo.Batch); Assert.Equal("22.05.26 L16", lo.Text);
        Assert.Equal("16", ParseLo("L016 22/5/2026").Batch);
        var guess = ParseLo("22.05 L7", new DateTime(2026, 11, 1)); Assert.True(guess.Guessed); Assert.Equal("2026-05-22", guess.Date); Assert.Equal("2025-05-22", guess.Alt);
        Assert.Equal("2025-12-30", ParseLo("30.12 L7", new DateTime(2026, 6, 1)).Date);      // ngày còn ở tương lai trong năm nay → lấy năm trước
        Assert.False(ParseLo("22.05.26").Ok); Assert.False(ParseLo("L16").Ok); Assert.False(ParseLo("").Ok);
        Assert.Equal("NHẬT, EU, HQ", MarketFromDat("ĐẠT NHẬT, EU, HQ"));
        Assert.Equal("NHẬT, ASC-A,B, HQ, MỸ", MarketFromDat("ĐẠT NHẬT, MỸ, ASC-A,B, HQ"));   // không phân biệt thứ tự
        Assert.Equal("KHÔNG ĐẠT", MarketFromDat("KHÔNG ĐẠT")); Assert.Equal("", MarketFromDat("ĐẠT XYZ"));
        var ma = MaHang(Cfg, "BTP", "0040", "6", 3);
        Assert.Equal("VRHLBTNXXXR000040IME10003", ma); Assert.Equal(25, ma.Length);
        Assert.Equal("BTP40", SizeLabel(ma));
        Assert.Equal("31/40", SizeLabel("VRHLBTNXXXR003140IME10003"));
        Assert.Equal("91/120", SizeLabel("VRHLBTNXXXR000914IME10003"));
        Assert.Equal("NL130", SizeLabel("VRHOBTNXXXR000130IME10003"));
        Assert.Equal(10.8, KgPerCarton("1.8Kg x 6")); Assert.Equal(6, KgPerCarton("100G x 60")); Assert.Null(KgPerCarton("abc"));
        Assert.Equal("NL", LoaiTheoQuyCach(Cfg, "8")); Assert.Equal("BTP", LoaiTheoQuyCach(Cfg, "9")); Assert.Equal("", LoaiTheoQuyCach(Cfg, "6"));
        Assert.True(ParseSn("BTP40-6", "NL", Cfg) is { Ok: true, Loai: "BTP", Size4: "0040", Digit: "6" });
        Assert.False(ParseSn("abc", "BTP", Cfg).Ok);
    }

    [Fact]
    public void Resolve_line_looks_up_antibiotic_result()
    {
        var c = Sample(); var ix = Ix();
        var r = ResolveLine(Cfg, c.Blocks[0], c.Blocks[0].Lines[0], ix);
        Assert.Empty(r.Errors);
        Assert.Equal(62, r.Kien); Assert.Equal("NHẬT, EU, HQ", r.Market); Assert.Equal("VRHLBTNXXXR000040IME10003", r.Ma); Assert.Equal("BTP40", r.Size); Assert.Equal("1.8Kg x 6", r.Pack);
        Assert.Equal("03.N-EU-H", r.KsText);
        var bad = ResolveLine(Cfg, c.Blocks[0], new CargoLine { Loai = "BTP", Sn = "0040-6", Lo = "01.01.26 L1", Dai = new() { ["1"] = "5" } }, ix);
        Assert.True(bad.ChuaCoKQ); Assert.Contains(bad.Errors, e => e.Contains("Chưa có kết quả kháng sinh"));
        var wrongLoai = ResolveLine(Cfg, c.Blocks[0], new CargoLine { Loai = "BTP", Sn = "0040-8", Lo = "22.05.26 L16", Dai = new() { ["1"] = "5" } }, ix);
        Assert.Contains(wrongLoai.Errors, e => e.Contains("là hàng NL"));
        var manual = ResolveLine(Cfg, c.Blocks[0], new CargoLine { Loai = "BTP", Sn = "0040-6", KsTay = "7", Dai = new() { ["1"] = "5" } }, null);
        Assert.Equal("NHẬT, ASC-B, HQ", manual.Market); Assert.True(manual.Ks!.Tay);
        Assert.Contains(ResolveLine(Cfg, c.Blocks[0], new CargoLine { Sn = "0040-6", KsTay = "999", Dai = new() { ["1"] = "5" } }, null).Errors, e => e.Contains("không có trong bảng 1–83"));
    }

    [Fact]
    public void Phiếu_groups_by_block_size_and_market()
    {
        var c = Sample(); var ph = BuildPhieu(Cfg, c, Ix());
        Assert.Empty(ph.Problems);
        Assert.Equal(62 + 30 + 8 + 5, ph.Kien);
        Assert.Equal(new[] { "NHẬT, EU, HQ", "NHẬT, ASC-B, HQ" }, ph.Cols);                          // đúng thứ tự cột của mẫu
        Assert.Equal(2, ph.Blocks.Count); Assert.Equal("HLSO", ph.Blocks[0].Title);
        var rows = ph.Blocks[0].Rows;
        Assert.Equal(new[] { "BTP30", "BTP40" }, rows.Select(r => r.Size));                       // size tăng dần
        var b40 = rows.Single(r => r.Size == "BTP40");
        Assert.Equal(62, b40.Cols["NHẬT, EU, HQ"]); Assert.Equal(8, b40.Cols["NHẬT, ASC-B, HQ"]); Assert.Equal(70, b40.Kien);   // lô 16 và lô 17 cùng size nhưng khác thị trường
        Assert.Equal(70 * 10.8, b40.Kg, 6);
        Assert.Equal(5, ph.Other);
        Assert.Equal(4, ph.SoDay);                                                                // dãy 1, 2, 3, 4 có hàng
        // chưa có kết quả → không tính vào cột nào + báo lỗi chặn xuất
        c.Blocks[0].Lines.Add(new CargoLine { Loai = "BTP", Sn = "0050-6", Lo = "01.01.26 L9", Dai = new() { ["5"] = "9" } });
        var bad = BuildPhieu(Cfg, c, Ix());
        Assert.Single(bad.Problems); Assert.Equal(9, bad.Blocks[0].Rows.Single(r => r.Size == "BTP50").Chua);
    }

    [Fact]
    public void Fill_all_days_and_numbers()
    {
        var c = Sample(); c.Blocks = new() { new CargoBlock { Loai = "ks", Ten = "HLSO", Lines = new() { new CargoLine { Loai = "BTP", Sn = "0040-6", Lo = "22.05.26 L16" }, new CargoLine { Loai = "BTP", Sn = "0030-6", Lo = "22.05.26 L17" } } } };
        var caps = new Dictionary<int, int> { [1] = 62, [2] = 62, [3] = 54 };
        var r1 = RaiTong(c, 0, 0, 100, caps);
        Assert.Equal(100, r1.Placed); Assert.Equal("62", c.Blocks[0].Lines[0].Dai["1"]); Assert.Equal("38", c.Blocks[0].Lines[0].Dai["2"]);
        var r2 = RaiTong(c, 0, 1, 100, caps);                                                       // dãy 2 còn 24 trống, dãy 3 còn 54 → xếp 24 + 54 = 78, thừa 22
        Assert.Equal(78, r2.Placed); Assert.Equal(22, r2.Left);
        Assert.Equal("24", c.Blocks[0].Lines[1].Dai["2"]); Assert.Equal("54", c.Blocks[0].Lines[1].Dai["3"]);
        Assert.True(RaiTong(c, 0, 0, 10, new Dictionary<int, int>()).NoCap);
    }

    [Fact]
    public void Numbering_and_names_and_vehicle_text()
    {
        var a = new CargoRec { Id = "1", SoPhieu = "04.10 XTP 45", Ngay = "2026-10-20" }; var b = new CargoRec { Id = "2", SoPhieu = "01.09 XTP 44", Ngay = "2026-09-02" }; var other = new CargoRec { Id = "3", SoPhieu = "09.10 XGC 60", Ngay = "2026-10-21" };
        Assert.Equal("05.10 XTP 46", NextSo(new[] { a, b, other }, "2026-10-29", "", "XTP"));
        Assert.Equal("01.11 XTP 46", NextSo(new[] { a, b }, "2026-11-02", "", "XTP"));            // sang tháng mới: NN về 01, KK vẫn đếm trong năm
        Assert.Equal("01.01 XTP 01", NextSo(new[] { a, b }, "2027-01-05", "", "XTP"));           // sang năm mới: cả hai về 01
        Assert.Equal("05.10XGK 30", NextSo(new[] { new CargoRec { Id = "9", SoPhieu = "04.10XGK 29", Ngay = "2026-10-01" } }, "2026-10-29", "", "XGK"));   // gửi kho ghi liền
        var c = Sample(); Assert.Equal("05_10 XTP 46 (29_10) An An+Long An.xlsx", FileName(c, "XTP"));
        c.SoPhieu = "09.05XGK 29"; c.Ngay = "2026-05-30"; c.Kho = "An An"; Assert.Equal("09_05XGK 29 (30_05) An An.xlsx", FileName(c, "XGK")); Assert.Equal("29/2026", ContSoOf(c));
        var xe = ParseXe("Họ tên tài xế: Trần B\nSĐT: 0912 345 678\nSố xe: 60C-999.99\nSố cont: TEMU 7654321\nSeal: AB123\nNgày xuất: 29/10/2026");
        Assert.Equal("Trần B", xe.TaiXe); Assert.Equal("0912345678", xe.Sdt); Assert.Equal("60C-999.99", xe.SoXe); Assert.Equal("TEMU 7654321", xe.Cont); Assert.Equal("AB123", xe.Seal); Assert.Equal("2026-10-29", xe.Ngay);
        Assert.Equal("Trần B ( 0912345678 )", NguoiNhanOf(xe)); Assert.Equal("60C-999.99 / TEMU 7654321", ContXeOf(xe));
    }

    [Fact]
    public void Store_roundtrip_and_export_thuan_phuoc_template()
    {
        SeedKs();
        var sv = _s.Cargo; sv.LoadRefs();
        Assert.Equal(3, sv.Ks.Size);
        var prof = Profiles[0];
        var rec = sv.NewRec(prof, new List<CargoRec>()); Assert.EndsWith("XTP 01", rec.SoPhieu);
        var c = Sample(rec.SoPhieu); c.Id = "";
        var saved = sv.Save(prof, c);
        var again = new CargoService(() => _s.Lan, _s.Session, _s.Audit).List(prof);
        Assert.Single(again); Assert.Equal(saved.Id, again[0].Id); Assert.Equal("22.05.26 L16", again[0].Blocks[0].Lines[0].Lo); Assert.Equal("50", again[0].Blocks[0].Lines[0].Dai["1"]);
        var sheet = _s.Modules.ReadSheet(CargoService.Db, "PNX_Cargo");
        Assert.StartsWith("__JSON__", sheet[0]!["data"]!.GetValue<string>()); Assert.Equal(105, sheet[0]!["kien"]!.GetValue<double>());

        var path = Path.Combine(_dir, "cargo.xlsx");
        sv.Export(prof, saved, path);
        var readable = Path.Combine(_dir, "cargo-doc.xlsx"); File.Copy(path, readable);
        using (var z = System.IO.Compression.ZipFile.Open(readable, System.IO.Compression.ZipArchiveMode.Update))   // ClosedXML không đọc được tên vùng in dạng $1:$12 (hợp lệ trong Excel)
        {
            var e = z.GetEntry("xl/workbook.xml")!; string xml; using (var r = new StreamReader(e.Open())) xml = r.ReadToEnd(); e.Delete();
            using var w = new StreamWriter(z.CreateEntry("xl/workbook.xml").Open()); w.Write(System.Text.RegularExpressions.Regex.Replace(xml, "<definedName name=\"_xlnm.Print_Titles\"[^>]*>[^<]*</definedName>", ""));
        }
        using var wb = new XLWorkbook(readable);
        Assert.Equal(new[] { "Cargo", "Phiếu xuất kho" }, wb.Worksheets.Select(w => w.Name).Where(n => n is "Cargo" or "Phiếu xuất kho").ToArray());
        var cg = wb.Worksheet("Cargo");
        Assert.Contains("Cont/Xe : 51C-12345 / MSKU1234567", cg.Cell("A4").GetString());
        Assert.Equal(1.0, cg.Cell("D7").GetDouble()); Assert.Equal(20.0, cg.Cell("W7").GetDouble());     // tiêu đề dãy 1..20 (Half = 20)
        var all = cg.CellsUsed().Select(x => x.GetString()).ToList();
        Assert.Contains("HLSO", all); Assert.Contains("NHẬT, EU, HQ", all); Assert.Contains("VRHLBTNXXXR000040IME10003", all); Assert.Contains("22.05.26 L16", all);
        var ph = wb.Worksheet("Phiếu xuất kho");
        var pAll = ph.CellsUsed().Select(x => x.GetString()).ToList();
        Assert.Contains("Số : " + rec.SoPhieu, pAll);
        Assert.Contains(pAll, s => s.StartsWith("Bằng chữ") || s.Contains("BẰNG CHỮ"));
        Assert.Contains(pAll, s => s.Contains("Một trăm lẻ năm kiện"));
        Assert.Contains("BTP40", pAll); Assert.Contains("1.8Kg x 6", pAll);
        // công thức tổng được ghi (SUM…) và có giá trị tính sẵn
        var formulas = ph.CellsUsed().Where(x => x.HasFormula).Select(x => x.FormulaA1).ToList();
        Assert.Contains(formulas, f => f.StartsWith("SUM(")); Assert.Contains(formulas, f => f.StartsWith("ROUND(AC"));

        // đang còn lỗi chưa xử lý → chặn xuất
        c.Blocks[0].Lines.Add(new CargoLine { Loai = "BTP", Sn = "0050-6", Lo = "01.01.26 L9", Dai = new() { ["5"] = "9" } });
        var ex = Assert.Throws<BusinessException>(() => sv.Export(prof, c, Path.Combine(_dir, "bad.xlsx")));
        Assert.Contains("lỗi chưa xử lý", ex.Message);
        sv.Delete(prof, saved.Id); Assert.Empty(sv.List(prof));
    }

    [Fact]
    public void Export_gui_kho_template_lists_each_line()
    {
        SeedKs();
        var sv = _s.Cargo; sv.LoadRefs();
        var prof = Profiles[2];
        var c = Sample("09.05XGK 29"); c.Ngay = "2026-05-30"; c.Kho = "An An"; c.ContSo = ""; c.KhoNhan = "";
        c.Blocks.RemoveAt(1);
        var g = BuildGk(Cfg, c, sv.Ks);
        Assert.Empty(g.Problems); Assert.Equal(3, g.Rows.Count); Assert.Equal(100, g.Kien);
        Assert.Equal(19, g.Rows[0].Ma.Length); Assert.Equal("VRHLBTN0040IME10003", g.Rows[0].Ma);
        Assert.Equal("TÔM TCT BTP40, BLOCK, 1.8Kg x 6, 0003", g.Rows[0].Ten);
        Assert.Equal("22.05.26 L16", g.Rows[0].Lo); Assert.Equal("22.05.26 L17", g.Rows[1].Lo);
        var path = Path.Combine(_dir, "gk.xlsx");
        sv.Export(prof, c, path);
        using var wb = new XLWorkbook(path);
        var ph = wb.Worksheet("Phiếu xuất kho"); var cg = wb.Worksheet("Cargo");
        var pAll = ph.CellsUsed().Select(x => x.GetString()).ToList();
        Assert.Contains("VRHLBTN0040IME10003", pAll); Assert.Contains("TÔM TCT BTP40, BLOCK, 1.8Kg x 6, 0003", pAll);
        Assert.Contains(pAll, s => s.Contains("CONT GỬI KHO SỐ:  29/2026"));
        Assert.Contains(pAll, s => s.Contains("Transimex Long An") || s.Contains("TRANSIMEX LONG AN") || s.Contains("gửi kho"));
        Assert.Contains(cg.CellsUsed().Select(x => x.GetString()), s => s.Contains("Khách hàng : GỬI KHO TRANSIMEX LONG AN"));
        Assert.Equal(1.0, cg.Cell("F7").GetDouble());                                                // 15 dãy / bảng (cột F…T)
    }
}
