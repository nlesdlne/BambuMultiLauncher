using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace BambuMultiLauncher
{
    internal sealed class RunningInstance
    {
        internal int Pid;
        internal string DataDir = "";
        internal string CommandLine = "";
    }

    internal enum LaunchStatus
    {
        Started,
        AlreadyRunning,
        NoExe,
        NoDir,
        Failed
    }

    /// <summary>启动方式：不同版本/不同电脑上，能启动成功的那种可能不一样。</summary>
    internal enum LaunchMethod
    {
        /// <summary>先"双击"式（ShellExecuteEx），不行再常规式。</summary>
        Auto = 0,
        /// <summary>和双击快捷方式完全一样。</summary>
        Shell = 1,
        /// <summary>常规 CreateProcess，带命令行参数。</summary>
        Direct = 2
    }

    internal sealed class LaunchResult
    {
        internal LaunchStatus Status;
        internal string Message = "";
        internal int Pid;
        internal LaunchMethod Method = LaunchMethod.Auto;
        /// <summary>实际生效的启动方式（Auto 时用来告诉用户走的是哪一种）。</summary>
        internal LaunchMethod UsedMethod = LaunchMethod.Auto;
        /// <summary>启动前为了让它能起来而做过的修补说明（没有修补就是空）。</summary>
        internal string RepairNote = "";
    }

    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        internal const int SW_RESTORE = 9;
        internal const int SW_SHOWNORMAL = 1;

        // ---- ShellExecuteEx：用"双击"的方式启动，而不是注入式 CreateProcess ----

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct SHELLEXECUTEINFO
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

        internal const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
        internal const uint SEE_MASK_NOASYNC = 0x00000100;
        internal const uint SEE_MASK_FLAG_NO_UI = 0x00000400;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO info);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern uint GetProcessId(IntPtr processHandle);

        internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetExitCodeProcess(IntPtr processHandle, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CloseHandle(IntPtr handle);

        // ---- 找窗口、把它拉到前面 ----

        internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetClassName(IntPtr hWnd, StringBuilder buffer, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        internal static extern int GetWindowText(IntPtr hWnd, StringBuilder buffer, int maxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        /// <summary>用资源管理器的方式打开一个文件或文件夹，成功时通过 pid 带回进程号。</summary>
        internal static bool ShellOpen(string file, string arguments, string workingDir,
            out int pid, out string error)
        {
            pid = 0;
            error = "";
            try
            {
                NativeMethods.SHELLEXECUTEINFO info = new NativeMethods.SHELLEXECUTEINFO();
                info.cbSize = Marshal.SizeOf(typeof(NativeMethods.SHELLEXECUTEINFO));
                info.fMask = NativeMethods.SEE_MASK_NOCLOSEPROCESS
                           | NativeMethods.SEE_MASK_NOASYNC
                           | NativeMethods.SEE_MASK_FLAG_NO_UI;
                info.lpVerb = "open";
                info.lpFile = file;
                info.lpParameters = string.IsNullOrEmpty(arguments) ? null : arguments;
                info.lpDirectory = string.IsNullOrEmpty(workingDir) ? null : workingDir;
                info.nShow = NativeMethods.SW_SHOWNORMAL;

                if (!NativeMethods.ShellExecuteEx(ref info))
                {
                    int code = Marshal.GetLastWin32Error();
                    error = code == 1223
                        ? "被取消了。"
                        : new Win32Exception(code).Message + "（" + code + "）";
                    return false;
                }

                if (info.hProcess != IntPtr.Zero)
                {
                    pid = (int)NativeMethods.GetProcessId(info.hProcess);
                    NativeMethods.CloseHandle(info.hProcess);
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    internal static class LaunchService
    {
        private static readonly object Gate = new object();

        // ---------------------------------------------------------------- 进程

        internal static bool IsAlive(int pid)
        {
            if (pid <= 0) { return false; }
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    if (p.HasExited) { return false; }
                    return p.ProcessName.IndexOf("bambu", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch { return false; }
        }

        /// <summary>拿一个"不是自己启动"的进程的退出码（ShellExecuteEx 启动的进程只能这么取）。</summary>
        internal static bool TryGetExitCode(int pid, out int code)
        {
            code = 0;
            if (pid <= 0) { return false; }
            IntPtr handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero) { return false; }
            try
            {
                uint value;
                if (!NativeMethods.GetExitCodeProcess(handle, out value)) { return false; }
                if (value == 259) { return false; } // STILL_ACTIVE：还在跑
                code = unchecked((int)value);
                return true;
            }
            catch { return false; }
            finally { NativeMethods.CloseHandle(handle); }
        }

        private static string PidFile(Profile p)
        {
            return Path.Combine(AppPaths.StateDir, "p" + p.Id.ToString() + ".pid");
        }

        private static int ReadRecordedPid(Profile p)
        {
            try
            {
                string file = PidFile(p);
                if (!File.Exists(file)) { return 0; }
                string text = File.ReadAllText(file).Trim();
                int pid;
                if (int.TryParse(text, out pid)) { return pid; }
            }
            catch { }
            return 0;
        }

        private static void WriteRecordedPid(Profile p, int pid)
        {
            try
            {
                AppPaths.EnsureDir(AppPaths.StateDir);
                File.WriteAllText(PidFile(p), pid.ToString());
            }
            catch (Exception ex) { Log.Warn("写进程记录失败: " + ex.Message); }
        }

        /// <summary>通过 WMI 拿到所有正在跑的 Bambu Studio 及其 --datadir。取不到就返回空表。</summary>
        internal static List<RunningInstance> QueryInstances()
        {
            List<RunningInstance> list = new List<RunningInstance>();
            try
            {
                string query = "select ProcessId, CommandLine from Win32_Process where Name='bambu-studio.exe'";
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        try
                        {
                            RunningInstance ri = new RunningInstance();
                            ri.Pid = Convert.ToInt32(mo["ProcessId"]);
                            ri.CommandLine = Convert.ToString(mo["CommandLine"]);
                            ri.DataDir = ExtractDataDir(ri.CommandLine);
                            list.Add(ri);
                        }
                        catch { }
                        finally { mo.Dispose(); }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn("WMI 查询进程失败（不影响使用）: " + ex.Message);
            }
            return list;
        }

        internal static string ExtractDataDir(string commandLine)
        {
            if (string.IsNullOrEmpty(commandLine)) { return ""; }
            int idx = commandLine.IndexOf("--datadir", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) { return ""; }
            // 跳过参数名后面的空白；名字和值之间也允许写等号（--datadir=...）
            int at = idx + 9;
            while (at < commandLine.Length && (commandLine[at] == ' ' || commandLine[at] == '\t')) { at++; }
            if (at < commandLine.Length && commandLine[at] == '=') { at++; }
            while (at < commandLine.Length && (commandLine[at] == ' ' || commandLine[at] == '\t')) { at++; }
            if (at >= commandLine.Length) { return ""; }

            string rest = commandLine.Substring(at);
            if (rest[0] == '"')
            {
                int close = rest.IndexOf('"', 1);
                if (close > 1) { return rest.Substring(1, close - 1); }
                return "";
            }
            int space = rest.IndexOf(' ');
            if (space < 0) { return rest; }
            return rest.Substring(0, space);
        }

        private static bool SameDir(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) { return false; }
            try
            {
                a = Path.GetFullPath(a).TrimEnd('\\');
                b = Path.GetFullPath(b).TrimEnd('\\');
            }
            catch { }
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        internal static int GetRunningPid(Profile p)
        {
            lock (Gate)
            {
                return GetRunningPid(p, QueryInstances());
            }
        }

        /// <summary>用一次已经取好的进程快照判断，避免每次刷新都查一遍 WMI。</summary>
        internal static int GetRunningPid(Profile p, List<RunningInstance> snapshot)
        {
            lock (Gate)
            {
                int recorded = ReadRecordedPid(p);
                if (IsAlive(recorded)) { return recorded; }
                if (snapshot == null) { return 0; }
                foreach (RunningInstance ri in snapshot)
                {
                    if (p.IsDefault)
                    {
                        if (string.IsNullOrEmpty(ri.DataDir)) { return ri.Pid; }
                    }
                    else if (SameDir(ri.DataDir, p.Dir))
                    {
                        return ri.Pid;
                    }
                }
            }
            return 0;
        }

        internal static bool IsRunning(Profile p)
        {
            return GetRunningPid(p) > 0;
        }

        // ---------------------------------------------------------------- 启动

        internal static LaunchMethod MethodOf(Settings settings)
        {
            if (settings != null && settings.LaunchMethodValue == 1) { return LaunchMethod.Shell; }
            if (settings != null && settings.LaunchMethodValue == 2) { return LaunchMethod.Direct; }
            return LaunchMethod.Auto;
        }

        internal static LaunchResult Launch(Settings settings, Profile p)
        {
            return Launch(settings, p, MethodOf(settings));
        }

        internal static LaunchResult Launch(Settings settings, Profile p, LaunchMethod method)
        {
            LaunchResult result = new LaunchResult();
            result.Method = method;

            string exe = settings.StudioExe;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                exe = StudioLocator.Find(settings.StudioExe);
                if (string.IsNullOrEmpty(exe))
                {
                    result.Status = LaunchStatus.NoExe;
                    result.Message = "没找到 Bambu Studio 主程序，请到“高级设置”里指定 bambu-studio.exe 的位置。";
                    return result;
                }
                settings.StudioExe = exe;
            }

            if (!p.IsDefault && !Directory.Exists(p.Dir))
            {
                result.Status = LaunchStatus.NoDir;
                result.Message = "这个账号的数据目录不见了：\r\n" + p.Dir +
                    "\r\n\r\n可以在“高级设置”里换个目录，或者删掉这个账号重新添加一个。";
                return result;
            }

            if (settings.DedupeLaunch)
            {
                int running = GetRunningPid(p);
                if (running > 0)
                {
                    result.Status = LaunchStatus.AlreadyRunning;
                    result.Pid = running;
                    result.Message = "“" + p.Name + "”已经开着（PID " + running + "），不重复启动。";
                    BringToFront(running, 2);
                    return result;
                }
            }

            string args = "";
            if (!p.IsDefault)
            {
                args = "--datadir \"" + p.Dir + "\"";
            }
            if (!string.IsNullOrEmpty(settings.ExtraArgs))
            {
                args = (args.Length > 0 ? args + " " : "") + settings.ExtraArgs;
            }

            string workDir = "";
            try { workDir = Path.GetDirectoryName(exe); }
            catch { }

            // 老版本（02.07 及更早）的新目录默认 single_instance: true，
            // 新进程会把启动请求转给"不存在的老窗口"然后自己退出，看起来就是"启动不了"。
            // 每次启动前顺手把这一项确认成 false（幂等，改之前会备份）。
            if (!p.IsDefault)
            {
                try
                {
                    string repairNote;
                    if (ProfileRepair.EnsureSingleInstanceOff(p.Dir, out repairNote) && repairNote.Length > 0)
                    {
                        result.RepairNote = repairNote;
                    }
                    if (!HasPlugins(p.Dir))
                    {
                        string extra = "这个账号目录里还没有网络插件，第一次打开时会自动下载，可能要等几十秒。";
                        result.RepairNote = result.RepairNote.Length > 0
                            ? result.RepairNote + " " + extra
                            : extra;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn("启动前检查 single_instance 失败: " + ex.Message);
                }
            }

            // 首选：和双击快捷方式完全一样的方式（ShellExecuteEx）。
            // 直接 CreateProcess 会在某些版本上让 Bambu Studio 启动几秒后崩掉（0xC0000409），
            // 因为它继承了我们这个 GUI 进程的 std 句柄。
            bool tryShell = method != LaunchMethod.Direct;
            bool tryDirect = method != LaunchMethod.Shell;

            if (tryShell)
            {
                int shellPid;
                string shellError;
                if (NativeMethods.ShellOpen(exe, args, workDir, out shellPid, out shellError) && shellPid > 0)
                {
                    result.Pid = shellPid;
                    result.UsedMethod = LaunchMethod.Shell;
                    WriteRecordedPid(p, shellPid);
                    result.Status = LaunchStatus.Started;
                    result.Message = "已启动“" + p.Name + "”（PID " + shellPid + "）";
                    Log.Info("启动 " + p.Name + " -> " + exe + " " + args + " [" + shellPid + "] 方式=双击");
                    return result;
                }
                if (!string.IsNullOrEmpty(shellError))
                {
                    Log.Warn("ShellExecuteEx 启动失败，改用常规方式：" + shellError);
                }
            }

            if (!tryDirect)
            {
                result.Status = LaunchStatus.Failed;
                result.Message = "没能用“双击方式”启动 Bambu Studio。\r\n\r\n" +
                    "可以在“高级设置 → 启动方式”里换成“常规方式”再试一次。";
                return result;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = workDir;
                psi.Arguments = args;

                using (Process proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        result.Pid = proc.Id;
                        result.UsedMethod = LaunchMethod.Direct;
                        WriteRecordedPid(p, proc.Id);
                        result.Status = LaunchStatus.Started;
                        result.Message = "已启动“" + p.Name + "”（PID " + proc.Id + "）";
                        Log.Info("启动 " + p.Name + " -> " + exe + " " + args + " 方式=常规");
                    }
                    else
                    {
                        result.Status = LaunchStatus.Failed;
                        result.Message = "启动失败：进程没有创建成功。";
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("启动失败: " + p.Name, ex);
                result.Status = LaunchStatus.Failed;
                result.Message = "启动失败：" + ex.Message;
            }
            return result;
        }

        /// <summary>按给定参数直接启动一次（诊断用）。失败返回 null，并给出原因。</summary>
        internal static Process StartRaw(string exe, string args, out string error)
        {
            return StartRaw(exe, args, LaunchMethod.Auto, out error);
        }

        internal static Process StartRaw(string exe, string args, LaunchMethod method, out string error)
        {
            error = "";

            string workDir = "";
            try { workDir = Path.GetDirectoryName(exe); }
            catch { }

            bool tryShell = method != LaunchMethod.Direct;
            bool tryDirect = method != LaunchMethod.Shell;

            int pid;
            string shellError = "";
            if (tryShell &&
                NativeMethods.ShellOpen(exe, args == null ? "" : args, workDir, out pid, out shellError) && pid > 0)
            {
                try { return Process.GetProcessById(pid); }
                catch (Exception ex)
                {
                    error = "进程起来了但拿不到句柄：" + ex.Message;
                    return null;
                }
            }
            if (!string.IsNullOrEmpty(shellError))
            {
                Log.Warn("诊断启动（ShellExecuteEx）失败，改用常规方式：" + shellError);
            }
            if (!tryDirect)
            {
                error = "没能用“双击方式”启动：" + shellError;
                return null;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = workDir;
                psi.Arguments = args == null ? "" : args;
                Process proc = Process.Start(psi);
                if (proc == null)
                {
                    error = "进程没有创建成功";
                    return null;
                }
                return proc;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Log.Error("诊断启动失败: " + exe + " " + (args == null ? "" : args), ex);
                return null;
            }
        }

        /// <summary>
        /// 试一次把窗口拉到前台：窗口已经出现就拉起来并返回 true，还没出现就返回 false。
        /// 界面可以拿它做非阻塞轮询，避免"启动"按钮卡住十几秒。
        /// </summary>
        internal static bool TryFocusOnce(int pid)
        {
            try
            {
                using (Process p = Process.GetProcessById(pid))
                {
                    p.Refresh();
                    IntPtr handle = p.MainWindowHandle;
                    if (handle == IntPtr.Zero) { return false; }
                    NativeMethods.ShowWindow(handle, NativeMethods.SW_RESTORE);
                    NativeMethods.SetForegroundWindow(handle);
                    return true;
                }
            }
            catch { return false; }
        }

        /// <summary>把已有窗口拉到前台（尽力而为，失败不算错）。</summary>
        internal static void BringToFront(int pid, int waitSeconds)
        {
            try
            {
                DateTime deadline = DateTime.Now.AddSeconds(Math.Max(0, waitSeconds));
                while (DateTime.Now < deadline)
                {
                    if (TryFocusOnce(pid)) { return; }
                    Thread.Sleep(300);
                }
            }
            catch { }
        }

        // ---------------------------------------------------------------- 数据

        /// <summary>把现有安装里的插件/预设复制到新账号目录（不含任何登录信息）。</summary>
        internal static void CopySharedData(string sourceDir, string targetDir, Action<string> progress)
        {
            string[] items = new string[] { "plugins", "ota", "user" };
            AppPaths.EnsureDir(targetDir);
            foreach (string item in items)
            {
                string src = Path.Combine(sourceDir, item);
                string dst = Path.Combine(targetDir, item);
                try
                {
                    if (!Directory.Exists(src)) { continue; }
                    if (progress != null) { progress("正在复制 " + item + " ..."); }
                    CopyDirectory(src, dst);
                }
                catch (Exception ex)
                {
                    Log.Warn("复制 " + item + " 失败（可忽略）: " + ex.Message);
                }
            }
            // 保险：绝不带登录凭据
            try
            {
                string cred = Path.Combine(targetDir, "BambuNetworkEngine.conf");
                if (File.Exists(cred)) { File.Delete(cred); }
                string conf = Path.Combine(targetDir, "BambuStudio.conf");
                if (File.Exists(conf)) { File.Delete(conf); }
            }
            catch { }
        }

        private static void CopyDirectory(string source, string target)
        {
            AppPaths.EnsureDir(target);
            foreach (string file in Directory.GetFiles(source))
            {
                string name = Path.GetFileName(file);
                try { File.Copy(file, Path.Combine(target, name), true); }
                catch (Exception ex) { Log.Warn("复制文件失败 " + file + ": " + ex.Message); }
            }
            foreach (string dir in Directory.GetDirectories(source))
            {
                CopyDirectory(dir, Path.Combine(target, Path.GetFileName(dir)));
            }
        }

        internal static void OpenFolder(string path)
        {
            string error;
            OpenFolder(path, out error);
        }

        /// <summary>这个数据目录里有没有网络插件（没有的话第一次开窗口要联网下载，可能很久）。</summary>
        internal static bool HasPlugins(string dir)
        {
            try
            {
                if (string.IsNullOrEmpty(dir)) { return false; }
                string plugins = Path.Combine(dir, "plugins");
                if (!Directory.Exists(plugins)) { return false; }
                return Directory.GetFiles(plugins, "*bambu_networking*", SearchOption.AllDirectories).Length > 0
                    || Directory.GetFileSystemEntries(plugins).Length > 0;
            }
            catch { return false; }
        }

        /// <summary>
        /// 这个进程有没有可见的顶层窗口。用来区分"进程活着但窗口没出来"
        /// （卡在插件下载/初始化）和"进程真的起来了"。
        /// </summary>
        internal static bool HasWindow(int pid, out string title)
        {
            title = "";
            if (pid <= 0) { return false; }
            string found = "";
            try
            {
                NativeMethods.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                {
                    uint owner;
                    NativeMethods.GetWindowThreadProcessId(hWnd, out owner);
                    if ((int)owner != pid) { return true; }
                    if (!NativeMethods.IsWindowVisible(hWnd)) { return true; }
                    StringBuilder text = new StringBuilder(512);
                    NativeMethods.GetWindowText(hWnd, text, text.Capacity);
                    if (text.Length > 0)
                    {
                        found = text.ToString();
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Log.Warn("枚举窗口失败: " + ex.Message);
            }
            title = found;
            return found.Length > 0;
        }

        /// <summary>
        /// 打开一个文件夹。三条路依次试（shell 打开 / explorer 参数 / cmd start），
        /// 都失败就把原因交回调用方，让界面上能看到，而不是静默什么都不发生。
        /// </summary>
        internal static bool OpenFolder(string path, out string error)
        {
            error = "";
            if (string.IsNullOrEmpty(path))
            {
                error = "路径是空的。";
                return false;
            }
            try
            {
                if (!Directory.Exists(path)) { Directory.CreateDirectory(path); }
            }
            catch (Exception ex)
            {
                error = "目录不存在，而且没法创建：" + ex.Message;
                Log.Error("准备目录失败: " + path, ex);
                return false;
            }

            Exception last = null;
            int pid;
            string shellError;

            // 方式 1：ShellExecuteEx（和双击文件夹一样，最稳，也最少被策略拦）
            if (NativeMethods.ShellOpen(path, "", path, out pid, out shellError))
            {
                ActivateFolderWindow(path, 1500);
                return true;
            }
            if (!string.IsNullOrEmpty(shellError))
            {
                Log.Warn("打开目录（方式1 ShellExecuteEx）失败: " + shellError);
                last = new InvalidOperationException(shellError);
            }

            // 方式 2：explorer.exe 带路径
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("explorer.exe", "\"" + path + "\"");
                psi.UseShellExecute = true;
                Process.Start(psi);
                ActivateFolderWindow(path, 1500);
                return true;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warn("打开目录（方式2）失败: " + ex.Message);
            }

            // 方式 3：cmd 的 start
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c start \"\" \"" + path + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
                ActivateFolderWindow(path, 1500);
                return true;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warn("打开目录（方式3）失败: " + ex.Message);
            }

            error = last == null ? "系统没有响应打开请求。" : last.Message;
            Log.Error("打开目录全部失败: " + path, last);
            return false;
        }

        /// <summary>
        /// 把刚打开的资源管理器窗口换到前台。
        /// 我们的窗口是模态对话框，explorer 新窗口经常生成在它后面，用户会以为"点了没反应"。
        /// </summary>
        internal static bool ActivateFolderWindow(string folder, int waitMilliseconds)
        {
            string leaf = "";
            try { leaf = Path.GetFileName(folder.TrimEnd('\\', '/')); }
            catch { }
            if (leaf.Length == 0) { leaf = folder; }

            int waited = 0;
            while (waited < waitMilliseconds)
            {
                IntPtr hit = FindFolderWindow(leaf);
                if (hit != IntPtr.Zero)
                {
                    NativeMethods.ShowWindow(hit, NativeMethods.SW_RESTORE);
                    NativeMethods.SetForegroundWindow(hit);
                    return true;
                }
                Thread.Sleep(100);
                waited += 100;
            }
            return false;
        }

        private static IntPtr FindFolderWindow(string leaf)
        {
            IntPtr found = IntPtr.Zero;
            try
            {
                NativeMethods.EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
                {
                    try
                    {
                        if (!NativeMethods.IsWindowVisible(hWnd)) { return true; }
                        StringBuilder cls = new StringBuilder(64);
                        NativeMethods.GetClassName(hWnd, cls, cls.Capacity);
                        if (!string.Equals(cls.ToString(), "CabinetWClass", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                        StringBuilder title = new StringBuilder(512);
                        NativeMethods.GetWindowText(hWnd, title, title.Capacity);
                        if (title.ToString().IndexOf(leaf, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            found = hWnd;
                            return false;
                        }
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
            return found;
        }

        /// <summary>安全的名字 -> 目录名。</summary>
        internal static string SafeFolderName(string name)
        {
            if (string.IsNullOrEmpty(name)) { return "profile"; }
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in name)
            {
                bool bad = false;
                foreach (char ic in invalid) { if (c == ic) { bad = true; break; } }
                if (bad || c == ' ') { sb.Append('_'); } else { sb.Append(c); }
            }
            // 只去掉首尾的空格和点：尾部的下划线是用户名字的一部分，别削掉
            string s = sb.ToString().Trim(' ', '.');
            if (s.Length == 0) { s = "profile"; }
            if (IsReservedName(s)) { s = "_" + s; }
            if (s.Length > 40) { s = s.Substring(0, 40); }
            return s;
        }

        /// <summary>Windows 保留设备名（CON、NUL、COM1…）不能当文件/目录名。</summary>
        private static bool IsReservedName(string name)
        {
            string bare = name;
            int dot = bare.IndexOf('.');
            if (dot >= 0) { bare = bare.Substring(0, dot); }
            bare = bare.Trim().ToUpperInvariant();
            if (bare.Length == 0) { return false; }
            if (bare == "CON" || bare == "PRN" || bare == "AUX" || bare == "NUL") { return true; }
            if (bare.Length == 4 &&
                (bare.StartsWith("COM", StringComparison.Ordinal) || bare.StartsWith("LPT", StringComparison.Ordinal)))
            {
                char digit = bare[3];
                if (digit >= '1' && digit <= '9') { return true; }
            }
            return false;
        }
    }
}
