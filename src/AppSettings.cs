using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ShotInk
{
    internal sealed class ToolPreferences
    {
        internal Color Ink = Color.FromArgb(239, 64, 76);
        internal float Width = 3;
        internal float FontSize = 24;
        internal FontStyle FontStyle;
        internal int BrushSize = 24;
        internal int MosaicSize = 14;
        internal MosaicEffect MosaicEffect;
        internal int BlurRadius = 8;
        internal ToolPreferences Clone() { return (ToolPreferences)MemberwiseClone(); }
    }

    internal sealed class AppSettings
    {
        internal bool AutoHistory = true;
        internal int HistoryLimit = 10;
        internal bool AskSaveLocation = true;
        internal string SaveDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        internal Keys CaptureShortcut = Keys.F1;
        internal Keys PinShortcut = Keys.F3;
        internal Keys ScrollShortcut = Keys.Control | Keys.Alt | Keys.L;
        private readonly Dictionary<DrawTool, ToolPreferences> tools = new Dictionary<DrawTool, ToolPreferences>();
        internal event Action Changed;
        internal ToolPreferences Tool(DrawTool tool)
        {
            ToolPreferences value;
            if (!tools.TryGetValue(tool, out value)) { value = new ToolPreferences(); tools.Add(tool, value); }
            return value;
        }
        internal Keys[] Shortcuts { get { return new[] { CaptureShortcut, PinShortcut, ScrollShortcut }; } }
        internal void NotifyChanged() { if (Changed != null) Changed(); }
        internal AppSettings Clone() { var copy = new AppSettings(); copy.CopyFrom(this); return copy; }
        internal void CopyFrom(AppSettings other)
        {
            AutoHistory = other.AutoHistory; HistoryLimit = other.HistoryLimit; AskSaveLocation = other.AskSaveLocation; SaveDirectory = other.SaveDirectory;
            CaptureShortcut = other.CaptureShortcut; PinShortcut = other.PinShortcut; ScrollShortcut = other.ScrollShortcut;
            tools.Clear(); foreach (var item in other.tools) tools.Add(item.Key, item.Value.Clone());
        }
        internal static AppSettings Load(string path)
        {
            var settings = new AppSettings();
            try
            {
                if (!File.Exists(path)) return settings;
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(path))
                { int at = line.IndexOf('='); if (at > 0) values[line.Substring(0, at).Trim()] = line.Substring(at + 1).Trim(); }
                string value;
                if (values.TryGetValue("save", out value)) settings.AutoHistory = value != "0";
                settings.HistoryLimit = (int)ReadNumber(values, "history-limit", 1, 10000, settings.HistoryLimit);
                if (values.TryGetValue("ask-save-location", out value)) settings.AskSaveLocation = value != "0";
                if (values.TryGetValue("save-directory", out value) && AbsolutePath(value)) settings.SaveDirectory = value;
                settings.CaptureShortcut = ReadShortcut(values, "hotkey-capture", settings.CaptureShortcut);
                settings.PinShortcut = ReadShortcut(values, "hotkey-pin", settings.PinShortcut);
                settings.ScrollShortcut = ReadShortcut(values, "hotkey-scroll", settings.ScrollShortcut);
                if (settings.Shortcuts.Where(key => key != Keys.None).Distinct().Count() != settings.Shortcuts.Count(key => key != Keys.None))
                { settings.CaptureShortcut = Keys.F1; settings.PinShortcut = Keys.F3; settings.ScrollShortcut = Keys.Control | Keys.Alt | Keys.L; }
                foreach (DrawTool tool in Enum.GetValues(typeof(DrawTool)))
                {
                    var style = settings.Tool(tool); string prefix = "tool." + tool + ".";
                    int color;
                    if (values.TryGetValue(prefix + "color", out value) && int.TryParse(value, out color)) style.Ink = Color.FromArgb(color);
                    style.Width = ReadNumber(values, prefix + "width", 1, 12, style.Width);
                    style.FontSize = ReadNumber(values, prefix + "font-size", 6, 300, style.FontSize);
                    style.FontStyle = (FontStyle)(int)ReadNumber(values, prefix + "font-style", 0, 3, (int)style.FontStyle);
                    style.BrushSize = (int)ReadNumber(values, prefix + "brush-size", 4, 160, style.BrushSize);
                    style.MosaicSize = (int)ReadNumber(values, prefix + "mosaic-size", 4, 48, style.MosaicSize);
                    style.MosaicEffect = (MosaicEffect)(int)ReadNumber(values, prefix + "mosaic-effect", 0, 1, 0);
                    style.BlurRadius = (int)ReadNumber(values, prefix + "blur-radius", 1, 24, style.BlurRadius);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            return settings;
        }
        private static Keys ReadShortcut(Dictionary<string, string> values, string name, Keys fallback)
        { string value; int number; return values.TryGetValue(name, out value) && int.TryParse(value, out number) && Shortcut.Valid((Keys)number) ? (Keys)number : fallback; }
        private static float ReadNumber(Dictionary<string, string> values, string name, float minimum, float maximum, float fallback)
        { string value; float number; return values.TryGetValue(name, out value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !float.IsNaN(number) && !float.IsInfinity(number) ? Math.Max(minimum, Math.Min(maximum, number)) : fallback; }
        internal void Validate()
        {
            if (!AbsolutePath(SaveDirectory) || SaveDirectory.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                throw new ArgumentException("请选择有效的绝对保存路径。");
            Path.GetFullPath(SaveDirectory);
            if (HistoryLimit < 1 || HistoryLimit > 10000) throw new ArgumentException("历史保留张数应为 1～10000。");
            if (Shortcuts.Any(key => !Shortcut.Valid(key))) throw new ArgumentException("快捷键无效：字母、数字和 Esc 需搭配 Ctrl 或 Alt，不能只使用修饰键。");
            if (Shortcuts.Where(key => key != Keys.None).Distinct().Count() != Shortcuts.Count(key => key != Keys.None)) throw new ArgumentException("不同功能不能使用相同的快捷键。");
        }
        private static bool AbsolutePath(string path)
        { return !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path) && !(path.Length >= 2 && path[1] == ':' && (path.Length < 3 || path[2] != '\\' && path[2] != '/')); }
        internal void Save(string path)
        {
            Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            var lines = new List<string> { "save=" + (AutoHistory ? "1" : "0"), "history-limit=" + HistoryLimit, "ask-save-location=" + (AskSaveLocation ? "1" : "0"),
                "save-directory=" + SaveDirectory, "hotkey-capture=" + (int)CaptureShortcut, "hotkey-pin=" + (int)PinShortcut, "hotkey-scroll=" + (int)ScrollShortcut };
            foreach (var item in tools.OrderBy(item => (int)item.Key))
            {
                string prefix = "tool." + item.Key + "."; var style = item.Value;
                lines.Add(prefix + "color=" + style.Ink.ToArgb()); lines.Add(prefix + "width=" + style.Width.ToString(CultureInfo.InvariantCulture));
                lines.Add(prefix + "font-size=" + style.FontSize.ToString(CultureInfo.InvariantCulture)); lines.Add(prefix + "font-style=" + (int)style.FontStyle);
                lines.Add(prefix + "brush-size=" + style.BrushSize); lines.Add(prefix + "mosaic-size=" + style.MosaicSize);
                lines.Add(prefix + "mosaic-effect=" + (int)style.MosaicEffect); lines.Add(prefix + "blur-radius=" + style.BlurRadius);
            }
            string temporary = path + ".new";
            try
            {
                File.WriteAllLines(temporary, lines, new System.Text.UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal static class Shortcut
    {
        internal static bool Valid(Keys keys)
        {
            if (keys == Keys.None) return true;
            Keys key = keys & Keys.KeyCode;
            if ((keys & ~(Keys.KeyCode | Keys.Control | Keys.Alt | Keys.Shift)) != 0 || !Enum.IsDefined(typeof(Keys), key) || key == Keys.None ||
                key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin ||
                key == Keys.LControlKey || key == Keys.RControlKey || key == Keys.LShiftKey || key == Keys.RShiftKey || key == Keys.LMenu || key == Keys.RMenu) return false;
            bool typingKey = key >= Keys.A && key <= Keys.Z || key >= Keys.D0 && key <= Keys.D9 || key >= Keys.NumPad0 && key <= Keys.Divide ||
                key >= Keys.OemSemicolon && key <= Keys.OemBackslash || key == Keys.Space || key == Keys.Enter || key == Keys.Tab || key == Keys.Back || key == Keys.Escape;
            return !typingKey || (keys & (Keys.Control | Keys.Alt)) != 0;
        }
        internal static uint Modifiers(Keys keys)
        { return 0x4000U | ((keys & Keys.Alt) != 0 ? 1U : 0) | ((keys & Keys.Control) != 0 ? 2U : 0) | ((keys & Keys.Shift) != 0 ? 4U : 0); }
        internal static string Display(Keys keys)
        {
            if (keys == Keys.None) return "未设置";
            return ((keys & Keys.Control) != 0 ? "Ctrl+" : "") + ((keys & Keys.Alt) != 0 ? "Alt+" : "") +
                ((keys & Keys.Shift) != 0 ? "Shift+" : "") + (keys & Keys.KeyCode);
        }
    }

    internal sealed class HotkeyBindings
    {
        private readonly Func<int, Keys, bool> register;
        private readonly Action<int> unregister;
        private readonly Dictionary<int, Keys> active = new Dictionary<int, Keys>();
        internal HotkeyBindings(Func<int, Keys, bool> register, Action<int> unregister) { this.register = register; this.unregister = unregister; }
        internal string RegisterInitial(Keys[] keys)
        {
            Clear(); var conflicts = new List<string>();
            for (int i = 0; i < keys.Length; i++) if (keys[i] != Keys.None)
            { if (register(i + 1, keys[i])) active.Add(i + 1, keys[i]); else conflicts.Add(Shortcut.Display(keys[i])); }
            return string.Join("、", conflicts.ToArray());
        }
        internal bool TryApply(Keys[] keys, out string error)
        { return TryApply(keys, null, out error); }
        internal bool TryApply(Keys[] keys, Action persist, out string error)
        {
            var previous = new Dictionary<int, Keys>(active); Clear();
            for (int i = 0; i < keys.Length; i++) if (keys[i] != Keys.None)
            {
                if (register(i + 1, keys[i])) { active.Add(i + 1, keys[i]); continue; }
                Restore(previous);
                error = Shortcut.Display(keys[i]) + " 已被其他程序占用，请换一个快捷键。"; return false;
            }
            try { if (persist != null) persist(); }
            catch (Exception ex) { Restore(previous); error = "设置未保存：" + ex.Message; return false; }
            error = null; return true;
        }
        private void Restore(Dictionary<int, Keys> previous)
        { Clear(); foreach (var item in previous) if (register(item.Key, item.Value)) active.Add(item.Key, item.Value); }
        internal void Clear() { foreach (int id in active.Keys) unregister(id); active.Clear(); }
    }
}
