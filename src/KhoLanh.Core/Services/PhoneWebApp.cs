using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KhoLanh.Core.Services;

/// <summary>
/// Kết nối ĐIỆN THOẠI: đọc / ghi Google Sheet đồng bộ qua Apps Script Web App mà app điện thoại đang dùng (chép core/sheets-webapp-client.js
/// của app cũ — cùng lệnh pc.ping / pc.ensureTabs / pc.readTable / pc.writeTable / pc.markRows, cùng tên tab và cột). Cấu hình: đường dẫn
/// Web App (…/exec) + mã PC_TOKEN, lưu trong cài đặt của máy (mã cất bằng <see cref="Secret"/>).
/// </summary>
public sealed class PhoneWebApp
{
    public const string KeyUrl = "phone_webapp_url", KeyToken = "phone_pc_token";
    /// <summary>1.58: Web App của Google Sheet MỚI (hệ thống app PC C# + app điện thoại kholanh-csharp-mobile) — cài sẵn, máy chưa
    /// lưu đường dẫn riêng thì dùng link này; người dùng chỉ nhập mã PC_TOKEN. KHÔNG phải Sheet của app cũ.</summary>
    public const string DefaultUrl = "https://script.google.com/macros/s/AKfycbyUgzXV6Nvkurd6QSuqHpvMHOwbzP5zKsXb19flE8HgAL3U_RxcyhTw0uwk_usUIqdr/exec";
    /// <summary>Tiêu đề 2 tab phiếu xuất — PHẢI khớp PX_HEADERS / PXI_HEADERS trong Code.gs.</summary>
    public static readonly string[] PhieuHeaders = { "id", "maPhieu", "ngayTao", "trangThai", "nguoiTao", "ghiChu", "xuLyLuc", "module", "lsxXuat" };
    public static readonly string[] PhieuItemHeaders = { "phieuId", "maHang", "tenHang", "phieuNhap", "soLo", "size", "soKien", "soLuongXuat", "itemKey", "rowIndex", "hopDong", "viTri" };
    public const string TabMeta = "Meta", TabPx = "PhieuXuat", TabPxi = "PhieuXuatItems";

    private readonly SettingsService _settings;
    private readonly HttpMessageHandler? _handler;
    public PhoneWebApp(SettingsService settings, HttpMessageHandler? handler = null) { _settings = settings; _handler = handler; }

    public string Url => _settings.Get(KeyUrl) is { Length: > 0 } u ? u : DefaultUrl;
    public bool CustomUrl => _settings.Get(KeyUrl) is { Length: > 0 } u && u != DefaultUrl;
    public string Token => Secret.Unprotect(_settings.Get(KeyToken));
    public bool Enabled => Url.Length > 0 && Token.Length > 0;

    public static string? CheckUrl(string u)
    {
        if (!Uri.TryCreate(u.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || !(uri.Host is "script.google.com" or "script.googleusercontent.com") || !uri.AbsolutePath.EndsWith("/exec"))
            return "Đường dẫn Web App phải có dạng https://script.google.com/macros/s/…/exec (giống ở app cũ: Cài Đặt › Điện Thoại).";
        return null;
    }
    public void Save(string url, string token)
    {
        if (url.Trim().Length == 0) url = DefaultUrl;
        if (CheckUrl(url) is { } err) throw new BusinessException(err);
        if (token.Trim().Length == 0) throw new BusinessException("Chưa nhập mã PC_TOKEN.");
        _settings.Set(KeyUrl, url.Trim()); _settings.Set(KeyToken, Secret.Protect(token.Trim()));
    }
    public void Clear() { _settings.Set(KeyUrl, ""); _settings.Set(KeyToken, ""); }

    public async Task<JsonObject> CallAsync(string action, JsonObject? payload = null, int timeoutSec = 120, CancellationToken ct = default)
    {
        if (Url.Length == 0) throw new BusinessException("Chưa nhập đường dẫn Web App (Vị Trí Bột › 📱 Điện thoại › Kết nối).");
        if (Token.Length == 0) throw new BusinessException("Chưa nhập mã PC_TOKEN.");
        var body = new JsonObject { ["token"] = Token, ["action"] = action };
        if (payload is not null) foreach (var (k, v) in payload) body[k] = v?.DeepClone();
        using var h = _handler is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) : new HttpClient(_handler, false);
        h.Timeout = TimeSpan.FromSeconds(timeoutSec);
        HttpResponseMessage res;
        try { res = await h.PostAsync(Url, new StringContent(body.ToJsonString(), Encoding.UTF8, new MediaTypeHeaderValue("text/plain")), ct); }
        catch (HttpRequestException e) { throw new BusinessException("Không kết nối được Web App (kiểm tra mạng / đường dẫn): " + e.Message); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new BusinessException($"Web App không phản hồi sau {timeoutSec} giây."); }
        using (res)
        {
            var text = await res.Content.ReadAsStringAsync(ct);
            JsonObject? o = null; try { o = JsonNode.Parse(text) as JsonObject; } catch (JsonException) { }
            if (o is null) throw new BusinessException($"Web App trả về trang lạ (HTTP {(int)res.StatusCode}). Đường dẫn phải kết thúc bằng /exec và quyền truy cập là \"Bất kỳ ai\".");
            if (o["ok"]?.GetValue<bool>() != true)
            {
                var msg = o["error"]?.ToString() ?? "Web App báo lỗi không rõ.";
                if (msg.Contains("Không được phép thao tác tab") || msg.Contains("Không có chức năng")) msg += " → Apps Script trên Google đang là bản cũ: dán Code.gs mới, Lưu, rồi Triển khai phiên bản mới.";
                throw new BusinessException(msg);
            }
            return o;
        }
    }

    public async Task<(string Title, List<string> Sheets)> PingAsync(CancellationToken ct = default)
    {
        var d = await CallAsync("pc.ping", null, 30, ct);
        return (d["title"]?.ToString() ?? "(không rõ tên)", (d["sheetNames"] as JsonArray ?? new()).Select(x => x?.ToString() ?? "").ToList());
    }

    /// <summary>Tạo các tab còn thiếu: tên tab → tiêu đề (null = không đặt tiêu đề).</summary>
    public Task EnsureTabsAsync(IDictionary<string, string[]?> tabs, CancellationToken ct = default)
    {
        var o = new JsonObject(); foreach (var (k, v) in tabs) o[k] = v is null ? null : new JsonArray(v.Select(x => (JsonNode)x).ToArray());
        return CallAsync("pc.ensureTabs", new JsonObject { ["tabs"] = o }, 120, ct);
    }

    /// <summary>GHI ĐÈ toàn bộ 1 tab (Web App ghi mới trước, xóa phần thừa sau).</summary>
    public Task WriteTableAsync(string name, IList<string> columns, IEnumerable<IDictionary<string, JsonNode?>> rows, CancellationToken ct = default)
    {
        var cols = columns.Count > 0 ? columns : new[] { "(không có dữ liệu)" };
        var arr = new JsonArray(rows.Select(r => (JsonNode)new JsonArray(cols.Select(c => r.TryGetValue(c, out var v) && v is not null ? v.DeepClone() : JsonValue.Create("")).ToArray())).ToArray());
        return CallAsync("pc.writeTable", new JsonObject { ["name"] = name, ["columns"] = new JsonArray(cols.Select(c => (JsonNode)c).ToArray()), ["rows"] = arr }, 300, ct);
    }

    public sealed record Table(List<string> Headers, List<Dictionary<string, JsonNode?>> Rows);
    /// <summary>Đọc 1 tab → dòng (mỗi dòng có "_rowNumber" = số dòng thật trên Sheet).</summary>
    public async Task<Table> ReadTableAsync(string name, CancellationToken ct = default)
    {
        var d = await CallAsync("pc.readTable", new JsonObject { ["name"] = name }, 120, ct);
        var headers = (d["headers"] as JsonArray ?? new()).Select(x => x?.ToString() ?? "").ToList();
        var nums = d["rowNumbers"] as JsonArray ?? new();
        var rows = (d["rows"] as JsonArray ?? new()).Select((r, i) =>
        {
            var o = new Dictionary<string, JsonNode?> { ["_rowNumber"] = nums.ElementAtOrDefault(i)?.DeepClone() };
            var a = r as JsonArray ?? new();
            for (var j = 0; j < headers.Count; j++) o[headers[j]] = j < a.Count && a[j] is not null ? a[j]!.DeepClone() : JsonValue.Create("");
            return o;
        }).ToList();
        return new Table(headers, rows);
    }

    /// <summary>Đánh dấu nhiều dòng đã xử lý trong 1 lệnh: [(số dòng, id, {cột: giá trị})].</summary>
    public Task MarkRowsAsync(string name, IEnumerable<(JsonNode? RowNumber, string Id, IDictionary<string, string> Values)> updates, CancellationToken ct = default)
    {
        var arr = new JsonArray(updates.Select(u => (JsonNode)new JsonObject { ["rowNumber"] = u.RowNumber?.DeepClone(), ["id"] = u.Id, ["values"] = new JsonObject(u.Values.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))) }).ToArray());
        if (arr.Count == 0) return Task.CompletedTask;
        return CallAsync("pc.markRows", new JsonObject { ["name"] = name, ["updates"] = arr }, 120, ct);
    }

    public static string Str(JsonNode? n) => n is null ? "" : n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n.ToJsonString().Trim('"');
    /// <summary>Số từ Sheet (giống syncToNumber: bỏ dấu phẩy nghìn, lỗi → 0).</summary>
    public static double Num(JsonNode? n)
    {
        if (n is JsonValue v && v.TryGetValue<double>(out var d)) return double.IsFinite(d) ? d : 0;
        var s = Str(n).Replace(",", "").Trim();
        var m = System.Text.RegularExpressions.Regex.Match(s, @"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?");
        return m.Success && double.TryParse(m.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : 0;
    }
    /// <summary>Ngày từ Sheet → ISO (số ngày Excel / chuỗi; trống → bây giờ) — giống sheetValueToIso.</summary>
    public static string Iso(JsonNode? n, DateTime nowUtc)
    {
        if (n is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d)) return DateTime.UnixEpoch.AddMilliseconds(Math.Round((d - 25569) * 86400 * 1000)).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        var s = Str(n).Trim(); return s.Length > 0 ? s : nowUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
    }
}
