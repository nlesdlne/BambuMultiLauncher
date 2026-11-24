using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BambuMultiLauncher;

/// <summary>
/// 专项验证：非默认账号启动前，会先在它的数据目录里把 single_instance 确认成 false。
/// 编译（在仓库根目录）：
///   csc /nologo /target:exe /codepage:65001 /out:tests\pathtest.exe src\*.cs tests\pathtest.cs /main:BambuMultiLauncher.PathTestExe /r:...Forms.dll /r:...Drawing.dll /r:...Management.dll /r:...Core.dll   （排除 src\Program.cs）
/// </summary>
internal static class PathTestExe
{
    internal static int Main(string[] args)
    {
        string root = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "bml-pathtest");
        if (Directory.Exists(root)) { Directory.Delete(root, true); }
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("BAMBUMULTI_HOME", Path.Combine(root, "cfg"));

        string profileDir = Path.Combine(root, "p2");
        Directory.CreateDirectory(profileDir);
        File.WriteAllText(Path.Combine(profileDir, "BambuStudio.conf"),
            "{\r\n  \"single_instance\": true,\r\n  \"region\": \"China\"\r\n}\r\n", new UTF8Encoding(false));

        Settings s = Settings.Load();
        s.StudioExe = args.Length > 1 ? args[1] : @"D:\DeepSeek\BambuMultiLauncher\tests\bambu-sleeper.exe";
        Profile p = new Profile();
        p.Id = 2;
        p.Name = "测试账号";
        p.Dir = profileDir;
        s.Profiles.Add(p);

        LaunchResult r = LaunchService.Launch(s, p, LaunchMethod.Shell);

        string conf = File.ReadAllText(Path.Combine(profileDir, "BambuStudio.conf"));
        bool fixedValue = conf.IndexOf("\"single_instance\": false", StringComparison.Ordinal) >= 0;
        bool hasBackup = File.Exists(Path.Combine(profileDir, "BambuStudio.conf.bak"));

        StringBuilder report = new StringBuilder();
        report.AppendLine("status=" + r.Status);
        report.AppendLine("repairNote=" + r.RepairNote);
        report.AppendLine("single_instance_false=" + fixedValue);
        report.AppendLine("backup_created=" + hasBackup);
        report.AppendLine("conf=" + Environment.NewLine + conf);
        File.WriteAllText(Path.Combine(root, "pathtest-result.txt"), report.ToString(), new UTF8Encoding(false));
        return fixedValue && hasBackup ? 0 : 1;
    }
}
