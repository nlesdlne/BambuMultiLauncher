using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BambuMultiLauncher
{
    /// <summary>统一的配色、字号和控件工厂，保证界面观感一致。</summary>
    internal static class Ui
    {
        internal static readonly Color Accent = Color.FromArgb(0, 166, 62);
        internal static readonly Color AccentDark = Color.FromArgb(0, 136, 50);
        internal static readonly Color Dark = Color.FromArgb(31, 36, 41);
        internal static readonly Color DarkSoft = Color.FromArgb(58, 65, 73);
        internal static readonly Color PageBack = Color.FromArgb(247, 248, 250);
        internal static readonly Color Card = Color.White;
        internal static readonly Color Border = Color.FromArgb(222, 226, 231);
        internal static readonly Color Text = Color.FromArgb(33, 38, 44);
        internal static readonly Color TextDim = Color.FromArgb(120, 128, 138);
        internal static readonly Color Warn = Color.FromArgb(196, 106, 0);
        internal static readonly Color Danger = Color.FromArgb(200, 55, 55);
        internal static readonly Color Ok = Color.FromArgb(0, 140, 70);

        private static float _scale = -1f;

        /// <summary>当前屏幕 DPI 相对 96 的比例，用来放大固定像素尺寸。</summary>
        internal static float Scale
        {
            get
            {
                if (_scale < 0f)
                {
                    try
                    {
                        using (Control probe = new Control())
                        using (Graphics g = probe.CreateGraphics())
                        {
                            _scale = g.DpiX / 96f;
                        }
                    }
                    catch { _scale = 1f; }
                    if (_scale < 1f) { _scale = 1f; }
                    if (_scale > 3f) { _scale = 3f; }
                }
                return _scale;
            }
        }

        internal static int Px(int value)
        {
            return (int)Math.Round(value * Scale);
        }

        internal static Font BaseFont { get { return new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point); } }
        internal static Font BoldFont { get { return new Font("Microsoft YaHei UI", 9F, FontStyle.Bold, GraphicsUnit.Point); } }
        internal static Font TitleFont { get { return new Font("Microsoft YaHei UI", 13F, FontStyle.Bold, GraphicsUnit.Point); } }
        internal static Font SubFont { get { return new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point); } }
        internal static Font H2Font { get { return new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold, GraphicsUnit.Point); } }

        internal static Button PrimaryButton(string text, int width)
        {
            Button b = MakeButton(text, width);
            b.BackColor = Accent;
            b.ForeColor = Color.White;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = AccentDark;
            b.FlatAppearance.MouseDownBackColor = AccentDark;
            return b;
        }

        internal static Button SecondaryButton(string text, int width)
        {
            Button b = MakeButton(text, width);
            b.BackColor = Color.White;
            b.ForeColor = Text;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(242, 244, 247);
            return b;
        }

        internal static Button LinkButton(string text)
        {
            Button b = new Button();
            b.Text = text;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Color.Transparent;
            b.ForeColor = AccentDark;
            b.Font = SubFont;
            b.Cursor = Cursors.Hand;
            b.AutoSize = true;
            b.Padding = new Padding(0);
            return b;
        }

        private static Button MakeButton(string text, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.Font = BaseFont;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.Size = new Size(Px(width), Px(32));
            b.Margin = new Padding(0, 0, Px(8), 0);
            b.UseVisualStyleBackColor = false;
            b.Cursor = Cursors.Hand;
            return b;
        }

        internal static Label Label(string text, Font font, Color color)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = font;
            l.ForeColor = color;
            l.AutoSize = true;
            l.BackColor = Color.Transparent;
            return l;
        }

        internal static Panel Separator()
        {
            Panel p = new Panel();
            p.Height = 1;
            p.Dock = DockStyle.Top;
            p.BackColor = Border;
            return p;
        }

        /// <summary>给控件画一个细边框（WinForms 的 Panel 没有）。</summary>
        internal static void DrawBorder(Control c)
        {
            c.Paint += delegate(object sender, PaintEventArgs e)
            {
                Control self = (Control)sender;
                using (Pen pen = new Pen(Border))
                {
                    e.Graphics.DrawRectangle(pen, 0, 0, self.Width - 1, self.Height - 1);
                }
            };
        }

        internal static void EnableDoubleBuffer(Control c)
        {
            try
            {
                System.Reflection.PropertyInfo pi = typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (pi != null) { pi.SetValue(c, true, null); }
            }
            catch { }
        }

        /// <summary>
        /// 用代码搭一个两行布局：上面是可滚动内容，下面是固定高度的按钮条。
        /// 窗口再小也只是出滚动条，不会把按钮压到文字上。
        /// </summary>
        internal static Panel Stack(Form form, int logicalContentWidth, int contentHeight, int footerHeight,
            out Panel canvas)
        {
            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.ColumnCount = 1;
            root.RowCount = 2;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, Px(footerHeight)));
            root.BackColor = Card;
            form.Controls.Add(root);

            ScrollCanvas host = new ScrollCanvas(logicalContentWidth, contentHeight);
            host.Dock = DockStyle.Fill;
            host.Margin = new Padding(0);
            root.Controls.Add(host, 0, 0);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Fill;
            footer.Margin = new Padding(0);
            footer.BackColor = Card;
            root.Controls.Add(footer, 0, 1);

            Panel line = new Panel();
            line.Dock = DockStyle.Top;
            line.Height = 1;
            line.BackColor = Border;
            footer.Controls.Add(line);

            canvas = host.Canvas;
            return footer;
        }
    }

    /// <summary>可滚动的内容区：里面是一块固定逻辑尺寸的画布。</summary>
    internal sealed class ScrollCanvas : Panel
    {
        internal Panel Canvas;
        private readonly int _minWidth;

        internal ScrollCanvas(int logicalWidth, int logicalHeight)
        {
            AutoScroll = true;
            BackColor = Ui.Card;
            _minWidth = Ui.Px(logicalWidth);
            Canvas = new Panel();
            Canvas.Location = new Point(0, 0);
            Canvas.Size = new Size(_minWidth, Ui.Px(logicalHeight));
            Canvas.BackColor = Ui.Card;
            Controls.Add(Canvas);
        }

        /// <summary>
        /// 画布宽度跟着可视宽度走，但要预留竖向滚动条的宽度。
        /// 否则内容一高就同时冒出竖条和横条（WinForms 的经典现象）。
        /// </summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Canvas == null) { return; }
            int room = ClientSize.Width - SystemInformation.VerticalScrollBarWidth;
            int w = Math.Max(_minWidth, room);
            Canvas.Size = new Size(w, Canvas.Height);
        }
    }

    /// <summary>执行耗时任务时显示的“请稍候”窗口（复制数据目录用）。</summary>
    internal sealed class BusyForm : Form
    {
        private readonly Label _label;
        private readonly Action _work;
        private Exception _error;
        private bool _done;

        internal BusyForm(string caption, string message, Action work)
        {
            _work = work;

            Text = caption;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            BackColor = Ui.Card;
            Font = Ui.BaseFont;
            ClientSize = new Size(Ui.Px(420), Ui.Px(120));
            AutoScaleMode = AutoScaleMode.None;

            _label = new Label();
            _label.Text = message;
            _label.AutoSize = false;
            _label.Location = new Point(Ui.Px(20), Ui.Px(22));
            _label.Size = new Size(Ui.Px(380), Ui.Px(40));
            _label.ForeColor = Ui.Text;
            Controls.Add(_label);

            ProgressBar bar = new ProgressBar();
            bar.Style = ProgressBarStyle.Marquee;
            bar.MarqueeAnimationSpeed = 30;
            bar.Location = new Point(Ui.Px(20), Ui.Px(72));
            bar.Size = new Size(Ui.Px(380), Ui.Px(18));
            Controls.Add(bar);

            Shown += delegate { StartWork(); };
        }

        private void StartWork()
        {
            System.Threading.Thread thread = new System.Threading.Thread(delegate()
            {
                try { _work(); }
                catch (Exception ex) { _error = ex; }
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                    {
                        Invoke((MethodInvoker)delegate
                        {
                            _done = true;
                            Close();
                        });
                    }
                }
                catch { }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_done && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        internal Exception Error { get { return _error; } }
    }
}
