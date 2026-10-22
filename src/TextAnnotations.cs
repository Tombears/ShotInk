using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class TextGeometry
    {
        internal static float FontSize(Mark mark) { return mark.FontSize > 0 ? mark.FontSize : Math.Max(14, mark.Width * 6); }
        internal static Font Font(Mark mark, float zoom)
        { return new Font("Microsoft YaHei UI", Math.Max(1, FontSize(mark) * zoom), mark.TextStyle, GraphicsUnit.Pixel); }
        internal static SizeF Measure(Mark mark)
        {
            using (var bitmap = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bitmap))
            using (var font = Font(mark, 1))
            {
                var size = g.MeasureString(string.IsNullOrEmpty(mark.Text) ? "文" : mark.Text + " ", font, int.MaxValue, StringFormat.GenericTypographic);
                return new SizeF(Math.Max(FontSize(mark) * 2, size.Width + 8), Math.Max(FontSize(mark) * 1.5F, size.Height + 6));
            }
        }
        internal static SizeF Size(Mark mark) { return mark.TextSize.IsEmpty ? Measure(mark) : mark.TextSize; }
        internal static PointF Center(Mark mark)
        { var size = Size(mark); return new PointF(mark.Points[0].X + size.Width / 2, mark.Points[0].Y + size.Height / 2); }
        internal static PointF Rotate(PointF point, float angle)
        {
            double radians = angle * Math.PI / 180;
            return new PointF((float)(point.X * Math.Cos(radians) - point.Y * Math.Sin(radians)), (float)(point.X * Math.Sin(radians) + point.Y * Math.Cos(radians)));
        }
        internal static PointF World(Mark mark, PointF local)
        {
            var size = Size(mark); var center = Center(mark);
            var delta = Rotate(new PointF(local.X - size.Width / 2, local.Y - size.Height / 2), mark.TextAngle);
            return new PointF(center.X + delta.X, center.Y + delta.Y);
        }
        internal static PointF Local(Mark mark, PointF point)
        {
            var center = Center(mark); var size = Size(mark);
            var delta = Rotate(new PointF(point.X - center.X, point.Y - center.Y), -mark.TextAngle);
            return new PointF(delta.X + size.Width / 2, delta.Y + size.Height / 2);
        }
        internal static PointF[] Corners(Mark mark)
        {
            var size = Size(mark);
            return new[] { World(mark, PointF.Empty), World(mark, new PointF(size.Width, 0)),
                World(mark, new PointF(size.Width, size.Height)), World(mark, new PointF(0, size.Height)) };
        }
        internal static bool Contains(Mark mark, PointF point, float margin)
        {
            var local = Local(mark, point); var size = Size(mark);
            return local.X >= -margin && local.Y >= -margin && local.X <= size.Width + margin && local.Y <= size.Height + margin;
        }
        internal static void Draw(Graphics g, Mark mark)
        {
            var state = g.Save(); var center = Center(mark); var size = Size(mark);
            try
            {
                g.TranslateTransform(center.X, center.Y); g.RotateTransform(mark.TextAngle);
                using (var font = Font(mark, 1))
                using (var brush = new SolidBrush(mark.Color))
                    g.DrawString(mark.Text ?? "", font, brush, new PointF(-size.Width / 2 + 3, -size.Height / 2 + 2), StringFormat.GenericTypographic);
            }
            finally { g.Restore(state); }
        }
    }

    internal sealed partial class AnnotationCanvas
    {
        internal float TextFontSize = 24;
        internal FontStyle TextFontStyle = FontStyle.Regular;
        private Mark selectedText;
        private TextBox textEditor;
        private Font textEditorFont;
        private List<Mark> beforeTextEdit;
        private Mark editOriginal;
        private bool newText;
        private List<Mark> beforeTextTransform;
        private Mark transformOriginal;
        private PointF transformAnchor;
        private int textHandle = -2; // -2: idle, -1: move, 0..3: scale, 4: rotate.
        private bool textTransformed;
        internal event Action<Mark> TextAnnotationSelected;
        private void NotifyTextAnnotationSelected()
        { if (selectedText != null && TextAnnotationSelected != null) TextAnnotationSelected(selectedText); }

        internal void ApplyTextSettings(float size, FontStyle style, Color color)
        {
            TextFontSize = size; TextFontStyle = style;
            if (Tool != DrawTool.Text || selectedText == null) return;
            if (selectedText.FontSize == size && selectedText.TextStyle == style && selectedText.Color == color) return;
            if (textEditor == null) SaveUndo();
            bool measure = selectedText.FontSize != size || selectedText.TextStyle != style;
            selectedText.FontSize = size; selectedText.TextStyle = style; selectedText.Color = color;
            if (measure) selectedText.TextSize = TextGeometry.Measure(selectedText);
            UpdateTextEditor(); NotifyChanged();
        }

        private PointF RotationHandle(Mark mark)
        {
            var point = TextGeometry.World(mark, new PointF(TextGeometry.Size(mark).Width / 2, -24 / Zoom));
            return new PointF(Math.Max(7 / Zoom, Math.Min(Image.Width - 7 / Zoom, point.X)), Math.Max(7 / Zoom, Math.Min(Image.Height - 7 / Zoom, point.Y)));
        }
        private int HitTextHandle(PointF point)
        {
            if (selectedText == null) return -2;
            var corners = TextGeometry.Corners(selectedText);
            var rotation = RotationHandle(selectedText);
            if (Distance(point, rotation) <= 8 / Zoom) return 4;
            for (int i = 0; i < corners.Length; i++) if (Distance(point, corners[i]) <= 8 / Zoom) return i;
            return TextGeometry.Contains(selectedText, point, 4 / Zoom) ? -1 : -2;
        }
        private static double Distance(PointF a, PointF b)
        { return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }
        private void TextMouseDown(MouseEventArgs e)
        {
            Focus(); var point = ImagePoint(e.Location); int hit = HitTextHandle(point);
            if (hit != -2)
            {
                if (e.Clicks > 1 && hit == -1) { BeginTextEdit(selectedText, false); return; }
                CommitTextEdit();
                if (selectedText == null) return;
                transformOriginal = selectedText.Clone(); beforeTextTransform = Snapshot();
                transformAnchor = point; textHandle = hit; textTransformed = false; Capture = true; return;
            }
            CommitTextEdit(); selectedText = null;
            for (int i = Marks.Count - 1; i >= 0; i--)
            {
                var mark = Marks[i];
                if (mark.Tool != DrawTool.Text || !TextGeometry.Contains(mark, point, 3 / Zoom)) continue;
                selectedText = mark;
                NotifyTextAnnotationSelected();
                if (e.Clicks > 1) BeginTextEdit(mark, false);
                else { transformOriginal = mark.Clone(); beforeTextTransform = Snapshot(); transformAnchor = point; textHandle = -1; Capture = true; }
                Invalidate(); return;
            }
            var created = new Mark { Tool = DrawTool.Text, Color = Ink, Width = Stroke, FontSize = TextFontSize, TextStyle = TextFontStyle, Text = "" };
            created.Points.Add(point); created.TextSize = TextGeometry.Measure(created);
            BeginTextEdit(created, true);
        }
        private void BeginTextEdit(Mark mark) { BeginTextEdit(mark, false); }
        private void BeginTextEdit(Mark mark, bool isNew)
        {
            if (textEditor != null && selectedText == mark) { textEditor.Focus(); return; }
            CommitTextEdit(); beforeTextEdit = Snapshot(); editOriginal = mark.Clone(); newText = isNew;
            if (isNew) Marks.Add(mark);
            selectedText = mark;
            textEditor = new TextBox { Name = "annotation-text-input", Multiline = true, AcceptsReturn = true,
                WordWrap = false, BorderStyle = BorderStyle.FixedSingle, ScrollBars = ScrollBars.None,
                BackColor = Color.White, ForeColor = mark.Color, Text = mark.Text ?? "" };
            textEditor.TextChanged += delegate
            {
                if (textEditor == null || selectedText == null) return;
                selectedText.Text = textEditor.Text; selectedText.TextSize = TextGeometry.Measure(selectedText);
                UpdateTextEditor(); NotifyChanged();
            };
            Controls.Add(textEditor); UpdateTextEditor(); textEditor.BringToFront(); textEditor.Focus(); textEditor.Select(textEditor.TextLength, 0); Invalidate();
            NotifyTextAnnotationSelected();
        }
        private void UpdateTextEditor()
        {
            if (textEditor == null || selectedText == null) return;
            var previousFont = textEditorFont;
            textEditorFont = TextGeometry.Font(selectedText, Zoom); textEditor.Font = textEditorFont;
            if (previousFont != null) previousFont.Dispose();
            textEditor.ForeColor = selectedText.Color;
            var origin = selectedText.Points[0]; var size = TextGeometry.Size(selectedText);
            int x = Math.Max(0, Math.Min(Width - 20, (int)(origin.X * Zoom) + 4)), y = Math.Max(0, Math.Min(Height - 20, (int)(origin.Y * Zoom) + 4));
            textEditor.Bounds = new Rectangle(x, y, Math.Max(20, Math.Min(Width - x, (int)Math.Ceiling(size.Width * Zoom) - 8)),
                Math.Max(20, Math.Min(Height - y, (int)Math.Ceiling(size.Height * Zoom) - 8)));
        }
        internal void CommitTextEdit()
        {
            if (textEditor == null) return;
            var editor = textEditor; textEditor = null; Controls.Remove(editor); editor.Dispose(); textEditorFont.Dispose(); textEditorFont = null;
            bool empty = string.IsNullOrWhiteSpace(selectedText.Text);
            bool changed = newText ? !empty : empty || selectedText.Text != editOriginal.Text || selectedText.FontSize != editOriginal.FontSize ||
                selectedText.TextStyle != editOriginal.TextStyle || selectedText.Color != editOriginal.Color;
            if (empty) { Marks.Remove(selectedText); selectedText = null; }
            if (changed) { undo.Push(beforeTextEdit); redo.Clear(); NotifyChanged(); }
            beforeTextEdit = null; editOriginal = null; newText = false; Invalidate();
        }
        private void CancelTextEdit()
        {
            if (textEditor == null) return;
            var state = beforeTextEdit; var editor = textEditor; textEditor = null; Controls.Remove(editor); editor.Dispose(); textEditorFont.Dispose(); textEditorFont = null;
            Marks.Clear(); Marks.AddRange(state); selectedText = null; beforeTextEdit = null; editOriginal = null; newText = false; NotifyChanged(); Focus();
        }
        private void TextMouseMove(MouseEventArgs e)
        {
            var point = ImagePoint(e.Location);
            if (textHandle == -2 || selectedText == null)
            {
                int hit = HitTextHandle(point);
                Cursor = hit == 4 ? Cursors.Hand : hit >= 0 ? Cursors.SizeNWSE : hit == -1 ? Cursors.SizeAll : Cursors.IBeam;
                return;
            }
            if (!textTransformed && Distance(point, transformAnchor) < 2 / Zoom) return;
            if (!textTransformed) { undo.Push(beforeTextTransform); redo.Clear(); textTransformed = true; }
            var original = transformOriginal; var origin = original.Points[0];
            if (textHandle == -1)
                selectedText.Points[0] = new PointF(origin.X + point.X - transformAnchor.X, origin.Y + point.Y - transformAnchor.Y);
            else if (textHandle == 4)
            {
                var center = TextGeometry.Center(original);
                double first = Math.Atan2(transformAnchor.Y - center.Y, transformAnchor.X - center.X);
                double next = Math.Atan2(point.Y - center.Y, point.X - center.X);
                float angle = original.TextAngle + (float)((next - first) * 180 / Math.PI);
                if ((ModifierKeys & Keys.Shift) != 0) angle = (float)Math.Round(angle / 15) * 15;
                selectedText.TextAngle = (angle % 360 + 360) % 360;
            }
            else
            {
                var size = TextGeometry.Size(original); var local = TextGeometry.Local(original, point);
                bool left = textHandle == 0 || textHandle == 3, top = textHandle == 0 || textHandle == 1;
                var opposite = new PointF(left ? size.Width : 0, top ? size.Height : 0);
                float dx = left ? -size.Width : size.Width, dy = top ? -size.Height : size.Height;
                float scale = ((local.X - opposite.X) * dx + (local.Y - opposite.Y) * dy) / (dx * dx + dy * dy);
                scale = Math.Max(6 / TextGeometry.FontSize(original), Math.Min(300 / TextGeometry.FontSize(original), scale));
                var fixedPoint = TextGeometry.World(original, opposite);
                var centerDelta = TextGeometry.Rotate(new PointF(dx * scale / 2, dy * scale / 2), original.TextAngle);
                selectedText.TextSize = new SizeF(size.Width * scale, size.Height * scale);
                selectedText.FontSize = TextGeometry.FontSize(original) * scale;
                selectedText.Points[0] = new PointF(fixedPoint.X + centerDelta.X - selectedText.TextSize.Width / 2, fixedPoint.Y + centerDelta.Y - selectedText.TextSize.Height / 2);
            }
            NotifyChanged();
        }
        private void TextMouseUp(MouseEventArgs e)
        {
            if (textHandle == -2) return;
            TextMouseMove(e); bool transformed = textTransformed; EndTextTransform(); Capture = false;
            if (transformed) NotifyTextAnnotationSelected();
        }
        private void EndTextTransform()
        { textHandle = -2; beforeTextTransform = null; transformOriginal = null; textTransformed = false; }
        private void DrawTextSelection(Graphics g)
        {
            if (Tool != DrawTool.Text || selectedText == null) return;
            var corners = TextGeometry.Corners(selectedText); var handle = RotationHandle(selectedText);
            using (var pen = new Pen(Color.DodgerBlue, 1 / Zoom))
            {
                g.DrawPolygon(pen, corners);
                g.DrawLine(pen, TextGeometry.World(selectedText, new PointF(TextGeometry.Size(selectedText).Width / 2, 0)), handle);
                foreach (var corner in corners)
                {
                    var box = new RectangleF(corner.X - 3 / Zoom, corner.Y - 3 / Zoom, 6 / Zoom, 6 / Zoom);
                    g.FillRectangle(Brushes.White, box); g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
                }
                g.FillEllipse(Brushes.White, handle.X - 4 / Zoom, handle.Y - 4 / Zoom, 8 / Zoom, 8 / Zoom);
                g.DrawEllipse(pen, handle.X - 4 / Zoom, handle.Y - 4 / Zoom, 8 / Zoom, 8 / Zoom);
            }
        }
        internal bool HandleTextKey(KeyEventArgs e)
        {
            if (textEditor != null && textEditor.ContainsFocus)
            {
                if (e.KeyCode == Keys.Escape) { CancelTextEdit(); e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.Enter) { CommitTextEdit(); Focus(); e.SuppressKeyPress = true; }
                else if (e.Control && e.KeyCode == Keys.S) { CommitTextEdit(); return false; }
                return true; // Let the native editor handle IME, newlines, selection, clipboard and undo.
            }
            if (Tool != DrawTool.Text || selectedText == null || !ContainsFocus) return false;
            if (e.KeyCode == Keys.Escape) { CommitTextEdit(); selectedText = null; Invalidate(); e.SuppressKeyPress = true; return true; }
            if (e.KeyCode == Keys.F2) { BeginTextEdit(selectedText); e.SuppressKeyPress = true; return true; }
            if (e.KeyCode == Keys.Delete) { CommitTextEdit(); if (selectedText != null) { SaveUndo(); Marks.Remove(selectedText); selectedText = null; NotifyChanged(); } e.SuppressKeyPress = true; return true; }
            return false;
        }
    }
}
