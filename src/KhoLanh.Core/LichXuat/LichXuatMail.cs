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
    public async Task<List<LichXuatEmail.Msg>> FetchAsync(MailConfig cfg, CancellationToken ct = default)
    {
        if (!cfg.Ready) throw new BusinessException("Chưa khai báo tài khoản Gmail (địa chỉ + mật khẩu ứng dụng).");
        var pass = Secret.Unprotect(cfg.PassEnc);
        using var c = new ImapClient { Timeout = 60_000 };
        try
        {
            await c.ConnectAsync(cfg.Host, cfg.Port, SecureSocketOptions.SslOnConnect, ct);
            await c.AuthenticateAsync(cfg.Email, pass, ct);
        }
        catch (AuthenticationException) { throw new BusinessException("Gmail từ chối đăng nhập — kiểm tra địa chỉ và MẬT KHẨU ỨNG DỤNG (không phải mật khẩu Gmail), IMAP đã bật."); }
        catch (Exception e) when (e is System.Net.Sockets.SocketException or IOException or TimeoutException) { throw new BusinessException("Không kết nối được " + cfg.Host + ": " + e.Message); }

        IMailFolder folder;
        try { folder = await c.GetFolderAsync(cfg.Folder, ct); }
        catch { folder = c.Inbox; }
        await folder.OpenAsync(FolderAccess.ReadOnly, ct);
        var uids = await folder.SearchAsync(SearchQuery.DeliveredAfter(DateTime.Today.AddDays(-Math.Max(cfg.Days, 1) - 30)), ct);
        var res = new List<LichXuatEmail.Msg>();
        if (uids.Count == 0) { await c.DisconnectAsync(true, ct); return res; }
        var items = MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate;
        if (c.Capabilities.HasFlag(ImapCapabilities.GMailExt1)) items |= MessageSummaryItems.GMailThreadId | MessageSummaryItems.GMailMessageId;
        var sums = await folder.FetchAsync(uids, items, ct);
        // chỉ tải nội dung của thư thuộc chuỗi có LSX trên tiêu đề (đỡ tải cả hộp thư)
        var keepThreads = sums.Where(s => System.Text.RegularExpressions.Regex.IsMatch(s.Envelope?.Subject ?? "", @"\d{2}\.\d{2}\.\d{2,3}"))
            .Select(s => ThreadOf(s)).ToHashSet();
        foreach (var s in sums)
        {
            ct.ThrowIfCancellationRequested();
            if (!keepThreads.Contains(ThreadOf(s))) continue;
            var msg = await folder.GetMessageAsync(s.UniqueId, ct);
            var text = msg.TextBody ?? (msg.HtmlBody is { } h ? System.Text.RegularExpressions.Regex.Replace(System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(h, "<(br|/p|/div)[^>]*>", "\n", System.Text.RegularExpressions.RegexOptions.IgnoreCase)), "<[^>]+>", "") : "");
            var from = msg.From.Mailboxes.FirstOrDefault() is { } mb ? (mb.Name.Length > 0 ? mb.Name + " <" + mb.Address + ">" : mb.Address) : "";
            var id = s.GMailMessageId is { } gm ? gm.ToString() : (msg.MessageId ?? s.UniqueId.ToString());
            res.Add(new LichXuatEmail.Msg(id, ThreadOf(s), (s.InternalDate ?? msg.Date).LocalDateTime, from, msg.Subject ?? "", text));
        }
        await c.DisconnectAsync(true, ct);
        return res;
    }

    /// <summary>Gmail có mã chuỗi thư; máy chủ khác thì gom theo tiêu đề đã bỏ "Re:/Fw:".</summary>
    private static string ThreadOf(IMessageSummary s) => s.GMailThreadId is { } t ? t.ToString()
        : "s:" + System.Text.RegularExpressions.Regex.Replace(s.Envelope?.Subject ?? "", @"^\s*((re|fw|fwd|tr)\s*:\s*)+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim().ToLowerInvariant();
}
