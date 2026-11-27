using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KhoLanh.Core.Modules;

namespace KhoLanh.Core.Lan;

/// <summary>
/// TÌNH TRẠNG HỆ THỐNG DÙNG CHUNG (1.179): máy chủ ghi một bản chụp "đã kết nối / đã cài chưa" của các mục Hệ thống (điện thoại, Gmail sao lưu,
/// Gmail lịch xuất, GitHub, Sheet tài khoản) vào database dùng chung <see cref="DbKey"/> sheet <see cref="SheetName"/> (1 dòng JSON, đi qua LAN / đường hầm Internet
/// như mọi module) để MÁY KHÁC xem được mà không cần có cấu hình đó trên máy mình. KHÔNG chứa token / mật khẩu — chỉ đã / chưa, giờ, lỗi.
/// </summary>
public sealed class HtTrangThai
{
    public string Id { get; set; } = "";
    public string Ten { get; set; } = "";
    /// <summary>Lúc máy chủ ghi (UTC, ISO).</summary>
    public string At { get; set; } = "";
    /// <summary>"" = đang chạy · "lich" = máy chủ báo sắp tắt theo lịch hẹn giờ · "thoat" = người dùng thoát app.</summary>
    public string Tat { get; set; } = "";
    /// <summary>Giờ hẹn tắt máy (UTC, ISO) — rỗng = không hẹn.</summary>
    public string HenTat { get; set; } = "";
    public string Ban { get; set; } = "";

    public bool DtNoi { get; set; }
    public string DtLoi { get; set; } = "";
    /// <summary>Chữ chu kỳ tự đồng bộ: "Tắt" / "Ngay khi có thay đổi" / "Mỗi 15 phút".</summary>
    public string DtAuto { get; set; } = "";
    public int DtBang { get; set; }
    public string DtLan { get; set; } = "";

    public bool DriveDn { get; set; }
    public string DriveMail { get; set; } = "";
    public string DriveLan { get; set; } = "";
    public string DriveLoi { get; set; } = "";
    public int DriveGio { get; set; }

    public bool SheetChung { get; set; }
    public bool MailLx { get; set; }
    public string MailLxTt { get; set; } = "";
    public bool GitHub { get; set; }

    public static readonly TimeSpan Heartbeat = TimeSpan.FromMinutes(5), Stale = TimeSpan.FromMinutes(15);

    /// <summary>Chữ ký không kể giờ ghi — để chỉ ghi khi nội dung đổi hoặc quá nhịp.</summary>
    public string Sig()
    {
        var c = (HtTrangThai)MemberwiseClone(); c.At = ""; return JsonSerializer.Serialize(c);
    }

    public DateTime? AtUtc => Parse(At);
    public DateTime? HenTatUtc => Parse(HenTat);
    public static DateTime? Parse(string s) => DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var d) ? d : null;
    public static string Iso(DateTime utc) => utc.ToString("o", CultureInfo.InvariantCulture);

    /// <summary>Máy chủ đang ở trạng thái nào lúc <paramref name="nowUtc"/> (theo đồng hồ máy xem).</summary>
    public enum May { Chay, TatTheoLich, DaThoat, KhongBao }

    public May Phan(DateTime nowUtc)
    {
        var at = AtUtc;
        if (at is null) return May.KhongBao;
        if (Tat.Length == 0 && nowUtc - at.Value <= Stale) return May.Chay;
        // quá nhịp: báo tắt theo lịch nếu máy chủ đã ghi dấu "lich", hoặc giờ hẹn tắt đã qua (kịp ghi hay không cũng vậy)
        if (Tat == "lich") return May.TatTheoLich;
        if (HenTatUtc is { } h && h <= nowUtc.AddMinutes(2) && at.Value <= h.AddMinutes(2)) return May.TatTheoLich;
        if (Tat == "thoat") return May.DaThoat;
        return nowUtc - at.Value <= Stale ? May.Chay : May.KhongBao;
    }

    public string ToJson() => JsonSerializer.Serialize(this);
    public static HtTrangThai? FromJson(string json) { try { return JsonSerializer.Deserialize<HtTrangThai>(json); } catch { return null; } }
}

/// <summary>Đọc / ghi bản chụp qua <see cref="ISheetSource"/> (LanService tự đi qua LAN / đường hầm).</summary>
public sealed class HeThongTrangThaiStore
{
    public const string DbKey = "he-thong", SheetName = "TrangThai";
    private readonly Func<ISheetSource> _src;
    public HeThongTrangThaiStore(Func<ISheetSource> src) { _src = src; }

    public HtTrangThai? Read()
    {
        try
        {
            if (JsonNode.Parse(_src().Read(DbKey, SheetName)) is not JsonArray a || a.Count == 0 || a[0] is not JsonObject o) return null;
            var c = o["data"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
            return c.StartsWith("__JSON__", StringComparison.Ordinal) ? HtTrangThai.FromJson(c[8..]) : null;
        }
        catch { return null; }
    }

    /// <summary>Ghi nếu chưa có / nội dung đổi / quá nhịp 5 phút. Trả về true khi đã ghi.</summary>
    public bool Write(HtTrangThai t)
    {
        var old = Read();
        var now = HtTrangThai.Parse(t.At) ?? DateTime.UtcNow;
        if (old is not null && old.Sig() == t.Sig() && old.AtUtc is { } oa && now >= oa && now - oa < HtTrangThai.Heartbeat) return false;
        var rows = new JsonArray { new JsonObject { ["data"] = "__JSON__" + t.ToJson() } };
        _src().Write(DbKey, new Dictionary<string, JsonArray?> { [SheetName] = rows });
        return true;
    }
}
