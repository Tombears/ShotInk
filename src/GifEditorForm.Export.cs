using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class GifEditorForm
    {
        private Button compression;
        private NumericUpDown targetSize;
        private System.Windows.Forms.Timer sizeRefresh;
        private CancellationTokenSource encodingCancel;
        private Task<GifEncodingResult> encodingTask;
        private int exportRevision;
        private int exportScale = 100;
        private int ExportScale { get { return exportScale; } }

        private void SetupGifCompression(FlowLayoutPanel row)
        {
            row.Controls.Add(new Label { Text = "目标大小", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            targetSize = new NumericUpDown { Name = "gif-target-mb", Width = 72, Minimum = .01M, Maximum = 500M, DecimalPlaces = 2, Increment = .1M, Value = settings.GifTargetMegabytes, Margin = Padding.Empty };
            row.Controls.Add(targetSize);
            row.Controls.Add(new Label { Text = "MB", AutoSize = true, Padding = new Padding(0, 4, 0, 0) });
            compression = new ToolOptionButton { Name = "gif-compression", Text = "开始压缩", Width = 84, Height = 23, Margin = Padding.Empty };
            compression.AccessibleDescription = "点击后按目标大小等比缩小像素尺寸，缩小会减少画面细节。";
            row.Controls.Add(compression);
            compression.Click += async delegate { await CompressGifAsync(); };
            targetSize.ValueChanged += delegate { settings.GifTargetMegabytes = targetSize.Value; settings.NotifyChanged(); };
            sizeRefresh = new System.Windows.Forms.Timer { Interval = 400 };
            sizeRefresh.Tick += async delegate
            {
                sizeRefresh.Stop();
                int revision = exportRevision;
                try { await GetGifBytesAsync(); }
                catch (OperationCanceledException) { }
                catch (Exception error) { if (!disposed && revision == exportRevision) { wechatPreview.Text = "大小：未能生成"; wechatPreview.Invalidate(); SetStatus(error.Message); } }
            };
        }

        private void InvalidateGifExport()
        {
            encoded = null; exportRevision++;
            if (sizeRefresh == null || disposed) return;
            sizeRefresh.Stop();
            if (encodingCancel != null) encodingCancel.Cancel();
            encodingCancel = null;
            encodingTask = null;
            if (wechatPreview.Text == "大小：未能生成") SetStatus("");
            wechatPreview.Text = clip.Frames.Count == 0 ? "大小：暂无帧" : "大小：计算中…";
            wechatPreview.Invalidate();
            if (clip.Frames.Count > 0) sizeRefresh.Start();
        }

        private async Task CompressGifAsync()
        {
            if (busy || clip.Frames.Count == 0) return;
            EnsureEditorUiContext(); Pause(); CommitFrameCanvas();
            busy = true; EnableEditing(false); InvalidateGifExport(); sizeRefresh.Stop();
            SetStatus("正在压缩 GIF…");
            try
            {
                await EncodeGifAsync((long)(targetSize.Value * 1024 * 1024));
                if (!disposed) SetStatus("压缩完成。");
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!disposed) { SetStatus("压缩未完成：" + error.Message); sizeRefresh.Start(); } }
            finally { if (!disposed) { busy = false; EnableEditing(true); } }
        }

        private Task<byte[]> GetGifBytesAsync() { return EncodeGifAsync(null); }
        private async Task<byte[]> EncodeGifAsync(long? targetBytes)
        {
            EnsureEditorUiContext();
            if (encoded != null) return encoded;
            if (clip.Frames.Count == 0) throw new OperationCanceledException();
            sizeRefresh.Stop();
            int revision = exportRevision;
            if (encodingTask == null)
            {
                // Capture immutable frame data on the UI thread before encoding in the background.
                var snapshot = new GifClip { Duration = clip.EndTime(range.Last) };
                snapshot.Frames.AddRange(clip.Frames.Skip(range.First).Take(range.Last - range.First + 1).Select(frame => new GifFrame
                {
                    Png = frame.Png, PngPath = frame.PngPath, Time = frame.Time,
                    Annotations = frame.Annotations.Select(mark => mark.Clone()).ToList()
                }));
                var cancellation = CancellationTokenSource.CreateLinkedTokenSource(generation.Token);
                encodingCancel = cancellation;
                int scale = exportScale;
                var token = cancellation.Token;
                encodingTask = Task.Run(delegate
                {
                    if (targetBytes.HasValue) return EncodeToSize(snapshot, targetBytes.Value, token);
                    return new GifEncodingResult { Bytes = GifEncoder.Encode(snapshot, 0, snapshot.Frames.Count - 1, token, scale), Scale = scale };
                });
            }
            var pending = encodingTask;
            var pendingCancellation = encodingCancel;
            try
            {
                var result = await pending;
                if (disposed || revision != exportRevision) throw new OperationCanceledException();
                var bytes = result.Bytes;
                encoded = bytes;
                exportScale = result.Scale;
                UpdateWechatPreview();
                double size = bytes.Length / 1024.0;
                string unit = "KB";
                if (size >= 1024) { size /= 1024; unit = "MB"; }
                wechatPreview.Text = "大小：" + size.ToString("0.00") + unit;
                wechatPreview.Invalidate();
                return bytes;
            }
            finally
            {
                if (ReferenceEquals(pending, encodingTask)) { encodingTask = null; encodingCancel = null; }
                pendingCancellation.Dispose();
            }
        }

        internal sealed class GifEncodingResult
        {
            internal byte[] Bytes;
            internal int Scale;
        }
        internal static GifEncodingResult EncodeToSize(GifClip snapshot, long limit, CancellationToken token)
        {
            var original = GifEncoder.Encode(snapshot, 0, snapshot.Frames.Count - 1, token);
            if (original.LongLength <= limit) return new GifEncodingResult { Bytes = original, Scale = 100 };
            GifEncodingResult best = null;
            int first = 1, last = 99;
            while (first <= last)
            {
                token.ThrowIfCancellationRequested();
                int scale = (first + last) / 2;
                var bytes = GifEncoder.Encode(snapshot, 0, snapshot.Frames.Count - 1, token, scale);
                if (bytes.LongLength <= limit) { best = new GifEncodingResult { Bytes = bytes, Scale = scale }; first = scale + 1; }
                else last = scale - 1;
            }
            if (best == null) throw new InvalidOperationException("目标大小过小，无法在保留全部帧和时长的情况下达到，请增大目标大小。");
            return best;
        }
    }
}
