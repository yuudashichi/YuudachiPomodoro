param(
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$Version
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio C++ build tools to build the native launcher.' }
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$visualStudio) { throw 'Visual Studio C++ x64 build tools are required.' }
$build = Join-Path $projectRoot ('artifacts\launcher-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $build, $OutputDirectory | Out-Null
$icon = (Join-Path $projectRoot 'src\XiliPomodoro\Assets\XiliPomodoro.ico').Replace('\','/')
$versionTuple = $Version.Replace('.', ',') + ',0'
@"
#include <windows.h>
1 ICON "$icon"
1 VERSIONINFO
FILEVERSION $versionTuple
PRODUCTVERSION $versionTuple
FILEFLAGSMASK 0x3fL
FILEFLAGS 0x0L
FILEOS 0x40004L
FILETYPE 0x1L
BEGIN
  BLOCK "StringFileInfo"
  BEGIN
    BLOCK "040904b0"
    BEGIN
      VALUE "FileDescription", "惜立番茄钟"
      VALUE "FileVersion", "$Version"
      VALUE "ProductName", "惜立番茄钟"
      VALUE "ProductVersion", "$Version"
      VALUE "OriginalFilename", "惜立番茄钟.exe"
    END
  END
  BLOCK "VarFileInfo"
  BEGIN
    VALUE "Translation", 0x0409, 1200
  END
END
"@ | Set-Content -LiteralPath (Join-Path $build 'launcher.rc') -Encoding utf8
# The CRT is statically linked so the launcher needs no preinstalled runtime.
$commands = @"
@echo off
chcp 65001 >nul
call "$visualStudio\VC\Auxiliary\Build\vcvars64.bat" >nul
if errorlevel 1 exit /b 1
rc /nologo /c65001 /fo "$build\launcher.res" "$build\launcher.rc"
if errorlevel 1 exit /b 1
cl /nologo /utf-8 /std:c++17 /W4 /WX /O1 /MT /EHsc /Fo"$build\launcher.obj" /Fe"$OutputDirectory\惜立番茄钟.exe" "$projectRoot\src\Launcher\main.cpp" "$build\launcher.res" user32.lib /link /SUBSYSTEM:WINDOWS /MANIFEST:EMBED /MANIFESTINPUT:"$projectRoot\src\XiliPomodoro\app.manifest"
exit /b %errorlevel%
"@
[IO.File]::WriteAllText((Join-Path $build 'build.cmd'), ($commands -replace "`r?`n", "`r`n"), [Text.UTF8Encoding]::new($false))
& cmd.exe /d /c (Join-Path $build 'build.cmd')
if ($LASTEXITCODE -ne 0) { throw 'Native launcher build failed' }
