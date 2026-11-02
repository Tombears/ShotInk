using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Threading;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class GifEditorForm : Form
    {
        private readonly GifClip clip;
        private readonly AppSettings settings;
        private readonly PictureBox preview;
        private readonly GifRangeSlider range;
        private readonly Label position, status;
        private readonly Button play, copy, save;
        private readonly System.Windows.Forms.Timer playback;
        private readonly CancellationTokenSource generation = new CancellationTokenSource();
        private int frameIndex;
        private byte[] encoded;
        private bool busy;
        private bool disposed;
        private int cachedFirst = -1, cachedLast = -1;
        internal GifEditorForm(GifClip clip, AppSettings settings)
        {
            if (clip.Frames.Count == 0) throw new ArgumentException("没有录制帧。");
            this.clip = clip; this.settings = settings;
            Text = "GIF 剪辑 · ShotInk"; MainForm.UseAppIcon(this);
            Font = ToolAppearance.Font; BackColor = Color.White; Size = new Size(900, 650); MinimumSize = new Size(680, 500);
            StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 6 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 54)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            foreach (int height in new[] { 28, 64, 24, 38, 22 }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
            preview = new GifPreviewBox { Name = "gif-preview", Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(28, 31, 38) };
            preview.MouseDown += delegate { preview.Focus(); Pause(); };
            layout.Controls.Add(preview, 0, 0); layout.SetColumnSpan(preview, 2);
            position = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
            layout.Controls.Add(position, 0, 1); layout.SetColumnSpan(position, 2);
            range = new GifRangeSlider(clip.Frames.Count - 1) { Name = "gif-range", Dock = DockStyle.Fill };
            layout.Controls.Add(range, 0, 2); layout.SetColumnSpan(range, 2);
            range.RangeChanged += TrimChanged;
            var hint = new Label { Text = "预览区 ← / → 逐帧查看；点击起点或终点手柄后，← / → 每次调整一帧。", ForeColor = Color.DimGray, Dock = DockStyle.Fill };
            layout.Controls.Add(hint, 0, 3); layout.SetColumnSpan(hint, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            play = new Button { Name = "gif-play", Text = "播放选段", Width = 104, Height = 32 };
            copy = new Button { Name = "gif-copy", Text = "复制 GIF", Width = 104, Height = 32 };
            save = new Button { Name = "gif-save", Text = "保存 GIF", Width = 104, Height = 32 };
            play.Click += delegate { if (playback.Enabled) Pause(); else { ShowFrame(range.First); play.Text = "暂停"; playback.Interval = FrameDelay(frameIndex); playback.Start(); } };
            copy.Click += async delegate { await ExportAsync(true); }; save.Click += async delegate { await ExportAsync(false); };
            buttons.Controls.Add(play); buttons.Controls.Add(copy); buttons.Controls.Add(save);
            layout.Controls.Add(buttons, 0, 4); layout.SetColumnSpan(buttons, 2);
            status = new Label { Dock = DockStyle.Fill, ForeColor = Color.DimGray, Visible = false };
            layout.Controls.Add(status, 0, 5); layout.SetColumnSpan(status, 2); Controls.Add(layout);
            SetStatus(clip.LimitReached ? "已达到录制上限，已保留内容。" : "");
            playback = new System.Windows.Forms.Timer { Interval = 100 };
            playback.Tick += delegate { ShowFrame(frameIndex >= range.Last ? range.First : frameIndex + 1); playback.Interval = FrameDelay(frameIndex); };
            ShowFrame(0);
        }
        private int FrameDelay(int index) { return Math.Max(20, clip.EndTime(index) - clip.Frames[index].Time); }
        private void Pause() { playback.Stop(); play.Text = "播放选段"; }
        private void TrimChanged(int index) { Pause(); encoded = null; ShowFrame(index); }
        private void ShowFrame(int index)
        {
            var image = clip.Frames[index].Decode(); var previous = preview.Image; preview.Image = image; if (previous != null) previous.Dispose(); frameIndex = index;
            position.Text = string.Format("第 {0} / {1} 帧 · 当前 {2:0.00}s · 选段 {3:0.00}s — {4:0.00}s · 共 {5} 帧", index + 1, clip.Frames.Count,
                clip.Frames[index].Time / 1000.0, clip.Frames[range.First].Time / 1000.0, clip.EndTime(range.Last) / 1000.0, range.Last - range.First + 1);
        }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.C)) { CopyFromKey(); return true; }
            if (keyData == (Keys.Control | Keys.S)) { SaveFromKey(); return true; }
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == Keys.Left || keyData == Keys.Right)
            {
                if (busy) return true;
                Pause(); int delta = keyData == Keys.Left ? -1 : 1;
                if (range.Focused) range.AdjustBoundary(delta);
                else ShowFrame(Math.Max(range.First, Math.Min(range.Last, frameIndex + delta)));
                return true;
            }
            if (range.Focused && (keyData == Keys.Home || keyData == Keys.End)) { range.SelectBoundary(keyData == Keys.Home); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        private async void CopyFromKey() { await ExportAsync(true); }
        private async void SaveFromKey() { await ExportAsync(false); }
        private void SetStatus(string message)
        {
            status.Text = message; status.Visible = message.Length > 0;
            ((TableLayoutPanel)status.Parent).RowStyles[5].Height = message.Length > 0 ? 22 : 0;
        }
        private async Task ExportAsync(bool copying)
        {
            if (busy) return;
            Pause(); busy = true; range.Enabled = play.Enabled = copy.Enabled = save.Enabled = false;
            int first = range.First, last = range.Last; SetStatus("正在生成 GIF…");
            try
            {
                if (encoded == null || cachedFirst != first || cachedLast != last)
                {
                    var token = generation.Token;
                    var bytes = await Task.Run(delegate { return GifEncoder.Encode(clip, first, last, token); });
                    if (IsDisposed) return; encoded = bytes; cachedFirst = first; cachedLast = last;
                }
                if (copying)
                {
                    string path = GifExport.WriteNew(encoded, GifExport.CacheDirectory);
                    Clipboard.SetDataObject(GifExport.ClipboardData(encoded, path), true, 5, 100);
                    SetStatus("GIF 已复制，可粘贴到支持动图文件的应用。");
                }
                else SetStatus(GifExport.Save(this, encoded, settings) ? "GIF 已保存。" : "已取消保存。");
            }
            catch (Exception error) { if (!IsDisposed) SetStatus("未完成：" + error.Message); }
            finally { if (!IsDisposed) { busy = false; range.Enabled = play.Enabled = copy.Enabled = save.Enabled = true; } }
        }
        protected override void Dispose(bool disposing)
        { if (disposing && !disposed) { disposed = true; generation.Cancel(); generation.Dispose(); if (playback != null) playback.Dispose(); if (preview != null && preview.Image != null) { preview.Image.Dispose(); preview.Image = null; } } base.Dispose(disposing); }
        private sealed class GifPreviewBox : PictureBox
        { internal GifPreviewBox() { SetStyle(ControlStyles.Selectable, true); TabStop = true; } }
    }
}
