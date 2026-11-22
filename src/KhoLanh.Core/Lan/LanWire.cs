using System.Buffers.Binary;
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

/// <summary>Một kết nối tới một máy (giữ mở, gọi lần lượt). Lỗi mạng → đóng, lần gọi sau tự nối lại.</summary>
public sealed class LanConnection : IDisposable
{
    public string Endpoint { get; }
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TcpClient? _tcp;
    private NetworkStream? _stream;

    public LanConnection(string endpoint) { Endpoint = endpoint; }

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
                if (_stream is null)
                {
                    var (host, port) = Split(Endpoint);
                    _tcp = new TcpClient { NoDelay = true };
                    using var cc = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cc.CancelAfter(TimeSpan.FromSeconds(2));
                    await _tcp.ConnectAsync(host, port, cc.Token);
                    _stream = _tcp.GetStream();
                }
                await LanWire.WriteAsync(_stream, req.ToJsonString(), cts.Token);
                var text = await LanWire.ReadAsync(_stream, cts.Token) ?? throw new IOException("Máy bên kia đã đóng kết nối.");
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

    private void Close()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tcp?.Dispose(); } catch { }
        _stream = null; _tcp = null;
    }

    public void Dispose() { Close(); }
}
