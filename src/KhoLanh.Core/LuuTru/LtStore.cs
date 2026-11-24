using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.LuuTru;

/// <summary>
/// 1 bản ghi của Lưu trữ cá nhân — CÙNG ĐỊNH DẠNG data.json của app cũ (core/luu-tru.js, M19) để nạp thẳng dữ liệu cũ:
/// k = "n" (ngăn / thư mục) hoặc "m" (mục: t = text / image / file), dl = 0 đang dùng · 1 thùng rác · 2 đã xóa hẳn.
/// Thêm mới so với app cũ: <c>ti</c> (tiêu đề mục).
/// </summary>
public sealed class LtRec
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("k")] public string K { get; set; } = "m";
    [JsonPropertyName("ng")] public string Ng { get; set; } = "";
    [JsonPropertyName("t")] public string T { get; set; } = "";
    [JsonPropertyName("ti")] public string Ti { get; set; } = "";
    [JsonPropertyName("tx")] public string Tx { get; set; } = "";
    [JsonPropertyName("fn")] public string Fn { get; set; } = "";
    [JsonPropertyName("fs")] public long Fs { get; set; }
    [JsonPropertyName("mt")] public string Mt { get; set; } = "";
    [JsonPropertyName("th")] public string Th { get; set; } = "";
    [JsonPropertyName("fid")] public string Fid { get; set; } = "";
    [JsonPropertyName("bg")] public int Bg { get; set; }
    [JsonPropertyName("ts")] public long Ts { get; set; }
    [JsonPropertyName("up")] public long Up { get; set; }
    [JsonPropertyName("dl")] public int Dl { get; set; }
    [JsonPropertyName("pn")] public int Pn { get; set; }
    [JsonPropertyName("tg")] public List<string> Tg { get; set; } = new();
    [JsonPropertyName("src")] public string Src { get; set; } = "pc";
    [JsonPropertyName("sq")] public long Sq { get; set; }
    [JsonPropertyName("pd")] public int Pd { get; set; }
    [JsonPropertyName("lf")] public string Lf { get; set; } = "";

    [JsonIgnore] public bool IsNgan => K == "n";
    [JsonIgnore] public bool IsFile => K == "m" && T is "image" or "file";
    [JsonIgnore] public bool IsLink => K == "m" && T == "text" && LtStore.UrlRe.IsMatch(Tx);
    [JsonIgnore] public string Kind => K == "n" ? "ngan" : T == "image" ? "image" : T == "file" ? "file" : IsLink ? "link" : "note";

    /// <summary>Tiêu đề hiển thị: tiêu đề riêng → tên tệp → dòng đầu nội dung.</summary>
    [JsonIgnore]
    public string Title
    {
        get
        {
            if (Ti.Trim().Length > 0) return Ti.Trim();
            if (IsFile && Fn.Length > 0) return Fn;
            var first = Tx.Replace("\r", "").Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0) ?? "";
            return first.Length > 120 ? first[..120] + "…" : first;
        }
    }
}

public sealed class LtData
{
    [JsonPropertyName("v")] public int V { get; set; } = 1;
    [JsonPropertyName("seq")] public long Seq { get; set; }
    [JsonPropertyName("recs")] public Dictionary<string, LtRec> Recs { get; set; } = new();
}

/// <summary>Ngăn (thư mục) kèm số mục và dung lượng.</summary>
public sealed record LtNgan(string Id, string Name, int Count, long Bytes);

/// <summary>Số liệu tổng của kho (thẻ số đầu trang).</summary>
public sealed record LtStats(int Items, int Notes, int Links, int Files, int Images, long Bytes, int Trash, int Pinned, int NotBackedUp);

/// <summary>
/// Kho Lưu trữ cá nhân của 1 tài khoản trên máy: <c>&lt;thư mục dữ liệu&gt;/luu-tru/&lt;tài khoản&gt;/</c> gồm data.json (danh sách mục)
/// + files/YYYY-MM/&lt;mã&gt;__&lt;tên tệp&gt; (tệp / ảnh). Riêng từng tài khoản, KHÔNG qua LAN. Ghi data.json qua file tạm rồi đổi tên.
/// </summary>
public sealed class LtStore
{
    public const string DefaultNgan = "mydocs";
    public const long MaxFile = 500L * 1048576;
    public static readonly Regex UrlRe = new(@"\bhttps?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IdRe = new("^[A-Za-z0-9_-]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex ImgRe = new(@"^image/(png|jpe?g|gif|webp|bmp)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    internal static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = "image/png", ["jpg"] = "image/jpeg", ["jpeg"] = "image/jpeg", ["gif"] = "image/gif", ["webp"] = "image/webp", ["bmp"] = "image/bmp",
        ["pdf"] = "application/pdf", ["doc"] = "application/msword", ["docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ["xls"] = "application/vnd.ms-excel", ["xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ["ppt"] = "application/vnd.ms-powerpoint", ["pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ["txt"] = "text/plain", ["csv"] = "text/csv", ["zip"] = "application/zip", ["mp4"] = "video/mp4", ["mp3"] = "audio/mpeg", ["json"] = "application/json",
    };

    public string Dir { get; }
    public string Account { get; }
    public string FilesDir => Path.Combine(Dir, "files");
    public string DataFile => Path.Combine(Dir, "data.json");
    public LtData D { get; private set; } = new();
    /// <summary>Đồng hồ (ms kiểu JS) — test đặt được.</summary>
    public Func<long> Now { get; set; } = () => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    /// <summary>Báo có thay đổi (giao diện vẽ lại, lịch sao lưu "khi có thay đổi").</summary>
    public event Action? Changed;

    public LtStore(string root, string user)
    {
        Account = AccountOf(user);
        if (Account.Length == 0) throw new BusinessException("Chưa đăng nhập.");
        Dir = Path.Combine(root, Account);
        Directory.CreateDirectory(FilesDir);
        Load();
    }

    /// <summary>Tên thư mục của tài khoản — giống app cũ (tkOf): chữ thường, ký tự lạ → "_", tối đa 40.</summary>
    public static string AccountOf(string user)
    {
        var s = (user ?? "").Trim().ToLowerInvariant();
        var sb = new StringBuilder();
        foreach (var ch in s) sb.Append(ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '_' or '-' ? ch : '_');
        var r = sb.ToString();
        return r.Length > 40 ? r[..40] : r;
    }

    /// <summary>Mã mới kiểu app cũ: thời điểm (ms, cơ số 36) + 10 ký tự hex ngẫu nhiên.</summary>
    public static string NewId()
    {
        const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
        var t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sb = new StringBuilder();
        do { sb.Insert(0, digits[(int)(t % 36)]); t /= 36; } while (t > 0);
        return sb + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
    }

    public static string SafeName(string s)
    {
        var t = Regex.Replace(s ?? "", @"[\\/:*?""<>|\x00-\x1f]", "_");
        t = Regex.Replace(t, @"\s+", " ").Trim();
        if (t.Length > 120) t = t[..120];
        return t.Length == 0 ? "file" : t;
    }

    public static string MimeOf(string name)
    {
        var ext = Path.GetExtension(name).TrimStart('.');
        return Mime.TryGetValue(ext, out var m) ? m : "application/octet-stream";
    }

    public static string NoAccent(string s)
    {
        var n = (s ?? "").Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(n.Length);
        foreach (var ch in n) if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().ToLowerInvariant();
    }

    // ------------------------------------------------------------------ đọc / ghi

    public void Load()
    {
        try
        {
            if (File.Exists(DataFile))
                D = JsonSerializer.Deserialize<LtData>(File.ReadAllText(DataFile), Json) ?? new LtData();
        }
        catch
        {
            try { File.Copy(DataFile, DataFile + ".loi-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); } catch { /* bỏ qua */ }
            D = new LtData();
        }
        D.Recs ??= new();
        foreach (var r in D.Recs.Values) { r.Tg ??= new(); r.Ti ??= ""; r.Tx ??= ""; r.Fn ??= ""; r.Lf ??= ""; r.Fid ??= ""; r.Ng ??= ""; r.Mt ??= ""; r.Th ??= ""; }
        if (!D.Recs.ContainsKey(DefaultNgan))
            D.Recs[DefaultNgan] = new LtRec { Id = DefaultNgan, K = "n", Tx = "Chung", Ts = 1, Up = 1 };
    }

    private readonly object _io = new();

    /// <summary>Ghi data.json. Tự sao lưu chạy ngầm cũng gọi → khóa ghi tệp, và đọc lại nếu danh sách đang bị sửa giữa chừng.</summary>
    public void Save()
    {
        var json = Retry(() => JsonSerializer.Serialize(D, Json));
        lock (_io)
        {
            Directory.CreateDirectory(Dir);
            var tmp = DataFile + ".tam";
            File.WriteAllText(tmp, json);
            File.Move(tmp, DataFile, true);
        }
        Changed?.Invoke();
    }

    /// <summary>Chạy lại (tối đa 5 lần) khi danh sách mục bị sửa cùng lúc từ luồng khác (giao diện ↔ tự sao lưu).</summary>
    public static T Retry<T>(Func<T> f)
    {
        for (var i = 0; ; i++)
        {
            try { return f(); }
            catch (InvalidOperationException) when (i < 4) { Thread.Sleep(15); }
        }
    }

    /// <summary>Bản chụp danh sách mục thỏa điều kiện (an toàn khi luồng khác đang sửa).</summary>
    public List<LtRec> Snap(Func<LtRec, bool> pred) => Retry(() => D.Recs.Values.Where(pred).ToList());

    private void Bump(LtRec r) { r.Sq = ++D.Seq; r.Pd = 1; }
    private long Stamp(LtRec? r = null) { var n = Now(); return r is null ? n : Math.Max(n, r.Up + 1); }

    public LtRec? Get(string id) => D.Recs.GetValueOrDefault(id);
    public string AbsPath(LtRec r) => r.Lf.Length == 0 ? "" : Path.Combine(FilesDir, r.Lf.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
    public bool HasLocal(LtRec r) => r.Lf.Length > 0 && File.Exists(AbsPath(r));

    private string ValidNgan(string? ng) =>
        ng is not null && D.Recs.TryGetValue(ng, out var n) && n.IsNgan && n.Dl == 0 ? ng : DefaultNgan;

    internal string RelOf(LtRec r)
    {
        var d = DateTimeOffset.FromUnixTimeMilliseconds(r.Ts > 0 ? r.Ts : Now()).ToLocalTime();
        return d.ToString("yyyy-MM", CultureInfo.InvariantCulture) + "/" + r.Id + "__" + SafeName(r.Fn);
    }

    // ------------------------------------------------------------------ thao tác mục

    public LtRec AddNote(string ngan, string title, string text, IEnumerable<string>? tags = null)
    {
        var tx = (text ?? "").Replace("\r\n", "\n");
        if (tx.Length > 20000) tx = tx[..20000];
        var ti = (title ?? "").Trim();
        if (ti.Length > 200) ti = ti[..200];
        if (tx.Trim().Length == 0 && ti.Length == 0) throw new BusinessException("Ghi chú trống.");
        var now = Now();
        var r = new LtRec { Id = NewId(), K = "m", T = "text", Ng = ValidNgan(ngan), Ti = ti, Tx = tx, Ts = now, Up = now, Tg = CleanTags(tags) };
        Bump(r); D.Recs[r.Id] = r; Save();
        return r;
    }

    public LtRec AddFile(string ngan, string path, string? note = null)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists) throw new BusinessException("Không thấy tệp " + path);
        if (fi.Length > MaxFile) throw new BusinessException($"Tệp {fi.Name} lớn hơn {MaxFile / 1048576} MB — không lưu được.");
        var fn = SafeName(fi.Name);
        var mt = MimeOf(fn);
        var now = Now();
        var r = new LtRec { Id = NewId(), K = "m", T = ImgRe.IsMatch(mt) ? "image" : "file", Ng = ValidNgan(ngan), Fn = fn, Mt = mt, Ts = now, Up = now, Tx = (note ?? "").Trim() };
        r.Lf = RelOf(r);
        var abs = AbsPath(r);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        File.Copy(path, abs + ".tam", true);
        File.Move(abs + ".tam", abs, true);
        r.Fs = new FileInfo(abs).Length;
        Bump(r); D.Recs[r.Id] = r; Save();
        return r;
    }

    public sealed record Patch(string? Title = null, string? Text = null, string? Ngan = null, IEnumerable<string>? Tags = null, bool? Pinned = null, string? FileName = null);

    public LtRec Edit(string id, Patch p)
    {
        var r = Need(id);
        if (r.Dl == 2) throw new BusinessException("Mục đã xóa hẳn.");
        if (r.IsNgan)
        {
            if (p.Title is { } nm) { nm = nm.Trim(); if (nm.Length == 0) throw new BusinessException("Tên ngăn trống."); r.Tx = nm.Length > 60 ? nm[..60] : nm; }
        }
        else
        {
            if (p.Title is { } ti) r.Ti = ti.Trim().Length > 200 ? ti.Trim()[..200] : ti.Trim();
            if (p.Text is { } tx) { tx = tx.Replace("\r\n", "\n"); r.Tx = tx.Length > 20000 ? tx[..20000] : tx; }
            if (p.Ngan is { } ng) r.Ng = ValidNgan(ng);
            if (p.Tags is { } tg) r.Tg = CleanTags(tg);
            if (p.Pinned is { } pn) r.Pn = pn ? 1 : 0;
            if (p.FileName is { } fn && r.IsFile && fn.Trim().Length > 0) r.Fn = SafeName(fn);
            if (r.T == "text" && r.Tx.Trim().Length == 0 && r.Ti.Length == 0) throw new BusinessException("Ghi chú trống.");
        }
        r.Up = Stamp(r); Bump(r); Save();
        return r;
    }

    /// <summary>Chuyển nhiều mục sang ngăn khác.</summary>
    public int Move(IEnumerable<string> ids, string ngan)
    {
        var to = ValidNgan(ngan); int n = 0;
        foreach (var id in ids)
            if (D.Recs.TryGetValue(id, out var r) && r.K == "m" && r.Dl == 0 && r.Ng != to) { r.Ng = to; r.Up = Stamp(r); Bump(r); n++; }
        if (n > 0) Save();
        return n;
    }

    /// <summary>Đặt trạng thái xóa: 0 khôi phục · 1 vào thùng rác · 2 xóa hẳn (xóa cả tệp trên máy; tệp trên Drive xóa ở lượt sao lưu sau).</summary>
    public int SetDeleted(IEnumerable<string> ids, int dl)
    {
        int n = 0;
        foreach (var id in ids)
        {
            if (!D.Recs.TryGetValue(id, out var r) || r.Dl == 2 || id == DefaultNgan || r.Dl == dl) continue;
            if (dl == 0 && r.K == "m") r.Ng = ValidNgan(r.Ng);
            if (dl == 2)
            {
                try { if (r.Lf.Length > 0) File.Delete(AbsPath(r)); } catch { /* đã mất */ }
                r.Lf = ""; r.Tx = ""; r.Th = ""; r.Ti = "";
            }
            r.Dl = dl; r.Up = Stamp(r); Bump(r); n++;
        }
        if (n > 0) Save();
        return n;
    }

    public int EmptyTrash() => SetDeleted(D.Recs.Values.Where(r => r.K == "m" && r.Dl == 1).Select(r => r.Id).ToList(), 2);

    public LtRec AddNgan(string name)
    {
        var tx = (name ?? "").Trim();
        if (tx.Length == 0) throw new BusinessException("Tên ngăn trống.");
        if (Ngans().Any(n => string.Equals(n.Name, tx, StringComparison.OrdinalIgnoreCase))) throw new BusinessException("Đã có ngăn \"" + tx + "\".");
        var now = Now();
        var r = new LtRec { Id = NewId(), K = "n", Tx = tx.Length > 60 ? tx[..60] : tx, Ts = now, Up = now };
        Bump(r); D.Recs[r.Id] = r; Save();
        return r;
    }

    /// <summary>Xóa ngăn: các mục trong ngăn chuyển về ngăn Chung (không mất mục nào).</summary>
    public void DeleteNgan(string id)
    {
        if (id == DefaultNgan) throw new BusinessException("Không xóa được ngăn Chung.");
        var c = Need(id);
        if (!c.IsNgan || c.Dl != 0) throw new BusinessException("Không xóa được ngăn này.");
        foreach (var r in D.Recs.Values.Where(r => r.K == "m" && r.Ng == id)) { r.Ng = DefaultNgan; r.Up = Stamp(r); Bump(r); }
        c.Dl = 1; c.Up = Stamp(c); Bump(c);
        Save();
    }

    private LtRec Need(string id) => D.Recs.TryGetValue(id ?? "", out var r) ? r : throw new BusinessException("Không thấy mục này (có thể đã bị xóa).");

    private static List<string> CleanTags(IEnumerable<string>? tags) =>
        (tags ?? Array.Empty<string>()).Select(t => (t ?? "").Trim().TrimStart('#')).Where(t => t.Length > 0)
            .Select(t => t.Length > 40 ? t[..40] : t).Distinct(StringComparer.OrdinalIgnoreCase).Take(20).ToList();

    public static List<string> ParseTags(string s) => CleanTags((s ?? "").Split(',', ';', '#'));

    // ------------------------------------------------------------------ truy vấn

    public List<LtNgan> Ngans()
    {
        var items = D.Recs.Values.Where(r => r.K == "m" && r.Dl == 0).ToList();
        return D.Recs.Values.Where(r => r.IsNgan && r.Dl == 0)
            .OrderBy(r => r.Id == DefaultNgan ? 0 : 1).ThenBy(r => r.Tx, StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), true))
            .Select(n => { var my = items.Where(i => i.Ng == n.Id).ToList(); return new LtNgan(n.Id, n.Tx, my.Count, my.Sum(i => i.Fs)); })
            .ToList();
    }

    public string NganName(string id) => D.Recs.TryGetValue(id, out var n) && n.IsNgan ? n.Tx : "Chung";

    /// <summary>Bộ lọc: kind = all / note / link / image / file / pin / trash; ngan = "" (mọi ngăn); q = tìm không dấu trong tiêu đề, nội dung, tên tệp, thẻ.</summary>
    public List<LtRec> Query(string ngan = "", string kind = "all", string q = "", string tag = "")
    {
        var nq = NoAccent(q.Trim());
        var words = nq.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<LtRec> s = D.Recs.Values.Where(r => r.K == "m");
        s = kind == "trash" ? s.Where(r => r.Dl == 1) : s.Where(r => r.Dl == 0);
        if (kind != "trash" && ngan.Length > 0) s = s.Where(r => r.Ng == ngan);
        s = kind switch
        {
            "note" => s.Where(r => r.T == "text" && !r.IsLink),
            "link" => s.Where(r => r.IsLink),
            "image" => s.Where(r => r.T == "image"),
            "file" => s.Where(r => r.T == "file"),
            "pin" => s.Where(r => r.Pn == 1),
            _ => s,
        };
        if (tag.Length > 0) s = s.Where(r => r.Tg.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)));
        if (words.Length > 0)
            s = s.Where(r => { var hay = NoAccent(r.Ti + " " + r.Tx + " " + r.Fn + " " + string.Join(" ", r.Tg)); return words.All(hay.Contains); });
        return s.OrderByDescending(r => r.Pn).ThenByDescending(r => r.Up).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
    }

    public List<string> Tags() =>
        D.Recs.Values.Where(r => r.K == "m" && r.Dl == 0).SelectMany(r => r.Tg).GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).ToList();

    public LtStats Stats(long backedUpSeq = long.MaxValue)
    {
        var live = D.Recs.Values.Where(r => r.K == "m" && r.Dl == 0).ToList();
        return new LtStats(live.Count, live.Count(r => r.T == "text" && !r.IsLink), live.Count(r => r.IsLink), live.Count(r => r.T == "file"),
            live.Count(r => r.T == "image"), live.Sum(r => r.Fs), D.Recs.Values.Count(r => r.K == "m" && r.Dl == 1), live.Count(r => r.Pn == 1),
            D.Recs.Values.Count(r => r.Sq > backedUpSeq));
    }

    // ------------------------------------------------------------------ gộp (khôi phục / nạp app cũ)

    /// <summary>
    /// Gộp 1 bản ghi từ nơi khác (bản sao lưu Drive, .zip, app cũ): bản có <c>up</c> lớn hơn thắng (bằng nhau: trạng thái xóa cao hơn thắng).
    /// Giữ tệp trên máy và mã tệp Drive của bản hiện có. Trả true nếu có thay đổi.
    /// </summary>
    public bool Merge(LtRec w)
    {
        if (w is null || !IdRe.IsMatch(w.Id ?? "")) return false;
        D.Recs.TryGetValue(w.Id, out var cur);
        if (cur is not null && (w.Up < cur.Up || (w.Up == cur.Up && w.Dl <= cur.Dl)))
        {
            if (cur.Fid.Length == 0 && (w.Fid ?? "").Length > 0) { cur.Fid = w.Fid; return true; }
            return false;
        }
        var r = new LtRec
        {
            Id = w.Id, K = w.K == "n" ? "n" : "m", Ng = w.Ng ?? "", Ti = w.Ti ?? "", Tx = w.Tx ?? "", Fn = w.Fn ?? "", Fs = w.Fs, Mt = w.Mt ?? "",
            Th = "", Fid = (w.Fid ?? "").Length > 0 ? w.Fid! : cur?.Fid ?? "", Bg = w.Bg, Ts = w.Ts > 0 ? w.Ts : w.Up, Up = w.Up, Dl = w.Dl is 1 or 2 ? w.Dl : 0,
            Pn = w.Pn != 0 ? 1 : 0, Tg = CleanTags(w.Tg), Src = w.Src == "dt" ? "dt" : "pc", Lf = cur?.Lf ?? "",
        };
        r.T = r.K == "n" ? "" : w.T is "text" or "image" or "file" ? w.T : "text";
        if (r.Dl == 2) { try { if (r.Lf.Length > 0) File.Delete(AbsPath(r)); } catch { } r.Lf = ""; r.Tx = ""; }
        r.Sq = ++D.Seq; r.Pd = 1;
        D.Recs[r.Id] = r;
        return true;
    }

    /// <summary>Đặt tệp (đã tải về / chép từ nơi khác) vào chỗ của bản ghi.</summary>
    public void AttachFile(LtRec r, string sourcePath, bool move = false)
    {
        r.Lf = RelOf(r);
        var abs = AbsPath(r);
        Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
        if (move) File.Move(sourcePath, abs, true); else File.Copy(sourcePath, abs, true);
        if (r.Fs <= 0) r.Fs = new FileInfo(abs).Length;
    }

    /// <summary>Mục tệp còn dùng nhưng chưa có tệp trên máy (cần tải từ Drive).</summary>
    public List<LtRec> MissingFiles() => Snap(r => r.IsFile && r.Dl == 0 && !HasLocal(r));
}
