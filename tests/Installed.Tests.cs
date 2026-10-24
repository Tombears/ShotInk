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
                Console.InputEncoding = new System.Text.UTF8Encoding(true);
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
                    using (var longImage = new Bitmap(image.Width, image.Height * 10))
                    {
                        using (var graphics = Graphics.FromImage(longImage))
                            for (int index = 0; index < 10; index++) graphics.DrawImageUnscaled(image, 0, index * image.Height);
                        var longResult = recognizer.RecognizeAsync(longImage, CancellationToken.None).GetAwaiter().GetResult();
                        if (longResult.Width != longImage.Width || longResult.Height != longImage.Height) throw new Exception("Long OCR lost original image dimensions.");
                        int total = 0;
                        for (int index = 0; index < 10; index++)
                        {
                            int inSection = 0;
                            foreach (var line in longResult.Lines)
                            {
                                float center = line.Bounds.Top + line.Bounds.Height / 2;
                                if (center >= image.Height * index && center < image.Height * (index + 1)) inSection++;
                                foreach (var character in line.Characters)
                                    if (character.Bounds.Bottom > longImage.Height || character.Bounds.Right > longImage.Width) throw new Exception("Long OCR character lies outside the image.");
                            }
                            if (inSection < result.Lines.Length - 1 || inSection > result.Lines.Length + 1) throw new Exception("Tile boundary lost or duplicated text in section " + index + ": " + inSection);
                            total += inSection;
                        }
                        Console.WriteLine("PASS: long-image OCR returns " + total + " lines across all ten sections, with global character coordinates");
                    }
                    Console.WriteLine("PASS: installed application resolves its bundled OCR process and returns " + characters + " characters");
                }
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
