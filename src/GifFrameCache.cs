using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ShotInk
{
    internal static class GifRenderWorkers
    { internal static readonly SemaphoreSlim Gate = new SemaphoreSlim(GifEncoder.Workers); }
    internal sealed class GifFrameCache : IDisposable
    {
        internal const long Budget = 64L * 1024 * 1024;
        private readonly object gate = new object();
        private readonly Dictionary<GifFrame, Entry> entries = new Dictionary<GifFrame, Entry>();
        private CancellationTokenSource pending;
        private Task work = Task.FromResult(true);
        private bool disposed;
        private long bytes, clock;
        internal Exception LastError { get; private set; }
        private sealed class Entry { internal List<Mark> State; internal Bitmap Image; internal long Bytes, Used; }
        internal Bitmap Read(GifFrame frame, List<Mark> state)
        {
            lock (gate)
            {
                Entry entry;
                if (!disposed && entries.TryGetValue(frame, out entry) && ReferenceEquals(entry.State, state))
                { entry.Used = ++clock; return new Bitmap(entry.Image); }
                return null;
            }
        }
        internal void Schedule(IEnumerable<GifFrame> nearby)
        {
            var frames = nearby.Distinct().Take(GifEncoder.Workers * 2).Select(frame => new { Frame = frame, State = frame.Annotations }).ToArray();
            lock (gate)
            {
                if (disposed) return; if (pending != null) pending.Cancel();
                var cancellation = new CancellationTokenSource(); pending = cancellation; LastError = null;
                work = Task.Run(delegate
                {
                    try
                    {
                        Parallel.ForEach(frames, new ParallelOptions { MaxDegreeOfParallelism = GifEncoder.Workers, CancellationToken = cancellation.Token }, item =>
                        {
                            cancellation.Token.ThrowIfCancellationRequested();
                            Bitmap image; GifRenderWorkers.Gate.Wait(cancellation.Token);
                            try { image = item.Frame.Render(item.State); } finally { GifRenderWorkers.Gate.Release(); }
                            lock (gate)
                            {
                                long size = (long)image.Width * image.Height * 4;
                                if (disposed || cancellation.IsCancellationRequested || !ReferenceEquals(item.Frame.Annotations, item.State) || size > Budget) { image.Dispose(); return; }
                                Remove(item.Frame);
                                while (bytes + size > Budget && entries.Count > 0) Remove(entries.OrderBy(pair => pair.Value.Used).First().Key);
                                entries.Add(item.Frame, new Entry { Image = image, State = item.State, Bytes = size, Used = ++clock }); bytes += size;
                            }
                        });
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception error) { lock (gate) if (!disposed && !cancellation.IsCancellationRequested) LastError = error; }
                    finally { lock (gate) { if (ReferenceEquals(pending, cancellation)) pending = null; cancellation.Dispose(); } }
                });
            }
        }
        internal Task CancelPrefetch()
        { lock (gate) { if (pending != null) pending.Cancel(); return work; } }
        internal Task Pending { get { lock (gate) return work; } }
        internal long CachedBytes { get { lock (gate) return bytes; } }
        private void Remove(GifFrame frame) { Entry entry; if (entries.TryGetValue(frame, out entry)) { entries.Remove(frame); bytes -= entry.Bytes; entry.Image.Dispose(); } }
        internal void Prune(ICollection<GifFrame> frames)
        { lock (gate) { if (pending != null) pending.Cancel(); foreach (var frame in entries.Keys.Where(frame => !frames.Contains(frame)).ToList()) Remove(frame); } }
        public void Dispose()
        { lock (gate) { if (disposed) return; disposed = true; if (pending != null) pending.Cancel(); foreach (var frame in entries.Keys.ToList()) Remove(frame); } }
    }
}
