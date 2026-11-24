using System;
using System.Drawing;
using System.Windows.Forms;

namespace BambuMultiLauncher
{
    /// <summary>
    /// 开双缓冲的 ListView。默认的 ListView 每次改内容都会先擦成白底再画，
    /// 定时刷新看起来就是"一闪一闪"。开双缓冲以后是整块换页，不会闪。
    /// </summary>
    internal sealed class BufferedListView : ListView
    {
        internal BufferedListView()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            UpdateStyles();
            DoubleBuffered = true;
        }
    }

    /// <summary>检测到多个 Bambu Studio 时，让用户挑一个。</summary>
    internal sealed class StudioPickerForm : Form
    {
        private ListBox _list;
        private string _picked = "";

        internal string Picked { get { return _picked; } }

        internal StudioPickerForm(System.Collections.Generic.List<string> candidates, string current)
        {
            Text = "选择 Bambu Studio";
            Font = Ui.BaseFont;
            BackColor = Ui.Card;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Ui.Px(620), Ui.Px(320));

            Label tip = Ui.Label("这台电脑上找到多个 Bambu Studio，选一个用来启动：", Ui.BaseFont, Ui.Text);
            tip.Location = new Point(Ui.Px(18), Ui.Px(16));
            Controls.Add(tip);

            _list = new ListBox();
            _list.Location = new Point(Ui.Px(18), Ui.Px(44));
            _list.Size = new Size(ClientSize.Width - Ui.Px(36), Ui.Px(190));
            _list.Font = Ui.BaseFont;
            _list.IntegralHeight = false;
            foreach (string path in candidates)
            {
                string ver = StudioLocator.VersionOf(path);
                string text = path + (ver.Length > 0 ? "   版本 " + ver : "");
                int index = _list.Items.Add(text);
                if (string.Equals(path, current, StringComparison.OrdinalIgnoreCase)) { _list.SelectedIndex = index; }
            }
            if (_list.SelectedIndex < 0 && _list.Items.Count > 0) { _list.SelectedIndex = 0; }
            _list.DoubleClick += delegate { Accept(); };
            Controls.Add(_list);

            Button ok = Ui.PrimaryButton("使用这个", 100);
            ok.Location = new Point(ClientSize.Width - Ui.Px(232), Ui.Px(250));
            ok.Click += delegate { Accept(); };
            Controls.Add(ok);

            Button cancel = Ui.SecondaryButton("取消", 90);
            cancel.Location = new Point(ClientSize.Width - Ui.Px(122), Ui.Px(250));
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        private void Accept()
        {
            if (_list.SelectedIndex >= 0)
            {
                string text = Convert.ToString(_list.Items[_list.SelectedIndex]);
                int cut = text.IndexOf("   版本 ", StringComparison.Ordinal);
                _picked = cut > 0 ? text.Substring(0, cut) : text;
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}
