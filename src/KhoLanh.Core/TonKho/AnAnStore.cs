using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KhoLanh.Core.Modules;

namespace KhoLanh.Core.TonKho;

/// <summary>
/// Đọc / ghi dữ liệu Tồn kho An An trong database "ton-kho-an-an" — ĐÚNG định dạng sheet của app cũ để bản WebView2,
/// điện thoại và các module khác (Đối chiếu, Cargo, Kiểm kê…) vẫn đọc được:
///  LoadData {c0..cN} · LoadDataMeta (1 ô "__JSON__…") · DieuKienTuThem · DacTinh · BaoCaoNgoaiSoSach · PhieuXuat · LanNapTruoc · TheoDoiTon.
/// </summary>
public static class AnAnStore
{
    public const string Db = "ton-kho-an-an";
    public const string SLoadData = "LoadData", SMeta = "LoadDataMeta", SCustom = "DieuKienTuThem", SDacTinh = "DacTinh", SNgss = "BaoCaoNgoaiSoSach",
        SPhieu = "PhieuXuat", SSnap = "LanNapTruoc", STheoDoi = "TheoDoiTon", SNapLog = "NapLog";
    public const string JsonTag = "__JSON__";

    public static JsonArray ReadArr(ISheetSource src, string sheet)
    {
        try { return JsonNode.Parse(src.Read(Db, sheet)) as JsonArray ?? new JsonArray(); }
        catch { return new JsonArray(); }
    }

    /// <summary>Ô JSON "__JSON__{…}" ở dòng đầu (cột data) của 1 sheet; null nếu chưa có.</summary>
    public static JsonNode? ReadJsonCell(ISheetSource src, string sheet)
    {
        var a = ReadArr(src, sheet);
        if (a.Count == 0 || a[0] is not JsonObject o) return null;
        var cell = o["data"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        if (cell is null || !cell.StartsWith(JsonTag)) return null;
        try { return JsonNode.Parse(cell[JsonTag.Length..]); } catch { return null; }
    }

    public static JsonArray JsonCell(JsonNode payload) => new() { new JsonObject { ["data"] = JsonTag + payload.ToJsonString(Opt) } };

    /// <summary>JSON.stringify: không thoát ký tự tiếng Việt.</summary>
    public static readonly JsonSerializerOptions Opt = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string S(JsonNode? n)
    {
        if (n is null) return "";
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s;
            if (v.TryGetValue<double>(out var d)) return JsText.JsStr(d);
            if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        }
        return n.ToJsonString();
    }

    // ------------------------------------------------------------------ đọc

    public static AnAnData Load(ISheetSource src)
    {
        var d = new AnAnData();
        var meta = ReadJsonCell(src, SMeta) as JsonObject;
        if (meta is not null)
        {
            d.Headers = (meta["headers"] as JsonArray ?? new()).Select(S).ToList();
            d.Sheets = (meta["sheets"] as JsonArray ?? new()).Select(S).ToList();
            d.CurrentSheet = meta["currentSheet"] is null ? null : S(meta["currentSheet"]);
            d.FileName = S(meta["fileName"]);
            d.ImportedAt = meta["importedAt"] is JsonValue iv && iv.TryGetValue<double>(out var ia) ? ia : null;
            d.DataDate = S(meta["dataDate"]);
            d.NapId = S(meta["napId"]);
            d.HeaderRowInfo = meta["headerRowInfo"]?.DeepClone();
            if (meta["manualAssign"] is JsonObject ma)
                foreach (var (k, v) in ma)
                    if (int.TryParse(k, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && S(v) is { Length: > 0 } g) d.ManualAssign[i] = g;
            var n = d.Headers.Count;
            var rows = ReadArr(src, SLoadData);
            for (var r = 0; r < rows.Count; r++)
            {
                var o = rows[r] as JsonObject; var arr = new string[n];
                for (var c = 0; c < n; c++)
                {
                    var node = o?["c" + c];
                    arr[c] = S(node);
                    if (node is JsonValue nv && !nv.TryGetValue<string>(out _) && nv.TryGetValue<double>(out _)) d.NumCells.Add(AnAnData.CellKey(r, c));
                }
                d.Rows.Add(arr);
            }
            // giống main.js m1StripTpcRows: bỏ "TPC-" còn sót ở Mã hàng
            var mi = d.Headers.FindIndex(h => JsText.Fuzzy(h) == "ma hang");
            if (mi >= 0) foreach (var r in d.Rows) if (System.Text.RegularExpressions.Regex.IsMatch(r[mi], @"^\s*TPC-", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) r[mi] = System.Text.RegularExpressions.Regex.Replace(r[mi], @"^\s*TPC-", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        LoadCustom(src, d);
        LoadDacTinh(src, d);
        LoadNgss(src, d);
        d.Invalidate();
        return d;
    }

    public static void LoadCustom(ISheetSource src, AnAnData d)
    {
        var raw = new Dictionary<string, List<JsonObject>>();
        if (ReadJsonCell(src, SCustom) is JsonObject o)
            foreach (var id in AnAnData.FilterOrder)
                raw[id] = (o[id] as JsonArray ?? new()).OfType<JsonObject>().Select(x => (JsonObject)x.DeepClone()).ToList();
        foreach (var id in AnAnData.FilterOrder) raw.TryAdd(id, new());
        d.SetCustom(raw);
    }

    public static void LoadDacTinh(ISheetSource src, AnAnData d)
    {
        d.DtKeys.Clear(); d.DtLabel.Clear(); d.DtGroup.Clear();
        foreach (var r in ReadArr(src, SDacTinh).OfType<JsonObject>())
        {
            var label = JsText.JsTrim(S(r["Đặc Tính"])); if (label.Length == 0) continue;
            var key = AnAnData.LabelKey(label);
            if (!d.DtLabel.ContainsKey(key)) d.DtKeys.Add(key);
            d.DtLabel[key] = label;
            var g = JsText.JsTrim(S(r["Nhóm đã gộp"]));
            if (g.Length > 0) d.DtGroup[key] = g; else d.DtGroup.Remove(key);
        }
    }

    public static void LoadNgss(ISheetSource src, AnAnData d)
    {
        foreach (var r in ReadArr(src, SNgss).OfType<JsonObject>())
        {
            var label = JsText.JsTrim(S(r["Nhãn"])); if (label.Length == 0) continue;
            var raw = r["Giá trị (kg)"];
            double? num = null;
            if (raw is JsonValue v)
            {
                if (v.TryGetValue<double>(out var x)) num = x;
                else if (v.TryGetValue<string>(out var s) && s.Length > 0) { var n = JsText.JsNumber(s); num = double.IsNaN(n) ? null : n; }
                else if (v.TryGetValue<bool>(out var b)) num = b ? 1 : 0;
            }
            d.ManualValues[AnAnData.LabelKey(label)] = num;
        }
    }

    // ------------------------------------------------------------------ ghi

    public static JsonArray LoadDataRows(AnAnData d)
    {
        var n = d.Headers.Count; var arr = new JsonArray();
        for (var r = 0; r < d.Rows.Count; r++)
        {
            var o = new JsonObject();
            for (var c = 0; c < n; c++)
            {
                var v = c < d.Rows[r].Length ? d.Rows[r][c] ?? "" : "";
                if (d.NumCells.Contains(AnAnData.CellKey(r, c)) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)) o["c" + c] = num;
                else o["c" + c] = v;
            }
            arr.Add(o);
        }
        return arr;
    }

    public static JsonObject MetaJson(AnAnData d)
    {
        var ma = new JsonObject();
        foreach (var (k, v) in d.ManualAssign.OrderBy(x => x.Key)) ma[k.ToString(CultureInfo.InvariantCulture)] = v;
        return new JsonObject
        {
            ["headers"] = new JsonArray(d.Headers.Select(h => (JsonNode)h).ToArray()),
            ["sheets"] = new JsonArray(d.Sheets.Select(h => (JsonNode)h).ToArray()),
            ["currentSheet"] = d.CurrentSheet,
            ["fileName"] = d.FileName,
            ["importedAt"] = d.ImportedAt is double t ? JsonValue.Create((long)t) : null,
            ["dataDate"] = d.DataDate,
            ["napId"] = d.NapId,
            ["headerRowInfo"] = d.HeaderRowInfo?.DeepClone(),
            ["manualAssign"] = ma,
        };
    }

    /// <summary>Ghi Load Data + meta cùng lúc (như writeSheets của app cũ).</summary>
    public static Dictionary<string, JsonArray?> LoadDataSheets(AnAnData d) => new()
    {
        [SMeta] = JsonCell(MetaJson(d)),
        [SLoadData] = LoadDataRows(d),
    };

    public static JsonArray CustomSheet(AnAnData d)
    {
        var o = new JsonObject();
        foreach (var id in AnAnData.FilterOrder) o[id] = new JsonArray((d.CustomRaw.GetValueOrDefault(id) ?? new()).Select(x => (JsonNode)x.DeepClone()).ToArray());
        return JsonCell(o);
    }

    public static JsonArray DacTinhSheet(AnAnData d) => new(d.DtKeys.Select(k => (JsonNode)new JsonObject
    {
        ["Đặc Tính"] = d.DtLabel[k], ["Nhóm đã gộp"] = d.DtGroup.GetValueOrDefault(k) ?? "",
    }).ToArray());

    public static readonly string[] NgssLabels = { AnAnData.ManualLabel, AnAnData.KhoGuiLabel };

    public static JsonArray NgssSheet(AnAnData d) => new(NgssLabels.Select(l =>
    {
        var v = d.ManualValues.GetValueOrDefault(AnAnData.LabelKey(l));
        return (JsonNode)new JsonObject { ["Nhãn"] = l, ["Giá trị (kg)"] = v is double x ? JsonValue.Create(x) : JsonValue.Create("") };
    }).ToArray());
}
