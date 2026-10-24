using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class OcrSelectionModel
    {
        private sealed class Line
        {
            internal OcrLine Source;
            internal int Row;
        }
        private sealed class Glyph
        {
            internal Line Line;
            internal OcrCharacter Character;
        }
        private readonly List<Glyph> glyphs = new List<Glyph>();
        private readonly HashSet<Glyph> selected = new HashSet<Glyph>();
        private readonly List<RectangleF> matches = new List<RectangleF>();
        private sealed class SearchRow
        {
            internal string Text;
            internal Glyph[] Characters;
        }
        private readonly List<SearchRow> searchRows = new List<SearchRow>();
        internal int MatchCount { get { return matches.Count; } }
        internal int MatchIndex { get; private set; }
        internal RectangleF CurrentMatch { get { return matches.Count == 0 ? RectangleF.Empty : matches[MatchIndex]; } }
        internal bool HasSelection { get { return selected.Count > 0; } }
        internal string SelectedText { get { return GetText(false); } }
        internal string AllText { get { return GetText(true); } }

        internal OcrSelectionModel(OcrDocument document)
        {
            var rows = new List<List<Line>>();
            foreach (var source in (document.Lines ?? new OcrLine[0]).Where(line => !string.IsNullOrEmpty(line.Text)).OrderBy(line => line.Bounds.Top))
            {
                var line = new Line { Source = source };
                var row = rows.LastOrDefault();
                var bounds = source.Bounds;
                if (row == null || Math.Abs(row[0].Source.Bounds.Top + row[0].Source.Bounds.Height / 2 - bounds.Top - bounds.Height / 2) > Math.Max(row[0].Source.Bounds.Height, bounds.Height) * 0.55F)
                { row = new List<Line>(); rows.Add(row); }
                line.Row = rows.Count - 1; row.Add(line);
            }
            foreach (var line in rows.SelectMany(row => row.OrderBy(item => item.Source.Bounds.Left)))
            {
                var characters = line.Source.Characters;
                var valid = (characters ?? new OcrCharacter[0]).Where(character => !string.IsNullOrWhiteSpace(character.Text) && character.Start >= 0 && character.Length > 0 && character.Start + character.Length <= line.Source.Text.Length).ToList();
                // Some recognizers return line text but omit individual character boxes.
                // Keep those characters searchable/selectable using the line's fallback geometry.
                foreach (var fallback in FallbackCharacters(line.Source))
                    if (!string.IsNullOrWhiteSpace(fallback.Text) && !valid.Any(character => character.Start <= fallback.Start && character.Start + character.Length >= fallback.Start + fallback.Length)) valid.Add(fallback);
                foreach (var character in valid.OrderBy(item => item.Start)) glyphs.Add(new Glyph { Line = line, Character = character });
            }
            BuildSearchIndex();
        }
        private static string NormalizeSearch(string text)
        {
            var normalized = new System.Text.StringBuilder();
            foreach (char value in text.Normalize(System.Text.NormalizationForm.FormKC))
                if (!char.IsWhiteSpace(value) && char.GetUnicodeCategory(value) != UnicodeCategory.Format) normalized.Append(value);
            return normalized.ToString();
        }
        private void BuildSearchIndex()
        {
            foreach (var row in glyphs.GroupBy(glyph => glyph.Line.Row))
            {
                var text = new System.Text.StringBuilder(); var positions = new List<Glyph>();
                foreach (var line in row.GroupBy(glyph => glyph.Line))
                {
                    var characters = line.ToArray();
                    var iterator = StringInfo.GetTextElementEnumerator(line.Key.Source.Text);
                    while (iterator.MoveNext())
                    {
                        string element = iterator.GetTextElement(), normalized = NormalizeSearch(element);
                        var glyph = characters.FirstOrDefault(item => item.Character.Start < iterator.ElementIndex + element.Length && item.Character.Start + item.Character.Length > iterator.ElementIndex);
                        foreach (char value in normalized) { text.Append(value); positions.Add(glyph); }
                    }
                }
                searchRows.Add(new SearchRow { Text = text.ToString(), Characters = positions.ToArray() });
            }
        }
        private static OcrCharacter[] FallbackCharacters(OcrLine line)
        {
            var elements = new List<OcrCharacter>();
            var iterator = StringInfo.GetTextElementEnumerator(line.Text);
            float units = 0;
            while (iterator.MoveNext())
            {
                string text = iterator.GetTextElement(); float weight = text[0] > 255 ? 2 : 1;
                elements.Add(new OcrCharacter { Text = text, Start = iterator.ElementIndex, Length = text.Length, Box = new[] { units, 0F, weight, 1F } });
                units += weight;
            }
            var box = line.Bounds;
            foreach (var item in elements) item.Box = new[] { box.Left + item.Box[0] / units * box.Width, box.Top, item.Box[2] / units * box.Width, box.Height };
            return elements.ToArray();
        }
        internal void SelectRectangle(RectangleF area)
        {
            selected.Clear();
            // A normal drag along one text line may have no vertical extent.
            // Use that line's height so horizontal text selection still works.
            if (area.Width > 0 && area.Height < 3)
            {
                var hit = glyphs.FirstOrDefault(glyph => glyph.Character.Bounds.Top <= area.Top && glyph.Character.Bounds.Bottom >= area.Bottom &&
                    glyph.Character.Bounds.Left < area.Right && glyph.Character.Bounds.Right > area.Left);
                if (hit != null) area = new RectangleF(area.Left, hit.Line.Source.Bounds.Top, area.Width, hit.Line.Source.Bounds.Height);
            }
            foreach (var glyph in glyphs)
            {
                var box = glyph.Character.Bounds;
                if (box.Width <= 0 || box.Height <= 0) continue;
                var overlap = RectangleF.Intersect(area, box);
                if (area.Contains(box.Left + box.Width / 2, box.Top + box.Height / 2) || overlap.Width * overlap.Height >= box.Width * box.Height * 0.2F)
                    selected.Add(glyph);
            }
        }
        internal void SelectWord(PointF point, bool wholeLine)
        {
            selected.Clear();
            var hit = glyphs.FirstOrDefault(glyph => { var box = glyph.Character.Bounds; box.Inflate(2, 2); return box.Contains(point); });
            if (hit == null) return;
            string text = hit.Line.Source.Text;
            int start = hit.Character.Start, end = start + hit.Character.Length;
            if (wholeLine) { start = 0; end = text.Length; }
            else if (AsciiWord(text[start]))
            {
                while (start > 0 && AsciiWord(text[start - 1])) start--;
                while (end < text.Length && AsciiWord(text[end])) end++;
            }
            foreach (var glyph in glyphs)
                if (glyph.Line == hit.Line && glyph.Character.Start < end && glyph.Character.Start + glyph.Character.Length > start) selected.Add(glyph);
        }
        private static bool AsciiWord(char value) { return value >= '!' && value <= '~'; }
        internal void SelectAll() { selected.Clear(); foreach (var glyph in glyphs) selected.Add(glyph); }
        internal void Search(string query)
        {
            matches.Clear(); MatchIndex = 0;
            if (string.IsNullOrWhiteSpace(query)) return;
            query = NormalizeSearch(query);
            if (query.Length == 0) return;
            foreach (var row in searchRows)
            {
                string text = row.Text;
                int offset = 0, start;
                while (offset <= text.Length - query.Length && (start = text.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase)) >= 0)
                {
                    var characters = row.Characters.Skip(start).Take(query.Length).Where(glyph => glyph != null).Distinct().ToArray();
                    if (characters.Length > 0)
                        matches.Add(RectangleF.FromLTRB(characters.Min(glyph => glyph.Character.Bounds.Left), characters.Min(glyph => glyph.Character.Bounds.Top),
                            characters.Max(glyph => glyph.Character.Bounds.Right), characters.Max(glyph => glyph.Character.Bounds.Bottom)));
                    offset = start + query.Length;
                }
            }
        }
        internal void MoveMatch(int direction)
        { if (matches.Count > 0) MatchIndex = (MatchIndex + direction % matches.Count + matches.Count) % matches.Count; }
        private string GetText(bool all)
        {
            var output = new System.Text.StringBuilder(); int previousRow = -1;
            foreach (var line in glyphs.Where(glyph => all || selected.Contains(glyph)).GroupBy(glyph => glyph.Line))
            {
                int start = line.Min(glyph => glyph.Character.Start), end = line.Max(glyph => glyph.Character.Start + glyph.Character.Length);
                if (output.Length > 0) output.Append(previousRow == line.Key.Row ? "\t" : Environment.NewLine);
                output.Append(line.Key.Source.Text.Substring(start, end - start)); previousRow = line.Key.Row;
            }
            return output.ToString();
        }
        internal void Draw(Graphics graphics)
        {
            using (var blue = new SolidBrush(Color.FromArgb(85, 75, 153, 246)))
                foreach (var row in selected.GroupBy(glyph => glyph.Line.Row))
                {
                    // Paint once per text run, including the spaces between glyphs.
                    // Per-character alpha rectangles leave gaps and darker seams.
                    var runs = row.GroupBy(glyph => glyph.Line).Select(line => RectangleF.FromLTRB(
                        line.Min(glyph => glyph.Character.Bounds.Left), line.Key.Source.Bounds.Top,
                        line.Max(glyph => glyph.Character.Bounds.Right), line.Key.Source.Bounds.Bottom)).OrderBy(box => box.Left);
                    RectangleF band = RectangleF.Empty;
                    foreach (var run in runs)
                    {
                        if (band.IsEmpty) band = run;
                        else if (run.Left - band.Right <= Math.Max(band.Height, run.Height) * 1.25F) band = RectangleF.Union(band, run);
                        else { band.Inflate(0.5F, 0.5F); graphics.FillRectangle(blue, band); band = run; }
                    }
                    if (!band.IsEmpty) { band.Inflate(0.5F, 0.5F); graphics.FillRectangle(blue, band); }
                }
            for (int index = 0; index < matches.Count; index++)
            {
                var bounds = matches[index]; bounds.Inflate(1, 1);
                using (var fill = new SolidBrush(Color.FromArgb(index == MatchIndex ? 155 : 80, 255, 185, 45))) graphics.FillRectangle(fill, bounds);
                if (index == MatchIndex) using (var border = new Pen(Color.FromArgb(218, 119, 12), 1.5F)) graphics.DrawRectangle(border, bounds.X, bounds.Y, bounds.Width, bounds.Height);
            }
        }
    }

    internal sealed class OcrSelectionActions : FlowLayoutPanel
    {
        private readonly Button copy;
        internal event Action CopySelected;
        internal event Action CopyAll;
        internal event Action ExitSelection;
        internal OcrSelectionActions()
        {
            Height = 112; Width = 440; WrapContents = false; FlowDirection = FlowDirection.TopDown;
            Padding = new Padding(12, 8, 12, 8); BackColor = Color.White; Font = ToolAppearance.Font;
            var header = new FlowLayoutPanel { Width = 400, Height = 30, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
            header.Controls.Add(new Label { Text = "文字识别", Width = 72, Height = 28, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ToolAppearance.Accent("ocr"), Font = ToolAppearance.HeadingFont, Margin = Padding.Empty });
            header.Controls.Add(new Label { Text = "从图片中选择并复制文字", Width = 260, Height = 28, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = ToolAppearance.Muted, Margin = Padding.Empty });
            Controls.Add(header);
            var actions = new FlowLayoutPanel { Width = 400, Height = 36, WrapContents = false, Margin = Padding.Empty };
            copy = ActionButton(actions, "复制选中", delegate { if (CopySelected != null) CopySelected(); });
            ((ToolOptionButton)copy).Selected = true;
            ActionButton(actions, "复制全部", delegate { if (CopyAll != null) CopyAll(); });
            ActionButton(actions, "返回标注", delegate { if (ExitSelection != null) ExitSelection(); });
            Controls.Add(actions);
            Controls.Add(new Label { Text = "拖选文字 · Ctrl+A 全选 · Ctrl+C 复制", Width = 400, Height = 24,
                TextAlign = ContentAlignment.MiddleLeft, ForeColor = ToolAppearance.Muted, Margin = Padding.Empty });
            SetSelection(false);
        }
        private Button ActionButton(FlowLayoutPanel row, string text, Action action)
        {
            var button = new ToolOptionButton { Text = text, Width = 96, Height = 30, Margin = new Padding(0, 1, 8, 0), AccentColor = ToolAppearance.Accent("ocr") };
            button.Click += delegate { action(); }; row.Controls.Add(button); return button;
        }
        internal void SetSelection(bool selected) { copy.Enabled = selected; }
    }
}
