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
                var lines = (mark.Text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                float width = 0;
                foreach (var line in lines) width = Math.Max(width, Advance(g, font, line));
                return new SizeF(Math.Max(FontSize(mark) / 2, width) + 6, font.GetHeight(g) * lines.Length + 4);
            }
        }
        private static float Advance(Graphics g, Font font, string text)
        {
            if (text.Length == 0) return 0;
            using (var format = Format()) return g.MeasureString(text, font, new SizeF(1000000, 1000000), format).Width;
        }
        internal static StringFormat Format()
        { var format = new StringFormat(StringFormat.GenericTypographic); format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap; return format; }
        internal static PointF Caret(Mark mark, int index)
        {
            string text = mark.Text ?? ""; index = Math.Max(0, Math.Min(index, text.Length));
            int line = 0, start = 0;
            for (int i = 0; i < index; i++)
                if (text[i] == '\n' || text[i] == '\r')
                { if (text[i] == '\r' && i + 1 < index && text[i + 1] == '\n') i++; line++; start = i + 1; }
            using (var bitmap = new Bitmap(1, 1)) using (var g = Graphics.FromImage(bitmap)) using (var font = Font(mark, 1))
                return new PointF(3 + Advance(g, font, text.Substring(start, index - start)), 2 + line * font.GetHeight(g));
        }
        internal static float LineHeight(Mark mark)
        { using (var font = Font(mark, 1)) return font.GetHeight(); }
        internal static int CharacterAt(Mark mark, PointF local)
        {
            string text = mark.Text ?? ""; float height = LineHeight(mark);
            int line = Math.Max(0, (int)Math.Floor((local.Y - 2) / height));
            int start = 0, current = 0;
            for (int i = 0; i < text.Length && current < line; i++)
                if (text[i] == '\n' || text[i] == '\r')
                { if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++; start = i + 1; current++; }
            int end = start;
            while (end < text.Length && text[end] != '\r' && text[end] != '\n') end++;
            using (var bitmap = new Bitmap(1, 1)) using (var g = Graphics.FromImage(bitmap)) using (var font = Font(mark, 1))
            {
                int low = start, high = end; float x = local.X - 3;
                while (low < high)
                {
                    int middle = low + (high - low) / 2;
                    if (Advance(g, font, text.Substring(start, middle - start)) < x) low = middle + 1; else high = middle;
                }
                int nearest = low;
                if (nearest > start && Math.Abs(Advance(g, font, text.Substring(start, nearest - start - 1)) - x) <
                    Math.Abs(Advance(g, font, text.Substring(start, nearest - start)) - x)) nearest--;
                if (nearest > start && nearest < text.Length && char.IsLowSurrogate(text[nearest]) && char.IsHighSurrogate(text[nearest - 1])) nearest--;
                return nearest;
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
                using (var format = Format())
                    g.DrawString(mark.Text ?? "", font, brush, new PointF(-size.Width / 2 + 3, -size.Height / 2 + 2), format);
            }
            finally { g.Restore(state); }
        }
    }

    internal sealed partial class AnnotationCanvas
    {
        internal float TextFontSize = 24;
        internal FontStyle TextFontStyle = FontStyle.Regular;
        private Mark selectedText;
        private InlineTextInput textEditor;
        private Font textEditorFont;
        private bool selectingAnnotationText;
        private int annotationTextAnchor;
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
            var point = ImagePoint(e.Location); int hit = HitTextHandle(point);
            if (textEditor != null && hit == -1)
            {
                var local = TextGeometry.Local(selectedText, point); var size = TextGeometry.Size(selectedText);
                float margin = 5 / Zoom;
                if (local.X > margin && local.Y > margin && local.X < size.Width - margin && local.Y < size.Height - margin)
                {
                    int index = TextGeometry.CharacterAt(selectedText, local);
                    annotationTextAnchor = (ModifierKeys & Keys.Shift) != 0 ? textEditor.SelectionStart : index;
                    textEditor.Select(Math.Min(annotationTextAnchor, index), Math.Abs(index - annotationTextAnchor));
                    if (e.Clicks > 1) textEditor.SelectAll();
                    textEditor.Focus(); selectingAnnotationText = true; Capture = true; UpdateTextEditor(); Invalidate(); return;
                }
            }
            Focus();
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
            textEditor = new InlineTextInput { Name = "annotation-text-input", Text = mark.Text ?? "" };
            textEditor.TextChanged += delegate
            {
                if (textEditor == null || selectedText == null) return;
                selectedText.Text = textEditor.Text;
                UpdateTextEditor(); NotifyChanged();
            };
            textEditor.InputStateChanged += delegate { UpdateTextEditor(); Invalidate(); };
            UpdateTextEditor(); Controls.Add(textEditor); textEditor.Select(textEditor.TextLength, 0); textEditor.Focus(); UpdateTextEditor(); Invalidate();
            NotifyTextAnnotationSelected();
        }
        private void UpdateTextEditor()
        {
            if (textEditor == null || selectedText == null) return;
            float pixels = Math.Max(1, TextGeometry.FontSize(selectedText) * Zoom);
            if (textEditorFont == null || Math.Abs(textEditorFont.Size - pixels) > 0.01F || textEditorFont.Style != selectedText.TextStyle)
            {
                var previousFont = textEditorFont;
                textEditorFont = TextGeometry.Font(selectedText, Zoom); textEditor.Font = textEditorFont;
                if (previousFont != null) previousFont.Dispose();
            }
            textEditor.ForeColor = selectedText.Color;
            var display = EditingTextMark(); selectedText.TextSize = TextGeometry.Measure(display); display.TextSize = selectedText.TextSize;
            int caret = textEditor.SelectionStart + textEditor.Composition.Length;
            var position = TextGeometry.World(display, TextGeometry.Caret(display, caret));
            textEditor.Bounds = new Rectangle(Math.Max(0, Math.Min(Width - 1, (int)Math.Round(position.X * Zoom))),
                Math.Max(0, Math.Min(Height - 1, (int)Math.Round(position.Y * Zoom))), 1, Math.Max(1, (int)Math.Ceiling(TextGeometry.LineHeight(display) * Zoom)));
            textEditor.PositionIme();
        }
        private Mark EditingTextMark()
        {
            if (textEditor == null || selectedText == null || textEditor.Composition.Length == 0) return selectedText;
            var display = selectedText.Clone(); string text = display.Text ?? "";
            int start = Math.Min(textEditor.SelectionStart, text.Length), length = Math.Min(textEditor.SelectionLength, text.Length - start);
            display.Text = text.Remove(start, length).Insert(start, textEditor.Composition); return display;
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
            if (selectingAnnotationText && textEditor != null)
            {
                int index = TextGeometry.CharacterAt(selectedText, TextGeometry.Local(selectedText, point));
                textEditor.Select(Math.Min(annotationTextAnchor, index), Math.Abs(index - annotationTextAnchor));
                UpdateTextEditor(); Invalidate(); return;
            }
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
            if (selectingAnnotationText) { TextMouseMove(e); selectingAnnotationText = false; Capture = false; return; }
            if (textHandle == -2) return;
            TextMouseMove(e); bool transformed = textTransformed; EndTextTransform(); Capture = false;
            if (transformed) NotifyTextAnnotationSelected();
        }
        private void EndTextTransform()
        { textHandle = -2; beforeTextTransform = null; transformOriginal = null; textTransformed = false; selectingAnnotationText = false; }
        private void DrawEditingText(Graphics g)
        {
            if (textEditor == null || selectedText == null) return;
            var display = EditingTextMark(); float height = TextGeometry.LineHeight(display);
            int start = textEditor.SelectionStart, end = start + textEditor.SelectionLength;
            if (textEditor.Composition.Length == 0 && end > start)
            {
                for (int index = start; index < end;)
                {
                    int lineEnd = index;
                    while (lineEnd < end && display.Text[lineEnd] != '\r' && display.Text[lineEnd] != '\n') lineEnd++;
                    var from = TextGeometry.Caret(display, index); var to = TextGeometry.Caret(display, lineEnd);
                    float right = to.X + (lineEnd < end ? TextGeometry.FontSize(display) / 2 : 0);
                    using (var brush = new SolidBrush(Color.FromArgb(85, Color.DodgerBlue)))
                        g.FillPolygon(brush, new[] { TextGeometry.World(display, from), TextGeometry.World(display, new PointF(right, from.Y)),
                            TextGeometry.World(display, new PointF(right, from.Y + height)), TextGeometry.World(display, new PointF(from.X, from.Y + height)) });
                    index = lineEnd;
                    if (index < end && display.Text[index] == '\r') index++;
                    if (index < end && display.Text[index] == '\n') index++;
                }
            }
            if (textEditor.Composition.Length > 0)
            {
                var from = TextGeometry.Caret(display, start); var to = TextGeometry.Caret(display, start + textEditor.Composition.Length);
                using (var pen = new Pen(display.Color, 1 / Zoom))
                    g.DrawLine(pen, TextGeometry.World(display, new PointF(from.X, from.Y + height)), TextGeometry.World(display, new PointF(to.X, to.Y + height)));
            }
            if (textEditor.Focused && textEditor.CaretVisible)
            {
                var caret = TextGeometry.Caret(display, start + textEditor.Composition.Length);
                using (var outline = new Pen(Color.FromArgb(180, Color.White), 3 / Zoom))
                using (var pen = new Pen(display.Color, 1 / Zoom))
                {
                    var from = TextGeometry.World(display, caret); var to = TextGeometry.World(display, new PointF(caret.X, caret.Y + height));
                    g.DrawLine(outline, from, to); g.DrawLine(pen, from, to);
                }
            }
        }
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
