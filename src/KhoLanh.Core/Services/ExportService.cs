using System.Globalization;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using KhoLanh.Core.Models;

namespace KhoLanh.Core.Services;

/// <summary>Một cột khi xuất bảng ra Excel.</summary>
public record ExportColumn<T>(string Header, Func<T, object?> Value, string? NumberFormat = null);

/// <summary>Xuất Excel và tạo trang in phiếu (HTML mở bằng trình duyệt để in hoặc lưu PDF).</summary>
public class ExportService
{
    private static readonly CultureInfo Vi = CultureInfo.GetCultureInfo("vi-VN");
    public const string Qty = "#,##0.##";

    public void ExportTable<T>(string path, string sheetName, string title, IEnumerable<T> rows,
        IReadOnlyList<ExportColumn<T>> columns, string? subtitle = null)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName.Length > 31 ? sheetName[..31] : sheetName);
        var r = 1;
        ws.Cell(r, 1).Value = title;
        ws.Cell(r, 1).Style.Font.Bold = true;
        ws.Cell(r, 1).Style.Font.FontSize = 14;
        r++;
        if (!string.IsNullOrEmpty(subtitle)) { ws.Cell(r, 1).Value = subtitle; ws.Cell(r, 1).Style.Font.Italic = true; r++; }
        r++;
        var headerRow = r;
        for (var i = 0; i < columns.Count; i++) ws.Cell(r, i + 1).Value = columns[i].Header;
        var header = ws.Range(r, 1, r, columns.Count);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DCEAF7");
        foreach (var row in rows)
        {
            r++;
            for (var i = 0; i < columns.Count; i++)
            {
                var cell = ws.Cell(r, i + 1);
                var v = columns[i].Value(row);
                switch (v)
                {
                    case null: break;
                    case double d: cell.Value = d; break;
                    case int n: cell.Value = n; break;
                    case long n: cell.Value = n; break;
                    case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = "dd/MM/yyyy"; break;
                    default: cell.Value = v.ToString(); break;
                }
                if (columns[i].NumberFormat is { } fmt) cell.Style.NumberFormat.Format = fmt;
            }
        }
        var table = ws.Range(headerRow, 1, Math.Max(r, headerRow), columns.Count);
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.SheetView.FreezeRows(headerRow);
        ws.Columns().AdjustToContents(8, 60);
        wb.SaveAs(path);
    }

    /// <summary>Tạo file HTML phiếu nhập/xuất khổ A4 để in.</summary>
    public string BuildVoucherHtml(Voucher v, Owner owner, IReadOnlyList<VoucherLineView> lines,
        string companyName, string companyAddress, string warehouseName)
    {
        static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
        string N(double d) => d.ToString("#,##0.##", Vi);
        var isIn = v.Type == VoucherTypes.In;
        var sb = new StringBuilder();
        sb.Append($$"""
            <!doctype html><html lang="vi"><head><meta charset="utf-8"><title>{{H(v.No)}}</title>
            <style>
            @page { size: A4; margin: 14mm; }
            body { font-family: "Times New Roman", serif; font-size: 13pt; color: #000; }
            .head { display:flex; justify-content:space-between; }
            h1 { text-align:center; margin: 18px 0 2px; font-size: 20pt; }
            .sub { text-align:center; margin-bottom: 14px; }
            table { width:100%; border-collapse: collapse; margin-top: 10px; }
            th, td { border: 1px solid #000; padding: 4px 6px; }
            th { background: #eee; }
            td.n { text-align:right; }
            .info td { border:none; padding: 2px 0; }
            .sign { display:flex; justify-content:space-around; margin-top: 28px; text-align:center; }
            .sign div { width: 30%; } .sign i { font-size: 11pt; }
            .cancel { color:#b00; border:3px solid #b00; padding:4px 10px; display:inline-block; transform: rotate(-8deg); font-weight:bold; }
            @media print { .noprint { display:none; } }
            </style></head><body>
            <div class="noprint" style="margin-bottom:10px"><button onclick="window.print()">In phiếu</button></div>
            <div class="head"><div><b>{{H(companyName)}}</b><br>{{H(companyAddress)}}<br>{{H(warehouseName)}}</div>
            <div style="text-align:right">Số: <b>{{H(v.No)}}</b><br>Ngày lập: {{v.CreatedAt:dd/MM/yyyy HH:mm}}</div></div>
            <h1>PHIẾU {{(isIn ? "NHẬP KHO" : "XUẤT KHO")}}</h1>
            <div class="sub">Ngày {{v.Date:dd}} tháng {{v.Date:MM}} năm {{v.Date:yyyy}}</div>
            """);
        if (v.Cancelled) sb.Append($"<p><span class=\"cancel\">ĐÃ HỦY: {H(v.CancelReason)}</span></p>");
        sb.Append($"""
            <table class="info">
            <tr><td style="width:28%">Chủ hàng:</td><td><b>{H(owner.Name)}</b> ({H(owner.Code)})</td></tr>
            <tr><td>{(isIn ? "Người giao hàng" : "Người nhận hàng")}:</td><td>{H(v.Partner)}</td></tr>
            <tr><td>Số xe / container:</td><td>{H(v.Vehicle)}</td></tr>
            <tr><td>Ghi chú:</td><td>{H(v.Note)}</td></tr>
            </table>
            <table><thead><tr><th>STT</th><th>Mã hàng</th><th>Tên hàng</th><th>Size</th><th>Lô</th><th>Vị trí</th><th>ĐVT</th><th>Số lượng</th><th>Số kg</th><th>Ghi chú</th></tr></thead><tbody>
            """);
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            sb.Append($"<tr><td class=\"n\">{i + 1}</td><td>{H(l.ProductCode)}</td><td>{H(l.ProductName)}</td><td>{H(l.Size)}</td><td>{H(l.Lot)}</td><td>{H(l.Location)}</td><td>{H(l.Unit)}</td><td class=\"n\">{N(l.Quantity)}</td><td class=\"n\">{N(l.Weight)}</td><td>{H(l.Note)}</td></tr>");
        }
        sb.Append($"""
            <tr><th colspan="7" style="text-align:right">Tổng cộng</th><th class="n" style="text-align:right">{N(lines.Sum(l => l.Quantity))}</th><th style="text-align:right">{N(lines.Sum(l => l.Weight))}</th><th></th></tr>
            </tbody></table>
            <div class="sign">
              <div><b>Người lập phiếu</b><br><i>(Ký, ghi rõ họ tên)</i><br><br><br><br>{H(v.CreatedBy)}</div>
              <div><b>{(isIn ? "Người giao hàng" : "Người nhận hàng")}</b><br><i>(Ký, ghi rõ họ tên)</i></div>
              <div><b>Thủ kho</b><br><i>(Ký, ghi rõ họ tên)</i></div>
            </div>
            </body></html>
            """);
        return sb.ToString();
    }
}
