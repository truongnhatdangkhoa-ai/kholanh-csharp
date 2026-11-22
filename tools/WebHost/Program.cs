// Máy chủ kiểm tra (chỉ dùng khi phát triển / CI): phục vụ thư mục web/ và cầu nối /__bridge bằng đúng ModuleBridge của app,
// để chạy 2 module gốc trong Chromium (Playwright) và so kết quả với app cũ. App thật dùng WebView2, không dùng máy chủ này.
using System.Net;
using System.Text;
using KhoLanh.Core;

var root = Path.GetFullPath(args.Length > 0 ? args[0] : "web");
var port = args.Length > 1 ? int.Parse(args[1]) : 8766;
var dataDir = args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), "kholanh-webhost-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dataDir);
var s = new AppServices(Path.Combine(dataDir, "kho.db"));
if (!s.Auth.HasAnyUser()) s.Auth.CreateFirstAdmin("admin", "Quản trị", "matkhau1");
s.Auth.Login("admin", "matkhau1");
if (args.Length > 3 && File.Exists(args[3]))
{
    var r = s.Legacy.Import(args[3]);
    Console.WriteLine($"Đã nhập {r.Sheets} sheet, {r.Rows} dòng từ app cũ.");
}

var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    [".html"] = "text/html; charset=utf-8", [".js"] = "text/javascript; charset=utf-8", [".css"] = "text/css; charset=utf-8",
    [".svg"] = "image/svg+xml", [".png"] = "image/png", [".woff2"] = "font/woff2", [".json"] = "application/json", [".txt"] = "text/plain; charset=utf-8",
};
var l = new HttpListener();
l.Prefixes.Add($"http://127.0.0.1:{port}/");
l.Start();
Console.WriteLine($"WebHost {root} → http://127.0.0.1:{port}/ (dữ liệu: {dataDir})");
var gate = new object();
while (true)
{
    var ctx = await l.GetContextAsync();
    _ = Task.Run(async () =>
    {
        try
        {
            var path = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath);
            if (path == "/__bridge" && ctx.Request.HttpMethod == "POST")
            {
                using var rd = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                var body = await rd.ReadToEndAsync();
                string res; lock (gate) res = s.Bridge.Handle(body);
                var b = Encoding.UTF8.GetBytes(res);
                ctx.Response.ContentType = "application/json; charset=utf-8";
                await ctx.Response.OutputStream.WriteAsync(b);
            }
            else
            {
                var file = Path.GetFullPath(Path.Combine(root, path.TrimStart('/')));
                if (!file.StartsWith(root) || !File.Exists(file)) { ctx.Response.StatusCode = 404; }
                else
                {
                    ctx.Response.ContentType = types.GetValueOrDefault(Path.GetExtension(file), "application/octet-stream");
                    var b = await File.ReadAllBytesAsync(file);
                    await ctx.Response.OutputStream.WriteAsync(b);
                }
            }
        }
        catch (Exception e) { Console.Error.WriteLine(e.Message); }
        finally { ctx.Response.Close(); }
    });
}
