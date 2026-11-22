using System.Security.Cryptography;
using System.Text;

namespace KhoLanh.Core.Services;

/// <summary>Cất mã bí mật (token, mã kết nối) trong cài đặt: Windows mã hóa bằng DPAPI theo tài khoản Windows.</summary>
public static class Secret
{
    public static string Protect(string s)
    {
        if (s.Length == 0) return "";
        if (OperatingSystem.IsWindows())
            return "dpapi:" + Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s), null, DataProtectionScope.CurrentUser));
        return "plain:" + s;
    }

    public static string Unprotect(string s)
    {
        try
        {
            if (s.StartsWith("dpapi:") && OperatingSystem.IsWindows())
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s[6..]), null, DataProtectionScope.CurrentUser));
            if (s.StartsWith("plain:")) return s[6..];
        }
        catch { /* mã hỏng / máy khác: coi như chưa có */ }
        return "";
    }
}
