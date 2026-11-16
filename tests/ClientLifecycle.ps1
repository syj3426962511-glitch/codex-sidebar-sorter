param(
    [Parameter(Mandatory=$true)][string]$InstallerPath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Use a disposable cloud Windows runner.' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$installDirectory = Join-Path $env:RUNNER_TEMP 'Independent Sidebar Sorter'
$program = Join-Path $installDirectory '聊天排序工具.exe'
$background = Join-Path $installDirectory '聊天排序后台.exe'
$report = [ordered]@{ success = $false; accountState = 'fresh cloud profile'; authenticatedSortingVerified = $false }

Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ClientWindowCapture {
    [StructLayout(LayoutKind.Sequential)]
    public struct Bounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr window, out Bounds bounds);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
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

function Close-Client($Client) {
    if (!$Client.CloseMainWindow()) { throw 'Codex did not accept a normal window-close request.' }
    if (!$Client.WaitForExit(45000)) { throw 'Codex stayed running after normal window close.' }
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
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /TASKS="" /DIR="' + $installDirectory + '"'
    $setup = Start-Process -FilePath $InstallerPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($setup.ExitCode -ne 0) { throw 'The independent sorter installation failed.' }
    if (!(Get-Process -Id $originalClientId -ErrorAction SilentlyContinue)) { throw 'Installing the sorter closed Codex.' }
    $launch = Start-Process -FilePath $program -ArgumentList '--background' -WindowStyle Hidden -PassThru -Wait
    if ($launch.ExitCode -ne 0) { throw 'The independent background entry failed.' }
    $backend = Wait-Condition {
        Get-CimInstance Win32_Process -Filter "Name='聊天排序后台.exe'" | Where-Object ExecutablePath -EQ $background
    } 'The independent background did not start.'
    $report.installPreservesOpenClient = $true
    Save-ClientWindow $client 'official-client-with-sorter.png'
    Close-Client $client
    if (!(Get-Process -Id $backend.ProcessId -ErrorAction SilentlyContinue)) { throw 'Closing Codex also closed the independent background.' }
    Start-Sleep -Seconds 3
    if (Get-Process -Id $originalClientId -ErrorAction SilentlyContinue) { throw 'The closed Codex process reappeared.' }
    $report.normalClientCloseWithSorter = $true
    $report.sorterSurvivesClientExit = $true
    $client = Open-Client
    $report.normalClientReopenWithSorter = $true
    Close-Client $client
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
    $uninstall = Start-Process -FilePath (Join-Path $installDirectory 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -PassThru -Wait
    if ($uninstall.ExitCode -ne 0) { throw 'The sorter uninstaller failed.' }
    if (!(Get-Process -Id $clientIdBeforeUninstall -ErrorAction SilentlyContinue)) { throw 'Uninstalling the sorter closed Codex.' }
    $report.sorterUninstallPreservesOpenClient = $true
    Close-Client $client
    $client = Open-Client
    $report.normalClientStartsAfterSorterUninstall = $true
    Close-Client $client
    $report.success = $true
}
catch { $report.error = $_.Exception.ToString() }
finally {
    $report.checkedAt = (Get-Date).ToUniversalTime().ToString('O')
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'client-lifecycle-results.json') -Encoding utf8
}
$report | ConvertTo-Json -Depth 8
if (!$report.success) { exit 1 }
exit 0
