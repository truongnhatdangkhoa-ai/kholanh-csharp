using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using KhoLanh.Core.Models;

namespace KhoLanh.App.Views;

public partial class VoucherDetailWindow : Window
{
    private readonly long _id;
    private Voucher _voucher = new();
    private List<VoucherLineView> _lines = new();
    public bool Changed { get; private set; }
    public (Voucher voucher, List<VoucherLineView> lines)? CopyRequest { get; private set; }

    public VoucherDetailWindow() : this(0) { }

    public VoucherDetailWindow(long id)
    {
        _id = id;
        InitializeComponent();
        if (id != 0) Load();
    }

    private void Load()
    {
        var (v, o, lines) = App.Services.Vouchers.Get(_id);
        _voucher = v; _lines = lines;
        Title = $"Phiếu {v.No}";
        HeadText.Text = $"Phiếu {VoucherTypes.Label(v.Type).ToLower()} {v.No}";
        InfoText.Text = $"Ngày {v.Date:dd/MM/yyyy} · Chủ hàng: {o.Name} · {(v.Type == VoucherTypes.In ? "Người giao" : "Người nhận")}: {v.Partner}"
                        + (v.Vehicle.Length > 0 ? $" · Xe: {v.Vehicle}" : "")
                        + (v.Note.Length > 0 ? $"\nGhi chú: {v.Note}" : "")
                        + $"\nLập bởi {v.CreatedBy} lúc {v.CreatedAt:dd/MM/yyyy HH:mm}";
        CancelText.Text = v.Cancelled ? $"ĐÃ HỦY: {v.CancelReason}" : "";
        TotalText.Text = $"{lines.Count} dòng · {Ui.Num(lines.Sum(l => l.Quantity))} đơn vị · {Ui.Num(lines.Sum(l => l.Weight))} kg";
        LinesGrid.ItemsSource = lines;
        CancelButton.IsVisible = !v.Cancelled && App.Services.Session.Can("cancel", Perm.Edit);
    }

    public static async Task<bool> Open(Visual owner, long id)
    {
        var w = new VoucherDetailWindow(id);
        await Ui.Show(owner, w);
        if (w.CopyRequest is { } req && MainWindow.Of((Control)owner) is { } main)
        {
            var view = new VoucherEditView(req.voucher.Type);
            view.Prefill(req.voucher, req.lines);
            main.Show(req.voucher.Type == VoucherTypes.In ? "in" : "out", view);
        }
        return w.Changed;
    }

    private async void OnCancel(object? sender, RoutedEventArgs e)
    {
        var reason = await Ui.Prompt(this, $"Hủy phiếu {_voucher.No}. Phiếu đã hủy không tính vào tồn nhưng vẫn lưu lại để tra cứu.\n\nLý do hủy:", "Hủy phiếu");
        if (reason is null) return;
        if (await Ui.Run(this, () => App.Services.Vouchers.Cancel(_id, reason)))
        {
            Changed = true;
            Load();
        }
    }

    private void OnCopy(object? sender, RoutedEventArgs e)
    {
        CopyRequest = (_voucher, _lines);
        Close();
    }

    private async void OnPrint(object? sender, RoutedEventArgs e) => await PrintHelper.Print(this, _id);
    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
