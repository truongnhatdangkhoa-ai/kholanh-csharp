using System.Globalization;
using System.Text.Json.Nodes;
using KhoLanh.Core.Modules;

namespace KhoLanh.Core.TonKho;

/// <summary>
/// 1.73 (N3) — "mẫu" (cấu trúc) của file Excel đã nạp thành công ở Tồn kho An An / Tồn kho gửi / Tồn theo vị trí:
/// tiêu đề cột theo thứ tự, sheet, ngày số liệu, số dòng, tên file, dấu vân tay nội dung. Lần nạp sau so file mới với mẫu này
/// → cảnh báo có mức (đỏ / vàng / thông tin) trong hộp xem trước khi nạp.
/// Lưu trong sheet <see cref="Sheet"/> của chính database module (ô JSON 1 dòng <c>[{ "data": "__JSON__{…}" }]</c>) qua <see cref="ISheetSource"/>
/// → máy LAN / Drive dùng chung cũng thấy. Chỉ đọc kết quả đã đọc file (không đọc lại file).
/// </summary>
public static class MauFile
{
    public const string Sheet = "NapMau";
    /// <summary>Số dòng đổi quá ngưỡng này (tỷ lệ) → cảnh báo vàng.</summary>
    public const double RowsPct = 0.30;

    public sealed class Mau
    {
        public string FileName = "", Sheet = "", DataDate = "", Sha = "", SavedAt = "", User = "";
        public List<string> Headers = new();
        public int Rows;
    }

    /// <summary>Mức cảnh báo: Loi = đỏ (nút Nạp → "Vẫn nạp" + xác nhận), CanhBao = vàng, ThongTin = tím nhạt.</summary>
    public enum Muc { Loi, CanhBao, ThongTin }
    public sealed record CanhBao(string Key, Muc Muc, string Text);

    /// <summary>Cột bắt buộc mà module dùng: tên hiện + các tên được chấp nhận (so theo <see cref="Key"/>).</summary>
    public sealed record Cot(string Label, params string[] Alias);

    public static readonly Cot[] BatBuocAnAn =
    {
        new("Mã hàng"), new("Tên hàng"), new("Đặc tính"), new("Size"), new("Tồn cuối"), new("Tồn cuối (NET)"), new("Trọng lượng"),
    };
    public static readonly Cot[] BatBuocGui =
    {
        new("Mã hàng"), new("Tên hàng"), new("Số lô"), new("Size"), new("Quy cách"),
    };
    public static readonly Cot[] BatBuocViTri =
    {
        new("Mã hàng", "Mã HH", "Mã sản phẩm", "Item code"), new("Vị trí", "Vị trí kho", "Location"),
        new("SL Tồn", "Số lượng tồn", "Số lượng", "SL"), new("T.Lượng", "Trọng lượng", "TL"),
    };

    /// <summary>Khóa so tên cột: bỏ dấu, chữ thường, bỏ mọi ký tự không phải chữ / số ("T.Lượng" = "tluong").</summary>
    public static string Key(string? h)
    {
        var f = JsText.Fuzzy(h);
        var sb = new System.Text.StringBuilder(f.Length);
        foreach (var ch in f) if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }

    private static List<string> Clean(IEnumerable<string>? headers) =>
        (headers ?? Array.Empty<string>()).Select(h => JsText.JsTrim(h ?? "")).Where(h => Key(h).Length > 0).ToList();

    public static Mau Tao(string fileName, string sheet, IEnumerable<string>? headers, int rows, string dataDate, string sha, string user = "", DateTime? now = null) => new()
    {
        FileName = fileName ?? "", Sheet = sheet ?? "", Headers = Clean(headers), Rows = rows, DataDate = DataDate.Iso(dataDate), Sha = sha ?? "", User = user ?? "",
        SavedAt = (now ?? DateTime.Now).ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
    };

    // ------------------------------------------------------------------ so sánh

    /// <summary>
    /// So file mới với mẫu lần nạp trước. <paramref name="ngayDangCo"/> = ngày số liệu module đang dùng (người dùng có thể đã sửa sau khi nạp) — ưu tiên hơn ngày trong mẫu.
    /// Danh sách xếp đỏ → vàng → thông tin.
    /// </summary>
    public static List<CanhBao> SoSanh(Mau? truoc, Mau moi, IReadOnlyList<Cot> batBuoc, string? ngayDangCo = null)
    {
        var o = new List<CanhBao>();
        var nh = moi.Headers.Select(Key).ToList();
        var newSet = nh.ToHashSet();
        // thiếu cột bắt buộc (chỉ xét khi đọc được tiêu đề)
        var thieu = new List<Cot>();
        if (nh.Count > 0)
            foreach (var c in batBuoc)
                if (!new[] { c.Label }.Concat(c.Alias).Any(a => newSet.Contains(Key(a)))) thieu.Add(c);

        if (truoc is not null && truoc.Headers.Count > 0 && nh.Count > 0)
        {
            var ph = truoc.Headers.Select(Key).ToList();
            var oldSet = ph.ToHashSet();
            var mat = truoc.Headers.Where(h => !newSet.Contains(Key(h))).Distinct().ToList();
            var them = moi.Headers.Where(h => !oldSet.Contains(Key(h))).Distinct().ToList();
            var dungDoiTen = new HashSet<string>();
            foreach (var h in mat)
            {
                var goiY = them.Where(t => !dungDoiTen.Contains(t)).Select(t => (t, s: Giong(h, t))).Where(x => x.s >= 0.6).OrderByDescending(x => x.s).Select(x => x.t).FirstOrDefault();
                var bb = thieu.FirstOrDefault(c => new[] { c.Label }.Concat(c.Alias).Any(a => Key(a) == Key(h)));
                if (bb is not null)
                {
                    thieu.Remove(bb);
                    o.Add(new("thieu:" + Key(h), Muc.Loi, $"Thiếu cột bắt buộc “{h}” (lần nạp trước có)" + (goiY is not null ? $" — file có cột gần giống “{goiY}”, có thể đã đổi tên" : "") + ". Module cần cột này để tính tồn."));
                }
                else if (goiY is not null)
                    o.Add(new("doiten:" + Key(h), Muc.CanhBao, $"Cột “{h}” có vẻ đã đổi tên thành “{goiY}”."));
                else
                    o.Add(new("mat:" + Key(h), Muc.CanhBao, $"Cột “{h}” không còn trong file (lần nạp trước có)."));
                if (goiY is not null) dungDoiTen.Add(goiY);
            }
            var moiThem = them.Where(t => !dungDoiTen.Contains(t)).ToList();
            if (moiThem.Count > 0)
                o.Add(new("them", Muc.CanhBao, $"Cột mới so với lần nạp trước: {string.Join(", ", moiThem.Take(8).Select(t => "“" + t + "”"))}{(moiThem.Count > 8 ? $" … (+{moiThem.Count - 8})" : "")}."));
            // thứ tự các cột có ở cả 2 lần
            var chungCu = ph.Where(newSet.Contains).Distinct().ToList();
            var chungMoi = nh.Where(oldSet.Contains).Distinct().ToList();
            if (!chungCu.SequenceEqual(chungMoi))
            {
                var name = moi.Headers.GroupBy(Key).ToDictionary(g => g.Key, g => g.First());
                var doi = chungMoi.Where((k, i) => i >= chungCu.Count || chungCu[i] != k).Take(5).Select(k => "“" + name.GetValueOrDefault(k, k) + "”").ToList();
                o.Add(new("thutu", Muc.CanhBao, $"Thứ tự cột khác lần nạp trước (vd {string.Join(", ", doi)}) — module đọc theo tên cột nên vẫn nạp được; kiểm tra file có đúng mẫu xuất không."));
            }
        }
        foreach (var c in thieu) o.Add(new("thieu:" + Key(c.Label), Muc.Loi, $"Thiếu cột bắt buộc “{c.Label}”. Module cần cột này để tính tồn."));

        if (truoc is not null)
        {
            var dTruoc = DataDate.Iso(ngayDangCo); if (dTruoc.Length == 0) dTruoc = truoc.DataDate;
            var dMoi = moi.DataDate;
            if (dTruoc.Length > 0 && dMoi.Length > 0)
            {
                var cmp = string.CompareOrdinal(dMoi, dTruoc);
                if (cmp < 0) o.Add(new("ngay-cu", Muc.Loi, $"Ngày số liệu {Vn(dMoi)} CŨ HƠN dữ liệu đang có ({Vn(dTruoc)}) — có chọn nhầm file cũ không?"));
                else if (cmp == 0) o.Add(new("ngay-bang", Muc.CanhBao, $"Ngày số liệu {Vn(dMoi)} trùng lần nạp trước — nạp sẽ thay số liệu cùng ngày."));
            }
            if (truoc.Rows > 0 && moi.Rows >= 0)
            {
                var pct = (moi.Rows - truoc.Rows) / (double)truoc.Rows;
                if (Math.Abs(pct) > RowsPct)
                    o.Add(new("sodong", Muc.CanhBao, $"Số dòng {(pct > 0 ? "TĂNG" : "GIẢM")} {Math.Abs(pct) * 100:0}% so với lần nạp trước ({truoc.Rows:#,0} → {moi.Rows:#,0}) — bất thường, kiểm tra có đúng file / đúng sheet không."));
            }
            if (truoc.Sheet.Length > 0 && moi.Sheet.Length > 0 && !string.Equals(truoc.Sheet, moi.Sheet, StringComparison.OrdinalIgnoreCase))
                o.Add(new("sheet", Muc.ThongTin, $"Sheet khác lần nạp trước (“{truoc.Sheet}” → “{moi.Sheet}”)."));
            if (truoc.FileName.Length > 0 && string.Equals(truoc.FileName, moi.FileName, StringComparison.OrdinalIgnoreCase)
                && truoc.Sha.Length > 0 && moi.Sha.Length > 0 && truoc.Sha != moi.Sha)
                o.Add(new("cungten", Muc.ThongTin, $"Cùng tên file với lần nạp trước (“{moi.FileName}”) nhưng nội dung đã khác — file đã được sửa / xuất lại."));
        }
        return o.OrderBy(x => x.Muc).ToList();
    }

    public static bool CoLoi(IEnumerable<CanhBao> list) => list.Any(x => x.Muc == Muc.Loi);

    private static string Vn(string iso) => iso.Length == 10 ? $"{iso[8..10]}/{iso[5..7]}/{iso[..4]}" : iso;

    /// <summary>Độ giống 2 tên cột (0..1): một khóa chứa khóa kia → 0,8+; còn lại 1 − Levenshtein / độ dài lớn hơn.</summary>
    public static double Giong(string a, string b)
    {
        string x = Key(a), y = Key(b);
        if (x.Length == 0 || y.Length == 0) return 0;
        if (x == y) return 1;
        var lev = 1 - Lev(x, y) / (double)Math.Max(x.Length, y.Length);
        if (Math.Min(x.Length, y.Length) >= 3 && (x.Contains(y) || y.Contains(x))) lev = Math.Max(lev, 0.8);
        return lev;
    }

    private static int Lev(string a, string b)
    {
        var d = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) d[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            var prev = d[0]; d[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var t = d[j];
                d[j] = Math.Min(Math.Min(d[j] + 1, d[j - 1] + 1), prev + (a[i - 1] == b[j - 1] ? 0 : 1));
                prev = t;
            }
        }
        return d[b.Length];
    }

    // ------------------------------------------------------------------ tiêu đề từ dữ liệu thô (Tồn kho gửi)

    /// <summary>Dòng tiêu đề của bảng thô: trong 30 dòng đầu, dòng đầu tiên có ô "Mã hàng" (bỏ ô trống). Không thấy → rỗng (bỏ qua so cột).</summary>
    public static List<string> TieuDeTho(IReadOnlyList<object?[]> raw)
    {
        for (var r = 0; r < Math.Min(30, raw.Count); r++)
        {
            var cells = raw[r].Select(c => c is double d ? JsText.JsStr(d) : Convert.ToString(c, CultureInfo.InvariantCulture) ?? "").ToList();
            if (cells.Any(c => Key(c) == "mahang")) return Clean(cells);
        }
        return new();
    }

    // ------------------------------------------------------------------ theo module (chỉ dùng kết quả đã đọc file)

    public static Mau Of(AnAnService.NapPreview pv, string user = "") =>
        Tao(pv.FileName, pv.Sheet, pv.Prepared.Headers, pv.Check.Total, pv.DataDate, pv.Sha, user);
    public static Mau Of(TonKhoGui.GuiService.NapPreview pv, string user = "") =>
        Tao(pv.FileName, pv.Sheet, TieuDeTho(pv.Raw), pv.Extracted?.Rows.Count ?? 0, pv.DataDate, pv.Sha, user);
    public static Mau Of(ViTri.ViTriService.FileData f, string user = "") =>
        Tao(f.FileName, f.Sheet, f.Headers, f.Rows.Count, ViTri.ViTriService.GuessDate(f.FileName), f.Sha, user);

    /// <summary>Đọc mẫu lần trước của module rồi so (lỗi đọc → coi như chưa có mẫu).</summary>
    public static (Mau? Truoc, List<CanhBao> List) SoVoiLanTruoc(ISheetSource src, string db, Mau moi, IReadOnlyList<Cot> batBuoc, string? ngayDangCo)
    {
        Mau? truoc = null;
        try { truoc = Doc(src, db); } catch { /* chưa có / mất mạng */ }
        return (truoc, SoSanh(truoc, moi, batBuoc, ngayDangCo));
    }

    /// <summary>Ghi mẫu sau khi nạp thành công — lỗi ghi không chặn việc nạp.</summary>
    public static void Luu(ISheetSource src, string db, Mau m) { try { Ghi(src, db, m); } catch { /* máy chỉ xem / mất mạng: lần nạp sau */ } }

    // ------------------------------------------------------------------ lưu / đọc

    public static Mau? Doc(ISheetSource src, string db)
    {
        try
        {
            if (JsonNode.Parse(src.Read(db, Sheet)) is not JsonArray a || a.Count == 0 || a[0] is not JsonObject r) return null;
            var cell = r["data"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            if (cell is null || !cell.StartsWith(AnAnStore.JsonTag)) return null;
            return FromJson(JsonNode.Parse(cell[AnAnStore.JsonTag.Length..]) as JsonObject);
        }
        catch { return null; }
    }

    public static void Ghi(ISheetSource src, string db, Mau m) =>
        src.Write(db, new Dictionary<string, JsonArray?> { [Sheet] = AnAnStore.JsonCell(ToJson(m)) });

    public static JsonObject ToJson(Mau m) => new()
    {
        ["v"] = 1, ["fileName"] = m.FileName, ["sheet"] = m.Sheet, ["dataDate"] = m.DataDate, ["rows"] = m.Rows, ["sha"] = m.Sha,
        ["savedAt"] = m.SavedAt, ["user"] = m.User, ["headers"] = new JsonArray(m.Headers.Select(h => (JsonNode)JsonValue.Create(h)!).ToArray()),
    };

    public static Mau? FromJson(JsonObject? o)
    {
        if (o is null) return null;
        string S(string k) => AnAnStore.S(o[k]);
        return new Mau
        {
            FileName = S("fileName"), Sheet = S("sheet"), DataDate = S("dataDate"), Sha = S("sha"), SavedAt = S("savedAt"), User = S("user"),
            Rows = int.TryParse(S("rows"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0,
            Headers = (o["headers"] as JsonArray ?? new()).Select(x => AnAnStore.S(x)).Where(x => x.Length > 0).ToList(),
        };
    }
}
