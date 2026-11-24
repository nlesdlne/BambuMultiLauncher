using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace BambuMultiLauncher
{
    /// <summary>
    /// 负责找到 Bambu Studio 主程序。
    /// 老版本/装在奇怪位置的情况不少（比如按用户安装、装在 D 盘、卸载表里名字不含 Bambu），
    /// 所以这里除了常见目录，还会把注册表翻一遍。
    /// </summary>
    internal static class StudioLocator
    {
        internal const string ExeName = "bambu-studio.exe";

        internal static readonly string[] RelativePaths = new string[]
        {
            @"Bambu Studio\bambu-studio.exe",
            @"BambuStudio\bambu-studio.exe",
            @"Programs\Bambu Studio\bambu-studio.exe",
            @"Bambu Studio\BambuStudio.exe",
            @"BambuLab\Bambu Studio\bambu-studio.exe"
        };

        /// <summary>按优先级返回所有可能的位置（只含真实存在的文件）。</summary>
        internal static List<string> FindAll(string preferred)
        {
            List<string> found = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Action<string> push = delegate(string path)
            {
                if (string.IsNullOrEmpty(path)) { return; }
                try
                {
                    string full = Path.GetFullPath(path.Trim().Trim('"', ' '));
                    if (!File.Exists(full)) { return; }
                    if (!seen.Add(full)) { return; }
                    found.Add(full);
                }
                catch { }
            };

            push(preferred);

            foreach (string fromRegistry in RegistryCandidates())
            {
                push(fromRegistry);
            }

            foreach (string dir in SearchRoots())
            {
                foreach (string rel in RelativePaths) { push(Path.Combine(dir, rel)); }
                push(Path.Combine(dir, "Bambu Studio", ExeName));
            }

            foreach (string install in ReadUninstallLocations())
            {
                push(Path.Combine(install, ExeName));
                foreach (string rel in RelativePaths) { push(Path.Combine(install, rel)); }
            }

            return Order(found, preferred);
        }

        internal static string Find(string preferred)
        {
            List<string> all = FindAll(preferred);
            if (all.Count > 0) { return all[0]; }
            return "";
        }

        /// <summary>同名的其他安装（用于提示"装了多个版本"）。</summary>
        internal static List<string> FindOthers(string preferred)
        {
            List<string> all = FindAll(preferred);
            if (all.Count <= 1) { return new List<string>(); }
            return all;
        }

        // ------------------------------------------------------------ 注册表

        private static readonly string[] KnownValueKeys = new string[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\bambu-studio.exe",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\bambu-studio.exe",
            @"SOFTWARE\Classes\Applications\bambu-studio.exe\shell\open\command",
            @"SOFTWARE\WOW6432Node\Classes\Applications\bambu-studio.exe\shell\open\command"
        };

        private static List<string> RegistryCandidates()
        {
            List<string> list = new List<string>();
            RegistryKey[] hives = new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser };

            // 1) App Paths / Classes\Applications 这类明确登记过的地方
            foreach (RegistryKey hive in hives)
            {
                foreach (string key in KnownValueKeys)
                {
                    try
                    {
                        using (RegistryKey k = hive.OpenSubKey(key))
                        {
                            if (k == null) { continue; }
                            string def = Convert.ToString(k.GetValue(""));
                            AddPathsFromText(list, def);
                            string dir = Convert.ToString(k.GetValue("Path"));
                            if (!string.IsNullOrEmpty(dir)) { AddPathsFromText(list, dir); }
                        }
                    }
                    catch { }
                }
            }

            // 2) 卸载表（HKLM/HKCU × 64/32 四份）：DisplayName 含 Bambu，或者 DisplayIcon 指向它
            string[] uninstallRoots = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (RegistryKey hive in hives)
            {
                foreach (string root in uninstallRoots)
                {
                    try
                    {
                        using (RegistryKey k = hive.OpenSubKey(root))
                        {
                            if (k == null) { continue; }
                            foreach (string sub in k.GetSubKeyNames())
                            {
                                try
                                {
                                    using (RegistryKey sk = k.OpenSubKey(sub))
                                    {
                                        if (sk == null) { continue; }
                                        string name = Convert.ToString(sk.GetValue("DisplayName"));
                                        string icon = Convert.ToString(sk.GetValue("DisplayIcon"));
                                        string loc = Convert.ToString(sk.GetValue("InstallLocation"));
                                        string uninstall = Convert.ToString(sk.GetValue("UninstallString"));
                                        bool relevant = (name != null && name.IndexOf("bambu", StringComparison.OrdinalIgnoreCase) >= 0)
                                            || (icon != null && icon.IndexOf(ExeName, StringComparison.OrdinalIgnoreCase) >= 0);
                                        if (!relevant) { continue; }
                                        AddPathsFromText(list, loc);
                                        AddPathsFromText(list, icon);
                                        AddPathsFromText(list, uninstall);
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }

            // 3) 厂商键：SOFTWARE（含 WOW6432Node）下名字带 bambu 的子树，往下三层把字符串值都看一遍
            foreach (RegistryKey hive in hives)
            {
                SweepVendor(hive, @"SOFTWARE", 0, list);
                SweepVendor(hive, @"SOFTWARE\WOW6432Node", 0, list);
            }
            return list;
        }

        private static void SweepVendor(RegistryKey hive, string path, int depth, List<string> list)
        {
            if (depth > 3) { return; }
            RegistryKey key = null;
            try
            {
                key = hive.OpenSubKey(path);
                if (key == null) { return; }

                if (depth > 0)
                {
                    foreach (string valueName in key.GetValueNames())
                    {
                        try { AddPathsFromText(list, Convert.ToString(key.GetValue(valueName))); }
                        catch { }
                    }
                }

                foreach (string sub in key.GetSubKeyNames())
                {
                    bool looksRelated = sub.IndexOf("bambu", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!looksRelated && depth >= 1) { continue; }
                    SweepVendor(hive, path + "\\" + sub, depth + 1, list);
                }
            }
            catch { }
            finally
            {
                if (key != null) { key.Close(); }
            }
        }

        /// <summary>从一段注册表文本里抠出可用的 exe 路径或安装目录。</summary>
        private static void AddPathsFromText(List<string> list, string text)
        {
            if (string.IsNullOrEmpty(text)) { return; }

            // 直接就是目录
            try
            {
                string t = text.Trim().Trim('"');
                if (Directory.Exists(t)) { list.Add(Path.Combine(t, ExeName)); }
            }
            catch { }

            // 文本里出现了 bambu-studio.exe：往前找到引号或空格，截出完整路径
            int from = 0;
            while (true)
            {
                int at = text.IndexOf(ExeName, from, StringComparison.OrdinalIgnoreCase);
                if (at < 0) { break; }
                int end = at + ExeName.Length;
                int start = 0;
                for (int i = at - 1; i >= 0; i--)
                {
                    char c = text[i];
                    if (c == '"' || c == '=' || c == ';' || c == '|')
                    {
                        start = i + 1;
                        break;
                    }
                }
                string candidate = text.Substring(start, end - start).Trim().Trim('"');
                if (candidate.Length > 3 && File.Exists(candidate)) { list.Add(candidate); }
                from = end;
            }

            // DisplayIcon 常见形式 "C:\...\bambu-studio.exe,0"
            int comma = text.IndexOf(',');
            if (comma > 0 && text.IndexOf(ExeName, StringComparison.OrdinalIgnoreCase) > 0)
            {
                string head = text.Substring(0, comma).Trim().Trim('"');
                try { if (File.Exists(head)) { list.Add(head); } }
                catch { }
            }
        }

        private static IEnumerable<string> ReadUninstallLocations()
        {
            List<string> result = new List<string>();
            string[] roots = new string[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            RegistryKey[] hives = new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser };
            foreach (RegistryKey hive in hives)
            {
                foreach (string root in roots)
                {
                    try
                    {
                        using (RegistryKey k = hive.OpenSubKey(root))
                        {
                            if (k == null) { continue; }
                            foreach (string sub in k.GetSubKeyNames())
                            {
                                try
                                {
                                    using (RegistryKey sk = k.OpenSubKey(sub))
                                    {
                                        if (sk == null) { continue; }
                                        string name = Convert.ToString(sk.GetValue("DisplayName"));
                                        if (string.IsNullOrEmpty(name)) { continue; }
                                        if (name.IndexOf("Bambu", StringComparison.OrdinalIgnoreCase) < 0) { continue; }
                                        string loc = Convert.ToString(sk.GetValue("InstallLocation"));
                                        if (string.IsNullOrEmpty(loc)) { continue; }
                                        loc = loc.Trim().Trim('"').TrimEnd('\\');
                                        if (Directory.Exists(loc)) { result.Add(loc); }
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                    catch { }
                }
            }
            return result;
        }

        // ------------------------------------------------------------ 目录

        private static IEnumerable<string> SearchRoots()
        {
            List<string> roots = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Action<string> add = delegate(string p)
            {
                if (string.IsNullOrEmpty(p)) { return; }
                try
                {
                    string full = Path.GetFullPath(p).TrimEnd('\\');
                    if (seen.Add(full) && Directory.Exists(full)) { roots.Add(full); }
                }
                catch { }
            };
            Action<Environment.SpecialFolder> addSpecial = delegate(Environment.SpecialFolder f)
            {
                try { add(Environment.GetFolderPath(f)); }
                catch { }
            };

            addSpecial(Environment.SpecialFolder.ProgramFiles);
            addSpecial(Environment.SpecialFolder.ProgramFilesX86);
            addSpecial(Environment.SpecialFolder.LocalApplicationData);
            addSpecial(Environment.SpecialFolder.ApplicationData);

            // 别的盘上也可能装了：只试几个固定的名字，不做全盘遍历
            try
            {
                foreach (DriveInfo drive in DriveInfo.GetDrives())
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) { continue; }
                    string root = drive.RootDirectory.FullName;
                    add(root);
                    add(Path.Combine(root, "Program Files"));
                    add(Path.Combine(root, "Program Files (x86)"));
                    add(Path.Combine(root, "Bambu Studio"));
                    add(Path.Combine(root, "BambuLab"));
                    add(Path.Combine(root, "software"));
                }
            }
            catch { }
            return roots;
        }

        // ------------------------------------------------------------ 杂项

        private static List<string> Order(List<string> found, string preferred)
        {
            if (found.Count <= 1) { return found; }
            List<string> ordered = new List<string>(found);
            ordered.Sort(delegate(string a, string b)
            {
                bool pa = SamePath(a, preferred);
                bool pb = SamePath(b, preferred);
                if (pa != pb) { return pa ? -1 : 1; }
                int v = CompareVersion(VersionOf(b), VersionOf(a));
                if (v != 0) { return v; }
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
            return ordered;
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) { return false; }
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static int CompareVersion(string a, string b)
        {
            int[] x = Split(a);
            int[] y = Split(b);
            int n = Math.Max(x.Length, y.Length);
            for (int i = 0; i < n; i++)
            {
                int xi = i < x.Length ? x[i] : 0;
                int yi = i < y.Length ? y[i] : 0;
                if (xi != yi) { return xi < yi ? -1 : 1; }
            }
            return 0;
        }

        private static int[] Split(string version)
        {
            List<int> parts = new List<int>();
            if (!string.IsNullOrEmpty(version))
            {
                foreach (string piece in version.Split('.'))
                {
                    int value;
                    if (int.TryParse(piece.Trim(), out value)) { parts.Add(value); }
                    else { parts.Add(0); }
                }
            }
            return parts.ToArray();
        }

        internal static string VersionOf(string exe)
        {
            try
            {
                if (string.IsNullOrEmpty(exe) || !File.Exists(exe)) { return ""; }
                FileVersionInfo info = FileVersionInfo.GetVersionInfo(exe);
                if (!string.IsNullOrEmpty(info.ProductVersion)) { return info.ProductVersion; }
                return info.FileVersion;
            }
            catch { return ""; }
        }

        /// <summary>候选目录里是否已有 Bambu Studio 的数据（判断“这台机器装没用过”）。</summary>
        internal static bool HasExistingData()
        {
            try
            {
                string dir = AppPaths.BambuDefaultDataDir;
                return Directory.Exists(dir) && File.Exists(Path.Combine(dir, "BambuStudio.conf"));
            }
            catch { return false; }
        }
    }
}
