using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace ShotInk
{
    internal enum SkinKind { Classic, Linear, Duotone, Clay, Glass, Neon, Pixel, Ink }

    internal sealed class SkinPalette
    {
        internal readonly SkinKind Kind;
        internal readonly string Name;
        internal readonly Color Background, Surface, Ink, Muted, Border, Accent, Secondary;
        internal bool Dark { get { return Kind == SkinKind.Glass || Kind == SkinKind.Neon; } }
        internal SkinPalette(SkinKind kind, string name, int background, int surface, int ink, int muted, int border, int accent, int secondary)
        {
            Kind = kind; Name = name;
            Background = Color.FromArgb(background | unchecked((int)0xff000000));
            Surface = Color.FromArgb(surface | unchecked((int)0xff000000));
            Ink = Color.FromArgb(ink | unchecked((int)0xff000000));
            Muted = Color.FromArgb(muted | unchecked((int)0xff000000));
            Border = Color.FromArgb(border | unchecked((int)0xff000000));
            Accent = Color.FromArgb(accent | unchecked((int)0xff000000));
            Secondary = Color.FromArgb(secondary | unchecked((int)0xff000000));
        }
    }

    internal static class Skins
    {
        internal static readonly SkinPalette[] All = {
            new SkinPalette(SkinKind.Classic, "经典", 0xf5f7fb, 0xffffff, 0x313948, 0x707989, 0xe2e6ed, 0x525fef, 0x7566e6),
            new SkinPalette(SkinKind.Linear, "极简线性", 0xf8fafc, 0xffffff, 0x252b36, 0x657084, 0xdce2eb, 0x3478e8, 0x818cf8),
            new SkinPalette(SkinKind.Duotone, "双色扁平", 0xedf3ff, 0xfafcff, 0x25266b, 0x666b91, 0xcbd8f1, 0x426bda, 0x829aff),
            new SkinPalette(SkinKind.Clay, "柔和立体", 0xfaf5ef, 0xfffcf8, 0x493b38, 0x85756e, 0xe4d6ca, 0xcf6556, 0x8e83cf),
            new SkinPalette(SkinKind.Glass, "磨砂玻璃", 0x10192f, 0x202e49, 0xe9f1ff, 0xaabbd5, 0x536780, 0x79cce9, 0xb5a1ee),
            new SkinPalette(SkinKind.Neon, "暗色霓虹", 0x0b1017, 0x151e29, 0xecf9fc, 0x9eafb8, 0x40505f, 0x62f0ef, 0xbe91f2),
            new SkinPalette(SkinKind.Pixel, "复古像素", 0xfff7e6, 0xfffbed, 0x17283c, 0x666c70, 0x7d8b88, 0x287e79, 0xc56a59),
            new SkinPalette(SkinKind.Ink, "黑白水墨", 0xf4f3f0, 0xfbfaf7, 0x202020, 0x666666, 0xababaa, 0x303030, 0x777777)
        };
        internal static SkinPalette Current { get; private set; }
        internal static event Action Changed;
        private sealed class ControlColors
        {
            internal Color Background, Foreground;
            internal ControlColors(Control control)
            {
                Background = control.BackColor; Foreground = control.ForeColor;
                if (Current.Kind == SkinKind.Classic) return;
                if (Background == Current.Background) Background = All[0].Background;
                else if (Background == Current.Surface) Background = All[0].Surface;
                if (Foreground == Current.Ink) Foreground = All[0].Ink;
                else if (Foreground == Current.Muted) Foreground = All[0].Muted;
            }
        }
        private static readonly ConditionalWeakTable<Control, ControlColors> colors = new ConditionalWeakTable<Control, ControlColors>();
        static Skins() { Current = All[0]; }
        internal static bool Valid(SkinKind value) { return (int)value >= 0 && (int)value < All.Length; }
        internal static void Select(SkinKind value)
        {
            if (!Valid(value)) value = SkinKind.Classic;
            if (Current.Kind == value) return;
            Current = All[(int)value];
            foreach (Form form in Application.OpenForms) Apply(form);
            if (Changed != null) Changed();
        }
        internal static Color Blend(Color first, Color last, float amount)
        {
            amount = Math.Max(0, Math.Min(1, amount));
            return Color.FromArgb((int)(first.R + (last.R - first.R) * amount), (int)(first.G + (last.G - first.G) * amount), (int)(first.B + (last.B - first.B) * amount));
        }
        internal static Color Accent(Color color)
        {
            switch (Current.Kind)
            {
                case SkinKind.Ink: return Current.Accent;
                case SkinKind.Duotone: return Current.Accent;
                case SkinKind.Glass: case SkinKind.Neon: case SkinKind.Clay: return color;
                default: return color;
            }
        }
        internal static void Attach(Form form)
        {
            form.Shown += delegate { Apply(form); };
        }
        internal static void Apply(Control control)
        {
            // These controls own screenshot pixels, transparency keys or live media.
            if (control is AnnotationCanvas || control is PinForm || control is PictureBox || control is InlineTextInput) return;
            bool overlay = control is CaptureOverlay;
            if (control.Parent is CaptureOverlay && (control is Label || control is Panel && !(control is RecordingSurface))) return;
            var option = control as ToolOptionButton;
            if (option != null && option.IsSwatch) return;
            var original = colors.GetValue(control, delegate(Control item) { return new ControlColors(item); });
            if (!overlay && !(control is HeroPanel))
            {
                bool sidebar = control.Name == "skin-sidebar" || control.Parent != null && control.Parent.Name == "skin-sidebar";
                if (Current.Kind == SkinKind.Classic)
                { control.BackColor = original.Background; control.ForeColor = original.Foreground; }
                else
                {
                    bool transparent = original.Background.A == 0;
                    if (!transparent) control.BackColor = sidebar ? Current.Background : control is Form || control is TableLayoutPanel ? Current.Background : Current.Surface;
                    bool heroText = control.Parent is HeroPanel;
                    control.ForeColor = heroText ? Color.White : sidebar ? Current.Ink : Foreground(original.Foreground);
                }
            }
            foreach (Control child in control.Controls) Apply(child);
            var strip = control as ToolStrip;
            if (strip != null) { strip.BackColor = Current.Surface; strip.ForeColor = Current.Ink; }
            control.Invalidate(true);
        }
        private static Color Foreground(Color color)
        {
            if (color.R == 255 && color.G == 255 && color.B == 255) return Color.White;
            if (Current.Kind == SkinKind.Ink) return color.GetBrightness() > .45F ? Current.Muted : Current.Ink;
            if (color.R > color.G * 1.3 && color.R > color.B * 1.3) return Current.Dark ? Color.LightCoral : Color.Firebrick;
            return color.GetBrightness() > .38F ? Current.Muted : Current.Ink;
        }
    }

    // Each control owns its short-lived animation and releases its timer on disposal.
    internal sealed class SkinInteraction : IDisposable
    {
        private readonly Control owner;
        private readonly Timer timer = new Timer { Interval = 16 };
        private int elapsed;
        internal bool Hot, Pressed;
        internal float Pulse { get { return Math.Max(0, 1 - elapsed / 360F); } }
        internal SkinInteraction(Control owner)
        {
            this.owner = owner; elapsed = 360;
            owner.MouseEnter += delegate { Hot = true; Animate(); };
            owner.MouseLeave += delegate { Hot = false; Pressed = false; Animate(); };
            owner.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Pressed = true; Animate(); } };
            owner.MouseUp += delegate { Pressed = false; Animate(); };
            owner.MouseCaptureChanged += delegate { if (!owner.Capture) { Pressed = false; owner.Invalidate(); } };
            owner.GotFocus += delegate { Animate(); };
            owner.LostFocus += delegate { Pressed = false; owner.Invalidate(); };
            owner.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { Pressed = true; Animate(); } };
            owner.KeyUp += delegate { Pressed = false; Animate(); };
            owner.EnabledChanged += delegate { Hot = Pressed = false; timer.Stop(); elapsed = 360; owner.Invalidate(); };
            owner.Disposed += delegate { Dispose(); };
            timer.Tick += delegate { elapsed += timer.Interval; owner.Invalidate(); if (elapsed >= 360) timer.Stop(); };
        }
        private void Animate() { if (!owner.Enabled) return; elapsed = 0; timer.Start(); owner.Invalidate(); }
        public void Dispose() { timer.Dispose(); }
    }

    internal static class SkinPainter
    {
        internal static GraphicsPath Shape(RectangleF box, float radius)
        {
            if (Skins.Current.Kind != SkinKind.Pixel) return ToolAppearance.Rounded(box, radius);
            var path = new GraphicsPath();
            const float step = 2;
            path.AddPolygon(new[] { new PointF(box.Left + step, box.Top), new PointF(box.Right - step, box.Top), new PointF(box.Right - step, box.Top + step), new PointF(box.Right, box.Top + step), new PointF(box.Right, box.Bottom - step), new PointF(box.Right - step, box.Bottom - step), new PointF(box.Right - step, box.Bottom), new PointF(box.Left + step, box.Bottom), new PointF(box.Left + step, box.Bottom - step), new PointF(box.Left, box.Bottom - step), new PointF(box.Left, box.Top + step), new PointF(box.Left + step, box.Top + step) });
            return path;
        }
        internal static void Surface(Graphics g, RectangleF bounds, float radius)
        {
            using (var path = Shape(bounds, radius))
            using (var fill = new SolidBrush(Skins.Current.Surface))
            using (var border = new Pen(Skins.Current.Border))
            {
                g.FillPath(fill, path);
                if (Skins.Current.Kind == SkinKind.Clay || Skins.Current.Kind == SkinKind.Glass)
                {
                    using (var light = new LinearGradientBrush(bounds, Color.FromArgb(45, Color.White), Color.FromArgb(12, Color.Black), 90)) g.FillPath(light, path);
                }
                if (Skins.Current.Kind == SkinKind.Ink) InkTexture(g, bounds, .12F);
                g.DrawPath(border, path);
            }
        }
        internal static void Button(Graphics g, RectangleF box, Color accent, bool selected, bool hot, bool pressed, bool focused, float pulse)
        {
            var skin = Skins.Current;
            accent = Skins.Accent(accent);
            Color face = selected ? Skins.Blend(skin.Surface, accent, skin.Dark ? .3F : .12F) : hot ? Skins.Blend(skin.Surface, accent, .07F) : skin.Surface;
            if (pressed) face = Skins.Blend(face, accent, .24F);
            using (var path = Shape(box, 6))
            using (var fill = new SolidBrush(face))
            using (var rim = new Pen(selected || focused ? accent : skin.Border, selected || focused ? 1.5F : 1))
            {
                g.FillPath(fill, path);
                switch (skin.Kind)
                {
                    case SkinKind.Duotone:
                        if (hot || selected || pressed) using (var wash = new SolidBrush(Color.FromArgb((int)(20 + pulse * 25), accent))) g.FillPath(wash, path);
                        break;
                    case SkinKind.Clay:
                    case SkinKind.Glass:
                        using (var light = new LinearGradientBrush(box, Color.FromArgb(pressed ? 8 : skin.Kind == SkinKind.Glass ? 24 : 70, Color.White), Color.FromArgb(pressed ? 36 : 8, Color.Black), 90)) g.FillPath(light, path);
                        if (skin.Kind == SkinKind.Glass && (hot || pressed))
                        {
                            var state = g.Save(); g.SetClip(path);
                            float shineX = box.Left - 6 + (1 - pulse) * (box.Width + 18);
                            using (var shine = new Pen(Color.FromArgb((int)(40 + 70 * pulse), Color.White), 3)) g.DrawLine(shine, shineX, box.Top, shineX - 8, box.Bottom);
                            g.Restore(state);
                        }
                        break;
                    case SkinKind.Neon:
                        if (hot || selected || pressed)
                        {
                            for (int width = 5; width >= 2; width--) using (var glow = new Pen(Color.FromArgb((int)(12 + 16 * pulse), accent), width)) g.DrawPath(glow, path);
                            rim.Color = selected ? Color.FromArgb(191, 241, 74) : accent;
                        }
                        break;
                    case SkinKind.Pixel:
                        if (hot || pressed) using (var shade = new SolidBrush(Skins.Blend(face, accent, pressed ? .26F : .12F))) g.FillPath(shade, path);
                        rim.Width = 2;
                        break;
                    case SkinKind.Ink:
                        InkTexture(g, box, hot || selected || pressed ? .3F + pulse * .5F : .08F);
                        if ((hot || pressed) && pulse > 0)
                            using (var wash = new SolidBrush(Color.FromArgb((int)(pulse * 22), Color.Black))) g.FillEllipse(wash, box.X + box.Width * pulse * .2F, box.Y + 2, box.Width * (1 - pulse * .2F), box.Height - 4);
                        break;
                }
                g.DrawPath(rim, path);
                if (skin.Kind == SkinKind.Linear && (hot || focused)) using (var line = new Pen(accent, 1 + pulse)) g.DrawLine(line, box.Left + 5, box.Bottom - 2, box.Right - 5, box.Bottom - 2);
            }
        }
        private static void InkTexture(Graphics g, RectangleF box, float strength)
        {
            using (var brush = new SolidBrush(Color.FromArgb((int)(strength * 28), Color.Black)))
                for (int x = 3; x < box.Width - 2; x += 7)
                    for (int y = 3; y < box.Height - 2; y += 9)
                        g.FillRectangle(brush, box.Left + x, box.Top + y + x % 3, 1 + x % 2, 1);
        }
        internal static int PressOffset(bool pressed)
        { return pressed && (Skins.Current.Kind == SkinKind.Clay || Skins.Current.Kind == SkinKind.Pixel) ? 1 : 0; }
    }

    internal sealed class SkinButton : Button
    {
        private readonly SkinInteraction interaction;
        internal bool Primary;
        internal SkinButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand;
            interaction = new SkinInteraction(this);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Skins.Current.Surface : Parent.BackColor);
            ToolAppearance.DrawSmooth(e.Graphics, ClientSize, delegate(Graphics g)
            { SkinPainter.Button(g, new RectangleF(1, 1, Width - 3, Height - 3), Skins.Current.Accent, Primary, Enabled && interaction.Hot, Enabled && interaction.Pressed, Focused && ShowFocusCues, interaction.Pulse); });
            var bounds = ClientRectangle; bounds.Offset(0, SkinPainter.PressOffset(interaction.Pressed));
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, Enabled ? Skins.Current.Ink : Skins.Current.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
        }
    }
}
