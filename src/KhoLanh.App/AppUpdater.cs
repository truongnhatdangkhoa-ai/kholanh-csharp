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
///  3. KhoLanh.exe.cu giữ lại để "Quay lại bản trước".
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

    /// <summary>Dọn file tạm của lần tải dở / bản chờ đã cũ.</summary>
    public static void Cleanup()
    {
        if (ExePath is null) return;
        try { File.Delete(Staged + ".tai"); } catch { }
        if (File.Exists(Staged) && StagedVersion is null)
        {
            try { File.Delete(Staged); File.Delete(StagedMeta); } catch { }
        }
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

    /// <summary>Đưa bản đã tải vào chỗ KhoLanh.exe rồi mở lại app. True = đã mở bản mới, nơi gọi phải thoát app ngay.</summary>
    public static bool ApplyAndRestart()
    {
        if (StagedVersion is null) return false;
        var exe = ExePath!;
        try { if (File.Exists(Previous)) File.Delete(Previous); }
        catch { /* .cu đang bị khóa: bỏ qua lần này */ return false; }
        File.Move(exe, Previous);
        try { File.Move(Staged, exe); }
        catch
        {
            File.Move(Previous, exe); // trả lại như cũ
            throw;
        }
        try { File.Delete(StagedMeta); } catch { }
        Restart(exe, "--da-cap-nhat");
        return true;
    }

    /// <summary>Đổi chỗ bản đang dùng với bản trước (KhoLanh.exe.cu) rồi mở lại.</summary>
    public static bool RollbackAndRestart()
    {
        if (!HasPrevious) return false;
        var exe = ExePath!;
        var tmp = exe + ".doi";
        try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        File.Move(exe, tmp);
        try { File.Move(Previous, exe); }
        catch { File.Move(tmp, exe); throw; }
        File.Move(tmp, Previous);
        try { File.Delete(Staged); File.Delete(StagedMeta); } catch { }
        Restart(exe, "--da-quay-lai");
        return true;
    }

    private static void Restart(string exe, string arg) =>
        Process.Start(new ProcessStartInfo(exe, arg) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! });
}
