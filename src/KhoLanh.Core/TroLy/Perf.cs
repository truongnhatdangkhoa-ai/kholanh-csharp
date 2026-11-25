using System.Diagnostics;
using System.Globalization;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// ĐO THAO TÁC CHẬM (1.72, A4): bọc thao tác dễ chậm (mở module, nạp file Excel, đồng bộ LAN / Drive, đẩy / kéo điện thoại, lưu trang Nhập liệu)
/// bằng <c>using (Perf.Measure("Mở module", "Tồn kho An An"))</c>; quá <see cref="SlowMs"/> thì ghi 1 dòng vào <c>logs/cham.log</c>
/// (giờ · thao tác · chi tiết · ms · số dòng · máy · bản). Trợ lý hệ thống đọc lại (<see cref="Read"/>, <see cref="Summary"/>).
/// </summary>
public static class Perf
{
    public static string DataDir { get; set; } = "";
    public static long SlowMs { get; set; } = 2000;
    public const string FileName = "cham.log";
    private static readonly object Lock = new();
    /// <summary>Ghi chú thêm cho thử nghiệm (không ghi file).</summary>
    public static Action<Entry>? Recorded;

    public sealed record Entry(DateTime At, string Op, string Detail, long Ms, int Rows, string Machine, string Version);

    public sealed class Scope : IDisposable
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private readonly string _op, _detail; private bool _done;
        public int Rows { get; set; }
        internal Scope(string op, string detail) { _op = op; _detail = detail; }
        public long ElapsedMs => _sw.ElapsedMilliseconds;
        public void Dispose()
        {
            if (_done) return; _done = true;
            _sw.Stop();
            Record(_op, _detail, _sw.ElapsedMilliseconds, Rows);
        }
    }

    public static Scope Measure(string op, string detail = "") => new(op, detail);

    public static void Record(string op, string detail, long ms, int rows = 0)
    {
        if (ms < SlowMs) return;
        var e = new Entry(DateTime.Now, op, detail, ms, rows, Environment.MachineName, UpdateVersion());
        try { Recorded?.Invoke(e); } catch { }
        if (DataDir.Length == 0) return;
        try
        {
            var dir = Path.Combine(DataDir, "logs"); Directory.CreateDirectory(dir);
            var line = string.Join("\t", e.At.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), Clean(op), Clean(detail), ms.ToString(CultureInfo.InvariantCulture),
                rows.ToString(CultureInfo.InvariantCulture), Clean(e.Machine), Clean(e.Version));
            lock (Lock)
            {
                var f = Path.Combine(dir, FileName);
                if (File.Exists(f) && new FileInfo(f).Length > 2_000_000) { try { File.Move(f, f + ".cu", true); } catch { } }   // không để file phình
                File.AppendAllText(f, line + "\n");
            }
        }
        catch { /* ghi đo đạc không được thì thôi, không ảnh hưởng thao tác */ }
    }

    private static string UpdateVersion() { try { return KhoLanh.Core.Services.UpdateService.Current.ToString(3); } catch { return ""; } }
    private static string Clean(string s) => (s ?? "").Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    /// <summary>Đọc các dòng đo trong <paramref name="days"/> ngày gần nhất.</summary>
    public static List<Entry> Read(string dataDir, DateTime now, int days = 7)
    {
        var f = Path.Combine(dataDir, "logs", FileName);
        var res = new List<Entry>();
        if (!File.Exists(f)) return res;
        foreach (var l in File.ReadAllLines(f))
        {
            var p = l.Split('\t');
            if (p.Length < 4 || !DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) || at < now.AddDays(-days)) continue;
            if (!long.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)) continue;
            res.Add(new Entry(at, p[1], p[2], ms, p.Length > 4 && int.TryParse(p[4], out var r) ? r : 0, p.Length > 5 ? p[5] : "", p.Length > 6 ? p[6] : ""));
        }
        return res;
    }

    public sealed record Group(string Op, string Detail, int Count, long MaxMs, long AvgMs, DateTime Last);
    /// <summary>Gộp theo thao tác + chi tiết, chậm nhất trước.</summary>
    public static List<Group> Summary(IEnumerable<Entry> list) =>
        list.GroupBy(e => (e.Op, e.Detail)).Select(g => new Group(g.Key.Op, g.Key.Detail, g.Count(), g.Max(x => x.Ms), (long)g.Average(x => x.Ms), g.Max(x => x.At)))
            .OrderByDescending(g => g.MaxMs).ToList();
}
