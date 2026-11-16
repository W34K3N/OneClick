using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinSetup
{
    // ------------------------------------------------------------ палитра
    //
    // Тёмно-фиолетовая тема: градиентный сайдбар, карточки, кольцевые прогрессы.

    public static class Theme
    {
        // ------------------------------------------------------------ DPI
        //
        // Процесс объявлен DPI-aware (см. Program.EnableDpiAwareness), поэтому Windows
        // больше не растягивает окно растром: шрифты масштабируются сами (они в пунктах),
        // а все пиксельные размеры раскладки и отрисовки прогоняются через S().
        // Scale = 1.0 при 100 %, 1.5 при 150 %, 2.0 при 200 %.

        public static float Scale = 1f;

        public static int S(int px)
        {
            if (Scale == 1f) return px;
            return (int)Math.Round(px * Scale);
        }

        public static float Sf(float px)
        {
            return px * Scale;
        }

        // коэффициент масштаба текущего экрана
        public static void InitScale()
        {
            float s = 1f;
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    s = g.DpiX / 96f;
            }
            catch { }
            if (s <= 0f) s = 1f;
            if (s < 1f) s = 1f;
            Scale = s;
        }

        public static Color Bg = Color.FromArgb(21, 10, 43);
        public static Color BgDark = Color.FromArgb(13, 6, 28);
        public static Color SideTop = Color.FromArgb(139, 70, 255);
        public static Color SideBottom = Color.FromArgb(58, 18, 122);
        public static Color Card = Color.FromArgb(36, 16, 70);
        public static Color CardHi = Color.FromArgb(48, 23, 92);
        public static Color Border = Color.FromArgb(66, 35, 118);
        public static Color GridLine = Color.FromArgb(54, 28, 96);
        public static Color Text = Color.FromArgb(241, 233, 255);
        public static Color TextDim = Color.FromArgb(174, 148, 220);
        public static Color TextFaint = Color.FromArgb(126, 100, 176);
        public static Color Accent1 = Color.FromArgb(34, 211, 238);   // cyan
        public static Color Accent2 = Color.FromArgb(168, 85, 247);   // violet
        public static Color Accent3 = Color.FromArgb(251, 146, 60);   // orange
        public static Color Accent4 = Color.FromArgb(244, 114, 182);  // pink
        public static Color Green = Color.FromArgb(52, 211, 153);
        public static Color Red = Color.FromArgb(248, 113, 113);

        public static Color Lighten(Color c)
        {
            return Lighten(c, 60);
        }

        public static Color Lighten(Color c, int d)
        {
            int r = c.R + d; if (r > 255) r = 255;
            int g = c.G + d; if (g > 255) g = 255;
            int b = c.B + d; if (b > 255) b = 255;
            return Color.FromArgb(c.A, r, g, b);
        }

        public static Font F(float size, FontStyle st)
        {
            return new Font("Segoe UI", size, st);
        }

        public static GraphicsPath Round(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = rad * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            if (d < 2)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Rectangle r, int rad, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Round(r, rad))
            using (SolidBrush b = new SolidBrush(c))
                g.FillPath(b, p);
        }

        public static void DrawRound(Graphics g, Rectangle r, int rad, Color c, float width)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Round(r, rad))
            using (Pen pen = new Pen(c, width))
                g.DrawPath(pen, p);
        }

        public static void FillGradient(Graphics g, Rectangle r, Color a, Color b, float angle)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (LinearGradientBrush br = new LinearGradientBrush(r, a, b, angle))
                g.FillRectangle(br, r);
        }

        public static void FillGradientRound(Graphics g, Rectangle r, int rad, Color a, Color b, float angle)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (GraphicsPath p = Round(r, rad))
            using (LinearGradientBrush br = new LinearGradientBrush(r, a, b, angle))
                g.FillPath(br, p);
        }

        public static void Draw(Graphics g, string s, Font f, Rectangle r, Color c, TextFormatFlags flags)
        {
            if (s == null) return;
            TextRenderer.DrawText(g, s, f, r, c, flags | TextFormatFlags.NoPadding);
        }
    }
}
