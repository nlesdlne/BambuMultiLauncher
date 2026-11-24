# 诊断试验程序

这些是排查"某些电脑上窗口开出来几秒就没了"时写的一次性小程序，保留下来方便以后复现。它们**不属于产品**，不会被 `build.bat` 编译，也不会打进发布包。

| 文件 | 作用 |
|---|---|
| `opentest.cs` | 对比三种"打开文件夹"的做法：`Process.Start(UseShellExecute)` / `explorer.exe` / `cmd /c start` |
| `launchtest.cs` | 用 6 种不同的 Win32 调用启动 `bambu-studio.exe`，看哪种能活下来（命令行参数是 `m1`…`m6`） |
| `shellopenmain.cs` | 直接调产品里的 `NativeMethods.ShellOpen`，验证封装本身没问题 |
| `shellopenmain2.cs` | 在同一个进程里先内联 `ShellExecuteEx`、再调产品封装，排除"封装差异" |
| `launchmain.cs` | 直接调产品的 `LaunchService.Launch`，用真实配置走完整启动链路 |

编译方式（在仓库根目录执行）：

```bat
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
"%FW%\csc.exe" /nologo /target:exe /codepage:65001 /out:tests\opentest.exe tests\opentest.cs
```

`launchmain.cs` 需要连产品源码一起编译（排除 `Program.cs`，因为那是入口）：

```bat
"%FW%\csc.exe" /nologo /target:exe /codepage:65001 /out:tests\applaunch.exe src\*.cs tests\launchmain.cs ^
  /r:"%FW%\System.Windows.Forms.dll" /r:"%FW%\System.Drawing.dll" /r:"%FW%\System.Management.dll" /r:"%FW%\System.Core.dll" ^
  /main:BambuMultiLauncher.LaunchMain
```

> 结论（详见 [docs/原理与限制.md](../docs/原理与限制.md)）：在部分电脑上 `CreateProcess` 拉起的 Bambu Studio 会以 `0xC0000409` 自己崩掉，而 `ShellExecuteEx` 不会，反过来也有。所以产品默认两种都试，并允许在高级设置里固定。
