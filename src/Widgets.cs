using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinSetup
{
    // ------------------------------------------------------------ фон с градиентом

    public class BgPanel : Panel
    {
        public Color TopColor = Theme.Bg;
        public Color BottomColor = Theme.BgDark;
        public bool Glows = false;

        public BgPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // полностью рисуем сами — без мерцания
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillGradient(g, ClientRectangle, TopColor, BottomColor, 90f);

            if (Glows)
            {
                int w = ClientSize.Width, h = ClientSize.Height;
                int d = Theme.S(620);
                using (SolidBrush b1 = new SolidBrush(Color.FromArgb(26, Theme.Accent2)))
                    g.FillEllipse(b1, new Rectangle(Theme.S(-160), Theme.S(-260), d, d));
                using (SolidBrush b2 = new SolidBrush(Color.FromArgb(20, Theme.Accent1)))
                    g.FillEllipse(b2, new Rectangle(w - Theme.S(420), h - Theme.S(320), d, d));
                using (SolidBrush b3 = new SolidBrush(Color.FromArgb(18, Theme.Accent4)))
                    g.FillEllipse(b3, new Rectangle(w / 2 - Theme.S(200), h / 2 - Theme.S(200),
                                                   Theme.S(400), Theme.S(400)));
            }

            base.OnPaint(e);
        }
    }

    // ------------------------------------------------------------ карточка

    public class Card : Panel
    {
        public string Header = "";
        public string HeaderRight = "";
        public Color Accent = Theme.Accent2;
        public int Radius = Theme.S(16);
        public Control Content;          // содержимое карточки (для подсказки при пустоте)
        public string EmptyHint = "";    // текст-заглушка, пока Content пуст

        public Card()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        public int ContentTop
        {
            get { return (Header != null && Header.Length > 0) ? Theme.S(48) : Theme.S(16); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Radius, Theme.Card);
            Theme.DrawRound(g, r, Radius, Theme.Border, 1f);

            if (Header == null || Header.Length == 0) return;

            Theme.FillRound(g, new Rectangle(Theme.S(18), Theme.S(18), Theme.S(4), Theme.S(14)),
                            Math.Max(1, Theme.S(2)), Accent);
            Theme.Draw(g, Header, Theme.F(8.5f, FontStyle.Bold),
                       new Rectangle(Theme.S(32), Theme.S(15), Width - Theme.S(120), Theme.S(20)),
                       Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            if (HeaderRight != null && HeaderRight.Length > 0)
                Theme.Draw(g, HeaderRight, Theme.F(8f, FontStyle.Regular),
                           new Rectangle(Width - Theme.S(220), Theme.S(15), Theme.S(198), Theme.S(20)),
                           Theme.TextFaint, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            if (EmptyHint != null && EmptyHint.Length > 0)
            {
                bool empty = true;
                if (Content != null)
                {
                    TextBox tb = Content as TextBox;
                    empty = tb != null ? tb.TextLength == 0 : Content.Text.Length == 0;
                }
                if (empty)
                    Theme.Draw(g, EmptyHint, Theme.F(8.5f, FontStyle.Regular),
                               new Rectangle(Theme.S(20), ContentTop + Theme.S(4),
                                             Width - Theme.S(40), Height - ContentTop - Theme.S(20)),
                               Theme.TextFaint, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
            }
        }
    }

    // ------------------------------------------------------------ кольцо с числом

    public class RingCard : Control
    {
        public string Title = "ПОКАЗАТЕЛЬ";
        public string Caption = "0";
        public Color Accent = Theme.Accent1;
        public int Value = 0;

        public RingCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = Theme.S(130);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int rad0 = Theme.S(14);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, rad0, Theme.Card);
            Theme.DrawRound(g, r, rad0, Theme.Border, 1f);

            Theme.Draw(g, Title, Theme.F(7.5f, FontStyle.Bold),
                       new Rectangle(0, Theme.S(9), Width, Theme.S(16)),
                       Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            int cx = Width / 2;
            int cy = Theme.S(62);
            int rad = Theme.S(26);
            Rectangle ring = new Rectangle(cx - rad, cy - rad, rad * 2, rad * 2);

            using (Pen track = new Pen(Color.FromArgb(58, 33, 102), Theme.Sf(6f)))
                g.DrawEllipse(track, ring);

            if (Value > 0)
            {
                Rectangle gr = new Rectangle(ring.X - 2, ring.Y - 2, ring.Width + 4, ring.Height + 4);
                using (LinearGradientBrush br = new LinearGradientBrush(gr, Theme.Lighten(Accent, 80), Accent, 90f))
                using (Pen pen = new Pen(br, Theme.Sf(6f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, ring, -90f, 360f * Value / 100f);
                }
            }

            Theme.Draw(g, Value + "%", Theme.F(10.5f, FontStyle.Bold),
                       new Rectangle(ring.X - Theme.S(12), ring.Y, ring.Width + Theme.S(24), ring.Height),
                       Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            Rectangle pill = new Rectangle(Theme.S(14), Height - Theme.S(32), Width - Theme.S(28), Theme.S(20));
            Theme.FillGradientRound(g, pill, Theme.S(8), Theme.Lighten(Accent, 70), Accent, 0f);
            Theme.Draw(g, Caption, Theme.F(8f, FontStyle.Bold), pill, Color.FromArgb(24, 8, 46),
                       TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ------------------------------------------------------------ большое кольцо прогресса

    public class BigRing : Control
    {
        public int Value = 0;
        public string Current = "готов к работе";
        public string Title = "ПРОГРЕСС";
        public Color Accent = Theme.Accent2;

        public BigRing()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Theme.S(16), Theme.Card);
            Theme.DrawRound(g, r, Theme.S(16), Theme.Border, 1f);

            Theme.FillRound(g, new Rectangle(Theme.S(18), Theme.S(18), Theme.S(4), Theme.S(14)),
                            Math.Max(1, Theme.S(2)), Accent);
            Theme.Draw(g, Title, Theme.F(8.5f, FontStyle.Bold),
                       new Rectangle(Theme.S(32), Theme.S(15), Width - Theme.S(60), Theme.S(20)),
                       Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            int cx = Width / 2;
            int cy = Theme.S(96);
            int rad = Theme.S(44);
            Rectangle ring = new Rectangle(cx - rad, cy - rad, rad * 2, rad * 2);

            using (Pen track = new Pen(Color.FromArgb(58, 33, 102), Theme.Sf(10f)))
                g.DrawEllipse(track, ring);

            if (Value > 0)
            {
                Rectangle gr = new Rectangle(ring.X - 3, ring.Y - 3, ring.Width + 6, ring.Height + 6);
                using (LinearGradientBrush br = new LinearGradientBrush(gr, Theme.Accent1, Theme.Accent2, 135f))
                using (Pen pen = new Pen(br, Theme.Sf(10f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, ring, -90f, 360f * Value / 100f);
                }
            }

            Theme.Draw(g, Value + "%", Theme.F(19f, FontStyle.Bold),
                       new Rectangle(ring.X - Theme.S(24), ring.Y, ring.Width + Theme.S(48), ring.Height),
                       Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            Rectangle sub = new Rectangle(ring.X - Theme.S(60), cy + rad + Theme.S(8),
                                          ring.Width + Theme.S(120), Theme.S(18));
            Theme.Draw(g, "ВЫПОЛНЕНО", Theme.F(7f, FontStyle.Bold), sub,
                       Theme.TextFaint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            Rectangle cur = new Rectangle(Theme.S(18), cy + rad + Theme.S(30), Width - Theme.S(36),
                                          Height - (cy + rad + Theme.S(30)) - Theme.S(12));
            Theme.Draw(g, Current, Theme.F(9f, FontStyle.Regular), cur,
                       Theme.TextDim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                     TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
    }

    // ------------------------------------------------------------ полоски по этапам

    public class BarsCard : Control
    {
        public string Title = "СОСТОЯНИЕ";
        public string[] Labels = new string[] { "Этап 1", "Этап 2", "Этап 3" };
        public int[] Values = new int[] { 0, 0, 0 };
        public Color[] Colors = new Color[] { Theme.Accent1, Theme.Accent3, Theme.Accent4 };
        public Color Accent = Theme.Accent1;
        public int Max = 0;                 // 0 = по максимальному значению

        public BarsCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Theme.S(16), Theme.Card);
            Theme.DrawRound(g, r, Theme.S(16), Theme.Border, 1f);

            Theme.FillRound(g, new Rectangle(Theme.S(18), Theme.S(18), Theme.S(4), Theme.S(14)),
                            Math.Max(1, Theme.S(2)), Accent);
            Theme.Draw(g, Title, Theme.F(8.5f, FontStyle.Bold),
                       new Rectangle(Theme.S(32), Theme.S(15), Width - Theme.S(60), Theme.S(20)),
                       Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            int max = 1;
            if (Max > 0) max = Max;
            else for (int i = 0; i < Values.Length; i++) if (Values[i] > max) max = Values[i];

            int y = Theme.S(52);
            int rowH = Theme.S(32);
            int barX = Theme.S(156);
            if (barX > Width / 2) barX = Width / 3;
            int barW = Width - barX - Theme.S(116);
            if (barW < Theme.S(40)) barW = Theme.S(40);
            int barH = Theme.S(8);
            int midShift = rowH / 2 - barH / 2;

            for (int i = 0; i < Labels.Length; i++)
            {
                if (y + rowH > Height - Theme.S(8)) break;
                Theme.Draw(g, Labels[i], Theme.F(8f, FontStyle.Regular),
                           new Rectangle(Theme.S(18), y, barX - Theme.S(30), rowH), Theme.TextDim,
                           TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                Rectangle track = new Rectangle(barX, y + midShift, barW, barH);
                Theme.FillRound(g, track, Math.Max(1, Theme.S(4)), Color.FromArgb(52, 29, 92));

                int v = i < Values.Length ? Values[i] : 0;
                int w = (int)((long)barW * v / max);
                if (w < Theme.S(4) && v > 0) w = Theme.S(4);
                if (w > 0)
                {
                    Rectangle fill = new Rectangle(barX, y + midShift, w, barH);
                    Theme.FillGradientRound(g, fill, Math.Max(1, Theme.S(4)),
                                            Theme.Lighten(Colors[i], 70), Colors[i], 0f);
                }

                int numX = Width - Theme.S(108);
                if (numX < barX + barW + Theme.S(8)) numX = barX + barW + Theme.S(8);
                Theme.Draw(g, v.ToString(), Theme.F(8.5f, FontStyle.Bold),
                           new Rectangle(numX, y, Theme.S(46), rowH), Theme.Text,
                           TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                Theme.Draw(g, "/ " + max, Theme.F(7.5f, FontStyle.Regular),
                           new Rectangle(numX + Theme.S(50), y, Theme.S(46), rowH), Theme.TextFaint,
                           TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                y += rowH;
            }
        }
    }

    // ------------------------------------------------------------ карточка-показатель (текст слева, кольцо справа)

    public class StatCard : Control
    {
        public string Title = "ПОКАЗАТЕЛЬ";
        public string Sub = "";
        public int Value = 0;
        public Color Accent = Theme.Accent1;

        public StatCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = Theme.S(112);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.FillRound(g, r, Theme.S(16), Theme.Card);
            Theme.DrawRound(g, r, Theme.S(16), Theme.Border, 1f);

            int textW = Width - Theme.S(100);
            if (textW < Theme.S(60)) textW = Theme.S(60);

            Theme.Draw(g, Title, Theme.F(8f, FontStyle.Bold),
                       new Rectangle(Theme.S(18), Theme.S(16), textW, Theme.S(16)),
                       Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, Sub, Theme.F(7.2f, FontStyle.Regular),
                       new Rectangle(Theme.S(18), Theme.S(36), textW, Theme.S(46)), Theme.TextDim,
                       TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);

            int rad = Theme.S(28);
            int cx = Width - Theme.S(48);
            int cy = Height / 2;
            Rectangle ring = new Rectangle(cx - rad, cy - rad, rad * 2, rad * 2);

            using (Pen track = new Pen(Color.FromArgb(58, 33, 102), Theme.Sf(6f)))
                g.DrawEllipse(track, ring);

            if (Value > 0)
            {
                Rectangle gr = new Rectangle(ring.X - 2, ring.Y - 2, ring.Width + 4, ring.Height + 4);
                using (LinearGradientBrush br = new LinearGradientBrush(gr, Theme.Lighten(Accent, 60), Accent, 90f))
                using (Pen pen = new Pen(br, Theme.Sf(6f)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, ring, -90f, 360f * Value / 100f);
                }
            }

            Theme.Draw(g, Value + "%", Theme.F(10.5f, FontStyle.Bold),
                       new Rectangle(ring.X - Theme.S(10), ring.Y, ring.Width + Theme.S(20), ring.Height),
                       Theme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ------------------------------------------------------------ кнопка-строка в сайдбаре

    public class NavButton : Control
    {
        public bool Active = false;
        public bool Pill = false;
        public Color Accent = Theme.Accent2;
        bool hover;

        public NavButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Height = Theme.S(42);
            Cursor = Cursors.Hand;
            Font = Theme.F(8.5f, FontStyle.Bold);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            if (Pill)
            {
                bool hot = hover && Enabled;
                Theme.FillRound(g, r, Theme.S(12), hot ? Color.FromArgb(34, 255, 255, 255) : Color.FromArgb(16, 255, 255, 255));
                Theme.DrawRound(g, r, Theme.S(12), Color.FromArgb(hot ? 90 : 48, 255, 255, 255), 1f);
                Color cp = Enabled ? (hot ? Color.White : Theme.TextDim) : Theme.TextFaint;
                Theme.Draw(g, Text + "   >", Theme.F(8.5f, FontStyle.Regular), r, cp,
                           TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                           TextFormatFlags.EndEllipsis);
                return;
            }

            if (Active)
            {
                Theme.FillRound(g, r, Theme.S(10), Color.FromArgb(46, 255, 255, 255));
                Theme.FillRound(g, new Rectangle(0, Theme.S(8), Math.Max(2, Theme.S(3)), Height - Theme.S(16)),
                                Math.Max(1, Theme.S(2)), Theme.Accent1);
            }
            else if (hover && Enabled)
            {
                Theme.FillRound(g, r, Theme.S(10), Color.FromArgb(28, 255, 255, 255));
            }

            Color c = Enabled ? (Active ? Color.White : Color.FromArgb(232, 222, 250)) : Color.FromArgb(120, 100, 160);
            Theme.Draw(g, Text, Font, new Rectangle(Theme.S(16), 0, Width - Theme.S(24), Height), c,
                       TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    // ------------------------------------------------------------ большая кнопка выбора (стартовый экран)

    public class ChoiceCard : Control
    {
        public string Title = "";
        public string Body = "";
        public string Tag = "";
        public string[] Steps = new string[0];
        public Color From = Theme.Accent2;
        public Color To = Theme.Accent1;
        bool hover;

        public ChoiceCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int shrink = (hover && Enabled) ? 0 : Theme.S(2);
            Rectangle r = new Rectangle(shrink, shrink, Width - 1 - shrink * 2, Height - 1 - shrink * 2);

            Theme.FillGradientRound(g, r, Theme.S(18), Theme.Card, Theme.CardHi, 90f);
            using (GraphicsPath path = Theme.Round(r, Theme.S(18)))
            using (LinearGradientBrush br = new LinearGradientBrush(
                       new Rectangle(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4),
                       Enabled ? Theme.Lighten(From, 30) : Color.FromArgb(70, 55, 105),
                       Enabled ? To : Color.FromArgb(55, 42, 85), 45f))
            using (Pen pen = new Pen(br, Theme.Sf(2f)))
                g.DrawPath(pen, path);

            Color cTag = Enabled ? Theme.Lighten(From, 90) : Theme.TextFaint;
            Color cTitle = Enabled ? Color.White : Theme.TextDim;
            Color cBody = Enabled ? Theme.TextDim : Theme.TextFaint;

            Theme.FillRound(g, new Rectangle(r.X + Theme.S(26), r.Y + Theme.S(28), Theme.S(6), Theme.S(6)),
                            Theme.S(3), Enabled ? From : Theme.TextFaint);
            Theme.Draw(g, Tag, Theme.F(7.5f, FontStyle.Bold),
                       new Rectangle(r.X + Theme.S(42), r.Y + Theme.S(22), r.Width - Theme.S(70), Theme.S(18)),
                       cTag, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, Title, Theme.F(16f, FontStyle.Bold),
                       new Rectangle(r.X + Theme.S(26), r.Y + Theme.S(50), r.Width - Theme.S(52), Theme.S(34)),
                       cTitle, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, Body, Theme.F(9f, FontStyle.Regular),
                       new Rectangle(r.X + Theme.S(26), r.Y + Theme.S(92), r.Width - Theme.S(52), Theme.S(40)),
                       cBody, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);

            int sy = r.Y + Theme.S(138);
            for (int i = 0; i < Steps.Length; i++)
            {
                Theme.FillRound(g, new Rectangle(r.X + Theme.S(28), sy + Theme.S(7), Theme.S(5), Theme.S(5)),
                                Theme.S(2), Enabled ? Theme.Accent1 : Theme.TextFaint);
                Theme.Draw(g, Steps[i], Theme.F(8.5f, FontStyle.Regular),
                           new Rectangle(r.X + Theme.S(42), sy, r.Width - Theme.S(70), Theme.S(18)),
                           Enabled ? Theme.Text : Theme.TextFaint,
                           TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                sy += Theme.S(22);
            }

            int a = Theme.S(32);
            Rectangle arrow = new Rectangle(r.Right - Theme.S(58), r.Bottom - Theme.S(50), a, a);
            if (Enabled) Theme.FillGradient(g, arrow, From, To, 45f);
            else Theme.FillRound(g, arrow, a / 2, Color.FromArgb(52, 40, 82));
            Theme.Draw(g, ">", Theme.F(11f, FontStyle.Bold), arrow,
                       Enabled ? Color.White : Theme.TextFaint,
                       TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ------------------------------------------------------------ круглая кнопка-иконка
    // Маленький полупрозрачный кружок со стрелкой влево — «вернуться в главное меню».
    class RoundIconButton : Control
    {
        bool hover;

        public RoundIconButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int s = Math.Min(Width, Height) - 1;
            if (s < Theme.S(8)) return;
            Rectangle r = new Rectangle((Width - s) / 2, (Height - s) / 2, s, s);

            using (SolidBrush b = new SolidBrush(hover ? Color.FromArgb(92, 255, 255, 255)
                                                       : Color.FromArgb(46, 255, 255, 255)))
                g.FillEllipse(b, r);
            using (Pen p = new Pen(Color.FromArgb(hover ? 240 : 195, 255, 255, 255), Theme.Sf(1.4f)))
                g.DrawEllipse(p, r);

            float cx = Width / 2f;
            float cy = Height / 2f;
            float half = s * 0.26f;   // половина древка
            float head = s * 0.19f;   // вылет усов стрелки
            using (Pen a = new Pen(Color.FromArgb(hover ? 255 : 250, 255, 255, 255),
                                   Math.Max(Theme.Sf(1.5f), s * 0.062f)))
            {
                a.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                a.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                a.LineJoin = System.Drawing.Drawing2D.LineJoin.Round;
                g.DrawLine(a, cx + half, cy, cx - half, cy);
                g.DrawLine(a, cx - half, cy, cx - half + head, cy - head);
                g.DrawLine(a, cx - half, cy, cx - half + head, cy + head);
            }
        }
    }
}
