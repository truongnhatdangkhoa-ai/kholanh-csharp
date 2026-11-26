using KhoLanh.Core.Data;
using Microsoft.Data.Sqlite;

namespace KhoLanh.Core.Services;

/// <summary>Sao lưu / khôi phục file CSDL bằng cơ chế backup trực tuyến của SQLite.</summary>
public class BackupService
{
    private readonly Database _db;
    private readonly Session _session;
    private readonly AuditService _audit;

    public BackupService(Database db, Session session, AuditService audit)
    {
        _db = db; _session = session; _audit = audit;
    }

    public string AutoBackupFolder => Path.Combine(Path.GetDirectoryName(_db.FilePath)!, "SaoLuu");

    /// <summary>Chép CSDL ra file .db (an toàn cả khi app đang mở).</summary>
    public string BackupTo(string targetPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        if (File.Exists(targetPath)) File.Delete(targetPath);
        using (var src = _db.Open())
        using (var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = targetPath, Pooling = false }.ToString()))
        {
            dst.Open();
            src.BackupDatabase(dst);
        }
        return targetPath;
    }

    /// <summary>Sao lưu tự động mỗi ngày một bản, giữ <paramref name="keep"/> bản gần nhất.</summary>
    public string? DailyAutoBackup(int keep = 30)
    {
        Directory.CreateDirectory(AutoBackupFolder);
        var path = Path.Combine(AutoBackupFolder, $"tu-dong_{DateTime.Today:yyyy-MM-dd}.db");
        if (File.Exists(path)) return null;
        BackupTo(path);
        foreach (var old in Directory.GetFiles(AutoBackupFolder, "tu-dong_*.db").OrderByDescending(f => f).Skip(keep))
        {
            try { File.Delete(old); } catch { /* bỏ qua file đang bị khóa */ }
        }
        return path;
    }

    public string ManualBackup(string targetPath)
    {
        _session.Require("backup");
        BackupTo(targetPath);
        _audit.Log(_session.Username, "Sao lưu", targetPath);
        return targetPath;
    }

    public IEnumerable<FileInfo> ListAutoBackups() =>
        Directory.Exists(AutoBackupFolder)
            ? new DirectoryInfo(AutoBackupFolder).GetFiles("*.db").OrderByDescending(f => f.Name)
            : Enumerable.Empty<FileInfo>();

    /// <summary>
    /// Khôi phục từ file sao lưu: kiểm tra file hợp lệ, sao lưu bản hiện tại trước, rồi chép đè nội dung.
    /// Sau khi khôi phục cần đăng nhập lại.
    /// </summary>
    /// <summary>Trả về lý do không cho khôi phục (ví dụ đang dùng LAN); null = được.</summary>
    public Func<string?>? Guard { get; set; }
    /// <summary>Máy ở nhà / máy xem từ xa qua Drive: được khôi phục bằng quyền "Sao lưu và cài đặt"; giữ nguyên cài đặt của máy (vai, thư mục Drive, mã…).</summary>
    public Func<bool>? IsRemote { get; set; }
    /// <summary>Gọi sau khi khôi phục (AppServices: dựng lại phần LAN theo cài đặt mới).</summary>
    public Action? AfterRestore { get; set; }
    /// <summary>Lần khôi phục gần nhất đã tách máy khỏi mạng LAN / Drive dùng chung.</summary>
    public bool LastDetached { get; private set; }
    /// <summary>settings: máy đã tách khỏi LAN sau khi khôi phục — không tự nhận vai LAN theo tài khoản nữa cho tới khi bấm "Nối lại".</summary>
    public const string DetachedKey = "lan_rieng";

    public string Restore(string sourcePath)
    {
        var remote = IsRemote?.Invoke() == true;
        _session.Require("khoiphuc");
        if (Guard?.Invoke() is { } why) throw new BusinessException(why);
        if (!File.Exists(sourcePath)) throw new BusinessException("Không tìm thấy file sao lưu.");
        using (var check = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        {
            check.Open();
            using var cmd = check.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('users','products','vouchers','voucher_lines')";
            if (Convert.ToInt32(cmd.ExecuteScalar()) != 4)
                throw new BusinessException("File này không phải bản sao lưu của Kho Lạnh C#.");
            cmd.CommandText = "PRAGMA integrity_check";
            if (cmd.ExecuteScalar() as string != "ok") throw new BusinessException("File sao lưu bị hỏng (kiểm tra toàn vẹn không đạt).");
        }
        var safety = Path.Combine(AutoBackupFolder, $"truoc-khoi-phuc_{DateTime.Now:yyyy-MM-dd_HHmmss}.db");
        BackupTo(safety);
        // 1.114: LUÔN giữ cài đặt của chính máy này (bản sao lưu có thể của máy khác — mang vai máy chủ / khóa LAN / token của máy đó)
        List<(string key, string value)> keepSettings;
        using (var c0 = _db.Open()) keepSettings = Dapper.SqlMapper.Query<(string, string)>(c0, "SELECT key, value FROM settings").ToList();
        string Old(string k) => keepSettings.FirstOrDefault(x => x.key == k).value ?? "";
        var detach = Old("lan_role").Length > 0 || Old("drive_mode").Length > 0;
        using (var src = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
        using (var dst = _db.Open())
        {
            src.Open();
            src.BackupDatabase(dst);
        }
        _db.Initialize();
        using (var c = _db.Open())
        using (var tx = c.BeginTransaction())
        {
            Dapper.SqlMapper.Execute(c, "DELETE FROM settings", transaction: tx);
            foreach (var (k, v) in keepSettings) Dapper.SqlMapper.Execute(c, "INSERT INTO settings(key, value) VALUES(@k, @v)", new { k, v }, tx);
            Dapper.SqlMapper.Execute(c, "DELETE FROM lan_pending", transaction: tx);
            if (detach)
            {
                // máy ngoài LAN / máy xem từ xa: TÁCH ra dùng riêng — không nối lại máy chủ, không gửi gì lên, không tự nhận vai theo tài khoản
                // (dữ liệu khôi phục chỉ nằm ở máy này, máy chủ / máy trong LAN không bị ảnh hưởng). "Nối lại" ở Đồng bộ & sao lưu › Giữa các máy.
                void Set(string k, string v) => Dapper.SqlMapper.Execute(c, "INSERT INTO settings(key, value) VALUES(@k, @v) ON CONFLICT(key) DO UPDATE SET value=excluded.value", new { k, v }, tx);
                Set("lan_role", ""); Set("lan_serving", ""); Set("drive_mode", ""); Set("drive_seq", "0"); Set(DetachedKey, "1");
            }
            tx.Commit();
        }
        LastDetached = detach;
        AfterRestore?.Invoke();
        _audit.Log(_session.Username, "Khôi phục dữ liệu", $"{sourcePath} (bản cũ lưu ở {safety})" + (detach ? " — máy ngoài LAN: đã tách ra dùng dữ liệu riêng" : ""));
        return safety;
    }
}
