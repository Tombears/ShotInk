using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ShotInk
{
    internal static class ToolCursors
    {
        private const string ResourcePrefix = "ShotInk.Cursors.";
        private static readonly Dictionary<string, Cursor> cursors = new Dictionary<string, Cursor>();
        private static readonly Lazy<byte[]> penDark = new Lazy<byte[]>(delegate { return Read("sample-dark"); });
        private static readonly Lazy<byte[]> penLight = new Lazy<byte[]>(delegate { return Read("sample-light"); });

        internal static Cursor For(DrawTool tool, bool blur = false)
        {
            switch (tool)
            {
                case DrawTool.Rectangle: return Get("rectangle");
                case DrawTool.Ellipse: return Get("ellipse");
                case DrawTool.Arrow: return Get("arrow");
                case DrawTool.Line: return Get("line");
                case DrawTool.Polyline: return Get("polyline");
                case DrawTool.ArrowPolyline: return Get("arrow-polyline");
                case DrawTool.Pen: return Get("pen");
                case DrawTool.Text: return Get("text");
                case DrawTool.Mosaic: return Get(blur ? "blur" : "mosaic");
                case DrawTool.Highlight: return Get("highlight");
                case DrawTool.Eraser: return Get("eraser");
                default: return Get("move");
            }
        }

        internal static Cursor Get(string name)
        {
            Cursor cursor;
            if (!cursors.TryGetValue(name, out cursor))
            {
                using (var stream = typeof(ToolCursors).Assembly.GetManifestResourceStream(ResourcePrefix + name))
                    cursor = new Cursor(stream);
                cursors.Add(name, cursor);
            }
            return cursor;
        }

        private static byte[] Read(string name)
        {
            using (var resource = typeof(ToolCursors).Assembly.GetManifestResourceStream(ResourcePrefix + name))
            using (var buffer = new MemoryStream())
            {
                resource.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        internal static Cursor CreateSamplingPen(Color color)
        {
            // Both 32-bit CUR templates share their silhouette, hotspot, and pixel alpha.
            // Interpolating the color planes leaves the white edge and metal nib intact.
            var dark = penDark.Value;
            var light = penLight.Value;
            var data = (byte[])dark.Clone();
            const int pixels = 6 + 16 + 40; // Directory, frame entry, and DIB header.
            for (int offset = pixels; offset < pixels + 32 * 32 * 4; offset += 4)
            {
                data[offset] = Tint(dark[offset], light[offset], color.B);
                data[offset + 1] = Tint(dark[offset + 1], light[offset + 1], color.G);
                data[offset + 2] = Tint(dark[offset + 2], light[offset + 2], color.R);
            }
            using (var stream = new MemoryStream(data, false)) return new Cursor(stream);
        }

        private static byte Tint(int dark, int light, int value)
        { return (byte)((dark * (255 - value) + light * value + 127) / 255); }
    }
}
