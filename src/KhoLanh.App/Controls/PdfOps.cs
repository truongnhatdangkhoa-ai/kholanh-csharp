using System.Runtime.InteropServices;
using System.Text;

namespace KhoLanh.App.Controls;

/// <summary>
/// Thao tác trên file PDF bằng PDFium (gói sẵn trong exe): gộp, lấy / tách trang, xoay, xóa trang, đóng dấu chữ / đánh số / hình,
/// xóa trang trắng. Mọi lời gọi đi qua cùng 1 khóa với PdfRender (PDFium không an toàn đa luồng).
/// </summary>
public static partial class PdfRender
{
    [DllImport(Lib)] private static extern IntPtr FPDF_CreateNewDocument();
    [DllImport(Lib, CharSet = CharSet.Ansi)] private static extern int FPDF_ImportPages(IntPtr dest, IntPtr src, string? pagerange, int index);
    [DllImport(Lib)] private static extern void FPDFPage_SetRotation(IntPtr page, int rotate);
    [DllImport(Lib)] private static extern int FPDFPage_GetRotation(IntPtr page);
    [DllImport(Lib)] private static extern void FPDFPage_Delete(IntPtr doc, int index);
    [DllImport(Lib)] private static extern int FPDF_SaveAsCopy(IntPtr doc, ref FileWrite fw, uint flags);
    [DllImport(Lib)] private static extern IntPtr FPDFText_LoadFont(IntPtr doc, byte[] data, uint size, int fontType, int cid);
    [DllImport(Lib)] private static extern void FPDFFont_Close(IntPtr font);
    [DllImport(Lib)] private static extern IntPtr FPDFPageObj_CreateTextObj(IntPtr doc, IntPtr font, float size);
    [DllImport(Lib)] private static extern int FPDFText_SetText(IntPtr textObj, byte[] wideText);
    [DllImport(Lib)] private static extern int FPDFPageObj_SetFillColor(IntPtr obj, uint r, uint g, uint b, uint a);
    [DllImport(Lib)] private static extern void FPDFPageObj_Transform(IntPtr obj, double a, double b, double c, double d, double e, double f);
    [DllImport(Lib)] private static extern int FPDFPageObj_GetBounds(IntPtr obj, out float left, out float bottom, out float right, out float top);
    [DllImport(Lib)] private static extern void FPDFPage_InsertObject(IntPtr page, IntPtr obj);
    [DllImport(Lib)] private static extern int FPDFPage_GenerateContent(IntPtr page);
    [DllImport(Lib)] private static extern IntPtr FPDFPageObj_NewImageObj(IntPtr doc);
    [DllImport(Lib)] private static extern int FPDFImageObj_SetBitmap(IntPtr[]? pages, int count, IntPtr imageObj, IntPtr bitmap);
    [DllImport(Lib)] private static extern int FPDFImageObj_SetMatrix(IntPtr imageObj, double a, double b, double c, double d, double e, double f);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int WriteBlockFn(IntPtr pThis, IntPtr data, uint size);
    [StructLayout(LayoutKind.Sequential)] private struct FileWrite { public int Version; public IntPtr WriteBlock; }

    private static void Init() { if (!_init) { FPDF_InitLibrary(); _init = true; } }

    /// <summary>Mở PDF (bộ nhớ giữ tới khi đóng). Lỗi → thông báo dễ hiểu.</summary>
    private sealed class Doc : IDisposable
    {
        public IntPtr H; private IntPtr _mem;
        public Doc(byte[] pdf, string name)
        {
            _mem = Marshal.AllocHGlobal(pdf.Length); Marshal.Copy(pdf, 0, _mem, pdf.Length);
            H = FPDF_LoadMemDocument(_mem, pdf.Length, null);
            if (H == IntPtr.Zero) { Marshal.FreeHGlobal(_mem); _mem = IntPtr.Zero; throw new InvalidOperationException($"\"{name}\" không phải PDF hợp lệ, bị hỏng hoặc bị khóa mật khẩu."); }
        }
        public Doc(IntPtr h) { H = h; }
        public int Pages => FPDF_GetPageCount(H);
        public void Dispose() { if (H != IntPtr.Zero) FPDF_CloseDocument(H); H = IntPtr.Zero; if (_mem != IntPtr.Zero) Marshal.FreeHGlobal(_mem); _mem = IntPtr.Zero; }
    }

    private static byte[] Save(IntPtr doc)
    {
        var ms = new MemoryStream();
        WriteBlockFn fn = (_, data, size) => { var buf = new byte[size]; Marshal.Copy(data, buf, 0, (int)size); ms.Write(buf, 0, buf.Length); return 1; };
        var fw = new FileWrite { Version = 1, WriteBlock = Marshal.GetFunctionPointerForDelegate(fn) };
        var ok = FPDF_SaveAsCopy(doc, ref fw, 0);
        GC.KeepAlive(fn);
        if (ok == 0) throw new InvalidOperationException("Không ghi được file PDF.");
        return ms.ToArray();
    }

    private static string Range(IEnumerable<int> pages0) => string.Join(",", pages0.Select(p => p + 1));

    public static int Count(byte[] pdf, string name) { lock (Gate) { Init(); using var d = new Doc(pdf, name); return d.Pages; } }

    /// <summary>Gộp nhiều PDF theo thứ tự.</summary>
    public static byte[] Merge(IList<(byte[] Pdf, string Name)> files)
    {
        lock (Gate)
        {
            Init(); using var outp = new Doc(FPDF_CreateNewDocument());
            foreach (var (pdf, name) in files)
            {
                using var src = new Doc(pdf, name);
                if (FPDF_ImportPages(outp.H, src.H, null, outp.Pages) == 0) throw new InvalidOperationException($"Không chép được trang của \"{name}\".");
            }
            return Save(outp.H);
        }
    }

    /// <summary>Tạo PDF mới từ các trang (0-based) của file gốc.</summary>
    public static byte[] Extract(byte[] pdf, string name, IList<int> pages0)
    {
        lock (Gate)
        {
            Init(); using var src = new Doc(pdf, name); using var outp = new Doc(FPDF_CreateNewDocument());
            if (FPDF_ImportPages(outp.H, src.H, Range(pages0), 0) == 0) throw new InvalidOperationException("Không lấy được trang.");
            return Save(outp.H);
        }
    }

    /// <summary>Xoay các trang (0-based) thêm <paramref name="deg"/> độ (90 / 180 / 270).</summary>
    public static byte[] Rotate(byte[] pdf, string name, IList<int> pages0, int deg)
    {
        lock (Gate)
        {
            Init(); using var d = new Doc(pdf, name); var q = ((deg / 90) % 4 + 4) % 4;
            foreach (var i in pages0) { var p = FPDF_LoadPage(d.H, i); if (p == IntPtr.Zero) continue; try { FPDFPage_SetRotation(p, (FPDFPage_GetRotation(p) + q) % 4); } finally { FPDF_ClosePage(p); } }
            return Save(d.H);
        }
    }

    /// <summary>Xóa các trang (0-based).</summary>
    public static byte[] Delete(byte[] pdf, string name, IList<int> pages0)
    {
        lock (Gate)
        {
            Init(); using var d = new Doc(pdf, name);
            foreach (var i in pages0.Distinct().OrderByDescending(x => x)) FPDFPage_Delete(d.H, i);
            return Save(d.H);
        }
    }

    /// <summary>Phông chữ có dấu tiếng Việt trên máy (Windows: Arial / Segoe UI; Linux: DejaVu Sans).</summary>
    public static byte[]? FindFont()
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (var f in new[] { Path.Combine(win, "arial.ttf"), Path.Combine(win, "segoeui.ttf"), Path.Combine(win, "tahoma.ttf"), "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/TTF/DejaVuSans.ttf", "/usr/share/fonts/dejavu/DejaVuSans.ttf" })
            if (f.Length > 0 && File.Exists(f)) return File.ReadAllBytes(f);
        return null;
    }

    public sealed class StampOpts
    {
        public bool Number; public string NumFmt = "Trang {n}/{N}", NumPos = "bc"; public int NumStart = 1;
        public bool Watermark; public string WmText = ""; public double WmSize = 72, WmOpacity = 0.2; public uint WmColor = 0xC62828;
        public (int W, int H, byte[] Bgra)? Image; public string ImgPos = "br", ImgPages = "all"; public double ImgWidthPct = 22;
    }

    /// <summary>Đánh số trang / chữ chìm chéo trang / hình đóng dấu (con dấu, chữ ký).</summary>
    public static byte[] Stamp(byte[] pdf, string name, StampOpts o)
    {
        lock (Gate)
        {
            Init(); using var d = new Doc(pdf, name); var n = d.Pages;
            var fontData = (o.Number || o.Watermark) ? FindFont() ?? throw new InvalidOperationException("Không tìm thấy phông chữ tiếng Việt trên máy (Arial / DejaVu Sans).") : null;
            var font = fontData is null ? IntPtr.Zero : FPDFText_LoadFont(d.H, fontData, (uint)fontData.Length, 2, 1);
            if (fontData is not null && font == IntPtr.Zero) throw new InvalidOperationException("Không nạp được phông chữ.");
            try
            {
                for (var i = 0; i < n; i++)
                {
                    var page = FPDF_LoadPage(d.H, i); if (page == IntPtr.Zero) continue;
                    try
                    {
                        double w = FPDF_GetPageWidthF(page), h = FPDF_GetPageHeightF(page);
                        if (o.Watermark && o.WmText.Length > 0)
                        {
                            var t = Text(d.H, font, o.WmText, (float)o.WmSize, o.WmColor, (uint)Math.Clamp(o.WmOpacity * 255, 10, 255));
                            FPDFPageObj_GetBounds(t, out var l0, out var b0, out var r0, out var t0);
                            var tw = r0 - l0; var diag = Math.Sqrt(w * w + h * h); var k = tw > diag * 0.8 ? diag * 0.8 / tw : 1;
                            var a = Math.Atan2(h, w); double cos = Math.Cos(a) * k, sin = Math.Sin(a) * k;
                            // đặt tâm chữ vào tâm trang rồi xoay theo đường chéo
                            double cx = (l0 + r0) / 2, cy = (b0 + t0) / 2;
                            FPDFPageObj_Transform(t, 1, 0, 0, 1, -cx, -cy);
                            FPDFPageObj_Transform(t, cos, sin, -sin, cos, w / 2, h / 2);
                            FPDFPage_InsertObject(page, t);
                        }
                        if (o.Image is { } img && (o.ImgPages == "all" || (o.ImgPages == "first" && i == 0) || (o.ImgPages == "last" && i == n - 1)))
                        {
                            var bmp = FPDFBitmap_Create(img.W, img.H, 1);
                            try
                            {
                                var buf = FPDFBitmap_GetBuffer(bmp); var stride = FPDFBitmap_GetStride(bmp);
                                for (var y = 0; y < img.H; y++) Marshal.Copy(img.Bgra, y * img.W * 4, buf + y * stride, img.W * 4);
                                var obj = FPDFPageObj_NewImageObj(d.H);
                                FPDFImageObj_SetBitmap(new[] { page }, 1, obj, bmp);
                                double iw = w * Math.Clamp(o.ImgWidthPct, 3, 100) / 100, ih = iw * img.H / img.W, mg = 28;
                                double x = o.ImgPos.EndsWith('c') ? (w - iw) / 2 : o.ImgPos.EndsWith('r') ? w - iw - mg : mg;
                                double yy = o.ImgPos == "c" ? (h - ih) / 2 : o.ImgPos.StartsWith('b') ? mg : h - ih - mg;
                                FPDFImageObj_SetMatrix(obj, iw, 0, 0, ih, x, yy);
                                FPDFPage_InsertObject(page, obj);
                            }
                            finally { FPDFBitmap_Destroy(bmp); }
                        }
                        if (o.Number)
                        {
                            var txt = o.NumFmt.Replace("{n}", (o.NumStart + i).ToString()).Replace("{N}", (o.NumStart + n - 1).ToString());
                            var t = Text(d.H, font, txt, 10, 0x1A1A1A, 255);
                            FPDFPageObj_GetBounds(t, out var l0, out var b0, out var r0, out _);
                            var tw = r0 - l0; const double mg = 24;
                            double x = o.NumPos.EndsWith('c') ? (w - tw) / 2 : o.NumPos.EndsWith('r') ? w - tw - mg : mg, y = o.NumPos.StartsWith('b') ? mg : h - mg - 10;
                            FPDFPageObj_Transform(t, 1, 0, 0, 1, x - l0, y - b0);
                            FPDFPage_InsertObject(page, t);
                        }
                        FPDFPage_GenerateContent(page);
                    }
                    finally { FPDF_ClosePage(page); }
                }
                return Save(d.H);
            }
            finally { if (font != IntPtr.Zero) FPDFFont_Close(font); }
        }
    }

    private static IntPtr Text(IntPtr doc, IntPtr font, string s, float size, uint rgb, uint alpha)
    {
        var t = FPDFPageObj_CreateTextObj(doc, font, size);
        FPDFText_SetText(t, Encoding.Unicode.GetBytes(s + "\0"));
        FPDFPageObj_SetFillColor(t, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255, alpha);
        return t;
    }

    /// <summary>Các trang gần như trắng (tỉ lệ điểm mực &lt; <paramref name="threshold"/>; vẽ ở 40 dpi).</summary>
    public static List<int> BlankPages(byte[] pdf, string name, double threshold, Action<int, int>? progress = null)
    {
        int n; lock (Gate) { Init(); using var d = new Doc(pdf, name); n = d.Pages; }
        var outp = new List<int>();
        for (var i = 0; i < n; i++)
        {
            progress?.Invoke(i + 1, n);
            var (w, h, rgba) = RenderRgbaDpi(pdf, i, 40);
            long ink = 0; for (var p = 0; p < rgba.Length; p += 4) if (rgba[p] < 200 || rgba[p + 1] < 200 || rgba[p + 2] < 200) ink++;
            if ((double)ink / Math.Max(1, w * h) < threshold) outp.Add(i);
        }
        return outp;
    }

    /// <summary>Vẽ trang theo dpi → RGBA + kích thước trang (point).</summary>
    public static (int W, int H, byte[] Rgba) RenderRgbaDpi(byte[] pdf, int index, double dpi)
    {
        double pw, ph;
        lock (Gate) { Init(); using var d = new Doc(pdf, "PDF"); var p = FPDF_LoadPage(d.H, index); try { pw = FPDF_GetPageWidthF(p); ph = FPDF_GetPageHeightF(p); } finally { FPDF_ClosePage(p); } }
        var maxSide = (int)Math.Round(Math.Max(pw, ph) * dpi / 72);
        return RenderRgba(pdf, index, maxSide);
    }

    public static (double W, double H) PageSize(byte[] pdf, int index)
    {
        lock (Gate) { Init(); using var d = new Doc(pdf, "PDF"); var p = FPDF_LoadPage(d.H, index); try { return (FPDF_GetPageWidthF(p), FPDF_GetPageHeightF(p)); } finally { FPDF_ClosePage(p); } }
    }
}
