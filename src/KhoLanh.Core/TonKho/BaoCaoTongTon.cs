using KhoLanh.Core.TonKhoGui;

namespace KhoLanh.Core.TonKho;

/// <summary>Một cảnh báo đối soát: <see cref="Loi"/> = lệch số chưa giải thích (đỏ), ngược lại = cần kiểm tra (vàng). <see cref="Key"/> = dòng bảng đối soát liên quan.</summary>
public sealed record CanhBao(string Key, bool Loi, string Text);

/// <summary>
/// Tab "Báo Cáo" của Tồn kho An An (1.69): bảng Báo Cáo Tổng Tồn gộp dòng Tồn Kho Gửi như app cũ (PC 5.35, renderBaoCao:
/// dòng "Kho Gửi" nằm ngay dưới TỔNG TỒN KHO, KHÔNG cộng vào tổng; file Excel có thêm "TỔNG TỒN KHO AN AN + KHO GỬI") +
/// bảng Đối soát và các cảnh báo lệch số. Hàm thuần trên <see cref="AnAnData"/>.
/// </summary>
public static class BaoCaoTongTon
{
    public const double Eps = 0.005;
    public static bool Lech(double? v) => v is double x && !double.IsNaN(x) && Math.Abs(x) >= Eps;

    /// <summary>Kind: manual (Bột + Sốt + Bếp) · module · total (TỔNG TỒN KHO) · khogui · grand.</summary>
    public sealed record Dong(string Label, double? Weight, string Kind);

    /// <summary>Chưa nhập "Bột + Sốt + Bếp" (trống hoặc 0) → dòng vẫn hiện số 0 và xuất Excel phải nhắc.</summary>
    public static bool ManualMissing(AnAnData d) => !AnAnData.HasStock(d.ManualValues.GetValueOrDefault(AnAnData.LabelKey(AnAnData.ManualLabel)));

    public static List<Dong> Bang(AnAnData d)
    {
        var rows = d.TongTonRows();
        var list = rows.Select(r => new Dong(r.Label, r.Weight, r.Manual ? "manual" : "module")).ToList();
        var total = AnAnData.TotalOf(rows);
        list.Add(new Dong(AnAnExport.TongTonMainTotal, total, "total"));
        var kg = d.KhoGui;
        list.Add(new Dong(AnAnExport.TongTonKhoGuiRow, kg, "khogui"));
        list.Add(new Dong(AnAnExport.TongTonGrand, total is null && kg is null ? null : (total ?? 0) + (kg ?? 0), "grand"));
        return list;
    }

    /// <summary>Nhắc trước khi xuất Excel tổng tồn (chỉ bản đủ 8 bảng — bản HLSO không có dòng nhập tay).</summary>
    public static List<string> CanhBaoXuat(AnAnData d)
    {
        var o = new List<string>();
        if (ManualMissing(d)) o.Add($"“{AnAnData.ManualLabel}” chưa nhập số liệu (đang là 0 kg) — nhập ở tab Tổng Hợp › Báo cáo ngoài sổ sách.");
        if (d.KhoGui is null) o.Add("Tồn Kho Gửi chưa có số liệu — bấm “Lấy Kho Gửi từ Tồn kho gửi” hoặc nạp dữ liệu bên Tồn kho gửi.");
        return o;
    }

    public sealed record DsDong(string Key, string Label, double? Value, bool Has = true);
    public sealed class DsKq { public List<DsDong> Rows = new(); public List<CanhBao> CanhBao = new(); public bool Lech => CanhBao.Count > 0; }

    /// <summary>
    /// Bảng "Đối Soát: 8 Module vs Load Data". Khác app cũ: tổng 8 module KHÔNG gồm dòng nhập tay Bột + Sốt + Bếp (số đó không có trong
    /// Load Data — trước đây nhập số là "lệch" ngay); so với Bảng Tổng Hợp HLSO thì dùng tổng tồn kho (có Bột + Sốt + Bếp, HLSO cũng có dòng này).
    /// </summary>
    public static DsKq DoiSoat(AnAnData d)
    {
        var k = new DsKq();
        var rows = d.TongTonRows();
        var mods = AnAnData.TotalOf(rows.Where(r => !r.Manual));
        var tong = AnAnData.TotalOf(rows);
        var all = d.TotalWeightAll(); var (hl, hasHl) = d.HlsoGrandTotal();
        double? Diff(double? a, double? b) => a is double x && b is double y ? x - y : null;
        var diff = Diff(mods, all); var dHl = hasHl ? Diff(hl, tong) : null;
        k.Rows.Add(new("all", "Tổng toàn bộ dữ liệu Load Data", all));
        k.Rows.Add(new("mod", "Tổng cộng 8 module (không gồm Bột + Sốt + Bếp)", mods));
        k.Rows.Add(new("diff", "Chênh lệch (8 module − Load Data)", diff));
        k.Rows.Add(new("tong", "Tổng tồn kho (bảng Báo Cáo Tổng Tồn)", tong));
        k.Rows.Add(new("hlso", "Tổng Bảng Tổng Hợp HLSO", hasHl ? hl : null, hasHl));
        k.Rows.Add(new("dhlso", "Chênh lệch (HLSO − Tổng tồn kho)", dHl, hasHl));
        if (!d.HasData) return k;
        var rc = d.Reconcile();
        if (Lech(diff))
        {
            var why = new List<string>();
            if (rc is not null && rc.Overlap.Count > 0) why.Add($"{JsText.En(rc.Overlap.Count, 0, 0)} dòng thuộc ≥ 2 bảng (đếm dư {AnAnData.KgText(rc.OverlapExcess)} kg)");
            if (rc is not null && rc.Orphan.Count > 0) why.Add($"{JsText.En(rc.Orphan.Count, 0, 0)} dòng không thuộc bảng nào ({AnAnData.KgText(rc.OrphanWeight)} kg)");
            k.CanhBao.Add(new("diff", true, $"Tổng 8 bảng Phân Loại lệch Load Data {AnAnData.KgText(diff)} kg" + (why.Count > 0 ? ": " + string.Join(" · ", why) : "") + "."));
        }
        if (Lech(dHl))
        {
            var gap = d.HlsoGap();
            k.CanhBao.Add(new("dhlso", false, $"Bảng Tổng Hợp HLSO lệch tổng tồn kho {AnAnData.KgText(dHl)} kg"
                + (gap is { } g && g.Rows.Count > 0 ? $": {JsText.En(g.Rows.Count, 0, 0)} dòng chưa gộp đặc tính ({AnAnData.KgText(g.Weight)} kg)" : "") + "."));
        }
        return k;
    }

    /// <summary>Đối soát Tồn kho gửi: tổng Load Data ↔ 6 nhóm Phân Loại ↔ Bảng Tổng Hợp. Key trùng khóa dòng của bảng Đối Soát (GuiView).</summary>
    public static List<CanhBao> GuiCanhBao(GuiData.DoiSoat ds, double pivotTotal)
    {
        var o = new List<CanhBao>();
        var diff = GuiUnexplained(ds, pivotTotal);
        if (Lech(diff)) o.Add(new("c", true, $"Chênh lệch chưa giải thích được {AnAnData.KgText(diff)} kg giữa tổng Phân Loại và Bảng Tổng Hợp."));
        if (Lech(ds.ModuleTotal - ds.LoadTotal)) o.Add(new("m", false, $"Tổng 6 nhóm Phân Loại lệch Load Data {AnAnData.KgText(ds.ModuleTotal - ds.LoadTotal)} kg"
            + (ds.DupCount > 0 ? $" ({JsText.En(ds.DupCount, 0, 0)} dòng đếm ở nhiều nhóm)" : "") + "."));
        if (Lech(ds.NoDtSum)) o.Add(new("!ndt", false, $"{JsText.En(ds.NoDtCount, 0, 0)} dòng không suy ra được đặc tính ({AnAnData.KgText(ds.NoDtSum)} kg) — chưa vào Bảng Tổng Hợp."));
        if (Lech(ds.UngroupedSum)) o.Add(new("!ung", false, $"{JsText.En(ds.UngroupedCount, 0, 0)} dòng có đặc tính chưa gộp nhóm ({AnAnData.KgText(ds.UngroupedSum)} kg) — chưa vào Bảng Tổng Hợp."));
        return o;
    }

    public static double GuiUnexplained(GuiData.DoiSoat ds, double pivotTotal) => ds.ModuleTotal - ds.DupSum - ds.NoDtSum - ds.UngroupedSum - pivotTotal;
}
