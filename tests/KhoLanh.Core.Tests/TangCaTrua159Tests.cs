using KhoLanh.Core.ChamCong;
using Xunit;

namespace KhoLanh.Core.Tests;

/// <summary>1.159: tăng ca buổi trưa (người dùng: "thêm trường hợp tăng ca buổi trưa") — bớt nghỉ trưa, cộng vào công, được dùng sang công bù ngày thiếu.</summary>
public class TangCaTrua159Tests
{
    private static readonly CcCauHinh C = new();
    private static int M(string hm) => CcCore.ParseTime(hm)!.Value;

    [Fact]
    public void Tang_ca_trua_bot_nghi_trua_va_cong_vao_cong()
    {
        var thuong = CcCore.Tinh(M("07:30"), M("16:30"), "XUẤT", 0, C);
        var ca = CcCore.Tinh(M("07:30"), M("16:30"), "XUẤT", 0, C, CcCore.TcCaTrua);
        var nua = CcCore.Tinh(M("07:30"), M("16:30"), "XUẤT", 0, C, 0.5);
        var qua = CcCore.Tinh(M("07:30"), M("16:30"), "XUẤT", 0, C, 5);           // nhập quá → tối đa = nghỉ trưa của bộ phận
        var trua = C.NghiTruaOf("XUẤT");
        Assert.Equal(trua, thuong.Nghi - ca.Nghi);
        Assert.Equal(thuong.Cong + trua, ca.Cong);
        Assert.Equal(trua, ca.TcTrua);
        Assert.Equal(thuong.Cong + 30, nua.Cong);
        Assert.Equal(30, nua.TcTrua);
        Assert.Equal(ca.Cong, qua.Cong);
        // ngày nửa buổi không trừ nghỉ trưa → tăng ca trưa không cộng thêm gì
        var buoi = CcCore.Tinh(M("07:30"), M("11:30"), "XUẤT", 0, C, CcCore.TcCaTrua);
        Assert.Equal(CcCore.Tinh(M("07:30"), M("11:30"), "XUẤT", 0, C).Cong, buoi.Cong);
        Assert.Equal(0, buoi.TcTrua);
    }

    [Fact]
    public void Doc_o_tang_ca_trua()
    {
        Assert.Null(CcCore.ParseTcTrua(""));
        Assert.Equal(CcCore.TcCaTrua, CcCore.ParseTcTrua("x"));
        Assert.Equal(CcCore.TcCaTrua, CcCore.ParseTcTrua("Cả buổi"));
        Assert.Equal(1.5, CcCore.ParseTcTrua("1,5"));
        Assert.Equal(0.5, CcCore.ParseTcTrua("30p"));
        Assert.Equal(0.75, CcCore.ParseTcTrua("45 phút"));
        Assert.Throws<ArgumentException>(() => CcCore.ParseTcTrua("abc"));
        Assert.Equal("x", CcCore.TcText(CcCore.TcCaTrua));
        Assert.Equal("1.5", CcCore.TcText(1.5));
    }
}
