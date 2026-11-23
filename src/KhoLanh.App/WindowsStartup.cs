using System.Runtime.Versioning;
using Microsoft.Win32;

namespace KhoLanh.App;

/// <summary>
/// Tự mở phần mềm khi đăng nhập Windows (mục "KhoLanh" trong HKCU\...\CurrentVersion\Run — theo từng tài khoản Windows, không cần quyền quản trị Windows).
/// Mở với tham số --tu-khoi-dong; nếu máy đã bật "Tự động đăng nhập" thì vào thẳng phần mềm.
/// </summary>
public static class WindowsStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run", Name = "KhoLanh";
    public const string Arg = "--tu-khoi-dong";

    public static bool Supported => OperatingSystem.IsWindows() && AppUpdater.ExePath is not null;
    public static bool LaunchedAtBoot => Array.IndexOf(Program.Args, Arg) >= 0;
    private static string Command => $"\"{AppUpdater.ExePath}\" {Arg}";

    public static bool IsOn()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try { return Read() is { Length: > 0 }; } catch { return false; }
    }

    public static void Set(bool on)
    {
        if (!OperatingSystem.IsWindows() || !Supported) throw new KhoLanh.Core.Services.BusinessException("Chỉ dùng được với bản KhoLanh.exe trên Windows.");
        Write(on);
    }

    /// <summary>Đã bật mà đường dẫn exe đổi (chép sang thư mục khác) → cập nhật lại lệnh mở. Gọi lúc mở app.</summary>
    public static void Refresh()
    {
        if (!OperatingSystem.IsWindows() || !Supported) return;
        try { if (Read() is { Length: > 0 } cur && !string.Equals(cur, Command, StringComparison.OrdinalIgnoreCase)) Write(true); } catch { }
    }

    [SupportedOSPlatform("windows")]
    private static string? Read()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(Name) as string;
    }

    [SupportedOSPlatform("windows")]
    private static void Write(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (on) k.SetValue(Name, Command); else k.DeleteValue(Name, false);
    }
}
