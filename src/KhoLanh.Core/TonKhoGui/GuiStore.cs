using System.Globalization;
using System.Text.Json.Nodes;
using KhoLanh.Core.Modules;
using KhoLanh.Core.TonKho;

namespace KhoLanh.Core.TonKhoGui;

/// <summary>Đọc / ghi các sheet của database "ton-kho-gui" ĐÚNG định dạng app cũ (dùng chung dữ liệu với bản WebView2).</summary>
public static class GuiStore
{
    public const string Db = "ton-kho-gui";
    public const string SRows = "DuLieuChinh", SMeta = "DuLieuMeta", SFile = "FileNap", SFileMeta = "FileNap_Meta", SCustom = "DieuKienTuThem",
        SManual = "DacTinh_NhapTay", SLoMap = "DacTinh_ChuyenLo", SGroup = "DacTinh_NhomGop", SSeen = "DacTinh_DaGhiNhan", SPhieu = "PhieuXuat", SPhieuItems = "PhieuXuatItems";
    private static string S(JsonNode? n) => AnAnStore.S(n);

    public static JsonArray ReadArr(ISheetSource src, string sheet)
    {
        try { return JsonNode.Parse(src.Read(Db, sheet)) as JsonArray ?? new(); } catch { return new(); }
    }
    public static JsonNode? ReadJsonCell(ISheetSource src, string sheet)
    {
        var a = ReadArr(src, sheet); if (a.Count == 0 || a[0] is not JsonObject o) return null;
        var t = S(o["data"]); if (!t.StartsWith(AnAnStore.JsonTag, StringComparison.Ordinal)) return null;
        try { return JsonNode.Parse(t[AnAnStore.JsonTag.Length..]); } catch { return null; }
    }
    public static double Num(JsonNode? n) { var x = JsText.LooseNum(S(n)); return double.IsNaN(x) ? 0 : x; }
    private static JsonNode? D(double v) => JsonValue.Create(v);

    // ---------- Dữ Liệu
    public static JsonObject RowToDb(GuiRow r)
    {
        var o = new JsonObject();
        foreach (var (key, label) in GuiData.TargetCols)
            o[label] = key switch { "soKien" => D(r.SoKien), "trongLuong" => D(r.TrongLuong), _ => JsonValue.Create(GuiData.Get(r, key)) };
        o[GuiData.PxDaXuatLabel] = r.PxDaXuat ? 1 : 0;
        return o;
    }
    public static GuiRow RowFromDb(JsonObject o)
    {
        var r = new GuiRow();
        string G(string label) => S(o[label]);
        r.MaHang = G("Mã hàng"); r.TenHang = G("Tên hàng"); r.DacTinh = G("Đặc tính"); r.SoLo = G("Số lô"); r.Size = G("Size"); r.QuyCach = G("Quy cách");
        r.NgayNhapKho = G("Ngày nhập kho"); r.PhieuNhap = G("Phiếu nhập/Voucher");
        r.SoKien = Num(o["Số kiện"]); r.TrongLuong = Num(o["Trọng Lượng (KG)"]);
        var px = o[GuiData.PxDaXuatLabel]; r.PxDaXuat = Num(px) != 0 || S(px) is "true" or "TRUE";
        return r;
    }
    public static JsonArray RowsSheet(IEnumerable<GuiRow> rows) => new(rows.Select(r => (JsonNode)RowToDb(r)).ToArray());
    public static JsonArray MetaSheet(GuiData d) => AnAnStore.JsonCell(new JsonObject { ["importedAt"] = d.ImportedAt, ["dataDate"] = d.DataDate, ["napId"] = d.NapId });

    // ---------- Điều kiện tự thêm
    public static JsonArray CustomSheet(GuiData d)
    {
        var o = new JsonObject();
        foreach (var t in GuiData.CustomTabs)
            o[t] = new JsonArray(d.CustomFor(t).Select(cf => (JsonNode)new JsonObject { ["id"] = cf.Id, ["colLabel"] = cf.ColLabel, ["op"] = cf.Op, ["value"] = cf.Value, ["from"] = cf.From, ["to"] = cf.To }).ToArray());
        return AnAnStore.JsonCell(o);
    }
    public static void LoadCustom(JsonNode? node, GuiData d)
    {
        d.Custom = new();
        if (node is not JsonObject o) return;
        foreach (var t in GuiData.CustomTabs)
        {
            var l = d.CustomFor(t);
            if (o[t] is JsonArray a)
                foreach (var x in a.OfType<JsonObject>())
                    l.Add(new CustomFilter { Id = S(x["id"]), ColLabel = S(x["colLabel"]), Op = S(x["op"]).Length > 0 ? S(x["op"]) : "eq", Value = S(x["value"]), From = (int)(Num(x["from"]) is var f && f > 0 ? f : 2), To = (int)(Num(x["to"]) is var tt && tt > 0 ? tt : 4) });
        }
    }

    // ---------- Đặc tính
    public static JsonArray ManualSheet(GuiData d) => new(d.DtManual.Select(r => (JsonNode)new JsonObject { ["Diễn giải đặc tính"] = r.MaHang, ["Đặc tính"] = r.DacTinh }).ToArray());
    public static JsonArray LoMapSheet(GuiData d) => new(d.DtLoMap.Values.Select(r => (JsonNode)new JsonObject { ["Số lô"] = r.Lo, ["Đặc tính"] = r.DacTinh }).ToArray());
    public static JsonArray GroupSheet(GuiData d) => new(d.DtGroupMap.Select(kv => (JsonNode)new JsonObject { ["Đặc tính"] = kv.Key, ["Nhóm đã gộp"] = kv.Value }).ToArray());
    public static JsonArray SeenSheet(GuiData d) => new(d.DtSeen.Select(v => (JsonNode)new JsonObject { ["Đặc tính"] = v }).ToArray());

    public static GuiData Load(ISheetSource src)
    {
        var d = new GuiData();
        d.Rows = ReadArr(src, SRows).OfType<JsonObject>().Select(RowFromDb).ToList();
        if (ReadJsonCell(src, SMeta) is JsonObject m) { d.ImportedAt = Num(m["importedAt"]); d.DataDate = S(m["dataDate"]); d.NapId = S(m["napId"]); }
        LoadCustom(ReadJsonCell(src, SCustom), d);
        d.DtManual = ReadArr(src, SManual).OfType<JsonObject>().Select(r => (S(r["Diễn giải đặc tính"]), S(r["Đặc tính"]))).Where(r => r.Item2.Length > 0).ToList();
        foreach (var r in ReadArr(src, SLoMap).OfType<JsonObject>()) { var lo = JsText.JsTrim(S(r["Số lô"])); var dt = JsText.JsTrim(S(r["Đặc tính"])); if (lo.Length > 0 && dt.Length > 0) d.DtLoMap[GuiData.LoKey(lo)] = (lo, dt); }
        foreach (var r in ReadArr(src, SGroup).OfType<JsonObject>()) { var k = JsText.JsTrim(S(r["Đặc tính"])); if (k.Length > 0) d.DtGroupMap[k] = S(r["Nhóm đã gộp"]); }
        foreach (var r in ReadArr(src, SSeen).OfType<JsonObject>()) { var v = JsText.JsTrim(S(r["Đặc tính"])); if (v.Length > 0) d.DtSeen.Add(v); }
        return d;
    }

    // ---------- Phiếu xuất
    public sealed class SlipItem { public string MaHang = "", TenHang = "", PhieuNhap = "", SoLo = "", Size = ""; public string SoKien = ""; public double SoLuongXuat, TrongLuongTru; public int? Occ; public double? XuatKg; }
    public sealed class Slip
    {
        public string Id = "", MaPhieu = "", NgayTao = "", Nguon = "", NguoiTao = "", GhiChu = "", LoadDate = ""; public double? LoadAt; public List<SlipItem> Items = new();
    }
    public static List<Slip> LoadSlips(ISheetSource src)
    {
        var items = new Dictionary<string, List<SlipItem>>();
        foreach (var r in ReadArr(src, SPhieuItems).OfType<JsonObject>())
        {
            var pid = S(r["phieuId"]); if (!items.TryGetValue(pid, out var l)) items[pid] = l = new();
            var it = new SlipItem { MaHang = S(r["maHang"]), TenHang = S(r["tenHang"]), PhieuNhap = S(r["phieuNhap"]), SoLo = S(r["soLo"]), Size = S(r["size"]), SoKien = S(r["soKien"]), SoLuongXuat = Num(r["soLuongXuat"]), TrongLuongTru = Num(r["trongLuongTru"]) };
            var occ = S(r["occ"]); if (occ.Length > 0 && double.TryParse(occ, NumberStyles.Float, CultureInfo.InvariantCulture, out var oc)) it.Occ = (int)oc;
            var xk = S(r["xuatKg"]); if (xk.Length > 0 && double.TryParse(xk, NumberStyles.Float, CultureInfo.InvariantCulture, out var xv)) it.XuatKg = xv;
            l.Add(it);
        }
        return ReadArr(src, SPhieu).OfType<JsonObject>().Select(r =>
        {
            var id = S(r["id"]); var la = S(r["loadAt"]);
            return new Slip
            {
                Id = id, MaPhieu = S(r["maPhieu"]), NgayTao = S(r["ngayTao"]), Nguon = S(r["nguon"]), NguoiTao = S(r["nguoiTao"]), GhiChu = S(r["ghiChu"]), LoadDate = S(r["loadDate"]),
                LoadAt = la.Length == 0 ? null : Num(r["loadAt"]), Items = items.GetValueOrDefault(id) ?? new(),
            };
        }).ToList();
    }
    public static Dictionary<string, JsonArray?> SlipSheets(IEnumerable<Slip> slips)
    {
        var list = slips.ToList(); var head = new JsonArray(); var it = new JsonArray();
        foreach (var p in list)
        {
            head.Add(new JsonObject { ["id"] = p.Id, ["maPhieu"] = p.MaPhieu, ["ngayTao"] = p.NgayTao, ["nguon"] = p.Nguon, ["nguoiTao"] = p.NguoiTao, ["ghiChu"] = p.GhiChu, ["loadAt"] = D(p.LoadAt ?? 0), ["loadDate"] = p.LoadDate });
            foreach (var x in p.Items)
                it.Add(new JsonObject
                {
                    ["phieuId"] = p.Id, ["maHang"] = x.MaHang, ["tenHang"] = x.TenHang, ["phieuNhap"] = x.PhieuNhap, ["soLo"] = x.SoLo, ["size"] = x.Size, ["soKien"] = x.SoKien,
                    ["soLuongXuat"] = D(x.SoLuongXuat), ["occ"] = x.Occ is int o ? o : "", ["xuatKg"] = x.XuatKg is double k ? D(k) : "", ["trongLuongTru"] = D(x.TrongLuongTru),
                });
        }
        return new() { [SPhieu] = head, [SPhieuItems] = it };
    }
}
