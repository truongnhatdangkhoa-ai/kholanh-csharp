using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace KhoLanh.Core.Lan;

/// <summary>Không nối được máy bên kia (tắt máy, đứt mạng, hết giờ chờ).</summary>
public class LanUnreachableException : Exception
{
    public LanUnreachableException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>Khung tin trên TCP: 4 byte độ dài (little-endian) + JSON UTF-8.</summary>
public static class LanWire
{
    public const int MaxFrame = 512 * 1024 * 1024;

    public static async Task WriteAsync(Stream s, string json, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(json);
        var head = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(head, body.Length);
        await s.WriteAsync(head, ct);
        await s.WriteAsync(body, ct);
        await s.FlushAsync(ct);
    }

    /// <summary>Đọc một khung; null = bên kia đóng kết nối.</summary>
    public static async Task<string?> ReadAsync(Stream s, CancellationToken ct)
    {
        var head = new byte[4];
        if (!await Fill(s, head, ct)) return null;
        var n = BinaryPrimitives.ReadInt32LittleEndian(head);
        if (n < 0 || n > MaxFrame) throw new IOException("Khung tin LAN không hợp lệ.");
        var body = new byte[n];
        if (!await Fill(s, body, ct)) return null;
        return Encoding.UTF8.GetString(body);
    }

    private static async Task<bool> Fill(Stream s, byte[] buf, CancellationToken ct)
    {
        var got = 0;
        while (got < buf.Length)
        {
            var r = await s.ReadAsync(buf.AsMemory(got), ct);
            if (r == 0) return false;
            got += r;
        }
        return true;
    }
}

/// <summary>Kênh tin (đọc / ghi từng khung JSON).</summary>
public interface ILanChannel
{
    Task WriteAsync(string json, CancellationToken ct);
    Task<string?> ReadAsync(CancellationToken ct);
}
public sealed class PlainChannel : ILanChannel
{
    private readonly Stream _s; private readonly byte[]? _firstHead; private bool _used;
    public PlainChannel(Stream s, byte[]? firstHead = null) { _s = s; _firstHead = firstHead; }
    public Task WriteAsync(string json, CancellationToken ct) => LanWire.WriteAsync(_s, json, ct);
    public async Task<string?> ReadAsync(CancellationToken ct)
    {
        if (_firstHead is not null && !_used)
        {
            _used = true;
            var n = BinaryPrimitives.ReadInt32LittleEndian(_firstHead);
            if (n < 0 || n > LanWire.MaxFrame) throw new IOException("Khung tin LAN không hợp lệ.");
            var body = new byte[n]; var got = 0;
            while (got < n) { var r = await _s.ReadAsync(body.AsMemory(got), ct); if (r == 0) return null; got += r; }
            return Encoding.UTF8.GetString(body);
        }
        return await LanWire.ReadAsync(_s, ct);
    }
}

/// <summary>
/// Kênh MÃ HÓA (AES-256-GCM) dùng khóa LAN: bắt tay "KLE1" + 16 byte ngẫu nhiên mỗi bên → khóa phiên riêng cho từng chiều
/// (HMAC-SHA256 của khóa LAN với hai số ngẫu nhiên), nonce = bộ đếm khung → không đọc lén, không sửa, không phát lại được.
/// Sai khóa LAN → giải mã thất bại → ngắt kết nối. Không cần chứng chỉ nên máy dự phòng / máy con dùng chung một khóa như trước.
/// </summary>
public sealed class SecureChannel : ILanChannel
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("KLE1");
    private readonly Stream _s; private readonly AesGcm _send, _recv; private ulong _sc, _rc;

    private SecureChannel(Stream s, byte[] sendKey, byte[] recvKey) { _s = s; _send = new AesGcm(sendKey, 16); _recv = new AesGcm(recvKey, 16); }

    private static byte[] Derive(string lanKey, byte[] cn, byte[] sn, string label)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(lanKey));
        return h.ComputeHash(Encoding.ASCII.GetBytes(label).Concat(cn).Concat(sn).ToArray());
    }
    private static async Task ReadExactly(Stream s, byte[] buf, CancellationToken ct)
    {
        var got = 0; while (got < buf.Length) { var r = await s.ReadAsync(buf.AsMemory(got), ct); if (r == 0) throw new IOException("Máy bên kia đã đóng kết nối."); got += r; }
    }
    public static async Task<SecureChannel> ClientAsync(Stream s, string lanKey, CancellationToken ct)
    {
        var cn = RandomNumberGenerator.GetBytes(16);
        await s.WriteAsync(Magic.Concat(cn).ToArray(), ct); await s.FlushAsync(ct);
        var sn = new byte[16]; await ReadExactly(s, sn, ct);
        return new SecureChannel(s, Derive(lanKey, cn, sn, "c2s"), Derive(lanKey, cn, sn, "s2c"));
    }
    /// <summary>Phía máy chủ: đã đọc xong 4 byte "KLE1".</summary>
    public static async Task<SecureChannel> ServerAsync(Stream s, string lanKey, CancellationToken ct)
    {
        var cn = new byte[16]; await ReadExactly(s, cn, ct);
        var sn = RandomNumberGenerator.GetBytes(16);
        await s.WriteAsync(sn, ct); await s.FlushAsync(ct);
        return new SecureChannel(s, Derive(lanKey, cn, sn, "s2c"), Derive(lanKey, cn, sn, "c2s"));
    }
    private static byte[] Nonce(ulong c) { var n = new byte[12]; BinaryPrimitives.WriteUInt64LittleEndian(n.AsSpan(4), c); return n; }

    public async Task WriteAsync(string json, CancellationToken ct)
    {
        var plain = Encoding.UTF8.GetBytes(json); var ct1 = new byte[plain.Length]; var tag = new byte[16];
        _send.Encrypt(Nonce(_sc++), plain, ct1, tag);
        var head = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(head, ct1.Length + 16);
        await _s.WriteAsync(head, ct); await _s.WriteAsync(ct1, ct); await _s.WriteAsync(tag, ct); await _s.FlushAsync(ct);
    }
    public async Task<string?> ReadAsync(CancellationToken ct)
    {
        var head = new byte[4];
        var got = 0; while (got < 4) { var r = await _s.ReadAsync(head.AsMemory(got), ct); if (r == 0) { if (got == 0) return null; throw new IOException("Máy bên kia đã đóng kết nối."); } got += r; }
        var n = BinaryPrimitives.ReadInt32LittleEndian(head);
        if (n < 16 || n > LanWire.MaxFrame) throw new IOException("Khung tin LAN không hợp lệ.");
        var body = new byte[n]; await ReadExactly(_s, body, ct);
        var plain = new byte[n - 16];
        try { _recv.Decrypt(Nonce(_rc++), body.AsSpan(0, n - 16), body.AsSpan(n - 16), plain); }
        catch (CryptographicException) { throw new IOException("Sai mã LAN hoặc dữ liệu bị sửa trên đường truyền."); }
        return Encoding.UTF8.GetString(plain);
    }
}

/// <summary>Một kết nối tới một máy (giữ mở, gọi lần lượt). Lỗi mạng → đóng, lần gọi sau tự nối lại.</summary>
public sealed class LanConnection : IDisposable
{
    public string Endpoint { get; }
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _tcp;
    private NetworkStream? _stream;

    private readonly string _key; private readonly bool _requireEnc; private ILanChannel? _chan;
    /// <param name="key">Khóa LAN (để mã hóa kênh).</param>
    /// <param name="requireEnc">true = không bao giờ hạ xuống kênh thường; false = máy chủ cũ chưa hiểu mã hóa thì dùng kênh thường.</param>
    public LanConnection(string endpoint, string key = "", bool requireEnc = false) { Endpoint = endpoint; _key = key; _requireEnc = requireEnc; }
    /// <summary>Kênh đang dùng có mã hóa không (để hiện cho người dùng).</summary>
    public bool Encrypted => _chan is SecureChannel;

    public static (string host, int port) Split(string endpoint)
    {
        var i = endpoint.LastIndexOf(':');
        return i > 0 && int.TryParse(endpoint[(i + 1)..], out var p) ? (endpoint[..i], p) : (endpoint, 47820);
    }

    public async Task<JsonObject> CallAsync(JsonObject req, TimeSpan timeout, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                if (_chan is null) await OpenAsync(ct, cts.Token);
                await _chan!.WriteAsync(req.ToJsonString(), cts.Token);
                var text = await _chan.ReadAsync(cts.Token) ?? throw new IOException("Máy bên kia đã đóng kết nối.");
                return JsonNode.Parse(text) as JsonObject ?? throw new IOException("Trả lời không hợp lệ.");
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException or ObjectDisposedException or System.Text.Json.JsonException)
            {
                Close();
                if (ct.IsCancellationRequested) throw;
                throw new LanUnreachableException($"Không nối được {Endpoint}: {e.Message}", e);
            }
        }
        finally { _gate.Release(); }
    }

    private async Task Connect(CancellationToken ct)
    {
        var (host, port) = Split(Endpoint);
        _tcp = new TcpClient { NoDelay = true };
        using var cc = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cc.CancelAfter(TimeSpan.FromSeconds(2));
        await _tcp.ConnectAsync(host, port, cc.Token);
        _stream = _tcp.GetStream();
    }
    private async Task OpenAsync(CancellationToken outer, CancellationToken ct)
    {
        await Connect(outer);
        if (_key.Length == 0) { _chan = new PlainChannel(_stream!); return; }
        try
        {
            var ch = await SecureChannel.ClientAsync(_stream!, _key, ct);
            // thử một khung nhỏ để biết máy chủ có hiểu mã hóa không (máy chủ cũ đóng kết nối ngay sau 4 byte đầu)
            _chan = ch;
        }
        catch (Exception e) when ((e is IOException or SocketException) && !_requireEnc)
        {
            Close(); await Connect(outer); _chan = new PlainChannel(_stream!);   // máy chủ bản cũ → kênh thường
        }
    }

    private void Close()
    {
        _chan = null;
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _stream = null; _tcp = null;
    }

    public void Dispose() { Close(); }
}
