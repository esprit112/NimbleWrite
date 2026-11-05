# Samples the NimbleWrite desktop app's CPU and memory (and its server's) over a fixed window.
# Measures; changes nothing. Use it before and after performance work, with the same scenario each time.
#
#   powershell -ExecutionPolicy Bypass -File scripts\perf-sample.ps1 -Label "idle notepad"
#   powershell -ExecutionPolicy Bypass -File scripts\perf-sample.ps1 -ProcessPath desktop\bin\Release\net10.0-windows\NimbleWrite.exe -OutCsv perf.csv
param(
    # Which NimbleWrite.exe to measure (an installed copy or a development build). Default: whichever one is running.
    [string]$ProcessPath = '',
    [int]$Seconds = 20,
    [string]$Label = '',
    # Optional: append the result to this CSV file.
    [string]$OutCsv = ''
)
$ErrorActionPreference = 'Stop'

$apps = @(Get-Process -Name NimbleWrite -ErrorAction SilentlyContinue)
if ($ProcessPath) {
    $want = [System.IO.Path]::GetFullPath($ProcessPath)
    $apps = @($apps | Where-Object { $_.Path -and ([System.IO.Path]::GetFullPath($_.Path) -ieq $want) })
}
if ($apps.Count -eq 0) { throw 'NimbleWrite.exe is not running' + $(if ($ProcessPath) { " from $ProcessPath" } else { '' }) + '.' }
$app = $apps[0]

# The server is the node.exe this app started (its child process), if it started one.
$child = Get-CimInstance Win32_Process -Filter "ParentProcessId = $($app.Id) AND Name = 'node.exe'" -ErrorAction SilentlyContinue | Select-Object -First 1
$server = if ($child) { Get-Process -Id $child.ProcessId -ErrorAction SilentlyContinue } else { $null }

$a0 = $app.CPU
$s0 = if ($server) { $server.CPU } else { 0 }
Start-Sleep -Seconds $Seconds
$app.Refresh()
if ($server) { $server.Refresh() }

$appCpu = $app.CPU - $a0
$row = [pscustomobject]@{
    At          = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    Label       = $Label
    Version     = $app.MainModule.FileVersionInfo.ProductVersion
    Seconds     = $Seconds
    AppCpuSec   = [math]::Round($appCpu, 2)
    AppCpuPct   = [math]::Round($appCpu / $Seconds * 100, 2)  # % of one core
    AppWsMB     = [math]::Round($app.WorkingSet64 / 1MB, 0)
    AppPrivMB   = [math]::Round($app.PrivateMemorySize64 / 1MB, 0)
    AppThreads  = $app.Threads.Count
    AppHandles  = $app.HandleCount
    ServerCpuSec = if ($server) { [math]::Round($server.CPU - $s0, 2) } else { $null }
    ServerWsMB  = if ($server) { [math]::Round($server.WorkingSet64 / 1MB, 0) } else { $null }
}
$row | Format-List | Out-String -Width 200 | Write-Output
if ($OutCsv) { $row | Export-Csv -Path $OutCsv -Append -NoTypeInformation -Encoding UTF8 }
