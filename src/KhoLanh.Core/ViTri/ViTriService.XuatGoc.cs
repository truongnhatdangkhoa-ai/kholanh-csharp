using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KhoLanh.Core.DoiChieu;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using KhoLanh.Core.TonKho;

namespace KhoLanh.Core.ViTri;

/// <summary>
/// 1.160 (người dùng: "thêm nạp file excel cho tab Tra cứu đã xuất; file lấy từ phần mềm kho gốc; tách 2 loại dữ liệu: app tạo và nạp từ Excel"):
/// báo cáo "Kho - 07-Nhập xuất chi tiết" của phần mềm kho gốc. Lưu RIÊNG (sheet <see cref="SheetXuatGoc"/>) — không lẫn với phiếu do app tạo
/// (<see cref="Slips"/>); nạp nhiều lần thì CỘNG DỒN (dòng đã có không nhân đôi), không ảnh hưởng tồn theo vị trí.
/// </summary>
public sealed partial class ViTriService
{
    public const string SheetXuatGoc = "XuatGoc", SheetXuatGocMeta = "XuatGocMeta";

    public sealed class XuatGocRow
    {
        public string Ngay = "", SoLenh = "", ItemId = "", MaGoc = "", Ten = "", Dvt = "", NgaySx = "", Vt = "", LoaiCt = "", GhiChu = "", Dt = "", Hd = "", Khach = "";
        public double Sl, Tl, Net;
        internal string Key => string.Join("|", Ngay, SoLenh, ItemId, MaGoc, Vt, NgaySx, Math.Round(Sl, 3).ToString("R", CultureInfo.InvariantCulture), LoaiCt);
    }
    public sealed class XuatGocMeta
    {
        public string FileName { get; set; } = "";
        public double? ImportedAt { get; set; }
        public int Count { get; set; }
        public string From { get; set; } = "";
        public string To { get; set; } = "";
    }
    public sealed class XuatGocFile { public string FileName = "", Sheet = ""; public List<XuatGocRow> Rows = new(); public int Skipped; }
    public sealed record XuatGocResult(int Added, int Duplicate, int Total, string From, string To);

    public List<XuatGocRow> XuatGoc { get; private set; } = new();
    public XuatGocMeta XuatGocInfo { get; private set; } = new();

    private static readonly (string F, string[] Alias)[] XgAliases =
    {
        ("ma", new[] { "mahang" }), ("ngay", new[] { "ngaychungtu" }), ("vt", new[] { "vitri" }), ("sl", new[] { "soluongton", "soluong", "sl" }),
        ("ten", new[] { "tenhang" }), ("lenh", new[] { "solenh" }), ("id", new[] { "itemid" }), ("tl", new[] { "tlgnet" }), ("net", new[] { "net" }),
        ("dvt", new[] { "dvt" }), ("sx", new[] { "ngaysx" }), ("loai", new[] { "loaichungtu" }), ("gc", new[] { "ghichuchungtu" }),
        ("dt", new[] { "dactinh" }), ("hd", new[] { "hopdong" }), ("kh", new[] { "khachhang" }),
    };
    private static readonly (string F, string Label)[] XgRequired = { ("ma", "Mã hàng"), ("ngay", "Ngày chứng từ"), ("vt", "Vị trí"), ("sl", "Số lượng tồn") };

    private void LoadXuatGoc()
    {
        var meta = JsonCell(Read(Db, SheetXuatGocMeta));
        XuatGocInfo = meta is null ? new XuatGocMeta() : (Try(() => JsonSerializer.Deserialize<XuatGocMeta>(meta, Json)) ?? new XuatGocMeta());
        XuatGoc = Read(Db, SheetXuatGoc).OfType<JsonObject>().Select(XgFromStored).Where(r => r.MaGoc.Length > 0).ToList();
    }
    private static JsonObject XgToStored(XuatGocRow r) => new()
    {
        ["ng"] = r.Ngay, ["sl"] = r.SoLenh, ["id"] = r.ItemId, ["ma"] = r.MaGoc, ["ten"] = r.Ten, ["dvt"] = r.Dvt, ["sx"] = r.NgaySx, ["vt"] = r.Vt,
        ["q"] = r.Sl, ["tl"] = r.Tl, ["net"] = r.Net, ["ct"] = r.LoaiCt, ["gc"] = r.GhiChu, ["dt"] = r.Dt, ["hd"] = r.Hd, ["kh"] = r.Khach,
    };
    private static XuatGocRow XgFromStored(JsonObject o)
    {
        double D(string k) => double.TryParse(S(o[k]), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
        return new XuatGocRow
        {
            Ngay = S(o["ng"]), SoLenh = S(o["sl"]), ItemId = S(o["id"]), MaGoc = S(o["ma"]), Ten = S(o["ten"]), Dvt = S(o["dvt"]), NgaySx = S(o["sx"]), Vt = S(o["vt"]),
            Sl = D("q"), Tl = D("tl"), Net = D("net"), LoaiCt = S(o["ct"]), GhiChu = S(o["gc"]), Dt = S(o["dt"]), Hd = S(o["hd"]), Khach = S(o["kh"]),
        };
    }

    /// <summary>Ngày trong ô Excel → yyyy-MM-dd ("" nếu không hiểu). Nhận số ngày của Excel, yyyy-MM-dd, dd/MM/yyyy (kiểu Việt), M/d/yyyy.</summary>
    public static string XgDate(string? s)
    {
        s = (s ?? "").Trim(); if (s.Length == 0) return "";
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 20000 && n < 80000) return DateTime.FromOADate(Math.Floor(n)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var f = new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss", "dd/MM/yyyy", "d/M/yyyy", "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:mm:ss", "dd/MM/yyyy HH:mm", "M/d/yyyy", "M/d/yyyy h:mm:ss tt", "M/d/yyyy H:mm:ss" };
        return DateTime.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "";
    }
    private static string XgClean(string? s) => (s ?? "").Replace("_x000d_", "").Replace("\r", "").Replace("\n", " ").Trim();

    /// <summary>Đọc file "Báo cáo - Kho - Nhập xuất chi tiết" (tiêu đề tự tìm trong 30 dòng đầu; cần Mã hàng, Ngày chứng từ, Vị trí, Số lượng tồn).</summary>
    public XuatGocFile ReadXuatGoc(string path)
    {
        RequireEdit();
        using var _perf = KhoLanh.Core.TroLy.Perf.Measure("Đọc file Excel", "Tra cứu đã xuất (kho gốc)");
        SheetText.Book book;
        try { book = SheetText.ReadXlsxFast(path, rawNumbers: true, fromA1: true); }
        catch { book = SheetText.Read(path); }
        (string sheet, List<string[]> t, int r, Dictionary<string, int> map, int score)? best = null;
        foreach (var name in book.SheetNames)
        {
            var t = book.Sheets[name]; if (t.Count == 0) continue;
            for (var r = 1; r <= Math.Min(t.Count, 30); r++)
            {
                var keys = t[r - 1].Select(HeaderKey).ToList();
                var map = new Dictionary<string, int>();
                foreach (var (f, al) in XgAliases) { var i = keys.FindIndex(k => al.Contains(k)); if (i >= 0) map[f] = i; }
                var score = XgRequired.Count(x => map.ContainsKey(x.F));
                if (best is null || score > best.Value.score) best = (name, t, r, map, score);
                if (score == XgRequired.Length) break;
            }
            if (best?.score == XgRequired.Length) break;
        }
        if (best is not { } b || b.score < XgRequired.Length)
        {
            var miss = XgRequired.Where(x => best is null || !best.Value.map.ContainsKey(x.F)).Select(x => x.Label);
            throw new BusinessException($"File “{Path.GetFileName(path)}” không giống báo cáo “Nhập xuất chi tiết” của phần mềm kho gốc.\n\nKhông tìm thấy cột: {string.Join(", ", miss)}.\nCần tối thiểu: Mã hàng, Ngày chứng từ, Vị trí, Số lượng tồn.");
        }
        var o = new XuatGocFile { FileName = Path.GetFileName(path), Sheet = b.sheet };
        string Txt(string[] row, string f) => b.map.TryGetValue(f, out var c) && c < row.Length ? XgClean(row[c]) : "";
        double Num(string[] row, string f)
        {
            var s = Txt(row, f); if (s.Length == 0) return 0;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : DcxEngine.ParseNum(s) ?? 0;
        }
        for (var r = b.r; r < b.t.Count; r++)
        {
            var row = b.t[r];
            var ma = Txt(row, "ma"); if (ma.Length == 0) { if (row.Any(x => !string.IsNullOrWhiteSpace(x))) o.Skipped++; continue; }   // dòng tổng cuối file không có mã
            var ngay = XgDate(Txt(row, "ngay")); if (ngay.Length == 0) { o.Skipped++; continue; }
            var sl = Num(row, "sl"); var net = Num(row, "net"); var tl = Num(row, "tl"); if (tl == 0 && sl != 0) tl = net / sl;
            if (net == 0) net = sl * tl;
            var sx = XgDate(Txt(row, "sx"));
            o.Rows.Add(new XuatGocRow
            {
                Ngay = ngay, SoLenh = Txt(row, "lenh"), ItemId = Txt(row, "id"), MaGoc = ma, Ten = Txt(row, "ten"), Dvt = Txt(row, "dvt"), NgaySx = sx, Vt = Txt(row, "vt"),
                Sl = sl, Tl = Math.Round(tl, 4), Net = Math.Round(net, 3), LoaiCt = Txt(row, "loai"), GhiChu = Txt(row, "gc"), Dt = Txt(row, "dt"), Hd = Txt(row, "hd"), Khach = Txt(row, "kh"),
            });
        }
        if (o.Rows.Count == 0) throw new BusinessException($"File “{o.FileName}” không có dòng xuất nào đọc được.");
        return o;
    }

    /// <summary>Cộng dồn vào dữ liệu Excel đã có: mỗi dòng giống hệt (ngày · số lệnh · mã · vị trí · ngày SX · SL · loại) chỉ giữ số lần nhiều hơn giữa 2 bên → nạp lại file chồng nhau không nhân đôi.</summary>
    public XuatGocResult ImportXuatGoc(XuatGocFile f)
    {
        RequireEdit();
        var have = XuatGoc.GroupBy(r => r.Key).ToDictionary(g => g.Key, g => g.Count());
        var seen = new Dictionary<string, int>(); var added = 0; var dup = 0;
        var merged = new List<XuatGocRow>(XuatGoc);
        foreach (var r in f.Rows)
        {
            var k = r.Key; seen[k] = seen.GetValueOrDefault(k) + 1;
            if (seen[k] <= have.GetValueOrDefault(k)) { dup++; continue; }
            merged.Add(r); added++;
        }
        XuatGoc = merged.OrderBy(r => r.Ngay, StringComparer.Ordinal).ToList();
        XuatGocInfo = new XuatGocMeta
        {
            FileName = f.FileName, ImportedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Count = XuatGoc.Count,
            From = XuatGoc.Count > 0 ? XuatGoc[0].Ngay : "", To = XuatGoc.Count > 0 ? XuatGoc[^1].Ngay : "",
        };
        PersistXuatGoc();
        Log("nạp Excel kho gốc (đã xuất)", $"{f.FileName}: +{added} dòng, {dup} dòng đã có");
        return new XuatGocResult(added, dup, XuatGoc.Count, XuatGocInfo.From, XuatGocInfo.To);
    }

    public void ClearXuatGoc()
    {
        RequireEdit();
        XuatGoc = new(); XuatGocInfo = new();
        PersistXuatGoc();
        Log("xóa dữ liệu Excel kho gốc (đã xuất)", "");
    }
    private void PersistXuatGoc() => _src().Write(Db, new Dictionary<string, JsonArray?>
    {
        [SheetXuatGoc] = new JsonArray(XuatGoc.Select(r => (JsonNode)XgToStored(r)).ToArray()),
        [SheetXuatGocMeta] = XuatGoc.Count > 0 ? JsonCellOf(XuatGocInfo) : new JsonArray(),
    });

    /// <summary>Dòng Excel kho gốc ở dạng chung với dòng phiếu app (cột Lô = ngày SX; Tồn = tổng tồn file ở đúng Mã · Vị trí).</summary>
    private List<DaXuat> XuatGocAsDaXuat(Dictionary<string, double> tonMv)
    {
        var o = new List<DaXuat>(XuatGoc.Count);
        foreach (var x in XuatGoc.OrderByDescending(r => r.Ngay, StringComparer.Ordinal))
        {
            var ma = ShortCode(x.MaGoc);
            var size = SizeOf(new Row { Ma = ma, Ten = x.Ten });
            var sx = x.NgaySx.Length > 0 ? Dmy(x.NgaySx) : "";
            o.Add(new DaXuat(x.Ngay, x.SoLenh, x.LoaiCt, x.MaGoc, ma, x.Ten, size, sx, x.Vt, x.Sl, x.Tl, "", false, true,
                Math.Round(tonMv.GetValueOrDefault(x.MaGoc.Trim() + "|" + x.Vt.Trim()), 3), "", "excel") { Dt = x.Dt, Hd = x.Hd, Khach = x.Khach, GhiChu = x.GhiChu });
        }
        return o;
    }

    /// <summary>Ghép dòng app ↔ dòng Excel (cùng mã · vị trí · SL, lệch ≤ 1 ngày): gán <see cref="DaXuat.DoiChieu"/>. Chỉ xét ngày nằm trong khoảng ngày của file Excel đã nạp.</summary>
    private static List<DaXuat> DoiChieuXuat(List<DaXuat> app, List<DaXuat> xl)
    {
        if (xl.Count == 0) return app;
        string From = xl.Min(x => x.Ngay), To = xl.Max(x => x.Ngay);
        static string K(DaXuat x) => ShortCode(x.MaGoc) + "|" + x.Vt.Trim().ToUpperInvariant() + "|" + Math.Round(x.Sl, 3).ToString("R", CultureInfo.InvariantCulture);
        static int Day(string ymd) => DateTime.TryParseExact(ymd, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? (int)(d - DateTime.UnixEpoch).TotalDays : int.MinValue;
        var pool = new Dictionary<string, List<int>>();
        for (var i = 0; i < xl.Count; i++) { if (!pool.TryGetValue(K(xl[i]), out var l)) pool[K(xl[i])] = l = new(); l.Add(i); }
        var xlOk = new bool[xl.Count]; var res = new List<DaXuat>(app.Count);
        foreach (var a in app)
        {
            var hit = false;
            if (pool.TryGetValue(K(a), out var l))
            {
                var da = Day(a.Ngay); var bi = -1; var bd = 2;
                foreach (var i in l) { var d = Math.Abs(Day(xl[i].Ngay) - da); if (d < bd) { bd = d; bi = i; if (d == 0) break; } }
                if (bi >= 0) { l.Remove(bi); xlOk[bi] = true; hit = true; }
            }
            var inRange = string.CompareOrdinal(a.Ngay, From) >= 0 && string.CompareOrdinal(a.Ngay, To) <= 0;
            res.Add(a with { DoiChieu = hit ? "Khớp Excel kho gốc" : inRange ? "Chưa thấy ở Excel" : "" });
        }
        for (var i = 0; i < xl.Count; i++) xl[i] = xl[i] with { DoiChieu = xlOk[i] ? "Khớp phiếu app" : "Chỉ có ở Excel" };
        return res;
    }
}
