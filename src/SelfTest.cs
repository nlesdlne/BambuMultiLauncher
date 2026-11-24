using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace BambuMultiLauncher
{
    /// <summary>
    /// 开发/CI 用自检：把不依赖界面的逻辑（配置读写、参数解析、复制、去重启动）跑一遍。
    /// 用法：拓竹多账号启动器.exe --selftest D:\out
    /// 结果写进 out\selftest.log，退出码 = 失败数（0 表示全过）。
    /// </summary>
    internal static class SelfTest
    {
        private static int _passed;
        private static int _failed;
        private static StringBuilder _log = new StringBuilder();

        internal static int Run(string dir)
        {
            // 自检全程用自己的临时配置目录，绝不碰用户真正的 %APPDATA%
            try
            {
                Directory.CreateDirectory(dir);
                string home = Path.Combine(dir, "home");
                Directory.CreateDirectory(home);
                Environment.SetEnvironmentVariable("BAMBUMULTI_HOME", home);
            }
            catch { }

            _log.AppendLine("拓竹多账号启动器 自检 " + AppPaths.Version);
            _log.AppendLine("时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            _log.AppendLine("机器：" + Environment.OSVersion.VersionString + " / .NET " + Environment.Version);
            _log.AppendLine("工作目录：" + dir);
            _log.AppendLine();

            try
            {
                TestIniAndSettings(dir);
                TestProfileState(dir);
                TestExtractDataDir();
                TestSafeFolderName();
                TestCopySharedData(dir);
                TestRepair(dir);
                TestLegacyScanner(dir);
                TestLocator();
                TestLaunchDedupe(dir);
            }
            catch (Exception ex)
            {
                Fail("自检本身抛异常", ex.ToString());
            }

            _log.AppendLine();
            _log.AppendLine("通过 " + _passed + " 项，失败 " + _failed + " 项。");
            string file = Path.Combine(dir, "selftest.log");
            try
            {
                File.WriteAllText(file, _log.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex)
            {
                Log.Error("写自检日志失败", ex);
            }
            Log.Info("自检结束：通过 " + _passed + "，失败 " + _failed + " → " + file);
            return _failed;
        }

        // ---------------------------------------------------------------- 用例

        private static void TestIniAndSettings(string dir)
        {
            Section("配置读写");

            string ini = Path.Combine(dir, "ini-test.ini");
            Try("IniFile 写入并读回", delegate
            {
                IniFile f = new IniFile();
                f.Set("app", "name", "拓竹");
                f.Set("app", "count", "3");
                f.Set("profile:1", "dir", @"D:\a b\c");
                f.Save(ini);
                IniFile back = IniFile.Load(ini);
                Check(back.Get("app", "name", "") == "拓竹", "name 不一致：" + back.Get("app", "name", ""));
                Check(back.GetInt("app", "count", 0) == 3, "count 不一致");
                Check(back.Get("profile:1", "dir", "") == @"D:\a b\c", "路径里的空格丢了");
            });

            string home = Path.Combine(dir, "settings-home");
            Try("Settings 保存与读取（含中文名、默认账号）", delegate
            {
                string old = Environment.GetEnvironmentVariable("BAMBUMULTI_HOME");
                Environment.SetEnvironmentVariable("BAMBUMULTI_HOME", home);
                try
                {
                    Settings s = new Settings();
                    s.StudioExe = @"C:\Program Files\Bambu Studio\bambu-studio.exe";
                    s.ExtraArgs = "--loglevel 3";
                    s.LastProfileId = 2;
                    s.FirstRunDone = true;
                    s.RememberWindow = false;
                    s.DedupeLaunch = false;
                    s.LaunchMethodValue = 2;
                    s.WindowX = 120;
                    s.WindowY = 80;

                    Profile a = new Profile();
                    a.Id = 1;
                    a.Name = "公司账号（默认）";
                    a.Dir = AppPaths.BambuDefaultDataDir;
                    a.IsDefault = true;
                    a.Note = "原目录";
                    s.Profiles.Add(a);

                    Profile b = new Profile();
                    b.Id = 2;
                    b.Name = "家里 打印机";
                    b.Dir = Path.Combine(home, "profiles", "家里_打印机");
                    s.Profiles.Add(b);

                    s.Save();

                    Settings back = Settings.Load();
                    Check(back.Profiles.Count == 2, "账号数不对：" + back.Profiles.Count);
                    Check(back.Profiles[0].IsDefault, "默认账号标记丢了");
                    Check(back.Profiles[0].Name == "公司账号（默认）", "名字丢了：" + back.Profiles[0].Name);
                    Check(back.Profiles[1].Name == "家里 打印机", "第二个名字丢了");
                    Check(back.Profiles[1].Dir == b.Dir, "目录不一致");
                    Check(back.LastProfileId == 2, "LastProfileId 丢了");
                    Check(!back.RememberWindow, "记住窗口开关丢了");
                    Check(!back.DedupeLaunch, "去重开关丢了");
                    Check(back.LaunchMethodValue == 2, "启动方式设置丢了：" + back.LaunchMethodValue);
                    Check(LaunchService.MethodOf(back) == LaunchMethod.Direct, "启动方式应该解析成常规方式");
                    Check(back.WindowX == 120 && back.WindowY == 80, "窗口位置丢了");
                    Check(back.ExtraArgs == "--loglevel 3", "附加参数丢了");
                    Check(back.FindById(2) != null && back.FindById(2).Name == "家里 打印机", "FindById 有问题");
                    Check(back.FindById(99) == null, "FindById 应该返回 null");
                    Check(back.NextId() == 3, "NextId 应为 3，实际 " + back.NextId());
                }
                finally
                {
                    Environment.SetEnvironmentVariable("BAMBUMULTI_HOME", old);
                }
            });
        }

        private static void TestProfileState(string dir)
        {
            Section("账号状态");
            Try("HasLogin / PrefsExist 跟着文件走", delegate
            {
                string pdir = Path.Combine(dir, "pstate");
                Directory.CreateDirectory(pdir);

                Profile p = new Profile();
                p.Id = 7;
                p.Name = "测试";
                p.Dir = pdir;

                Check(!p.HasLogin, "空目录应该算未登录");
                Check(!p.PrefsExist, "空目录不应该有偏好文件");

                // 32 字节 = 16 字节文件头 + 16 字节密文，是退出登录/异常中断留下的空壳
                File.WriteAllBytes(p.CredentialFile, new byte[32]);
                File.WriteAllText(p.PrefsFile, "{}");
                Profile stub = new Profile();
                stub.Id = 7;
                stub.Dir = pdir;
                Check(stub.CredentialBytes == 32, "凭据字节数应该是 32");
                Check(!stub.HasLogin, "32 字节的空壳凭据不能算已登录");

                // 真实凭据是 704 / 768 字节这个量级
                File.WriteAllBytes(p.CredentialFile, new byte[704]);
                Profile q = new Profile();
                q.Id = 7;
                q.Dir = pdir;
                Check(q.HasLogin, "有真实大小的凭据文件应该算已登录");
                Check(q.PrefsExist, "有 conf 应该算已初始化");
            });
        }

        private static void TestExtractDataDir()
        {
            Section("命令行解析");
            Try("各种写法都能取到 --datadir", delegate
            {
                Check(LaunchService.ExtractDataDir("\"C:\\x\\bambu-studio.exe\" --datadir \"D:\\home dir\\p1\"") == @"D:\home dir\p1",
                    "带引号带空格解析失败：" + LaunchService.ExtractDataDir("\"C:\\x\\bambu-studio.exe\" --datadir \"D:\\home dir\\p1\""));
                Check(LaunchService.ExtractDataDir("bambu-studio.exe --datadir D:\\p2") == @"D:\p2", "无引号解析失败");
                Check(LaunchService.ExtractDataDir("bambu-studio.exe --DATADIR \"E:\\大写\"") == @"E:\大写", "大写参数解析失败");
                Check(LaunchService.ExtractDataDir("bambu-studio.exe --datadir=D:\\noSpace") == @"D:\noSpace",
                    "等号写法解析失败：" + LaunchService.ExtractDataDir("bambu-studio.exe --datadir=D:\\noSpace"));
                Check(LaunchService.ExtractDataDir("bambu-studio.exe --datadir=\"D:\\a b\"") == @"D:\a b",
                    "等号加引号解析失败：" + LaunchService.ExtractDataDir("bambu-studio.exe --datadir=\"D:\\a b\""));
                Check(LaunchService.ExtractDataDir("bambu-studio.exe --datadir=D:\\a b") == @"D:\a",
                    "等号加空格解析失败：" + LaunchService.ExtractDataDir("bambu-studio.exe --datadir=D:\\a b"));
                Check(LaunchService.ExtractDataDir("bambu-studio.exe") == "", "没有参数时应为空");
                Check(LaunchService.ExtractDataDir("") == "", "空命令行应为空");
                Check(LaunchService.ExtractDataDir("bambu-studio.exe --datadir") == "", "参数后没值应为空");
            });
        }

        private static void TestSafeFolderName()
        {
            Section("目录名净化");
            Try("非法字符被替换", delegate
            {
                string s = LaunchService.SafeFolderName("家里 打印机/二号?");
                Check(s.IndexOf('/') < 0 && s.IndexOf('?') < 0 && s.IndexOf(' ') < 0, "还有非法字符：" + s);
                Check(LaunchService.SafeFolderName("") == "profile", "空名字应回落 profile");
                Check(LaunchService.SafeFolderName("///").Length > 0, "全非法字符也要有结果");
                Check(LaunchService.SafeFolderName(new string('长', 100)).Length <= 40, "长名字应被截断");
                Check(LaunchService.SafeFolderName("a/b:c*?\"<>|") == "a_b_c______", "非法字符的替换个数不对：" + LaunchService.SafeFolderName("a/b:c*?\"<>|"));
                Check(LaunchService.SafeFolderName("NUL").StartsWith("_", StringComparison.Ordinal), "保留名 NUL 应该加前缀：" + LaunchService.SafeFolderName("NUL"));
                Check(LaunchService.SafeFolderName("com1.txt").StartsWith("_", StringComparison.Ordinal), "保留名 com1.txt 应该加前缀：" + LaunchService.SafeFolderName("com1.txt"));
                Check(LaunchService.SafeFolderName("家里").Length > 0, "中文名不该被弄坏");
            });
        }

        private static void TestCopySharedData(string dir)
        {
            Section("复制插件与预设");
            Try("只复制 plugins/ota/user，绝不带凭据", delegate
            {
                string src = Path.Combine(dir, "copy-src");
                string dst = Path.Combine(dir, "copy-dst");
                Directory.CreateDirectory(Path.Combine(src, "plugins", "sub"));
                Directory.CreateDirectory(Path.Combine(src, "user"));
                Directory.CreateDirectory(Path.Combine(src, "printers"));
                Directory.CreateDirectory(dst);

                File.WriteAllText(Path.Combine(src, "plugins", "a.dll"), "dll");
                File.WriteAllText(Path.Combine(src, "plugins", "sub", "b.bin"), "bin");
                File.WriteAllText(Path.Combine(src, "user", "u.txt"), "u");
                File.WriteAllText(Path.Combine(src, "printers", "printer.ini"), "p");
                File.WriteAllText(Path.Combine(src, "BambuNetworkEngine.conf"), "SECRET-CREDENTIAL");
                File.WriteAllText(Path.Combine(src, "BambuStudio.conf"), "{}");
                File.WriteAllText(Path.Combine(dst, "BambuNetworkEngine.conf"), "LEFTOVER");

                LaunchService.CopySharedData(src, dst, null);

                Check(File.Exists(Path.Combine(dst, "plugins", "a.dll")), "插件没复制过去");
                Check(File.Exists(Path.Combine(dst, "plugins", "sub", "b.bin")), "子目录没复制过去");
                Check(File.Exists(Path.Combine(dst, "user", "u.txt")), "预设没复制过去");
                Check(!File.Exists(Path.Combine(dst, "BambuNetworkEngine.conf")), "凭据被复制过去了（严重）");
                Check(!File.Exists(Path.Combine(dst, "BambuStudio.conf")), "conf 被复制过去了");
                Check(!Directory.Exists(Path.Combine(dst, "printers")), "printers 不该复制（设备列表属于账号）");
            });
        }

        private static void TestRepair(string dir)
        {
            Section("老版本兼容修补");

            Try("single_instance true → false", delegate
            {
                string text = "{\r\n  \"single_instance\": true,\r\n  \"x\": 1\r\n}\r\n";
                string patched = ProfileRepair.PatchSingleInstance(text);
                Check(patched != null, "应该有改动");
                Check(patched.IndexOf("\"single_instance\": false", StringComparison.Ordinal) >= 0, "没改成 false");
                Check(patched.IndexOf("\"x\": 1", StringComparison.Ordinal) >= 0, "别的字段被改坏了");
            });

            Try("没有 single_instance 字段就插一个", delegate
            {
                string patched = ProfileRepair.PatchSingleInstance("{\r\n  \"y\": 2\r\n}\r\n");
                Check(patched != null, "应该插进去");
                Check(patched.IndexOf("\"single_instance\": false", StringComparison.Ordinal) >= 0, "没插入");
                Check(patched.IndexOf("\"y\": 2", StringComparison.Ordinal) >= 0, "原字段丢了");
            });

            Try("空对象 {} 插进去后仍是合法 JSON", delegate
            {
                string patched = ProfileRepair.PatchSingleInstance("{}");
                Check(patched != null, "应该有改动");
                Check(patched.IndexOf(",}", StringComparison.Ordinal) < 0, "插出了多余的逗号：" + patched);
                Check(patched.IndexOf("\"single_instance\": false", StringComparison.Ordinal) >= 0, "没插入字段");
                Check(patched.Trim().StartsWith("{", StringComparison.Ordinal) && patched.Trim().EndsWith("}", StringComparison.Ordinal),
                    "花括号不配对：" + patched);
            });

            Try("改不了的时候说明原因，而不是瞎说没字段", delegate
            {
                string problem;
                string notJson = ProfileRepair.PatchSingleInstance("这不是 JSON", out problem);
                Check(notJson == null && problem.IndexOf("JSON", StringComparison.Ordinal) >= 0, "非 JSON 应说明是内容问题：" + problem);

                string badValue = ProfileRepair.PatchSingleInstance("{\r\n  \"single_instance\": \"true\"\r\n}", out problem);
                Check(badValue == null && problem.IndexOf("不是 true/false", StringComparison.Ordinal) >= 0, "类型不对应说明原因：" + problem);

                string alreadyOff = ProfileRepair.PatchSingleInstance("{\r\n  \"single_instance\": false\r\n}", out problem);
                Check(alreadyOff != null && alreadyOff.Contains("\"single_instance\": false"), "本来就是 false 应原样返回：" + alreadyOff);
            });

            Try("配置目录里没有 conf 时写一份最小的", delegate
            {
                string dir2 = Path.Combine(dir, "repair-empty");
                Directory.CreateDirectory(dir2);
                string note;
                bool changed = ProfileRepair.EnsureSingleInstanceOff(dir2, out note);
                Check(changed, "应该创建配置");
                Check(note.Length > 0, "应该说明做了什么");
                Check(ProfileRepair.ReadSingleInstance(dir2).IndexOf("false", StringComparison.Ordinal) >= 0,
                    "读出来应该是 false，实际 " + ProfileRepair.ReadSingleInstance(dir2));
            });

            Try("改之前留备份、重复调用不再改动", delegate
            {
                string dir2 = Path.Combine(dir, "repair-backup");
                Directory.CreateDirectory(dir2);
                string conf = Path.Combine(dir2, "BambuStudio.conf");
                File.WriteAllText(conf, "{\r\n  \"single_instance\": true\r\n}\r\n");
                string note;
                Check(ProfileRepair.EnsureSingleInstanceOff(dir2, out note), "第一次应该改");
                Check(File.Exists(conf + ".bak"), "应该留下 .bak");
                Check(!ProfileRepair.EnsureSingleInstanceOff(dir2, out note), "已经是对的就不该再改");
            });
        }

        private static void TestLegacyScanner(string dir)
        {
            Section("旧数据检测");
            string root = Path.Combine(dir, "legacy-home");
            string old = Environment.GetEnvironmentVariable("BAMBUMULTI_HOME");
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "profiles", "家里打印机"));
                Directory.CreateDirectory(Path.Combine(root, "profiles", "公司打印机"));
                Directory.CreateDirectory(Path.Combine(root, "profiles", "公司打印机", "system"));
                File.WriteAllText(Path.Combine(root, "profiles", "家里打印机", "BambuStudio.conf"), "{}");
                File.WriteAllBytes(Path.Combine(root, "profiles", "家里打印机", "BambuNetworkEngine.conf"), new byte[Profile.MinCredentialBytes * 8]);
                Directory.CreateDirectory(Path.Combine(root, "profiles", "不像数据目录"));

                Environment.SetEnvironmentVariable("BAMBUMULTI_HOME", root);
                Try("能扫到以前留下的账号目录", delegate
                {
                    List<LegacyScanner.Found> found = LegacyScanner.Scan(new Settings());
                    Check(found.Count == 2, "应该是 2 个，实际 " + found.Count);
                });

                Try("导入后出现在列表里，再扫一次不重复加", delegate
                {
                    Settings s = new Settings();
                    int first = LegacyScanner.Import(s);
                    Check(first == 2, "第一次应该导入 2 个，实际 " + first);
                    Check(s.Profiles.Count == 2, "账号数不对：" + s.Profiles.Count);
                    Check(s.Profiles[0].Note == "从以前的安装里找到", "备注没写上");
                    Profile withCred = null;
                    foreach (Profile q in s.Profiles)
                    {
                        if (q.Dir.EndsWith("家里打印机", StringComparison.Ordinal)) { withCred = q; }
                    }
                    Check(withCred != null && withCred.HasLogin, "登录文件应该被认出来");
                    int second = LegacyScanner.Import(s);
                    Check(second == 0, "第二次不该重复导入，实际 " + second);
                    Check(s.Profiles.Count == 2, "账号数被加多了：" + s.Profiles.Count);
                });

                Try("删掉的账号不会被重新认回来", delegate
                {
                    Settings s = new Settings();
                    int first = LegacyScanner.Import(s);
                    Check(first == 2, "先要导入 2 个，实际 " + first);
                    string victimDir = s.Profiles[0].Dir;
                    s.Profiles.RemoveAt(0);
                    s.IgnoreDir(victimDir);
                    int again = LegacyScanner.Import(s);
                    Check(again == 0, "删掉的目录不该再被导入，实际 " + again);
                    Check(s.Profiles.Count == 1, "账号数不对：" + s.Profiles.Count);
                    Check(s.IsIgnored(victimDir), "忽略记录没生效");
                });
            }
            finally
            {
                Environment.SetEnvironmentVariable("BAMBUMULTI_HOME", old);
            }
        }

        private static void TestLocator()
        {
            Section("查找 Bambu Studio");
            Try("能找到本机安装位置", delegate
            {
                List<string> all = StudioLocator.FindAll("");
                Check(all.Count > 0, "一个候选位置都没找到");
                if (all.Count > 0)
                {
                    Check(File.Exists(all[0]), "返回的路径不存在：" + all[0]);
                    string version = StudioLocator.VersionOf(all[0]);
                    _log.AppendLine("      找到：" + all[0] + "（版本 " + (version.Length == 0 ? "未知" : version) + "）");
                }
            });
        }

        private static void TestLaunchDedupe(string dir)
        {
            Section("启动与去重");
            string sleeper = Path.Combine(dir, "bambu-sleeper.exe");
            if (!File.Exists(sleeper))
            {
                _log.AppendLine("      跳过：没有准备 bambu-sleeper.exe（用 tools\\make-sleeper.bat 生成）");
                return;
            }

            Try("第一次启动成功、第二次被去重拦下", delegate
            {
                string profileDir = Path.Combine(dir, "launch-profile");
                Directory.CreateDirectory(profileDir);

                Settings s = new Settings();
                s.StudioExe = sleeper;
                s.DedupeLaunch = true;

                Profile p = new Profile();
                p.Id = 42;
                p.Name = "自检账号";
                p.Dir = profileDir;

                LaunchResult first = LaunchService.Launch(s, p);
                Check(first.Status == LaunchStatus.Started, "首次启动失败：" + first.Status + " " + first.Message);
                Check(first.Pid > 0, "没有拿到 PID");
                Thread.Sleep(600);
                Check(LaunchService.GetRunningPid(p) == first.Pid, "启动后应该能查到 PID");

                LaunchResult second = LaunchService.Launch(s, p);
                Check(second.Status == LaunchStatus.AlreadyRunning, "第二次应该被拦下，实际 " + second.Status);
                Check(second.Pid == first.Pid, "拦下时 PID 应一致");

                try { Process.GetProcessById(first.Pid).Kill(); } catch { }
                Thread.Sleep(400);
                try { File.Delete(Path.Combine(AppPaths.StateDir, "p42.pid")); } catch { }
                Check(LaunchService.GetRunningPid(p) == 0, "进程结束后应该显示未运行");
            });
        }

        // ---------------------------------------------------------------- 工具

        private static void Section(string title)
        {
            _log.AppendLine("== " + title);
        }

        private static void Try(string name, Action body)
        {
            try
            {
                body();
                _log.AppendLine("  [通过] " + name);
                _passed++;
            }
            catch (Exception ex)
            {
                Fail(name, ex.Message);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) { throw new Exception(message); }
        }

        private static void Fail(string name, string message)
        {
            _log.AppendLine("  [失败] " + name + " → " + message);
            _failed++;
        }
    }
}
