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

    // ------------------------------------------------------------ nạp file "Vị trí Bột" (kiểu cũ — trước PC 1.62)
    public static readonly string[] TemplateColumns = { "VT", "MÃ HÀNG", "SL", "NSX", "HSD", "NHẬP", "NGÀY XUẤT", "VỊ TRÍ CŨ", "VỊ TRÍ MỚI", "GHI CHÚ" };
    private static readonly (string F, string[] Aliases)[] ColAliases =
    {
        ("vt", new[] { "vt", "vitri", "vitrikho" }), ("ma", new[] { "mahang", "mahh", "masanpham", "itemcode" }), ("sl", new[] { "sl", "soluong", "slton" }),
        ("nsx", new[] { "nsx", "ngaysanxuat" }), ("hsd", new[] { "hsd", "hansudung" }), ("nhap", new[] { "nhap", "ngaynhap" }), ("ngayXuat", new[] { "ngayxuat", "ngayxuatkho" }),
        ("vtCu", new[] { "vitricu" }), ("vtMoi", new[] { "vitrimoi" }), ("ghiChu", new[] { "ghichu" }),
    };
    /// <summary>Khóa tên cột (giống vtHeaderKey): bỏ dấu, đ → d, chỉ giữ chữ / số.</summary>
    public static string HeaderKey(object? h)
    {
        var d = (h is string hs ? hs : Convert.ToString(h, CultureInfo.InvariantCulture) ?? "").Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(); foreach (var ch in d) if (ch < '\u0300' || ch > '\u036f') sb.Append(ch);
        return Regex.Replace(sb.ToString().Replace('đ', 'd').Replace('Đ', 'd').ToLowerInvariant(), "[^a-z0-9]", "");
    }
    /// <summary>Nhận diện cột (giống vtbMapColumns): trường → chỉ số cột.</summary>
    public static Dictionary<string, int> MapColumns(IReadOnlyList<object?> row)
    {
        var keys = row.Select(HeaderKey).ToList(); var map = new Dictionary<string, int>();
        foreach (var (f, al) in ColAliases) { var i = keys.FindIndex(k => al.Contains(k)); if (i >= 0) map[f] = i; }
        return map;
    }
    private static string CellText(object? v) => v switch
    {
        null => "", DateTime dt => dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        double d => d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(CultureInfo.InvariantCulture) : d.ToString("R", CultureInfo.InvariantCulture),
        _ => (Convert.ToString(v, CultureInfo.InvariantCulture) ?? "").Trim(),
    };

    public sealed class FileParse { public bool Ok; public string Error = "", Sheet = "", DataDate = ""; public int HeaderRow, Skipped; public List<VtbRow> Rows = new(); }

    /// <summary>Đọc 1 sheet file "Vị trí Bột": dòng "NGÀY:" (ngày số liệu) rồi dòng tiêu đề (cần VT, MÃ HÀNG, SL); dòng không có cả VT lẫn MÃ HÀNG bị bỏ.</summary>
    public static FileParse ParseSheet(IReadOnlyList<IReadOnlyList<object?>> aoa, string sheet)
    {
        var res = new FileParse { Sheet = sheet };
        int hr = -1; Dictionary<string, int> map = new();
        for (var r = 0; r < Math.Min(aoa.Count, 25); r++) { var m = MapColumns(aoa[r]); if (m.ContainsKey("vt") && m.ContainsKey("ma") && m.ContainsKey("sl")) { hr = r; map = m; break; } }
        if (hr < 0) { res.Error = "Không thấy dòng tiêu đề có đủ cột VT, MÃ HÀNG, SL."; return res; }
        // ngày số liệu: ô "NGÀY: dd/mm/yyyy" hoặc ô "NGÀY:" + ô kế bên, ở các dòng phía trên tiêu đề
        for (var r = 0; r < hr && res.DataDate.Length == 0; r++)
            for (var c = 0; c < aoa[r].Count; c++)
            {
                var t = CellText(aoa[r][c]); var k = HeaderKey(t);
                if (!k.StartsWith("ngay")) continue;
                var after = t.Contains(':') ? t[(t.IndexOf(':') + 1)..].Trim() : "";
                if (after.Length == 0) after = aoa[r].Skip(c + 1).Select(CellText).FirstOrDefault(x => x.Length > 0) ?? "";
                if (after.Length > 0) { res.DataDate = FmtDate(after); break; }
            }
        string G(IReadOnlyList<object?> row, string f) => map.TryGetValue(f, out var i) && i < row.Count ? CellText(row[i]) : "";
        for (var r = hr + 1; r < aoa.Count; r++)
        {
            var row = aoa[r]; var vt = G(row, "vt"); var ma = G(row, "ma");
            if (vt.Length == 0 && ma.Length == 0) { if (row.Any(x => CellText(x).Length > 0)) res.Skipped++; continue; }
            var slRaw = map.TryGetValue("sl", out var si) && si < row.Count ? row[si] : null;
            var sl = slRaw is double sd ? (double.IsFinite(sd) ? sd : 0) : (JsText.LooseNum(CellText(slRaw)) is var x && !double.IsNaN(x) ? x : 0);
            res.Rows.Add(new VtbRow { Vt = vt, Ma = ma, Sl = sl, Nsx = FmtDate(G(row, "nsx")), Hsd = FmtDate(G(row, "hsd")), Nhap = FmtDate(G(row, "nhap")), NgayXuat = FmtDate(G(row, "ngayXuat")), VtCu = G(row, "vtCu"), VtMoi = G(row, "vtMoi"), GhiChu = G(row, "ghiChu") });
        }
        res.HeaderRow = hr + 1; res.Ok = res.Rows.Count > 0; if (!res.Ok) res.Error = "Sheet không có dòng dữ liệu nào dưới tiêu đề.";
        return res;
    }

    /// <summary>Chọn sheet đầu tiên đọc được (hoặc sheet chỉ định).</summary>
    public static FileParse ParseBook(IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyList<object?>>> book, IList<string> names, string? sheet = null)
    {
        FileParse? first = null;
        foreach (var n in sheet is not null && names.Contains(sheet) ? new[] { sheet } : names.ToArray())
        {
            var p = ParseSheet(book[n], n); if (p.Ok) return p; first ??= p;
        }
        return first ?? new FileParse { Error = "File không có sheet nào." };
    }

    /// <summary>THAY toàn bộ dữ liệu Vị Trí Bột bằng file "Vị trí Bột" (phiếu đợt cũ bị khóa — giống khi đưa tồn từ NXT Bột/Sốt).</summary>
    public int LoadFile(FileParse p, string fileName)
    {
        if (!CanEdit) throw new BusinessException("Bạn chỉ có quyền xem.");
        if (!p.Ok) throw new BusinessException(p.Error);
        Rows = p.Rows;
        ImportedAt = new DateTimeOffset(Now()).ToUnixTimeMilliseconds();
        _meta = new JsonObject { ["fileName"] = fileName, ["importedAt"] = ImportedAt, ["dataDate"] = p.DataDate, ["sheet"] = p.Sheet, ["headerRowNumber"] = p.HeaderRow, ["skippedRows"] = p.Skipped };
        FileName = fileName; DataDate = p.DataDate; Sheet = p.Sheet;
        Save(true, false);
        _audit?.Log(_session.Username, "Vị Trí Bột: Nạp file Vị trí Bột", $"{fileName} · {Rows.Count} dòng" + (p.DataDate.Length > 0 ? " · ngày " + p.DataDate : ""));
        return Rows.Count;
    }

    // ------------------------------------------------------------ ĐIỆN THOẠI (Google Sheet qua Web App — giống main.js của app cũ)
    public const string TabStock = "TonKho_M01_ViTriBot", PhoneModule = "M01VTB";
    public const string DaXuLy = "Đã xử lý", LoiThieuDong = "Lỗi - không có dòng hàng";
    /// <summary>Cột đẩy lên tab TonKho_M01_ViTriBot (điện thoại + Code.gs đọc theo tên này) + "_key" để phiếu điện thoại chỉ đúng dòng.</summary>
    public static readonly string[] PushColumns = { "Vị trí", "Mã hàng", "SL", "NSX", "HSD", "Nhập", "Ghi chú", "Vị trí mới", "_key" };
    private static readonly string[] SyncTables = { "M01", "M01VT", "M01VTB", "M02", "M03", "M08", "LX", "C3D" };

    /// <summary>Dòng đẩy lên điện thoại: đúng thứ tự dòng (khóa tính theo thứ tự này), GIỮ dòng SL = 0.</summary>
    public List<Dictionary<string, JsonNode?>> PushRows()
    {
        var keys = Keys(Rows);
        return Rows.Select((r, i) => new Dictionary<string, JsonNode?>
        {
            ["Vị trí"] = r.Vt.Trim(), ["Mã hàng"] = r.Ma.Trim(), ["SL"] = r.Sl, ["NSX"] = r.Nsx.Trim(), ["HSD"] = r.Hsd.Trim(), ["Nhập"] = r.Nhap.Trim(), ["Ghi chú"] = r.GhiChu.Trim(), ["Vị trí mới"] = r.VtMoi.Trim(), ["_key"] = keys[i],
        }).Where(o => S(o["_key"]).Length > 0).ToList();
    }

    public sealed record PhoneItem(string ItemKey, double Qty);
    public sealed record PhonePhieu(string Id, string MaPhieu, string NgayTao, string NguoiTao, string GhiChu, List<PhoneItem> Items);

    /// <summary>Nhận phiếu điện thoại: trừ cột SL đúng dòng (theo khóa) và lưu phiếu (giống applyPhonePhieuVTBDirectToExcel). Phiếu đã nhận (cùng phoneId) bỏ qua; phiếu có dòng không khớp → không nhận.</summary>
    public (List<string> Imported, int NewCount, List<string> Errors) ApplyPhone(IEnumerable<PhonePhieu> list)
    {
        if (!CanEdit && !_session.Can(SoKhoService.PermKey, Perm.Edit)) throw new BusinessException("Bạn không có quyền nhận phiếu Vị Trí Bột.");
        var idx = Keys(Rows).Select((k, i) => (k, i)).Where(x => x.k.Length > 0).GroupBy(x => x.k).ToDictionary(g => g.Key, g => g.First().i);
        var imported = new List<string>(); var errors = new List<string>(); var n = 0;
        var nowIso = Now().ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        foreach (var ph in list)
        {
            if (Slips.Any(x => x.PhoneId == ph.Id)) { imported.Add(ph.Id); continue; }
            var rows = ph.Items.Select(it => idx.TryGetValue(it.ItemKey.Trim(), out var i) ? i : -1).ToList();
            if (rows.Count == 0 || rows.Any(i => i < 0)) { errors.Add($"Phiếu {(ph.MaPhieu.Length > 0 ? ph.MaPhieu : ph.Id)}: có dòng không còn khớp tồn hiện tại (tồn đã được đưa lại?)"); continue; }
            var lines = ph.Items.Select((it, k) =>
            {
                var r = Rows[rows[k]]; r.Sl = Math.Round(r.Sl - it.Qty, 6);
                return new VtbLine { Key = it.ItemKey, Vt = r.Vt, Ma = r.Ma, Nsx = r.Nsx, Hsd = r.Hsd, Nhap = r.Nhap, GhiChu = r.GhiChu, Qty = it.Qty };
            }).ToList();
            Slips.Insert(0, new VtbSlip { Id = "VTB-" + Seq++.ToString("0000", CultureInfo.InvariantCulture), PhoneId = ph.Id, MaPhieu = ph.MaPhieu, NguoiTao = ph.NguoiTao, GhiChu = ph.GhiChu, CreatedAt = ph.NgayTao.Length > 0 ? ph.NgayTao : nowIso, Lines = lines, Deducted = true, DeductedAt = nowIso, LoadAt = ImportedAt });
            imported.Add(ph.Id); n++;
        }
        if (n > 0) { Save(true, true); _audit?.Log(_session.Username, "Vị Trí Bột: Nhận phiếu điện thoại", $"{n} phiếu"); }
        return (imported, n, errors);
    }

    public sealed record SyncResult(int NewPhieu, int ErrPhieu, int Pushed, List<string> Errors);

    /// <summary>
    /// Đồng bộ điện thoại cho Vị Trí Bột: (1) kéo phiếu xuất "M01VTB" chưa xử lý ở tab PhieuXuat / PhieuXuatItems → trừ tồn, đánh dấu "Đã xử lý"
    /// (phiếu không có dòng hàng quá 10 phút → "Lỗi - không có dòng hàng"); (2) đẩy tồn lên tab TonKho_M01_ViTriBot + cập nhật Meta.
    /// Chỉ đụng tới phiếu của Vị Trí Bột — phiếu module khác giữ nguyên trạng thái.
    /// </summary>
    public async Task<SyncResult> SyncPhoneAsync(PhoneWebApp w, bool pull = true, CancellationToken ct = default)
    {
        Load();
        await w.EnsureTabsAsync(new Dictionary<string, string[]?> { [PhoneWebApp.TabMeta] = null, [PhoneWebApp.TabPx] = PhoneWebApp.PhieuHeaders, [PhoneWebApp.TabPxi] = PhoneWebApp.PhieuItemHeaders, [TabStock] = null }, ct);
        int nNew = 0, nErr = 0; var errors = new List<string>();
        if (pull)
        {
            var px = await w.ReadTableAsync(PhoneWebApp.TabPx, ct);
            var pending = px.Rows.Where(h =>
            {
                var t = PhoneWebApp.Str(h.GetValueOrDefault("trangThai")).Trim();
                return PhoneWebApp.Str(h.GetValueOrDefault("id")).Trim().Length > 0 && t != DaXuLy && !t.StartsWith("Lỗi", StringComparison.Ordinal) && PhoneWebApp.Str(h.GetValueOrDefault("module")).Trim() == PhoneModule;
            }).ToList();
            if (pending.Count > 0)
            {
                var pxi = await w.ReadTableAsync(PhoneWebApp.TabPxi, ct);
                var itemsBy = pxi.Rows.GroupBy(it => PhoneWebApp.Str(it.GetValueOrDefault("phieuId")).Trim()).Where(g => g.Key.Length > 0)
                    .ToDictionary(g => g.Key, g => g.Select(it => new PhoneItem(PhoneWebApp.Str(it.GetValueOrDefault("itemKey")), PhoneWebApp.Num(it.GetValueOrDefault("soLuongXuat")))).Where(it => it.Qty > 0).ToList());
                var nowUtc = Now().ToUniversalTime(); var list = new List<(PhonePhieu P, JsonNode? Row)>(); var loi = new List<(JsonNode?, string, IDictionary<string, string>)>();
                var stamp = nowUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
                foreach (var h in pending)
                {
                    var id = PhoneWebApp.Str(h["id"]).Trim(); var ngay = PhoneWebApp.Iso(h.GetValueOrDefault("ngayTao"), nowUtc);
                    var items = itemsBy.GetValueOrDefault(id) ?? new();
                    if (items.Count == 0)
                    {
                        // điện thoại ghi dòng hàng TRƯỚC rồi mới ghi đầu phiếu → phiếu không có dòng là phiếu hỏng; chờ 10 phút rồi mới đánh dấu lỗi
                        if (DateTime.TryParse(ngay, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t0) && (nowUtc - t0).TotalMinutes > 10)
                            loi.Add((h.GetValueOrDefault("_rowNumber"), id, new Dictionary<string, string> { ["trangThai"] = LoiThieuDong, ["xuLyLuc"] = stamp }));
                        continue;
                    }
                    list.Add((new PhonePhieu(id, PhoneWebApp.Str(h.GetValueOrDefault("maPhieu")).Trim(), ngay, PhoneWebApp.Str(h.GetValueOrDefault("nguoiTao")), PhoneWebApp.Str(h.GetValueOrDefault("ghiChu")), items), h.GetValueOrDefault("_rowNumber")));
                }
                if (list.Count > 0)
                {
                    var (imported, n, errs) = ApplyPhone(list.Select(x => x.P));
                    nNew = n; errors.AddRange(errs);
                    var done = imported.ToHashSet();
                    await w.MarkRowsAsync(PhoneWebApp.TabPx, list.Where(x => done.Contains(x.P.Id)).Select(x => (x.Row, x.P.Id, (IDictionary<string, string>)new Dictionary<string, string> { ["trangThai"] = DaXuLy, ["xuLyLuc"] = stamp })), ct);
                }
                if (loi.Count > 0) { await w.MarkRowsAsync(PhoneWebApp.TabPx, loi, ct); nErr = loi.Count; }
            }
        }
        // đẩy tồn + Meta (ghi SAU CÙNG: điện thoại dựa vào lastPushAt_M01VTB để biết phiếu nào đã nằm trong số tồn)
        var rows = PushRows();
        await w.WriteTableAsync(TabStock, rows.Count > 0 ? PushColumns : Array.Empty<string>(), rows, ct);
        var meta = new Dictionary<string, JsonNode?>();
        try { foreach (var r in (await w.ReadTableAsync(PhoneWebApp.TabMeta, ct)).Rows) { var k = PhoneWebApp.Str(r.GetValueOrDefault("key")); if (k.Length > 0) meta[k] = r.GetValueOrDefault("value")?.DeepClone(); } } catch { /* Meta chưa có — tạo mới */ }
        var old = meta.GetValueOrDefault("lastPushAt") is { } lp ? PhoneWebApp.Str(lp) : "";
        var nowS = Now().ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        meta["lastPushAt_M01VTB"] = nowS; meta["m01ViTriBotRows"] = rows.Count; meta["m01ViTriBotDataDate"] = DataDate;
        meta["m01ViTriBotImportedAt"] = ImportedAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)ImportedAt).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture) : "";
        var stamps = SyncTables.Select(t => meta.GetValueOrDefault("lastPushAt_" + t) is { } v ? PhoneWebApp.Str(v) : "").Where(x => x.Length > 0).OrderBy(x => x, StringComparer.Ordinal).ToList();
        meta["lastPushAt"] = stamps.Count == SyncTables.Length ? stamps[0] : old;
        await w.WriteTableAsync(PhoneWebApp.TabMeta, new[] { "key", "value" }, meta.Select(kv => (IDictionary<string, JsonNode?>)new Dictionary<string, JsonNode?> { ["key"] = kv.Key, ["value"] = kv.Value?.DeepClone() }), ct);
        _audit?.Log(_session.Username, "Vị Trí Bột: Đồng bộ điện thoại", $"nhận {nNew} phiếu · đẩy {rows.Count} dòng tồn");
        return new SyncResult(nNew, nErr, rows.Count, errors);
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
