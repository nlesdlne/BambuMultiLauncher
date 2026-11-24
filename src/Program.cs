using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace BambuMultiLauncher
{
    internal static class Program
    {
        private const string MutexName = "BambuMultiLauncher.SingleInstance.77A2F1";
        private static Mutex _mutex;

        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    Crash("界面出现未处理的错误", e.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Log.Error("后台线程出现未处理的错误", e.ExceptionObject as Exception);
                };

                if (args.Length >= 2 && args[0] == "--render")
                {
                    Render.Run(args[1]);
                    return;
                }

                if (args.Length >= 2 && args[0] == "--selftest")
                {
                    Environment.ExitCode = SelfTest.Run(args[1]);
                    return;
                }

                bool createdNew;
                _mutex = new Mutex(true, MutexName, out createdNew);
                if (!createdNew)
                {
                    ActivateExisting();
                    return;
                }

                Log.Info("启动 " + AppPaths.AppName + " v" + AppPaths.Version);
                Application.Run(new MainForm());
                Log.Info("正常退出");
            }
            catch (Exception ex)
            {
                Crash("程序无法启动", ex);
            }
            finally
            {
                if (_mutex != null)
                {
                    try { _mutex.ReleaseMutex(); _mutex.Dispose(); }
                    catch { }
                    _mutex = null;
                }
            }
        }

        /// <summary>已经有实例在跑：把它拉到前面，而不是开第二个。</summary>
        private static void ActivateExisting()
        {
            try
            {
                Process self = Process.GetCurrentProcess();
                Process[] same = Process.GetProcessesByName(self.ProcessName);
                foreach (Process p in same)
                {
                    try
                    {
                        if (p.Id == self.Id) { continue; }
                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            NativeMethods.ShowWindow(p.MainWindowHandle, NativeMethods.SW_RESTORE);
                            NativeMethods.SetForegroundWindow(p.MainWindowHandle);
                            return;
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }
            catch { }

            MessageBox.Show(AppPaths.AppName + " 已经在运行了，看看任务栏。",
                AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static void Crash(string what, Exception ex)
        {
            try { Log.Error(what, ex); } catch { }
            string detail = ex == null ? "（没有更多信息）" : ex.GetType().Name + ": " + ex.Message;
            try
            {
                MessageBox.Show(
                    what + "。\r\n\r\n" + detail + "\r\n\r\n可以把日志发给我看看：\r\n" + Log.CurrentFile,
                    AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch { }
        }
    }
}
