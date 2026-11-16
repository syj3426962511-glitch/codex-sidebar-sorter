param([Parameter(Mandatory)][string]$OutputDirectory)

$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$environmentScript = Join-Path $visualStudio 'VC\Auxiliary\Build\vcvars64.bat'
$commands = @(
    '@echo off',
    ('call "' + $environmentScript + '" >nul'),
    'if errorlevel 1 exit /b %errorlevel%',
    ('cl.exe /nologo /O2 /MT /EHsc /std:c++17 /utf-8 /DUNICODE /D_UNICODE /LD "' + (Join-Path $PSScriptRoot 'windows.cpp') + '" /Fo"' + (Join-Path $OutputDirectory 'sorter_tray.obj') + '" /Fe"' + (Join-Path $OutputDirectory 'sorter_tray.dll') + '" /link user32.lib shell32.lib')
)
$commandFile = Join-Path $OutputDirectory 'build-tray.cmd'
Set-Content -LiteralPath $commandFile -Value $commands -Encoding utf8NoBOM
& $env:ComSpec /d /c $commandFile
if ($LASTEXITCODE -ne 0) { throw 'The native tray library build failed.' }
