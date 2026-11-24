using System;
using System.Drawing;
using System.Windows.Forms;

namespace BambuMultiLauncher
{
    /// <summary>内置使用说明，保证离线也能看懂怎么用。</summary>
    internal sealed class HelpForm : Form
    {
        internal HelpForm()
        {
            Text = "使用说明 · " + AppPaths.AppName;
            Font = Ui.BaseFont;
            BackColor = Ui.Card;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Ui.Px(720), Ui.Px(540));
            MinimumSize = new Size(Ui.Px(520), Ui.Px(360));

            TextBox box = new TextBox();
            box.Multiline = true;
            box.ReadOnly = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.WordWrap = true;
            box.BorderStyle = BorderStyle.None;
            box.BackColor = Color.White;
            box.Font = Ui.BaseFont;
            box.Text = Guide();
            box.Select(0, 0);
            box.Dock = DockStyle.Fill;

            Panel canvas;
            Panel bottom = Ui.Stack(this, 700, 620, 56, out canvas);
            canvas.Padding = new Padding(Ui.Px(18), Ui.Px(14), Ui.Px(18), Ui.Px(10));
            canvas.Controls.Add(box);

            Button copy = Ui.SecondaryButton("复制说明", 96);
            copy.Location = new Point(Ui.Px(12), Ui.Px(12));
            copy.Click += delegate
            {
                try { Clipboard.SetText(box.Text); copy.Text = "已复制"; }
                catch { }
            };
            bottom.Controls.Add(copy);

            Button cfg = Ui.SecondaryButton("配置目录", 96);
            cfg.Location = new Point(Ui.Px(116), Ui.Px(12));
            cfg.Click += delegate { LaunchService.OpenFolder(AppPaths.ConfigDir); };
            bottom.Controls.Add(cfg);

            Button close = Ui.PrimaryButton("关闭", 90);
            close.Location = new Point(ClientSize.Width - Ui.Px(110), Ui.Px(12));
            close.DialogResult = DialogResult.OK;
            bottom.Controls.Add(close);
            bottom.Resize += delegate
            {
                close.Location = new Point(bottom.Width - close.Width - Ui.Px(12), Ui.Px(12));
            };
            AcceptButton = close;
            CancelButton = close;
        }

        private static string Guide()
        {
            return
"【它是做什么的】\r\n" +
"Bambu Studio 一台电脑只能登录一个拓竹账号，换账号要退出、重新登录、收验证码。\r\n" +
"这个工具给每个账号一份独立的数据目录，每个账号一个独立窗口，登录状态各自记住，互不顶号。\r\n" +
"打印机不需要开“局域网模式”，仍然按原来的云端方式连接。\r\n" +
"\r\n" +
"【第一次使用】\r\n" +
"1. 打开程序，跟着三步向导走：确认环境 → 起名字 → 完成。\r\n" +
"2. 如果加了“第二个账号”，第一次启动它时，在弹出的 Bambu Studio 里用另一个账号登录一次，收到验证码就填。\r\n" +
"3. 登录完成后正常使用。以后打开这个工具，点一下账号就能直接进，不用再登录。\r\n" +
"\r\n" +
"【日常使用】\r\n" +
"· 双击列表里的一行 = 启动这个账号（已经开着的话会把它的窗口切到前面）。\r\n" +
"· “全部启动” = 把每个账号的窗口都打开，两个账号可以同时在线。\r\n" +
"· “运行”一列显示 运行中 / 未运行，“登录”一列显示这个账号是否已经登录过。\r\n" +
"· 右键那一行还有：打开数据目录、复制目录路径。\r\n" +
"\r\n" +
"【关于“默认账号”】\r\n" +
"名字后面标[默认]的那个账号，用的是 Bambu Studio 自己的数据目录。\r\n" +
"它的好处是：即使你不用这个工具，直接双击原版 Bambu Studio 快捷方式，也还是这个账号，什么都不变。\r\n" +
"\r\n" +
"【常见问题】\r\n" +
"Q: 换了新电脑，怎么把登录状态带过去？\r\n" +
"A: 把 %APPDATA%\\BambuMultiLauncher 整个目录拷过去就行（里面就是各账号的数据目录）。\r\n" +
"\r\n" +
"Q: 提示“没有找到 Bambu Studio”？\r\n" +
"A: 打开“高级设置”，用“浏览…”指定 bambu-studio.exe，或者点“自动检测”。\r\n" +
"\r\n" +
"Q: 某个窗口打开后要求重新登录，或者提示已在别处登录？\r\n" +
"A: 同一个账号不要在多个窗口里同时登录。要换账号用另一个账号的窗口，不要在同一份数据目录上反复登录。\r\n" +
"\r\n" +
"Q: 家里打印机显示离线？\r\n" +
"A: 这是打印机和云端之间的问题，跟本工具无关。确认打印机联网、账号里能看到这台机器。\r\n" +
"\r\n" +
"Q: 会不会把我的登录信息传到网上？\r\n" +
"A: 不会。程序只在本机启动 bambu-studio.exe，不联网、不读也不上传任何凭据。\r\n" +
"   所有数据都在你自己的电脑上：配置在 %APPDATA%\\BambuMultiLauncher。\r\n" +
"\r\n" +
"Q: 不想用了怎么清理？\r\n" +
"A: 删除程序文件，再删除 %APPDATA%\\BambuMultiLauncher 即可。\r\n" +
"   默认账号的数据在 %APPDATA%\\BambuStudio，那本来就是 Bambu Studio 自己的，不要动。\r\n" +
"\r\n" +
"【原理（给好奇的人）】\r\n" +
"Bambu Studio 支持 --datadir 参数，用来指定自己的数据目录。\r\n" +
"本工具就是给每个账号准备一份目录，启动时传进去：\r\n" +
"    bambu-studio.exe --datadir \"...\\profiles\\公司\"\r\n" +
"登录凭据（BambuNetworkEngine.conf）就存在各自的目录里，所以互不干扰。\r\n" +
"默认账号不传这个参数，用的是 Bambu Studio 本来那套。\r\n" +
"\r\n" +
"版本 " + AppPaths.Version + " · " + AppPaths.AppNameEn + "\r\n";
        }
    }
}
