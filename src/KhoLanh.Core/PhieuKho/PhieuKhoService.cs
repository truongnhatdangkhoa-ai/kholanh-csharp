using System.Text.Json.Nodes;
using KhoLanh.Core.Models;
using KhoLanh.Core.Modules;
using KhoLanh.Core.Services;

namespace KhoLanh.Core.PhieuKho;

/// <summary>
/// Module Phiếu xuất kho (C#, viết lại theo "Phiếu nhập/xuất kho" của app cũ). Giai đoạn 1: Phiếu XUẤT KHẨU.
/// Dùng CHUNG dữ liệu với app cũ: database "phieu-nhap-xuat", sheet PNX_Phieu (mỗi phiếu 1 dòng: id, loai, ngay, soPhieu, lsx, khach, capNhat, data = "__JSON__" + JSON phiếu).
/// Xuất Excel = điền thẳng vào file mẫu .xlsm nhúng sẵn (giữ nguyên định dạng, hình, macro).
/// </summary>
public sealed class PhieuKhoService
{
    public const string Db = "phieu-nhap-xuat", SheetPhieu = "PNX_Phieu", PermKey = "m13";

    public List<Phieu> List { get; private set; } = new();
    public bool Loaded { get; private set; }

    private readonly Func<ISheetSource> _src;
    private readonly Session _session;
    private readonly AuditService _audit;
    public PhieuKhoService(Func<ISheetSource> source, Session session, AuditService audit) { _src = source; _session = session; _audit = audit; }

    public bool CanView => _session.Can(PermKey, Perm.View);
    public bool CanEdit => _session.Can(PermKey, Perm.Edit);
    public bool CanExport => _session.Can(PermKey, Perm.Export);
    private void RequireEdit() { if (!CanEdit) throw new BusinessException("Tài khoản không có quyền sửa Phiếu xuất kho (lập / sửa / xóa phiếu)."); }
    private void Log(string action, string detail) => _audit.Log(_session.Username, "Phiếu xuất kho: " + action, detail);

    public void Load()
    {
        if (!CanView) throw new BusinessException("Tài khoản không có quyền xem Phiếu xuất kho.");
        JsonArray rows;
        try { rows = JsonNode.Parse(_src().Read(Db, SheetPhieu)) as JsonArray ?? new JsonArray(); } catch { rows = new JsonArray(); }
        var list = new List<Phieu>();
        foreach (var o in rows.OfType<JsonObject>())
        {
            var d = o["data"]?.ToString() ?? "";
            if (d.StartsWith("__JSON__")) d = d[8..];
            if (PhieuKhoCore.FromJson(d) is { } p) list.Add(p);
        }
        List = list.OrderByDescending(p => p.CapNhat, StringComparer.Ordinal).ToList();
        Loaded = true;
    }

    private void SaveAll()
    {
        var rows = new JsonArray(List.Select(p => (JsonNode?)new JsonObject
        {
            ["id"] = p.Id, ["loai"] = p.Loai, ["ngay"] = p.Ngay, ["soPhieu"] = p.SoPhieu, ["lsx"] = p.Lsx, ["khach"] = p.Khach, ["capNhat"] = p.CapNhat,
            ["data"] = "__JSON__" + PhieuKhoCore.ToJson(p),
        }).ToArray());
        _src().Write(Db, new Dictionary<string, JsonArray?> { [SheetPhieu] = rows });
    }

    public Phieu NewPhieu()
    {
        var p = PhieuKhoCore.NewPhieu();
        PhieuKhoCore.RefreshNumbers(List, p);
        return p;
    }

    /// <summary>Lưu (thêm / sửa) 1 phiếu. Trả về bản đã lưu.</summary>
    public Phieu Save(Phieu p)
    {
        RequireEdit();
        p = PhieuKhoCore.Clone(p);
        foreach (var m in p.Mh) foreach (var r in m.DongList) r.Date = PhieuKhoCore.NormDate(r.Date);
        var isNew = p.Id.Length == 0;
        if (isNew) p.Id = "px" + DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString("x") + Guid.NewGuid().ToString("N")[..3];
        p.CapNhat = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
        p.DaSua = true;
        var i = List.FindIndex(x => x.Id == p.Id);
        if (i >= 0) List[i] = p; else List.Insert(0, p);
        SaveAll();
        Log(isNew ? "Tạo phiếu" : "Sửa phiếu", $"{p.SoPhieu} ({PhieuKhoCore.LoaiTen(p.Loai)})");
        return p;
    }

    public void Delete(string id)
    {
        RequireEdit();
        var p = List.FirstOrDefault(x => x.Id == id) ?? throw new BusinessException("Không tìm thấy phiếu.");
        List.Remove(p);
        SaveAll();
        Log("Xóa phiếu", p.SoPhieu);
    }

    public static byte[] Template()
    {
        using var s = typeof(PhieuKhoService).Assembly.GetManifestResourceStream("KhoLanh.Core.PhieuKho.mau-xuat-khau.xlsm")
            ?? throw new InvalidOperationException("Thiếu file mẫu Phiếu xuất kho.");
        using var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
    }

    /// <summary>Xuất phiếu ra file .xlsm bằng cách điền vào mẫu. Phiếu phải qua kiểm tra; đã xuất = coi là ĐÃ IN (không tự đổi số nữa).</summary>
    public void Export(Phieu p, string path)
    {
        if (!CanExport) throw new BusinessException("Tài khoản không có quyền xuất file Phiếu xuất kho.");
        p = PhieuKhoCore.Clone(p);
        foreach (var m in p.Mh) foreach (var r in m.DongList) r.Date = PhieuKhoCore.NormDate(r.Date);
        var errs = PhieuKhoCore.Validate(p);
        if (errs.Count > 0) throw new BusinessException(string.Join(" ", errs.Take(3)) + (errs.Count > 3 ? $" (+{errs.Count - 3} lỗi nữa)" : ""));
        var plan = PhieuKhoCore.BuildPlan(p);
        File.WriteAllBytes(path, XlsxTemplate.Fill(Template(), plan.Cells, plan.Options));
        Log("Xuất Excel", $"{p.SoPhieu} → {Path.GetFileName(path)}");
        if (CanEdit && p.Id.Length > 0 && List.FirstOrDefault(x => x.Id == p.Id) is { } saved)
        {
            saved.XuatLuc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
            try { SaveAll(); } catch { /* không ghi được dấu "đã xuất" thì bỏ qua */ }
        }
    }
}
