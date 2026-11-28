using System.Text.Json.Nodes;
using ClosedXML.Excel;
using KhoLanh.Core.ChungTu;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;
using static KhoLanh.Core.ChungTu.ChungTuService;

namespace KhoLanh.Core.Tests;

public class ChungTuTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-ct-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;

    public ChungTuTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private ChungTuService Ct => _s.ChungTu;

    /// <summary>File kiểu phần mềm kho: 2 dòng tiêu đề lớn, cột Size không có tiêu đề, cột ghi chú phía sau.</summary>
    private string File_(string name, bool withTl = true)
    {
        var path = Path.Combine(_dir, name);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Sheet1");
        ws.Cell(1, 1).Value = "BÁO CÁO XUẤT KHO";
        var heads = withTl
            ? new[] { "STT", "Mã hàng", "Tên hàng", "", "Số lượng (kiện)", "Ngày chứng từ", "Trọng lượng (kg)", "Net", "", "Ghi chú" }
            : new[] { "STT", "Mã hàng", "Tên hàng", "", "Số lượng", "Ngày chứng từ", "Net", "", "Ghi chú" };
        for (var i = 0; i < heads.Length; i++) ws.Cell(3, i + 1).Value = heads[i];
        var rows = new (string ma, string ten, string size, double sl, DateTime d, double tl, string note)[]
        {
            ("TPC-VRHLBTNXECR003140IME1", "Tôm sú, IQF, 31/40 (9 kg x 1 goi)", "", 10, new(2026, 11, 2), 9, ""),
            ("B2", "Tôm thẻ", "16/20", 4, new(2026, 11, 1), 10.8, ""),
            ("B3", "Mực, 2L30 (0.140 kg", "", 3, new(2026, 11, 2), 5, "markfood"),
            ("", "dòng tổng", "", 17, new(2026, 11, 2), 0, ""),
            ("B4", "Cá", "U10", 2, new(2026, 11, 3), 12.5, "12"),
        };
        var r = 4;
        foreach (var x in rows)
        {
            ws.Cell(r, 1).Value = r - 3; ws.Cell(r, 2).Value = x.ma; ws.Cell(r, 3).Value = x.ten; ws.Cell(r, 4).Value = x.size;
            ws.Cell(r, 5).Value = x.sl; ws.Cell(r, 6).Value = x.d;
            if (withTl) { ws.Cell(r, 7).Value = x.tl; ws.Cell(r, 8).Value = 999; ws.Cell(r, 10).Value = x.note; }
            else { ws.Cell(r, 7).Value = x.sl * x.tl; ws.Cell(r, 9).Value = x.note; }
            r++;
        }
        wb.SaveAs(path);
        return path;
    }

    [Fact]
    public void Parse_detects_header_fuzzy_size_hint_and_net()
    {
        var p = Parse(ReadRaw(File_("x.xlsx")));
        Assert.Equal("", p.Error);
        Assert.Equal(4, p.Rows.Count);                              // bỏ dòng không có mã
        var a = p.Rows[0];
        Assert.Equal("02/11/2026", a.NgayChungTu);
        Assert.Equal(90, a.Net);                                    // Net = SL × TL, không lấy cột Net (999) của file
        Assert.Equal("31/40", a.SizeText);                          // size lấy từ tên hàng
        Assert.Equal("16/20", p.Rows[1].SizeText);                  // cột Size không tiêu đề nằm giữa Tên hàng và Số lượng
        Assert.Equal("2L30", p.Rows[2].SizeText);                   // tên bị cắt mất ")"
        Assert.Equal("markfood", p.Rows[2].GoiY);
        Assert.Equal("", p.Rows[3].GoiY);                           // ghi chú toàn số không phải gợi ý

        var q = Parse(ReadRaw(File_("y.xlsx", withTl: false)));
        Assert.Contains("Net ÷ Số lượng", q.Warning);
        Assert.Equal(10.8, q.Rows[1].TrongLuong);

        var bad = Parse(new List<List<object?>> { new() { "Mã hàng", "Tên" }, new() { "A", "B" } });
        Assert.StartsWith("Thiếu cột:", bad.Error);
        Assert.Equal("trongLuong", MatchFieldFuzzy("TL (kg)"));
        Assert.Null(MatchFieldFuzzy("Tổng trọng lượng"));
    }

    [Fact]
    public void Moi_loai_phieu_co_thong_tin_phieu_rieng()
    {
        Ct.Load();
        Ct.Mod = "xa"; Ct.Form["fKho"] = "Kho Xả"; Ct.Form["fGhiChu"] = "ghi chú xả"; Ct.Form["fLoaiChungTu"] = "Xuất Xả đặc biệt";
        Ct.Mod = "kiem";
        Assert.Equal("Xuất Kiểm", Ct.Form["fLoaiChungTu"]);
        Assert.Equal("Kho Lạnh - An An", Ct.Form["fKho"]);
        Assert.Equal("", Ct.Form["fGhiChu"]);
        Ct.Form["fKho"] = "Kho Kiểm"; Ct.SaveForm();
        Ct.Mod = "tbb";
        Assert.Equal("Xuất Thay Thùng", Ct.Form["fLoaiChungTu"]);
        Assert.Equal("Kho Lạnh - An An", Ct.Form["fKho"]);
        Ct.SaveForm();
        var again = new ChungTuService(() => _s.Lan, _s.Session, _s.Audit);
        again.Load();
        Assert.Equal("Kho Xả", again.FormOf("xa")["fKho"]);
        Assert.Equal("Xuất Xả đặc biệt", again.FormOf("xa")["fLoaiChungTu"]);
        Assert.Equal("Kho Kiểm", again.FormOf("kiem")["fKho"]);
        Assert.Equal("Kho Lạnh - An An", again.FormOf("tbb")["fKho"]);
    }

    [Fact]
    public void Meta_cu_mot_bo_thong_tin_chung_duoc_chep_cho_ca_ba_loai()
    {
        Ct.Load();
        // bản ≤ 1.192 chỉ có "form" dùng chung (đang ghi "Xuất Xả", số XA-1128)
        var json = "{\"v\":1,\"mod\":\"xa\",\"form\":{\"fNgayLap\":\"2026-11-28\",\"fSoPhieu\":\"XA-1128\",\"fLoaiChungTu\":\"Xuất Xả\",\"fMaBieuMau\":\"BM02/QT23\",\"fKho\":\"Kho Lạnh\",\"fSoBanMoiNgay\":\"2\",\"fGhiChu\":\"\"}}";
        _s.Lan.Write(Db, new Dictionary<string, System.Text.Json.Nodes.JsonArray?> { [SheetMeta] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject { ["data"] = "__JSON__" + json }) });
        var again = new ChungTuService(() => _s.Lan, _s.Session, _s.Audit);
        again.Load();
        Assert.Equal("Xuất Xả", again.FormOf("xa")["fLoaiChungTu"]);
        Assert.Equal("XA-1128", again.FormOf("xa")["fSoPhieu"]);
        Assert.Equal("Xuất Thay Thùng", again.FormOf("tbb")["fLoaiChungTu"]);
        Assert.Equal("Xuất Kiểm", again.FormOf("kiem")["fLoaiChungTu"]);
        Assert.Equal("TBB-" + DateTime.Today.ToString("MMdd"), again.FormOf("tbb")["fSoPhieu"]);
        Assert.Equal("2", again.FormOf("kiem")["fSoBanMoiNgay"]);
    }

    [Fact]
    public void Groups_merge_split_and_numbering()
    {
        Ct.Load();
        Ct.Import("tbb", File_("x.xlsx"));
        Assert.Equal("Xuất Thay Thùng", Ct.Form["fLoaiChungTu"]);
        var eff = Ct.Effective("tbb");
        Assert.Equal(new[] { "01/11/2026", "02/11/2026", "03/11/2026" }, eff.Select(g => g.Label));
        Assert.Equal(new[] { "01112026", "02112026", "03112026" }, eff.Select(g => g.SoPhieu));
        Assert.Equal("Ngày 02 Tháng 11 Năm 2026", eff[1].DateLine);

        var merged = Ct.Merge("tbb", new[] { eff[1], eff[2] });
        Assert.True(merged.IsMerged);
        Assert.Equal("02/11 - 03/11", merged.Label);
        Assert.Equal("Từ ngày 02/11/2026 đến ngày 03/11/2026", merged.DateLine);
        Assert.Equal(3, merged.Items.Count);
        Assert.Equal(2, Ct.Effective("tbb").Count);

        // tách dòng "markfood" sang phiếu riêng cùng ngày → gộp cũ không còn đủ 2 nhóm cũ nhưng phiếu tách đứng sau phiếu thường
        Ct.Unmerge("tbb", merged);
        Ct.SetTach("tbb", Ct.Rows["tbb"].Where(r => r.GoiY.Length > 0), true);
        eff = Ct.Effective("tbb");
        Assert.Equal(4, eff.Count);
        Assert.True(eff[2].Tach);
        Assert.Equal("02/11/2026", eff[2].Label);
        Assert.Throws<BusinessException>(() => Ct.Merge("tbb", new[] { eff[1], eff[2] }));   // không gộp lẫn phiếu tách / thường

        // lưu và đọc lại (giống app cũ: DuLieu_TBB + CT_Meta)
        Ct.Merge("tbb", new[] { eff[0], eff[1] });
        Ct.Form["fGhiChu"] = "Ca sáng"; Ct.SaveForm();
        var again = new ChungTuService(() => _s.Lan, _s.Session, _s.Audit);
        again.Load();
        Assert.Equal(4, again.Rows["tbb"].Count);
        Assert.Single(again.Rows["tbb"], r => r.Tach);
        Assert.Equal("x.xlsx", again.Files["tbb"]);
        Assert.Equal("Ca sáng", again.Form["fGhiChu"]);
        Assert.Equal(3, again.Effective("tbb").Count);
        var meta = _s.Modules.ReadSheet(Db, SheetMeta);
        Assert.StartsWith("__JSON__", meta[0]!["data"]!.GetValue<string>());
    }

    [Fact]
    public void Reads_data_saved_by_old_app()
    {
        _s.Modules.WriteSheets(Db, new Dictionary<string, JsonArray?>
        {
            ["DuLieu_Xa"] = (JsonArray)JsonNode.Parse("""
                [{"maHang":"A1","tenHang":"Tôm","size":"","soLuong":5,"ngayChungTu":"05/10/2026","ngayChungTuSort":46300,"trongLuong":2,"net":10,"tach":"","goiY":""},
                 {"maHang":"A2","tenHang":"Mực","size":"","soLuong":"3","ngayChungTu":"06/10/2026","ngayChungTuSort":46301,"trongLuong":"1.5","net":"4.5","tach":1,"goiY":"x"}]
                """)!,
            ["Xa"] = (JsonArray)JsonNode.Parse("""[{"pxNhan":"Lan","qcGiamSat":"Minh","keToan":"","quanLyKho":"An","giamDoc":"Bình"}]""")!,
            [SheetMeta] = new JsonArray(new JsonObject { ["data"] = """__JSON__{"v":1,"mod":"xa","files":{"tbb":"","xa":"xa.xlsx","kiem":""},"merged":{"tbb":[],"xa":[],"kiem":[]},"form":{"fSoPhieu":"XA-1005","fMaBieuMau":"BM02/QT23"},"at":1}""" }),
        });
        Ct.Load();
        Assert.Equal("xa", Ct.Mod);
        Assert.Equal("xa.xlsx", Ct.Files["xa"]);
        Assert.Equal(2, Ct.Rows["xa"].Count);
        Assert.True(Ct.Rows["xa"][1].Tach);
        Assert.Equal(4.5, Ct.Rows["xa"][1].Net);
        Assert.Equal(new[] { "Lan", "Minh", "", "An", "Bình" }, Ct.SignerNames("xa"));
        Assert.Equal("XA-1005", Ct.Meta().SoPhieu);
        Assert.Equal("Kho Lạnh - An An", Ct.Meta().BoPhan);   // ô chưa lưu → mặc định
    }

    [Fact]
    public void Compose_name_from_code()
    {
        var t = new Dictionary<(int, string), string>
        {
            [(1, "VRHL")] = "Tôm sú", [(2, "B")] = "IQF", [(3, "T")] = "Túi", [(4, "N")] = "Không hóa chất", [(5, "E")] = "EU",
            [(6, "CR")] = "Culinar", [(7, "003")] = "10%", [(8, "1403")] = "31/40", [(9, "IM")] = "9.0 kg/gói", [(10, "E1")] = "1 gói/thùng",
        };
        string? L(int s, string c) => t.TryGetValue((s, c), out var v) ? v : null;
        var r = Compose("TPC-VRHLBTNECR0031403IME1", L);
        Assert.True(r.Ok, r.Err);
        Assert.Equal("Tôm sú, IQF, Túi, Không hóa chất, EU, Culinar, 10%, 31/40 (9 kg x 1 gói)", r.Ten);
        Assert.False(Compose("ABC", L).Ok);
        Assert.Contains("không có trong Mã hóa", Compose("VRHLBTNECR0031403IMZ9", L).Err);
        Assert.Equal("cut", NameStatus("Tôm sú, IQF, Túi", r.Ten));
        Assert.Equal("thieu", NameStatus("", r.Ten));
        Assert.Equal("dung", NameStatus("Tôm sú,IQF, Túi, Không hóa chất, EU, Culinar, 10%, 31/40 (9 kg x 1 gói)", r.Ten));
        Assert.Equal("khac", NameStatus("Tôm khác", r.Ten));
    }

    [Fact]
    public void Exports_excel_and_print_page()
    {
        Ct.Load();
        Ct.Import("kiem", File_("x.xlsx"));
        Ct.SaveSigners("kiem", new() { new() { ["nguoiNhan"] = "Lan", ["giamDoc"] = "Bình" } });
        var eff = Ct.Effective("kiem");
        var path = Path.Combine(_dir, "ct.xlsx");
        Ct.ExportExcel("kiem", eff, path);
        using (var wb = new XLWorkbook(path))
        {
            Assert.Equal(new[] { "01-11-2026", "02-11-2026", "03-11-2026" }, wb.Worksheets.Select(w => w.Name));
            var ws = wb.Worksheet("02-11-2026");
            Assert.Equal("Số : 02112026", ws.Cell(2, 1).GetString());
            Assert.Equal("Tổng kiện", ws.Cell(9, 1).GetString());
            Assert.Equal(13, ws.Cell(9, 4).GetDouble());
            Assert.Contains("NGƯỜI NHẬN", ws.Cell(11, 1).GetString());
            Assert.Contains("Lan", ws.Cell(14, 1).GetString());
            Assert.Single(ws.Pictures);
        }
        var html = Ct.PrintHtml("kiem", eff, 2);
        Assert.Equal(6, System.Text.RegularExpressions.Regex.Matches(html, "class=\"dn-page\"").Count);
        Assert.Contains("DISPATCHING NOTE", html);
        Assert.Contains("BM02/QT23", html);
    }

    [Fact]
    public void Xuat_PDF_truc_tiep_tu_trang_in()
    {
        if (HtmlPdf.FindBrowser() is null) return;   // máy chạy test không có Edge / Chrome → bỏ qua
        Ct.Load();
        Ct.Import("tbb", File_("x.xlsx"));
        var eff = Ct.Effective("tbb");
        var pdf = Path.Combine(_dir, "ra", "phieu.pdf");
        Ct.WritePdf("tbb", eff.Take(2).ToList(), 2, pdf, Path.Combine(_dir, "in"));
        var bytes = File.ReadAllBytes(pdf);
        Assert.True(bytes.Length > 1500);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Empty(Directory.GetFiles(Path.Combine(_dir, "in"), "*.html"));   // trang tạm đã xóa
        // 2 phiếu × 2 bản = 4 trang A4
        var pages = System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(bytes), @"/Type\s*/Page[^s]").Count;
        Assert.Equal(4, pages);
    }
}
