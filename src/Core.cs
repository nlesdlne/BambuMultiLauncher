using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace BambuMultiLauncher
{
    /// <summary>全局路径与常量。</summary>
    internal static class AppPaths
    {
        internal const string AppName = "拓竹多账号启动器";
        internal const string AppNameEn = "Bambu Multi-Account Launcher";
        internal const string Version = "1.0.5";

        /// <summary>
        /// 配置目录，默认 %APPDATA%\BambuMultiLauncher。
        /// 设置环境变量 BAMBUMULTI_HOME 可以换到别处（便携使用 / 放在 U 盘里），也方便测试。
        /// </summary>
        internal static string ConfigDir
        {
            get
            {
                string custom = Environment.GetEnvironmentVariable("BAMBUMULTI_HOME");
                if (!string.IsNullOrEmpty(custom) && custom.Trim().Length > 0)
                {
                    try { return Path.GetFullPath(custom.Trim()); }
                    catch { }
                }
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BambuMultiLauncher");
            }
        }

        internal static string SettingsFile { get { return Path.Combine(ConfigDir, "settings.ini"); } }
        internal static string LogDir { get { return Path.Combine(ConfigDir, "logs"); } }
        internal static string StateDir { get { return Path.Combine(ConfigDir, "state"); } }
        internal static string DefaultProfileRoot { get { return Path.Combine(ConfigDir, "profiles"); } }

        /// <summary>Bambu Studio 自己的默认数据目录（“原来那个账号”）。</summary>
        internal static string BambuDefaultDataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BambuStudio"); }
        }

        internal static string ExeDir
        {
            get
            {
                try
                {
                    string p = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    if (!string.IsNullOrEmpty(p)) { return Path.GetDirectoryName(p); }
                }
                catch { }
                return AppDomain.CurrentDomain.BaseDirectory;
            }
        }

        internal static void EnsureDir(string dir)
        {
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }

    /// <summary>极简日志：写到 %APPDATA%\BambuMultiLauncher\logs\app-yyyyMMdd.log。</summary>
    internal static class Log
    {
        private static readonly object Gate = new object();

        internal static string CurrentFile
        {
            get { return Path.Combine(AppPaths.LogDir, "app-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log"); }
        }

        internal static void Info(string message)
        {
            Write("INFO", message, null);
        }

        internal static void Warn(string message)
        {
            Write("WARN", message, null);
        }

        internal static void Error(string message, Exception ex)
        {
            Write("ERROR", message, ex);
        }

        private static void Write(string level, string message, Exception ex)
        {
            try
            {
                lock (Gate)
                {
                    AppPaths.EnsureDir(AppPaths.LogDir);
                    StringBuilder sb = new StringBuilder();
                    sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                    sb.Append(" [").Append(level).Append("] ").Append(message);
                    if (ex != null)
                    {
                        sb.AppendLine();
                        sb.Append(ex.GetType().FullName).Append(": ").Append(ex.Message);
                        if (!string.IsNullOrEmpty(ex.StackTrace)) { sb.AppendLine().Append(ex.StackTrace); }
                    }
                    sb.AppendLine();
                    File.AppendAllText(CurrentFile, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // 日志失败绝不影响主流程
            }
        }
    }

    /// <summary>一个“账号” = 一份 Bambu Studio 数据目录。</summary>
    internal sealed class Profile
    {
        internal int Id;
        internal string Name;
        internal string Dir;
        internal bool IsDefault;
        internal string Note;

        internal string CredentialFile { get { return Path.Combine(Dir, "BambuNetworkEngine.conf"); } }
        internal string PrefsFile { get { return Path.Combine(Dir, "BambuStudio.conf"); } }

        /// <summary>
        /// 凭据文件至少要有这么多字节才算"登录过"。
        /// 退出登录或异常中断会留下 32 字节的空壳（16 字节文件头 + 16 字节密文），
        /// 真实凭据是 704 / 768 字节这个量级，所以 48 是个安全的门槛。
        /// </summary>
        internal const int MinCredentialBytes = 48;

        /// <summary>凭据文件字节数；文件不存在返回 0。</summary>
        internal int CredentialBytes
        {
            get
            {
                try
                {
                    FileInfo fi = new FileInfo(CredentialFile);
                    return fi.Exists ? (int)fi.Length : 0;
                }
                catch { return 0; }
            }
        }

        /// <summary>
        /// 有登录凭据。注意不能只看"文件存在"或"长度 &gt; 0"：
        /// 32 字节的空壳文件会让一个其实没登录的账号显示成"已登录"。
        /// </summary>
        internal bool HasLogin
        {
            get { return CredentialBytes >= MinCredentialBytes; }
        }

        internal bool PrefsExist
        {
            get { return SafeExists(PrefsFile); }
        }

        internal static bool SafeExists(string path)
        {
            try { return !string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)); }
            catch { return false; }
        }

        internal string ShortDir
        {
            get
            {
                if (string.IsNullOrEmpty(Dir)) { return ""; }
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(home) && Dir.StartsWith(home, StringComparison.OrdinalIgnoreCase))
                {
                    return "%USERPROFILE%" + Dir.Substring(home.Length);
                }
                return Dir;
            }
        }

        internal Profile Clone()
        {
            Profile p = new Profile();
            p.Id = Id; p.Name = Name; p.Dir = Dir; p.IsDefault = IsDefault; p.Note = Note;
            return p;
        }
    }

    /// <summary>settings.ini 读写 + 账号集合。</summary>
    internal sealed class Settings
    {
        internal string StudioExe = "";
        internal string ExtraArgs = "";
        internal string ProfileRoot = "";
        internal int LastProfileId;
        internal bool FirstRunDone;
        internal bool RememberWindow = true;
        internal bool DedupeLaunch = true;
        internal bool AutoRefresh = true;
        /// <summary>启动方式：0=自动（先双击式），1=双击式，2=常规式。</summary>
        internal int LaunchMethodValue = 0;
        internal int WindowX = -1;
        internal int WindowY = -1;
        internal int WindowW = 900;
        internal int WindowH = 560;

        internal readonly List<Profile> Profiles = new List<Profile>();

        /// <summary>用户已经从列表里删掉的目录。下次不许再被"认旧数据"捡回来。</summary>
        internal readonly List<string> IgnoredDirs = new List<string>();

        internal string EffectiveProfileRoot
        {
            get
            {
                if (!string.IsNullOrEmpty(ProfileRoot)) { return ProfileRoot; }
                return AppPaths.DefaultProfileRoot;
            }
        }

        internal Profile FindById(int id)
        {
            foreach (Profile p in Profiles) { if (p.Id == id) { return p; } }
            return null;
        }

        internal int NextId()
        {
            int max = 0;
            foreach (Profile p in Profiles) { if (p.Id > max) { max = p.Id; } }
            return max + 1;
        }

        // ---------------------------------------------------------- 路径与忽略列表
        /// <summary>把路径变成绝对路径；相对路径按配置目录算（Bambu Studio 会按自己的目录解析相对路径）。</summary>
        internal static string NormalizeDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) { return ""; }
            string d = dir.Trim();
            if (d.Length == 0) { return ""; }
            try
            {
                if (!Path.IsPathRooted(d)) { d = Path.Combine(AppPaths.ConfigDir, d); }
                return Path.GetFullPath(d);
            }
            catch { return d; }
        }

        /// <summary>比较用的规范化写法（去尾部分隔符，不分大小写比）。</summary>
        private static string DirKey(string dir)
        {
            string d = NormalizeDir(dir);
            return d.TrimEnd('\\');
        }

        internal void IgnoreDir(string dir)
        {
            string key = DirKey(dir);
            if (key.Length == 0) { return; }
            foreach (string old in IgnoredDirs)
            {
                if (string.Equals(DirKey(old), key, StringComparison.OrdinalIgnoreCase)) { return; }
            }
            IgnoredDirs.Add(key);
        }

        internal void UnignoreDir(string dir)
        {
            string key = DirKey(dir);
            if (key.Length == 0) { return; }
            for (int i = IgnoredDirs.Count - 1; i >= 0; i--)
            {
                if (string.Equals(DirKey(IgnoredDirs[i]), key, StringComparison.OrdinalIgnoreCase)) { IgnoredDirs.RemoveAt(i); }
            }
        }

        internal bool IsIgnored(string dir)
        {
            string key = DirKey(dir);
            if (key.Length == 0) { return false; }
            foreach (string old in IgnoredDirs)
            {
                if (string.Equals(DirKey(old), key, StringComparison.OrdinalIgnoreCase)) { return true; }
            }
            return false;
        }

        // ---------------------------------------------------------- 读
        internal static Settings Load()
        {
            Settings s = new Settings();
            try
            {
                if (!File.Exists(AppPaths.SettingsFile)) { return s; }
                IniFile ini = IniFile.Load(AppPaths.SettingsFile);

                s.StudioExe = ini.Get("app", "studioexe", "");
                s.ExtraArgs = ini.Get("app", "extraargs", "");
                s.ProfileRoot = NormalizeDir(ini.Get("app", "profileroot", ""));
                s.LastProfileId = ini.GetInt("app", "lastprofile", 0);
                s.FirstRunDone = ini.GetBool("app", "firstrun", false);
                s.RememberWindow = ini.GetBool("app", "remember", true);
                s.DedupeLaunch = ini.GetBool("app", "dedupe", true);
                s.AutoRefresh = ini.GetBool("app", "autorefresh", true);
                s.LaunchMethodValue = ini.GetInt("app", "launchmethod", 0);
                s.WindowX = ini.GetInt("app", "windowx", -1);
                s.WindowY = ini.GetInt("app", "windowy", -1);
                s.WindowW = ini.GetInt("app", "windoww", 900);
                s.WindowH = ini.GetInt("app", "windowh", 560);

                string ignored = ini.Get("app", "ignoreddirs", "");
                if (ignored.Length > 0)
                {
                    foreach (string piece in ignored.Split('|'))
                    {
                        string one = piece.Trim();
                        if (one.Length > 0) { s.IgnoreDir(one); }
                    }
                }

                foreach (string section in ini.Sections)
                {
                    if (!section.StartsWith("profile:", StringComparison.OrdinalIgnoreCase)) { continue; }
                    int id;
                    if (!int.TryParse(section.Substring(8), NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) { continue; }
                    Profile p = new Profile();
                    p.Id = id;
                    p.Name = ini.Get(section, "name", "账号 " + id.ToString(CultureInfo.InvariantCulture));
                    p.Dir = NormalizeDir(ini.Get(section, "dir", ""));
                    p.IsDefault = ini.GetBool(section, "default", false);
                    p.Note = ini.Get(section, "note", "");
                    if (string.IsNullOrEmpty(p.Dir)) { continue; }
                    s.Profiles.Add(p);
                }
                s.Profiles.Sort(CompareProfiles);
            }
            catch (Exception ex)
            {
                Log.Error("读取设置失败，使用默认设置", ex);
            }
            return s;
        }

        private static int CompareProfiles(Profile a, Profile b)
        {
            if (a.IsDefault != b.IsDefault) { return a.IsDefault ? -1 : 1; }
            return a.Id.CompareTo(b.Id);
        }

        // ---------------------------------------------------------- 写
        /// <summary>保存设置。返回 false 表示写不进去（例如文件被杀毒/权限挡住），调用方应该提示用户。</summary>
        internal bool Save()
        {
            try
            {
                AppPaths.EnsureDir(AppPaths.ConfigDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("; 拓竹多账号启动器 配置文件");
                sb.AppendLine("; 可以直接手动编辑，改完重新打开程序生效。");
                sb.AppendLine("[app]");
                sb.AppendLine("studioexe=" + StudioExe);
                sb.AppendLine("extraargs=" + ExtraArgs);
                sb.AppendLine("profileroot=" + ProfileRoot);
                sb.AppendLine("lastprofile=" + LastProfileId.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("firstrun=" + (FirstRunDone ? "1" : "0"));
                sb.AppendLine("remember=" + (RememberWindow ? "1" : "0"));
                sb.AppendLine("dedupe=" + (DedupeLaunch ? "1" : "0"));
                sb.AppendLine("autorefresh=" + (AutoRefresh ? "1" : "0"));
                sb.AppendLine("launchmethod=" + LaunchMethodValue.ToString());
                sb.AppendLine("windowx=" + WindowX.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("windowy=" + WindowY.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("windoww=" + WindowW.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("windowh=" + WindowH.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("ignoreddirs=" + JoinIgnored());

                foreach (Profile p in Profiles)
                {
                    sb.AppendLine();
                    sb.AppendLine("[profile:" + p.Id.ToString(CultureInfo.InvariantCulture) + "]");
                    sb.AppendLine("name=" + OneLine(p.Name));
                    sb.AppendLine("dir=" + p.Dir);
                    sb.AppendLine("default=" + (p.IsDefault ? "1" : "0"));
                    sb.AppendLine("note=" + OneLine(p.Note));
                }
                File.WriteAllText(AppPaths.SettingsFile, sb.ToString(), new UTF8Encoding(true));
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("保存设置失败", ex);
                return false;
            }
        }

        /// <summary>忽略列表写进 ini 时用 | 分隔。</summary>
        private string JoinIgnored()
        {
            StringBuilder sb = new StringBuilder();
            foreach (string dir in IgnoredDirs)
            {
                string one = OneLine(dir).Replace("|", "_");
                if (one.Length == 0) { continue; }
                if (sb.Length > 0) { sb.Append('|'); }
                sb.Append(one);
            }
            return sb.ToString();
        }

        private static string OneLine(string text)
        {
            if (text == null) { return ""; }
            return text.Replace("\r", " ").Replace("\n", " ");
        }
    }

    /// <summary>够用的 INI 解析器（不支持注释以外的花哨语法）。</summary>
    internal sealed class IniFile
    {
        private readonly Dictionary<string, Dictionary<string, string>> _data =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _order = new List<string>();

        internal IEnumerable<string> Sections { get { return _order; } }

        internal static IniFile Load(string path)
        {
            IniFile ini = new IniFile();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            string section = "";
            ini.EnsureSection(section);
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) { continue; }
                if (line[0] == ';' || line[0] == '#') { continue; }
                if (line[0] == '[')
                {
                    int end = line.IndexOf(']');
                    if (end > 0)
                    {
                        section = line.Substring(1, end - 1).Trim();
                        ini.EnsureSection(section);
                    }
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0) { continue; }
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                ini._data[section][key] = val;
            }
            return ini;
        }

        private void EnsureSection(string section)
        {
            if (!_data.ContainsKey(section))
            {
                _data[section] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _order.Add(section);
            }
        }

        internal void Set(string section, string key, string value)
        {
            EnsureSection(section);
            _data[section][key] = value == null ? "" : value;
        }

        internal void Save(string path)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string section in _order)
            {
                if (section.Length > 0) { sb.AppendLine("[" + section + "]"); }
                foreach (KeyValuePair<string, string> pair in _data[section])
                {
                    sb.AppendLine(pair.Key + "=" + pair.Value);
                }
                if (section.Length > 0) { sb.AppendLine(); }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
        }

        internal string Get(string section, string key, string fallback)
        {
            Dictionary<string, string> dict;
            if (_data.TryGetValue(section, out dict))
            {
                string v;
                if (dict.TryGetValue(key, out v)) { return v; }
            }
            return fallback;
        }

        internal int GetInt(string section, string key, int fallback)
        {
            int v;
            if (int.TryParse(Get(section, key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) { return v; }
            return fallback;
        }

        internal bool GetBool(string section, string key, bool fallback)
        {
            string v = Get(section, key, "");
            if (v.Length == 0) { return fallback; }
            if (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase)) { return true; }
            if (v == "0" || v.Equals("false", StringComparison.OrdinalIgnoreCase) || v.Equals("no", StringComparison.OrdinalIgnoreCase)) { return false; }
            return fallback;
        }
    }
}
