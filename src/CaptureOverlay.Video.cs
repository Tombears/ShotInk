using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class CaptureOverlay
    {
        private Panel recordingOptions, recordingNavigation;
        private bool videoMode, videoStarting, recordingGuideRunning;
        private bool recordingOptionsPlaced;
        private int recordingCountdownValue;
        private ToolOptionButton recordMicrophone, recordSpeaker, recordPointer;
        private readonly ToolTip recordingTip = new ToolTip { IsBalloon = true, ToolTipTitle = "录屏设置" };
        private async void StartVideoFromKey() { await CaptureVideoAsync(); }
        private void PrepareVideoCapture()
        {
            if (gifSession != null || scrollSession != null || longViewport != null || selectionOutline != null) return;
            videoMode = true; toolbar.SelectTool(DrawTool.None); toolbar.Hide();
            if (recordingOptions == null)
            {
                recordingOptions = new RecordingSurface { Name = "recording-options", Size = new Size(276, 44) };
                AddRecordingDragHandle(recordingOptions, delegate { recordingOptionsPlaced = true; });
                recordMicrophone = RecordingSwitch("microphone", "麦克风", 30, settings.RecordMicrophone);
                recordSpeaker = RecordingSwitch("speaker", "系统声音", 68, settings.RecordSpeaker);
                recordPointer = RecordingSwitch("pointer", "鼠标指针", 106, settings.RecordPointer);
                var start = new ToolOptionButton { Text = "开始录屏", Location = new Point(148, 6), Size = new Size(122, 32),
                    AccentColor = ToolAppearance.Accent("video"), Selected = true };
                start.Click += async delegate { await CaptureVideoAsync(); };
                recordingOptions.Controls.Add(start); Controls.Add(recordingOptions);
            }
            ClearSelection(); freeSelectionMode = false; modePicker.Hide(); recordingOptions.Hide(); Invalidate(true);
        }
        private ToolOptionButton RecordingSwitch(string icon, string label, int left, bool value)
        {
            var toggle = new ToolOptionButton {
                Name = "record-" + icon, IconName = icon, Selected = value,
                Location = new Point(left, 6), Size = new Size(32, 32),
                AccentColor = value ? ToolAppearance.Accent("copy") : ToolAppearance.Muted,
                AccessibleName = label, AccessibleRole = AccessibleRole.CheckButton,
                AccessibleDescription = value ? "已开启" : "已关闭"
            };
            toggle.Click += delegate {
                toggle.Selected = !toggle.Selected;
                toggle.AccentColor = toggle.Selected ? ToolAppearance.Accent("copy") : ToolAppearance.Muted;
                toggle.AccessibleDescription = toggle.Selected ? "已开启" : "已关闭";
                toggle.Invalidate();
            };
            recordingTip.SetToolTip(toggle, label + "开关，蓝色表示开启");
            recordingOptions.Controls.Add(toggle);
            return toggle;
        }
        private async void ShowRecordingGuide()
        {
            recordingGuideRunning = true;
            var controls = new Control[] { recordMicrophone, recordSpeaker, recordPointer };
            var messages = new[] { "1 / 3  麦克风开关，开启后录入你的声音。", "2 / 3  扬声器开关，开启后录入电脑播放的声音。", "3 / 3  鼠标指针开关，开启后在视频中显示指针。" };
            for (int index = 0; index < controls.Length; index++)
            {
                if (IsDisposed || videoStarting || !videoMode) { recordingGuideRunning = false; return; }
                recordingTip.Show(messages[index], controls[index], 0, -54, 2800); await Task.Delay(2900);
            }
            if (IsDisposed) return;
            settings.RecordingGuideSeen = true; recordingGuideRunning = false; settings.NotifyChanged();
        }
        private void PositionVideoOptions()
        {
            if (!videoMode || recordingOptions == null || videoStarting || !ready) return;
            recordingOptions.Show();
            if (!settings.RecordingGuideSeen && !recordingGuideRunning) ShowRecordingGuide();
            recordingOptions.Location = recordingOptionsPlaced
                ? CaptureToolbar.Clamp(recordingOptions.Location, ClientRectangle, recordingOptions.Size)
                : CaptureToolbar.Place(selection, ClientRectangle, recordingOptions.Size);
            recordingOptions.BringToFront();
        }
        private void AddRecordingDragHandle(Panel panel, Action moved)
        {
            var image = ToolbarIcons.Create("grip");
            var grip = new Label { Name = "recording-drag", Image = image,
                BackColor = Color.White, Cursor = Cursors.SizeAll, Location = new Point(5, 9), Size = new Size(20, 26), AccessibleName = "拖动录屏功能条" };
            Point anchor = Point.Empty;
            grip.MouseDown += delegate(object sender, MouseEventArgs args) {
                if (args.Button != MouseButtons.Left) return;
                anchor = MousePosition; grip.Capture = true;
            };
            grip.MouseMove += delegate {
                if (!grip.Capture) return;
                var pointer = MousePosition;
                panel.Location = CaptureToolbar.Clamp(new Point(panel.Left + pointer.X - anchor.X, panel.Top + pointer.Y - anchor.Y), ClientRectangle, panel.Size);
                anchor = pointer; moved(); panel.Refresh();
            };
            grip.MouseUp += delegate { grip.Capture = false; };
            grip.Disposed += delegate { image.Dispose(); };
            panel.Controls.Add(grip);
        }
        private void PaintRecordingCountdown(Graphics graphics)
        {
            if (recordingCountdownValue == 0) return;
            var at = CaptureToolbar.Clamp(new Point(selection.Left + (selection.Width - 160) / 2,
                selection.Top + (selection.Height - 160) / 2), ClientRectangle, new Size(160, 160));
            using (var image = ToolAppearance.RenderSmooth(new Size(160, 160), delegate(Graphics circle)
            {
                using (var fill = new SolidBrush(Color.FromArgb(102, 18, 20, 25))) circle.FillEllipse(fill, 1, 1, 158, 158);
                using (var font = new Font(Font.FontFamily, 64, FontStyle.Bold))
                using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    circle.DrawString(recordingCountdownValue.ToString(), font, Brushes.White, new RectangleF(0, 0, 160, 160), format);
            })) graphics.DrawImageUnscaled(image, at);
        }
        private async Task CaptureVideoAsync()
        {
            if (videoStarting || gifSession != null || !ready) return;
            EnsureCaptureUiContext(); videoStarting = true; recordingTip.Hide(recordMicrophone); recordingTip.Hide(recordSpeaker); recordingTip.Hide(recordPointer);
            settings.RecordMicrophone = recordMicrophone.Selected; settings.RecordSpeaker = recordSpeaker.Selected; settings.RecordPointer = recordPointer.Selected; settings.NotifyChanged();
            var region = new Rectangle(selection.X + screenBounds.X, selection.Y + screenBounds.Y, selection.Width, selection.Height);
            var media = new RecordingMedia(); bool handedToEditor = false;
            var session = new GifCaptureSession(delegate { var frame = CaptureGifFrame(region); if (settings.RecordPointer) RecordingCursor.Draw(frame, region); return frame; }, settings.GifFramesPerSecond);
            session.FrameDirectory = media.DirectoryPath; gifSession = session;
            var timer = new Timer { Interval = 200 };
            try
            {
                if (!System.IO.File.Exists(VideoExport.EncoderPath)) throw new System.IO.FileNotFoundException("录屏编码器缺失，请重新解压完整便携包。");
                recordingOptions.Hide(); canvas.Hide(); sizeHint.Hide(); foreach (var grip in grips) grip.Hide(); foreach (var edge in edgeTargets) edge.Hide();
                for (int seconds = 3; seconds > 0; seconds--)
                {
                    recordingCountdownValue = seconds; Invalidate(); Update();
                    await Task.Delay(1000); if (IsDisposed) return;
                }
                recordingCountdownValue = 0; Invalidate(); Update();
                recordingNavigation = new RecordingSurface { Name = "recording-navigation", Size = new Size(314, 46) };
                AddRecordingDragHandle(recordingNavigation, UpdateRecordingRegion);
                var pause = new ToolOptionButton { IconName = "pause", AccessibleName = "暂停录屏",
                    AccentColor = ToolAppearance.Accent("copy"), Location = new Point(28, 7), Size = new Size(32, 32) };
                var time = new Label { Text = "00:00:00 / 00:01:00", ForeColor = ToolAppearance.Accent("copy"), BackColor = Color.White, Location = new Point(68, 15), AutoSize = true };
                var stop = new ToolOptionButton { Text = "结束录制", Location = new Point(218, 7), Size = new Size(88, 32),
                    AccentColor = ToolAppearance.Accent("video"), Selected = true };
                bool paused = false;
                pause.Click += delegate { paused = !paused; session.SetPaused(paused); pause.IconName = paused ? "play" : "pause"; pause.Invalidate(); pause.AccessibleName = paused ? "继续录屏" : "暂停录屏"; };
                stop.Click += delegate { pause.Enabled = stop.Enabled = false; session.Finish(); };
                recordingNavigation.Controls.AddRange(new Control[] { pause, time, stop }); Controls.Add(recordingNavigation);
                recordingNavigation.Location = CaptureToolbar.Place(selection, ClientRectangle, recordingNavigation.Size); recordingNavigation.BringToFront();
                captureChromeExcluded = Native.ExcludeFromCapture(Handle, true);
                if (!captureChromeExcluded && recordingNavigation.Bounds.IntersectsWith(selection)) throw new InvalidOperationException("请缩小选区，为录制导航留出屏幕空间。");
                UpdateRecordingRegion(); Capture = false; Native.ReleaseCapture();
                scrollEscapeRegistered = Native.RegisterHotKey(Handle, 1001, 0x4000, (uint)Keys.Escape);
                Native.FlushDesktop();
                media.StartAudio(settings.RecordMicrophone, settings.RecordSpeaker);
                timer.Tick += delegate { time.Text = TimeSpan.FromMilliseconds(session.Elapsed).ToString(@"hh\:mm\:ss") + " / 00:01:00"; }; timer.Start();
                var clip = await session.RunAsync(); timer.Stop(); await media.StopAudio();
                if (IsDisposed) return;
                clip.Recording = media; gifSession = null;
                var editor = new GifEditorForm(clip, settings); handedToEditor = true;
                ResetCaptureRegion(); Hide(); Close(); editor.Show(); editor.Activate();
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!IsDisposed) MessageBox.Show(this, error.Message, "录屏未完成"); }
            finally
            {
                timer.Dispose(); session.Dispose(); if (!handedToEditor) media.Dispose();
                recordingCountdownValue = 0;
                if (recordingNavigation != null) { recordingNavigation.Dispose(); recordingNavigation = null; }
                gifSession = null; videoStarting = false;
                if (!IsDisposed) { ResetCaptureRegion(); canvas.Show(); recordingOptions.Show(); UpdateSelectionChrome(); PositionVideoOptions(); }
            }
        }
    }
}
