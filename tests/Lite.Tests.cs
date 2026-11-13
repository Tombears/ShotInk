using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class Tests
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Native.EnableDpiAwareness(); Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                System.Threading.Tasks.Task.Run(new Action(UpdatePartsTests.Run)).GetAwaiter().GetResult();
                if (args.Length == 1 && args[0] == "--skin-tests") { SkinTests.Run(); return 0; }
                if (args.Length == 2 && args[0] == "--skin-previews") { SkinTests.Previews(args[1]); return 0; }
                if (args.Length == 2 && args[0] == "--update-live")
                {
                    var info = UpdateService.CheckAsync(CancellationToken.None).GetAwaiter().GetResult();
                    UpdateService.DownloadAsync(info, Path.Combine(args[1], "target"), CancellationToken.None, new Progress<int>(delegate { }), args[1]).GetAwaiter().GetResult();
                    Console.WriteLine("PASS: lite Gitee index, download and package staging"); return 0;
                }
                SkinTests.Run();
                if (BuildFeatures.Ocr || !UpdateService.IndexUrl.Contains("latest-lite.json")) throw new Exception("Lite update channel is incorrect.");
                using (var options = new SettingsForm(new AppSettings(), delegate { return null; }, delegate { return true; }))
                {
                    if (options.Controls.Find("check-updates", true).Length != 0) throw new Exception("Settings retained automatic update checks.");
                }
                using (var toolbar = new CaptureToolbar())
                {
                    var strip = (ToolStrip)typeof(CaptureToolbar).GetField("strip", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(toolbar);
                    foreach (ToolStripItem item in strip.Items) if (item.Name == "recognize-text") throw new Exception("Lite toolbar still contains OCR.");
                }
                CaptureWorkflowTests.Run();
                GifToolsTests.Run();
                Console.WriteLine("PASS: lite build omits OCR logic and controls; shared GIF tools, scopes, undo and export work"); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
