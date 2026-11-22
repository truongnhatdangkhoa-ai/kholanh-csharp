using Dapper;
using KhoLanh.Core.Data;
using KhoLanh.Core.Models;
using Microsoft.Data.Sqlite;

namespace KhoLanh.Core.Services;

/// <summary>Lỗi nghiệp vụ: thông báo hiển thị thẳng cho người dùng.</summary>
public class BusinessException : Exception
{
    public BusinessException(string message) : base(message) { }
}

/// <summary>Phiên làm việc: ai đang đăng nhập.</summary>
public class Session
{
    public User? CurrentUser { get; set; }
    public string Username => CurrentUser?.Username ?? "";
    public string Role => CurrentUser?.Role ?? "";

    public void RequireLogin()
    {
        if (CurrentUser is null) throw new BusinessException("Bạn cần đăng nhập.");
    }

    public void RequireManager()
    {
        RequireLogin();
        if (!Roles.CanManage(Role)) throw new BusinessException("Chức năng này cần quyền Quản lý trở lên.");
    }

    public void RequireAdmin()
    {
        RequireLogin();
        if (!Roles.IsAdmin(Role)) throw new BusinessException("Chức năng này chỉ dành cho Quản trị.");
    }
}

public class AuditService
{
    private readonly Database _db;
    public AuditService(Database db) { _db = db; }

    public void Log(SqliteConnection c, SqliteTransaction? tx, string username, string action, string detail)
    {
        c.Execute("INSERT INTO audit_log(at, username, action, detail) VALUES (@at, @u, @a, @d)",
            new { at = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), u = username, a = action, d = detail }, tx);
    }

    public void Log(string username, string action, string detail)
    {
        using var c = _db.Open();
        Log(c, null, username, action, detail);
    }

    public List<AuditEntry> Recent(int limit = 500, string? search = null)
    {
        using var c = _db.Open();
        var like = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";
        return c.Query<AuditEntry>(
            """
            SELECT id, at, username, action, detail FROM audit_log
            WHERE @like IS NULL OR username LIKE @like OR action LIKE @like OR detail LIKE @like
            ORDER BY id DESC LIMIT @limit
            """, new { like, limit }).ToList();
    }
}

public class SettingsService
{
    private readonly Database _db;
    public SettingsService(Database db) { _db = db; }

    public const string CompanyName = "company_name";
    public const string CompanyAddress = "company_address";
    public const string WarehouseName = "warehouse_name";

    public string Get(string key, string fallback = "")
    {
        using var c = _db.Open();
        return c.ExecuteScalar<string?>("SELECT value FROM settings WHERE key=@key", new { key }) ?? fallback;
    }

    public void Set(string key, string value)
    {
        using var c = _db.Open();
        c.Execute("INSERT INTO settings(key, value) VALUES(@key, @value) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
            new { key, value });
    }
}
