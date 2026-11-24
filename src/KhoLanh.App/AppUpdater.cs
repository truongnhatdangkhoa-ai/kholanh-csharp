using System.Diagnostics;
using System.Text.Json;
using Avalonia.Threading;
using KhoLanh.Core.Services;

namespace KhoLanh.App;

/// <summary>
/// Cập nhật KhoLanh.exe tại chỗ, không cần cài lại:
///  1. Tải ngầm bản mới về cạnh exe: KhoLanh.exe.moi (+ KhoLanh.exe.moi.json ghi số phiên bản), đã kiểm SHA-256.
///  2. Áp dụng (bấm "Cập nhật" hoặc tự động lần mở app kế tiếp): đổi tên exe đang chạy → KhoLanh.exe.cu
///     (Windows cho đổi tên file đang chạy), đưa bản mới vào đúng tên KhoLanh.exe, mở lại app rồi thoát.
///  3. KhoLanh.exe.cu giữ lại (1 file, ẩn, bị thay ở lần cập nhật sau) để "Quay lại bản trước".
/// Dữ liệu ở %LOCALAPPDATA%\KhoLanhCS không bị đụng tới.
/// </summary>
public static class AppUpdater
{
    public static string? ExePath => Environment.ProcessPath is { } p && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : null;
    private static string Staged => ExePath + ".moi";
    private static string StagedMeta => ExePath + ".moi.json";
    private static string Previous => ExePath + ".cu";

    /// <summary>Phiên bản đang chờ áp dụng (đã tải xong), hoặc null.</summary>
    public static Version? StagedVersion
    {
        get
        {
            if (ExePath is null || !File.Exists(Staged) || !File.Exists(StagedMeta)) return null;
            try
            {
                using var d = JsonDocument.Parse(File.ReadAllText(StagedMeta));
                return Version.TryParse(d.RootElement.GetProperty("version").GetString(), out var v) && v > UpdateService.Current ? v : null;
            }
            catch { return null; }
        }
    }

    public static bool HasPrevious => ExePath is not null && File.Exists(Previous);

    /// <summary>Có ghi được vào thư mục chứa exe không (VD đặt trong Program Files thì không).</summary>
    public static bool CanWriteHere()
    {
        if (ExePath is null || !OperatingSystem.IsWindows()) return false;
        try
        {
            var probe = Path.Combine(Path.GetDirectoryName(ExePath)!, ".kholanh-thu-ghi");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Dọn file tạm của lần tải dở / bản chờ đã cũ. Bản trước (KhoLanh.exe.cu, chỉ 1 file, bị thay ở mỗi lần cập nhật) được ẩn đi.</summary>
    public static void Cleanup()
    {
        if (ExePath is null) return;
        HidePrevious();
        try { File.Delete(Staged + ".tai"); } catch { }
        if (File.Exists(Staged) && StagedVersion is null)
        {
            try { File.Delete(Staged); File.Delete(StagedMeta); } catch { }
        }
    }

    /// <summary>Ẩn KhoLanh.exe.cu (vẫn giữ để "Quay lại bản trước") cho thư mục gọn, khỏi nhầm với exe đang dùng.</summary>
    public static void HidePrevious()
    {
        if (ExePath is null || !OperatingSystem.IsWindows() || !File.Exists(Previous)) return;
        try { var a = File.GetAttributes(Previous); if (!a.HasFlag(FileAttributes.Hidden)) File.SetAttributes(Previous, a | FileAttributes.Hidden); } catch { }
    }

    // ---------------- tải ngầm ----------------

    private static bool _busy;
    public static event Action<Version>? Ready;      // có bản mới đã tải xong, sẵn sàng áp dụng
    public static event Action<string>? Progress;    // trạng thái để hiện cho người dùng

    /// <summary>Hỏi GitHub; có bản mới thì tải về chờ áp dụng. Trả về phiên bản đã sẵn sàng (hoặc null).</summary>
    public static async Task<Version?> CheckAndStageAsync(bool quiet)
    {
        if (ExePath is null || !OperatingSystem.IsWindows()) throw new BusinessException("Chỉ cập nhật được khi chạy KhoLanh.exe trên Windows.");
        if (StagedVersion is { } already) { Ready?.Invoke(already); return already; }
        if (_busy) return null;
        _busy = true;
        try
        {
            var svc = App.Services.Updates;
            if (!quiet) Progress?.Invoke("Đang hỏi GitHub…");
            var info = await svc.CheckAsync();
            if (info is null) { if (!quiet) Progress?.Invoke($"Đang dùng bản mới nhất ({UpdateService.Current})."); return null; }
            if (!CanWriteHere()) throw new BusinessException($"Có bản {info.Version} nhưng không ghi được vào thư mục chứa KhoLanh.exe. Hãy chép KhoLanh.exe ra thư mục thường (VD Desktop\\KhoLanh) rồi mở lại.");
            var last = -1;
            await svc.DownloadAsync(info, Staged, new Progress<double>(p =>
            {
                var pct = (int)(p * 100);
                if (pct / 5 != last / 5) { last = pct; Progress?.Invoke($"Đang tải bản {info.Version}: {pct}%"); }
            }));
            File.WriteAllText(StagedMeta, JsonSerializer.Serialize(new { version = info.Version.ToString(), tag = info.Tag, notes = info.Notes, at = DateTime.Now }));
            Progress?.Invoke($"Đã tải xong bản {info.Version} — bấm Cập nhật để dùng ngay (hoặc tự áp dụng lần mở app sau).");
            Ready?.Invoke(info.Version);
            return info.Version;
        }
        finally { _busy = false; }
    }

    /// <summary>Tự hỏi lúc mở app và mỗi 30 phút (nếu bật tự động). Lỗi mạng bỏ qua, không làm phiền.</summary>
    public static void StartAuto()
    {
        if (ExePath is null || !OperatingSystem.IsWindows()) return;
        async void Tick()
        {
            try { if (App.Services.Updates.Auto) await CheckAndStageAsync(quiet: true); }
            catch { /* tự động: lỗi thì im lặng, lần sau hỏi lại */ }
        }
        DispatcherTimer.RunOnce(Tick, TimeSpan.FromSeconds(20));
        var t = new DispatcherTimer { Interval = TimeSpan.FromMinutes(30) };
        t.Tick += (_, _) => Tick();
        t.Start();
    }

    // ---------------- áp dụng / quay lại ----------------

    /// <summary>
    /// Đưa bản đã tải vào chỗ KhoLanh.exe rồi mở lại app. True = đã giao việc cho tiến trình phụ, nơi gọi phải thoát app ngay
    /// (gọi <see cref="ExitForUpdate"/>).
    /// KHÔNG đổi tên exe khi app còn chạy: bản .exe một-file đọc tiếp mã của chính nó từ đường dẫn exe, đổi file dưới chân
    /// làm app lỗi (hộp "Có lỗi xảy ra" trống). Tiến trình phụ (PowerShell ẩn) chờ app thoát hẳn rồi mới đổi file và mở lại.
    /// </summary>
    public static bool ApplyAndRestart()
    {
        if (StagedVersion is null || ExePath is null) return false;
        if (!CanWriteHere()) throw new BusinessException("Không ghi được vào thư mục chứa KhoLanh.exe — chép KhoLanh.exe ra thư mục thường (VD Desktop\\KhoLanh) rồi mở lại.");
        StartHelper(rollback: false);
        return true;
    }

    /// <summary>Đổi chỗ bản đang dùng với bản trước (KhoLanh.exe.cu) rồi mở lại — cũng do tiến trình phụ làm sau khi app thoát.</summary>
    public static bool RollbackAndRestart()
    {
        if (!HasPrevious) return false;
        StartHelper(rollback: true);
        return true;
    }

    /// <summary>Thoát app để tiến trình phụ thay exe; nếu sau 5 giây còn kẹt (WebView2…) thì buộc thoát.</summary>
    public static void ExitForUpdate()
    {
        (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        _ = Task.Delay(5000).ContinueWith(_ => Environment.Exit(0));
    }

    internal static string HelperScript(string exe, int pid, bool rollback, string log) => UpdateScript.Build(exe, pid, rollback, log);

    /// <summary>
    /// Gọi lúc mở app (exe còn nguyên): nạp sẵn System.Diagnostics.Process và biên dịch sẵn RunHelper. Bản một-file chỉ nạp thư viện
    /// từ chính file exe khi cần lần đầu — nếu đến lúc bấm Cập nhật file exe đã bị cửa sổ khác thay thì sẽ lỗi FileNotFoundException.
    /// </summary>
    public static void Warmup()
    {
        try
        {
            _ = Process.GetCurrentProcess().Id;
            _ = OtherInstances();
            var m = typeof(AppUpdater).GetMethod(nameof(RunHelper), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (m is not null) System.Runtime.CompilerServices.RuntimeHelpers.PrepareMethod(m.MethodHandle);
        }
        catch { /* chỉ là chuẩn bị trước, lỗi thì bỏ qua */ }
    }

    /// <summary>Số cửa sổ KhoLanh KHÁC đang chạy cùng file exe (không đọc được đường dẫn — VD tài khoản Windows khác — vẫn tính, cho chắc).</summary>
    public static int OtherInstances()
    {
        if (ExePath is not { } exe) return 0;
        var me = Environment.ProcessId; var n = 0;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            using (p)
            {
                if (p.Id == me) continue;
                string? path = null;
                try { path = p.MainModule?.FileName; } catch { /* không có quyền đọc */ }
                if (path is null || string.Equals(Path.GetFullPath(path), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase)) n++;
            }
        }
        return n;
    }

    /// <summary>Kiểm tra cửa sổ khác rồi giao việc cho tiến trình phụ; lỗi nạp thư viện (exe vừa bị thay khi app đang mở) → báo dễ hiểu.</summary>
    private static void StartHelper(bool rollback)
    {
        var others = OtherInstances();
        if (others > 0) throw new BusinessException($"Còn {others} cửa sổ KhoLanh khác đang mở trên máy này (có thể ở tài khoản Windows khác). Đóng hết các cửa sổ đó rồi bấm Cập nhật lại — thay file khi cửa sổ khác còn chạy sẽ làm cửa sổ đó lỗi.");
        try { RunHelper(ExePath!, rollback); }
        catch (Exception e) when (e is FileNotFoundException or FileLoadException)
        {
            throw new BusinessException("File KhoLanh.exe vừa bị thay (thường do một cửa sổ KhoLanh khác đã cập nhật) trong khi cửa sổ này còn mở. Hãy đóng cửa sổ này rồi mở lại app — bản mới sẽ được dùng.");
        }
    }

    private static void RunHelper(string exe, bool rollback)
    {
        var log = Path.Combine(Path.GetDirectoryName(exe)!, "loi-cap-nhat.log");
        var script = HelperScript(exe, Environment.ProcessId, rollback, log);
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
        var ps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
        var psi = new ProcessStartInfo(File.Exists(ps) ? ps : "powershell.exe",
            $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand {encoded}")
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        Process.Start(psi);
    }
}
