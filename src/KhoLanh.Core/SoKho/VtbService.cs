using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.TonKho;

namespace KhoLanh.Core.SoKho;

/// <summary>1 dòng tồn Vị Trí Bột (tên cột lưu giống app cũ: VT, MÃ HÀNG, SL, NSX, HSD, NHẬP, NGÀY XUẤT, VỊ TRÍ CŨ, VỊ TRÍ MỚI, GHI CHÚ).</summary>
public sealed class VtbRow
{
    public string Vt = "", Ma = "", Nsx = "", Hsd = "", Nhap = "", NgayXuat = "", VtCu = "", VtMoi = "", GhiChu = "";
    public double Sl;
}

public sealed class VtbLine
{
    public string Key = "", Vt = "", Ma = "", Nsx = "", Hsd = "", Nhap = "", GhiChu = "";
    public double Qty;
}

/// <summary>Phiếu xuất Vị Trí Bột (từ PC hoặc điện thoại) — lưu trừ thẳng cột SL; hủy trừ / trừ lại / xóa.</summary>
public sealed class VtbSlip
{
    public string Id = "", PhoneId = "", MaPhieu = "", NguoiTao = "", GhiChu = "", CreatedAt = "", DeductedAt = "", Nguon = "";
    public bool Deducted;
    public double LoadAt;
    public List<VtbLine> Lines = new();
    public JsonObject Extra = new();   // giữ nguyên các trường lạ của app cũ / điện thoại
    public bool IsPhone => PhoneId.Length > 0 || Nguon == "phone";
}

/// <summary>
/// VỊ TRÍ BỘT (M01VTB của app cũ): tồn bột / sốt theo vị trí cho điện thoại xem và xuất. Từ PC 1.62 tồn được ĐẨY từ NXT Bột/Sốt › Tồn theo vị trí
/// (thay toàn bộ dữ liệu cũ; phiếu của đợt cũ bị khóa); phiếu xuất (PC / điện thoại) trừ thẳng cột SL. Database "ton-kho-an-an",
/// sheet ViTriBotMeta (1 ô JSON) + ViTriBot (dòng) + ViTriBotPhieuDienThoai (1 ô JSON {slips, seq}).
/// </summary>
public sealed class VtbService
{
    public const string PermKey = "m1vtb", Db = "ton-kho-an-an", SMeta = "ViTriBotMeta", SRows = "ViTriBot", SSlips = "ViTriBotPhieuDienThoai";
    private static readonly (string f, string col)[] Cols = { ("vt", "VT"), ("ma", "MÃ HÀNG"), ("sl", "SL"), ("nsx", "NSX"), ("hsd", "HSD"), ("nhap", "NHẬP"), ("ngayXuat", "NGÀY XUẤT"), ("vtCu", "VỊ TRÍ CŨ"), ("vtMoi", "VỊ TRÍ MỚI"), ("ghiChu", "GHI CHÚ") };
    private static readonly JsonSerializerOptions J = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService? _audit;
    public Func<DateTime> Now { get; set; } = () => DateTime.Now;
    public VtbService(Func<ISheetSource> src, Session session, AuditService? audit = null) { _src = src; _session = session; _audit = audit; }

    public bool CanView => _session.Can(PermKey, Perm.View) || _session.Can(SoKhoService.PermKey, Perm.View);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);

    public List<VtbRow> Rows { get; private set; } = new();
    public List<VtbSlip> Slips { get; private set; } = new();
    public int Seq { get; private set; } = 1;
    public string FileName { get; private set; } = "";
    public string DataDate { get; private set; } = "";
    public string Sheet { get; private set; } = "";
    public double ImportedAt { get; private set; }
    private JsonObject _meta = new();

    private JsonArray Read(string sheet) { try { return JsonNode.Parse(_src().Read(Db, sheet)) as JsonArray ?? new(); } catch { return new(); } }
    private static string S(JsonNode? n) => AnAnStore.S(n);
    private static JsonNode? Cell(JsonArray a)
    {
        if (a.Count == 0 || a[0] is not JsonObject o) return null;
        var t = S(o["data"]); if (!t.StartsWith(AnAnStore.JsonTag, StringComparison.Ordinal)) return null;
        try { return JsonNode.Parse(t[AnAnStore.JsonTag.Length..]); } catch { return null; }
    }
    private static bool Bool(JsonNode? n) { if (n is JsonValue v && v.TryGetValue<bool>(out var b)) return b; return S(n) == "true"; }
    private static double N(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : JsText.LooseNum(S(n)) is var x && !double.IsNaN(x) ? x : 0;

    public void Load()
    {
        var m = Cell(Read(SMeta)) as JsonObject;
        _meta = m ?? new JsonObject();
        FileName = S(_meta["fileName"]); DataDate = S(_meta["dataDate"]); Sheet = S(_meta["sheet"]); ImportedAt = N(_meta["importedAt"]);
        Rows = m is null ? new() : Read(SRows).OfType<JsonObject>().Select(FromStored).Where(r => r.Vt.Length > 0 || r.Ma.Length > 0).ToList();
        var p = Cell(Read(SSlips)) as JsonObject;
        Seq = p?["seq"] is { } sq ? (int)N(sq) : 1; if (Seq < 1) Seq = 1;
        Slips = (p?["slips"] as JsonArray ?? new()).OfType<JsonObject>().Select(SlipFrom).ToList();
    }

    private static VtbRow FromStored(JsonObject o)
    {
        string G(string col) => S(o[col]).Trim();
        return new VtbRow { Vt = G("VT"), Ma = G("MÃ HÀNG"), Sl = N(o["SL"]), Nsx = FmtDate(G("NSX")), Hsd = FmtDate(G("HSD")), Nhap = FmtDate(G("NHẬP")), NgayXuat = FmtDate(G("NGÀY XUẤT")), VtCu = G("VỊ TRÍ CŨ"), VtMoi = G("VỊ TRÍ MỚI"), GhiChu = G("GHI CHÚ") };
    }
    private static JsonObject ToStored(VtbRow r) => new()
    {
        ["VT"] = r.Vt, ["MÃ HÀNG"] = r.Ma, ["SL"] = r.Sl, ["NSX"] = r.Nsx, ["HSD"] = r.Hsd, ["NHẬP"] = r.Nhap, ["NGÀY XUẤT"] = r.NgayXuat, ["VỊ TRÍ CŨ"] = r.VtCu, ["VỊ TRÍ MỚI"] = r.VtMoi, ["GHI CHÚ"] = r.GhiChu,
    };

    private static VtbSlip SlipFrom(JsonObject o)
    {
        var s = new VtbSlip
        {
            Id = S(o["id"]), PhoneId = S(o["phoneId"]), MaPhieu = S(o["maPhieu"]), NguoiTao = S(o["nguoiTao"]), GhiChu = S(o["ghiChu"]), CreatedAt = S(o["createdAt"]),
            DeductedAt = S(o["deductedAt"]), Nguon = S(o["nguon"]), Deducted = Bool(o["deducted"]), LoadAt = N(o["loadAt"]),
            Extra = (JsonObject)o.DeepClone(),
        };
        foreach (var l in (o["lines"] as JsonArray ?? new()).OfType<JsonObject>())
            s.Lines.Add(new VtbLine { Key = S(l["key"]), Vt = S(l["vt"]), Ma = S(l["ma"]), Nsx = S(l["nsx"]), Hsd = S(l["hsd"]), Nhap = S(l["nhap"]), GhiChu = S(l["ghiChu"]), Qty = N(l["qty"]) });
        return s;
    }
    private static JsonObject SlipTo(VtbSlip s)
    {
        var o = (JsonObject)s.Extra.DeepClone();
        o["id"] = s.Id; o["phoneId"] = s.PhoneId; o["maPhieu"] = s.MaPhieu; o["nguoiTao"] = s.NguoiTao; o["ghiChu"] = s.GhiChu; o["createdAt"] = s.CreatedAt;
        o["deducted"] = s.Deducted; o["deductedAt"] = s.DeductedAt; o["loadAt"] = s.LoadAt; if (s.Nguon.Length > 0) o["nguon"] = s.Nguon;
        o["lines"] = new JsonArray(s.Lines.Select(l => (JsonNode)new JsonObject { ["key"] = l.Key, ["vt"] = l.Vt, ["ma"] = l.Ma, ["nsx"] = l.Nsx, ["hsd"] = l.Hsd, ["nhap"] = l.Nhap, ["ghiChu"] = l.GhiChu, ["qty"] = l.Qty }).ToArray());
        return o;
    }

    private JsonArray MetaCell() => AnAnStore.JsonCell(_meta);
    private JsonArray SlipCell() => AnAnStore.JsonCell(new JsonObject { ["slips"] = new JsonArray(Slips.Select(s => (JsonNode)SlipTo(s)).ToArray()), ["seq"] = Seq });
    private void Save(bool rows, bool slips)
    {
        var d = new Dictionary<string, JsonArray?>();
        if (rows) { d[SMeta] = Rows.Count > 0 || FileName.Length > 0 ? MetaCell() : new JsonArray(); d[SRows] = new JsonArray(Rows.Select(r => (JsonNode)ToStored(r)).ToArray()); }
        if (slips) d[SSlips] = SlipCell();
        _src().Write(Db, d);
    }

    /// <summary>Khóa dòng (giống app cũ): Mã|VT|NSX|HSD#thứ tự trùng — để phiếu tìm đúng dòng.</summary>
    public static List<string> Keys(IList<VtbRow> rows)
    {
        var seen = new Dictionary<string, int>();
        return rows.Select(r =>
        {
            var ma = r.Ma.Trim(); var vt = r.Vt.Trim(); if (ma.Length == 0 && vt.Length == 0) return "";
            var b = string.Join("|", ma, vt, r.Nsx.Trim(), r.Hsd.Trim()); var n = seen.GetValueOrDefault(b); seen[b] = n + 1; return b + "#" + n;
        }).ToList();
    }

    /// <summary>Phiếu của đợt dữ liệu cũ (đã đẩy / nạp lại tồn) — khóa, không trừ / hoàn được nữa.</summary>
    public bool Locked(VtbSlip s) => Math.Abs(s.LoadAt - ImportedAt) > 0.5;

    private int ApplySlip(VtbSlip s, int sign)
    {
        var idx = Keys(Rows).Select((k, i) => (k, i)).Where(x => x.k.Length > 0).GroupBy(x => x.k).ToDictionary(g => g.Key, g => g.First().i);
        var missing = 0;
        foreach (var l in s.Lines) { if (!idx.TryGetValue(l.Key, out var i)) { missing++; continue; } Rows[i].Sl = Math.Round(Rows[i].Sl + sign * l.Qty, 6); }
        return missing;
    }

    // ------------------------------------------------------------ đẩy tồn từ NXT Bột/Sốt
    public sealed record PushRow(string ViTri, string MaHang, string Size, double Sl, double Net);

    /// <summary>Tồn theo vị trí của sổ kho tại ngày <paramref name="toIso"/> (chỉ dòng SL &gt; 0).</summary>
    public static List<PushRow> FromLedger(IList<SkLine> lines, string toIso) =>
        SoKhoCore.Stock(lines, toIso, "viTri", "maHang", "size").Where(r => r.Sl > 1e-9).Select(r => new PushRow(r["viTri"], r["maHang"], r["size"], r.Sl, r.Net))
            .OrderBy(r => r.ViTri, StringComparer.Ordinal).ThenBy(r => r.MaHang, StringComparer.Ordinal).ToList();

    /// <summary>THAY toàn bộ dữ liệu Vị Trí Bột bằng tồn theo vị trí của NXT Bột/Sốt (phiếu đợt cũ bị khóa).</summary>
    public int Push(IList<PushRow> rows, string toIso)
    {
        if (!_session.Can(SoKhoService.PermKey, Perm.Edit) && !CanEdit) throw new BusinessException("Bạn không có quyền đưa tồn lên Vị Trí Bột.");
        if (rows.Count == 0) throw new BusinessException("Không có dòng tồn cuối > 0 để đẩy (kiểm tra lại ngày).");
        static string Nf(double v) => v.ToString("#,0.###", CultureInfo.InvariantCulture);
        Rows = rows.Select(r => new VtbRow { Vt = r.ViTri, Ma = r.MaHang + (r.Size.Length > 0 ? " " + r.Size : ""), Sl = r.Sl, GhiChu = $"NXT Bột/Sốt · {Nf(r.Net)} kg" }).ToList();
        var dmy = DateTime.TryParseExact(toIso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : toIso;
        ImportedAt = new DateTimeOffset(Now()).ToUnixTimeMilliseconds();
        _meta = new JsonObject { ["fileName"] = "NXT Bột/Sốt — tồn cuối " + dmy, ["importedAt"] = ImportedAt, ["dataDate"] = dmy, ["sheet"] = "NXT Bột/Sốt · Theo vị trí", ["headerRowNumber"] = 0, ["skippedRows"] = 0 };
        FileName = S(_meta["fileName"]); DataDate = dmy; Sheet = S(_meta["sheet"]);
        Save(true, false);
        _audit?.Log(_session.Username, "Vị Trí Bột: Đưa tồn theo vị trí lên Vị Trí Bột", $"{Rows.Count} dòng · tồn cuối {dmy}");
        return Rows.Count;
    }

    // ------------------------------------------------------------ phiếu
    public sealed record Pick(int Row, double Qty);

    /// <summary>Tạo phiếu xuất từ PC: lưu phiếu VÀ trừ ngay cột SL.</summary>
    public VtbSlip CreateSlip(IList<Pick> picks, string ghiChu)
    {
        if (!CanEdit) throw new BusinessException("Bạn chỉ có quyền xem.");
        var keys = Keys(Rows); var lines = new List<VtbLine>();
        foreach (var p in picks)
        {
            if (p.Row < 0 || p.Row >= Rows.Count) continue; if (!(p.Qty > 0)) continue;
            var r = Rows[p.Row];
            if (p.Qty > r.Sl + 1e-9) throw new BusinessException($"Dòng {r.Vt} · {r.Ma}: xuất {p.Qty} lớn hơn tồn {r.Sl}.");
            lines.Add(new VtbLine { Key = keys[p.Row], Vt = r.Vt, Ma = r.Ma, Nsx = r.Nsx, Hsd = r.Hsd, Nhap = r.Nhap, GhiChu = r.GhiChu, Qty = p.Qty });
        }
        if (lines.Count == 0) throw new BusinessException("Chưa chọn dòng / số lượng xuất.");
        var now = Now();
        var s = new VtbSlip
        {
            Id = "VTB-" + Seq.ToString("0000", CultureInfo.InvariantCulture), MaPhieu = "PC-" + now.ToString("ddMMyy-HHmm", CultureInfo.InvariantCulture), NguoiTao = _session.Username, GhiChu = ghiChu.Trim(),
            CreatedAt = now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"), Lines = lines, Deducted = true, DeductedAt = now.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"), LoadAt = ImportedAt, Nguon = "pc",
        };
        Seq++;
        ApplySlip(s, -1);
        Slips.Insert(0, s);
        Save(true, true);
        _audit?.Log(_session.Username, "Vị Trí Bột: Tạo phiếu xuất Vị Trí Bột", $"{s.Id} · {lines.Count} dòng");
        return s;
    }

    /// <summary>Hủy trừ (hoàn SL) / trừ lại. Trả số dòng không còn tìm thấy.</summary>
    public int SetDeducted(string id, bool on)
    {
        if (!CanEdit) throw new BusinessException("Bạn chỉ có quyền xem.");
        var s = Slips.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không thấy phiếu.");
        if (Locked(s)) throw new BusinessException("Phiếu thuộc đợt dữ liệu cũ (tồn đã được đưa lại) — đã khóa, không trừ / hoàn được nữa.");
        if (s.Deducted == on) return 0;
        var miss = ApplySlip(s, on ? -1 : 1);
        s.Deducted = on; s.DeductedAt = on ? DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'") : "";
        Save(true, true);
        _audit?.Log(_session.Username, on ? "Vị Trí Bột: Trừ lại phiếu" : "Vị Trí Bột: Hủy trừ phiếu", s.Id);
        return miss;
    }

    /// <summary>Xóa phiếu (đang trừ và chưa khóa → hoàn SL trước).</summary>
    public void DeleteSlip(string id)
    {
        if (!CanEdit) throw new BusinessException("Bạn chỉ có quyền xem.");
        var s = Slips.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không thấy phiếu.");
        var rows = s.Deducted && !Locked(s);
        if (rows) ApplySlip(s, 1);
        Slips.Remove(s);
        Save(rows, true);
        _audit?.Log(_session.Username, "Vị Trí Bột: Xóa phiếu Vị Trí Bột", s.Id);
    }

    // ------------------------------------------------------------ ngày dd/mm/yyyy (giống vtbFmtDate)
    public static string FmtDate(string? v)
    {
        var t = (v ?? "").Trim(); if (t.Length == 0) return "";
        static bool Valid(int d, int m, int y) => m >= 1 && m <= 12 && d >= 1 && y >= 1 && y <= 9999 && d <= DateTime.DaysInMonth(y, m);
        static string Fmt(int d, int m, int y) => $"{d:00}/{m:00}/{y:0000}";
        if (Regex.IsMatch(t, @"^\d{4,6}$") && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && n > 0)
        {
            var dd = new DateTime(1899, 12, 30).AddDays(Math.Round(n));
            if (dd.Year >= 1990 && dd.Year <= 2100) return Fmt(dd.Day, dd.Month, dd.Year);
        }
        var m = Regex.Match(t, @"^(\d{2})(\d{2})(\d{4})$");
        if (m.Success) { int d = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value), y = int.Parse(m.Groups[3].Value); if (Valid(d, mo, y)) return Fmt(d, mo, y); }
        m = Regex.Match(t, @"^(\d{4})\s*[.\-/]\s*(\d{1,2})\s*[.\-/]\s*(\d{1,2})");
        if (m.Success) { int y = int.Parse(m.Groups[1].Value), mo = int.Parse(m.Groups[2].Value), d = int.Parse(m.Groups[3].Value); if (Valid(d, mo, y)) return Fmt(d, mo, y); }
        m = Regex.Match(t, @"^(\d{1,2})\s*[.\-/]\s*(\d{1,2})\s*[.\-/]\s*(\d{4}|\d{2})(?!\d)");
        if (m.Success)
        {
            int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value), y = int.Parse(m.Groups[3].Value); if (y < 100) y += 2000;
            int d = a, mo = b; if (b > 12 && a <= 12) { d = b; mo = a; }
            if (Valid(d, mo, y)) return Fmt(d, mo, y);
        }
        return t;
    }
}
