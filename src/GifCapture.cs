using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class GifFrame
    {
        internal byte[] Png;
        internal int Time;
        internal List<Mark> Annotations = new List<Mark>();
        internal GifFrameCache Cache;
        internal Bitmap DecodeOriginal() { using (var stream = new MemoryStream(Png)) using (var image = Image.FromStream(stream)) return new Bitmap(image); }
        internal Bitmap Decode()
        {
            var snapshot = Annotations; var cached = Cache == null ? null : Cache.Read(this, snapshot);
            return cached ?? Render(snapshot);
        }
        internal Bitmap Render(List<Mark> snapshot)
        {
            var image = DecodeOriginal(); if (snapshot.Count == 0) return image;
            using (image) using (var renderer = new AnnotationRasterRenderer(image, snapshot.Contains)) return renderer.Render(snapshot);
        }

    }
    internal sealed class GifClip
    {
        internal readonly List<GifFrame> Frames = new List<GifFrame>();
        internal int Duration;
        internal bool LimitReached;
        internal int EndTime(int index) { return index + 1 < Frames.Count ? Frames[index + 1].Time : Duration; }
        internal void DeleteRange(int first, int last)
        {
            if (first < 0 || last < first || last >= Frames.Count) throw new ArgumentOutOfRangeException("first");
            int removedTime = EndTime(last) - Frames[first].Time;
            Frames.RemoveRange(first, last - first + 1);
            for (int index = first; index < Frames.Count; index++) Frames[index].Time -= removedTime;
            Duration = Frames.Count == 0 ? 0 : Duration - removedTime;
        }
    }
    internal sealed class GifCaptureSession : IDisposable
    {
        private readonly Func<Bitmap> capture;
        private readonly CancellationTokenSource cancel = new CancellationTokenSource();
        private readonly TaskCompletionSource<bool> finishing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool finish;
        private int disposed;
        private readonly double interval;
        internal GifCaptureSession(Func<Bitmap> capture, int framesPerSecond = 30)
        {
            if (framesPerSecond < 1 || framesPerSecond > 50) throw new ArgumentOutOfRangeException("framesPerSecond");
            this.capture = capture; interval = 1000.0 / framesPerSecond;
        }
        internal void Finish() { finish = true; finishing.TrySetResult(true); }
        internal bool IsFinishing { get { return finish; } }
        internal Task<GifClip> RunAsync()
        {
            var token = cancel.Token;
            return Task.Run(async delegate
            {
                var clip = new GifClip(); var clock = Stopwatch.StartNew(); long bytes = 0; double nextFrame = 0;
                do
                {
                    token.ThrowIfCancellationRequested();
                    int at = (int)clock.ElapsedMilliseconds;
                    using (var image = capture()) using (var png = new MemoryStream())
                    {
                        image.Save(png, ImageFormat.Png);
                        var frame = new GifFrame { Png = png.ToArray(), Time = at };
                        clip.Frames.Add(frame); bytes += frame.Png.Length;
                    }
                    if (clock.ElapsedMilliseconds >= 60000 || bytes >= 192L * 1024 * 1024) { clip.LimitReached = true; break; }
                    nextFrame = Math.Max(nextFrame + interval, clock.Elapsed.TotalMilliseconds);
                    if (!finish)
                    {
                        int delay = (int)Math.Max(1, Math.Ceiling(nextFrame - clock.Elapsed.TotalMilliseconds));
                        await Task.WhenAny(Task.Delay(delay, token), finishing.Task).ConfigureAwait(false);
                    }
                } while (!finish);
                token.ThrowIfCancellationRequested();
                clip.Duration = Math.Max(clip.Frames[clip.Frames.Count - 1].Time + 20, (int)clock.ElapsedMilliseconds);
                return clip;
            });
        }
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) { cancel.Cancel(); cancel.Dispose(); } }
    }
    internal static class GifEncoder
    {
        internal static readonly int Workers = Math.Max(1, Math.Min(4, Environment.ProcessorCount - 1));
        private sealed class EncodedFrame { internal int Width, Height; internal byte[] Data; }
        private static EncodedFrame EncodeFrame(GifFrame source, CancellationToken token)
        {
            GifRenderWorkers.Gate.Wait(token);
            try
            {
                token.ThrowIfCancellationRequested();
                using (var bitmap = source.Decode()) using (var stream = new MemoryStream())
                { bitmap.Save(stream, ImageFormat.Gif); return new EncodedFrame { Width = bitmap.Width, Height = bitmap.Height, Data = stream.ToArray() }; }
            }
            finally { GifRenderWorkers.Gate.Release(); }
        }
        // Use GDI+'s palette quantizer and LZW encoder for each frame, then assemble
        // full-size local-palette frames with explicit timing and an infinite loop.
        internal static byte[] Encode(GifClip clip, int first, int last, CancellationToken cancellation = default(CancellationToken))
        {
            if (first < 0 || last < first || last >= clip.Frames.Count) throw new ArgumentOutOfRangeException("first");
            using (var output = new MemoryStream()) using (var writer = new BinaryWriter(output))
            {
                int width = 0, height = 0, writtenTime = 0;
                EncodedFrame[] batch = null; int batchStart = first;
                for (int index = first; index <= last; index++)
                {
                cancellation.ThrowIfCancellationRequested();
                if (batch == null || index >= batchStart + batch.Length)
                {
                    batchStart = index; batch = new EncodedFrame[Math.Min(Workers * 2, last - index + 1)];
                    Parallel.For(0, batch.Length, new ParallelOptions { MaxDegreeOfParallelism = Workers, CancellationToken = cancellation },
                        delegate(int at) { batch[at] = EncodeFrame(clip.Frames[batchStart + at], cancellation); });
                }
                var encoded = batch[index - batchStart];
                {
                    if (index == first)
                    {
                        width = encoded.Width; height = encoded.Height;
                        writer.Write(Encoding.ASCII.GetBytes("GIF89a")); writer.Write((ushort)width); writer.Write((ushort)height);
                        writer.Write((byte)0x70); writer.Write((byte)0); writer.Write((byte)0);
                        writer.Write(new byte[] { 0x21, 0xff, 11 }); writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0")); writer.Write(new byte[] { 3, 1, 0, 0, 0 });
                    }
                    if (encoded.Width != width || encoded.Height != height) throw new InvalidOperationException("GIF 帧尺寸发生变化。");
                    var data = encoded.Data;
                    int globalSize = (data[10] & 0x80) != 0 ? 3 * (1 << ((data[10] & 7) + 1)) : 0;
                    int offset = 13 + globalSize;
                    while (data[offset] == 0x21) { offset += 2; SkipBlocks(data, ref offset); }
                    if (data[offset] != 0x2c) throw new InvalidDataException("GIF 帧数据无效。");
                    int packed = data[offset + 9], paletteSize = (packed & 0x80) != 0 ? 3 * (1 << ((packed & 7) + 1)) : globalSize;
                    int paletteAt = (packed & 0x80) != 0 ? offset + 10 : 13;
                    if (paletteSize == 0) throw new InvalidDataException("GIF 缺少调色板。");
                    int end = offset + 10 + ((packed & 0x80) != 0 ? paletteSize : 0), payload = end;
                    end++; SkipBlocks(data, ref end);
                    int desired = Math.Max(writtenTime + 2, (int)Math.Round((clip.EndTime(index) - clip.Frames[first].Time) / 10.0));
                    int delay = Math.Min(65535, desired - writtenTime); writtenTime += delay;
                    writer.Write(new byte[] { 0x21, 0xf9, 4, 4 }); writer.Write((ushort)delay); writer.Write((byte)0); writer.Write((byte)0);
                    writer.Write(data, offset, 9); writer.Write((byte)((packed & 0x78) | 0x80 | ((packed & 0x80) != 0 ? packed & 7 : data[10] & 7)));
                    writer.Write(data, paletteAt, paletteSize); writer.Write(data, payload, end - payload);
                }
                }
                writer.Write((byte)0x3b); writer.Flush(); return output.ToArray();
            }
        }
        private static void SkipBlocks(byte[] data, ref int at)
        { while (data[at] != 0) { int size = data[at++]; at += size; } at++; }
    }
    internal static class GifExport
    {
        internal static string CacheDirectory
        {
            get
            {
                string root = Directory.Exists(@"E:\DevData") ? @"E:\DevData\ShotInk" : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShotInk");
                return Path.Combine(root, "gif-clipboard");
            }
        }
        internal static string WriteNew(byte[] bytes, string directory)
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "录屏_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".gif");
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) stream.Write(bytes, 0, bytes.Length);
            return path;
        }
        internal static DataObject ClipboardData(byte[] bytes, string path)
        {
            var data = new DataObject();
            data.SetFileDropList(new System.Collections.Specialized.StringCollection { path });
            data.SetData("GIF", new MemoryStream(bytes));
            return data;
        }
        internal static bool Save(IWin32Window owner, byte[] bytes, AppSettings settings)
        {
            if (!settings.AskSaveLocation) { WriteNew(bytes, settings.SaveDirectory); return true; }
            using (var dialog = new SaveFileDialog { Filter = "GIF 动图 (*.gif)|*.gif", DefaultExt = "gif", AddExtension = true, FileName = "录屏_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".gif", InitialDirectory = settings.SaveDirectory, RestoreDirectory = true })
            { if (dialog.ShowDialog(owner) != DialogResult.OK) return false; File.WriteAllBytes(dialog.FileName, bytes); return true; }
        }
    }
}
