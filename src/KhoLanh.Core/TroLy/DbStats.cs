using KhoLanh.Core.Data;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// 1.100: dung lượng database theo module / bảng — file .db (+ -wal), dữ liệu module (module_sheets: db_key = module, sheet = bảng),
/// lịch sử phiên bản bảng (sheet_history) và nhật ký thao tác.
/// </summary>
public static class DbStats
{
    public sealed record Table(string Module, string Sheet, long Bytes, long Rows);
    public sealed record ModuleSize(string Module, long Bytes, long Rows, int Tables);
    public sealed record Result(long FileBytes, long WalBytes, long DataBytes, long HistoryBytes, long HistoryRows, long AuditRows, List<ModuleSize> Modules, List<Table> Tables);

    public static Result Read(Database db)
    {
        var tables = new List<Table>();
        long hist = 0, histRows = 0, audit = 0;
        using (var c = db.Open())
        {
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT db_key, sheet, LENGTH(CAST(data AS BLOB)), row_count FROM module_sheets";
                using var r = cmd.ExecuteReader();
                while (r.Read()) tables.Add(new Table(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? 0 : r.GetInt64(2), r.IsDBNull(3) ? 0 : r.GetInt64(3)));
            }
            hist = Scalar(c, "SELECT COALESCE(SUM(LENGTH(CAST(data AS BLOB))), 0) FROM sheet_history");
            histRows = Scalar(c, "SELECT COUNT(*) FROM sheet_history");
            audit = Scalar(c, "SELECT COUNT(*) FROM audit_log");
        }
        long Size(string p) { try { return File.Exists(p) ? new FileInfo(p).Length : 0; } catch { return 0; } }
        var mods = tables.GroupBy(t => t.Module).Select(g => new ModuleSize(g.Key, g.Sum(t => t.Bytes), g.Sum(t => t.Rows), g.Count()))
            .OrderByDescending(m => m.Bytes).ToList();
        return new Result(Size(db.FilePath), Size(db.FilePath + "-wal"), tables.Sum(t => t.Bytes), hist, histRows, audit, mods,
            tables.OrderByDescending(t => t.Bytes).ToList());
    }

    private static long Scalar(Microsoft.Data.Sqlite.SqliteConnection c, string sql)
    {
        try { using var cmd = c.CreateCommand(); cmd.CommandText = sql; return Convert.ToInt64(cmd.ExecuteScalar() ?? 0L); }
        catch { return 0; }   // bảng chưa có (CSDL cũ)
    }

    /// <summary>Dung lượng dễ đọc: 512 B · 12.3 KB · 4.5 MB · 1.2 GB.</summary>
    public static string Human(long b) => b < 1024 ? $"{b} B" : b < 1048576 ? $"{b / 1024.0:0.0} KB" : b < 1073741824 ? $"{b / 1048576.0:0.0} MB" : $"{b / 1073741824.0:0.00} GB";
}
