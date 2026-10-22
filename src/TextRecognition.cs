using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShotInk
{
    [DataContract]
    internal sealed class OcrDocument
    {
        [DataMember(Name = "id")] public int Id;
        [DataMember(Name = "ready")] public bool Ready;
        [DataMember(Name = "width")] public int Width;
        [DataMember(Name = "height")] public int Height;
        [DataMember(Name = "engine")] public string Engine;
        [DataMember(Name = "elapsedMs")] public double ElapsedMs;
        [DataMember(Name = "error")] public string Error;
        [DataMember(Name = "lines")] public OcrLine[] Lines;
    }
    [DataContract]
    internal sealed class OcrLine
    {
        [DataMember(Name = "text")] public string Text;
        [DataMember(Name = "box")] public float[] Box;
        [DataMember(Name = "characters")] public OcrCharacter[] Characters;
        internal RectangleF Bounds { get { return OcrGeometry.Bounds(Box); } }
    }
    [DataContract]
    internal sealed class OcrCharacter
    {
        [DataMember(Name = "text")] public string Text;
        [DataMember(Name = "box")] public float[] Box;
        [DataMember(Name = "start")] public int Start;
        [DataMember(Name = "length")] public int Length;
        [DataMember(Name = "score")] public float Score;
        internal RectangleF Bounds { get { return OcrGeometry.Bounds(Box); } }
    }
    internal static class OcrGeometry
    {
        internal static RectangleF Bounds(float[] box)
        { return box == null || box.Length != 4 ? RectangleF.Empty : new RectangleF(box[0], box[1], Math.Max(0, box[2]), Math.Max(0, box[3])); }
    }
    internal interface ITextRecognizer
    {
        Task<OcrDocument> RecognizeAsync(Bitmap image, CancellationToken cancellation);
    }
    internal sealed class RapidTextRecognizer : ITextRecognizer, IDisposable
    {
        internal static readonly RapidTextRecognizer Shared = new RapidTextRecognizer();
        private readonly string executable;
        private readonly string arguments;
        private readonly string workingDirectory;
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        private Process worker;
        private string lastHash;
        private OcrDocument lastResult;
        private int requestId;
        private bool disposed;
        private readonly StringBuilder diagnostics = new StringBuilder();

        internal RapidTextRecognizer() : this(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ocr", "ShotInk.OcrHost.exe"), "", Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ocr")) { }
        internal RapidTextRecognizer(string executable, string arguments, string workingDirectory)
        { this.executable = executable; this.arguments = arguments; this.workingDirectory = workingDirectory; }
        public Task<OcrDocument> RecognizeAsync(Bitmap image, CancellationToken cancellation)
        {
            return Task.Run(async delegate
            {
                await gate.WaitAsync(cancellation);
                try
                {
                    if (disposed) throw new ObjectDisposedException("RapidTextRecognizer");
                    cancellation.ThrowIfCancellationRequested();
                    byte[] png;
                    using (var stream = new MemoryStream()) { image.Save(stream, ImageFormat.Png); png = stream.ToArray(); }
                    string hash;
                    using (var sha = SHA256.Create()) hash = Convert.ToBase64String(sha.ComputeHash(png));
                    if (hash == lastHash && lastResult != null) return lastResult;
                    await EnsureStarted();
                    int id = ++requestId;
                    await worker.StandardInput.WriteLineAsync("{\"id\":" + id + ",\"png\":\"" + Convert.ToBase64String(png) + "\"}");
                    var result = Parse(await ReadLine(60000));
                    if (!string.IsNullOrWhiteSpace(result.Error)) throw new InvalidOperationException(result.Error);
                    if (result.Id != id) { ResetWorker(); throw new InvalidOperationException("文字识别响应不匹配，请重试。"); }
                    if (result.Width != image.Width || result.Height != image.Height) throw new InvalidOperationException("文字坐标与图片尺寸不匹配。");
                    lastHash = hash; lastResult = result;
                    cancellation.ThrowIfCancellationRequested();
                    return result;
                }
                catch (IOException) { ResetWorker(); throw new InvalidOperationException("文字识别组件已关闭，请重新识别。"); }
                finally { gate.Release(); }
            }, cancellation);
        }
        internal Task WarmUpAsync()
        {
            return Task.Run(async delegate
            {
                await gate.WaitAsync();
                try { if (!disposed) await EnsureStarted(); }
                catch { ResetWorker(); } // A click reports any startup error; hovering stays quiet.
                finally { gate.Release(); }
            });
        }
        private async Task EnsureStarted()
        {
            if (worker != null && !worker.HasExited) return;
            ResetWorker();
            if (!File.Exists(executable)) throw new InvalidOperationException("缺少文字识别组件。请运行 ShotInk-Setup.exe 完整安装包，或将便携包完整解压后再启动程序。");
            var start = new ProcessStartInfo(executable, arguments) { WorkingDirectory = workingDirectory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = new UTF8Encoding(false) };
            worker = new Process { StartInfo = start };
            lock (diagnostics) diagnostics.Clear();
            worker.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock (diagnostics) { if (diagnostics.Length < 2000) diagnostics.AppendLine(e.Data); } };
            worker.Start(); worker.StandardInput.AutoFlush = true; worker.BeginErrorReadLine();
            try { var ready = Parse(await ReadLine(45000)); if (!ready.Ready) throw new InvalidOperationException("文字识别组件初始化失败。" + ready.Error); }
            catch { ResetWorker(); throw; }
        }
        private async Task<string> ReadLine(int timeout)
        {
            var read = worker.StandardOutput.ReadLineAsync();
            if (await Task.WhenAny(read, Task.Delay(timeout)) != read) { ResetWorker(); throw new TimeoutException("文字识别超时，请缩小区域后重试。"); }
            string line = await read;
            if (line == null)
            {
                string error; lock (diagnostics) error = diagnostics.ToString().Trim();
                ResetWorker(); throw new InvalidOperationException("文字识别组件启动失败。" + (error.Length > 0 ? "\n" + error : ""));
            }
            return line;
        }
        internal static OcrDocument Parse(string json)
        {
            using (var data = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (OcrDocument)new DataContractJsonSerializer(typeof(OcrDocument)).ReadObject(data);
        }
        private void ResetWorker()
        {
            var process = worker; worker = null;
            if (process == null) return;
            try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            process.Dispose();
        }
        public void Dispose() { disposed = true; ResetWorker(); }
    }

    internal sealed class TextRecognitionSession : IDisposable
    {
        private readonly Form owner;
        private readonly Func<Bitmap> render;
        private readonly Func<int> revision;
        private readonly Action<bool> setBusy;
        private readonly ITextRecognizer recognizer;
        private readonly Action<OcrDocument> showResult;
        private readonly Action<string> showError;
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool busy;
        private bool disposed;
        internal TextRecognitionSession(Form owner, Func<Bitmap> render, Func<int> revision, Action<bool> setBusy, Action<OcrDocument> showResult)
            : this(owner, render, revision, setBusy, RapidTextRecognizer.Shared, showResult,
                delegate(string error) { MessageBox.Show(owner, error, "文字识别", MessageBoxButtons.OK, MessageBoxIcon.Information); }) { }
        internal TextRecognitionSession(Form owner, Func<Bitmap> render, Func<int> revision, Action<bool> setBusy, ITextRecognizer recognizer, Action<OcrDocument> showResult, Action<string> showError)
        { this.owner = owner; this.render = render; this.revision = revision; this.setBusy = setBusy; this.recognizer = recognizer; this.showResult = showResult; this.showError = showError; }
        internal async Task RecognizeAsync()
        {
            if (disposed || busy) return;
            busy = true; int original = revision();
            try
            {
                setBusy(true);
                using (var image = render())
                {
                    var result = await recognizer.RecognizeAsync(image, cancellation.Token);
                    if (disposed || owner.IsDisposed || owner.Disposing) return;
                    if (revision() != original) { showError("图片已改变，请重新点击文字识别。"); return; }
                    if (result.Lines == null || result.Lines.Length == 0) showError("未识别到文字，请选择包含清晰文字的区域后重试。");
                    else showResult(result);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!disposed && !owner.IsDisposed && !owner.Disposing) showError("文字识别失败：" + error.Message); }
            finally { busy = false; if (disposed) cancellation.Dispose(); else if (!owner.IsDisposed && !owner.Disposing) setBusy(false); }
        }
        public void Dispose() { if (disposed) return; disposed = true; cancellation.Cancel(); if (!busy) cancellation.Dispose(); }
    }
}
