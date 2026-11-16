param([Parameter(Mandatory=$true)][string]$ProgramDirectory,[Parameter(Mandatory=$true)][string]$OutputDirectory)
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Run this acceptance script in a disposable cloud runner.' }
$ErrorActionPreference = 'Stop'
$native = @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class SorterWindowChecks {
    private delegate bool WindowCallback(IntPtr window, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(WindowCallback callback, IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr GetProp(IntPtr window, string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("kernel32.dll")] public static extern bool IsProcessInJob(IntPtr process, IntPtr job, out bool result);
    public static IntPtr Find(uint processId, string title) {
        IntPtr found = IntPtr.Zero;
        WindowCallback callback = (window, data) => {
            GetWindowThreadProcessId(window, out uint owner);
            if (owner != processId) return true;
            var text = new StringBuilder(128);
            GetWindowText(window, text, text.Capacity);
            if (text.ToString() != title) return true;
            found = window;
            return false;
        };
        EnumWindows(callback, IntPtr.Zero);
        return found;
    }
}
'@
Add-Type -TypeDefinition $native
$exe = Join-Path $ProgramDirectory '聊天排序工具.exe'
$hostExe = Join-Path $ProgramDirectory '聊天排序后台.exe'
$report = [ordered]@{ Success = $false }
$clientWindowIds = @(Get-Process -Name Codex,ChatGPT -ErrorAction SilentlyContinue | Where-Object MainWindowHandle -NE 0 | Select-Object -ExpandProperty Id)
$testHostId = 0
$testUiId = 0

function Invoke-Tool([string]$Argument) {
    $process = Start-Process -FilePath $exe -ArgumentList $Argument -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "工具入口退出码 $($process.ExitCode)" }
}

function Find-Host {
    Get-CimInstance Win32_Process -Filter "Name = '聊天排序后台.exe'" | Where-Object ExecutablePath -EQ $hostExe
}

function Find-Ui {
    Get-CimInstance Win32_Process -Filter "Name = '聊天排序工具.exe'" | Where-Object {
        $_.ExecutablePath -eq $exe -and $_.CommandLine -match '(?:^|\s)--ui(?:\s|$)'
    }
}

function Wait-Probe([scriptblock]$Probe, [int]$Seconds = 15) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $value = & $Probe
        if ($value) { return $value }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw '后台生命周期核验等待超时'
}

try {
    $report.Phase = 'background-performance'
    if (@(Find-Host).Count -gt 0) { throw '候选目录已有后台实例，需先核对其归属' }
    Invoke-Tool '--background'
    $hostInfo = Wait-Probe { Find-Host }
    $testHostId = $hostInfo.ProcessId
    $hostProcess = Get-Process -Id $testHostId
    $hostWindow = Wait-Probe { $handle = [SorterWindowChecks]::Find($testHostId,'聊天排序后台'); if($handle -ne [IntPtr]::Zero){$handle} }
    if ([SorterWindowChecks]::GetProp($hostWindow,'ChatSorterTrayAdded') -eq [IntPtr]::Zero) { throw '托盘图标创建未通过' }
    $inJob = $true
    if (![SorterWindowChecks]::IsProcessInJob($hostProcess.Handle,[IntPtr]::Zero,[ref]$inJob) -or $inJob) { throw '后台仍在宿主Windows Job中' }
    Invoke-Tool '--background'
    Invoke-Tool '--background'
    if (@(Find-Host).Count -ne 1 -or @(Find-Ui).Count -ne 0) { throw '后台启动的单实例或界面释放核验失败' }
    $hostProcess.Refresh()
    $cpuBefore = $hostProcess.TotalProcessorTime.TotalMilliseconds
    $sample = [Diagnostics.Stopwatch]::StartNew()
    Start-Sleep -Seconds 10
    $hostProcess.Refresh()
    $report.BackgroundProcessId = $testHostId
    $report.BackgroundWorkingSetBytes = $hostProcess.WorkingSet64
    $report.BackgroundPrivateBytes = $hostProcess.PrivateMemorySize64
    $report.SampleSeconds = $sample.Elapsed.TotalSeconds
    $report.CpuMilliseconds = $hostProcess.TotalProcessorTime.TotalMilliseconds - $cpuBefore
    $report.AverageCpuPercent = 100 * $report.CpuMilliseconds / ($sample.Elapsed.TotalMilliseconds * [Environment]::ProcessorCount)
    $report.TrayIconCreated = $true
    $report.ProcessInWindowsJob = $inJob
    $report.RepeatedBackgroundStartsUseOneInstance = $true
    $report.IdleManagedUiProcessCount = 0
    Invoke-Tool '--exit'
    if (!$hostProcess.WaitForExit(5000)) { throw '后台退出失败' }
    $testHostId = 0

    $environmentBlock = @()
    foreach ($name in @('SystemRoot','windir','SystemDrive','USERPROFILE','APPDATA','LOCALAPPDATA','TEMP','TMP','PATH','CODEX_HOME','ProgramFiles','ProgramData','COMSPEC')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($null -ne $value) { $environmentBlock += $name + '=' + $value }
    }
    $environmentBlock += 'CHAT_SORTER_TEST_HIDE_WINDOW=1'
    $report.Phase = 'hidden-normal-window'
    $startup = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ShowWindow=[uint16]0;WinstationDesktop='winsta0\default';EnvironmentVariables=[string[]]$environmentBlock}
    $launch = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine=('"'+$exe+'"');CurrentDirectory=$ProgramDirectory;ProcessStartupInformation=$startup}
    if ($launch.ReturnValue -ne 0) { throw '透明界面的正常入口启动失败' }
    $report.NormalLauncherProcessId = $launch.ProcessId
    $report.Phase = 'normal-host-start'
    $hostInfo = Wait-Probe { Find-Host }
    $testHostId = $hostInfo.ProcessId
    $report.NormalHostProcessId = $testHostId
    $report.Phase = 'normal-ui-start'
    $ui = Wait-Probe { Find-Ui }
    $testUiId = $ui.ProcessId
    $report.NormalUiProcessId = $testUiId
    $report.Phase = 'normal-window-handle'
    $window = Wait-Probe { $handle=[SorterWindowChecks]::Find($testUiId,'聊天排序工具'); if($handle -ne [IntPtr]::Zero){$handle} }
    $second = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine=('"'+$exe+'"');CurrentDirectory=$ProgramDirectory;ProcessStartupInformation=$startup}
    if ($second.ReturnValue -ne 0) { throw '重复正常入口启动失败' }
    Start-Sleep -Seconds 2
    if (@(Find-Host).Count -ne 1 -or @(Find-Ui).Count -ne 1) { throw '重复打开创建了额外实例' }
    $report.RepeatedNormalStartsReuseUi = $true
    [void][SorterWindowChecks]::PostMessage($window,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
    $uiProcess = Get-Process -Id $testUiId -ErrorAction SilentlyContinue
    if ($uiProcess -and !$uiProcess.WaitForExit(45000)) { throw '关闭窗口后界面进程仍未释放' }
    $testUiId = 0
    if (!(Get-Process -Id $testHostId -ErrorAction SilentlyContinue)) { throw '关闭窗口导致后台退出' }
    $report.WindowCloseReleasesUiAndKeepsBackground = $true
    $reopen = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{CommandLine=('"'+$exe+'"');CurrentDirectory=$ProgramDirectory;ProcessStartupInformation=$startup}
    if ($reopen.ReturnValue -ne 0) { throw '重开窗口入口失败' }
    $ui = Wait-Probe { Find-Ui }
    $testUiId = $ui.ProcessId
    $window = Wait-Probe { $handle=[SorterWindowChecks]::Find($testUiId,'聊天排序工具'); if($handle -ne [IntPtr]::Zero){$handle} }
    $report.WindowReopenSucceeded = $true
    Invoke-Tool '--exit'
    $hostProcess = Get-Process -Id $testHostId -ErrorAction SilentlyContinue
    if ($hostProcess -and !$hostProcess.WaitForExit(45000)) { throw '托盘退出流程未结束' }
    if (Get-Process -Id $testUiId -ErrorAction SilentlyContinue) { throw '退出后界面进程仍存在' }
    $testHostId = 0
    $testUiId = 0
    $report.TrayExitClosesAllToolProcesses = $true
    $report.Phase = 'complete'
    $report.ClientWindowProcessIds = $clientWindowIds
    $report.ClientWindowProcessesPreserved = @($clientWindowIds | Where-Object { !(Get-Process -Id $_ -ErrorAction SilentlyContinue) }).Count -eq 0
    if (!$report.ClientWindowProcessesPreserved) { throw '客户端原窗口进程发生变化' }
    $report.Success = $true
}
catch { $report.Error = $_.Exception.Message }
finally {
    foreach ($ownedId in @($testUiId,$testHostId)) {
        if($ownedId -gt 0) {Stop-Process -Id $ownedId -ErrorAction SilentlyContinue}
    }
    $report.CheckedAt = (Get-Date).ToString('O')
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'background-results.json') -Encoding utf8
}
Get-Content -LiteralPath (Join-Path $OutputDirectory 'background-results.json') -Raw
if(!$report.Success){exit 1}
