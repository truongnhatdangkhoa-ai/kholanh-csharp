using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.PhieuXuat;

/// <summary>
/// Module Phiếu xuất hệ thống (C#, viết lại theo "Phiếu xuất hệ thống" M09 của app cũ — phieu-xuat-he-thong.js). CHỈ ĐỌC.
/// Gom phiếu xuất thật từ dữ liệu của từng module về một dạng chung để lọc, sắp xếp, xem chi tiết:
///   M01 · Xuất Hàng        ton-kho-an-an / PhieuXuat               (1 ô JSON "__JSON__{slips,seq}")
///   M01VT · Tồn theo vị trí ton-vi-tri-an-an / ViTriPhieuDienThoai  (1 ô JSON)
///   M01 · Vị Trí Bột       ton-kho-an-an / ViTriBotPhieuDienThoai  (1 ô JSON — phiếu từ điện thoại)
///   M02 · Tồn Kho Gửi      ton-kho-gui / PhieuXuat + PhieuXuatItems
///   M03 · NXT Bột/Sốt      nxt-bot-sot / SoCai (loai = 'xuat'), gộp theo phieuId (điện thoại) hoặc số chứng từ / ngày
/// Không ghi gì vào dữ liệu.
/// </summary>
public sealed class PxhtService
{
    public const string PermKey = "m11";

    public sealed record Source(string Key, string Module, string Short, string Name, string Sub, string NavKey);

    public static readonly Source[] Sources =
    {
        new("m1xh", "ton-kho-an-an", "M01", "Tồn Kho An An", "Xuất Hàng", "m1"),
        new("m1vt", "ton-vi-tri-an-an", "M01VT", "Tồn Vị Trí An An", "Tồn theo vị trí", "m7"),
        new("m1vtb", "ton-kho-an-an", "M01", "Tồn Kho An An", "Vị Trí Bột", ""),
        new("m2", "ton-kho-gui", "M02", "Tồn Kho Gửi", "Phiếu Xuất", "m2"),
        new("m3", "nxt-bot-sot", "M03", "NXT Bột/Sốt", "Xuất/Chuyển Lệnh", ""),
    };
    public static Source Src(string key) => Sources.First(s => s.Key == key);
    public static string SrcLabel(string key) { var s = Src(key); return s.Short + " · " + s.Name; }
    public static string SrcFull(string key) => SrcLabel(key) + " — " + Src(key).Sub;

    /// <summary>Cột hiển thị trong chi tiết theo từng nguồn. Num = căn phải; Sum = cộng tổng.</summary>
    public sealed record Col(string Key, string Label, bool Num = false, bool Sum = false);

    public static readonly Dictionary<string, Col[]> Cols = new()
    {
        ["m1xh"] = new Col[] { new("maHang", "Mã hàng"), new("tenHang", "Tên hàng"), new("size", "Size"), new("hopDong", "Hợp đồng"), new("qty", "SL xuất", true, true) },
        ["m1vt"] = new Col[] { new("maGoc", "Mã hàng"), new("ten", "Tên hàng"), new("lo", "Lô"), new("vt", "Vị trí"), new("dvt", "ĐVT"), new("tl", "Trọng lượng", true), new("qty", "SL xuất", true, true) },
        ["m1vtb"] = new Col[] { new("ma", "Mã"), new("vt", "Vị trí"), new("nsx", "NSX"), new("hsd", "HSD"), new("nhap", "Nhập"), new("ghiChu", "Ghi chú"), new("qty", "SL xuất", true, true) },
        ["m2"] = new Col[] { new("maHang", "Mã hàng"), new("tenHang", "Tên hàng"), new("phieuNhap", "Phiếu nhập"), new("soLo", "Số lô"), new("size", "Size"), new("soKien", "Số kiện gốc", true), new("qty", "SL xuất", true, true), new("tlTru", "TL trừ (kg)", true, true) },
        ["m3"] = new Col[] { new("maHang", "Mã hàng"), new("size", "Size"), new("viTri", "Vị trí"), new("hopDong", "LSX / Hợp đồng dùng hàng"), new("hanDung", "Hạn dùng"), new("soLuong", "Số lượng", true), new("net", "Khối lượng (kg)", true, true) },
    };

    /// <summary>Khóa tìm theo mã / tên hàng bên trong phiếu.</summary>
    private static readonly string[] SearchKeys = { "maHang", "tenHang", "maGoc", "ten", "ma", "code", "hopDong", "lo", "vt", "loc", "lsx", "po", "inv" };
    private static readonly HashSet<string> CodeKeys = new() { "maHang", "maGoc", "code", "ma" };

    public sealed class Slip
    {
        public string Key = "", Src = "", MaPhieu = "", RawDate = "", NguoiTao = "", GhiChu = "";
        public bool Phone;
        public DateTime? Date;
        public bool HasTime;
        public List<Dictionary<string, string>> Items = new();
        public List<(string K, string V)> Meta = new();
        public string DayIso => Date is { } d ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
        public long Ts => Date is { } d ? d.Ticks : 0;
        public int SoMH => Items.Count;
        public string DateText => Date is not { } d ? "—" : HasTime ? d.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) : d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        public string ModuleText => SrcLabel(Src) + " · " + PxhtService.Src(Src).Sub;
        public string NguonText => Phone ? "Điện thoại" : "PC";
        public double Sum(string key) => Items.Sum(it => N(it.GetValueOrDefault(key)));
    }

    public sealed record LoadError(string Src, string Error);

    public List<Slip> All { get; private set; } = new();
    public List<LoadError> Errors { get; } = new();

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    public PxhtService(Func<ISheetSource> source, Session session) { _src = source; _session = session; }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanExport => _session.Can(PermKey, Perm.Export);

    // ------------------------------------------------------------------ tiện ích

    internal static string S(JsonNode? n)
    {
        if (n is null) return "";
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s.Trim();
            if (v.TryGetValue<double>(out var d)) return d.ToString("0.############", CultureInfo.InvariantCulture);
            if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        }
        return n.ToJsonString().Trim();
    }
    internal static double N(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        return double.TryParse(s.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && double.IsFinite(d) ? d : 0;
    }
    private static string Num(JsonNode? n) => N(S(n)).ToString("0.############", CultureInfo.InvariantCulture);

    /// <summary>Đọc ngày: số ms, "yyyy-mm-dd" (giờ địa phương), "dd/mm/yyyy", ISO có giờ.</summary>
    internal static (DateTime? Date, bool HasTime) ToDate(JsonNode? n)
    {
        if (n is null) return (null, false);
        if (n is JsonValue v && v.TryGetValue<double>(out var ms))
            return ms > 0 ? (DateTimeOffset.FromUnixTimeMilliseconds((long)ms).LocalDateTime, true) : (null, false);
        var s = S(n);
        if (s.Length == 0) return (null, false);
        var m = Regex.Match(s, @"^(\d{4})-(\d{2})-(\d{2})$");
        if (m.Success) return (Safe(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value)), false);
        m = Regex.Match(s, @"^(\d{1,2})/(\d{1,2})/(\d{4})$");
        if (m.Success) return (Safe(int.Parse(m.Groups[3].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value)), false);
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var ms2) && ms2 > 1e11)
            return (DateTimeOffset.FromUnixTimeMilliseconds((long)ms2).LocalDateTime, true);
        if (DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dto))
            return (Regex.IsMatch(s, @"[T ]\d{2}:\d{2}") && (s.EndsWith("Z") || Regex.IsMatch(s, @"[+-]\d{2}:?\d{2}$")) ? dto.LocalDateTime : dto.DateTime,
                    Regex.IsMatch(s, @"T\d{2}:\d{2}"));
        return (null, false);
    }
    private static DateTime? Safe(int y, int mo, int d) { try { return new DateTime(y, mo, d); } catch { return null; } }

    private JsonArray? ReadArr(string db, string sheet, string srcKey)
    {
        try { return JsonNode.Parse(_src().Read(db, sheet)) as JsonArray ?? new JsonArray(); }
        catch (Exception e) { Errors.Add(new(srcKey, e.Message)); return null; }
    }

    /// <summary>Sheet 1 ô JSON: [{ data: "__JSON__{...}" }].</summary>
    internal static JsonObject? JsonCell(JsonArray? rows)
    {
        if (rows is null || rows.Count == 0 || rows[0] is not JsonObject o) return null;
        var cell = S(o["data"]);
        if (!cell.StartsWith("__JSON__")) return null;
        try { return JsonNode.Parse(cell[8..]) as JsonObject; } catch { return null; }
    }

    private static Slip Mk(string key, string src, string maPhieu, JsonNode? rawDate, bool phone, string nguoi, string ghiChu,
        List<Dictionary<string, string>> items, List<(string, string)>? meta = null)
    {
        var (d, t) = ToDate(rawDate);
        return new Slip { Key = key, Src = src, MaPhieu = maPhieu, RawDate = S(rawDate), Phone = phone, NguoiTao = nguoi, GhiChu = ghiChu, Date = d, HasTime = t, Items = items, Meta = meta ?? new() };
    }

    private static Dictionary<string, string> Item(JsonObject l, params (string Key, string From, bool Num)[] map)
    {
        var d = new Dictionary<string, string>();
        foreach (var (k, f, num) in map) d[k] = num ? Num(l[f]) : S(l[f]);
        return d;
    }

    private static IEnumerable<JsonObject> Objs(JsonNode? n) => (n as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>();
    private static string Deducted(JsonObject s) => s["deducted"] is JsonValue v && v.TryGetValue<bool>(out var b) && b ? "Đã trừ" : "Chưa trừ / đã hủy trừ";

    // ------------------------------------------------------------------ đọc từng nguồn

    internal List<Slip> LoadM1()
    {
        var res = new List<Slip>();
        var pxh = JsonCell(ReadArr("ton-kho-an-an", "PhieuXuat", "m1xh"));
        foreach (var s in Objs(pxh?["slips"]))
        {
            var phone = S(s["phoneId"]).Length > 0 || S(s["moduleId"]) == "dienthoai";
            res.Add(Mk("m1xh|" + S(s["id"]), "m1xh", S(s["id"]), s["createdAt"], phone, S(s["nguoiTao"]), S(s["ghiChu"]),
                Objs(s["lines"]).Select(l => Item(l, ("maHang", "maHang", false), ("tenHang", "tenHang", false), ("size", "size", false), ("hopDong", "hopDong", false), ("qty", "qty", true))).ToList(),
                new() { ("Nhóm nguồn", S(s["moduleLabel"])), ("Mã phiếu điện thoại", S(s["phoneMaPhieu"])), ("Trừ tồn kho", Deducted(s)) }));
        }
        var pvt = JsonCell(ReadArr("ton-vi-tri-an-an", "ViTriPhieuDienThoai", "m1vt"));
        foreach (var s in Objs(pvt?["slips"]))
        {
            var source = S(s["source"]);
            var phone = source.Length == 0 || source != "pc";
            var loai = S(s["loai"]) switch { "thaythung" => "Phiếu thay thùng", "xuatxa" => "Phiếu xuất xả", var x => x };
            res.Add(Mk("m1vt|" + S(s["id"]), "m1vt", FirstNonEmpty(S(s["maPhieu"]), S(s["id"])), s["createdAt"], phone, S(s["nguoiTao"]), S(s["ghiChu"]),
                Objs(s["lines"]).Select(l => Item(l, ("maGoc", "maGoc", false), ("ten", "ten", false), ("lo", "lo", false), ("vt", "vt", false), ("dvt", "dvt", false), ("tl", "tl", false), ("qty", "qty", true))).ToList(),
                new() { ("Loại phiếu", loai), ("Tiêu đề", S(s["tieuDe"])), ("Mã nội bộ trên PC", S(s["id"])), ("Trừ tồn kho", Deducted(s)) }));
        }
        var pvtb = JsonCell(ReadArr("ton-kho-an-an", "ViTriBotPhieuDienThoai", "m1vtb"));
        foreach (var s in Objs(pvtb?["slips"]))
            res.Add(Mk("m1vtb|" + S(s["id"]), "m1vtb", FirstNonEmpty(S(s["maPhieu"]), S(s["id"])), s["createdAt"], true, S(s["nguoiTao"]), S(s["ghiChu"]),
                Objs(s["lines"]).Select(l => Item(l, ("ma", "ma", false), ("vt", "vt", false), ("nsx", "nsx", false), ("hsd", "hsd", false), ("nhap", "nhap", false), ("ghiChu", "ghiChu", false), ("qty", "qty", true))).ToList(),
                new() { ("Mã nội bộ trên PC", S(s["id"])), ("Trừ tồn kho", Deducted(s)) }));
        return res;
    }

    internal List<Slip> LoadM2()
    {
        var h = ReadArr("ton-kho-gui", "PhieuXuat", "m2");
        var it = ReadArr("ton-kho-gui", "PhieuXuatItems", "m2");
        var byId = new Dictionary<string, List<Dictionary<string, string>>>();
        foreach (var r in it?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            var id = S(r["phieuId"]);
            if (!byId.TryGetValue(id, out var list)) byId[id] = list = new();
            list.Add(Item(r, ("maHang", "maHang", false), ("tenHang", "tenHang", false), ("phieuNhap", "phieuNhap", false), ("soLo", "soLo", false),
                ("size", "size", false), ("soKien", "soKien", false), ("qty", "soLuongXuat", true), ("tlTru", "trongLuongTru", true)));
        }
        return (h?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            .Where(r => S(r["maPhieu"]).Length > 0 || S(r["id"]).Length > 0)
            .Select(r => Mk("m2|" + S(r["id"]), "m2", FirstNonEmpty(S(r["maPhieu"]), S(r["id"])), r["ngayTao"],
                Regex.IsMatch(S(r["nguon"]), "điện thoại|dien thoai|dienthoai", RegexOptions.IgnoreCase), S(r["nguoiTao"]), S(r["ghiChu"]),
                byId.GetValueOrDefault(S(r["id"])) ?? new()))
            .ToList();
    }

    /// <summary>Sổ kho: phiếu xuất = các dòng loại "xuat" trong SoCai; cùng phieuId (điện thoại) hoặc cùng số chứng từ (thiếu thì theo ngày) gộp thành 1 phiếu.</summary>
    internal List<Slip> LoadSoKho(string srcKey, string db)
    {
        var res = new List<Slip>();
        var groups = new Dictionary<string, Slip>();
        foreach (var l in ReadArr(db, "SoCai", srcKey)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        {
            if (S(l["loai"]) != "xuat") continue;
            var phone = S(l["nguon"]) == "dt";
            var gk = S(l["phieuId"]).Length > 0 ? "ph|" + S(l["phieuId"]) : "ct|" + FirstNonEmpty(S(l["chungTu"]), S(l["ngay"]));
            if (!groups.TryGetValue(gk, out var g))
            {
                var luc = S(l["luc"]);
                g = Mk(srcKey + "|" + gk, srcKey, FirstNonEmpty(S(l["chungTu"]), "XK-" + S(l["ngay"]).Replace("-", "")),
                    luc.Length > 0 ? l["luc"] : l["ngay"], phone, S(l["nguoi"]), S(l["ghiChu"]), new(), new() { ("Nguồn", phone ? "Điện thoại" : "PC") });
                groups[gk] = g; res.Add(g);
            }
            g.Items.Add(Item(l, ("maHang", "maHang", false), ("size", "size", false), ("viTri", "viTri", false), ("hopDong", "hopDong", false),
                ("hanDung", "hanDung", false), ("soLuong", "soLuong", false), ("net", "net", true)));
        }
        return res;
    }

    private static string FirstNonEmpty(params string[] s) => s.FirstOrDefault(x => x.Length > 0) ?? "";

    public void Load()
    {
        if (!CanView) throw new BusinessException("Tài khoản không có quyền xem Phiếu xuất hệ thống.");
        Errors.Clear();
        var all = new List<Slip>();
        all.AddRange(LoadM1());
        all.AddRange(LoadM2());
        all.AddRange(LoadSoKho("m3", "nxt-bot-sot"));
        All = all;
    }

    // ------------------------------------------------------------------ lọc + sắp xếp

    public sealed class Filter
    {
        public string From = "", To = "", Query = "", Src = "", Nguon = "", Nguoi = "";
        public string SortKey = "ts";
        public int SortDir = -1;
    }

    private static readonly CompareInfo ViCmp = CultureInfo.GetCultureInfo("vi-VN").CompareInfo;
    private static int Cmp(string a, string b) => ViCmp.Compare(a, b, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) is var c && c != 0 ? c : NumCmp(a, b);
    private static int NumCmp(string a, string b) => string.CompareOrdinal(a, b);

    public static List<Slip> Apply(IEnumerable<Slip> all, Filter f)
    {
        var q = f.Query.Trim().ToLowerInvariant();
        var view = all.Where(s =>
        {
            if (f.From.Length > 0 && (s.DayIso.Length == 0 || string.CompareOrdinal(s.DayIso, f.From) < 0)) return false;
            if (f.To.Length > 0 && (s.DayIso.Length == 0 || string.CompareOrdinal(s.DayIso, f.To) > 0)) return false;
            if (f.Src.Length > 0 && s.Src != f.Src) return false;
            if (f.Nguon == "pc" && s.Phone) return false;
            if (f.Nguon == "dt" && !s.Phone) return false;
            if (f.Nguoi.Length > 0 && s.NguoiTao != f.Nguoi) return false;
            if (q.Length > 0)
            {
                var hay = (s.MaPhieu + " " + s.GhiChu + " " + s.NguoiTao + " " + string.Join(" ", s.Meta.Select(m => m.V))).ToLowerInvariant();
                if (!hay.Contains(q) && !s.Items.Any(it => SearchKeys.Any(k => it.TryGetValue(k, out var v) && v.ToLowerInvariant().Contains(q)))) return false;
            }
            return true;
        }).ToList();
        int KeyCmp(Slip a, Slip b) => f.SortKey switch
        {
            "ts" => a.Ts.CompareTo(b.Ts),
            "soMH" => a.SoMH.CompareTo(b.SoMH),
            "module" => Cmp(SrcLabel(a.Src) + a.Src, SrcLabel(b.Src) + b.Src),
            "nguon" => a.Phone.CompareTo(b.Phone),
            "maPhieu" => NaturalCmp(a.MaPhieu, b.MaPhieu),
            "nguoiTao" => Cmp(a.NguoiTao, b.NguoiTao),
            "ghiChu" => Cmp(a.GhiChu, b.GhiChu),
            _ => 0,
        };
        view.Sort((a, b) =>
        {
            var c = KeyCmp(a, b);
            if (c != 0) return c * f.SortDir;
            c = b.Ts.CompareTo(a.Ts);
            return c != 0 ? c : NaturalCmp(a.MaPhieu, b.MaPhieu);
        });
        return view;
    }

    /// <summary>So sánh có số tự nhiên: "PX2" &lt; "PX10".</summary>
    public static int NaturalCmp(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsDigit(a[i])) i++;
                while (j < b.Length && char.IsDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0'); var nb = b[sj..j].TrimStart('0');
                if (na.Length != nb.Length) return na.Length.CompareTo(nb.Length);
                var c = string.CompareOrdinal(na, nb);
                if (c != 0) return c;
            }
            else
            {
                var c = ViCmp.Compare(a[i].ToString(), b[j].ToString(), CompareOptions.IgnoreCase);
                if (c != 0) return c;
                i++; j++;
            }
        }
        return (a.Length - i).CompareTo(b.Length - j);
    }

    /// <summary>Giá trị ô chi tiết đã định dạng (mã bỏ "TPC-", số có phân cách nghìn).</summary>
    public static string ItemText(Dictionary<string, string> it, Col c)
    {
        var v = it.GetValueOrDefault(c.Key) ?? "";
        if (CodeKeys.Contains(c.Key)) v = Regex.Replace(v, @"^\s*TPC-", "", RegexOptions.IgnoreCase);
        if (c.Num) return v.Length == 0 ? "" : FmtNum(N(v));
        return v;
    }
    public static string FmtNum(double d) => d.ToString("#,0.###", CultureInfo.GetCultureInfo("en-US"));

    // ------------------------------------------------------------------ xuất Excel

    /// <summary>Sheet 1 = danh sách phiếu đang lọc, sheet 2 = toàn bộ dòng hàng của các phiếu đó.</summary>
    public void Export(IReadOnlyList<Slip> slips, string path)
    {
        if (!CanExport) throw new BusinessException("Tài khoản không có quyền xuất file Phiếu xuất hệ thống.");
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Phiếu xuất");
        string[] head = { "Mã phiếu", "Ngày tạo", "Module", "Nguồn", "Người tạo", "Số mặt hàng", "Tổng SL xuất", "Ghi chú" };
        for (int c = 0; c < head.Length; c++) ws.Cell(1, c + 1).Value = head[c];
        int r = 2;
        foreach (var s in slips)
        {
            ws.Cell(r, 1).Value = s.MaPhieu;
            if (s.Date is { } d) { ws.Cell(r, 2).Value = d; ws.Cell(r, 2).Style.DateFormat.Format = s.HasTime ? "dd/mm/yyyy hh:mm" : "dd/mm/yyyy"; }
            ws.Cell(r, 3).Value = s.ModuleText;
            ws.Cell(r, 4).Value = s.NguonText;
            ws.Cell(r, 5).Value = s.NguoiTao;
            ws.Cell(r, 6).Value = s.SoMH;
            var qtyKey = Cols[s.Src].FirstOrDefault(c => c.Sum)?.Key;
            if (qtyKey is not null) ws.Cell(r, 7).Value = s.Sum(qtyKey);
            ws.Cell(r, 8).Value = s.GhiChu;
            r++;
        }
        Style(ws, head.Length, r - 1);

        var wi = wb.AddWorksheet("Dòng hàng");
        string[] h2 = { "Mã phiếu", "Ngày tạo", "Module", "Mã hàng", "Tên hàng", "Size", "Lô / Phiếu nhập", "Vị trí", "Hợp đồng", "SL xuất", "Khối lượng (kg)" };
        for (int c = 0; c < h2.Length; c++) wi.Cell(1, c + 1).Value = h2[c];
        r = 2;
        foreach (var s in slips)
            foreach (var it in s.Items)
            {
                string G(params string[] keys) => keys.Select(k => it.GetValueOrDefault(k) ?? "").FirstOrDefault(v => v.Length > 0) ?? "";
                wi.Cell(r, 1).Value = s.MaPhieu;
                if (s.Date is { } d) { wi.Cell(r, 2).Value = d.Date; wi.Cell(r, 2).Style.DateFormat.Format = "dd/mm/yyyy"; }
                wi.Cell(r, 3).Value = s.ModuleText;
                wi.Cell(r, 4).Value = Regex.Replace(G("maHang", "maGoc", "ma", "code"), @"^\s*TPC-", "", RegexOptions.IgnoreCase);
                wi.Cell(r, 5).Value = G("tenHang", "ten");
                wi.Cell(r, 6).Value = G("size");
                wi.Cell(r, 7).Value = G("soLo", "lo", "phieuNhap");
                wi.Cell(r, 8).Value = G("vt", "viTri");
                wi.Cell(r, 9).Value = G("hopDong");
                var qty = G("qty", "soLuong");
                if (qty.Length > 0) wi.Cell(r, 10).Value = N(qty);
                var kg = G("net", "tlTru");
                if (kg.Length > 0) wi.Cell(r, 11).Value = N(kg);
                r++;
            }
        Style(wi, h2.Length, r - 1);
        wb.SaveInPlace(path);
    }

    private static void Style(IXLWorksheet ws, int cols, int lastRow)
    {
        var h = ws.Range(1, 1, 1, cols);
        h.Style.Font.Bold = true; h.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCE6F0");
        if (lastRow >= 1) ws.Range(1, 1, Math.Max(1, lastRow), cols).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        if (lastRow >= 2) ws.Range(1, 1, lastRow, cols).SetAutoFilter();
        ws.SheetView.FreezeRows(1);
        ws.Columns(1, cols).AdjustToContents(1, Math.Min(lastRow, 500), 8, 50);
    }
}
