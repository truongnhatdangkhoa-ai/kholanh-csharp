using System.Globalization;
using System.Text.Json.Nodes;
using KhoLanh.Core.Models;
using KhoLanh.Core.Services;
using KhoLanh.Core.ViTri;

namespace KhoLanh.Core.TonKho;

/// <summary>
/// 1.165 (người dùng: "trừ nhanh tồn kho để xuất file báo cáo nhanh, không làm thường xuyên"; khóa ghép = mã hàng + đặc tính + hợp đồng):
/// trừ tồn Tồn kho An An theo phiếu của Tồn theo vị trí. An An không có cột Lô / Vị trí nên chỉ trừ TỔNG theo (mã · đặc tính · hợp đồng).
/// Tạo 1 phiếu xuất An An (module "vitri") rồi trừ ngay — hủy bằng "Hủy trừ" ở tab Phiếu xuất đã tạo như phiếu thường.
/// </summary>
public sealed partial class AnAnService
{
    public const string VtModId = "vitri", VtModLabel = "Trừ nhanh từ phiếu vị trí";

    /// <summary>1 nhóm cần trừ: số lượng đã cộng gộp của mọi dòng phiếu cùng mã · đặc tính · hợp đồng.</summary>
    public sealed record VtLine(string Ma, string Dt, string Hd, double Qty);
    public sealed record TruRow(VtLine Src, string Status, double Ton, double Con, List<(int Row, double Qty)> Alloc)
    {
        public bool Ok => Status == "ok";
    }
    public sealed record TruResult(JsonObject? Slip, List<TruRow> Rows)
    {
        public int Done => Rows.Count(r => r.Ok);
        public int Skipped => Rows.Count(r => !r.Ok);
    }

    private static string Key(string? v) => System.Text.RegularExpressions.Regex.Replace((v ?? "").Trim().ToUpperInvariant(), @"\s+", " ");

    /// <summary>Xem trước: nhóm nào ghép được dòng An An (đủ tồn) / không ghép được / vượt tồn. Không ghi dữ liệu.</summary>
    public List<TruRow> PreviewTruNhanh(IEnumerable<VtLine> lines)
    {
        var res = new List<TruRow>();
        int cMa = D.Col("Mã hàng"), cDt = D.Col("Đặc tính"), cHd = D.Col("Hợp đồng"), cTon = D.Col("Tồn cuối");
        if (cMa < 0 || cTon < 0) throw new BusinessException("Load Data chưa có cột “Mã hàng” / “Tồn cuối” — nạp file Tồn kho An An trước.");
        var idx = new Dictionary<string, List<int>>();
        for (var i = 0; i < D.Rows.Count; i++)
        {
            var k = Key(ViTriService.ShortCode(D.Cell(i, cMa))) + "|" + Key(D.Cell(i, cDt)) + "|" + Key(D.Cell(i, cHd));
            if (!idx.TryGetValue(k, out var l)) idx[k] = l = new(); l.Add(i);
        }
        foreach (var g in lines.Where(x => x.Qty > 0))
        {
            var k = Key(g.Ma) + "|" + Key(g.Dt) + "|" + Key(g.Hd);
            if (!idx.TryGetValue(k, out var rows)) { res.Add(new TruRow(g, "khong-ghep", 0, 0, new())); continue; }
            double Ton(int i) { var v = JsText.LooseNum(D.Cell(i, cTon)); return double.IsNaN(v) ? 0 : v; }
            var total = rows.Sum(i => Math.Max(0, Ton(i)));
            if (total + 1e-9 < g.Qty) { res.Add(new TruRow(g, "vuot-ton", total, total - g.Qty, new())); continue; }
            var need = g.Qty; var alloc = new List<(int, double)>();
            foreach (var i in rows)
            {
                var t = Math.Max(0, Ton(i)); if (t <= 0) continue;
                var take = Math.Min(t, need); alloc.Add((i, take)); need -= take;
                if (need <= 1e-9) break;
            }
            res.Add(new TruRow(g, "ok", total, Math.Round(total - g.Qty, 4), alloc));
        }
        return res;
    }

    /// <summary>Tạo phiếu An An từ các nhóm ghép được và trừ tồn ngay. Nhóm không ghép / vượt tồn bị bỏ qua (có trong kết quả để báo).</summary>
    public TruResult TruNhanh(IEnumerable<VtLine> lines, IEnumerable<string> vtSlipIds)
    {
        RequireEdit();
        var prev = PreviewTruNhanh(lines);
        var byRow = new Dictionary<int, double>();
        foreach (var r in prev.Where(r => r.Ok)) foreach (var (row, q) in r.Alloc) byRow[row] = byRow.GetValueOrDefault(row) + q;
        if (byRow.Count == 0) return new TruResult(null, prev);
        var ids = vtSlipIds.ToList();
        var slip = CreateSlip(VtModId, byRow, VtModLabel, s => { s["nguon"] = "vitri"; s["vtSlips"] = new JsonArray(ids.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray()); });
        if (slip is null) return new TruResult(null, prev);
        DeductSlip(SlipId(slip));
        return new TruResult(slip, prev);
    }
}
