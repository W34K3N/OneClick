using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WinSetup
{
    // ------------------------------------------------------------ новый интерфейс
    //
    // Плотный дашборд: сайдбар с профилем и меню, ряд карточек с кольцами,
    // таблица приложений, полосы по этапам и журнал справа.

    public partial class MainForm : IMessageFilter
    {
        Label lblStartTitle;
        Label lblStartSub;
        PictureBox logoBox;
        Rectangle pillRect;
        MiniScroll miniScroll;
        TextScroll textScroll;
        Panel logHost;

        // Ширины в «логических» пикселях: при 150 % они умножаются через Theme.S(),
        // иначе сайдбар и отступы остались бы крошечными на больших экранах.
        int SideW { get { return Theme.S(250); } }
        int Gap { get { return Theme.S(14); } }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Theme.FillGradient(e.Graphics, ClientRectangle,
                               Color.FromArgb(34, 14, 68), Theme.BgDark, 90f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (pillRect.Width < Theme.S(40)) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            Theme.FillRound(g, pillRect, Theme.S(18), Color.FromArgb(30, 255, 255, 255));
            Theme.DrawRound(g, pillRect, Theme.S(18), Color.FromArgb(40, 255, 255, 255), 1f);

            int icon = pillRect.Height - Theme.S(16);
            Rectangle ic = new Rectangle(pillRect.Right - icon - Theme.S(8), pillRect.Y + Theme.S(8),
                                         icon, icon);
            Theme.FillGradientRound(g, ic, Theme.S(7), Theme.Accent2, Theme.Accent1, 45f);

            string txt = "Склад: " + warehouse;
            Theme.Draw(g, txt, Theme.F(8.5f, FontStyle.Regular),
                       new Rectangle(pillRect.X + Theme.S(18), pillRect.Y,
                                     pillRect.Width - icon - Theme.S(40), pillRect.Height),
                       Theme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis);
        }

        // размер окна не под мой монитор, а под текущий экран:
        //   большой экран   — 1240x780 (как задумано),
        //   маленький экран — по рабочей области, но не меньше 1180x760,
        //                     а если и столько не влезает — по рабочей области целиком.
        void BuildStartupSize()
        {
            int wantW = Theme.S(1240);
            int wantH = Theme.S(780);
            ClientSize = new Size(wantW, wantH);               // желаемый клиент
            int bw = Math.Max(0, Width - ClientSize.Width);    // рамка + заголовок окна
            int bh = Math.Max(0, Height - ClientSize.Height);

            Rectangle wa = new Rectangle(0, 0, Theme.S(1280), Theme.S(720));
            try { if (Screen.PrimaryScreen != null) wa = Screen.PrimaryScreen.WorkingArea; } catch { }

            int limW = wa.Width - Theme.S(16);
            int limH = wa.Height - Theme.S(16);
            if (limW < Theme.S(640)) limW = Theme.S(640);
            if (limH < Theme.S(480)) limH = Theme.S(480);

            int minW = Math.Min(Theme.S(1180) + bw, limW);
            int minH = Math.Min(Theme.S(760) + bh, limH);
            MinimumSize = new Size(minW, minH);

            int w = Math.Min(wantW + bw, limW);
            int h = Math.Min(wantH + bh, limH);
            if (w < minW) w = minW;
            if (h < minH) h = minH;
            Size = new Size(w, h);
        }

        void BuildUi()
        {
            Text = Logo.Name;
            Icon = Logo.WindowIcon();
            BuildStartupSize();
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.F(9f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.None;   // масштабируем сами через Theme.S()
            DoubleBuffered = true;

            // ---------------------------------------------------------- сайдбар
            sidePanel = new BgPanel();
            sidePanel.TopColor = Theme.SideTop;
            sidePanel.BottomColor = Theme.SideBottom;
            sidePanel.Paint += delegate(object s, PaintEventArgs e) { PaintSidebar(e.Graphics); };
            Controls.Add(sidePanel);

            btnScan = MakeNav("СКАНИРОВАТЬ ПК");
            btnScan.Click += delegate { BeginScan(); };
            sidePanel.Controls.Add(btnScan);

            btnInstall = MakeNav("СОБРАТЬ ВСЁ");
            btnInstall.Active = true;
            btnInstall.Click += delegate { if (scanMode) BuildAll(); else StartRun(true); };
            sidePanel.Controls.Add(btnInstall);

            btnSaveCfg = MakeNav("СОХРАНИТЬ НАСТРОЙКИ");
            btnSaveCfg.Click += delegate { StartRun(false); };
            sidePanel.Controls.Add(btnSaveCfg);

            btnStop = MakeNav("СТОП");
            btnStop.Enabled = false;
            btnStop.Click += delegate { cancelFlag = true; Append("... запрошена остановка"); };
            sidePanel.Controls.Add(btnStop);

            btnAdd = MakeNav("+ ДОБАВИТЬ ФАЙЛ");
            btnAdd.Click += delegate { AddFileDialog(); };
            sidePanel.Controls.Add(btnAdd);

            btnFolder = MakeNav("ОТКРЫТЬ ПАПКУ");
            btnFolder.Click += delegate { try { System.Diagnostics.Process.Start("explorer.exe", appDir); } catch { } };
            sidePanel.Controls.Add(btnFolder);

            // возврат на стартовый экран — круглая стрелка рядом с логотипом
            btnBack = new RoundIconButton();
            btnBack.Click += delegate { BackToStart(); };
            sidePanel.Controls.Add(btnBack);
            ToolTip tipBack = new ToolTip();
            tipBack.SetToolTip(btnBack, "Вернуться в главное меню");

            // ---------------------------------------------------------- заголовок
            lblHeader = new Label();
            lblHeader.Text = "ПАНЕЛЬ УПРАВЛЕНИЯ";
            lblHeader.Font = Theme.F(13f, FontStyle.Bold);
            lblHeader.ForeColor = Theme.Text;
            lblHeader.BackColor = Color.Transparent;
            lblHeader.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(lblHeader);

            lblMode = new Label();
            lblMode.Text = "РЕЖИМ: ОБЫЧНЫЙ СПИСОК";
            lblMode.Font = Theme.F(7.5f, FontStyle.Bold);
            lblMode.ForeColor = Theme.TextFaint;
            lblMode.BackColor = Color.Transparent;
            lblMode.TextAlign = ContentAlignment.MiddleLeft;
            Controls.Add(lblMode);

            // ---------------------------------------------------------- карточки-показатели
            rcFound = MakeStat("НАЙДЕНО", "приложений в списке и отмечено галочками", Theme.Accent1);
            rcInst = MakeStat("УСТАНОВЩИКИ", "скачаны в Bundle или лежат в складе", Theme.Accent3);
            rcCfg = MakeStat("НАСТРОЙКИ", "пунктов с сохранёнными конфигами", Theme.Accent4);
            rcReady = MakeStat("ГОТОВНОСТЬ", "общий прогресс текущей операции", Theme.Accent2);

            // ---------------------------------------------------------- таблица
            tableCard = new Card();
            tableCard.Header = "ПРИЛОЖЕНИЯ";
            tableCard.HeaderRight = "";
            tableCard.Accent = Theme.Accent2;
            Controls.Add(tableCard);

            grid = new DataGridView();
            grid.BackgroundColor = Theme.Card;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Theme.GridLine;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.DefaultCellStyle.BackColor = Theme.Card;
            grid.DefaultCellStyle.ForeColor = Theme.Text;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(76, 29, 149);
            grid.DefaultCellStyle.SelectionForeColor = Color.White;
            grid.DefaultCellStyle.Font = Theme.F(8f, FontStyle.Regular);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(48, 22, 90);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextDim;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(48, 22, 90);
            grid.ColumnHeadersDefaultCellStyle.Font = Theme.F(8f, FontStyle.Bold);
            grid.ColumnHeadersHeight = Theme.S(32);
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.RowTemplate.Height = Theme.S(28);
            grid.RowHeadersVisible = false;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.EditMode = DataGridViewEditMode.EditOnEnter;
            grid.ScrollBars = ScrollBars.None;
            // двойная буферность: без неё при прокрутке остаются «хвосты» строк
            try
            {
                System.Reflection.PropertyInfo db = typeof(DataGridView).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (db != null) db.SetValue(grid, true, null);
            }
            catch { }
            tableCard.Controls.Add(grid);

            miniScroll = new MiniScroll();
            miniScroll.Grid = grid;
            tableCard.Controls.Add(miniScroll);
            grid.Scroll += delegate { if (miniScroll != null) miniScroll.Invalidate(); };

            grid.AllowDrop = true;
            grid.DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            grid.DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null) return;
                for (int i = 0; i < files.Length; i++) AddFile(files[i]);
                FillGrid();
                SaveManifest();
            };
            AllowDrop = true;
            DragEnter += delegate(object s, DragEventArgs e)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            DragDrop += delegate(object s, DragEventArgs e)
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files == null) return;
                for (int i = 0; i < files.Length; i++) AddFile(files[i]);
                FillGrid();
                SaveManifest();
            };
            grid.CellValueChanged += delegate(object s, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0) return;
                if (e.ColumnIndex != 0) return;
                object v = grid.Rows[e.RowIndex].Cells[0].Value;
                Item it = grid.Rows[e.RowIndex].Tag as Item;
                if (it != null) it.enabled = (v != null && (bool)v);
                UpdateStats();
            };
            grid.CurrentCellDirtyStateChanged += delegate
            {
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };

            // свой чекбокс: системный в тёмной теме не виден
            grid.CellPainting += delegate(object s, DataGridViewCellPaintingEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
                e.PaintBackground(e.CellBounds, true);

                bool on = false;
                if (e.FormattedValue is bool) on = (bool)e.FormattedValue;
                else
                {
                    object v = grid.Rows[e.RowIndex].Cells[0].Value;
                    if (v is bool) on = (bool)v;
                    else if (v != null && v.ToString() == "True") on = true;
                }

                int cb = Theme.S(17);
                Rectangle box = new Rectangle(e.CellBounds.X + (e.CellBounds.Width - cb) / 2,
                                              e.CellBounds.Y + (e.CellBounds.Height - cb) / 2, cb, cb);
                Graphics g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                if (on)
                {
                    Theme.FillGradientRound(g, box, Theme.S(5), Theme.Accent1, Theme.Accent2, 45f);
                    Theme.DrawRound(g, box, Theme.S(5), Color.FromArgb(130, 255, 255, 255), 1f);
                    using (Pen p = new Pen(Color.White, Math.Max(1f, Theme.Sf(2f))))
                        g.DrawLines(p, new Point[] {
                            new Point(box.X + Theme.S(4), box.Y + Theme.S(9)),
                            new Point(box.X + Theme.S(7), box.Y + Theme.S(12)),
                            new Point(box.X + Theme.S(13), box.Y + Theme.S(5)) });
                }
                else
                {
                    Theme.FillRound(g, box, Theme.S(5), Color.FromArgb(26, 255, 255, 255));
                    Theme.DrawRound(g, box, Theme.S(5), Color.FromArgb(80, 180, 155, 230), 1f);
                }
                e.Handled = true;
            };

            // ---------------------------------------------------------- этапы + прогресс
            barsCard = new BarsCard();
            barsCard.Title = "ПО ЭТАПАМ";
            barsCard.Accent = Theme.Accent3;
            barsCard.Labels = new string[] { "Этап 1 — системное", "Этап 2 — утилиты", "Этап 3 — программы" };
            Controls.Add(barsCard);

            bigCard = new BigRing();
            Controls.Add(bigCard);

            // ---------------------------------------------------------- журнал
            logCard = new Card();
            logCard.Header = "ЖУРНАЛ";
            logCard.HeaderRight = "Bundle\\logs";
            logCard.Accent = Theme.Accent1;
            Controls.Add(logCard);

            txtLog = new TextBox();
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Color.FromArgb(26, 11, 50);
            txtLog.ForeColor = Color.FromArgb(208, 192, 242);
            txtLog.Font = new Font("Consolas", 8.5f);
            txtLog.BorderStyle = BorderStyle.None;

            // системная полоса прокрутки всегда светлая — отдаём её за край хоста,
            // а тонкую свою рисуем рядом (TextScroll)
            logHost = new Panel();
            logHost.BackColor = Color.FromArgb(26, 11, 50);
            logCard.Controls.Add(logHost);
            logHost.Controls.Add(txtLog);

            textScroll = new TextScroll();
            textScroll.Box = txtLog;
            logCard.Controls.Add(textScroll);

            logCard.Content = txtLog;
            logCard.EmptyHint = "здесь будет каждый шаг: что скачивается, что ставится, что распаковано. " +
                                "Пока пусто — запусти сборку или восстановление.";

            txtLog.TextChanged += delegate
            {
                // пустой журнал — показать подсказку карточки, а не чёрную панель
                if (logHost != null) logHost.Visible = txtLog.TextLength > 0;
                if (textScroll != null) textScroll.Invalidate();
            };
            txtLog.MouseWheel += delegate { if (textScroll != null) textScroll.Invalidate(); };
            txtLog.KeyUp += delegate { if (textScroll != null) textScroll.Invalidate(); };
            logHost.Visible = false;

            UpdateStats();
        }

        NavButton MakeNav(string text)
        {
            NavButton b = new NavButton();
            b.Text = text;
            return b;
        }

        void SetActiveNav(NavButton active)
        {
            NavButton[] all = new NavButton[] { btnScan, btnInstall, btnSaveCfg, btnStop, btnAdd, btnFolder };
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] == null) continue;
                all[i].Active = (all[i] == active);
                all[i].Invalidate();
            }
        }

        // колесо мыши: у грида отключены штатные скроллбары, крутим вручную
        void ScrollGrid(int delta)
        {
            if (grid == null) return;
            int rows = grid.Rows.Count;
            int vis = Math.Max(1, grid.DisplayedRowCount(false));
            int max = Math.Max(0, rows - vis);
            int idx = Math.Max(0, grid.FirstDisplayedScrollingRowIndex) - Math.Sign(delta) * 3;
            if (idx < 0) idx = 0;
            if (idx > max) idx = max;
            try { grid.FirstDisplayedScrollingRowIndex = idx; } catch { }
            if (miniScroll != null) miniScroll.Invalidate();
        }

        // WM_MOUSEWHEEL доставляется окну с фокусом, поэтому ловим на уровне формы:
        // над таблицей крутим грид, над журналом — сам журнал
        public bool PreFilterMessage(ref Message m)
        {
            const int WM_MOUSEWHEEL = 0x020A;
            if (m.Msg != WM_MOUSEWHEEL) return false;

            int delta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
            Point p = Cursor.Position;
            try
            {
                if (grid != null && grid.IsHandleCreated &&
                    grid.RectangleToScreen(grid.ClientRectangle).Contains(p))
                {
                    ScrollGrid(delta);
                    return true;
                }
                if (textScroll != null && logCard != null && logCard.IsHandleCreated &&
                    logCard.RectangleToScreen(logCard.ClientRectangle).Contains(p))
                {
                    textScroll.ScrollLines(delta);
                    return true;
                }
            }
            catch { }
            return false;
        }

        StatCard MakeStat(string title, string sub, Color accent)
        {
            StatCard c = new StatCard();
            c.Title = title;
            c.Sub = sub;
            c.Accent = accent;
            Controls.Add(c);
            return c;
        }

        void PaintSidebar(Graphics g)
        {
            int w = sidePanel.ClientSize.Width;
            int h = sidePanel.ClientSize.Height;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            // логотип (сдвинут вниз — над ним круглая кнопка «в меню»)
            int lg = Theme.S(78);
            Rectangle av = new Rectangle((w - lg) / 2, Theme.S(62), lg, lg);
            Logo.Draw(g, av);

            Theme.Draw(g, Logo.Name, Theme.F(16f, FontStyle.Bold),
                       new Rectangle(0, Theme.S(148), w, Theme.S(24)),
                       Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, "один клик — всё на месте", Theme.F(7.5f, FontStyle.Regular),
                       new Rectangle(0, Theme.S(172), w, Theme.S(16)), Color.FromArgb(200, 228, 216, 255),
                       TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            using (Pen p = new Pen(Color.FromArgb(50, 255, 255, 255), 1f))
                g.DrawLine(p, Theme.S(22), Theme.S(200), w - Theme.S(22), Theme.S(200));

            Theme.Draw(g, "ДЕЙСТВИЯ", Theme.F(7f, FontStyle.Bold),
                       new Rectangle(Theme.S(18), Theme.S(208), w - Theme.S(36), Theme.S(14)),
                       Color.FromArgb(150, 255, 255, 255), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            int top = Theme.S(196) + Theme.S(46) * 6 + Theme.S(20);
            if (top < h - Theme.S(70)) top = h - Theme.S(70);
            using (Pen p2 = new Pen(Color.FromArgb(40, 255, 255, 255), 1f))
                g.DrawLine(p2, Theme.S(22), top - Theme.S(8), w - Theme.S(22), top - Theme.S(8));

            Theme.Draw(g, "версия " + Logo.Version, Theme.F(7.5f, FontStyle.Regular),
                       new Rectangle(0, top + Theme.S(2), w, Theme.S(18)), Color.FromArgb(150, 226, 212, 255),
                       TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            Theme.Draw(g, "Bundle внутри папки программы", Theme.F(7.5f, FontStyle.Regular),
                       new Rectangle(0, top + Theme.S(20), w, Theme.S(18)), Color.FromArgb(130, 222, 206, 252),
                       TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        // ------------------------------------------------------------ раскладка

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutUi();
            LayoutStart();
            DarkScrollbars();
            DarkTitle();
            try { BeginInvoke(new MethodInvoker(DarkScrollbars)); } catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DarkTitle();
        }

        // тёмный заголовок окна (Win10 1903+ = 20, раньше = 19)
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void DarkTitle()
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(Handle, 20, ref on, 4) != 0)
                    DwmSetWindowAttribute(Handle, 19, ref on, 4);
            }
            catch { }
        }

        // тёмные полосы прокрутки (Win10 1809+ через тему проводника)
        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hWnd, string subApp, string subIdList);

        void DarkScrollbars()
        {
            try { if (grid != null && grid.IsHandleCreated) SetWindowTheme(grid.Handle, "DarkMode_Explorer", null); } catch { }
            try { if (txtLog != null && txtLog.IsHandleCreated) SetWindowTheme(txtLog.Handle, "DarkMode_Explorer", null); } catch { }
            try
            {
                if (grid != null && grid.IsHandleCreated)
                {
                    EnumChildWindows(grid.Handle, delegate(IntPtr h, IntPtr p)
                    {
                        System.Text.StringBuilder sb = new System.Text.StringBuilder(80);
                        GetClassName(h, sb, 80);
                        if (sb.ToString().IndexOf("Scroll") >= 0) SetWindowTheme(h, "DarkMode_Explorer", null);
                        return true;
                    }, IntPtr.Zero);
                }
            }
            catch { }
        }

        delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool EnumChildWindows(IntPtr hWnd, EnumProc cb, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder sb, int max);

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutUi();
            LayoutStart();
        }

        void LayoutUi()
        {
            if (grid == null) return;

            int w = ClientSize.Width;
            int h = ClientSize.Height;
            sidePanel.Bounds = new Rectangle(0, 0, SideW, h);

            int x0 = SideW + Theme.S(26);
            int contentW = w - x0 - Theme.S(26);
            if (contentW < Theme.S(460)) contentW = Theme.S(460);

            int pillW = Theme.S(340);
            if (pillW > contentW - Theme.S(320)) pillW = contentW - Theme.S(320);
            if (pillW < Theme.S(160)) pillW = Theme.S(160);
            pillRect = new Rectangle(x0 + contentW - pillW, Theme.S(14), pillW, Theme.S(38));

            lblHeader.Bounds = new Rectangle(x0, Theme.S(12), contentW - pillW - Theme.S(24), Theme.S(30));
            lblMode.Bounds = new Rectangle(x0, Theme.S(40), contentW - pillW - Theme.S(24), Theme.S(16));

            // кнопки сайдбара
            int by = Theme.S(226);
            int bw = SideW - Theme.S(32);
            if (btnBack != null) btnBack.SetBounds(Theme.S(36), Theme.S(18), Theme.S(34), Theme.S(34));
            NavButton[] nav = new NavButton[] { btnScan, btnInstall, btnSaveCfg, btnStop, btnAdd, btnFolder };
            for (int i = 0; i < nav.Length; i++)
            {
                if (nav[i] == null) continue;
                nav[i].SetBounds(Theme.S(16), by, bw, Theme.S(42));
                by += Theme.S(46);
            }

            // карточки-показатели
            int top = Theme.S(72);
            int cardH = Theme.S(112);
            int cw = (contentW - Gap * 3) / 4;
            if (cw < Theme.S(130)) cw = Theme.S(130);
            rcFound.SetBounds(x0, top, cw, cardH);
            rcInst.SetBounds(x0 + (cw + Gap), top, cw, cardH);
            rcCfg.SetBounds(x0 + (cw + Gap) * 2, top, cw, cardH);
            rcReady.SetBounds(x0 + (cw + Gap) * 3, top, cw, cardH);

            // средняя часть
            int midTop = top + cardH + Theme.S(16);
            int bottom = h - Theme.S(24);
            int barsH = Theme.S(170);

            int rightW = Theme.S(300);
            if (rightW > contentW / 2) rightW = contentW / 2;
            int rightX = x0 + contentW - rightW;
            int leftW = contentW - rightW - Theme.S(16);
            if (leftW < Theme.S(340)) leftW = Theme.S(340);

            int tableH = bottom - barsH - Theme.S(14) - midTop;
            if (tableH < Theme.S(220)) tableH = Theme.S(220);

            tableCard.SetBounds(x0, midTop, leftW, tableH);
            int gy = tableCard.ContentTop;
            int scW = Theme.S(8);
            grid.SetBounds(Theme.S(14), gy, leftW - Theme.S(28) - scW - Theme.S(4), tableH - gy - Theme.S(12));
            if (miniScroll != null)
            {
                miniScroll.SetBounds(leftW - Theme.S(14) - scW, gy, scW, tableH - gy - Theme.S(12));
                miniScroll.BringToFront();
            }

            barsCard.SetBounds(x0, midTop + tableH + Theme.S(14), leftW, barsH);

            int bigH = Theme.S(208);
            if (bigH > bottom - midTop - Theme.S(200)) bigH = bottom - midTop - Theme.S(200);
            bigCard.SetBounds(rightX, midTop, rightW, bigH);
            logCard.SetBounds(rightX, midTop + bigH + Theme.S(14), rightW,
                              bottom - (midTop + bigH + Theme.S(14)));
            int ly = logCard.ContentTop;
            int lh = logCard.Height - ly - Theme.S(14);
            if (lh < Theme.S(40)) lh = Theme.S(40);
            int innerW = rightW - Theme.S(32) - Theme.S(10);
            logHost.SetBounds(Theme.S(16), ly, innerW, lh);
            txtLog.SetBounds(0, 0, innerW + Theme.S(12), lh);  // полоса TextBox уходит за край хоста
            if (textScroll != null)
            {
                textScroll.SetBounds(rightW - Theme.S(16) - Theme.S(8), ly, Theme.S(8), lh);
                textScroll.BringToFront();
                textScroll.Invalidate();
            }

            if (startPanel != null)
            {
                startPanel.Bounds = ClientRectangle;
                startPanel.BringToFront();
            }

            Invalidate(true);
        }

        // ------------------------------------------------------------ статистика

        void UpdateStats()
        {
            if (manifest == null || manifest.items == null) return;

            int total = manifest.items.Count;
            int enabled = 0;
            int withInst = 0;
            int withCfg = 0;
            int[] stage = new int[4];

            for (int i = 0; i < total; i++)
            {
                Item it = manifest.items[i];
                if (it.enabled) enabled++;
                if (it.configs != null && it.configs.Count > 0) withCfg++;

                string st = it.installerState == null ? "" : it.installerState;
                if (st == "скачан" || st == "в складе" || st == "из интернета") withInst++;

                int sg = it.stage;
                if (sg < 1 || sg > 3) sg = 3;
                stage[sg]++;
            }

            if (rcFound != null)
            {
                rcFound.Value = total > 0 ? enabled * 100 / total : 0;
                rcFound.Sub = total + " в списке · " + enabled + " отмечено";
                rcFound.Invalidate();
            }
            if (rcInst != null)
            {
                rcInst.Value = total > 0 ? withInst * 100 / total : 0;
                rcInst.Sub = withInst + " из " + total + " готовы";
                rcInst.Invalidate();
            }
            if (rcCfg != null)
            {
                rcCfg.Value = total > 0 ? withCfg * 100 / total : 0;
                rcCfg.Sub = withCfg + " из " + total + " с настройками";
                rcCfg.Invalidate();
            }
            if (barsCard != null)
            {
                barsCard.Values = new int[] { stage[1], stage[2], stage[3] };
                barsCard.Max = total;
                if (miniScroll != null) miniScroll.Invalidate();
                barsCard.Invalidate();
            }
            if (tableCard != null)
            {
                tableCard.HeaderRight = total + " записей";
                tableCard.Invalidate();
            }
        }

        // ------------------------------------------------------------ стартовый экран

        void BuildStartPanel()
        {
            startPanel = new BgPanel();
            startPanel.TopColor = Color.FromArgb(58, 20, 116);
            startPanel.BottomColor = Theme.BgDark;
            startPanel.Glows = true;
            Controls.Add(startPanel);
            startPanel.Bounds = ClientRectangle;
            startPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            logoBox = new PictureBox();
            logoBox.SizeMode = PictureBoxSizeMode.Zoom;
            logoBox.BackColor = Color.Transparent;
            using (Bitmap lb = Logo.Render(112))
                logoBox.Image = new Bitmap(lb);
            startPanel.Controls.Add(logoBox);

            lblStartTitle = new Label();
            lblStartTitle.Text = Logo.Name;
            lblStartTitle.Font = Theme.F(26f, FontStyle.Bold);
            lblStartTitle.ForeColor = Color.White;
            lblStartTitle.BackColor = Color.Transparent;
            lblStartTitle.TextAlign = ContentAlignment.MiddleLeft;
            startPanel.Controls.Add(lblStartTitle);

            lblStartSub = new Label();
            lblStartSub.Text = "Один раз собрал на старой системе — потом ставится всё само.";
            lblStartSub.Font = Theme.F(10f, FontStyle.Regular);
            lblStartSub.ForeColor = Theme.TextDim;
            lblStartSub.BackColor = Color.Transparent;
            lblStartSub.TextAlign = ContentAlignment.MiddleLeft;
            startPanel.Controls.Add(lblStartSub);

            btnOld = new ChoiceCard();
            btnOld.Tag = "ЭТО СТАРАЯ WINDOWS";
            btnOld.Title = "Просканировать ПК";
            btnOld.Body = "Найду установленные приложения, подберу и скачаю установщики, сохраню их настройки в Bundle.";
            btnOld.From = Color.FromArgb(16, 185, 129);
            btnOld.To = Color.FromArgb(34, 211, 238);
            btnOld.Steps = new string[] {
                "найти установленные программы",
                "найти и скачать установщики",
                "сохранить настройки в Bundle" };
            btnOld.Click += delegate { BeginScan(); };
            startPanel.Controls.Add(btnOld);

            btnRestore = new ChoiceCard();
            btnRestore.Tag = "ЭТО НОВАЯ WINDOWS";
            btnRestore.Title = "Поставить всё обратно";
            btnRestore.Body = "Один клик: установка приложений из Bundle и распаковка конфигов по старым путям.";
            btnRestore.From = Theme.Accent2;
            btnRestore.To = Theme.Accent4;
            btnRestore.Steps = new string[] {
                "поставить программы тихо",
                "распаковать их настройки",
                "получить систему как была" };
            btnRestore.Click += delegate { RestoreAll(); };
            startPanel.Controls.Add(btnRestore);

            btnPlain = MakeNav("Обычный режим — список из manifest.json");
            btnPlain.Pill = true;
            btnPlain.Font = Theme.F(9f, FontStyle.Regular);
            btnPlain.Click += delegate
            {
                startPanel.Visible = false;
                scanMode = false;
                restoreMode = false;
                btnInstall.Text = "УСТАНОВИТЬ ВСЁ";
                lblMode.Text = "РЕЖИМ: СПИСОК ИЗ MANIFEST";
                SetActiveNav(btnInstall);
                FillGrid();
            };
            startPanel.Controls.Add(btnPlain);

            lblStartHint = new Label();
            lblStartHint.Font = Theme.F(8f, FontStyle.Regular);
            lblStartHint.ForeColor = Theme.TextFaint;
            lblStartHint.BackColor = Color.Transparent;
            lblStartHint.TextAlign = ContentAlignment.MiddleCenter;
            lblStartHint.AutoEllipsis = true;
            startPanel.Controls.Add(lblStartHint);

            LayoutStart();
            startPanel.BringToFront();
        }

        void LayoutStart()
        {
            if (startPanel == null || lblStartTitle == null) return;

            int w = ClientSize.Width;
            int h = ClientSize.Height;

            if (logoBox != null)
                logoBox.SetBounds(Theme.S(58), Theme.S(30), Theme.S(56), Theme.S(56));
            lblStartTitle.SetBounds(Theme.S(126), Theme.S(32),
                                    Math.Min(Theme.S(700), Math.Max(Theme.S(200), w - Theme.S(160))), Theme.S(46));
            lblStartSub.SetBounds(Theme.S(128), Theme.S(82),
                                  Math.Min(Theme.S(820), Math.Max(Theme.S(200), w - Theme.S(180))), Theme.S(26));

            int cw = Theme.S(400);
            int ch = Theme.S(230);
            int gap = Theme.S(26);
            int inner = w - Theme.S(120);
            if (cw * 2 + gap > inner) cw = (inner - gap) / 2;
            if (cw < Theme.S(260)) cw = Theme.S(260);
            int x = (w - (cw * 2 + gap)) / 2;
            int y = h / 2 - ch / 2 - Theme.S(10);

            btnOld.SetBounds(x, y, cw, ch);
            btnRestore.SetBounds(x + cw + gap, y, cw, ch);
            btnPlain.SetBounds(x, y + ch + Theme.S(26), cw * 2 + gap, Theme.S(42));
            lblStartHint.SetBounds(Theme.S(40), h - Theme.S(56), w - Theme.S(80), Theme.S(40));
        }
    }
}
