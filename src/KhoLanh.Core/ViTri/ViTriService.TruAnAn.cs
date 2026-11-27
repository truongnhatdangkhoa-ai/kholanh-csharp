using System.Text.Json;
using KhoLanh.Core.TonKho;

namespace KhoLanh.Core.ViTri;

/// <summary>1.165: nối phiếu vị trí → trừ nhanh Tồn kho An An (cộng gộp theo mã · đặc tính · hợp đồng; An An không có lô / vị trí).</summary>
public sealed partial class ViTriService
{
    private const string ExtraAnAn = "anAnSlip";

    /// <summary>Phiếu vị trí đã được trừ nhanh vào An An chưa → mã phiếu An An ("" nếu chưa).</summary>
    public static string AnAnSlipOf(Slip s) => s.Extra is { } e && e.TryGetValue(ExtraAnAn, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    /// <summary>Cộng gộp dòng các phiếu theo (mã 21 ký tự, đặc tính, hợp đồng); đặc tính / hợp đồng tách từ tên hàng "… - Đặc tính - Hợp đồng".</summary>
    public static List<AnAnService.VtLine> ToAnAnLines(IEnumerable<Slip> slips)
    {
        var g = new Dictionary<(string, string, string), double>();
        foreach (var l in slips.SelectMany(s => s.Lines))
        {
            var (dt, hd) = SplitName(l.Ten);
            var k = (ShortCode(l.MaGoc).ToUpperInvariant(), dt.Trim(), hd.Trim());
            g[k] = g.GetValueOrDefault(k) + l.Qty;
        }
        return g.Select(x => new AnAnService.VtLine(x.Key.Item1, x.Key.Item2, x.Key.Item3, Math.Round(x.Value, 4))).OrderBy(x => x.Ma).ToList();
    }

    /// <summary>Đánh dấu các phiếu đã trừ nhanh vào phiếu An An <paramref name="anAnSlipId"/> (chặn trừ trùng).</summary>
    public void MarkAnAn(IEnumerable<string> slipIds, string anAnSlipId)
    {
        RequireEdit();
        var set = slipIds.ToHashSet();
        foreach (var s in Slips.Where(x => set.Contains(x.Id)))
        {
            s.Extra ??= new();
            s.Extra[ExtraAnAn] = JsonSerializer.SerializeToElement(anAnSlipId);
        }
        Persist();
        Log("trừ nhanh tồn An An", $"{string.Join(", ", set)} → {anAnSlipId}");
    }
}
