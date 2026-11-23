using KhoLanh.Core.Models;
using KhoLanh.Core.Scan;
using KhoLanh.Core.Services;
using Xunit;

namespace KhoLanh.Core.Tests;

public class ScanTests
{
    // Ảnh giả "chụp tài liệu": bàn gỗ màu nâu, tờ giấy trắng hơi nghiêng (4 góc biết trước), chữ đen, dòng kẻ xám nhạt, bóng tối dần sang phải.
    private static readonly (double x, double y)[] Corners = { (0.18, 0.12), (0.84, 0.16), (0.80, 0.90), (0.14, 0.86) };

    private static RgbaImage Photo(int W = 900, int H = 1200)
    {
        var img = new RgbaImage(W, H); var d = img.Px;
        var P = Corners.Select(c => (x: c.x * W, y: c.y * H)).ToArray();
        bool Inside(double x, double y)
        {
            var sign = 0;
            for (var i = 0; i < 4; i++)
            {
                var a = P[i]; var b = P[(i + 1) % 4]; var z = (b.x - a.x) * (y - a.y) - (b.y - a.y) * (x - a.x);
                var s = z > 0 ? 1 : -1; if (sign == 0) sign = s; else if (s != sign) return false;
            }
            return true;
        }
        for (var y = 0; y < H; y++)
            for (var x = 0; x < W; x++)
            {
                var p = (y * W + x) * 4; byte r, g, b;
                if (Inside(x, y))
                {
                    var shade = 1 - 0.35 * x / W;                     // bóng tối dần
                    var v = 235 * shade;
                    if (y % 60 == 0) v *= 0.75;                         // dòng kẻ mảnh
                    if (y % 60 > 20 && y % 60 < 34 && x % 40 < 22 && x > W * 0.3 && x < W * 0.7) v = 30; // "chữ"
                    r = g = b = (byte)v;
                }
                else { r = 120; g = 78; b = 40; }                       // bàn gỗ
                d[p] = r; d[p + 1] = g; d[p + 2] = b; d[p + 3] = 255;
            }
        return img;
    }

    [Fact]
    public void Detects_paper_corners()
    {
        var q = ScanCore.DetectQuad(Photo());
        Assert.NotNull(q);
        for (var i = 0; i < 4; i++)
        {
            Assert.True(Math.Abs(q![i].X - Corners[i].x) < 0.03, $"góc {i} X {q[i].X:0.000} ≠ {Corners[i].x}");
            Assert.True(Math.Abs(q[i].Y - Corners[i].y) < 0.03, $"góc {i} Y {q[i].Y:0.000} ≠ {Corners[i].y}");
        }
    }

    [Fact]
    public void Plain_page_has_no_quad()
    {
        var img = new RgbaImage(400, 500); Array.Fill(img.Px, (byte)250);
        Assert.Null(ScanCore.DetectQuad(img));
    }

    [Fact]
    public void Bw_mode_whitens_shadowed_background_and_keeps_text_black()
    {
        var src = Photo(); var q = ScanCore.DetectQuad(src)!;
        var outp = ScanCore.Process(src, q, 0, "bw", null);
        Assert.True(outp.W > 300 && outp.H > 400);
        // vùng nền giấy phía bên phải (nơi có bóng) phải trắng hẳn sau khi khử bóng
        int white = 0, total = 0, black = 0;
        for (var y = outp.H / 5; y < outp.H * 4 / 5; y++)
            for (var x = outp.W * 3 / 4; x < outp.W * 9 / 10; x++) { total++; if (outp.Px[(y * outp.W + x) * 4] >= 245) white++; }
        for (var y = 0; y < outp.H; y++) for (var x = 0; x < outp.W; x++) if (outp.Px[(y * outp.W + x) * 4] < 60) black++;
        Assert.True(white > total * 0.9, $"nền trắng {white}/{total}");
        Assert.True(black > outp.W * outp.H * 0.01, "chữ đen phải còn");
    }

    [Fact]
    public void Rotate_and_modes()
    {
        var img = new RgbaImage(3, 2); img.Px[0] = 9; // điểm (0,0)
        var r = ScanCore.Rotate(img, 90);
        Assert.Equal(2, r.W); Assert.Equal(3, r.H);
        Assert.Equal(9, r.Px[(0 * r.W + 1) * 4]); // (0,0) → (H-1-0, 0) = (1,0)
        Assert.Equal("bw", ScanCore.Resolve(null, null).Kieu);
        Assert.Equal("goc", ScanCore.Resolve("goc", null).Kieu);
        Assert.True(ScanCore.Resolve("tay", null).Li);
        Assert.Equal(100, ScanCore.Resolve("tuy", new ScanParams("bw", 150, -3, 50, false, 9, 0)).Bong);
    }

    [Fact]
    public void Jpeg_pdf_opens_with_pdfpig()
    {
        // JPEG tối thiểu hợp lệ không cần: PdfPig chỉ đọc cấu trúc trang
        var fake = new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 };
        var pdf = JpegPdf.Build(new[] { new JpegPdf.Page(fake, 100, 140, true), new JpegPdf.Page(fake, 200, 100, false) }, a4: true, title: "Tài liệu thử");
        using var doc = UglyToad.PdfPig.PdfDocument.Open(pdf);
        Assert.Equal(2, doc.NumberOfPages);
        Assert.True(doc.GetPage(1).Height > doc.GetPage(1).Width);   // A4 dọc
        Assert.True(doc.GetPage(2).Width > doc.GetPage(2).Height);   // A4 ngang (ảnh ngang)
    }

    [Fact]
    public void Store_saves_per_account()
    {
        var root = Path.Combine(Path.GetTempPath(), "kl-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            var s = new Session { CurrentUser = new User { Username = "anan", Role = Roles.Admin } };
            var st = new ScanStore(root, s);
            var doc = new ScanDoc { Id = ScanStore.NewId(), Name = "Phiếu nhập" };
            doc.Pages.Add(new ScanPage { Source = new byte[] { 1, 2, 3 }, CropOn = true, Rot = 90, Mode = "tuy", P = new ScanParams("xam", 70, 40, 30, true, 0.9, 10) });
            st.Save(doc);
            var l = st.List(); Assert.Single(l); Assert.Equal("Phiếu nhập", l[0].Name);
            var back = st.Load(doc.Id)!;
            Assert.Equal(90, back.Pages[0].Rot); Assert.True(back.Pages[0].CropOn); Assert.Equal(70, back.Pages[0].P!.Bong); Assert.Equal(new byte[] { 1, 2, 3 }, back.Pages[0].Source);
            s.CurrentUser = new User { Username = "khac", Role = Roles.Admin };
            Assert.Empty(st.List());                                    // tài khoản khác không thấy
            Assert.Throws<BusinessException>(() => st.Load("../x"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
