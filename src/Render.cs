using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace BambuMultiLauncher
{
    /// <summary>
    /// 开发用：把各个窗口离屏画成 PNG，方便在没有显示器的环境里检查排版。
    /// 用法：拓竹多账号启动器.exe --render D:\out
    /// </summary>
    internal static class Render
    {
        internal static void Run(string dir)
        {
            try { Directory.CreateDirectory(dir); } catch { }

            // 渲染会改动 settings.ini（强制 firstrun 等），绝不能写到用户真正的配置里。
            // 没有指定 BAMBUMULTI_HOME 时就地建一个隔离目录。
            string home = Environment.GetEnvironmentVariable("BAMBUMULTI_HOME");
            if (string.IsNullOrEmpty(home))
            {
                home = Path.Combine(dir, "render-home");
                Environment.SetEnvironmentVariable("BAMBUMULTI_HOME", home);
                Log.Info("渲染使用隔离的配置目录：" + home);
            }

            Settings settings = Settings.Load();
            settings.FirstRunDone = true;
            settings.RememberWindow = false;
            if (settings.Profiles.Count == 0) { Seed(settings, dir); }
            settings.Save();

            Shot(new MainForm(), Path.Combine(dir, "01-main.png"));

            Settings fresh = Settings.Load();
            fresh.FirstRunDone = false;
            for (int step = 0; step < 3; step++)
            {
                FirstRunForm wizard = new FirstRunForm(fresh);
                wizard.RenderStep(step);
                Shot(wizard, Path.Combine(dir, "0" + (step + 2) + "-wizard" + (step + 1) + ".png"));
            }

            Shot(new AccountForm(settings, null), Path.Combine(dir, "05-account.png"));
            Shot(new AdvancedForm(settings), Path.Combine(dir, "06-advanced.png"));
            Shot(new HelpForm(), Path.Combine(dir, "07-help.png"));

            Log.Info("渲染完成：" + dir);
        }

        private static void Seed(Settings settings, string dir)
        {
            // 截图/文档用：路径写成真实用户会看到的样子（这些目录不一定会真的建出来）
            string home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BambuMultiLauncher", "profiles", "家里打印机");
            try { Directory.CreateDirectory(Path.Combine(home, "plugins")); }
            catch { }

            Profile first = new Profile();
            first.Name = "公司账号（默认）";
            first.Dir = AppPaths.BambuDefaultDataDir;
            first.IsDefault = true;
            first.Note = "Bambu Studio 原来的数据目录";
            first.Id = settings.NextId();
            settings.Profiles.Add(first);

            Profile second = new Profile();
            second.Name = "家里打印机";
            second.Dir = home;
            second.Note = "独立数据目录";
            second.Id = settings.NextId();
            settings.Profiles.Add(second);

            try
            {
                string exe = StudioLocator.Find(settings.StudioExe);
                if (exe != null) { settings.StudioExe = exe; }
            }
            catch { }
        }

        private static void Shot(Form form, string path)
        {
            try
            {
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(0, 0);
                form.Show();
                form.PerformLayout();
                Application.DoEvents();
                System.Threading.Thread.Sleep(180);
                Application.DoEvents();

                int w = form.Width;
                int h = form.Height;
                using (Bitmap bmp = new Bitmap(w, h))
                {
                    form.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                    bmp.Save(path, ImageFormat.Png);
                }
                Log.Info("渲染 " + Path.GetFileName(path) + " " + w + "x" + h);
            }
            catch (Exception ex)
            {
                Log.Error("渲染失败 " + path, ex);
            }
            finally
            {
                try { form.Hide(); form.Dispose(); } catch { }
            }
        }
    }
}
