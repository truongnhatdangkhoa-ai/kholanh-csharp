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

    public bool Can(string key, string level = Perm.View) => Perm.Has(CurrentUser, key, level);

    /// <summary>1.110: được tạo / sửa tài khoản = Quản trị, hoặc tài khoản được tích quyền "Tạo / sửa tài khoản" (đăng nhập ở máy nào cũng vậy).</summary>
    public bool CanManageAccounts => CurrentUser is { IsActive: true } u && (Roles.IsAdmin(u.Role) || Perm.Has(u, "taikhoan", Perm.Edit));

    public void RequireAccountManager()
    {
        RequireLogin();
        if (!CanManageAccounts) throw new BusinessException("Tài khoản này chưa được cấp quyền \"Tạo / sửa tài khoản\". Nhờ Quản trị cấp ở Hệ thống › Tài khoản.");
    }

    public void Require(string key, string level = Perm.Edit)
    {
        RequireLogin();
        if (!Can(key, level))
            throw new BusinessException($"Bạn chưa được cấp quyền \"{Perm.LevelLabel(level)}\" cho mục {Perm.Find(key)?.Label ?? key}. Nhờ Quản trị cấp quyền ở Hệ thống › Tài khoản.");
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
        try { return Read(key, fallback); }
        catch (SqliteException ex) when (NoTable(ex))
        {
            // 1.142: file dữ liệu bị xóa / làm rỗng ngoài app (vd phần mềm dọn rác) → tạo lại bảng rồi đọc lại; vẫn lỗi thì dùng giá trị mặc định
            // (lỗi này từng làm sập handler đóng cửa sổ ở ChayAn.Enabled).
            try { Repair(); return Read(key, fallback); } catch { return fallback; }
        }
    }

    private string Read(string key, string fallback)
    {
        using var c = _db.Open();
        return c.ExecuteScalar<string?>("SELECT value FROM settings WHERE key=@key", new { key }) ?? fallback;
    }

    public static bool NoTable(SqliteException ex) => ex.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tạo lại cấu trúc CSDL khi thiếu bảng (file bị xóa / rỗng).</summary>
    private void Repair()
    {
        _db.Initialize();
        using var c = _db.Open();
        c.Execute("CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL DEFAULT '')");
    }

    public void Set(string key, string value)
    {
        try { Write(key, value); }
        catch (SqliteException ex) when (NoTable(ex)) { Repair(); Write(key, value); }
        if (Mirrored(key)) SaveMirror();
    }

    // ── 1.143: bản dự phòng cài đặt quan trọng ngoài bảng settings ──
    // Phần mềm dọn rác từng xóa bảng settings → máy chủ mất vai (lan_role), khóa LAN, link Web App… và lặng lẽ thành "máy đơn".
    // Nay các khóa quan trọng được chép thêm vào file nhỏ cạnh CSDL (<khodata.db>.cai-dat.json) mỗi lần ghi; mở app thấy khóa vắng ở bảng → nạp lại.
    // KHÔNG chép các khóa phụ thuộc dữ liệu hiện có (lan_last_seq, lan_sync_epoch, lan_serving, lan_claim…): khôi phục sai chúng làm lệch đồng bộ.
    private static readonly HashSet<string> MirrorKeys = new(StringComparer.Ordinal)
    {
        "lan_role", "lan_key", "lan_port", "lan_http_port", "lan_public", "lan_nodes", "lan_addrs", "lan_name", "lan_machine_id",
        "lan_server_name", "lan_rieng", "lan_ngoai_kho", "lan_plan", "lan_enc",
        "drive_mode", "drive_dir", "drive_every", "drive_key", "drive_bo", "drive_home",
        "acct_url", "acct_token", "acct_admin_key", "acct_keyblobs",
        "phone_webapp_url", "phone_pc_token", "lt_google_id", "lt_google_secret", "may_chu_chay_an",
        "phone_sync_interval", "phone_sync_instant", "phone_sync_pick",
        "company_name", "company_address", "warehouse_name",
    };
    // 1.155: lịch tự sao lưu Drive cá nhân theo tài khoản (dcn:<tk> — Gmail + chu kỳ) + ô tự đồng bộ Công cụ: bảng settings bị làm trống thì
    // đăng nhập Gmail vẫn được nhận lại từ Lưu trữ cá nhân (file riêng) nhưng chu kỳ về mặc định "Mỗi ngày" (người dùng: "chọn 1 giờ, sau cập nhật thành mỗi ngày").
    private static readonly string[] MirrorPrefixes = { "lx_mail_", "dcn:", "cc_dongbo:" };
    private static bool Mirrored(string key) => MirrorKeys.Contains(key) || MirrorPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal));

    public string MirrorPath => _db.FilePath + ".cai-dat.json";
    private readonly object _mirrorLock = new();

    /// <summary>Chép các khóa quan trọng đang có trong bảng ra file dự phòng (ghi tạm rồi đổi tên; lỗi ghi file không làm hỏng việc lưu cài đặt).</summary>
    public void SaveMirror()
    {
        try
        {
            Dictionary<string, string> d;
            using (var c = _db.Open())
                d = c.Query<(string key, string value)>("SELECT key, value FROM settings").Where(r => Mirrored(r.key)).ToDictionary(r => r.key, r => r.value);
            if (d.Count == 0) return;   // bảng đang rỗng / mới tạo lại: đừng ghi đè bản dự phòng còn tốt
            lock (_mirrorLock)
            {
                var tmp = MirrorPath + ".tmp";
                File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(d));
                File.Move(tmp, MirrorPath, true);
            }
        }
        catch { }
    }

    /// <summary>
    /// Mở app: khóa quan trọng nào có trong file dự phòng mà bảng KHÔNG còn dòng (bị xóa ngoài app) thì nạp lại. Khóa còn trong bảng (kể cả rỗng) giữ nguyên.
    /// Trả danh sách khóa đã nạp lại.
    /// </summary>
    /// <summary>1.186: khóa có trong bản dự phòng cài đặt mà bảng settings đang thiếu (không sửa gì) — Trợ lý báo "Cài đặt máy bị mất".</summary>
    public List<string> MissingFromMirror()
    {
        var miss = new List<string>();
        try
        {
            if (!File.Exists(MirrorPath)) return miss;
            var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(MirrorPath));
            if (d == null || d.Count == 0) return miss;
            using var c = _db.Open();
            var have = c.Query<string>("SELECT key FROM settings").ToHashSet(StringComparer.Ordinal);
            miss.AddRange(d.Keys.Where(k => Mirrored(k) && !have.Contains(k)));
        }
        catch { }
        return miss;
    }

    public List<string> RestoreFromMirror()
    {
        var restored = new List<string>();
        try
        {
            if (!File.Exists(MirrorPath)) return restored;
            var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(MirrorPath));
            if (d == null || d.Count == 0) return restored;
            try { Read("lan_role", ""); } catch (SqliteException ex) when (NoTable(ex)) { Repair(); }
            using var c = _db.Open();
            var have = c.Query<string>("SELECT key FROM settings").ToHashSet(StringComparer.Ordinal);
            foreach (var (k, v) in d)
            {
                if (!Mirrored(k) || have.Contains(k)) continue;
                c.Execute("INSERT INTO settings(key, value) VALUES(@k, @v)", new { k, v });
                restored.Add(k);
            }
        }
        catch { }
        return restored;
    }

    private static bool Trivial(string k) => k is "lan_machine_id" or "lan_name";

    /// <summary>Danh sách khóa (hoặc file dự phòng) có khóa nào ngoài mã máy / tên máy — nếu không thì mất cũng không đáng báo.</summary>
    public static bool Meaningful(IEnumerable<string> keys) => keys.Any(k => !Trivial(k));

    private bool MirrorHasMeaning(object? restoredOnly)
    {
        try
        {
            if (!File.Exists(MirrorPath)) return false;
            var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(MirrorPath));
            return d != null && Meaningful(d.Keys);
        }
        catch { return false; }
    }

    /// <summary>Bảng cài đặt trống nhưng máy đã có tài khoản và không có file dự phòng → cài đặt (vai máy, LAN…) đã mất.</summary>
    public bool LostWithoutBackup()
    {
        try
        {
            if (MirrorHasMeaning(restoredOnly: null)) return false;
            using var c = _db.Open();
            try { if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM settings") > 0) return false; }
            catch (SqliteException ex) when (NoTable(ex)) { }   // bảng bị xóa hẳn = trống
            return c.ExecuteScalar<long>("SELECT COUNT(*) FROM users") > 0;
        }
        catch { return false; }
    }

    private void Write(string key, string value)
    {
        using var c = _db.Open();
        c.Execute("INSERT INTO settings(key, value) VALUES(@key, @value) ON CONFLICT(key) DO UPDATE SET value=excluded.value",
            new { key, value });
    }
}
