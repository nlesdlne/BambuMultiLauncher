using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BambuMultiLauncher
{
    /// <summary>同一个进程里先跑"内联 ShellExecuteEx"，再跑产品的 NativeMethods.ShellOpen，直接对比差异。</summary>
    internal static class ShellOpenMain2
    {
        private const string Exe = @"C:\Program Files\Bambu Studio\bambu-studio.exe";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SEI
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public string lpVerb;
            public string lpFile;
            public string lpParameters;
            public string lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            public string lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ShellExecuteEx(ref SEI info);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetProcessId(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        private static readonly StringBuilder Sb = new StringBuilder();

        [STAThread]
        private static void Main(string[] args)
        {
            string dir = Path.GetDirectoryName(Exe);
            Sb.AppendLine("inline struct size=" + Marshal.SizeOf(typeof(SEI)));
            Sb.AppendLine("product struct size=" + Marshal.SizeOf(typeof(NativeMethods.SHELLEXECUTEINFO)));
            Sb.AppendLine("cwd=" + Environment.CurrentDirectory);

            SEI sei = new SEI();
            sei.cbSize = Marshal.SizeOf(typeof(SEI));
            sei.fMask = 0x40 | 0x100 | 0x400;
            sei.lpVerb = "open";
            sei.lpFile = Exe;
            sei.lpParameters = null;
            sei.lpDirectory = dir;
            sei.nShow = 1;
            if (!ShellExecuteEx(ref sei))
            {
                Sb.AppendLine("inline FAIL err=" + Marshal.GetLastWin32Error());
            }
            else
            {
                int pid = (int)GetProcessId(sei.hProcess);
                CloseHandle(sei.hProcess);
                Sb.AppendLine("inline ok pid=" + pid);
                Sb.AppendLine("inline " + Watch(pid));
            }

            Thread.Sleep(1000);

            int pid2;
            string error;
            bool ok = NativeMethods.ShellOpen(Exe, "", dir, out pid2, out error);
            Sb.AppendLine("product ok=" + ok + " pid=" + pid2 + " error=" + error);
            if (ok && pid2 > 0) { Sb.AppendLine("product " + Watch(pid2)); }

            string here = Path.GetDirectoryName(typeof(ShellOpenMain2).Assembly.Location);
            try { File.WriteAllText(Path.Combine(here, "shellopen2-result.txt"), Sb.ToString(), new UTF8Encoding(true)); } catch { }
        }

        private static string Watch(int pid)
        {
            int waited = 0;
            while (waited < 10000)
            {
                Thread.Sleep(500);
                waited += 500;
                if (!LaunchService.IsAlive(pid)) { return "exited after " + waited + " ms"; }
            }
            try { Process.GetProcessById(pid).Kill(); } catch { }
            return "alive after 10s (killed)";
        }
    }
}
