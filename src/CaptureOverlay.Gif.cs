using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class CaptureOverlay
    {
        private GifCaptureSession gifSession;
        private bool captureChromeExcluded;
        internal Func<Rectangle, Bitmap> CaptureGifFrame = Native.Capture;
        private async void StartGifCapture() { await CaptureGifAsync(); }
        internal async Task CaptureGifAsync()
        {
            EnsureCaptureUiContext();
            if (!ready || gifSession != null || scrollSession != null || longViewport != null) return;
            var region = new Rectangle(selection.X + screenBounds.X, selection.Y + screenBounds.Y, selection.Width, selection.Height);
            var session = new GifCaptureSession(delegate { return CaptureGifFrame(region); }); gifSession = session;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var timer = new Timer { Interval = 250 };
            try
            {
                toolbar.SelectTool(DrawTool.None); toolbar.SetScrollCapture(true); toolbar.SetRecordingText("停止录制");
                canvas.Hide(); sizeHint.Hide(); foreach (var grip in grips) grip.Hide(); foreach (var edge in edgeTargets) edge.Hide(); PositionToolbar();
                captureChromeExcluded = Native.ExcludeFromCapture(Handle, true);
                if (toolbar.Bounds.IntersectsWith(selection) && !captureChromeExcluded) throw new InvalidOperationException("请缩小选区，为停止按钮留出屏幕空间。");
                UpdateRecordingRegion(); Capture = false; Native.ReleaseCapture();
                scrollEscapeRegistered = Native.RegisterHotKey(Handle, 1001, 0x4000, (uint)Keys.Escape);
                Native.FocusAt(new Point(region.Left + region.Width / 2, region.Top + region.Height / 2)); Native.RaiseWithoutActivation(Handle);
                await Task.Delay(80);
                if (IsDisposed) return;
                EnsureCaptureUiContext();
                Native.FlushDesktop();
                clock.Restart(); timer.Tick += delegate { if (!IsDisposed && !session.IsFinishing && object.ReferenceEquals(gifSession, session)) toolbar.SetRecordingText("停止 · " + clock.Elapsed.TotalSeconds.ToString("0.0") + "s"); }; timer.Start();
                var clip = await session.RunAsync();
                timer.Stop();
                if (IsDisposed) return;
                gifSession = null; ResetCaptureRegion(); Hide(); Close();
                var editor = new GifEditorForm(clip, settings); editor.Show(); editor.Activate();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!IsDisposed) MessageBox.Show(this, error.Message, "GIF 录制未完成"); }
            finally
            {
                timer.Dispose(); session.Dispose();
                if (object.ReferenceEquals(gifSession, session)) gifSession = null;
                if (!IsDisposed)
                { ResetCaptureRegion(); canvas.Show(); toolbar.SetScrollCapture(false); UpdateSelectionChrome(); PositionToolbar(); }
            }
        }
        private void UpdateRecordingRegion()
        {
            if (IsDisposed || (gifSession == null && scrollSession == null)) return;
            if (!captureChromeExcluded && toolbar.Bounds.IntersectsWith(selection))
                toolbar.Location = CaptureToolbar.Place(selection, ClientRectangle, toolbar.Size);
            var surface = new Region(ClientRectangle); var hole = selection; hole.Inflate(5, 5);
            surface.Exclude(hole); surface.Union(toolbar.Bounds);
            var previous = Region; Region = surface; if (previous != null) previous.Dispose();
        }
    }
}
