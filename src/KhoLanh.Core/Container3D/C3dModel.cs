using System.Globalization;
using System.Text.Json.Nodes;

namespace KhoLanh.Core.Container3D;

// MODULE "CONTAINER LOADING 3D" (M14) — mô hình dữ liệu. Toạ độ (mm): x = dọc cont, 0 ở VÁCH ĐẦU (phía máy lạnh) → L ở CỬA;
// y = ngang cont (0 = vách PHẢI khi đứng ở cửa nhìn vào); z = cao (0 = sàn). Định dạng JSON giữ y như app cũ (dùng chung dữ liệu).

public sealed class C3dContainer
{
    public string Id = "", Name = "", Type = "", Note = "";
    public double L, W, H, DoorW, DoorH, MaxPayload, RedLine, ClearFront, ClearRear, ClearSide;

    public C3dContainer Clone() => (C3dContainer)MemberwiseClone();

    public JsonObject ToJson() => new()
    {
        ["id"] = Id, ["name"] = Name, ["type"] = Type, ["L"] = L, ["W"] = W, ["H"] = H, ["doorW"] = DoorW, ["doorH"] = DoorH,
        ["maxPayload"] = MaxPayload, ["redLine"] = RedLine, ["clearFront"] = ClearFront, ["clearRear"] = ClearRear, ["clearSide"] = ClearSide, ["note"] = Note,
    };

    public static C3dContainer FromJson(JsonNode? n)
    {
        var o = n as JsonObject ?? new JsonObject();
        return new C3dContainer
        {
            Id = C3dJson.S(o["id"]), Name = C3dJson.S(o["name"]), Type = C3dJson.S(o["type"]), Note = C3dJson.S(o["note"]),
            L = C3dJson.N(o["L"]), W = C3dJson.N(o["W"]), H = C3dJson.N(o["H"]), DoorW = C3dJson.N(o["doorW"]), DoorH = C3dJson.N(o["doorH"]),
            MaxPayload = C3dJson.N(o["maxPayload"]), RedLine = C3dJson.N(o["redLine"]), ClearFront = C3dJson.N(o["clearFront"]),
            ClearRear = C3dJson.N(o["clearRear"]), ClearSide = C3dJson.N(o["clearSide"]),
        };
    }

    /// <summary>Chuẩn hoá: cửa trống = kích thước trong, khe âm = 0.</summary>
    public C3dContainer Normalized()
    {
        var c = Clone();
        if (!(c.DoorW > 0)) c.DoorW = c.W;
        if (!(c.DoorH > 0)) c.DoorH = c.H;
        c.ClearFront = Math.Max(0, c.ClearFront); c.ClearRear = Math.Max(0, c.ClearRear); c.ClearSide = Math.Max(0, c.ClearSide);
        return c;
    }
}

public sealed class C3dSku
{
    public string Id = "", Code = "", Name = "", Size = "", Date = "", Orient = "", RowPlan = "", Note = "", Color = "";
    public int Qty, MaxLayers;
    public double L, W, H, Kg;
    public bool AllowRotate = true, AllowStand = true, AllowLay, Sole;
    /// <summary>Chỉnh riêng từng dãy của mặt hàng: thứ tự dãy (từ 0) → "nam" / "dung" / "cao" / hướng (LWH…) / dãy mẫu "6 nằm 2 đứng".</summary>
    public Dictionary<string, string> RowSet = new();

    // dùng trong lúc tính
    internal int Idx;
    internal bool Auto, Drop;
    internal int[]? TierSeq;

    public C3dSku Clone() { var k = (C3dSku)MemberwiseClone(); k.RowSet = new(RowSet); k.TierSeq = null; return k; }

    public JsonObject ToJson()
    {
        var rs = new JsonObject(); foreach (var (k, v) in RowSet) rs[k] = v;
        return new JsonObject
        {
            ["id"] = Id, ["code"] = Code, ["name"] = Name, ["size"] = Size, ["date"] = Date, ["qty"] = Qty, ["L"] = L, ["W"] = W, ["H"] = H, ["kg"] = Kg,
            ["allowRotate"] = AllowRotate, ["allowStand"] = AllowStand, ["allowLay"] = AllowLay, ["sole"] = Sole, ["maxLayers"] = MaxLayers,
            ["orient"] = Orient, ["rowPlan"] = RowPlan, ["rowSet"] = rs, ["note"] = Note, ["color"] = Color,
        };
    }

    public static C3dSku FromJson(JsonNode? n, int i = 0)
    {
        var o = n as JsonObject ?? new JsonObject();
        var k = new C3dSku
        {
            Id = C3dJson.S(o["id"]) is { Length: > 0 } id ? id : "s" + i,
            Code = C3dJson.S(o["code"]).Trim(), Name = C3dJson.S(o["name"]).Trim(), Size = C3dJson.S(o["size"]).Trim(), Date = C3dJson.S(o["date"]).Trim(),
            Qty = (int)Math.Max(0, Math.Floor(C3dJson.N(o["qty"]))), L = C3dJson.N(o["L"]), W = C3dJson.N(o["W"]), H = C3dJson.N(o["H"]), Kg = Math.Max(0, C3dJson.N(o["kg"])),
            AllowRotate = C3dJson.Flag(o["allowRotate"], true), AllowStand = C3dJson.Flag(o["allowStand"], true), AllowLay = C3dJson.Flag(o["allowLay"], false),
            Sole = C3dJson.Flag(o["sole"], false), MaxLayers = (int)Math.Max(0, Math.Floor(C3dJson.N(o["maxLayers"]))),
            Orient = C3dJson.S(o["orient"]), RowPlan = C3dJson.S(o["rowPlan"]).Trim(), Note = C3dJson.S(o["note"]), Color = C3dJson.S(o["color"]),
        };
        if (o["rowSet"] is JsonObject rs) foreach (var (key, v) in rs) { var s = C3dJson.S(v); if (s.Length > 0) k.RowSet[key] = s; }
        return k;
    }
}

public sealed class C3dOptions
{
    public bool MergeTail = true, StackTop = true, HeightRule = true, SampleOn = true, AutoLayout = true, Spread, Compare = true, Hints = true;
    public int SampleQty = 10;
    public double SpreadPct = 70, MinSupport = 0.7;
    public string DateDir = "asc", SortMode = "auto";

    // nội bộ (các lần tính thử)
    internal bool Auto, Airflow, Drop;
    internal Dictionary<string, int[]>? TierSeqs;
    internal int? TierCap;

    public C3dOptions Clone() { var o = (C3dOptions)MemberwiseClone(); return o; }

    public JsonObject ToJson() => new()
    {
        ["mergeTail"] = MergeTail, ["stackTop"] = StackTop, ["heightRule"] = HeightRule, ["sampleOn"] = SampleOn, ["sampleQty"] = SampleQty,
        ["autoLayout"] = AutoLayout, ["spread"] = Spread, ["spreadPct"] = SpreadPct, ["dateDir"] = DateDir, ["sortMode"] = SortMode,
    };

    public static C3dOptions FromJson(JsonNode? n)
    {
        var o = n as JsonObject ?? new JsonObject();
        return new C3dOptions
        {
            MergeTail = C3dJson.Flag(o["mergeTail"], true), StackTop = C3dJson.Flag(o["stackTop"], true), HeightRule = C3dJson.Flag(o["heightRule"], true),
            SampleOn = C3dJson.Flag(o["sampleOn"], true), AutoLayout = C3dJson.Flag(o["autoLayout"], true), Spread = C3dJson.Flag(o["spread"], false),
            SpreadPct = C3dJson.N(o["spreadPct"]) is var sp && sp > 0 ? sp : 70, DateDir = C3dJson.S(o["dateDir"]) == "desc" ? "desc" : "asc",
            SortMode = C3dJson.S(o["sortMode"]) == "manual" ? "manual" : "auto",
        };
    }
}

/// <summary>Vùng chứa hàng (đã trừ khe) + chiều cao xếp tối đa.</summary>
public sealed record C3dSpace(double X0, double X1, double Y0, double Y1, double Lu, double Wu, double EffH, double Red, double L, double W, double H);

/// <summary>Hướng đặt: dx dọc cont, dy ngang cont, dz cao. Kind "dung" (đặt đúng chiều cao gốc) / "nam".</summary>
public sealed record C3dOrient(double Dx, double Dy, double Dz, string Kind, int Rot, string Perm = "");

public sealed class C3dBox
{
    public double X, Y, Z, Dx, Dy, Dz;
    public int Tier, Rot, Group, Seq, Row;
    public string Ori = "", SkuId = "";
    public bool Sole, OnPrev, Sample;
    public int? Wall;
}

public sealed record C3dPatNote(string Text, string Kind, C3dOrient O);

public sealed class C3dGroup
{
    public C3dSku Sku = null!;
    public int Index, Requested, Loaded, Left, Sample, StripUsed, StackedOn;
    public List<string> Reasons = new();
    public List<C3dPatNote> Patterns = new();
    public double? XStart, XEnd;
    public double Kg, VolUnit, VolReq, VolLoaded;
    public bool Ok;
    public string SoleFail = "";
}

public sealed class C3dRow
{
    public int Row, N, Samples, Group = -1, Wall = -1;
    public double X0, X1, Y0 = 1e18, Y1 = -1e18, Top, HMin = 1e18;
    public bool Sample;
    public C3dGaps Gaps = new();
    internal HashSet<int> Gs = new();
    internal HashSet<int> Ws = new();
}

public sealed class C3dGaps { public double Front, Door, Right, Left, Top, Ceiling, Red, Eff; }

public sealed record C3dViolation(string Type, int Box, string Msg);
public sealed record C3dVerify(bool Ok, List<C3dViolation> Violations);
public sealed record C3dHeightViol(int Row, int Next, double Top, double NextTop);
public sealed record C3dCog(double X, double Y, double XPct, double YPct);

public sealed class C3dTotals
{
    public double VolReqAll, VolFreeInternal, VolFreeUsable, Kg, Payload, VolBox, VolUsable, VolInternal, FillUsablePct, FillInternalPct, UsedLength, FreeLength, EffH;
    public int Rows, Sample, Requested, Loaded, Left;
    public C3dGaps? Gaps;
    public C3dCog? Cog;
}

public sealed record C3dCompare(bool MergeTail, int Loaded, double Kg, bool Ok, double UsedLength, int Delta);

/// <summary>Gợi ý đóng thêm. Áp dụng: các mặt hàng SkuIds được bỏ chốt hướng + cho đặt nằm / xoay; GapZero = khe đầu / cửa về 0; Opt* = đổi tuỳ chọn.</summary>
public sealed record C3dHint(string Id, int Gain, int Loaded, int Left, string Text, string Note, List<string> SkuIds, bool GapZero, bool? OptSpread, bool? OptHeightRule);

public sealed class C3dPlan
{
    public bool Ok;
    public List<string> Errors = new(), Warnings = new();
    public C3dContainer Container = null!;
    public C3dSpace Space = null!;
    public List<C3dGroup> Groups = new();
    public List<C3dBox> Boxes = new();
    public List<C3dSku> Skus = new();
    public List<C3dRow> Rows = new();
    public List<C3dHeightViol> HeightViol = new();
    public C3dVerify Verify = new(true, new());
    public int MaxTier;
    public C3dTotals Totals = new();
    public C3dOptions Options = new();
    public string LayoutNote = "", SpreadNote = "";
    public int SpreadTier;
    public C3dCompare? Compare;
    public List<C3dHint> Hints = new();
}

public static class C3dJson
{
    public static string S(JsonNode? n)
    {
        if (n is null) return "";
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out var s)) return s;
            if (v.TryGetValue<double>(out var d)) return d.ToString(CultureInfo.InvariantCulture);
            if (v.TryGetValue<bool>(out var b)) return b ? "true" : "false";
        }
        return n.ToJsonString();
    }

    /// <summary>Số kiểu app cũ (parseFloat, chấp nhận dấu phẩy thập phân). Không đọc được → d.</summary>
    public static double N(JsonNode? n, double d = 0)
    {
        if (n is JsonValue v && v.TryGetValue<double>(out var x)) return double.IsFinite(x) ? x : d;
        return Num(S(n), d);
    }

    public static double Num(string? s, double d = 0)
    {
        var t = (s ?? "").Trim();
        var i = t.IndexOf(','); if (i >= 0) t = t[..i] + "." + t[(i + 1)..];
        // parseFloat: lấy phần số dài nhất ở đầu chuỗi
        var m = System.Text.RegularExpressions.Regex.Match(t, @"^[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?");
        return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var r) && double.IsFinite(r) ? r : d;
    }

    public static bool Flag(JsonNode? n, bool d)
    {
        if (n is null) return d;
        if (n is JsonValue v && v.TryGetValue<bool>(out var b)) return b;
        var s = S(n);
        if (s.Length == 0) return d;
        return s is "1" or "true";
    }
}
