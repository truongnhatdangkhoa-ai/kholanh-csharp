using KhoLanh.Core;
using KhoLanh.Core.Lan;

namespace KhoLanh.Server;

/// <summary>
/// Máy chủ LAN KHÔNG GIAO DIỆN (Linux / Google Cloud VPS): giữ dữ liệu chung, các máy trong kho (máy con) nối vào qua mạng, kênh mã hóa AES-GCM.
/// Dùng lại đúng LanService của app. Chạy bằng systemd / Docker — xem docs/MAY-CHU-GOOGLE-CLOUD.md.
/// </summary>
public static class Program
{
    private const string Usage = """
        kholanh-server — máy chủ LAN Kho Lạnh (không giao diện)

        Dùng:  kholanh-server [tùy chọn]
          --data <thư mục>     Nơi lưu dữ liệu (kho.db). Mặc định: $KHOLANH_DATA hoặc ./data
          --role chu|duphong   Máy chủ chính (mặc định) hoặc máy dự phòng ($KHOLANH_ROLE)
          --port <số>          Cổng (mặc định 47820, $KHOLANH_PORT)
          --public <host:port[;host:port]>  Địa chỉ CÔNG KHAI máy con dùng để nối (IP tĩnh / tên miền của VPS) ($KHOLANH_PUBLIC)
          --code <mã LAN>      Mã LAN của máy chủ chính (bắt buộc khi --role duphong) ($KHOLANH_CODE)
          --name <tên>         Tên hiển thị của máy ($KHOLANH_NAME)
          --allow-plain        Cho phép máy con bản cũ chưa mã hóa (mặc định: BẮT BUỘC mã hóa)
          --show-code          In mã LAN (chứa khóa bí mật!) rồi thoát
          --help               Xem hướng dẫn này
        """;

    public static async Task<int> Main(string[] args)
    {
        var opt = Parse(args);
        if (opt.ContainsKey("help")) { Console.WriteLine(Usage); return 0; }
        try
        {
            var data = Path.GetFullPath(Get(opt, "data", "KHOLANH_DATA") ?? "data");
            Directory.CreateDirectory(data);
            var role = (Get(opt, "role", "KHOLANH_ROLE") ?? "chu").ToLowerInvariant();
            var pub = Get(opt, "public", "KHOLANH_PUBLIC");
            int? port = int.TryParse(Get(opt, "port", "KHOLANH_PORT"), out var pp) ? pp : null;
            var app = new AppServices(Path.Combine(data, "kho.db"));
            LanService.ConfigureHeadless(app.Settings, role, Get(opt, "code", "KHOLANH_CODE"), port, pub, !opt.ContainsKey("allow-plain"), Get(opt, "name", "KHOLANH_NAME") ?? "VPS " + Environment.MachineName);
            app.ReloadLan(start: false);
            if (opt.ContainsKey("show-code")) { Console.WriteLine(app.Lan.ConnectionCode()); return 0; }
            if (pub is null) Log("CẢNH BÁO: chưa đặt --public — máy con sẽ nhận địa chỉ nội bộ của VPS, không nối được từ ngoài.");

            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();
            System.Runtime.Loader.AssemblyLoadContext.Default.Unloading += _ => stop.Cancel();
            app.Lan.Start();
            Log($"Khởi động: vai {LanService.RoleLabel(app.Lan.Role)} · cổng {app.Lan.Port} · dữ liệu {data} · mã hóa {(app.Settings.Get("lan_enc") == "1" ? "BẮT BUỘC" : "tùy chọn")}");
            string last = "";
            while (!stop.IsCancellationRequested)
            {
                var st = $"{(app.Lan.Serving ? "đang phục vụ" : "chờ")} · epoch {app.Lan.Epoch}" + (app.Lan.ListenError.Length > 0 ? " · " + app.Lan.ListenError : "");
                if (st != last) { Log(st); last = st; }
                try { await Task.Delay(TimeSpan.FromSeconds(5), stop.Token); } catch (OperationCanceledException) { }
            }
            Log("Đang dừng…");
            app.Lan.Dispose();
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine("LỖI: " + e.Message); return 1; }
    }

    private static void Log(string s) => Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {s}");
    private static string? Get(Dictionary<string, string> o, string k, string env) => o.TryGetValue(k, out var v) && v.Length > 0 ? v : (Environment.GetEnvironmentVariable(env) is { Length: > 0 } e ? e : null);
    private static Dictionary<string, string> Parse(string[] a)
    {
        var d = new Dictionary<string, string>();
        for (var i = 0; i < a.Length; i++)
        {
            if (!a[i].StartsWith("--")) continue;
            var k = a[i][2..];
            if (k is "help" or "allow-plain" or "show-code") d[k] = "1";
            else if (i + 1 < a.Length) d[k] = a[++i];
        }
        return d;
    }
}
