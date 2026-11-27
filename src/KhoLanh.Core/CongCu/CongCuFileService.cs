using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using KhoLanh.Core.LuuTru;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.CongCu;

/// <summary>Một file / thư mục mà module Công cụ (Scan, Container 3D, Xử lý PDF &amp; Excel, So sánh bảng) đã xuất ra.</summary>
public sealed class CcFile
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("tool")] public string Tool { get; set; } = "";
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("dir")] public bool IsDir { get; set; }
    [JsonPropertyName("at")] public DateTime At { get; set; }
    [JsonPropertyName("bytes")] public long Bytes { get; set; }
    /// <summary>"" = không đồng bộ · "ok" = đã vào Lưu trữ cá nhân (và lên Drive nếu đã đăng nhập Google) · "local" = vào Lưu trữ cá nhân trên máy, chưa lên Drive · "err" = lỗi.</summary>
    [JsonPropertyName("sync")] public string Sync { get; set; } = "";
    [JsonPropertyName("note")] public string Note { get; set; } = "";

    [JsonIgnore] public string Name => string.IsNullOrEmpty(Path) ? "" : (System.IO.Path.GetFileName(Path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : Path);
    [JsonIgnore] public bool Exists => IsDir ? Directory.Exists(Path) : File.Exists(Path);
}

/// <summary>
/// 1.154: danh sách file module Công cụ đã xuất (theo từng tài khoản, trên máy này) + ô tích "Tự lưu vào Lưu trữ cá nhân".
/// File vẫn nằm đúng chỗ người dùng chọn ở hộp "Lưu thành…" — danh sách chỉ NHỚ đường dẫn để mở lại; khi tích đồng bộ thì chép thêm
/// 1 bản vào ngăn "Công cụ" của Lưu trữ cá nhân (và đẩy lên Google Drive nếu tài khoản đã đăng nhập Google).
/// </summary>
public sealed class CongCuFileService
{
    public const string NganName = "Công cụ";
    public const int Keep = 300;
    private readonly SettingsService _set;
    private readonly Session _ses;
    private readonly LuuTruService _lt;
    private readonly Func<DateTime> _now;
    private static readonly object Gate = new();

    public CongCuFileService(SettingsService set, Session ses, LuuTruService lt, Func<DateTime>? now = null)
    { _set = set; _ses = ses; _lt = lt; _now = now ?? (() => DateTime.Now); }

    private string ListKey => "cc_daluu:" + _ses.Username;
    private string SyncKey => "cc_dongbo:" + _ses.Username;

    /// <summary>Ô tích "Tự lưu vào Lưu trữ cá nhân" — nhớ theo tài khoản, dùng chung cho mọi module công cụ.</summary>
    public bool SyncOn => _ses.CurrentUser is not null && _set.Get(SyncKey, "0") == "1";
    public void SetSync(bool on) { if (_ses.CurrentUser is not null) _set.Set(SyncKey, on ? "1" : "0"); }

    public List<CcFile> List(string? tool = null)
    {
        if (_ses.CurrentUser is null) return new();
        lock (Gate)
        {
            var all = Load();
            return (tool is null ? all : all.Where(f => f.Tool == tool).ToList()).OrderByDescending(f => f.At).ToList();
        }
    }

    private List<CcFile> Load()
    {
        try { return JsonSerializer.Deserialize<List<CcFile>>(_set.Get(ListKey, "[]")) ?? new(); } catch { return new(); }
    }

    private void SaveAll(List<CcFile> l) =>
        _set.Set(ListKey, JsonSerializer.Serialize(l.OrderByDescending(f => f.At).Take(Keep).ToList()));

    /// <summary>Ghi nhớ 1 file / thư mục vừa xuất. Xuất lại cùng đường dẫn → cập nhật dòng cũ.</summary>
    public CcFile Record(string tool, string path)
    {
        _ses.RequireLogin();
        var dir = Directory.Exists(path);
        long bytes = 0;
        try { bytes = dir ? new DirectoryInfo(path).GetFiles().Sum(f => f.Length) : new FileInfo(path).Length; } catch { }
        lock (Gate)
        {
            var all = Load();
            var e = all.FirstOrDefault(f => f.Tool == tool && string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
            if (e is null) { e = new CcFile { Id = "c" + _now().ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture), Tool = tool, Path = path, IsDir = dir }; all.Add(e); }
            e.At = _now(); e.Bytes = bytes; e.Sync = ""; e.Note = "";
            SaveAll(all);
            return e;
        }
    }

    private void Update(string id, string sync, string note)
    {
        lock (Gate)
        {
            var all = Load();
            if (all.FirstOrDefault(f => f.Id == id) is { } e) { e.Sync = sync; e.Note = note; SaveAll(all); }
        }
    }

    public void Remove(string id, bool deleteFile)
    {
        lock (Gate)
        {
            var all = Load();
            if (all.FirstOrDefault(f => f.Id == id) is not { } e) return;
            if (deleteFile)
            {
                try { if (e.IsDir) { if (Directory.Exists(e.Path)) Directory.Delete(e.Path, true); } else if (File.Exists(e.Path)) File.Delete(e.Path); }
                catch (Exception ex) { throw new BusinessException("Không xóa được " + e.Name + ": " + ex.Message); }
            }
            all.Remove(e); SaveAll(all);
        }
    }

    /// <summary>Chép vào Lưu trữ cá nhân (ngăn "Công cụ") rồi đẩy lên Drive nếu đã đăng nhập Google. Không ném lỗi: kết quả ghi vào dòng + trả về.</summary>
    public async Task<CcFile> SyncAsync(string id, CancellationToken ct = default)
    {
        var e = List().FirstOrDefault(f => f.Id == id) ?? throw new BusinessException("Không thấy mục này trong danh sách.");
        string sync, note;
        try
        {
            (sync, note) = await Task.Run(() => DoSync(e), ct);
        }
        catch (BusinessException b) { sync = "err"; note = b.Message; }
        catch (Exception ex) { sync = "err"; note = "Không lưu được vào Lưu trữ cá nhân: " + ex.Message; }
        Update(e.Id, sync, note);
        e.Sync = sync; e.Note = note;
        return e;
    }

    private (string, string) DoSync(CcFile e)
    {
        if (!_lt.CanUse) throw new BusinessException("Tài khoản này chưa có quyền “Lưu trữ cá nhân” — nhờ Quản trị cấp quyền rồi thử lại.");
        if (!e.Exists) throw new BusinessException("File đã bị xóa / di chuyển khỏi " + e.Path + " nên không lưu được.");
        var store = _lt.Store;
        var ngan = store.Ngans().FirstOrDefault(n => n.Name == NganName)?.Id ?? store.AddNgan(NganName).Id;
        var files = e.IsDir ? Directory.GetFiles(e.Path).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Take(100).ToArray() : new[] { e.Path };
        if (files.Length == 0) throw new BusinessException("Thư mục trống — không có gì để lưu.");
        foreach (var f in files) store.AddFile(ngan, f, e.Tool + " · " + _now().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture));
        var n = files.Length > 1 ? $"{files.Length} file" : "file";
        if (!_lt.Config.Linked)
            return ("local", $"Đã lưu {n} vào Lưu trữ cá nhân (ngăn “{NganName}”) trên máy này. Chưa đăng nhập Google nên chưa lên Drive — đăng nhập ở Hệ thống › Kết nối.");
        try { _lt.BackupAsync(auto: true).GetAwaiter().GetResult(); }
        catch (BusinessException b) when (b.Message.StartsWith("Đang sao lưu")) { return ("local", $"Đã lưu {n} vào Lưu trữ cá nhân; Drive đang bận, sẽ tự đẩy lên ở lượt sau."); }
        catch (Exception ex) { return ("local", $"Đã lưu {n} vào Lưu trữ cá nhân nhưng chưa đẩy lên Drive: " + (ex is BusinessException ? ex.Message : ex.Message)); }
        return ("ok", $"Đã lưu {n} vào Lưu trữ cá nhân (ngăn “{NganName}”) và đồng bộ lên Google Drive.");
    }
}
