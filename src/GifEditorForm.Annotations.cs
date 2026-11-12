using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed partial class GifEditorForm
    {
        private TableLayoutPanel layout;
        private Panel viewport;
        private CaptureToolbar toolbar;
        private GifScopeSelector scope;
        private AnnotationCanvas frameCanvas;
        private GifFrame boundFrame;
#if !SHOTINK_LITE
        private TextRecognitionSession recognition;
#endif
        private DrawTool activeTool = DrawTool.None;
        private GifEditScope editScope;
        private readonly GifFrameCache frameCache = new GifFrameCache();
        private readonly Stack<FrameEdit> annotationUndo = new Stack<FrameEdit>(), annotationRedo = new Stack<FrameEdit>();
        private sealed class FrameEdit
        {
            internal readonly Dictionary<GifFrame, List<Mark>> Before = new Dictionary<GifFrame, List<Mark>>();
            internal readonly Dictionary<GifFrame, List<Mark>> After = new Dictionary<GifFrame, List<Mark>>();
            internal TimelineState BeforeTimeline, AfterTimeline;
        }
        private sealed class TimelineState
        {
            internal GifFrame[] Frames;
            internal int[] Times;
            internal int Duration, First, Last, FrameIndex, OutputCount;
        }
        private TimelineState CaptureTimeline()
        {
            return new TimelineState { Frames = clip.Frames.ToArray(), Times = clip.Frames.Select(frame => frame.Time).ToArray(), Duration = clip.Duration,
                First = range.First, Last = range.Last, FrameIndex = frameIndex, OutputCount = frameCount.Value };
        }
        private void RestoreTimeline(TimelineState state)
        {
            clip.Frames.Clear(); clip.Frames.AddRange(state.Frames); clip.Duration = state.Duration;
            for (int index = 0; index < state.Frames.Length; index++) state.Frames[index].Time = state.Times[index];
            range.Reset(Math.Max(0, clip.Frames.Count - 1)); range.SetRange(state.First, state.Last);
            SetFrameCount(state.OutputCount);
            frameCache.Prune(clip.Frames); EnableEditing(true); ShowFrame(state.FrameIndex); UpdateFrameCountLabel();
        }
        private void SetupAnnotationTools()
        {
            foreach (var frame in clip.Frames) frame.Cache = frameCache;
            var row = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            toolbar = new CaptureToolbar(settings) { Name = "gif-tools", Location = Point.Empty };
            toolbar.ConfigureForGif();
            scope = new GifScopeSelector { Name = "gif-tool-scope", Anchor = AnchorStyles.Top | AnchorStyles.Right };
            row.Controls.Add(toolbar); row.Controls.Add(scope);
            Action positionTools = delegate { scope.Location = new Point(Math.Max(toolbar.Right + 8, row.ClientSize.Width - scope.Width), 0); };
            row.Resize += delegate { positionTools(); };
            toolbar.LayoutChanged += delegate { layout.RowStyles[1].Height = toolbar.Height + 4; positionTools(); };
            scope.ValueChanged += delegate { CommitFrameCanvas(); editScope = scope.Value; };
            toolbar.ToolChanged += delegate(DrawTool tool)
            {
                if (busy || clip.Frames.Count == 0) return;
                Pause(); CommitFrameCanvas(); activeTool = tool;
                if (tool == DrawTool.None) ShowFrame(frameIndex);
                else { EnsureFrameCanvas(); frameCanvas.Tool = tool; toolbar.ApplyTo(frameCanvas); frameCanvas.Focus(); }
            };
            toolbar.SettingsChanged += delegate { if (frameCanvas != null) toolbar.ApplyTo(frameCanvas); };
            toolbar.UndoClicked += delegate { ChangeAnnotationHistory(false); };
            toolbar.RedoClicked += delegate { ChangeAnnotationHistory(true); };
#if !SHOTINK_LITE
            toolbar.RecognitionClicked += async delegate
            {
                if (busy || clip.Frames.Count == 0) return; EnsureEditorUiContext(); Pause(); CommitFrameCanvas(); EnsureFrameCanvas(); await recognition.RecognizeAsync();
            };
            toolbar.RecognitionHovered += delegate { RapidTextRecognizer.Shared.WarmUpAsync(); };
            toolbar.CopySelectedText += delegate { if (frameCanvas != null) frameCanvas.CopyRecognizedText(false); };
            toolbar.CopyAllText += delegate { if (frameCanvas != null) frameCanvas.CopyRecognizedText(true); };
#endif
            layout.Controls.Add(row, 0, 1); layout.SetColumnSpan(row, 2); positionTools();
        }
        private void EnsureFrameCanvas()
        {
            if (frameCanvas != null || clip.Frames.Count == 0) return;
            boundFrame = clip.Frames[frameIndex];
            var canvas = new AnnotationCanvas(boundFrame.DecodeOriginal()) { Name = "gif-frame-canvas", BackColor = viewport.BackColor };
            canvas.Marks.AddRange(boundFrame.Annotations.Select(mark => mark.Clone())); canvas.Tool = activeTool;
            frameCanvas = canvas; toolbar.ApplyTo(canvas); canvas.Changed += FrameAnnotationChanged;
#if !SHOTINK_LITE
            canvas.TextSelectionChanged += delegate
            {
                if (frameCanvas != canvas) return;
                toolbar.SetTextSelection(canvas.HasTextRecognition, canvas.HasSelectedText);
                if (canvas.HasTextRecognition) SetStatus("文字识别仅处理当前帧，绘图范围不影响识别。");
            };
            recognition = new TextRecognitionSession(this, canvas.Render, delegate { return canvas.Revision; },
                delegate(bool running) { if (frameCanvas == canvas) toolbar.SetRecognitionBusy(running); }, gifRecognizer,
                delegate(OcrDocument document) { if (frameCanvas == canvas) { canvas.SetTextRecognition(document); canvas.Focus(); } }, SetStatus);
#endif
            viewport.Controls.Add(canvas); preview.Hide(); FitFrameCanvas(); canvas.BringToFront(); canvas.Focus();
        }
        private void EnsureEditorUiContext()
        {
            if (InvokeRequired) throw new InvalidOperationException("请在 GIF 编辑窗口线程执行此操作。");
            var context = System.Threading.SynchronizationContext.Current;
            if (context == null || context.GetType() == typeof(System.Threading.SynchronizationContext))
                System.Threading.SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        }
        private void FitFrameCanvas()
        {
            if (frameCanvas == null) return;
            float zoom = Math.Min(3, Math.Min((viewport.ClientSize.Width - 8F) / frameCanvas.Image.Width, (viewport.ClientSize.Height - 8F) / frameCanvas.Image.Height));
            frameCanvas.SetZoom(Math.Max(.05F, zoom));
            frameCanvas.Location = new Point(Math.Max(0, (viewport.ClientSize.Width - frameCanvas.Width) / 2), Math.Max(0, (viewport.ClientSize.Height - frameCanvas.Height) / 2));
        }
        private void CommitFrameCanvas() { if (frameCanvas != null) frameCanvas.CommitFrameEdit(); }
        private void ReleaseFrameCanvas(bool commit)
        {
            if (frameCanvas == null) return;
            if (commit) CommitFrameCanvas();
#if !SHOTINK_LITE
            if (recognition != null) { recognition.Dispose(); recognition = null; }
#endif
            var canvas = frameCanvas; frameCanvas = null; boundFrame = null;
            canvas.Changed -= FrameAnnotationChanged; viewport.Controls.Remove(canvas); canvas.Dispose();
            toolbar.SetRecognitionBusy(false); toolbar.SetTextSelection(false, false);
            scope.Enabled = !busy && clip.Frames.Count > 0;
        }
        private static bool SameMark(Mark a, Mark b)
        {
            return a.Id == b.Id && a.Tool == b.Tool && a.Color == b.Color && a.Width == b.Width && a.Text == b.Text && a.Freehand == b.Freehand &&
                a.MosaicSize == b.MosaicSize && a.MosaicEffect == b.MosaicEffect && a.BlurRadius == b.BlurRadius && a.BrushSize == b.BrushSize &&
                a.FontSize == b.FontSize && a.TextStyle == b.TextStyle && a.TextSize == b.TextSize && a.TextAngle == b.TextAngle && a.Points.SequenceEqual(b.Points);
        }
        private void FrameAnnotationChanged()
        {
            if (frameCanvas == null || boundFrame == null || busy) return;
            var before = boundFrame.Annotations; var after = frameCanvas.Marks.Select(mark => mark.Clone()).ToList();
            var removed = before.Where(mark => !after.Any(next => next.Id == mark.Id)).Select(mark => mark.Id).ToList();
            var changed = after.Where(mark => !before.Any(previous => SameMark(previous, mark))).ToList();
            if (removed.Count == 0 && changed.Count == 0) return;
            int first = editScope == GifEditScope.Current ? frameIndex : editScope == GifEditScope.Selected ? range.First : 0;
            int last = editScope == GifEditScope.Current ? frameIndex : editScope == GifEditScope.Selected ? range.Last : clip.Frames.Count - 1;
            var edit = new FrameEdit();
            for (int index = first; index <= last; index++)
            {
                var frame = clip.Frames[index]; var next = frame == boundFrame ? after : frame.Annotations.Where(mark => !removed.Contains(mark.Id)).ToList();
                if (frame != boundFrame) foreach (var mark in changed)
                { int at = next.FindIndex(previous => previous.Id == mark.Id); if (at < 0) next.Add(mark); else next[at] = mark; }
                edit.Before.Add(frame, frame.Annotations); edit.After.Add(frame, next); frame.Annotations = next;
            }
            annotationUndo.Push(edit); annotationRedo.Clear(); encoded = null; cachedFirst = cachedLast = -1; UpdateAnnotationHistory(); PreloadNearbyFrames();
        }
        private void PreloadNearbyFrames()
        { frameCache.Schedule(Enumerable.Range(0, clip.Frames.Count).OrderBy(index => Math.Abs(index - frameIndex)).Select(index => clip.Frames[index])); }
        private void UpdateAnnotationHistory()
        {
            if (toolbar == null) return;
            toolbar.Enabled = !busy && (clip.Frames.Count > 0 || annotationUndo.Count > 0 || annotationRedo.Count > 0);
            toolbar.UpdateHistory(annotationUndo.Count > 0, annotationRedo.Count > 0);
        }
        private void ChangeAnnotationHistory(bool redo)
        {
            if (busy) return; Pause(); CommitFrameCanvas();
            var source = redo ? annotationRedo : annotationUndo; if (source.Count == 0) return;
            var edit = source.Pop(); ReleaseFrameCanvas(false);
            if (edit.BeforeTimeline != null) RestoreTimeline(redo ? edit.AfterTimeline : edit.BeforeTimeline);
            else foreach (var pair in redo ? edit.After : edit.Before) pair.Key.Annotations = pair.Value;
            (redo ? annotationUndo : annotationRedo).Push(edit); encoded = null; cachedFirst = cachedLast = -1; ShowFrame(frameIndex); PreloadNearbyFrames();
        }
        private bool HandleAnnotationKey(ref Message message, Keys keyData)
        {
            if (scope.Focused && (keyData == Keys.Left || keyData == Keys.Right))
            { scope.SelectScope((GifEditScope)Math.Max(0, Math.Min(2, (int)scope.Value + (keyData == Keys.Left ? -1 : 1)))); return true; }
            if (toolbar.ContainsFocus && (keyData == Keys.Left || keyData == Keys.Right)) return false;
            if (frameCanvas != null)
            {
                var key = new KeyEventArgs(keyData);
                if (frameCanvas.HandleGestureKey(key) || frameCanvas.HandleTextKey(key) || frameCanvas.HandleRecognitionKey(key))
                    return key.SuppressKeyPress || frameCanvas.EditingInlineText;
            }
            if (keyData == (Keys.Control | Keys.Y)) { ChangeAnnotationHistory(true); return true; }
            return false;
        }
    }
}
