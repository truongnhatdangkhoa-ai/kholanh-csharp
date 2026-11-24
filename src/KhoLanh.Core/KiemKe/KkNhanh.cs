using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using static KhoLanh.Core.KiemKe.KiemKeCore;

namespace KhoLanh.Core.KiemKe;

/// <summary>
/// KIỂM KÊ NHANH — lõi (chép kk-nhanh-core.js của app cũ, PC 3.32), hàm thuần:
///  · DetectKind: nhận ra file nạp là Tồn kho An An / Kế toán / Tồn kho gửi theo TIÊU ĐỀ cột (không rõ → hỏi người dùng, không đoán);
///  · Summarize: đếm theo kết quả + kg lệch + số mã gõ sai ĐỦ CHẮC sửa tự động được;
///  · Todo: "Việc cần làm" (mỗi việc 1 nút) · FilterRows: lọc + tìm, chênh lớn nhất lên đầu.
/// Kiểm tra trước khi chốt dùng <see cref="KiemKeService.Readiness"/> của module.
/// </summary>
public static class KkNhanh
{
    public const string An = "an", Kt = "kt", Gui = "gui";
    public static readonly Dictionary<string, string> Kinds = new() { [An] = "Tồn kho An An", [Kt] = "Kế toán", [Gui] = "Tồn kho gửi" };

    public static string Strip(string? s)
    {
        var d = (s ?? "").Normalize(NormalizationForm.FormD); var sb = new StringBuilder(d.Length);
        foreach (var ch in d) if (ch < '̀' || ch > 'ͯ') sb.Append(ch);
        var t = sb.ToString().Replace('đ', 'd').Replace('Đ', 'd').ToLowerInvariant();
        return Regex.Replace(t, "[^a-z0-9]+", " ").Trim();
    }

    public sealed record Detect(string Kind, int Header, string Why);

    /// <summary>Bảng chữ (≤ 40 dòng đầu được xét) → loại file. Dòng tiêu đề = dòng đầu có ô "Mã hàng".</summary>
    public static Detect DetectKind(IReadOnlyList<IReadOnlyList<string?>>? aoa)
    {
        var h = -1; List<string> cells = new();
        for (var r = 0; r < Math.Min(aoa?.Count ?? 0, 40); r++)
        {
            var c = (aoa![r] ?? Array.Empty<string?>()).Select(Strip).ToList();
            if (c.Contains("ma hang") || c.Contains("mahang")) { h = r; cells = c; break; }
        }
        if (h < 0) return new("", -1, "không thấy dòng tiêu đề có cột \"Mã hàng\"");
        bool Has(string re) => cells.Any(c => Regex.IsMatch(c, re));
        var ton = Has("^ton cuoi|^ton kho|^ton$|^ton cuoi ky"); var tl = Has("^trong luong|^tlg|^tl$"); var net = Has("^net");
        var sl = Has("^so luong|^sl$|^so kien"); var vt = Has("^vi tri");
        var gui = Has("kho gui|^ma hang gui|ma 19|^ma cu") || (Has("^khach hang") && sl && !ton);
        if (ton) return new(An, h, "có cột Tồn cuối / Tồn kho");
        if (gui) return new(Gui, h, "có cột đặc trưng tồn kho gửi");
        if ((net || tl) && !vt) return new(Kt, h, "có Mã hàng + Net / Trọng lượng, không có Tồn cuối");
        return new("", h, "chưa chắc là loại nào");
    }

    /// <summary>Nhiều sheet: lấy sheet đầu nhận ra được; không sheet nào nhận ra → kết quả có dòng tiêu đề (hoặc sheet đầu).</summary>
    public static Detect DetectBook(IEnumerable<IReadOnlyList<IReadOnlyList<string?>>> sheets)
    {
        Detect? best = null;
        foreach (var s in sheets.Take(6))
        {
            var d = DetectKind(s); if (d.Kind.Length > 0) return d;
            if (best is null || (d.Header >= 0 && best.Header < 0)) best = d;
        }
        return best ?? new("", -1, "không đọc được nội dung");
    }

    public sealed record Fix(string Key, string Ma, string To);
    public sealed class Sum
    {
        public int Total, Khop, Thua, Thieu, LechKg, ChiKK, ChuaKK, Open, Auto, Nghi, ChuaRo, NoName; public double KgLech;
        public List<Fix> AutoList = new();
    }

    /// <summary>Hàng tham gia Kiểm kê nhanh: chỉ đối chiếu An An — bỏ mã chỉ có ở kho gửi / kế toán.</summary>
    public static List<Row> Rows(IEnumerable<Row> all) => all.Where(r => r.Status is not (ChiGui or ChiKT)).ToList();

    public static Sum Summarize(IEnumerable<Row> rows)
    {
        var S = new Sum(); double kg = 0;
        foreach (var r in rows)
        {
            S.Total++;
            switch (r.Status) { case Khop: S.Khop++; break; case Thua: S.Thua++; break; case Thieu: S.Thieu++; break; case LechKg: S.LechKg++; break; case ChiKK: S.ChiKK++; break; case ChuaKK: S.ChuaKK++; break; }
            if (Lech.Contains(r.Status))
            {
                S.Open++; kg += Math.Abs(r.DKg ?? 0);
                var n = r.Nn;
                if (n is not null)
                {
                    if (n.Code == "sai-ma" && n.Side == "kk" && !string.IsNullOrEmpty(n.FixTo) && n.Strong) { S.Auto++; S.AutoList.Add(new Fix(r.Key, r.Ma, n.FixTo)); }
                    if (n.Level == "ng") S.Nghi++; else if (n.Level == "cx") S.ChuaRo++;
                }
                else S.ChuaRo++;
            }
            if ((r.Ten ?? "").Trim().Length == 0 && r.Status != Khop) S.NoName++;
        }
        S.KgLech = Math.Round(kg * 100, MidpointRounding.AwayFromZero) / 100;
        return S;
    }

    public sealed record Ctx(bool HasKK, bool HasAn, bool HasKT, int Dirty, bool Locked, int NPages);
    public sealed record Todo(string Id, string Icon, string Tone, string Text, string Act, string Label);

    public static List<Todo> TodoList(Sum? S, Ctx c)
    {
        var L = new List<Todo>();
        if (!c.HasKK) L.Add(new("kk", "📝", "warn", "Chưa có số liệu kiểm kê — chọn trang kiểm kê ở tab \"Số liệu kiểm kê\" (hoặc nhập ở module Nhập liệu).", "goto-tong", "Mở Số liệu kiểm kê"));
        if (!c.HasAn) L.Add(new("an", "📦", "warn", "Chưa có Tồn kho An An để đối chiếu — chọn file tồn ở ô phía trên.", "pick", "Chọn file"));
        if (c.Dirty > 0) L.Add(new("save", "💾", "warn", c.Dirty + " trang đang sửa dở chưa lưu — lưu để số liệu không mất.", "save", "Lưu ngay"));
        if (S is null) return L;
        if (S.Auto > 0) L.Add(new("fix", "🪄", "ok", S.Auto + " mã kiểm kê gõ sai (đủ chắc) — app đổi theo mã An An, vào bản nháp, bạn xem lại rồi lưu.", "fix", "Sửa " + S.Auto + " mã"));
        if (S.ChiKK > 0) L.Add(new("chikk", "🔎", "bad", S.ChiKK + " mã chỉ có ở kiểm kê (không có trong tồn An An) — kiểm tra mã gõ sai hoặc hàng chưa nhập hệ thống.", "filter:" + ChiKK, "Xem"));
        if (S.ChuaKK > 0) L.Add(new("chuakk", "🚶", "bad", S.ChuaKK + " mã còn tồn nhưng chưa kiểm kê — có thể chưa đếm vị trí đó.", "filter:" + ChuaKK, "Xem"));
        if (S.Thua + S.Thieu + S.LechKg > 0) L.Add(new("lech", "⚖️", "bad", (S.Thua + S.Thieu + S.LechKg) + " mã lệch số lượng / kg — mở từng dòng để xem nguyên nhân và cách xử lý.", "filter:LECH", "Xem"));
        if (S.NoName > 0) L.Add(new("ten", "🏷️", "info", S.NoName + " mã chưa có tên hàng — cập nhật tên theo Danh sách mã hàng.", "ten", "Cập nhật tên"));
        return L;
    }

    private static readonly CompareInfo Ci = CultureInfo.InvariantCulture.CompareInfo;
    /// <summary>filt: "LECH" (mặc định, mọi dòng lệch) · "ALL" · "LECHSL" (Thừa / Thiếu / Lệch kg) · tên 1 kết quả. q: tìm trong mã / tên (không dấu, bỏ khoảng trắng).</summary>
    public static List<Row> FilterRows(IEnumerable<Row> rows, string? filt, string? q)
    {
        Func<Row, bool> f = filt switch
        {
            null or "" or "LECH" => r => Lech.Contains(r.Status),
            "ALL" => _ => true,
            "LECHSL" => r => r.Status is Thua or Thieu or LechKg,
            _ => r => r.Status == filt,
        };
        var k = Strip(q).Replace(" ", "");
        return rows.Where(r => f(r) && (k.Length == 0 || Strip(r.Ma).Replace(" ", "").Contains(k) || Strip(r.Ten).Replace(" ", "").Contains(k)))
            .OrderByDescending(r => Math.Abs(r.DKg ?? 0)).ThenBy(r => r.Ma, Comparer<string>.Create((a, b) => Ci.Compare(a, b, CompareOptions.None))).ToList();
    }
}
