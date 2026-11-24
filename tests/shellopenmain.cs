using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace BambuMultiLauncher
{
    /// <summary>测试桩：只调用产品里的 NativeMethods.ShellOpen，看问题是不是出在这个封装里。</summary>
    internal static class ShellOpenMain
    {
        [STAThread]
        private static void Main(string[] args)
        {
            StringBuilder sb = new StringBuilder();
            string exe = @"C:\Program Files\Bambu Studio\bambu-studio.exe";
            string args2 = args.Length > 0 ? args[0] : "";
            bool withWmi = args.Length > 1 && args[1] == "wmi";
            if (withWmi)
            {
                System.Collections.Generic.List<RunningInstance> snapshot = LaunchService.QueryInstances();
                sb.AppendLine("wmi snapshot=" + snapshot.Count);
            }
            int pid;
            string error;
            bool ok = NativeMethods.ShellOpen(exe, args2, Path.GetDirectoryName(exe), out pid, out error);
            sb.AppendLine("ok=" + ok + " pid=" + pid + " error=" + error);
            if (ok && pid > 0)
            {
                int waited = 0;
                bool alive = true;
                while (waited < 12000)
                {
                    Thread.Sleep(500);
                    waited += 500;
                    if (!LaunchService.IsAlive(pid)) { alive = false; break; }
                }
                sb.AppendLine(alive ? "alive after 12s" : ("exited after " + waited + " ms"));
                if (alive)
                {
                    try { Process.GetProcessById(pid).Kill(); } catch { }
                }
            }
            string here = Path.GetDirectoryName(typeof(ShellOpenMain).Assembly.Location);
            try { File.WriteAllText(Path.Combine(here, "shellopen-result.txt"), sb.ToString(), new UTF8Encoding(true)); } catch { }
        }
    }
}
