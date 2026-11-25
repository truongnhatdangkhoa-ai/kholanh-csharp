using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KhoLanh.Core.Modules;
using Microsoft.Data.Sqlite;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// Giai đoạn 5 (nhiều máy): "ẢNH CHỤP TÌNH TRẠNG" nhỏ của 1 máy — Quản trị xem cảnh báo / hiệu năng các máy khác qua LAN / Drive.
/// Ghi vào database dùng chung <see cref="TroLyMay.DbKey"/> qua <see cref="ISheetSource"/> (LanService tự đi qua LAN / Drive), MỖI MÁY 1 SHEET
/// riêng <c>May_&lt;mã máy&gt;</c> (1 ô JSON) → các máy ghi cùng lúc không bao giờ đè / gộp nhầm dòng của nhau.
/// </summary>
public sealed class MayTinhTrang
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Ver { get; set; } = "";
    /// <summary>Vai LAN / Drive (nhãn tiếng Việt).</summary>
    public string Role { get; set; } = "";
    /// <summary>Lúc ghi ảnh chụp này (dùng để biết mất liên lạc).</summary>
    public DateTime At { get; set; }
    /// <summary>Lần quét Trợ lý gần nhất của máy (null = chưa quét).</summary>
    public DateTime? ScanAt { get; set; }
    public int Do { get; set; }
    public int Vang { get; set; }
    public int Xanh { get; set; }
    public List<MayCanh> Canh { get; set; } = new();
    public DateTime? PerfAt { get; set; }
    public double? Cpu { get; set; }
    public double? Ram { get; set; }
    public double? DiskFree { get; set; }
    public double? DiskTotal { get; set; }
    public double? LanMs { get; set; }
    public double? GoogleMs { get; set; }
    public double? Cpu24 { get; set; }
    public double? Ram24 { get; set; }
}

/// <summary>Một cảnh báo trong ảnh chụp tình trạng: mức (do / vang), nơi, tiêu đề (đã ẩn tên khách / người dùng).</summary>
public sealed class MayCanh
{
    public string Sev { get; set; } = "";
    public string Mod { get; set; } = "";
    public string Title { get; set; } = "";
}

public static class TroLyMay
{
    public const string DbKey = "tro-ly", SheetPrefix = "May_";
    public const int MaxCanh = 30;
    /// <summary>Nội dung không đổi vẫn ghi lại sau khoảng này (nhịp "còn sống") — để máy đang chạy không bị coi là mất liên lạc.</summary>
    public static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(30);
    /// <summary>Quá khoảng này không có ảnh chụp mới = mất liên lạc (máy tắt / mất mạng / không đồng bộ).</summary>
    public static readonly TimeSpan Lost = TimeSpan.FromHours(1);

    public static string RoleLabel(string lanRole, bool drive) => lanRole switch
    {
        "chu" => "Máy chủ LAN", "duphong" => "Máy dự phòng LAN", "con" => "Máy con LAN", "xa" => drive ? "Xem từ xa (Drive)" : "Xem từ xa",
        _ => drive ? "Drive dùng chung" : "Máy đơn",
    };

    public static string SheetOf(string id)
    {
        var s = Regex.Replace(id ?? "", @"[^A-Za-z0-9_-]", "");
        if (s.Length == 0) s = "khong-ro";
        return SheetPrefix + (s.Length > 40 ? s[..40] : s);
    }

    public static bool MatLienLac(MayTinhTrang t, DateTime now) => now - t.At > Lost;

    /// <summary>Dựng ảnh chụp: cảnh báo Nặng trước rồi Cần xem, tối đa <see cref="MaxCanh"/>, tiêu đề / nơi qua <see cref="TroLyReport.Mask"/>.</summary>
    public static MayTinhTrang Build(string id, string name, string ver, string role, DateTime now, DateTime? scanAt,
        int nDo, int nVang, int nXanh, IEnumerable<(string Sev, string Mod, string Title)> canh,
        PerfStore.Sample? perf, double? cpu24, double? ram24, PerfStore.Sample? net = null)
    {
        var t = new MayTinhTrang { Id = id, Name = name, Ver = ver, Role = role, At = now, ScanAt = scanAt, Do = nDo, Vang = nVang, Xanh = nXanh, Cpu24 = R1(cpu24), Ram24 = R1(ram24) };
        t.Canh = canh.Where(c => c.Sev is "do" or "vang").OrderBy(c => c.Sev == "do" ? 0 : 1)
            .Take(MaxCanh).Select(c => new MayCanh { Sev = c.Sev, Mod = TroLyReport.Mask(c.Mod, true), Title = TroLyReport.Mask(c.Title, true) }).ToList();
        if (perf is not null)
        {
            t.PerfAt = perf.At; t.Cpu = R1(perf.CpuSys); t.Ram = R1(perf.RamSys); t.DiskFree = R1(perf.DiskFreeGb); t.DiskTotal = R1(perf.DiskTotalGb);
        }
        var n = net ?? perf;
        if (n is not null) { t.LanMs = R1(n.LanMs); t.GoogleMs = R1(n.GoogleMs); }
        return t;
    }
    private static double? R1(double? v) => v is double d && double.IsFinite(d) ? Math.Round(d, 1) : null;

    private static readonly JsonSerializerOptions Json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Chữ ký NỘI DUNG (bỏ lúc ghi / lúc đo; số hiệu năng làm tròn thô: CPU / RAM 10 %, ổ 1 GB, độ trễ 100 ms) — giống nhau thì không ghi lại
    /// (trừ nhịp <see cref="Heartbeat"/>), để dao động nhỏ mỗi phút không làm phình đồng bộ.
    /// </summary>
    public static string Sig(MayTinhTrang t)
    {
        static string B(double? v, double step) => v is double d ? Math.Round(d / step).ToString(CultureInfo.InvariantCulture) : "-";
        var head = string.Join("|", t.Id, t.Name, t.Ver, t.Role, t.ScanAt?.ToString("s", CultureInfo.InvariantCulture) ?? "-", t.Do, t.Vang, t.Xanh,
            B(t.Cpu, 10), B(t.Ram, 10), B(t.DiskFree, 1), B(t.DiskTotal, 1), B(t.LanMs, 100), B(t.GoogleMs, 100), B(t.Cpu24, 5), B(t.Ram24, 5));
        return head + "#" + string.Join("¦", t.Canh.Select(c => c.Sev + "~" + c.Mod + "~" + c.Title));
    }

    public static string ToJson(MayTinhTrang t) => JsonSerializer.Serialize(t, Json);
    public static MayTinhTrang? FromJson(string s)
    {
        try { return JsonSerializer.Deserialize<MayTinhTrang>(s, Json); } catch { return null; }
    }

    /// <summary>Có cần ghi không: chưa có bản cũ, nội dung đổi, hoặc bản cũ đã quá nhịp <see cref="Heartbeat"/>.</summary>
    public static bool NeedWrite(MayTinhTrang? old, MayTinhTrang now) =>
        old is null || Sig(old) != Sig(now) || now.At - old.At >= Heartbeat || now.At < old.At;
}

/// <summary>Đọc / ghi ảnh chụp tình trạng các máy qua <see cref="ISheetSource"/> (db <see cref="TroLyMay.DbKey"/>, sheet May_&lt;mã máy&gt;).</summary>
public sealed class TroLyMayStore
{
    private readonly Func<ISheetSource> _src;
    public TroLyMayStore(Func<ISheetSource> src) { _src = src; }

    private static MayTinhTrang? Parse(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonArray a || a.Count == 0 || a[0] is not JsonObject o) return null;
            var c = o["data"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
            return c.StartsWith("__JSON__", StringComparison.Ordinal) ? TroLyMay.FromJson(c[8..]) : null;
        }
        catch { return null; }
    }

    public MayTinhTrang? Read(string id) => Parse(_src().Read(TroLyMay.DbKey, TroLyMay.SheetOf(id)));

    /// <summary>Ghi ảnh chụp của máy mình nếu cần (<see cref="TroLyMay.NeedWrite"/>). Trả về true khi đã ghi.</summary>
    public bool Write(MayTinhTrang t)
    {
        var old = Read(t.Id);
        if (!TroLyMay.NeedWrite(old, t)) return false;
        var rows = new JsonArray { new JsonObject { ["data"] = "__JSON__" + TroLyMay.ToJson(t) } };
        _src().Write(TroLyMay.DbKey, new Dictionary<string, JsonArray?> { [TroLyMay.SheetOf(t.Id)] = rows });
        return true;
    }

    /// <summary>Mọi máy đã ghi ảnh chụp, mới cập nhật trước.</summary>
    public List<MayTinhTrang> ReadAll()
    {
        var src = _src();
        var res = new List<MayTinhTrang>();
        foreach (var sh in src.List(TroLyMay.DbKey).Where(s => s.StartsWith(TroLyMay.SheetPrefix, StringComparison.Ordinal)))
            if (Parse(src.Read(TroLyMay.DbKey, sh)) is { } t) res.Add(t);
        return res.OrderByDescending(x => x.At).ToList();
    }
}

/// <summary>
/// Giai đoạn 5 (lịch sử): mỗi lần quét Trợ lý 1 dòng tóm tắt, lưu RIÊNG MÁY NÀY trong hieu-nang.db (bảng <c>tro_ly_scans</c>, giữ 30 ngày) —
/// cùng file với <see cref="PerfStore"/>, không đồng bộ, không vào bản sao lưu.
/// </summary>
public sealed class TroLyLichSu
{
    public const int KeepDays = 30;
    public sealed record Scan(DateTime At, bool Deep, int Do, int Vang, int Xanh, int Moi, int TaiPhat, int Dong);
    /// <summary>Tóm tắt 1 ngày: số Nặng / Cần xem / Ổn CAO NHẤT trong ngày, cộng số mới / tái phát / đã hết, số lần quét.</summary>
    public sealed record Day(DateTime Date, int Do, int Vang, int Xanh, int Moi, int TaiPhat, int Dong, int Scans);

    private readonly string _cs;
    private readonly object _lock = new();
    public string Path { get; }

    public TroLyLichSu(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".");
        _cs = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS tro_ly_scans (at INTEGER PRIMARY KEY, deep INTEGER, n_do INTEGER, n_vang INTEGER, n_xanh INTEGER,
                moi INTEGER, tai_phat INTEGER, dong INTEGER);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open() { var c = new SqliteConnection(_cs); c.Open(); return c; }
    private static long Ts(DateTime t) => new DateTimeOffset(t).ToUnixTimeSeconds();
    private static DateTime At(long s) => DateTimeOffset.FromUnixTimeSeconds(s).LocalDateTime;

    public void Add(Scan s)
    {
        lock (_lock)
        {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO tro_ly_scans VALUES ($at,$d,$a,$b,$c,$m,$t,$g)";
            cmd.Parameters.AddWithValue("$at", Ts(s.At)); cmd.Parameters.AddWithValue("$d", s.Deep ? 1 : 0);
            cmd.Parameters.AddWithValue("$a", s.Do); cmd.Parameters.AddWithValue("$b", s.Vang); cmd.Parameters.AddWithValue("$c", s.Xanh);
            cmd.Parameters.AddWithValue("$m", s.Moi); cmd.Parameters.AddWithValue("$t", s.TaiPhat); cmd.Parameters.AddWithValue("$g", s.Dong);
            cmd.ExecuteNonQuery();
        }
    }

    public List<Scan> Range(DateTime from, DateTime to)
    {
        lock (_lock)
        {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT * FROM tro_ly_scans WHERE at BETWEEN $a AND $b ORDER BY at";
            cmd.Parameters.AddWithValue("$a", Ts(from)); cmd.Parameters.AddWithValue("$b", Ts(to));
            using var r = cmd.ExecuteReader();
            var res = new List<Scan>();
            while (r.Read())
                res.Add(new Scan(At(r.GetInt64(0)), r.GetInt64(1) != 0, r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7)));
            return res;
        }
    }

    public int Prune(DateTime now)
    {
        lock (_lock)
        {
            using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "DELETE FROM tro_ly_scans WHERE at < $c";
            cmd.Parameters.AddWithValue("$c", Ts(now.AddDays(-KeepDays)));
            return cmd.ExecuteNonQuery();
        }
    }

    public int Count()
    {
        lock (_lock) { using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM tro_ly_scans"; return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture); }
    }

    /// <summary>Gộp theo ngày cho <paramref name="days"/> ngày gần nhất (tính cả hôm nay); ngày không quét → null (biểu đồ để trống).</summary>
    public static List<Day?> ByDay(IEnumerable<Scan> scans, DateTime today, int days)
    {
        var g = scans.GroupBy(s => s.At.Date).ToDictionary(x => x.Key, x => x.ToList());
        var res = new List<Day?>();
        for (var i = days - 1; i >= 0; i--)
        {
            var d = today.Date.AddDays(-i);
            if (!g.TryGetValue(d, out var l)) { res.Add(null); continue; }
            res.Add(new Day(d, l.Max(x => x.Do), l.Max(x => x.Vang), l.Max(x => x.Xanh), l.Sum(x => x.Moi), l.Sum(x => x.TaiPhat), l.Sum(x => x.Dong), l.Count));
        }
        return res;
    }
}
