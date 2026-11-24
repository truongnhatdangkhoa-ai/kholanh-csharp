namespace KhoLanh.Core.Services;

/// <summary>
/// Script PowerShell của tiến trình phụ thay KhoLanh.exe khi cập nhật / quay lại bản trước (app đã thoát hẳn mới đổi file).
/// Tách khỏi AppUpdater để kiểm thử được. Chờ cửa sổ đang yêu cầu cập nhật thoát, rồi chờ MỌI tiến trình khác chạy cùng file exe thoát
/// (bản một-file đọc mã của chính nó từ exe — đổi file khi còn cửa sổ khác đang chạy sẽ làm cửa sổ đó lỗi "không tìm thấy thư viện").
/// </summary>
public static class UpdateScript
{
    public const int WaitSelfMs = 60000, WaitOthersSec = 30;

    public static string Build(string exe, int pid, bool rollback, string log)
    {
        static string Q(string x) => "'" + x.Replace("'", "''") + "'";
        var swap = rollback
            ? """
              Retry { Move-Item -LiteralPath $exe -Destination $doi -Force }
              try { Retry { Move-Item -LiteralPath $cu -Destination $exe -Force } } catch { Move-Item -LiteralPath $doi -Destination $exe -Force; throw }
              Retry { Move-Item -LiteralPath $doi -Destination $cu -Force }
              Remove-Item -LiteralPath $moi, ($moi + '.json') -Force -ErrorAction SilentlyContinue
              $arg = '--da-quay-lai'
              """
            : """
              Retry { if (Test-Path -LiteralPath $cu) { Remove-Item -LiteralPath $cu -Force } }
              Retry { Move-Item -LiteralPath $exe -Destination $cu -Force }
              try { Retry { Move-Item -LiteralPath $moi -Destination $exe -Force } } catch { Move-Item -LiteralPath $cu -Destination $exe -Force; throw }
              Remove-Item -LiteralPath ($moi + '.json') -Force -ErrorAction SilentlyContinue
              $arg = '--da-cap-nhat'
              """;
        return $$"""
            $ErrorActionPreference = 'Stop'
            $exe = {{Q(exe)}}; $moi = $exe + '.moi'; $cu = $exe + '.cu'; $doi = $exe + '.doi'; $log = {{Q(log)}}
            function Retry([scriptblock]$b) { for ($i = 0; $i -lt 30; $i++) { try { & $b; return } catch { Start-Sleep -Milliseconds 500 } }; & $b }
            function Others { @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne {{pid}} -and $(try { $_.Path -eq $exe } catch { $false }) }) }
            try {
              $p = Get-Process -Id {{pid}} -ErrorAction SilentlyContinue
              if ($p) { if (-not $p.WaitForExit({{WaitSelfMs}})) { throw 'KhoLanh.exe chưa thoát sau 60 giây' } }
              $han = (Get-Date).AddSeconds({{WaitOthersSec}})
              while ((Others).Count -gt 0) {
                if ((Get-Date) -gt $han) { throw ('Còn ' + (Others).Count + ' cửa sổ KhoLanh.exe khác đang mở — chưa thay file; đóng hết rồi cập nhật lại') }
                Start-Sleep -Milliseconds 500
              }
              Start-Sleep -Milliseconds 700
            {{swap}}
              Start-Process -FilePath $exe -ArgumentList $arg -WorkingDirectory (Split-Path -LiteralPath $exe)
            } catch {
              Add-Content -LiteralPath $log -Value ((Get-Date -Format 'yyyy-MM-dd HH:mm:ss') + ' ' + $_.ToString())
              if (Test-Path -LiteralPath $exe) { Start-Process -FilePath $exe -WorkingDirectory (Split-Path -LiteralPath $exe) }
            }
            """;
    }
}
