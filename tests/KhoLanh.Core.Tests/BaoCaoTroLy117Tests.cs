using System.Net;
using System.Net.Sockets;
using KhoLanh.Core.Lan;
using Xunit;
using static KhoLanh.Core.TroLy.TroLyChecks;

namespace KhoLanh.Core.Tests;

/// <summary>1.117 — báo cáo Trợ lý 26/11 (app 1.115): lỗi đã sửa ở bản cũ không còn báo Nặng; cổng LAN bận báo rõ ai giữ.</summary>
public class BaoCaoTroLy117Tests
{
    private static readonly DateTime Now = new(2026, 11, 26, 11, 15, 0);

    private static string Block(string at, string ver, string tag, string type, string msg, string fn) =>
        $"{at} bản {ver}{(tag.Length > 0 ? " [" + tag + "]" : "")}\n{type}: {msg}\n   at {fn}()\n";

    [Fact]
    public void Loi_cu_chua_tai_phat_o_ban_moi_chi_la_thong_tin()
    {
        var combo = "The control ComboBox already has a visual parent WrapPanel while trying to add it as a child of WrapPanel.";
        var log = string.Concat(Enumerable.Range(0, 6).Select(i =>
                      Block($"2026-11-24 16:1{i}:00", "1.53.0", "", "System.InvalidOperationException", combo, "KhoLanh.App.Views.AnAnView.RenderRules")))
                + Block("2026-11-25 08:50:22", "1.73.0", "Tác vụ nền lỗi", "System.NullReferenceException", "Object reference not set to an instance of an object.", "KhoLanh.App.Views.TongQuanView.EmailDialog")
                // Lỗi cũ nhưng TÁI PHÁT ở bản đang chạy → giữ mức cũ.
                + string.Concat(Enumerable.Range(0, 5).Select(i =>
                      Block(i < 4 ? $"2026-11-25 0{i}:00:00" : "2026-11-26 09:00:00", i < 4 ? "1.95.0" : "1.117.0", "", "System.ArgumentOutOfRangeException", "Index was out of range.", "KhoLanh.App.Views.ViTriView.RenderDraft")))
                // Log cũ không ghi bản → giữ mức cũ.
                + "2026-11-25 10:00:00\nSystem.IO.IOException: disk full\n   at KhoLanh.Core.X.Y()\n";
        var errs = ParseErrorLog(log, Now);
        Assert.Equal("1.53.0", errs.Single(e => e.Msg.Contains("ComboBox")).LastVer);
        var f = CheckErrors(errs, "1.117.0");
        var c = f.Single(x => x.Title.Contains("ComboBox"));
        Assert.Equal("xanh", c.Sev);
        Assert.StartsWith("Lỗi cũ, chưa tái phát ở bản mới", c.Title);
        Assert.Contains("bản 1.53.0", c.Detail);
        Assert.Equal("xanh", f.Single(x => x.Title.Contains("Object reference")).Sev);
        var vt = f.Single(x => x.Title.Contains("Index was out of range"));
        Assert.Equal("do", vt.Sev);                                    // 5 lần, lần cuối ở bản 1.117 đang chạy
        Assert.StartsWith("Lỗi phần mềm:", vt.Title);
        Assert.Equal("vang", f.Single(x => x.Title.Contains("disk full")).Sev);
        // Mã phát hiện không đổi theo mức (hàng đợi giữ trạng thái cũ, tái phát thì báo lại).
        Assert.Equal(c.Id, CheckErrors(errs).Single(x => x.Title.Contains("ComboBox")).Id);
        Assert.Equal("do", CheckErrors(errs).Single(x => x.Title.Contains("ComboBox")).Sev);   // không biết bản đang chạy → như cũ
        Assert.True(FixedSince("1.9.0", "1.10.0"));                     // so theo số, không theo chữ
        Assert.False(FixedSince("1.117.0", "1.117.0"));
    }

    [Fact]
    public void Cong_LAN_bi_giu_bao_ro_ai_giu()
    {
        using var first = new TcpListener(IPAddress.Any, 0);
        first.Start();
        var port = ((IPEndPoint)first.LocalEndpoint).Port;
        var second = new TcpListener(IPAddress.Any, port);
        var e = Assert.Throws<SocketException>(() => second.Start());
        Assert.Equal(SocketError.AddressAlreadyInUse, e.SocketErrorCode);

        var other = LanService.PortBusyText(port, e, Array.Empty<string>());
        Assert.Contains($"cổng {port}", other);
        Assert.Contains("không phải KhoLanh", other);
        Assert.Contains("tự thử lại", other);
        var same = LanService.PortBusyText(port, e, new[] { "Một cửa sổ KhoLanh" });
        Assert.Contains("Một cửa sổ KhoLanh khác trên máy này đang giữ cổng", same);
        Assert.Contains("chờ 1 phút", same);
    }
}
