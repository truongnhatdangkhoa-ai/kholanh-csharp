namespace KhoLanh.Core.TroLy;

/// <summary>
/// 1.100: bộ đo hiệu năng chạy ngầm — mỗi <see cref="Interval"/> (1 phút) ghi 1 mẫu vào <see cref="PerfStore"/>; độ trễ LAN / Google đo
/// mỗi 5 phút (giữa các lượt ghi null); mỗi ngày xóa dữ liệu cũ hơn 30 ngày. <see cref="Mark"/> ghi mốc thời gian (mở app, đăng nhập,
/// đồng bộ…) từ bất cứ đâu trong app. Lỗi đo không bao giờ làm phiền người dùng.
/// </summary>
public sealed class PerfMonitor : IDisposable
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);
    public const int LatencyEvery = 5;   // lượt
    public const string GoogleHost = "www.google.com:443";
    /// <summary>Mốc thời gian dùng chung (khóa ghi trong bảng marks).</summary>
    public const string MarkMoApp = "mo-app", MarkDangNhap = "dang-nhap", MarkDongBoLan = "dong-bo-lan", MarkDongBoDrive = "dong-bo-drive", MarkDongBoDt = "dong-bo-dien-thoai";
    public static string MarkLabel(string k) => k switch
    {
        MarkMoApp => "Mở app", MarkDangNhap => "Đăng nhập", MarkDongBoLan => "Đồng bộ LAN", MarkDongBoDrive => "Đồng bộ Drive", MarkDongBoDt => "Đồng bộ điện thoại", _ => k,
    };

    /// <summary>Bộ đo đang chạy (null = chưa bật, vd công cụ chụp / test).</summary>
    public static PerfMonitor? Current { get; private set; }

    public PerfStore Store { get; }
    private readonly PerfProbe _probe;
    private readonly Func<string?> _lanAddr;
    private Timer? _timer;
    private int _tick, _busy;
    private DateTime _pruned;
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;
    /// <summary>Đo độ trễ (đổi được trong test).</summary>
    public Func<string?, double?> Ping { get; set; } = h => PerfProbe.Latency(h);

    public PerfMonitor(string dataDir, Func<string?> lanServerAddr)
    {
        Store = new PerfStore(Path.Combine(dataDir, PerfStore.FileName));
        _probe = new PerfProbe(dataDir);
        _lanAddr = lanServerAddr;
    }

    /// <summary>Bật đo ngầm (mẫu đầu tiên sau vài giây để CPU có lần đọc mốc).</summary>
    public void Start()
    {
        Current = this;
        _probe.CpuSystem(); _probe.CpuApp();   // lần đọc mốc
        _timer ??= new Timer(_ => Tick(), null, TimeSpan.FromSeconds(15), Interval);
    }

    /// <summary>Một lượt đo + ghi (gọi từ bộ hẹn giờ; test gọi trực tiếp).</summary>
    public PerfStore.Sample? Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return null;
        try
        {
            var now = Now();
            var (free, total) = _probe.Disk();
            double? lan = null, gg = null;
            if (_tick++ % LatencyEvery == 0) { lan = Ping(_lanAddr()); gg = Ping(GoogleHost); }
            var s = new PerfStore.Sample(now, Math.Round(_probe.CpuSystem(), 1), Math.Round(_probe.CpuApp(), 1), Math.Round(PerfProbe.RamSystem(), 1),
                Math.Round(PerfProbe.RamAppMb(), 1), Math.Round(free, 2), Math.Round(total, 2), lan, gg);
            Store.Add(s);
            if ((now - _pruned).TotalHours >= 24) { Store.Prune(now); _pruned = now; }
            return s;
        }
        catch { return null; }
        finally { Interlocked.Exchange(ref _busy, 0); }
    }

    /// <summary>Ghi mốc thời gian (ms) của 1 việc — an toàn khi chưa bật bộ đo.</summary>
    public static void Mark(string kind, double ms)
    {
        try { if (Current is { } m) m.Store.AddMark(kind, Math.Round(ms), m.Now()); } catch { }
    }

    /// <summary>Thao tác đo bằng <see cref="Perf.Measure"/> có mốc riêng (đồng bộ LAN / Drive / điện thoại).</summary>
    public static void MarkOp(string op, double ms)
    {
        var k = op switch { "Đồng bộ LAN" => MarkDongBoLan, "Đồng bộ thư mục / Drive" => MarkDongBoDrive, "Đồng bộ điện thoại" => MarkDongBoDt, _ => null };
        if (k is not null) Mark(k, ms);
    }

    public void Dispose() { _timer?.Dispose(); _timer = null; if (Current == this) Current = null; }
}
