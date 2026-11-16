using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WinSetup
{
    public delegate void Reporter(int percent, string current, string log);

    // ---------------------------------------------------------------- winget

    public static class Winget
    {
        public static bool Available()
        {
            ProcResult r = Proc.Run("winget", "--version", null, 20000, null, null);
            return r.ExitCode == 0 && r.Output.Trim().Length > 0;
        }

        public static HashSet<string> ListInstalled()
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ProcResult r = Proc.Run("winget", "list --accept-source-agreements", null, 180000, null, null);
            if (string.IsNullOrEmpty(r.Output)) return ids;
            string[] lines = r.Output.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string s = lines[i].TrimEnd('\r');
                if (s.Trim().Length == 0) continue;
                if (s.StartsWith("Name ") || s.StartsWith("---") || s.StartsWith("Имя ")) continue;
                string[] parts = Regex.Split(s.Trim(), "\\s{2,}");
                if (parts.Length >= 2)
                {
                    string id = parts[1].Trim();
                    if (id.IndexOf('.') > 0 && id.IndexOf(' ') < 0) ids.Add(id);
                }
            }
            return ids;
        }

        public static ProcResult Install(string id, Action<string> onLine, Func<bool> cancelled)
        {
            string args = "install --id " + id + " --exact --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";
            return Proc.Run("winget", args, null, 0, onLine, cancelled);
        }

        public static bool IsInstalled(string id)
        {
            ProcResult r = Proc.Run("winget", "list --id " + id + " --exact --accept-source-agreements", null, 60000, null, null);
            if (string.IsNullOrEmpty(r.Output)) return false;
            string[] lines = r.Output.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(id, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }
    }

    // ---------------------------------------------------------------- установка

    public class InstallEngine
    {
        public Manifest manifest;
        public string warehouse;
        public bool restoreConfigs;
        public string bundleDir;
        public Reporter report;
        public Func<bool> cancelled;

        internal int cntOk;
        internal int cntSkip;
        internal int cntErr;

        List<Item> Ordered()
        {
            List<Item> list = new List<Item>();
            for (int i = 0; i < manifest.items.Count; i++)
            {
                if (manifest.items[i].enabled) list.Add(manifest.items[i]);
            }
            // сортировка по этапу устойчивая
            for (int i = 1; i < list.Count; i++)
            {
                Item cur = list[i];
                int j = i - 1;
                while (j >= 0 && list[j].stage > cur.stage)
                {
                    list[j + 1] = list[j];
                    j--;
                }
                list[j + 1] = cur;
            }
            return list;
        }

        int ConfigCount(List<Item> items)
        {
            int n = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].configs != null && items[i].configs.Count > 0) n++;
            }
            return n;
        }

        internal void Log(string s)
        {
            Log(s, true);
        }

        // count = false для служебных строк (перезапуск служб/процессов, справка),
        // иначе они попадают в итоговые счётчики «установлено/пропущено/ошибок»
        internal void Log(string s, bool count)
        {
            if (s != null && count)
            {
                string t = s.TrimStart();
                if (t.StartsWith("+")) cntOk++;
                else if (t.StartsWith("=") || t.StartsWith("—")) cntSkip++;
                else if (t.StartsWith("!")) cntErr++;
            }
            if (report != null) report(-1, null, s);
        }

        // служебные строки: не влияют на итоговые счётчики
        internal void LogQuiet(string s) { Log(s, false); }

        void Progress(int percent, string current)
        {
            if (report != null) report(percent, current, null);
        }

        public void Run()
        {
            List<Item> items = Ordered();
            HashSet<string> installed = null;
            bool wingetOk = Winget.Available();
            if (wingetOk)
            {
                Log("winget доступен, читаю список установленного...");
                installed = Winget.ListInstalled();
                Log("уже установлено пакетов (распознано): " + installed.Count);
            }
            else
            {
                Log("winget НЕДОСТУПЕН — пакеты winget будут пропущены");
            }

            int cfgCount = restoreConfigs ? ConfigCount(items) : 0;
            int totalSteps = items.Count + cfgCount;
            int done = 0;

            for (int i = 0; i < items.Count; i++)
            {
                if (cancelled != null && cancelled()) { Log("ОСТАНОВЛЕНО пользователем"); return; }
                Item it = items[i];
                int percent = (int)((done * 100.0) / Math.Max(1, totalSteps));
                Progress(percent, it.name + " — установка");
                try
                {
                    InstallItem(it, installed, wingetOk);
                }
                catch (Exception ex)
                {
                    Log("ОШИБКА [" + it.name + "]: " + ex.Message);
                }
                done++;
                Progress((int)((done * 100.0) / Math.Max(1, totalSteps)), it.name + " — готово");
            }

            if (restoreConfigs)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (cancelled != null && cancelled()) { Log("ОСТАНОВЛЕНО пользователем"); return; }
                    Item it = items[i];
                    if (it.configs == null || it.configs.Count == 0) continue;
                    int percent = (int)((done * 100.0) / Math.Max(1, totalSteps));
                    Progress(percent, it.name + " — восстановление настроек");
                    try
                    {
                        ConfigEngine.RunPreActions(it, this);
                        ConfigEngine.RestoreItem(it, bundleDir, this);
                        ConfigEngine.RunPostActions(it, this);
                    }
                    catch (Exception ex)
                    {
                        Log("ОШИБКА настроек [" + it.name + "]: " + ex.Message);
                    }
                    done++;
                    Progress((int)((done * 100.0) / Math.Max(1, totalSteps)), it.name + " — настройки готовы");
                }
            }

            Log("=== ИТОГО: установлено " + cntOk + ", пропущено " + cntSkip + ", ошибок " + cntErr + " ===");
            Progress(100, "Готово");
        }

        void InstallItem(Item it, HashSet<string> installed, bool wingetOk)
        {
            string source = it.source == null ? "" : it.source.ToLowerInvariant();

            if (source == "winget")
            {
                if (!wingetOk) { Log("— пропуск " + it.name + ": winget недоступен"); return; }
                if (string.IsNullOrEmpty(it.wingetId)) { Log("— пропуск " + it.name + ": не задан wingetId"); return; }
                if (installed != null && installed.Contains(it.wingetId))
                {
                    Log("= " + it.name + " уже установлен (" + it.wingetId + ")");
                    return;
                }
                Log("> winget install " + it.wingetId);
                ProcResult r = Winget.Install(it.wingetId, delegate(string line) { Log("   " + line); }, cancelled);
                if (r.Cancelled) throw new Exception("отменено");
                bool ok = r.Output.IndexOf("Successfully installed", StringComparison.OrdinalIgnoreCase) >= 0
                          || r.Output.IndexOf("Успешно установлено", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!ok && r.ExitCode != 0)
                {
                    // возможно уже стояло — проверим
                    if (Winget.IsInstalled(it.wingetId)) { Log("= " + it.name + " уже установлен"); return; }
                    Log("! " + it.name + ": winget вернул код " + r.ExitCode);
                    return;
                }
                Log("+ " + it.name + " установлен");
                return;
            }

            if (source == "file" || source == "script")
            {
                string path = ResolvePath(it.filePath);
                if (!File.Exists(path)) { Log("— пропуск " + it.name + ": файл не найден (" + path + ")"); return; }
                string kind = string.IsNullOrEmpty(it.kind) ? Detector.Detect(path) : it.kind;
                string args = it.silentArgs;
                string exe = path;

                if (kind == "msi")
                {
                    exe = "msiexec";
                    args = "/i \"" + path + "\" /qn /norestart";
                }
                else if (args == null)
                {
                    args = Detector.SilentArgs(kind, path);
                }

                // тип не распознан и ключей нет — пробуем манифест winget, который лежит рядом
                if (kind == "unknown" && string.IsNullOrEmpty(args))
                {
                    string yml = AutoSetup.FindYaml(Path.GetDirectoryName(path), Path.GetFileName(path));
                    if (yml != null)
                    {
                        string k2;
                        string s2;
                        AutoSetup.ReadYamlManifest(yml, Path.GetFileName(path), out k2, out s2);
                        if (!string.IsNullOrEmpty(s2))
                        {
                            args = s2;
                            Log("   тип не распознан, ключи из манифеста: " + s2);
                        }
                        if (k2 == "msi")
                        {
                            exe = "msiexec";
                            args = "/i \"" + path + "\" /qn /norestart";
                        }
                    }
                }

                int tmo = it.timeoutSec > 0 ? it.timeoutSec * 1000 : 900000;

                // архивы: zip распаковываем в target, остальные форматы честно пропускаем
                if (kind == "archive" || kind == "zip" || kind == "7z" || kind == "rar")
                {
                    if (Path.GetExtension(path).ToLowerInvariant() == ".zip")
                    {
                        string target = Util.Expand(string.IsNullOrEmpty(it.target) ? "%LOCALAPPDATA%\\Programs\\" + it.name : it.target);
                        Directory.CreateDirectory(target);
                        Log("> распаковка " + Path.GetFileName(path) + " -> " + target);
                        string psA = "-NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath '" +
                                     path.Replace("'", "''") + "' -DestinationPath '" + target.Replace("'", "''") + "' -Force\"";
                        ProcResult ra = Proc.Run("powershell", psA, null, tmo, null, cancelled);
                        if (ra.ExitCode != 0)
                        {
                            Log("! " + it.name + ": распаковка вернула " + ra.ExitCode);
                            return;
                        }
                        long got = 0;
                        try { got = Directory.GetFiles(Util.LongPath(target), "*", SearchOption.AllDirectories).Length; } catch { }
                        if (got > 0) Log("+ " + it.name + " распакован в " + target + " (" + got + " файлов)");
                        else Log("! " + it.name + ": архив распаковался пустым — файл повреждён или это не архив (" +
                                 Path.GetFileName(path) + ")");
                        return;
                    }
                    Log("— " + it.name + ": архив " + Path.GetFileName(path) + " не ставится автоматически (поддерживается только .zip)");
                    return;
                }

                // MSIX/APPX: обычный запуск не работает, нужен Add-AppxPackage
                if (kind == "msix")
                {
                    Log("> Add-AppxPackage " + Path.GetFileName(path));
                    string psM = "-NoProfile -ExecutionPolicy Bypass -Command \"Add-AppxPackage -Path '" + path.Replace("'", "''") + "'\"";
                    ProcResult rm = Proc.Run("powershell", psM, null, tmo, null, cancelled);
                    if (rm.ExitCode == 0) Log("+ " + it.name + " установлен");
                    else Log("! " + it.name + ": Add-AppxPackage вернул " + rm.ExitCode);
                    return;
                }

                // скрипты: .ps1 / .bat / .cmd / .reg запускаются своим интерпретатором
                if (source == "script")
                {
                    string ext1 = Path.GetExtension(path).ToLowerInvariant();
                    string exeS = null;
                    string argsS = null;
                    if (ext1 == ".ps1") { exeS = "powershell"; argsS = "-NoProfile -ExecutionPolicy Bypass -File \"" + path + "\""; }
                    else if (ext1 == ".bat" || ext1 == ".cmd") { exeS = "cmd.exe"; argsS = "/c \"" + path + "\""; }
                    else if (ext1 == ".reg") { exeS = "reg.exe"; argsS = "import \"" + path + "\""; }
                    if (exeS != null)
                    {
                        Log("> " + Path.GetFileName(path) + "  [скрипт]");
                        ProcResult rs = Proc.Run(exeS, argsS, Path.GetDirectoryName(path), tmo,
                            delegate(string line) { if (line.Trim().Length > 0) Log("   " + line); }, cancelled);
                        if (rs.ExitCode == 0) Log("+ " + it.name + " выполнен");
                        else Log("! " + it.name + ": код " + rs.ExitCode);
                        return;
                    }
                }

                Log("> " + Path.GetFileName(path) + "  [" + Detector.KindLabel(kind) + "]  " + args);
                ProcResult r = Proc.Run(exe, args, Path.GetDirectoryName(path), tmo,
                    delegate(string line) { if (line.Trim().Length > 0) Log("   " + line); }, cancelled);
                if (r.Cancelled) throw new Exception("отменено");
                if (r.ExitCode == -2)
                {
                    Log("! " + it.name + ": не ответил за " + (tmo / 1000) + " с — процесс закрыт, иду дальше");
                    return;
                }
                if (r.ExitCode == 5 || r.ExitCode == 1223)
                {
                    // Inno Setup: 5 — установка отменена (обычно уже запущен другой экземпляр),
                    // 1223 — операция отменена пользователем в UAC
                    Log("— " + it.name + ": установщик не продолжил (код " + r.ExitCode + " — занято/отменено)");
                    return;
                }
                if (r.ExitCode != 0 && r.ExitCode != 3010 && r.ExitCode != 1641 && r.ExitCode != 1638)
                    Log("! " + it.name + ": код возврата " + r.ExitCode);
                else
                    Log("+ " + it.name + " установлен");
                return;
            }

            if (source == "portable")
            {
                InstallPortable(it);
                return;
            }

            if (source == "manual")
            {
                Log("— " + it.name + ": нет установщика (ставится вручную)");
                return;
            }

            Log("— " + it.name + ": неизвестный источник '" + source + "'");
        }

        void InstallPortable(Item it)
        {
            // у найденного сканом портативного приложения файла в складе может не быть:
            // пустой filePath раньше приводил к копированию всего Bundle в папку приложения
            if (string.IsNullOrEmpty(it.filePath))
            {
                Log("— пропуск " + it.name + ": нет файла для установки (положи архив/exe в склад)");
                return;
            }
            string path = ResolvePath(it.filePath);
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                Log("— пропуск " + it.name + ": нет " + path);
                return;
            }
            string target = Util.Expand(string.IsNullOrEmpty(it.target) ? "%LOCALAPPDATA%\\Programs\\" + it.name : it.target);

            if (File.Exists(path) && Path.GetExtension(path).ToLowerInvariant() == ".zip")
            {
                Directory.CreateDirectory(target);
                Log("> распаковка " + Path.GetFileName(path) + " -> " + target);
                string ps = "-NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath '" +
                            path.Replace("'", "''") + "' -DestinationPath '" + target.Replace("'", "''") + "' -Force\"";
                ProcResult r = Proc.Run("powershell", ps, null, 0, null, cancelled);
                if (r.ExitCode != 0) Log("! " + it.name + ": распаковка вернула " + r.ExitCode);
                else Log("+ " + it.name + " распакован в " + target);
                return;
            }

            if (Directory.Exists(path))
            {
                long bytes = 0, files = 0;
                Log("> копирование " + it.name + " -> " + target);
                Util.ResetCopyStats();
                Util.CopyTree(path, target, null, null, false, ref bytes, ref files, cancelled);
                Log("+ " + it.name + " скопирован (" + files + " файлов, " + Util.FormatSize(bytes) + ")");
                string sk = Util.CopyStatsText();
                if (sk != null) Log("— " + it.name + ": " + sk);
                return;
            }

            // одиночный exe
            Directory.CreateDirectory(target);
            string dest = Path.Combine(target, Path.GetFileName(path));
            File.Copy(path, dest, true);
            Log("+ " + it.name + " -> " + dest);
        }

        public string ResolvePath(string rel)
        {
            if (string.IsNullOrEmpty(rel)) return warehouse;
            if (Path.IsPathRooted(rel)) return rel;
            return Path.Combine(warehouse, rel);
        }
    }

    // ---------------------------------------------------------------- конфиги

    public static class ConfigEngine
    {
        public static void SaveItem(Item it, string bundleDir, InstallEngine ctx)
        {
            if (it.configs == null) return;
            int idx = 0;
            foreach (ConfigEntry ce in it.configs)
            {
                idx++;
                string exp = Util.Expand(ce.path);
                string safe = Util.SafeName(ce.type + "_" + idx.ToString("00") + "_" + Path.GetFileName(exp));
                string dest = Path.Combine(bundleDir, "configs", Util.SafeName(it.id), safe);
                string tmp = dest + ".new";

                // бэкап собирается во временную папку и подменяет старую только после успеха,
                // иначе в Bundle остаются файлы, которых в приложении давно нет
                Util.RemoveDir(tmp);

                if (ce.type == "reg")
                {
                    Directory.CreateDirectory(Util.LongPath(tmp));
                    string outFile = Path.Combine(tmp, "reg.reg");
                    ProcResult r = Proc.Run("reg", "export \"" + exp + "\" \"" + outFile + "\" /y", null, 60000, null, null);
                    if (r.ExitCode == 0 && File.Exists(outFile))
                    {
                        Util.ReplaceDir(tmp, dest);
                        ctx.Log("  сохранён реестр: " + ce.path);
                    }
                    else
                    {
                        Util.RemoveDir(tmp);
                        ctx.Log("  реестр не сохранён: " + ce.path + " (код " + r.ExitCode + ")");
                    }
                }
                else if (ce.type == "file")
                {
                    if (!File.Exists(exp)) { ctx.Log("  нет файла: " + ce.path); continue; }
                    Directory.CreateDirectory(Util.LongPath(tmp));
                    File.Copy(exp, Util.LongPath(Path.Combine(tmp, Path.GetFileName(exp))), true);
                    Util.ReplaceDir(tmp, dest);
                    ctx.Log("  сохранён файл: " + ce.path);
                }
                else
                {
                    if (!Directory.Exists(exp)) { ctx.Log("  нет папки: " + ce.path); continue; }
                    Directory.CreateDirectory(Util.LongPath(tmp));
                    long bytes = 0, files = 0;
                    bool selective = ce.mode != null && ce.mode.ToLowerInvariant() == "selective";
                    Util.ResetCopyStats();
                    Util.CopyTree(exp, tmp, ce.include, ce.exclude, selective, ref bytes, ref files, ctx.cancelled);
                    if (ctx.cancelled != null && ctx.cancelled())
                    {
                        Util.RemoveDir(tmp);
                        ctx.LogQuiet("  — отменено, прежний бэкап оставлен: " + ce.path);
                        return;
                    }
                    if (files > 0)
                    {
                        Util.ReplaceDir(tmp, dest);
                        ctx.Log("  сохранено: " + ce.path + " (" + files + " файлов, " + Util.FormatSize(bytes) + ")");
                    }
                    else
                    {
                        Util.RemoveDir(tmp);
                        ctx.Log("  ! " + ce.path + ": скопировано 0 файлов, старый бэкап оставлен");
                    }
                    string sk = Util.CopyStatsText();
                    if (sk != null) ctx.Log("  — пропущено: " + ce.path + ": " + sk);
                }
            }
        }

        // действия до/после восстановления настроек: закрыть/перезапустить службы и процессы
        // (нужно, например, Windhawk: моды применяются только после рестарта службы и explorer,
        //  а до восстановления explorer держит файлы модов)
        public static void RunPreActions(Item it, InstallEngine ctx)
        {
            RunActions(it.preRestore, "до", ctx);
        }

        public static void RunPostActions(Item it, InstallEngine ctx)
        {
            RunActions(it.postRestore, "после", ctx);
        }

        static void RunActions(List<PostAction> list, string phase, InstallEngine ctx)
        {
            if (list == null || list.Count == 0) return;
            for (int i = 0; i < list.Count; i++)
            {
                PostAction a = list[i];
                if (a == null || string.IsNullOrEmpty(a.name)) continue;
                int wait = a.waitSec > 0 ? a.waitSec : 3;
                string type = a.type == null ? "" : a.type.ToLowerInvariant();
                string exeName = a.name.ToLowerInvariant().EndsWith(".exe") ? a.name : a.name + ".exe";
                try
                {
                    if (type == "kill")
                    {
                        ctx.LogQuiet("  > закрываю " + exeName + " (" + phase + " восстановления)");
                        Proc.Run("taskkill.exe", "/f /im \"" + exeName + "\"", null, 60000, null, null);
                        System.Threading.Thread.Sleep(wait * 1000);
                    }
                    else if (type == "service-stop")
                    {
                        ctx.LogQuiet("  > останавливаю службу: " + a.name);
                        ProcResult r0 = Proc.Run("sc.exe", "stop \"" + a.name + "\"", null, 120000, null, null);
                        if (r0.ExitCode == 0) ctx.LogQuiet("  + служба остановлена: " + a.name);
                        else ctx.LogQuiet("  ! служба " + a.name + ": код " + r0.ExitCode);
                        System.Threading.Thread.Sleep(wait * 1000);
                    }
                    else if (type == "service")
                    {
                        ctx.LogQuiet("  > перезапуск службы: " + a.name);
                        Proc.Run("sc.exe", "stop \"" + a.name + "\"", null, 120000, null, null);
                        System.Threading.Thread.Sleep(wait * 1000);
                        ProcResult r = Proc.Run("sc.exe", "start \"" + a.name + "\"", null, 120000, null, null);
                        if (r.ExitCode == 0) ctx.LogQuiet("  + служба перезапущена: " + a.name);
                        else ctx.LogQuiet("  ! служба " + a.name + ": код " + r.ExitCode);
                    }
                    else if (type == "restart")
                    {
                        ctx.LogQuiet("  > перезапуск: " + exeName);
                        Proc.Run("taskkill.exe", "/f /im \"" + exeName + "\"", null, 60000, null, null);
                        System.Threading.Thread.Sleep(wait * 1000);
                        StartDetached(exeName);
                        System.Threading.Thread.Sleep(5000);
                        string bare = exeName.Substring(0, exeName.Length - 4);
                        if (System.Diagnostics.Process.GetProcessesByName(bare).Length == 0)
                        {
                            ctx.LogQuiet("  ! " + exeName + " не поднялся сам, пробую ещё раз");
                            StartDetached(exeName);
                            System.Threading.Thread.Sleep(5000);
                        }
                        if (System.Diagnostics.Process.GetProcessesByName(bare).Length > 0) ctx.LogQuiet("  + перезапущен: " + exeName);
                        if (bare == "explorer" && !Util.ShellTrayExists())
                        {
                            System.Threading.Thread.Sleep(4000);
                            if (!Util.ShellTrayExists())
                                ctx.LogQuiet("  ! shell (Shell_TrayWnd) not found after explorer restart - a Windhawk mod probably broke it; disable mods and start explorer.exe manually");
                        }
                        else ctx.LogQuiet("  ! " + exeName + " не запустился — запусти вручную");
                    }
                    else if (type == "start")
                    {
                        StartDetached(exeName);
                        ctx.LogQuiet("  + запущен: " + exeName);
                    }
                }
                catch (Exception ex)
                {
                    ctx.LogQuiet("  ! действие " + phase + " [" + a.name + "]: " + ex.Message);
                }
            }
        }

        static void StartDetached(string exeName)
        {
            try
            {
                System.Diagnostics.Process p = new System.Diagnostics.Process();
                p.StartInfo.FileName = exeName;
                p.StartInfo.UseShellExecute = true;
                p.Start();
            }
            catch
            {
                Proc.Run("cmd.exe", "/c start \"\" \"" + exeName + "\"", null, 30000, null, null);
            }
        }

        public static void RestoreItem(Item it, string bundleDir, InstallEngine ctx)
        {
            if (it.configs == null) return;
            int idx = 0;
            foreach (ConfigEntry ce in it.configs)
            {
                idx++;
                string exp = Util.Expand(ce.path);
                string safe = Util.SafeName(ce.type + "_" + idx.ToString("00") + "_" + Path.GetFileName(exp));
                string src = Path.Combine(bundleDir, "configs", Util.SafeName(it.id), safe);
                if (!Directory.Exists(src)) { ctx.Log("  нет бэкапа: " + ce.path); continue; }

                if (ce.type == "reg")
                {
                    string regFile = Path.Combine(src, "reg.reg");
                    if (!File.Exists(regFile)) { ctx.Log("  нет reg-файла: " + ce.path); continue; }
                    ProcResult r = Proc.Run("reg", "import \"" + regFile + "\"", null, 60000, null, null);
                    if (r.ExitCode == 0) ctx.Log("  восстановлен реестр: " + ce.path);
                    else ctx.Log("  реестр не восстановлен: " + ce.path + " (код " + r.ExitCode + ")");
                }
                else if (ce.type == "file")
                {
                    string[] files = Directory.GetFiles(src);
                    string wantedName = Path.GetFileName(exp);
                    for (int fi = 0; fi < files.Length; fi++)
                        if (string.Equals(Path.GetFileName(files[fi]), wantedName, StringComparison.OrdinalIgnoreCase)) { files[0] = files[fi]; break; }
                    if (files.Length == 0) { ctx.Log("  пусто: " + ce.path); continue; }
                    int kf = Util.KillProcessesIn(Path.GetDirectoryName(exp));
                    if (kf > 0) ctx.Log("  остановлено процессов: " + kf + " (" + ce.path + ")");
                    Directory.CreateDirectory(Util.LongPath(Path.GetDirectoryName(exp)));
                    File.Copy(files[0], Util.LongPath(exp), true);
                    ctx.Log("  восстановлен файл: " + ce.path);
                }
                else
                {
                    long bytes = 0, files = 0;
                    int kd = Util.KillProcessesIn(exp);
                    if (kd > 0) ctx.Log("  остановлено процессов: " + kd + " (" + ce.path + ")");
                    Util.ResetCopyStats();
                    // при восстановлении обычно копируем ВСЁ, что лежит в бэкапе:
                    // include/exclude — это фильтр сохранения, а не восстановления.
                    // исключение — restoreSkip: папка совпадает с папкой установки,
                    // и свежие файлы программы перезаписывать старыми нельзя
                    Util.CopyTree(src, exp, null, ce.restoreSkip, false, ref bytes, ref files, ctx.cancelled);
                    ctx.Log("  восстановлено: " + ce.path + " (" + files + " файлов, " + Util.FormatSize(bytes) + ")");
                    string sk = Util.CopyStatsText();
                    if (sk != null) ctx.Log("  — пропущено: " + ce.path + ": " + sk);
                }
            }
        }
    }
}
