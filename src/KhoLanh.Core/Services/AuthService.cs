using System.Security.Cryptography;
using Dapper;
using KhoLanh.Core.Data;
using KhoLanh.Core.Models;

namespace KhoLanh.Core.Services;

public class AuthService
{
    private const int Iterations = 100_000;
    private readonly Database _db;
    private readonly Session _session;
    private readonly AuditService _audit;
    /// <summary>Dùng chung tài khoản qua Google Sheet (null = chỉ trong máy). Mọi thay đổi tài khoản ghi lên Sheet trước.</summary>
    public AccountSyncService? Sync { get; set; }

    public AuthService(Database db, Session session, AuditService audit)
    {
        _db = db; _session = session; _audit = audit;
    }

    public bool HasAnyUser()
    {
        using var c = _db.Open();
        return c.ExecuteScalar<long>("SELECT COUNT(*) FROM users") > 0;
    }

    /// <summary>Tạo tài khoản Quản trị đầu tiên (chỉ khi chưa có tài khoản nào).</summary>
    public User CreateFirstAdmin(string username, string fullName, string password)
    {
        if (HasAnyUser()) throw new BusinessException("Đã có tài khoản, không thể tạo Quản trị đầu tiên nữa.");
        var u = Insert(username, fullName, password, Roles.Admin);
        _audit.Log(u.Username, "Tạo Quản trị đầu tiên", u.Username);
        return u;
    }

    /// <summary>Kiểm tra mật khẩu của 1 tài khoản (không đăng nhập, không ghi nhật ký) — dùng khi bật tự đăng nhập.</summary>
    public bool CheckPassword(string username, string password)
    {
        using var c = _db.Open();
        var u = c.QuerySingleOrDefault<User>("SELECT * FROM users WHERE username=@username", new { username = username.Trim() });
        return u is not null && u.IsActive && Verify(password, u.Salt, u.PasswordHash);
    }

    public User Login(string username, string password)
    {
        using var c = _db.Open();
        var u = c.QuerySingleOrDefault<User>("SELECT * FROM users WHERE username=@username", new { username = username.Trim() });
        if (u is null || !Verify(password, u.Salt, u.PasswordHash))
        {
            _audit.Log(username.Trim(), "Đăng nhập sai", "");
            throw new BusinessException("Sai tên đăng nhập hoặc mật khẩu.");
        }
        if (!u.IsActive) throw new BusinessException("Tài khoản đã bị khóa. Liên hệ Quản trị.");
        _session.CurrentUser = u;
        _audit.Log(u.Username, "Đăng nhập", "");
        Sync?.AfterLogin(u, password);
        return u;
    }

    /// <summary>Đăng nhập bằng email Google đã xác minh (1.70): tìm tài khoản đang dùng được gán đúng email đó.</summary>
    public User LoginGoogle(string email)
    {
        email = GoogleSignIn.NormEmail(email);
        using var c = _db.Open();
        var list = c.Query<User>("SELECT * FROM users WHERE google_email=@email COLLATE NOCASE AND google_email<>''", new { email }).ToList();
        if (list.Count == 0)
        {
            _audit.Log(email, "Đăng nhập Google không có tài khoản", "");
            throw new BusinessException($"Tài khoản Google {email} chưa được gán cho tài khoản nào. Nhờ Quản trị gán ở Hệ thống › Tài khoản (ô Email Google).");
        }
        var u = list.FirstOrDefault(x => x.IsActive) ?? throw new BusinessException("Tài khoản gán với Google này đã bị khóa. Liên hệ Quản trị.");
        _session.CurrentUser = u;
        _audit.Log(u.Username, "Đăng nhập Google", email);
        Sync?.AfterLogout();   // không có mật khẩu: máy quản trị chưa mở khóa ghi tài khoản dùng chung trong phiên này
        return u;
    }

    /// <summary>Gán / bỏ email Google cho tài khoản (chỉ Quản trị); 1 email chỉ gán cho 1 tài khoản.</summary>
    public void SetGoogleEmail(long id, string email)
    {
        _session.RequireAdmin();
        email = GoogleSignIn.NormEmail(email);
        if (email.Length > 0 && !GoogleSignIn.LooksLikeEmail(email)) throw new BusinessException("Email Google không hợp lệ.");
        using var c = _db.Open();
        var u = c.QuerySingleOrDefault<User>("SELECT * FROM users WHERE id=@id", new { id }) ?? throw new BusinessException("Không tìm thấy tài khoản.");
        if ((u.GoogleEmail ?? "") == email) return;
        if (email.Length > 0 && c.QuerySingleOrDefault<string>("SELECT username FROM users WHERE google_email=@email COLLATE NOCASE AND id<>@id", new { email, id }) is { } other)
            throw new BusinessException($"Email {email} đã gán cho tài khoản {other}.");
        u.GoogleEmail = email;
        Sync?.BeforeSave(u);
        c.Execute("UPDATE users SET google_email=@email WHERE id=@id", new { id, email });
        _audit.Log(_session.Username, "Gán email Google", $"{u.Username}: {(email.Length == 0 ? "(bỏ)" : email)}");
        if (_session.CurrentUser?.Id == id) _session.CurrentUser.GoogleEmail = email;
    }

    public void Logout()
    {
        if (_session.CurrentUser is not null) _audit.Log(_session.Username, "Đăng xuất", "");
        _session.CurrentUser = null;
        Sync?.AfterLogout();
    }

    public List<User> ListUsers()
    {
        _session.RequireAdmin();
        using var c = _db.Open();
        return c.Query<User>("SELECT * FROM users ORDER BY username").ToList();
    }

    public User CreateUser(string username, string fullName, string password, string role, string? permissions = null)
    {
        _session.RequireAdmin();
        var clean = string.IsNullOrWhiteSpace(permissions) || role == Roles.Admin ? "" : Perm.Serialize(Perm.Parse(permissions));
        var u = Insert(username, fullName, password, role, clean);
        if (role == Roles.Admin) Sync?.Reseal(u.Username, password);
        _audit.Log(_session.Username, "Tạo tài khoản", $"{u.Username} ({Roles.Label(role)})");
        return u;
    }

    /// <summary>Lưu quyền chỉnh riêng (JSON {mục: mức}); chuỗi rỗng = theo vai trò.</summary>
    public void SetPermissions(long id, string permissions)
    {
        _session.RequireAdmin();
        var clean = string.IsNullOrWhiteSpace(permissions) ? "" : Perm.Serialize(Perm.Parse(permissions));
        using var c = _db.Open();
        var u = c.QuerySingleOrDefault<User>("SELECT * FROM users WHERE id=@id", new { id }) ?? throw new BusinessException("Không tìm thấy tài khoản.");
        if (u.Permissions == clean) return;
        u.Permissions = clean;
        Sync?.BeforeSave(u);
        c.Execute("UPDATE users SET permissions=@clean WHERE id=@id", new { id, clean });
        _audit.Log(_session.Username, "Phân quyền", $"{u.Username}: {(clean.Length == 0 ? "theo vai trò" : clean)}");
        if (_session.CurrentUser?.Id == id) _session.CurrentUser.Permissions = clean;
    }

    public void UpdateUser(long id, string fullName, string role, bool isActive)
    {
        _session.RequireAdmin();
        if (!Roles.All.Contains(role)) throw new BusinessException("Vai trò không hợp lệ.");
        using var c = _db.Open();
        var u = c.QuerySingleOrDefault<User>("SELECT * FROM users WHERE id=@id", new { id })
                ?? throw new BusinessException("Không tìm thấy tài khoản.");
        if (u.Id == _session.CurrentUser!.Id && (!isActive || role != Roles.Admin))
            throw new BusinessException("Không thể tự khóa hoặc tự hạ quyền tài khoản đang đăng nhập.");
        if (u.Role == Roles.Admin && (role != Roles.Admin || !isActive))
        {
            var admins = c.ExecuteScalar<long>("SELECT COUNT(*) FROM users WHERE role='admin' AND is_active=1 AND id<>@id", new { id });
            if (admins == 0) throw new BusinessException("Phải còn ít nhất một tài khoản Quản trị đang dùng.");
        }
        u.FullName = fullName.Trim(); u.Role = role; u.IsActive = isActive;
        if (role == Roles.Admin) u.Permissions = "";
        Sync?.BeforeSave(u);
        c.Execute("UPDATE users SET full_name=@fullName, role=@role, is_active=@isActive, permissions=@perms WHERE id=@id",
            new { id, fullName = fullName.Trim(), role, isActive, perms = u.Permissions });
        _audit.Log(_session.Username, "Sửa tài khoản", $"{u.Username}: {Roles.Label(role)}, {(isActive ? "đang dùng" : "khóa")}");
    }

    public void ResetPassword(long id, string newPassword)
    {
        _session.RequireAdmin();
        SetPassword(id, newPassword);
        _audit.Log(_session.Username, "Đặt lại mật khẩu", $"id {id}");
    }

    public void ChangeOwnPassword(string oldPassword, string newPassword)
    {
        _session.RequireLogin();
        using (var c = _db.Open())
        {
            var u = c.QuerySingle<User>("SELECT * FROM users WHERE id=@id", new { id = _session.CurrentUser!.Id });
            if (!Verify(oldPassword, u.Salt, u.PasswordHash)) throw new BusinessException("Mật khẩu hiện tại không đúng.");
        }
        SetPassword(_session.CurrentUser!.Id, newPassword);
        _audit.Log(_session.Username, "Đổi mật khẩu", "");
    }

    private void SetPassword(long id, string password)
    {
        ValidatePassword(password);
        var (hash, salt) = Hash(password);
        using var c = _db.Open();
        var u = c.QuerySingleOrDefault<User>("SELECT * FROM users WHERE id=@id", new { id }) ?? throw new BusinessException("Không tìm thấy tài khoản.");
        u.PasswordHash = hash; u.Salt = salt;
        Sync?.BeforeSave(u);
        c.Execute("UPDATE users SET password_hash=@hash, salt=@salt WHERE id=@id", new { id, hash, salt });
        if (u.Role == Roles.Admin) Sync?.Reseal(u.Username, password);   // khóa ghi Sheet mã hóa bằng mật khẩu Quản trị → ghi lại theo mật khẩu mới
    }

    private User Insert(string username, string fullName, string password, string role, string permissions = "")
    {
        username = username.Trim();
        if (username.Length < 3 || username.Any(char.IsWhiteSpace))
            throw new BusinessException("Tên đăng nhập cần ít nhất 3 ký tự và không có khoảng trắng.");
        if (!Roles.All.Contains(role)) throw new BusinessException("Vai trò không hợp lệ.");
        ValidatePassword(password);
        using var c = _db.Open();
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM users WHERE username=@username", new { username }) > 0)
            throw new BusinessException($"Tên đăng nhập \"{username}\" đã tồn tại.");
        var (hash, salt) = Hash(password);
        Sync?.BeforeSave(new User { Username = username, FullName = fullName.Trim(), PasswordHash = hash, Salt = salt, Role = role, IsActive = true, Permissions = permissions });
        var id = c.ExecuteScalar<long>(
            "INSERT INTO users(username, full_name, password_hash, salt, role, permissions) VALUES(@username, @fullName, @hash, @salt, @role, @permissions); SELECT last_insert_rowid();",
            new { username, fullName = fullName.Trim(), hash, salt, role, permissions });
        return c.QuerySingle<User>("SELECT * FROM users WHERE id=@id", new { id });
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 6)
            throw new BusinessException("Mật khẩu cần ít nhất 6 ký tự.");
    }

    private static (string hash, string salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    internal static bool Verify(string password, string salt, string expected)
    {
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(salt), Iterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(expected));
    }
}
