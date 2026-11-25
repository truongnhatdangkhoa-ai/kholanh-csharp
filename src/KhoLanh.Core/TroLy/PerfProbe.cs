using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// 1.100: đọc số liệu máy (CPU cả máy + KhoLanh, RAM, ổ trống, độ trễ mạng). Windows dùng API hệ điều hành (GetSystemTimes,
/// GlobalMemoryStatusEx); Linux đọc /proc (công cụ chụp / CI). CPU tính theo chênh lệch giữa 2 lần đọc nên lần đọc đầu trả 0.
/// </summary>
public sealed class PerfProbe
{
    private (ulong Idle, ulong Total)? _sys;
    private (TimeSpan Cpu, DateTime At)? _app;
    private readonly string _dataDir;

    public PerfProbe(string dataDir) { _dataDir = dataDir; }

    // ------------------------------------------------------------------ CPU

    /// <summary>% CPU cả máy từ lần đọc trước tới giờ.</summary>
    public double CpuSystem()
    {
        var now = ReadSysTimes();
        if (now is null) return 0;
        var prev = _sys; _sys = now;
        return prev is null ? 0 : CpuPct(prev.Value, now.Value);
    }

    /// <summary>% CPU (idle, tổng) giữa 2 lần đọc — tách riêng để test.</summary>
    public static double CpuPct((ulong Idle, ulong Total) a, (ulong Idle, ulong Total) b)
    {
        var total = b.Total - a.Total; var idle = b.Idle - a.Idle;
        if (b.Total <= a.Total || total == 0) return 0;
        return Math.Clamp(100.0 * (total - Math.Min(idle, total)) / total, 0, 100);
    }

    /// <summary>% CPU của chính KhoLanh (chia theo số nhân).</summary>
    public double CpuApp()
    {
        using var p = Process.GetCurrentProcess();
        var now = (p.TotalProcessorTime, DateTime.UtcNow);
        var prev = _app; _app = now;
        if (prev is null) return 0;
        var wall = (now.UtcNow - prev.Value.At).TotalMilliseconds * Environment.ProcessorCount;
        return wall <= 0 ? 0 : Math.Clamp(100 * (now.TotalProcessorTime - prev.Value.Cpu).TotalMilliseconds / wall, 0, 100);
    }

    private static (ulong, ulong)? ReadSysTimes()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (!GetSystemTimes(out var idle, out var kernel, out var user)) return null;
                return (idle.Value, kernel.Value + user.Value);   // kernel đã gồm idle
            }
            if (File.Exists("/proc/stat")) return ParseProcStat(File.ReadLines("/proc/stat").FirstOrDefault() ?? "");
        }
        catch { }
        return null;
    }

    /// <summary>Dòng "cpu  user nice system idle iowait irq softirq steal …" của /proc/stat → (idle + iowait, tổng).</summary>
    public static (ulong Idle, ulong Total)? ParseProcStat(string line)
    {
        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 5 || p[0] != "cpu") return null;
        var v = p.Skip(1).Take(8).Select(x => ulong.TryParse(x, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0).ToArray();
        ulong total = 0; foreach (var x in v) total += x;
        return (v[3] + (v.Length > 4 ? v[4] : 0), total);
    }

    // ------------------------------------------------------------------ RAM

    /// <summary>% RAM cả máy đang dùng.</summary>
    public static double RamSystem()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var m = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
                if (GlobalMemoryStatusEx(ref m)) return m.MemoryLoad;
            }
            if (File.Exists("/proc/meminfo")) return ParseMemInfo(File.ReadAllText("/proc/meminfo")) ?? 0;
            var gc = GC.GetGCMemoryInfo();
            return gc.TotalAvailableMemoryBytes > 0 ? 100.0 * gc.MemoryLoadBytes / gc.TotalAvailableMemoryBytes : 0;
        }
        catch { return 0; }
    }

    /// <summary>/proc/meminfo → % dùng = (MemTotal − MemAvailable) / MemTotal.</summary>
    public static double? ParseMemInfo(string text)
    {
        double? Get(string k) =>
            text.Split('\n').Select(l => l.Split(':')).Where(p => p.Length == 2 && p[0].Trim() == k)
                .Select(p => double.TryParse(p[1].Replace("kB", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : (double?)null).FirstOrDefault();
        var t = Get("MemTotal"); var a = Get("MemAvailable");
        return t is > 0 && a is double av ? Math.Clamp(100 * (t.Value - av) / t.Value, 0, 100) : null;
    }

    /// <summary>RAM KhoLanh đang giữ (MB, working set).</summary>
    public static double RamAppMb() { using var p = Process.GetCurrentProcess(); p.Refresh(); return p.WorkingSet64 / 1048576.0; }

    // ------------------------------------------------------------------ ổ đĩa / mạng

    /// <summary>(trống, tổng) GB của ổ chứa thư mục dữ liệu.</summary>
    public (double Free, double Total) Disk()
    {
        try
        {
            var root = System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(_dataDir));
            if (string.IsNullOrEmpty(root)) return (0, 0);
            var d = new DriveInfo(root);
            return (d.AvailableFreeSpace / 1073741824.0, d.TotalSize / 1073741824.0);
        }
        catch { return (0, 0); }
    }

    /// <summary>Thời gian mở kết nối TCP tới "máy:cổng" (ms); không nối được trong <paramref name="timeoutMs"/> → null.</summary>
    public static double? Latency(string? hostPort, int timeoutMs = 3000)
    {
        if (string.IsNullOrWhiteSpace(hostPort)) return null;
        var i = hostPort.LastIndexOf(':');
        if (i <= 0 || !int.TryParse(hostPort[(i + 1)..], out var port)) return null;
        try
        {
            using var c = new TcpClient();
            var sw = Stopwatch.StartNew();
            if (!c.ConnectAsync(hostPort[..i], port).Wait(timeoutMs)) return null;
            return Math.Round(sw.Elapsed.TotalMilliseconds, 1);
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ Windows

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Low, High; public ulong Value => ((ulong)High << 32) | Low; }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx m);
}
