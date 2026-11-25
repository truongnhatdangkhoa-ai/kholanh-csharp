using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.TroLy;

/// <summary>
/// HÀNG ĐỢI ĐỀ XUẤT CÓ TRẠNG THÁI (theo core/tro-ly/hang-doi.js). HÀM THUẦN — TroLyService lo lưu file.
/// Vòng đời: moi → daGui (đã sao chép báo cáo cho Claude) → daSua (Quản trị bấm "Đã sửa") → lần quét sau hết lỗi = dong; còn lỗi = taiPhat.
/// Cảnh báo tự hết cũng thành dong. boQua = Quản trị chủ động bỏ qua. loiNguon = lỗi từ file nguồn, nhắc 1 lần, không đếm tái phát.
/// </summary>
public static class TroLyQueue
{
    public static readonly string[] States = { "moi", "daGui", "daSua", "taiPhat", "dong", "boQua", "loiNguon" };
    public static readonly Dictionary<string, string> Label = new()
    {
        ["moi"] = "Mới", ["daGui"] = "Đã gửi Claude", ["daSua"] = "Chờ kiểm tra", ["taiPhat"] = "Tái phát",
        ["dong"] = "Đã đóng", ["boQua"] = "Bỏ qua", ["loiNguon"] = "Lỗi từ nguồn",
    };
    public const string NoteNguon = "Lỗi từ file nguồn — cần sửa ở phần mềm kho / file Excel gốc, không phải lỗi của app";
    private const int HistMax = 12, Max = 600;
    private static readonly TimeSpan KeepClosed = TimeSpan.FromDays(60), KeepGone = TimeSpan.FromDays(90);
    /// <summary>1.102: mục "Đã sửa" chỉ được kết luận sau ≥ 24 giờ không thấy lại; lỗi phần mềm (log 7 ngày còn dòng cũ) sau 48 giờ không có lần mới.</summary>
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromHours(24), ErrRecheckAfter = TimeSpan.FromHours(48);

    public sealed class Hist { public string St { get; set; } = ""; public DateTime At { get; set; } public string Ver { get; set; } = ""; }

    public sealed class Item
    {
        public string St { get; set; } = "moi";
        public DateTime FirstAt { get; set; }
        public DateTime LastSeenAt { get; set; }
        public DateTime? ClosedAt { get; set; }
        public DateTime? SentAt { get; set; }
        public DateTime? FixedAt { get; set; }
        public string FixedVer { get; set; } = "";
        public int Recur { get; set; }
        public string Note { get; set; } = "";
        public string Title { get; set; } = "";
        public string Mod { get; set; } = "";
        public string Sev { get; set; } = "";
        public string Cat { get; set; } = "";
        public bool LightSeen { get; set; }
        public bool DeepSeen { get; set; }
        public bool Hidden { get; set; }
        public bool Verified { get; set; }
        public bool Nguon { get; set; }
        public bool NguonOff { get; set; }
        public List<Hist> History { get; set; } = new();
        public Item Clone()
        {
            var x = (Item)MemberwiseClone();
            x.History = History.Select(h => new Hist { St = h.St, At = h.At, Ver = h.Ver }).ToList();
            return x;
        }
    }

    public sealed record Changes(int Moi, int TaiPhat, int Dong);

    private static void Push(Item r, string st, DateTime at, string ver)
    {
        r.History.Add(new Hist { St = st, At = at, Ver = ver });
        if (r.History.Count > HistMax) r.History.RemoveRange(0, r.History.Count - HistMax);
    }

    /// <summary>
    /// Cập nhật hàng đợi sau một lần quét. safeClose = false (có bước quét lỗi) thì KHÔNG tự đóng gì.
    /// Lỗi phần mềm (loi-app) không tự đóng — log 7 ngày vẫn còn nên "hết" chỉ khi hết hạn log.
    /// </summary>
    public static (Dictionary<string, Item> Items, Changes Changes) Update(Dictionary<string, Item> items, IEnumerable<Finding> findings,
        DateTime now, bool deep, bool safeClose, string ver)
    {
        var o = items.ToDictionary(x => x.Key, x => x.Value.Clone());
        int moi = 0, tai = 0, dong = 0;
        var cur = new HashSet<string>();
        void Seen(Item r, Finding f) { r.Title = f.Title; r.Mod = f.Mod; r.Sev = f.Sev; r.Cat = f.Cat; r.LastSeenAt = now; if (deep) r.DeepSeen = true; else r.LightSeen = true; }
        foreach (var f in findings)
        {
            if (f.Sev == "xanh") continue;
            cur.Add(f.Id);
            o.TryGetValue(f.Id, out var r);
            if (r is { St: "loiNguon" }) { Seen(r, f); continue; }
            if (r is not null && f.Nguon && !r.NguonOff && r.St != "boQua")
            {
                r.Nguon = true; r.St = "loiNguon"; r.Note = NoteNguon; r.ClosedAt = null; r.Hidden = false; Push(r, "loiNguon", now, ver); Seen(r, f); continue;
            }
            if (r is null) { r = o[f.Id] = new Item { St = "moi", FirstAt = now }; Push(r, "moi", now, ver); moi++; }
            else if (r.St == "daSua" && f.Cat == "loi-app" && f.LastAt is { } la && r.FixedAt is { } fa && la <= fa)
            {   // 1.102: log vẫn còn dòng lỗi CŨ (trước lúc bấm Đã sửa) → chưa phải tái phát; 48 giờ không có lần mới → đóng
                if (now - fa >= ErrRecheckAfter)
                {
                    r.Verified = true; r.St = "dong"; r.ClosedAt = now; Push(r, "dong", now, ver);
                    r.Note = $"Đã xác nhận: {ErrRecheckAfter.TotalHours:0} giờ sau khi sửa không xuất hiện lại"; dong++;
                }
                Seen(r, f); continue;
            }
            else if (r.St is "dong" or "daSua")
            {
                r.Recur++;
                var was = r.St;
                r.Note = was == "daSua" ? "Đã đánh dấu sửa nhưng quét lại vẫn thấy" : r.Hidden ? "Đã xóa khỏi lịch sử nhưng xuất hiện lại" : "Đã đóng nhưng xuất hiện lại";
                if (r.FixedVer.Length > 0 && ver.Length > 0 && r.FixedVer != ver) r.Note += $" (sửa ở bản {r.FixedVer}, tái phát ở bản {ver})";
                r.St = "taiPhat"; r.ClosedAt = null; r.Hidden = false; Push(r, "taiPhat", now, ver); tai++;
            }
            Seen(r, f);
        }
        foreach (var (id, r) in o)
        {
            if (cur.Contains(id) || r.St is "dong" or "boQua") continue;
            if (!safeClose || r.Cat == "loi-app") continue;
            if (!deep && !r.LightSeen) continue; // chỉ thấy khi Quét sâu → chỉ Quét sâu mới được kết luận hết
            if (r.St == "daSua" && r.FixedAt is { } fx && now - fx < RecheckAfter) continue;   // 1.102: chờ đủ 24 giờ rồi mới kết luận
            r.Verified = r.St == "daSua"; r.St = "dong"; r.ClosedAt = now; Push(r, "dong", now, ver);
            r.Note = r.Verified ? "Đã xác nhận hết lỗi sau khi sửa" : "Lần quét sau không còn thấy"; dong++;
        }
        foreach (var id in o.Keys.ToList())
        {
            var r = o[id];
            if (r.St == "dong" && now - (r.ClosedAt ?? now) > KeepClosed) o.Remove(id);
            else if (r.St == "boQua" && !cur.Contains(id) && now - (r.LastSeenAt == default ? r.FirstAt : r.LastSeenAt) > KeepGone) o.Remove(id);
        }
        if (o.Count > Max)
            foreach (var id in o.OrderBy(x => x.Value.LastSeenAt).Take(o.Count - Max).Select(x => x.Key).ToList()) o.Remove(id);
        return (o, new Changes(moi, tai, dong));
    }

    /// <summary>Quản trị đổi trạng thái tay: moi | daGui | daSua | boQua | loiNguon. Chỉ đổi mục đã có trong hàng đợi.</summary>
    public static (Dictionary<string, Item> Items, int N) Mark(Dictionary<string, Item> items, IEnumerable<string> ids, string st, DateTime now, string ver)
    {
        if (!States.Contains(st) || st is "dong" or "taiPhat") return (items, 0);
        var o = new Dictionary<string, Item>(items); var n = 0;
        foreach (var id in ids)
        {
            if (!o.TryGetValue(id, out var r)) continue;
            var x = r.Clone(); x.St = st; x.Note = "";
            if (st == "moi" && r.St == "loiNguon") x.NguonOff = true; // Quản trị nói «không phải lỗi nguồn» → không tự chuyển lại
            if (st == "loiNguon") { x.Nguon = true; x.Note = NoteNguon; }
            if (st == "daGui") x.SentAt = now;
            if (st == "daSua") { x.FixedAt = now; x.FixedVer = ver; }
            Push(x, st, now, ver); o[id] = x; n++;
        }
        return (o, n);
    }

    /// <summary>Xóa lịch sử đã xử lý: chỉ mục đã đóng (giữ dấu vết ẩn để nhận ra tái phát) hoặc đã bỏ qua (xóa hẳn).</summary>
    public static (Dictionary<string, Item> Items, int N) Purge(Dictionary<string, Item> items, IEnumerable<string> ids)
    {
        var o = new Dictionary<string, Item>(items); var n = 0;
        foreach (var id in ids)
        {
            if (!o.TryGetValue(id, out var r)) continue;
            if (r.St == "dong") { var x = r.Clone(); x.Hidden = true; o[id] = x; n++; }
            else if (r.St == "boQua") { o.Remove(id); n++; }
        }
        return (o, n);
    }

    public static List<(string Id, Item Item)> List(Dictionary<string, Item> items) =>
        items.Where(x => !x.Value.Hidden).OrderByDescending(x => x.Value.LastSeenAt > (x.Value.ClosedAt ?? default) ? x.Value.LastSeenAt : x.Value.ClosedAt ?? default)
             .Select(x => (x.Key, x.Value)).ToList();
}

/// <summary>Soạn báo cáo để Quản trị sao chép đưa cho Claude (theo core/tro-ly/report.js). Mặc định ẩn email / tên người.</summary>
public static class TroLyReport
{
    private static readonly Dictionary<string, string> Sev = new() { ["do"] = "🔴 NẶNG", ["vang"] = "🟡 CẦN XEM", ["xanh"] = "🟢 ỔN" };

    public static string Mask(string? s, bool on)
    {
        s ??= ""; if (!on) return s;
        s = Regex.Replace(s, @"[\w.+-]+@[\w-]+\.[\w.-]+", "[email]");
        return Regex.Replace(s, @"\b(tk|user|người|nguoi|người tạo|khách|khach|ten)\s*[=:]\s*[^,;|·\n]+", "$1=[ẩn]", RegexOptions.IgnoreCase);
    }

    public static string Build(IReadOnlyList<Finding> findings, string version, DateTime at, bool anDanh = true)
    {
        var L = new StringBuilder();
        L.AppendLine("# BÁO CÁO TỪ \"TRỢ LÝ HỆ THỐNG\" — Kho Lạnh C#");
        L.AppendLine($"Phiên bản app: {version} · lập lúc {at.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)} · {findings.Count} phát hiện" + (anDanh ? " · đã ẩn tên khách / người dùng" : ""));
        L.AppendLine();
        L.AppendLine("Yêu cầu: hãy điều tra các phát hiện dưới đây trong repo kholanh-csharp, CHỈ đề xuất cách sửa (nêu rõ file / hàm cần đổi và lý do), rồi sửa kèm test và tăng phiên bản. Phần \"File nghi ngờ\" chỉ là gợi ý điều tra, chưa phải kết luận nguyên nhân. Mục ghi \"lỗi từ nguồn\" là do file Excel / phần mềm kho gốc, không cần sửa app.");
        L.AppendLine();
        for (var i = 0; i < findings.Count; i++)
        {
            var f = findings[i];
            L.AppendLine($"## {i + 1}. {Sev.GetValueOrDefault(f.Sev, f.Sev)} — {Mask(f.Title, anDanh)}");
            L.AppendLine("- Module / nơi: " + Mask(f.Mod, anDanh));
            L.AppendLine("- Hiện tượng: " + Mask(f.Detail, anDanh));
            if (f.Nguon || f.Q == "loiNguon") L.AppendLine("- Lỗi từ nguồn (file Excel / phần mềm kho gốc).");
            if (f.Recur > 0) L.AppendLine($"- ⚠ TÁI PHÁT {f.Recur} lần" + (f.QNote.Length > 0 ? " — " + Mask(f.QNote, anDanh) : "") + " (lần sửa trước chưa dứt điểm — hãy tìm nguyên nhân gốc, đừng vá triệu chứng).");
            if (f.Evidence.Count > 0) { L.AppendLine("- Bằng chứng:"); foreach (var e in f.Evidence) L.AppendLine("  - " + Mask(e, anDanh)); }
            if (f.Files.Count > 0) L.AppendLine("- File nghi ngờ: " + string.Join(", ", f.Files));
            L.AppendLine();
        }
        return L.ToString();
    }
}
