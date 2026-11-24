using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace BambuMultiLauncher
{
    /// <summary>高级设置：给愿意折腾的人用的开关都在这里。</summary>
    internal sealed class AdvancedForm : Form
    {
        private readonly Settings _settings;

        private TextBox _txtStudio;
        private TextBox _txtArgs;
        private TextBox _txtRoot;
        private CheckBox _chkRemember;
        private CheckBox _chkDedupe;
        private CheckBox _chkRefresh;
        private ComboBox _cboMethod;
        private Label _lblStatus;

        internal AdvancedForm(Settings settings)
        {
            _settings = settings;

            Text = "高级设置";
            Font = Ui.BaseFont;
            BackColor = Ui.Card;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Ui.Px(660), Ui.Px(624));
            BuildUi();
            LoadValues();
        }

        private void BuildUi()
        {
            Panel canvas;
            Panel footer = Ui.Stack(this, 640, 560, 60, out canvas);

            int left = Ui.Px(24);
            int width = canvas.Width - Ui.Px(48);

            Label title = Ui.Label("高级设置", Ui.H2Font, Ui.Text);
            title.Location = new Point(left, Ui.Px(16));
            canvas.Controls.Add(title);

            Label sub = Ui.Label("一般情况不用改。改完点“确定”，下次启动生效。", Ui.SubFont, Ui.TextDim);
            sub.Location = new Point(left, Ui.Px(44));
            canvas.Controls.Add(sub);

            // Bambu Studio 路径
            Label l1 = Ui.Label("Bambu Studio 主程序", Ui.BaseFont, Ui.Text);
            l1.Location = new Point(left, Ui.Px(78));
            canvas.Controls.Add(l1);

            _txtStudio = new TextBox();
            _txtStudio.Location = new Point(left, Ui.Px(100));
            _txtStudio.Size = new Size(width - Ui.Px(180), Ui.Px(24));
            canvas.Controls.Add(_txtStudio);

            Button browseExe = Ui.SecondaryButton("浏览…", 70);
            browseExe.Location = new Point(left + width - Ui.Px(168), Ui.Px(96));
            browseExe.Click += delegate { PickExe(); };
            canvas.Controls.Add(browseExe);

            Button auto = Ui.SecondaryButton("自动检测", 86);
            auto.Location = new Point(left + width - Ui.Px(88), Ui.Px(96));
            auto.Click += delegate
            {
                List<string> found = StudioLocator.FindAll(_txtStudio.Text.Trim());
                if (found.Count == 0)
                {
                    SetStatus("没有自动找到。可能是装在别的位置，请手动浏览指定。", Ui.Warn);
                }
                else if (found.Count == 1)
                {
                    _txtStudio.Text = found[0];
                    SetStatus("找到了：" + found[0], Ui.Ok);
                }
                else
                {
                    using (StudioPickerForm picker = new StudioPickerForm(found, _txtStudio.Text.Trim()))
                    {
                        if (picker.ShowDialog(this) == DialogResult.OK && !string.IsNullOrEmpty(picker.Picked))
                        {
                            _txtStudio.Text = picker.Picked;
                            SetStatus("找到了 " + found.Count + " 个，已选：" + picker.Picked, Ui.Ok);
                        }
                        else
                        {
                            SetStatus("找到 " + found.Count + " 个 Bambu Studio，没有选择。", Ui.TextDim);
                        }
                    }
                }
            };
            canvas.Controls.Add(auto);

            // 附加参数
            Label l2 = Ui.Label("启动时附加的命令行参数（可选）", Ui.BaseFont, Ui.Text);
            l2.Location = new Point(left, Ui.Px(140));
            canvas.Controls.Add(l2);

            _txtArgs = new TextBox();
            _txtArgs.Location = new Point(left, Ui.Px(162));
            _txtArgs.Size = new Size(width, Ui.Px(24));
            canvas.Controls.Add(_txtArgs);

            Label argsHint = Ui.Label("会原样加到 bambu-studio.exe 后面。例如日志等级或自定义插件参数；一般留空。",
                Ui.SubFont, Ui.TextDim);
            argsHint.Location = new Point(left, Ui.Px(190));
            canvas.Controls.Add(argsHint);

            // 数据根目录
            Label l3 = Ui.Label("新账号的数据根目录", Ui.BaseFont, Ui.Text);
            l3.Location = new Point(left, Ui.Px(222));
            canvas.Controls.Add(l3);

            _txtRoot = new TextBox();
            _txtRoot.Location = new Point(left, Ui.Px(244));
            _txtRoot.Size = new Size(width - Ui.Px(180), Ui.Px(24));
            canvas.Controls.Add(_txtRoot);

            Button browseRoot = Ui.SecondaryButton("浏览…", 70);
            browseRoot.Location = new Point(left + width - Ui.Px(168), Ui.Px(240));
            browseRoot.Click += delegate { PickRoot(); };
            canvas.Controls.Add(browseRoot);

            Button resetRoot = Ui.SecondaryButton("恢复默认", 86);
            resetRoot.Location = new Point(left + width - Ui.Px(88), Ui.Px(240));
            resetRoot.Click += delegate { _txtRoot.Text = AppPaths.DefaultProfileRoot; };
            canvas.Controls.Add(resetRoot);

            Label rootHint = Ui.Label("留空 = 默认位置（%APPDATA%\\BambuMultiLauncher\\profiles）。只影响以后新加的账号。",
                Ui.SubFont, Ui.TextDim);
            rootHint.Location = new Point(left, Ui.Px(272));
            canvas.Controls.Add(rootHint);

            // 行为开关
            Label l4 = Ui.Label("行为", Ui.BaseFont, Ui.Text);
            l4.Location = new Point(left, Ui.Px(304));
            canvas.Controls.Add(l4);

            _chkDedupe = MakeCheck(canvas, "同一个账号已经在运行时不重复启动（推荐）", left, Ui.Px(328));
            _chkRemember = MakeCheck(canvas, "记住窗口大小和位置", left, Ui.Px(354));
            _chkRefresh = MakeCheck(canvas, "自动刷新运行状态（每 2.5 秒）", left, Ui.Px(380));

            Label note = Ui.Label("关掉“自动刷新”可以降低老机器上的后台开销，手动点“刷新”即可更新。",
                Ui.SubFont, Ui.TextDim);
            note.Location = new Point(left + Ui.Px(20), Ui.Px(406));
            canvas.Controls.Add(note);

            // 启动方式
            Label l6 = Ui.Label("启动方式", Ui.BaseFont, Ui.Text);
            l6.Location = new Point(left, Ui.Px(432));
            canvas.Controls.Add(l6);

            _cboMethod = new ComboBox();
            _cboMethod.DropDownStyle = ComboBoxStyle.DropDownList;
            _cboMethod.Font = Ui.BaseFont;
            _cboMethod.Items.Add("自动（推荐）");
            _cboMethod.Items.Add("双击方式（和双击快捷方式一样）");
            _cboMethod.Items.Add("常规方式（带命令行参数启动）");
            _cboMethod.Location = new Point(left + Ui.Px(86), Ui.Px(428));
            _cboMethod.Size = new Size(Ui.Px(280), Ui.Px(26));
            canvas.Controls.Add(_cboMethod);

            Label methodHint = Ui.Label("窗口一闪就没、或者起不来时换一种再试。自动＝先用双击方式，不行再换常规方式。",
                Ui.SubFont, Ui.TextDim);
            methodHint.Location = new Point(left, Ui.Px(460));
            methodHint.AutoSize = false;
            methodHint.Size = new Size(Ui.Px(560), Ui.Px(20));
            canvas.Controls.Add(methodHint);

            // 快捷入口
            Label l5 = Ui.Label("诊断", Ui.BaseFont, Ui.Text);
            l5.Location = new Point(left, Ui.Px(492));
            canvas.Controls.Add(l5);

            Button report = Ui.SecondaryButton("环境报告", 96);
            report.Location = new Point(left, Ui.Px(514));
            report.Click += delegate
            {
                using (ReportForm form = new ReportForm("环境报告", EnvironmentReport.Build(_settings)))
                {
                    form.ShowDialog(this);
                }
            };
            canvas.Controls.Add(report);

            Button openCfg = Ui.SecondaryButton("配置目录", 96);
            openCfg.Location = new Point(left + Ui.Px(104), Ui.Px(514));
            openCfg.Click += delegate { OpenDir(AppPaths.ConfigDir, "配置目录"); };
            canvas.Controls.Add(openCfg);

            Button openLog = Ui.SecondaryButton("日志目录", 96);
            openLog.Location = new Point(left + Ui.Px(208), Ui.Px(514));
            openLog.Click += delegate { OpenDir(AppPaths.LogDir, "日志目录"); };
            canvas.Controls.Add(openLog);

            Button test = Ui.SecondaryButton("启动测试", 96);
            test.Location = new Point(left + Ui.Px(312), Ui.Px(514));
            test.Click += delegate { RunStartupTest(); };
            canvas.Controls.Add(test);

            // 底部：状态 + 确定/取消
            _lblStatus = new Label();
            _lblStatus.AutoSize = false;
            _lblStatus.Font = Ui.SubFont;
            _lblStatus.ForeColor = Ui.TextDim;
            _lblStatus.Location = new Point(Ui.Px(24), Ui.Px(20));
            _lblStatus.Size = new Size(Ui.Px(430), Ui.Px(20));
            _lblStatus.AutoEllipsis = true;
            footer.Controls.Add(_lblStatus);

            Button ok = Ui.PrimaryButton("确定", 90);
            ok.Location = new Point(ClientSize.Width - Ui.Px(130), Ui.Px(14));
            ok.Click += delegate { OnOk(); };
            footer.Controls.Add(ok);
            AcceptButton = ok;

            Button cancel = Ui.SecondaryButton("取消", 90);
            cancel.Location = new Point(ok.Left - cancel.Width - Ui.Px(8), Ui.Px(14));
            cancel.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(cancel);
            CancelButton = cancel;

            footer.Resize += delegate
            {
                _lblStatus.Width = Math.Max(Ui.Px(120), cancel.Left - Ui.Px(40));
                ok.Location = new Point(footer.Width - ok.Width - Ui.Px(14), Ui.Px(14));
                cancel.Location = new Point(ok.Left - cancel.Width - Ui.Px(8), Ui.Px(14));
            };
        }

        private CheckBox MakeCheck(Control parent, string text, int x, int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Font = Ui.BaseFont;
            c.ForeColor = Ui.Text;
            c.AutoSize = true;
            c.Location = new Point(x, y);
            parent.Controls.Add(c);
            return c;
        }

        private void LoadValues()
        {
            _txtStudio.Text = _settings.StudioExe;
            _txtArgs.Text = _settings.ExtraArgs;
            _txtRoot.Text = string.IsNullOrEmpty(_settings.ProfileRoot)
                ? AppPaths.DefaultProfileRoot
                : _settings.ProfileRoot;
            _chkRemember.Checked = _settings.RememberWindow;
            _chkDedupe.Checked = _settings.DedupeLaunch;
            _chkRefresh.Checked = _settings.AutoRefresh;
            int method = _settings.LaunchMethodValue;
            if (method < 0 || method > 2) { method = 0; }
            _cboMethod.SelectedIndex = method;
            SetStatus("数据目录：" + _settings.EffectiveProfileRoot, Ui.TextDim);
        }

        private void SetStatus(string text, Color color)
        {
            _lblStatus.Text = text;
            _lblStatus.ForeColor = color;
        }

        /// <summary>打开一个目录，打不开就说清楚为什么，而不是点了没反应。</summary>
        private void OpenDir(string path, string label)
        {
            try
            {
                if (!Directory.Exists(path)) { Directory.CreateDirectory(path); }
            }
            catch { }

            string error;
            if (LaunchService.OpenFolder(path, out error))
            {
                SetStatus("已在资源管理器里打开" + label + "：" + path, Ui.Ok);
                return;
            }
            SetStatus("打不开" + label + "：" + error, Ui.Danger);
            MessageBox.Show(this,
                "打不开这个目录：\r\n" + path + "\r\n\r\n" + error +
                "\r\n\r\n可以把路径复制到资源管理器的地址栏里打开。",
                AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void RunStartupTest()
        {
            Profile target = null;
            foreach (Profile p in _settings.Profiles)
            {
                if (p.IsDefault) { target = p; break; }
            }
            if (target == null && _settings.Profiles.Count > 0) { target = _settings.Profiles[0]; }
            if (target == null)
            {
                SetStatus("还没有账号，先回主界面添加一个。", Ui.Warn);
                return;
            }

            string report = "";
            bool repaired = false;
            Exception error = null;
            using (BusyForm busy = new BusyForm("启动测试",
                "正在试着启动“" + target.Name + "”，大约十几秒…",
                delegate
                {
                    try { report = LaunchDiagnostics.Run(_settings, target, out repaired); }
                    catch (Exception ex) { error = ex; }
                }))
            {
                busy.ShowDialog(this);
                if (busy.Error != null) { error = busy.Error; }
            }

            if (error != null)
            {
                SetStatus("测试出错：" + error.Message, Ui.Danger);
                return;
            }
            SetStatus(repaired ? "已经补上必要设置，细节见报告。" : "测试完成，细节见报告。",
                repaired ? Ui.Ok : Ui.TextDim);
            using (ReportForm form = new ReportForm("启动测试 · " + target.Name, report))
            {
                form.ShowDialog(this);
            }
        }

        private void PickExe()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 bambu-studio.exe";
                dlg.Filter = "Bambu Studio|bambu-studio.exe|程序|*.exe";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _txtStudio.Text = dlg.FileName;
                }
            }
        }

        private void PickRoot()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择新账号数据目录的父目录";
                dlg.ShowNewFolderButton = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _txtRoot.Text = dlg.SelectedPath;
                }
            }
        }

        private void OnOk()
        {
            string studio = _txtStudio.Text.Trim().Trim('"');
            if (studio.Length > 0 && !File.Exists(studio))
            {
                MessageBox.Show(this, "这个位置没有找到 bambu-studio.exe：\r\n" + studio,
                    AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string root = _txtRoot.Text.Trim().Trim('"');
            if (root.Length > 0)
            {
                try
                {
                    AppPaths.EnsureDir(root);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "这个目录不能用：" + ex.Message, AppPaths.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (string.Equals(Path.GetFullPath(root).TrimEnd('\\'),
                        Path.GetFullPath(AppPaths.DefaultProfileRoot).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                {
                    root = "";
                }
            }

            _settings.StudioExe = studio;
            _settings.ExtraArgs = _txtArgs.Text.Trim();
            _settings.ProfileRoot = root;
            _settings.RememberWindow = _chkRemember.Checked;
            _settings.DedupeLaunch = _chkDedupe.Checked;
            _settings.AutoRefresh = _chkRefresh.Checked;
            _settings.LaunchMethodValue = _cboMethod.SelectedIndex < 0 ? 0 : _cboMethod.SelectedIndex;
            if (!_settings.Save())
            {
                SetStatus("配置没能写入，检查一下配置文件是不是只读或被占用。", Ui.Danger);
                MessageBox.Show(this, "配置没能写入：\r\n" + AppPaths.SettingsFile +
                    "\r\n\r\n可能是文件只读、被杀毒软件锁住，或者这个目录没有写权限。\r\n改完再点一次“确定”试试。",
                    AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>长文本展示窗口（环境报告、帮助等）。</summary>
    internal sealed class ReportForm : Form
    {
        internal ReportForm(string caption, string text)
        {
            Text = caption;
            Font = Ui.BaseFont;
            BackColor = Ui.Card;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Ui.Px(720), Ui.Px(520));
            MinimumSize = new Size(Ui.Px(520), Ui.Px(360));

            TextBox box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.ScrollBars = ScrollBars.Both;
            box.WordWrap = false;
            box.Font = new Font("Consolas", 9F);
            box.Text = text;
            box.Dock = DockStyle.Fill;
            box.BackColor = Color.White;
            box.BorderStyle = BorderStyle.None;
            Controls.Add(box);

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = Ui.Px(52);
            bottom.BackColor = Ui.Card;
            Controls.Add(bottom);

            Panel line = new Panel();
            line.Dock = DockStyle.Top;
            line.Height = 1;
            line.BackColor = Ui.Border;
            bottom.Controls.Add(line);

            Button copy = Ui.SecondaryButton("复制全文", 96);
            copy.Location = new Point(Ui.Px(12), Ui.Px(10));
            copy.Click += delegate
            {
                try { Clipboard.SetText(box.Text); copy.Text = "已复制"; }
                catch { }
            };
            bottom.Controls.Add(copy);

            Button close = Ui.PrimaryButton("关闭", 90);
            close.DialogResult = DialogResult.OK;
            bottom.Controls.Add(close);
            bottom.Resize += delegate
            {
                close.Location = new Point(bottom.Width - close.Width - Ui.Px(12), Ui.Px(10));
            };
            bottom.PerformLayout();
            AcceptButton = close;
            CancelButton = close;
        }
    }

    /// <summary>把排查需要的环境信息汇总成一份纯文本。</summary>
    internal static class EnvironmentReport
    {
        internal static string Build(Settings settings)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("拓竹多账号启动器 · 环境报告");
            sb.AppendLine("生成时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("版本：" + AppPaths.Version);
            sb.AppendLine();

            sb.AppendLine("== 系统 ==");
            sb.AppendLine("Windows：" + Environment.OSVersion.VersionString + "（" + (Environment.Is64BitOperatingSystem ? "64 位" : "32 位") + "）");
            sb.AppendLine("用户：" + Environment.UserName);
            sb.AppendLine("DPI 缩放：" + (Ui.Scale * 100).ToString("0") + "%");
            sb.AppendLine();

            sb.AppendLine("== Bambu Studio ==");
            string exe = settings.StudioExe;
            bool exists = !string.IsNullOrEmpty(exe) && File.Exists(exe);
            sb.AppendLine("配置的记录：" + (string.IsNullOrEmpty(exe) ? "（空）" : exe));
            sb.AppendLine("该文件存在：" + (exists ? "是" : "否"));
            List<string> all = StudioLocator.FindAll(settings.StudioExe);
            sb.AppendLine("检测到 " + all.Count + " 个可用位置：");
            foreach (string path in all) { sb.AppendLine("  - " + path + "  版本 " + StudioLocator.VersionOf(path)); }
            sb.AppendLine();

            sb.AppendLine("== 数据目录 ==");
            sb.AppendLine("Bambu Studio 默认目录：" + AppPaths.BambuDefaultDataDir + "（" + (Directory.Exists(AppPaths.BambuDefaultDataDir) ? "存在" : "不存在") + "）");
            sb.AppendLine("本工具配置目录：" + AppPaths.ConfigDir);
            sb.AppendLine("新账号根目录：" + settings.EffectiveProfileRoot);
            sb.AppendLine("日志：  " + Log.CurrentFile);
            sb.AppendLine();

            sb.AppendLine("== 账号 ==");
            if (settings.Profiles.Count == 0) { sb.AppendLine("（还没有账号）"); }
            List<RunningInstance> running = LaunchService.QueryInstances();
            foreach (Profile p in settings.Profiles)
            {
                int pid = LaunchService.GetRunningPid(p);
                sb.AppendLine("· " + p.Name + (p.IsDefault ? "  [默认]" : ""));
                sb.AppendLine("    目录：" + p.Dir + (Directory.Exists(p.Dir) ? "" : "  （不存在！）"));
                sb.AppendLine("    登录凭据：" + (p.CredentialBytes == 0
                ? "无（没登录过）"
                : p.CredentialBytes + " 字节（" + (p.HasLogin ? "已登录" : "空壳，实际未登录") + "）"));
                sb.AppendLine("    配置文件：" + (p.PrefsExist ? "有" : "无")
                    + "    插件：" + (LaunchService.HasPlugins(p.Dir) ? "有" : "无")
                    + "    single_instance：" + ProfileRepair.ReadSingleInstance(p.Dir));
                sb.AppendLine("    进程：" + (pid > 0 ? "运行中 PID " + pid : "未运行"));
            }
            sb.AppendLine();

            sb.AppendLine("== 正在运行的 bambu-studio.exe ==");
            if (running.Count == 0) { sb.AppendLine("（没有检测到，或 WMI 不可用）"); }
            foreach (RunningInstance ri in running)
            {
                sb.AppendLine("PID " + ri.Pid + "  datadir=" + (string.IsNullOrEmpty(ri.DataDir) ? "（默认）" : ri.DataDir));
            }
            sb.AppendLine();
            sb.AppendLine("把这段内容发给开发者，一般就能定位问题。");
            return sb.ToString();
        }
    }
}
