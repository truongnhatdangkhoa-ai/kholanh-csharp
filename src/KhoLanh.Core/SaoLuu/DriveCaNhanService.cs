using System.Globalization;
using System.IO.Compression;
using System.Text.Json.Nodes;
using KhoLanh.Core.Lan;
using KhoLanh.Core.LuuTru;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.SaoLuu;

/// <summary>
/// SAO LƯU LÊN GOOGLE DRIVE CÁ NHÂN (1.68 — như "Drive cá nhân" của app cũ, core/drive-ca-nhan.js):
/// mỗi tài khoản đăng nhập app tự đăng nhập GMAIL CỦA MÌNH (OAuth máy tính: mở trình duyệt, gọi lại 127.0.0.1, PKCE; quyền
/// drive.file = app chỉ thấy tệp do chính nó tạo) rồi sao lưu database lên Drive của mình.
///  • Chỉ ĐỌC database: chụp bản sao (SQLite backup API) ra thư mục tạm, nén .gz, tải bản tạm lên. Không đụng đồng bộ LAN / Drive dùng chung.
///  • Một chiều máy → Drive. Khôi phục: nút riêng, Quản trị xác nhận; app tự chụp bản hiện tại (SaoLuu/truoc-khoi-phuc_…) trước khi ghi đè.
///  • Chỉ máy GIỮ dữ liệu chính (máy đơn / máy chủ LAN đang làm chủ) mới sao lưu / khôi phục — máy con LAN, máy xem từ xa chỉ có bản sao.
///  • Không cần "mã đồng bộ": đăng nhập đúng Gmail là thấy và khôi phục được, kể cả trên máy mới (cùng Client ID).
/// Cây thư mục: KhoLanh_SaoLuu / &lt;tên máy&gt; / KhoLanh_yyyy-MM-dd_HHmm.db.gz — giữ mỗi ngày 1 bản trong <see cref="KeepDays"/> ngày + 1 bản mỗi tháng trong 12 tháng.
/// Client ID / Secret OAuth dùng chung với Lưu trữ cá nhân (cài đặt máy lt_google_id / lt_google_secret).
/// </summary>
public sealed class DriveCaNhanService
{
    public const string Root = "KhoLanh_SaoLuu";
    public const string Mime = "application/gzip";
    public const int KeepDays = 14, KeepMonths = 12;
    /// <summary>Chu kỳ tự sao lưu (giờ; 0 = tắt).</summary>
    public static readonly (int Hours, string Label)[] Intervals = { (0, "Tắt (chỉ sao lưu khi bấm)"), (1, "Mỗi giờ"), (3, "Mỗi 3 giờ"), (6, "Mỗi 6 giờ"), (12, "Mỗi 12 giờ"), (24, "Mỗi ngày") };

    private readonly SettingsService _settings;
    private readonly Session _session;
    private readonly BackupService _backup;
    private readonly Func<LanService> _lan;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LtGoogle Google { get; }
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;
    /// <summary>Client OAuth cho test (bỏ qua cài đặt máy).</summary>
    public LtGoogle.Client? ClientOverride { get; set; }
    public event Action? StatusChanged;
    public bool Busy => _gate.CurrentCount == 0;

    public DriveCaNhanService(SettingsService settings, Session session, BackupService backup, Func<LanService> lan, HttpMessageHandler? http = null)
    {
        _settings = settings; _session = session; _backup = backup; _lan = lan;
        Google = new LtGoogle(http);
    }

    // ------------------------------------------------------------------ cấu hình (theo tài khoản đăng nhập app, lưu trên máy)

    public sealed class Cfg
    {
        public string Email = "", Refresh = "", LastErr = "", LastName = "";
        public int Every = 24;
        public DateTime? Last;
        public bool Linked => Refresh.Length > 0;

        public JsonObject ToJson() => new()
        {
            ["mail"] = Email, ["rt"] = Refresh, ["every"] = Every, ["err"] = LastErr, ["file"] = LastName,
            ["last"] = Last?.ToString("s", CultureInfo.InvariantCulture),
        };
        public static Cfg From(string? json)
        {
            var c = new Cfg();
            try
            {
                if (JsonNode.Parse(json ?? "") is not JsonObject o) return c;
                c.Email = o["mail"]?.ToString() ?? ""; c.Refresh = o["rt"]?.ToString() ?? ""; c.LastErr = o["err"]?.ToString() ?? "";
                c.LastName = o["file"]?.ToString() ?? "";
                c.Every = o["every"] is JsonValue v && v.TryGetValue<int>(out var e) ? e : 24;
                c.Last = DateTime.TryParse(o["last"]?.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            }
            catch { /* hỏng → mặc định */ }
            return c;
        }
    }

    private string Key => "dcn:" + _session.Username.ToLowerInvariant();
    public Cfg Config { get { _session.RequireLogin(); return Cfg.From(_settings.Get(Key)); } }
    private void Save(Cfg c) { _settings.Set(Key, c.ToJson().ToJsonString()); StatusChanged?.Invoke(); }

    public void SetEvery(int hours)
    {
        RequirePerm();
        if (!Intervals.Any(i => i.Hours == hours)) throw new BusinessException("Chu kỳ không hợp lệ.");
        var c = Config; c.Every = hours; Save(c);
    }

    public LtGoogle.Client Client => ClientOverride ?? new(_settings.Get(LuuTruService.SetClientId), Secret.Unprotect(_settings.Get(LuuTruService.SetClientSecret)));

    public void SetClient(string id, string secret)
    {
        _session.RequireAdmin();
        id = (id ?? "").Trim(); secret = (secret ?? "").Trim();
        if (id.Length > 0 && !id.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Client ID phải có dạng ….apps.googleusercontent.com");
        if (ClientOverride is not null) { ClientOverride = new(id, secret); return; }
        _settings.Set(LuuTruService.SetClientId, id);
        _settings.Set(LuuTruService.SetClientSecret, Secret.Protect(secret));
    }

    /// <summary>Tài khoản được dùng (quyền Sao lưu và cài đặt mức Sửa).</summary>
    public bool CanUse => _session.CurrentUser is not null && _session.Can("backup", Perm.Edit);
    private void RequirePerm() { if (!CanUse) throw new BusinessException("Tài khoản này chưa được cấp quyền «Sao lưu và cài đặt» (mức Sửa)."); }

    /// <summary>Máy này có giữ dữ liệu chính không (máy đơn / máy chủ LAN đang làm chủ). Null = được; khác null = lý do không được.</summary>
    public string? WhyNotMain()
    {
        var lan = _lan();
        if (lan.HoldsMainData) return null;
        return lan.Role == LanService.RoleRemote
            ? "Máy này là máy xem từ xa (đồng bộ qua Drive dùng chung) — dữ liệu chính ở máy chính, hãy sao lưu / khôi phục ở máy đó."
            : "Máy này là máy con / dự phòng LAN — dữ liệu chính ở máy chủ, hãy sao lưu / khôi phục trên máy chủ.";
    }

    // ------------------------------------------------------------------ đăng nhập Gmail

    /// <summary>1.112: lấy Client ID / secret OAuth dùng chung từ Google Sheet chung khi máy này còn thiếu (gắn ở AppServices).</summary>
    public Func<CancellationToken, Task>? EnsureClient { get; set; }

    public async Task<string> SignInAsync(Action<string> openBrowser, CancellationToken ct)
    {
        RequirePerm();
        if ((!Client.Ok || Client.Secret.Trim().Length == 0) && EnsureClient is { } ensure) { try { await ensure(ct); } catch { /* báo ở dưới */ } }
        if (Client.Ok && Client.Secret.Trim().Length == 0) throw new BusinessException("Máy này chưa có Client secret Google (mã OAuth dùng chung cho Gmail sao lưu / Lưu trữ cá nhân / Đăng nhập Google). Ở máy đã nhập Client secret, mở app (đăng nhập bất kỳ) để app tự chia sẻ lên Google Sheet chung rồi thử lại ở máy này — hoặc nhập Client secret ngay trên máy này.");
        if (!Client.Ok) throw new BusinessException("Máy này chưa có Client ID Google — Quản trị nhập 1 lần (dùng chung với Lưu trữ cá nhân).");
        var c = Config;
        var res = await Google.SignInAsync(Client, openBrowser, ct, c.Email.Length > 0 ? c.Email : null);
        c = Config;
        c.Email = res.Email; c.Refresh = Secret.Protect(res.Refresh); c.LastErr = "";
        Save(c);
        return res.Email;
    }

    public async Task SignOutAsync()
    {
        var c = Config;
        try { await Google.RevokeAsync(Secret.Unprotect(c.Refresh)); } catch { /* mất mạng: vẫn gỡ trên máy */ }
        c.Refresh = ""; c.LastErr = "";
        Save(c);
    }

    private async Task<string> AccessAsync(Cfg c, CancellationToken ct)
    {
        try { return await Google.AccessAsync(Client, Secret.Unprotect(c.Refresh), ct); }
        catch (LtRelinkException e) { c.LastErr = e.Message; Save(c); throw; }
    }

    // ------------------------------------------------------------------ sao lưu

    public static string FileName(DateTime at) => $"KhoLanh_{at:yyyy-MM-dd_HHmm}.db.gz";
    public static DateTime? TimeOf(string name)
    {
        if (!name.StartsWith("KhoLanh_") || !name.EndsWith(".db.gz")) return null;
        return DateTime.TryParseExact(name[8..^6], "yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }
    private static string SafeName(string s)
    {
        var t = new string((s ?? "").Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch == '\'' ? '_' : ch).ToArray()).Trim();
        return t.Length == 0 ? "may" : t;
    }
    public string Machine => SafeName(_lan().MachineName);

    public sealed record Result(string Name, long Bytes, int Deleted, string Message);

    public async Task<Result> BackupNowAsync(CancellationToken ct = default)
    {
        RequirePerm();
        if (WhyNotMain() is { } why) throw new BusinessException(why);
        var c = Config;
        if (!c.Linked) throw new BusinessException("Chưa đăng nhập Google — bấm “Đăng nhập Gmail”.");
        if (!await _gate.WaitAsync(0, ct)) throw new BusinessException("Đang sao lưu — đợi lượt trước xong.");
        var tmp = Path.Combine(Path.GetTempPath(), "kl-dcn-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(tmp);
            var db = Path.Combine(tmp, "kho.db"); var gz = db + ".gz";
            _backup.BackupTo(db);
            await using (var src = File.OpenRead(db))
            await using (var dst = File.Create(gz))
            await using (var z = new GZipStream(dst, CompressionLevel.SmallestSize))
                await src.CopyToAsync(z, ct);
            var access = await AccessAsync(c, ct);
            var root = await Google.EnsureFolderAsync(access, "root", Root, ct);
            var folder = await Google.EnsureFolderAsync(access, root, Machine, ct);
            var now = Clock(); var name = FileName(now);
            var existing = (await Google.ListAsync(access, folder, ct, name)).FirstOrDefault(f => !f.Folder);
            await Google.UploadAsync(access, gz, name, Mime, folder, existing?.Id, ct);
            var deleted = 0;
            var files = (await Google.ListAsync(access, folder, ct)).Where(f => !f.Folder && TimeOf(f.Name) is not null).ToList();
            var keep = KeepSet(files.Select(f => (f.Name, TimeOf(f.Name)!.Value)), now);
            foreach (var f in files.Where(f => !keep.Contains(f.Name)))
                try { await Google.DeleteAsync(access, f.Id, ct); deleted++; } catch { /* để lần sau */ }
            var bytes = new FileInfo(gz).Length;
            c = Config; c.Last = now; c.LastName = name; c.LastErr = ""; Save(c);
            return new(name, bytes, deleted, $"Đã sao lưu {name} ({bytes / 1024.0 / 1024:0.0} MB) lên Drive {c.Email}" + (deleted > 0 ? $", xóa {deleted} bản cũ." : "."));
        }
        catch (Exception e) when (e is not LtRelinkException && e is not OperationCanceledException)
        {
            var cc = Config; cc.LastErr = e.Message; Save(cc);
            throw;
        }
        finally
        {
            _gate.Release();
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    /// <summary>Giữ: mọi bản trong ngày hôm nay, bản mới nhất mỗi ngày trong <see cref="KeepDays"/> ngày, bản mới nhất mỗi tháng trong <see cref="KeepMonths"/> tháng.</summary>
    public static HashSet<string> KeepSet(IEnumerable<(string Name, DateTime At)> files, DateTime now)
    {
        var list = files.OrderByDescending(f => f.At).ToList();
        var keep = new HashSet<string>(list.Where(f => f.At.Date == now.Date).Select(f => f.Name));
        foreach (var g in list.Where(f => f.At.Date > now.Date.AddDays(-KeepDays)).GroupBy(f => f.At.Date)) keep.Add(g.First().Name);
        foreach (var g in list.GroupBy(f => f.At.ToString("yyyy-MM")).OrderByDescending(g => g.Key).Take(KeepMonths)) keep.Add(g.First().Name);
        return keep;
    }

    /// <summary>Đến hạn tự sao lưu chưa (đã đăng nhập, bật chu kỳ, máy giữ dữ liệu chính).</summary>
    public bool IsDue(Cfg c, DateTime now) => c.Linked && c.Every > 0 && (c.Last is null || now - c.Last.Value >= TimeSpan.FromHours(c.Every) - TimeSpan.FromMinutes(2));

    /// <summary>Gọi định kỳ (app: mỗi phút). Trả câu báo khi vừa sao lưu, null khi không làm gì / lỗi (lỗi ghi vào cấu hình).</summary>
    public async Task<string?> TickAsync(CancellationToken ct = default)
    {
        if (_session.CurrentUser is null || !CanUse || Busy || WhyNotMain() is not null) return null;
        var c = Config;
        if (!IsDue(c, Clock()) || c.LastErr.StartsWith("Phiên đăng nhập Google")) return null;
        try { return (await BackupNowAsync(ct)).Message; }
        catch { return null; }
    }

    private Timer? _timer;
    /// <summary>App gọi sau khi đăng nhập: kiểm tra lịch mỗi phút (luồng nền).</summary>
    public void StartAuto(TimeSpan? every = null)
    {
        StopAuto();
        _timer = new Timer(_ => { _ = TickAsync(); }, null, TimeSpan.FromSeconds(45), every ?? TimeSpan.FromSeconds(60));
    }
    public void StopAuto() { _timer?.Dispose(); _timer = null; }

    // ------------------------------------------------------------------ danh sách + khôi phục

    public sealed record Item(string Machine, string Name, string Id, long Size, DateTime At);

    /// <summary>Mọi bản sao lưu trên Drive của Gmail đang đăng nhập (mọi máy — để khôi phục trên máy mới).</summary>
    public async Task<List<Item>> ListAsync(CancellationToken ct = default)
    {
        RequirePerm();
        var c = Config;
        if (!c.Linked) return new();
        var access = await AccessAsync(c, ct);
        var root = (await Google.ListAsync(access, "root", ct, Root)).FirstOrDefault(f => f.Folder);
        if (root is null) return new();
        var res = new List<Item>();
        foreach (var m in (await Google.ListAsync(access, root.Id, ct)).Where(f => f.Folder))
            foreach (var f in (await Google.ListAsync(access, m.Id, ct)).Where(f => !f.Folder))
                if (TimeOf(f.Name) is { } at) res.Add(new(m.Name, f.Name, f.Id, f.Size, at));
        return res.OrderByDescending(i => i.At).ThenBy(i => i.Machine).ToList();
    }

    /// <summary>Tải 1 bản về file .db (giải nén) — để tự giữ hoặc khôi phục ở máy khác.</summary>
    public async Task DownloadAsync(Item it, string toDb, CancellationToken ct = default)
    {
        RequirePerm();
        var access = await AccessAsync(Config, ct);
        var gz = toDb + ".gz.tmp";
        try
        {
            if (!await Google.DownloadAsync(access, it.Id, gz, ct)) throw new BusinessException("Bản sao lưu không còn trên Drive.");
            await using var src = File.OpenRead(gz);
            await using var z = new GZipStream(src, CompressionMode.Decompress);
            await using var dst = File.Create(toDb);
            try { await z.CopyToAsync(dst, ct); }
            catch (InvalidDataException) { throw new BusinessException("Tệp trên Drive bị hỏng (không giải nén được)."); }
        }
        finally { try { File.Delete(gz); } catch { } }
    }

    /// <summary>
    /// Khôi phục MÁY NÀY về 1 bản trên Drive (Quản trị). Bản hiện tại được chụp vào SaoLuu/truoc-khoi-phuc_… trước khi ghi đè
    /// (BackupService.Restore). Chỉ máy giữ dữ liệu chính. Xong phải đăng nhập lại.
    /// </summary>
    public async Task RestoreAsync(Item it, CancellationToken ct = default)
    {
        _session.Require("khoiphuc");   // 1.114: theo quyền Khôi phục dữ liệu; máy đang nối máy chủ LAN bị khóa trong BackupService.Restore
        if (KhoLanh.Core.AppServices.RestoreLock(_lan()) is { } why) throw new BusinessException(why);
        var tmp = Path.Combine(Path.GetTempPath(), "kl-dcn-khoi-phuc-" + Guid.NewGuid().ToString("N")[..8] + ".db");
        try
        {
            await DownloadAsync(it, tmp, ct);
            // giữ đăng nhập Gmail + Client ID của MÁY NÀY (bản khôi phục mang cài đặt lúc sao lưu / của máy khác: token mã hóa theo máy cũ, không dùng được)
            var keep = new[] { Key, LuuTruService.SetClientId, LuuTruService.SetClientSecret }.Select(k => (k, v: _settings.Get(k))).ToList();
            _backup.Restore(tmp);
            foreach (var (k, v) in keep) _settings.Set(k, v);
            StatusChanged?.Invoke();
        }
        finally { try { File.Delete(tmp); } catch { } }
    }
}
