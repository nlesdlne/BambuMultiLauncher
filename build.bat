@echo off
chcp 65001 >nul
setlocal

rem ============================================================
rem  拓竹多账号启动器 - 一键编译
rem  只用 .NET Framework 自带的 csc.exe，不需要 Visual Studio
rem  用法：双击，或在命令行执行  build.bat
rem        build.bat /icon   顺带用 Python + Pillow 重新生成图标后再编译
rem ============================================================

cd /d "%~dp0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo [错误] 找不到 .NET Framework 的 csc.exe。
    echo        需要 Windows 10/11 自带的 .NET Framework 4.x。
    pause
    exit /b 1
)

set "OUTDIR=dist"
set "OUT=%OUTDIR%\拓竹多账号启动器.exe"
if not exist "%OUTDIR%" mkdir "%OUTDIR%"

rem ---- 图标：默认沿用 src\app.ico；缺失、或加了 /icon 时才重新生成 ----
rem 必须在编译之前：/win32icon 读的是当前 src\app.ico，放后面生成这次编译用不上。
set "MAKEICON="
if /i "%~1"=="/icon" set "MAKEICON=1"
if not exist "src\app.ico" set "MAKEICON=1"

if defined MAKEICON (
    if not exist "tools\make_icon.py" (
        echo [跳过] 没有 tools\make_icon.py，无法重新生成图标。
    ) else (
        call python -c "import PIL" >nul 2>nul
        if errorlevel 1 (
            echo [跳过] 没有 Python/Pillow，无法重新生成图标。
        ) else (
            echo 正在重新生成图标...
            call python "tools\make_icon.py"
            if errorlevel 1 (
                echo [警告] 图标重新生成失败，沿用现有图标（编译继续）。
            ) else (
                echo [成功] 图标：src\app.ico
            )
        )
    )
)

if not exist "src\app.ico" (
    echo.
    echo [错误] 缺少 src\app.ico，无法编译。
    echo        请装 Python + Pillow 后执行  build.bat /icon  自动生成。
    pause
    exit /b 1
)

echo 正在编译... 
echo   %CSC%

"%CSC%" /nologo /target:winexe /codepage:65001 /optimize+ /warn:4 ^
    /win32icon:src\app.ico ^
    /win32manifest:src\app.manifest ^
    /out:"%OUT%" ^
    src\*.cs ^
    /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll" ^
    /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Drawing.dll" ^
    /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Management.dll" ^
    /r:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Core.dll"

if errorlevel 1 (
    echo.
    echo [失败] 编译出错。
    pause
    exit /b 1
)

echo.
echo [成功] 生成：%OUT%
for %%F in ("%OUT%") do echo        大小：%%~zF 字节

rem ---- 可选：编译自检用的假 Bambu Studio ----
if exist "tools\sleeper.cs" (
    if not exist "%OUTDIR%\..\tests" mkdir "%OUTDIR%\..\tests"
    "%CSC%" /nologo /target:exe /codepage:65001 /out:tests\bambu-sleeper.exe tools\sleeper.cs >nul
    if errorlevel 1 (
        echo [提示] 自检桩编译失败（不影响主程序）。
    ) else (
        echo [成功] 自检桩：tests\bambu-sleeper.exe
    )
)

echo.
echo 完成。直接运行 dist\拓竹多账号启动器.exe
echo.
pause