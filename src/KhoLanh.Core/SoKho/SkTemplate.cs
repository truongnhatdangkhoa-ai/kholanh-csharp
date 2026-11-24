using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace KhoLanh.Core.SoKho;

/// <summary>1 cột của mẫu: cột dựng sẵn (đổi tên / ẩn / sắp lại) hoặc cột phụ khóa "x_…" (ghi vào phần thêm của dòng sổ).</summary>
public sealed class SkCol
{
    public string Key = "", Label = "", Type = "text"; public bool Hidden, Builtin, Required; public double W = 120;
    /// <summary>Công thức của cột số (giữ nguyên JSON của app cũ, VD Tổng (kg) = Số lượng × Net).</summary>
    public JsonNode? Formula;
    public SkCol Clone() => new() { Key = Key, Label = Label, Type = Type, Hidden = Hidden, Builtin = Builtin, Required = Required, W = W, Formula = Formula?.DeepClone() };
}

/// <summary>Mẫu cột của sổ (sheet SoKhoMau — chép defaultTemplate / normTemplate / templateFor của so-kho-core.js).</summary>
public sealed class SkTemplate
{
    public string Id = "", Ten = "Mẫu", Loai = ""; public bool MacDinh;
    public List<SkCol> Columns = new(); public List<string> Removed = new();

    public static readonly SkCol[] Builtin =
    {
        new() { Key = "viTri", Label = "Vị trí", W = 100 }, new() { Key = "maHang", Label = "Mã hàng", Required = true, W = 190 }, new() { Key = "size", Label = "Size", W = 90 },
        new() { Key = "soLuong", Label = "Số lượng", Type = "number", W = 100 }, new() { Key = "netDv", Label = "Net (kg/đơn vị)", Type = "number", W = 120 },
        new() { Key = "net", Label = "Tổng (kg)", Type = "number", W = 110, Formula = JsonNode.Parse("[{\"c\":\"soLuong\"},\"*\",{\"c\":\"netDv\"}]") },
        new() { Key = "hopDong", Label = "Hợp đồng", W = 130 }, new() { Key = "hanDung", Label = "Hạn dùng", Type = "date", W = 110 },
        new() { Key = "ngayNhap", Label = "Ngày nhập", Type = "date", W = 110 }, new() { Key = "ghiChu", Label = "Ghi chú", W = 220 },
    };
    public static readonly string[] Loais = { "nhap", "xuat", "chuyen", "dieuchinh", "tondau" };
    private static bool IsB(string k) => Builtin.Any(b => b.Key == k);

    public static SkTemplate Default() => new()
    {
        Id = "mau-co-ban", Ten = "Mẫu cơ bản", MacDinh = true,
        Columns = Builtin.Select(c => { var o = c.Clone(); o.Builtin = true; if (o.Key == "soLuong") o.Label = "Số lượng (bao)"; return o; }).ToList(),
    };

    private static string S(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : n is null ? "" : n.ToString().Trim();
    private static double Num(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : double.TryParse(S(n), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var x) ? x : 0;

    /// <summary>Chuẩn hóa: bỏ khóa lạ, thêm lại cột dựng sẵn bị thiếu (ẩn), Mã hàng luôn hiện, cột đã xóa không tự thêm lại.</summary>
    public static SkTemplate Norm(string id, string ten, string loai, bool macDinh, IEnumerable<JsonObject> cols0)
    {
        var src = cols0.ToList(); var rem = new HashSet<string>(); var seen = new HashSet<string>(); var cols = new List<SkCol>();
        foreach (var c in src) if (c["removed"]?.GetValue<bool>() == true) { var k = S(c["key"]); if (IsB(k) && k != "maHang") rem.Add(k); }
        src = src.Where(c => c["removed"]?.GetValue<bool>() != true).ToList();
        if (!rem.Contains("netDv") && !src.Any(c => S(c["key"]) == "netDv"))
        {
            var qi = src.FindIndex(c => S(c["key"]) == "soLuong");
            src.Insert(qi >= 0 ? qi + 1 : src.Count, new JsonObject { ["key"] = "netDv", ["label"] = "Net (kg/đơn vị)", ["hidden"] = false, ["w"] = 120 });
        }
        foreach (var c in src)
        {
            var key = S(c["key"]); if (key.Length == 0 || seen.Contains(key)) continue;
            var b = Builtin.FirstOrDefault(x => x.Key == key);
            if (b is null && !Regex.IsMatch(key, "^x_[A-Za-z0-9_]+$")) continue;
            seen.Add(key); rem.Remove(key);
            var t0 = S(c["type"]); var type = b?.Type ?? (t0 is "text" or "number" or "date" ? t0 : "text");
            var lb = S(c["label"]);
            if (key == "net" && (lb == "Net (kg)" || lb.Length == 0)) lb = "Tổng (kg)";
            if (lb.Length == 0) lb = b?.Label ?? key[2..];
            var w = Num(c["w"]);
            cols.Add(new SkCol { Key = key, Label = lb, Type = type, Required = key == "maHang", Hidden = key != "maHang" && c["hidden"]?.GetValue<bool>() == true, W = w > 40 ? w : b?.W ?? 120, Builtin = b is not null,
                Formula = type != "number" ? null : c.ContainsKey("formula") ? c["formula"]?.DeepClone() : b?.Formula?.DeepClone() });
        }
        foreach (var b in Builtin) if (!seen.Contains(b.Key) && !rem.Contains(b.Key)) { var o = b.Clone(); o.Builtin = true; o.Hidden = true; cols.Add(o); }
        return new SkTemplate { Id = id.Length > 0 ? id : "mau-" + Guid.NewGuid().ToString("N")[..8], Ten = ten.Length > 0 ? ten : "Mẫu", Loai = Loais.Contains(loai) ? loai : "", MacDinh = macDinh, Removed = rem.ToList(), Columns = cols };
    }

    public static SkTemplate FromStore(JsonObject r)
    {
        var cols = new List<JsonObject>();
        try { if (JsonNode.Parse(S(r["columnsJson"]) is { Length: > 0 } j ? j : "[]") is JsonArray a) cols = a.OfType<JsonObject>().ToList(); } catch { }
        var md = r["macDinh"]; var mdOn = md is JsonValue mv && (mv.TryGetValue<bool>(out var bb) ? bb : S(md) == "1");
        return Norm(S(r["id"]), S(r["ten"]), S(r["loai"]), mdOn, cols);
    }

    public JsonObject ToStore()
    {
        var arr = new JsonArray();
        foreach (var c in Columns)
        {
            var o = new JsonObject { ["key"] = c.Key, ["label"] = c.Label, ["type"] = c.Type, ["required"] = c.Required, ["hidden"] = c.Hidden, ["w"] = c.W, ["builtin"] = c.Builtin };
            if (c.Type == "number") o["formula"] = c.Formula?.DeepClone();
            arr.Add(o);
        }
        foreach (var k in Removed) arr.Add(new JsonObject { ["key"] = k, ["removed"] = true });
        return new JsonObject { ["id"] = Id, ["ten"] = Ten, ["loai"] = Loai, ["macDinh"] = MacDinh ? "1" : "", ["columnsJson"] = arr.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) };
    }

    /// <summary>Mẫu dùng cho 1 loại phiếu: mẫu riêng của loại → mẫu mặc định → mẫu đầu → mẫu cơ bản.</summary>
    public static SkTemplate For(IReadOnlyList<SkTemplate> list, string loai) =>
        list.FirstOrDefault(t => t.Loai == loai) ?? list.FirstOrDefault(t => t.MacDinh) ?? list.FirstOrDefault() ?? Default();

    /// <summary>
    /// Tính công thức cột (evalFormula của so-kho-core.js): [toán hạng, phép, toán hạng, …], toán hạng {c: khóa} | {n: số}, phép + − × ÷;
    /// × ÷ trước, + − sau; ô trống: × ÷ → trống, + − coi là 0 (trừ khi mọi ô trống); chia 0 → trống; làm tròn 4 số lẻ.
    /// </summary>
    public static double? Eval(JsonNode? formula, Func<string, double?> get)
    {
        if (formula is not JsonArray f || f.Count < 3 || f.Count % 2 == 0) return null;
        var vals = new List<double?>(); var ops = new List<string>();
        for (var i = 0; i < f.Count; i++)
        {
            if (i % 2 == 1) { ops.Add(S(f[i])); continue; }
            double? v = null;
            if (f[i] is JsonObject t)
            {
                if (t["c"] is { } c) { var x = get(S(c)); v = x is { } d && double.IsFinite(d) ? d : null; }
                else if (t["n"] is JsonValue n && n.TryGetValue<double>(out var nd) && double.IsFinite(nd)) v = nd;
            }
            vals.Add(v);
        }
        var terms = new List<double?> { vals[0] }; var tops = new List<string>();
        for (var i = 0; i < ops.Count; i++)
        {
            var o = ops[i]; var b = vals[i + 1];
            if (o is "*" or "/")
            {
                var a = terms[^1];
                if (a is null || b is null) terms[^1] = null;
                else if (o == "*") terms[^1] = a * b;
                else { if (Math.Abs(b.Value) < 1e-12) return null; terms[^1] = a / b; }
            }
            else { tops.Add(o); terms.Add(b); }
        }
        if (terms.All(x => x is null)) return null;
        var acc = terms[0] ?? 0;
        for (var i = 0; i < tops.Count; i++) { var x = terms[i + 1] ?? 0; acc = tops[i] == "+" ? acc + x : acc - x; }
        return double.IsFinite(acc) ? Math.Round(acc * 10000, MidpointRounding.AwayFromZero) / 10000 : null;
    }

    /// <summary>Khóa cột phụ mới từ tên: "Số xe" → "x_so_xe" (không trùng).</summary>
    public static string NewExtraKey(string label, IEnumerable<string> used)
    {
        var b = Regex.Replace(Regex.Replace(KhoLanh.Core.TimNhanh.Gs.Norm(label), "[^a-z0-9]+", "_").Trim('_'), "_+", "_");
        if (b.Length == 0) b = "cot"; var k = "x_" + b; var u = used.ToHashSet(); var i = 2;
        while (u.Contains(k)) k = $"x_{b}_{i++}";
        return k;
    }
}
