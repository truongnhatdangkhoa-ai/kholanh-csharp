using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using KhoLanh.Core.DoiChieu;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.TongHop;

namespace KhoLanh.Core.ChungTu;

/// <summary>
/// Module In chứng từ kho (C#, viết lại theo "In chứng từ kho" của app cũ — chung-tu-xuat-kho.js, ctxk-tenhang.js, tenhang-core.js).
/// Dùng CHUNG dữ liệu với app cũ (database "in-chung-tu-kho"):
///   TTB / Xa / Kiem              = người ký của từng loại phiếu (dòng 1 dùng in phiếu)
///   DuLieu_TBB / DuLieu_Xa / DuLieu_Kiem = dòng đã nạp từ Excel của từng loại phiếu
///   CT_Meta                      = 1 ô JSON {v, mod, files, merged, form, at}: tên file đã nạp, các ngày đã gộp, các ô điền phiếu.
/// Mỗi ngày chứng từ = 1 Phiếu xuất (DISPATCHING NOTE, mẫu BM02/QT23); gộp nhiều ngày thành 1 phiếu; tách dòng sang phiếu riêng cùng ngày;
/// bổ sung tên hàng bị cắt từ mã 21 ký tự theo Mã hóa sản phẩm (Tổng hợp); xuất Excel theo mẫu, in / lưu PDF qua trang in.
/// </summary>
public sealed class ChungTuService
{
    public const string Db = "in-chung-tu-kho", PermKey = "m12", SheetMeta = "CT_Meta";

    public sealed record Kind(string Key, string Label, string DocName, string Loai, string Prefix, string SignSheet, string RowSheet, string[] SignCols, string[] Roles);

    public static readonly Kind[] Kinds =
    {
        new("tbb", "Xuất TBB", "PHIẾU XUẤT TBB", "Xuất Thay Thùng", "TBB", "TTB", "DuLieu_TBB",
            new[] { "pxNhan", "thuKhoXuat", "keToan", "quanLyKho", "giamDoc" }, new[] { "PX NHẬN", "THỦ KHO XUẤT", "KẾ TOÁN", "QUẢN LÝ KHO", "GIÁM ĐỐC" }),
        new("xa", "Xuất Xả", "PHIẾU XUẤT XẢ", "Xuất Xả", "XA", "Xa", "DuLieu_Xa",
            new[] { "pxNhan", "qcGiamSat", "keToan", "quanLyKho", "giamDoc" }, new[] { "PX NHẬN", "QC GIÁM SÁT KS", "KẾ TOÁN", "QUẢN LÝ KHO", "GIÁM ĐỐC" }),
        new("kiem", "Xuất Kiểm", "PHIẾU XUẤT KIỂM", "Xuất Kiểm", "KIEM", "Kiem", "DuLieu_Kiem",
            new[] { "nguoiNhan", "thuKhoXuat", "keToan", "quanLyKho", "giamDoc" }, new[] { "NGƯỜI NHẬN", "THỦ KHO XUẤT", "KẾ TOÁN", "QUẢN LÝ KHO", "GIÁM ĐỐC" }),
    };
    public static Kind K(string key) => Kinds.FirstOrDefault(k => k.Key == key) ?? Kinds[0];

    /// <summary>Các ô điền phiếu (giữ đúng khóa của app cũ trong CT_Meta.form).</summary>
    public static readonly string[] FormKeys = { "fNgayLap", "fSoPhieu", "fLoaiChungTu", "fMaBieuMau", "fKho", "fSoBanMoiNgay", "fGhiChu" };
    public static Dictionary<string, string> DefaultForm() => new()
    {
        ["fNgayLap"] = "", ["fSoPhieu"] = "", ["fLoaiChungTu"] = "", ["fMaBieuMau"] = "BM02/QT23", ["fKho"] = "Kho Lạnh - An An", ["fSoBanMoiNgay"] = "1", ["fGhiChu"] = "",
    };

    // ------------------------------------------------------------------ dữ liệu

    public sealed class Row
    {
        public string MaHang = "", TenHang = "", Size = "", NgayChungTu = "", GoiY = "";
        public double SoLuong, TrongLuong, Net, NgaySort;
        public bool Tach;
        public bool TenMoi;   // tên vừa bổ sung (chỉ để tô xanh trong app, không lưu)
        public string SizeText => Size.Trim().Length > 0 ? Size.Trim() : ExtractSize(TenHang);
    }

    public sealed class Group
    {
        public string Key = "", Label = "", SoPhieu = "", DateLine = "";
        public bool Tach, IsMerged;
        public double Sort;
        public int Index;
        public List<string> Dates = new(), Labels = new();
        public List<Row> Items = new();
        public double SumSL => Items.Sum(r => r.SoLuong);
        public double SumTL => Items.Sum(r => r.TrongLuong);
        public double SumNet => Items.Sum(r => r.Net);
    }

    public Dictionary<string, List<Row>> Rows { get; } = Kinds.ToDictionary(k => k.Key, _ => new List<Row>());
    public Dictionary<string, string> Files { get; } = Kinds.ToDictionary(k => k.Key, _ => "");
    public Dictionary<string, List<List<string>>> Merged { get; } = Kinds.ToDictionary(k => k.Key, _ => new List<List<string>>());
    public Dictionary<string, List<Dictionary<string, string>>> Signers { get; } = Kinds.ToDictionary(k => k.Key, _ => new List<Dictionary<string, string>>());
    /// <summary>1.193: mỗi loại phiếu (TBB / Xả / Kiểm) có bộ thông tin phiếu RIÊNG — tránh lẫn lộn khi đổi tab.</summary>
    public Dictionary<string, Dictionary<string, string>> Forms { get; private set; } = Kinds.ToDictionary(k => k.Key, k => DefaultFormOf(k.Key));
    /// <summary>Thông tin phiếu của loại đang mở (<see cref="Mod"/>).</summary>
    public Dictionary<string, string> Form => FormOf(Mod);
    public Dictionary<string, string> FormOf(string mod)
    {
        if (!Forms.TryGetValue(mod, out var f)) Forms[mod] = f = DefaultFormOf(mod);
        return f;
    }
    public static Dictionary<string, string> DefaultFormOf(string mod)
    {
        var f = DefaultForm(); f["fLoaiChungTu"] = K(mod).Loai; return f;
    }
    public string Mod { get; set; } = "tbb";
    public bool Loaded { get; private set; }

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    private readonly TongHopService? _tongHop;

    public ChungTuService(Func<ISheetSource> source, Session session, AuditService audit, TongHopService? tongHop = null)
    { _src = source; _session = session; _audit = audit; _tongHop = tongHop; }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    private void RequireView() { if (!CanView) throw new BusinessException("Tài khoản không có quyền xem In chứng từ kho."); }
    private void RequireEdit() { if (!CanEdit) throw new BusinessException("Tài khoản không có quyền sửa In chứng từ kho (nạp file, gộp / tách phiếu, người ký)."); }
    private void RequireExport() { if (!CanExport) throw new BusinessException("Tài khoản không có quyền xuất file / in chứng từ."); }
    private void Log(string action, string detail) => _audit.Log(_session.Username, "In chứng từ kho: " + action, detail);

    // ------------------------------------------------------------------ tiện ích

    internal static string S(JsonNode? n)
    {
        if (n is null) return "";
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s;
            if (v.TryGetValue<double>(out var d)) return d.ToString("0.############", CultureInfo.InvariantCulture);
            if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        }
        return n.ToJsonString();
    }
    private static double D(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? (double.IsFinite(d) ? d : 0) : (DcxEngine.ParseNum(S(n)) ?? 0);
    public static double R3(double v) => Math.Round(v * 1000, MidpointRounding.AwayFromZero) / 1000;

    public static string StripAccents(string s) =>
        new string(s.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .Replace('đ', 'd').Replace('Đ', 'd').ToLowerInvariant().Trim();
    private static string NormHead(string s) => Regex.Replace(StripAccents(s), "[^a-z0-9]+", " ").Trim();

    /// <summary>Lấy size từ tên hàng: "…, 31/40 (9 kg x 1 goi)" → "31/40" (kể cả khi tên bị cắt mất ")").</summary>
    public static string ExtractSize(string? ten)
    {
        var last = "";
        foreach (Match m in Regex.Matches(ten ?? "", @",\s*([A-Za-z0-9\/.]+)\s*\(")) last = m.Groups[1].Value;
        return last;
    }

    public static (string D, string M, string Y)? DateParts(string? label)
    {
        var m = Regex.Match(label ?? "", @"^(\d{1,2})/(\d{1,2})/(\d{2,4})$");
        return m.Success ? (m.Groups[1].Value.PadLeft(2, '0'), m.Groups[2].Value.PadLeft(2, '0'), m.Groups[3].Value) : null;
    }
    public static string VnDateLong(string label) => DateParts(label) is { } p ? $"Ngày {p.D} Tháng {p.M} Năm {p.Y}" : label.Length > 0 ? "Ngày " + label : "—";
    private static string RangeLabel(string a, string b) => DateParts(a) is { } x && DateParts(b) is { } y ? $"{x.D}/{x.M} - {y.D}/{y.M}" : $"{a} - {b}";

    // ------------------------------------------------------------------ đọc / lưu

    private JsonArray Read(string sheet)
    {
        try { return JsonNode.Parse(_src().Read(Db, sheet)) as JsonArray ?? new JsonArray(); } catch { return new JsonArray(); }
    }

    public void Load()
    {
        RequireView();
        foreach (var k in Kinds)
        {
            var sg = Read(k.SignSheet).OfType<JsonObject>().Select(o => k.SignCols.ToDictionary(c => c, c => S(o[c]))).ToList();
            Signers[k.Key] = sg.Count > 0 ? sg : new List<Dictionary<string, string>> { k.SignCols.ToDictionary(c => c, _ => "") };
            Rows[k.Key] = Read(k.RowSheet).OfType<JsonObject>().Select(FromStored).Where(r => r.MaHang.Length > 0).ToList();
            Files[k.Key] = ""; Merged[k.Key] = new();
        }
        Forms = Kinds.ToDictionary(k => k.Key, k => DefaultFormOf(k.Key));
        Mod = "tbb";
        var raw = string.Concat(Read(SheetMeta).OfType<JsonObject>().Select(o => S(o["data"])));
        if (raw.StartsWith("__JSON__"))
        {
            try
            {
                if (JsonNode.Parse(raw[8..]) is JsonObject meta)
                {
                    foreach (var k in Kinds)
                    {
                        if (Rows[k.Key].Count == 0) continue;
                        Files[k.Key] = meta["files"]?[k.Key] is { } f && S(f).Length > 0 ? S(f) : "(dữ liệu đã lưu)";
                        if (meta["merged"]?[k.Key] is JsonArray arr)
                            Merged[k.Key] = arr.OfType<JsonArray>().Select(a => a.Select(S).ToList()).ToList();
                    }
                    var mod = S(meta["mod"]);
                    if (Kinds.Any(k => k.Key == mod)) Mod = mod;
                    if (meta["forms"] is JsonObject forms)
                    {
                        foreach (var k in Kinds)
                            if (forms[k.Key] is JsonObject fk)
                                foreach (var key in FormKeys) if (fk[key] is { } v) Forms[k.Key][key] = S(v);
                    }
                    else if (meta["form"] is JsonObject form)
                    {
                        // bản cũ (≤ 1.192): 1 bộ thông tin dùng chung → chép cho cả 3 loại, sửa phần lẫn của loại khác
                        foreach (var k in Kinds)
                        {
                            foreach (var key in FormKeys) if (form[key] is { } v) Forms[k.Key][key] = S(v);
                            var f = Forms[k.Key];
                            if (Kinds.Any(o => o.Key != k.Key && f["fLoaiChungTu"] == o.Loai)) f["fLoaiChungTu"] = k.Loai;
                            if (Kinds.Any(o => o.Key != k.Key && f["fSoPhieu"].StartsWith(o.Prefix + "-"))) f["fSoPhieu"] = SuggestNumber(k.Key, DateTime.Today);
                        }
                    }
                }
            }
            catch { /* hỏng thì bỏ qua */ }
        }
        foreach (var k in Kinds) if (Rows[k.Key].Count > 0 && Files[k.Key].Length == 0) Files[k.Key] = "(dữ liệu đã lưu)";
        if (Rows[Mod].Count == 0 && Kinds.FirstOrDefault(k => Rows[k.Key].Count > 0) is { } first) Mod = first.Key;
        Loaded = true;
    }

    private static Row FromStored(JsonObject o)
    {
        var ngay = S(o["ngayChungTu"]);
        return new Row
        {
            MaHang = S(o["maHang"]).Trim(), TenHang = S(o["tenHang"]), Size = S(o["size"]), SoLuong = D(o["soLuong"]), NgayChungTu = ngay,
            NgaySort = D(o["ngayChungTuSort"]), TrongLuong = D(o["trongLuong"]), Net = D(o["net"]), Tach = S(o["tach"]) == "1", GoiY = S(o["goiY"]),
        };
    }
    private static JsonObject ToStored(Row r) => new()
    {
        ["maHang"] = r.MaHang, ["tenHang"] = r.TenHang, ["size"] = r.Size, ["soLuong"] = r.SoLuong, ["ngayChungTu"] = r.NgayChungTu,
        ["ngayChungTuSort"] = r.NgaySort, ["trongLuong"] = r.TrongLuong, ["net"] = r.Net, ["tach"] = r.Tach ? 1 : "", ["goiY"] = r.GoiY,
    };

    private JsonArray MetaArray()
    {
        var obj = new JsonObject
        {
            ["v"] = 1, ["mod"] = Mod,
            ["files"] = new JsonObject(Kinds.Select(k => KeyValuePair.Create(k.Key, (JsonNode?)Files[k.Key]))),
            ["merged"] = new JsonObject(Kinds.Select(k => KeyValuePair.Create(k.Key, (JsonNode?)new JsonArray(Merged[k.Key].Select(a => (JsonNode?)new JsonArray(a.Select(x => (JsonNode?)x).ToArray())).ToArray())))),
            ["form"] = new JsonObject(FormKeys.Select(f => KeyValuePair.Create(f, (JsonNode?)Form.GetValueOrDefault(f, "")))),   // giữ cho bản cũ đọc được
            ["forms"] = new JsonObject(Kinds.Select(k => KeyValuePair.Create(k.Key, (JsonNode?)new JsonObject(FormKeys.Select(f => KeyValuePair.Create(f, (JsonNode?)FormOf(k.Key).GetValueOrDefault(f, ""))))))),
            ["at"] = DateTimeOffset.Now.ToUnixTimeMilliseconds(),
        };
        var json = obj.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        var rows = new JsonArray();
        for (var i = 0; i < json.Length; i += 30000)   // 1 ô Excel tối đa ~32.000 ký tự (app cũ chia nhỏ như vậy)
            rows.Add(new JsonObject { ["data"] = (i == 0 ? "__JSON__" : "") + json.Substring(i, Math.Min(30000, json.Length - i)) });
        return rows;
    }

    /// <summary>Lưu dòng của 1 loại phiếu (null = không đổi) + meta.</summary>
    private void Save(string? mod)
    {
        var sheets = new Dictionary<string, JsonArray?> { [SheetMeta] = MetaArray() };
        if (mod is not null) sheets[K(mod).RowSheet] = new JsonArray(Rows[mod].Select(r => (JsonNode?)ToStored(r)).ToArray());
        _src().Write(Db, sheets);
    }

    /// <summary>Lưu các ô điền phiếu + tab đang mở (không cần quyền Sửa: chỉ là thông tin in).</summary>
    public void SaveForm()
    {
        RequireView();
        if (!CanEdit) return;   // ghi database cần quyền Sửa (mạng LAN cũng kiểm như vậy)
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetMeta] = MetaArray() });
    }

    public void SaveSigners(string mod, List<Dictionary<string, string>> rows)
    {
        RequireEdit();
        var k = K(mod);
        var clean = rows.Select(r => k.SignCols.ToDictionary(c => c, c => (r.GetValueOrDefault(c) ?? "").Trim())).Where(r => r.Values.Any(v => v.Length > 0)).ToList();
        if (clean.Count == 0) clean.Add(k.SignCols.ToDictionary(c => c, _ => ""));
        Signers[mod] = clean;
        _src().Write(Db, new Dictionary<string, JsonArray?> { [k.SignSheet] = new JsonArray(clean.Select(r => (JsonNode?)new JsonObject(r.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value)))).ToArray()) });
        Log("Lưu người ký", k.Label);
    }

    /// <summary>Tên người ký in trên phiếu = dòng đầu của bảng Thông tin phiếu.</summary>
    public string[] SignerNames(string mod)
    {
        var k = K(mod);
        var row = Signers[mod].FirstOrDefault() ?? new();
        return k.SignCols.Select(c => row.GetValueOrDefault(c) ?? "").ToArray();
    }

    // ------------------------------------------------------------------ nạp file

    private static readonly (string Key, string Label, string[] Labels, bool Required)[] Fields =
    {
        ("maHang", "Mã hàng", new[] { "ma hang", "mahang", "ma_hang" }, true),
        ("tenHang", "Tên hàng", new[] { "ten hang", "tenhang", "ten_hang" }, true),
        ("soLuong", "Số lượng", new[] { "so luong", "soluong", "so_luong" }, true),
        ("ngayChungTu", "Ngày chứng từ", new[] { "ngay chung tu", "ngaychungtu", "ngay_chung_tu" }, true),
        ("trongLuong", "Trọng lượng", new[] { "trong luong", "trongluong", "trong_luong" }, true),
        ("net", "Net", new[] { "net" }, true),
        ("size", "Size", new[] { "size", "kich thuoc", "kichthuoc", "kich_thuoc" }, false),
    };

    internal static string? MatchField(string cell)
    {
        var norm = NormHead(cell);
        foreach (var f in Fields)
            foreach (var l in f.Labels)
                if (norm == l || norm.Replace(" ", "") == l.Replace(" ", "").Replace("_", "")) return f.Key;
        return null;
    }

    /// <summary>Khớp gần đúng ("Trọng lượng (kg)", "TL", "Khối lượng", "Số lượng (kiện)", "Mã hàng mới"…).</summary>
    internal static string? MatchFieldFuzzy(string cell)
    {
        var norm = NormHead(cell);
        if (norm.Length == 0) return null;
        var w = norm.Split(' ');
        bool Has(string t) => norm.Contains(t);
        if (w.Contains("net")) return "net";
        if (Has("trong luong") || Has("khoi luong") || norm is "tl" or "kl" || w[0] is "tl" or "kl")
            return Has("tong ") && !Has("kien") ? null : "trongLuong";
        if (Has("so luong") || Has("so kien") || norm == "sl") return "soLuong";
        if (Has("ngay chung tu") || Has("ngay ct") || norm == "ngay") return "ngayChungTu";
        if (Has("ma hang") || Has("ma sp") || Has("ma san pham")) return "maHang";
        if (Has("ten hang") || Has("ten san pham") || Has("ten sp")) return "tenHang";
        if (norm == "size" || Has("kich thuoc")) return "size";
        return null;
    }

    public sealed record ParseResult(List<Row> Rows, string Warning, string Error);

    /// <summary>Đọc ô Excel thành danh sách dòng (giá trị: string / double / DateTime) của sheet đầu.</summary>
    public static List<List<object?>> ReadRaw(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var wb = new XLWorkbook(fs);
            var ws = wb.Worksheets.First();
            var used = ws.RangeUsed();
            var res = new List<List<object?>>();
            if (used is null) return res;
            int r2 = used.LastRow().RowNumber(), c2 = used.LastColumn().ColumnNumber();
            for (var r = 1; r <= r2; r++)
            {
                var row = new List<object?>();
                for (var c = 1; c <= c2; c++)
                {
                    var cell = ws.Cell(r, c);
                    row.Add(cell.IsEmpty() ? "" : cell.DataType switch
                    {
                        XLDataType.Number => cell.GetDouble(),
                        XLDataType.DateTime => cell.GetDateTime(),
                        XLDataType.Boolean => cell.GetBoolean() ? "TRUE" : "FALSE",
                        _ => cell.GetFormattedString(),
                    });
                }
                res.Add(row);
            }
            return res;
        }
        catch (IOException e) { throw new BusinessException("Không mở được file (có thể đang mở trong Excel): " + e.Message); }
        catch (Exception e) when (e is not BusinessException) { throw new BusinessException("Không đọc được file. Vui lòng kiểm tra định dạng file Excel (.xlsx). " + e.Message); }
    }

    private static string Str(object? v) => v switch { null => "", double d => d.ToString("0.############", CultureInfo.InvariantCulture), DateTime t => t.ToString("dd/MM/yyyy"), _ => v.ToString() ?? "" };
    private static double Num(object? v) => DcxEngine.ParseNum(v is DateTime ? null : v) ?? 0;

    internal static (string Label, double Sort) DateCell(object? v)
    {
        switch (v)
        {
            case DateTime t: return (t.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Ms(t));
            case double d when d > 0 && d < 2958466:
                var dt = DateTime.FromOADate(d);
                return (dt.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), Ms(dt.Date));
        }
        var s = Str(v).Trim();
        var m = Regex.Match(s, @"^(\d{1,2})[\/\-](\d{1,2})[\/\-](\d{2,4})$");
        if (m.Success)
        {
            var y = int.Parse(m.Groups[3].Value); if (m.Groups[3].Value.Length == 2) y += 2000;
            try { return (s, Ms(new DateTime(y, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value)))); } catch { return (s, 0); }
        }
        return (s, DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out var p) ? Ms(p) : 0);
    }
    private static double Ms(DateTime t) => new DateTimeOffset(DateTime.SpecifyKind(t, DateTimeKind.Local)).ToUnixTimeMilliseconds();

    /// <summary>Tìm dòng tiêu đề trong 15 dòng đầu + nhận tiêu đề gần đúng, Size không tiêu đề, Trọng lượng suy từ Net ÷ SL, ghi chú gợi ý.</summary>
    public static ParseResult Parse(List<List<object?>> raw)
    {
        if (raw.Count == 0) return new(new(), "", "File không có dữ liệu.");
        int bestIdx = 0, bestScore = -1;
        Dictionary<string, int> best = new();
        for (var r = 0; r < Math.Min(raw.Count, 15); r++)
        {
            var row = raw[r];
            var map = new Dictionary<string, int>();
            for (var c = 0; c < row.Count; c++) if (MatchField(Str(row[c])) is { } k && !map.ContainsKey(k)) map[k] = c;
            var claimed = map.Values.ToHashSet();
            for (var c = 0; c < row.Count; c++)
            {
                if (claimed.Contains(c)) continue;
                if (MatchFieldFuzzy(Str(row[c])) is { } k && !map.ContainsKey(k)) { map[k] = c; claimed.Add(c); }
            }
            var score = Fields.Count(f => f.Required && map.ContainsKey(f.Key));
            if (score > bestScore) { bestScore = score; bestIdx = r; best = map; }
            if (score == Fields.Count(f => f.Required)) break;
        }
        var header = raw[bestIdx];
        var col = best;
        if (!col.ContainsKey("size") && col.TryGetValue("tenHang", out var ct) && col.TryGetValue("soLuong", out var cs))
        {
            var claimed = col.Values.ToHashSet();
            var between = Enumerable.Range(ct + 1, Math.Max(0, cs - ct - 1)).Where(i => !claimed.Contains(i)).ToList();
            if (between.Count == 1) col["size"] = between[0];
        }
        var derivedTL = !col.ContainsKey("trongLuong") && col.ContainsKey("net") && col.ContainsKey("soLuong");
        var missing = Fields.Where(f => f.Required && !col.ContainsKey(f.Key) && !(derivedTL && f.Key == "trongLuong")).ToList();
        if (missing.Count > 0)
        {
            var heads = string.Join(" | ", header.Select(c => Str(c).Trim()).Where(s => s.Length > 0));
            return new(new(), "", "Thiếu cột: " + string.Join(", ", missing.Select(f => f.Label)) + ". Vui lòng kiểm tra lại tiêu đề cột trong file. (Dòng tiêu đề app đọc được: " + (heads.Length > 0 ? heads : "không có") + ")");
        }
        var noteStart = col.Values.Max() + 1;
        string Hint(List<object?> r)
        {
            for (var c = noteStart; c < r.Count; c++)
            {
                var v = Str(r[c]).Trim();
                if (v.Length > 0 && !Regex.IsMatch(v, @"^[\d.,\s+-]+$")) return v;
            }
            return "";
        }
        object? At(List<object?> r, string key) => col.TryGetValue(key, out var c) && c < r.Count ? r[c] : null;
        var rows = new List<Row>();
        for (var i = bestIdx + 1; i < raw.Count; i++)
        {
            var r = raw[i];
            if (r.All(c => Str(c).Trim().Length == 0)) continue;
            var ma = Str(At(r, "maHang")).Trim();
            if (ma.Length == 0) continue;   // dòng trống / dòng tổng của file gốc
            var sl = Num(At(r, "soLuong"));
            double tl, net;
            if (derivedTL) { var nf = Num(At(r, "net")); tl = sl != 0 ? R3(nf / sl) : 0; net = R3(nf); }
            else { tl = Num(At(r, "trongLuong")); net = R3(sl * tl); }   // Net = SL × TL (cột Net của file gốc đôi khi sai)
            var (label, sort) = DateCell(At(r, "ngayChungTu"));
            rows.Add(new Row
            {
                MaHang = ma, TenHang = Str(At(r, "tenHang")), Size = Str(At(r, "size")).Trim(), SoLuong = sl, TrongLuong = tl, Net = net,
                NgayChungTu = label, NgaySort = sort, GoiY = Hint(r),
            });
        }
        if (rows.Count == 0) return new(new(), "", "Không tìm thấy dòng dữ liệu hợp lệ (cột Mã hàng đang trống ở mọi dòng sau dòng tiêu đề).");
        return new(rows, derivedTL ? "Lưu ý: file không có cột Trọng lượng — app lấy Trọng lượng = Net ÷ Số lượng từ file." : "", "");
    }

    /// <summary>Nạp file Excel cho loại phiếu đang chọn (thay dữ liệu cũ của loại này). Trả về cảnh báo (nếu có).</summary>
    public string Import(string mod, string path)
    {
        RequireEdit();
        var p = Parse(ReadRaw(path));
        if (p.Error.Length > 0) throw new BusinessException(p.Error);
        Rows[mod] = p.Rows;
        Files[mod] = Path.GetFileName(path);
        Merged[mod] = new();
        Mod = mod;
        Form["fNgayLap"] = DateTime.Today.ToString("yyyy-MM-dd");
        Form["fSoPhieu"] = SuggestNumber(mod, DateTime.Today);
        Form["fLoaiChungTu"] = K(mod).Loai;
        Save(mod);
        Log("Nạp file", $"{K(mod).Label}: {Files[mod]} ({p.Rows.Count} dòng)");
        return p.Warning;
    }

    public static string SuggestNumber(string mod, DateTime d) => $"{K(mod).Prefix}-{d:MMdd}";

    public void Clear(string mod)
    {
        RequireEdit();
        Rows[mod] = new(); Files[mod] = ""; Merged[mod] = new();
        Save(mod);
        Log("Xóa dữ liệu", K(mod).Label);
    }

    // ------------------------------------------------------------------ nhóm theo ngày / gộp / tách

    /// <summary>Nhóm theo ngày; dòng đã tách thành phiếu riêng cùng ngày (khóa "ngày|2"), phiếu thường đứng trước phiếu tách.</summary>
    public static List<Group> GroupByDate(IEnumerable<Row> rows)
    {
        var map = new Dictionary<string, Group>();
        var order = new List<Group>();
        foreach (var r in rows)
        {
            var label = r.NgayChungTu.Length > 0 ? r.NgayChungTu : "(Không có ngày)";
            var key = r.Tach ? label + "|2" : label;
            if (!map.TryGetValue(key, out var g)) { map[key] = g = new Group { Key = key, Label = label, Tach = r.Tach, Sort = r.NgaySort }; order.Add(g); }
            g.Items.Add(r);
        }
        return order.OrderBy(g => g.Sort).ThenBy(g => g.Tach ? 1 : 0).ToList();
    }

    /// <summary>Danh sách phiếu thật sự sẽ in: các ngày đã gộp + các ngày còn lại; đánh số phiếu theo thứ tự ngày (NN + tháng + năm).</summary>
    public List<Group> Effective(string mod)
    {
        var groups = GroupByDate(Rows[mod]);
        var byKey = groups.ToDictionary(g => g.Key);
        var used = new HashSet<string>();
        var eff = new List<Group>();
        foreach (var set in Merged[mod])
        {
            var rel = set.Where(d => byKey.ContainsKey(d) && !used.Contains(d)).Distinct().ToList();
            if (rel.Count < 2) continue;   // gộp cũ không còn khớp dữ liệu → bỏ qua
            foreach (var d in rel) used.Add(d);
            var sub = rel.Select(d => byKey[d]).OrderBy(g => g.Sort).ToList();
            eff.Add(new Group
            {
                Label = RangeLabel(sub[0].Label, sub[^1].Label), Items = sub.SelectMany(g => g.Items).ToList(),
                Dates = sub.Select(g => g.Key).ToList(), Labels = sub.Select(g => g.Label).ToList(), Tach = sub[0].Tach, Sort = sub[0].Sort, IsMerged = true,
            });
        }
        foreach (var g in groups.Where(g => !used.Contains(g.Key)))
            eff.Add(new Group { Label = g.Label, Items = g.Items, Dates = new() { g.Key }, Labels = new() { g.Label }, Tach = g.Tach, Sort = g.Sort });
        eff = eff.OrderBy(g => g.Sort).ThenBy(g => g.Tach ? 1 : 0).ToList();
        for (var i = 0; i < eff.Count; i++)
        {
            var g = eff[i];
            var p = DateParts(g.Labels[0]);
            g.Index = i;
            g.Key = string.Join("+", g.Dates);
            g.SoPhieu = $"{i + 1:00}{p?.M ?? "01"}{p?.Y ?? "0000"}";
            g.DateLine = g.IsMerged ? $"Từ ngày {g.Labels[0]} đến ngày {g.Labels[^1]}" : VnDateLong(g.Label);
        }
        return eff;
    }

    /// <summary>Gộp các phiếu đã chọn thành 1 phiếu (không gộp lẫn phiếu tách với phiếu thường).</summary>
    public Group Merge(string mod, IReadOnlyList<Group> sel)
    {
        RequireEdit();
        if (sel.Count < 2) throw new BusinessException("Chọn ít nhất 2 ngày để gộp.");
        if (sel.Any(g => g.Tach != sel[0].Tach)) throw new BusinessException("Không gộp lẫn phiếu tách với phiếu thường — hãy chọn các phiếu cùng loại.");
        var all = sel.SelectMany(g => g.Dates).Distinct().ToList();
        Merged[mod] = Merged[mod].Where(s => !s.Any(all.Contains)).ToList();
        Merged[mod].Add(all);
        Save(null);
        Log("Gộp phiếu", $"{K(mod).Label}: {string.Join(", ", sel.SelectMany(g => g.Labels))}");
        return Effective(mod).First(g => g.Dates.Count == all.Count && all.All(g.Dates.Contains));
    }

    public void Unmerge(string mod, Group g)
    {
        RequireEdit();
        if (!g.IsMerged) return;
        var set = g.Dates.ToHashSet();
        var i = Merged[mod].FindIndex(s => s.Count == set.Count && s.All(set.Contains));
        if (i < 0) return;
        Merged[mod].RemoveAt(i);
        Save(null);
    }

    public void UnmergeAll(string mod)
    {
        RequireEdit();
        Merged[mod] = new();
        Save(null);
    }

    /// <summary>Tách (true) / gộp lại (false) các dòng sang phiếu riêng cùng ngày.</summary>
    public int SetTach(string mod, IEnumerable<Row> rows, bool v)
    {
        RequireEdit();
        var n = 0;
        foreach (var r in rows) if (r.Tach != v) { r.Tach = v; n++; }
        if (n > 0) { Save(mod); Log(v ? "Tách dòng sang phiếu riêng" : "Gộp dòng về phiếu thường", $"{K(mod).Label}: {n} dòng"); }
        return n;
    }

    // ------------------------------------------------------------------ bổ sung tên hàng (Mã hóa sản phẩm)

    private static readonly string[] SegLabel = { "", "Sản phẩm", "Cấp đông", "Đóng gói", "Hóa chất", "Thị trường", "Khách hàng", "Mạ băng", "Size", "Trọng lượng", "Quy cách" };
    public static string NormMa(string? v) => Regex.Replace(v ?? "", @"\s+", "").ToUpperInvariant();

    /// <summary>Mã 21 ký tự → tên đầy đủ "&lt;Sản phẩm&gt;, …, &lt;Size&gt; (&lt;Trọng lượng&gt; x &lt;Số gói&gt;)". Đoạn toàn X bỏ qua; không đoán.</summary>
    public static (bool Ok, string Ten, string Err) Compose(string? maRaw, Func<int, string, string?> lookup)
    {
        var ma = NormMa(maRaw);
        if (ma.StartsWith("TPC-")) ma = ma[4..];
        if (ma.Length == 0) return (false, "", "Ô mã trống");
        if (ma.Length != TongHopService.CodeLength) return (false, "", $"Mã có {ma.Length} ký tự (cần 21)");
        var p = new string[11];
        foreach (var s in TongHopService.Segs)
        {
            var code = ma.Substring(TongHopService.Offset(s.Seg), s.Length);
            if (Regex.IsMatch(code, "^X+$")) { p[s.Seg] = ""; continue; }
            var name = lookup(s.Seg, code);
            if (name is null) return (false, "", $"{SegLabel[s.Seg]} \"{code}\" không có trong Mã hóa sản phẩm");
            p[s.Seg] = name.Trim();
        }
        if (p[1].Length == 0) return (false, "", $"Không giải được sản phẩm \"{ma[..4]}\"");
        var head = string.Join(", ", Enumerable.Range(1, 8).Select(i => p[i]).Where(x => x.Length > 0));
        var w = Regex.Replace(Regex.Replace(p[9], @"\s*/\s*gói\s*$", "", RegexOptions.IgnoreCase), @"^(\d+)\.0+(?=\s|$)", "$1").Trim();
        var q = Regex.Replace(p[10], @"\s*/\s*thùng.*$", "", RegexOptions.IgnoreCase).Trim();
        var tail = w.Length > 0 && q.Length > 0 ? $" ({w} x {q})" : w.Length > 0 ? $" ({w})" : "";
        return (true, head + tail, "");
    }

    /// <summary>'thieu' (ô trống) | 'cut' (bị cắt) | 'dung' | 'khac'.</summary>
    public static string NameStatus(string? oldName, string neu)
    {
        var o = (oldName ?? "").Trim();
        if (o.Length == 0) return "thieu";
        static string K(string s) => Regex.Replace(s.ToLowerInvariant(), @"[\s,]+", "");
        string a = K(o), b = K(neu);
        if (a == b) return "dung";
        if (a.Length < b.Length && b.StartsWith(a)) return "cut";
        return "khac";
    }

    public sealed record NameFix(Row Row, string Old, string New, string Status, string Err);

    /// <summary>Phân tích tên hàng của loại phiếu đang xem so với Mã hóa sản phẩm (dữ liệu Tổng hợp).</summary>
    public List<NameFix> AnalyzeNames(string mod, bool overwriteAll)
    {
        RequireView();
        if (_tongHop is null) throw new BusinessException("Chưa nối với Mã hóa sản phẩm.");
        _tongHop.Load();
        var dict = _tongHop.SegmentData.ToDictionary(p => p.Key, p => p.Value.GroupBy(e => e.Code.Trim().ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First().Name));
        if (dict.Values.All(d => d.Count == 0)) throw new BusinessException("Mã hóa sản phẩm (module Tổng hợp) chưa có dữ liệu.");
        string? Lookup(int seg, string code) => dict.TryGetValue(seg, out var d) && d.TryGetValue(code, out var n) ? n : null;
        var res = new List<NameFix>();
        foreach (var r in Rows[mod])
        {
            var c = Compose(r.MaHang, Lookup);
            if (!c.Ok) { res.Add(new(r, r.TenHang, "", "loi", c.Err)); continue; }
            res.Add(new(r, r.TenHang, c.Ten, NameStatus(r.TenHang, c.Ten), ""));
        }
        return res;
    }
    public static bool ShouldFix(NameFix f, bool overwriteAll) => f.Status is "thieu" or "cut" || (f.Status == "khac" && overwriteAll);

    public int ApplyNames(string mod, IEnumerable<NameFix> fixes, bool mark)
    {
        RequireEdit();
        var n = 0;
        foreach (var f in fixes) { f.Row.TenHang = f.New; f.Row.TenMoi = mark; n++; }
        if (n > 0) { Save(mod); Log("Bổ sung tên hàng", $"{K(mod).Label}: {n} dòng"); }
        return n;
    }

    // ------------------------------------------------------------------ phiếu in

    public sealed record DocMeta(string SoPhieu, string BoPhan, string LoaiChungTu, string MaBieuMau, string GhiChu, string NgayLap);

    public DocMeta Meta()
    {
        string F(string k) => (Form.GetValueOrDefault(k) ?? "").Trim();
        var ngay = F("fNgayLap");
        var m = Regex.Match(ngay, @"^(\d{4})-(\d{2})-(\d{2})$");
        return new(F("fSoPhieu").Length > 0 ? F("fSoPhieu") : "—", F("fKho").Length > 0 ? F("fKho") : "—", F("fLoaiChungTu").Length > 0 ? F("fLoaiChungTu") : "—",
            F("fMaBieuMau"), F("fGhiChu").Length > 0 ? F("fGhiChu") : "…………………..", m.Success ? $"{m.Groups[3]}/{m.Groups[2]}/{m.Groups[1]}" : ngay);
    }

    public int Copies => int.TryParse(Form.GetValueOrDefault("fSoBanMoiNgay"), out var n) && n > 0 ? Math.Min(20, n) : 1;

    public static byte[] Logo()
    {
        using var s = typeof(ChungTuService).Assembly.GetManifestResourceStream("KhoLanh.Core.ChungTu.logo-phieu.png");
        if (s is null) return Array.Empty<byte>();
        using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
    }

    public static string Fmt(double v) => v.ToString("#,0.##", CultureInfo.GetCultureInfo("en-US"));
    public static string FmtNet(double v) => v.ToString("#,0.00", CultureInfo.GetCultureInfo("en-US"));
    private static string H(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

    /// <summary>Trang HTML in các phiếu (mỗi phiếu × số bản, sang trang mới) — mở bằng trình duyệt rồi In / Lưu PDF.</summary>
    public string PrintHtml(string mod, IReadOnlyList<Group> groups, int copies)
    {
        RequireExport();
        var meta = Meta();
        var k = K(mod);
        var names = SignerNames(mod);
        var logo = "data:image/png;base64," + Convert.ToBase64String(Logo());
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"vi\"><head><meta charset=\"utf-8\"><title>").Append(H(k.DocName)).Append("</title><style>")
          .Append("""
            @page{size:A4 portrait;margin:12mm 12mm 12mm 12mm}
            body{margin:0;background:#e9edf2;font-family:'Times New Roman',Times,serif;color:#000}
            .bar{position:sticky;top:0;background:#0b2a45;color:#fff;padding:8px 14px;font:13px Arial,sans-serif;display:flex;gap:10px;align-items:center;z-index:2}
            .bar button{font:13px Arial,sans-serif;padding:5px 14px;border-radius:5px;border:0;background:#1f8fd1;color:#fff;cursor:pointer}
            .dn-page{width:186mm;margin:10mm auto;background:#fff;padding:10mm 12mm;box-shadow:0 1px 6px rgba(0,0,0,.25);box-sizing:content-box}
            .dn-head{display:flex;align-items:flex-start;gap:10px;border:1px solid #000;padding:6px 10px;margin-bottom:6px}
            .dn-logo{width:46px;height:46px;object-fit:contain;flex-shrink:0}
            .dn-title{flex:1;text-align:center}.dn-title-en{font-weight:700;font-size:19px;letter-spacing:.3px}.dn-title-vn{font-size:14px}
            .dn-code{font-size:12px;border:1px solid #000;padding:8px 10px;min-width:70px;text-align:center}
            .dn-so{font-weight:700;font-size:13.5px;text-align:center;margin-bottom:6px}
            .dn-row{display:flex;gap:24px;font-size:12.5px;margin-bottom:2px}.dn-col{flex:1 1 240px}
            table{width:100%;border-collapse:collapse;font-size:11.5px;margin-top:10px}
            th,td{border:1px solid #000;padding:4px 6px;vertical-align:top}
            th{font-weight:700;text-align:center;vertical-align:middle}
            thead{display:table-header-group}tr{page-break-inside:avoid}
            .c-stt{width:26px;text-align:center}.c-size{width:50px;text-align:center;vertical-align:middle}
            .num{text-align:right;width:68px;vertical-align:middle}.c-sl{font-weight:700}
            .item-code,.item-name{display:block}
            tfoot td{font-weight:700;text-align:center}tfoot td.num{text-align:right}
            .dn-sign{display:flex;justify-content:space-between;margin-top:24px;font-size:11px;text-align:center;page-break-inside:avoid}
            .dn-sign-col{flex:1;display:flex;flex-direction:column;align-items:center;padding:0 4px}
            .dn-sign-role{font-weight:700;margin-bottom:65px}.dn-sign-name{font-size:10.5px;min-height:13px}
            @media print{body{background:#fff}.bar{display:none}.dn-page{margin:0;padding:0;box-shadow:none;width:auto}.dn-page+.dn-page{page-break-before:always}}
            """)
          .Append("</style></head><body><div class=\"bar\"><b>").Append(H(k.DocName)).Append("</b><span>")
          .Append(groups.Count).Append(" phiếu × ").Append(copies).Append(" bản</span><button onclick=\"window.print()\">In / Lưu PDF</button>")
          .Append("<span style=\"opacity:.75\">Chọn máy in “Microsoft Print to PDF” hoặc “Lưu dưới dạng PDF” để ra file PDF.</span></div>");
        foreach (var g in groups)
            for (var c = 0; c < copies; c++)
            {
                sb.Append("<div class=\"dn-page\"><div class=\"dn-head\"><img class=\"dn-logo\" src=\"").Append(logo).Append("\" alt=\"\"><div class=\"dn-title\"><div class=\"dn-title-en\">DISPATCHING NOTE</div><div class=\"dn-title-vn\">(Phiếu xuất)</div></div><div class=\"dn-code\">")
                  .Append(H(meta.MaBieuMau)).Append("</div></div>")
                  .Append("<div class=\"dn-so\">Số : ").Append(H(g.SoPhieu)).Append("</div>")
                  .Append("<div class=\"dn-row\"><div class=\"dn-col\">").Append(H(g.DateLine)).Append("</div><div class=\"dn-col\">Loại Chứng Từ : ").Append(H(meta.LoaiChungTu)).Append("</div></div>")
                  .Append("<div class=\"dn-row\"><div class=\"dn-col\">Bộ phận : ").Append(H(meta.BoPhan)).Append("</div><div class=\"dn-col\">Ghi chú : ").Append(H(meta.GhiChu)).Append("</div></div>")
                  .Append("<table><thead><tr><th class=\"c-stt\">STT</th><th>MÃ HÀNG</th><th class=\"c-size\">Size</th><th class=\"num c-sl\">Số lượng</th><th class=\"num\">W/pkg</th><th class=\"num\">Net(kg)</th></tr></thead><tbody>");
                for (var i = 0; i < g.Items.Count; i++)
                {
                    var r = g.Items[i];
                    sb.Append("<tr><td class=\"c-stt\">").Append(i + 1).Append("</td><td><span class=\"item-code\">").Append(H(r.MaHang)).Append("</span><span class=\"item-name\">").Append(H(r.TenHang))
                      .Append("</span></td><td class=\"c-size\">").Append(H(r.SizeText)).Append("</td><td class=\"num c-sl\">").Append(Fmt(r.SoLuong)).Append("</td><td class=\"num\">")
                      .Append(Fmt(r.TrongLuong)).Append("</td><td class=\"num\">").Append(FmtNet(r.Net)).Append("</td></tr>");
                }
                sb.Append("</tbody><tfoot><tr><td colspan=\"3\">Tổng kiện</td><td class=\"num c-sl\">").Append(Fmt(g.SumSL)).Append("</td><td></td><td class=\"num\">").Append(FmtNet(g.SumNet)).Append("</td></tr></tfoot></table>")
                  .Append("<div class=\"dn-sign\">");
                for (var i = 0; i < k.Roles.Length; i++)
                    sb.Append("<div class=\"dn-sign-col\"><div class=\"dn-sign-role\">").Append(H(k.Roles[i])).Append("</div><div class=\"dn-sign-name\">").Append(H(names[i])).Append("</div></div>");
                sb.Append("</div></div>");
            }
        sb.Append("</body></html>");
        return sb.ToString();
    }

    /// <summary>Ghi trang in ra file .html (thư mục tạm) — người dùng mở bằng trình duyệt để in / lưu PDF.</summary>
    public string WritePrintFile(string mod, IReadOnlyList<Group> groups, int copies, string dir)
    {
        Directory.CreateDirectory(dir);
        foreach (var old in Directory.GetFiles(dir, "chung-tu-*.html").Where(f => File.GetLastWriteTime(f) < DateTime.Now.AddDays(-2)))
            try { File.Delete(old); } catch { }
        var name = groups.Count == 1 ? groups[0].SoPhieu : K(mod).Prefix + "-" + groups.Count + "phieu";
        var path = Path.Combine(dir, $"chung-tu-{name}-{DateTime.Now:HHmmss}.html");
        File.WriteAllText(path, PrintHtml(mod, groups, copies), Encoding.UTF8);
        Log("In phiếu", $"{K(mod).Label}: {groups.Count} phiếu × {copies} bản");
        return path;
    }

    // ------------------------------------------------------------------ xuất Excel (mẫu BM02/QT23)

    /// <summary>Mỗi phiếu 1 sheet theo mẫu Phiếu xuất / Dispatching Note (Times New Roman, logo, ô ký).</summary>
    public void ExportExcel(string mod, IReadOnlyList<Group> groups, string path)
    {
        RequireExport();
        if (groups.Count == 0) throw new BusinessException("Không có phiếu nào để xuất.");
        var meta = Meta();
        var k = K(mod);
        var names = SignerNames(mod);
        var logo = Logo();
        using var wb = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in groups)
        {
            var nm = Regex.Replace(g.Label.Replace("/", "-"), @"[\[\]\*\?:\\/]", "-");
            nm = nm.Length > 31 ? nm[..31] : nm;
            if (nm.Length == 0) nm = "Phieu";
            var bas = nm; var n = 2;
            while (used.Contains(nm)) { var suf = $" ({n++})"; nm = bas[..Math.Min(bas.Length, 31 - suf.Length)] + suf; }
            used.Add(nm);
            Sheet(wb.AddWorksheet(nm), g, meta, k, names, logo);
        }
        wb.SaveInPlace(path);
        Log("Xuất Excel", $"{k.Label}: {groups.Count} phiếu → {Path.GetFileName(path)}");
    }

    private const string Font = "Times New Roman";

    private static void Sheet(IXLWorksheet ws, Group g, DocMeta meta, Kind k, string[] names, byte[] logo)
    {
        double[] widths = { 5.14, 60, 8, 9.5, 9, 12.5 };
        for (var i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];
        ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.Margins.Left = 0.35; ws.PageSetup.Margins.Right = 0.35; ws.PageSetup.Margins.Top = 0.4; ws.PageSetup.Margins.Bottom = 0.4;
        ws.PageSetup.Margins.Header = 0; ws.PageSetup.Margins.Footer = 0;
        ws.Style.Font.FontName = Font;

        if (logo.Length > 0)
        {
            using var ms = new MemoryStream(logo);
            ws.AddPicture(ms).MoveTo(ws.Cell(1, 1), 2, 3).WithSize(56, 56);
        }
        var title = ws.Range("A1:D1").Merge();
        title.Value = "DISPATCHING NOTE\n(Phiếu xuất)";
        title.Style.Font.SetBold().Font.SetFontSize(18).Font.SetFontName(Font);
        title.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetWrapText();
        title.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        var code = ws.Range("E1:F1").Merge();
        code.Value = meta.MaBieuMau;
        code.Style.Font.SetFontSize(12).Font.SetFontName(Font);
        code.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetWrapText();
        code.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Row(1).Height = 46;

        var so = ws.Range("A2:F2").Merge();
        so.Value = "Số : " + g.SoPhieu;
        so.Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontName(Font);
        so.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center);

        void Txt(string a, string v) { var c = ws.Range(a).Merge(); c.Value = v; c.Style.Font.SetFontSize(12).Font.SetFontName(Font); c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left; }
        Txt("A3:B3", g.DateLine);
        Txt("C3:F3", "Loại Chứng Từ : " + meta.LoaiChungTu);
        Txt("A4:B4", "Bộ phận : " + meta.BoPhan);
        Txt("C4:F4", "Ghi chú : " + meta.GhiChu);
        ws.Row(5).Height = 7.5;

        const int hr = 6;
        string[] heads = { "STT", "MÃ HÀNG", "Size", "Số lượng", "W/pkg", "Net(kg)" };
        for (var c = 0; c < heads.Length; c++)
        {
            var cell = ws.Cell(hr, c + 1);
            cell.Value = heads[c];
            cell.Style.Font.SetBold().Font.SetFontSize(12).Font.SetFontName(Font);
            cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetWrapText();
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }
        ws.Row(hr).Height = 30;
        for (var i = 0; i < g.Items.Count; i++)
        {
            var r = g.Items[i];
            var row = hr + 1 + i;
            ws.Cell(row, 1).Value = i + 1;
            ws.Cell(row, 2).Value = r.MaHang + "\n" + r.TenHang;
            ws.Cell(row, 3).Value = r.SizeText;
            ws.Cell(row, 4).Value = r.SoLuong;
            ws.Cell(row, 5).Value = r.TrongLuong;
            ws.Cell(row, 6).Value = r.Net;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 1).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Top);
            ws.Cell(row, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left).Alignment.SetVertical(XLAlignmentVerticalValues.Top).Alignment.SetWrapText();
            ws.Cell(row, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center).Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetWrapText();
            for (var c = 4; c <= 6; c++) ws.Cell(row, c).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            for (var c = 1; c <= 6; c++)
            {
                ws.Cell(row, c).Style.Font.SetFontSize(11).Font.SetFontName(Font).Font.SetBold(c == 4);
                ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            var lines = 1 + Math.Max(1, (int)Math.Ceiling(r.TenHang.Length / 62.0));
            ws.Row(row).Height = Math.Max(28, lines * 15 + 6);
        }
        var tr = hr + 1 + g.Items.Count;
        ws.Range(tr, 1, tr, 3).Merge().Value = "Tổng kiện";
        ws.Cell(tr, 4).Value = g.SumSL;
        ws.Cell(tr, 6).Value = g.SumNet;
        ws.Cell(tr, 6).Style.NumberFormat.Format = "#,##0.00";
        for (var c = 1; c <= 6; c++)
        {
            ws.Cell(tr, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            ws.Cell(tr, c).Style.Font.SetBold().Font.SetFontSize(11).Font.SetFontName(Font);
        }
        ws.Cell(tr, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Cell(tr, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Cell(tr, 6).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

        var sr = tr + 2;
        var role = ws.Range(sr, 1, sr, 6).Merge();
        role.Value = PaddedLine(k.Roles);
        role.Style.Font.SetBold().Font.SetFontSize(9.5).Font.SetFontName(Font);
        role.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        var nr = sr + 3;
        var nm2 = ws.Range(nr, 1, nr, 6).Merge();
        nm2.Value = PaddedLine(names);
        nm2.Style.Font.SetFontSize(10).Font.SetFontName(Font);
        nm2.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.PageSetup.PrintAreas.Add(1, 1, nr, 6);
    }

    /// <summary>Rải các nhãn ký trên 1 dòng chữ bằng khoảng trắng (1 ô gộp A–F) — giống app cũ để không bị cột bảng bóp lệch.</summary>
    public static string PaddedLine(IEnumerable<string> items, int slot = 34, int start = 4)
    {
        var sb = new StringBuilder();
        var i = 0;
        foreach (var it in items)
        {
            var target = start + i * slot;
            while (sb.Length < target) sb.Append(' ');
            sb.Append(it);
            i++;
        }
        return sb.ToString();
    }
}
