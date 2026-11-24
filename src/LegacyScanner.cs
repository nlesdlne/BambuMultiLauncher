using System;
using System.Collections.Generic;
using System.IO;

namespace BambuMultiLauncher
{
    /// <summary>
    /// 找"以前留下的账号目录"：卸掉再装、或者以前用过 PowerShell 脚本的人，
    /// 数据目录还在硬盘上，但 settings.ini 已经没了。这里把它们捡回来。
    /// </summary>
    internal static class LegacyScanner
    {
        internal sealed class Found
        {
            internal string Name = "";
            internal string Dir = "";
            internal string Source = "";
        }

        /// <summary>扫描所有可能的历史账号目录（不做去重判断，去重交给 Import）。</summary>
        internal static List<Found> Scan(Settings settings)
        {
            List<Found> result = new List<Found>();
            List<string> roots = new List<string>();
            HashSet<string> rootSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> dirSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Action<string> addRoot = delegate(string dir)
            {
                if (string.IsNullOrEmpty(dir)) { return; }
                try
                {
                    string full = Path.GetFullPath(dir).TrimEnd('\\');
                    if (rootSeen.Add(full) && Directory.Exists(full)) { roots.Add(full); }
                }
                catch { }
            };

            // 1) 本工具自己的位置（重装后 settings.ini 丢了也还在）
            addRoot(Path.Combine(AppPaths.ConfigDir, "profiles"));
            addRoot(AppPaths.DefaultProfileRoot);

            // 2) 当前配置里记录的数据根目录
            try { addRoot(settings.EffectiveProfileRoot); } catch { }

            // 3) 以前的 PowerShell 脚本（BambuMulti.ps1）留下的 profiles
            foreach (string ps1 in FindLegacyScripts())
            {
                try { addRoot(Path.Combine(Path.GetDirectoryName(ps1), "profiles")); }
                catch { }
            }

            // 4) 桌面 / 文档 / 常见软件目录下形如 "xxx\profiles" 的目录
            foreach (string near in NearRoots())
            {
                try
                {
                    string[] subs = Directory.GetDirectories(near);
                    foreach (string sub in subs)
                    {
                        string name = Path.GetFileName(sub);
                        if (string.Equals(name, "profiles", StringComparison.OrdinalIgnoreCase))
                        {
                            addRoot(sub);
                        }
                    }
                }
                catch { }
            }

            foreach (string root in roots)
            {
                string[] subs;
                try { subs = Directory.GetDirectories(root); }
                catch { continue; }
                foreach (string sub in subs)
                {
                    string full;
                    try { full = Path.GetFullPath(sub).TrimEnd('\\'); }
                    catch { continue; }
                    if (!dirSeen.Add(full)) { continue; }
                    if (!LooksLikeStudioData(full)) { continue; }
                    Found f = new Found();
                    f.Name = Path.GetFileName(full);
                    f.Dir = full;
                    f.Source = root;
                    result.Add(f);
                }
            }
            return result;
        }

        /// <summary>把找到的目录并进配置（已存在的跳过）。返回新加入的个数。</summary>
        internal static int Import(Settings settings)
        {
            int added = 0;
            List<Found> found = Scan(settings);
            foreach (Found f in found)
            {
                // 用户明确删掉过的目录，不要再"认"回来
                if (settings.IsIgnored(f.Dir)) { continue; }
                bool known = false;
                foreach (Profile p in settings.Profiles)
                {
                    if (SameDir(p.Dir, f.Dir)) { known = true; break; }
                }
                if (known) { continue; }
                if (SameDir(f.Dir, AppPaths.BambuDefaultDataDir)) { continue; }

                Profile np = new Profile();
                np.Id = settings.NextId();
                np.Name = UniqueName(settings, FancyName(f.Name));
                np.Dir = f.Dir;
                np.IsDefault = false;
                np.Note = "从以前的安装里找到";
                settings.Profiles.Add(np);
                added++;
                Log.Info("导入历史账号目录: " + f.Dir);
            }
            if (added > 0) { settings.Save(); }
            return added;
        }

        /// <summary>旧目录名常常是 home / work 这种，翻译一下让人看得懂。</summary>
        private static string FancyName(string folder)
        {
            string name = (folder == null ? "" : folder.Trim());
            if (name.Length == 0) { return "以前的账号"; }
            string lower = name.ToLowerInvariant();
            if (lower == "home") { return "以前的账号（家）"; }
            if (lower == "work") { return "以前的账号（公司）"; }
            if (lower == "default" || lower == "profile") { return "以前的账号"; }
            return "以前的账号（" + name + "）";
        }

        private static string UniqueName(Settings settings, string wanted)
        {
            if (string.IsNullOrEmpty(wanted)) { wanted = "账号"; }
            string name = wanted;
            int n = 2;
            while (HasName(settings, name)) { name = wanted + " (" + n.ToString() + ")"; n++; }
            return name;
        }

        private static bool HasName(Settings settings, string name)
        {
            foreach (Profile p in settings.Profiles)
            {
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        internal static bool SameDir(string a, string b)
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

        /// <summary>这个目录像不像一份 Bambu Studio 数据目录。</summary>
        internal static bool LooksLikeStudioData(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) { return false; }
                if (File.Exists(Path.Combine(dir, "BambuStudio.conf"))) { return true; }
                if (File.Exists(Path.Combine(dir, "BambuNetworkEngine.conf"))) { return true; }
                if (Directory.Exists(Path.Combine(dir, "plugins"))) { return true; }
                if (Directory.Exists(Path.Combine(dir, "printers"))) { return true; }
                if (Directory.Exists(Path.Combine(dir, "system"))) { return true; }
            }
            catch { }
            return false;
        }

        private static List<string> NearRoots()
        {
            List<string> list = new List<string>();
            Action<Environment.SpecialFolder> add = delegate(Environment.SpecialFolder f)
            {
                try
                {
                    string p = Environment.GetFolderPath(f);
                    if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) { list.Add(p); }
                }
                catch { }
            };
            add(Environment.SpecialFolder.Desktop);
            add(Environment.SpecialFolder.MyDocuments);
            add(Environment.SpecialFolder.UserProfile);
            add(Environment.SpecialFolder.LocalApplicationData);
            add(Environment.SpecialFolder.ApplicationData);
            return list;
        }

        /// <summary>找以前的 PowerShell 脚本（就是最早那版），最多往下找两层。</summary>
        private static List<string> FindLegacyScripts()
        {
            List<string> hits = new List<string>();
            List<string> roots = new List<string>();
            Action<string> addRoot = delegate(string dir)
            {
                try
                {
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) { roots.Add(dir); }
                }
                catch { }
            };
            addRoot(Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            addRoot(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            addRoot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
            addRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            addRoot(@"C:\software");
            addRoot(@"D:\software");

            // 只找两层，避免在用户目录里全盘递归（会很慢）
            foreach (string root in roots)
            {
                ProbeScript(root, hits);
                string[] subs;
                try { subs = Directory.GetDirectories(root); }
                catch { continue; }
                foreach (string sub in subs)
                {
                    ProbeScript(sub, hits);
                    string[] subs2;
                    try { subs2 = Directory.GetDirectories(sub); }
                    catch { continue; }
                    foreach (string sub2 in subs2) { ProbeScript(sub2, hits); }
                }
            }
            return hits;
        }

        private static void ProbeScript(string dir, List<string> hits)
        {
            try
            {
                if (!Directory.Exists(dir)) { return; }
                FileAttributes attr = File.GetAttributes(dir);
                if ((attr & FileAttributes.ReparsePoint) != 0) { return; }
                string file = Path.Combine(dir, "BambuMulti.ps1");
                if (File.Exists(file)) { hits.Add(file); }
            }
            catch { }
        }
    }
}
