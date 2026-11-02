using System;
using System.IO;
using System.Threading;

namespace ShotInk
{
    internal static class InstalledTests
    {
        private static int Main(string[] args)
        {
            string stage = Path.Combine(Path.GetTempPath(), "ShotInk-lite-stage-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (BuildFeatures.Ocr || typeof(InstalledTests).Assembly.GetType("ShotInk.RapidTextRecognizer") != null || Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ocr"))) throw new Exception("Lite package retained OCR.");
                string archive = args[1];
                var info = new UpdateInfo { Version = System.Windows.Forms.Application.ProductVersion, Revision = UpdateService.InstalledRevision(AppDomain.CurrentDomain.BaseDirectory), Sha256 = UpdateService.Digest(archive), Size = new FileInfo(archive).Length,
                    Url = "https://gitee.com/clear_wind_and_rain/ShotInk/releases/download/v" + System.Windows.Forms.Application.ProductVersion + "/" + BuildFeatures.Archive };
                var prepared = UpdateService.Prepare(archive, info, Path.Combine(stage, "target"), Path.Combine(stage, "stage"), int.MaxValue, CancellationToken.None);
                if (UpdateService.ReadJson<UpdatePlan>(File.ReadAllBytes(prepared.PlanPath)).Flavor != "lite") throw new Exception("Lite update plan changed edition.");
                Console.WriteLine("PASS: lite ZIP contains no OCR and stages through its own updater"); return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        }
    }
}
