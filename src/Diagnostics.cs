using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace BambuMultiLauncher
{
    /// <summary>针对"新窗口起不来"的检查和补救。</summary>
    internal static class ProfileRepair
    {
        /// <summary>
        /// 让这份数据目录里的 Bambu Studio 关掉"只允许一个实例"。
        /// 老版本的 conf 默认是 true，会把新进程的启动请求转给已经开着的窗口，
        /// 看起来就是"点了没反应"，实际是进程瞬间退出了。
        /// </summary>
        internal static bool EnsureSingleInstanceOff(string dir, out string note)
        {
            note = "";
            string conf = Path.Combine(dir, "BambuStudio.conf");
            try
            {
                if (File.Exists(conf))
                {
                    string text = File.ReadAllText(conf);
                    string problem;
                    string patched = PatchSingleInstance(text, out problem);
                    if (patched == null)
                    {
                        note = "没能自动修补：" + problem + "。可以把这份 BambuStudio.conf 删掉，让 Bambu Studio 自己重新生成一份。";
                        return false;
                    }
                    if (string.Equals(text, patched, StringComparison.Ordinal)) { return false; }
                    File.Copy(conf, conf + ".bak", true);
                    File.WriteAllText(conf, patched, new UTF8Encoding(false));
                    note = "已把数据目录里的 single_instance 改成 false（原文件备份为 BambuStudio.conf.bak）。";
                    Log.Info("修补 single_instance: " + conf);
                    return true;
                }

                // 还没有配置文件：写一份最小的，只为关掉单实例
                File.WriteAllText(conf, "{\r\n  \"single_instance\": false\r\n}\r\n", new UTF8Encoding(false));
                note = "数据目录里还没有配置，已先写一份只含 single_instance=false 的最小配置。";
                Log.Info("写入最小配置: " + conf);
                return true;
            }
            catch (Exception ex)
            {
                note = "改配置失败：" + ex.Message;
                Log.Warn("修补 single_instance 失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>把 "single_instance": true 改成 false；没有这个字段就插一个。改不了返回 null，problem 说明原因。</summary>
        internal static string PatchSingleInstance(string text)
        {
            string problem;
            return PatchSingleInstance(text, out problem);
        }

        internal static string PatchSingleInstance(string text, out string problem)
        {
            problem = "";
            if (text == null) { problem = "读不到内容"; return null; }
            int at = text.IndexOf("\"single_instance\"", StringComparison.Ordinal);
            if (at < 0)
            {
                int brace = text.IndexOf('{');
                if (brace < 0) { problem = "内容不是 JSON（找不到左花括号）"; return null; }

                // 看左花括号后面第一个有效字符是不是右花括号：空对象插字段不能带逗号
                int probe = brace + 1;
                while (probe < text.Length && (text[probe] == ' ' || text[probe] == '\t' || text[probe] == '\r' || text[probe] == '\n')) { probe++; }
                bool emptyObject = probe >= text.Length || text[probe] == '}';
                string insert = emptyObject
                    ? "\r\n  \"single_instance\": false\r\n"
                    : "\r\n  \"single_instance\": false,";
                return text.Substring(0, brace + 1) + insert + text.Substring(brace + 1);
            }
            int colon = text.IndexOf(':', at);
            if (colon < 0) { problem = "字段后面没有冒号，格式不完整"; return null; }
            int valueStart = colon + 1;
            while (valueStart < text.Length && (text[valueStart] == ' ' || text[valueStart] == '\t')) { valueStart++; }
            if (valueStart >= text.Length) { problem = "字段后面没有值"; return null; }
            if (string.Compare(text, valueStart, "true", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return text.Substring(0, valueStart) + "false" + text.Substring(valueStart + 4);
            }
            if (string.Compare(text, valueStart, "false", 0, 5, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return text; // 已经是 false：原样返回，调用方会认为"无需改动"
            }
            problem = "single_instance 的值不是 true/false（可能是字符串或数字）";
            return null;
        }

        /// <summary>读默认账号 conf 里的 single_instance，用于报告。</summary>
        internal static string ReadSingleInstance(string dir)
        {
            try
            {
                string conf = Path.Combine(dir, "BambuStudio.conf");
                if (!File.Exists(conf)) { return "（没有配置文件）"; }
                string text = File.ReadAllText(conf);
                int at = text.IndexOf("\"single_instance\"", StringComparison.Ordinal);
                if (at < 0) { return "（没有这个字段）"; }
                int line = text.IndexOf('\n', at);
                string piece = line > at ? text.Substring(at, line - at) : text.Substring(at);
                return piece.Trim().TrimEnd(',');
            }
            catch (Exception ex) { return "（读取失败：" + ex.Message + "）"; }
        }
    }

    /// <summary>一次启动尝试的观测结果。</summary>
    internal sealed class Observation
    {
        internal LaunchMethod Method = LaunchMethod.Auto;
        internal int Pid;
        internal string ProfileDir = "";
        internal bool Started;
        internal bool Exited;
        internal int ExitCode;
        internal bool HasExitCode;
        internal int Milliseconds;
        internal bool DirChanged;
        internal string Detail = "";
    }

    /// <summary>
    /// 启动诊断：真的把 Bambu Studio 拉起来，看它活着多久、目标目录有没有被写。
    /// 第一次失败时自动修补 single_instance 再试一次。
    /// </summary>
    internal static class LaunchDiagnostics
    {
        internal static string Run(Settings settings, Profile p, out bool fixedNow)
        {
            fixedNow = false;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("== 启动诊断 ==");
            sb.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("账号：" + p.Name + (p.IsDefault ? "（默认账号）" : ""));
            sb.AppendLine("目录：" + p.Dir + (Directory.Exists(p.Dir) ? "" : "  【不存在】"));

            string exe = settings.StudioExe;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) { exe = StudioLocator.Find(settings.StudioExe); }
            sb.AppendLine("主程序：" + (string.IsNullOrEmpty(exe) ? "（没找到）" : exe + "  版本 " + StudioLocator.VersionOf(exe)));
            if (!string.IsNullOrEmpty(exe))
            {
                sb.AppendLine("同目录下的其他候选：");
                List<string> all = StudioLocator.FindAll(settings.StudioExe);
                foreach (string c in all)
                {
                    sb.AppendLine("  - " + c + "  版本 " + StudioLocator.VersionOf(c));
                }
            }
            sb.AppendLine("默认账号的 single_instance：" + ProfileRepair.ReadSingleInstance(AppPaths.BambuDefaultDataDir));
            if (!p.IsDefault)
            {
                sb.AppendLine("本账号的 single_instance：" + ProfileRepair.ReadSingleInstance(p.Dir));
            }
            try
            {
                if (!p.IsDefault)
                {
                    bool hasPlugins = Directory.Exists(Path.Combine(p.Dir, "plugins"))
                        && File.Exists(Path.Combine(p.Dir, "plugins", "bambu_networking.dll"));
                    sb.AppendLine("网络插件：" + (hasPlugins ? "已就位" : "缺少（第一次启动要联网下载，会慢一点）"));
                }
            }
            catch { }
            sb.AppendLine();

            if (string.IsNullOrEmpty(exe))
            {
                sb.AppendLine("结论：找不到 bambu-studio.exe，先到“高级设置”里指定。");
                return sb.ToString();
            }

            string args = p.IsDefault ? "" : "--datadir \"" + p.Dir + "\"";
            if (!string.IsNullOrEmpty(settings.ExtraArgs))
            {
                args = (args.Length > 0 ? args + " " : "") + settings.ExtraArgs;
            }

            LaunchMethod configured = LaunchService.MethodOf(settings);
            sb.AppendLine("当前的启动方式设置：" + DescribeMethod(configured));
            sb.AppendLine();
            sb.AppendLine("【第 1 种】双击方式：和双击快捷方式完全一样");
            Observation o1 = Attempt(exe, args, p.Dir, 7, LaunchMethod.Shell);
            AppendObservation(sb, o1);

            if (IsOkay(o1))
            {
                sb.AppendLine();
                sb.AppendLine("结论：用“双击方式”能正常起来。");
                if (configured != LaunchMethod.Shell)
                {
                    sb.AppendLine("     如果平时点不动，可以把“高级设置 → 启动方式”固定成“双击方式”。");
                }
                return sb.ToString();
            }

            sb.AppendLine();
            if (p.IsDefault)
            {
                sb.AppendLine("第一种不理想，直接换第二种（默认账号没有可修补的开关）。");
            }
            else
            {
                sb.AppendLine("第一种不理想，顺手试试能不能修补“单实例”开关。");
                string note = "";
                bool patched = ProfileRepair.EnsureSingleInstanceOff(p.Dir, out note);
                if (patched) { fixedNow = true; }
                sb.AppendLine("  修补：" + (note.Length > 0 ? note : "无需改动"));
            }
            Thread.Sleep(600);

            sb.AppendLine();
            sb.AppendLine("【第 2 种】常规方式：带命令行参数，直接创建进程");
            Observation o2 = Attempt(exe, args, p.Dir, 7, LaunchMethod.Direct);
            AppendObservation(sb, o2);

            sb.AppendLine();
            if (IsOkay(o2))
            {
                sb.AppendLine("结论：用“常规方式”能起来，双击方式在这台电脑上不行。");
                sb.AppendLine("     请到“高级设置 → 启动方式”里选“常规方式（带命令行参数启动）”，以后就都能开了。");
                return sb.ToString();
            }
            if (o2.Started && !o2.Exited)
            {
                sb.AppendLine("结论：进程还在运行，但没看到窗口、数据目录也没有写入，无法确认它是不是真的起来了。");
                sb.AppendLine("     再等 20 秒看看屏幕上有没有新窗口；如果还是没有，把这份报告发出来。");
                return sb.ToString();
            }
            sb.AppendLine("结论：两种方式都没起来。请把这份报告（连同环境报告）发给开发者。");
            sb.AppendLine("另外可以先试两件事：");
            sb.AppendLine("  1. 直接双击 Bambu Studio 原版快捷方式，确认它本身能正常打开；");
            sb.AppendLine("  2. 在“高级设置”里把 Bambu Studio 手动指定成另一个位置（如果列表里有多个）。");
            return sb.ToString();
        }

        private static string DescribeMethod(LaunchMethod method)
        {
            if (method == LaunchMethod.Shell) { return "双击方式"; }
            if (method == LaunchMethod.Direct) { return "常规方式"; }
            return "自动（先双击方式，不行再用常规方式）";
        }

        /// <summary>这次尝试算不算"真的起来了"：进程还活着，而且看得到窗口或者目标目录被写过。</summary>
        private static bool IsOkay(Observation o)
        {
            if (o == null || !o.Started || o.Exited) { return false; }
            if (o.DirChanged) { return true; }
            string title;
            return LaunchService.HasWindow(o.Pid, out title);
        }

        private static void AppendObservation(StringBuilder sb, Observation o)
        {
            if (!o.Started)
            {
                sb.AppendLine("  结果：进程没有创建。" + (o.Detail.Length > 0 ? "（" + o.Detail + "）" : ""));
                return;
            }
            string how = o.Method == LaunchMethod.Direct ? "常规方式" : "双击方式";
            sb.AppendLine("  进程：创建成功（" + how + "），存活 " + o.Milliseconds.ToString() + " 毫秒");
            if (o.Exited)
            {
                if (!o.HasExitCode)
                {
                    sb.AppendLine("  状态：自己退出了（拿不到退出码，通常是权限限制）");
                }
                else if (o.ExitCode == 0)
                {
                    sb.AppendLine("  状态：已经正常退出（退出码 0）");
                }
                else
                {
                    sb.AppendLine("  状态：自己退出了，退出码 " + o.ExitCode.ToString()
                        + "（0x" + unchecked((uint)o.ExitCode).ToString("X8") + "）");
                    if (unchecked((uint)o.ExitCode) == 0xC0000409u)
                    {
                        sb.AppendLine("        → 这是 Bambu Studio 自己崩了（内部错误），不是参数问题。");
                        sb.AppendLine("          常见原因：已经有另一个 Bambu Studio 在用同一份数据目录、插件损坏。");
                    }
                    else if (unchecked((uint)o.ExitCode) == 0xC0000135u || unchecked((uint)o.ExitCode) == 0xC0000139u)
                    {
                        sb.AppendLine("        → 缺少运行库或 DLL（0xC0000135 / 0xC0000139），这台机器上 Bambu Studio 本身跑不起来。");
                    }
                }
            }
            else
            {
                string title;
                if (LaunchService.HasWindow(o.Pid, out title))
                {
                    sb.AppendLine("  状态：还在运行，窗口已经出现（“" + title + "”）");
                }
                else
                {
                    sb.AppendLine("  状态：进程还在，但没看到窗口");
                    sb.AppendLine("        → 多半卡在初始化或下载网络插件（这个账号目录里插件：" +
                        (LaunchService.HasPlugins(o.ProfileDir) ? "有" : "没有") + "）。再等 30 秒看看。");
                }
            }
            sb.AppendLine("  目标目录：" + (o.DirChanged ? "有新的写入（Bambu Studio 读到了这个目录）" : "7 秒内没有任何写入（可能只是还没写到）"));
            if (o.Detail.Length > 0) { sb.AppendLine("  备注：" + o.Detail); }
        }

        private static Observation Attempt(string exe, string args, string dir, int seconds, LaunchMethod method)
        {
            Observation o = new Observation();
            Snapshot before = Snapshot.Take(dir);
            o.Method = method;
            o.ProfileDir = dir;

            string error = "";
            Process proc = LaunchService.StartRaw(exe, args, method, out error);
            if (proc == null)
            {
                o.Started = false;
                o.Detail = error;
                return o;
            }
            o.Started = true;
            int pid = proc.Id;
            o.Pid = pid;

            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                while (watch.Elapsed.TotalSeconds < seconds)
                {
                    if (proc.HasExited) { o.Exited = true; break; }
                    Thread.Sleep(200);
                }
                if (o.Exited)
                {
                    int code;
                    if (LaunchService.TryGetExitCode(pid, out code)) { o.ExitCode = code; o.HasExitCode = true; }
                }
            }
            catch (Exception ex)
            {
                o.Detail = ex.Message;
            }
            o.Milliseconds = (int)watch.ElapsedMilliseconds;

            Snapshot after = Snapshot.Take(dir);
            o.DirChanged = !before.SameAs(after);
            if (o.DirChanged) { o.Detail = "目录变化：" + before.Describe() + " -> " + after.Describe(); }
            return o;
        }

        internal sealed class Snapshot
        {
            internal int Files;
            internal bool Conf;
            internal long ConfSize;
            internal bool Logs;

            internal static Snapshot Take(string dir)
            {
                Snapshot s = new Snapshot();
                try
                {
                    if (!Directory.Exists(dir)) { return s; }
                    s.Files = Directory.GetFiles(dir).Length;
                    string conf = Path.Combine(dir, "BambuStudio.conf");
                    s.Conf = File.Exists(conf);
                    if (s.Conf) { s.ConfSize = new FileInfo(conf).Length; }
                    s.Logs = Directory.Exists(Path.Combine(dir, "logs"));
                }
                catch { }
                return s;
            }

            internal bool SameAs(Snapshot other)
            {
                if (other == null) { return false; }
                return Files == other.Files && Conf == other.Conf && ConfSize == other.ConfSize && Logs == other.Logs;
            }

            internal string Describe()
            {
                return "文件 " + Files.ToString() + "，配置 " + (Conf ? "有(" + ConfSize.ToString() + ")" : "无")
                    + "，日志 " + (Logs ? "有" : "无");
            }
        }
    }
}
