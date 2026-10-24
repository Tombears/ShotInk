using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace ShotInk
{
    internal sealed class ScrollCaptureResult : IDisposable
    {
        internal Bitmap Image;
        internal bool LimitReached;
        internal bool AlignmentLost;
        internal int Frames;
        internal Bitmap TakeImage() { var image = Image; Image = null; return image; }
        public void Dispose() { if (Image != null) Image.Dispose(); }
    }

    // Preserve sampled frames in order. Back pressure never replaces an overlap frame.
    internal sealed class ScrollCaptureSession : IDisposable
    {
        internal const int PollInterval = 10;
        internal const int QueueCapacity = 8;
        private readonly Func<Bitmap> capture;
        private readonly int heightLimit;
        private readonly long pixelLimit;
        private readonly object gate = new object();
        private sealed class CapturedFrame { internal Bitmap Image; internal int Direction; }
        private readonly Queue<CapturedFrame> pending = new Queue<CapturedFrame>();
        private readonly SemaphoreSlim changed = new SemaphoreSlim(0);
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private bool producerDone;
        private volatile bool finishRequested;
        private int started;
        private int disposed;
        private int stitchedHeight;
        private int stitchedFrames;
        private int capturedVersion, processedVersion, lastChangedAt = Environment.TickCount, alignmentLost;
        private int scrollDirection;
        internal Action BeforeStitch;
        internal bool IsFinishing { get { return finishRequested; } }
        internal bool IsCaughtUp { get { return Volatile.Read(ref processedVersion) > 0 && Volatile.Read(ref processedVersion) == Volatile.Read(ref capturedVersion); } }
        internal int PendingFrames { get { return Volatile.Read(ref capturedVersion) - Volatile.Read(ref processedVersion); } }
        internal int ProcessedVersion { get { return Volatile.Read(ref processedVersion); } }
        internal int StableMilliseconds { get { return unchecked(Environment.TickCount - Volatile.Read(ref lastChangedAt)); } }
        internal bool AlignmentLost { get { return Volatile.Read(ref alignmentLost) != 0; } }
        internal int Height { get { return Volatile.Read(ref stitchedHeight); } }
        internal int Frames { get { return Volatile.Read(ref stitchedFrames); } }
        internal int PeakPendingFrames { get; private set; }
        internal ScrollCaptureSession(Func<Bitmap> capture, int heightLimit = VerticalStitcher.MaxHeight, long pixelLimit = VerticalStitcher.MaxPixels)
        { this.capture = capture; this.heightLimit = heightLimit; this.pixelLimit = pixelLimit; }
        internal void Finish() { finishRequested = true; }
        internal void NotifyWheel(int delta) { if (delta != 0) Volatile.Write(ref scrollDirection, delta < 0 ? 1 : -1); }
        internal Task<ScrollCaptureResult> RunAsync()
        {
            if (disposed != 0) throw new ObjectDisposedException("ScrollCaptureSession");
            if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("长截图已启动。");
            return Task.Run(async delegate
            {
                var token = cancellation.Token;
                var producer = Task.Run(async delegate
                {
                    ulong previous = 0; bool first = true;
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        while (true)
                        {
                            long sampleStarted = clock.ElapsedMilliseconds;
                            token.ThrowIfCancellationRequested();
                            while (true)
                            {
                                lock (gate) { if (pending.Count < QueueCapacity) break; }
                                if (finishRequested) return;
                                await Task.Delay(1, token).ConfigureAwait(false);
                            }
                            int direction = Volatile.Read(ref scrollDirection);
                            var frame = capture();
                            try
                            {
                                ulong signature = Signature(frame);
                                if (first || signature != previous)
                                {
                                    first = false; previous = signature;
                                    lock (gate)
                                    {
                                        pending.Enqueue(new CapturedFrame { Image = frame, Direction = direction });
                                        Interlocked.Increment(ref capturedVersion); Volatile.Write(ref lastChangedAt, Environment.TickCount);
                                        changed.Release();
                                        frame = null; PeakPendingFrames = Math.Max(PeakPendingFrames, pending.Count);
                                    }
                                }
                            }
                            finally { if (frame != null) frame.Dispose(); }
                            if (finishRequested) break;
                            await Task.Delay((int)Math.Max(1, PollInterval - (clock.ElapsedMilliseconds - sampleStarted)), token).ConfigureAwait(false);
                        }
                    }
                    finally { lock (gate) { producerDone = true; changed.Release(); } }
                });
                ScrollCaptureResult output = null; Exception failure = null;
                try
                {
                    using (var stitcher = new VerticalStitcher(heightLimit, pixelLimit))
                    {
                        bool limit = false;
                        while (true)
                        {
                            await changed.WaitAsync(token).ConfigureAwait(false);
                            CapturedFrame frame = null;
                            lock (gate)
                            {
                                if (pending.Count > 0) frame = pending.Dequeue();
                                else if (producerDone) break;
                            }
                            if (frame == null) continue;
                            StitchStatus status;
                            using (frame.Image) { if (BeforeStitch != null) BeforeStitch(); status = stitcher.Add(frame.Image, frame.Direction); }
                            Volatile.Write(ref alignmentLost, status == StitchStatus.Unmatched ? 1 : 0);
                            Volatile.Write(ref stitchedHeight, stitcher.Height); Volatile.Write(ref stitchedFrames, stitcher.Frames);
                            Interlocked.Increment(ref processedVersion);
                            if (status == StitchStatus.LimitReached || stitcher.Height >= heightLimit || (long)stitcher.Width * stitcher.Height >= pixelLimit)
                            { limit = true; Finish(); break; }
                        }
                        await producer.ConfigureAwait(false); token.ThrowIfCancellationRequested();
                        if (stitcher.Frames == 0) throw new InvalidOperationException("选区超过长截图尺寸上限。");
                        output = new ScrollCaptureResult { Image = stitcher.Build(), Frames = stitcher.Frames, LimitReached = limit, AlignmentLost = AlignmentLost };
                    }
                }
                catch (Exception error) { failure = error; }
                cancellation.Cancel();
                try { await producer.ConfigureAwait(false); } catch (Exception error) { if (failure == null) failure = error; }
                lock (gate) { while (pending.Count > 0) pending.Dequeue().Image.Dispose(); }
                changed.Dispose(); cancellation.Dispose();
                if (failure != null) { if (output != null) output.Dispose(); System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw(); }
                return output;
            });
        }
        private static ulong Signature(Bitmap image)
        {
            int bytes = System.Drawing.Image.GetPixelFormatSize(image.PixelFormat) / 8;
            if (bytes < 3) throw new InvalidOperationException("不支持的截图像素格式。");
            var data = image.LockBits(new Rectangle(Point.Empty, image.Size), ImageLockMode.ReadOnly, image.PixelFormat);
            try
            {
                ulong hash = 1469598103934665603UL;
                for (int y = 0; y < image.Height; y += Math.Max(1, image.Height / 48))
                    for (int x = 0; x < image.Width; x += Math.Max(1, image.Width / 64))
                    {
                        int at = y * data.Stride + x * bytes;
                        for (int channel = 0; channel < 3; channel++)
                            hash = unchecked((hash ^ Marshal.ReadByte(data.Scan0, at + channel)) * 1099511628211UL);
                    }
                return hash;
            }
            finally { image.UnlockBits(data); }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) != 0) return;
            try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
            if (started == 0) { cancellation.Dispose(); changed.Dispose(); }
        }
    }
}
