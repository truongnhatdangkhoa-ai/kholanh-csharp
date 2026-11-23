using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using KhoLanh.Core.Scan;
using SkiaSharp;

namespace KhoLanh.App.Controls;

/// <summary>Cầu nối ảnh cho Scan tài liệu: giải mã (xoay đúng chiều theo EXIF), mã hoá JPEG, chuyển sang ảnh hiển thị.</summary>
public static class ScanImaging
{
    public static readonly string[] ImageExt = { "jpg", "jpeg", "png", "bmp", "webp", "gif", "heic", "heif" };

    /// <summary>File ảnh → RgbaImage (đã xoay theo EXIF, nền trắng, cạnh dài ≤ maxSide).</summary>
    public static RgbaImage Decode(byte[] data, int maxSide = ScanCore.MaxSide)
    {
        using var codec = SKCodec.Create(new SKMemoryStream(data)) ?? throw new InvalidOperationException("Không đọc được ảnh này (định dạng không hỗ trợ?).");
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        var r = codec.GetPixels(info, bmp.GetPixels());
        if (r is not (SKCodecResult.Success or SKCodecResult.IncompleteInput)) throw new InvalidOperationException("Không đọc được ảnh này (" + r + ").");
        var img = new RgbaImage(info.Width, info.Height);
        Marshal.Copy(bmp.GetPixels(), img.Px, 0, img.Px.Length);
        ScanCore.FlattenOnWhite(img);
        img = codec.EncodedOrigin switch
        {
            SKEncodedOrigin.RightTop => ScanCore.Rotate(img, 90),
            SKEncodedOrigin.BottomRight => ScanCore.Rotate(img, 180),
            SKEncodedOrigin.LeftBottom => ScanCore.Rotate(img, 270),
            _ => img,
        };
        return ScanCore.Scale(img, maxSide);
    }

    public static byte[] EncodeJpeg(RgbaImage img, int quality = 92, bool gray = false)
    {
        var info = new SKImageInfo(img.W, img.H, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        Marshal.Copy(img.Px, 0, bmp.GetPixels(), img.Px.Length);
        if (gray)
        {
            using var g = new SKBitmap(new SKImageInfo(img.W, img.H, SKColorType.Gray8, SKAlphaType.Opaque));
            var px = new byte[img.W * img.H];
            for (int i = 0, p = 0; i < px.Length; i++, p += 4) px[i] = (byte)((img.Px[p] * 77 + img.Px[p + 1] * 150 + img.Px[p + 2] * 29) >> 8);
            Marshal.Copy(px, 0, g.GetPixels(), px.Length);
            using var gi = SKImage.FromBitmap(g); using var gd = gi.Encode(SKEncodedImageFormat.Jpeg, quality); return gd.ToArray();
        }
        using var im = SKImage.FromBitmap(bmp); using var d = im.Encode(SKEncodedImageFormat.Jpeg, quality);
        return d.ToArray();
    }

    public static WriteableBitmap ToBitmap(RgbaImage img)
    {
        var wb = new WriteableBitmap(new PixelSize(img.W, img.H), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Opaque);
        using var fb = wb.Lock();
        for (var y = 0; y < img.H; y++) Marshal.Copy(img.Px, y * img.W * 4, fb.Address + y * fb.RowBytes, img.W * 4);
        return wb;
    }
}
