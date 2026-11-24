@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem  编译自检用的假 Bambu Studio：tools\sleeper.cs -> tests\bambu-sleeper.exe
rem  自检 --selftest 需要把它放到自检目录的根，去重用例才会真正执行。
rem  用法：tools\make-sleeper.bat（在哪个目录执行都可以）
rem ============================================================

cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [错误] 找不到 .NET Framework 的 csc.exe。
    exit /b 1
)

if not exist "sleeper.cs" (
    echo [错误] 缺少 tools\sleeper.cs。
    exit /b 1
)

if not exist "..\tests" mkdir "..\tests"
"%CSC%" /nologo /target:exe /codepage:65001 /out:"..\tests\bambu-sleeper.exe" "sleeper.cs"
if errorlevel 1 (
    echo [错误] 编译 bambu-sleeper.exe 失败。
    exit /b 1
)

echo [成功] tests\bambu-sleeper.exe
