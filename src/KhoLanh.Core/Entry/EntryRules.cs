using System.Globalization;
using KhoLanh.Core.Grid;

namespace KhoLanh.Core.Entry;

/// <summary>
/// Ô TỰ ĐIỀN của trang Nhập liệu (1.62) — hàm thuần, dùng cho lưới (mỗi lần sửa / dán) và khi lưu:
///  · cột "tự điền theo mã" (Tên hàng / Size / Trọng lượng): đổi Mã hàng (gõ, dán nhiều dòng, điền xuống) → cập nhật theo danh mục / tồn;
///    cột cùng tên ở trang cũ (chưa đặt tự điền) chỉ được điền khi ô đang trống hoặc đang là giá trị tự điền của mã cũ;
///  · cột "Ngày tạo dòng": dòng trống được nhập lần đầu → ngày hôm nay; sửa dòng về sau KHÔNG đổi ngày (sửa tay được);
///  · cột công thức: tính lại cho dòng vừa đổi.
/// Dòng bị xóa hết phần nhập tay → các ô tự điền / công thức của dòng cũng được xóa.
/// </summary>
public static class EntryRules
{
    public const string Ten = "ten", Size = "size", Tl = "tl", Today = "today", NgayXuat = "ngayxuat", Dt = "dt";

    public static readonly (string Key, string Label)[] AutoKinds =
    {
        ("", "Không"), (Ten, "Tên hàng theo mã"), (Size, "Size theo mã"), (Tl, "Trọng lượng theo mã"), (Today, "Ngày tạo dòng"), (NgayXuat, "Ngày khi gõ SL xuất"),
        (Dt, "Đặc tính theo lô (kết quả kháng sinh)"),
    };
    public static string AutoLabel(string? key) => AutoKinds.FirstOrDefault(a => a.Key == (key ?? "")).Label ?? "Không";
    /// <summary>Chữ người dùng gõ / chọn ở ô "Tự điền" → khóa.</summary>
    public static string AutoKey(string? label)
    {
        var f = GridFilter.Fold(label ?? "").Trim();
        if (f.Length == 0 || f.StartsWith("khong")) return "";
        if (f == NgayXuat || (f.StartsWith("ngay") && f.Contains("xuat"))) return NgayXuat;
        if (f.StartsWith("ngay") || f.Contains("hom nay") || f == Today) return Today;
        if (f.StartsWith("dac tinh") || f == Dt) return Dt;
        if (f.StartsWith("ten") || f == Ten) return Ten;
        if (f.StartsWith("size") || f.StartsWith("co")) return Size;
        if (f.StartsWith("trong") || f.StartsWith("tl") || f.StartsWith("kg")) return Tl;
        return "";
    }

    /// <summary>Thông tin một mã hàng để tự điền.</summary>
    public sealed record Info(string Ten, string Size, string Tl);

    private static readonly string[] CodeNames = { "ma hang", "ma", "ma hh", "ma sp", "ma san pham", "item code", "code" };
    public static bool IsCodeCol(GridColumn c) => CodeNames.Contains(GridFilter.Fold(c.Header).Trim());
    public static int CodeCol(IReadOnlyList<GridColumn> cols)
    {
        for (var i = 0; i < cols.Count; i++) if (IsCodeCol(cols[i])) return i;
        return -1;
    }

    /// <summary>Cột chưa đặt tự điền nhưng tên cột cho biết nội dung (trang cũ / mẫu tự làm): Tên hàng, Size, Trọng lượng / Kg / thùng.</summary>
    public static string ImplicitAuto(GridColumn c)
    {
        if (c.Auto.Length > 0 || c.Formula.Length > 0) return "";
        return GridFilter.Fold(c.Header).Trim() switch
        {
            "ten hang" or "ten" or "ten san pham" => Ten,
            "size" or "kich co" => Size,
            "trong luong" or "tl" or "kg / thung" or "kg/thung" or "kg thung" or "tl thung" or "trong luong thung" => Tl,
            _ => "",
        };
    }

    /// <summary>1.115: cột Lô (Lô / Số lô / Lô batch).</summary>
    public static int LoCol(IReadOnlyList<GridColumn> cols)
    {
        for (var i = 0; i < cols.Count; i++) if (GridFilter.Fold(cols[i].Header).Trim() is "lo" or "so lo" or "lo batch" or "lo hang") return i;
        return -1;
    }

    /// <summary>
    /// 1.116: chuẩn hóa ngày trong ô Lô: "22.05 L16" → "22.05.2026 L16" (không gõ năm = năm hiện tại; ngày đó còn ở tương lai thì năm trước —
    /// nguyên liệu không thể có ngày tương lai), "22.05.25" → "22.05.2025", "2/5/25" → "02.05.2025". Không có ngày / ngày sai → giữ nguyên.
    /// </summary>
    public static string NormLo(string? text, DateTime today)
    {
        var s = text ?? "";
        var m = System.Text.RegularExpressions.Regex.Match(s, @"(?<!\d)(\d{1,2})\s*[./\-]\s*(\d{1,2})(?:\s*[./\-]\s*(\d{4}|\d{2}))?(?!\d)");
        if (!m.Success) return s;
        int d = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value);
        if (mo < 1 || mo > 12 || d < 1 || d > 31) return s;
        int y;
        if (m.Groups[3].Success) { y = int.Parse(m.Groups[3].Value); if (y < 100) y += 2000; }
        else { y = today.Year; if (new DateTime(y, mo, Math.Min(d, DateTime.DaysInMonth(y, mo))) > today.Date) y--; }
        if (d > DateTime.DaysInMonth(y, mo)) return s;
        return s[..m.Index] + $"{d:00}.{mo:00}.{y}" + s[(m.Index + m.Length)..];
    }

    /// <summary>Cột số lượng xuất (Số lượng xuất / SL xuất / Số kiện xuất).</summary>
    public static int XuatQtyCol(IReadOnlyList<GridColumn> cols)
    {
        for (var i = 0; i < cols.Count; i++)
            if (GridFilter.Fold(cols[i].Header).Trim() is "so luong xuat" or "sl xuat" or "so kien xuat" or "so thung xuat") return i;
        return -1;
    }

    private static string Field(Info? i, string kind) => i is null ? "" : kind switch { Ten => i.Ten, Size => i.Size, Tl => i.Tl, _ => "" };

    /// <summary>Ô "tự động" (không tính là phần người dùng nhập khi xét dòng trống).</summary>
    private static bool Derived(GridColumn c) => c.Auto.Length > 0 || c.Formula.Length > 0;

    public static string TodayText(DateTime today) => today.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// Các ô cần ghi thêm sau một lần sửa. <paramref name="get"/> đọc giá trị HIỆN TẠI (đã áp phần vừa sửa);
    /// <paramref name="lookup"/> tra mã hàng (null = chưa nạp / không có).
    /// </summary>
    public static List<(int r, int c, string v)> Derive(IReadOnlyList<GridColumn> cols, Func<int, int, string> get,
        IEnumerable<(int r, int c, string before, string after)> changed, Func<string, Info?> lookup, DateTime today,
        Func<string, string?>? loLookup = null)
    {
        var res = new List<(int r, int c, string v)>();
        var byRow = changed.Where(x => x.c < cols.Count).GroupBy(x => x.r);
        var code = CodeCol(cols);
        var hasFx = cols.Any(c => c.Formula.Length > 0);
        var hasToday = cols.Any(c => c.Auto == Today);
        var hasXuatDay = cols.Any(c => c.Auto == NgayXuat);
        var loCol = cols.Any(c => c.Auto == Dt) ? LoCol(cols) : -1;
        var loAny = LoCol(cols);
        foreach (var g in byRow)
        {
            var r = g.Key;
            var cur = new Dictionary<int, string>();
            string Get(int c) => cur.TryGetValue(c, out var v) ? v : get(r, c);
            void Set(int c, string v) { if (Get(c) != v) { cur[c] = v; } }
            var userChanged = g.Select(x => x.c).ToHashSet();
            var before = g.ToDictionary(x => x.c, x => x.before);

            // 1. dòng không còn phần nhập tay → xóa ô tự động
            var userEmpty = Enumerable.Range(0, cols.Count).Where(c => !Derived(cols[c])).All(c => Get(c).Trim().Length == 0);
            if (userEmpty)
            {
                for (var c = 0; c < cols.Count; c++) if (Derived(cols[c]) && !userChanged.Contains(c)) Set(c, "");
                foreach (var (c, v) in cur) res.Add((r, c, v));
                continue;
            }

            // 2. đổi mã hàng → tự điền theo mã
            if (code >= 0 && before.TryGetValue(code, out var oldCode))
            {
                var newCode = Get(code).Trim();
                Info? ni = newCode.Length > 0 ? lookup(newCode) : null, oi = oldCode.Trim().Length > 0 ? lookup(oldCode.Trim()) : null;
                for (var c = 0; c < cols.Count; c++)
                {
                    if (c == code || userChanged.Contains(c)) continue;          // ô người dùng vừa dán cùng lúc → giữ
                    var explicitKind = cols[c].Auto is Ten or Size or Tl ? cols[c].Auto : "";
                    var kind = explicitKind.Length > 0 ? explicitKind : ImplicitAuto(cols[c]);
                    if (kind.Length == 0) continue;
                    string nv = Field(ni, kind), ov = Field(oi, kind), now = Get(c).Trim();
                    var wasAuto = now.Length == 0 || (ov.Length > 0 && now == ov);
                    if (nv.Length > 0) { if (explicitKind.Length > 0 || wasAuto) Set(c, nv); }
                    else if (now.Length > 0 && ov.Length > 0 && now == ov) Set(c, "");   // mã mới không tra được → bỏ giá trị của mã cũ
                }
            }

            // 2a. (1.116) gõ lô → chuẩn hóa ngày (22.05 → 22.05.2026, 22.05.25 → 22.05.2025)
            if (loAny >= 0 && userChanged.Contains(loAny))
            {
                var raw = Get(loAny); var norm = NormLo(raw, today);
                if (norm != raw) Set(loAny, norm);
            }

            // 2b. (1.115) đổi lô → Đặc tính = kết quả kháng sinh của lô (như phiếu xuất nội địa); lô không tra được → bỏ giá trị của lô cũ
            if (loCol >= 0 && loLookup is not null && before.TryGetValue(loCol, out var oldLo))
            {
                var newLo = Get(loCol).Trim();
                string nv = newLo.Length > 0 ? loLookup(newLo) ?? "" : "", ov = oldLo.Trim().Length > 0 ? loLookup(oldLo.Trim()) ?? "" : "";
                for (var c = 0; c < cols.Count; c++)
                {
                    if (cols[c].Auto != Dt || userChanged.Contains(c)) continue;
                    var now = Get(c).Trim();
                    if (nv.Length > 0) Set(c, nv);
                    else if (now.Length > 0 && (now == ov || newLo.Length == 0)) Set(c, "");
                }
            }

            // 3. ngày tạo dòng: dòng trước đó trống hoàn toàn (phần nhập tay)
            if (hasToday)
            {
                var wasEmpty = Enumerable.Range(0, cols.Count).Where(c => !Derived(cols[c]))
                    .All(c => (before.TryGetValue(c, out var b) ? b : Get(c)).Trim().Length == 0);
                if (wasEmpty)
                    for (var c = 0; c < cols.Count; c++)
                        if (cols[c].Auto == Today && !userChanged.Contains(c) && Get(c).Trim().Length == 0) Set(c, TodayText(today));
            }

            // 3b. (1.71) ngày xuất: vừa gõ số lượng xuất (ô trước đó trống) mà ô ngày xuất còn trống → hôm nay
            if (hasXuatDay)
            {
                var xs = XuatQtyCol(cols);
                if (xs >= 0 && userChanged.Contains(xs) && Get(xs).Trim().Length > 0 && (before.TryGetValue(xs, out var bx) ? bx : "").Trim().Length == 0)
                    for (var c = 0; c < cols.Count; c++)
                        if (cols[c].Auto == NgayXuat && !userChanged.Contains(c) && Get(c).Trim().Length == 0) Set(c, TodayText(today));
            }

            // 4. công thức
            if (hasFx) foreach (var (c, v) in Formulas(cols, Get)) if (!userChanged.Contains(c) || cols[c].Formula.Length > 0) Set(c, v);

            foreach (var (c, v) in cur) res.Add((r, c, v));
        }
        return res;
    }

    /// <summary>Tính các cột công thức của một dòng (2 lượt để công thức dùng cột công thức khác).</summary>
    public static List<(int c, string v)> Formulas(IReadOnlyList<GridColumn> cols, Func<int, string> get)
    {
        var vals = new Dictionary<int, string>();
        string Val(int c) => vals.TryGetValue(c, out var v) ? v : get(c);
        var row = new RowView(cols, Val);
        for (var pass = 0; pass < 2; pass++)
            for (var c = 0; c < cols.Count; c++)
                if (cols[c].Formula.Length > 0) vals[c] = CongThuc.Eval(cols[c].Formula, row);
        return vals.Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>Tính lại mọi cột công thức của cả bảng (khi lưu / đổi công thức). Dòng không có phần nhập tay → ô công thức trống.</summary>
    public static List<(int r, int c, string v)> RecomputeAll(IReadOnlyList<GridColumn> cols, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        var res = new List<(int, int, string)>();
        if (!cols.Any(c => c.Formula.Length > 0)) return res;
        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            string G(int c) => c < row.Count ? row[c] ?? "" : "";
            var empty = Enumerable.Range(0, cols.Count).Where(c => !Derived(cols[c])).All(c => G(c).Trim().Length == 0);
            foreach (var (c, v) in empty ? cols.Select((x, i) => (i, x)).Where(t => t.x.Formula.Length > 0).Select(t => (t.i, "")).ToList() : Formulas(cols, G))
                if (G(c) != v) res.Add((r, c, v));
        }
        return res;
    }

    private sealed class RowView : CongThuc.IRow
    {
        private readonly IReadOnlyList<GridColumn> _cols; private readonly Func<int, string> _get;
        public RowView(IReadOnlyList<GridColumn> cols, Func<int, string> get) { _cols = cols; _get = get; }
        public string? ByIndex(int col) => col >= 0 && col < _cols.Count ? _get(col) : null;
        public string? ByName(string header)
        {
            var f = GridFilter.Fold(header).Trim();
            for (var i = 0; i < _cols.Count; i++) if (GridFilter.Fold(_cols[i].Header).Trim() == f) return _get(i);
            var k = Squash(header);
            for (var i = 0; i < _cols.Count; i++) if (Squash(_cols[i].Header) == k) return _get(i);
            return null;
        }
        private static string Squash(string s) => new(GridFilter.Fold(s).Where(char.IsLetterOrDigit).ToArray());
    }
}
