using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace BambuMultiLauncher
{
    /// <summary>添加账号（追加一份独立数据目录）。</summary>
    internal sealed class AccountForm : Form
    {
        private readonly Settings _settings;
        private readonly Profile _editing;

        private TextBox _txtName;
        private RadioButton _rdoDefault;
        private RadioButton _rdoNew;
        private TextBox _txtDir;
        private Button _btnBrowse;
        private Button _btnReset;
        private CheckBox _chkCopy;
        private Label _lblHint;
        private Button _btnOk;

        /// <summary>上一次根据名字自动算出来的目录；用来判断用户有没有手改过路径框。</summary>
        private string _lastAutoDir = "";

        internal Profile Result;

        internal AccountForm(Settings settings, Profile editing)
        {
            _settings = settings;
            _editing = editing;

            Text = editing == null ? "添加账号" : "修改账号";
            Font = Ui.BaseFont;
            BackColor = Ui.Card;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Ui.Px(560), Ui.Px(400));
            BuildUi();
            Prefill();
        }

        private void BuildUi()
        {
            int left = Ui.Px(24);
            int width = ClientSize.Width - Ui.Px(48);

            Label title = Ui.Label(_editing == null ? "添加一个账号" : "修改账号", Ui.H2Font, Ui.Text);
            title.Location = new Point(left, Ui.Px(18));
            Controls.Add(title);

            Label nameLabel = Ui.Label("名字", Ui.BaseFont, Ui.Text);
            nameLabel.Location = new Point(left, Ui.Px(58));
            Controls.Add(nameLabel);

            _txtName = new TextBox();
            _txtName.Location = new Point(left, Ui.Px(80));
            _txtName.Size = new Size(width, Ui.Px(24));
            _txtName.MaxLength = 40;
            _txtName.TextChanged += delegate { SyncSuggestedDir(); };
            Controls.Add(_txtName);

            Label modeLabel = Ui.Label("数据目录", Ui.BaseFont, Ui.Text);
            modeLabel.Location = new Point(left, Ui.Px(118));
            Controls.Add(modeLabel);

            _rdoDefault = new RadioButton();
            _rdoDefault.Text = "使用 Bambu Studio 的默认目录（我平时直接打开的那个账号）";
            _rdoDefault.Font = Ui.BaseFont;
            _rdoDefault.ForeColor = Ui.Text;
            _rdoDefault.AutoSize = true;
            _rdoDefault.Location = new Point(left, Ui.Px(142));
            _rdoDefault.CheckedChanged += delegate { UpdateState(); };
            Controls.Add(_rdoDefault);

            _rdoNew = new RadioButton();
            _rdoNew.Text = "新建一份独立目录（用来登录另一个拓竹账号）";
            _rdoNew.Font = Ui.BaseFont;
            _rdoNew.ForeColor = Ui.Text;
            _rdoNew.AutoSize = true;
            _rdoNew.Location = new Point(left, Ui.Px(172));
            _rdoNew.CheckedChanged += delegate { UpdateState(); };
            Controls.Add(_rdoNew);

            _txtDir = new TextBox();
            _txtDir.Location = new Point(left + Ui.Px(22), Ui.Px(200));
            _txtDir.Size = new Size(width - Ui.Px(194), Ui.Px(24));
            _txtDir.TextChanged += delegate { UpdateState(); };
            Controls.Add(_txtDir);

            _btnBrowse = Ui.SecondaryButton("浏览…", 70);
            _btnBrowse.Location = new Point(left + width - Ui.Px(166), Ui.Px(196));
            _btnBrowse.Click += delegate { Browse(); };
            Controls.Add(_btnBrowse);

            _btnReset = Ui.SecondaryButton("默认位置", 86);
            _btnReset.Location = new Point(left + width - Ui.Px(86), Ui.Px(196));
            _btnReset.Click += delegate
            {
                string s = SuggestedDir();
                _lastAutoDir = s;
                _txtDir.Text = s;
            };
            Controls.Add(_btnReset);

            _chkCopy = new CheckBox();
            _chkCopy.Text = "从默认目录复制插件与预设（推荐，省一次联网下载）";
            _chkCopy.Font = Ui.BaseFont;
            _chkCopy.ForeColor = Ui.Text;
            _chkCopy.AutoSize = true;
            _chkCopy.Checked = true;
            _chkCopy.Location = new Point(left + Ui.Px(22), Ui.Px(232));
            Controls.Add(_chkCopy);

            _lblHint = new Label();
            _lblHint.AutoSize = false;
            _lblHint.Font = Ui.SubFont;
            _lblHint.ForeColor = Ui.TextDim;
            _lblHint.Location = new Point(left + Ui.Px(22), Ui.Px(262));
            _lblHint.Size = new Size(width - Ui.Px(22), Ui.Px(56));
            Controls.Add(_lblHint);

            Panel line = new Panel();
            line.BackColor = Ui.Border;
            line.Location = new Point(0, ClientSize.Height - Ui.Px(62));
            line.Size = new Size(ClientSize.Width, 1);
            Controls.Add(line);

            _btnOk = Ui.PrimaryButton("确定", 90);
            _btnOk.Location = new Point(ClientSize.Width - Ui.Px(24) - _btnOk.Width, ClientSize.Height - Ui.Px(48));
            _btnOk.Click += delegate { OnOk(); };
            Controls.Add(_btnOk);
            AcceptButton = _btnOk;

            Button cancel = Ui.SecondaryButton("取消", 90);
            cancel.Location = new Point(_btnOk.Left - cancel.Width - Ui.Px(8), ClientSize.Height - Ui.Px(48));
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);
            CancelButton = cancel;
        }

        private void Prefill()
        {
            bool hasDefault = FindDefault() != null;

            if (_editing != null)
            {
                _txtName.Text = _editing.Name;
                _txtDir.Text = _editing.Dir;
                if (_editing.IsDefault)
                {
                    _rdoDefault.Checked = true;
                }
                else
                {
                    _rdoNew.Checked = true;
                }
                _rdoDefault.Enabled = false;
                _rdoNew.Enabled = false;
                _txtDir.ReadOnly = true;
                _btnBrowse.Enabled = false;
                _btnReset.Enabled = false;
                _chkCopy.Visible = false;
            }
            else
            {
                _txtName.Text = "账号 " + (_settings.Profiles.Count + 1).ToString();
                _rdoDefault.Enabled = !hasDefault;
                if (hasDefault)
                {
                    _rdoNew.Checked = true;
                }
                else
                {
                    _rdoDefault.Checked = true;
                }
            }
            UpdateState();
        }

        private void UpdateState()
        {
            bool isNew = _editing == null && _rdoNew.Checked;
            _txtDir.Enabled = isNew && _editing == null;
            _btnBrowse.Enabled = isNew && _editing == null;
            _btnReset.Enabled = isNew && _editing == null;
            _chkCopy.Enabled = isNew && _editing == null;

            if (_editing != null) { return; }

            if (_rdoDefault.Checked)
            {
                _txtDir.Text = AppPaths.BambuDefaultDataDir;
                _chkCopy.Checked = false;
                _lblHint.Text = "这个账号会继续使用原目录，不需要复制任何东西。适合“我现在就在用的那个账号”。";
            }
            else
            {
                if (_txtDir.Text.Length == 0)
                {
                    string suggested = SuggestedDir();
                    _lastAutoDir = suggested;
                    _txtDir.Text = suggested;
                }
                _chkCopy.Checked = true;
                _lblHint.Text = "新目录和默认目录完全隔离，登录状态互不影响。第一次打开它时需要登录一次。";
            }
        }

        /// <summary>改名字时，如果路径框还停在"自动算出来的那个"，就跟着更新。</summary>
        private void SyncSuggestedDir()
        {
            if (_editing != null) { return; }
            if (_rdoDefault.Checked) { return; }
            string current = _txtDir.Text.Trim();
            if (current.Length > 0 && !string.Equals(current, _lastAutoDir, StringComparison.OrdinalIgnoreCase)) { return; }
            string suggested = SuggestedDir();
            _lastAutoDir = suggested;
            if (!string.Equals(current, suggested, StringComparison.OrdinalIgnoreCase)) { _txtDir.Text = suggested; }
        }

        private string SuggestedDir()
        {
            string name = _txtName.Text.Trim();
            if (name.Length == 0) { name = "账号 " + (_settings.Profiles.Count + 1).ToString(); }
            return Path.Combine(_settings.EffectiveProfileRoot, LaunchService.SafeFolderName(name));
        }

        private Profile FindDefault()
        {
            foreach (Profile p in _settings.Profiles) { if (p.IsDefault) { return p; } }
            return null;
        }

        private void Browse()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择这个账号的数据目录";
                dlg.ShowNewFolderButton = true;
                string current = _txtDir.Text.Trim();
                if (current.Length > 0 && Directory.Exists(current)) { dlg.SelectedPath = current; }
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    _lastAutoDir = "";
                    _txtDir.Text = dlg.SelectedPath;
                }
            }
        }

        private void OnOk()
        {
            string name = _txtName.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "请填一个名字。", AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            foreach (Profile p in _settings.Profiles)
            {
                if (_editing != null && p.Id == _editing.Id) { continue; }
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show(this, "已经有一个叫“" + name + "”的账号了，换个名字吧。",
                        AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            bool useDefault = _rdoDefault.Checked;
            string dir = useDefault ? AppPaths.BambuDefaultDataDir : _txtDir.Text.Trim();

            if (!useDefault)
            {
                if (dir.Length == 0)
                {
                    MessageBox.Show(this, "请指定数据目录，或者点“默认位置”。", AppPaths.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                try
                {
                    dir = Path.GetFullPath(dir);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "这个路径不对：" + ex.Message, AppPaths.AppName,
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                foreach (Profile p in _settings.Profiles)
                {
                    if (string.Equals(Path.GetFullPath(p.Dir).TrimEnd('\\'), dir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show(this, "这个目录已经被“" + p.Name + "”占用了，换一个吧。",
                            AppPaths.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
            }

            if (_editing == null)
            {
                Profile p = new Profile();
                p.Id = _settings.NextId();
                p.Name = name;
                p.Dir = dir;
                p.IsDefault = useDefault;
                Result = p;

                if (!useDefault)
                {
                    if (_chkCopy.Checked)
                    {
                        string source = AppPaths.BambuDefaultDataDir;
                        if (Directory.Exists(source))
                        {
                            bool existed = Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length > 0;
                            if (!existed)
                            {
                                Exception failure = null;
                                using (BusyForm busy = new BusyForm("准备数据目录", "正在复制插件与预设，请稍候…",
                                           delegate { LaunchService.CopySharedData(source, dir, null); }))
                                {
                                    busy.ShowDialog(this);
                                    failure = busy.Error;
                                }
                                if (failure != null)
                                {
                                    Log.Warn("复制插件失败（可忽略）: " + failure.Message);
                                }
                            }
                        }
                    }
                    // 不管有没有复制，目录本身一定得存在，否则启动时会报"NoDir"
                    if (!Directory.Exists(dir))
                    {
                        try { Directory.CreateDirectory(dir); }
                        catch (Exception ex)
                        {
                            MessageBox.Show(this, "创建目录失败：" + ex.Message, AppPaths.AppName,
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                            return;
                        }
                    }
                }
            }
            else
            {
                _editing.Name = name;
                Result = _editing;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>一个极简输入框，用于重命名。</summary>
    internal static class PromptForm
    {
        internal static string Ask(IWin32Window owner, string caption, string label, string defaultValue)
        {
            using (Form form = new Form())
            {
                form.Text = caption;
                form.Font = Ui.BaseFont;
                form.BackColor = Ui.Card;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;
                form.ShowInTaskbar = false;
                form.StartPosition = FormStartPosition.CenterParent;
                form.AutoScaleMode = AutoScaleMode.None;
                form.ClientSize = new Size(Ui.Px(380), Ui.Px(150));

                Label text = Ui.Label(label, Ui.BaseFont, Ui.Text);
                text.Location = new Point(Ui.Px(18), Ui.Px(18));
                form.Controls.Add(text);

                TextBox box = new TextBox();
                box.Text = defaultValue;
                box.Location = new Point(Ui.Px(18), Ui.Px(46));
                box.Size = new Size(Ui.Px(344), Ui.Px(24));
                box.MaxLength = 40;
                form.Controls.Add(box);

                Button ok = Ui.PrimaryButton("确定", 80);
                ok.Location = new Point(form.ClientSize.Width - Ui.Px(100) - ok.Width, Ui.Px(96));
                ok.DialogResult = DialogResult.OK;
                form.Controls.Add(ok);
                form.AcceptButton = ok;

                Button cancel = Ui.SecondaryButton("取消", 80);
                cancel.Location = new Point(ok.Left - cancel.Width - Ui.Px(8), Ui.Px(96));
                cancel.DialogResult = DialogResult.Cancel;
                form.Controls.Add(cancel);
                form.CancelButton = cancel;

                box.SelectAll();
                if (form.ShowDialog(owner) != DialogResult.OK) { return null; }
                return box.Text;
            }
        }
    }
}
