using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using KhoLanh.App.Views;

namespace KhoLanh.App;

/// <summary>
/// Chế độ tự kiểm tra (KhoLanh.exe --tu-kiem-module kq.json): mở 2 module gốc trong WebView2 với dữ liệu tạm,
/// kiểm tra cầu nối dbAPI (ghi / đọc / authAPI), thư viện Excel 0.20.3 đã nạp, rồi ghi kết quả JSON và thoát. Dùng trên máy Windows / CI.
/// </summary>
internal static class SelfTest
{
    public static void Run(IClassicDesktopStyleApplicationLifetime desktop, string outFile)
    {
        var log = new StringBuilder();
        var ok = true;
        void Done()
        {
            File.WriteAllText(outFile, "{\"ok\":" + (ok ? "true" : "false") + ",\"log\":" + System.Text.Json.JsonSerializer.Serialize(log.ToString()) + "}");
            desktop.Shutdown(ok ? 0 : 1);
        }
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "kholanh-tukiem-" + Guid.NewGuid().ToString("N"));
            App.InitServices(dir);
            App.Services.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
            App.Services.Auth.Login("admin", "matkhau1");
        }
        catch (Exception e) { ok = false; log.AppendLine("init: " + e.Message); Done(); return; }
        // Đọc + vẽ PDF (PdfPig + pdfium.dll gói trong exe) — module Kết quả kháng sinh
        try
        {
            var pdf = MiniPdf("Enro Cipro 23.MKS");
            var txt = string.Join(" ", Core.KhangSinh.PdfText.Read(pdf).SelectMany(p => p.Items).Select(t => t.Text));
            var bmp = Controls.PdfRender.Render(pdf, 0, 300);
            log.AppendLine($"pdf: chữ \"{txt}\", ảnh {bmp.PixelSize.Width}x{bmp.PixelSize.Height}");
            if (!txt.Contains("23.MKS") || bmp.PixelSize.Width != 300) ok = false;
        }
        catch (Exception e) { ok = false; log.AppendLine("pdf: " + e.GetType().Name + " " + e.Message); }

        var pages = new[] { "ton-kho-an-an.html", "ton-kho-gui.html" };
        var w = new Window { Title = "Tự kiểm tra module", Width = 1280, Height = 800 };
        var host = new Panel();
        w.Content = host;
        desktop.MainWindow = w;
        w.Show();
        var timeout = DispatcherTimer.RunOnce(() => { ok = false; log.AppendLine("quá thời gian 120 giây"); Done(); }, TimeSpan.FromSeconds(120));

        async Task Check(int i)
        {
            if (i >= pages.Length) { timeout.Dispose(); Done(); return; }
            var mv = new ModuleView(pages[i], pages[i], "");
            mv.ModuleLoaded += async (good, msg) =>
            {
                if (!good) { ok = false; log.AppendLine(pages[i] + ": " + msg); timeout.Dispose(); Done(); return; }
                await Task.Delay(1500);
                await mv.Host!.EvalAsync("""
                    window.__kt = null;
                    (async () => {
                      const out = {};
                      try {
                        if (window.klNapXlsx) await window.klNapXlsx();
                        out.dbAPI = typeof window.dbAPI;
                        out.xlsxDoc = window.XLSX_DOC && window.XLSX_DOC.version;
                        out.xlsxStyle = window.XLSX && window.XLSX.version;
                        out.auth = await window.authAPI.state();
                        const w = await window.dbAPI.writeSheet('kiem-tra', 'T1', [{ a: 1, b: 'Tôm sú', c: null }]);
                        const r = await window.dbAPI.readSheet('kiem-tra', 'T1');
                        out.write = w && w.ok; out.read = JSON.stringify(r);
                        const ws = await window.dbAPI.writeSheets('kiem-tra', { T1: null, T2: [] });
                        out.list = JSON.stringify(await window.dbAPI.listSheets('kiem-tra'));
                        out.title = document.title;
                      } catch (e) { out.error = String(e && e.stack || e); }
                      window.__kt = JSON.stringify(out);
                    })();
                    """);
                string res = "null";
                for (var k = 0; k < 60 && res == "null"; k++) { await Task.Delay(500); res = await mv.Host.EvalAsync("window.__kt"); }
                var json = res == "null" ? "" : System.Text.Json.JsonSerializer.Deserialize<string>(res) ?? "";
                log.AppendLine(pages[i] + ": " + json);
                var good2 = json.Contains("\"dbAPI\":\"object\"") && (json.Contains("\"xlsxDoc\":\"0.20.3\"") || (pages[i] == "ton-kho-gui.html" && json.Contains("\"xlsxStyle\":\"0.20.3\""))) && json.Contains("\"write\":true")
                            && json.Contains("Tôm sú") && json.Contains("[\\\"T2\\\"]") && json.Contains("quantri") && !json.Contains("\"error\"");
                if (!good2) ok = false;
                host.Children.Remove(mv);
                await Check(i + 1);
            };
            host.Children.Add(mv);
            mv.SetActive(true);
        }
        Dispatcher.UIThread.Post(async () => await Check(0));
    }

    private static byte[] MiniPdf(string text)
    {
        var content = $"BT /F1 12 Tf 72 720 Td ({text}) Tj ET";
        var objs = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>", "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream", "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        };
        var sb = new StringBuilder("%PDF-1.4\n"); var offs = new List<int>();
        for (int i = 0; i < objs.Length; i++) { offs.Add(sb.Length); sb.Append($"{i + 1} 0 obj\n{objs[i]}\nendobj\n"); }
        var x = sb.Length;
        sb.Append($"xref\n0 {objs.Length + 1}\n0000000000 65535 f \n");
        foreach (var o in offs) sb.Append($"{o:D10} 00000 n \n");
        sb.Append($"trailer\n<< /Size {objs.Length + 1} /Root 1 0 R >>\nstartxref\n{x}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(sb.ToString());
    }
}
