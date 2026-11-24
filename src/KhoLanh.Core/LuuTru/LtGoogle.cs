using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.LuuTru;

/// <summary>Lỗi Google cần đăng nhập lại (refresh token hết hạn / bị thu hồi / thiếu quyền Drive).</summary>
public sealed class LtRelinkException : BusinessException
{
    public LtRelinkException(string message) : base(message) { }
}

/// <summary>
/// Đăng nhập Google CÁ NHÂN ngay trong module (OAuth 2.0 cho ứng dụng máy tính: mở trình duyệt, Google gọi lại
/// http://127.0.0.1:&lt;cổng ngẫu nhiên&gt;/, PKCE S256, kiểm tra state) + các lệnh Google Drive tối thiểu cho sao lưu.
/// Quyền xin: <c>drive.file</c> (app chỉ thấy tệp / thư mục do chính nó tạo — không đọc được Drive khác của người dùng) + <c>openid email</c>.
/// </summary>
public sealed class LtGoogle
{
    public const string Scope = "openid email https://www.googleapis.com/auth/drive.file";
    public const string DriveScope = "https://www.googleapis.com/auth/drive.file";
    public const string FolderMime = "application/vnd.google-apps.folder";
    public const string AuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
    public const string TokenUrl = "https://oauth2.googleapis.com/token";
    public const string RevokeUrl = "https://oauth2.googleapis.com/revoke";
    public const string Api = "https://www.googleapis.com/drive/v3/files";
    public const string UploadApi = "https://www.googleapis.com/upload/drive/v3/files";

    private readonly HttpClient _http;
    private string _access = "";
    private DateTime _accessUntil = DateTime.MinValue;
    private string _accessFor = "";

    public LtGoogle(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler, false);
        _http.Timeout = TimeSpan.FromMinutes(30);
    }

    public sealed record Client(string Id, string Secret)
    {
        public bool Ok => Id.Trim().Length > 0;
    }

    public sealed record LinkResult(string Refresh, string Access, int ExpiresIn, string Email, string Scope);

    // ------------------------------------------------------------------ đăng nhập

    public static (string Verifier, string Challenge) Pkce()
    {
        var verifier = B64Url(RandomNumberGenerator.GetBytes(48));
        var challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return (verifier, challenge);
    }

    public static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string BuildAuthUrl(Client c, string redirect, string challenge, string state, string? loginHint = null)
    {
        var q = new Dictionary<string, string>
        {
            ["client_id"] = c.Id.Trim(), ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = Scope,
            ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["state"] = state,
            ["access_type"] = "offline", ["prompt"] = "consent select_account", ["include_granted_scopes"] = "true",
        };
        if (!string.IsNullOrEmpty(loginHint)) q["login_hint"] = loginHint;
        return AuthUrl + "?" + string.Join("&", q.Select(kv => kv.Key + "=" + Uri.EscapeDataString(kv.Value)));
    }

    /// <summary>Đọc email trong id_token (chỉ để hiện — không dùng xác thực).</summary>
    public static string EmailOf(string idToken)
    {
        try
        {
            var p = (idToken ?? "").Split('.');
            if (p.Length < 2) return "";
            var s = p[1].Replace('-', '+').Replace('_', '/');
            s += new string('=', (4 - s.Length % 4) % 4);
            return JsonNode.Parse(Convert.FromBase64String(s))?["email"]?.GetValue<string>() ?? "";
        }
        catch { return ""; }
    }

    /// <summary>
    /// Đăng nhập: mở trình duyệt (openBrowser), chờ Google gọi lại cổng nội bộ (tối đa 5 phút), đổi mã lấy token.
    /// Dùng TcpListener (không cần quyền quản trị Windows như HttpListener).
    /// </summary>
    public async Task<LinkResult> SignInAsync(Client c, Action<string> openBrowser, CancellationToken ct, string? loginHint = null)
    {
        if (!c.Ok) throw new BusinessException("Chưa nhập Client ID của Google (mục Cài đặt OAuth).");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var redirect = $"http://127.0.0.1:{port}/";
            var (verifier, challenge) = Pkce();
            var state = B64Url(RandomNumberGenerator.GetBytes(16));
            openBrowser(BuildAuthUrl(c, redirect, challenge, state, loginHint));

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(5));
            string code = "";
            while (code.Length == 0)
            {
                TcpClient tcp;
                try { tcp = await listener.AcceptTcpClientAsync(timeout.Token); }
                catch (OperationCanceledException) { throw new BusinessException(ct.IsCancellationRequested ? "Đã hủy đăng nhập Google." : "Quá 5 phút chưa đăng nhập xong — bấm Đăng nhập Google để thử lại."); }
                using (tcp)
                {
                    var stream = tcp.GetStream();
                    var line = await ReadRequestLine(stream, timeout.Token);
                    var q = ParseQuery(line);
                    string msg; bool done = false;
                    if (q.GetValueOrDefault("state") != state) msg = "Yêu cầu không hợp lệ — đóng trang này.";
                    else if (q.TryGetValue("error", out var err)) { msg = "Google báo: " + err + ". Đóng trang này và thử lại trong phần mềm."; done = true; code = "\0" + err; }
                    else if (q.TryGetValue("code", out var cd) && cd.Length > 0) { code = cd; msg = "Đã đăng nhập Google. Quay lại phần mềm Kho Lạnh."; done = true; }
                    else msg = "Thiếu mã đăng nhập — đóng trang này.";
                    var html = "<!doctype html><meta charset=utf-8><title>Kho Lạnh</title><body style=\"font-family:Segoe UI,Arial;padding:40px;color:#1A2433\"><h2>" +
                               WebUtility.HtmlEncode(msg) + "</h2><p>Có thể đóng thẻ này.</p></body>";
                    var body = Encoding.UTF8.GetBytes(html);
                    var head = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                    try { await stream.WriteAsync(head, timeout.Token); await stream.WriteAsync(body, timeout.Token); } catch { /* trình duyệt đã đóng */ }
                    if (!done) continue;
                }
            }
            if (code.StartsWith('\0'))
                throw new BusinessException(code == "\0access_denied" ? "Bạn đã từ chối cho phép trên trang Google." : "Google báo lỗi: " + code[1..]);
            var form = new Dictionary<string, string>
            {
                ["code"] = code, ["client_id"] = c.Id.Trim(), ["redirect_uri"] = redirect, ["grant_type"] = "authorization_code", ["code_verifier"] = verifier,
            };
            if (c.Secret.Trim().Length > 0) form["client_secret"] = c.Secret.Trim();
            var j = await PostForm(TokenUrl, form, ct);
            var scope = j["scope"]?.GetValue<string>() ?? "";
            var res = new LinkResult(j["refresh_token"]?.GetValue<string>() ?? "", j["access_token"]?.GetValue<string>() ?? "",
                j["expires_in"]?.GetValue<int>() ?? 3000, EmailOf(j["id_token"]?.GetValue<string>() ?? ""), scope);
            if (scope.Length > 0 && !scope.Contains(DriveScope))
            {
                await RevokeAsync(res.Refresh.Length > 0 ? res.Refresh : res.Access);
                throw new BusinessException("Google chưa cấp quyền Drive (ở trang cho phép chưa tick ô \"Xem, sửa, tạo và xóa các tệp Google Drive mà ứng dụng này dùng\"). Đăng nhập lại và tick đủ các ô.");
            }
            if (res.Refresh.Length == 0) throw new BusinessException("Google không trả mã dùng lâu dài (refresh token). Vào myaccount.google.com › Bảo mật › Ứng dụng bên thứ ba, gỡ quyền của app rồi đăng nhập lại.");
            _access = res.Access; _accessFor = res.Refresh; _accessUntil = DateTime.UtcNow.AddSeconds(res.ExpiresIn - 120);
            return res;
        }
        finally { listener.Stop(); }
    }

    private static async Task<string> ReadRequestLine(NetworkStream s, CancellationToken ct)
    {
        var buf = new byte[8192]; int n = 0;
        while (n < buf.Length)
        {
            var k = await s.ReadAsync(buf.AsMemory(n), ct);
            if (k <= 0) break;
            n += k;
            var txt = Encoding.ASCII.GetString(buf, 0, n);
            if (txt.Contains("\r\n\r\n") || txt.Contains("\n\n")) break;
        }
        var all = Encoding.ASCII.GetString(buf, 0, n);
        var i = all.IndexOf('\n');
        return i < 0 ? all : all[..i].TrimEnd('\r');
    }

    /// <summary>"GET /?code=..&amp;state=.. HTTP/1.1" → các tham số.</summary>
    public static Dictionary<string, string> ParseQuery(string requestLine)
    {
        var d = new Dictionary<string, string>();
        var parts = (requestLine ?? "").Split(' ');
        if (parts.Length < 2) return d;
        var path = parts[1];
        var qi = path.IndexOf('?');
        if (qi < 0) return d;
        foreach (var kv in path[(qi + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = kv.IndexOf('=');
            var k = Uri.UnescapeDataString((eq < 0 ? kv : kv[..eq]).Replace('+', ' '));
            var v = eq < 0 ? "" : Uri.UnescapeDataString(kv[(eq + 1)..].Replace('+', ' '));
            d[k] = v;
        }
        return d;
    }

    public async Task RevokeAsync(string token)
    {
        if (token.Length == 0) return;
        try { using var r = await _http.PostAsync(RevokeUrl, new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token })); }
        catch { /* thu hồi không được thì vẫn xóa ở máy */ }
        if (_accessFor == token) { _access = ""; _accessFor = ""; }
    }

    // ------------------------------------------------------------------ token

    /// <summary>Mã truy cập còn hạn (làm mới bằng refresh token khi cần).</summary>
    public async Task<string> AccessAsync(Client c, string refresh, CancellationToken ct = default)
    {
        if (refresh.Length == 0) throw new LtRelinkException("Chưa đăng nhập Google.");
        if (_accessFor == refresh && _access.Length > 0 && DateTime.UtcNow < _accessUntil) return _access;
        var form = new Dictionary<string, string> { ["client_id"] = c.Id.Trim(), ["refresh_token"] = refresh, ["grant_type"] = "refresh_token" };
        if (c.Secret.Trim().Length > 0) form["client_secret"] = c.Secret.Trim();
        JsonObject j;
        try { j = await PostForm(TokenUrl, form, ct); }
        catch (BusinessException e) when (e.Message.Contains("invalid_grant") || e.Message.Contains("unauthorized_client") || e.Message.Contains("invalid_client"))
        { throw new LtRelinkException("Phiên đăng nhập Google đã hết hạn hoặc bị thu hồi — bấm Đăng nhập Google để đăng nhập lại."); }
        _access = j["access_token"]?.GetValue<string>() ?? "";
        _accessFor = refresh;
        _accessUntil = DateTime.UtcNow.AddSeconds((j["expires_in"]?.GetValue<int>() ?? 3000) - 120);
        return _access;
    }

    private async Task<JsonObject> PostForm(string url, Dictionary<string, string> form, CancellationToken ct)
    {
        HttpResponseMessage r;
        try { r = await _http.PostAsync(url, new FormUrlEncodedContent(form), ct); }
        catch (HttpRequestException e) { throw new BusinessException("Không kết nối được Google: " + e.Message); }
        using (r)
        {
            var txt = await r.Content.ReadAsStringAsync(ct);
            JsonObject? j = null;
            try { j = JsonNode.Parse(txt) as JsonObject; } catch { /* không phải JSON */ }
            if (!r.IsSuccessStatusCode || j is null)
            {
                var err = j?["error"]?.ToString() ?? ((int)r.StatusCode).ToString();
                var desc = j?["error_description"]?.ToString() ?? "";
                throw new BusinessException("Google từ chối (" + err + (desc.Length > 0 ? ": " + desc : "") + ").");
            }
            return j;
        }
    }

    // ------------------------------------------------------------------ Drive

    public sealed record DFile(string Id, string Name, long Size, DateTime Modified, bool Folder);

    private async Task<HttpResponseMessage> Send(string access, HttpRequestMessage req, CancellationToken ct, bool allow404 = false)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", access);
        HttpResponseMessage r;
        try { r = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException e) { throw new BusinessException("Không kết nối được Google Drive: " + e.Message); }
        if (r.IsSuccessStatusCode || (allow404 && r.StatusCode == HttpStatusCode.NotFound)) return r;
        var txt = "";
        try { txt = await r.Content.ReadAsStringAsync(ct); } catch { }
        r.Dispose();
        var msg = "";
        try { msg = JsonNode.Parse(txt)?["error"]?["message"]?.ToString() ?? ""; } catch { }
        if (r.StatusCode == HttpStatusCode.Unauthorized) { _access = ""; throw new LtRelinkException("Google Drive từ chối phiên đăng nhập — đăng nhập Google lại."); }
        if (r.StatusCode == HttpStatusCode.Forbidden && msg.Contains("insufficient", StringComparison.OrdinalIgnoreCase))
            throw new LtRelinkException("Google chưa cấp quyền Drive — đăng nhập lại và tick ô quyền Drive.");
        if (r.StatusCode == HttpStatusCode.Forbidden && msg.Contains("storage", StringComparison.OrdinalIgnoreCase))
            throw new BusinessException("Google Drive đã hết dung lượng.");
        throw new BusinessException($"Google Drive báo lỗi {(int)r.StatusCode}{(msg.Length > 0 ? ": " + msg : "")}.");
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("'", "\\'");

    public async Task<List<DFile>> ListAsync(string access, string parentId, CancellationToken ct = default, string? name = null)
    {
        var res = new List<DFile>();
        string? page = null;
        do
        {
            var q = $"'{Esc(parentId)}' in parents and trashed=false" + (name is null ? "" : $" and name='{Esc(name)}'");
            var url = Api + "?q=" + Uri.EscapeDataString(q) + "&spaces=drive&pageSize=1000&fields=" + Uri.EscapeDataString("nextPageToken,files(id,name,size,modifiedTime,mimeType)")
                      + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            using var r = await Send(access, new HttpRequestMessage(HttpMethod.Get, url), ct);
            var j = JsonNode.Parse(await r.Content.ReadAsStringAsync(ct)) as JsonObject;
            foreach (var f in j?["files"] as JsonArray ?? new JsonArray())
            {
                if (f is null) continue;
                long.TryParse(f["size"]?.ToString(), out var size);
                DateTime.TryParse(f["modifiedTime"]?.ToString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var mod);
                res.Add(new DFile(f["id"]!.ToString(), f["name"]?.ToString() ?? "", size, mod.ToLocalTime(), f["mimeType"]?.ToString() == FolderMime));
            }
            page = j?["nextPageToken"]?.ToString();
        } while (!string.IsNullOrEmpty(page));
        return res;
    }

    /// <summary>Tìm thư mục con theo tên (tạo nếu chưa có). parent = "root" cho thư mục gốc Drive.</summary>
    public async Task<string> EnsureFolderAsync(string access, string parentId, string name, CancellationToken ct = default)
    {
        var found = (await ListAsync(access, parentId, ct, name)).FirstOrDefault(f => f.Folder);
        if (found is not null) return found.Id;
        var meta = new JsonObject { ["name"] = name, ["mimeType"] = FolderMime, ["parents"] = new JsonArray(parentId) };
        using var r = await Send(access, new HttpRequestMessage(HttpMethod.Post, Api + "?fields=id") { Content = JsonBody(meta) }, ct);
        return JsonNode.Parse(await r.Content.ReadAsStringAsync(ct))!["id"]!.ToString();
    }

    private static StringContent JsonBody(JsonNode n) => new(n.ToJsonString(), Encoding.UTF8, "application/json");

    /// <summary>Tải 1 tệp lên (tạo mới trong thư mục, hoặc ghi đè nội dung nếu có fileId). Kiểu tải resumable — không giữ cả tệp trong bộ nhớ.</summary>
    public async Task<string> UploadAsync(string access, string localPath, string name, string mime, string? parentId, string? fileId = null, CancellationToken ct = default)
    {
        var size = new FileInfo(localPath).Length;
        HttpRequestMessage init;
        if (fileId is null)
        {
            var meta = new JsonObject { ["name"] = name, ["parents"] = new JsonArray(parentId) };
            init = new HttpRequestMessage(HttpMethod.Post, UploadApi + "?uploadType=resumable&fields=id") { Content = JsonBody(meta) };
        }
        else init = new HttpRequestMessage(HttpMethod.Patch, UploadApi + "/" + Uri.EscapeDataString(fileId) + "?uploadType=resumable&fields=id") { Content = JsonBody(new JsonObject()) };
        init.Headers.Add("X-Upload-Content-Type", mime);
        init.Headers.Add("X-Upload-Content-Length", size.ToString());
        Uri? loc;
        using (var r = await Send(access, init, ct)) loc = r.Headers.Location;
        if (loc is null) throw new BusinessException("Google Drive không trả địa chỉ tải lên.");
        await using var fs = File.OpenRead(localPath);
        var put = new HttpRequestMessage(HttpMethod.Put, loc) { Content = new StreamContent(fs) };
        put.Content.Headers.ContentType = new MediaTypeHeaderValue(mime.Length > 0 ? mime : "application/octet-stream");
        put.Content.Headers.ContentLength = size;
        using var pr = await Send(access, put, ct);
        var j = JsonNode.Parse(await pr.Content.ReadAsStringAsync(ct));
        return j?["id"]?.ToString() ?? fileId ?? "";
    }

    /// <summary>Tải tệp về; false nếu tệp không còn trên Drive.</summary>
    public async Task<bool> DownloadAsync(string access, string fileId, string toPath, CancellationToken ct = default)
    {
        using var r = await Send(access, new HttpRequestMessage(HttpMethod.Get, Api + "/" + Uri.EscapeDataString(fileId) + "?alt=media"), ct, allow404: true);
        if (r.StatusCode == HttpStatusCode.NotFound) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(toPath))!);
        var tmp = toPath + ".tam";
        await using (var fs = File.Create(tmp)) await r.Content.CopyToAsync(fs, ct);
        File.Move(tmp, toPath, true);
        return true;
    }

    public async Task<string> DownloadTextAsync(string access, string fileId, CancellationToken ct = default)
    {
        using var r = await Send(access, new HttpRequestMessage(HttpMethod.Get, Api + "/" + Uri.EscapeDataString(fileId) + "?alt=media"), ct);
        return await r.Content.ReadAsStringAsync(ct);
    }

    public async Task DeleteAsync(string access, string fileId, CancellationToken ct = default)
    {
        using var r = await Send(access, new HttpRequestMessage(HttpMethod.Delete, Api + "/" + Uri.EscapeDataString(fileId)), ct, allow404: true);
    }

    /// <summary>Dung lượng Drive (đã dùng / tổng, byte; tổng = 0 khi không giới hạn). Không đọc được thì null.</summary>
    public async Task<(long Used, long Limit)?> QuotaAsync(string access, CancellationToken ct = default)
    {
        try
        {
            using var r = await Send(access, new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/drive/v3/about?fields=storageQuota"), ct);
            var q = JsonNode.Parse(await r.Content.ReadAsStringAsync(ct))?["storageQuota"];
            long.TryParse(q?["usage"]?.ToString(), out var used); long.TryParse(q?["limit"]?.ToString(), out var lim);
            return (used, lim);
        }
        catch { return null; }
    }
}
