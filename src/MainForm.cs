using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BambuMultiLauncher
{
    internal sealed class MainForm : Form
    {
        private Settings _settings;
        private ListView _list;
        private Panel _header;
        private Panel _bottom;
        private Label _lblStudio;
        private Label _lblHint;
        private Label _lblVersion;
        private Button _btnLaunch;
        private Button _btnLaunchAll;
        private Button _btnRename;
        private Button _btnRemove;
        private Button _btnOpen;
        private Button _btnAdvanced;
        private Button _btnCheck;
        private Button _btnHelp;
        private Timer _timer;
        private bool _firstRunShown;
        private int _hover = -1;

        // 启动后 7 秒回头看一眼：进程要是已经退出了，说明这个版本的 Studio 不接受独立目录
        private Timer _verifyTimer;
        private readonly List<StartedOnce> _starting = new List<StartedOnce>();

        private sealed class StartedOnce
        {
            internal Profile Profile;
            internal int Pid;
            internal LaunchMethod Used = LaunchMethod.Auto;
            internal bool Retried;
        }

        private int _importedCount;
        private int _columnsWidth = -1;

        private Timer _focusTimer;
        private int _focusPid;
        private DateTime _focusDeadline;

        internal MainForm()
        {
            _settings = Settings.Load();

            // 以前装过（或者是早先那版 PowerShell 脚本）留下的数据目录，直接认回来
            try { _importedCount = LegacyScanner.Import(_settings); }
            catch (Exception ex) { Log.Warn("扫描旧数据失败：" + ex.Message); }

            Text = AppPaths.AppName + " " + AppPaths.Version;
            Font = Ui.BaseFont;
            BackColor = Ui.PageBack;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(Ui.Px(720), Ui.Px(460));
            ClientSize = new Size(Ui.Px(980), Ui.Px(620));
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }

            BuildUi();
            FitToScreen();
            ApplyGeometry();
            RefreshList();

            _timer = new Timer();
            _timer.Interval = 2500;
            _timer.Tick += delegate { if (_settings.AutoRefresh) { RefreshList(); } };
            _timer.Start();
        }

        // ------------------------------------------------------------------ 界面

        private void BuildUi()
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.BackColor = Ui.PageBack;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.Px(72)));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.Px(52)));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Ui.Px(66)));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);

            Panel line = new Panel();
            line.Dock = DockStyle.Fill;
            line.BackColor = Ui.Accent;
            root.Controls.Add(line, 0, 1);

            root.Controls.Add(BuildToolbar(), 0, 2);
            root.Controls.Add(BuildList(), 0, 3);
            root.Controls.Add(BuildBottom(), 0, 4);
        }

        private Control BuildHeader()
        {
            _header = new Panel();
            _header.Dock = DockStyle.Fill;
            _header.BackColor = Ui.Dark;

            Label title = Ui.Label(AppPaths.AppName, Ui.TitleFont, Color.White);
            title.Location = new Point(Ui.Px(22), Ui.Px(10));
            _header.Controls.Add(title);

            Label sub = Ui.Label("一台电脑同时登录多个拓竹账号，各自一个窗口，互不顶号",
                Ui.SubFont, Color.FromArgb(168, 176, 186));
            sub.Location = new Point(Ui.Px(24), Ui.Px(44));
            _header.Controls.Add(sub);

            _lblVersion = Ui.Label("v" + AppPaths.Version, Ui.SubFont, Color.FromArgb(132, 140, 150));
            _header.Controls.Add(_lblVersion);

            _header.Resize += delegate
            {
                _lblVersion.Location = new Point(_header.Width - _lblVersion.Width - Ui.Px(22), Ui.Px(14));
            };
            return _header;
        }

        private Control BuildToolbar()
        {
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Fill;
            bar.BackColor = Ui.Card;
            bar.Padding = new Padding(Ui.Px(14), Ui.Px(10), 0, 0);
            bar.WrapContents = false;
            bar.FlowDirection = FlowDirection.LeftToRight;

            _btnLaunch = Ui.PrimaryButton("启动选中账号", 120);
            _btnLaunchAll = Ui.SecondaryButton("全部启动", 86);
            Button add = Ui.SecondaryButton("添加账号", 86);
            _btnRename = Ui.SecondaryButton("重命名", 74);
            _btnRemove = Ui.SecondaryButton("删除", 64);
            _btnOpen = Ui.SecondaryButton("打开目录", 86);
            Button refresh = Ui.SecondaryButton("刷新", 64);

            _btnLaunch.Click += delegate { LaunchSelected(); };
            _btnLaunchAll.Click += delegate { LaunchAll(); };
            add.Click += delegate { AddAccount(); };
            _btnRename.Click += delegate { RenameAccount(); };
            _btnRemove.Click += delegate { RemoveAccount(); };
            _btnOpen.Click += delegate { OpenSelectedFolder(); };
            refresh.Click += delegate { RefreshList(); };

            bar.Controls.Add(_btnLaunch);
            bar.Controls.Add(_btnLaunchAll);
            bar.Controls.Add(add);
            bar.Controls.Add(_btnRename);
            bar.Controls.Add(_btnRemove);
            bar.Controls.Add(_btnOpen);
            bar.Controls.Add(refresh);
            return bar;
        }

        private Control BuildList()
        {
            Panel wrapper = new Panel();
            wrapper.Dock = DockStyle.Fill;
            wrapper.BackColor = Ui.Card;
            wrapper.Padding = new Padding(0);

            _list = new BufferedListView();
            _list.Dock = DockStyle.Fill;
            _list.View = View.Details;
            _list.FullRowSelect = true;
            _list.HideSelection = false;
            _list.MultiSelect = false;
            _list.BorderStyle = BorderStyle.None;
            _list.Font = Ui.BaseFont;
            _list.BackColor = Ui.Card;
            _list.ForeColor = Ui.Text;
            _list.HeaderStyle = ColumnHeaderStyle.Nonclickable;

            // 用一个 1 像素宽的占位图标表把行高撑开，后面对每一行自己绘制
            ImageList spacer = new ImageList();
            spacer.ImageSize = new Size(1, Ui.Px(24));
            spacer.ColorDepth = ColorDepth.Depth32Bit;
            _list.SmallImageList = spacer;

            _list.OwnerDraw = true;
            _list.DrawColumnHeader += DrawListHeader;
            _list.DrawItem += DrawListItem;
            _list.DrawSubItem += DrawListSubItem;
            _list.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                ListViewItem hit = _list.GetItemAt(e.X, e.Y);
                int index = hit == null ? -1 : hit.Index;
                if (index != _hover) { _hover = index; _list.Invalidate(); }
            };
            _list.MouseLeave += delegate { if (_hover != -1) { _hover = -1; _list.Invalidate(); } };

            _list.Columns.Add("账号名称", Ui.Px(170));
            _list.Columns.Add("运行", Ui.Px(76));
            _list.Columns.Add("登录", Ui.Px(76));
            _list.Columns.Add("数据目录", Ui.Px(460));
            _list.DoubleClick += delegate { LaunchSelected(); };
            _list.SelectedIndexChanged += delegate { UpdateButtons(); };
            _list.Resize += delegate { ResizeColumns(); };
            _list.MouseUp += delegate(object sender, MouseEventArgs e) { SelectFromRightClick(e); };
            wrapper.Controls.Add(_list);

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Font = Ui.BaseFont;
            menu.Items.Add("启动这个账号", null, delegate { LaunchSelected(); });
            menu.Items.Add("打开数据目录", null, delegate { OpenSelectedFolder(); });
            menu.Items.Add("复制目录路径", null, delegate { CopyPath(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("诊断启动问题…", null, delegate { DiagnoseSelected(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("删除这个账号", null, delegate { RemoveAccount(); });
            _list.ContextMenuStrip = menu;
            return wrapper;
        }

        private Control BuildBottom()
        {
            _bottom = new Panel();
            _bottom.Dock = DockStyle.Fill;
            _bottom.BackColor = Ui.Card;

            Panel topLine = new Panel();
            topLine.Dock = DockStyle.Top;
            topLine.Height = 1;
            topLine.BackColor = Ui.Border;
            _bottom.Controls.Add(topLine);

            _lblStudio = new Label();
            _lblStudio.AutoSize = false;
            _lblStudio.AutoEllipsis = true;
            _lblStudio.Font = Ui.SubFont;
            _lblStudio.ForeColor = Ui.TextDim;
            _lblStudio.Location = new Point(Ui.Px(16), Ui.Px(10));
            _lblStudio.Size = new Size(Ui.Px(520), Ui.Px(20));
            _bottom.Controls.Add(_lblStudio);

            _lblHint = new Label();
            _lblHint.AutoSize = false;
            _lblHint.AutoEllipsis = true;
            _lblHint.Font = Ui.SubFont;
            _lblHint.ForeColor = Ui.TextDim;
            _lblHint.Location = new Point(Ui.Px(16), Ui.Px(32));
            _lblHint.Size = new Size(Ui.Px(520), Ui.Px(20));
            _lblHint.Text = "提示：双击列表里的某一行，就可以直接启动那个账号。";
            _bottom.Controls.Add(_lblHint);

            _btnCheck = Ui.SecondaryButton("检查环境", 86);
            _btnAdvanced = Ui.SecondaryButton("高级设置", 86);
            _btnHelp = Ui.SecondaryButton("使用说明", 86);
            _btnCheck.Click += delegate { ShowEnvironmentReport(); };
            _btnAdvanced.Click += delegate { ShowAdvanced(); };
            _btnHelp.Click += delegate { ShowHelp(); };
            _bottom.Controls.Add(_btnCheck);
            _bottom.Controls.Add(_btnAdvanced);
            _bottom.Controls.Add(_btnHelp);

            _bottom.Resize += delegate
            {
                int right = _bottom.Width - Ui.Px(12);
                _btnHelp.Location = new Point(right - _btnHelp.Width, Ui.Px(17));
                _btnAdvanced.Location = new Point(_btnHelp.Left - _btnAdvanced.Width - Ui.Px(6), Ui.Px(17));
                _btnCheck.Location = new Point(_btnAdvanced.Left - _btnCheck.Width - Ui.Px(6), Ui.Px(17));
                int labelWidth = Math.Max(Ui.Px(180), _btnCheck.Left - Ui.Px(28));
                _lblStudio.Width = labelWidth;
                _lblHint.Width = labelWidth;
            };
            return _bottom;
        }

        private void SelectFromRightClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) { return; }
            ListViewItem item = _list.GetItemAt(e.X, e.Y);
            if (item != null) { item.Selected = true; }
        }

        private void ResizeColumnsIfNeeded()
        {
            if (_list.ClientSize.Width == _columnsWidth) { return; }
            ResizeColumns();
        }

        private void ResizeColumns()
        {
            if (_list.Columns.Count < 4) { return; }
            int fixedWidth = Ui.Px(170) + Ui.Px(76) + Ui.Px(76);
            int rest = _list.ClientSize.Width - fixedWidth - Ui.Px(6);
            _list.Columns[3].Width = Math.Max(Ui.Px(160), rest);
            _columnsWidth = _list.ClientSize.Width;
        }

        // ------------------------------------------------------------------ 首次运行

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_importedCount > 0)
            {
                SetHint("从以前的位置找到了 " + _importedCount + " 个账号的数据目录，已经加进列表，登录状态都还在。", Ui.Ok);
            }
            if (!_firstRunShown && !_settings.FirstRunDone)
            {
                _firstRunShown = true;
                BeginFirstRun();
            }
        }

        private void BeginFirstRun()
        {
            using (FirstRunForm form = new FirstRunForm(_settings))
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                {
                    Close();
                    return;
                }
                _settings = form.Settings;
                _settings.FirstRunDone = true;
                SaveSettings();
                RefreshList();
                if (form.LaunchNow && _settings.Profiles.Count > 0)
                {
                    // 只开默认账号：一下子把好几个窗口全弹出来太吓人
                    Profile first = null;
                    foreach (Profile p in _settings.Profiles)
                    {
                        if (p.IsDefault) { first = p; break; }
                    }
                    if (first == null) { first = _settings.Profiles[0]; }
                    SelectById(first.Id);
                    LaunchSelected();
                }
                else
                {
                    SetHint("设置完成。以后双击账号那一行就能直接打开。", Ui.Ok);
                }
            }
        }

        // ------------------------------------------------------------------ 列表

        private void RefreshList()
        {
            Profile current = CurrentProfile();
            int keepId = current != null ? current.Id : _settings.LastProfileId;

            // 整个过程只查一次进程表，然后原地更新行，不再整表清空重画（那是闪烁的来源）
            List<RunningInstance> instances = LaunchService.QueryInstances();

            _list.BeginUpdate();
            try
            {
                for (int i = _list.Items.Count - 1; i >= 0; i--)
                {
                    Profile old = _list.Items[i].Tag as Profile;
                    if (old == null || _settings.FindById(old.Id) == null) { _list.Items.RemoveAt(i); }
                }

                int index = 0;
                foreach (Profile p in _settings.Profiles)
                {
                    int pid = LaunchService.GetRunningPid(p, instances);
                    string runText = pid > 0 ? "运行中" : "未运行";
                    string loginText = p.HasLogin ? "已登录" : "未登录";
                    Color runColor = pid > 0 ? Ui.Ok : Ui.TextDim;
                    Color loginColor = p.HasLogin ? Ui.Text : Ui.Warn;

                    ListViewItem item = FindItem(p.Id);
                    if (item == null)
                    {
                        item = new ListViewItem(p.Name);
                        item.SubItems.Add(runText);
                        item.SubItems.Add(loginText);
                        item.SubItems.Add(p.ShortDir);
                        item.Tag = p;
                        item.UseItemStyleForSubItems = false;
                        item.SubItems[1].ForeColor = runColor;
                        item.SubItems[2].ForeColor = loginColor;
                        _list.Items.Insert(Math.Min(index, _list.Items.Count), item);
                    }
                    else
                    {
                        UpdateCell(item, 0, p.Name, null);
                        UpdateCell(item, 1, runText, runColor);
                        UpdateCell(item, 2, loginText, loginColor);
                        UpdateCell(item, 3, p.ShortDir, null);
                        if (item.Index != index)
                        {
                            _list.Items.Remove(item);
                            _list.Items.Insert(Math.Min(index, _list.Items.Count), item);
                        }
                    }
                    index++;
                }

                if (_list.SelectedItems.Count == 0 && keepId > 0)
                {
                    ListViewItem keep = FindItem(keepId);
                    if (keep != null) { keep.Selected = true; }
                }
            }
            finally
            {
                _list.EndUpdate();
            }

            ResizeColumnsIfNeeded();
            UpdateStudioLabel();
            UpdateButtons();
            if (_settings.Profiles.Count == 0)
            {
                SetHint("还没有账号。点上面的“添加账号”，或者关掉程序重新打开走一遍向导。", Ui.Warn);
            }
        }

        private ListViewItem FindItem(int profileId)
        {
            foreach (ListViewItem item in _list.Items)
            {
                Profile p = item.Tag as Profile;
                if (p != null && p.Id == profileId) { return item; }
            }
            return null;
        }

        /// <summary>只在文字真的变了的时候才改，避免每 2.5 秒无谓重绘。</summary>
        private static void UpdateCell(ListViewItem item, int sub, string text, Color? color)
        {
            if (sub >= item.SubItems.Count) { return; }
            ListViewItem.ListViewSubItem cell = item.SubItems[sub];
            if (cell.Text != text) { cell.Text = text; }
            if (color.HasValue && cell.ForeColor != color.Value) { cell.ForeColor = color.Value; }
        }

        private void UpdateStudioLabel()
        {
            string exe = _settings.StudioExe;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                exe = StudioLocator.Find(_settings.StudioExe);
            }
            if (string.IsNullOrEmpty(exe))
            {
                _lblStudio.Text = "没有找到 Bambu Studio（点“高级设置”可以手动指定）";
                _lblStudio.ForeColor = Ui.Warn;
                return;
            }
            string version = StudioLocator.VersionOf(exe);
            _lblStudio.Text = "Bambu Studio：" + exe + (version.Length > 0 ? "  版本 " + version : "");
            _lblStudio.ForeColor = Ui.TextDim;
        }

        private void UpdateButtons()
        {
            Profile p = CurrentProfile();
            bool has = p != null;
            _btnLaunch.Enabled = has;
            _btnRename.Enabled = has;
            _btnOpen.Enabled = has;
            _btnRemove.Enabled = has && !p.IsDefault;
            _btnLaunchAll.Enabled = _settings.Profiles.Count > 0;
            _btnLaunch.Text = has && LaunchService.IsRunning(p) ? "切到该窗口" : "启动选中账号";
        }

        private Profile CurrentProfile()
        {
            if (_list.SelectedItems.Count == 0) { return null; }
            return _list.SelectedItems[0].Tag as Profile;
        }

        // ------------------------------------------------------------------ 自绘列表

        private void DrawListHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            using (SolidBrush back = new SolidBrush(Ui.PageBack))
            {
                e.Graphics.FillRectangle(back, e.Bounds);
            }
            using (Pen line = new Pen(Ui.Border))
            {
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
            Rectangle text = new Rectangle(e.Bounds.X + Ui.Px(10),
                e.Bounds.Y + Ui.Px(2),
                e.Bounds.Width - Ui.Px(14),
                e.Bounds.Height - Ui.Px(4));
            TextRenderer.DrawText(e.Graphics, e.Header.Text, Ui.BoldFont, text, Ui.TextDim,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.EndEllipsis);
        }

        private void DrawListItem(object sender, DrawListViewItemEventArgs e)
        {
            Color back = Ui.Card;
            if (e.Item.Selected) { back = Color.FromArgb(231, 246, 238); }
            else if (e.ItemIndex == _hover) { back = Color.FromArgb(245, 248, 249); }

            using (SolidBrush brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, e.Bounds);
            }
            using (Pen line = new Pen(Color.FromArgb(238, 241, 244)))
            {
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }
            e.DrawDefault = false;
        }

        private void DrawListSubItem(object sender, DrawListViewSubItemEventArgs e)
        {
            Color fore = e.SubItem.ForeColor;
            Rectangle text = new Rectangle(e.Bounds.X + Ui.Px(10),
                e.Bounds.Y,
                e.Bounds.Width - Ui.Px(12),
                e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, Ui.BaseFont, text, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix
                | TextFormatFlags.EndEllipsis);
        }

        private void SetHint(string text, Color color)
        {
            _lblHint.Text = text;
            _lblHint.ForeColor = color;
        }

        // ------------------------------------------------------------------ 动作

        private void LaunchSelected()
        {
            Profile p = CurrentProfile();
            if (p == null) { return; }
            _settings.LastProfileId = p.Id;
            LaunchResult r = LaunchService.Launch(_settings, p);
            SaveSettings();

            if (r.Status == LaunchStatus.Started)
            {
                if (r.RepairNote.Length > 0)
                {
                    SetHint("“" + p.Name + "”" + r.RepairNote + " 正在唤起窗口…", Ui.Ok);
                }
                else
                {
                    SetHint(r.Message + "，正在唤起窗口…", Ui.Ok);
                }
                ScheduleVerify(p, r.Pid, r.UsedMethod);
                ScheduleFocus(r.Pid);
            }
            else if (r.Status == LaunchStatus.AlreadyRunning)
            {
                SetHint(r.Message, Ui.TextDim);
                ScheduleFocus(r.Pid);
            }
            else
            {
                SetHint(r.Message, Ui.Danger);
                MessageBox.Show(this, r.Message, AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshList();
        }

        private void LaunchAll()
        {
            if (_settings.Profiles.Count == 0) { return; }
            int started = 0;
            List<string> problems = new List<string>();
            foreach (Profile p in _settings.Profiles)
            {
                LaunchResult r = LaunchService.Launch(_settings, p);
                if (r.Status == LaunchStatus.Started) { started++; ScheduleVerify(p, r.Pid, r.UsedMethod); }
                else if (r.Status == LaunchStatus.AlreadyRunning) { }
                else { problems.Add(p.Name + "：" + r.Message); }
            }
            SaveSettings();
            if (problems.Count > 0)
            {
                SetHint("启动了 " + started + " 个账号，" + problems.Count + " 个有问题。", Ui.Warn);
                MessageBox.Show(this, string.Join("\r\n\r\n", problems.ToArray()), "有账号没有启动",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            else
            {
                SetHint("已启动 " + started + " 个账号窗口。", Ui.Ok);
            }
            RefreshList();
        }

        private void AddAccount()
        {
            using (AccountForm form = new AccountForm(_settings, null))
            {
                if (form.ShowDialog(this) != DialogResult.OK) { return; }
                Profile created = form.Result;
                if (created == null) { return; }
                _settings.Profiles.Add(created);
                _settings.UnignoreDir(created.Dir);
                SortProfiles();
                _settings.LastProfileId = created.Id;
                SaveSettings();
                RefreshList();

                string ask = "“" + created.Name + "”准备好了。\r\n\r\n现在打开它的窗口吗？" +
                             "打开后请用这个账号对应的拓竹账号登录一次，以后就不用再登录了。";
                if (MessageBox.Show(this, ask, AppPaths.AppName, MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) == DialogResult.Yes)
                {
                    SelectById(created.Id);
                    LaunchSelected();
                }
            }
        }

        private void SortProfiles()
        {
            _settings.Profiles.Sort(delegate(Profile a, Profile b)
            {
                if (a.IsDefault != b.IsDefault) { return a.IsDefault ? -1 : 1; }
                return a.Id.CompareTo(b.Id);
            });
        }

        private void SelectById(int id)
        {
            foreach (ListViewItem item in _list.Items)
            {
                Profile p = item.Tag as Profile;
                if (p != null && p.Id == id)
                {
                    _list.SelectedItems.Clear();
                    item.Selected = true;
                    item.Focused = true;
                    return;
                }
            }
        }

        private void RenameAccount()
        {
            Profile p = CurrentProfile();
            if (p == null) { return; }
            string name = PromptForm.Ask(this, "重命名账号", "新的名字：", p.Name);
            if (name == null) { return; }
            name = name.Trim();
            if (name.Length == 0) { return; }
            foreach (Profile other in _settings.Profiles)
            {
                if (other == p) { continue; }
                if (string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, "已经有一个叫“" + name + "”的账号了，换个名字吧。",
                        AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            p.Name = name;
            SaveSettings();
            RefreshList();
        }

        private void RemoveAccount()
        {
            Profile p = CurrentProfile();
            if (p == null) { return; }
            if (p.IsDefault)
            {
                MessageBox.Show(this, "这是“默认账号”，不能删除。\r\n它对应 Bambu Studio 自己的数据目录，删掉会影响你原来的使用。",
                    AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string text = "确定要移除“" + p.Name + "”吗？\r\n\r\n数据目录：" + p.Dir;
            if (MessageBox.Show(this, text, AppPaths.AppName, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            bool insideRoot = false;
            try
            {
                string root = Path.GetFullPath(_settings.EffectiveProfileRoot).TrimEnd('\\') + "\\";
                insideRoot = Path.GetFullPath(p.Dir).StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch { }

            if (insideRoot)
            {
                DialogResult del = MessageBox.Show(this,
                    "顺便把这个账号的数据目录也删掉吗？\r\n\r\n删掉以后它记住的登录状态和本地设置都会消失，需要重新登录。",
                    AppPaths.AppName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (del == DialogResult.Cancel) { return; }
                if (del == DialogResult.Yes)
                {
                    try { Directory.Delete(p.Dir, true); }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, "删除数据目录失败：" + ex.Message, AppPaths.AppName,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }
            }
            _settings.Profiles.Remove(p);
            // 记下来：这个目录是用户主动删掉的，以后"认旧数据"不许再捡回来
            _settings.IgnoreDir(p.Dir);
            SaveSettings();
            RefreshList();
        }

        private void OpenSelectedFolder()
        {
            Profile p = CurrentProfile();
            if (p == null) { return; }
            OpenFolderSafely(p.Dir);
        }

        private void OpenFolderSafely(string path)
        {
            string error;
            if (LaunchService.OpenFolder(path, out error)) { return; }
            SetHint("打不开这个目录：" + error, Ui.Danger);
            MessageBox.Show(this,
                "打不开这个目录：\r\n" + path + "\r\n\r\n" + error +
                "\r\n\r\n可以把下面这行复制到资源管理器的地址栏里：\r\n" + path,
                AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        // ------------------------------------------------------------------ 启动后校验

        /// <summary>Bambu Studio 出窗口可能要十几秒；轮询放到计时器里，别把界面卡住。</summary>
        private void ScheduleFocus(int pid)
        {
            if (pid <= 0) { return; }
            if (LaunchService.TryFocusOnce(pid)) { return; }
            _focusPid = pid;
            _focusDeadline = DateTime.Now.AddSeconds(25);
            if (_focusTimer == null)
            {
                _focusTimer = new Timer();
                _focusTimer.Interval = 400;
                _focusTimer.Tick += delegate { OnFocusTick(); };
            }
            _focusTimer.Start();
        }

        private void OnFocusTick()
        {
            if (_focusPid <= 0 || DateTime.Now > _focusDeadline || !LaunchService.IsAlive(_focusPid))
            {
                StopFocusTimer();
                return;
            }
            if (LaunchService.TryFocusOnce(_focusPid)) { StopFocusTimer(); }
        }

        private void StopFocusTimer()
        {
            _focusPid = 0;
            if (_focusTimer != null) { _focusTimer.Stop(); }
        }

        /// <summary>保存配置；写不进去就提示一下，别静默失败。</summary>
        private bool SaveSettings()
        {
            bool ok = _settings.Save();
            if (!ok)
            {
                SetHint("配置没能写入（可能被占用或没有权限），这次的改动只在本次运行里生效。", Ui.Danger);
            }
            return ok;
        }

        private void ScheduleVerify(Profile p, int pid, LaunchMethod used)
        {
            if (pid <= 0) { return; }
            StartedOnce item = new StartedOnce();
            item.Profile = p;
            item.Pid = pid;
            item.Used = used;
            _starting.Add(item);
            if (_verifyTimer == null)
            {
                _verifyTimer = new Timer();
                _verifyTimer.Interval = 7000;
                _verifyTimer.Tick += delegate { OnVerifyTick(); };
            }
            _verifyTimer.Stop();
            _verifyTimer.Start();
        }

        private void OnVerifyTick()
        {
            _verifyTimer.Stop();
            List<StartedOnce> dead = new List<StartedOnce>();
            foreach (StartedOnce item in _starting)
            {
                if (!LaunchService.IsAlive(item.Pid)) { dead.Add(item); }
            }
            _starting.Clear();
            if (dead.Count == 0) { return; }

            // 先自动换一种启动方式再试一次：不同版本的 Bambu Studio 认的方式不一样。
            List<StartedOnce> stillDead = new List<StartedOnce>();
            foreach (StartedOnce item in dead)
            {
                if (item.Retried)
                {
                    stillDead.Add(item);
                    continue;
                }
                LaunchMethod other = item.Used == LaunchMethod.Shell ? LaunchMethod.Direct : LaunchMethod.Shell;
                LaunchResult again = LaunchService.Launch(_settings, item.Profile, other);
                if (again.Status == LaunchStatus.Started)
                {
                    SetHint("“" + item.Profile.Name + "”第一次没起来，换了一种启动方式，正在重新尝试…", Ui.Warn);
                    ScheduleVerify(item.Profile, again.Pid, again.UsedMethod);
                }
                else
                {
                    item.Retried = true;
                    stillDead.Add(item);
                }
            }
            if (stillDead.Count == 0) { return; }

            Profile first = stillDead[0].Profile;
            SetHint("“" + first.Name + "”的窗口没有起来。可以右键这一行，点“诊断启动问题”。", Ui.Danger);
            DialogResult answer = MessageBox.Show(this,
                stillDead.Count == 1
                    ? "“" + first.Name + "”的窗口没有启动成功（进程已经退出）。\r\n\r\n" +
                      "两种启动方式都试过了。要不要跑一次启动诊断？它会试着修好，并告诉你原因。"
                    : "有 " + stillDead.Count + " 个账号的窗口没有启动成功。\r\n\r\n要不要现在对“" + first.Name + "”跑一次启动诊断？",
                AppPaths.AppName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes) { RunLaunchDiagnostics(first); }
        }

        private void DiagnoseSelected()
        {
            Profile p = CurrentProfile();
            if (p == null)
            {
                MessageBox.Show(this, "先在列表里选一个账号。", AppPaths.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            RunLaunchDiagnostics(p);
        }

        private void RunLaunchDiagnostics(Profile p)
        {
            string report = "";
            bool fixedNow = false;
            Exception error = null;
            using (BusyForm busy = new BusyForm("启动诊断",
                "正在试启动一次并观察结果，大约需要十几秒…",
                delegate
                {
                    try
                    {
                        report = LaunchDiagnostics.Run(_settings, p, out fixedNow);
                    }
                    catch (Exception ex) { error = ex; }
                }))
            {
                busy.ShowDialog(this);
                if (busy.Error != null) { error = busy.Error; }
            }

            RefreshList();
            if (error != null)
            {
                MessageBox.Show(this, "诊断过程出错：" + error.Message, AppPaths.AppName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (fixedNow)
            {
                SetHint("已经给“" + p.Name + "”补上必要设置，再启动一次应该就行了。", Ui.Ok);
            }
            using (ReportForm form = new ReportForm("启动诊断 · " + p.Name, report))
            {
                form.ShowDialog(this);
            }
        }

        private void CopyPath()
        {
            Profile p = CurrentProfile();
            if (p == null) { return; }
            try
            {
                Clipboard.SetText(p.Dir);
                SetHint("目录路径已复制到剪贴板。", Ui.TextDim);
            }
            catch { }
        }

        private void ShowAdvanced()
        {
            using (AdvancedForm form = new AdvancedForm(_settings))
            {
                if (form.ShowDialog(this) != DialogResult.OK) { return; }
                SaveSettings();
                RefreshList();
            }
        }

        private void ShowEnvironmentReport()
        {
            using (ReportForm form = new ReportForm("环境报告", EnvironmentReport.Build(_settings)))
            {
                form.ShowDialog(this);
            }
        }

        private void ShowHelp()
        {
            using (HelpForm form = new HelpForm())
            {
                form.ShowDialog(this);
            }
        }

        // ------------------------------------------------------------------ 窗口位置

        /// <summary>高分屏上别让窗口大过桌面。</summary>
        private void FitToScreen()
        {
            try
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                int maxW = (int)(wa.Width * 0.92);
                int maxH = (int)(wa.Height * 0.90);
                if (Width > maxW) { Width = maxW; }
                if (Height > maxH) { Height = maxH; }
            }
            catch { }
        }

        private void ApplyGeometry()
        {
            if (!_settings.RememberWindow) { return; }
            if (_settings.WindowW > 400 && _settings.WindowH > 300)
            {
                ClientSize = new Size(_settings.WindowW, _settings.WindowH);
            }
            if (_settings.WindowX != -1 && _settings.WindowY != -1)
            {
                Rectangle screen = Screen.PrimaryScreen.WorkingArea;
                int x = Math.Max(screen.Left - 40, Math.Min(_settings.WindowX, screen.Right - 200));
                int y = Math.Max(screen.Top - 30, Math.Min(_settings.WindowY, screen.Bottom - 150));
                StartPosition = FormStartPosition.Manual;
                Location = new Point(x, y);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                if (_timer != null) { _timer.Stop(); }
                if (_verifyTimer != null) { _verifyTimer.Stop(); }
                StopFocusTimer();
                if (_settings.RememberWindow && WindowState == FormWindowState.Normal)
                {
                    _settings.WindowX = Location.X;
                    _settings.WindowY = Location.Y;
                    _settings.WindowW = ClientSize.Width;
                    _settings.WindowH = ClientSize.Height;
                }
                SaveSettings();
            }
            catch { }
            base.OnFormClosing(e);
        }
    }
}
