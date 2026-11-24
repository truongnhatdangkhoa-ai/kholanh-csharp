using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.LuuTru;

/// <summary>
/// Lưu trữ cá nhân (M19 app cũ, làm lại kiểu mới): ghi chú / liên kết / ảnh / tệp riêng từng tài khoản, chia ngăn + thẻ,
/// đăng nhập Google cá nhân và sao lưu Google Drive ngay trong module (lịch tự động, giữ bản theo ngày, khôi phục),
/// sao lưu ra .zip, nạp dữ liệu Lưu trữ cá nhân của app cũ (config/luu-tru/&lt;tài khoản&gt;/).
/// Cây thư mục trên Drive: KhoLanh_LuuTruCaNhan / &lt;tài khoản&gt; / data.json + tep/ + ban-theo-ngay/data-YYYY-MM-DD.json.
/// </summary>
public sealed class LuuTruService
{
    public const string PermKey = "m19";
    public const string DriveRoot = "KhoLanh_LuuTruCaNhan";
    public const string SetClientId = "lt_google_id", SetClientSecret = "lt_google_secret";

    private readonly string _root;
    private readonly Session _session;
    private readonly SettingsService? _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private LtStore? _store;
    private Timer? _timer;

    public LtGoogle Google { get; }
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;
    /// <summary>Client OAuth cho test (bỏ qua cài đặt máy).</summary>
    public LtGoogle.Client? ClientOverride { get; set; }
    /// <summary>Báo lượt sao lưu / khôi phục xong (giao diện cập nhật trạng thái).</summary>
    public event Action? StatusChanged;
    public bool Busy => _gate.CurrentCount == 0;

    public LuuTruService(string root, Session session, SettingsService? settings, HttpMessageHandler? http = null)
    {
        _root = root; _session = session; _settings = settings;
        Google = new LtGoogle(http);
    }

    public bool CanUse => _session.Can(PermKey, Models.Perm.Edit);

    /// <summary>Kho của tài khoản đang đăng nhập (đổi tài khoản → mở kho khác).</summary>
    public LtStore Store
    {
        get
        {
            _session.RequireLogin();
            var acc = LtStore.AccountOf(_session.Username);
            if (_store is null || _store.Account != acc) _store = new LtStore(_root, _session.Username);
            return _store;
        }
    }

    private string CfgFile(LtStore s) => Path.Combine(s.Dir, "cai-dat.json");
    public LtConfig Config => LtConfig.Load(CfgFile(Store));
    public void SaveConfig(LtConfig c) => c.Save(CfgFile(Store));

    // ------------------------------------------------------------------ OAuth client (1 lần mỗi máy)

    public LtGoogle.Client Client
    {
        get
        {
            if (ClientOverride is not null) return ClientOverride;
            if (_settings is null) return new("", "");
            return new(_settings.Get(SetClientId), Secret.Unprotect(_settings.Get(SetClientSecret)));
        }
    }

    public void SetClient(string id, string secret)
    {
        id = (id ?? "").Trim(); secret = (secret ?? "").Trim();
        if (id.Length > 0 && !id.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Client ID phải có dạng ….apps.googleusercontent.com");
        if (_settings is null) { ClientOverride = new(id, secret); return; }
        _settings.Set(SetClientId, id);
        _settings.Set(SetClientSecret, Secret.Protect(secret));
    }

    /// <summary>
    /// Đọc Client ID / Secret đã nhập ở Drive cá nhân của app cũ (config/drive-ca-nhan.json). Secret app cũ mã hóa bằng Electron ("enc:")
    /// thì không đọc được → trả Secret rỗng (người dùng dán lại). Không thấy thì null.
    /// </summary>
    public static LtGoogle.Client? ReadLegacyClient(string folder)
    {
        foreach (var dir in LegacyConfigDirs(folder))
        {
            var f = Path.Combine(dir, "drive-ca-nhan.json");
            if (!File.Exists(f)) continue;
            try
            {
                var j = JsonNode.Parse(File.ReadAllText(f));
                var id = j?["clientId"]?.ToString() ?? "";
                var sec = j?["clientSecret"]?.ToString() ?? "";
                if (sec.StartsWith("enc:")) sec = "";
                if (id.Length > 0) return new(id, sec);
            }
            catch { /* hỏng → thử chỗ khác */ }
        }
        return null;
    }

    // ------------------------------------------------------------------ đăng nhập Google

    public async Task<string> SignInAsync(Action<string> openBrowser, CancellationToken ct)
    {
        var s = Store;
        var cfg = LtConfig.Load(CfgFile(s));
        var res = await Google.SignInAsync(Client, openBrowser, ct, cfg.Email.Length > 0 ? cfg.Email : null);
        cfg = LtConfig.Load(CfgFile(s));
        if (cfg.Linked && !string.Equals(cfg.Email, res.Email, StringComparison.OrdinalIgnoreCase))
        {
            cfg.LastSeq = 0; cfg.LastOk = null;      // đổi sang Gmail khác: lần sao lưu tới tải lại toàn bộ
            foreach (var r in s.D.Recs.Values) r.Fid = "";
            s.Save();
        }
        cfg.Email = res.Email; cfg.Refresh = Secret.Protect(res.Refresh); cfg.NeedRelink = false; cfg.LastErr = "";
        cfg.AddRun(Clock(), true, "Google", "Đăng nhập " + res.Email);
        cfg.Save(CfgFile(s));
        StatusChanged?.Invoke();
        return res.Email;
    }

    public async Task SignOutAsync()
    {
        var s = Store;
        var cfg = LtConfig.Load(CfgFile(s));
        await Google.RevokeAsync(Secret.Unprotect(cfg.Refresh));
        var mail = cfg.Email;
        cfg.Refresh = ""; cfg.NeedRelink = false; cfg.LastErr = "";
        cfg.AddRun(Clock(), true, "Google", "Hủy liên kết " + mail);
        cfg.Save(CfgFile(s));
        StatusChanged?.Invoke();
    }

    // ------------------------------------------------------------------ sao lưu Drive

    public sealed record BackupResult(int Uploaded, long Bytes, int Deleted, string Message);

    private sealed record Folders(string User, string Files, string Days);

    private async Task<Folders> EnsureFolders(string access, LtStore s, CancellationToken ct)
    {
        var root = await Google.EnsureFolderAsync(access, "root", DriveRoot, ct);
        var user = await Google.EnsureFolderAsync(access, root, s.Account, ct);
        var files = await Google.EnsureFolderAsync(access, user, "tep", ct);
        var days = await Google.EnsureFolderAsync(access, user, "ban-theo-ngay", ct);
        return new Folders(user, files, days);
    }

    public bool Dirty(LtStore s, LtConfig c) => s.D.Seq > c.LastSeq || LtStore.Retry(() => s.D.Recs.Values.Any(r => (r.IsFile && r.Dl == 0 && r.Fid.Length == 0 && s.HasLocal(r)) || (r.Dl == 2 && r.Fid.Length > 0)));

    /// <summary>Sao lưu lên Google Drive: tệp mới (mỗi tệp tải 1 lần), xóa trên Drive tệp đã xóa hẳn, rồi data.json + bản theo ngày.</summary>
    public async Task<BackupResult> BackupAsync(bool auto = false, CancellationToken ct = default)
    {
        var s = Store;
        if (!await _gate.WaitAsync(0, ct)) throw new BusinessException("Đang sao lưu / khôi phục — chờ xong lượt này.");
        var cfg = LtConfig.Load(CfgFile(s));
        try
        {
            if (!cfg.Linked) throw new BusinessException("Chưa đăng nhập Google — bấm Đăng nhập Google ở tab Sao lưu & Google.");
            cfg.LastTry = Clock();
            var access = await Google.AccessAsync(Client, Secret.Unprotect(cfg.Refresh), ct);
            var f = await EnsureFolders(access, s, ct);
            int up = 0, del = 0; long bytes = 0;
            foreach (var r in s.Snap(r => r.Dl == 2 && r.Fid.Length > 0))
            {
                await Google.DeleteAsync(access, r.Fid, ct); r.Fid = ""; del++;
            }
            foreach (var r in s.Snap(r => r.IsFile && r.Dl < 2 && r.Fid.Length == 0 && s.HasLocal(r)).OrderBy(r => r.Ts))
            {
                ct.ThrowIfCancellationRequested();
                r.Fid = await Google.UploadAsync(access, s.AbsPath(r), r.Id + "__" + r.Fn, r.Mt, f.Files, null, ct);
                up++; bytes += r.Fs;
                if (up % 10 == 0) s.Save();        // giữ mã tệp đã tải nếu lượt này bị ngắt giữa chừng
            }
            var seq = s.D.Seq;
            s.Save();
            // data.json (bản mới nhất) + bản theo ngày
            var existing = await Google.ListAsync(access, f.User, ct, "data.json");
            await Google.UploadAsync(access, s.DataFile, "data.json", "application/json", f.User, existing.FirstOrDefault(x => !x.Folder)?.Id, ct);
            var now = Clock();
            var dayName = "data-" + now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".json";
            var days = await Google.ListAsync(access, f.Days, ct);
            await Google.UploadAsync(access, s.DataFile, dayName, "application/json", f.Days, days.FirstOrDefault(x => x.Name == dayName)?.Id, ct);
            foreach (var old in days.Where(x => DayOf(x.Name) is { } d && d < now.Date.AddDays(-Math.Max(1, cfg.KeepDays) + 1)))
                await Google.DeleteAsync(access, old.Id, ct);

            cfg.LastSeq = seq; cfg.LastOk = now; cfg.LastErr = ""; cfg.NeedRelink = false;
            var msg = (auto ? "Tự sao lưu" : "Sao lưu") + (up > 0 ? $": tải lên {up} tệp ({Size(bytes)})" : ": danh sách mục") + (del > 0 ? $", xóa {del} tệp đã xóa hẳn" : "");
            cfg.AddRun(now, true, "Sao lưu", msg);
            return new BackupResult(up, bytes, del, msg);
        }
        catch (Exception e)
        {
            cfg.LastErr = e is BusinessException ? e.Message : "Lỗi: " + e.Message;
            if (e is LtRelinkException) cfg.NeedRelink = true;
            if (e is not OperationCanceledException) cfg.AddRun(Clock(), false, "Sao lưu", cfg.LastErr);
            throw;
        }
        finally
        {
            try { cfg.Save(CfgFile(s)); } catch { /* bỏ qua */ }
            _gate.Release();
            StatusChanged?.Invoke();
        }
    }

    public static DateTime? DayOf(string name) =>
        name.StartsWith("data-") && name.EndsWith(".json") && DateTime.TryParseExact(name[5..^5], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static string Size(long b) =>
        b >= 1L << 30 ? (b / (double)(1L << 30)).ToString("0.##", CultureInfo.GetCultureInfo("vi-VN")) + " GB"
        : b >= 1 << 20 ? (b / (double)(1 << 20)).ToString("0.#", CultureInfo.GetCultureInfo("vi-VN")) + " MB"
        : b >= 1024 ? (b / 1024.0).ToString("0", CultureInfo.GetCultureInfo("vi-VN")) + " KB" : b + " B";

    /// <summary>Các bản trên Drive để khôi phục: "data.json" (mới nhất) + bản theo ngày (mới nhất trước).</summary>
    public async Task<List<LtGoogle.DFile>> SnapshotsAsync(CancellationToken ct = default)
    {
        var s = Store; var cfg = LtConfig.Load(CfgFile(s));
        if (!cfg.Linked) throw new BusinessException("Chưa đăng nhập Google.");
        var access = await Google.AccessAsync(Client, Secret.Unprotect(cfg.Refresh), ct);
        var f = await EnsureFolders(access, s, ct);
        var latest = (await Google.ListAsync(access, f.User, ct, "data.json")).Where(x => !x.Folder);
        var days = (await Google.ListAsync(access, f.Days, ct)).Where(x => DayOf(x.Name) is not null).OrderByDescending(x => x.Name);
        return latest.Concat(days).ToList();
    }

    public sealed record RestoreResult(int Items, int Files, int MissingFiles);

    /// <summary>
    /// Khôi phục từ Drive: gộp danh sách mục (bản sửa sau thắng — không xóa mục đang có trên máy), rồi tải các tệp còn thiếu.
    /// snapshotId = null → bản mới nhất (data.json).
    /// </summary>
    public async Task<RestoreResult> RestoreAsync(string? snapshotId = null, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var s = Store;
        if (!await _gate.WaitAsync(0, ct)) throw new BusinessException("Đang sao lưu / khôi phục — chờ xong lượt này.");
        var cfg = LtConfig.Load(CfgFile(s));
        try
        {
            if (!cfg.Linked) throw new BusinessException("Chưa đăng nhập Google.");
            var access = await Google.AccessAsync(Client, Secret.Unprotect(cfg.Refresh), ct);
            if (snapshotId is null)
            {
                var f = await EnsureFolders(access, s, ct);
                snapshotId = (await Google.ListAsync(access, f.User, ct, "data.json")).FirstOrDefault(x => !x.Folder)?.Id
                             ?? throw new BusinessException("Drive chưa có bản sao lưu nào của tài khoản " + s.Account + " (Gmail " + cfg.Email + ").");
            }
            var data = JsonSerializer.Deserialize<LtData>(await Google.DownloadTextAsync(access, snapshotId, ct), LtStore.Json)
                       ?? throw new BusinessException("Bản sao lưu hỏng.");
            int n = 0;
            foreach (var r in data.Recs.Values) if (s.Merge(r)) n++;
            s.Save();
            int got = 0, miss = 0;
            var need = s.MissingFiles().Where(r => r.Fid.Length > 0).ToList();
            foreach (var r in need)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"Đang tải tệp {got + miss + 1} / {need.Count}: {r.Fn}");
                var tmp = Path.Combine(s.Dir, "_tai-" + r.Id);
                if (await Google.DownloadAsync(access, r.Fid, tmp, ct)) { s.AttachFile(r, tmp, move: true); got++; }
                else { r.Fid = ""; miss++; }
            }
            s.Save();
            cfg.LastSeq = Math.Max(cfg.LastSeq, s.D.Seq);    // vừa lấy từ Drive về: không cần tải ngược lên
            cfg.AddRun(Clock(), true, "Khôi phục", $"Khôi phục từ Drive: {n} mục mới / đổi, tải {got} tệp" + (miss > 0 ? $", {miss} tệp không còn trên Drive" : ""));
            return new RestoreResult(n, got, miss);
        }
        catch (Exception e)
        {
            if (e is LtRelinkException) cfg.NeedRelink = true;
            if (e is not OperationCanceledException) cfg.AddRun(Clock(), false, "Khôi phục", e.Message);
            throw;
        }
        finally
        {
            try { cfg.Save(CfgFile(s)); } catch { }
            _gate.Release();
            StatusChanged?.Invoke();
        }
    }

    // ------------------------------------------------------------------ tự sao lưu

    public DateTime LastChange(LtStore s) => File.Exists(s.DataFile) ? File.GetLastWriteTime(s.DataFile) : DateTime.MinValue;

    /// <summary>Kiểm tra lịch 1 lần (gọi theo chu kỳ). Trả true nếu đã chạy sao lưu.</summary>
    public async Task<bool> TickAsync()
    {
        if (_session.CurrentUser is null || !CanUse || Busy) return false;
        var s = Store; var cfg = LtConfig.Load(CfgFile(s));
        if (!cfg.IsDue(Clock(), Dirty(s, cfg), LastChange(s))) return false;
        try { await BackupAsync(auto: true); } catch { /* lỗi đã ghi vào lịch sử / LastErr */ }
        return true;
    }

    public void StartAuto(TimeSpan? every = null)
    {
        StopAuto();
        var p = every ?? TimeSpan.FromSeconds(60);
        _timer = new Timer(_ => { _ = TickAsync(); }, null, TimeSpan.FromSeconds(20), p);
    }

    public void StopAuto() { _timer?.Dispose(); _timer = null; }

    // ------------------------------------------------------------------ .zip trên máy

    /// <summary>Xuất toàn bộ kho (data.json + tệp) ra 1 tệp .zip.</summary>
    public int ExportZip(string zipPath)
    {
        var s = Store;
        var tmp = zipPath + ".tam";
        if (File.Exists(tmp)) File.Delete(tmp);
        int n = 0;
        using (var z = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            z.CreateEntryFromFile(s.DataFile, "data.json");
            foreach (var r in s.D.Recs.Values.Where(r => r.IsFile && r.Dl < 2 && s.HasLocal(r)))
            {
                z.CreateEntryFromFile(s.AbsPath(r), "files/" + r.Lf.Replace('\\', '/'), CompressionLevel.Fastest); n++;
            }
        }
        File.Move(tmp, zipPath, true);
        return n;
    }

    /// <summary>Nạp tệp .zip (của module này) — gộp như khôi phục, không xóa mục đang có.</summary>
    public RestoreResult ImportZip(string zipPath)
    {
        var s = Store;
        using var z = ZipFile.OpenRead(zipPath);
        var de = z.GetEntry("data.json") ?? throw new BusinessException("Tệp .zip không phải bản sao lưu của Lưu trữ cá nhân (thiếu data.json).");
        LtData data;
        using (var st = de.Open()) data = JsonSerializer.Deserialize<LtData>(st, LtStore.Json) ?? throw new BusinessException("data.json hỏng.");
        int n = 0, got = 0, miss = 0;
        var lfOf = data.Recs.Values.ToDictionary(r => r.Id, r => r.Lf ?? "");
        foreach (var r in data.Recs.Values) if (s.Merge(r)) n++;
        foreach (var r in s.MissingFiles())
        {
            var e = lfOf.TryGetValue(r.Id, out var lf) && lf.Length > 0 ? z.GetEntry("files/" + lf.Replace('\\', '/')) : null;
            if (e is null) { miss++; continue; }
            var tmp = Path.Combine(s.Dir, "_zip-" + r.Id);
            e.ExtractToFile(tmp, true);
            s.AttachFile(r, tmp, move: true); got++;
        }
        s.Save();
        return new RestoreResult(n, got, miss);
    }

    // ------------------------------------------------------------------ app cũ

    private static IEnumerable<string> LegacyConfigDirs(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) yield break;
        yield return folder;
        yield return Path.Combine(folder, "config");
        yield return Path.Combine(folder, "app", "config");
    }

    /// <summary>Tìm các kho Lưu trữ cá nhân của app cũ trong thư mục người dùng chọn (thư mục app cũ, app/, config/, luu-tru/ hoặc thư mục 1 tài khoản).</summary>
    public static List<(string Account, string Dir, int Items)> FindLegacy(string folder)
    {
        var res = new List<(string, string, int)>();
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return res;
        void Add(string dir)
        {
            var f = Path.Combine(dir, "data.json");
            if (!File.Exists(f) || res.Any(x => x.Item2 == dir)) return;
            int n = 0;
            try { n = JsonSerializer.Deserialize<LtData>(File.ReadAllText(f), LtStore.Json)?.Recs.Values.Count(r => r.K == "m" && r.Dl == 0) ?? 0; } catch { return; }
            res.Add((Path.GetFileName(dir), dir, n));
        }
        Add(folder);
        foreach (var lt in new[] { folder, Path.Combine(folder, "luu-tru") }.Concat(LegacyConfigDirs(folder).Select(d => Path.Combine(d, "luu-tru"))).Distinct())
            if (Directory.Exists(lt))
                foreach (var d in Directory.GetDirectories(lt)) Add(d);
        return res;
    }

    /// <summary>Nạp kho Lưu trữ cá nhân của app cũ (CHỈ ĐỌC thư mục cũ): gộp mục + chép tệp còn thiếu.</summary>
    public RestoreResult ImportLegacy(string accountDir)
    {
        var s = Store;
        var f = Path.Combine(accountDir, "data.json");
        if (!File.Exists(f)) throw new BusinessException("Thư mục này không có data.json của Lưu trữ cá nhân app cũ.");
        var data = JsonSerializer.Deserialize<LtData>(File.ReadAllText(f), LtStore.Json) ?? throw new BusinessException("data.json app cũ hỏng.");
        var lfOf = data.Recs.Values.ToDictionary(r => r.Id, r => r.Lf ?? "");
        int n = 0, got = 0, miss = 0;
        foreach (var r in data.Recs.Values)
        {
            if (r.Id == LtStore.DefaultNgan) continue;            // ngăn mặc định của app cũ ("My Documents") = ngăn Chung
            r.Fid = "";                                           // mã tệp Drive của app cũ không dùng được với đăng nhập mới
            if (s.Merge(r)) n++;
        }
        foreach (var r in s.MissingFiles())
        {
            var src = lfOf.TryGetValue(r.Id, out var lf) && lf.Length > 0
                ? Path.Combine(accountDir, "files", lf.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)) : "";
            if (src.Length == 0 || !File.Exists(src)) { if (lfOf.ContainsKey(r.Id)) miss++; continue; }
            s.AttachFile(r, src); got++;
        }
        s.Save();
        return new RestoreResult(n, got, miss);
    }
}
