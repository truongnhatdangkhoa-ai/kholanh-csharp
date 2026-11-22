using Dapper;
using Microsoft.Data.Sqlite;

namespace KhoLanh.Core.Data;

/// <summary>
/// Kết nối và lược đồ CSDL SQLite. Mỗi thao tác mở một kết nối mới (SQLite rất nhẹ),
/// bật WAL và khóa ngoại.
/// </summary>
public class Database
{
    public const int SchemaVersion = 1;

    public string FilePath { get; }
    private readonly string _connectionString;

    static Database()
    {
        SqlMapper.AddTypeHandler(new DateTimeHandler());
        DefaultTypeMap.MatchNamesWithUnderscores = true;
    }

    public Database(string filePath)
    {
        FilePath = Path.GetFullPath(filePath);
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        }.ToString();
    }

    /// <summary>Thư mục dữ liệu mặc định: %LOCALAPPDATA%\KhoLanhCS (tách hẳn khỏi app cũ).</summary>
    public static string DefaultDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KhoLanhCS");

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        c.Execute("PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;");
        return c;
    }

    public void Initialize()
    {
        using var c = Open();
        c.Execute("PRAGMA journal_mode = WAL;");
        var version = c.ExecuteScalar<long>("PRAGMA user_version;");
        if (version < 1)
        {
            using var tx = c.BeginTransaction();
            c.Execute(SchemaV1, transaction: tx);
            c.Execute("PRAGMA user_version = 1;", transaction: tx);
            tx.Commit();
            version = 1;
        }
        if (version < 2)
        {
            using var tx = c.BeginTransaction();
            c.Execute(SchemaV2, transaction: tx);
            c.Execute("PRAGMA user_version = 2;", transaction: tx);
            tx.Commit();
            version = 2;
        }
        if (version < 3)
        {
            // v3: phân quyền riêng từng tài khoản tới từng màn hình.
            using var tx = c.BeginTransaction();
            c.Execute("ALTER TABLE users ADD COLUMN permissions TEXT NOT NULL DEFAULT '';", transaction: tx);
            c.Execute("PRAGMA user_version = 3;", transaction: tx);
            tx.Commit();
            version = 3;
        }
        if (version < 4)
        {
            // v4: mạng LAN — số phiên bản từng bảng, lịch sử bản trước, sổ chờ (máy con), xung đột (máy chủ).
            using var tx = c.BeginTransaction();
            c.Execute(SchemaV4, transaction: tx);
            c.Execute("PRAGMA user_version = 4;", transaction: tx);
            tx.Commit();
        }
    }

    /// <summary>
    /// v2: dữ liệu của các module gốc (Tồn kho An An, Tồn kho gửi) — mỗi (db_key, sheet) là một mảng dòng JSON,
    /// đúng cách app cũ lưu qua window.dbAPI.readSheet / writeSheet.
    /// </summary>
    private const string SchemaV2 = """
        CREATE TABLE IF NOT EXISTS module_sheets (
            db_key TEXT NOT NULL,
            sheet TEXT NOT NULL,
            data TEXT NOT NULL DEFAULT '[]',
            row_count INTEGER NOT NULL DEFAULT 0,
            updated_at TEXT NOT NULL DEFAULT '',
            updated_by TEXT NOT NULL DEFAULT '',
            PRIMARY KEY (db_key, sheet)
        );
        """;

    private const string SchemaV4 = """
        ALTER TABLE module_sheets ADD COLUMN version INTEGER NOT NULL DEFAULT 0;
        UPDATE module_sheets SET version = rowid;
        CREATE TABLE IF NOT EXISTS lan_meta (k TEXT PRIMARY KEY, v TEXT NOT NULL DEFAULT '');
        CREATE TABLE IF NOT EXISTS sheet_tombstones (
            db_key TEXT NOT NULL, sheet TEXT NOT NULL, version INTEGER NOT NULL, PRIMARY KEY (db_key, sheet));
        CREATE TABLE IF NOT EXISTS sheet_history (
            id INTEGER PRIMARY KEY AUTOINCREMENT, db_key TEXT NOT NULL, sheet TEXT NOT NULL,
            version INTEGER NOT NULL, data TEXT NOT NULL, saved_at TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_sheet_history ON sheet_history(db_key, sheet, version);
        CREATE TABLE IF NOT EXISTS lan_pending (
            db_key TEXT NOT NULL, sheet TEXT NOT NULL, op_id TEXT NOT NULL, data TEXT,
            base_version INTEGER NOT NULL, base_data TEXT, username TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL,
            PRIMARY KEY (db_key, sheet));
        CREATE TABLE IF NOT EXISTS lan_applied (op_id TEXT PRIMARY KEY, at TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS lan_conflicts (
            id INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT NOT NULL, machine TEXT NOT NULL, username TEXT NOT NULL,
            db_key TEXT NOT NULL, sheet TEXT NOT NULL, status TEXT NOT NULL,
            base_data TEXT, client_data TEXT, server_data TEXT, result_data TEXT, note TEXT NOT NULL DEFAULT '');
        """;

    private const string SchemaV1 = """
        CREATE TABLE IF NOT EXISTS users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT NOT NULL UNIQUE COLLATE NOCASE,
            full_name TEXT NOT NULL DEFAULT '',
            password_hash TEXT NOT NULL,
            salt TEXT NOT NULL,
            role TEXT NOT NULL,
            is_active INTEGER NOT NULL DEFAULT 1
        );
        CREATE TABLE IF NOT EXISTS products (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            code TEXT NOT NULL UNIQUE COLLATE NOCASE,
            name TEXT NOT NULL,
            category TEXT NOT NULL DEFAULT '',
            unit TEXT NOT NULL DEFAULT 'thùng',
            kg_per_unit REAL NOT NULL DEFAULT 0,
            sizes TEXT NOT NULL DEFAULT '',
            min_stock REAL NOT NULL DEFAULT 0,
            note TEXT NOT NULL DEFAULT '',
            is_active INTEGER NOT NULL DEFAULT 1
        );
        CREATE TABLE IF NOT EXISTS owners (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            code TEXT NOT NULL UNIQUE COLLATE NOCASE,
            name TEXT NOT NULL,
            phone TEXT NOT NULL DEFAULT '',
            address TEXT NOT NULL DEFAULT '',
            note TEXT NOT NULL DEFAULT '',
            is_active INTEGER NOT NULL DEFAULT 1
        );
        CREATE TABLE IF NOT EXISTS vouchers (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            no TEXT NOT NULL UNIQUE,
            type TEXT NOT NULL CHECK (type IN ('N','X')),
            date TEXT NOT NULL,
            owner_id INTEGER NOT NULL REFERENCES owners(id),
            partner TEXT NOT NULL DEFAULT '',
            vehicle TEXT NOT NULL DEFAULT '',
            note TEXT NOT NULL DEFAULT '',
            created_by TEXT NOT NULL DEFAULT '',
            created_at TEXT NOT NULL,
            cancelled INTEGER NOT NULL DEFAULT 0,
            cancel_reason TEXT NOT NULL DEFAULT ''
        );
        CREATE INDEX IF NOT EXISTS ix_vouchers_date ON vouchers(date);
        CREATE TABLE IF NOT EXISTS voucher_lines (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            voucher_id INTEGER NOT NULL REFERENCES vouchers(id) ON DELETE CASCADE,
            product_id INTEGER NOT NULL REFERENCES products(id),
            size TEXT NOT NULL DEFAULT '',
            lot TEXT NOT NULL DEFAULT '',
            location TEXT NOT NULL DEFAULT '',
            quantity REAL NOT NULL CHECK (quantity > 0),
            weight REAL NOT NULL DEFAULT 0 CHECK (weight >= 0),
            note TEXT NOT NULL DEFAULT ''
        );
        CREATE INDEX IF NOT EXISTS ix_lines_voucher ON voucher_lines(voucher_id);
        CREATE INDEX IF NOT EXISTS ix_lines_product ON voucher_lines(product_id);
        CREATE TABLE IF NOT EXISTS audit_log (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            at TEXT NOT NULL,
            username TEXT NOT NULL DEFAULT '',
            action TEXT NOT NULL,
            detail TEXT NOT NULL DEFAULT ''
        );
        CREATE TABLE IF NOT EXISTS settings (
            key TEXT PRIMARY KEY,
            value TEXT NOT NULL DEFAULT ''
        );
        """;

    /// <summary>Lưu ngày giờ dạng ISO để so sánh chuỗi đúng thứ tự.</summary>
    private sealed class DateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override void SetValue(System.Data.IDbDataParameter parameter, DateTime value)
        {
            parameter.Value = value.TimeOfDay == TimeSpan.Zero
                ? value.ToString("yyyy-MM-dd")
                : value.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public override DateTime Parse(object value) => value switch
        {
            DateTime d => d,
            string s => DateTime.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
            _ => Convert.ToDateTime(value, System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    public static string D(DateTime d) => d.ToString("yyyy-MM-dd");
}
