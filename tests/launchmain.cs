using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace BambuMultiLauncher
{
    /// <summary>测试桩：直接走产品里的 LaunchService.Launch，看它是否真能把 Bambu Studio 拉起来。</summary>
    internal static class LaunchMain
    {
        [STAThread]
        private static void Main(string[] args)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("version=" + AppPaths.Version);
            sb.AppendLine("config=" + AppPaths.ConfigDir);

            Settings settings = Settings.Load();
            Profile target = null;
            foreach (Profile p in settings.Profiles)
            {
                if (p.IsDefault) { target = p; break; }
            }
            if (target == null && settings.Profiles.Count > 0) { target = settings.Profiles[0]; }
            if (target == null)
            {
                sb.AppendLine("没有账号");
                Finish(sb);
                return;
            }

            sb.AppendLine("profile=" + target.Name + " default=" + target.IsDefault + " dir=" + target.Dir);
            sb.AppendLine("exe=" + settings.StudioExe);
            sb.AppendLine("extraargs.len=" + (settings.ExtraArgs == null ? -1 : settings.ExtraArgs.Length));
            if (!string.IsNullOrEmpty(settings.ExtraArgs))
            {
                StringBuilder codes = new StringBuilder();
                foreach (char c in settings.ExtraArgs) { codes.Append(((int)c).ToString() + " "); }
                sb.AppendLine("extraargs.codes=" + codes);
            }
            sb.AppendLine("bambumulti_home=" + (Environment.GetEnvironmentVariable("BAMBUMULTI_HOME") ?? "(null)"));

            LaunchResult r = LaunchService.Launch(settings, target);
            sb.AppendLine("status=" + r.Status + " pid=" + r.Pid);
            sb.AppendLine("message=" + r.Message);

            if (r.Status == LaunchStatus.Started && r.Pid > 0)
            {
                int waited = 0;
                bool alive = true;
                while (waited < 14000)
                {
                    Thread.Sleep(500);
                    waited += 500;
                    if (!LaunchService.IsAlive(r.Pid)) { alive = false; break; }
                }
                sb.AppendLine(alive
                    ? ("结果=14 秒后还活着（启动成功）")
                    : ("结果=进程在第 " + waited + " 毫秒退出了"));
                try
                {
                    Process p2 = Process.GetProcessById(r.Pid);
                    sb.AppendLine("exitcode=" + (p2.HasExited ? p2.ExitCode.ToString() : "still running"));
                }
                catch { }
                if (alive)
                {
                    try { Process.GetProcessById(r.Pid).Kill(); } catch { }
                }
            }

            Finish(sb);
        }

        private static void Finish(StringBuilder sb)
        {
            string here = Path.GetDirectoryName(typeof(LaunchMain).Assembly.Location);
            try
            {
                File.WriteAllText(Path.Combine(here, "applaunch-result.txt"), sb.ToString(), new UTF8Encoding(true));
            }
            catch { }
        }
    }
}
