param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$BuildDirectory
)

$ErrorActionPreference = 'Stop'
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$environmentScript = Join-Path $visualStudio 'VC\Auxiliary\Build\vcvars64.bat'
New-Item -ItemType Directory -Path $BuildDirectory,$OutputDirectory -Force | Out-Null
$commands = @(
    '@echo off',
    'chcp 65001 >nul',
    ('call "' + $environmentScript + '" >nul'),
    'if errorlevel 1 exit /b %errorlevel%',
    ('cd /d "' + $PSScriptRoot + '"'),
    ('rc.exe /nologo /fo "' + (Join-Path $BuildDirectory 'tray_host.res') + '" "tray_host.rc"'),
    'if errorlevel 1 exit /b %errorlevel%',
    ('cl.exe /nologo /O2 /MT /EHsc /std:c++17 /utf-8 /DUNICODE /D_UNICODE "tray_host.cpp" "' + (Join-Path $BuildDirectory 'tray_host.res') + '" /Fo"' + (Join-Path $BuildDirectory 'tray_host.obj') + '" /Fe"' + (Join-Path $OutputDirectory '聊天排序后台.exe') + '" /link /SUBSYSTEM:WINDOWS user32.lib shell32.lib gdi32.lib')
)
$commandFile = Join-Path $BuildDirectory '编译后台.cmd'
Set-Content -LiteralPath $commandFile -Value $commands -Encoding utf8NoBOM
& $env:ComSpec /d /c $commandFile
if ($LASTEXITCODE -ne 0) { throw "后台组件编译失败 $LASTEXITCODE" }
