using System;
using System.Threading;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Native.RefreshExecutableIcon(Application.ExecutablePath);
            bool first;
            // Share the legacy mutex so both names cannot register the same hotkeys.
            using (var instance = new Mutex(true, "Local\\SnipasteLite.Desktop", out first))
            {
                if (!first)
                {
                    MessageBox.Show("ShotInk 已在运行。若要使用刚编译或下载的程序，请先从系统托盘退出正在运行的 ShotInk，再重新启动。\n\n本次启动：" + Application.ProductVersion + "\n" + Application.ExecutablePath, "ShotInk");
                    return;
                }
                Native.EnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    MessageBox.Show(e.Exception.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };
                Application.ApplicationExit += delegate { RapidTextRecognizer.Shared.Dispose(); };
                Application.Run(new MainForm());
            }
        }
    }
}
