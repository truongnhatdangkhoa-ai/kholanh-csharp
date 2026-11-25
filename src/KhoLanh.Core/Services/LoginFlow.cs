using Dapper;
using KhoLanh.Core.Data;
using KhoLanh.Core.Models;

namespace KhoLanh.Core.Services;

/// <summary>
/// Đăng nhập nhanh khi dùng chung tài khoản qua Google Sheet (1.59). Trước đây bấm Đăng nhập phải CHỜ tải danh sách từ Sheet (Apps Script
/// thường 3–8 giây) rồi mới kiểm mật khẩu. Nay:
///  1. Mở cửa sổ đăng nhập là bắt đầu tải ngầm (<see cref="AccountSyncService.PrefetchAsync"/>), song song kiểm tra cập nhật / gõ mật khẩu.
///  2. Bấm Đăng nhập: chờ thêm tối đa <c>wait</c> (≈1,5 giây); chưa xong thì kiểm mật khẩu bằng bản lưu trong máy và vào luôn.
///  3. Bản lưu chưa có / sai (tài khoản mới tạo, vừa đổi mật khẩu ở máy khác) → lúc đó mới chờ danh sách mới rồi kiểm lại.
///  4. Danh sách tải ngầm xong sau khi đã vào: quyền được cập nhật ngay; tài khoản bị khóa / đổi mật khẩu ở máy khác → <see cref="Recheck"/> báo để đăng xuất.
/// </summary>
public static class LoginFlow
{
    public static readonly TimeSpan DefaultWait = TimeSpan.FromMilliseconds(1500);

    /// <param name="Pending">Lượt tải danh sách còn đang chạy sau khi đã đăng nhập (null = đã xong / không dùng chung).</param>
    /// <param name="Note">Câu báo khi không lấy được danh sách mới (rỗng = ổn).</param>
    /// <param name="PasswordHash">Băm mật khẩu lúc đăng nhập, để <see cref="Recheck"/> nhận ra mật khẩu đổi ở máy khác.</param>
    public sealed record Result(User User, Task<int>? Pending, string Note, string PasswordHash);

    public static async Task<Result> LoginAsync(AuthService auth, AccountSyncService acc, string user, string pass, TimeSpan? wait = null)
    {
        Task<int>? pull = acc.Enabled ? acc.PrefetchAsync() : null;
        if (pull is { IsCompleted: false }) await Task.WhenAny(pull, Task.Delay(wait ?? DefaultWait));
        if (pull is { IsCompleted: false } && !auth.CheckPassword(user, pass))
        {
            try { await pull; } catch { /* báo ở Note, dùng bản lưu */ }
        }
        var note = pull is { IsCompleted: true } ? NoteOf(pull) : "";
        User u;
        try { u = auth.Login(user, pass); }
        catch (BusinessException ex) when (ex.Message.StartsWith("Sai tên đăng nhập") && user.Trim().Length > 0 && pass.Length > 0)
        {
            // 1.107: máy khác báo "sai mật khẩu" dù tài khoản đúng — vì máy này chưa nối Google Sheet chung, hoặc tài khoản chưa lên Sheet.
            // 1.108: máy đã nối nhưng giữ mã cũ ("Sai mã kết nối") cũng hỏi lại Sheet chung bằng tên + mật khẩu (không phụ thuộc mã lưu trong máy).
            var pullErr = pull is { IsFaulted: true } ? pull.Exception!.GetBaseException().Message.TrimEnd('.') : null;
            var missing = !auth.Exists(user);
            var wasOn = acc.Enabled;
            if (acc.Enabled && pullErr is null && !missing)
            {
                // 1.110: danh sách tải ngầm được dùng lại 2 phút — Quản trị vừa đổi mật khẩu / tạo tài khoản / đặt lại mã ở máy khác thì bản đó
                // đã cũ. Trước khi báo sai mật khẩu: tải lại danh sách mới nhất 1 lần; tải lỗi (vd mã cũ "Sai mã kết nối") → hỏi Sheet bằng mật khẩu.
                try { await acc.PullAsync(TimeSpan.FromSeconds(20)); }
                catch (BusinessException pe) { pullErr = pe.Message.TrimEnd('.'); }
                missing = !auth.Exists(user);
                if (pullErr is null && !missing)
                {
                    if (!auth.CheckPassword(user, pass)) throw;
                    u = auth.Login(user, pass);
                    return new Result(u, null, "", u.PasswordHash);
                }
            }
            var why = LocalWrong(auth, user) + (pullErr is null ? "" : $" Không lấy được danh sách mới từ Google Sheet chung ({pullErr}).");
            try { await acc.JoinByPasswordAsync(user, pass, allowExisting: true); }
            catch (JoinException je) when (je.Kind is JoinException.NoAccount or JoinException.WrongPassword or JoinException.Locked)
            {
                throw new BusinessException(je.Kind == JoinException.NoAccount ? AccountSyncService.NotOnSheetMsg(user.Trim()) : je.Message);
            }
            catch (BusinessException be) { throw new BusinessException(why + " Hỏi lại Google Sheet chung cũng chưa được: " + be.Message); }
            u = auth.Login(user, pass);
            return new Result(u, null, !wasOn ? "Máy này đã tự kết nối Google Sheet chung — dùng chung tài khoản với các máy khác."
                : "Máy này đã kết nối lại Google Sheet chung (mã cũ trong máy đã thay).", u.PasswordHash);
        }
        return new Result(u, pull is { IsCompleted: false } ? pull : null, note, u.PasswordHash);
    }

    private static string LocalWrong(AuthService auth, string user) =>
        auth.Exists(user) ? "Sai mật khẩu trên máy này." : $"Máy này chưa có tài khoản \"{user.Trim()}\".";

    /// <summary>Câu báo lỗi tải danh sách (rỗng nếu tải được).</summary>
    public static string NoteOf(Task t)
    {
        if (!t.IsFaulted) return "";
        var e = t.Exception!.GetBaseException();
        return "Không lấy được danh sách mới (" + e.Message.TrimEnd('.') + ") — dùng bản lưu trong máy.";
    }

    /// <summary>
    /// Sau khi danh sách tải ngầm xong: tài khoản đang đăng nhập còn dùng được không. Trả lý do phải đăng xuất (bị khóa / xóa,
    /// mật khẩu đã đổi ở máy khác), hoặc null nếu vẫn ổn.
    /// </summary>
    public static string? Recheck(Database db, User u, string passwordHash)
    {
        using var c = db.Open();
        var now = c.QuerySingleOrDefault<User>("SELECT * FROM users WHERE id=@Id", u);
        if (now is null || !now.IsActive) return "Tài khoản " + u.Username + " vừa bị Quản trị khóa trên danh sách dùng chung.";
        if (now.PasswordHash != passwordHash) return "Mật khẩu của tài khoản " + u.Username + " đã được đổi ở máy khác — đăng nhập lại bằng mật khẩu mới.";
        return null;
    }
}
