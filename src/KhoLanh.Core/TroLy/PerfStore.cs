using System.Globalization;
using Microsoft.Data.Sqlite;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// 1.100 (Hiệu năng, giai đoạn 1): kho số liệu hiệu năng của MÁY NÀY — file SQLite riêng <c>hieu-nang.db</c> trong thư mục dữ liệu
/// (không nằm trong database chính nên không đồng bộ LAN / Drive, không vào bản sao lưu). Mỗi phút 1 mẫu (CPU máy / KhoLanh, RAM,
/// ổ trống, độ trễ LAN / Google) + các mốc thời gian (mở app, đăng nhập, đồng bộ…). Tự xóa dữ liệu cũ hơn <see cref="KeepDays"/> ngày.
/// </summary>
public sealed class PerfStore
{
    public const int KeepDays = 30;
    public const string FileName = "hieu-nang.db";
    private readonly string _cs;
    private readonly object _lock = new();

    /// <summary>Một mẫu đo. CPU / RAM máy tính theo %, RAM KhoLanh theo MB, ổ đĩa theo GB, độ trễ theo ms (null = không đo được / không áp dụng).</summary>
    public sealed record Sample(DateTime At, double CpuSys, double CpuApp, double RamSys, double RamAppMb, double DiskFreeGb, double DiskTotalGb, double? LanMs, double? GoogleMs);
    /// <summary>Mốc thời gian một việc (mở app, đăng nhập, đồng bộ…).</summary>
    public sealed record Mark(DateTime At, string Kind, double Ms);

    public string Path { get; }

    public PerfStore(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".");
        _cs = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS samples (at INTEGER PRIMARY KEY, cpu_sys REAL, cpu_app REAL, ram_sys REAL, ram_app REAL,
                disk_free REAL, disk_total REAL, lan_ms REAL, google_ms REAL);
            CREATE TABLE IF NOT EXISTS marks (at INTEGER NOT NULL, kind TEXT NOT NULL, ms REAL NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_marks ON marks(kind, at);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open() { var c = new SqliteConnection(_cs); c.Open(); return c; }
    private static long Ts(DateTime t) => new DateTimeOffset(t).ToUnixTimeSeconds();
    private static DateTime At(long s) => DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime;
    private static object N(double? v) => v is double d && !double.IsNaN(d) ? d : DBNull.Value;
    private static double? D(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetDouble(i);

    public void Add(Sample s)
    {
        lock (_lock)
        {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO samples VALUES ($at,$a,$b,$c,$d,$e,$f,$g,$h)";
            cmd.Parameters.AddWithValue("$at", Ts(s.At)); cmd.Parameters.AddWithValue("$a", s.CpuSys); cmd.Parameters.AddWithValue("$b", s.CpuApp);
            cmd.Parameters.AddWithValue("$c", s.RamSys); cmd.Parameters.AddWithValue("$d", s.RamAppMb); cmd.Parameters.AddWithValue("$e", s.DiskFreeGb);
            cmd.Parameters.AddWithValue("$f", s.DiskTotalGb); cmd.Parameters.AddWithValue("$g", N(s.LanMs)); cmd.Parameters.AddWithValue("$h", N(s.GoogleMs));
            cmd.ExecuteNonQuery();
        }
    }

    public void AddMark(string kind, double ms, DateTime at)
    {
        lock (_lock)
        {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT INTO marks VALUES ($at,$k,$ms)";
            cmd.Parameters.AddWithValue("$at", Ts(at)); cmd.Parameters.AddWithValue("$k", kind); cmd.Parameters.AddWithValue("$ms", ms);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Mẫu mới nhất (null = chưa có).</summary>
    public Sample? Latest()
    {
        lock (_lock)
        {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT * FROM samples ORDER BY at DESC LIMIT 1";
            using var r = cmd.ExecuteReader();
            return r.Read() ? Read(r) : null;
        }
    }
    private static Sample Read(SqliteDataReader r) =>
        new(At(r.GetInt64(0)), r.GetDouble(1), r.GetDouble(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6), D(r, 7), D(r, 8));

    /// <summary>
    /// Các mẫu trong [from, to], gộp thành tối đa <paramref name="maxPoints"/> điểm (trung bình theo khoảng đều nhau) để vẽ biểu đồ
    /// 1 giờ / 24 giờ / 7 ngày / 30 ngày nhanh dù có tới 43.200 mẫu.
    /// </summary>
    public List<Sample> Range(DateTime from, DateTime to, int maxPoints = 240)
    {
        long a = Ts(from), b = Ts(to);
        var step = Math.Max(60, (b - a) / Math.Max(1, maxPoints));
        lock (_lock)
        {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = """
                SELECT MIN(at), AVG(cpu_sys), AVG(cpu_app), AVG(ram_sys), AVG(ram_app), AVG(disk_free), AVG(disk_total), AVG(lan_ms), AVG(google_ms)
                FROM samples WHERE at BETWEEN $a AND $b GROUP BY (at - $a) / $s ORDER BY 1
                """;
            cmd.Parameters.AddWithValue("$a", a); cmd.Parameters.AddWithValue("$b", b); cmd.Parameters.AddWithValue("$s", step);
            using var r = cmd.ExecuteReader();
            var res = new List<Sample>();
            while (r.Read()) res.Add(Read(r));
            return res;
        }
    }

    public List<Mark> Marks(string? kind, DateTime since)
    {
        lock (_lock)
        {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT at, kind, ms FROM marks WHERE at >= $s" + (kind is null ? "" : " AND kind = $k") + " ORDER BY at";
            cmd.Parameters.AddWithValue("$s", Ts(since)); if (kind is not null) cmd.Parameters.AddWithValue("$k", kind);
            using var r = cmd.ExecuteReader();
            var res = new List<Mark>();
            while (r.Read()) res.Add(new Mark(At(r.GetInt64(0)), r.GetString(1), r.GetDouble(2)));
            return res;
        }
    }

    /// <summary>Xóa mẫu / mốc cũ hơn <see cref="KeepDays"/> ngày. Trả về số dòng đã xóa.</summary>
    public int Prune(DateTime now)
    {
        var cut = Ts(now.AddDays(-KeepDays));
        lock (_lock)
        {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM samples WHERE at < $c; DELETE FROM marks WHERE at < $c;";
            cmd.Parameters.AddWithValue("$c", cut);
            return cmd.ExecuteNonQuery();
        }
    }

    public int Count()
    {
        lock (_lock) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM samples"; return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture); }
    }

    // ------------------------------------------------------------------ tổng hợp cho giao diện

    /// <summary>Thống kê 1 loại mốc: số lần, trung vị, lớn nhất, lần cuối (ms).</summary>
    public sealed record MarkStat(string Kind, int Count, double Median, double Max, double Last);
    public static MarkStat? Stat(IEnumerable<Mark> marks, string kind)
    {
        var l = marks.Where(m => m.Kind == kind).ToList();
        if (l.Count == 0) return null;
        var s = l.Select(m => m.Ms).OrderBy(x => x).ToList();
        var med = s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;
        return new MarkStat(kind, l.Count, med, s[^1], l[^1].Ms);
    }
}

/// <summary>
/// 1.100: ngưỡng tô thẻ số tab Hiệu năng (0 = ổn, 1 = vàng, 2 = đỏ) — dùng chung cho cảnh báo hiệu năng của Trợ lý.
/// CPU ≥ 70 % vàng / ≥ 90 % đỏ · RAM ≥ 80 % / ≥ 92 % · ổ trống &lt; 15 % hoặc &lt; 10 GB vàng, &lt; 5 % hoặc &lt; 2 GB đỏ ·
/// độ trễ LAN &gt; 150 / 800 ms, Google &gt; 600 / 2000 ms, không nối được = đỏ.
/// </summary>
public static class PerfLevel
{
    public static int Cpu(double pct) => pct >= 90 ? 2 : pct >= 70 ? 1 : 0;
    public static int Ram(double pct) => pct >= 92 ? 2 : pct >= 80 ? 1 : 0;
    public static int Disk(double freeGb, double totalGb)
    {
        if (totalGb <= 0) return 0;
        var pct = 100 * freeGb / totalGb;
        return pct < 5 || freeGb < 2 ? 2 : pct < 15 || freeGb < 10 ? 1 : 0;
    }
    /// <summary>Độ trễ mạng: <paramref name="expected"/> = có cần nối không (máy con LAN / có Internet); null + cần nối = đỏ.</summary>
    public static int Net(double? ms, bool lan, bool expected = true) =>
        ms is not double v ? (expected ? 2 : 0) : lan ? (v > 800 ? 2 : v > 150 ? 1 : 0) : (v > 2000 ? 2 : v > 600 ? 1 : 0);
}
