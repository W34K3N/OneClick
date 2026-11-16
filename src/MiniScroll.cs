using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace WinSetup
{
    // тонкий скроллбар вместо системного белого
    public class MiniScroll : Control
    {
        public DataGridView Grid;
        bool drag;
        int dragY;
        int dragIdx;

        public MiniScroll()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Grid == null) return;
            int rows = Grid.Rows.Count;
            int vis = Math.Max(1, Grid.DisplayedRowCount(false));
            if (rows <= vis) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle track = new Rectangle(0, 0, Width, Height);
            Theme.FillRound(g, track, Width / 2, Color.FromArgb(26, 255, 255, 255));

            int h = (int)((long)Height * vis / rows);
            if (h < Theme.S(34)) h = Theme.S(34);
            if (h > Height) h = Height;

            int maxIdx = Math.Max(1, rows - vis);
            int idx = Grid.FirstDisplayedScrollingRowIndex;
            if (idx < 0) idx = 0;
            if (idx > maxIdx) idx = maxIdx;
            int y = (int)((long)(Height - h) * idx / maxIdx);

            Theme.FillGradientRound(g, new Rectangle(0, y, Width, h), Width / 2,
                                    Color.FromArgb(190, 172, 140, 255), Color.FromArgb(160, 130, 95, 235), 90f);
        }

        void ScrollTo(int y)
        {
            if (Grid == null) return;
            int rows = Grid.Rows.Count;
            int vis = Math.Max(1, Grid.DisplayedRowCount(false));
            if (rows <= vis) return;

            int h = (int)((long)Height * vis / rows);
            if (h < Theme.S(34)) h = Theme.S(34);
            int maxIdx = Math.Max(1, rows - vis);
            int span = Math.Max(1, Height - h);
            int off = y - dragY + (int)((long)dragIdx * span / maxIdx);
            if (off < 0) off = 0;
            if (off > span) off = span;
            int idx = (int)((long)off * maxIdx / span);
            try { Grid.FirstDisplayedScrollingRowIndex = idx; } catch { }
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Grid == null) return;
            drag = true;
            dragY = e.Y;
            dragIdx = Math.Max(0, Grid.FirstDisplayedScrollingRowIndex);
            ScrollTo(e.Y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag) ScrollTo(e.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            drag = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Grid != null)
            {
                int rows = Grid.Rows.Count;
                int vis = Math.Max(1, Grid.DisplayedRowCount(false));
                int idx = Math.Max(0, Grid.FirstDisplayedScrollingRowIndex) - Math.Sign(e.Delta) * 3;
                if (idx < 0) idx = 0;
                if (idx > Math.Max(0, rows - vis)) idx = Math.Max(0, rows - vis);
                try { Grid.FirstDisplayedScrollingRowIndex = idx; } catch { }
                Invalidate();
            }
        }
    }

    // ------------------------------------------------------------ тонкий скроллбар журнала
    //
    // У TextBox системная полоса всегда светлая и не темнеет. Поэтому сама полоса
    // уводится за правый край родителя (обрезается), а рисуем свою.

    public class TextScroll : Control
    {
        public TextBox Box;
        bool drag;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        const int EM_GETFIRSTVISIBLELINE = 0xCE;
        const int EM_GETLINECOUNT = 0xBA;
        const int EM_LINESCROLL = 0xB6;

        public TextScroll()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
        }

        // прокрутка журнала колесом (delta>0 — вверх)
        public void ScrollLines(int delta)
        {
            if (Box == null || !Box.IsHandleCreated) return;
            int lines = delta > 0 ? -3 : 3;
            SendMessage(Box.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)lines);
            Invalidate();
        }

        int LineH()
        {
            if (Box == null) return 15;
            int h = Box.Font.Height;
            return h < 10 ? 10 : h;
        }

        int Total()
        {
            if (Box == null || !Box.IsHandleCreated) return 0;
            return (int)SendMessage(Box.Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero);
        }

        int FirstLine()
        {
            if (Box == null || !Box.IsHandleCreated) return 0;
            return (int)SendMessage(Box.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
        }

        int Visible()
        {
            if (Box == null) return 1;
            int v = Box.ClientSize.Height / LineH();
            return v < 1 ? 1 : v;
        }

        int ThumbH(int total, int vis)
        {
            int h = (int)((long)Height * vis / total);
            if (h < Theme.S(28)) h = Theme.S(28);
            if (h > Height) h = Height;
            return h;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Box == null) return;
            int total = Total();
            int vis = Visible();
            if (total <= vis) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(g, new Rectangle(0, 0, Width, Height), Width / 2, Color.FromArgb(24, 255, 255, 255));

            int h = ThumbH(total, vis);
            int span = Height - h;
            int maxFirst = total - vis;
            int first = FirstLine();
            if (first < 0) first = 0;
            if (first > maxFirst) first = maxFirst;
            int y = maxFirst <= 0 ? 0 : (int)((long)span * first / maxFirst);

            Theme.FillGradientRound(g, new Rectangle(0, y, Width, h), Width / 2,
                                    Color.FromArgb(190, 172, 140, 255), Color.FromArgb(160, 130, 95, 235), 90f);
        }

        void JumpTo(int y)
        {
            if (Box == null || !Box.IsHandleCreated) return;
            int total = Total();
            int vis = Visible();
            if (total <= vis) return;

            int h = ThumbH(total, vis);
            int span = Height - h;
            if (span < 1) span = 1;

            int off = y - h / 2;
            if (off < 0) off = 0;
            if (off > span) off = span;

            int maxFirst = total - vis;
            int target = (int)((long)off * maxFirst / span);
            int delta = target - FirstLine();
            if (delta != 0) SendMessage(Box.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            drag = true;
            JumpTo(e.Y);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (drag) JumpTo(e.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            drag = false;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Box == null || !Box.IsHandleCreated) return;
            SendMessage(Box.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)(e.Delta > 0 ? -3 : 3));
            Invalidate();
        }
    }
}
