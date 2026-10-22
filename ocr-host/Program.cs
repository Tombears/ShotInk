using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using RapidOcrNet;
using SkiaSharp;

// One process and one set of ONNX sessions are retained across screenshots.
Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
var protocol = Console.Out;
Console.SetOut(Console.Error);
Directory.SetCurrentDirectory(AppContext.BaseDirectory);
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
string model = args.Contains("--fast") ? "tiny" : "small";
using var ocr = new RapidOcr();
try
{
    using var options = RapidOcr.GetDefaultSessionOptions(Math.Clamp(Environment.ProcessorCount / 2, 1, 4));
    options.InterOpNumThreads = 1;
    options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
    ocr.InitModels(model == "tiny" ? RapidOcrModelSet.PPOCRv6Tiny : RapidOcrModelSet.PPOCRv6Small, options);
}
catch (Exception error)
{
    string message = error.GetBaseException().Message;
    if (OperatingSystem.IsWindows() && error.GetBaseException() is DllNotFoundException)
        message += "\n请确认已完整解压便携包，并安装 Microsoft Visual C++ 2015–2022 x64 运行库（下载链接见 README）。";
    protocol.WriteLine(JsonSerializer.Serialize(new { ready = false, error = message }, json));
    return;
}
var settings = RapidOcrOptions.PPOCRv6 with
{
    DoAngle = false, ReturnWordBox = true, ReturnSingleCharBox = true,
    LimitSideLen = args.Contains("--quality") ? 736 : 480,
    RecMaxDegreeOfParallelism = Environment.ProcessorCount >= 4 ? 2 : 1,
    TextScore = 0.45f
};
protocol.WriteLine(JsonSerializer.Serialize(new { ready = true, engine = "RapidOcrNet / PP-OCRv6-" + model }, json));
while (Console.ReadLine() is { } requestLine)
{
    int id = 0;
    try
    {
        // .NET Framework's redirected UTF-8 stdin may emit a preamble before
        // the first request. Console.ReadLine preserves it in redirected input.
        var request = JsonSerializer.Deserialize<Request>(requestLine.TrimStart('\uFEFF'), json) ?? throw new InvalidDataException("Empty request");
        id = request.Id;
        using var image = SKBitmap.Decode(Convert.FromBase64String(request.Png)) ?? throw new InvalidDataException("Invalid image");
        var clock = Stopwatch.StartNew();
        var result = ocr.Detect(image, settings);
        var lines = result.TextBlocks.Select(block => ConvertLine(block, image.Width, image.Height)).Where(line => line.Text.Length > 0).ToArray();
        protocol.WriteLine(JsonSerializer.Serialize(new
        {
            id, width = image.Width, height = image.Height, elapsedMs = clock.Elapsed.TotalMilliseconds,
            engine = "RapidOcrNet / PP-OCRv6-" + model, lines
        }, json));
    }
    catch (Exception error) { protocol.WriteLine(JsonSerializer.Serialize(new { id, error = error.Message }, json)); }
}

static TextLine ConvertLine(TextBlock block, int width, int height)
{
    string text = Filter(block.Text);
    var characters = new List<Character>();
    int position = 0;
    foreach (var word in block.WordResults ?? [])
    {
        string character = Filter(word.Text);
        if (character.Length == 0) continue;
        int start = text.IndexOf(character, position, StringComparison.Ordinal);
        if (start < 0) continue;
        characters.Add(new Character(character, Box(word.BoxPoints, width, height), start, character.Length, word.Score));
        position = start + character.Length;
    }
    return new TextLine(text, Box(block.BoxPoints, width, height), characters.ToArray());
}
static float[] Box(SKPointI[] points, int width, int height)
{
    float left = Math.Clamp(points.Min(p => p.X), 0, width), top = Math.Clamp(points.Min(p => p.Y), 0, height);
    float right = Math.Clamp(points.Max(p => p.X), 0, width), bottom = Math.Clamp(points.Max(p => p.Y), 0, height);
    return [left, top, Math.Max(1, right - left), Math.Max(1, bottom - top)];
}
static string Filter(string text)
{
    var filtered = new StringBuilder();
    foreach (var rune in text.EnumerateRunes())
    {
        int code = rune.Value;
        var category = Rune.GetUnicodeCategory(rune);
        if (code is >= 0x20 and <= 0x7e || code is >= 0x3400 and <= 0x9fff || code is >= 0xf900 and <= 0xfaff || code is >= 0x20000 and <= 0x323af ||
            code is >= 0x2000 and <= 0x206f || code is >= 0x3000 and <= 0x303f || code is >= 0xff01 and <= 0xff60 ||
            category is UnicodeCategory.OpenPunctuation or UnicodeCategory.ClosePunctuation or UnicodeCategory.OtherPunctuation or UnicodeCategory.CurrencySymbol)
            filtered.Append(rune);
    }
    return filtered.ToString();
}
sealed record Request(int Id, string Png);
sealed record Character(string Text, float[] Box, int Start, int Length, float Score);
sealed record TextLine(string Text, float[] Box, Character[] Characters);
