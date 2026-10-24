using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class OcrSearchPanel : UserControl
    {
        private readonly TextBox query;
        private readonly Label count;
        private readonly Button previous, next;
        private AnnotationCanvas canvas;
        private bool recognized;

        internal OcrSearchPanel()
        {
            Name = "ocr-search"; Size = new Size(350, 38); BackColor = Color.White; Font = ToolAppearance.Font;
            AutoScaleMode = AutoScaleMode.None; Visible = false;
            Controls.Add(new Label { Text = "搜索", Bounds = new Rectangle(10, 8, 34, 22), TextAlign = ContentAlignment.MiddleLeft, ForeColor = ToolAppearance.Muted });
            query = new TextBox { Name = "ocr-search-query", AccessibleName = "搜索识别文字", BorderStyle = BorderStyle.FixedSingle, Bounds = new Rectangle(46, 8, 145, 24) };
            count = new Label { Name = "ocr-search-count", Bounds = new Rectangle(193, 8, 58, 22), TextAlign = ContentAlignment.MiddleCenter, ForeColor = ToolAppearance.Muted };
            previous = SearchButton("↑", "上一个匹配 (Shift+F3)", 253, delegate { canvas.MoveSearchMatch(-1); });
            next = SearchButton("↓", "下一个匹配 (Enter / F3)", 284, delegate { canvas.MoveSearchMatch(1); });
            SearchButton("×", "关闭搜索 (Esc)", 315, CloseSearch);
            Controls.Add(query); Controls.Add(count);
            query.TextChanged += delegate { if (canvas != null) canvas.SearchText(query.Text); };
        }
        private Button SearchButton(string text, string hint, int x, Action action)
        {
            var button = new ToolOptionButton { Text = text, AccessibleName = hint, Bounds = new Rectangle(x, 5, 28, 28), AccentColor = ToolAppearance.Accent("ocr") };
            button.Click += delegate { if (canvas != null) action(); }; Controls.Add(button); return button;
        }
        internal void Bind(AnnotationCanvas nextCanvas)
        {
            if (canvas != null) { canvas.TextSelectionChanged -= Synchronize; canvas.SearchRequested -= OpenSearch; }
            canvas = nextCanvas; recognized = false; query.Clear();
            canvas.TextSelectionChanged += Synchronize; canvas.SearchRequested += OpenSearch;
            Synchronize();
        }
        private void Synchronize()
        {
            bool available = canvas != null && canvas.HasTextRecognition;
            if (!available) { Visible = false; recognized = false; query.Clear(); }
            else if (!recognized) { recognized = true; Visible = true; BringToFront(); }
            count.Text = available ? canvas.SearchMatchNumber + "/" + canvas.SearchMatchCount : "0/0";
            previous.Enabled = next.Enabled = available && canvas.SearchMatchCount > 0;
        }
        private void OpenSearch() { Visible = true; BringToFront(); query.Focus(); query.SelectAll(); }
        private void CloseSearch() { query.Clear(); Visible = false; if (canvas != null) canvas.Focus(); }
        internal static bool ForwardGlobalFindNext()
        {
            var active = Form.ActiveForm;
            if (active == null || Native.GetForegroundWindow() != active.Handle) return false;
            var found = active.Controls.Find("ocr-search", true);
            if (found.Length == 0) return false;
            var search = found[0] as OcrSearchPanel;
            if (search == null || search.canvas == null || !search.canvas.HasTextRecognition) return false;
            if (search.canvas.SearchMatchCount == 0) search.OpenSearch();
            else search.canvas.MoveSearchMatch(1);
            return true;
        }
        internal bool HandleKey(KeyEventArgs e)
        {
            if (!ContainsFocus) return false;
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.F3) { canvas.MoveSearchMatch(e.Shift ? -1 : 1); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { CloseSearch(); e.SuppressKeyPress = true; }
            // Text editing shortcuts belong to the search box while it has focus.
            return true;
        }
        protected override void OnPaint(PaintEventArgs e)
        { base.OnPaint(e); using (var border = new Pen(ToolAppearance.Border)) e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1); }
        protected override void Dispose(bool disposing)
        {
            if (disposing && canvas != null) { canvas.TextSelectionChanged -= Synchronize; canvas.SearchRequested -= OpenSearch; }
            base.Dispose(disposing);
        }
    }
}
