using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using RapidOcrNet;
using SkiaSharp;

// Reuse bounded, independent ONNX engines across screenshots and long-image tiles.
Console.InputEncoding = new UTF8Encoding(false);
Console.OutputEncoding = new UTF8Encoding(false);
var protocol = Console.Out;
Console.SetOut(Console.Error);
Directory.SetCurrentDirectory(AppContext.BaseDirectory);
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
string model = args.Contains("--fast") ? "tiny" : "small";
using var engines = new OcrEngines(model);
try
{
    engines.EnsureCount(1);
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
        image.SetImmutable();
        var lines = RecognizeImage(engines, image, settings, args.Contains("--serial-tiles") ? 1 : Math.Clamp(Environment.ProcessorCount / 4, 1, 2));
        protocol.WriteLine(JsonSerializer.Serialize(new
        {
            id, width = image.Width, height = image.Height, elapsedMs = clock.Elapsed.TotalMilliseconds,
            engine = "RapidOcrNet / PP-OCRv6-" + model, lines
        }, json));
    }
    catch (Exception error) { protocol.WriteLine(JsonSerializer.Serialize(new { id, error = error.Message }, json)); }
}

static TextLine[] RecognizeImage(OcrEngines engines, SKBitmap image, RapidOcrOptions settings, int workers)
{
    // Detecting a 30,000-pixel image in one pass shrinks normal text to a few
    // pixels. Overlapping tiles preserve readable scale and original coordinates.
    if (image.Width <= 1600 && image.Height <= 1600)
        return engines[0].Detect(image, settings).TextBlocks.Select(block => ConvertLine(block, image.Width, image.Height)).Where(line => line.Text.Length > 0).ToArray();
    const int side = 1280, overlap = 160;
    int[] ys = TileStarts(image.Height, side, overlap);
    workers = Math.Min(workers, ys.Length);
    engines.EnsureCount(workers);
    var results = new TextLine[ys.Length][];
    Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, worker =>
    {
      // Each worker owns an engine and processes only one tile at a time.
      for (int row = worker; row < ys.Length; row += workers)
      {
        var lines = new List<TextLine>();
        int y = ys[row];
        int width = image.Width, height = Math.Min(side, image.Height - y);
        using var tile = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(tile)) { canvas.Clear(SKColors.White); canvas.DrawBitmap(image, 0, -y); }
        float top = row == 0 ? 0 : (ys[row - 1] + side + y) / 2f;
        float bottom = row == ys.Length - 1 ? image.Height : (y + side + ys[row + 1]) / 2f;
        // Keep each text line at full width; horizontal tiles split long lines
        // and can duplicate the characters in their overlap.
        // LimitSideLen targets the SHORT side and used to enlarge narrow tiles unnecessarily.
        // Preserve native text scale, with a cap for unusually wide images.
        foreach (var block in engines[worker].Detect(tile, settings with { ImgResize = Math.Min(2048, Math.Max(width, height)), RecMaxDegreeOfParallelism = 1 }).TextBlocks)
        {
            var line = ConvertLine(block, width, height);
            float centerY = line.Box[1] + line.Box[3] / 2 + y;
            if (line.Text.Length == 0 || centerY < top || centerY >= bottom) continue;
            line.Box[1] += y;
            foreach (var character in line.Characters) character.Box[1] += y;
            lines.Add(line);
        }
        results[row] = lines.ToArray();
      }
    });
    return results.SelectMany(lines => lines).OrderBy(line => line.Box[1]).ThenBy(line => line.Box[0]).ToArray();
}
static int[] TileStarts(int length, int side, int overlap)
{
    var starts = new List<int> { 0 };
    while (starts[^1] + side < length) starts.Add(Math.Min(length - side, starts[^1] + side - overlap));
    return starts.ToArray();
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
sealed class OcrEngines(string model) : IDisposable
{
    private readonly List<RapidOcr> engines = [];
    public RapidOcr this[int index] => engines[index];
    public void EnsureCount(int count)
    {
        while (engines.Count < count)
        {
            var engine = new RapidOcr();
            try
            {
                using var options = RapidOcr.GetDefaultSessionOptions(Math.Clamp(Environment.ProcessorCount / 2, 1, 4));
                options.InterOpNumThreads = 1;
                options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                engine.InitModels(model == "tiny" ? RapidOcrModelSet.PPOCRv6Tiny : RapidOcrModelSet.PPOCRv6Small, options);
                engines.Add(engine);
            }
            catch { engine.Dispose(); throw; }
        }
    }
    public void Dispose() { foreach (var engine in engines) engine.Dispose(); }
}
