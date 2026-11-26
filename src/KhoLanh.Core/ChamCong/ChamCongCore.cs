using System.Globalization;
using System.Text.Json.Nodes;

namespace KhoLanh.Core.ChamCong;

/// <summary>
/// CHẤM CÔNG (1.129) — lõi thuần, theo bảng chấm công kho người dùng làm tay trên Excel (tháng 10/2026):
///  • Nguồn = file "CHI TIẾT CHẤM CÔNG" của phần mềm máy chấm công (mỗi dòng 1 nhân viên × 1 ngày: Mã nhân viên, Tên, Phòng ban,
///    Chức vụ, Ngày, Thứ, Giờ vào, Giờ ra, …, Tên ca). Chỉ lấy giờ vào / ra; các cột Công / Giờ / TC của máy KHÔNG dùng.
///  • Làm tròn giờ vào: phút &lt; 5 → tròn giờ · 5–34 → :30 · ≥ 35 → giờ sau. Giờ ra: &lt; 26 → tròn giờ · 26–55 → :30 · ≥ 56 → giờ sau.
///  • Nghỉ trưa theo bộ phận (Xuất 1:30 · VP 1:30 · Nhập 1:00) trừ "V.Sớm" (giờ được bớt nghỉ); có mặt ≤ 6 tiếng (nửa ngày) không trừ.
///  • Có mặt ≥ 15 tiếng: trừ thêm 1 giờ nghỉ tối.  Thiếu giờ vào hoặc ra → 0 giờ (cảnh báo).
/// Khớp 680 / 682 dòng của bảng người dùng (2 dòng còn lại do người dùng chỉnh tay riêng) — test ChamCongTests.
/// </summary>
public static class CcCore
{
    public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>"06:57" / "6:57" / "06:57:30" / số giờ Excel (0.29) / "2026-10-01 06:57" → số phút trong ngày; trống / sai → null.</summary>
    public static int? ParseTime(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0 || s == "-") return null;
        var sp = s.LastIndexOf(' ');
        if (sp > 0 && s[(sp + 1)..].Contains(':')) s = s[(sp + 1)..];
        var p = s.Split(':');
        if (p.Length is 2 or 3 && int.TryParse(p[0], NumberStyles.Integer, Inv, out var h) && int.TryParse(p[1], NumberStyles.Integer, Inv, out var m)
            && h is >= 0 and < 24 && m is >= 0 and < 60)
            return h * 60 + m;
        if (double.TryParse(s, NumberStyles.Float, Inv, out var d) && d >= 0 && d < 1) return (int)Math.Round(d * 1440) % 1440;
        return null;
    }

    public static string Hm(int? min) => min is int m ? $"{m / 60:00}:{m % 60:00}" : "";
    /// <summary>Phút → "8:30" (kiểu ô giờ làm tròn của bảng Excel).</summary>
    public static string HmShort(int? min) => min is int m ? $"{m / 60}:{m % 60:00}" : "";

    /// <summary>Ngày: dd/MM/yyyy, d/M/yyyy, yyyy-MM-dd (có thể kèm giờ), số ngày Excel.</summary>
    public static DateOnly? ParseDate(string? s)
    {
        s = (s ?? "").Trim();
        if (s.Length == 0) return null;
        var sp = s.IndexOf(' ');
        var d0 = sp > 0 ? s[..sp] : s;
        foreach (var f in new[] { "dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "d-M-yyyy", "yyyy-MM-dd", "yyyy/MM/dd", "dd.MM.yyyy", "d.M.yyyy", "M/d/yyyy" })
            if (DateOnly.TryParseExact(d0, f, Inv, DateTimeStyles.None, out var d)) return d;
        if (double.TryParse(s, NumberStyles.Float, Inv, out var n) && n > 20000 && n < 80000) return DateOnly.FromDateTime(DateTime.FromOADate(n));
        return null;
    }

    public static string Thu(DateOnly d) => d.DayOfWeek == DayOfWeek.Sunday ? "CN" : "T." + ((int)d.DayOfWeek + 1);

    /// <summary>Tên bộ phận chuẩn (khóa nghỉ trưa): XUẤT / NHẬP / VP / tên khác viết hoa.</summary>
    public static string NormNhom(string? s)
    {
        var t = (s ?? "").Trim().ToUpperInvariant();
        return t switch
        {
            "XUAT" or "XUẤT" => "XUẤT",
            "NHAP" or "NHẬP" => "NHẬP",
            "V.P" or "VĂN PHÒNG" or "VAN PHONG" => "VP",
            _ => t,
        };
    }

    public static int RoundIn(int min, CcCauHinh c)
    {
        int h = min / 60, m = min % 60;
        return m < c.VaoTronGio ? h * 60 : m < c.VaoNuaGio ? h * 60 + 30 : (h + 1) * 60;
    }

    public static int RoundOut(int min, CcCauHinh c)
    {
        int h = min / 60, m = min % 60;
        return m < c.RaTronGio ? h * 60 : m < c.RaNuaGio ? h * 60 + 30 : (h + 1) * 60;
    }

    public sealed record Ket(int? VaoTron, int? RaTron, int Nghi, int Cong, string CanhBao)
    {
        public double Gio => Math.Round(Cong / 60.0, 2);
    }

    /// <summary>Tính 1 ngày: giờ vào / ra (phút), bộ phận, V.Sớm (giờ bớt nghỉ trưa).</summary>
    public static Ket Tinh(int? vao, int? ra, string nhom, double vsom, CcCauHinh c)
    {
        if (vao is null && ra is null) return new(null, null, 0, 0, "");
        if (vao is null || ra is null) return new(vao is int v0 ? RoundIn(v0, c) : null, ra is int r0 ? RoundOut(r0, c) : null, 0, 0, vao is null ? "Thiếu giờ vào" : "Thiếu giờ ra");
        int a = RoundIn(vao.Value, c), b = RoundOut(ra.Value, c);
        var warn = vao.Value < c.VaoSomNhat ? $"Giờ vào {Hm(vao)} quá sớm — kiểm tra lại" : "";
        if (b <= a) return new(a, b, 0, 0, warn.Length > 0 ? warn : "Giờ ra không sau giờ vào");
        var span = b - a;
        var nghi = 0;
        if (span > c.NuaNgayToiDa) nghi = Math.Max(0, c.NghiTruaOf(nhom) - (int)Math.Round(vsom * 60));
        if (span >= c.NghiToiTu) nghi += c.NghiToiPhut;
        return new(a, b, nghi, Math.Max(0, span - nghi), warn);
    }
}

/// <summary>Quy tắc tính giờ (lưu sheet CauHinh, sửa ở tab Cấu hình).</summary>
public sealed class CcCauHinh
{
    /// <summary>Phòng ban lấy từ file máy chấm công (cách nhau dấu phẩy). Nhân viên đã có trong danh sách luôn được lấy dù khác phòng ban.</summary>
    public string PhongBan = "Kho";
    public int VaoTronGio = 5, VaoNuaGio = 35, RaTronGio = 26, RaNuaGio = 56;
    public Dictionary<string, int> NghiTrua = new(StringComparer.OrdinalIgnoreCase) { ["XUẤT"] = 90, ["NHẬP"] = 60, ["VP"] = 90 };
    /// <summary>Có mặt (sau làm tròn) không quá số phút này → nửa ngày, không trừ nghỉ trưa.</summary>
    public int NuaNgayToiDa = 360;
    /// <summary>Có mặt từ số phút này trở lên → trừ thêm <see cref="NghiToiPhut"/> nghỉ tối.</summary>
    public int NghiToiTu = 900, NghiToiPhut = 60;
    /// <summary>Giờ vào trước mốc này (phút) → cảnh báo (thường là lần bấm ra của đêm trước).</summary>
    public int VaoSomNhat = 240;

    public int NghiTruaOf(string nhom) => NghiTrua.TryGetValue(CcCore.NormNhom(nhom), out var v) ? v : 0;

    public JsonObject ToJson() => new()
    {
        ["phongBan"] = PhongBan, ["vaoTron"] = VaoTronGio, ["vaoNua"] = VaoNuaGio, ["raTron"] = RaTronGio, ["raNua"] = RaNuaGio,
        ["nghiTrua"] = new JsonObject(NghiTrua.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
        ["nuaNgay"] = NuaNgayToiDa, ["nghiToiTu"] = NghiToiTu, ["nghiToi"] = NghiToiPhut, ["vaoSomNhat"] = VaoSomNhat,
    };

    public static CcCauHinh From(JsonObject? o)
    {
        var c = new CcCauHinh();
        if (o is null) return c;
        int I(string k, int d) => o[k] is JsonValue v && v.TryGetValue<int>(out var x) ? x : d;
        c.PhongBan = o["phongBan"]?.ToString() ?? c.PhongBan;
        c.VaoTronGio = I("vaoTron", c.VaoTronGio); c.VaoNuaGio = I("vaoNua", c.VaoNuaGio); c.RaTronGio = I("raTron", c.RaTronGio); c.RaNuaGio = I("raNua", c.RaNuaGio);
        c.NuaNgayToiDa = I("nuaNgay", c.NuaNgayToiDa); c.NghiToiTu = I("nghiToiTu", c.NghiToiTu); c.NghiToiPhut = I("nghiToi", c.NghiToiPhut); c.VaoSomNhat = I("vaoSomNhat", c.VaoSomNhat);
        if (o["nghiTrua"] is JsonObject nt)
        {
            c.NghiTrua.Clear();
            foreach (var (k, v) in nt) if (v is JsonValue jv && jv.TryGetValue<int>(out var x)) c.NghiTrua[CcCore.NormNhom(k)] = x;
        }
        return c;
    }

    public IEnumerable<string> PhongBanList() => PhongBan.Split(',', ';').Select(s => s.Trim()).Where(s => s.Length > 0);
}

/// <summary>Nhân viên chấm công. <see cref="Ma"/> = mã trên máy chấm công ("" = không chấm máy, vd người làm thay — nhập giờ tay / chép giờ người khác).</summary>
public sealed class CcNhanVien
{
    public string Id = "", Ma = "", Ten = "", Nhom = "", PhongBan = "", GhiChu = "";
    public bool DangLam = true;
}

/// <summary>1 dòng file máy chấm công.</summary>
public sealed record CcGoc(string Ma, string Ten, string PhongBan, string ChucVu, DateOnly Ngay, string Vao, string Ra, string Ca);

/// <summary>Sửa tay 1 ngày của 1 nhân viên (null = theo file máy). <see cref="ChepTu"/> = lấy giờ vào / ra của nhân viên khác cùng ngày.</summary>
public sealed class CcSua
{
    public string Nv = "", GhiChu = "";
    public DateOnly Ngay;
    public string? Vao, Ra, ChepTu;
    public double? VSom, Chot;
    public bool Empty => Vao is null && Ra is null && ChepTu is null && VSom is null && Chot is null && GhiChu.Length == 0;
}

/// <summary>Kết quả 1 nhân viên × 1 ngày.</summary>
public sealed class CcDong
{
    public CcNhanVien Nv = null!;
    public DateOnly Ngay;
    public string VaoGoc = "", RaGoc = "", Vao = "", Ra = "", GhiChu = "", CanhBao = "", ChepTu = "";
    public double VSom;
    public CcCore.Ket Ket = null!;
    /// <summary>Giờ chốt (ghi đè tay nếu có).</summary>
    public double Chot;
    public bool SuaVao, SuaRa, SuaVSom, SuaChot;
    public bool DaSua => SuaVao || SuaRa || SuaVSom || SuaChot || ChepTu.Length > 0;
    public bool CoMat => Chot > 0;
}
