@echo off
rem ============================================================================
rem  InFalsusAdvVN - Thunderstore packaging wrapper
rem
rem  Runs build\package.ps1 with a per-process execution policy so the script
rem  also works on machines whose global policy is AllSigned/Restricted.
rem  Nothing is changed globally: the policy only applies to this process.
rem
rem  Usage:
rem     build\package.cmd
rem     build\package.cmd -SkipBuild -ExpectedTag v1.0.0
rem     build\package.cmd -Version 1.1.0
rem ============================================================================
setlocal

set "SCRIPT=%~dp0package.ps1"
if not exist "%SCRIPT%" (
    echo package.cmd: "%SCRIPT%" not found. 1>&2
    exit /b 1
)

where powershell.exe >nul 2>nul
if errorlevel 1 (
    echo package.cmd: powershell.exe not found on PATH. 1>&2
    exit /b 1
)

powershell.exe -NoProfile -NoLogo -ExecutionPolicy Bypass -File "%SCRIPT%" %*
exit /b %ERRORLEVEL%
