using System.Text.Json.Nodes;

namespace KhoLanh.Core.Lan;

/// <summary>
/// Gộp 3 chiều một bảng (mảng dòng JSON) khi hai nơi cùng sửa lúc không nối được với nhau:
/// gốc = bản lúc bắt đầu sửa riêng, máy con = bản của máy gửi lên, máy chủ = bản hiện tại trên máy chủ.
/// Mỗi dòng nhận diện bằng toàn bộ nội dung (sửa một dòng = bỏ dòng cũ + thêm dòng mới).
///  - Hai bên đổi các dòng KHÁC nhau → gộp: lấy bản máy chủ, bỏ các dòng máy con đã bỏ, thêm các dòng máy con đã thêm.
///  - Hai bên cùng đổi một dòng, hoặc một bên nạp lại cả bảng (bỏ hết dòng gốc), hoặc một bên xóa cả bảng → KHÔNG tự đoán (xung đột).
/// </summary>
public static class SheetMerge
{
    public record Result(bool Clean, string? Merged, string Note);

    public static Result ThreeWay(string? baseJson, string? mineJson, string? theirsJson)
    {
        // null = bảng bị xóa (hoặc chưa có).
        if (Same(mineJson, theirsJson)) return new(true, theirsJson, "hai bên giống nhau");
        if (Same(mineJson, baseJson)) return new(true, theirsJson, "máy con không đổi gì");
        if (Same(theirsJson, baseJson)) return new(true, mineJson, "máy chủ không đổi gì");
        if (mineJson is null) return new(false, null, "máy con xóa bảng trong khi máy chủ đã sửa");
        if (theirsJson is null) return new(false, null, "máy chủ đã xóa bảng trong khi máy con sửa");

        var b = Rows(baseJson); var m = Rows(mineJson); var t = Rows(theirsJson);
        var delM = Minus(b, m); var addM = Minus(m, b);
        var delT = Minus(b, t); var addT = Minus(t, b);
        if (Within(addM, addT) && Within(delM, delT)) return new(true, theirsJson, "máy chủ đã có sẵn các thay đổi này");
        // 1.120 (báo cáo Trợ lý 26/11: Tồn kho gửi / Tồn vị trí gần như mọi dòng ×2): chưa có bản chung (máy con chưa từng có bảng này khi
        // bắt đầu sửa riêng) mà hai bên đều có dữ liệu khác nhau = hai bên cùng NẠP / tạo bảng. Trước đây coi mọi dòng máy con là "thêm mới"
        // rồi nối vào bản máy chủ → dòng giống nhau giữa 2 file thành 2 lần. Nay giữ bản máy chủ, bản máy con vào Trùng sửa cho Quản trị chọn.
        if (b.Count == 0 && t.Count > 0 && m.Count > 0) return new(false, null, "hai bên cùng nạp / tạo bảng khi chưa có bản chung");
        if (b.Count > 0 && Total(delM) == b.Count) return new(false, null, "máy con nạp lại / thay cả bảng");
        if (b.Count > 0 && Total(delT) == b.Count) return new(false, null, "máy chủ nạp lại / thay cả bảng");
        foreach (var r in delM.Keys)
            if (delT.ContainsKey(r)) return new(false, null, "hai bên cùng sửa hoặc xóa một dòng");

        // Bản máy chủ, bỏ các dòng máy con đã bỏ.
        var result = new List<string>(t);
        foreach (var (row, n) in delM)
            for (var i = 0; i < n; i++)
            {
                var at = result.IndexOf(row);
                if (at >= 0) result.RemoveAt(at);
            }
        // Chèn dòng máy con thêm vào sau dòng đứng trước nó (theo thứ tự bên máy con).
        // 1.134: dòng hai bên CÙNG thêm (vd hai máy cùng nạp / cùng trừ ra đúng dòng đó) chỉ lấy một lần — bản máy chủ đã có,
        // chỉ chèn phần máy con thêm NHIỀU HƠN máy chủ. Trước đây chèn thêm cả phần trùng → dòng ×2.
        var pendingAdd = new Dictionary<string, int>();
        foreach (var (row, n) in addM) { var k = n - addT.GetValueOrDefault(row); if (k > 0) pendingAdd[row] = k; }
        var addedM = Total(pendingAdd);
        var anchor = -1;
        var used = new HashSet<int>();
        foreach (var row in m)
        {
            if (pendingAdd.TryGetValue(row, out var left) && left > 0)
            {
                pendingAdd[row] = left - 1;
                result.Insert(anchor + 1, row);
                // dời các chỉ số đã dùng
                var shifted = used.Select(i => i > anchor ? i + 1 : i).ToList();
                used = new HashSet<int>(shifted) { anchor + 1 };
                anchor++;
                continue;
            }
            for (var i = 0; i < result.Count; i++)
                if (!used.Contains(i) && result[i] == row) { used.Add(i); anchor = i; break; }
        }
        // 1.134: chốt chặn cuối — kết quả tự gộp to hơn hẳn cả hai bên (≥ 1,5 lần bên lớn hơn, từ 20 dòng) gần như chắc là hai bảng
        // khác nhau bị nối vào nhau (kiểu Tồn theo vị trí ×2) → không tự gộp, để Quản trị chọn ở Trùng sửa.
        if (result.Count >= 20 && result.Count > 1.5 * Math.Max(m.Count, t.Count))
            return new(false, null, $"kết quả gộp {result.Count} dòng lớn hơn hẳn cả hai bên ({m.Count} / {t.Count}) — nghi nhân đôi");
        var arr = new JsonArray(result.Select(x => JsonNode.Parse(x)).ToArray());
        return new(true, arr.ToJsonString(), $"tự gộp: máy con bỏ {Total(delM)} dòng, thêm {addedM} dòng");
    }

    private static bool Same(string? a, string? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a == b || Rows(a).SequenceEqual(Rows(b));
    }

    private static List<string> Rows(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        return JsonNode.Parse(json) is JsonArray a ? a.Select(n => n?.ToJsonString() ?? "null").ToList() : new();
    }

    private static Dictionary<string, int> Count(List<string> rows)
    {
        var d = new Dictionary<string, int>();
        foreach (var r in rows) d[r] = d.GetValueOrDefault(r) + 1;
        return d;
    }

    /// <summary>Phần của a không có trong b (đếm cả số lần lặp).</summary>
    private static Dictionary<string, int> Minus(List<string> a, List<string> b)
    {
        var ca = Count(a); var cb = Count(b);
        var d = new Dictionary<string, int>();
        foreach (var (r, n) in ca) { var k = n - cb.GetValueOrDefault(r); if (k > 0) d[r] = k; }
        return d;
    }

    private static bool Within(Dictionary<string, int> a, Dictionary<string, int> b) => a.All(p => b.GetValueOrDefault(p.Key) >= p.Value);

    private static int Total(Dictionary<string, int> d) => d.Values.Sum();
}
