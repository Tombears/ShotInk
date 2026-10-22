using System;
using System.Drawing;
using System.Threading;

namespace ShotInk
{
    internal static class InstalledTests
    {
        private static int Main(string[] args)
        {
            try
            {
                using (var recognizer = new RapidTextRecognizer())
                using (var image = new Bitmap(args[0]))
                {
                    var result = recognizer.RecognizeAsync(image, CancellationToken.None).GetAwaiter().GetResult();
                    if (result.Width != image.Width || result.Height != image.Height || result.Lines == null || result.Lines.Length == 0)
                        throw new Exception("Installed OCR returned invalid image coordinates or no text.");
                    int characters = 0;
                    foreach (var line in result.Lines) if (line.Characters != null) characters += line.Characters.Length;
                    if (characters == 0) throw new Exception("Installed OCR returned no character coordinates.");
                    var cached = recognizer.RecognizeAsync(image, CancellationToken.None).GetAwaiter().GetResult();
                    if (!object.ReferenceEquals(cached, result)) throw new Exception("Installed OCR cache did not reuse its result.");
                    Console.WriteLine("PASS: installed application resolves its bundled OCR process and returns " + characters + " characters");
                }
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
