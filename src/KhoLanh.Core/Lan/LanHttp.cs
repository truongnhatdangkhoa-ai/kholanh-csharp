using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.Lan;

/// <summary>Một đường gọi tới máy khác: TCP (<see cref="LanConnection"/>) hoặc HTTPS qua đường hầm (<see cref="HttpLanConnection"/>).</summary>
public interface ILanConn : IDisposable
{
    string Endpoint { get; }
    Task<JsonObject> CallAsync(JsonObject req, TimeSpan timeout, CancellationToken ct = default);
}

/// <summary>
/// 1.104: gói tin LAN qua HTTP(S) — để máy ở xa nối máy chủ đặt ở nhà qua Cloudflare Tunnel (máy con không cài gì, chỉ cần Internet).
/// Mỗi yêu cầu tự mã hóa bằng khóa LAN (Cloudflare / mạng giữa đường không đọc / sửa được): thân = "KLH1" + 16 byte ngẫu nhiên +
/// AES-256-GCM(JSON) với khóa = HMAC-SHA256(khóa LAN, "http-c2s" + số ngẫu nhiên); trả lời mã hóa bằng khóa "http-s2c" cùng số đó.
/// Chống phát lại: JSON kèm "ts" (lệch ≤ 5 phút) + máy chủ nhớ số ngẫu nhiên đã dùng 10 phút.
/// </summary>
public static class LanHttp
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("KLH1");
    public const string Path = "/kl";
    public static readonly TimeSpan MaxSkew = TimeSpan.FromMinutes(5);

    public static bool IsUrl(string ep) => ep.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || ep.StartsWith("http://", StringComparison.OrdinalIgnoreCase);

    /// <summary>"kho.kcenter.xyz" / "https://kho.kcenter.xyz/" → "https://kho.kcenter.xyz"; rỗng / sai → null.</summary>
    public static string? NormUrl(string? s)
    {
        s = (s ?? "").Trim().TrimEnd('/');
        if (s.Length == 0) return null;
        if (!IsUrl(s)) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || u.Host.Length == 0 || u.Host.Contains(' ')) return null;
        return (u.Scheme + "://" + u.Authority + u.AbsolutePath.TrimEnd('/')).TrimEnd('/');
    }

    private static byte[] Key(string lanKey, string label, ReadOnlySpan<byte> n)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(lanKey));
        return h.ComputeHash(Encoding.ASCII.GetBytes(label).Concat(n.ToArray()).ToArray());
    }

    private static byte[] Enc(byte[] key, byte[] plain)
    {
        var c = new byte[plain.Length]; var tag = new byte[16];
        using var a = new AesGcm(key, 16); a.Encrypt(new byte[12], plain, c, tag);   // khóa riêng mỗi yêu cầu → nonce 0 an toàn
        return c.Concat(tag).ToArray();
    }

    private static byte[]? Dec(byte[] key, ReadOnlySpan<byte> blob)
    {
        if (blob.Length < 16) return null;
        var p = new byte[blob.Length - 16];
        try { using var a = new AesGcm(key, 16); a.Decrypt(new byte[12], blob[..^16], blob[^16..], p); return p; }
        catch (CryptographicException) { return null; }
    }

    /// <summary>Phía máy con: gói yêu cầu; trả cả số ngẫu nhiên để mở trả lời.</summary>
    public static (byte[] Body, byte[] Nonce) SealRequest(string lanKey, JsonObject req, DateTime? now = null)
    {
        var o = (JsonObject)req.DeepClone(); o["ts"] = new DateTimeOffset(now ?? DateTime.UtcNow).ToUnixTimeMilliseconds();
        var n = RandomNumberGenerator.GetBytes(16);
        return (Magic.Concat(n).Concat(Enc(Key(lanKey, "http-c2s", n), Encoding.UTF8.GetBytes(o.ToJsonString()))).ToArray(), n);
    }

    public static JsonObject? OpenResponse(string lanKey, byte[] nonce, byte[] body)
    {
        var p = Dec(Key(lanKey, "http-s2c", nonce), body);
        return p is null ? null : JsonNode.Parse(Encoding.UTF8.GetString(p)) as JsonObject;
    }

    /// <summary>Phía máy chủ: mở yêu cầu. null = sai khóa / hỏng / quá hạn / phát lại.</summary>
    public static (JsonObject Req, byte[] Nonce)? OpenRequest(string lanKey, byte[] body, ReplayGuard guard, DateTime? now = null)
    {
        if (body.Length < 4 + 16 + 16 || !body.AsSpan(0, 4).SequenceEqual(Magic)) return null;
        var n = body.AsSpan(4, 16).ToArray();
        var p = Dec(Key(lanKey, "http-c2s", n), body.AsSpan(20));
        if (p is null) return null;
        if (JsonNode.Parse(Encoding.UTF8.GetString(p)) is not JsonObject o) return null;
        var t = now ?? DateTime.UtcNow;
        var ts = o["ts"]?.GetValue<long>() ?? 0;
        if (Math.Abs((DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime - t).TotalMinutes) > MaxSkew.TotalMinutes) return null;
        if (!guard.Add(Convert.ToHexString(n), t)) return null;
        o.Remove("ts");
        return (o, n);
    }

    public static byte[] SealResponse(string lanKey, byte[] nonce, JsonObject res) => Enc(Key(lanKey, "http-s2c", nonce), Encoding.UTF8.GetBytes(res.ToJsonString()));

    /// <summary>Nhớ số ngẫu nhiên đã dùng (gấp đôi hạn lệch giờ) để chặn gửi lại đúng gói tin cũ.</summary>
    public sealed class ReplayGuard
    {
        private readonly ConcurrentDictionary<string, DateTime> _seen = new();
        private DateTime _lastPrune = DateTime.MinValue;
        public bool Add(string id, DateTime now)
        {
            if (now - _lastPrune > TimeSpan.FromMinutes(1))
            {
                _lastPrune = now;
                foreach (var kv in _seen) if (now - kv.Value > MaxSkew * 2) _seen.TryRemove(kv.Key, out _);
            }
            return _seen.TryAdd(id, now);
        }
    }
}

/// <summary>Máy con gọi máy chủ qua HTTPS (Cloudflare Tunnel). Mỗi lần gọi = 1 yêu cầu POST, HttpClient dùng chung giữ kết nối.</summary>
public sealed class HttpLanConnection : ILanConn
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), ConnectTimeout = TimeSpan.FromSeconds(8) })
    { Timeout = Timeout.InfiniteTimeSpan };
    public string Endpoint { get; }
    private readonly string _key;
    public HttpLanConnection(string endpoint, string key) { Endpoint = endpoint.TrimEnd('/'); _key = key; }

    public async Task<JsonObject> CallAsync(JsonObject req, TimeSpan timeout, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // qua Internet chậm hơn LAN: lệnh "hello" 2 giây ở LAN → cho tối thiểu 8 giây
        cts.CancelAfter(timeout < TimeSpan.FromSeconds(8) ? TimeSpan.FromSeconds(8) : timeout);
        try
        {
            var (body, nonce) = LanHttp.SealRequest(_key, req);
            using var msg = new HttpRequestMessage(HttpMethod.Post, Endpoint + LanHttp.Path) { Content = new ByteArrayContent(body) };
            msg.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            using var res = await Http.SendAsync(msg, cts.Token).ConfigureAwait(false);
            var bytes = await res.Content.ReadAsByteArrayAsync(cts.Token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode)
                throw new IOException(res.StatusCode switch
                {
                    HttpStatusCode.Forbidden => "máy chủ từ chối (sai mã LAN / giờ máy lệch > 5 phút)",
                    HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout or (HttpStatusCode)530
                        => "đường hầm chạy nhưng máy chủ ở nhà không trả lời (máy tắt / app chưa mở / chưa làm máy chủ)",
                    _ => $"HTTP {(int)res.StatusCode}",
                });
            return LanHttp.OpenResponse(_key, nonce, bytes) ?? throw new IOException("Trả lời không giải mã được (sai mã LAN?).");
        }
        catch (Exception e) when (e is IOException or HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (ct.IsCancellationRequested) throw;
            throw new LanUnreachableException($"Không nối được {Endpoint}: {e.Message}", e);
        }
    }

    public void Dispose() { }
}

/// <summary>
/// Máy chủ HTTP tối giản CHỈ nghe 127.0.0.1 (cloudflared trên cùng máy chuyển yêu cầu từ Internet vào đây — cổng này không mở ra mạng).
/// POST /kl = gói tin LAN mã hóa; GET / = "KhoLanh OK" (kiểm tra đường hầm bằng trình duyệt).
/// </summary>
public sealed class LanHttpServer : IDisposable
{
    public const int MaxBody = 512 * 1024 * 1024;
    private readonly TcpListener _l;
    private readonly Func<string> _key;
    private readonly Func<JsonObject, string, JsonObject> _handle;
    private readonly LanHttp.ReplayGuard _guard = new();
    private readonly CancellationTokenSource _cts = new();
    public int Port { get; }
    /// <summary>Lần cuối nhận được yêu cầu hợp lệ qua đường hầm (để hiện "đường hầm đang chạy").</summary>
    public DateTime LastOk { get; private set; }

    public LanHttpServer(int port, Func<string> key, Func<JsonObject, string, JsonObject> handle)
    {
        Port = port; _key = key; _handle = handle;
        _l = new TcpListener(IPAddress.Loopback, port);
        _l.Start();
        _ = Task.Run(Accept);   // không bám luồng giao diện (Start gọi từ luồng giao diện)
    }

    private async Task Accept()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await _l.AcceptTcpClientAsync(_cts.Token); } catch { break; }
            _ = Task.Run(() => Serve(c));
        }
    }

    private async Task Serve(TcpClient c)
    {
        using (c)
        {
            var s = c.GetStream();
            try
            {
                while (!_cts.IsCancellationRequested)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
                    idle.CancelAfter(TimeSpan.FromSeconds(120));
                    var head = await ReadHead(s, idle.Token);
                    if (head is null) return;
                    var lines = head.Split("\r\n");
                    var first = lines[0].Split(' ');
                    if (first.Length < 2) return;
                    var hdr = lines.Skip(1).Select(l => l.Split(':', 2)).Where(p => p.Length == 2)
                        .GroupBy(p => p[0].Trim().ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First()[1].Trim());
                    var len = hdr.TryGetValue("content-length", out var cl) && long.TryParse(cl, out var n) ? n : 0;
                    if (len < 0 || len > MaxBody) { await Reply(s, 413, "text/plain", "Too large"u8.ToArray(), false); return; }
                    var body = new byte[len];
                    var got = 0; while (got < len) { var r = await s.ReadAsync(body.AsMemory(got), idle.Token); if (r == 0) return; got += r; }
                    var keep = !(hdr.TryGetValue("connection", out var conn) && conn.Equals("close", StringComparison.OrdinalIgnoreCase));
                    var remote = hdr.GetValueOrDefault("cf-connecting-ip") ?? hdr.GetValueOrDefault("x-forwarded-for")?.Split(',')[0].Trim() ?? "";
                    if (first[0] == "GET") { await Reply(s, 200, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes("KhoLanh OK"), keep); }
                    else if (first[0] == "POST" && first[1].Split('?')[0] == LanHttp.Path)
                    {
                        var key = _key();
                        var open = LanHttp.OpenRequest(key, body, _guard);
                        if (open is null) { await Reply(s, 403, "text/plain", "Forbidden"u8.ToArray(), keep); }
                        else
                        {
                            JsonObject res;
                            // remote là IP công khai của máy con — không dùng làm địa chỉ nối lại (máy con sau NAT), nên để trống
                            try { res = _handle(open.Value.Req, ""); }
                            catch (BusinessException e) { res = new JsonObject { ["ok"] = false, ["error"] = e.Message }; }
                            catch (Exception e) { res = new JsonObject { ["ok"] = false, ["error"] = "Máy chủ lỗi: " + e.Message }; }
                            LastOk = DateTime.Now; _ = remote;
                            await Reply(s, 200, "application/octet-stream", LanHttp.SealResponse(key, open.Value.Nonce, res), keep);
                        }
                    }
                    else await Reply(s, 404, "text/plain", "Not found"u8.ToArray(), keep);
                    if (!keep) return;
                }
            }
            catch { /* đóng kết nối */ }
        }
    }

    private static async Task<string?> ReadHead(NetworkStream s, CancellationToken ct)
    {
        var buf = new List<byte>(1024); var one = new byte[1];
        while (buf.Count < 64 * 1024)
        {
            var r = await s.ReadAsync(one, ct);
            if (r == 0) return null;
            buf.Add(one[0]);
            var n = buf.Count;
            if (n >= 4 && buf[n - 4] == '\r' && buf[n - 3] == '\n' && buf[n - 2] == '\r' && buf[n - 1] == '\n')
                return Encoding.ASCII.GetString(buf.ToArray(), 0, n - 4);
        }
        return null;
    }

    private static async Task Reply(NetworkStream s, int code, string type, byte[] body, bool keep)
    {
        var reason = code switch { 200 => "OK", 403 => "Forbidden", 404 => "Not Found", 413 => "Payload Too Large", _ => "Error" };
        var h = $"HTTP/1.1 {code} {reason}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nConnection: {(keep ? "keep-alive" : "close")}\r\nCache-Control: no-store\r\n\r\n";
        await s.WriteAsync(Encoding.ASCII.GetBytes(h));
        await s.WriteAsync(body);
        await s.FlushAsync();
    }

    public void Dispose() { try { _cts.Cancel(); } catch { } try { _l.Stop(); } catch { } }
}
