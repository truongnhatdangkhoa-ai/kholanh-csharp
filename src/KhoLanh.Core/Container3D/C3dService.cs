using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;
using KhoLanh.Core.TonKho;

namespace KhoLanh.Core.Container3D;

/// <summary>
/// Container 3D — lưu trữ (database "container-3d", y định dạng app cũ):
///  C3D_Container: cont người dùng sửa / thêm (sửa mẫu = 1 dòng "ghi đè"; xóa = về mẫu gốc) ·
///  C3D_Plan: phương án đã lưu (cont + hàng + tuỳ chọn; kết quả TÍNH LẠI khi mở) · C3D_State: trạng thái làm việc lần trước · C3D_Thung: danh mục loại thùng.
/// </summary>
public sealed class C3dService
{
    public const string PermKey = "m14", Db = "container-3d", SCont = "C3D_Container", SPlan = "C3D_Plan", SState = "C3D_State", SCat = "C3D_Thung";
    public static readonly string[] Palette = { "#3b82f6", "#f97316", "#22c55e", "#e11d48", "#a855f7", "#eab308", "#06b6d4", "#84cc16", "#ec4899", "#14b8a6", "#f43f5e", "#8b5cf6" };
    public static readonly (string id, string name)[] Types = { ("20RF", "20' RF"), ("40RF", "40' RF"), ("40HC", "40' HC RF"), ("KHAC", "Khác") };
    private static readonly JsonSerializerOptions Opt = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;

    public C3dService(Func<ISheetSource> source, Session session) { _src = source; _session = session; }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);

    // ------------------------------------------------------------ trạng thái làm việc
    public sealed class State
    {
        public string Unit = "mm", Cid = "rf40hc";
        public C3dContainer? Cont;
        public List<C3dSku> Skus = new();
        public Dictionary<string, string> Colors = new();
        public C3dOptions Opts = new();
        public JsonNode? AutoMiss;
    }

    public sealed record SavedCont(string Id, C3dContainer V);
    public sealed class Plan
    {
        public string Id = "", Name = "", CapNhat = "", ContName = "";
        public int Loaded, Requested;
        public JsonObject Data = new();
        public bool IsAuto => Data["auto"] is JsonObject;
        public bool Locked => Data["auto"] is JsonObject a && C3dJson.Flag(a["lock"], false);
    }
    public sealed class CatItem { public string Id = "", Name = "", CapNhat = ""; public C3dSku V = new(); }

    public State St { get; private set; } = new();
    public List<SavedCont> Saved { get; private set; } = new();
    public List<Plan> Plans { get; private set; } = new();
    public List<CatItem> Cat { get; private set; } = new();

    private JsonArray Read(string sheet) { try { return JsonNode.Parse(_src().Read(Db, sheet)) as JsonArray ?? new(); } catch { return new(); } }
    private void Write(string sheet, JsonArray rows) => _src().Write(Db, new Dictionary<string, JsonArray?> { [sheet] = rows });
    private static JsonNode? Unpack(JsonNode? row)
    {
        var t = AnAnStore.S((row as JsonObject)?["data"]);
        if (t.StartsWith(AnAnStore.JsonTag, StringComparison.Ordinal)) t = t[AnAnStore.JsonTag.Length..];
        try { return JsonNode.Parse(t); } catch { return null; }
    }
    private static string Pack(JsonNode n) => AnAnStore.JsonTag + n.ToJsonString(Opt);
    private static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    private static string NewId(string prefix) => prefix + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x") + Random.Shared.Next(0, 1296).ToString("x");

    public void Load()
    {
        Cat = Read(SCat).Select(x => { var v = Unpack(x); var o = x as JsonObject; return v is null ? null : new CatItem { Id = AnAnStore.S(o?["id"]), Name = AnAnStore.S(o?["name"]), CapNhat = AnAnStore.S(o?["capNhat"]), V = C3dSku.FromJson(v) }; })
            .Where(x => x is not null).Cast<CatItem>().ToList();
        Saved = Read(SCont).Select(x => { var v = Unpack(x); var o = x as JsonObject; if (v is null) return null; var c = C3dContainer.FromJson(v); var id = AnAnStore.S(o?["id"]); if (id.Length == 0) id = c.Id; return id.Length == 0 ? null : new SavedCont(id, c); })
            .Where(x => x is not null).Cast<SavedCont>().ToList();
        Plans = Read(SPlan).Select(x =>
        {
            if (Unpack(x) is not JsonObject d || x is not JsonObject o) return null;
            return new Plan { Id = AnAnStore.S(o["id"]), Name = AnAnStore.S(o["name"]), CapNhat = AnAnStore.S(o["capNhat"]), ContName = AnAnStore.S(o["contName"]), Loaded = (int)C3dJson.N(o["loaded"]), Requested = (int)C3dJson.N(o["requested"]), Data = d };
        }).Where(x => x is not null).Cast<Plan>().ToList();
        var st = new State();
        var rows = Read(SState);
        if (rows.Count > 0 && Unpack(rows[0]) is JsonObject s)
        {
            if (AnAnStore.S(s["unit"]) is "mm" or "cm") st.Unit = AnAnStore.S(s["unit"]);
            if (AnAnStore.S(s["cid"]) is { Length: > 0 } cid) st.Cid = cid;
            if (s["cont"] is JsonObject co) st.Cont = C3dContainer.FromJson(co);
            if (s["skus"] is JsonArray ks) st.Skus = ks.Select((k, i) => C3dSku.FromJson(k, i)).ToList();
            if (s["colors"] is JsonObject cl) foreach (var (k, v) in cl) st.Colors[k] = AnAnStore.S(v);
            if (s["opts"] is JsonObject op) st.Opts = C3dOptions.FromJson(op);
            st.AutoMiss = s["autoMiss"]?.DeepClone();
        }
        St = st;
        if (St.Cont is null || ContList().All(x => x.Id != St.Cid)) { if (St.Cont is null || !(St.Cont.L > 0)) PickContainer(ContList().Any(x => x.Id == St.Cid) ? St.Cid : "rf40hc"); else if (ContList().All(x => x.Id != St.Cid)) St.Cid = "rf40hc"; }
        if (!(St.Cont!.L > 0)) PickContainer("rf40hc");
    }

    public void SaveState()
    {
        if (!CanEdit) return;
        var colors = new JsonObject(); foreach (var (k, v) in St.Colors) colors[k] = v;
        var o = new JsonObject
        {
            ["unit"] = St.Unit, ["cid"] = St.Cid, ["cont"] = St.Cont?.ToJson(), ["skus"] = new JsonArray(St.Skus.Select(k => (JsonNode)k.ToJson()).ToArray()),
            ["colors"] = colors, ["opts"] = St.Opts.ToJson(), ["autoMiss"] = St.AutoMiss?.DeepClone() ?? new JsonObject(),
        };
        Write(SState, new JsonArray(new JsonObject { ["id"] = "state", ["capNhat"] = Now(), ["data"] = Pack(o) }));
    }

    // ------------------------------------------------------------ container
    public sealed record ContEntry(string Id, C3dContainer C, bool Builtin, bool Edited);

    public List<ContEntry> ContList()
    {
        var ov = Saved.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.Last().V);
        var outp = C3dEngine.Presets.Select(p => { var e = ov.TryGetValue(p.Id, out var o); var c = (e ? o! : p).Clone(); c.Id = p.Id; return new ContEntry(p.Id, c, true, e); }).ToList();
        foreach (var c in Saved.Where(c => C3dEngine.Presets.All(p => p.Id != c.Id))) { var x = c.V.Clone(); x.Id = c.Id; outp.Add(new ContEntry(c.Id, x, false, false)); }
        return outp;
    }

    public void PickContainer(string id)
    {
        var l = ContList(); var c = l.FirstOrDefault(x => x.Id == id) ?? l[2];
        St.Cid = c.Id; St.Cont = c.C.Clone();
    }

    private void PersistContainers() =>
        Write(SCont, new JsonArray(Saved.Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["name"] = c.V.Name, ["builtin"] = C3dEngine.Presets.Any(p => p.Id == c.Id) ? "1" : "", ["data"] = Pack(c.V.ToJson()) }).ToArray()));

    /// <summary>Lưu container đang sửa (asNew = thành container mới). Trả về thông báo.</summary>
    public string SaveContainer(bool asNew)
    {
        if (!CanEdit) throw new BusinessException("Tài khoản chỉ xem — không lưu được container.");
        var c = St.Cont!.Clone(); if (c.Name.Trim().Length == 0) throw new BusinessException("Nhập tên container trước khi lưu.");
        var id = St.Cid; var cur = ContList().FirstOrDefault(x => x.Id == St.Cid);
        if (asNew || cur is null) { id = NewId("cx"); if (ContList().Any(x => x.C.Name == c.Name)) c.Name += " (bản sao)"; }
        c.Id = id;
        var keep = Saved.ToList(); var i = Saved.FindIndex(x => x.Id == id);
        if (i >= 0) Saved[i] = new SavedCont(id, c); else Saved.Add(new SavedCont(id, c));
        try { PersistContainers(); } catch { Saved = keep; throw; }
        St.Cid = id; St.Cont = c.Clone(); SaveState();
        return asNew ? "Đã lưu thành container mới." : "Đã lưu thay đổi container.";
    }

    /// <summary>Xóa container tự thêm / khôi phục mẫu gốc đã sửa.</summary>
    public string DeleteContainer()
    {
        if (!CanEdit) throw new BusinessException("Tài khoản chỉ xem — không xóa được.");
        var cur = ContList().FirstOrDefault(x => x.Id == St.Cid) ?? throw new BusinessException("Chưa chọn container.");
        if (cur.Builtin && !cur.Edited) throw new BusinessException("Đây là thông số mẫu gốc, chưa sửa.");
        var keep = Saved.ToList(); Saved = Saved.Where(x => x.Id != St.Cid).ToList();
        try { PersistContainers(); } catch { Saved = keep; throw; }
        PickContainer(cur.Builtin ? cur.Id : "rf40hc"); SaveState();
        return cur.Builtin ? "Đã khôi phục mẫu gốc." : "Đã xóa container.";
    }

    // ------------------------------------------------------------ màu mặt hàng
    public string ColorFor(string? code)
    {
        var k = (code ?? "").Trim(); if (k.Length == 0) k = "(trống)";
        if (St.Colors.TryGetValue(k, out var c)) return c;
        var used = St.Colors.Values.ToHashSet(); var free = Palette.FirstOrDefault(p => !used.Contains(p));
        return St.Colors[k] = free ?? Palette[St.Colors.Count % Palette.Length];
    }

    /// <summary>Màu từng nhóm của phương án: cùng mã hàng cùng tông, các dòng (size / ngày) sáng tối khác nhau.</summary>
    public List<string> GroupColors(C3dPlan plan)
    {
        var seen = new Dictionary<string, int>(); double[] vr = { 0, 0.1, -0.1, 0.18, -0.16 };
        return plan.Groups.Select(g =>
        {
            var k = g.Sku.Code.Trim(); if (k.Length == 0) k = "(trống)";
            var n = seen.TryGetValue(k, out var x) ? x : 0; seen[k] = n + 1;
            return Shade(ColorFor(k), vr[n % vr.Length]);
        }).ToList();
    }

    public static string Shade(string hex, double d)
    {
        var h = hex.TrimStart('#'); if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var n)) return hex;
        double r = ((n >> 16) & 255) / 255.0, g = ((n >> 8) & 255) / 255.0, b = (n & 255) / 255.0;
        double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), hh = 0, s = 0, l = (mx + mn) / 2;
        if (mx != mn)
        {
            var dd = mx - mn; s = l > 0.5 ? dd / (2 - mx - mn) : dd / (mx + mn);
            hh = mx == r ? (g - b) / dd + (g < b ? 6 : 0) : mx == g ? (b - r) / dd + 2 : (r - g) / dd + 4; hh /= 6;
        }
        l = Math.Min(0.82, Math.Max(0.22, l + d));
        static double Q(double p, double q, double t) { if (t < 0) t += 1; if (t > 1) t -= 1; if (t < 1 / 6.0) return p + (q - p) * 6 * t; if (t < 0.5) return q; if (t < 2 / 3.0) return p + (q - p) * (2 / 3.0 - t) * 6; return p; }
        double R, G, B;
        if (s == 0) R = G = B = l; else { var qq = l < 0.5 ? l * (1 + s) : l + s - l * s; var p = 2 * l - qq; R = Q(p, qq, hh + 1 / 3.0); G = Q(p, qq, hh); B = Q(p, qq, hh - 1 / 3.0); }
        static string X(double v) => ((int)Math.Floor(v * 255 + 0.5)).ToString("x2");
        return "#" + X(R) + X(G) + X(B);
    }

    // ------------------------------------------------------------ phương án đã lưu
    private void PersistPlans() =>
        Write(SPlan, new JsonArray(Plans.Select(p => (JsonNode)new JsonObject { ["id"] = p.Id, ["name"] = p.Name, ["capNhat"] = p.CapNhat, ["contName"] = p.ContName, ["loaded"] = p.Loaded, ["requested"] = p.Requested, ["data"] = Pack(p.Data) }).ToArray()));

    private JsonObject CurrentData()
    {
        var colors = new JsonObject(); foreach (var (k, v) in St.Colors) colors[k] = v;
        return new JsonObject { ["cont"] = St.Cont!.ToJson(), ["cid"] = St.Cid, ["skus"] = new JsonArray(St.Skus.Select(k => (JsonNode)k.ToJson()).ToArray()), ["opts"] = St.Opts.ToJson(), ["colors"] = colors, ["unit"] = St.Unit };
    }

    /// <summary>Lưu phương án đang tính. overwriteId = ghi đè (và khóa) phương án tự động đang mở.</summary>
    public Plan SavePlan(string name, C3dPlan plan, string? overwriteId = null)
    {
        if (!CanEdit) throw new BusinessException("Tài khoản chỉ xem — không lưu được phương án.");
        if (plan.Totals.Requested <= 0) throw new BusinessException("Chưa có phương án để lưu.");
        var keep = Plans.ToList();
        Plan rec;
        var old = overwriteId is null ? null : Plans.FirstOrDefault(p => p.Id == overwriteId);
        if (old is not null)
        {
            var data = CurrentData();
            if (old.Data["auto"] is JsonObject a) { var na = (JsonObject)a.DeepClone(); na["lock"] = true; na["edited"] = true; na["warn"] = new JsonArray(); data["auto"] = na; }
            rec = new Plan { Id = old.Id, Name = name, CapNhat = Now(), ContName = St.Cont!.Name, Loaded = plan.Totals.Loaded, Requested = plan.Totals.Requested, Data = data };
            Plans[Plans.IndexOf(old)] = rec;
        }
        else
        {
            rec = new Plan { Id = NewId("pl"), Name = name, CapNhat = Now(), ContName = St.Cont!.Name, Loaded = plan.Totals.Loaded, Requested = plan.Totals.Requested, Data = CurrentData() };
            Plans.Insert(0, rec);
        }
        try { PersistPlans(); } catch { Plans = keep; throw; }
        return rec;
    }

    public void DeletePlan(string id)
    {
        if (!CanEdit) throw new BusinessException("Tài khoản chỉ xem — không xóa được.");
        var keep = Plans.ToList(); Plans = Plans.Where(p => p.Id != id).ToList();
        try { PersistPlans(); } catch { Plans = keep; throw; }
    }

    public void ToggleLock(string id)
    {
        if (!CanEdit) throw new BusinessException("Tài khoản chỉ xem.");
        var p = Plans.FirstOrDefault(x => x.Id == id); if (p?.Data["auto"] is not JsonObject a) return;
        var was = C3dJson.Flag(a["lock"], false); a["lock"] = !was;
        try { PersistPlans(); } catch { a["lock"] = was; throw; }
    }

    /// <summary>Mở phương án đã lưu vào trạng thái làm việc.</summary>
    public void OpenPlan(Plan p)
    {
        var d = p.Data;
        if (d["cont"] is JsonObject co) St.Cont = C3dContainer.FromJson(co);
        if (AnAnStore.S(d["cid"]) is { Length: > 0 } cid) St.Cid = cid;
        if (d["skus"] is JsonArray ks) St.Skus = ks.Select((k, i) => C3dSku.FromJson(k, i)).ToList();
        if (d["opts"] is JsonObject op) St.Opts = C3dOptions.FromJson(op);
        if (d["colors"] is JsonObject cl) foreach (var (k, v) in cl) St.Colors[k] = AnAnStore.S(v);
        if (AnAnStore.S(d["unit"]) is "mm" or "cm") St.Unit = AnAnStore.S(d["unit"]);
        SaveState();
    }

    // ------------------------------------------------------------ danh mục loại thùng
    public static string CatKey(C3dSku v) => v.Code.Trim().ToLowerInvariant() + "|" + v.Size.Trim().ToLowerInvariant();
    public static string CatLabel(C3dSku v) => (v.Code.Trim().Length > 0 ? v.Code.Trim() : "(chưa có mã)") + (v.Size.Length > 0 ? " · " + v.Size : "") + (v.Name.Length > 0 ? " · " + v.Name : "");

    private void PersistCat() =>
        Write(SCat, new JsonArray(Cat.Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["name"] = c.Name, ["capNhat"] = c.CapNhat, ["data"] = Pack(CatJson(c.V)) }).ToArray()));

    private static JsonObject CatJson(C3dSku k)
    {
        var o = k.ToJson();
        foreach (var f in new[] { "id", "qty", "date", "rowSet", "color" }) o.Remove(f);
        return o;
    }

    public string SaveToCat(C3dSku k)
    {
        if (!CanEdit) throw new BusinessException("Tài khoản chỉ xem — không lưu được danh mục.");
        if (k.Code.Trim().Length == 0) throw new BusinessException("Nhập Mã hàng trước khi lưu vào danh mục.");
        if (!(k.L > 0 && k.W > 0 && k.H > 0)) throw new BusinessException("Nhập đủ Dài / Rộng / Cao thùng trước.");
        var v = C3dSku.FromJson(CatJson(k)); var key = CatKey(v); var i = Cat.FindIndex(c => CatKey(c.V) == key);
        var rec = new CatItem { Id = i >= 0 ? Cat[i].Id : NewId("th"), Name = CatLabel(v), CapNhat = Now(), V = v };
        var keep = Cat.ToList(); if (i >= 0) Cat[i] = rec; else Cat.Add(rec);
        try { PersistCat(); } catch { Cat = keep; throw; }
        return (i >= 0 ? "Đã cập nhật" : "Đã lưu") + " loại thùng “" + rec.Name + "” vào danh mục (" + Math.Round(v.L) + "×" + Math.Round(v.W) + "×" + Math.Round(v.H) + " mm).";
    }

    public void DeleteCat(string id)
    {
        if (!CanEdit) throw new BusinessException("Tài khoản chỉ xem.");
        var keep = Cat.ToList(); Cat = Cat.Where(c => c.Id != id).ToList();
        try { PersistCat(); } catch { Cat = keep; throw; }
    }

    public CatItem? CatMatch(C3dSku k) => k.Code.Trim().Length == 0 ? null : Cat.FirstOrDefault(c => CatKey(c.V) == CatKey(k));

    /// <summary>Lấy kích thước + cách xếp từ danh mục vào mặt hàng (giữ mã, size, ngày, số lượng).</summary>
    public static void ApplyCat(C3dSku k, C3dSku v)
    {
        k.Name = v.Name.Length > 0 ? v.Name : k.Name; k.L = v.L; k.W = v.W; k.H = v.H; k.Kg = v.Kg; k.AllowRotate = v.AllowRotate; k.AllowStand = v.AllowStand;
        k.AllowLay = v.AllowLay; k.Sole = v.Sole; k.MaxLayers = v.MaxLayers; k.Orient = v.Orient; k.RowPlan = v.RowPlan; k.Note = v.Note;
    }

    // ------------------------------------------------------------ mặt hàng
    private static int _seq;
    public static C3dSku NewSku() => new() { Id = "s" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString("x") + (_seq++).ToString(CultureInfo.InvariantCulture), Qty = 100, L = 520, W = 280, H = 190, Kg = 10 };

    public static List<C3dSku> DemoSkus()
    {
        C3dSku M(string code, string size, string date, int qty, string name, double L = 520, double W = 280, double H = 190, double kg = 10, bool sole = false, string note = "")
        { var k = NewSku(); k.Code = code; k.Size = size; k.Date = date; k.Qty = qty; k.Name = name; k.L = L; k.W = W; k.H = H; k.Kg = kg; k.Sole = sole; k.Note = note; return k; }
        return new()
        {
            M("VRHLCK", "31/40", "15/09/2026", 420, "Tôm HLSO"), M("VRHLCK", "31/40", "22/09/2026", 360, "Tôm HLSO"),
            M("VRHLCK", "41/50", "20/09/2026", 480, "Tôm HLSO"), M("VRPDTO", "51/60", "18/09/2026", 300, "Tôm PDTO", 600, 400, 220, 12, true, "Xếp sole cho thoáng khí"),
        };
    }

    /// <summary>Dán từ Excel: Mã · Size · Date · SL · Dài · Rộng · Cao · kg · Xoay · Nằm · Đứng · Sole · Tầng tối đa. unitMul = 10 khi nhập cm.</summary>
    public static List<C3dSku> ParsePaste(string text, double unitMul)
    {
        static bool Yes(string? v, bool d) { var t = (v ?? "").Trim().ToLowerInvariant(); if (t.Length == 0) return d; return t is "1" or "x" or "c" or "có" or "co" or "true" or "y" or "yes" or "✓"; }
        var outp = new List<C3dSku>();
        foreach (var line in text.Replace("\r", "").Split('\n'))
        {
            var r = line.Split('\t'); if (!r.Any(x => x.Trim().Length > 0)) continue;
            string G(int i) => i < r.Length ? r[i] : "";
            double L = C3dJson.Num(G(4)) * unitMul, W = C3dJson.Num(G(5)) * unitMul, H = C3dJson.Num(G(6)) * unitMul, q = C3dJson.Num(G(3));
            if (!(L > 0 && W > 0 && H > 0) || !(q > 0)) continue;
            var k = NewSku(); k.Code = G(0).Trim(); k.Size = G(1).Trim(); k.Date = G(2).Trim(); k.Qty = (int)Math.Floor(q); k.L = L; k.W = W; k.H = H; k.Kg = C3dJson.Num(G(7));
            k.AllowRotate = Yes(G(8), true); k.AllowLay = Yes(G(9), false); k.AllowStand = Yes(G(10), true); k.Sole = Yes(G(11), false); k.MaxLayers = (int)Math.Max(0, Math.Floor(C3dJson.Num(G(12))));
            outp.Add(k);
        }
        return outp;
    }

    // ------------------------------------------------------------ xuất Excel
    public static XlsxOut BuildExcel(C3dPlan p)
    {
        if (p.Totals.Requested <= 0) throw new BusinessException("Chưa có phương án để xuất.");
        var c = p.Container; var t = p.Totals; var sp = p.Space;
        static double R2(double v) => Math.Round(v * 100, MidpointRounding.AwayFromZero) / 100;
        var today = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var s1 = new List<object?[]>
        {
            new object?[] { "BÁO CÁO XẾP HÀNG CONTAINER — Container Loading 3D" }, new object?[] { "Ngày", today }, Array.Empty<object?>(),
            new object?[] { "Container", c.Name }, new object?[] { "Loại", c.Type }, new object?[] { "Kích thước trong (D×R×C, mm)", $"{C3dEngine.Js(c.L)} × {C3dEngine.Js(c.W)} × {C3dEngine.Js(c.H)}" },
            new object?[] { "Cửa (R×C, mm)", $"{C3dEngine.Js(c.DoorW)} × {C3dEngine.Js(c.DoorH)}" }, new object?[] { "Tải trọng cho phép (kg)", c.MaxPayload }, new object?[] { "Red line (mm)", c.RedLine > 0 ? c.RedLine : "" },
            new object?[] { "Khe vách đầu / cửa / hai bên (mm)", $"{C3dEngine.Js(c.ClearFront)} / {C3dEngine.Js(c.ClearRear)} / {C3dEngine.Js(c.ClearSide)}" }, new object?[] { "Chiều cao xếp tối đa (mm)", sp.EffH }, Array.Empty<object?>(),
            new object?[] { "Số thùng yêu cầu", t.Requested }, new object?[] { "Số thùng đã xếp", t.Loaded }, new object?[] { "RỚT LẠI KHO (thùng)", t.Left },
            new object?[] { "Khối lượng hàng đã xếp (kg)", R2(t.Kg) }, new object?[] { "Thể tích trong cont (m³)", R2(t.VolInternal / 1e9) }, new object?[] { "Thể tích chứa hàng được (m³)", R2(t.VolUsable / 1e9) },
            new object?[] { "Khối hàng đã xếp (m³)", R2(t.VolBox / 1e9) }, new object?[] { "Khối còn trống tới giới hạn xếp (m³)", R2(t.VolFreeUsable / 1e9) }, new object?[] { "Khối còn trống trong cả cont (m³)", R2(t.VolFreeInternal / 1e9) },
            new object?[] { "Lấp đầy vùng chứa (%)", Math.Round(t.FillUsablePct, 1) }, new object?[] { "Chiều dài đã dùng (mm)", Math.Round(t.UsedLength) }, new object?[] { "Chiều dài còn trống trước cửa (mm)", Math.Round(t.FreeLength) },
            new object?[] { "Trọng tâm dọc / ngang (%)", t.Cog is null ? "" : $"{Math.Round(t.Cog.XPct)} / {Math.Round(t.Cog.YPct)}" },
            new object?[] { "Kiểm tra độc lập", p.Verify.Ok ? "ĐẠT (không chồng, đủ đỡ, đúng thứ tự đóng)" : "CÓ LỖI" },
            new object?[] { "Cách xếp", p.Options.MergeTail ? "Ghép lấp chỗ trống cạnh vách cuối" : "Tách riêng từng mặt hàng" }, Array.Empty<object?>(), new object?[] { "CẢNH BÁO" },
        };
        s1.AddRange(p.Warnings.Concat(p.Errors).Select(m => new object?[] { m }));
        var s2 = new List<object?[]> { new object?[] { "STT đóng", "Mã hàng", "Tên hàng", "Size", "Date", "Dài (mm)", "Rộng (mm)", "Cao (mm)", "kg/thùng", "Yêu cầu", "Đã xếp", "Rớt lại kho", "Kiểu xếp / hướng đặt", "Từ vách đầu (mm)", "Đến (mm)", "KG đã xếp", "Khối 1 thùng (m³)", "Khối yêu cầu (m³)", "Khối đã xếp (m³)", "Ghi chú đóng hàng", "Lý do rớt / lưu ý" } };
        for (var i = 0; i < p.Groups.Count; i++)
        {
            var g = p.Groups[i]; var k = g.Sku;
            s2.Add(new object?[] { i + 1, k.Code, k.Name, k.Size, k.Date, k.L, k.W, k.H, k.Kg, g.Requested, g.Loaded, g.Left, string.Join(" | ", g.Patterns.Select(x => x.Text)),
                g.XStart is { } xs ? Math.Round(xs - sp.X0) : "", g.XEnd is { } xe ? Math.Round(xe - sp.X0) : "", R2(g.Kg), Math.Round(g.VolUnit / 1e9, 4), R2(g.VolReq / 1e9), R2(g.VolLoaded / 1e9), k.Note, string.Join(" ", g.Reasons) });
        }
        s2.Add(new object?[] { "", "TỔNG", "", "", "", "", "", "", "", t.Requested, t.Loaded, t.Left, "", "", "", R2(t.Kg), "", R2(t.VolReqAll / 1e9), R2(t.VolBox / 1e9), "", "" });
        var s3 = new List<object?[]> { new object?[] { "STT đóng", "Dãy", "Loại", "Mã hàng", "Size", "Date", "Lớp (tầng)", "Cách vách đầu (mm)", "Cách vách phải (mm)", "Cao từ sàn (mm)", "Dài đặt (mm)", "Ngang đặt (mm)", "Cao đặt (mm)", "Hướng" } };
        foreach (var b in p.Boxes)
        {
            var k = p.Groups[b.Group].Sku;
            s3.Add(new object?[] { b.Seq, b.Row, b.Sample ? "Mẫu hải quan" : "Hàng chính", k.Code, k.Size, k.Date, b.Tier, Math.Round(b.X - sp.X0), Math.Round(b.Y - sp.Y0), Math.Round(b.Z), Math.Round(b.Dx), Math.Round(b.Dy), Math.Round(b.Dz), (b.Ori == "dung" ? "dựng đứng" : "đặt nằm") + (b.Rot != 0 ? " xoay 90°" : "") });
        }
        var s4 = new List<object?[]> { new object?[] { "Dãy", "Từ vách đầu (mm)", "Đến (mm)", "Số thùng", "Mẫu hải quan", "Các mặt hàng trong dãy" } };
        foreach (var r in p.Rows)
        {
            var names = new List<string>();
            foreach (var b in p.Boxes.Where(b => b.Row == r.Row)) { var k = p.Groups[b.Group].Sku; var nm = $"{k.Code} {k.Size} {k.Date}"; if (!names.Contains(nm)) names.Add(nm); }
            s4.Add(new object?[] { r.Row, Math.Round(r.X0 - sp.X0), Math.Round(r.X1 - sp.X0), r.N, r.Samples > 0 ? r.Samples : "", string.Join(" | ", names) });
        }
        var x = new XlsxOut();
        XlsxOut.Sheet Mk(string name, List<object?[]> aoa, double[] w, bool head)
        {
            var sh = new XlsxOut.Sheet { Name = name, Aoa = aoa, Widths = w, FreezeRows = head ? 1 : 0 };
            var hs = new XlsxOut.St(Bold: true, Fill: "D9E1F2", Font: "Calibri", Size: 11);
            var ns = new XlsxOut.St(Font: "Calibri", Size: 11);
            for (var r = 0; r < aoa.Count; r++) for (var cI = 0; cI < aoa[r].Length; cI++) sh.Set(r, cI, head && r == 0 ? hs : ns);
            return sh;
        }
        x.Sheets.Add(Mk("Tổng quan", s1, new double[] { 38, 60 }, false));
        x.Sheets[0].Set(0, 0, new XlsxOut.St(Bold: true, Font: "Calibri", Size: 14));
        x.Sheets.Add(Mk("Theo mặt hàng", s2, new double[] { 9, 16, 20, 10, 12, 9, 9, 9, 9, 10, 10, 11, 44, 14, 12, 12, 12, 12, 12, 34, 40 }, true));
        x.Sheets.Add(Mk("Trình tự đóng", s3, new double[] { 9, 7, 14, 16, 10, 12, 10, 16, 16, 14, 11, 11, 11, 18 }, true));
        x.Sheets.Add(Mk("Theo dãy", s4, new double[] { 7, 16, 12, 10, 14, 70 }, true));
        return x;
    }

    public static string SafeName(string? s) { var t = System.Text.RegularExpressions.Regex.Replace(s ?? "", @"[\\/:*?""<>|]+", "-").Trim(); return t.Length > 0 ? t : "Container"; }
}
