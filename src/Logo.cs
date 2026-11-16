using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace WinSetup
{
    // ------------------------------------------------------------ логотип OneClick
    //
    // Знак: скруглённый квадрат с градиентом (фиолет -> циан) и белый курсор-клик.
    // Рисуется программно, поэтому одинаково выглядит и в окне, и в иконке exe.

    public static class Logo
    {
        public const string Name = "OneClick";
        public const string Version = "1.0";

        public static void Draw(Graphics g, Rectangle r)
        {
            if (r.Width < 4 || r.Height < 4) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int rad = (int)(r.Width / 4.2f);
            if (rad < 2) rad = 2;

            Theme.FillGradientRound(g, r, rad, Theme.Accent2, Theme.Accent1, 45f);

            // курсор
            PointF[] cur = new PointF[]
            {
                P(r, 0.34f, 0.17f),
                P(r, 0.34f, 0.79f),
                P(r, 0.47f, 0.65f),
                P(r, 0.56f, 0.86f),
                P(r, 0.66f, 0.81f),
                P(r, 0.57f, 0.61f),
                P(r, 0.73f, 0.59f)
            };

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddPolygon(cur);
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(45, 20, 4, 45)))
                {
                    GraphicsState st = g.Save();
                    g.TranslateTransform(r.Width * 0.012f, r.Width * 0.014f);
                    g.FillPath(shadow, path);
                    g.Restore(st);
                }
                using (SolidBrush white = new SolidBrush(Color.White))
                    g.FillPath(white, path);
                using (Pen edge = new Pen(Color.FromArgb(70, 90, 40, 150), Math.Max(1f, r.Width * 0.012f)))
                    g.DrawPath(edge, path);
            }

            // «щелчок»: две дуги справа-снизу, только на крупных размерах
            if (r.Width >= 40)
            {
                float cx = r.X + r.Width * 0.70f;
                float cy = r.Y + r.Height * 0.74f;
                float w = r.Width * 0.26f;
                using (Pen p = new Pen(Color.FromArgb(140, 255, 255, 255), Math.Max(1.2f, r.Width * 0.028f)))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    g.DrawArc(p, cx - w / 2, cy - w / 2, w, w, 12f, 70f);
                    float w2 = r.Width * 0.42f;
                    using (Pen p2 = new Pen(Color.FromArgb(85, 255, 255, 255), Math.Max(1f, r.Width * 0.022f)))
                    {
                        p2.StartCap = LineCap.Round;
                        p2.EndCap = LineCap.Round;
                        g.DrawArc(p2, cx - w2 / 2, cy - w2 / 2, w2, w2, 10f, 66f);
                    }
                }
            }
        }

        static PointF P(Rectangle r, float x, float y)
        {
            float cx = 0.53f, cy = 0.50f, s = 0.94f;
            return new PointF(r.X + r.Width * (cx + (x - cx) * s),
                              r.Y + r.Height * (cy + (y - cy) * s));
        }

        public static Bitmap Render(int size)
        {
            Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.Clear(Color.Transparent);
                Draw(g, new Rectangle(0, 0, size, size));
            }
            return bmp;
        }

        public static Icon WindowIcon()
        {
            try
            {
                using (Bitmap bmp = Render(32))
                    return Icon.FromHandle(bmp.GetHicon());
            }
            catch { return null; }
        }

        // .ico с PNG-кадрами (Vista+/Win10); 0 в ширине означает 256
        public static void SaveIco(string path, int[] sizes)
        {
            if (sizes == null || sizes.Length == 0) sizes = new int[] { 16, 32, 48, 64, 128, 256 };

            byte[][] frames = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
            {
                using (Bitmap bmp = Render(sizes[i]))
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    frames[i] = ms.ToArray();
                }
            }

            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (BinaryWriter w = new BinaryWriter(fs))
            {
                w.Write((ushort)0);            // reserved
                w.Write((ushort)1);            // type = icon
                w.Write((ushort)sizes.Length); // count

                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0);          // palette
                    w.Write((byte)0);          // reserved
                    w.Write((ushort)1);        // planes
                    w.Write((ushort)32);       // bpp
                    w.Write((uint)frames[i].Length);
                    w.Write((uint)offset);
                    offset += frames[i].Length;
                }

                for (int i = 0; i < frames.Length; i++) w.Write(frames[i]);
            }
        }
    }
}
