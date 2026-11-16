param(
    [string]$InstallerPath,
    [string]$ProgramDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Use a disposable cloud Windows runner.' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$installDirectory = Join-Path $env:RUNNER_TEMP 'Independent Sidebar Sorter'
$program = Join-Path $installDirectory '聊天排序工具.exe'
$background = Join-Path $installDirectory '聊天排序后台.exe'
if ($ProgramDirectory) {
    $installDirectory = [IO.Path]::GetFullPath($ProgramDirectory)
    $program = Join-Path $installDirectory 'SidebarSorter.exe'
    $background = $program
}
$report = [ordered]@{ success = $false; accountState = 'fresh cloud profile'; authenticatedSortingVerified = $false }
$acceptanceStarted = Get-Date

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ClientWindowCapture {
    [StructLayout(LayoutKind.Sequential)]
    public struct Bounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Bounds bounds);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr window, int command);
}
'@

function Wait-Condition([scriptblock]$Probe, [string]$Failure, [int]$Seconds = 45) {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    do {
        $value = & $Probe
        if ($value) { return $value }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    throw $Failure
}

function Open-Client {
    Start-Process -FilePath (Join-Path $env:WINDIR 'explorer.exe') -ArgumentList ('shell:AppsFolder\' + $script:aumid) -WindowStyle Hidden | Out-Null
    $client = Wait-Condition {
        Get-Process -Name ChatGPT,Codex -ErrorAction SilentlyContinue | Where-Object {
            $_.Path -eq $script:clientExe -and $_.MainWindowHandle -ne 0
        } | Select-Object -First 1
    } 'The official Codex window did not open.'
    Start-Sleep -Seconds 5
    $client.Refresh()
    if ($client.HasExited -or !$client.Responding) { throw 'The official Codex window failed its startup check.' }
    return $client
}

function Close-ClientWindow($Client) {
    if (!$Client.CloseMainWindow()) { throw 'Codex did not accept a normal window-close request.' }
    [void](Wait-Condition { $Client.Refresh(); $Client.HasExited -or $Client.MainWindowHandle -eq 0 } 'The Codex window stayed open.')
}

function Quit-Client($Client) {
    [void][ClientWindowCapture]::ShowWindow($Client.MainWindowHandle,9)
    [void][ClientWindowCapture]::SetForegroundWindow($Client.MainWindowHandle)
    Start-Sleep -Milliseconds 300
    [Windows.Forms.SendKeys]::SendWait('^q')
    if ($Client.WaitForExit(15000)) { $report.nativeQuitShortcut = 'Ctrl+Q'; return }
    if ($Client.CloseMainWindow() -and $Client.WaitForExit(15000)) { $report.nativeQuitMethod = 'normal-window-close'; return }
    $root = [Windows.Automation.AutomationElement]::RootElement
    $buttons = [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty,[Windows.Automation.ControlType]::Button)
    $icon = @($root.FindAll([Windows.Automation.TreeScope]::Descendants,$buttons) | Where-Object { $_.Current.Name -match '^(?:Codex|ChatGPT)(?:\r?\n|$)' }) | Select-Object -First 1
    if (!$icon) {
        $overflow = $root.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Show hidden icons'))
        if ($overflow) {
            $overflow.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Milliseconds 500
        }
        $icon = @($root.FindAll([Windows.Automation.TreeScope]::Descendants,$buttons) | Where-Object { $_.Current.Name -match '^(?:Codex|ChatGPT)(?:\r?\n|$)' }) | Select-Object -First 1
    }
    if (!$icon) {
        $report.notificationAreaButtonNames = @($root.FindAll([Windows.Automation.TreeScope]::Descendants,$buttons) | ForEach-Object { $_.Current.Name } | Select-Object -Last 50)
        throw 'The native Codex notification-area icon was unavailable.'
    }
    $icon.SetFocus()
    [Windows.Forms.SendKeys]::SendWait('+{F10}')
    $quit = Wait-Condition {
        $root.FindFirst([Windows.Automation.TreeScope]::Descendants,[Windows.Automation.OrCondition]::new(
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Quit Codex'),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Quit'),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Exit'),
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Exit Codex')))
    } 'The native Codex Quit menu did not open.' 10
    $report.nativeQuitMenuLabel = $quit.Current.Name
    $quit.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    if (!$Client.WaitForExit(45000)) { throw 'Codex stayed running after its native Quit command.' }
}

function Save-ClientWindow($Client, [string]$Name) {
    $Client.Refresh()
    $bounds = [ClientWindowCapture+Bounds]::new()
    if (![ClientWindowCapture]::GetWindowRect($Client.MainWindowHandle,[ref]$bounds)) { throw 'The client window bounds were unavailable.' }
    $bitmap = [Drawing.Bitmap]::new($bounds.Right - $bounds.Left,$bounds.Bottom - $bounds.Top)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    $dc = $graphics.GetHdc()
    try {
        if (![ClientWindowCapture]::PrintWindow($Client.MainWindowHandle,$dc,2)) { throw 'The client window capture failed.' }
    } finally { $graphics.ReleaseHdc($dc); $graphics.Dispose() }
    try { $bitmap.Save((Join-Path $OutputDirectory $Name),[Drawing.Imaging.ImageFormat]::Png) }
    finally { $bitmap.Dispose() }
}

function Get-StandaloneBackground {
    $dataRoot = $env:CODEX_HOME
    if (!$dataRoot) { $dataRoot = [Environment]::GetEnvironmentVariable('CODEX_HOME','User') }
    if (!$dataRoot) { $dataRoot = Join-Path $env:USERPROFILE '.codex' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.Path]::GetFullPath($dataRoot).ToUpperInvariant())) }
    finally { $sha.Dispose() }
    $identity = 'sst-' + ([BitConverter]::ToString($hash).Replace('-','').Substring(0,12)) + '-bg'
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.', $identity, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $pipe.Connect(10000)
        $writer = [IO.StreamWriter]::new($pipe,[Text.UTF8Encoding]::new($false),1024,$true)
        $writer.AutoFlush = $true
        $writer.WriteLine('status')
        $reader = [IO.StreamReader]::new($pipe)
        try {
            $response = $reader.ReadLineAsync()
            if (!$response.Wait(10000)) { throw 'The standalone worker did not return its process identity.' }
            $status = $response.Result | ConvertFrom-Json
            return Get-CimInstance Win32_Process -Filter ('ProcessId=' + $status.ProcessId)
        } finally { $writer.Dispose(); $reader.Dispose() }
    } finally { $pipe.Dispose() }
}

try {
    $package = @(Get-AppxPackage -Name OpenAI.Codex)
    if ($package.Count -ne 1) { throw 'Expected one officially registered Codex package.' }
    $script:clientExe = Join-Path $package[0].InstallLocation 'app\ChatGPT.exe'
    [xml]$manifest = Get-Content -LiteralPath (Join-Path $package[0].InstallLocation 'AppxManifest.xml') -Raw
    $script:aumid = $package[0].PackageFamilyName + '!' + $manifest.Package.Applications.Application[0].Id
    $beforeFiles = @('app\ChatGPT.exe','app\resources\app.asar') | ForEach-Object {
        $file = Join-Path $package[0].InstallLocation $_
        [ordered]@{ relativePath = $_; hash = (Get-FileHash -LiteralPath $file).Hash }
    }
    $report.packageVersionBefore = [string]$package[0].Version
    $report.normalEntry = $script:aumid
    $client = Open-Client
    $originalClientId = $client.Id
    if ($InstallerPath) {
        $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /TASKS="" /DIR="' + $installDirectory + '"'
        $setup = Start-Process -FilePath $InstallerPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
        if ($setup.ExitCode -ne 0) { throw 'The independent sorter installation failed.' }
    }
    if (!$InstallerPath -and !$ProgramDirectory) { throw 'Supply an installer or a standalone candidate directory.' }
    if (!(Get-Process -Id $originalClientId -ErrorAction SilentlyContinue)) { throw 'Installing the sorter closed Codex.' }
    $launch = Start-Process -FilePath $program -ArgumentList '--background' -WindowStyle Hidden -PassThru
    Start-Sleep -Seconds 2
    if ($ProgramDirectory) { $backend = Get-StandaloneBackground }
    else {
        $backend = Wait-Condition {
            Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $background } | Select-Object -Last 1
        } 'The independent background did not start.'
    }
    if ($backend.ExecutablePath -ne $background) { throw 'The responding worker has an unexpected program path.' }
    $report.backgroundProcessId = $backend.ProcessId
    if ($InstallerPath) { $report.installPreservesOpenClient = $true }
    else { $report.sorterStartupPreservesOpenClient = $true }
    Save-ClientWindow $client 'official-client-with-sorter.png'
    Close-ClientWindow $client
    $report.normalClientWindowCloseWithSorter = $true
    $client = Open-Client
    $report.normalClientWindowReopenWithSorter = $true
    Quit-Client $client
    if (!(Get-Process -Id $backend.ProcessId -ErrorAction SilentlyContinue)) { throw 'Closing Codex also closed the independent background.' }
    Start-Sleep -Seconds 3
    if (Get-Process -Id $originalClientId -ErrorAction SilentlyContinue) { throw 'The closed Codex process reappeared.' }
    $report.normalClientCloseWithSorter = $true
    $report.sorterSurvivesClientExit = $true
    $client = Open-Client
    $report.normalClientReopenWithSorter = $true
    Quit-Client $client
    $winget = Get-Command winget.exe -ErrorAction Stop
    $updateOutput = & $winget.Source upgrade --id 9PLM9XGG6VKS --exact --source msstore --silent --accept-package-agreements --accept-source-agreements --disable-interactivity 2>&1
    $report.storeUpdateExitCode = $LASTEXITCODE
    $updateOutput | Set-Content -LiteralPath (Join-Path $OutputDirectory 'official-update.log') -Encoding utf8
    $afterPackage = @(Get-AppxPackage -Name OpenAI.Codex)
    if ($afterPackage.Count -ne 1) { throw 'The Codex registration disappeared after the Store update check.' }
    $report.packageVersionAfter = [string]$afterPackage[0].Version
    $report.crossVersionUpdateVerified = $report.packageVersionBefore -ne $report.packageVersionAfter
    $script:clientExe = Join-Path $afterPackage[0].InstallLocation 'app\ChatGPT.exe'
    $client = Open-Client
    $report.clientStartsAfterStoreUpdateCheck = $true
    Save-ClientWindow $client 'official-client-after-store-check.png'
    if (!$report.crossVersionUpdateVerified) {
        foreach ($file in $beforeFiles) {
            $afterHash = (Get-FileHash -LiteralPath (Join-Path $afterPackage[0].InstallLocation $file.relativePath)).Hash
            if ($afterHash -ne $file.hash) { throw 'A signed Codex application resource changed.' }
        }
        $report.clientExecutableAndResourcesUnchanged = $true
    }
    $clientIdBeforeUninstall = $client.Id
    if ($InstallerPath) {
        $uninstall = Start-Process -FilePath (Join-Path $installDirectory 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -PassThru -Wait
        if ($uninstall.ExitCode -ne 0) { throw 'The sorter uninstaller failed.' }
    } else {
        $stop = Start-Process -FilePath $program -ArgumentList '--exit' -WindowStyle Hidden -PassThru -Wait
        if ($stop.ExitCode -ne 0) { throw 'The standalone sorter did not exit.' }
        [void](Wait-Condition { !(Get-Process -Id $backend.ProcessId -ErrorAction SilentlyContinue) } 'The standalone background stayed running after Exit.')
        $report.standaloneCandidate = $true
    }
    if (!(Get-Process -Id $clientIdBeforeUninstall -ErrorAction SilentlyContinue)) { throw 'Uninstalling the sorter closed Codex.' }
    if ($InstallerPath) { $report.sorterUninstallPreservesOpenClient = $true }
    else { $report.sorterExitPreservesOpenClient = $true }
    Quit-Client $client
    $client = Open-Client
    if ($InstallerPath) { $report.normalClientStartsAfterSorterUninstall = $true }
    else { $report.normalClientStartsAfterSorterExit = $true }
    Quit-Client $client
    $report.success = $true
}
catch { $report.error = $_.Exception.ToString() }
finally {
    if (!$report.success -and $ProgramDirectory) {
        $report.sorterProcesses = @(Get-CimInstance Win32_Process | Where-Object ExecutablePath -EQ $program | Select-Object ProcessId,ParentProcessId,CommandLine)
        $report.sorterPipeNames = @(Get-ChildItem -LiteralPath '\\.\pipe\' -Filter 'sst-*' | Select-Object -ExpandProperty Name)
        $report.expectedDataRoot = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
        $report.recentRuntimeErrors = @(Get-WinEvent -FilterHashtable @{LogName='Application';ProviderName='.NET Runtime';StartTime=$acceptanceStarted} -ErrorAction SilentlyContinue | Where-Object Message -Match 'SidebarSorter' | Select-Object -First 5 -ExpandProperty Message)
    }
    $report.checkedAt = (Get-Date).ToUniversalTime().ToString('O')
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'client-lifecycle-results.json') -Encoding utf8
}
$report | ConvertTo-Json -Depth 8
if (!$report.success) { exit 1 }
exit 0
