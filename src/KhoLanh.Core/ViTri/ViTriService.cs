using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using KhoLanh.Core.DoiChieu;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.TongHop;

namespace KhoLanh.Core.ViTri;

/// <summary>
/// Module Tồn kho theo vị trí (C#, viết lại theo "Tồn Vị Trí An An" của app cũ — ton-vi-tri-an-an.js, vt-keys.js, vt-doi-chieu.js, vt-canh-bao.js).
/// Dùng CHUNG dữ liệu với app cũ (database "ton-vi-tri-an-an": ViTri = tồn hiện tại đã trừ phiếu, ViTriMeta, ViTriPhieuDienThoai = phiếu xuất).
///   • Nạp file "Tồn kho theo vị trí" (Mã hàng, Vị trí, SL Tồn, T.Lượng …) → đối chiếu phiếu đã trừ với file mới, phiếu HÔM NAY trừ lại vào file mới.
///   • Phiếu xuất: chọn dòng + SL xuất → lưu phiếu (trừ tồn ngay), loại phiếu (thay thùng / xuất xả), ngày, tiêu đề; gộp theo Size khi xem / xuất.
///   • Phiếu đã lưu: sửa, hủy trừ / trừ lại, xóa (hoàn tồn), gộp / tách phiếu, tách mã ra phiếu mới, mở khóa phiếu của đợt nạp cũ.
///   • Lưu trữ theo ngày (tự gộp phiếu cùng ngày + cùng loại), thống kê Size.
/// </summary>
public sealed partial class ViTriService
{
    public const string Db = "ton-vi-tri-an-an", SheetRows = "ViTri", SheetMeta = "ViTriMeta", SheetSlips = "ViTriPhieuDienThoai", PermKey = "m7";
    public const string NoPos = "(chưa có vị trí)";
    public static readonly string[] TemplateColumns = { "Item ID", "Mã hàng", "Tên hàng", "ĐVT", "Lô (Batch)", "Vị trí", "SL Tồn", "T.Lượng", "Net" };
    public static readonly (string Key, string Label, string Tag, string Title)[] Loai =
    {
        ("thaythung", "Phiếu thay thùng", "ThayThung", "PHIẾU THAY THÙNG"),
        ("xuatxa", "Phiếu xuất xả", "XuatXa", "PHIẾU XUẤT XẢ"),
    };
    public static string LoaiLabel(string? k) => Loai.FirstOrDefault(l => l.Key == k).Label ?? "";
    public const string TitleDefault = "PHIẾU XUẤT";

    // ------------------------------------------------------------------ dữ liệu

    public sealed class Row
    {
        public string MaGoc = "", Ma = "", Ten = "", Dvt = "", Lo = "", Vt = "", Dt = "", Hd = "";
        public double Sl, Tl;
        public bool Ghost;                       // dòng dựng tạm khi sửa phiếu mà file mới không còn dòng đó
        internal string Search = "", SearchNs = "", Tag = "";
        internal string[] Cells = Array.Empty<string>();
        public double Net => Sl * Tl;
        public Row Clone() => (Row)MemberwiseClone();
    }
    public sealed class Line
    {
        public string Key { get; set; } = "";
        public string MaGoc { get; set; } = "";
        public string Ten { get; set; } = "";
        public string Dvt { get; set; } = "";
        public string Lo { get; set; } = "";
        public string Vt { get; set; } = "";
        public double Tl { get; set; }
        public double Qty { get; set; }
        public bool? FileDa { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
        public Line Clone() => new() { Key = Key, MaGoc = MaGoc, Ten = Ten, Dvt = Dvt, Lo = Lo, Vt = Vt, Tl = Tl, Qty = Qty, FileDa = FileDa, Extra = Extra is null ? null : new(Extra) };
    }
    public sealed class ViewItem
    {
        public string Ma { get; set; } = "";
        public string Ma21 { get; set; } = "";
        public string Vt { get; set; } = "";
        public string Dt { get; set; } = "";
        public string Hd { get; set; } = "";
        public double Tl { get; set; }
        public double X { get; set; }
        public int Gop { get; set; } = 1;
        [JsonIgnore] public string Qc = "";
        [JsonIgnore] public List<int>? Idxs;
    }
    public sealed class SlipView { public bool Gop { get; set; } public List<ViewItem> Items { get; set; } = new(); }
    public sealed class Slip
    {
        public string Id { get; set; } = "";
        public string Source { get; set; } = "pc";
        public string MaPhieu { get; set; } = "";
        public string NguoiTao { get; set; } = "";
        public string GhiChu { get; set; } = "";
        public string Loai { get; set; } = "";
        public string CreatedAt { get; set; } = "";
        public List<Line> Lines { get; set; } = new();
        public bool Deducted { get; set; }
        public string? DeductedAt { get; set; }
        public double? LoadAt { get; set; }
        public SlipView? View { get; set; }
        public List<Slip>? MergedFrom { get; set; }
        public JsonElement? PhoneId { get; set; }
        public List<JsonElement>? PhoneIds { get; set; }
        public string? TieuDe { get; set; }
        public string? EditedAt { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; set; }
        [JsonIgnore] public double TongSl => Lines.Sum(l => l.Qty);
        [JsonIgnore] public bool FromPhone => Source != "pc";
        public Slip Clone() => JsonSerializer.Deserialize<Slip>(JsonSerializer.Serialize(this, Json), Json)!;
    }
    public sealed class Meta
    {
        public string FileName { get; set; } = "";
        public double? ImportedAt { get; set; }
        public string DataDate { get; set; } = "";
        public string Sheet { get; set; } = "";
        public int HeaderRowNumber { get; set; }
        public int SkippedRows { get; set; }
    }
    private sealed class SlipStore { public List<Slip> Slips { get; set; } = new(); public double Seq { get; set; } = 1; }

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public List<Row> Rows { get; private set; } = new();
    public Meta Info { get; private set; } = new();
    public List<Slip> Slips { get; private set; } = new();
    private double _seq = 1;
    public bool Loaded { get; private set; }
    public Recon? LastRecon { get; private set; }

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    private Dictionary<string, string> _size = new();
    private Dictionary<string, string> _dtGroup = new();
    private AnhXa _ax = AnhXa.Defaults();

    public ViTriService(Func<ISheetSource> source, Session session, AuditService audit) { _src = source; _session = session; _audit = audit; }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    private void RequireView() { if (!CanView) throw new BusinessException("Tài khoản không có quyền xem Tồn kho theo vị trí."); }
    private void RequireEdit() { if (!CanEdit) throw new BusinessException("Tài khoản không có quyền sửa Tồn kho theo vị trí (nạp file, lập / sửa phiếu xuất)."); }
    private string UserName => _session.CurrentUser is { } u ? (u.FullName.Length > 0 ? u.FullName : u.Username) : "PC";
    private void Log(string action, string detail) => _audit.Log(_session.Username, "Tồn theo vị trí: " + action, detail);

    // ------------------------------------------------------------------ đọc / lưu

    private JsonArray Read(string db, string sheet)
    {
        try { return JsonNode.Parse(_src().Read(db, sheet)) as JsonArray ?? new JsonArray(); } catch { return new JsonArray(); }
    }
    internal static string S(JsonNode? n)
    {
        var s = n switch
        {
            null => "",
            JsonValue v when v.TryGetValue<string>(out var x) => x,
            JsonValue v when v.TryGetValue<double>(out var d) => d.ToString("R", CultureInfo.InvariantCulture),
            JsonValue v when v.TryGetValue<bool>(out var b) => b ? "TRUE" : "FALSE",
            _ => n.ToJsonString(),
        };
        return s;
    }
    private static string? JsonCell(JsonArray a)
    {
        var c = a.Count > 0 && a[0] is JsonObject o ? S(o["data"]) : "";
        return c.StartsWith("__JSON__", StringComparison.Ordinal) ? c[8..] : null;
    }
    private static JsonArray JsonCellOf(object o) => new() { new JsonObject { ["data"] = "__JSON__" + JsonSerializer.Serialize(o, Json) } };

    public void Load()
    {
        RequireView();
        var meta = JsonCell(Read(Db, SheetMeta));
        Info = meta is null ? new Meta() : (Try(() => JsonSerializer.Deserialize<Meta>(meta, Json)) ?? new Meta());
        Rows = meta is null ? new() : Read(Db, SheetRows).OfType<JsonObject>().Select(FromStored).Where(r => r.MaGoc.Length > 0).ToList();
        var sj = JsonCell(Read(Db, SheetSlips));
        var st = sj is null ? null : Try(() => JsonSerializer.Deserialize<SlipStore>(sj, Json));
        Slips = st?.Slips ?? new(); _seq = st?.Seq ?? 1;
        ResetXuatGocLoad();   // bảng Excel kho gốc nạp khi tab Tra cứu cần
        LoadDictionaries();
        Loaded = true;
    }
    private static T? Try<T>(Func<T?> f) where T : class { try { return f(); } catch { return null; } }

    /// <summary>Từ điển Size (Mã hóa › 8. Size), Ánh xạ tên hàng (tiền tố gộp) — Tổng hợp; nhóm Đặc tính đã gộp — Tồn kho An An. Chỉ đọc.</summary>
    public void LoadDictionaries()
    {
        var dict = new Dictionary<string, string>();
        foreach (var e in TongHopService.FixedCatalog().GetValueOrDefault(8) ?? new()) dict[e.Code.Trim().ToUpperInvariant()] = e.Name.Trim();
        foreach (var r in Read(TongHopService.Db, "MaHoa").OfType<JsonObject>())
            if (S(r["Tổ hợp"]) == "8") { var c = S(r["Mã"]).Trim().ToUpperInvariant(); if (c.Length > 0) dict[c] = S(r["Tên"]).Trim(); }
        _size = dict;
        AnhXa? ax = null;
        try { ax = AnhXa.FromSheets(Read(TongHopService.Db, "AnhXaTen"), Read(TongHopService.Db, "AnhXaCauHinh")); } catch { ax = null; }
        _ax = ax ?? AnhXa.Defaults();
        var dt = new Dictionary<string, string>();
        foreach (var r in Read("ton-kho-an-an", "DacTinh").OfType<JsonObject>())
        {
            var l = S(r["Đặc Tính"]).Trim(); var g = S(r["Nhóm đã gộp"]).Trim();
            if (l.Length > 0 && g.Length > 0) dt[l.ToLowerInvariant()] = g;
        }
        _dtGroup = dt;
        foreach (var r in Rows) Index(r);
    }

    private static readonly (string F, string Col)[] StoreKeys =
        { ("maGoc", "Mã hàng"), ("ten", "Tên hàng"), ("dvt", "ĐVT"), ("lo", "Lô (Batch)"), ("vt", "Vị trí"), ("sl", "SL Tồn"), ("tl", "T.Lượng") };
    private Row FromStored(JsonObject o) => Build(S(o["Mã hàng"]), S(o["Tên hàng"]), S(o["ĐVT"]), S(o["Lô (Batch)"]), S(o["Vị trí"]), Num(o["SL Tồn"]), Num(o["T.Lượng"]));
    private static JsonObject ToStored(Row r) => new()
    {
        ["Mã hàng"] = r.MaGoc, ["Tên hàng"] = r.Ten, ["ĐVT"] = r.Dvt, ["Lô (Batch)"] = r.Lo, ["Vị trí"] = r.Vt, ["SL Tồn"] = Round(r.Sl), ["T.Lượng"] = r.Tl,
    };
    private static double Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? (double.IsFinite(d) ? d : 0) : (DcxEngine.ParseNum(S(n)) ?? 0);
    public static double Round(double v) => Math.Round(v * 1e6, MidpointRounding.AwayFromZero) / 1e6;

    /// <summary>Lưu tồn + phiếu (+ thông tin file) trong 1 lần ghi. Máy khác vừa sửa → SheetConflictException (giao diện tải lại).</summary>
    private void Persist(bool meta = false)
    {
        InvalidateDaXuat();
        var sheets = new Dictionary<string, JsonArray?>
        {
            [SheetRows] = new JsonArray(Rows.Where(r => !r.Ghost || r.Sl > 1e-9).Select(r => (JsonNode)ToStored(r)).ToArray()),
            [SheetSlips] = JsonCellOf(new SlipStore { Slips = Slips, Seq = _seq }),
        };
        if (meta) sheets[SheetMeta] = Rows.Count > 0 ? JsonCellOf(Info) : new JsonArray();
        _src().Write(Db, sheets);
    }

    // ------------------------------------------------------------------ dựng dòng / tìm kiếm / size

    /// <summary>Mã 21 ký tự: bỏ "TPC-"; tiền tố khác dạng "XXX-" mà còn dài hơn 21 ký tự → lấy phần sau "-" cuối.</summary>
    public static string ShortCode(string? ma)
    {
        var s = Regex.Replace((ma ?? "").Trim(), "^TPC-", "", RegexOptions.IgnoreCase);
        if (s.Length > 21 && s.Contains('-')) s = s[(s.LastIndexOf('-') + 1)..];
        return s;
    }
    /// <summary>Tên "… (quy cách) - Đặc tính - Hợp đồng" → (Đặc tính, Hợp đồng): 2 phần cuối khi tách theo " - ".</summary>
    public static (string Dt, string Hd) SplitName(string? ten)
    {
        var p = (ten ?? "").Split(" - ");
        return p.Length >= 3 ? (p[^2].Trim(), p[^1].Trim()) : ("", "");
    }
    public Row Build(string maGoc, string ten, string dvt, string lo, string vt, double sl, double tl)
    {
        var (dt, hd) = SplitName(ten);
        var r = new Row { MaGoc = maGoc.Trim(), Ma = ShortCode(maGoc), Ten = ten.Trim(), Dvt = dvt.Trim(), Lo = lo.Trim(), Vt = vt.Trim(), Sl = sl, Tl = tl, Dt = dt, Hd = hd };
        Index(r);
        return r;
    }
    private Row RowOfLine(Line l) => Build(l.MaGoc, l.Ten, l.Dvt, l.Lo, l.Vt, l.Qty, l.Tl);
    private void Index(Row r)
    {
        var g = GopLabel(r);
        r.Search = Norm(string.Join(" ", r.Ma, r.MaGoc, r.Vt, r.Dt, r.Hd, r.Lo, r.Ten, SizeOf(r), g ?? ""));
        r.SearchNs = r.Search.Replace(" ", "");
        r.Tag = g is null ? "" : Norm(g).Replace(" ", "");
        r.Cells = new[] { r.Ma, r.MaGoc, r.Ten, r.Vt, r.Lo, SizeOf(r), r.Dt, r.Hd }.Select(Norm).ToArray();
    }
    public static string Norm(string? s) => Regex.Replace(TongHopService.NormSearch(s), @"\s+", " ").Trim();

    private List<Row>? _tagRows; private int _tagCount = -1;
    private HashSet<string> _tags = new(), _prefixes = new();
    /// <summary>
    /// Ô tìm "thông minh" — CÙNG quy tắc app cũ (js/smart-search.js, dùng ở Tồn theo vị trí / Dữ Liệu / Tìm trong toàn kho / điện thoại):
    /// bỏ dấu, không tính khoảng trắng; mỗi từ tìm riêng ở mọi cột (VÀ);
    /// phần đầu là ĐÚNG 1 nhãn Size ("BTP 50 MKS") → chỉ dòng có đúng nhãn đó (không lẫn 41/50, BTP 500…), phần sau lọc tiếp;
    /// cả ô là dạng nhãn ("BTP 13") → nhãn BẮT ĐẦU bằng BTP13, có dấu cách cuối ("BTP 13 ") → đúng nhãn;
    /// ô chỉ có 1 con số ("50") → ô nào ĐÚNG BẰNG số đó. Ô trống → null (không lọc).
    /// </summary>
    public Func<Row, bool>? Matcher(string? raw)
    {
        raw ??= "";
        var qs = Norm(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (qs.Count == 0) return null;
        if (_tagRows != Rows || _tagCount != Rows.Count)
        {
            _tags = new(); _prefixes = new();
            foreach (var r in Rows)
                if (r.Tag.Length > 0) { _tags.Add(r.Tag); var m = Regex.Match(r.Tag, "^[a-z]+"); if (m.Success) _prefixes.Add(m.Value); }
            _tagRows = Rows; _tagCount = Rows.Count;
        }
        if (qs.Count == 1 && Regex.IsMatch(raw.Trim(), @"^\d+([.,]\d+)?$"))
        {
            var q = qs[0];
            return r => r.Cells.Contains(q);
        }
        for (var k = qs.Count - 1; k >= 1; k--)
        {
            var tag = string.Concat(qs.Take(k)); if (!_tags.Contains(tag)) continue;
            var rest = qs.Skip(k).ToList();
            return r => r.Tag == tag && WordsHit(r, rest);
        }
        var all = string.Concat(qs); var pm = Regex.Match(all, @"^([a-z]+)\d");
        if (pm.Success && _prefixes.Contains(pm.Groups[1].Value))
            return Regex.IsMatch(raw, @"\s$") ? r => r.Tag == all : r => r.Tag.Length > 0 && r.Tag.StartsWith(all, StringComparison.Ordinal);
        return r => WordsHit(r, qs);
    }
    private static bool WordsHit(Row r, List<string> qs)
    {
        if (qs.Count == 0 || qs.All(w => r.Search.Contains(w, StringComparison.Ordinal))) return true;
        return r.SearchNs.Contains(string.Concat(qs), StringComparison.Ordinal);
    }

    /// <summary>Size của dòng: ký tự 14–17 tra từ điển Size; chưa khai báo → "NN/NNN" trong tên hàng; không có → "".</summary>
    public string SizeOf(Row r)
    {
        var ma = r.Ma.ToUpperInvariant(); if (ma.Length < 17) return "";
        var code = ma.Substring(13, 4); if (code == "XXXX") return "";
        if (_size.TryGetValue(code, out var nm)) return Regex.Replace(nm.Trim(), @"^size\s+", "", RegexOptions.IgnoreCase).Trim();
        var m = Regex.Match(r.Ten, @"\d{1,3}\s*\/\s*\d{1,3}");
        return m.Success ? Regex.Replace(m.Value, @"\s+", "") : "";
    }
    /// <summary>Nhãn gộp "&lt;tiền tố&gt; &lt;size&gt;" (tiền tố theo Ánh xạ tên hàng, VD "BTP 50"); không quy ra được → null.</summary>
    public string? GopLabel(Row r)
    {
        var pre = ""; try { pre = _ax.ShortPrefix(r.Ma.ToUpperInvariant()); } catch { pre = ""; }
        if (string.IsNullOrWhiteSpace(pre)) return null;
        var size = SizeOf(r); if (size.Length == 0) return null;
        return pre + " " + size;
    }
    /// <summary>Quy cách đóng gói ngắn: "…(1.8 kg x 6 gói)…" → "1.8KG".</summary>
    public static string QcOf(string? ten)
    {
        var head = (ten ?? "").Split(" - ")[0];
        var groups = Regex.Matches(head, @"\(([^()]*)\)").Select(m => m.Groups[1].Value).ToList();
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var m = Regex.Match(groups[i], @"(\d+(?:[.,]\d+)?)\s*(kg|gr|gram|g|lbs|lb)\b", RegexOptions.IgnoreCase);
            if (m.Success) { var u = m.Groups[2].Value.ToLowerInvariant(); return m.Groups[1].Value.Replace(',', '.') + (u is "gr" or "gram" ? "G" : u.ToUpperInvariant()); }
        }
        return "";
    }
    /// <summary>Khu của vị trí = phần trước dấu chấm / gạch đầu tiên ("A.01.02" → "A").</summary>
    public static string ZoneOf(string? vt)
    {
        var s = (vt ?? "").Trim().ToUpperInvariant(); if (s.Length == 0) return "";
        var p = Regex.Split(s, @"[.\-\/\s]+"); return p[0];
    }
    /// <summary>So vị trí theo từng đoạn (số theo số) — đúng thứ tự đi lấy hàng.</summary>
    public static int CmpViTri(string? a, string? b)
    {
        var sa = Regex.Split((a ?? "").Trim().ToUpperInvariant(), @"[.\-\/\s]+"); var sb = Regex.Split((b ?? "").Trim().ToUpperInvariant(), @"[.\-\/\s]+");
        var n = Math.Max(sa.Length, sb.Length);
        for (var i = 0; i < n; i++)
        {
            if (i >= sa.Length) return -1; if (i >= sb.Length) return 1;
            string x = sa[i], y = sb[i];
            bool nx = Regex.IsMatch(x, @"^\d+$"), ny = Regex.IsMatch(y, @"^\d+$");
            if (nx && ny) { var d = long.Parse(x).CompareTo(long.Parse(y)); if (d != 0) return d; }
            else if (nx != ny) return nx ? -1 : 1;
            else { var c = string.Compare(x, y, StringComparison.Ordinal); if (c != 0) return c; }
        }
        return 0;
    }

    // ------------------------------------------------------------------ khóa dòng (vt-keys.js)

    /// <summary>Khóa dòng = Mã hàng|Lô|Vị trí#thứ tự nếu trùng (giống app cũ, dùng chung với phiếu đã lưu).</summary>
    public static List<string> RowKeys(IEnumerable<Row> rows)
    {
        var seen = new Dictionary<string, int>(); var o = new List<string>();
        foreach (var r in rows)
        {
            var ma = r.MaGoc.Trim(); if (ma.Length == 0) { o.Add(""); continue; }
            var b = ma + "|" + r.Lo.Trim() + "|" + r.Vt.Trim(); var n = seen.GetValueOrDefault(b); seen[b] = n + 1;
            o.Add(b + "#" + n);
        }
        return o;
    }
    public static string BaseOf(string? k) => Regex.Replace(k ?? "", @"#\d+$", "");
    /// <summary>Khóa phiếu mất (nạp file mới, dòng trùng đổi chỗ) → khớp lại theo Mã|Lô|Vị trí với khóa chưa dùng.</summary>
    public static (int Moved, int Missing) Relink(List<Line> lines, List<string> keys)
    {
        var have = keys.Where(k => k.Length > 0).ToHashSet(); var used = new HashSet<string>();
        foreach (var l in lines) if (have.Contains(l.Key)) used.Add(l.Key);
        var byBase = new Dictionary<string, List<string>>();
        foreach (var k in keys) { if (k.Length == 0) continue; var b = BaseOf(k); if (!byBase.TryGetValue(b, out var x)) byBase[b] = x = new(); x.Add(k); }
        int moved = 0, missing = 0;
        foreach (var l in lines)
        {
            if (have.Contains(l.Key)) continue;
            var c = byBase.GetValueOrDefault(BaseOf(l.Key))?.FirstOrDefault(k => !used.Contains(k));
            if (c is not null) { l.Key = c; used.Add(c); moved++; } else missing++;
        }
        return (moved, missing);
    }
    private Dictionary<string, int> KeyIndex()
    {
        var keys = RowKeys(Rows); var d = new Dictionary<string, int>();
        for (var i = 0; i < keys.Count; i++) if (keys[i].Length > 0) d[keys[i]] = i;
        return d;
    }

    // ------------------------------------------------------------------ phiếu: hiệu lực / hôm nay

    public double ImportedAt => Info.ImportedAt ?? 0;
    public static DateTime? Local(string? iso) => DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.LocalDateTime : null;
    public static string Iso(DateTime local) => local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    public static double Ms(DateTime utc) => Math.Round((utc - DateTime.UnixEpoch).TotalMilliseconds);
    public static bool IsToday(Slip s, DateTime? now = null) => Local(s.CreatedAt) is { } d && d.Date == (now ?? DateTime.Now).Date;
    public static string DayKey(Slip s) => Local(s.CreatedAt) is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
    public static string Dmy(string ymd) => Regex.Match(ymd ?? "", @"^(\d{4})-(\d{2})-(\d{2})") is { Success: true } m ? $"{m.Groups[3]}/{m.Groups[2]}/{m.Groups[1]}" : ymd ?? "";
    /// <summary>Đợt nạp của phiếu (loadAt). Phiếu cũ chưa có: tạo trước lần nạp hiện tại = đợt cũ (khóa).</summary>
    public double Batch(Slip s)
    {
        if (s.LoadAt is double v) return v;
        var cur = ImportedAt; if (cur == 0) return (s.LoadAt = 0).Value;
        if (s.PhoneId is not null || Local(s.CreatedAt) is not { } t) return (s.LoadAt = cur).Value;
        return (s.LoadAt = Ms(t.ToUniversalTime()) >= cur ? cur : 1).Value;
    }
    /// <summary>Phiếu còn hiệu lực (thuộc đợt nạp hiện tại). Nạp file mới → phiếu đợt cũ bị KHÓA.</summary>
    public bool Active(Slip s) => Batch(s) == ImportedAt;
    public bool Mergeable(Slip s) => s.Deducted && Active(s);
    public static string Code(Slip s) => s.Id;
    public string NewId(string prefix, DateTime? when = null)
    {
        var d = when ?? DateTime.Now; var pre = prefix + "VT-" + d.ToString("ddMMyy", CultureInfo.InvariantCulture);
        var mx = 0;
        foreach (var x in Slips) foreach (var c in new[] { x.Id, x.MaPhieu })
            if (c is not null && c.StartsWith(pre, StringComparison.Ordinal) && int.TryParse(c[pre.Length..], out var n) && n > mx) mx = n;
        return pre + (mx + 1).ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>Trừ (sign -1) / hoàn (+1) tồn cho các dòng của phiếu. Trả số dòng không còn trong tồn hiện tại.</summary>
    private int Apply(Slip s, int sign, HashSet<string>? skipBases = null)
    {
        var idx = KeyIndex(); var missing = 0;
        foreach (var l in s.Lines)
        {
            if (!idx.TryGetValue(l.Key, out var i)) { missing++; continue; }
            if (skipBases is not null && sign < 0 && skipBases.Contains(BaseOf(l.Key))) { l.FileDa = true; _keepSkipped++; continue; }
            if (skipBases is not null) l.FileDa = null;
            Rows[i].Sl = Round(Rows[i].Sl + sign * l.Qty);
        }
        return missing;
    }
    private int _keepSkipped;

    /// <summary>Số đã làm phiếu (phiếu đang trừ, còn hiệu lực) theo từng dòng tồn.</summary>
    public Dictionary<int, double> DoneMap()
    {
        var byKey = new Dictionary<string, double>();
        foreach (var s in Slips) if (s.Deducted && Active(s)) foreach (var l in s.Lines) byKey[l.Key] = byKey.GetValueOrDefault(l.Key) + l.Qty;
        var m = new Dictionary<int, double>(); if (byKey.Count == 0) return m;
        var keys = RowKeys(Rows);
        for (var i = 0; i < keys.Count; i++) if (keys[i].Length > 0 && byKey.TryGetValue(keys[i], out var q)) m[i] = q;
        return m;
    }

    // ------------------------------------------------------------------ nạp file vị trí

    public sealed class FileData { public string FileName = "", Sheet = ""; public int HeaderRow; public List<Row> Rows = new(); public List<string> Headers = new(); public string Sha = ""; }   // Headers / Sha: 1.73 (mẫu file, MauFile)
    private static string HeaderKey(string? h) => Regex.Replace(TongHopService.NormSearch(h), "[^a-z0-9]", "");
    private static readonly (string F, string[] Alias)[] Aliases =
    {
        ("maGoc", new[] { "mahang", "mahh", "masanpham", "itemcode" }), ("ten", new[] { "tenhang", "tenhh", "tensanpham", "itemname" }),
        ("dvt", new[] { "dvt", "donvitinh", "donvi" }), ("lo", new[] { "lobatch", "lo", "batch", "solo" }),
        ("vt", new[] { "vitri", "vitrikho", "location" }), ("sl", new[] { "slton", "soluongton", "soluong", "sl" }), ("tl", new[] { "tluong", "trongluong", "tl" }),
    };
    private static readonly (string F, string Label)[] Required = { ("maGoc", "Mã hàng"), ("vt", "Vị trí"), ("sl", "SL Tồn"), ("tl", "T.Lượng") };

    /// <summary>Đọc file Excel "Tồn kho theo vị trí": tìm dòng tiêu đề trong 30 dòng đầu của từng sheet (không phân biệt hoa / thường, dấu).</summary>
    public FileData ReadFile(string path)
    {
        RequireEdit();
        using var _perf = KhoLanh.Core.TroLy.Perf.Measure("Đọc file Excel", "Tồn theo vị trí (chi tiết)");
        // 1.73 (N4): đọc nhanh thẳng gói xlsx; file lạ / lỗi → cách cũ (ClosedXML)
        if (Path.GetExtension(path).ToLowerInvariant() is ".xlsx" or ".xlsm")
        {
            KhoLanh.Core.TonKho.SheetText.Book? book = null;
            try { book = KhoLanh.Core.TonKho.SheetText.ReadXlsxFast(path, rawNumbers: true, fromA1: true); } catch { book = null; }
            if (book is not null) return ReadFileFrom(book, path);
        }
        return ReadFileClosedXml(path);
    }

    /// <summary>Cùng luật với <see cref="ReadFileClosedXml"/> nhưng trên bảng chữ đã đọc (số gốc, bắt đầu từ A1).</summary>
    public FileData ReadFileFrom(KhoLanh.Core.TonKho.SheetText.Book book, string path)
    {
        (string sheet, List<string[]> t, int r, Dictionary<string, int> map, int score)? best = null;
        foreach (var name in book.SheetNames)
        {
            var t = book.Sheets[name]; if (t.Count == 0) continue;
            var lastCol = t.Max(x => x.Length);
            for (var r = 1; r <= Math.Min(t.Count, 30); r++)
            {
                var row = t[r - 1];
                var keys = Enumerable.Range(1, lastCol).Select(c => HeaderKey(c - 1 < row.Length ? row[c - 1] : "")).ToList();
                var map = new Dictionary<string, int>();
                foreach (var (f, al) in Aliases) { var i = keys.FindIndex(k => al.Contains(k)); if (i >= 0) map[f] = i + 1; }
                var score = Required.Count(x => map.ContainsKey(x.F));
                if (best is null || score > best.Value.score) best = (name, t, r, map, score);
                if (score == 4) break;
            }
            if (best?.score == 4) break;
        }
        if (best is not { score: 4 } b)
        {
            var miss = Required.Where(x => best is null || !best.Value.map.ContainsKey(x.F)).Select(x => x.Label);
            throw new BusinessException($"File “{Path.GetFileName(path)}” không giống báo cáo “Tồn kho theo vị trí”.\n\nKhông tìm thấy cột: {string.Join(", ", miss)}.\nCần tối thiểu các cột: Mã hàng, Vị trí, SL Tồn, T.Lượng.");
        }
        var outp = new FileData { FileName = Path.GetFileName(path), Sheet = b.sheet, HeaderRow = b.r };
        outp.Headers = b.t[b.r - 1].Where(x => !string.IsNullOrEmpty(x)).Select(x => x.Trim()).ToList();   // 1.73: mẫu file (MauFile), như CellsUsed
        string Cell(int r, int c) => r - 1 < b.t.Count && c - 1 < b.t[r - 1].Length ? b.t[r - 1][c - 1] ?? "" : "";
        string Txt(int r, string f) => b.map.TryGetValue(f, out var c) ? Cell(r, c).Trim() : "";
        double NumAt(int r, string f)
        {
            if (!b.map.TryGetValue(f, out var c)) return 0; var s = Cell(r, c);
            if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d)) return d;
            return DcxEngine.ParseNum(s) ?? 0;
        }
        for (var r = b.r + 1; r <= b.t.Count; r++)
        {
            var ma = Txt(r, "maGoc"); if (ma.Length == 0) continue;
            outp.Rows.Add(Build(ma, Txt(r, "ten"), Txt(r, "dvt"), Txt(r, "lo"), Txt(r, "vt"), NumAt(r, "sl"), NumAt(r, "tl")));
        }
        if (outp.Rows.Count == 0) throw new BusinessException($"File “{outp.FileName}” không có dòng dữ liệu nào (cột Mã hàng trống).");
        return outp;
    }

    public FileData ReadFileClosedXml(string path)
    {
        XLWorkbook wb;
        try { wb = new XLWorkbook(new MemoryStream(File.ReadAllBytes(path))); }
        catch (Exception e) { throw new BusinessException($"Không đọc được file vị trí (cần file Excel .xlsx).\n\n{e.Message}"); }
        using (wb)
        {
            (IXLWorksheet ws, int r, Dictionary<string, int> map, int score)? best = null;
            foreach (var ws in wb.Worksheets)
            {
                var used = ws.RangeUsed(); if (used is null) continue;
                var lastCol = used.LastColumn().ColumnNumber();
                for (var r = 1; r <= Math.Min(used.LastRow().RowNumber(), 30); r++)
                {
                    var keys = Enumerable.Range(1, lastCol).Select(c => HeaderKey(ws.Cell(r, c).GetFormattedString())).ToList();
                    var map = new Dictionary<string, int>();
                    foreach (var (f, al) in Aliases) { var i = keys.FindIndex(k => al.Contains(k)); if (i >= 0) map[f] = i + 1; }
                    var score = Required.Count(x => map.ContainsKey(x.F));
                    if (best is null || score > best.Value.score) best = (ws, r, map, score);
                    if (score == 4) break;
                }
                if (best?.score == 4) break;
            }
            if (best is not { score: 4 } b)
            {
                var miss = Required.Where(x => best is null || !best.Value.map.ContainsKey(x.F)).Select(x => x.Label);
                throw new BusinessException($"File “{Path.GetFileName(path)}” không giống báo cáo “Tồn kho theo vị trí”.\n\nKhông tìm thấy cột: {string.Join(", ", miss)}.\nCần tối thiểu các cột: Mã hàng, Vị trí, SL Tồn, T.Lượng.");
            }
            var outp = new FileData { FileName = Path.GetFileName(path), Sheet = b.ws.Name, HeaderRow = b.r };
            outp.Headers = b.ws.Row(b.r).CellsUsed().Select(c => c.GetFormattedString().Trim()).ToList();   // 1.73: mẫu file (MauFile)
            string Txt(int r, string f) => b.map.TryGetValue(f, out var c) ? b.ws.Cell(r, c).GetFormattedString().Trim() : "";
            double NumAt(int r, string f)
            {
                if (!b.map.TryGetValue(f, out var c)) return 0; var cell = b.ws.Cell(r, c);
                if (cell.DataType == XLDataType.Number) return cell.GetDouble();
                return DcxEngine.ParseNum(cell.GetFormattedString()) ?? 0;
            }
            var last = b.ws.RangeUsed()!.LastRow().RowNumber();
            for (var r = b.r + 1; r <= last; r++)
            {
                var ma = Txt(r, "maGoc"); if (ma.Length == 0) continue;
                outp.Rows.Add(Build(ma, Txt(r, "ten"), Txt(r, "dvt"), Txt(r, "lo"), Txt(r, "vt"), NumAt(r, "sl"), NumAt(r, "tl")));
            }
            if (outp.Rows.Count == 0) throw new BusinessException($"File “{outp.FileName}” không có dòng dữ liệu nào (cột Mã hàng trống).");
            return outp;
        }
    }

    public sealed record ImportResult(int Rows, int KeepSlips, int KeepMissing, int KeepSkipped, Recon? Recon);
    /// <summary>Thay tồn bằng file mới: đối chiếu phiếu đợt cũ với file mới; phiếu HÔM NAY đang trừ được trừ lại vào file mới (dòng file đã giảm đúng số phiếu thì không trừ lại).</summary>
    public ImportResult Import(FileData f)
    {
        RequireEdit();
        var recon = Reconcile(Rows, f.Rows, Slips.Where(s => s.Deducted && Active(s)).ToList(), Info.FileName, f.FileName);
        var khop = recon?.Items.Where(i => i.Status == "khop").Select(i => i.Base).ToHashSet() ?? new HashSet<string>();
        Rows = f.Rows;
        var now = Ms(DateTime.UtcNow);
        Info = new Meta { FileName = f.FileName, ImportedAt = now, DataDate = GuessDate(f.FileName), Sheet = f.Sheet, HeaderRowNumber = f.HeaderRow, SkippedRows = f.HeaderRow - 1 };
        _keepSkipped = 0; int keepN = 0, keepMiss = 0;
        foreach (var s in Slips)
        {
            if (!s.Deducted || !IsToday(s)) continue;
            Relink(s.Lines, RowKeys(Rows));
            keepMiss += Apply(s, -1, khop); s.LoadAt = now; keepN++;
        }
        LastRecon = recon;
        Persist(meta: true);
        KhoLanh.Core.TonKho.MauFile.Luu(_src(), Db, KhoLanh.Core.TonKho.MauFile.Of(f, UserName));   // 1.73: nhớ mẫu file
        Log("nạp file", $"{f.FileName}: {f.Rows.Count} dòng" + (keepN > 0 ? $", trừ lại {keepN} phiếu hôm nay" : ""));
        return new ImportResult(f.Rows.Count, keepN, keepMiss, _keepSkipped, recon);
    }
    public void SetDataDate(string text)
    {
        RequireEdit();
        Info.DataDate = text.Trim(); Persist(meta: true);
    }
    public void Clear()
    {
        RequireEdit();
        Rows = new(); Info = new Meta(); Persist(meta: true);
        Log("xóa dữ liệu vị trí", "");
    }
    /// <summary>"TON 23.9.xlsx" → "23/09/2026" (năm thiếu → năm nay; ra ngày tương lai quá 7 ngày → năm trước).</summary>
    public static string GuessDate(string? name)
    {
        var b = Regex.Replace(name ?? "", @"\.[^.]+$", "");
        static int Year(int d, int m, string y)
        {
            if (y.Length > 0) { var v = int.Parse(y); return v < 100 ? 2000 + v : v; }
            var now = DateTime.Now; try { return new DateTime(now.Year, m, d) - now > TimeSpan.FromDays(7) ? now.Year - 1 : now.Year; } catch { return now.Year; }
        }
        static bool Valid(int d, int m, int y) { try { _ = new DateTime(y, m, d); return true; } catch { return false; } }
        foreach (Match m in Regex.Matches(b, @"(?:^|\D)(\d{1,2})[._\-\/ ](\d{1,2})(?:[._\-\/ ](\d{4}|\d{2}))?(?!\d)"))
        {
            int d = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value); if (d is < 1 or > 31 || mo is < 1 or > 12) continue;
            var y = Year(d, mo, m.Groups[3].Value); if (Valid(d, mo, y)) return $"{d:00}/{mo:00}/{y}";
        }
        var m8 = Regex.Match(b, @"(?:^|\D)(\d{2})(\d{2})(\d{4})(?!\d)");
        if (m8.Success && Valid(int.Parse(m8.Groups[1].Value), int.Parse(m8.Groups[2].Value), int.Parse(m8.Groups[3].Value))) return $"{m8.Groups[1].Value}/{m8.Groups[2].Value}/{m8.Groups[3].Value}";
        return "";
    }

    // ------------------------------------------------------------------ đối chiếu phiếu với file mới (vt-doi-chieu.js)

    public sealed record ReconItem(string Base, string Ma, string Lo, string Vt, List<string> Phieu, double SlipQty, double? OldNet, double OldBase, double NewQty, bool InNew, string Status, double Lech);
    public sealed record Move(string Ma, string Lo, List<string> Tu, List<string> Den, double SlCu, double SlMoi);
    public sealed class Recon { public string OldFile = "", NewFile = ""; public List<ReconItem> Items = new(); public List<Move> Moves = new(); public int Khop, ChuaGiam, Khac; }

    public static Recon? Reconcile(List<Row> oldRows, List<Row> newRows, List<Slip> slips, string oldFile, string newFile)
    {
        if (oldRows.Count == 0) return null;
        static Dictionary<string, double> Sum(List<Row> rows)
        {
            var m = new Dictionary<string, double>();
            foreach (var r in rows) { if (r.MaGoc.Trim().Length == 0) continue; var b = r.MaGoc.Trim() + "|" + r.Lo.Trim() + "|" + r.Vt.Trim(); m[b] = m.GetValueOrDefault(b) + r.Sl; }
            return m;
        }
        var moves = DoiViTri(oldRows, newRows);
        if (slips.Count == 0 && moves.Count == 0) return null;
        var oldM = Sum(oldRows); var newM = Sum(newRows);
        var per = new Dictionary<string, (double qty, List<string> ids)>();
        foreach (var s in slips) foreach (var l in s.Lines)
        {
            var b = BaseOf(l.Key); if (b.Length == 0 || l.Qty == 0) continue;
            (double qty, List<string> ids) e = per.TryGetValue(b, out var x) ? x : (0d, new List<string>());
            e.qty += l.Qty; var id = s.MaPhieu.Length > 0 ? s.MaPhieu : s.Id; if (id.Length > 0 && !e.ids.Contains(id)) e.ids.Add(id);
            per[b] = e;
        }
        static bool Near(double a, double b) => Math.Abs(a - b) <= 0.0005;
        var rc = new Recon { OldFile = oldFile, NewFile = newFile, Moves = moves };
        foreach (var (b, e) in per)
        {
            var p = b.Split('|'); double? oldNet = oldM.TryGetValue(b, out var on) ? on : null;
            var inNew = newM.TryGetValue(b, out var nw); var oldBase = (oldNet ?? 0) + e.qty;
            var st = oldNet is null ? "khac" : Near(nw, oldNet.Value) ? "khop" : Near(nw, oldBase) ? "chuagiam" : "khac";
            rc.Items.Add(new ReconItem(b, p.ElementAtOrDefault(0) ?? "", p.ElementAtOrDefault(1) ?? "", p.ElementAtOrDefault(2) ?? "", e.ids, e.qty, oldNet, oldBase, nw, inNew, st, nw - (oldNet ?? oldBase)));
        }
        var order = new Dictionary<string, int> { ["khac"] = 0, ["chuagiam"] = 1, ["khop"] = 2 };
        rc.Items = rc.Items.OrderBy(i => order[i.Status]).ThenBy(i => i.Ma, StringComparer.Ordinal).ThenBy(i => i.Vt, StringComparer.Ordinal).ToList();
        rc.Khop = rc.Items.Count(i => i.Status == "khop"); rc.ChuaGiam = rc.Items.Count(i => i.Status == "chuagiam"); rc.Khac = rc.Items.Count(i => i.Status == "khac");
        return rc;
    }
    /// <summary>Hàng đổi vị trí giữa 2 lần nạp: cùng Mã + Lô, vừa có ô bỏ vừa có ô mới.</summary>
    public static List<Move> DoiViTri(List<Row> oldRows, List<Row> newRows)
    {
        static Dictionary<string, (string ma, string lo, Dictionary<string, double> vt)> G(List<Row> rows)
        {
            var m = new Dictionary<string, (string, string, Dictionary<string, double>)>();
            foreach (var r in rows)
            {
                var ma = r.MaGoc.Trim(); if (ma.Length == 0 || !(r.Sl > 0)) continue;
                var k = ma + "|" + r.Lo.Trim();
                if (!m.TryGetValue(k, out var e)) m[k] = e = (ma, r.Lo.Trim(), new Dictionary<string, double>());
                e.Item3[r.Vt.Trim()] = e.Item3.GetValueOrDefault(r.Vt.Trim()) + r.Sl;
            }
            return m;
        }
        var A = G(oldRows); var B = G(newRows); var o = new List<Move>();
        foreach (var (k, a) in A)
        {
            if (!B.TryGetValue(k, out var b)) continue;
            var tu = a.vt.Keys.Where(v => !b.vt.ContainsKey(v)).ToList(); var den = b.vt.Keys.Where(v => !a.vt.ContainsKey(v)).ToList();
            if (tu.Count == 0 || den.Count == 0) continue;
            o.Add(new Move(a.ma, a.lo, tu, den, a.vt.Values.Sum(), b.vt.Values.Sum()));
        }
        return o.OrderBy(x => x.Ma, StringComparer.Ordinal).ThenBy(x => x.Lo, StringComparer.Ordinal).ToList();
    }

    // ------------------------------------------------------------------ lập phiếu

    public sealed record Pick(int Index, double Qty);
    /// <summary>Dòng sẽ xuất (sắp theo Vị trí rồi Mã — đúng thứ tự đi lấy hàng).</summary>
    public List<(Row R, double X, int I)> Items(IEnumerable<Pick> picks) =>
        picks.Where(p => p.Index >= 0 && p.Index < Rows.Count && p.Qty > 0).Select(p => (Rows[p.Index], p.Qty, p.Index))
            .OrderBy(t => t.Item1, Comparer<Row>.Create((a, b) => CmpViTri(a.Vt, b.Vt) is var c && c != 0 ? c : string.Compare(a.Ma, b.Ma, StringComparison.Ordinal))).ToList();

    /// <summary>Bản xem / in của phiếu: gộp theo Size (trùng Vị trí + "tiền tố size" cộng lại) hoặc chỉ gộp dòng trùng Mã + Vị trí + Đặc tính + Hợp đồng.</summary>
    public List<ViewItem> BuildView(List<(Row R, double X, int I)> items, bool gop)
    {
        var list = gop ? GopItems(items) : MergeSame(items);
        return list.OrderBy(v => v, Comparer<ViewItem>.Create((a, b) => CmpViTri(a.Vt, b.Vt))).ToList();
    }
    private static string N(string? v) => Regex.Replace(v ?? "", @"\s+", " ").Trim().ToUpperInvariant();
    private List<ViewItem> MergeSame(List<(Row R, double X, int I)> items)
    {
        var groups = new Dictionary<string, List<(Row R, double X, int I)>>(); var order = new List<string>();
        foreach (var it in items)
        {
            var k = string.Join("\u0001", N(it.R.Ma), N(it.R.Vt), N(it.R.Dt), N(it.R.Hd));
            if (!groups.TryGetValue(k, out var g)) { groups[k] = g = new(); order.Add(k); }
            g.Add(it);
        }
        return order.Select(k =>
        {
            var g = groups[k]; var f = g[0].R; var sx = g.Sum(x => x.X); var sk = g.Sum(x => x.X * x.R.Tl);
            return new ViewItem { Ma = f.Ma, Ma21 = g.Count > 1 ? string.Join("\n", g.Select(x => x.R.Ma).Distinct()) : "", Vt = f.Vt, Dt = f.Dt, Hd = f.Hd, Tl = sx != 0 ? sk / sx : 0, X = Round(sx), Gop = g.Count, Qc = QcOf(f.Ten), Idxs = g.Select(x => x.I).ToList() };
        }).ToList();
    }
    private List<ViewItem> GopItems(List<(Row R, double X, int I)> items)
    {
        var groups = new Dictionary<string, (string label, string vt, List<(Row R, double X, int I)> list)>(); var order = new List<string>();
        foreach (var it in items)
        {
            var lab = GopLabel(it.R); string key;
            if (lab is null) { var raw = it.R.Ma.Trim(); lab = raw; key = it.R.Vt + "\u0001RAW\u0001" + raw.ToUpperInvariant(); }
            else key = it.R.Vt + "\u0001" + lab;
            if (!groups.TryGetValue(key, out var g)) { groups[key] = g = (lab, it.R.Vt, new()); order.Add(key); }
            g.list.Add(it);
        }
        return order.Select(k =>
        {
            var (label, vt, list) = groups[k]; var sx = list.Sum(x => x.X); var sk = list.Sum(x => x.X * x.R.Tl);
            string Uniq(Func<Row, string> f) => string.Join(" / ", list.Select(x => f(x.R)).Where(v => v.Length > 0).Distinct());
            var qcs = list.Select(x => QcOf(x.R.Ten)).Distinct().ToList();
            return new ViewItem { Ma = label, Ma21 = string.Join("\n", list.Select(x => x.R.Ma).Distinct()), Vt = vt, Dt = Uniq(r => r.Dt), Hd = Uniq(r => r.Hd), Tl = sx != 0 ? sk / sx : 0, X = Round(sx), Gop = list.Count, Qc = qcs.Count == 1 ? qcs[0] : "", Idxs = list.Select(x => x.I).ToList() };
        }).ToList();
    }

    /// <summary>Cảnh báo trước khi lưu (chỉ hỏi lại, không chặn): còn hàng cùng mã + lô ở vị trí khác chưa lấy; SL ≥ 10× mức thường; trùng phiếu cùng ngày.</summary>
    public List<string> Warnings(List<Pick> picks, DateTime ngay, string? editingId = null)
    {
        var o = new List<string>(); var items = Items(picks);
        var chosen = items.Select(x => x.I).ToHashSet(); var seen = new HashSet<string>();
        foreach (var (r, _, _) in items)
        {
            var key = N(r.Ma) + "|" + N(r.Lo); if (!seen.Add(key)) continue;
            var others = Rows.Select((x, i) => (x, i)).Where(t => !chosen.Contains(t.i) && N(t.x.Ma) + "|" + N(t.x.Lo) == key && t.x.Sl > 1e-9).ToList();
            if (others.Count == 0) continue;
            var picked = items.Where(e => N(e.R.Ma) + "|" + N(e.R.Lo) == key).Select(e => e.R.Vt.Length > 0 ? e.R.Vt : NoPos);
            o.Add($"{r.Ma}{(r.Lo.Length > 0 ? " (lô " + r.Lo + ")" : "")}: đang xuất ở {string.Join(", ", picked)} — còn ở {others.Count} vị trí khác: " +
                  string.Join(", ", others.Take(6).Select(t => $"{(t.x.Vt.Length > 0 ? t.x.Vt : NoPos)} ({Fmt(t.x.Sl)})")) + (others.Count > 6 ? " …" : ""));
            if (o.Count >= 6) break;
        }
        var hist = new Dictionary<string, List<double>>();
        foreach (var s in Slips) { if (s.Id == editingId) continue; foreach (var l in s.Lines) { var m = N(l.MaGoc); if (m.Length > 0 && l.Qty > 0) { if (!hist.TryGetValue(m, out var h)) hist[m] = h = new(); h.Add(l.Qty); } } }
        foreach (var g in items.GroupBy(x => N(x.R.MaGoc)))
        {
            var x = g.Sum(t => t.X); if (!hist.TryGetValue(g.Key, out var a) || a.Count < 3) continue;
            a = a.OrderBy(v => v).ToList(); var med = a.Count % 2 == 1 ? a[a.Count / 2] : (a[a.Count / 2 - 1] + a[a.Count / 2]) / 2;
            if (med > 0 && x >= 10 * med) o.Add($"SL xuất mã {g.First().R.Ma} = {Fmt(x)} — gấp {Math.Round(x / med)} lần mức thường ({Fmt(med)}, theo {a.Count} lần xuất trước).");
        }
        foreach (var (r, x, _) in items)
        {
            var hit = Slips.FirstOrDefault(s => s.Id != editingId && Local(s.CreatedAt)?.Date == ngay.Date && s.Lines.Any(l => N(l.MaGoc) == N(r.MaGoc) && N(l.Vt) == N(r.Vt) && Math.Abs(l.Qty - x) < 1e-9));
            if (hit is not null) o.Add($"Mã {r.Ma} tại {(r.Vt.Length > 0 ? r.Vt : NoPos)} xuất {Fmt(x)} đã có trong phiếu {hit.Id} cùng ngày — lưu 2 lần?");
        }
        return o;
    }
    public static string Fmt(double v) => v.ToString("#,0.###", CultureInfo.InvariantCulture);

    private List<Line> LinesOf(List<Pick> picks, Func<int, double> avail)
    {
        var keys = RowKeys(Rows); var lines = new List<Line>();
        foreach (var (r, x, i) in Items(picks))
        {
            if (keys[i].Length == 0) throw new BusinessException("Dữ liệu Tồn theo vị trí vừa thay đổi — chọn lại dòng.");
            if (x > avail(i) + 1e-9) throw new BusinessException($"Mã {r.Ma} tại {(r.Vt.Length > 0 ? r.Vt : NoPos)} chỉ còn {Fmt(avail(i))}, không đủ xuất {Fmt(x)}.");
            lines.Add(new Line { Key = keys[i], MaGoc = r.MaGoc, Ten = r.Ten, Dvt = r.Dvt, Lo = r.Lo, Vt = r.Vt, Tl = r.Tl, Qty = Round(x) });
        }
        if (lines.Count == 0) throw new BusinessException("Chưa có dòng nào có SL xuất hợp lệ.");
        return lines;
    }
    private static SlipView ViewOf(List<ViewItem> v, bool gop) => new() { Gop = gop, Items = v.Select(x => new ViewItem { Ma = x.Ma, Ma21 = x.Ma21, Vt = x.Vt, Dt = x.Dt, Hd = x.Hd, Tl = x.Tl, X = x.X, Gop = x.Gop }).ToList() };
    private static string CreatedOn(DateTime ngay, DateTime? keepTime = null)
    {
        var t = keepTime ?? DateTime.Now; return Iso(new DateTime(ngay.Year, ngay.Month, ngay.Day, t.Hour, t.Minute, t.Second, DateTimeKind.Local));
    }

    /// <summary>Lưu phiếu mới và TRỪ tồn đúng các dòng đã chọn.</summary>
    public Slip CreateSlip(List<Pick> picks, bool gop, string loai, DateTime ngay, string title)
    {
        RequireEdit();
        var lines = LinesOf(picks, i => Rows[i].Sl);
        var s = new Slip
        {
            Id = NewId("PC"), Source = "pc", NguoiTao = UserName, GhiChu = gop ? "Phiếu gộp theo Size" : "", Loai = Loai.Any(l => l.Key == loai) ? loai : "",
            CreatedAt = CreatedOn(ngay), Lines = lines, Deducted = true, DeductedAt = Iso(DateTime.Now), LoadAt = ImportedAt,
            View = ViewOf(BuildView(Items(picks), gop), gop), TieuDe = CleanTitle(title) == TitleDefault ? null : CleanTitle(title),
        };
        var idx = KeyIndex(); foreach (var l in lines) Rows[idx[l.Key]].Sl = Round(Rows[idx[l.Key]].Sl - l.Qty);
        Slips.Insert(0, s);
        Persist();
        Log("lập phiếu", $"{s.Id}: {lines.Count} dòng, SL {Fmt(s.TongSl)}");
        return s;
    }
    public static string CleanTitle(string? t)
    {
        var s = Regex.Replace(Regex.Replace(t ?? "", @"[\r\n\t]+", " "), @"\s+", " ").Trim(); if (s.Length > 80) s = s[..80];
        return s.Length > 0 ? s : TitleDefault;
    }

    // ------------------------------------------------------------------ sửa phiếu đã lưu

    public sealed class EditState { public string Id = ""; public Dictionary<int, double> Sel = new(), Bonus = new(); public string Loai = ""; public DateTime Ngay; public string Title = ""; public bool Gop; }
    /// <summary>Mở phiếu (đang hiệu lực) để sửa: SL của phiếu được tính là còn dùng được ở đúng dòng (bonus). Dòng không còn trong file → dựng dòng tạm SL 0.</summary>
    public EditState BeginEdit(string id)
    {
        RequireEdit();
        PruneGhosts();
        var s = Slips.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không thấy phiếu này.");
        if (!Active(s)) throw new BusinessException("Phiếu này đã bị khóa vì số liệu vị trí đã được nạp lại — hãy “Mở khóa” trước khi sửa.");
        var idx = KeyIndex(); var miss = s.Lines.Where(l => !idx.ContainsKey(l.Key)).ToList();
        if (miss.Count > 0)
        {
            if (!s.Deducted) throw new BusinessException($"{miss.Count} dòng của phiếu không còn trong dữ liệu vị trí hiện tại — không sửa được phiếu này.");
            var first = Rows.Count;
            foreach (var l in miss) { var r = Build(l.MaGoc, l.Ten, l.Dvt, l.Lo, l.Vt, 0, l.Tl); r.Ghost = true; Rows.Add(r); }
            var keys = RowKeys(Rows); for (var j = 0; j < miss.Count; j++) miss[j].Key = keys[first + j];
            idx = KeyIndex();
        }
        var e = new EditState { Id = s.Id, Loai = s.Loai, Ngay = Local(s.CreatedAt)?.Date ?? DateTime.Today, Title = s.TieuDe ?? "", Gop = s.View?.Gop == true };
        foreach (var l in s.Lines)
        {
            if (!idx.TryGetValue(l.Key, out var i)) continue;
            e.Sel[i] = e.Sel.GetValueOrDefault(i) + l.Qty; if (s.Deducted) e.Bonus[i] = e.Bonus.GetValueOrDefault(i) + l.Qty;
        }
        return e;
    }
    /// <summary>1.90: bỏ khỏi phiếu đang soạn các chỉ số dòng không còn trong <see cref="Rows"/> (dòng tạm của phiếu đang sửa bị dọn khi lưu,
    /// dữ liệu vừa tải lại / nạp lại ngắn hơn). Trả số dòng đã bỏ.</summary>
    public int DropStale(IDictionary<int, double> draft)
    {
        var n = Rows.Count;
        var bad = draft.Keys.Where(i => i < 0 || i >= n).ToList();
        foreach (var i in bad) draft.Remove(i);
        return bad.Count;
    }
    public void PruneGhosts() { Rows = Rows.Where(r => !(r.Ghost && r.Sl <= 1e-9)).ToList(); foreach (var r in Rows) r.Ghost = false; }
    public Slip SaveEdit(EditState e, List<Pick> picks, bool gop, string loai, DateTime ngay, string title)
    {
        RequireEdit();
        var s = Slips.FirstOrDefault(x => x.Id == e.Id) ?? throw new BusinessException("Không tìm thấy phiếu đang sửa.");
        if (!Active(s)) throw new BusinessException("Phiếu đã bị khóa do nạp lại số liệu vị trí — không lưu được.");
        var lines = LinesOf(picks, i => Rows[i].Sl + e.Bonus.GetValueOrDefault(i));
        var view = ViewOf(BuildView(Items(picks), gop), gop);
        if (s.Deducted) { Apply(s, +1); var idx = KeyIndex(); foreach (var l in lines) Rows[idx[l.Key]].Sl = Round(Rows[idx[l.Key]].Sl - l.Qty); }
        s.CreatedAt = CreatedOn(ngay, Local(s.CreatedAt));
        s.Lines = lines; s.Loai = Loai.Any(l => l.Key == loai) ? loai : s.Loai;
        if (gop) s.GhiChu = "Phiếu gộp theo Size"; else if (s.GhiChu == "Phiếu gộp theo Size") s.GhiChu = "";
        s.View = view; s.TieuDe = CleanTitle(title) == TitleDefault ? null : CleanTitle(title); s.EditedAt = Iso(DateTime.Now);
        PruneGhosts();
        Persist();
        Log("sửa phiếu", $"{s.Id}: {lines.Count} dòng, SL {Fmt(s.TongSl)}");
        return s;
    }

    // ------------------------------------------------------------------ thao tác trên phiếu đã lưu

    private Slip Find(string id) => Slips.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không thấy phiếu " + id + " (vừa bị xóa ở máy khác?).");
    /// <summary>Hủy trừ (hoàn tồn) hoặc trừ lại tồn. Trả số dòng không còn trong tồn hiện tại.</summary>
    public int SetDeducted(string id, bool deducted)
    {
        RequireEdit();
        var s = Find(id);
        if (!Active(s)) throw new BusinessException("Phiếu này đã bị khóa vì số liệu vị trí đã được nạp lại — không hủy trừ / trừ lại được.");
        if (s.Deducted == deducted) return 0;
        var miss = Apply(s, deducted ? -1 : +1);
        s.Deducted = deducted; s.DeductedAt = deducted ? Iso(DateTime.Now) : null;
        Persist();
        Log(deducted ? "trừ lại tồn" : "hủy trừ tồn", s.Id);
        return miss;
    }
    /// <summary>Phiếu đợt cũ (đã khóa) nhưng thực tế chưa xuất → gắn vào đợt hiện tại và trừ tồn. Trả (dòng thiếu, dòng bị âm).</summary>
    public (int Missing, int Negative) Unlock(string id)
    {
        RequireEdit();
        var s = Find(id); if (Active(s)) return (0, 0);
        var miss = Apply(s, -1); var idx = KeyIndex();
        var neg = s.Lines.Count(l => idx.TryGetValue(l.Key, out var i) && Rows[i].Sl < 0);
        s.LoadAt = ImportedAt; s.Deducted = true; s.DeductedAt = Iso(DateTime.Now);
        Persist();
        Log("mở khóa phiếu", s.Id);
        return (miss, neg);
    }
    /// <summary>Xóa phiếu: đang trừ + còn hiệu lực → cộng lại tồn rồi xóa; đã khóa / đã hoàn → chỉ gỡ khỏi danh sách.</summary>
    public void Delete(string id)
    {
        RequireEdit();
        var s = Find(id);
        if (s.Deducted && Active(s)) Apply(s, +1);
        Slips.Remove(s);
        Persist();
        Log("xóa phiếu", s.Id);
    }
    public void SetLoai(string id, string loai)
    {
        RequireEdit();
        var s = Find(id); s.Loai = Loai.Any(l => l.Key == loai) ? loai : ""; Persist();
    }
    public void SetDay(string id, DateTime day)
    {
        RequireEdit();
        var s = Find(id); s.CreatedAt = CreatedOn(day, Local(s.CreatedAt)); Persist();
    }
    /// <summary>Gộp ≥2 phiếu cùng loại (đang trừ, chưa khóa) thành 1 phiếu; tồn không đổi; giữ bản gốc để "Tách phiếu".</summary>
    public Slip Merge(List<string> ids)
    {
        RequireEdit();
        var list = Slips.Where(s => ids.Contains(s.Id)).ToList();
        if (list.Count < 2) throw new BusinessException("Chọn từ 2 phiếu trở lên để gộp.");
        if (list.Any(s => !Mergeable(s))) throw new BusinessException("Có phiếu đã khóa / chưa trừ tồn — chỉ gộp được phiếu đang trừ tồn.");
        var kinds = list.Select(s => s.Loai).Distinct().ToList();
        if (kinds.Contains("")) throw new BusinessException("Có phiếu chưa chọn loại. Chọn “Loại phiếu” (Phiếu thay thùng / Phiếu xuất xả) cho từng phiếu rồi mới gộp.");
        if (kinds.Count > 1) throw new BusinessException("Không gộp chung Phiếu thay thùng với Phiếu xuất xả — chọn các phiếu cùng loại.");
        var lines = SumLines(list);
        var raw = lines.Select(l => (RowOfLine(l), l.Qty, -1)).ToList();
        var newest = list.Select(s => s.CreatedAt).Where(c => Local(c) is not null).OrderBy(c => Local(c)).LastOrDefault() ?? Iso(DateTime.Now);
        var s = new Slip
        {
            Id = NewId("PC"), Source = "pc", NguoiTao = UserName, GhiChu = "Gộp từ " + string.Join(", ", list.Select(Code)), Loai = list[0].Loai,
            CreatedAt = newest, Lines = lines, Deducted = true, DeductedAt = Iso(DateTime.Now), LoadAt = ImportedAt,
            View = ViewOf(BuildView(raw, true), true), MergedFrom = list.Select(x => x.Clone()).ToList(),
        };
        var pids = PhoneIdsOf(list); if (pids.Count > 0) s.PhoneIds = pids;
        var at = Slips.IndexOf(list[0]);
        Slips = Slips.Where(x => !list.Contains(x)).ToList();
        Slips.Insert(Math.Clamp(at, 0, Slips.Count), s);
        Persist();
        Log("gộp phiếu", $"{s.Id} ← {string.Join(", ", list.Select(Code))}");
        return s;
    }
    private static List<JsonElement> PhoneIdsOf(IEnumerable<Slip> list)
    {
        var o = new List<JsonElement>();
        foreach (var s in list) { if (s.PhoneId is { } p) o.Add(p); if (s.PhoneIds is { } ps) o.AddRange(ps); }
        return o.GroupBy(e => e.ToString()).Select(g => g.First()).ToList();
    }
    private static List<Line> SumLines(IEnumerable<Slip> slips)
    {
        var by = new Dictionary<string, Line>(); var o = new List<Line>();
        foreach (var s in slips) foreach (var l in s.Lines)
        {
            if (by.TryGetValue(l.Key, out var x)) x.Qty = Round(x.Qty + l.Qty);
            else { var n = l.Clone(); by[l.Key] = n; o.Add(n); }
        }
        return o;
    }
    /// <summary>Hoàn tác gộp: trả lại các phiếu gốc (tồn không đổi).</summary>
    public void SplitBack(string id)
    {
        RequireEdit();
        var s = Find(id);
        if (s.MergedFrom is not { Count: > 0 }) throw new BusinessException("Phiếu này không phải phiếu gộp.");
        if (!Active(s)) throw new BusinessException("Phiếu gộp đã bị khóa vì số liệu vị trí đã được nạp lại — không tách được.");
        if (!s.Deducted) throw new BusinessException("Phiếu đang ở trạng thái hủy trừ tồn — bấm “Trừ lại tồn” trước khi tách.");
        var back = s.MergedFrom.Select(o => { var c = o.Clone(); c.LoadAt = ImportedAt; return c; }).ToList();
        var at = Slips.IndexOf(s); Slips.RemoveAt(at); Slips.InsertRange(at, back);
        Persist();
        Log("tách phiếu gộp", $"{s.Id} → {string.Join(", ", back.Select(Code))}");
    }
    public sealed record SplitPick(string SlipId, int Line, double Take);
    /// <summary>Tách một số dòng (và SL) của phiếu đã lưu ra PHIẾU MỚI với loại + ngày riêng. Tồn không đổi.</summary>
    public Slip SplitLines(List<SplitPick> picks, string loai, DateTime day, string note)
    {
        RequireEdit();
        if (picks.Count == 0) throw new BusinessException("Chưa chọn dòng nào để tách.");
        var srcs = picks.Select(p => p.SlipId).Distinct().Select(Find).ToList();
        foreach (var s in srcs) if (!Mergeable(s)) throw new BusinessException("Phiếu " + s.Id + " đã khóa / chưa trừ tồn.");
        var by = new Dictionary<string, Line>(); var newLines = new List<Line>(); var emptied = new List<Slip>();
        foreach (var s in srcs)
        {
            var ps = picks.Where(p => p.SlipId == s.Id).ToList(); var kept = new List<Line>();
            for (var li = 0; li < s.Lines.Count; li++)
            {
                var l = s.Lines[li]; var p = ps.FirstOrDefault(x => x.Line == li);
                if (p is null) { kept.Add(l); continue; }
                if (!(p.Take > 0) || p.Take > l.Qty + 1e-9) throw new BusinessException($"SL tách của {ShortCode(l.MaGoc)} phải > 0 và ≤ {Fmt(l.Qty)}.");
                var take = Math.Min(Round(p.Take), l.Qty); var rem = Round(l.Qty - take);
                if (by.TryGetValue(l.Key, out var o)) o.Qty = Round(o.Qty + take); else { var n = l.Clone(); n.Qty = take; by[l.Key] = n; newLines.Add(n); }
                if (rem > 1e-9) { var k = l.Clone(); k.Qty = rem; kept.Add(k); }
            }
            s.Lines = kept; s.MergedFrom = null;
            if (s.View?.Gop == true) s.View = ViewOf(BuildView(kept.Select(l => (RowOfLine(l), l.Qty, -1)).ToList(), true), true); else s.View = null;
            if (kept.Count == 0) emptied.Add(s);
        }
        var first = srcs[0];
        var created = DayKey(first) == day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ? first.CreatedAt : CreatedOn(day, Local(first.CreatedAt));
        var slip = new Slip
        {
            Id = NewId("PC"), Source = "pc", NguoiTao = UserName, GhiChu = "Tách từ " + string.Join(", ", srcs.Select(Code)) + (note.Trim().Length > 0 ? " — " + note.Trim() : ""),
            Loai = Loai.Any(l => l.Key == loai) ? loai : "", CreatedAt = created, Lines = newLines, Deducted = true, DeductedAt = Iso(DateTime.Now), LoadAt = ImportedAt,
        };
        var pids = PhoneIdsOf(emptied); if (pids.Count > 0) slip.PhoneIds = pids;
        var at = Slips.IndexOf(first);
        Slips = Slips.Where(x => !emptied.Contains(x)).ToList();
        Slips.Insert(Math.Clamp(at, 0, Slips.Count), slip);
        Persist();
        Log("tách mã ra phiếu mới", $"{slip.Id} ← {string.Join(", ", srcs.Select(Code))}");
        return slip;
    }

    /// <summary>Bản xem của phiếu đã lưu: phiếu PC có bản đã gộp lúc lưu; không thì gộp dòng trùng (hoặc gộp theo Size nếu yêu cầu).</summary>
    public List<ViewItem> ViewOfSlip(Slip s, bool? gop = null)
    {
        if (gop is null && s.View is { Items.Count: > 0 } v) return v.Items;
        var raw = s.Lines.Select(l => (RowOfLine(l), l.Qty, -1)).ToList();
        return BuildView(raw, gop ?? false);
    }

    // ------------------------------------------------------------------ lưu trữ theo ngày + thống kê Size

    /// <summary>1.132: 1 dòng hàng đã xuất (mọi phiếu, đang mở + đã lưu trữ) — tra cứu / rà soát theo mã, size, vị trí.</summary>
    public sealed record DaXuat(string Ngay, string MaPhieu, string Loai, string MaGoc, string Ma, string Ten, string Size, string Lo, string Vt,
        double Sl, double Tl, string NguoiTao, bool DienThoai, bool DaTru, double TonVt, string SlipId, string Nguon = "app")
    {
        /// <summary>1.160: Đặc tính / Hợp đồng / Khách / Ghi chú — chỉ dòng Excel kho gốc có; <see cref="DoiChieu"/> = ghép app ↔ Excel.</summary>
        public string Dt { get; init; } = "";
        public string Hd { get; init; } = "";
        public string Khach { get; init; } = "";
        public string GhiChu { get; init; } = "";
        public string DoiChieu { get; init; } = "";
        private string? _hay;
        /// <summary>Chuỗi tìm đã bỏ dấu (dựng 1 lần, dùng cho mọi lần gõ).</summary>
        public string Hay => _hay ??= KhoLanh.Core.Grid.GridFilter.Fold($"{Ma} {MaGoc} {Ten} {Size} {Vt} {Lo} {MaPhieu} {Loai} {NguoiTao} {Dt} {Hd} {Khach} {GhiChu}");
    }

    /// <summary>
    /// 1.132 (người dùng: "trong tồn vị trí thêm tìm mã hàng, size, vị trí đã xuất để rà soát lại các mã hàng đã xuất"): mọi dòng của mọi phiếu xuất,
    /// mới nhất trước; <see cref="DaXuat.TonVt"/> = tồn hiện tại (file đang nạp) ở đúng Mã · Lô · Vị trí của dòng — đã xuất hết mà file còn tồn thì dễ thấy.
    /// </summary>
    public List<DaXuat> DaXuatList(bool app = true, bool excel = true)
    {
        var ton = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Rows) { if (r.Ghost) continue; var k = r.MaGoc.Trim() + "|" + r.Lo.Trim() + "|" + r.Vt.Trim(); ton[k] = ton.GetValueOrDefault(k) + r.Sl; }
        var o = new List<DaXuat>();
        foreach (var s in Slips.OrderByDescending(x => x.CreatedAt, StringComparer.Ordinal))
            foreach (var l in s.Lines)
            {
                var ma = ShortCode(l.MaGoc);
                var size = SizeOf(new Row { Ma = ma, Ten = l.Ten });
                o.Add(new DaXuat(DayKey(s), s.MaPhieu.Length > 0 ? s.MaPhieu : s.Id, LoaiLabel(s.Loai), l.MaGoc, ma, l.Ten, size, l.Lo, l.Vt, l.Qty, l.Tl,
                    s.NguoiTao, s.FromPhone, s.Deducted, Math.Round(ton.GetValueOrDefault(l.MaGoc.Trim() + "|" + l.Lo.Trim() + "|" + l.Vt.Trim()), 3), s.Id));
            }
        if (XuatGoc.Count == 0) return app ? o : new();
        var tonMv = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Rows) { if (r.Ghost) continue; var k = r.MaGoc.Trim() + "|" + r.Vt.Trim(); tonMv[k] = tonMv.GetValueOrDefault(k) + r.Sl; }
        var xl = XuatGocAsDaXuat(tonMv);
        o = DoiChieuXuat(o, xl);   // gán đối chiếu trên cả 2 phía kể cả khi chỉ hiện 1 nguồn
        return (app ? o : new()).Concat(excel ? xl : new()).OrderByDescending(x => x.Ngay, StringComparer.Ordinal).ToList();
    }

    public sealed record ArcGroup(string Day, string Loai, List<Slip> Slips)
    {
        public int NLines => Slips.Sum(s => s.Lines.Count);
        public double Tong => Slips.Sum(s => s.TongSl);
    }
    /// <summary>Phiếu đang trừ, đã qua ngày, đã chọn loại → gộp theo (ngày, loại). Kèm số phiếu chưa chọn loại theo ngày.</summary>
    public (List<ArcGroup> Groups, Dictionary<string, int> Missing) Archive()
    {
        var map = new Dictionary<string, ArcGroup>(); var miss = new Dictionary<string, int>();
        foreach (var s in Slips)
        {
            if (!s.Deducted || IsToday(s)) continue;
            var d = DayKey(s); if (d.Length == 0) continue;
            if (s.Loai.Length == 0 || !Loai.Any(l => l.Key == s.Loai)) { miss[d] = miss.GetValueOrDefault(d) + 1; continue; }
            var k = d + "|" + s.Loai; if (!map.TryGetValue(k, out var g)) map[k] = g = new ArcGroup(d, s.Loai, new());
            g.Slips.Add(s);
        }
        var groups = map.Values.OrderByDescending(g => g.Day, StringComparer.Ordinal).ThenBy(g => g.Loai == "thaythung" ? 0 : 1).ToList();
        return (groups, miss);
    }
    public List<ViewItem> ArchiveItems(ArcGroup g) => BuildView(SumLines(g.Slips).Select(l => (RowOfLine(l), l.Qty, -1)).ToList(), true);
    /// <summary>Danh sách phiếu (không gồm phiếu đã vào Lưu trữ theo ngày), mới nhất trên cùng.</summary>
    public List<Slip> OpenSlips()
    {
        var arc = Archive().Groups.SelectMany(g => g.Slips).ToHashSet();
        return Slips.Where(s => !arc.Contains(s)).OrderByDescending(s => Local(s.CreatedAt) ?? DateTime.MinValue).ToList();
    }

    public sealed class SizeStat { public string Label = ""; public HashSet<string> Dts = new(); public double Done, Draft, Net; }
    /// <summary>Mỗi Size đã làm phiếu bao nhiêu kiện: today (phiếu đang trừ hôm nay + đang soạn) | week (7 ngày) | month (tháng này) | all.</summary>
    public List<SizeStat> SizeStats(string range, IEnumerable<Pick>? draft = null)
    {
        var by = new Dictionary<string, SizeStat>();
        void Add(Row r, double q, bool isDraft)
        {
            if (!(q > 0)) return;
            var label = GopLabel(r) ?? (r.Ma.Length > 0 ? r.Ma : r.MaGoc);
            if (!by.TryGetValue(label, out var o)) by[label] = o = new SizeStat { Label = label };
            if (r.Dt.Length > 0) o.Dts.Add(_dtGroup.GetValueOrDefault(r.Dt.ToLowerInvariant()) ?? r.Dt);
            if (isDraft) o.Draft += q; else o.Done += q; o.Net += q * r.Tl;
        }
        var today = DateTime.Today;
        var from = range switch { "week" => today.AddDays(-6), "month" => new DateTime(today.Year, today.Month, 1), "all" => DateTime.MinValue, _ => today };
        foreach (var s in Slips)
        {
            if (!s.Deducted) continue;
            if (range == "today") { if (!Active(s) || !IsToday(s)) continue; }
            else if (!(Local(s.CreatedAt) is { } d && d >= from)) continue;
            foreach (var l in s.Lines) Add(RowOfLine(l), l.Qty, false);
        }
        if (range == "today" && draft is not null) foreach (var p in draft) if (p.Index >= 0 && p.Index < Rows.Count) Add(Rows[p.Index], p.Qty, true);
        return by.Values.OrderBy(x => x.Label, StringComparer.Create(CultureInfo.GetCultureInfo("vi-VN"), CompareOptions.None)).ToList();
    }

    // ------------------------------------------------------------------ xuất Excel

    /// <summary>Tên file: "XX dd.mm" (xuất xả) / "XTT dd.mm" (thay thùng), không có loại → "PhieuXuat_ViTri_yyMMdd-HHmm".</summary>
    public static string FileBase(string loai, DateTime day)
    {
        var tag = loai == "xuatxa" ? "XX" : loai == "thaythung" ? "XTT" : "";
        return tag.Length > 0 ? $"{tag} {day:dd.MM}" : "PhieuXuat_ViTri_" + DateTime.Now.ToString("yyMMdd-HHmm", CultureInfo.InvariantCulture);
    }
    /// <summary>Excel phiếu xuất theo mẫu app cũ: dòng 1 "&lt;TIÊU ĐỀ&gt; : dd/mm/yyyy", cột Mã hàng | Vị trí | SL xuất | Đặc tính | Hợp đồng, Times New Roman 16, dòng Tổng.</summary>
    public void ExportExcel(string path, List<ViewItem> items, string title, DateTime day)
    {
        if (!CanExport) throw new BusinessException("Tài khoản không có quyền xuất file Tồn kho theo vị trí.");
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Phieu_Xuat");
        ws.Style.Font.FontName = "Times New Roman"; ws.Style.Font.FontSize = 16;
        ws.Cell(1, 1).Value = $"{CleanTitle(title)} : {day:dd/MM/yyyy}";
        ws.Range(1, 1, 1, 5).Merge(); ws.Cell(1, 1).Style.Font.Bold = true; ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        var heads = new[] { "Mã hàng", "Vị trí", "SL xuất", "Đặc tính", "Hợp đồng" };
        for (var c = 0; c < 5; c++) { var h = ws.Cell(2, c + 1); h.Value = heads[c]; h.Style.Font.Bold = true; h.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center; }
        var r = 3;
        foreach (var it in items)
        {
            ws.Cell(r, 1).Value = it.Ma; ws.Cell(r, 2).Value = it.Vt; ws.Cell(r, 3).Value = it.X; ws.Cell(r, 4).Value = it.Dt; ws.Cell(r, 5).Value = it.Hd; r++;
        }
        ws.Cell(r, 1).Value = $"Tổng ({items.Count} dòng)"; ws.Cell(r, 3).Value = items.Sum(i => i.X);
        ws.Range(r, 1, r, 5).Style.Font.Bold = true; ws.Range(r, 1, r, 5).Style.Border.TopBorder = XLBorderStyleValues.Medium;
        ws.Range(3, 3, r, 3).Style.NumberFormat.Format = "#,##0.00"; ws.Range(3, 3, r, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        var tbl = ws.Range(2, 1, r, 5);
        tbl.Style.Border.OutsideBorder = XLBorderStyleValues.Thin; tbl.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        tbl.Style.Border.OutsideBorderColor = XLColor.Black; tbl.Style.Border.InsideBorderColor = XLColor.Black;
        for (var i = 1; i <= r; i++) ws.Row(i).Height = 28;
        ws.Columns(1, 5).AdjustToContents(2, r); foreach (var col in ws.Columns(1, 5)) col.Width = Math.Max(col.Width * 1.15, 10);
        ws.SheetView.FreezeRows(2);
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait; ws.PageSetup.FitToPages(1, 0);
        wb.SaveInPlace(path);
        Log("xuất Excel phiếu", Path.GetFileName(path));
    }
    /// <summary>File mẫu để nạp (đúng tên cột).</summary>
    public static void SaveTemplate(string path)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Ton_Theo_Vi_Tri");
        for (var c = 0; c < TemplateColumns.Length; c++)
        {
            var h = ws.Cell(1, c + 1); h.Value = TemplateColumns[c];
            h.Style.Font.FontName = "Times New Roman"; h.Style.Font.FontSize = 14; h.Style.Font.Bold = true; h.Style.Font.FontColor = XLColor.White;
            h.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78"); h.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }
        ws.Columns().AdjustToContents(); ws.SheetView.FreezeRows(1);
        wb.SaveInPlace(path);
    }
}
