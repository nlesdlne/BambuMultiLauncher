using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BambuMultiLauncher
{
    /// <summary>首次运行向导：三步，尽量少让用户思考。</summary>
    internal sealed class FirstRunForm : Form
    {
        private readonly Settings _settings;
        private int _step;

        private Panel[] _panes;
        private Label _lblStep;
        private Button _btnBack;
        private Button _btnNext;
        private Button _btnCancel;

        private Label _envExe;
        private Label _envData;
        private Label _envWrite;

        private TextBox _txtName1;
        private CheckBox _chkSecond;
        private TextBox _txtName2;
        private Label _lblDir2;
        private Label _lblName2Hint;

        private Label _lblSummary;
        private CheckBox _chkLaunch;

        internal Settings Settings { get { return _settings; } }
        internal bool LaunchNow;

        internal FirstRunForm(Settings settings)
        {
            _settings = settings;

            Text = "首次使用 · " + AppPaths.AppName;
            Font = Ui.BaseFont;
            BackColor = Ui.Card;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Ui.Px(820), Ui.Px(600));

            BuildChrome();
            BuildPanes();
            BuildFooter();
            ShowStep(0);
        }

        // ------------------------------------------------------------------ 外框

        private void BuildChrome()
        {
            Label title = Ui.Label("设置你的账号", Ui.TitleFont, Ui.Text);
            title.Location = new Point(Ui.Px(26), Ui.Px(18));
            Controls.Add(title);

            Label sub = Ui.Label("只需要一分钟。以后想改都可以在主界面或高级设置里调整。", Ui.SubFont, Ui.TextDim);
            sub.Location = new Point(Ui.Px(28), Ui.Px(48));
            Controls.Add(sub);

            _lblStep = Ui.Label("第 1 步 / 共 3 步", Ui.SubFont, Ui.TextDim);
            _lblStep.AutoSize = false;
            _lblStep.TextAlign = ContentAlignment.MiddleRight;
            _lblStep.Size = new Size(Ui.Px(150), Ui.Px(20));
            _lblStep.Location = new Point(ClientSize.Width - Ui.Px(26) - _lblStep.Width, Ui.Px(24));
            Controls.Add(_lblStep);

            Panel line = new Panel();
            line.Location = new Point(Ui.Px(26), Ui.Px(76));
            line.Size = new Size(ClientSize.Width - Ui.Px(52), 1);
            line.BackColor = Ui.Border;
            Controls.Add(line);

            Panel footerLine = new Panel();
            footerLine.BackColor = Ui.Border;
            footerLine.Location = new Point(0, ClientSize.Height - Ui.Px(66));
            footerLine.Size = new Size(ClientSize.Width, 1);
            Controls.Add(footerLine);
        }

        private void BuildFooter()
        {
            int y = ClientSize.Height - Ui.Px(50);

            _btnCancel = Ui.SecondaryButton("取消", 80);
            _btnCancel.Location = new Point(ClientSize.Width - Ui.Px(100) - _btnCancel.Width, y);
            _btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(_btnCancel);

            _btnNext = Ui.PrimaryButton("下一步", 100);
            _btnNext.Location = new Point(_btnCancel.Left - _btnNext.Width - Ui.Px(8), y);
            _btnNext.Click += delegate { OnNext(); };
            Controls.Add(_btnNext);
            AcceptButton = _btnNext;

            _btnBack = Ui.SecondaryButton("上一步", 90);
            _btnBack.Location = new Point(_btnNext.Left - _btnBack.Width - Ui.Px(8), y);
            _btnBack.Click += delegate { ShowStep(_step - 1); };
            Controls.Add(_btnBack);

            Label privacy = Ui.Label("所有数据都保存在你自己的电脑上，这个工具不会上传任何东西。",
                Ui.SubFont, Ui.TextDim);
            privacy.Location = new Point(Ui.Px(26), y + Ui.Px(8));
            Controls.Add(privacy);
        }

        // ------------------------------------------------------------------ 三个步骤页

        private void BuildPanes()
        {
            _panes = new Panel[3];
            for (int i = 0; i < _panes.Length; i++)
            {
                Panel pane = new Panel();
                pane.Location = new Point(Ui.Px(26), Ui.Px(88));
                pane.Size = new Size(ClientSize.Width - Ui.Px(52), ClientSize.Height - Ui.Px(166));
                pane.BackColor = Ui.Card;
                pane.Visible = false;
                Controls.Add(pane);
                _panes[i] = pane;
            }

            BuildStep0(_panes[0]);
            BuildStep1(_panes[1]);
            BuildStep2(_panes[2]);
        }

        private static FlowLayoutPanel NewFlow(Panel pane)
        {
            FlowLayoutPanel flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            flow.BackColor = Ui.Card;
            pane.Controls.Add(flow);
            return flow;
        }

        /// <summary>正文段落。width 已经是像素（内容区宽度），不要再缩放。</summary>
        private static Label FlowText(FlowLayoutPanel flow, string text, Font font, Color color, int width)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.AutoSize = true;
            l.MaximumSize = new Size(width, 0);
            l.Margin = new Padding(0, 0, 0, Ui.Px(7));
            flow.Controls.Add(l);
            return l;
        }

        /// <summary>内容区可用宽度：扣掉左右边距和可能出现的竖向滚动条。</summary>
        private int ContentWidth
        {
            get { return ClientSize.Width - Ui.Px(52) - Ui.Px(60); }
        }

        /// <summary>小节标题：上下留白，避免和上一段挤在一起。</summary>
        private static Label FlowHead(FlowLayoutPanel flow, string text, int width, int topGap)
        {
            Label l = FlowText(flow, text, Ui.H2Font, Ui.Text, width);
            l.Margin = new Padding(0, Ui.Px(topGap), 0, Ui.Px(5));
            return l;
        }

        /// <summary>占一整行的容器。width 已经是像素，height 是逻辑值。</summary>
        private static Panel FlowRow(FlowLayoutPanel flow, int width, int height)
        {
            Panel p = new Panel();
            p.Size = new Size(width, Ui.Px(height));
            p.Margin = new Padding(0, 0, 0, Ui.Px(4));
            p.BackColor = Ui.Card;
            flow.Controls.Add(p);
            return p;
        }

        private static Label RowLabel(Panel row, string text, int x, int y)
        {
            Label l = Ui.Label(text, Ui.BaseFont, Ui.Text);
            l.Location = new Point(Ui.Px(x), Ui.Px(y));
            row.Controls.Add(l);
            return l;
        }

        // ---------------------------------------------------------- 第 1 步

        private void BuildStep0(Panel pane)
        {
            FlowLayoutPanel flow = NewFlow(pane);
            int w = ContentWidth;

            FlowHead(flow, "为什么需要它", w, 0);
            FlowText(flow,
                "Bambu Studio 一台电脑只能登录一个拓竹账号。想换账号，就得退出、重新登录，还要收验证码。\r\n\r\n" +
                "这个工具给每个账号准备一份独立的数据目录。每个账号开一个自己的窗口，登录状态各自记住，互不影响，也互不顶号。",
                Ui.BaseFont, Ui.Text, w);

            FlowHead(flow, "先确认一下环境", w, 14);

            _envExe = FlowText(flow, "", Ui.BaseFont, Ui.Text, w);
            _envData = FlowText(flow, "", Ui.BaseFont, Ui.Text, w);
            _envWrite = FlowText(flow, "", Ui.BaseFont, Ui.Text, w);

            Button rescan = Ui.LinkButton("重新检测");
            rescan.Margin = new Padding(0, Ui.Px(2), 0, Ui.Px(10));
            rescan.Click += delegate { RefreshEnvironment(); };
            flow.Controls.Add(rescan);

            Button pick = Ui.LinkButton("没找到？手动指定 bambu-studio.exe 的位置");
            pick.Margin = new Padding(0, 0, 0, Ui.Px(10));
            pick.Click += delegate { PickStudio(); };
            flow.Controls.Add(pick);

            FlowText(flow, "小提示：如果你还没装 Bambu Studio，可以先装好再回来点“重新检测”。",
                Ui.SubFont, Ui.TextDim, w);

            RefreshEnvironment();
        }

        private void RefreshEnvironment()
        {
            string exe = StudioLocator.Find(_settings.StudioExe);
            if (exe.Length > 0)
            {
                _settings.StudioExe = exe;
                string ver = StudioLocator.VersionOf(exe);
                _envExe.Text = "✔ 已找到 Bambu Studio" + (ver.Length > 0 ? "（版本 " + ver + "）" : "");
                _envExe.ForeColor = Ui.Ok;
            }
            else
            {
                _envExe.Text = "✖ 没有找到 Bambu Studio，请手动指定位置";
                _envExe.ForeColor = Ui.Danger;
            }

            if (Directory.Exists(AppPaths.BambuDefaultDataDir))
            {
                _envData.Text = "✔ 找到了现有的数据目录，会把它作为“默认账号”";
                _envData.ForeColor = Ui.Ok;
            }
            else
            {
                _envData.Text = "· 还没有现有数据目录，第一次打开 Bambu Studio 时会自动创建";
                _envData.ForeColor = Ui.TextDim;
            }

            string root = _settings.EffectiveProfileRoot;
            try
            {
                AppPaths.EnsureDir(root);
                string probe = Path.Combine(root, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                _envWrite.Text = "✔ 账号数据可以写在：" + root;
                _envWrite.ForeColor = Ui.Ok;
            }
            catch (Exception ex)
            {
                _envWrite.Text = "✖ 这个位置不能写入（" + ex.Message + "），请在高级设置里换一个目录";
                _envWrite.ForeColor = Ui.Danger;
            }
        }

        private void PickStudio()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 bambu-studio.exe";
                dlg.Filter = "Bambu Studio|bambu-studio.exe|程序|*.exe";
                dlg.CheckFileExists = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _settings.StudioExe = dlg.FileName;
                    RefreshEnvironment();
                }
            }
        }

        // ---------------------------------------------------------- 第 2 步

        private void BuildStep1(Panel pane)
        {
            FlowLayoutPanel flow = NewFlow(pane);
            int w = ContentWidth;

            FlowHead(flow, "给账号起个名字", w, 0);
            FlowText(flow,
                "名字只用来区分窗口，随便填，比如“家里”和“公司”。它和拓竹账号里的昵称没有关系。",
                Ui.BaseFont, Ui.Text, w);

            Panel row1 = FlowRow(flow, w, 34);
            RowLabel(row1, "默认账号", 0, 8);
            _txtName1 = new TextBox();
            _txtName1.Location = new Point(Ui.Px(90), Ui.Px(3));
            _txtName1.Size = new Size(Ui.Px(260), Ui.Px(24));
            _txtName1.MaxLength = 40;
            row1.Controls.Add(_txtName1);

            FlowText(flow, "默认账号 = 你平时直接打开 Bambu Studio 用的那个，它继续使用原来的数据目录，什么都不用搬。",
                Ui.SubFont, Ui.TextDim, w);

            FlowRow(flow, w, 8);

            _chkSecond = new CheckBox();
            _chkSecond.Text = "我再添加一个独立账号（用来登录另一个拓竹账号）";
            _chkSecond.Font = Ui.BaseFont;
            _chkSecond.ForeColor = Ui.Text;
            _chkSecond.AutoSize = true;
            _chkSecond.Margin = new Padding(0, 0, 0, Ui.Px(10));
            _chkSecond.CheckedChanged += delegate { UpdateSecondState(); };
            flow.Controls.Add(_chkSecond);

            Panel row2 = FlowRow(flow, w, 34);
            RowLabel(row2, "第二个账号", 0, 8);
            _txtName2 = new TextBox();
            _txtName2.Location = new Point(Ui.Px(90), Ui.Px(3));
            _txtName2.Size = new Size(Ui.Px(260), Ui.Px(24));
            _txtName2.MaxLength = 40;
            row2.Controls.Add(_txtName2);

            _lblDir2 = FlowText(flow, "", Ui.SubFont, Ui.TextDim, w);
            _lblName2Hint = FlowText(flow,
                "第二个账号会有自己独立的目录，互不干扰。第一次打开它时，用另一个拓竹账号登录一次就好，之后会一直记住。",
                Ui.SubFont, Ui.TextDim, w);

            _txtName2.TextChanged += delegate { UpdateSecondState(); };
            _txtName2.Enabled = false;
            _lblDir2.Visible = false;
            _lblName2Hint.Visible = false;

            FlowRow(flow, w, 8);

            // 预填已有账号名
            Profile def = FindDefault();
            if (def != null) { _txtName1.Text = def.Name; }
            else { _txtName1.Text = "默认账号"; }

            Profile second = FindNonDefault();
            if (second != null)
            {
                _chkSecond.Checked = true;
                _txtName2.Text = second.Name;
                _chkSecond.Enabled = false;
                _chkSecond.Text = "第二个独立账号：“" + second.Name + "”";
            }
            UpdateSecondState();
        }

        private void UpdateSecondState()
        {
            bool on = _chkSecond.Checked && _chkSecond.Enabled;
            _txtName2.Enabled = _chkSecond.Checked;
            _lblDir2.Visible = _chkSecond.Checked;
            _lblName2Hint.Visible = _chkSecond.Checked;
            if (!_chkSecond.Checked) { return; }

            Profile second = FindNonDefault();
            if (second != null)
            {
                _lblDir2.Text = "数据目录：" + second.ShortDir;
                return;
            }
            string name = _txtName2.Text.Trim();
            if (name.Length == 0) { name = "第二个账号"; }
            string dir = Path.Combine(_settings.EffectiveProfileRoot, LaunchService.SafeFolderName(name));
            _lblDir2.Text = "数据目录：" + dir;
            if (!on) { _lblDir2.ForeColor = Ui.TextDim; }
        }

        // ---------------------------------------------------------- 第 3 步

        private void BuildStep2(Panel pane)
        {
            FlowLayoutPanel flow = NewFlow(pane);
            int w = ContentWidth;

            FlowHead(flow, "都准备好了", w, 0);
            _lblSummary = FlowText(flow, "", Ui.BaseFont, Ui.Text, w);

            FlowHead(flow, "接下来可能会发生什么", w, 14);
            FlowText(flow,
                "· 第一次打开某个账号的窗口时，Bambu Studio 可能要求登录。用那个账号登录一次，之后它会自己记住。\r\n" +
                "· 如果提示“验证码”，按提示输入就行，只需要这一次。\r\n" +
                "· 家里和公司的打印机都不用开局域网模式，照旧用云端方式连接。",
                Ui.BaseFont, Ui.Text, w);

            _chkLaunch = new CheckBox();
            _chkLaunch.Text = "完成后顺带打开默认账号的窗口（不勾也行，以后双击列表就能开）";
            _chkLaunch.Font = Ui.BaseFont;
            _chkLaunch.ForeColor = Ui.Text;
            _chkLaunch.AutoSize = true;
            _chkLaunch.Checked = false;
            _chkLaunch.Margin = new Padding(0, Ui.Px(8), 0, 0);
            flow.Controls.Add(_chkLaunch);
        }

        private void UpdateSummary()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Profile def = FindDefault();
            sb.Append("· 默认账号：").Append(_txtName1.Text.Trim());
            sb.Append("（沿用原来的目录）\r\n");

            if (_chkSecond.Checked)
            {
                Profile second = FindNonDefault();
                string name = _txtName2.Text.Trim();
                sb.Append("· 独立账号：").Append(name);
                if (second != null) { sb.Append("（" + second.ShortDir + "）"); }
                sb.Append("\r\n");
            }
            sb.Append("\r\n每份数据目录 ≈ 200 MB 左右，第一次创建时会从默认目录复制插件，可能需要几十秒。");
            _lblSummary.Text = sb.ToString();
        }

        // ------------------------------------------------------------------ 导航

        /// <summary>开发用：直接跳到某一步，方便截图检查排版。</summary>
        internal void RenderStep(int index)
        {
            if (index < 0) { index = 0; }
            if (index > 2) { index = 2; }
            ShowStep(index);
        }

        private void ShowStep(int step)
        {
            if (step < 0) { step = 0; }
            if (step > 2) { step = 2; }
            _step = step;
            for (int i = 0; i < _panes.Length; i++) { _panes[i].Visible = (i == step); }
            _lblStep.Text = "第 " + (step + 1) + " 步 / 共 3 步";
            _btnBack.Visible = step > 0;
            _btnNext.Text = step == 2 ? "完成" : "下一步";
            if (step == 2) { UpdateSummary(); }
            ActiveControl = _btnNext;
        }

        private void OnNext()
        {
            if (_step == 0)
            {
                if (string.IsNullOrEmpty(StudioLocator.Find(_settings.StudioExe)))
                {
                    DialogResult r = MessageBox.Show(this,
                        "还没有找到 Bambu Studio。\r\n\r\n可以点“手动指定位置”选一下，也可以先继续，之后在高级设置里再指定。",
                        AppPaths.AppName, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
                    if (r != DialogResult.OK) { return; }
                }
                ShowStep(1);
                return;
            }
            if (_step == 1)
            {
                string err = ValidateNames();
                if (err != null)
                {
                    MessageBox.Show(this, err, AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                ShowStep(2);
                return;
            }
            Finish();
        }

        private string ValidateNames()
        {
            string n1 = _txtName1.Text.Trim();
            if (n1.Length == 0) { return "请给默认账号起一个名字。"; }
            if (_chkSecond.Checked)
            {
                string n2 = _txtName2.Text.Trim();
                if (n2.Length == 0) { return "请给第二个账号起一个名字，或者取消上面的勾选。"; }
                if (string.Equals(n1, n2, StringComparison.OrdinalIgnoreCase))
                {
                    return "两个账号的名字不能一样，不然分不清窗口。";
                }
                if (LaunchService.SafeFolderName(n2).Length == 0) { return "第二个账号的名字里有不能用作文件夹的字符。"; }
            }
            return null;
        }

        private void Finish()
        {
            string error = null;
            using (BusyForm busy = new BusyForm("准备账号", "正在准备数据目录，请稍候…", delegate { error = CreateProfiles(); }))
            {
                busy.ShowDialog(this);
                if (busy.Error != null) { error = busy.Error.Message; }
            }
            if (error != null)
            {
                MessageBox.Show(this, "准备账号时出错：\r\n\r\n" + error, AppPaths.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            LaunchNow = _chkLaunch.Checked;
            DialogResult = DialogResult.OK;
            Close();
        }

        private string CreateProfiles()
        {
            try
            {
                string name1 = _txtName1.Text.Trim();
                Profile def = FindDefault();
                if (def == null)
                {
                    def = new Profile();
                    def.Id = _settings.NextId();
                    def.IsDefault = true;
                    def.Dir = AppPaths.BambuDefaultDataDir;
                    def.Name = name1;
                    _settings.Profiles.Add(def);
                }
                else
                {
                    def.Name = name1;
                }

                if (_chkSecond.Checked)
                {
                    string name2 = _txtName2.Text.Trim();
                    Profile second = FindNonDefault();
                    if (second == null)
                    {
                        string dir = Path.Combine(_settings.EffectiveProfileRoot, LaunchService.SafeFolderName(name2));
                        bool existed = Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length > 0;
                        if (!existed)
                        {
                            LaunchService.CopySharedData(AppPaths.BambuDefaultDataDir, dir, null);
                        }
                        second = new Profile();
                        second.Id = _settings.NextId();
                        second.Name = name2;
                        second.Dir = dir;
                        _settings.Profiles.Add(second);
                        Log.Info("向导创建独立账号: " + name2 + " -> " + dir);
                    }
                    else
                    {
                        second.Name = name2;
                    }
                    _settings.LastProfileId = second.Id;
                }
                else
                {
                    _settings.LastProfileId = def.Id;
                }

                _settings.Profiles.Sort(delegate(Profile a, Profile b)
                {
                    if (a.IsDefault != b.IsDefault) { return a.IsDefault ? -1 : 1; }
                    return a.Id.CompareTo(b.Id);
                });
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("向导创建账号失败", ex);
                return ex.Message;
            }
        }

        private Profile FindDefault()
        {
            foreach (Profile p in _settings.Profiles) { if (p.IsDefault) { return p; } }
            return null;
        }

        private Profile FindNonDefault()
        {
            foreach (Profile p in _settings.Profiles) { if (!p.IsDefault) { return p; } }
            return null;
        }
    }
}
