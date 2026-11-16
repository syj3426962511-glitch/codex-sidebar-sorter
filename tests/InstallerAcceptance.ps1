param(
    [Parameter(Mandatory=$true)][string]$InstallerPath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
if ($env:GITHUB_ACTIONS -ne 'true') { throw 'Run installer acceptance in a disposable cloud runner.' }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$installDirectory = Join-Path $env:RUNNER_TEMP 'Sidebar 安装测试\Application With Spaces'
$program = Join-Path $installDirectory '聊天排序工具.exe'
$background = Join-Path $installDirectory '聊天排序后台.exe'
$registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{57DA4787-4BE0-43CB-9BFC-FE83679E370F}_is1'
$settingsDirectory = Join-Path $env:LOCALAPPDATA 'CodexSidebarSorter'
$settingsPath = Join-Path $settingsDirectory 'settings.json'
$report = [ordered]@{ success = $false; installDirectory = $installDirectory }

function Install-Candidate([string]$LogName) {
    $arguments = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /TASKS="" /DIR="' + $installDirectory + '" /LOG="' + (Join-Path $OutputDirectory $LogName) + '"'
    $process = Start-Process -FilePath $InstallerPath -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Installer exit code $($process.ExitCode)" }
}

function Invoke-Installed([string]$Arguments) {
    $process = Start-Process -FilePath $program -ArgumentList $Arguments -WindowStyle Hidden -PassThru -Wait
    if ($process.ExitCode -ne 0) { throw "Installed application exit code $($process.ExitCode)" }
}

try {
    if (Test-Path -LiteralPath $registryPath) { throw 'The disposable account already has this product installed.' }
    Install-Candidate 'install.log'
    if ((Get-Item -LiteralPath $program).VersionInfo.ProductVersion -notlike '1.3.0*') { throw 'Unexpected installed version.' }
    if (!(Test-Path -LiteralPath (Join-Path $installDirectory 'coreclr.dll'))) { throw 'The bundled runtime is missing.' }
    if (!(Test-Path -LiteralPath (Join-Path $installDirectory 'Source\SidebarSorter.csproj'))) { throw 'The source package is incomplete.' }
    $report.customUnicodeInstallPath = $true
    $report.bundledRuntimePresent = $true
    $report.sourceInstalled = $true
    $env:DOTNET_ROOT = Join-Path $env:RUNNER_TEMP 'no-system-dotnet'
    $env:DOTNET_MULTILEVEL_LOOKUP = '0'
    $env:CODEX_HOME = Join-Path $env:RUNNER_TEMP 'empty-cloud-client-data'
    New-Item -ItemType Directory -Path $env:CODEX_HOME,$settingsDirectory -Force | Out-Null
    '{"language":"zh-CN","projectSortMode":0,"chatSortMode":0}' | Set-Content -LiteralPath $settingsPath -Encoding utf8
    & (Join-Path $PSScriptRoot 'BackgroundLifecycle.ps1') -ProgramDirectory $installDirectory -OutputDirectory $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Installed background acceptance failed.' }
    $report.backgroundLifecyclePassed = $true
    $capture = Join-Path $OutputDirectory 'installed-with-codex-closed.png'
    Invoke-Installed ('--capture "' + $capture + '"')
    if (!(Test-Path -LiteralPath $capture)) { throw 'The installed application did not render.' }
    $report.runsWithoutSystemDotnet = $true
    '{"language":"en","projectSortMode":2,"chatSortMode":5}' | Set-Content -LiteralPath $settingsPath -Encoding utf8
    Invoke-Installed '--background'
    Start-Sleep -Seconds 1
    $originalBackground = Get-CimInstance Win32_Process -Filter "Name='聊天排序后台.exe'" | Where-Object ExecutablePath -EQ $background
    if (!$originalBackground) { throw 'The background was not running before the upgrade.' }
    Install-Candidate 'upgrade.log'
    if (Get-Process -Id $originalBackground.ProcessId -ErrorAction SilentlyContinue) { throw 'The old background is still running after the upgrade.' }
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    if ($settings.language -ne 'en' -or $settings.projectSortMode -ne 2 -or $settings.chatSortMode -ne 5) { throw 'The upgrade changed tool preferences.' }
    $report.upgradeStopsRunningTool = $true
    $report.upgradeRetainsLanguageAndSortPreferences = $true
    Invoke-Installed '--background'
    Start-Sleep -Seconds 1
    $uninstall = Start-Process -FilePath (Join-Path $installDirectory 'unins000.exe') -ArgumentList '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' -WindowStyle Hidden -PassThru -Wait
    if ($uninstall.ExitCode -ne 0) { throw "Uninstaller exit code $($uninstall.ExitCode)" }
    if ((Test-Path -LiteralPath $program) -or (Test-Path -LiteralPath $registryPath)) { throw 'Uninstall left the application or its registration.' }
    if (Get-CimInstance Win32_Process -Filter "Name='聊天排序后台.exe'" | Where-Object ExecutablePath -EQ $background) { throw 'Uninstall left the background running.' }
    if (!(Test-Path -LiteralPath $settingsPath)) { throw 'Uninstall removed the saved user preferences.' }
    $report.uninstallWhileBackgroundRunning = $true
    $report.userPreferencesRetained = $true
    $report.success = $true
}
catch { $report.error = $_.Exception.ToString() }
finally {
    $report.checkedAt = (Get-Date).ToUniversalTime().ToString('O')
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'installer-results.json') -Encoding utf8
}
$report | ConvertTo-Json -Depth 6
if (!$report.success) { exit 1 }
exit 0
