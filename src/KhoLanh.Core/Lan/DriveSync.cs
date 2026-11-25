using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.Lan;

/// <summary>
/// ĐỒNG BỘ QUA THƯ MỤC (Google Drive for Desktop): dữ liệu chính làm việc + sao lưu trên máy tính bình thường; máy ở mạng khác
/// (không nối được LAN) tải dữ liệu về xem, và (tùy chọn) gửi thay đổi ngược lên.
///   Máy chính  (chinh): chụp toàn bộ dữ liệu → snap/s-NNNNNN.klsync + latest.json; nhận in/*.klsync của máy xa, gộp (3 bên) như máy con LAN,
///                       trả kết quả vào out/&lt;máy&gt;.klsync.
///   Máy xa     (xa)   : tải bản mới nhất về database riêng; sửa gì thì vào sổ chờ → in/…; máy chính gộp rồi chụp lại → máy xa nhận.
/// Mọi tệp (trừ latest.json) mã hóa AES-256-GCM bằng "mã đồng bộ" nên Google Drive / người khác trong thư mục chung không đọc / sửa được.
/// </summary>
public sealed class DriveSyncService
{
    public const string ModeOff = "", ModeMain = "chinh", ModeRemote = "xa", CodePrefix = "KLDRV1-";
    public const string Folder = "KhoLanh-Sync";

    private readonly Func<LanService> _lan;
    private readonly SettingsService _settings;
    private CancellationTokenSource? _cts;
    private readonly object _run = new();
    private long _lastPubSeq = -1;
    private readonly Dictionary<string, DateTime> _pushedAt = new();

    public DriveSyncService(Func<LanService> lan, SettingsService settings) { _lan = lan; _settings = settings; }

    public string Mode => _settings.Get("drive_mode");
    public string Dir => _settings.Get("drive_dir");
    public bool Enabled => Mode is ModeMain or ModeRemote && Dir.Length > 0 && Key().Length > 0;
    public string Status { get; private set; } = "";
    /// <summary>Tài khoản đang đăng nhập (ghi vào khóa máy chính để các máy khác biết ai đang giữ).</summary>
    public string CurrentUser { get; set; } = "";
    public DateTime LastOk { get; private set; }
    /// <summary>Chu kỳ đồng bộ (giây): cài theo máy (drive_every, mặc định 5 phút); 0 = chỉ đồng bộ khi mở app / bấm "Đồng bộ ngay" / thoát app.</summary>
    public int IntervalSeconds
    {
        get => _intervalOverride ?? (int.TryParse(_settings.Get("drive_every"), out var s) && s >= 0 ? s : 300);
        set => _intervalOverride = value;
    }
    private int? _intervalOverride;
    public void SetInterval(int seconds) { _settings.Set("drive_every", Math.Max(0, seconds).ToString()); _intervalOverride = null; }
    public static readonly (int Sec, string Label)[] Intervals = { (45, "45 giây"), (300, "5 phút"), (900, "15 phút"), (1800, "30 phút"), (3600, "60 phút"), (0, "Chỉ khi mở app / bấm Đồng bộ ngay / thoát app") };

    /// <summary>MÁY Ở NHÀ: luôn là máy xem từ xa, không bao giờ tự nhận làm máy chính (trừ khi Quản trị bấm "Chiếm quyền" lúc máy công ty hỏng).</summary>
    // 1.98: bỏ chế độ "máy ở nhà" (đã có Drive cá nhân) — cờ cũ settings "drive_home" không còn tác dụng, xóa khi lưu chu kỳ đồng bộ.
    public void ClearOldHomeFlag() { if (_settings.Get("drive_home").Length > 0) _settings.Set("drive_home", ""); }

    /// <summary>Tạo file sao lưu .db (BackupService.BackupTo) — máy chính dùng để sao lưu hằng ngày lên Drive.</summary>
    public Func<string, string>? MakeBackup { get; set; }
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;
    private string Root => Path.Combine(Dir, Folder);
    private string Key() => Secret.Unprotect(_settings.Get("drive_key"));

    // ------------------------------------------------------------------ cấu hình

    public static string NewKey() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public string Code()
    {
        var o = new JsonObject { ["k"] = Key() };
        return CodePrefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(o.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    public static string ParseCode(string code)
    {
        var t = new string((code ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!t.StartsWith(CodePrefix)) throw new BusinessException("Mã đồng bộ phải bắt đầu bằng " + CodePrefix);
        try
        {
            var b = t[CodePrefix.Length..].Replace('-', '+').Replace('_', '/'); b = b.PadRight(b.Length + (4 - b.Length % 4) % 4, '=');
            return JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(b)))!["k"]!.GetValue<string>();
        }
        catch { throw new BusinessException("Mã đồng bộ bị thiếu hoặc sai — chép lại đủ cả dòng."); }
    }

    /// <summary>mode: "" tắt · "chinh" máy chính (tạo khóa nếu chưa có) · "xa" máy xem từ xa (cần mã đồng bộ của máy chính).</summary>
    public void Configure(string mode, string dir, string? code, Session session)
    {
        session.RequireAdmin();
        if (mode is not (ModeOff or ModeMain or ModeRemote)) throw new BusinessException("Vai không hợp lệ.");
        if (mode != ModeOff)
        {
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) throw new BusinessException("Chọn thư mục đã có (thư mục của Google Drive trên máy này).");
            if (mode == ModeMain)
            {
                if (_lan().Role == LanService.RoleRemote) throw new BusinessException("Máy này đang là máy xem từ xa.");
                if (Key().Length == 0) _settings.Set("drive_key", Secret.Protect(NewKey()));
            }
            else
            {
                if (string.IsNullOrWhiteSpace(code)) { if (Key().Length == 0) throw new BusinessException("Dán mã đồng bộ chép từ máy chính."); }
                else _settings.Set("drive_key", Secret.Protect(ParseCode(code)));
                if (_lan().Role is LanService.RoleServer or LanService.RoleStandby or LanService.RoleClient)
                    throw new BusinessException("Máy này đang dùng LAN. Chuyển về “Máy đơn” ở Mạng LAN trước khi làm máy xem từ xa.");
            }
            _settings.Set("drive_dir", dir);
        }
        var old = _settings.Get("lan_role");
        _settings.Set("drive_mode", mode);
        if (mode == ModeRemote) _settings.Set("lan_role", LanService.RoleRemote);
        else if (old == LanService.RoleRemote) _settings.Set("lan_role", "");
        if (mode != old) _settings.Set("drive_seq", "0");
        _lastPubSeq = -1;
    }

    // ------------------------------------------------------------------ mã hóa tệp

    private byte[] AesKey() => SHA256.HashData(Encoding.UTF8.GetBytes(Key() + "|kholanh-drive-v1"));
    internal byte[] Seal(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(12); var ct = new byte[plain.Length]; var tag = new byte[16];
        using var a = new AesGcm(AesKey(), 16); a.Encrypt(nonce, plain, ct, tag);
        return nonce.Concat(ct).Concat(tag).ToArray();
    }
    internal byte[] Open(byte[] blob)
    {
        if (blob.Length < 28) throw new InvalidDataException("Tệp đồng bộ hỏng.");
        var plain = new byte[blob.Length - 28];
        try { using var a = new AesGcm(AesKey(), 16); a.Decrypt(blob.AsSpan(0, 12), blob.AsSpan(12, blob.Length - 28), blob.AsSpan(blob.Length - 16), plain); }
        catch (CryptographicException) { throw new InvalidDataException("Sai mã đồng bộ hoặc tệp bị sửa."); }
        return plain;
    }
    private static byte[] Gz(byte[] b) { using var ms = new MemoryStream(); using (var g = new GZipStream(ms, CompressionLevel.Optimal, true)) g.Write(b); return ms.ToArray(); }
    private static byte[] Ungz(byte[] b) { using var ms = new MemoryStream(b); using var g = new GZipStream(ms, CompressionMode.Decompress); using var o = new MemoryStream(); g.CopyTo(o); return o.ToArray(); }
    private byte[] Pack(JsonNode n) => Seal(Gz(Encoding.UTF8.GetBytes(n.ToJsonString())));
    private JsonNode Unpack(byte[] b) => JsonNode.Parse(Encoding.UTF8.GetString(Ungz(Open(b))))!;

    /// <summary>Ghi tệp qua tên tạm rồi đổi tên (Google Drive không thấy tệp ghi dở).</summary>
    private static void WriteAtomic(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp-" + Guid.NewGuid().ToString("N")[..6];
        File.WriteAllBytes(tmp, data);
        File.Move(tmp, path, true);
    }
    private static byte[]? TryRead(string path) { try { return File.ReadAllBytes(path); } catch { return null; } }

    // ------------------------------------------------------------------ vòng chạy nền

    public void Start()
    {
        if (_cts is not null || !Enabled) return;
        _cts = new CancellationTokenSource(); var ct = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try { SyncNow(); } catch { }
                var sec = IntervalSeconds;
                if (sec <= 0) break; // chỉ đồng bộ khi bấm: lượt đầu lúc mở app rồi dừng
                try { await Task.Delay(TimeSpan.FromSeconds(sec), ct); } catch { break; }
            }
            _cts = null;
        });
    }
    public void Stop() { _cts?.Cancel(); _cts = null; }

    /// <summary>Đồng bộ một lượt ngay. Trả dòng kết quả (cũng gán vào Status).</summary>
    public string SyncNow()
    {
        using var _perf = KhoLanh.Core.TroLy.Perf.Measure("Đồng bộ thư mục / Drive");
        lock (_run)
        {
            var lan = _lan();
            try
            {
                if (!Enabled) return Status = "Chưa bật đồng bộ qua Drive.";
                if (!Directory.Exists(Dir)) return Status = "Không thấy thư mục " + Dir + " (Google Drive chưa chạy?).";
                Directory.CreateDirectory(Root);
                Status = Mode == ModeMain ? RunMain(lan) : RunRemote(lan);
                LastOk = DateTime.Now;
            }
            catch (InvalidDataException e) { Status = "Lỗi: " + e.Message; }
            catch (Exception e) { Status = "Lỗi đồng bộ: " + e.Message; }
            if (Mode == ModeRemote) lan.RemoteStatus = Status.StartsWith("Lỗi") || Status.StartsWith("Không") ? Status : $"{DateTime.Now:HH:mm} " + Status;
            lan.RaiseChanged();
            return Status;
        }
    }

    // ------------------------------------------------------------------ khóa "máy chính" (chỉ một máy được làm máy chính)

    public sealed record Lease(string Id, string Name, string User, DateTime At, bool Released)
    {
        public const int StaleMinutes = 15;
        public bool Fresh => !Released && DateTime.UtcNow - At < TimeSpan.FromMinutes(StaleMinutes);
    }
    private string LeasePath => Path.Combine(Root, "main.lock");
    public Lease? ReadLease()
    {
        try
        {
            var b = TryRead(LeasePath); if (b is null) return null;
            var n = Unpack(b);
            return new Lease(n["id"]?.ToString() ?? "", n["ten"]?.ToString() ?? "", n["tk"]?.ToString() ?? "", DateTime.TryParse(n["luc"]?.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToUniversalTime() : DateTime.MinValue, n["xong"]?.GetValue<bool>() == true);
        }
        catch { return null; }
    }
    private void WriteLease(LanService lan, string user, bool released)
    {
        WriteAtomic(LeasePath, Pack(new JsonObject { ["id"] = lan.MachineId, ["ten"] = lan.MachineName, ["tk"] = user, ["luc"] = DateTime.UtcNow.ToString("o"), ["xong"] = released }));
    }
    /// <summary>Máy khác đang giữ khóa máy chính (còn hoạt động)?</summary>
    public Lease? HeldByOther(LanService lan) { var l = ReadLease(); return l is not null && l.Id != lan.MachineId && l.Fresh ? l : null; }

    /// <summary>Thư mục Drive trên máy này (mỗi máy một đường dẫn riêng).</summary>
    public void SetFolder(string dir) { if (!Directory.Exists(dir)) throw new BusinessException("Không thấy thư mục " + dir); _settings.Set("drive_dir", dir); }
    public void SetKey(string key) { if (key.Length > 0 && key != Key()) _settings.Set("drive_key", Secret.Protect(key)); }

    /// <summary>
    /// Máy này thành MÁY CHÍNH (từ máy xem từ xa hoặc từ tắt): tải bản mới nhất trên Drive, đưa các thay đổi chờ gửi của máy này
    /// qua cùng cơ chế gộp 3 bên (không mất việc đã làm ở máy xa), rồi giữ khóa máy chính. force = chiếm quyền dù khóa chưa hết hạn.
    /// </summary>
    public string BecomeMain(AppServices app, string user, bool force = false)
    {
        lock (_run)
        {
            var lan = _lan();
            if (Dir.Length == 0 || Key().Length == 0 || !Directory.Exists(Dir)) throw new BusinessException("Chưa chọn thư mục Drive hoặc chưa có mã đồng bộ.");
            Directory.CreateDirectory(Root);
            if (!force && HeldByOther(lan) is { } o) throw new BusinessException($"Máy “{o.Name}” (tài khoản {o.User}) đang là máy chính.");
            Stop();
            var pending = lan.PendingItems();
            Dictionary<(string, string), (long Ver, string Data)>? snap = null; long seq = 0;
            var lp = Path.Combine(Root, "latest.json");
            if (File.Exists(lp))
            {
                var latest = JsonNode.Parse(Encoding.UTF8.GetString(TryRead(lp) ?? Array.Empty<byte>()));
                if (latest is not null) { seq = latest["seq"]!.GetValue<long>(); snap = LoadCurrentSnap(lan, latest); }
            }
            // tải bản của máy chính cũ về những bảng KHÔNG có thay đổi chờ gửi
            var local = lan.LocalVersions(); int n = 0;
            if (snap is not null)
                foreach (var (k, v) in snap)
                    if (!lan.HasPendingSheet(k.Item1, k.Item2) && !(local.TryGetValue(k, out var lv) && lv == v.Ver) && lan.InstallSheet(k.Item1, k.Item2, v.Data, v.Ver)) n++;
            // bảng có thay đổi chờ gửi: đặt bản của máy chính cũ làm "bản hiện tại" rồi gộp thay đổi của mình lên trên
            if (snap is not null)
                foreach (var p in pending)
                    if (snap.TryGetValue((p["db"]!.ToString(), p["sheet"]!.ToString()), out var v)) lan.ForceInstallSheet(p["db"]!.ToString(), p["sheet"]!.ToString(), v.Data, v.Ver);
            _settings.Set("drive_mode", ModeMain);
            if (_settings.Get("lan_role") == LanService.RoleRemote) _settings.Set("lan_role", "");
            _settings.Set("drive_seq", seq.ToString()); _settings.Set("drive_pub_seq", seq.ToString()); _lastPubSeq = -1;
            app.ReloadLan(start: true);
            var main = app.Lan; var merged = 0;
            if (pending.Count > 0)
            {
                var res = main.ApplyExternal(pending, lan.MachineName);
                main.Acknowledge(pending.Select(x => x["op"]!.ToString()));
                merged = res.Count;
            }
            main.RemoteStatus = "";
            var msg = $"Máy này là máy chính: đã tải bản #{seq} ({n} bảng cập nhật" + (merged > 0 ? $", gộp {merged} bảng thay đổi chờ gửi" : "") + ")";
            Start();
            return msg;
        }
    }

    /// <summary>Máy này thành MÁY XEM TỪ XA (máy chính khác đang hoạt động). Dữ liệu trên máy giữ nguyên, lần đồng bộ tới sẽ thay bằng bản của máy chính.</summary>
    public void BecomeRemote(AppServices app)
    {
        lock (_run)
        {
            Stop();
            _settings.Set("drive_mode", ModeRemote); _settings.Set("lan_role", LanService.RoleRemote); _settings.Set("drive_seq", "0");
            app.ReloadLan(start: true);
            Start();
        }
    }

    /// <summary>Thoát phần mềm / đăng xuất: máy chính đưa bản cuối lên Drive rồi nhả khóa để máy khác nhận làm máy chính.</summary>
    public void ReleaseOnExit()
    {
        if (!Enabled) return;
        lock (_run)
        {
            try
            {
                var lan = _lan();
                if (!Directory.Exists(Root)) return;
                if (Mode == ModeMain && lan.HoldsMainData) { RunMain(lan); WriteLease(lan, "", true); }
                else if (Mode == ModeRemote) RunRemote(lan);   // máy xa: gửi nốt thay đổi chờ trước khi tắt
            }
            catch { }
        }
    }

    // ------------------------------------------------------------------ sao lưu trên Drive (máy chính, mỗi ngày 1 bản)

    private string BackupDir => Path.Combine(Root, "backup");
    public sealed record DriveBackup(string Name, DateTime Day, long Bytes);

    /// <summary>Các bản sao lưu trên Drive (mới nhất trước).</summary>
    public List<DriveBackup> DriveBackups()
    {
        if (Dir.Length == 0 || !Directory.Exists(BackupDir)) return new();
        return Directory.GetFiles(BackupDir, "b-*.kldb").Select(f => new FileInfo(f))
            .Select(f => (f, ok: DateTime.TryParseExact(f.Name[2..12], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d), d))
            .Where(x => x.ok).OrderByDescending(x => x.d).Select(x => new DriveBackup(x.f.Name, x.d, x.f.Length)).ToList();
    }

    /// <summary>Sao lưu toàn bộ dữ liệu lên Drive (nén + mã hóa bằng mã đồng bộ). Chỉ máy giữ dữ liệu chính. force = sao lưu lại dù hôm nay đã có.</summary>
    public string BackupToDrive(bool force = false)
    {
        var lan = _lan();
        if (!Enabled || Mode != ModeMain || !lan.HoldsMainData) throw new BusinessException("Chỉ máy chính (đang giữ dữ liệu) mới sao lưu lên Drive.");
        if (MakeBackup is null) throw new BusinessException("Chưa nối chức năng sao lưu.");
        var day = Now().Date; var name = $"b-{day:yyyy-MM-dd}.kldb"; var path = Path.Combine(BackupDir, name);
        if (!force && File.Exists(path)) return "Hôm nay đã có bản sao lưu trên Drive.";
        var tmp = Path.Combine(Path.GetTempPath(), "kl-sl-" + Guid.NewGuid().ToString("N")[..8] + ".db");
        try
        {
            MakeBackup(tmp);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            WriteAtomic(path, Seal(Gz(File.ReadAllBytes(tmp))));
        }
        finally { TryDelete(tmp); }
        // giữ 14 bản gần nhất + bản đầu tiên của mỗi tháng trong 12 tháng
        var all = DriveBackups(); var keep = all.Take(14).Select(b => b.Name).ToHashSet();
        foreach (var m in all.GroupBy(b => b.Day.ToString("yyyy-MM")).OrderByDescending(g => g.Key).Take(12)) keep.Add(m.OrderBy(b => b.Day).First().Name);
        foreach (var b in all.Where(b => !keep.Contains(b.Name))) TryDelete(Path.Combine(BackupDir, b.Name));
        return $"đã sao lưu lên Drive: {name} ({new FileInfo(path).Length / 1024:#,0} KB)";
    }

    /// <summary>Giải mã 1 bản sao lưu trên Drive ra file .db (để khôi phục ở Cài đặt máy, hoặc khôi phục máy ở nhà).</summary>
    public string DownloadBackup(string name, string toPath)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^b-\d{4}-\d{2}-\d{2}\.kldb$")) throw new BusinessException("Tên bản sao lưu không hợp lệ.");
        var b = TryRead(Path.Combine(BackupDir, name)) ?? throw new BusinessException("Không đọc được " + name + " (Google Drive chưa tải xong?).");
        byte[] db;
        try { db = Ungz(Open(b)); } catch (InvalidDataException e) { throw new BusinessException(e.Message); }
        var dir = Path.GetDirectoryName(Path.GetFullPath(toPath)); if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllBytes(toPath, db);
        return toPath;
    }

    // ------------------------------------------------------------------ máy chính

    private string RunMain(LanService lan)
    {
        if (!lan.HoldsMainData) return "Máy này không giữ dữ liệu chính (đang là máy con / dự phòng LAN) — chạy đồng bộ Drive trên máy chủ.";
        if (HeldByOther(lan) is { } other)
            return $"Lỗi: máy “{other.Name}” (tài khoản {other.User}) cũng đang là máy chính — máy này tạm dừng đưa dữ liệu lên Drive để không ghi đè. Đăng xuất ở một trong hai máy.";
        WriteLease(lan, CurrentUser, false);
        int applied = 0, merged = 0, conflict = 0, bad = 0;
        var inDir = Path.Combine(Root, "in"); var outDir = Path.Combine(Root, "out");
        if (Directory.Exists(inDir))
            foreach (var f in Directory.GetFiles(inDir, "*.klsync").OrderBy(x => x, StringComparer.Ordinal))
            {
                var bytes = TryRead(f); if (bytes is null) continue;
                JsonNode pk;
                try { pk = Unpack(bytes); } catch (InvalidDataException) { bad++; TryMove(f, f + ".loi"); continue; } catch { continue; }
                var machine = pk["mn"]?.ToString() ?? "máy xa"; var mid = pk["m"]?.ToString() ?? "x";
                var items = (pk["items"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().Select(o => (JsonObject)o.DeepClone()).ToList();
                var res = lan.ApplyExternal(items, machine);
                foreach (var r in res) { var st = r["st"]?.ToString(); if (st == "gop") merged++; else if (st == "xungdot") conflict++; else applied++; }
                WriteResults(outDir, mid, res);
                try { File.Delete(f); } catch { TryMove(f, f + ".xong"); }
            }
        var seq = lan.DataSeq();
        var changed = applied + merged + conflict > 0 || seq != _lastPubSeq || !File.Exists(Path.Combine(Root, "latest.json"));
        string pub = "";
        if (changed) { pub = Publish(lan); _lastPubSeq = seq; }
        try { if (MakeBackup is not null && !File.Exists(Path.Combine(BackupDir, $"b-{Now():yyyy-MM-dd}.kldb"))) pub += (pub.Length > 0 ? " · " : "") + BackupToDrive(); }
        catch (Exception e) { pub += " · chưa sao lưu được lên Drive: " + e.Message; }
        var from = applied + merged + conflict > 0 ? $" · nhận {applied + merged + conflict} bảng từ máy xa ({merged} tự gộp, {conflict} trùng sửa → Quản trị chọn ở Xung đột)" : "";
        return (pub.Length > 0 ? pub : "dữ liệu không đổi, giữ bản trên Drive") + from + (bad > 0 ? $" · bỏ {bad} tệp sai mã" : "");
    }

    private void WriteResults(string outDir, string machineId, List<JsonObject> res)
    {
        var path = Path.Combine(outDir, Safe(machineId) + ".klsync");
        var keep = new List<JsonNode>();
        var old = TryRead(path);
        if (old is not null) { try { keep.AddRange((Unpack(old)["results"] as JsonArray ?? new JsonArray()).Select(x => x!.DeepClone()).Take(300)); } catch { } }
        foreach (var r in res) keep.Add(new JsonObject { ["op"] = r["op"]?.ToString(), ["st"] = r["st"]?.ToString(), ["note"] = r["note"]?.ToString() });
        var after = long.TryParse(_settings.Get("drive_pub_seq"), out var n) ? n + 1 : 1;
        WriteAtomic(path, Pack(new JsonObject { ["after"] = after, ["results"] = new JsonArray(keep.TakeLast(300).ToArray()) }));
    }

    private string Publish(LanService lan)
    {
        var all = lan.ExportAll();
        var seq = (long.TryParse(_settings.Get("drive_pub_seq"), out var n) ? n : 0) + 1;
        var snap = new JsonObject
        {
            ["seq"] = seq, ["at"] = DateTime.Now.ToString("s"), ["by"] = lan.MachineName,
            ["sheets"] = new JsonArray(all.Select(x => (JsonNode)new JsonObject { ["db"] = x.Db, ["sheet"] = x.Sheet, ["ver"] = x.Version, ["data"] = x.Data }).ToArray()),
        };
        var blob = Pack(snap); var name = $"s-{seq:D6}.klsync";
        WriteAtomic(Path.Combine(Root, "snap", name), blob);
        var latest = new JsonObject { ["seq"] = seq, ["file"] = name, ["sha"] = Convert.ToHexString(SHA256.HashData(blob)).ToLowerInvariant(), ["at"] = snap["at"]!.ToString(), ["by"] = lan.MachineName, ["sheets"] = all.Count, ["bytes"] = blob.Length };
        WriteAtomic(Path.Combine(Root, "latest.json"), Encoding.UTF8.GetBytes(latest.ToJsonString()));
        _settings.Set("drive_pub_seq", seq.ToString());
        foreach (var old in Directory.GetFiles(Path.Combine(Root, "snap"), "s-*.klsync").OrderByDescending(x => x, StringComparer.Ordinal).Skip(4)) TryDelete(old);
        return $"đã đưa bản #{seq} lên Drive ({all.Count} bảng, {blob.Length / 1024:#,0} KB)";
    }

    // ------------------------------------------------------------------ máy xem từ xa

    private string RunRemote(LanService lan)
    {
        var parts = new List<string>();
        // 1) gửi thay đổi chờ lên
        var pend = lan.PendingItems();
        var inDir = Path.Combine(Root, "in");
        var fresh = pend.Where(p => !_pushedAt.TryGetValue(p["op"]!.ToString(), out var t) || DateTime.Now - t > TimeSpan.FromMinutes(10)).ToList();
        if (fresh.Count > 0)
        {
            var pk = new JsonObject { ["m"] = lan.MachineId, ["mn"] = lan.MachineName, ["at"] = DateTime.Now.ToString("s"), ["items"] = new JsonArray(fresh.Select(x => (JsonNode)x).ToArray()) };
            WriteAtomic(Path.Combine(inDir, $"{Safe(lan.MachineId)}-{DateTime.Now:yyyyMMddHHmmss}.klsync"), Pack(pk));
            foreach (var p in fresh) _pushedAt[p["op"]!.ToString()] = DateTime.Now;
            parts.Add($"đã gửi {fresh.Count} bảng thay đổi lên Drive (chờ máy chính gộp)");
        }
        // 2) tải bản mới nhất
        var lp = Path.Combine(Root, "latest.json");
        if (!File.Exists(lp)) return parts.Count > 0 ? string.Join(" · ", parts) : "chưa có dữ liệu trên Drive (máy chính chưa chạy đồng bộ, hoặc Drive chưa tải xong)";
        var latest = JsonNode.Parse(Encoding.UTF8.GetString(TryRead(lp) ?? throw new IOException("Đang đồng bộ latest.json — thử lại sau.")))!;
        var seq = latest["seq"]!.GetValue<long>(); var applied = long.TryParse(_settings.Get("drive_seq"), out var a) ? a : 0;
        Dictionary<(string, string), (long Ver, string Data)>? snapSheets = null;
        if (seq > applied)
        {
            var sp = Path.Combine(Root, "snap", latest["file"]!.ToString());
            var blob = TryRead(sp);
            if (blob is null) parts.Add($"bản #{seq} chưa tải xong từ Drive");
            else if (Convert.ToHexString(SHA256.HashData(blob)).ToLowerInvariant() != latest["sha"]?.ToString()) parts.Add($"bản #{seq} đang tải dở — thử lại sau");
            else
            {
                var snap = Unpack(blob);
                snapSheets = new();
                foreach (var x in (snap["sheets"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()) snapSheets[(x["db"]!.ToString(), x["sheet"]!.ToString())] = (x["ver"]!.GetValue<long>(), x["data"]!.ToString());
                var local = lan.LocalVersions(); int n = 0, held = 0;
                foreach (var (k, v) in snapSheets)
                {
                    if (local.TryGetValue(k, out var lv) && lv == v.Ver && !lan.HasPendingSheet(k.Item1, k.Item2)) continue;
                    if (lan.InstallSheet(k.Item1, k.Item2, v.Data, v.Ver)) n++; else held++;
                }
                _settings.Set("drive_seq", seq.ToString()); applied = seq;
                parts.Add($"đã tải bản #{seq} ({latest["at"]}): {n} bảng cập nhật" + (held > 0 ? $", {held} bảng giữ thay đổi của máy này" : ""));
            }
        }
        // 3) kết quả máy chính đã gộp thay đổi của máy này
        var rp = Path.Combine(Root, "out", Safe(lan.MachineId) + ".klsync"); var rb = File.Exists(rp) ? TryRead(rp) : null;
        if (rb is not null)
        {
            JsonNode? rn = null; try { rn = Unpack(rb); } catch { }
            if (rn is not null && rn["after"]!.GetValue<long>() <= applied)
            {
                var results = (rn["results"] as JsonArray ?? new JsonArray()).OfType<JsonObject>().ToDictionary(x => x["op"]!.ToString(), x => x, StringComparer.Ordinal);
                var done = new List<string>(); var errs = new List<string>();
                foreach (var p in lan.PendingItems())
                {
                    var op = p["op"]!.ToString();
                    if (!results.TryGetValue(op, out var r)) continue;
                    if (r["st"]?.ToString() == "loi") { errs.Add($"{p["sheet"]}: {r["note"]}"); continue; }
                    done.Add(op); _pushedAt.Remove(op);
                }
                if (done.Count > 0)
                {
                    var keyOf = lan.PendingItems().Where(p => done.Contains(p["op"]!.ToString())).Select(p => (p["db"]!.ToString(), p["sheet"]!.ToString())).ToList();
                    lan.Acknowledge(done);
                    // đặt lại các bảng đó đúng bản của máy chính (đã gộp)
                    var snapNow = snapSheets ?? LoadCurrentSnap(lan, latest);
                    if (snapNow is not null) foreach (var k in keyOf) if (snapNow.TryGetValue(k, out var v)) lan.InstallSheet(k.Item1, k.Item2, v.Data, v.Ver);
                    parts.Add($"máy chính đã nhận {done.Count} bảng thay đổi của bạn");
                }
                if (errs.Count > 0) parts.Add("máy chính từ chối: " + string.Join("; ", errs));
            }
        }
        return parts.Count > 0 ? string.Join(" · ", parts) : $"đã khớp bản #{applied} ({latest["at"]})";
    }

    private Dictionary<(string, string), (long Ver, string Data)>? LoadCurrentSnap(LanService lan, JsonNode latest)
    {
        var blob = TryRead(Path.Combine(Root, "snap", latest["file"]!.ToString())); if (blob is null) return null;
        try
        {
            var d = new Dictionary<(string, string), (long, string)>();
            foreach (var x in (Unpack(blob)["sheets"] as JsonArray ?? new JsonArray()).OfType<JsonObject>()) d[(x["db"]!.ToString(), x["sheet"]!.ToString())] = (x["ver"]!.GetValue<long>(), x["data"]!.ToString());
            return d;
        }
        catch { return null; }
    }

    private static string Safe(string s) => new string(s.Where(char.IsLetterOrDigit).ToArray());
    private static void TryMove(string a, string b) { try { File.Move(a, b, true); } catch { } }
    private static void TryDelete(string a) { try { File.Delete(a); } catch { } }
}
