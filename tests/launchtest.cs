using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

internal static class LaunchTest
{
    private const string Exe = @"C:\Program Files\Bambu Studio\bambu-studio.exe";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHELLEXECUTEINFO
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
    private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetProcessId(IntPtr process);

    private static readonly StringBuilder Sb = new StringBuilder();

    [STAThread]
    private static void Main(string[] args)
    {
        string mode = args.Length > 0 ? args[0] : "m1";
        string data = args.Length > 1 ? args[1] : "";
        string extra = string.IsNullOrEmpty(data) ? "" : "--datadir \"" + data + "\"";

        Sb.AppendLine("mode=" + mode + " args=" + (extra.Length == 0 ? "(none)" : extra));
        Sb.AppendLine("cwd=" + Directory.GetCurrentDirectory());

        try
        {
            if (mode == "m1") { RunProcess(false, true, extra); }
            else if (mode == "m2") { RunProcess(false, false, extra); }
            else if (mode == "m3") { RunShell(extra); }
            else if (mode == "m4") { RunShellSimple(extra); }
            else if (mode == "m5") { RunProcessWithCwd(extra); }
            else if (mode == "m6") { RunShellExact(extra); }
        }
        catch (Exception ex)
        {
            Sb.AppendLine("throw " + ex.GetType().Name + ": " + ex.Message);
        }

        string here = Path.GetDirectoryName(typeof(LaunchTest).Assembly.Location);
        try { File.WriteAllText(Path.Combine(here, "launchtest-" + mode + ".txt"), Sb.ToString(), new UTF8Encoding(true)); } catch { }
    }

    private static void Watch(Process p, string label)
    {
        Sb.AppendLine(label + " pid=" + p.Id);
        int waited = 0;
        while (waited < 12000)
        {
            Thread.Sleep(500);
            waited += 500;
            try
            {
                if (p.HasExited)
                {
                    Sb.AppendLine("exited after " + waited + " ms, exitcode=" + p.ExitCode + " (0x" + p.ExitCode.ToString("X8") + ")");
                    return;
                }
            }
            catch { }
        }
        Sb.AppendLine("still alive after " + waited + " ms -> 看起来启动成功了");
        try { p.Kill(); } catch { }
    }

    private static void RunProcess(bool useShell, bool noWindow, string extra)
    {
        ProcessStartInfo psi = new ProcessStartInfo(Exe);
        psi.Arguments = extra;
        psi.UseShellExecute = useShell;
        psi.CreateNoWindow = noWindow;
        Process p = Process.Start(psi);
        Watch(p, "started");
    }

    private static void RunShell(string extra)
    {
        SHELLEXECUTEINFO sei = new SHELLEXECUTEINFO();
        sei.cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO));
        sei.fMask = 0x40 | 0x100 | 0x400;   // NOCLOSEPROCESS | NOASYNC | FLAG_NO_UI
        sei.lpVerb = "open";
        sei.lpFile = Exe;
        sei.lpParameters = extra;
        sei.lpDirectory = Path.GetDirectoryName(Exe);
        sei.nShow = 1;
        if (!ShellExecuteEx(ref sei))
        {
            Sb.AppendLine("ShellExecuteEx FAIL err=" + Marshal.GetLastWin32Error());
            return;
        }
        uint pid = GetProcessId(sei.hProcess);
        Sb.AppendLine("ShellExecuteEx ok pid=" + pid);
        Watch(Process.GetProcessById((int)pid), "started");
    }

    private static void RunShellSimple(string extra)
    {
        ProcessStartInfo psi = new ProcessStartInfo(Exe);
        psi.Arguments = extra;
        psi.UseShellExecute = true;
        psi.WorkingDirectory = Path.GetDirectoryName(Exe);
        Process p = Process.Start(psi);
        Watch(p, "started");
    }

    private static void RunProcessWithCwd(string extra)
    {
        ProcessStartInfo psi = new ProcessStartInfo(Exe);
        psi.Arguments = extra;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.WorkingDirectory = Path.GetDirectoryName(Exe);
        Process p = Process.Start(psi);
        Watch(p, "started");
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>和产品里 ShellOpen 完全一致的写法：空参数传 null，成功后关掉句柄。</summary>
    private static void RunShellExact(string extra)
    {
        SHELLEXECUTEINFO sei = new SHELLEXECUTEINFO();
        sei.cbSize = Marshal.SizeOf(typeof(SHELLEXECUTEINFO));
        sei.fMask = 0x40 | 0x100 | 0x400;
        sei.lpVerb = "open";
        sei.lpFile = Exe;
        sei.lpParameters = string.IsNullOrEmpty(extra) ? null : extra;
        sei.lpDirectory = Path.GetDirectoryName(Exe);
        sei.nShow = 1;
        if (!ShellExecuteEx(ref sei))
        {
            Sb.AppendLine("ShellExecuteEx FAIL err=" + Marshal.GetLastWin32Error());
            return;
        }
        uint pid = GetProcessId(sei.hProcess);
        if (sei.hProcess != IntPtr.Zero) { CloseHandle(sei.hProcess); }
        Sb.AppendLine("ShellExecuteEx ok pid=" + pid);
        Watch(Process.GetProcessById((int)pid), "started");
    }
}
