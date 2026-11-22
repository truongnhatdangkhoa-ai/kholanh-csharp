using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KhoLanh.Core.Services;

/// <summary>Bản phát hành mới trên GitHub.</summary>
public record UpdateInfo(Version Version, string Tag, string Notes, DateTime? PublishedAt,
    long ExeAssetId, long ExeSize, long? ShaAssetId);

/// <summary>
/// Cập nhật phần mềm ngay trong app (không cần cài lại): hỏi GitHub Releases của kho phần mềm, tải KhoLanh.exe mới,
/// kiểm tra mã băm SHA-256. Việc thay file exe đang chạy do lớp giao diện làm (đổi tên exe cũ → .cu, đặt exe mới vào chỗ, mở lại).
/// Dữ liệu nằm ở %LOCALAPPDATA%\KhoLanhCS nên không bị đụng tới.
/// </summary>
public class UpdateService
{
    public const string DefaultRepo = "truongnhatdangkhoa-ai/kholanh-csharp";
    public const string ExeAsset = "KhoLanh.exe";
    public const string ShaAsset = "KhoLanh.exe.sha256";

    private const string KeyRepo = "update_repo", KeyToken = "update_token", KeyAuto = "update_auto", KeyLast = "update_last_check";
    private readonly SettingsService _settings;
    private readonly Session _session;
    private readonly HttpMessageHandler? _handler;

    public UpdateService(SettingsService settings, Session session, HttpMessageHandler? handler = null)
    {
        _settings = settings; _session = session; _handler = handler;
    }

    public static Version Current =>
        typeof(UpdateService).Assembly.GetName().Version is { } v ? new Version(v.Major, v.Minor, Math.Max(0, v.Build)) : new Version(0, 0, 0);

    // ---------------- cấu hình ----------------

    public string Repo
    {
        get => NormalizeRepo(_settings.Get(KeyRepo)) is { Length: > 0 } r ? r : DefaultRepo;
    }

    public bool Auto => _settings.Get(KeyAuto, "1") != "0";
    public bool HasToken => Token().Length > 0;
    public string LastCheck => _settings.Get(KeyLast);

    public void SaveConfig(string repo, string? token, bool auto)
    {
        _session.RequireAdmin();
        var r = NormalizeRepo(repo);
        if (repo.Trim().Length > 0 && r.Length == 0) throw new BusinessException("Kho GitHub phải có dạng chủ/tên-kho, VD: truongnhatdangkhoa-ai/kholanh-csharp.");
        _settings.Set(KeyRepo, r);
        if (token is not null) _settings.Set(KeyToken, Secret.Protect(token.Trim()));
        _settings.Set(KeyAuto, auto ? "1" : "0");
    }

    /// <summary>Mã GitHub để đóng vào mã kết nối / bộ cài cho máy khác (chỉ Quản trị).</summary>
    public string ShareToken() { _session.RequireAdmin(); return Token(); }

    /// <summary>Nhận mã GitHub từ mã kết nối / bộ cài (máy mới chưa có ai đăng nhập). Chỉ ghi khi máy chưa có mã.</summary>
    public void ImportToken(string token)
    {
        if (token.Trim().Length > 0 && !HasToken) _settings.Set(KeyToken, Secret.Protect(token.Trim()));
    }

    /// <summary>Mã truy cập: ưu tiên cấu hình trong app, sau đó file github-token.txt cạnh KhoLanh.exe (dòng # là ghi chú).</summary>
    private string Token()
    {
        var t = Secret.Unprotect(_settings.Get(KeyToken));
        if (t.Length > 0) return t;
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";
            var f = Path.Combine(dir, "github-token.txt");
            if (File.Exists(f))
                return File.ReadAllLines(f).Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0 && x[0] != '#') ?? "";
        }
        catch { /* không đọc được thì coi như không có */ }
        return "";
    }

    public static string NormalizeRepo(string? s)
    {
        var t = (s ?? "").Trim().TrimEnd('/');
        if (t.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) t = t[..^4];
        var m = System.Text.RegularExpressions.Regex.Match(t, @"github\.com[/:]([^/\s]+)/([^/\s]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value + "/" + m.Groups[2].Value;
        return System.Text.RegularExpressions.Regex.IsMatch(t, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") ? t : "";
    }

    // ---------------- hỏi GitHub ----------------

    private HttpClient Client(string accept = "application/vnd.github+json")
    {
        var h = _handler is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) : new HttpClient(_handler, false);
        h.Timeout = TimeSpan.FromMinutes(10);
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KhoLanh-CS-Updater", Current.ToString()));
        h.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
        h.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        var token = Token();
        if (token.Length > 0) h.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return h;
    }

    /// <summary>Hỏi bản phát hành mới nhất. Trả null nếu đang dùng bản mới nhất.</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var h = Client();
        HttpResponseMessage res;
        try { res = await h.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct); }
        catch (HttpRequestException) { throw new BusinessException("Không kết nối được GitHub — kiểm tra Internet rồi thử lại."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new BusinessException("GitHub trả lời quá lâu — thử lại sau."); }
        using (res)
        {
            if (res.StatusCode == HttpStatusCode.NotFound)
                throw new BusinessException(HasToken
                    ? $"Không thấy bản phát hành nào ở kho {Repo} (hoặc mã truy cập không được đọc kho này)."
                    : $"Kho {Repo} là kho riêng tư — cần nhập mã truy cập GitHub (token chỉ-đọc) ở Cập nhật phần mềm.");
            if (res.StatusCode is HttpStatusCode.Unauthorized)
                throw new BusinessException("Mã truy cập GitHub sai hoặc đã hết hạn — tạo mã mới và nhập lại.");
            if (res.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
                throw new BusinessException("GitHub tạm giới hạn số lần hỏi hoặc từ chối mã truy cập — thử lại sau ít phút.");
            if (!res.IsSuccessStatusCode) throw new BusinessException($"GitHub báo lỗi {(int)res.StatusCode}.");
            var json = await res.Content.ReadAsStringAsync(ct);
            _settings.Set(KeyLast, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            var info = Parse(json);
            return info is not null && info.Version > Current ? info : null;
        }
    }

    internal static UpdateInfo? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (r.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
        var tag = r.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return null;
        long exeId = 0, exeSize = 0; long? shaId = null;
        foreach (var a in r.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString();
            if (name == ExeAsset) { exeId = a.GetProperty("id").GetInt64(); exeSize = a.GetProperty("size").GetInt64(); }
            else if (name == ShaAsset) shaId = a.GetProperty("id").GetInt64();
        }
        if (exeId == 0) return null;
        DateTime? at = r.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String && p.TryGetDateTime(out var dt) ? dt.ToLocalTime() : null;
        var notes = r.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";
        return new UpdateInfo(new Version(v.Major, v.Minor, Math.Max(0, v.Build)), tag, notes, at, exeId, exeSize, shaId);
    }

    /// <summary>
    /// Tải KhoLanh.exe mới về <paramref name="destPath"/> (ghi tạm rồi đổi tên), kiểm tra kích thước, đầu file "MZ" và SHA-256
    /// (khớp file KhoLanh.exe.sha256 của bản phát hành). Sai thì xóa file và báo lỗi.
    /// </summary>
    public async Task DownloadAsync(UpdateInfo info, string destPath, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        string? expected = null;
        if (info.ShaAssetId is { } shaId)
        {
            var txt = Encoding.UTF8.GetString(await GetAsset(shaId, ct));
            expected = txt.Split(new[] { ' ', '\t', '\r', '\n', '*' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        }
        var tmp = destPath + ".tai";
        try
        {
            using (var h = Client("application/octet-stream"))
            using (var res = await h.GetAsync($"https://api.github.com/repos/{Repo}/releases/assets/{info.ExeAssetId}", HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!res.IsSuccessStatusCode) throw new BusinessException($"Tải bản mới thất bại (GitHub báo lỗi {(int)res.StatusCode}).");
                var total = res.Content.Headers.ContentLength ?? info.ExeSize;
                await using var src = await res.Content.ReadAsStreamAsync(ct);
                await using var f = File.Create(tmp);
                var buf = new byte[1 << 16];
                long got = 0; int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await f.WriteAsync(buf.AsMemory(0, n), ct);
                    got += n;
                    if (total > 0) progress?.Report(Math.Min(1.0, (double)got / total));
                }
            }
            var len = new FileInfo(tmp).Length;
            if (info.ExeSize > 0 && len != info.ExeSize) throw new BusinessException("File tải về không đủ dung lượng — thử lại.");
            await using (var f = File.OpenRead(tmp))
            {
                var head = new byte[2];
                if (await f.ReadAsync(head, ct) != 2 || head[0] != 'M' || head[1] != 'Z') throw new BusinessException("File tải về không phải chương trình Windows hợp lệ.");
                f.Position = 0;
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant();
                if (expected is not null && hash != expected) throw new BusinessException("Mã kiểm tra (SHA-256) của file tải về không khớp — đã hủy, thử lại.");
            }
            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tmp, destPath);
        }
        catch
        {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }

    private async Task<byte[]> GetAsset(long id, CancellationToken ct)
    {
        using var h = Client("application/octet-stream");
        using var res = await h.GetAsync($"https://api.github.com/repos/{Repo}/releases/assets/{id}", ct);
        if (!res.IsSuccessStatusCode) throw new BusinessException($"Tải mã kiểm tra thất bại (GitHub báo lỗi {(int)res.StatusCode}).");
        return await res.Content.ReadAsByteArrayAsync(ct);
    }
}
