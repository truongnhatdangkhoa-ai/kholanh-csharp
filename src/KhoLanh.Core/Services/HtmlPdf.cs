using System.Diagnostics;
using System.Runtime.InteropServices;

namespace KhoLanh.Core.Services;

/// <summary>
/// 1.194: tạo file PDF thẳng từ trang HTML in (không cần mở trình duyệt, không cần chọn máy in) —
/// dùng Microsoft Edge (có sẵn trên Windows 10 / 11) hoặc Chrome chạy ngầm ở chế độ "headless" (`--print-to-pdf`).
/// </summary>
public static class HtmlPdf
{
    /// <summary>Đường dẫn trình duyệt dùng để tạo PDF; null = không tìm thấy. Có thể ép bằng biến môi trường <c>KL_PDF_BROWSER</c>.</summary>
    public static string? FindBrowser()
    {
        var env = Environment.GetEnvironmentVariable("KL_PDF_BROWSER");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        var list = new List<string>();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var root in new[] { pf86, pf, local })
            {
                if (string.IsNullOrEmpty(root)) continue;
                list.Add(Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe"));
                list.Add(Path.Combine(root, "Google", "Chrome", "Application", "chrome.exe"));
            }
        }
        else
        {
            foreach (var n in new[] { "microsoft-edge", "microsoft-edge-stable", "google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "chrome" })
                foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
                    list.Add(Path.Combine(dir, n));
        }
        return list.FirstOrDefault(File.Exists);
    }

    /// <summary>Chuyển <paramref name="htmlPath"/> thành PDF A4 tại <paramref name="pdfPath"/>. Lỗi → <see cref="BusinessException"/> (tiếng Việt).</summary>
    public static void Convert(string htmlPath, string pdfPath, int timeoutSeconds = 90)
    {
        var browser = FindBrowser() ?? throw new BusinessException(
            "Máy này không có Microsoft Edge hoặc Google Chrome nên chưa tạo PDF trực tiếp được. Dùng nút “In phiếu chọn…” rồi chọn “Lưu dưới dạng PDF”.");
        var tmpProfile = Path.Combine(Path.GetTempPath(), "kholanh-pdf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpProfile);
        var dir = Path.GetDirectoryName(Path.GetFullPath(pdfPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmpPdf = Path.Combine(tmpProfile, "out.pdf");   // ghi ra thư mục tạm rồi chép đè đúng chỗ (file đích không bị xóa-tạo lại)
        try
        {
            var psi = new ProcessStartInfo(browser) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var a in new[]
            {
                "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-extensions",
                "--no-pdf-header-footer", "--print-to-pdf-no-header",   // tên cờ khác nhau theo đời trình duyệt, cờ lạ bị bỏ qua
                "--user-data-dir=" + tmpProfile,   // hồ sơ riêng: chạy được cả khi Edge / Chrome của người dùng đang mở
                "--print-to-pdf=" + tmpPdf,
            }) psi.ArgumentList.Add(a);
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) psi.ArgumentList.Add("--no-sandbox");
            psi.ArgumentList.Add(new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri);
            using var p = Process.Start(psi) ?? throw new BusinessException("Không chạy được trình duyệt để tạo PDF.");
            _ = p.StandardOutput.ReadToEndAsync(); _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutSeconds * 1000))
            {
                try { p.Kill(true); } catch { }
                throw new BusinessException("Tạo PDF quá lâu (hơn " + timeoutSeconds + " giây) nên đã dừng. Thử lại với ít phiếu hơn.");
            }
            // một số bản Chrome thoát trước khi ghi xong file → chờ thêm chút
            for (var i = 0; i < 50 && !(File.Exists(tmpPdf) && new FileInfo(tmpPdf).Length > 0); i++) Thread.Sleep(100);
            if (!File.Exists(tmpPdf) || new FileInfo(tmpPdf).Length == 0)
                throw new BusinessException("Không tạo được file PDF (trình duyệt không trả kết quả). Dùng nút “In phiếu chọn…” rồi chọn “Lưu dưới dạng PDF”.");
            GhiDe.Write(pdfPath, File.ReadAllBytes(tmpPdf));
        }
        finally { try { Directory.Delete(tmpProfile, true); } catch { } }
    }
}
