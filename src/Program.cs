using System;
using System.Threading;
using System.Windows.Forms;

namespace SnipasteLite
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool first;
            using (var instance = new Mutex(true, "Local\\SnipasteLite.Desktop", out first))
            {
                if (!first)
                {
                    MessageBox.Show("Snipaste Lite 已在运行，请从系统托盘打开。", "Snipaste Lite");
                    return;
                }
                Native.EnableDpiAwareness();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    MessageBox.Show(e.Exception.Message, "操作未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                };
                Application.Run(new MainForm());
            }
        }
    }
}
