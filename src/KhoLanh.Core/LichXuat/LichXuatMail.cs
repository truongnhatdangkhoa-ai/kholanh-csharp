using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.LichXuat;

/// <summary>
/// Tài khoản Gmail để đọc email lịch xuất (IMAP imap.gmail.com:993, "mật khẩu ứng dụng" — bật xác thực 2 lớp rồi tạo trên Google).
/// Mật khẩu lưu mã hóa (Secret / DPAPI) trong settings của máy; giao diện không bao giờ nhận lại mật khẩu.
/// CHỈ ĐỌC hộp thư (không gửi, không xóa, không gắn nhãn) — trạng thái duyệt lưu trong database lichxuat (sheet LX_EmailXuLy).
/// </summary>
public sealed class MailConfig
{
    public string Email = "", PassEnc = "", Host = "imap.gmail.com";
    public int Port = 993, Days = 7, IntervalMin = 10;
    public string Keywords = "", Allow = "", Folder = "[Gmail]/All Mail";
    public bool Auto = true;

    public const string K = "lx_mail_";
    public bool Ready => Email.Length > 0 && PassEnc.Length > 0;

    public static MailConfig Load(SettingsService s) => new()
    {
        Email = s.Get(K + "email"), PassEnc = s.Get(K + "pass"), Host = s.Get(K + "host", "imap.gmail.com"),
        Port = int.TryParse(s.Get(K + "port"), out var p) && p > 0 ? p : 993, Days = int.TryParse(s.Get(K + "days"), out var d) && d > 0 ? d : 7,
        IntervalMin = int.TryParse(s.Get(K + "every"), out var e) && e >= 2 ? e : 10, Keywords = s.Get(K + "kw"), Allow = s.Get(K + "allow"),
        Folder = s.Get(K + "folder", "[Gmail]/All Mail"), Auto = s.Get(K + "auto", "1") == "1",
    };

    public void Save(SettingsService s)
    {
        s.Set(K + "email", Email.Trim()); s.Set(K + "pass", PassEnc); s.Set(K + "host", Host.Trim()); s.Set(K + "port", Port.ToString());
        s.Set(K + "days", Days.ToString()); s.Set(K + "every", IntervalMin.ToString()); s.Set(K + "kw", Keywords); s.Set(K + "allow", Allow);
        s.Set(K + "folder", Folder.Trim()); s.Set(K + "auto", Auto ? "1" : "0");
    }

    public List<string> KeywordList => Keywords.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
    public List<string> AllowList => Allow.Split(new[] { ',', ';', '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
}

/// <summary>Nguồn thư (tách ra để test không cần mạng).</summary>
public interface IMailSource
{
    Task<List<LichXuatEmail.Msg>> FetchAsync(MailConfig cfg, CancellationToken ct = default);
}

/// <summary>Đọc thư Gmail qua IMAP (chỉ đọc, mở hộp thư ở chế độ ReadOnly).</summary>
public sealed class ImapMailSource : IMailSource
{
    /// <summary>Báo tiến độ (đang kết nối / tìm thư / tải k/n) — giao diện hiện ở dòng trạng thái (1.77).</summary>
    public Action<string>? Progress { get; set; }
    /// <summary>Số liệu lần đọc gần nhất: thư mục, số thư tìm thấy, số thư thuộc chuỗi có LSX trên tiêu đề (đã tải nội dung).</summary>
    public (string Folder, int Found, int Downloaded) LastStats { get; private set; }

    private async Task<ImapClient> ConnectAsync(MailConfig cfg, CancellationToken ct)
    {
        if (!cfg.Ready) throw new BusinessException("Chưa khai báo tài khoản Gmail (địa chỉ + mật khẩu ứng dụng).");
        var pass = Secret.Unprotect(cfg.PassEnc);
        var c = new ImapClient { Timeout = 60_000 };
        try
        {
            Progress?.Invoke("Đang kết nối " + cfg.Host + "…");
            await c.ConnectAsync(cfg.Host, cfg.Port, SecureSocketOptions.SslOnConnect, ct);
            Progress?.Invoke("Đang đăng nhập " + cfg.Email + "…");
            await c.AuthenticateAsync(cfg.Email, pass, ct);
            return c;
        }
        catch (AuthenticationException) { c.Dispose(); throw new BusinessException("Gmail từ chối đăng nhập — kiểm tra địa chỉ và MẬT KHẨU ỨNG DỤNG 16 ký tự (không phải mật khẩu Gmail), Xác minh 2 bước đã bật."); }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or TimeoutException) { c.Dispose(); throw new BusinessException("Không kết nối được " + cfg.Host + ": " + e.Message); }
        catch { c.Dispose(); throw; }
    }

    /// <summary>
    /// Thư mục cần đọc. "[Gmail]/All Mail" mang tên khác khi Gmail để tiếng Việt ("[Gmail]/Tất cả thư") → tìm theo công dụng
    /// đặc biệt "All" (SPECIAL-USE); không có thì Hộp thư đến. Trả kèm ghi chú khi phải đổi.
    /// </summary>
    private static async Task<(IMailFolder F, string Note)> FolderAsync(ImapClient c, MailConfig cfg, CancellationToken ct)
    {
        try { return (await c.GetFolderAsync(cfg.Folder, ct), ""); }
        catch (Exception) when (!ct.IsCancellationRequested) { }
        if ((c.Capabilities & (ImapCapabilities.SpecialUse | ImapCapabilities.XList)) != 0)
        {
            try { var all = c.GetFolder(SpecialFolder.All); if (all is not null) return (all, $"không có thư mục «{cfg.Folder}» → dùng «{all.FullName}» (Tất cả thư)"); } catch { }
        }
        return (c.Inbox, $"không có thư mục «{cfg.Folder}» → đọc Hộp thư đến");
    }

    /// <summary>Tìm thư theo ngày, và theo người gửi NGAY TRÊN MÁY CHỦ nếu có ô "Chỉ nhận thư từ" (đỡ tải cả hộp thư).</summary>
    private static SearchQuery Query(MailConfig cfg, int extraDays)
    {
        SearchQuery q = SearchQuery.DeliveredAfter(DateTime.Today.AddDays(-Math.Max(cfg.Days, 1) - extraDays));
        var allow = cfg.AllowList;
        if (allow.Count > 0)
        {
            SearchQuery from = SearchQuery.FromContains(allow[0]);
            foreach (var a in allow.Skip(1)) from = from.Or(SearchQuery.FromContains(a));
            q = q.And(from);
        }
        return q;
    }

    /// <summary>Kiểm tra kết nối (nút "Kiểm tra kết nối"): đăng nhập, mở thư mục, đếm thư — không tải nội dung, không ghi gì.</summary>
    public async Task<string> CheckAsync(MailConfig cfg, CancellationToken ct = default)
    {
        using var c = await ConnectAsync(cfg, ct);
        var (folder, note) = await FolderAsync(c, cfg, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        Progress?.Invoke("Đang đếm thư…");
        var all = await folder.SearchAsync(SearchQuery.DeliveredAfter(DateTime.Today.AddDays(-Math.Max(cfg.Days, 1))), ct);
        var mine = cfg.AllowList.Count > 0 ? (await folder.SearchAsync(Query(cfg, 0), ct)).Count : all.Count;
        await c.DisconnectAsync(true, ct);
        return $"✓ Đã kết nối {cfg.Email} · thư mục «{folder.FullName}»" + (note.Length > 0 ? $" ({note})" : "") +
               $" · {all.Count} thư trong {Math.Max(cfg.Days, 1)} ngày" + (cfg.AllowList.Count > 0 ? $", trong đó {mine} thư từ người ở ô «Chỉ nhận thư từ»" : "") + ".";
    }

    public async Task<List<LichXuatEmail.Msg>> FetchAsync(MailConfig cfg, CancellationToken ct = default)
    {
        using var c = await ConnectAsync(cfg, ct);
        var (folder, _) = await FolderAsync(c, cfg, ct);
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        Progress?.Invoke("Đang tìm thư trong «" + folder.FullName + "»…");
        // thêm 30 ngày để lấy thư gốc của chuỗi (nhận diện LSX / ngày ETD từ thư đầu)
        var uids = await folder.SearchAsync(Query(cfg, 30), ct);
        var res = new List<LichXuatEmail.Msg>();
        LastStats = (folder.FullName, uids.Count, 0);
        if (uids.Count == 0) { await c.DisconnectAsync(true, ct); return res; }
        var items = MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate;
        if (c.Capabilities.HasFlag(ImapCapabilities.GMailExt1)) items |= MessageSummaryItems.GMailThreadId | MessageSummaryItems.GMailMessageId;
        Progress?.Invoke($"Đang đọc tiêu đề {uids.Count} thư…");
        var sums = await folder.FetchAsync(uids, items, ct);
        // chỉ tải nội dung của thư thuộc chuỗi có LSX trên tiêu đề (đỡ tải cả hộp thư)
        var keepThreads = sums.Where(s => System.Text.RegularExpressions.Regex.IsMatch(s.Envelope?.Subject ?? "", @"\d{2}\.\d{2}\.\d{2,3}"))
            .Select(s => ThreadOf(s)).ToHashSet();
        var want = sums.Where(s => keepThreads.Contains(ThreadOf(s))).ToList();
        // 1.121 (người dùng: "vì sao app tải toàn bộ thư" — 813 thư mỗi lượt, quá 3 phút): thư đã đọc ở lượt trước lấy từ bộ nhớ đệm trên máy
        // (MailCache, theo mã thư Gmail), chỉ tải thư MỚI; và chỉ tải PHẦN CHỮ của thư (không tải file đính kèm PDF / ảnh).
        var cache = MailCache.Load(CachePath);
        var fresh = want.Where(s => !cache.ContainsKey(KeyOf(s, folder))).ToList();
        if (fresh.Count > 0 && !items.HasFlag(MessageSummaryItems.BodyStructure))
        {
            Progress?.Invoke($"Đang đọc cấu trúc {fresh.Count} thư mới…");
            var withBody = await folder.FetchAsync(fresh.Select(s => s.UniqueId).ToList(), items | MessageSummaryItems.BodyStructure, ct);
            fresh = withBody.ToList();
        }
        var k = 0;
        foreach (var s in fresh)
        {
            ct.ThrowIfCancellationRequested();
            Progress?.Invoke($"Đang tải thư mới {++k}/{fresh.Count} (bỏ qua {want.Count - fresh.Count} thư đã đọc)…");
            cache[KeyOf(s, folder)] = await ReadAsync(folder, s, ct);
        }
        foreach (var s in want)
            if (cache.TryGetValue(KeyOf(s, folder), out var m)) res.Add(m with { ThreadId = ThreadOf(s) });
        MailCache.Save(CachePath, cache, DateTime.Today.AddDays(-Math.Max(cfg.Days, 1) - 45));
        LastStats = (folder.FullName, uids.Count, want.Count);
        LastFresh = fresh.Count;
        await c.DisconnectAsync(true, ct);
        return res;
    }

    /// <summary>1.121: file đệm thư đã đọc (null = không đệm). AppServices đặt cạnh database.</summary>
    public string? CachePath { get; set; }
    /// <summary>Số thư phải tải mới ở lượt đọc gần nhất (còn lại lấy từ bộ nhớ đệm).</summary>
    public int LastFresh { get; private set; }

    private static string KeyOf(IMessageSummary s, IMailFolder f) => s.GMailMessageId is { } gm ? "g" + gm : $"u{f.UidValidity}:{s.UniqueId.Id}";

    /// <summary>Chỉ tải phần chữ (text/plain, không có thì html) — không tải file đính kèm. Thiếu cấu trúc thư thì tải cả thư như cũ.</summary>
    private static async Task<LichXuatEmail.Msg> ReadAsync(IMailFolder folder, IMessageSummary s, CancellationToken ct)
    {
        string text = "", subject = s.Envelope?.Subject ?? "", from = "";
        if (s.Envelope?.From?.Mailboxes.FirstOrDefault() is { } mb0) from = mb0.Name is { Length: > 0 } ? mb0.Name + " <" + mb0.Address + ">" : mb0.Address;
        var date = (s.InternalDate ?? s.Envelope?.Date ?? DateTimeOffset.Now).LocalDateTime;
        var part = (BodyPart?)s.TextBody ?? s.HtmlBody;
        if (part is not null)
        {
            var ent = await folder.GetBodyPartAsync(s.UniqueId, part, ct);
            var raw = ent is MimeKit.TextPart tp ? tp.Text ?? "" : "";
            text = part == s.TextBody ? raw : HtmlToText(raw);
        }
        else
        {
            var msg = await folder.GetMessageAsync(s.UniqueId, ct);
            text = msg.TextBody ?? (msg.HtmlBody is { } h ? HtmlToText(h) : "");
            if (subject.Length == 0) subject = msg.Subject ?? "";
            if (from.Length == 0 && msg.From.Mailboxes.FirstOrDefault() is { } mb) from = mb.Name.Length > 0 ? mb.Name + " <" + mb.Address + ">" : mb.Address;
        }
        var id = s.GMailMessageId is { } gm ? gm.ToString() : (s.Envelope?.MessageId ?? s.UniqueId.ToString());
        return new LichXuatEmail.Msg(id, ThreadOf(s), date, from, subject, text);
    }

    private static string HtmlToText(string h) => System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlDecode(
        System.Text.RegularExpressions.Regex.Replace(h, "<(br|/p|/div)[^>]*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase)), "<[^>]+>", "");

    /// <summary>Gmail có mã chuỗi thư; máy chủ khác thì gom theo tiêu đề đã bỏ "Re:/Fw:".</summary>
    private static string ThreadOf(IMessageSummary s) => s.GMailThreadId is { } t ? t.ToString()
        : "s:" + System.Text.RegularExpressions.Regex.Replace(s.Envelope?.Subject ?? "", @"^\s*((re|fw|fwd|tr)\s*:\s*)+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim().ToLowerInvariant();
}

/// <summary>
/// 1.121: bộ nhớ đệm thư lịch xuất đã đọc (file JSON trên máy, theo mã thư): lượt quét sau chỉ tải thư mới.
/// Thư cũ hơn mốc giữ (số ngày quét + 45 ngày) tự bỏ. File hỏng / không đọc được → coi như trống.
/// </summary>
public static class MailCache
{
    private sealed record Row(string K, string Id, string T, DateTime D, string F, string S, string X);

    public static Dictionary<string, LichXuatEmail.Msg> Load(string? path)
    {
        var d = new Dictionary<string, LichXuatEmail.Msg>();
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return d;
        try
        {
            foreach (var r in System.Text.Json.JsonSerializer.Deserialize<List<Row>>(File.ReadAllText(path)) ?? new())
                d[r.K] = new LichXuatEmail.Msg(r.Id, r.T, r.D, r.F, r.S, r.X);
        }
        catch { d.Clear(); }
        return d;
    }

    public static void Save(string? path, Dictionary<string, LichXuatEmail.Msg> cache, DateTime keepFrom)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var rows = cache.Where(p => p.Value.Date >= keepFrom).Select(p => new Row(p.Key, p.Value.Id, p.Value.ThreadId, p.Value.Date, p.Value.From, p.Value.Subject, p.Value.Text)).ToList();
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, System.Text.Json.JsonSerializer.Serialize(rows));
            File.Move(tmp, path, true);
        }
        catch { /* không ghi được đệm: lượt sau tải lại, không sao */ }
    }
}
