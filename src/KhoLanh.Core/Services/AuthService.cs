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
        return u;
    }

    public void Logout()
    {
        if (_session.CurrentUser is not null) _audit.Log(_session.Username, "Đăng xuất", "");
        _session.CurrentUser = null;
    }

    public List<User> ListUsers()
    {
        _session.RequireAdmin();
        using var c = _db.Open();
        return c.Query<User>("SELECT * FROM users ORDER BY username").ToList();
    }

    public User CreateUser(string username, string fullName, string password, string role)
    {
        _session.RequireAdmin();
        var u = Insert(username, fullName, password, role);
        _audit.Log(_session.Username, "Tạo tài khoản", $"{u.Username} ({Roles.Label(role)})");
        return u;
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
        c.Execute("UPDATE users SET full_name=@fullName, role=@role, is_active=@isActive WHERE id=@id",
            new { id, fullName = fullName.Trim(), role, isActive });
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
        c.Execute("UPDATE users SET password_hash=@hash, salt=@salt WHERE id=@id", new { id, hash, salt });
    }

    private User Insert(string username, string fullName, string password, string role)
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
        var id = c.ExecuteScalar<long>(
            "INSERT INTO users(username, full_name, password_hash, salt, role) VALUES(@username, @fullName, @hash, @salt, @role); SELECT last_insert_rowid();",
            new { username, fullName = fullName.Trim(), hash, salt, role });
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

    private static bool Verify(string password, string salt, string expected)
    {
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(salt), Iterations, HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(expected));
    }
}
