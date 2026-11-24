using System;
using System.Diagnostics;
using System.Threading;

/// <summary>
/// 自检用的假 Bambu Studio：接受任意参数，什么都不做，睡眠 N 毫秒后退出。
/// 编译出来叫 bambu-sleeper.exe 就是为了让进程名里带 bambu，
/// 这样启动器的“同一账号不重复启动”逻辑能被真实地测一遍。
/// </summary>
internal static class Sleeper
{
    private static int Main(string[] args)
    {
        int ms = 15000;
        foreach (string a in args)
        {
            if (a.StartsWith("--sleep=", StringComparison.OrdinalIgnoreCase))
            {
                int parsed;
                if (int.TryParse(a.Substring(8), out parsed)) { ms = parsed; }
            }
        }
        Thread.Sleep(ms);
        return 0;
    }
}
