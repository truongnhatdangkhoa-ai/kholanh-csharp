using System.Globalization;
using System.Text.Json;

namespace KhoLanh.Core.Services;

/// <summary>
/// Lỗi kết nối GẦN NHẤT của các dịch vụ ngoài (Tài khoản dùng chung Google Sheet, Web App điện thoại) — lưu trong cài đặt của máy,
/// lần gọi thành công thì xóa. Trợ lý hệ thống đọc lại để báo cấu hình hỏng (trước đây lỗi chỉ hiện lúc đó rồi mất).
/// Giá trị settings: JSON {"at": ISO, "msg": "...", "kind": ""|"phone-script"|"auth"}.
/// </summary>
public static class ConnErrorLog
{
    public const string Acct = "acct_last_error", Phone = "phone_last_error";
    /// <summary>Đường dẫn Tài khoản dùng chung trỏ nhầm Web App của app điện thoại.</summary>
    public const string KindPhoneScript = "phone-script";
    /// <summary>Web App từ chối mã truy cập (vd PC_TOKEN sai).</summary>
    public const string KindAuth = "auth";

    public sealed record Entry(DateTime At, string Msg, string Kind);

    public static void Note(SettingsService s, string key, string msg, string kind = "", DateTime? at = null)
    {
        try
        {
            var m = msg.Length > 600 ? msg[..600] : msg;
            s.Set(key, JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["at"] = (at ?? DateTime.Now).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture), ["msg"] = m, ["kind"] = kind,
            }));
        }
        catch { /* ghi nhật ký lỗi không được làm hỏng thao tác chính */ }
    }

    /// <summary>Xóa lỗi đã ghi (gọi khi thành công) — chỉ ghi CSDL khi đang có lỗi.</summary>
    public static void Clear(SettingsService s, string key)
    {
        try { if (s.Get(key).Length > 0) s.Set(key, ""); } catch { }
    }

    public static Entry? Read(SettingsService s, string key)
    {
        try
        {
            var raw = s.Get(key);
            if (raw.Length == 0) return null;
            var d = JsonSerializer.Deserialize<Dictionary<string, string>>(raw);
            if (d is null || !d.TryGetValue("msg", out var msg) || msg.Length == 0) return null;
            var at = DateTime.TryParseExact(d.GetValueOrDefault("at") ?? "", "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : default;
            return new Entry(at, msg, d.GetValueOrDefault("kind") ?? "");
        }
        catch { return null; }
    }
}
