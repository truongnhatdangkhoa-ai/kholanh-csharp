using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// Một phát hiện của Trợ lý hệ thống. Sev: "do" (nặng) · "vang" (cần xem) · "xanh" (ổn).
/// Id ổn định giữa các lần quét (bỏ chữ số trong tiêu đề) để hàng đợi đề xuất nhận ra cùng một cảnh báo.
/// </summary>
public sealed class Finding
{
    public string Id { get; set; } = "";
    public string Cat { get; set; } = "";
    public string Sev { get; set; } = "vang";
    public string Mod { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public List<string> Evidence { get; set; } = new();
    public List<string> Files { get; set; } = new();
    /// <summary>Lỗi do file nguồn (Excel / phần mềm kho), không phải lỗi của app.</summary>
    public bool Nguon { get; set; }
    /// <summary>Mã có thể thêm vào Danh sách mã hàng (cảnh báo "Mã hợp lệ nhưng chưa có trong Danh sách mã hàng").</summary>
    public List<MaFix>? Fixes { get; set; }

    // gắn từ hàng đợi khi hiển thị
    public string Q { get; set; } = "";
    public int Recur { get; set; }
    public string QNote { get; set; } = "";

    public string SevLabel => Sev switch { "do" => "Nặng", "vang" => "Cần xem", _ => "Ổn" };
}

public sealed record MaFix(string Ma, string Ten, string Near, string Src);

/// <summary>
/// Các kiểm tra KỸ THUẬT của Trợ lý hệ thống (viết lại theo core/tro-ly/checks.js của app cũ). HÀM THUẦN: nhận dữ liệu thường,
/// trả về danh sách phát hiện — không đọc đĩa / mạng, không ghi gì (TroLyService lo thu thập) → test được bằng dữ liệu giả.
/// </summary>
public static class TroLyChecks
{
    public const int MaxEvid = 6;
    public const double MB = 1048576;

    /// <summary>Loại lỗi → file nghi ngờ (gợi ý điều tra cho Claude, KHÔNG phải kết luận nguyên nhân).</summary>
    public static readonly Dictionary<string, string[]> FileMap = new()
    {
        ["ghi-phieu"] = new[] { "src/KhoLanh.Core/PhieuXuat/PxhtService.cs", "src/KhoLanh.Core/ViTri/ViTriService.cs", "src/KhoLanh.Core/TonKhoGui/GuiService.cs", "src/KhoLanh.Core/TonKho/AnAnService.cs" },
        ["trung-khoa"] = new[] { "src/KhoLanh.Core/Modules/ModuleBridge.cs › WriteSheets", "src/KhoLanh.Core/Lan/SheetMerge.cs", "Service của module liên quan (nơi thêm dòng)" },
        ["sao-luu"] = new[] { "src/KhoLanh.Core/Services/BackupService.cs", "src/KhoLanh.App/App.axaml.cs › DailyAutoBackup" },
        ["dong-bo"] = new[] { "src/KhoLanh.Core/Lan/LanService.cs", "src/KhoLanh.Core/Lan/SheetMerge.cs", "src/KhoLanh.Core/Lan/DriveSync.cs" },
        ["csdl"] = new[] { "src/KhoLanh.Core/Data/Database.cs", "src/KhoLanh.Core/Modules/ModuleBridge.cs" },
        ["loi-app"] = new[] { "logs/loi.log (chi tiết lỗi)", "src/KhoLanh.App/Views/<màn hình phát sinh lỗi>" },
        ["cham"] = new[] { "logs/cham.log (thao tác > 2 giây)", "src/KhoLanh.Core/TroLy/Perf.cs" },
        ["nghiep-vu"] = new[] { "src/KhoLanh.Core/PhieuXuat/PxhtService.cs", "src/KhoLanh.Core/ViTri/ViTriService.cs", "src/KhoLanh.Core/TonKho/AnAnService.cs", "src/KhoLanh.Core/TonKhoGui/GuiService.cs" },
        ["ma-hang"] = new[] { "src/KhoLanh.Core/TonKho/NapExcel.cs (nạp Load Data)", "src/KhoLanh.Core/ViTri/ViTriService.cs › Import", "src/KhoLanh.Core/TongHop/TongHopService.cs", "file Excel nguồn" },
        ["cheo"] = new[] { "src/KhoLanh.Core/TonKho/AnAnService.cs", "src/KhoLanh.Core/ViTri/ViTriService.cs", "src/KhoLanh.Core/TonKhoGui/GuiService.cs" },
    };

    // ------------------------------------------------------------------ tiện ích

    public static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s)))[..12].ToLowerInvariant();

    /// <summary>Tạo phát hiện; id = băm (loại | nơi | tiêu đề bỏ chữ số) nếu chưa đặt.</summary>
    public static Finding Mk(string cat, string sev, string mod, string title, string detail, IEnumerable<string>? evidence = null, string? id = null, IEnumerable<string>? files = null) => new()
    {
        Id = id ?? Hash(cat + "|" + mod + "|" + Regex.Replace(title, @"\d+", "#")),
        Cat = cat, Sev = sev, Mod = mod, Title = title, Detail = detail,
        Evidence = (evidence ?? Enumerable.Empty<string>()).ToList(),
        Files = (files ?? FileMap.GetValueOrDefault(cat) ?? Array.Empty<string>()).ToList(),
    };

    public static List<string> Cut(IEnumerable<string> a, int n = MaxEvid) => a.Take(n).ToList();

    public static string S(JsonNode? n)
    {
        if (n is null) return "";
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s.Trim();
            if (v.TryGetValue<double>(out var d)) return d.ToString("0.############", CultureInfo.InvariantCulture);
            if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        }
        return n.ToJsonString().Trim();
    }

    /// <summary>Số kiểu JS Number(): rỗng / không đọc được = NaN.</summary>
    public static double Num(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return double.NaN;
        return double.TryParse(s.Replace(",", "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;
    }

    public static string Nf(double n) => (Math.Round(n * 100) / 100).ToString("#,0.##", KhoLanh.Core.Grid.NumFmt.Culture);
    public static string Dmy(string iso) => Regex.Match(iso ?? "", @"^(\d{4})-(\d{2})-(\d{2})") is { Success: true } m ? $"{m.Groups[3]}/{m.Groups[2]}/{m.Groups[1]}" : iso ?? "";

    private static readonly Dictionary<string, int> SevOrder = new() { ["do"] = 0, ["vang"] = 1, ["xanh"] = 2 };
    public static List<Finding> Sort(IEnumerable<Finding> a) =>
        a.OrderBy(x => SevOrder.GetValueOrDefault(x.Sev, 3)).ThenBy(x => x.Mod, StringComparer.Ordinal).ToList();

    // ------------------------------------------------------------------ 1) Ghi phiếu

    /// <summary>Phiếu xuất dạng chung (gom từ các module): Id nội bộ, số phiếu, module, đã hủy?, có đang trừ tồn?</summary>
    public sealed record Phieu(string Id, string MaPhieu, string Module, string NgayTao, bool Huy = false, bool? Deducted = null);
    public sealed record PhieuItem(string PhieuId, string MaHang, string SoLuongXuat, string ViTri = "");
    public sealed record PhieuCol(List<Phieu> Phieu, List<PhieuItem> Items);

    public static List<Finding> CheckPhieu(PhieuCol? col, string label)
    {
        var o = new List<Finding>();
        var phieu = col?.Phieu ?? new(); var items = col?.Items ?? new();
        var seenMa = new Dictionary<string, int>();
        foreach (var p in phieu)
            if (p.MaPhieu.Length > 0) { var k = p.Module + "|" + p.MaPhieu; seenMa[k] = seenMa.GetValueOrDefault(k) + 1; }
        var dupMa = seenMa.Where(x => x.Value > 1).ToList();
        if (dupMa.Count > 0) o.Add(Mk("ghi-phieu", "do", label, "Trùng số phiếu", $"{dupMa.Count} số phiếu xuất hiện nhiều lần trong cùng module (có thể phiếu bị ghi 2 lần).",
            Cut(dupMa.Select(x => x.Key.Replace("|", " · ") + " ×" + x.Value))));
        var ids = phieu.Select(p => p.Id).ToHashSet();
        var orphan = items.Where(i => !ids.Contains(i.PhieuId)).ToList();
        if (orphan.Count > 0) o.Add(Mk("ghi-phieu", "do", label, "Dòng hàng mồ côi (không có phiếu)", $"{orphan.Count} dòng hàng trỏ tới phiếu không tồn tại (phiếu ghi dở hoặc bị xóa mất phần đầu).",
            Cut(orphan.Select(i => "phiếu " + i.PhieuId + " · " + i.MaHang))));
        var hasItems = items.Select(i => i.PhieuId).ToHashSet();
        var empty = phieu.Where(p => !hasItems.Contains(p.Id) && !p.Huy).ToList();
        if (empty.Count > 0) o.Add(Mk("ghi-phieu", "vang", label, "Phiếu không có dòng hàng", $"{empty.Count} phiếu chưa hủy nhưng không có dòng hàng nào (ghi dở?).",
            Cut(empty.Select(p => p.MaPhieu.Length > 0 ? p.MaPhieu : p.Id))));
        var bad = items.Where(i => !(Num(i.SoLuongXuat) > 0)).ToList();
        if (bad.Count > 0) o.Add(Mk("ghi-phieu", "vang", label, "Số lượng xuất bất thường (≤ 0 hoặc không phải số)", $"{bad.Count} dòng hàng có số lượng xuất không hợp lệ.",
            Cut(bad.Select(i => "phiếu " + i.PhieuId + " · " + i.MaHang + " · SL=" + i.SoLuongXuat))));
        var noCode = items.Where(i => i.MaHang.Length == 0).ToList();
        if (noCode.Count > 0) o.Add(Mk("ghi-phieu", "vang", label, "Dòng hàng thiếu mã hàng", $"{noCode.Count} dòng hàng trong phiếu không có mã hàng.",
            Cut(noCode.Select(i => "phiếu " + i.PhieuId))));
        return o;
    }

    // ------------------------------------------------------------------ 2) Dòng trùng khóa

    private static readonly (string T, Regex Re)[] Ident =
    {
        ("ma", new(@"^(mã ?hàng|ma ?hang|mã|code|mahang|magoc)$", RegexOptions.IgnoreCase)),
        ("lo", new(@"^(lô|lo|số ?lô|so ?lo|lô \(batch\)|solo)$", RegexOptions.IgnoreCase)),
        ("phieu", new(@"^(số ?phiếu|so ?phieu|mã ?phiếu|ma ?phieu|phiếu ?nhập|phieu ?nhap|phiếu nhập/voucher|maphieu|phieunhap)$", RegexOptions.IgnoreCase)),
        ("size", new(@"^size$", RegexOptions.IgnoreCase)),
        ("vitri", new(@"^(vị ?trí|vi ?tri|vitri)$", RegexOptions.IgnoreCase)),
    };

    private static List<(string T, string Col)> IdentCols(JsonObject sample)
    {
        var found = new List<(string, string)>();
        foreach (var (k, _) in sample)
            if (Ident.FirstOrDefault(t => t.Re.IsMatch(k.Trim())) is { Re: not null } t) found.Add((t.T, k));
        return found;
    }

    public static List<Finding> FindDupKeys(string modLabel, string sheetName, JsonArray? rows)
    {
        var o = new List<Finding>();
        if (rows is null || rows.Count < 2) return o;
        var objs = rows.OfType<JsonObject>().ToList();
        if (objs.Count < 2) return o;
        var where = modLabel + " › " + sheetName;
        // (a) dòng GIỐNG HỆT nhau (bỏ cột rỗng)
        var sig = new Dictionary<string, (int N, List<string> Vals)>();
        foreach (var r in objs)
        {
            var pairs = r.Select(p => (p.Key, V: S(p.Value))).Where(p => p.V.Length > 0).OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
            if (pairs.Count == 0) continue;
            var s = string.Join("\u0001", pairs.Select(p => p.Key + "\u0002" + p.V));
            sig[s] = sig.TryGetValue(s, out var e) ? (e.N + 1, e.Vals) : (1, pairs.Select(p => p.V).ToList());
        }
        var same = sig.Values.Where(x => x.N > 1).ToList();
        var sameRows = same.Sum(x => x.N);
        if (same.Count > 0) o.Add(Mk("trung-khoa", "vang", where, "Dòng trùng hoàn toàn", $"{same.Count} nhóm dòng giống hệt nhau (tổng {sameRows} dòng).",
            Cut(same.Select(x => "×" + x.N + " " + string.Join(" | ", x.Vals.Take(4))))));
        // (b) trùng KHÓA nhận diện: ≥ 3 cột nhận diện, có mã hàng và (lô hoặc số phiếu) — mã + size / mã + vị trí lặp là bình thường ở bảng tồn
        var cols = IdentCols(objs[0]);
        if (cols.Count >= 3 && cols.Any(c => c.T == "ma") && cols.Any(c => c.T is "lo" or "phieu"))
        {
            var m = new Dictionary<string, int>();
            foreach (var r in objs)
            {
                var vals = cols.Select(c => S(r[c.Col])).ToList();
                if (vals.All(v => v.Length == 0)) continue;
                var k = string.Join("¦", vals);
                m[k] = m.GetValueOrDefault(k) + 1;
            }
            var dup = m.Where(x => x.Value > 1).ToList();
            var dupRows = dup.Sum(x => x.Value);
            if (dup.Count > 0 && dupRows > sameRows)
                o.Add(Mk("trung-khoa", "vang", where, "Nghi trùng khóa (" + string.Join(" + ", cols.Select(c => c.Col)) + ")",
                    $"{dup.Count} khóa xuất hiện nhiều lần (nội dung các cột khác có thể khác nhau).",
                    Cut(dup.Select(x => "×" + x.Value + " " + x.Key.Replace("¦", " · ")))));
        }
        return o;
    }

    // ------------------------------------------------------------------ 3) Sức khỏe cơ sở dữ liệu

    public sealed class DbInfo
    {
        public string Failed = "";
        public string Integrity = "ok";
        public long FileBytes, WalBytes;
        public long AuditRows, HistoryRows, Sheets, SheetRows;
        public long PageCount, FreePages;
        public int Conflicts;
        public int Pending;
        public string PendingOldest = "";
    }

    public sealed class DbThresholds
    {
        public double BytesWarn = 800 * MB, WalWarn = 200 * MB;
        public long AuditWarn = 300_000, HistoryWarn = 5_000;
        public double FreeRatioWarn = 0.3;
        public long FreeMinPages = 5_000;
    }

    public static List<Finding> CheckDb(DbInfo? info, DbThresholds? th = null)
    {
        th ??= new(); var o = new List<Finding>(); const string M = "Cơ sở dữ liệu";
        if (info is null) return o;
        if (info.Failed.Length > 0) o.Add(Mk("csdl", "do", M, "Không mở được cơ sở dữ liệu để kiểm tra", info.Failed[..Math.Min(300, info.Failed.Length)]));
        if (info.Integrity.Length > 0 && info.Integrity != "ok")
            o.Add(Mk("csdl", "do", M, "integrity_check KHÔNG đạt", "SQLite báo file dữ liệu có thể hỏng — sao lưu ngay ra file riêng rồi báo người hỗ trợ.", new[] { info.Integrity[..Math.Min(300, info.Integrity.Length)] }));
        if (info.FileBytes > th.BytesWarn) o.Add(Mk("csdl", "vang", M, "File dữ liệu lớn", $"Dung lượng {Math.Round(info.FileBytes / MB)} MB (ngưỡng {Math.Round(th.BytesWarn / MB)} MB)."));
        if (info.WalBytes > th.WalWarn) o.Add(Mk("csdl", "vang", M, "File nhật ký ghi (khodata.db-wal) phình to", $"{Math.Round(info.WalBytes / MB)} MB — thường do có kết nối mở lâu không đóng; mở lại phần mềm để gộp vào file chính."));
        if (info.AuditRows > th.AuditWarn) o.Add(Mk("csdl", "vang", M, "Nhật ký thao tác phình to", $"{info.AuditRows} dòng nhật ký (ngưỡng {th.AuditWarn}) — làm file dữ liệu và bản sao lưu nặng thêm."));
        if (info.HistoryRows > th.HistoryWarn) o.Add(Mk("csdl", "vang", M, "Lịch sử bản trước của các bảng phình to", $"{info.HistoryRows} bản lưu trong sheet_history (ngưỡng {th.HistoryWarn}) — bình thường chỉ giữ 3 bản gần nhất mỗi bảng."));
        if (info.PageCount > 0 && info.FreePages > th.FreeMinPages && info.FreePages > info.PageCount * th.FreeRatioWarn)
            o.Add(Mk("csdl", "vang", M, "File dữ liệu có nhiều chỗ trống", $"{info.FreePages:#,0} / {info.PageCount:#,0} trang đang trống ({Math.Round(100.0 * info.FreePages / info.PageCount)}%) — file to hơn cần thiết, sao lưu chậm hơn."));
        if (info.Conflicts > 0) o.Add(Mk("dong-bo", "vang", "Mạng LAN", "Có xung đột gộp dữ liệu chưa xem", $"{info.Conflicts} xung đột (2 máy cùng sửa một bảng) đang chờ Quản trị xem ở Hệ thống › Mạng LAN."));
        if (o.Count == 0) o.Add(Mk("csdl", "xanh", M, "Cơ sở dữ liệu ổn", $"integrity_check đạt, {Math.Round(info.FileBytes / MB)} MB, {info.Sheets} bảng module ({info.SheetRows:#,0} dòng), {info.AuditRows:#,0} dòng nhật ký."));
        return o;
    }

    // ------------------------------------------------------------------ 4) Sao lưu & phục hồi

    public sealed record BackupFile(string Name, DateTime Modified, long Bytes);
    /// <summary>Kết quả thử mở bản sao lưu mới nhất (Quét sâu): Ok, lỗi, số bảng module, tổng dòng.</summary>
    public sealed record RestoreProbe(bool Ok, string Error, int Sheets, long Rows);

    /// <summary>
    /// Bản chụp an toàn "truoc-…" (truoc-doi-vai-… lúc máy nhận vai LAN mới, truoc-khoi-phuc_… lúc khôi phục): chụp dữ liệu CŨ ngay
    /// trước khi bị thay hết, nên không dùng làm "bản sao lưu mới nhất" để so số dòng với dữ liệu hiện tại.
    /// </summary>
    public static bool IsSnapshot(string name) => name.StartsWith("truoc-", StringComparison.OrdinalIgnoreCase);

    /// <summary>Bản sao lưu đều đặn mới nhất (theo giờ sửa file), không có thì bản mới nhất bất kỳ. Trợ lý thử mở (Quét sâu) đúng bản này.</summary>
    public static BackupFile? PickLatest(IEnumerable<BackupFile> backups)
    {
        var all = backups.OrderByDescending(b => b.Modified).ToList();
        return all.FirstOrDefault(b => !IsSnapshot(b.Name)) ?? all.FirstOrDefault();
    }

    public static List<Finding> CheckBackup(List<BackupFile> backups, long liveRows, RestoreProbe? probe, DateTime now, double staleDays = 2, double shrinkRatio = 0.5)
    {
        var o = new List<Finding>(); const string M = "Sao lưu tự động";
        var latest = PickLatest(backups);
        if (latest is null)
        {
            if (liveRows > 0) o.Add(Mk("sao-luu", "vang", M, "Chưa có bản sao lưu", $"Dữ liệu có {liveRows:#,0} dòng nhưng thư mục SaoLuu chưa có bản nào (app tự sao lưu mỗi ngày khi mở)."));
            return o;
        }
        var age = (now - latest.Modified).TotalDays;
        if (age > staleDays) o.Add(Mk("sao-luu", age > 7 ? "do" : "vang", M, "Bản sao lưu cũ", $"Bản mới nhất cách đây {age.ToString("0.0", CultureInfo.InvariantCulture)} ngày.", new[] { latest.Name }));
        if (latest.Bytes == 0) o.Add(Mk("sao-luu", "do", M, "Bản sao lưu mới nhất rỗng (0 byte)", "File sao lưu không có nội dung — lần sao lưu đó bị ngắt giữa chừng.", new[] { latest.Name }));
        if (probe is not null)
        {
            if (!probe.Ok) o.Add(Mk("sao-luu", "do", M, "Thử phục hồi THẤT BẠI", "Không đọc được bản sao lưu mới nhất: " + probe.Error[..Math.Min(200, probe.Error.Length)], new[] { latest.Name }));
            else if (liveRows > 20 && probe.Rows < liveRows * shrinkRatio && age < 1
                     && backups.Where(b => IsSnapshot(b.Name) && b.Modified > latest.Modified).OrderByDescending(b => b.Modified).FirstOrDefault() is { } swap)
                // Sau bản sao lưu đó máy đã đổi vai LAN / khôi phục (có bản chụp truoc-… mới hơn) → dữ liệu được thay hết, số dòng khác là đúng.
                o.Add(Mk("sao-luu", "xanh", M, "Thử phục hồi đạt", $"Bản {latest.Name} đọc được: {probe.Sheets} bảng, {probe.Rows:#,0} dòng. Hiện có {liveRows:#,0} dòng vì dữ liệu đã được thay lúc {swap.Modified:dd/MM HH:mm} ({(swap.Name.StartsWith("truoc-doi-vai", StringComparison.OrdinalIgnoreCase) ? "máy nhận vai LAN mới, lấy dữ liệu máy chủ" : "khôi phục bản sao lưu")}); bản tự động của ngày mai sẽ chụp dữ liệu mới.", new[] { latest.Name, swap.Name }));
            else if (liveRows > 20 && probe.Rows < liveRows * shrinkRatio && age < 1)
                o.Add(Mk("sao-luu", "vang", M, "Bản sao lưu ít dòng hơn nhiều so với dữ liệu hiện tại", $"Bản sao lưu {probe.Rows:#,0} dòng, hiện có {liveRows:#,0} dòng dù bản sao lưu mới (<1 ngày).", new[] { latest.Name }));
            else o.Add(Mk("sao-luu", "xanh", M, "Thử phục hồi đạt", $"Bản {latest.Name} đọc được: {probe.Sheets} bảng, {probe.Rows:#,0} dòng, kiểm tra toàn vẹn đạt."));
        }
        else if (o.Count == 0) o.Add(Mk("sao-luu", "xanh", M, "Sao lưu tự động đều đặn", $"Bản mới nhất {latest.Name} ({latest.Modified:dd/MM/yyyy HH:mm}), có {backups.Count} bản. Bấm Quét sâu để thử mở bản sao lưu."));
        return o;
    }

    // ------------------------------------------------------------------ 5) Đồng bộ (LAN / Google Drive / file tạm)

    public sealed class SyncInfo
    {
        public List<(string Name, double AgeMin)> TmpFiles = new();
        public bool LanEnabled;
        public string LanRole = "";
        public int LanHealth;              // 0 tắt · 1 tốt · 2 mất máy chủ / làm riêng
        public string LanListenError = "";
        public int Pending;                // số bảng trong sổ chờ (máy con chưa gửi lên)
        public double PendingAgeMin = double.NaN;
        public bool DriveEnabled;
        public string DriveStatus = "";
        public double DriveOkAgeMin = double.NaN;
    }

    public static List<Finding> CheckSync(SyncInfo? info, double tmpMin = 10, double staleMin = 60)
    {
        var o = new List<Finding>();
        if (info is null) return o;
        var stuck = info.TmpFiles.Where(f => f.AgeMin > tmpMin).ToList();
        if (stuck.Count > 0) o.Add(Mk("dong-bo", "vang", "Thư mục dữ liệu", "File tạm dở dang", $"{stuck.Count} file tạm tồn tại quá {tmpMin} phút (thao tác ghi / tải bị ngắt giữa chừng?).",
            Cut(stuck.Select(f => f.Name + " (" + Math.Round(f.AgeMin) + " phút)"))));
        if (info.LanEnabled)
        {
            const string L = "Mạng LAN";
            if (info.LanListenError.Length > 0) o.Add(Mk("dong-bo", "do", L, "Máy chủ LAN không mở được cổng", info.LanListenError[..Math.Min(250, info.LanListenError.Length)]));
            if (info.LanHealth == 2) o.Add(Mk("dong-bo", "do", L, "Không nối được máy chủ LAN", "Máy này đang làm riêng trên dữ liệu của mình; có máy chủ lại sẽ tự gửi lên và gộp. Kiểm tra máy chủ còn bật / cùng mạng."));
            if (info.Pending > 0 && info.PendingAgeMin > staleMin)
                o.Add(Mk("dong-bo", "vang", L, "Thay đổi chờ gửi lên máy chủ lâu", $"{info.Pending} bảng sửa trên máy này chưa gửi được lên máy chủ, lâu nhất {Math.Round(info.PendingAgeMin)} phút."));
        }
        if (info.DriveEnabled)
        {
            const string D = "Đồng bộ Google Drive";
            if (Regex.IsMatch(info.DriveStatus, "lỗi|không", RegexOptions.IgnoreCase)) o.Add(Mk("dong-bo", "vang", D, "Đồng bộ Google Drive báo lỗi", info.DriveStatus[..Math.Min(250, info.DriveStatus.Length)]));
            else if (info.DriveOkAgeMin > staleMin * 6) o.Add(Mk("dong-bo", "vang", D, "Lâu chưa đồng bộ được qua Google Drive", $"Lần thành công gần nhất cách đây {Math.Round(info.DriveOkAgeMin)} phút."));
        }
        if (o.Count == 0) o.Add(Mk("dong-bo", "xanh", "Đồng bộ", "Đồng bộ ổn",
            info.LanEnabled || info.DriveEnabled ? "Không thấy file dở dang, sổ chờ tồn đọng hay lỗi đồng bộ." : "Máy đơn (không dùng LAN / Drive), không có file tạm dở dang."));
        return o;
    }

    // ------------------------------------------------------------------ 6) Lỗi của app (logs/loi.log + lỗi chưa bắt)

    public sealed record AppError(string Mod, string Msg, string Type, int Count, DateTime Last, string Stack);

    /// <summary>Đọc logs/loi.log (Ui.Describe + lỗi chưa bắt ghi vào) → gom theo lời nhắn + loại lỗi, chỉ lấy trong <paramref name="days"/> ngày.</summary>
    public static List<AppError> ParseErrorLog(string text, DateTime now, int days = 7)
    {
        var res = new Dictionary<string, AppError>();
        var head = new Regex(@"^(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})(?: bản (\S+))?(?: \[(.+?)\])?\s*$");
        var blocks = new List<(DateTime At, string Where, List<string> Lines)>();
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var m = head.Match(raw);
            if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at))
            { blocks.Add((at, m.Groups[3].Value, new())); continue; }
            if (blocks.Count > 0 && raw.Trim().Length > 0) blocks[^1].Lines.Add(raw);
        }
        foreach (var b in blocks)
        {
            if ((now - b.At).TotalDays > days || b.Lines.Count == 0) continue;
            var first = b.Lines[0].Trim();
            var tm = Regex.Match(first, @"^([\w.`]+(?:Exception|Error))(?::\s*(.*))?$");
            var type = tm.Success ? tm.Groups[1].Value.Split('.').Last() : "";
            var msg = tm.Success ? (tm.Groups[2].Value.Length > 0 ? tm.Groups[2].Value : type) : first;
            if (type == "BusinessException") continue; // lỗi nghiệp vụ đã báo người dùng — không phải lỗi app
            var at = b.Lines.FirstOrDefault(l => Regex.IsMatch(l, @"^\s*at KhoLanh\.")) ?? "";
            var mod = b.Where.Length > 0 ? b.Where : ModOfStack(at);
            var key = type + "|" + msg + "|" + at.Trim();
            var stack = string.Join(" ⏎ ", b.Lines.Take(4).Select(l => l.Trim()));
            res[key] = res.TryGetValue(key, out var e)
                ? e with { Count = e.Count + 1, Last = b.At > e.Last ? b.At : e.Last }
                : new AppError(mod, msg, type, 1, b.At, stack);
        }
        return res.Values.ToList();
    }

    private static string ModOfStack(string at)
    {
        var m = Regex.Match(at, @"at KhoLanh\.(?:App\.Views|Core)\.(\w+)");
        return m.Success ? m.Groups[1].Value : "Phần mềm";
    }

    public static List<Finding> CheckErrors(IEnumerable<AppError> errs) => errs.Select(e =>
    {
        var f = Mk("loi-app", e.Count >= 5 ? "do" : "vang", e.Mod, "Lỗi phần mềm: " + Short(e.Msg, 90),
            $"Xuất hiện {e.Count} lần trong 7 ngày, lần cuối {e.Last:dd/MM/yyyy HH:mm}" + (e.Type.Length > 0 ? " · " + e.Type : ""),
            e.Stack.Length > 0 ? new[] { e.Stack } : null, Hash("loi-app|" + e.Mod + "|" + e.Type + "|" + e.Msg));
        return f;
    }).ToList();

    /// <summary>1.72 (A4): thao tác chạy quá 2 giây trong 7 ngày (logs/cham.log) — mỗi thao tác 1 phát hiện, ≥ 10 giây hoặc lặp ≥ 5 lần = vàng.</summary>
    public static List<Finding> CheckSlow(IEnumerable<Perf.Entry> list)
    {
        var res = new List<Finding>();
        foreach (var g in Perf.Summary(list).Take(12))
        {
            var sev = g.MaxMs >= 10_000 || g.Count >= 5 ? "vang" : "xanh";
            res.Add(Mk("cham", sev, g.Op, $"Thao tác chậm: {g.Op}" + (g.Detail.Length > 0 ? " · " + Short(g.Detail, 60) : ""),
                $"{g.Count} lần trong 7 ngày · chậm nhất {g.MaxMs / 1000.0:0.0} giây · trung bình {g.AvgMs / 1000.0:0.0} giây · lần cuối {g.Last:dd/MM/yyyy HH:mm}",
                null, Hash("cham|" + g.Op + "|" + g.Detail)));
        }
        return res;
    }

    public static string Short(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
