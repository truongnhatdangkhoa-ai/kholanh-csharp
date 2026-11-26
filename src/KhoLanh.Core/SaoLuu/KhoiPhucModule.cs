using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using KhoLanh.Core.Lan;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.SaoLuu;

/// <summary>
/// 1.114: "Khôi phục cho cả hệ thống" — chỉ thay DỮ LIỆU MODULE (bảng module_sheets) bằng bản trong file sao lưu, ghi qua nguồn dữ liệu đang dùng
/// (máy đơn: database máy này; LAN: ghi lên máy chủ như một lần sửa bình thường → mọi máy, kể cả máy dự phòng, tự nhận).
/// GIỮ NGUYÊN tài khoản, cài đặt máy, khóa LAN, Gmail… (không chép đè cả file như khôi phục cũ). Quản trị làm được ở máy nào cũng được
/// (máy con phải đang nối máy chủ — đang mất kết nối thì bị chặn, tránh bản khôi phục vào sổ chờ rồi bị gộp với dữ liệu máy chủ).
/// </summary>
public static class KhoiPhucModule
{
    /// <summary>Không khôi phục: tình trạng các máy của Trợ lý (mỗi máy tự ghi).</summary>
    public static readonly string[] Skip = { "tro-ly" };

    public sealed record ModuleInfo(string Db, string Label, int Sheets, long Rows);
    public sealed record KetQua(int Modules, int Written, int Deleted);

    /// <summary>Đọc file sao lưu: các module có dữ liệu (để người dùng chọn).</summary>
    public static List<ModuleInfo> Scan(string dbFile)
    {
        using var c = Open(dbFile);
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT db_key, COUNT(*), COALESCE(SUM(row_count), 0) FROM module_sheets GROUP BY db_key ORDER BY db_key";
        var res = new List<ModuleInfo>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var db = r.GetString(0);
            if (Skip.Contains(db)) continue;
            res.Add(new ModuleInfo(db, LegacyImporter.LabelOf(db), r.GetInt32(1), r.GetInt64(2)));
        }
        return res.OrderBy(m => Array.FindIndex(LegacyImporter.Modules, x => x.Db == m.Db) is var i && i < 0 ? 999 : i).ThenBy(m => m.Db).ToList();
    }

    /// <summary>Lý do không khôi phục được ở máy này lúc này (null = được).</summary>
    public static string? WhyNot(LanService lan)
    {
        if (!lan.Enabled || lan.Serving) return null;
        if (lan.Role == LanService.RoleRemote) return "Máy này xem dữ liệu qua Drive dùng chung — hãy khôi phục ở máy chính.";
        if (!lan.Online) return "Máy này đang KHÔNG nối được máy chủ (chấm đỏ góc trên) — khôi phục lúc này sẽ nằm trong sổ chờ rồi bị gộp với dữ liệu máy chủ. Nối lại máy chủ rồi khôi phục, hoặc khôi phục ngay trên máy chủ.";
        return null;
    }

    /// <summary>
    /// Thay dữ liệu các module <paramref name="only"/> (null = tất cả trong file) bằng bản trong file sao lưu: bảng có trong file → ghi bản trong file,
    /// bảng hiện có mà file không có → xóa. Đọc bản mới nhất trước khi ghi (không bị báo "máy khác vừa sửa").
    /// </summary>
    public static KetQua Apply(ISheetSource src, string dbFile, IReadOnlyCollection<string>? only = null)
    {
        var backup = new Dictionary<string, Dictionary<string, string>>();
        using (var c = Open(dbFile))
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "SELECT db_key, sheet, data FROM module_sheets";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var db = r.GetString(0);
                if (Skip.Contains(db) || only is not null && !only.Contains(db)) continue;
                if (!backup.TryGetValue(db, out var d)) backup[db] = d = new();
                d[r.GetString(1)] = r.IsDBNull(2) ? "[]" : r.GetString(2);
            }
        }
        int written = 0, deleted = 0;
        foreach (var (db, sheets) in backup)
        {
            var now = src.List(db);
            foreach (var s in now.Concat(sheets.Keys).Distinct()) src.Read(db, s);   // nhận bản mới nhất (phiên bản) trước khi ghi đè
            var w = new Dictionary<string, JsonArray?>();
            foreach (var (s, json) in sheets) { w[s] = JsonNode.Parse(json) as JsonArray ?? new JsonArray(); written++; }
            foreach (var s in now.Where(s => !sheets.ContainsKey(s))) { w[s] = null; deleted++; }
            src.Write(db, w);
        }
        return new KetQua(backup.Count, written, deleted);
    }

    /// <summary>Cả quy trình (Quản trị): kiểm tra máy, chụp dữ liệu hiện tại vào SaoLuu (truoc-khoi-phuc-module_…), ghi, ghi nhật ký. Trả đường dẫn bản chụp.</summary>
    public static (KetQua Kq, string Safety) Run(AppServices s, string dbFile, IReadOnlyCollection<string>? only, string from)
    {
        s.Session.RequireAdmin();
        if (WhyNot(s.Lan) is { } why) throw new BusinessException(why);
        var safety = Path.Combine(s.Backup.AutoBackupFolder, $"truoc-khoi-phuc-module_{DateTime.Now:yyyy-MM-dd_HHmmss}.db");
        s.Backup.BackupTo(safety);
        var kq = Apply(s.Lan, dbFile, only);
        s.Audit.Log(s.Session.Username, "Khôi phục dữ liệu module",
            $"{from} — {kq.Modules} module ({(only is null ? "tất cả" : string.Join(", ", only))}), ghi {kq.Written} bảng, xóa {kq.Deleted} bảng; bản trước khi khôi phục: {safety}");
        return (kq, safety);
    }

    private static SqliteConnection Open(string file)
    {
        if (!File.Exists(file)) throw new BusinessException("Không tìm thấy file sao lưu.");
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='module_sheets'";
        if (Convert.ToInt32(cmd.ExecuteScalar()) != 1) { c.Dispose(); throw new BusinessException("File này không phải bản sao lưu của Kho Lạnh C# (không có dữ liệu module)."); }
        return c;
    }
}
