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

    /// <summary>Loại ngày đặc biệt: P nghỉ có phép · L nghỉ lễ · T nghỉ Tết · CT công tác ("" = ngày thường).</summary>
    public static readonly (string Ma, string Ten)[] Loais = { ("P", "Nghỉ có phép"), ("L", "Nghỉ lễ"), ("T", "Nghỉ Tết"), ("CT", "Công tác") };
    public static string NormLoai(string? s)
    {
        var t = CcCoreText.Fold(s).Replace(".", "").Replace(" ", "");
        return t switch
        {
            "" => "",
            "p" or "phep" or "x" or "nghiphep" or "nghicophep" or "cophep" => "P",
            "l" or "le" or "nghile" => "L",
            "t" or "tet" or "nghitet" => "T",
            "ct" or "congtac" => "CT",
            _ => "?",
        };
    }
    public static string TenLoai(string loai) => Loais.FirstOrDefault(x => x.Ma == loai).Ten ?? "";

    public sealed record Ket(int? VaoTron, int? RaTron, int Nghi, int Cong, string CanhBao)
    {
        public double Gio => Math.Round(Cong / 60.0, 2);
    }

    /// <summary>Tính 1 ngày: giờ vào / ra (phút), bộ phận, V.Sớm (giờ bớt nghỉ trưa).</summary>
    public static Ket Tinh(int? vao, int? ra, string nhom, double vsom, CcCauHinh c)
    {
        if (vao is null && ra is null) return new(null, null, 0, 0, "");
        if (vao is null || ra is null)
            return new(vao is int v0 ? RoundIn(v0, c) : null, ra is int r0 ? RoundOut(r0, c) : null, 0, 0, $"Chỉ có 1 lần chấm ({Hm(vao ?? ra)}) — thiếu giờ vào hoặc giờ ra");
        int a = RoundIn(vao.Value, c), b = RoundOut(ra.Value, c);
        var warn = vao.Value < c.VaoSomNhat ? $"Giờ vào {Hm(vao)} quá sớm — kiểm tra lại" : "";
        if (b <= a) return new(a, b, 0, 0, warn.Length > 0 ? warn : "Giờ ra không sau giờ vào");
        var span = b - a;
        var nghi = 0;
        if (span > c.NuaNgayToiDa) nghi = c.NghiTruaOf(nhom);
        if (span >= c.NghiToiTu) nghi += c.NghiToiPhut;
        // V.Sớm = vào sớm / đi sớm (người dùng 26/11): giờ CỘNG THÊM vào ngày đó. Bảng Excel cũ trừ vào ô Nghỉ (cùng kết quả khi có nghỉ trưa);
        // cộng thẳng để ngày nửa buổi (không trừ nghỉ trưa) vẫn được tính.
        return new(a, b, nghi, Math.Max(0, span - nghi + (int)Math.Round(vsom * 60)), warn);
    }

    /// <summary>1 lần sang công: <paramref name="Gio"/> giờ từ ngày <paramref name="Tu"/> (null = giờ dư tháng trước) sang ngày <paramref name="Den"/>.</summary>
    public sealed record Sang(DateOnly? Tu, DateOnly Den, double Gio);

    /// <summary>
    /// SANG CÔNG (người dùng 26/11: "ngày 5 làm 9 tiếng, ngày 6 làm 7 tiếng → sang 1 tiếng cho ngày 6, 2 ngày đủ 8 tiếng"):
    /// giờ trên <paramref name="chuan"/> của mỗi ngày (kể cả Chủ nhật) bù cho ngày thiếu trong tháng — ngày có đi làm nhưng thiếu giờ trước,
    /// rồi tới ngày nghỉ hẳn (0 giờ); luôn ghép cặp ngày thiếu – ngày dư GẦN NHAU NHẤT trước; <paramref name="duTruoc"/> =
    /// giờ dư còn lại của tháng trước (coi như ngày cuối tháng trước). Ngày <c>Khoa</c> (đã sửa tay "Sau sang", nghỉ có phép) không cho / không nhận.
    /// Trả giờ sau sang từng ngày, danh sách lần sang và giờ dư còn lại (= tăng ca chưa dùng).
    /// </summary>
    public static (double[] Sau, List<Sang> Lan, double Con) SangCong(IReadOnlyList<(DateOnly Ngay, double Gio, bool Khoa)> days, double chuan, double duTruoc = 0)
    {
        var sau = days.Select(d => d.Gio).ToArray();
        var lan = new List<Sang>();
        if (days.Count == 0) return (sau, lan, Math.Max(0, duTruoc));
        // nguồn: (chỉ số ngày hoặc -1 = tháng trước, ngày, giờ còn)
        var nguon = new List<(int I, DateOnly Ngay, double Con)>();
        if (duTruoc > 1e-9) nguon.Add((-1, days[0].Ngay.AddDays(-1), duTruoc));
        for (var i = 0; i < days.Count; i++)
            if (!days[i].Khoa && days[i].Gio > chuan + 1e-9) nguon.Add((i, days[i].Ngay, days[i].Gio - chuan));
        var thieu = Enumerable.Range(0, days.Count).Where(i => !days[i].Khoa && days[i].Gio < chuan - 1e-9).ToList();
        // 2 lượt: ngày có đi làm nhưng thiếu giờ, rồi ngày nghỉ hẳn; mỗi bước ghép cặp (ngày thiếu, ngày dư) GẦN NHAU NHẤT
        // (bằng nhau → ngày thiếu sớm hơn, ngày dư trước ngày thiếu)
        foreach (var nhom in new[] { thieu.Where(i => days[i].Gio > 1e-9).ToList(), thieu.Where(i => days[i].Gio <= 1e-9).ToList() })
            while (true)
            {
                int bi = -1, bk = -1; long best = long.MaxValue;
                foreach (var i in nhom)
                {
                    if (chuan - sau[i] <= 1e-9) continue;
                    for (var k = 0; k < nguon.Count; k++)
                    {
                        if (nguon[k].Con <= 1e-9) continue;
                        long key = (long)Math.Abs(nguon[k].Ngay.DayNumber - days[i].Ngay.DayNumber) * 1_000_000 + (long)(days[i].Ngay.DayNumber % 100_000) * 2 + (nguon[k].Ngay > days[i].Ngay ? 1 : 0);
                        if (key < best) { best = key; bi = i; bk = k; }
                    }
                }
                if (bi < 0) break;
                var lay = Math.Min(chuan - sau[bi], nguon[bk].Con);
                nguon[bk] = nguon[bk] with { Con = nguon[bk].Con - lay };
                if (nguon[bk].I >= 0) sau[nguon[bk].I] -= lay;
                sau[bi] += lay;
                lan.Add(new(nguon[bk].I >= 0 ? nguon[bk].Ngay : null, days[bi].Ngay, Math.Round(lay, 2)));
            }
        lan.Sort((x, y) => x.Den != y.Den ? x.Den.CompareTo(y.Den) : Nullable.Compare(x.Tu, y.Tu));
        for (var i = 0; i < sau.Length; i++) sau[i] = Math.Round(sau[i], 2);
        return (sau, lan, Math.Round(nguon.Sum(n => n.Con), 2));
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
    /// <summary>Giờ 1 công (phút) — mốc sang công.</summary>
    public int GioChuan = 480;
    /// <summary>Tự sang công (giờ dư bù ngày thiếu trong tháng).</summary>
    public bool SangCong = true;
    /// <summary>Các tháng (yyyy-MM) lấy thêm giờ dư còn lại của tháng trước để bù.</summary>
    public HashSet<string> BuThangTruoc = new();
    /// <summary>Ngày lễ / Tết chung cả kho: ngày → (loại L / T, tên).</summary>
    public SortedDictionary<DateOnly, (string Loai, string Ten)> NgayLe = new();
    /// <summary>Loại ngày được tính đủ 1 công khi không có giờ chấm (mặc định lễ, Tết, công tác — nghỉ lễ / Tết hưởng lương).</summary>
    public HashSet<string> DuCongKhiNghi = new() { "L", "T", "CT" };

    public int NghiTruaOf(string nhom) => NghiTrua.TryGetValue(CcCore.NormNhom(nhom), out var v) ? v : 0;

    public JsonObject ToJson() => new()
    {
        ["phongBan"] = PhongBan, ["vaoTron"] = VaoTronGio, ["vaoNua"] = VaoNuaGio, ["raTron"] = RaTronGio, ["raNua"] = RaNuaGio,
        ["nghiTrua"] = new JsonObject(NghiTrua.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
        ["nuaNgay"] = NuaNgayToiDa, ["nghiToiTu"] = NghiToiTu, ["nghiToi"] = NghiToiPhut, ["vaoSomNhat"] = VaoSomNhat,
        ["gioChuan"] = GioChuan, ["sangCong"] = SangCong ? "1" : "0",
        ["ngayLe"] = new JsonArray(NgayLe.Select(p => (JsonNode)new JsonObject { ["ngay"] = p.Key.ToString("yyyy-MM-dd"), ["loai"] = p.Value.Loai, ["ten"] = p.Value.Ten }).ToArray()),
        ["duCong"] = new JsonArray(DuCongKhiNghi.OrderBy(x => x, StringComparer.Ordinal).Select(x => (JsonNode)x).ToArray()),
        ["buThangTruoc"] = new JsonArray(BuThangTruoc.OrderBy(x => x, StringComparer.Ordinal).Select(x => (JsonNode)x).ToArray()),
    };

    public static CcCauHinh From(JsonObject? o)
    {
        var c = new CcCauHinh();
        if (o is null) return c;
        int I(string k, int d) => o[k] is JsonValue v && v.TryGetValue<int>(out var x) ? x : d;
        c.PhongBan = o["phongBan"]?.ToString() ?? c.PhongBan;
        c.VaoTronGio = I("vaoTron", c.VaoTronGio); c.VaoNuaGio = I("vaoNua", c.VaoNuaGio); c.RaTronGio = I("raTron", c.RaTronGio); c.RaNuaGio = I("raNua", c.RaNuaGio);
        c.NuaNgayToiDa = I("nuaNgay", c.NuaNgayToiDa); c.NghiToiTu = I("nghiToiTu", c.NghiToiTu); c.NghiToiPhut = I("nghiToi", c.NghiToiPhut); c.VaoSomNhat = I("vaoSomNhat", c.VaoSomNhat);
        c.GioChuan = I("gioChuan", c.GioChuan); c.SangCong = o["sangCong"]?.ToString() != "0";
        if (o["buThangTruoc"] is JsonArray bt) foreach (var x in bt) if (x?.ToString() is { Length: 7 } k) c.BuThangTruoc.Add(k);
        if (o["ngayLe"] is JsonArray nl)
            foreach (var x in nl.OfType<JsonObject>())
                if (CcCore.ParseDate(x["ngay"]?.ToString()) is DateOnly d) c.NgayLe[d] = (CcCore.NormLoai(x["loai"]?.ToString()) is "T" ? "T" : "L", x["ten"]?.ToString() ?? "");
        if (o["duCong"] is JsonArray dc) { c.DuCongKhiNghi.Clear(); foreach (var x in dc) if (CcCore.NormLoai(x?.ToString()) is { Length: > 0 } l and not "?") c.DuCongKhiNghi.Add(l); }
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
    /// <summary>Giờ sau sang công sửa tay (ngày này không cho / nhận sang công tự động).</summary>
    public double? SauSang;
    /// <summary>Loại ngày đánh dấu tay (P / L / T / CT; null = theo ngày lễ / Tết chung; "" = ngày thường dù là ngày lễ chung).
    /// Người dùng 26/11: "nghỉ có phép thì không bù", "nghỉ lễ, nghỉ Tết, công tác".</summary>
    public string? Loai;
    public bool Empty => Vao is null && Ra is null && ChepTu is null && VSom is null && Chot is null && SauSang is null && Loai is null && GhiChu.Length == 0;
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
    public bool SuaVao, SuaRa, SuaVSom, SuaChot, SuaSang, SuaLoai;
    /// <summary>Loại ngày (P / L / T / CT / ""), tên ngày lễ nếu có; <see cref="DuCong"/> = tính đủ 1 công vì nghỉ lễ / Tết / công tác không có giờ chấm.</summary>
    public string Loai = "", TenLe = "";
    public bool DuCong;
    /// <summary>Giờ đi làm vào ngày lễ / Tết (tính riêng, không đem bù).</summary>
    public double GioLe;
    public bool Phep => Loai == "P";
    /// <summary>Giờ sau sang công (= <see cref="Chot"/> khi không sang).</summary>
    public double SauSang;
    /// <summary>Lần sang công nhận vào / cho đi của ngày này.</summary>
    public List<CcCore.Sang> Nhan = new(), Cho = new();
    public bool DaSua => SuaVao || SuaRa || SuaVSom || SuaChot || SuaSang || SuaLoai || ChepTu.Length > 0;
    public bool CoMat => Chot > 0;
}
