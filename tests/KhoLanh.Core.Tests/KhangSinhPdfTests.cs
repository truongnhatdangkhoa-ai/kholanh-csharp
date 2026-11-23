using System.Text.Json.Nodes;
using KhoLanh.Core.KhangSinh;
using KhoLanh.Core.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>
/// Đọc PDF phiếu kết quả kháng sinh: so với app cũ (pdf.js 3.11 + bộ đọc trong tra-cuu-khang-sinh.html) trên 4 phiếu mẫu tự tạo
/// (Data/ks-pdf, tạo bằng tao-phieu-mau.py + LibreOffice; kết quả app cũ ở old-app.json). Không có dữ liệu thật trong repo.
/// </summary>
public class KhangSinhPdfTests : IDisposable
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Data", "ks-pdf");
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "klcs-kspdf-" + Guid.NewGuid().ToString("N"));
    private readonly AppServices _s;

    public KhangSinhPdfTests()
    {
        Directory.CreateDirectory(_dir);
        _s = new AppServices(Path.Combine(_dir, "kho.db"));
        _s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
        _s.Auth.Login("admin", "matkhau1");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch { } }

    private static JsonArray Old() => JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, "old-app.json")))!.AsArray();
    private static List<string> S(JsonNode? a) => a is JsonArray arr ? arr.Select(x => x?.ToString() ?? "").ToList() : new();
    private static KhangSinhPdf.Phieu Phieu(string file)
    {
        var r = KhangSinhPdf.Read(File.ReadAllBytes(Path.Combine(Dir, file)), file)!;
        KhangSinhPdf.CheckAll(r);
        return r;
    }

    [Fact]
    public void Text_pieces_and_generic_table_match_pdfjs()
    {
        foreach (var f in Old())
        {
            var file = f!["file"]!.ToString();
            var pages = PdfText.Pages(PdfText.Read(File.ReadAllBytes(Path.Combine(Dir, file))));
            var raw = f["raw"]!.AsArray();
            Assert.Equal(raw.Count, pages.Count);
            for (int i = 0; i < raw.Count; i++) Assert.Equal(S(raw[i]), pages[i].Raw);
            var t = KhangSinhPdf.Table(File.ReadAllBytes(Path.Combine(Dir, file)));
            Assert.Equal(S(f["table"]!["headers"]), t.Headers);
            Assert.Equal(f["table"]!["rows"]!.AsArray().Select(r => string.Join("¦", S(r))), t.Rows.Select(r => string.Join("¦", r)));
        }
    }

    [Fact]
    public void Phieu_samples_and_warnings_match_old_app()
    {
        foreach (var f in Old())
        {
            var file = f!["file"]!.ToString();
            var r = Phieu(file);
            Assert.Equal(f["kind"]!.ToString(), r.Kind);
            Assert.Equal(f["date"]!.ToString(), r.Date);
            Assert.Equal(f["so"]!.ToString(), r.So);
            Assert.Equal((int)f["altCount"]!, r.AltCount);
            Assert.Equal(f["fileIssues"]!.AsArray().Select(i => i!["lv"] + "|" + i["msg"]), r.FileIssues.Select(i => i.Lv + "|" + i.Msg));
            var ss = f["samples"]!.AsArray();
            Assert.Equal(ss.Count, r.Samples.Count);
            for (int i = 0; i < ss.Count; i++)
            {
                var o = ss[i]!; var s = r.Samples[i];
                var ctx = $"{file} mẫu {s.Sid}";
                Assert.True((double)o["stt"]! == s.Stt, ctx);
                Assert.Equal(o["sid"]!.ToString(), s.Sid);
                Assert.Equal(o["group"]!.ToString(), s.Group);
                Assert.Equal(S(o["lines"]), s.Lines);
                foreach (var k in KhangSinhCore.FieldKeys) Assert.Equal(o["raw"]![k]!.ToString(), s.Raw[k]);
                Assert.Equal(o["kq1"]!.ToString(), s.Kq1); Assert.Equal(o["kq2"]!.ToString(), s.Kq2);
                Assert.Equal(o["dattinh"]!.ToString(), s.DatTinh); Assert.Equal(o["ghichu"]!.ToString(), s.GhiChu);
                Assert.Equal((int)o["page"]!, s.Page);
                Assert.Equal(o["issues"]!.AsArray().Select(x => x!["lv"] + "|" + x["msg"]), s.Issues.Select(x => x.Lv + "|" + x.Msg));
                var rf = KhangSinhPdf.LotRef(s);
                if (o["ref"] is null) Assert.Null(rf); else { Assert.Equal(o["ref"]!["batch"]!.ToString(), rf!.Batch); Assert.Equal(o["ref"]!["date"]!.ToString(), rf.Date); }
            }
        }
    }

    [Fact]
    public void Helpers_like_old_app()
    {
        Assert.Equal(23, KhangSinhPdf.DtNum("23.MKS"));
        Assert.Equal(23, KhangSinhPdf.DtNum("23 ĐẠT MKS"));
        Assert.Null(KhangSinhPdf.DtNum("5.90"));
        Assert.Null(KhangSinhPdf.DtNum("99.X"));
        Assert.Equal(4.1, KhangSinhPdf.SubStt(4, 1));
        Assert.Equal(5.901, KhangSinhPdf.SubStt(5, 10));
        var l = KhangSinhPdf.LotOfLines(new[] { "MKS chờ 2", "Lô: 3+5 (08.09.26)" })!;
        Assert.Equal("3+5", l.Batch); Assert.Equal(new[] { "3", "5" }, l.Batches); Assert.Equal("2026-09-08", l.Date);
        Assert.Null(KhangSinhPdf.Read(System.Text.Encoding.ASCII.GetBytes(MinimalPdf("Hello")), "x.pdf"));
    }

    [Fact]
    public void Import_lan1_then_kiem_lai_attaches_to_lots()
    {
        var ks = _s.KhangSinh; ks.Load();
        // phiếu lần 1: 5 lô mới
        var l1 = Phieu("lan1-a.pdf");
        Assert.DoesNotContain(l1.Samples, KhangSinhPdf.Need);
        var msg = ks.ImportPdf(new[] { l1 }, keepOthers: true);
        Assert.Contains("5 lô mới", msg);
        var lo13 = ks.Records.Single(r => r.Date == "2026-06-30" && r.Batch == "13");
        Assert.Equal("41.AS1-H", lo13.KyHieu); Assert.Equal("THUẬN PHƯỚC X2L1", lo13.Note); Assert.Equal("4.83", lo13.Inputs["cipro"]);
        Assert.Equal("lan1-a.pdf", lo13.Lan1Src!["file"]!.ToString());
        // nạp lại phiếu lần 1 thứ 2: 3 lô đã có → cập nhật, 1 lô mới
        msg = ks.ImportPdf(new[] { Phieu("lan1-b.pdf") }, true);
        Assert.Contains("1 lô mới", msg); Assert.Contains("3 lô đã có", msg);
        Assert.Equal(6, ks.Records.Count);

        // phiếu kiểm lại: có mẫu cảnh báo → chưa tick "Đã so" thì không cho nhập
        var k = Phieu("lan2-a.pdf");
        Assert.Contains(k.Samples, KhangSinhPdf.Need);
        Assert.Throws<BusinessException>(() => ks.ImportPdf(new[] { k }, true));
        foreach (var s in k.Samples.Where(KhangSinhPdf.Need)) s.Ok = true;
        ks.AttachSavedLan1(new[] { k });
        msg = ks.ImportPdf(new[] { k }, true);
        Assert.Contains("1 mẫu lưu dòng riêng", msg);           // mẫu 3 không ghi lô
        var sep = ks.Records.Single(r => r.Batch.StartsWith("MKS chờ 3"));
        Assert.Equal("2026-10-01", sep.Date); Assert.Equal("23.MKS", sep.KyHieu); Assert.Contains("：", sep.Batch);
        var lo3 = ks.Records.Single(r => r.Date == "2026-09-08" && r.Batch == "3");   // "Lô: 3+5" → tạo lô 3 và 5
        Assert.Equal("lan2-import", lo3.Source); Assert.Single(lo3.Lan2);
        Assert.Equal("41.AS1-H", lo3.Lan2[0]["kyhieu"]!.ToString());
        Assert.True((bool)lo3.Lan2[0]["xacNhanTay"]!);
        Assert.Single(ks.Records, r => r.Date == "2026-09-08" && r.Batch == "5");
        // nạp lại cùng phiếu → không nhân đôi lần kiểm lại (cùng số phiếu + ngày + STT + file)
        ks.ImportPdf(new[] { k }, true);
        Assert.Single(ks.Records.Single(r => r.Date == "2026-09-08" && r.Batch == "3").Lan2);
    }

    /// <summary>PDF tối thiểu 1 trang có chữ (không phải phiếu).</summary>
    private static string MinimalPdf(string text)
    {
        var content = $"BT /F1 12 Tf 72 720 Td ({text}) Tj ET";
        var objs = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var sb = new System.Text.StringBuilder("%PDF-1.4\n"); var offs = new List<int>();
        for (int i = 0; i < objs.Length; i++) { offs.Add(sb.Length); sb.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
        var x = sb.Length;
        sb.Append($"xref\n0 {objs.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offs) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objs.Length + 1} /Root 1 0 R >>\nstartxref\n{x}\n%%EOF\n");
        return sb.ToString();
    }
}
