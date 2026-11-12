using System;
using System.Drawing;
using System.Threading.Tasks;

namespace ShotInk
{
    internal sealed partial class GifEditorForm
    {
        private int dragPreviewRevision;
        private int dragPreviewSession;
        private bool dragPreviewRunning;
        private async void RequestDragPreview()
        {
            dragPreviewRevision++;
            if (dragPreviewRunning) return;
            EnsureEditorUiContext(); dragPreviewRunning = true;
            var token = generation.Token;
            try
            {
                while (!disposed && range.IsDragging)
                {
                    int revision = dragPreviewRevision, session = dragPreviewSession;
                    var frame = clip.Frames[frameIndex]; var annotations = frame.Annotations;
                    var image = await Task.Run(delegate
                    {
                        GifRenderWorkers.Gate.Wait(token);
                        try
                        {
                            token.ThrowIfCancellationRequested();
                            var cached = frame.Cache == null ? null : frame.Cache.Read(frame, annotations);
                            return cached ?? frame.Render(annotations);
                        }
                        finally { GifRenderWorkers.Gate.Release(); }
                    });
                    if (disposed || !range.IsDragging || session != dragPreviewSession)
                    { image.Dispose(); continue; }
                    var previous = preview.Image; preview.Image = image; if (previous != null) previous.Dispose();
                    if (revision == dragPreviewRevision) break;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (!disposed) SetStatus("预览未完成：" + error.Message); }
            finally { dragPreviewRunning = false; }
        }
    }
}
