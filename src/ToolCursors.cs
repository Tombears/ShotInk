using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace ShotInk
{
    internal static class ToolCursors
    {
        private const string ResourcePrefix = "ShotInk.Cursors.";
        private static readonly Dictionary<string, NativeCursor> cursors = new Dictionary<string, NativeCursor>();
        private static readonly Lazy<byte[][]> penDark = new Lazy<byte[][]>(delegate { return ReadSkinFrames("sample-dark"); });
        private static readonly Lazy<byte[][]> penLight = new Lazy<byte[][]>(delegate { return ReadSkinFrames("sample-light"); });

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
            var skin = Skins.Current.Kind;
            string key = skin + "/" + name;
            NativeCursor cursor;
            if (!cursors.TryGetValue(key, out cursor))
            {
                cursor = Load(ReadFrame(Read(name), skin));
                cursors.Add(key, cursor);
            }
            return cursor.View;
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

        private static byte[][] ReadSkinFrames(string name)
        {
            var data = Read(name);
            var frames = new byte[Skins.All.Length][];
            foreach (var skin in Skins.All) frames[(int)skin.Kind] = ReadFrame(data, skin.Kind);
            return frames;
        }

        private static byte[] ReadFrame(byte[] data, SkinKind skin)
        {
            // The cursor directory lists one 32 px frame per SkinKind in enum order.
            int entry = 6 + 16 * (int)skin;
            int size = BitConverter.ToInt32(data, entry + 8);
            int offset = BitConverter.ToInt32(data, entry + 12);
            var resource = new byte[4 + size];
            // Win32 RT_CURSOR resources begin with the hotspot, followed by the DIB.
            Buffer.BlockCopy(data, entry + 4, resource, 0, 4);
            Buffer.BlockCopy(data, offset, resource, 4, size);
            return resource;
        }

        internal static NativeCursor CreateSamplingPen(Color color)
        {
            // Both 32-bit CUR templates share their silhouette, hotspot, and pixel alpha.
            // Interpolating the color planes leaves the white edge and metal nib intact.
            int skin = (int)Skins.Current.Kind;
            var dark = penDark.Value[skin];
            var light = penLight.Value[skin];
            var data = (byte[])dark.Clone();
            const int pixels = 4 + 40; // Hotspot and DIB header precede the BGRA pixels.
            for (int offset = pixels; offset < pixels + 32 * 32 * 4; offset += 4)
            {
                data[offset] = Tint(dark[offset], light[offset], color.B);
                data[offset + 1] = Tint(dark[offset + 1], light[offset + 1], color.G);
                data[offset + 2] = Tint(dark[offset + 2], light[offset + 2], color.R);
            }
            return Load(data);
        }

        private static NativeCursor Load(byte[] resource)
        {
            // The WinForms stream constructor converts color CUR files to monochrome.
            var handle = CreateIconFromResourceEx(resource, resource.Length, false, 0x00030000, 0, 0, 0);
            if (handle == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            return new NativeCursor(handle);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CreateIconFromResourceEx(byte[] bits, int size, bool isIcon, uint version,
            int width, int height, uint flags);
        [DllImport("user32.dll")]
        private static extern bool DestroyCursor(IntPtr handle);

        internal sealed class NativeCursor : SafeHandleZeroOrMinusOneIsInvalid
        {
            internal readonly Cursor View;
            internal NativeCursor(IntPtr cursor) : base(true)
            {
                SetHandle(cursor);
                View = new Cursor(cursor);
            }

            protected override bool ReleaseHandle() { return DestroyCursor(handle); }
        }

        private static byte Tint(int dark, int light, int value)
        { return (byte)((dark * (255 - value) + light * value + 127) / 255); }
    }
}
