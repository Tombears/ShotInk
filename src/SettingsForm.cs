using System;
using System.Drawing;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class ShortcutBox : TextBox
    {
        private Keys value;
        internal Keys ShortcutValue { get { return value; } set { this.value = value; Text = Shortcut.Display(value); } }
        internal ShortcutBox() { ReadOnly = true; BackColor = Color.White; }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) ShortcutValue = Keys.None;
            else if (e.KeyCode != Keys.ControlKey && e.KeyCode != Keys.ShiftKey && e.KeyCode != Keys.Menu && e.KeyCode != Keys.LWin && e.KeyCode != Keys.RWin)
                ShortcutValue = e.KeyData;
            base.OnKeyDown(e);
        }
    }

    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings original;
        private readonly ShortcutBox capture;
        private readonly ShortcutBox pin;
        private readonly ShortcutBox scroll;
        private readonly TextBox folder;
        private readonly CheckBox ask;
        private readonly Label error;
        internal SettingsForm(AppSettings current, Func<AppSettings, string> apply)
        {
            original = current; Text = "设置 · ShotInk"; AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 9); ClientSize = new Size(650, 430); MinimumSize = new Size(640, 460);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; MaximizeBox = false; FormBorderStyle = FormBorderStyle.FixedDialog;
            BackColor = Color.White;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 3, RowCount = 9 };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
            for (int i = 0; i < 9; i++) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, new[] { 38, 38, 38, 35, 42, 34, 35, 42, 50 }[i]));
            capture = AddShortcut(layout, "区域截图", 0, current.CaptureShortcut);
            pin = AddShortcut(layout, "剪贴板贴图", 1, current.PinShortcut);
            scroll = AddShortcut(layout, "滚动长截图", 2, current.ScrollShortcut);
            var hint = new Label { Text = "点击输入框后按快捷键；Backspace 清除。", Dock = DockStyle.Fill, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleLeft };
            layout.Controls.Add(hint, 0, 3); layout.SetColumnSpan(hint, 3);
            layout.Controls.Add(new Label { Text = "默认保存文件夹", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 4);
            folder = new TextBox { Name = "save-directory", Dock = DockStyle.Fill, Text = current.SaveDirectory, Margin = new Padding(3, 8, 3, 3) }; layout.Controls.Add(folder, 1, 4);
            var browse = new Button { Text = "浏览…", Dock = DockStyle.Fill, Margin = new Padding(6, 5, 0, 5) };
            browse.Click += delegate { using (var dialog = new FolderBrowserDialog { Description = "选择图片默认保存文件夹", SelectedPath = folder.Text }) if (dialog.ShowDialog(this) == DialogResult.OK) folder.Text = dialog.SelectedPath; };
            layout.Controls.Add(browse, 2, 4);
            ask = new CheckBox { Name = "ask-save-location", Text = "每次保存时询问保存位置", Checked = current.AskSaveLocation, Dock = DockStyle.Fill };
            layout.Controls.Add(ask, 0, 5); layout.SetColumnSpan(ask, 3);
            var description = new Label { Text = "取消勾选后，保存按钮和 Ctrl+S 直接保存到上方文件夹。", Dock = DockStyle.Fill, ForeColor = Theme.Muted };
            layout.Controls.Add(description, 0, 6); layout.SetColumnSpan(description, 3);
            error = new Label { Dock = DockStyle.Fill, ForeColor = Color.Firebrick }; layout.Controls.Add(error, 0, 7); layout.SetColumnSpan(error, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
            var save = new Button { Text = "保存", Width = 85, Height = 30 };
            save.Click += delegate
            {
                try { var candidate = ReadSettings(); string message = apply(candidate); if (message != null) { error.Text = message; return; } DialogResult = DialogResult.OK; Close(); }
                catch (Exception ex) { error.Text = ex.Message; }
            };
            var cancel = new Button { Text = "取消", Width = 85, Height = 30, DialogResult = DialogResult.Cancel };
            var reset = new Button { Text = "恢复默认快捷键", Width = 130, Height = 30 };
            reset.Click += delegate { var defaults = new AppSettings(); capture.ShortcutValue = defaults.CaptureShortcut; pin.ShortcutValue = defaults.PinShortcut; scroll.ShortcutValue = defaults.ScrollShortcut; };
            buttons.Controls.Add(save); buttons.Controls.Add(cancel); buttons.Controls.Add(reset);
            layout.Controls.Add(buttons, 0, 8); layout.SetColumnSpan(buttons, 3);
            Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
        }
        private static ShortcutBox AddShortcut(TableLayoutPanel layout, string title, int row, Keys value)
        {
            layout.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
            var box = new ShortcutBox { Name = "hotkey-" + row, ShortcutValue = value, Dock = DockStyle.Fill, Margin = new Padding(3, 6, 3, 3) };
            layout.Controls.Add(box, 1, row); layout.SetColumnSpan(box, 2); return box;
        }
        internal AppSettings ReadSettings()
        {
            var candidate = original.Clone(); candidate.CaptureShortcut = capture.ShortcutValue; candidate.PinShortcut = pin.ShortcutValue; candidate.ScrollShortcut = scroll.ShortcutValue;
            candidate.SaveDirectory = folder.Text.Trim(); candidate.AskSaveLocation = ask.Checked; candidate.Validate(); return candidate;
        }
    }
}
