using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace KhoLanh.App.Controls;

/// <summary>
/// Vẽ 1 trang PDF thành ảnh bằng PDFium (thư viện của Chrome, gói sẵn trong exe — không cần cài gì).
/// PDFium không an toàn đa luồng → mọi lời gọi đi qua 1 khóa.
/// </summary>
public static class PdfRender
{
    private const string Lib = "pdfium";
    [DllImport(Lib)] private static extern void FPDF_InitLibrary();
    [DllImport(Lib)] private static extern IntPtr FPDF_LoadMemDocument(IntPtr data, int size, string? password);
    [DllImport(Lib)] private static extern void FPDF_CloseDocument(IntPtr doc);
    [DllImport(Lib)] private static extern int FPDF_GetPageCount(IntPtr doc);
    [DllImport(Lib)] private static extern IntPtr FPDF_LoadPage(IntPtr doc, int index);
    [DllImport(Lib)] private static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Lib)] private static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport(Lib)] private static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport(Lib)] private static extern IntPtr FPDFBitmap_Create(int width, int height, int alpha);
    [DllImport(Lib)] private static extern void FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Lib)] private static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Lib)] private static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(Lib)] private static extern void FPDFBitmap_Destroy(IntPtr bitmap);
    [DllImport(Lib)] private static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page, int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    private static readonly object Gate = new();
    private static bool _init;

    /// <summary>Vẽ trang (đánh số từ 0) rộng widthPx điểm ảnh. Trả về ảnh BGRA; lỗi (thiếu thư viện, PDF hỏng) → ném lỗi.</summary>
    public static WriteableBitmap Render(byte[] pdf, int pageIndex, int widthPx)
    {
        lock (Gate)
        {
            if (!_init) { FPDF_InitLibrary(); _init = true; }
            var mem = Marshal.AllocHGlobal(pdf.Length);
            try
            {
                Marshal.Copy(pdf, 0, mem, pdf.Length);
                var doc = FPDF_LoadMemDocument(mem, pdf.Length, null);
                if (doc == IntPtr.Zero) throw new InvalidOperationException("Không mở được file PDF.");
                try
                {
                    if (pageIndex < 0 || pageIndex >= FPDF_GetPageCount(doc)) throw new ArgumentOutOfRangeException(nameof(pageIndex));
                    var page = FPDF_LoadPage(doc, pageIndex);
                    if (page == IntPtr.Zero) throw new InvalidOperationException("Không mở được trang PDF.");
                    try
                    {
                        double pw = FPDF_GetPageWidthF(page), ph = FPDF_GetPageHeightF(page);
                        var w = Math.Max(16, widthPx); var h = Math.Max(16, (int)Math.Round(w * ph / pw));
                        var bmp = FPDFBitmap_Create(w, h, 1);
                        if (bmp == IntPtr.Zero) throw new InvalidOperationException("Không đủ bộ nhớ để vẽ trang PDF.");
                        try
                        {
                            FPDFBitmap_FillRect(bmp, 0, 0, w, h, 0xFFFFFFFF);
                            FPDF_RenderPageBitmap(bmp, page, 0, 0, w, h, 0, 0x01); // FPDF_ANNOT
                            var stride = FPDFBitmap_GetStride(bmp); var buf = FPDFBitmap_GetBuffer(bmp);
                            var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
                            using (var fb = wb.Lock())
                            {
                                var row = new byte[w * 4];
                                for (int y = 0; y < h; y++)
                                {
                                    Marshal.Copy(buf + y * stride, row, 0, row.Length);
                                    Marshal.Copy(row, 0, fb.Address + y * fb.RowBytes, row.Length);
                                }
                            }
                            return wb;
                        }
                        finally { FPDFBitmap_Destroy(bmp); }
                    }
                    finally { FPDF_ClosePage(page); }
                }
                finally { FPDF_CloseDocument(doc); }
            }
            finally { Marshal.FreeHGlobal(mem); }
        }
    }
}
