using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KhoLanh.Core.LichXuat;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using KhoLanh.Core.TonKho;
using KhoLanh.Core.TonKhoGui;

namespace KhoLanh.Core.PhoneSync;

/// <summary>
/// Phần đồng bộ điện thoại còn thiếu so với app PC cũ (1.55) — chép main.js:
/// <list type="bullet">
/// <item>Báo cáo Excel cho điện thoại tải về: tab <c>BaoCao_M01</c> (Báo cáo tổng tồn 7 module + Bảng tổng hợp HLSO + dữ liệu Phân loại) và <c>BaoCao_M02</c>
/// (Bảng Tổng Hợp + Phân loại kho gửi) — file cắt base64 thành nhiều dòng (pushReportTab / buildM01ReportRows). Bản C# dựng báo cáo thẳng từ dữ liệu
/// (không cần mở trang module như app cũ).</item>
/// <item>Kháng sinh "chỉ phần mới" (pushKhangSinhIncremental + pc.ksUpsert; Code.gs cũ → đẩy cả bảng).</item>
/// <item>Hộp thư yêu cầu lịch xuất / nhập từ điện thoại (tab <c>LichXuat_YeuCau</c>: thêm nhanh / chuyển ngày).</item>
/// <item>Lưu trữ phiếu cũ trên Sheet (pc.archive, phiếu xong &gt; 90 ngày, tối đa 1 lần / ngày).</item>
/// </list>
/// </summary>
public sealed partial class PhoneSyncService
{
    public const string TabBaoCaoM01 = "BaoCao_M01", TabBaoCaoM02 = "BaoCao_M02", TabLxYc = "LichXuat_YeuCau";
    public static readonly string[] ReportColumns = { "key", "title", "name", "reportDate", "generatedAt", "part", "parts", "data" };
    public static readonly string[] LxYcHeaders = { "id", "hanhDong", "loai", "targetId", "ngay", "lsx", "trangThaiLich", "nguoiTao", "nguoiDung", "taoLuc", "trangThai", "xuLyLuc" };
    public const int ReportChunk = 40000;
    public const string KeyKsState = "phone_ks_state", KeyArchiveDay = "phone_archive_day";
    public const int KsFormat = 1, ArchiveDays = 90;

    // ================================================================ BÁO CÁO EXCEL
    public sealed record ReportFile(string Key, string Title, string Name, string B64);
    public sealed record ReportSnap(string GeneratedAt, string ReportDate, List<ReportFile> Files, List<string> Errors);

    private static string Today() => DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
    private static string B64(XlsxOut x) { using var ms = x.ToStream(); return Convert.ToBase64String(ms.ToArray()); }
    private static string Utf8B64(JsonObject o) => Convert.ToBase64String(Encoding.UTF8.GetBytes(o.ToJsonString(J)));
    private static double R1(double x) => Math.Round(x * 10, MidpointRounding.AwayFromZero) / 10;

    /// <summary>Báo cáo Module 01 (Tồn kho An An): tổng tồn 7 module + HLSO + Phân loại (buildPhoneReportSnapshot).</summary>
    public ReportSnap? BuildReportM01()
    {
        var a = _s.AnAn; if (!a.Loaded) a.Load();
        var d = a.D; if (!d.HasData) return null;
        var now = Now(); var date = d.DataDate.Length > 0 ? d.DataDate : Today(); var stamp = AnAnExport.Stamp(now);
        var snap = new ReportSnap(NowIso(), date, new(), new());
        void Add(string key, string title, string name, Func<XlsxOut?> build)
        {
            try { if (build() is { } x) snap.Files.Add(new ReportFile(key, title, name, B64(x))); }
            catch (Exception e) { snap.Errors.Add(title + ": " + e.Message); }
        }
        Add("tongton", "Báo cáo tổng tồn (AA)", $"BaoCao_TongTon_7Module_{stamp}.xlsx", () => AnAnExport.BaoCaoAll(d, date));
        Add("hlso", "Báo cáo tổng hợp HLSO (AA)", $"BangTongHopHLSO_{stamp}.xlsx", () => AnAnExport.HlsoSummary(d, date));
        try { var pl = PhanLoaiM01(d); if (pl["loai"] is JsonArray l && l.Count > 0) snap.Files.Add(new ReportFile("phanloai", "Phân loại (AA)", "PhanLoai_M01.json", Utf8B64(pl))); }
        catch (Exception e) { snap.Errors.Add("Phân loại: " + e.Message); }
        return snap;
    }

    /// <summary>Báo cáo Module 02 (Tồn kho gửi): Bảng Tổng Hợp + Phân loại (buildM2PhoneReportSnapshot).</summary>
    public ReportSnap? BuildReportM02()
    {
        var g = _s.Gui; if (!g.Loaded) g.Load();
        var d = g.D; if (!d.HasData) return null;
        var date = d.DataDate.Length > 0 ? d.DataDate : Today();
        var snap = new ReportSnap(NowIso(), date, new(), new());
        try { snap.Files.Add(new ReportFile("tonghop", "Báo cáo kho gửi", GuiExport.BangTongHopFileName(Now()), B64(GuiExport.BangTongHop(d.ComputePivot(), date)))); }
        catch (Exception e) { snap.Errors.Add("Bảng Tổng Hợp: " + e.Message); }
        try { var pl = PhanLoaiM02(d); if (pl["loai"] is JsonArray l && l.Count > 0) snap.Files.Add(new ReportFile("phanloai", "Phân loại (Kho gửi)", "PhanLoai_M02.json", Utf8B64(pl))); }
        catch (Exception e) { snap.Errors.Add("Phân loại: " + e.Message); }
        return snap;
    }

    /// <summary>Mỗi file = nhiều dòng (part 0, 1, 2…), cột data có tiền tố "~" (Sheet không hiểu nhầm base64 là công thức) — khớp getBaoCaoM01_() của Code.gs.</summary>
    public static List<Dictionary<string, JsonNode?>> ReportRows(ReportSnap snap)
    {
        var rows = new List<Dictionary<string, JsonNode?>>();
        foreach (var f in snap.Files)
        {
            var parts = Math.Max(1, (f.B64.Length + ReportChunk - 1) / ReportChunk);
            for (var i = 0; i < parts; i++)
                rows.Add(new()
                {
                    ["key"] = f.Key, ["title"] = f.Title, ["name"] = f.Name, ["reportDate"] = snap.ReportDate, ["generatedAt"] = snap.GeneratedAt, ["part"] = i, ["parts"] = parts,
                    ["data"] = "~" + f.B64.Substring(i * ReportChunk, Math.Min(ReportChunk, f.B64.Length - i * ReportChunk)),
                });
        }
        return rows;
    }

    /// <summary>Đẩy báo cáo 1 module + Meta &lt;prefix&gt;BaoCaoAt / BaoCaoCu / BaoCaoLoi; lỗi KHÔNG làm hỏng lần đẩy tồn.</summary>
    private async Task PushReportAsync(string code, Dictionary<string, JsonNode?> meta, List<string> errors, CancellationToken ct)
    {
        var (tab, prefix) = code == "M01" ? (TabBaoCaoM01, "m01") : (TabBaoCaoM02, "m02");
        try
        {
            var snap = code == "M01" ? BuildReportM01() : BuildReportM02();
            if (snap is null) return;
            await W.WriteTableAsync(tab, ReportColumns, ReportRows(snap).Select(r => (IDictionary<string, JsonNode?>)r), ct);
            meta[prefix + "BaoCaoAt"] = snap.GeneratedAt; meta[prefix + "BaoCaoCu"] = ""; meta[prefix + "BaoCaoLoi"] = string.Join(" | ", snap.Errors);
        }
        catch (Exception e) { meta[prefix + "BaoCaoLoi"] = e.Message; errors.Add($"Báo cáo {code}: {e.Message}"); }
    }

    // ---- Phân loại (bảng con theo Size + mã hàng lớn nhất + bảng chéo Size × Đặc tính) ----
    private static readonly Dictionary<string, string> M01Color = new()
    {
        ["tpc"] = "#34D399", ["tptam"] = "#F0A94E", ["bm"] = "#2DD4BF", ["hlso"] = "#FB7185", ["tomgc"] = "#38BDF8", ["tomnk"] = "#E879F9", ["tomnl"] = "#A3E635", ["tomgs"] = "#FB923C",
    };
    private sealed class PlSize { public string Label = ""; public double Net; public int Rows; public int Low; public Dictionary<string, PlItem> Items = new(); }
    private sealed class PlItem { public string Ma = "", Sub = "", Dt = ""; public double Net; public bool Low; }

    private static JsonObject PlLoai(string id, string name, string color, double total, int rows, IEnumerable<PlSize> sizes, Dictionary<string, Dictionary<string, double>> cross, Dictionary<string, double> dtTot, Comparison<string> cmp)
    {
        var list = sizes.ToList(); list.Sort((a, b) => cmp(a.Label, b.Label));
        var arr = new JsonArray();
        foreach (var sz in list)
        {
            var all = AnAnData.StableSort(sz.Items.Values.ToList(), (a, b) => b.Net.CompareTo(a.Net));
            arr.Add(new JsonObject
            {
                ["label"] = sz.Label, ["net"] = R1(sz.Net), ["rows"] = sz.Rows, ["low"] = all.Count(i => i.Low), ["more"] = Math.Max(0, all.Count - 25),
                ["items"] = new JsonArray(all.Take(25).Select(i => (JsonNode)new JsonObject { ["ma"] = i.Ma, ["sub"] = i.Sub, ["dt"] = i.Dt, ["net"] = R1(i.Net), ["low"] = i.Low ? 1 : 0 }).ToArray()),
            });
        }
        var lo = new JsonObject { ["id"] = id, ["name"] = name, ["color"] = color, ["net"] = R1(total), ["rows"] = rows, ["sizes"] = arr };
        if (dtTot.Count > 0) lo["cross"] = PlCross(list.Select(s => s.Label).ToList(), cross, dtTot, 6);
        return lo;
    }
    /// <summary>plBuildCross: tối đa 6 đặc tính lớn nhất, còn lại gộp cột "Khác".</summary>
    private static JsonObject PlCross(List<string> sizes, Dictionary<string, Dictionary<string, double>> cell, Dictionary<string, double> dtTot, int maxCols)
    {
        var order = AnAnData.StableSort(dtTot.ToList(), (a, b) => b.Value.CompareTo(a.Value)).Select(x => x.Key).ToList();
        var main = order.Take(maxCols).ToList(); var rest = order.Skip(maxCols).ToList();
        var cols = new JsonArray(main.Select(l => (JsonNode)new JsonObject { ["label"] = l }).ToArray());
        if (rest.Count > 0) cols.Add(new JsonObject { ["label"] = "Khác", ["other"] = rest.Count });
        var rows = sizes.Select(sz =>
        {
            var m = cell.GetValueOrDefault(sz) ?? new();
            var cells = main.Select(l => R1(m.GetValueOrDefault(l))).ToList();
            if (rest.Count > 0) cells.Add(R1(rest.Sum(l => m.GetValueOrDefault(l))));
            return (Label: sz, Cells: cells, Total: R1(m.Values.Sum()));
        }).ToList();
        return new JsonObject
        {
            ["cols"] = cols,
            ["rows"] = new JsonArray(rows.Select(r => (JsonNode)new JsonObject { ["label"] = r.Label, ["cells"] = new JsonArray(r.Cells.Select(c => (JsonNode)c).ToArray()), ["total"] = r.Total }).ToArray()),
            ["totals"] = new JsonArray(Enumerable.Range(0, main.Count + (rest.Count > 0 ? 1 : 0)).Select(i => (JsonNode)R1(rows.Sum(r => r.Cells[i]))).ToArray()),
            ["total"] = R1(rows.Sum(r => r.Total)),
        };
    }

    /// <summary>buildPhanLoaiSnapshot (Module 01): 8 bảng Phân Loại, KG = Tồn cuối × Trọng lượng; TPC đánh dấu mã "Sắp hết".</summary>
    public static JsonObject PhanLoaiM01(AnAnData d)
    {
        var outp = new JsonObject { ["v"] = 1, ["nguon"] = "m01", ["threshold"] = d.TpcThreshold, ["loai"] = new JsonArray() };
        if (!d.HasData) return outp;
        int sizeCi = d.Col("Size"), maCi = d.Col("Mã hàng"), dtCi = d.Col("Đặc tính"), hdCi = d.Col("Hợp đồng");
        var basis = d.WeightBasis(); if (basis is null) return outp;
        foreach (var mod in AnAnData.FilterModules)
        {
            var isTpc = mod.Id == "tpc";
            var bySize = new Dictionary<string, PlSize>(); var cross = new Dictionary<string, Dictionary<string, double>>(); var dtTot = new Dictionary<string, double>();
            double total = 0; var rows = 0;
            foreach (var i in d.BaseRows(mod.Id))
            {
                var v = d.RowWeight(i, basis.Value); rows++;
                string Cell(int ci) => ci >= 0 ? JsText.JsTrim(d.Cell(i, ci)) : "";
                var size = Cell(sizeCi); var label = size.Length > 0 ? size : "(Không có size)";
                if (!bySize.TryGetValue(label, out var sz)) bySize[label] = sz = new PlSize { Label = label };
                sz.Rows++;
                if (v is double w)
                {
                    sz.Net += w; total += w;
                    if (dtCi >= 0)
                    {
                        var dtl = Cell(dtCi); if (dtl.Length == 0) dtl = "(Trống)";
                        if (!cross.TryGetValue(label, out var cm)) cross[label] = cm = new();
                        cm[dtl] = cm.GetValueOrDefault(dtl) + w; dtTot[dtl] = dtTot.GetValueOrDefault(dtl) + w;
                    }
                }
                var ma = Cell(maCi); var hd = isTpc ? Cell(hdCi) : ""; var key = ma + "|" + hd;
                if (!sz.Items.TryGetValue(key, out var it)) sz.Items[key] = it = new PlItem { Ma = ma, Sub = hd, Dt = Cell(dtCi) };
                if (v is double w2) it.Net += w2;
                if (isTpc && d.TpcStatus(i) == "Sắp hết") it.Low = true;
            }
            ((JsonArray)outp["loai"]!).Add(PlLoai(mod.Id, mod.Label, M01Color.GetValueOrDefault(mod.Id, "#94A3B8"), total, rows, bySize.Values, cross, dtTot, AnAnData.CompareSize));
        }
        return outp;
    }

    private static readonly (string Id, string Name, string Color)[] M02Defs =
    {
        ("nl", "Tôm Nguyên Liệu", "#A3E635"), ("5", "Tôm HLSO", "#FB7185"), ("4", "Tôm Giám Sát", "#FB923C"), ("3", "Tôm Gia Công", "#38BDF8"), ("ad", "Ấn Độ", "#818CF8"), ("2", "Ecuador", "#F472B6"),
    };
    /// <summary>plSizeCmp (Module 02): số đầu tiên trong nhãn size, rồi so chữ.</summary>
    public static int PlSizeCmp(string a, string b)
    {
        static double N(string s) { var m = System.Text.RegularExpressions.Regex.Match(s, @"\d+(?:[.,]\d+)?"); return m.Success && double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : double.NaN; }
        double na = N(a), nb = N(b);
        if (!double.IsNaN(na) && !double.IsNaN(nb) && na != nb) return na.CompareTo(nb);
        if (!double.IsNaN(na) && double.IsNaN(nb)) return -1;
        if (double.IsNaN(na) && !double.IsNaN(nb)) return 1;
        return JsText.ViNumericCompare(a, b);
    }
    /// <summary>buildM2PhanLoai (Module 02): 6 nhóm như các tab con Phân Loại (cùng điều kiện lọc tự thêm), KG = Trọng lượng.</summary>
    public static JsonObject PhanLoaiM02(GuiData d)
    {
        var outp = new JsonObject { ["v"] = 1, ["nguon"] = "m02", ["loai"] = new JsonArray() };
        if (!d.HasData) return outp;
        foreach (var (id, name, color) in M02Defs)
        {
            var rows = d.GroupRows(id);
            var bySize = new Dictionary<string, PlSize>(); var cross = new Dictionary<string, Dictionary<string, double>>(); var dtTot = new Dictionary<string, double>(); double total = 0;
            foreach (var r in rows)
            {
                var w = r.TrongLuong; var label = (r.Size ?? "").Trim(); if (label.Length == 0) label = "(Không có size)";
                total += w;
                var dtl = (r.DacTinh ?? "").Trim(); if (dtl.Length == 0) dtl = "(Trống)";
                if (!cross.TryGetValue(label, out var cm)) cross[label] = cm = new();
                cm[dtl] = cm.GetValueOrDefault(dtl) + w; dtTot[dtl] = dtTot.GetValueOrDefault(dtl) + w;
                if (!bySize.TryGetValue(label, out var sz)) bySize[label] = sz = new PlSize { Label = label };
                sz.Net += w; sz.Rows++;
                var ma = (r.MaHang ?? "").Trim(); var lo = (r.SoLo ?? "").Trim(); var key = ma + "|" + lo;
                if (!sz.Items.TryGetValue(key, out var it)) sz.Items[key] = it = new PlItem { Ma = ma, Sub = lo.Length > 0 ? "Lô " + lo : "", Dt = (r.DacTinh ?? "").Trim() };
                it.Net += w;
            }
            ((JsonArray)outp["loai"]!).Add(PlLoai(id, name, color, total, rows.Count, bySize.Values, cross, dtTot, PlSizeCmp));
        }
        return outp;
    }

    // ================================================================ KHÁNG SINH "CHỈ PHẦN MỚI"
    private static readonly string[] KsAb = { "enro", "cipro", "oxy", "doxy", "sulfo", "aoz", "cap" };
    private static JsonNode Or(JsonNode? n) => n switch
    {
        null => "",
        JsonValue v when v.TryGetValue<string>(out var s) => s.Length == 0 ? "" : s,
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? n.DeepClone() : "",
        JsonValue v when v.TryGetValue<double>(out var d) => d == 0 || double.IsNaN(d) ? "" : n.DeepClone(),
        _ => n.DeepClone(),
    };
    private static string JsStr(JsonNode? n) => n is null ? "" : n is JsonValue v && v.TryGetValue<string>(out var s) ? s : n is JsonValue v2 && v2.TryGetValue<double>(out var d) ? JsNum(d) : n is JsonValue v3 && v3.TryGetValue<bool>(out var b) ? (b ? "true" : "false") : n.ToJsonString(J);

    /// <summary>ksCompact: bản rút gọn 1 dòng kháng sinh cho điện thoại ("~" + JSON; điện thoại tự mở rộng).</summary>
    public static string KsCompact(string key, JsonNode? value)
    {
        string Raw() => value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value is null ? "" : value.ToJsonString(J);
        if (!key.StartsWith("s:batch:", StringComparison.Ordinal)) return Raw();
        JsonObject? r;
        try { r = value is JsonValue sv && sv.TryGetValue<string>(out var txt) ? JsonNode.Parse(txt) as JsonObject : value as JsonObject; }
        catch { return value is JsonValue v2 && v2.TryGetValue<string>(out var t2) ? t2 : ""; }
        if (r is null) return "";
        var inp = r["inputs"] as JsonObject ?? new JsonObject();
        var src = r["lan2All"] is JsonArray la && la.Count > 0 ? la.ToList() : r["lan2"] is JsonObject l2 ? new List<JsonNode?> { l2 } : new List<JsonNode?>();
        var L = new JsonArray(src.Select(l => (JsonNode)new JsonArray(new[] { "date", "dat", "kyhieu", "ghichu", "so", "stt", "mau", "group" }.Select(f => Or(l?[f])).ToArray())).ToArray());
        var o = new JsonObject
        {
            ["d"] = Or(r["date"]), ["b"] = Or(r["batch"]), ["n"] = Or(r["note"]), ["r"] = Or(r["dat"]), ["k"] = Or(r["kyhieu"]),
            ["i"] = new JsonArray(KsAb.Select(f => (JsonNode)(inp[f] is null ? "" : JsStr(inp[f]))).ToArray()), ["s"] = Or(r["savedAt"]),
        };
        if (L.Count > 0) o["L"] = L;
        if (r["lan1Src"] is JsonObject l1 && Or(l1["ngayKiem"]) is JsonNode nk && !(nk is JsonValue nv && nv.TryGetValue<string>(out var ns) && ns.Length == 0)) o["nk"] = nk;
        if (Or(r["mota"]) is JsonNode mt && !(mt is JsonValue mv && mv.TryGetValue<string>(out var ms) && ms.Length == 0)) { var t = JsStr(mt); o["m"] = t.Length > 200 ? t[..200] : t; }
        return "~" + o.ToJsonString(J);
    }
    /// <summary>ksHash: FNV-1a 32 bit trên UTF-16 + ":" + độ dài.</summary>
    public static string KsHash(string s)
    {
        uint h = 2166136261; foreach (var ch in s) { h ^= ch; h *= 16777619; }
        const string dg = "0123456789abcdefghijklmnopqrstuvwxyz"; var sb = new StringBuilder(); var x = h;
        do { sb.Insert(0, dg[(int)(x % 36)]); x /= 36; } while (x > 0);
        return sb + ":" + s.Length;
    }

    public sealed record KsPush(int Sent, int Deleted, bool Full, bool Legacy);
    /// <summary>Dòng kháng sinh MỚI / ĐỔI / XÓA so với lần gửi trước (dấu nội dung lưu trong settings); lần đầu gửi cả bảng, đợt 2.000 dòng.</summary>
    public async Task<KsPush> PushKhangSinhAsync(CancellationToken ct = default)
    {
        var rows = Rows(KhangSinh.KhangSinhService.Db, KhangSinh.KhangSinhService.Sheet).Where(r => S(r["key"]).Length > 0).ToList();
        Dictionary<string, string>? prev = null; var full = true;
        try
        {
            var st = JsonNode.Parse(_s.Settings.Get(KeyKsState, "")) as JsonObject;
            if (st?["format"]?.GetValue<int>() == KsFormat && st["hashes"] is JsonObject hs) { prev = hs.ToDictionary(kv => kv.Key, kv => kv.Value?.GetValue<string>() ?? ""); full = false; }
        }
        catch { /* chưa có — gửi cả bảng */ }
        prev ??= new();
        var hashes = new Dictionary<string, string>(); var send = new List<(string K, string V)>();
        foreach (var r in rows)
        {
            var k = S(r["key"]); var v = KsCompact(k, r["value"]); var h = KsHash(v);
            hashes[k] = h;
            if (full || prev.GetValueOrDefault(k) != h) send.Add((k, v));
        }
        var dels = full ? new List<string>() : prev.Keys.Where(k => !hashes.ContainsKey(k)).ToList();
        if (send.Count == 0 && dels.Count == 0) return new KsPush(0, 0, full, false);
        const int ch = 2000;
        try
        {
            for (var i = 0; i < Math.Max(send.Count, 1); i += ch)
            {
                var payload = new JsonObject
                {
                    ["rows"] = new JsonArray(send.Skip(i).Take(ch).Select(x => (JsonNode)new JsonArray(x.K, "", x.V)).ToArray()),
                    ["dels"] = i == 0 ? new JsonArray(dels.Select(k => (JsonNode)new JsonArray(k, "")).ToArray()) : new JsonArray(),
                    ["full"] = full && i == 0,
                };
                await W.CallAsync("pc.ksUpsert", payload, 300, ct);
            }
        }
        catch (BusinessException e) when (e.Message.Contains("Không có chức năng") || e.Message.Contains("bản cũ"))
        {
            // Apps Script cũ chưa có pc.ksUpsert → đẩy cả bảng kiểu cũ (key, value); không lưu dấu → lần sau gửi đầy đủ kiểu mới
            await W.WriteTableAsync(Tab.M08Ks, new[] { "key", "value" }, rows.Select(r => (IDictionary<string, JsonNode?>)new Dictionary<string, JsonNode?> { ["key"] = S(r["key"]), ["value"] = r["value"] is JsonValue jv && jv.TryGetValue<string>(out var sv) ? sv : r["value"]?.ToJsonString(J) ?? "" }), ct);
            _s.Settings.Set(KeyKsState, "");
            return new KsPush(rows.Count, 0, true, true);
        }
        _s.Settings.Set(KeyKsState, new JsonObject { ["format"] = KsFormat, ["at"] = NowIso(), ["hashes"] = new JsonObject(hashes.Select(kv => KeyValuePair.Create(kv.Key, (JsonNode?)kv.Value))) }.ToJsonString());
        return new KsPush(send.Count, dels.Count, full, false);
    }

    // ================================================================ YÊU CẦU LỊCH XUẤT / NHẬP TỪ ĐIỆN THOẠI
    public sealed record LxOp(string Id, string HanhDong, string Loai, string TargetId, string Ngay, string Lsx, string TrangThaiLich, JsonNode? Row);
    public sealed record LxResult(int Done, int Err, List<string> Notes);
    private DateTime _lxCleanAt = DateTime.MinValue;

    /// <summary>Đọc yêu cầu chưa xử lý (tài khoản Quản lý trở lên trên điện thoại gửi: thêm nhanh / chuyển ngày), ghi vào Lịch xuất, đẩy lại bảng LX rồi đánh dấu.</summary>
    public async Task<LxResult> PullLichXuatRequestsAsync(CancellationToken ct = default)
    {
        var lx = _s.LichXuat;
        if (!lx.CanEdit) return new LxResult(0, 0, new());
        await W.EnsureTabsAsync(new Dictionary<string, string[]?> { [TabLxYc] = LxYcHeaders }, ct);
        var t = await W.ReadTableAsync(TabLxYc, ct);
        await CleanOldLxErrorsAsync(t, ct);
        string G(Dictionary<string, JsonNode?> r, string k) => PhoneWebApp.Str(r.GetValueOrDefault(k)).Trim();
        var ops = t.Rows.Where(r => { var st = G(r, "trangThai"); return G(r, "id").Length > 0 && st != DaXuLy && !st.StartsWith("Lỗi", StringComparison.Ordinal); }).Take(60)
            .Select(r => new LxOp(G(r, "id"), G(r, "hanhDong") == "move" ? "move" : "add", G(r, "loai") == "nhap" ? "nhap" : "xuat", G(r, "targetId"),
                G(r, "ngay") is var n && n.Length >= 10 ? n[..10] : n, G(r, "lsx"), G(r, "trangThaiLich"), r.GetValueOrDefault("_rowNumber"))).ToList();
        if (ops.Count == 0) return new LxResult(0, 0, new());
        var res = ApplyLichXuatOps(ops);
        if (res.Count == 0) return new LxResult(0, 0, new());
        try { await PushAsync(new[] { "LX" }, ct); } catch { /* đánh dấu vẫn làm */ }
        var stamp = NowIso();
        await W.MarkRowsAsync(TabLxYc, ops.Where(o => res.ContainsKey(o.Id)).Select(o => (o.Row, o.Id, (IDictionary<string, string>)new Dictionary<string, string>
        {
            ["trangThai"] = res[o.Id] is { } e ? "Lỗi - " + (e.Length > 200 ? e[..200] : e) : DaXuLy, ["xuLyLuc"] = stamp,
        })), ct);
        var errs = res.Count(kv => kv.Value is not null);
        _s.Audit.Log(_s.Session.Username, "Đồng bộ điện thoại: Yêu cầu lịch xuất", $"{res.Count - errs} đã xử lý" + (errs > 0 ? $", {errs} lỗi" : ""));
        return new LxResult(res.Count - errs, errs, res.Where(kv => kv.Value is not null).Select(kv => kv.Key + ": " + kv.Value).ToList());
    }

    /// <summary>Áp các yêu cầu (applyRemoteNow): id → null = đã xử lý, chuỗi = lỗi. Không có trong kết quả = để vòng sau (lỗi chung, vd không có quyền).</summary>
    public Dictionary<string, string?> ApplyLichXuatOps(List<LxOp> ops)
    {
        var lx = _s.LichXuat; var res = new Dictionary<string, string?>();
        var good = new List<LxOp>();
        foreach (var o in ops) { if (LichXuatCore.IsIso(o.Ngay)) good.Add(o); else res[o.Id] = "Ngày không hợp lệ."; }
        var addX = good.Where(o => o.HanhDong == "add" && o.Loai != "nhap").ToList();
        var addN = good.Where(o => o.HanhDong == "add" && o.Loai == "nhap").ToList();
        var mv = good.Where(o => o.HanhDong == "move").ToList();
        if (addX.Count > 0)
        {
            var rows = new List<LichXuatCore.QuickRow>(); var gi = 0;
            foreach (var o in addX)
            {
                var codes = LichXuatCore.Codes(o.Lsx); var st = LichXuatCore.StLabel.ContainsKey(o.TrangThaiLich) ? o.TrangThaiLich : "plan";
                var grp = codes.Count > 1 ? "y" + gi++ : "";
                foreach (var c in codes.Count > 0 ? codes : new List<string> { o.Lsx }) rows.Add(new LichXuatCore.QuickRow(o.Ngay, c, st, grp));
            }
            try { lx.AddQuick(rows); foreach (var o in addX) res[o.Id] = null; }
            catch (BusinessException) { /* lỗi chung → vòng sau */ }
        }
        if (addN.Count > 0)
        {
            try
            {
                var i = 0;
                lx.Mutate(l =>
                {
                    foreach (var o in addN)
                    {
                        if (!l.Any(x => x.IsNhap && x.Ngay == o.Ngay && LichXuatCore.Sq(x.Lsx) == LichXuatCore.Sq(o.Lsx)))
                            l.Add(new Lich { Id = LichXuatCore.NewId(i++), Loai = "nhap", Ngay = o.Ngay, Lsx = o.Lsx, TrangThai = o.TrangThaiLich.Length > 0 ? o.TrangThaiLich : "plan" });
                        res[o.Id] = null;
                    }
                }, $"Nhận {addN.Count} lịch nhập từ điện thoại");
            }
            catch (BusinessException) { foreach (var o in addN) res.Remove(o.Id); }
        }
        foreach (var o in mv)
        {
            try
            {
                lx.Move(new[] { o.TargetId }, o.Ngay);
                if (LichXuatCore.StLabel.ContainsKey(o.TrangThaiLich)) lx.SetStatus(new[] { o.TargetId }, o.TrangThaiLich);
            }
            catch (BusinessException) { /* đã có lịch ngày đó / lịch đã xóa → coi như đã xử lý (PC 8.54) */ }
            res[o.Id] = null;
        }
        return res;
    }

    /// <summary>Dòng yêu cầu "Lỗi - …" cũ hơn 24 giờ → "Đã xử lý (bỏ lỗi cũ)" (điện thoại hết cảnh báo đỏ); tối đa 30 phút / lần.</summary>
    private async Task CleanOldLxErrorsAsync(PhoneWebApp.Table t, CancellationToken ct)
    {
        if ((DateTime.Now - _lxCleanAt).TotalMinutes < 30) return;
        _lxCleanAt = DateTime.Now;
        var cut = Now().ToUniversalTime().AddHours(-24);
        var up = t.Rows.Where(r =>
        {
            if (!PhoneWebApp.Str(r.GetValueOrDefault("trangThai")).Trim().StartsWith("Lỗi", StringComparison.Ordinal)) return false;
            var at = PhoneWebApp.Str(r.GetValueOrDefault("xuLyLuc")); if (at.Length == 0) at = PhoneWebApp.Str(r.GetValueOrDefault("taoLuc"));
            return DateTime.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) && d < cut;
        }).Select(r => (r.GetValueOrDefault("_rowNumber"), PhoneWebApp.Str(r.GetValueOrDefault("id")).Trim(), (IDictionary<string, string>)new Dictionary<string, string> { ["trangThai"] = "Đã xử lý (bỏ lỗi cũ)" })).ToList();
        if (up.Count > 0) try { await W.MarkRowsAsync(TabLxYc, up, ct); } catch { /* lần sau */ }
    }

    // ================================================================ LƯU TRỮ PHIẾU CŨ
    /// <summary>Tối đa 1 lần / ngày: phiếu đã xong &gt; 90 ngày chuyển sang PhieuXuat_LuuTru / PhieuXuatItems_LuuTru (Code.gs pcArchive_). Lỗi chỉ ghi lại, thử lại ngày mai.</summary>
    public async Task<int?> MaybeArchiveAsync(CancellationToken ct = default)
    {
        var today = Now().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (_s.Settings.Get(KeyArchiveDay, "") == today) return null;
        _s.Settings.Set(KeyArchiveDay, today);
        try
        {
            var r = await W.CallAsync("pc.archive", new JsonObject { ["days"] = ArchiveDays }, 300, ct);
            return r["moved"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
        }
        catch (Exception e) { _s.Audit.Log(_s.Session.Username, "Đồng bộ điện thoại: Lưu trữ phiếu cũ", "Lỗi: " + e.Message); return null; }
    }
}
