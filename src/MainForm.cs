using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace WinSetup
{
    public partial class MainForm : Form
    {
        Manifest manifest;
        List<Item> curatedItems;
        string appDir;
        string manifestPath;
        string bundleDir;
        string warehouse;
        // последняя проверка свободного места (для строки в логе)
        long lastNeed = 0;
        long lastFree = 0;
        string lastRoot = "";

        DataGridView grid;
        BgPanel sidePanel;
        Card tableCard;
        Card logCard;
        BigRing bigCard;
        BarsCard barsCard;
        StatCard rcFound;
        StatCard rcInst;
        StatCard rcCfg;
        StatCard rcReady;
        Label lblHeader;
        Label lblMode;
        TextBox txtLog;
        NavButton btnInstall;
        NavButton btnSaveCfg;
        NavButton btnStop;
        NavButton btnAdd;
        NavButton btnFolder;
        NavButton btnScan;
        RoundIconButton btnBack;
        BgPanel startPanel;
        ChoiceCard btnOld;
        ChoiceCard btnRestore;
        NavButton btnPlain;
        Label lblStartHint;
        bool scanMode;
        bool restoreMode;
        bool notifyDone;
        int pct;
        string curText = "готов к работе";

        Thread worker;
        string logFile;
        volatile bool cancelFlag;
        bool running;

        public MainForm()
        {
            appDir = Path.GetDirectoryName(Application.ExecutablePath);
            manifestPath = Path.Combine(appDir, "manifest.json");
            bundleDir = Path.Combine(appDir, "Bundle");
            warehouse = Path.GetDirectoryName(appDir);   // склад = родительская папка

            manifest = Json.Load<Manifest>(manifestPath, null);
            if (manifest == null) manifest = new Manifest();
            if (manifest.items == null) manifest.items = new List<Item>();
            curatedItems = manifest.items;   // выверенный вручную список — не терять при сканировании
            if (!string.IsNullOrEmpty(manifest.warehouse)) warehouse = Util.Expand(manifest.warehouse);
            if (!Directory.Exists(bundleDir)) Directory.CreateDirectory(bundleDir);

            BuildUi();
            FillGrid();
            BuildStartPanel();
            ShowStart();
            Application.AddMessageFilter(this);   // колесо мыши над таблицей/журналом
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            try { Application.RemoveMessageFilter(this); } catch { }
            base.OnFormClosed(e);
        }

        void SetProgress(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            pct = percent;
            if (bigCard != null) { bigCard.Value = pct; bigCard.Invalidate(); }
            if (rcReady != null) { rcReady.Value = pct; rcReady.Invalidate(); }
        }

        void SetCur(string t)
        {
            curText = t;
            if (bigCard != null) { bigCard.Current = curText; bigCard.Invalidate(); }
        }

        void FillGrid()
        {
            grid.Rows.Clear();
            grid.Columns.Clear();
            DataGridViewCheckBoxColumn c0 = new DataGridViewCheckBoxColumn();
            c0.HeaderText = "";
            c0.Width = Theme.S(30);
            c0.FillWeight = 6;
            grid.Columns.Add(c0);
            grid.Columns.Add("name", "Приложение");
            grid.Columns.Add("src", "Источник");
            grid.Columns.Add("stage", "Этап");
            grid.Columns.Add("cfg", "Настройки");
            grid.Columns.Add("inst", "Установщик");
            grid.Columns["name"].FillWeight = 36;
            grid.Columns["src"].FillWeight = 11;
            grid.Columns["stage"].FillWeight = 18;
            grid.Columns["cfg"].FillWeight = 14;
            grid.Columns["inst"].FillWeight = 21;
            // нижние границы ширины: иначе на маленьком экране заголовки режутся
            grid.Columns["name"].MinimumWidth = Theme.S(130);
            grid.Columns["src"].MinimumWidth = Theme.S(74);
            grid.Columns["stage"].MinimumWidth = Theme.S(92);
            grid.Columns["cfg"].MinimumWidth = Theme.S(84);
            grid.Columns["inst"].MinimumWidth = Theme.S(86);

            // выравнивание: числовые колонки по центру, текстовые слева
            grid.Columns["name"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.Columns["src"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.Columns["stage"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns["cfg"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns["inst"].DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
            grid.Columns["stage"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;
            grid.Columns["cfg"].HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleCenter;

            // расшифровка колонок по наведению на заголовок
            grid.Columns["src"].HeaderCell.ToolTipText =
                "Откуда берётся установщик:\n" +
                "winget — ставится командой winget;\n" +
                "файл — готовый установщик внутри Bundle;\n" +
                "portable — архив или папка, просто распаковывается;\n" +
                "вручную — ставить руками, автоматически не выйдет.";
            grid.Columns["stage"].HeaderCell.ToolTipText =
                "Порядок установки (те же этапы, что в карточке «ПО ЭТАПАМ»):\n" +
                "1 — системное: драйверы, рантаймы, оболочки;\n" +
                "2 — утилиты;\n" +
                "3 — программы.";
            grid.Columns["cfg"].HeaderCell.ToolTipText =
                "Сколько наборов настроек будет сохранено в Bundle\n" +
                "и возвращено на новую Windows.";
            grid.Columns["inst"].HeaderCell.ToolTipText =
                "Что с установщиком прямо сейчас:\n" +
                "в Bundle — уже лежит в Bundle\\installers, перенесётся;\n" +
                "в складе — лежит в папке-складе, при сборке скопируется;\n" +
                "из интернета — скачается на новой Windows через winget;\n" +
                "нет установщика — автоматически не поставится;\n" +
                "не скачан — пакет найден в winget, установщик скачается при сборке.";

            for (int i = 0; i < manifest.items.Count; i++)
            {
                Item it = manifest.items[i];
                string src = it.source;
                if (src == "winget") src = "winget";
                else if (src == "file") src = "файл";
                else if (src == "portable") src = "portable";
                else if (src == "script") src = "скрипт";
                else if (src == "manual") src = "вручную";
                int cfg = it.configs == null ? 0 : it.configs.Count;
                string inst = InstText(it.installerState);
                int idx = grid.Rows.Add(new object[] { it.enabled, it.name, src, StageText(it.stage), cfg > 0 ? (cfg + " шт.") : "не найдено", inst });
                grid.Rows[idx].Tag = it;

                // подсказка к ячейке «Установщик» — что именно значит это слово
                grid.Rows[idx].Cells["inst"].ToolTipText = InstHint(it.installerState);

                // подсказка к «Этапу» — расшифровка номера словами
                grid.Rows[idx].Cells["stage"].ToolTipText = StageHint(it.stage);

                // серым — то, чего нет, чтобы цветом читалось с одного взгляда
                if (cfg == 0)
                {
                    grid.Rows[idx].Cells["cfg"].Style.ForeColor = Theme.TextFaint;
                    grid.Rows[idx].Cells["cfg"].ToolTipText = "У этой программы нет настроек для переноса.";
                }
                string st = it.installerState == null ? "" : it.installerState;
                if (st == "нет" || st == "нет файла" || st == "?")
                {
                    grid.Rows[idx].Cells["inst"].Style.ForeColor = Theme.TextFaint;
                }
                else if (st == "скачан" || st == "в складе")
                {
                    grid.Rows[idx].Cells["inst"].Style.ForeColor = Theme.Green;
                }
            }

            UpdateStats();
        }

        // ------------------------------------------------------------ подписи колонки «Установщик»

        // служебные коды (скачан / в складе / нет / ?) -> человеческий текст
        static string InstText(string state)
        {
            switch (state)
            {
                case "скачан": return "в Bundle";
                case "в складе": return "в складе";
                case "из интернета": return "из интернета";
                case "нет": return "нет установщика";
                case "нет файла": return "нет файла";
                default: return "не скачан";
            }
        }

        // подробное пояснение для той же ячейки (по наведению)
        static string InstHint(string state)
        {
            switch (state)
            {
                case "скачан":
                    return "Установщик уже лежит в Bundle\\installers — перенесётся на новую Windows вместе с программой.";
                case "в складе":
                    return "Установщик найден в складе (папка рядом с программой) — при сборке Bundle он будет скопирован внутрь.";
                case "из интернета":
                    return "Скачать установщик не успели, но пакет есть в winget — на новой Windows он скачается из интернета.";
                case "нет":
                    return "Установщика нет ни в складе, ни в winget — такую программу придётся поставить вручную.";
                case "нет файла":
                    return "Портативная программа: папка или архив не найдены в складе, переносить нечего.";
                default:
                    return "Установщик ещё не качали: при сборке Bundle программа найдёт его в складе или скачает из winget.";
            }
        }

        // этап словами — для подсказки над ячейкой
        static string StageText(int stage)
        {
            if (stage == 1) return "1 · системное";
            if (stage == 2) return "2 · утилиты";
            return "3 · программы";
        }

        static string StageHint(int stage)
        {
            if (stage == 1) return "Этап 1 — системное: драйверы, рантаймы, оболочки. Ставится первым.";
            if (stage == 2) return "Этап 2 — утилиты: мелкие программы и инструменты. Ставятся после системного.";
            return "Этап 3 — программы: обычные приложения. Ставятся последними.";
        }

        // ------------------------------------------------------------ добавление нового

        void AddFileDialog()
        {
            OpenFileDialog dlg = new OpenFileDialog();
            dlg.Filter = "Установщики|*.exe;*.msi;*.msix;*.zip;*.7z;*.bat;*.cmd|Все файлы|*.*";
            dlg.Multiselect = true;
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            for (int i = 0; i < dlg.FileNames.Length; i++) AddFile(dlg.FileNames[i]);
            FillGrid();
            SaveManifest();
        }

        void AddFile(string fullPath)
        {
            if (!File.Exists(fullPath)) return;
            string name = Path.GetFileNameWithoutExtension(fullPath);
            for (int i = 0; i < manifest.items.Count; i++)
            {
                if (manifest.items[i].name == name) { Append("= уже в списке: " + name); return; }
            }

            string kind = Detector.Detect(fullPath);
            string ext = Path.GetExtension(fullPath).ToLowerInvariant();
            Item it = new Item();
            it.id = MakeId(name);
            it.name = name;
            it.kind = kind;
            it.stage = 3;

            if (ext == ".bat" || ext == ".cmd")
            {
                it.source = "script";
                it.silentArgs = "";
            }
            else if (ext == ".zip" || ext == ".7z")
            {
                it.source = "portable";
                it.target = "%LOCALAPPDATA%\\Programs\\" + name;
            }
            else
            {
                it.source = "file";
                it.silentArgs = kind == "msi" ? null : Detector.SilentArgs(kind, fullPath);
            }

            // путь относительно склада, если файл внутри склада
            string rel = fullPath;
            try
            {
                if (fullPath.StartsWith(warehouse, StringComparison.OrdinalIgnoreCase))
                    rel = fullPath.Substring(warehouse.Length).TrimStart('\\', '/');
            }
            catch { }
            it.filePath = rel;

            // автоподвязка настроек
            it.configs = FindConfigs(name, fullPath);
            manifest.items.Add(it);
            Append("+ добавлено: " + name + " [" + Detector.KindLabel(kind) + "], настроек найдено: " + it.configs.Count);
        }

        string MakeId(string name)
        {
            string s = name.ToLowerInvariant();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '_') sb.Append('_');
            }
            string id = sb.ToString().Trim('_');
            if (id.Length == 0) id = "app";
            string baseId = id;
            int n = 2;
            bool exists = true;
            while (exists)
            {
                exists = false;
                for (int i = 0; i < manifest.items.Count; i++)
                {
                    if (manifest.items[i].id == id) { exists = true; break; }
                }
                if (exists) { id = baseId + "_" + n; n++; }
            }
            return id;
        }

        List<ConfigEntry> FindConfigs(string name, string installerPath)
        {
            List<ConfigEntry> list = new List<ConfigEntry>();
            string[] roots = new string[] { "%APPDATA%", "%LOCALAPPDATA%", "%PROGRAMDATA%" };
            string[] variants = new string[] { name, name.Replace(" ", ""), name.Replace(" ", "_") };

            for (int r = 0; r < roots.Length; r++)
            {
                string baseDir = Util.Expand(roots[r]);
                for (int v = 0; v < variants.Length; v++)
                {
                    string cand = Path.Combine(baseDir, variants[v]);
                    if (Directory.Exists(cand))
                    {
                        ConfigEntry ce = new ConfigEntry();
                        ce.path = roots[r] + "\\" + variants[v];
                        ce.type = "dir";
                        ce.mode = "selective";
                        list.Add(ce);
                    }
                }
            }
            // папка профиля рядом с установщиком
            try
            {
                string dir = Path.GetDirectoryName(installerPath);
                foreach (string d in Directory.GetDirectories(dir))
                {
                    string dn = Path.GetFileName(d);
                    if (string.Equals(dn, name, StringComparison.OrdinalIgnoreCase))
                    {
                        ConfigEntry ce = new ConfigEntry();
                        ce.path = d;
                        ce.type = "dir";
                        ce.mode = "full";
                        list.Add(ce);
                    }
                }
            }
            catch { }
            return list;
        }

        void SaveManifest()
        {
            try { Json.Save<Manifest>(manifestPath, manifest); }
            catch (Exception ex) { Append("! не смог сохранить manifest.json: " + ex.Message); }
        }

        // ------------------------------------------------------------ запуск

        void StartRun(bool withInstall)
        {
            if (running) return;

            // собрать актуальные галочки
            for (int i = 0; i < grid.Rows.Count; i++)
            {
                Item it = grid.Rows[i].Tag as Item;
                if (it == null) continue;
                object v = grid.Rows[i].Cells[0].Value;
                it.enabled = (v != null && (bool)v);
            }
            if (!restoreMode) SaveManifest();

            int count = 0;
            for (int i = 0; i < manifest.items.Count; i++) if (manifest.items[i].enabled) count++;
            if (count == 0) { Append("! нечего делать: не выбрано ни одного пункта"); return; }

            if (!withInstall && MessageBox.Show(this,
                "Сохранить настройки выбранных приложений в папку Bundle?\n\nПунктов: " + count +
                "\n\nЗатем эту папку OneClick просто перенеси на новую Windows.",
                Logo.Name, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            cancelFlag = false;
            running = true;
            btnInstall.Enabled = false;
            btnSaveCfg.Enabled = false;
            btnAdd.Enabled = false;
            btnStop.Enabled = true;
            SetProgress(0);
            txtLog.Clear();
            try
            {
                string logDir = Path.Combine(bundleDir, "logs");
                Directory.CreateDirectory(logDir);
                logFile = Path.Combine(logDir, "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            }
            catch { logFile = null; }
            Append("=== СТАРТ ===");
            Append("склад: " + warehouse);
            Append("пунктов: " + count);
            if (restoreMode && lastNeed > 0)
            {
                Append("место на " + lastRoot + ": нужно " + Util.FormatSize(lastNeed) +
                        ", свободно " + Util.FormatSize(lastFree) + " (запас 2 ГБ)");
            }

            InstallEngine eng = new InstallEngine();
            eng.manifest = manifest;
            eng.warehouse = warehouse;
            eng.bundleDir = bundleDir;
            eng.restoreConfigs = withInstall;
            eng.report = new Reporter(OnReport);
            eng.cancelled = delegate { return cancelFlag; };

            worker = new Thread(delegate()
            {
                try
                {
                    if (withInstall) eng.Run();
                    else SaveConfigsOnly(eng);
                }
                catch (Exception ex) { OnReport(-1, null, "КРИТИЧЕСКАЯ ОШИБКА: " + ex.Message); }
                finally { try { BeginInvoke(new MethodInvoker(RunDone)); } catch { } }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        void SaveConfigsOnly(InstallEngine eng)
        {
            int total = 0;
            for (int i = 0; i < manifest.items.Count; i++)
            {
                Item it = manifest.items[i];
                if (!it.enabled) continue;
                if (it.configs == null || it.configs.Count == 0) continue;
                total++;
            }
            int done = 0;
            for (int i = 0; i < manifest.items.Count; i++)
            {
                Item it = manifest.items[i];
                if (!it.enabled) continue;
                if (it.configs == null || it.configs.Count == 0) continue;
                if (cancelFlag) { OnReport(-1, null, "ОСТАНОВЛЕНО"); return; }
                OnReport(total == 0 ? 0 : (int)((done * 100.0) / total), it.name + " — сохранение настроек", null);
                try { ConfigEngine.SaveItem(it, bundleDir, eng); }
                catch (Exception ex) { OnReport(-1, null, "! " + it.name + ": настройки не сохранены — " + ex.Message); }
                done++;
            }
            OnReport(100, "Готово", null);
        }

        void OnReport(int percent, string current, string log)
        {
            try
            {
                BeginInvoke(new MethodInvoker(delegate()
                {
                    if (percent >= 0)
                    {
                        if (percent > 100) percent = 100;
                        SetProgress(percent);
                    }
                    if (current != null) SetCur(current);
                    if (log != null) Append(log);
                }));
            }
            catch { }
        }

        // ------------------------------------------------------------ стартовый экран

        void ShowStart()
        {
            // отладка верстки: файл nostart.flag рядом с exe — сразу открыть дашборд
            if (File.Exists(Path.Combine(appDir, "nostart.flag")) ||
                Environment.GetEnvironmentVariable("WINSETUP_NOSTART") == "1")
            {
                startPanel.Visible = false;
                return;
            }

            PrepareStart();
            startPanel.Visible = true;
            startPanel.BringToFront();
        }

        // обновляет подписи стартового экрана (что лежит в Bundle и где склад)
        void PrepareStart()
        {
            Manifest b = AutoSetup.LoadBundle(bundleDir);
            int n = (b != null && b.items != null) ? b.items.Count : 0;
            if (btnRestore != null)
            {
                btnRestore.Enabled = n > 0;
                if (n > 0)
                {
                    btnRestore.Title = "Поставить " + n + " приложений";
                    btnRestore.Body = "Один клик: установка всего из Bundle и распаковка конфигов по старым путям.";
                }
                else
                {
                    btnRestore.Title = "Bundle ещё не собран";
                    btnRestore.Body = "Сначала нажми «ЭТО СТАРАЯ WINDOWS» и собери установщики с настройками.";
                }
            }
            if (lblStartHint != null)
                lblStartHint.Text = "Склад: " + warehouse + "     ·     Bundle: " + bundleDir;
        }

        // кнопка «← ГЛАВНОЕ МЕНЮ»: вернуться на стартовый экран, не закрывая программу
        void BackToStart()
        {
            if (running)
            {
                MessageBox.Show(this,
                    "Сейчас идёт операция. Нажми «СТОП» и дождись окончания — потом можно вернуться в меню.",
                    Logo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PrepareStart();
            startPanel.Visible = true;
            startPanel.BringToFront();
        }

        void Notify(string title, string text)
        {
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
            try { MessageBox.Show(this, text, Logo.Name + " — " + title, MessageBoxButtons.OK, MessageBoxIcon.Information); } catch { }
        }

        // ------------------------------------------------------------ сканирование ПК

        void BeginScan()
        {
            if (running) return;
            if (MessageBox.Show(this,
                "Просканировать этот ПК?\r\n\r\nНайду установленные приложения и их настройки.\r\n" +
                "Установщики скачаются позже — по кнопке «СОБРАТЬ ВСЁ».",
                Logo.Name + " — сканирование", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            startPanel.Visible = false;
            cancelFlag = false;
            running = true;
            notifyDone = false;
            btnInstall.Enabled = false;
            btnSaveCfg.Enabled = false;
            btnAdd.Enabled = false;
            btnScan.Enabled = false;
            btnStop.Enabled = true;
            SetProgress(0);
            txtLog.Clear();
            Append("=== СКАНИРОВАНИЕ ПК ===");

            worker = new Thread(delegate()
            {
                try
                {
                    List<Item> found = AutoSetup.Scan(delegate(string l) { OnReport(-1, null, l); }, warehouse);
                    int cfg = 0;
                    for (int i = 0; i < found.Count; i++)
                    {
                        if (cancelFlag) break;
                        List<ConfigEntry> cs = AutoSetup.FindConfigs(found[i], null);
                        found[i].configs = cs;
                        if (cs.Count > 0) cfg++;
                        if ((i % 10) == 0) OnReport(found.Count == 0 ? 0 : (i * 100) / found.Count, "поиск настроек: " + found[i].name, null);
                    }
                    OnReport(100, "готово", "найдено приложений: " + found.Count + ", с настройками: " + cfg);
                    AutoSetup.MergeCurated(found, curatedItems);
                    ApplyScan(found);
                    notifyDone = true;
                }
                catch (Exception ex) { OnReport(-1, null, "КРИТИЧЕСКАЯ ОШИБКА: " + ex.Message); }
                finally { try { BeginInvoke(new MethodInvoker(RunDone)); } catch { } }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        void ApplyScan(List<Item> found)
        {
            try
            {
                BeginInvoke(new MethodInvoker(delegate()
                {
                    manifest.items = found;
                    scanMode = true;
                    restoreMode = false;
                    btnInstall.Text = "СОБРАТЬ ВСЁ";
                    btnInstall.Invalidate();
                    lblMode.Text = "РЕЖИМ: НАЙДЕНО " + found.Count + " ПРИЛОЖЕНИЙ";
                    SetActiveNav(btnScan);
                    SetProgress(0);
                    SetCur("проверь галочки и нажми «СОБРАТЬ ВСЁ»");
                    FillGrid();
                }));
            }
            catch { }
        }

        void ReadChecks()
        {
            for (int i = 0; i < grid.Rows.Count; i++)
            {
                Item it = grid.Rows[i].Tag as Item;
                if (it == null) continue;
                object v = grid.Rows[i].Cells[0].Value;
                it.enabled = (v != null && (bool)v);
            }
        }

        // ------------------------------------------------------------ сборка Bundle

        void BuildAll()
        {
            if (running) return;
            ReadChecks();

            List<Item> sel = new List<Item>();
            for (int i = 0; i < manifest.items.Count; i++)
                if (manifest.items[i].enabled) sel.Add(manifest.items[i]);
            if (sel.Count == 0) { Append("! нечего собирать: нет ни одной галочки"); return; }

            if (MessageBox.Show(this,
                "Собрать Bundle?\r\n\r\nПриложений: " + sel.Count +
                "\r\nСкачаю установщики и скопирую настройки в Bundle.\r\nМожет занять 10-40 минут и несколько ГБ.",
                Logo.Name + " — сборка", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            cancelFlag = false;
            running = true;
            notifyDone = true;
            btnInstall.Enabled = false;
            btnSaveCfg.Enabled = false;
            btnAdd.Enabled = false;
            btnScan.Enabled = false;
            btnStop.Enabled = true;
            SetProgress(0);
            txtLog.Clear();
            MakeLogFile();
            Append("=== СБОРКА BUNDLE ===");
            Append("приложений: " + sel.Count);

            worker = new Thread(delegate()
            {
                try
                {
                    int instTotal = 0;
                    Manifest m = AutoSetup.Build(sel, bundleDir, warehouse, new Reporter(OnReport), delegate { return cancelFlag; }, out instTotal);
                    OnReport(-1, null, "=== СОБРАНО: приложений " + m.items.Count + ", установщиков скачано " + instTotal + " ===");
                    OnReport(-1, null, "bundle: " + Path.Combine(bundleDir, "bundle.json"));
                }
                catch (Exception ex) { OnReport(-1, null, "КРИТИЧЕСКАЯ ОШИБКА: " + ex.Message); }
                finally { try { BeginInvoke(new MethodInvoker(RunDone)); } catch { } }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        void MakeLogFile()
        {
            try
            {
                string logDir = Path.Combine(bundleDir, "logs");
                Directory.CreateDirectory(logDir);
                logFile = Path.Combine(logDir, "run-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log");
            }
            catch { logFile = null; }
        }

        // ------------------------------------------------------------ восстановление Bundle

        void RestoreAll()
        {
            if (running) return;

            Manifest b = AutoSetup.LoadBundle(bundleDir);
            if (b == null || b.items == null || b.items.Count == 0)
            {
                Notify("Bundle не найден", "В папке Bundle нет bundle.json.\r\nСначала собери Bundle на старой Windows.");
                return;
            }
            if (MessageBox.Show(this,
                "Установить " + b.items.Count + " приложений и вернуть настройки?\r\n\r\nЭто надолго — не мешай процессу.",
                Logo.Name + " — восстановление", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;

            startPanel.Visible = false;
            manifest = b;
            warehouse = bundleDir;
            if (!EnsureDiskSpace(b)) return;

            scanMode = false;
            restoreMode = true;
            btnInstall.Text = "УСТАНОВИТЬ ВСЁ";
            lblMode.Text = "РЕЖИМ: ВОССТАНОВЛЕНИЕ ИЗ BUNDLE";
            SetActiveNav(btnInstall);
            FillGrid();
            notifyDone = true;
            StartRun(true);
        }

        // проверка свободного места перед восстановлением из Bundle
        bool EnsureDiskSpace(Manifest b)
        {
            try
            {
                long need = Util.DirSize(Path.Combine(bundleDir, "configs")) +
                            Util.DirSize(Path.Combine(bundleDir, "installers"));
                string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                DriveInfo di = new DriveInfo(root);
                long free = di.AvailableFreeSpace;
                lastNeed = need;
                lastFree = free;
                lastRoot = root;
                long reserve = 2L * 1024 * 1024 * 1024;
                if (free > need + reserve) return true;

                DialogResult dr = MessageBox.Show(this,
                    "На диске " + root + " недостаточно места.\r\n\r\n" +
                    "Нужно: " + Util.FormatSize(need) + "  +  запас 2 ГБ\r\n" +
                    "Свободно: " + Util.FormatSize(free) + "\r\n\r\n" +
                    "Продолжить всё равно? Ошибки копирования возможны.",
                    Logo.Name + " — мало места", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                return dr == DialogResult.Yes;
            }
            catch { return true; }
        }

        void RunDone()
        {
            running = false;
            btnInstall.Enabled = true;
            btnSaveCfg.Enabled = true;
            btnAdd.Enabled = true;
            btnScan.Enabled = true;
            btnStop.Enabled = false;
            SetCur("готово");
            Append("=== ЗАВЕРШЕНО ===");
            Append("бэкап настроек: " + bundleDir);

            if (notifyDone)
            {
                notifyDone = false;
                string msg;
                if (restoreMode) msg = "Установка и восстановление настроек закончены.\r\nСмотри лог справа.";
                else if (scanMode) msg = "Сканирование закончено.\r\nПроверь галочки и нажми «СОБРАТЬ ВСЁ».";
                else msg = "Bundle собран.\r\nТеперь папку переноси на новую Windows и жми там «ЭТО НОВАЯ WINDOWS».";
                Notify("Готово", msg);
            }
        }

        void Append(string line)
        {
            if (line == null) return;
            txtLog.AppendText(line + "\r\n");
            if (logFile != null)
            {
                try { File.AppendAllText(logFile, line + "\r\n", System.Text.Encoding.UTF8); } catch { }
            }
        }
    }
}
