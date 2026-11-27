using System.Globalization;
using System.Text;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// 1.172 (người dùng: "tab Hiệu năng của Trợ lý hệ thống thêm nút gửi prompt báo cáo cho Claude để kiểm tra app"): soạn PROMPT + số liệu hiệu năng THẬT của máy
/// (CPU / RAM / ổ / mạng, mốc mở app · đăng nhập · đồng bộ, thao tác chậm, kích thước database, cảnh báo hiệu năng) để Quản trị sao chép dán vào Claude.
/// Không có tên khách / người dùng / đường dẫn: chỉ số đo (cùng quy tắc che của <see cref="TroLyReport.Mask"/>).
/// </summary>
public static class TroLyHieuNangBaoCao
{
    public sealed class Input
    {
        public string Version = "", Os = "", RangeLabel = "24 giờ", Note = "";
        public int Cores;
        public DateTime At;
        public PerfStore.Sample? Last;
        public List<PerfStore.Sample> Points = new();
        public List<PerfStore.Mark> Marks = new();
        public List<Perf.Entry> Slow = new();
        public DbStats.Result? Db;
        public List<Finding> Findings = new();
        public bool LanExpected;
        public long SlowMs = 2000;
    }

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static string N(double v, string f = "0.#") => v.ToString(f, Inv);
    private static double Pct(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        var i = (sorted.Count - 1) * p; var lo = (int)Math.Floor(i); var hi = (int)Math.Ceiling(i);
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (i - lo);
    }
    private static string Sec(double ms) => ms >= 60000 ? $"{N(ms / 60000)} phút" : $"{N(ms / 1000)} giây";
    private static string Flag(int level) => level switch { 2 => " 🔴", 1 => " 🟡", _ => "" };

    /// <summary>"trung vị · p95 · lớn nhất" của 1 cột số.</summary>
    private static string Dist(IEnumerable<double> xs, string unit, Func<double, int>? level = null)
    {
        var s = xs.OrderBy(x => x).ToList(); if (s.Count == 0) return "(chưa có mẫu)";
        var med = Pct(s, 0.5); var p95 = Pct(s, 0.95);
        return $"trung vị {N(med)}{unit} · p95 {N(p95)}{unit} · lớn nhất {N(s[^1])}{unit}" + (level is null ? "" : Flag(level(p95)));
    }

    public static string Build(Input i)
    {
        var L = new StringBuilder();
        L.AppendLine("# BÁO CÁO HIỆU NĂNG — Kho Lạnh C# (từ \"Trợ lý hệ thống › Hiệu năng\")");
        L.AppendLine($"Phiên bản app: {i.Version} · lập lúc {i.At.ToString("dd/MM/yyyy HH:mm", Inv)} · khoảng đo CPU / RAM: {i.RangeLabel} ({i.Points.Count} mẫu, 1 mẫu / phút khi app mở)");
        L.AppendLine($"Máy: {(i.Os.Length > 0 ? i.Os : "?")} · {i.Cores} luồng CPU");
        L.AppendLine();
        L.AppendLine("## Yêu cầu cho Claude");
        L.AppendLine("Hãy KIỂM TRA HIỆU NĂNG app Kho Lạnh C# (repo kholanh-csharp, đọc CLAUDE.md trước) dựa trên số liệu THẬT của máy người dùng bên dưới. App chạy trên WINDOWS,");
        L.AppendLine("trong khi môi trường của bạn là Linux — số đo ở Linux có thể khác (bài học 1.141: đừng áp sửa theo đo ở Linux cho Windows nếu chưa chứng minh được).");
        L.AppendLine("1. Nhận định: chỗ nào bất thường, mức độ ảnh hưởng tới người dùng (đứng giao diện, mở chậm, đồng bộ chậm, tốn RAM / CPU, database phình).");
        L.AppendLine("2. Với mỗi điểm nghẽn: nêu NGUYÊN NHÂN KHẢ DĨ xếp theo độ chắc chắn, chỉ rõ file / hàm trong repo cần xem và bằng chứng từ số liệu dưới đây.");
        L.AppendLine("3. Nếu chưa đủ bằng chứng: đề xuất đo thêm (công cụ SHOTS_ONLY=perf / perf2 trong tools/Screenshots, logs/cham.log, Trợ lý › Hiệu năng) và nói cần số liệu gì từ máy người dùng.");
        L.AppendLine("4. CHỈ đề xuất cách sửa nhỏ nhất, an toàn trước; sau khi người dùng đồng ý mới sửa kèm test, tăng phiên bản, cập nhật README theo CLAUDE.md.");
        if (i.Note.Trim().Length > 0) { L.AppendLine(); L.AppendLine("Người dùng mô tả hiện tượng: " + TroLyReport.Mask(i.Note.Trim(), true)); }
        L.AppendLine();

        // ---- máy
        L.AppendLine("## Máy (theo mẫu đo)");
        if (i.Points.Count > 0)
        {
            L.AppendLine("- CPU cả máy: " + Dist(i.Points.Select(p => p.CpuSys), "%", PerfLevel.Cpu));
            L.AppendLine("- CPU KhoLanh: " + Dist(i.Points.Select(p => p.CpuApp), "%"));
            L.AppendLine("- RAM cả máy: " + Dist(i.Points.Select(p => p.RamSys), "%", PerfLevel.Ram));
            L.AppendLine("- RAM KhoLanh: " + Dist(i.Points.Select(p => p.RamAppMb), " MB", v => v > PerfChecks.AppRamMb ? 2 : v > PerfChecks.AppRamMb * 0.6 ? 1 : 0));
            L.AppendLine("- Mạng LAN (độ trễ): " + (i.Points.Any(p => p.LanMs is not null) ? Dist(i.Points.Where(p => p.LanMs is not null).Select(p => p.LanMs!.Value), " ms") : i.LanExpected ? "KHÔNG nối được máy chủ trong khoảng đo" : "máy này làm chủ / không dùng LAN"));
            L.AppendLine("- Mạng Internet (Google): " + (i.Points.Any(p => p.GoogleMs is not null) ? Dist(i.Points.Where(p => p.GoogleMs is not null).Select(p => p.GoogleMs!.Value), " ms") : "không có mẫu nối được"));
        }
        else L.AppendLine("- (chưa có mẫu nào — bộ đo ghi mỗi phút khi app đang mở)");
        if (i.Last is { } l) L.AppendLine($"- Ổ chứa dữ liệu: còn {N(l.DiskFreeGb)} GB trống / {N(l.DiskTotalGb, "0")} GB" + Flag(PerfLevel.Disk(l.DiskFreeGb, l.DiskTotalGb)));
        L.AppendLine();

        // ---- mốc
        L.AppendLine("## Mốc thời gian của app (7 ngày)");
        var kinds = i.Marks.GroupBy(m => m.Kind).Select(g => g.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (kinds.Count == 0) L.AppendLine("- (chưa có mốc nào)");
        else
        {
            L.AppendLine("| Mốc | Số lần | Trung vị | Lớn nhất | Lần cuối |");
            L.AppendLine("|---|---|---|---|---|");
            foreach (var k in kinds)
                if (PerfStore.Stat(i.Marks, k) is { } st)
                    L.AppendLine($"| {PerfMonitor.MarkLabel(k)} | {st.Count} | {Sec(st.Median)}{(st.Median > 10000 ? " 🔴" : st.Median > 4000 ? " 🟡" : "")} | {Sec(st.Max)} | {Sec(st.Last)} |");
        }
        L.AppendLine();

        // ---- thao tác chậm
        L.AppendLine($"## Thao tác chậm (> {Sec(i.SlowMs)}, theo logs/cham.log, 7 ngày)");
        var groups = Perf.Summary(i.Slow).OrderByDescending(g => g.AvgMs * Math.Sqrt(g.Count)).Take(15).ToList();
        if (groups.Count == 0) L.AppendLine("- không có thao tác nào chậm trong 7 ngày");
        else
        {
            L.AppendLine("| Thao tác | Số lần | Trung bình | Lớn nhất | Gần nhất |");
            L.AppendLine("|---|---|---|---|---|");
            foreach (var g in groups)
                L.AppendLine($"| {TroLyReport.Mask(g.Op + (g.Detail.Length > 0 ? " · " + g.Detail : ""), true)} | {g.Count} | {Sec(g.AvgMs)}{(g.AvgMs > 10000 ? " 🔴" : g.AvgMs > 5000 ? " 🟡" : "")} | {Sec(g.MaxMs)} | {g.Last.ToString("dd/MM HH:mm", Inv)} |");
        }
        L.AppendLine();

        // ---- dữ liệu
        L.AppendLine("## Dữ liệu (database trên máy)");
        if (i.Db is { } d)
        {
            L.AppendLine($"- File database {DbStats.Human(d.FileBytes)}" + (d.WalBytes > 0 ? $" (+ {DbStats.Human(d.WalBytes)} đang ghi)" : "") + $" · dữ liệu module {DbStats.Human(d.DataBytes)} · lịch sử phiên bản bảng {DbStats.Human(d.HistoryBytes)} ({d.HistoryRows:#,0} bản) · nhật ký thao tác {d.AuditRows:#,0} dòng");
            if (d.Modules.Count > 0) L.AppendLine("- Module lớn nhất: " + string.Join(" · ", d.Modules.Take(8).Select(m => $"{m.Module} {DbStats.Human(m.Bytes)} ({m.Tables} bảng)")));
            if (d.Tables.Count > 0)
            {
                L.AppendLine("- Bảng lớn nhất (module › bảng: dung lượng · số dòng):");
                foreach (var t in d.Tables.Take(10)) L.AppendLine($"  - {t.Module} › {t.Sheet}: {DbStats.Human(t.Bytes)} · {t.Rows:#,0} dòng");
            }
        }
        else L.AppendLine("- (không đọc được thống kê database)");
        L.AppendLine();

        // ---- cảnh báo
        var fs = i.Findings.Where(f => f.Mod == PerfChecks.Mod).ToList();
        L.AppendLine("## Cảnh báo hiệu năng Trợ lý đã phát hiện");
        if (fs.Count == 0) L.AppendLine("- không có (lượt quét gần nhất)");
        foreach (var f in fs)
        {
            L.AppendLine($"- {(f.Sev == "do" ? "🔴" : f.Sev == "vang" ? "🟡" : "🟢")} {TroLyReport.Mask(f.Title, true)} — {TroLyReport.Mask(f.Detail, true)}");
            foreach (var e in f.Evidence.Take(4)) L.AppendLine("  - " + TroLyReport.Mask(e, true));
        }
        return L.ToString();
    }
}
