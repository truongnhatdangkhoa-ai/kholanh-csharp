namespace KhoLanh.Core.Services;

/// <summary>
/// Nhớ mật khẩu / tự động đăng nhập trên máy này. Mật khẩu được mã hóa DPAPI theo tài khoản Windows (máy khác, người khác
/// không giải được). Lúc tự đăng nhập vẫn kiểm tra lại với danh sách tài khoản mới nhất — đổi mật khẩu / khóa thì hết tự vào.
/// </summary>
public class LoginMemory
{
    private const string KeyUser = "login_user", KeyPass = "login_pass", KeyAuto = "login_auto";
    private readonly SettingsService _settings;
    public LoginMemory(SettingsService settings) { _settings = settings; }

    public string Username => _settings.Get(KeyUser);
    public string Password => Secret.Unprotect(_settings.Get(KeyPass));
    public bool Remember => Password.Length > 0;
    public bool Auto => Remember && _settings.Get(KeyAuto) == "1";

    /// <summary>Gọi sau khi đăng nhập đúng. Không nhớ → chỉ giữ tên đăng nhập.</summary>
    public void Save(string username, string password, bool remember, bool auto)
    {
        _settings.Set(KeyUser, username.Trim());
        _settings.Set(KeyPass, remember || auto ? Secret.Protect(password) : "");
        _settings.Set(KeyAuto, auto ? "1" : "0");
    }

    /// <summary>Mật khẩu đã nhớ không còn đúng: bỏ nhớ.</summary>
    public void Forget()
    {
        _settings.Set(KeyPass, "");
        _settings.Set(KeyAuto, "0");
    }
}
