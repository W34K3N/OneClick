using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace WinSetup
{
    // ------------------------------------------------------------ авто-режим
    //
    // "Старая Windows": сканирует установленные приложения, сам находит пути
    // конфигов, скачивает установщики в Bundle и пишет Bundle\bundle.json.
    //
    // "Новая Windows": читает bundle.json и ставит всё одним кликом.

    public static class AutoSetup
    {
        // ---------------------------------------------------------- winget export

        public class ExportPkg
        {
            public string PackageIdentifier { get; set; }
            public string Version { get; set; }
        }

        public class ExportSource
        {
            public List<ExportPkg> Packages { get; set; }
        }

        public class ExportFile
        {
            public List<ExportSource> Sources { get; set; }
            public List<ExportPkg> Packages { get; set; }   // на всякий случай (старый формат)
        }

        // ---------------------------------------------------------- скан ПК

        public static List<Item> Scan(Action<string> log)
        {
            return Scan(log, null);
        }

        public static List<Item> Scan(Action<string> log, string warehouse)
        {
            List<Item> list = new List<Item>();
            List<Item> wingetItems = new List<Item>();
            Dictionary<string, Item> byWinget = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, Item> byName = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);

            string tmp = Path.Combine(Path.GetTempPath(), "winsetup_export.json");
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }

            log("winget: читаю список установленных пакетов...");
            Proc.Run("winget", "export -o \"" + tmp + "\" --include-versions --accept-source-agreements", null, 240000, null, null);

            ExportFile ef = Json.Load<ExportFile>(tmp, null);
            List<ExportPkg> pkgs = new List<ExportPkg>();
            if (ef != null)
            {
                if (ef.Packages != null) pkgs.AddRange(ef.Packages);
                if (ef.Sources != null)
                {
                    for (int s = 0; s < ef.Sources.Count; s++)
                        if (ef.Sources[s] != null && ef.Sources[s].Packages != null) pkgs.AddRange(ef.Sources[s].Packages);
                }
            }
            if (pkgs.Count == 0) log("  ! winget export не вернул пакетов");

            for (int i = 0; i < pkgs.Count; i++)
            {
                string wid = pkgs[i].PackageIdentifier;
                if (string.IsNullOrEmpty(wid)) continue;
                if (byWinget.ContainsKey(wid)) continue;

                Item it = new Item();
                it.id = Util.SafeName(wid);
                it.name = NiceName(wid);
                it.source = "winget";
                it.wingetId = wid;
                it.stage = StageFor(wid, it.name);
                it.enabled = true;
                it.notes = pkgs[i].Version;
                it.installerState = "?";
                list.Add(it);
                wingetItems.Add(it);

                byWinget[wid] = it;
                byName[Norm(it.name)] = it;
            }
            log("пакетов из winget: " + byWinget.Count);

            int regAdded = 0;
            AddFromUninstall(RegistryHive.LocalMachine, RegistryView.Registry64, byName, list, wingetItems, log, ref regAdded);
            AddFromUninstall(RegistryHive.LocalMachine, RegistryView.Registry32, byName, list, wingetItems, log, ref regAdded);
            AddFromUninstall(RegistryHive.CurrentUser, RegistryView.Default, byName, list, wingetItems, log, ref regAdded);
            log("дополнительно из реестра (не winget): " + regAdded);

            // для приложений не из winget пробуем подобрать id по названию
            HashSet<string> usedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < list.Count; i++)
                if (!string.IsNullOrEmpty(list[i].wingetId)) usedIds.Add(list[i].wingetId);

            int pending = 0;
            for (int i = 0; i < list.Count; i++) if (list[i].source == "manual") pending++;
            if (pending > 0)
            {
                log("winget: подбираю установщики для " + pending + " приложений (до пары минут)...");
                int resolved = 0;
                for (int i = 0; i < list.Count; i++)
                {
                    Item it = list[i];
                    if (it.source != "manual") continue;

                    string wid = ResolveWinget(it.name, usedIds, log);
                    if (string.IsNullOrEmpty(wid)) continue;

                    it.source = "winget";
                    it.wingetId = wid;
                    it.installerState = "?";
                    it.enabled = true;
                    usedIds.Add(wid);
                    resolved++;
                    log("  + " + it.name + " -> " + wid);
                }
                log("подобрано установщиков: " + resolved + " из " + pending);
            }

            // портативные приложения (GitHub-сборки вроде G-Helper): в реестре их нет
            int portable = 0;
            AddPortableInstalled(list, byName, log, ref portable);
            log("портативных приложений (без установщика): " + portable);

            // файлы в складе: подвязываем к найденным пунктам или добавляем новыми
            int attached = 0;
            int fromWarehouse = 0;
            MatchWarehouse(list, byName, warehouse, log, ref attached, ref fromWarehouse);
            log("из склада подвязано файлов: " + attached + ", новых пунктов: " + fromWarehouse);

            // стабильная сортировка по этапу
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

        static void AddFromUninstall(RegistryHive hive, RegistryView view,
                                     Dictionary<string, Item> byName, List<Item> list,
                                     List<Item> wingetItems, Action<string> log, ref int added)
        {
            RegistryKey un = null;
            try
            {
                RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, view);
                un = baseKey.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall");
                baseKey.Close();
                if (un == null) return;

                string[] subKeys = un.GetSubKeyNames();
                for (int i = 0; i < subKeys.Length; i++)
                {
                    RegistryKey k = null;
                    try
                    {
                        k = un.OpenSubKey(subKeys[i]);
                        if (k == null) continue;

                        string disp = k.GetValue("DisplayName") as string;
                        if (string.IsNullOrEmpty(disp)) continue;

                        object sysc = k.GetValue("SystemComponent");
                        if (sysc != null && Convert.ToInt32(sysc) == 1) continue;
                        if (!string.IsNullOrEmpty(k.GetValue("ParentKeyName") as string)) continue;
                        if (!string.IsNullOrEmpty(k.GetValue("ReleaseType") as string)) continue;
                        if (IsNoise(disp)) continue;
                        if (IsGame(k, subKeys[i], disp, log)) continue;

                        string clean = CleanName(disp);
                        if (clean.Length < 2) continue;
                        string key = Norm(clean);
                        string ver = k.GetValue("DisplayVersion") as string;

                        if (byName.ContainsKey(key))
                        {
                            Item exist = byName[key];
                            if (string.IsNullOrEmpty(exist.notes)) exist.notes = ver;
                            continue;
                        }

                        Item loose = LooseMatch(wingetItems, clean);
                        if (loose != null)
                        {
                            if (string.IsNullOrEmpty(loose.notes)) loose.notes = ver;
                            continue;
                        }

                        Item it = new Item();
                        it.id = Util.SafeName(clean);
                        it.name = clean;
                        it.source = "manual";
                        it.stage = StageFor("", clean);
                        it.enabled = false;              // без winget-идентификатора не установить
                        it.notes = ver;
                        it.installerState = "нет";
                        list.Add(it);
                        byName[key] = it;
                        added++;
                    }
                    catch { }
                    finally { if (k != null) { try { k.Close(); } catch { } } }
                }
            }
            catch (Exception ex) { log("реестр: " + ex.Message); }
            finally { if (un != null) { try { un.Close(); } catch { } } }
        }

        // подбор winget-идентификатора по названию приложения (для тех, кого нет в export)
        static string ResolveWinget(string name, HashSet<string> used, Action<string> log)
        {
            if (string.IsNullOrEmpty(name)) return null;
            string want = Norm(name);
            if (want.Length < 3) return null;

            ProcResult r = Proc.Run("winget",
                "search --name \"" + name + "\" --source winget --accept-source-agreements --disable-interactivity",
                null, 90000, null, null);
            if (r == null || string.IsNullOrEmpty(r.Output)) return null;

            string[] lines = r.Output.Replace("\r", "").Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();
                if (ln.Length < 10) continue;
                if (ln.IndexOf("---") >= 0) continue;
                if (ln.StartsWith("Name")) continue;

                string[] parts = System.Text.RegularExpressions.Regex.Split(ln, "\\s{2,}");
                if (parts.Length < 3) continue;

                string nm = parts[0].Trim();
                string id = parts[1].Trim();
                if (id.IndexOf('.') < 0) continue;          // id winget содержат точку
                if (id.StartsWith("ARP")) continue;
                if (used != null && used.Contains(id)) continue;

                // только точное совпадение названия: иначе легко подхватить чужой пакет
                if (Norm(nm) != want) continue;

                // не ставим второй пакет того же семейства (Node.js LTS vs Node.js)
                bool family = false;
                if (used != null)
                {
                    foreach (string u in used)
                        if (SameFamily(id, u)) { family = true; break; }
                }
                if (family) continue;

                return id;
            }
            return null;
        }

        static bool SameFamily(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            string[] pa = a.Split('.');
            string[] pb = b.Split('.');
            if (pa.Length < 2 || pb.Length < 2) return false;
            return string.Equals(pa[0], pb[0], StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(pa[1], pb[1], StringComparison.OrdinalIgnoreCase);
        }

        // одно и то же приложение под разными названиями (winget id vs реестр)
        static Item LooseMatch(List<Item> wingetItems, string regName)
        {
            string rn = Norm(regName);
            if (rn.Length < 3) return null;

            for (int i = 0; i < wingetItems.Count; i++)
            {
                Item w = wingetItems[i];
                if (string.IsNullOrEmpty(w.wingetId)) continue;
                string wn = Norm(LastSeg(w.wingetId));
                string wi = Norm(w.wingetId);
                if (wn.Length < 3 && wi.Length < 4) continue;

                if (rn == wn || rn == wi) return w;
                if (rn.Length >= 4)
                {
                    if (wn.Length >= 4 && (rn.IndexOf(wn) >= 0 || wn.IndexOf(rn) >= 0)) return w;
                    if (wi.Length >= 6 && (rn.IndexOf(wi) >= 0 || wi.IndexOf(rn) >= 0))
                    {
                        if (!EditionMismatch(wi, rn)) return w;
                    }
                }

                if (wi.IndexOf("vcredist") >= 0 && rn.IndexOf("visualc") >= 0) return w;
                if (wi.IndexOf("vclibs") >= 0 && rn.IndexOf("vclibs") >= 0) return w;
                if (wi.IndexOf("uixaml") >= 0 && rn.IndexOf("uixaml") >= 0) return w;
                if (wi.IndexOf("windowsappruntime") >= 0 && rn.IndexOf("windowsappruntime") >= 0) return w;
                if (wi.IndexOf("directx") >= 0 && rn.IndexOf("directx") >= 0) return w;
                if (wi.IndexOf("appinstaller") >= 0 && rn.IndexOf("appinstaller") >= 0) return w;
                if (wi.IndexOf("terminal") >= 0 && rn.IndexOf("terminal") >= 0) return w;
                if (wi.IndexOf("dotnet") >= 0 && rn.IndexOf("dotnet") >= 0) return w;
            }
            return null;
        }

        // другое издание того же продукта (Canary/Beta/Portable) — не склеиваем
        static bool EditionMismatch(string wi, string rn)
        {
            string[] risky = new string[] {
                "canary", "beta", "nightly", "preview", "insider", "portable",
                "classic", "remotedesktop", "remote", "console", "server"
            };
            for (int i = 0; i < risky.Length; i++)
            {
                if (wi.IndexOf(risky[i]) >= 0 && rn.IndexOf(risky[i]) < 0) return true;
            }
            return false;
        }

        // известные синонимы: id winget -> как называется папка/ключ настроек
        static string[] AliasesFor(string wingetId)
        {
            if (string.IsNullOrEmpty(wingetId)) return new string[0];
            string s = wingetId.ToLowerInvariant();

            if (s.IndexOf("7zip") >= 0) return new string[] { "7-Zip" };
            if (s.IndexOf("winrar") >= 0) return new string[] { "WinRAR", "RARLab" };
            if (s.IndexOf("zen-team") >= 0) return new string[] { "Zen", "zen-browser" };
            if (s.IndexOf("sunshine") >= 0) return new string[] { "Sunshine" };
            if (s.IndexOf("windhawk") >= 0) return new string[] { "Windhawk" };
            if (s.IndexOf("steam") >= 0) return new string[] { "Steam" };
            if (s.IndexOf("ubisoft") >= 0) return new string[] { "Ubisoft", "Ubisoft Connect" };
            if (s.IndexOf("playnite") >= 0) return new string[] { "Playnite" };
            if (s.IndexOf("cloudflare") >= 0) return new string[] { "Cloudflare", "WARP" };
            if (s.IndexOf("revouninstaller") >= 0) return new string[] { "Revo Uninstaller Pro", "RevoUninstaller" };
            if (s.IndexOf("windowsterminal") >= 0) return new string[] { "Windows Terminal" };
            if (s.IndexOf("everything") >= 0) return new string[] { "Everything" };
            if (s.IndexOf("powertoys") >= 0) return new string[] { "PowerToys", "Microsoft\\PowerToys" };
            if (s.IndexOf("ghelper") >= 0 || s.IndexOf("g-helper") >= 0) return new string[] { "GHelper", "G-Helper" };
            if (s.IndexOf("nodejs") >= 0) return new string[] { "npm", "Node.js" };
            if (s.IndexOf("dlss") >= 0) return new string[] { "DLSS Swapper" };
            if (s.IndexOf("openal") >= 0) return new string[] { "OpenAL" };
            if (s.IndexOf("warp") >= 0) return new string[] { "Cloudflare", "WARP" };
            return new string[0];
        }

        // мусор внутри папок настроек: кэши, крэш-дампы, бинарники Electron
        static List<string> JunkExcludes()
        {
            return new List<string>(new string[] {
                "app-*", "Cache", "cache", "Cache2", "CachedData", "Code Cache", "GPUCache",
                "ShaderCache", "GrShaderCache", "DawnGraphiteCache", "DawnWebGPUCache",
                "logs", "log", "Crashpad", "crashpad", "CrashReports",
                "temp", "Temp", "tmp", "Blob Storage", "Service Worker",
                "*.log", "*.tmp", "*.dmp", "*.etl", "*.exe", "*.dll", "*.pak", "*.node",
                "log.txt", "*.bak", "*.bak-*", "*.backup", "*.backup-*", "*.old"
            });
        }

        // ---------------------------------------------- портативные приложения (GitHub)

        static bool IsSystemFolderName(string dirName)
        {
            string s = Norm(dirName);
            if (s.Length < 2) return true;
            string[] skip = new string[] {
                "windows", "microsoft", "commonfiles", "internetexplorer", "msbuild", "dotnet",
                "nvidia", "amd", "intel", "steam", "epicgames", "modifiablewindowsapps",
                "reference", "uninstall", "temp"
            };
            for (int i = 0; i < skip.Length; i++)
                if (s.StartsWith(skip[i])) return true;
            return false;
        }

        static string FindMainExe(string dir)
        {
            string[] files;
            try { files = Directory.GetFiles(dir, "*.exe"); } catch { return null; }
            string best = null;
            long bestLen = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string n = Path.GetFileNameWithoutExtension(files[i]).ToLowerInvariant();
                if (n.StartsWith("unins") || n.StartsWith("setup") || n.StartsWith("install") ||
                    n.StartsWith("update") || n.StartsWith("vc_redist") || n.StartsWith("uninstall")) continue;
                long len = 0;
                try { len = new FileInfo(files[i]).Length; } catch { }
                if (len > bestLen) { bestLen = len; best = files[i]; }
            }
            if (best == null || bestLen < 100000) return null;
            return best;
        }

        // "Easy Anti Cheat" -> "Easy Anti Cheat", "G-Aether" -> "G Aether"
        static string CleanDisplayName(string raw)
        {
            string s = SplitCamel(raw);
            s = s.Replace("_", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, "\\s*-+\\s*", " ");
            s = System.Text.RegularExpressions.Regex.Replace(s, "\\s{2,}", " ");
            return s.Trim(' ', '-');
        }

        static bool IsNoiseExe(string exeName)
        {
            string n = Norm(exeName);
            string[] bad = new string[] {
                "anticheat", "easyanti", "battleye", "service", "maintenance", "crashhandler",
                "unitycrash", "updater", "bootstrap", "report", "eos"
            };
            for (int i = 0; i < bad.Length; i++)
                if (n.IndexOf(bad[i]) >= 0) return true;
            return false;
        }

        // в папке лежит деинсталлятор -> приложение ставилось установщиком и уже есть в реестре
        static bool HasUninstaller(string dir)
        {
            try
            {
                string[] files = Directory.GetFiles(dir, "*.exe");
                for (int i = 0; i < files.Length; i++)
                {
                    string n = Path.GetFileName(files[i]).ToLowerInvariant();
                    if (n.StartsWith("unins") || n.StartsWith("uninstall") || n.StartsWith("unwise")) return true;
                }
            }
            catch { }
            return false;
        }

        // приложение, установленное без установщика (GitHub-сборка): папка с одним главным exe
        static void AddPortableInstalled(List<Item> list, Dictionary<string, Item> byName,
                                         Action<string> log, ref int added)
        {
            string[] roots = new string[] { "%PROGRAMFILES%", "%PROGRAMFILES(X86)%", "%LOCALAPPDATA%\\Programs" };
            for (int r = 0; r < roots.Length; r++)
            {
                string root = Util.Expand(roots[r]);
                if (!Directory.Exists(root)) continue;

                string[] dirs;
                try { dirs = Directory.GetDirectories(root); } catch { continue; }
                for (int i = 0; i < dirs.Length; i++)
                {
                    string dirName = Path.GetFileName(dirs[i]);
                    if (IsSystemFolderName(dirName)) continue;

                    string key = Norm(dirName);
                    if (key.Length < 3) continue;
                    if (byName.ContainsKey(key)) continue;
                    if (LooseMatch(list, dirName) != null) continue;

                    string exe = FindMainExe(dirs[i]);
                    if (exe == null) continue;

                    string exeBase = Path.GetFileNameWithoutExtension(exe);
                    if (IsNoiseExe(exeBase)) continue;
                    if (HasUninstaller(dirs[i])) continue;          // ставилось установщиком, есть в реестре

                    string exeKey = Norm(exeBase);
                    if (exeKey.IndexOf(key) < 0 && key.IndexOf(exeKey) < 0) continue;

                    string shown = CleanDisplayName(exeBase);
                    if (shown.Length < 2) continue;
                    if (FindItemByName(list, shown) != null) continue;   // уже есть из реестра

                    Item it = new Item();
                    it.id = Util.SafeName(dirName);
                    it.name = shown;
                    it.source = "portable";
                    it.target = dirs[i];
                    it.stage = StageFor("", it.name);
                    it.enabled = false;
                    it.installerState = "нет файла";
                    it.notes = "портативная сборка";
                    list.Add(it);
                    byName[key] = it;
                    byName[Norm(it.name)] = it;
                    added++;
                }
            }
        }

        // -------------------------------------------------- склад: файлы установщиков

        static string SplitCamel(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(s[i - 1]) && s[i - 1] != ' ')
                    sb.Append(' ');
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        // "DiscordSetup.exe" -> "Discord", "Cloudflare_WARP_2026.6.880.0.msi" -> "Cloudflare WARP"
        public static string WareName(string fileName)
        {
            string s = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(s)) return s;

            s = System.Text.RegularExpressions.Regex.Replace(s, "(?i)(setup|installer|install|portable|bundled)", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, "\\s*[-_]?v?\\d+(\\.\\d+)+.*$", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, "(?i)(win-?x64|win-?x86|en-us|-?x64|-?x86|beta)", "");
            s = System.Text.RegularExpressions.Regex.Replace(s, "\\d{3,}$", "");
            s = s.Replace('_', ' ').Replace('.', ' ').Replace('-', ' ');
            s = System.Text.RegularExpressions.Regex.Replace(s, "\\s{2,}", " ");
            s = SplitCamel(s);
            return s.Trim();
        }

        static Item FindItemByName(List<Item> list, string name)
        {
            string key = Norm(name);
            if (key.Length < 3) return null;
            for (int i = 0; i < list.Count; i++)
                if (Norm(list[i].name) == key) return list[i];
            for (int i = 0; i < list.Count; i++)
            {
                string k = Norm(list[i].name);
                if (k.Length < 5) continue;
                if (k.IndexOf(key) >= 0 || key.IndexOf(k) >= 0) return list[i];
            }
            return null;
        }

        static void MatchWarehouse(List<Item> list, Dictionary<string, Item> byName, string warehouse,
                                   Action<string> log, ref int attached, ref int added)
        {
            if (string.IsNullOrEmpty(warehouse) || !Directory.Exists(warehouse)) return;

            string[] files;
            try { files = Directory.GetFiles(warehouse); } catch { return; }

            for (int i = 0; i < files.Length; i++)
            {
                string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                if (ext != ".exe" && ext != ".msi" && ext != ".msix" && ext != ".zip" && ext != ".7z") continue;

                string fileName = Path.GetFileName(files[i]);
                string nm = WareName(fileName);
                string key = Norm(nm);
                if (key.Length < 3) continue;
                if (key.IndexOf("winsetup") >= 0) continue;

                bool archive = ext == ".zip" || ext == ".7z";
                Item hit = FindItemByName(list, nm);

                if (hit != null)
                {
                    if (hit.source == "winget") continue;          // winget-пункты ставим из интернета
                    if (!string.IsNullOrEmpty(hit.filePath)) continue;

                    hit.filePath = fileName;
                    if (archive)
                    {
                        hit.source = "portable";
                        if (string.IsNullOrEmpty(hit.target))
                            hit.target = "%LOCALAPPDATA%\\Programs\\" + Util.SafeName(hit.name);
                    }
                    else if (hit.source != "portable") hit.source = "file";

                    hit.enabled = true;
                    hit.installerState = "в складе";
                    attached++;
                    continue;
                }

                Item it = new Item();
                it.id = Util.SafeName(nm);
                it.name = nm;
                it.filePath = fileName;
                it.source = archive ? "portable" : "file";
                if (archive) it.target = "%LOCALAPPDATA%\\Programs\\" + Util.SafeName(nm);
                it.stage = StageFor("", nm);
                it.enabled = true;
                it.installerState = "в складе";
                list.Add(it);
                byName[key] = it;
                added++;
            }
        }

        // ---------------------------------------------------------- поиск конфигов

        public static List<ConfigEntry> FindConfigs(Item it, Action<string> log)
        {
            List<ConfigEntry> res = new List<ConfigEntry>();
            List<string> names = new List<string>();
            AddName(names, it.name);
            if (!string.IsNullOrEmpty(it.wingetId)) AddName(names, LastSeg(it.wingetId));
            string[] alias = AliasesFor(it.wingetId);
            for (int a = 0; a < alias.Length; a++) AddName(names, alias[a]);

            string[] dirRoots = new string[] { "%APPDATA%", "%LOCALAPPDATA%", "%PROGRAMDATA%" };
            string[] regRoots = new string[] { "HKCU\\Software", "HKLM\\Software", "HKLM\\Software\\WOW6432Node" };
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int n = 0; n < names.Count; n++)
            {
                if (res.Count >= 8) break;
                string nm = names[n];

                // %LOCALAPPDATA% у per-user программ (Electron и т.п.) — это сами бинарники,
                // настройки лежат в %APPDATA%. Если там одноимённая папка есть — local пропускаем.
                bool roamingTwin = Directory.Exists(Util.Expand("%APPDATA%\\" + nm));

                for (int d = 0; d < dirRoots.Length; d++)
                {
                    if (res.Count >= 8) break;
                    string rel = dirRoots[d] + "\\" + nm;
                    string full = Util.Expand(rel);
                    if (!Directory.Exists(full)) continue;
                    if (d == 1 && roamingTwin) continue;
                    if (!seen.Add(full)) continue;

                    ConfigEntry ce = new ConfigEntry();
                    ce.path = rel;
                    ce.type = "dir";
                    ce.mode = "selective";
                    ce.exclude = JunkExcludes();
                    res.Add(ce);
                }

                for (int r = 0; r < regRoots.Length; r++)
                {
                    if (res.Count >= 8) break;
                    string rel = regRoots[r] + "\\" + nm;
                    if (!RegExists(rel)) continue;
                    if (!seen.Add(rel)) continue;

                    ConfigEntry ce = new ConfigEntry();
                    ce.path = rel;
                    ce.type = "reg";
                    ce.mode = "full";
                    res.Add(ce);
                }
            }
            return res;
        }

        static void AddName(List<string> names, string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            string n = raw.Trim();
            if (n.Length < 2) return;
            for (int i = 0; i < names.Count; i++)
                if (string.Equals(names[i], n, StringComparison.OrdinalIgnoreCase)) return;
            names.Add(n);

            if (n.IndexOf(' ') > 0)
            {
                string noSpace = n.Replace(" ", "");
                if (noSpace.Length >= 2) names.Add(noSpace);
            }

            // G-Helper -> GHelper, Revo Uninstaller Pro -> Revo-Uninstaller-Pro
            string squeezed = n.Replace(" ", "").Replace("-", "").Replace(".", "");
            if (squeezed.Length >= 2 && !string.Equals(squeezed, n, StringComparison.OrdinalIgnoreCase))
                names.Add(squeezed);
        }

        static bool RegExists(string path)
        {
            try
            {
                int i = path.IndexOf('\\');
                if (i <= 0) return false;
                string hive = path.Substring(0, i).ToUpperInvariant();
                string sub = path.Substring(i + 1);

                RegistryHive h;
                if (hive == "HKCU") h = RegistryHive.CurrentUser;
                else if (hive == "HKLM") h = RegistryHive.LocalMachine;
                else return false;

                RegistryView[] views = new RegistryView[] { RegistryView.Registry64, RegistryView.Registry32 };
                for (int v = 0; v < views.Length; v++)
                {
                    RegistryKey bk = null;
                    RegistryKey k = null;
                    try
                    {
                        bk = RegistryKey.OpenBaseKey(h, views[v]);
                        k = bk.OpenSubKey(sub);
                        if (k != null) return true;
                    }
                    catch { }
                    finally
                    {
                        if (k != null) { try { k.Close(); } catch { } }
                        if (bk != null) { try { bk.Close(); } catch { } }
                    }
                }
                return false;
            }
            catch { return false; }
        }

        // ---------------------------------------------------------- сборка Bundle

        public static Manifest Build(List<Item> items, string bundleDir, string warehouse, Reporter report,
                                     Func<bool> cancelled, out int instTotal)
        {
            instTotal = 0;
            Manifest m = new Manifest();
            m.version = 1;

            InstallEngine ctx = new InstallEngine();
            ctx.report = report;
            ctx.bundleDir = bundleDir;

            int total = items.Count;
            int done = 0;

            for (int i = 0; i < total; i++)
            {
                if (cancelled != null && cancelled()) break;
                Item it = items[i];
                report(total == 0 ? 0 : (done * 100) / total, it.name + " — сборка", null);

                Item copy = Clone(it);
                string inst = null;
                string instKind = null;
                string instSilent = null;
                if (!string.IsNullOrEmpty(it.wingetId)) inst = DownloadInstaller(it, bundleDir, report, out instKind, out instSilent);
                else inst = CopyLocalInstaller(it, warehouse, bundleDir, report);

                if (inst != null)
                {
                    if (copy.source != "portable" && copy.source != "script") copy.source = "file";
                    copy.filePath = Rel(bundleDir, inst);
                    copy.silentArgs = instSilent;
                    copy.kind = instKind;
                    copy.installerState = "скачан";
                    instTotal++;
                }
                else if (!string.IsNullOrEmpty(it.wingetId))
                {
                    copy.source = "winget";
                    copy.installerState = "из интернета";
                }
                else
                {
                    copy.source = it.source == "portable" ? "portable" : "manual";
                    copy.installerState = it.source == "portable" ? "нет файла" : "нет";
                }

                if (copy.configs != null && copy.configs.Count > 0)
                {
                    ConfigEngine.SaveItem(copy, bundleDir, ctx);
                }

                m.items.Add(copy);
                done++;
                report(total == 0 ? 0 : (done * 100) / total, it.name + " — готово", null);
            }

            Json.Save(Path.Combine(bundleDir, "bundle.json"), m);
            return m;
        }

        // локальный установщик/архив из склада -> в Bundle (чтобы Bundle был самодостаточным)
        static string CopyLocalInstaller(Item it, string warehouse, string bundleDir, Reporter report)
        {
            if (string.IsNullOrEmpty(it.filePath)) return null;

            string src = it.filePath;
            if (!Path.IsPathRooted(src))
                src = Path.Combine(warehouse == null ? "" : warehouse, it.filePath);

            string dir = Path.Combine(bundleDir, "installers", Util.SafeName(it.id));
            try { Directory.CreateDirectory(dir); } catch { return null; }

            try
            {
                if (File.Exists(src))
                {
                    string dest = Path.Combine(dir, Path.GetFileName(src));
                    report(-1, null, "> копирую в Bundle: " + Path.GetFileName(src) +
                                     " (" + Util.FormatSize(new FileInfo(src).Length) + ")");
                    File.Copy(src, dest, true);

                    // манифест тихих ключей, если он лежит рядом
                    string ym = Path.ChangeExtension(src, ".yaml");
                    if (File.Exists(ym))
                    {
                        try { File.Copy(ym, Path.Combine(dir, Path.GetFileName(ym)), true); } catch { }
                    }
                    return dest;
                }

                if (Directory.Exists(src))
                {
                    string destDir = Path.Combine(dir, Path.GetFileName(src));
                    long bytes = 0;
                    long files = 0;
                    report(-1, null, "> копирую папку в Bundle: " + Path.GetFileName(src));
                    Util.ResetCopyStats();
                    Util.CopyTree(src, destDir, null, null, false, ref bytes, ref files, null);
                    report(-1, null, "  + " + files + " файлов, " + Util.FormatSize(bytes));
                    string sk = Util.CopyStatsText();
                    if (sk != null) report(-1, null, "  ! " + sk);
                    return destDir;
                }

                report(-1, null, "  ! файла нет в складе: " + it.filePath);
            }
            catch (Exception ex)
            {
                report(-1, null, "  ! не скопировалось: " + ex.Message);
            }
            return null;
        }

        static string DownloadInstaller(Item it, string bundleDir, Reporter report,
                                        out string kind, out string silent)
        {
            kind = null;
            silent = null;
            if (string.IsNullOrEmpty(it.wingetId)) return null;

            string dir = Path.Combine(bundleDir, "installers", Util.SafeName(it.id));
            try { Directory.CreateDirectory(dir); } catch { return null; }

            report(-1, null, "> скачиваю установщик: " + it.wingetId);
            string args = "download --id " + it.wingetId + " --exact --download-directory \"" + dir +
                          "\" --accept-package-agreements --accept-source-agreements --disable-interactivity";
            Proc.Run("winget", args, null, 1800000, null, null);

            string best = null;
            long bestLen = 0;
            try
            {
                string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    string ext = Path.GetExtension(files[i]).ToLowerInvariant();
                    if (ext != ".exe" && ext != ".msi" && ext != ".msix" && ext != ".appx") continue;
                    long len = 0;
                    try { len = new FileInfo(files[i]).Length; } catch { }
                    if (len > bestLen) { bestLen = len; best = files[i]; }
                }
            }
            catch { }

            if (best == null)
            {
                report(-1, null, "  ! установщик не скачался (поставится из интернета)");
                return null;
            }
            report(-1, null, "  + " + Path.GetFileName(best) + " (" + Util.FormatSize(bestLen) + ")");

            // рядом с установщиком winget кладёт yaml-манифест: в нём точные тихие ключи
            string yaml = FindYaml(dir, best);
            string detKind = Detector.Detect(best);
            if (yaml != null && detKind == "unknown")
            {
                ReadYamlManifest(yaml, Path.GetFileName(best), out kind, out silent);
                if (!string.IsNullOrEmpty(silent)) report(-1, null, "    тип не распознан, беру ключи из манифеста: " + silent);
                else report(-1, null, "    ! и манифест без тихих ключей — установщик может открыть окно");
            }
            return best;
        }

        class YamlBlock
        {
            public string type;
            public string url;
            public string silent;
            public bool inSwitches;
        }

        public static string FindYaml(string dir, string installer)
        {
            try
            {
                string guess = Path.ChangeExtension(installer, ".yaml");
                if (File.Exists(guess)) return guess;
                string[] any = Directory.GetFiles(dir, "*.yaml", SearchOption.AllDirectories);
                if (any.Length > 0) return any[0];
            }
            catch { }
            return null;
        }

        // берём InstallerType и InstallerSwitches.Silent из манифеста winget
        public static void ReadYamlManifest(string yamlPath, string installerName, out string kind, out string silent)
        {
            kind = null;
            silent = null;

            string[] lines;
            try { lines = File.ReadAllLines(yamlPath, System.Text.Encoding.UTF8); }
            catch { return; }

            List<YamlBlock> blocks = new List<YamlBlock>();
            YamlBlock cur = null;
            string gType = null;
            string gSilent = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                string t = ln.Trim();
                if (t.Length == 0) continue;

                int indent = ln.Length - ln.TrimStart().Length;
                if (indent == 0 && ln.StartsWith("- ") && t.StartsWith("Architecture:"))
                {
                    cur = new YamlBlock();
                    blocks.Add(cur);
                    continue;
                }

                string key = t;
                string val = null;
                int c = t.IndexOf(':');
                if (c > 0) { key = t.Substring(0, c).Trim(); val = t.Substring(c + 1).Trim(); }

                if (key == "InstallerSwitches")
                {
                    if (cur != null) cur.inSwitches = true;
                    continue;
                }

                if (key == "InstallerType")
                {
                    if (gType == null) gType = val;
                    if (cur != null && cur.type == null) cur.type = val;
                }
                if (key == "InstallerUrl" && cur != null) cur.url = val;

                if (key == "Silent")
                {
                    if (gSilent == null) gSilent = val;
                    if (cur != null && cur.inSwitches && cur.silent == null) cur.silent = val;
                }

                if (cur != null && cur.inSwitches && indent <= 2) cur.inSwitches = false;
            }

            YamlBlock pick = null;
            for (int i = 0; i < blocks.Count && pick == null; i++)
            {
                if (string.IsNullOrEmpty(blocks[i].url)) continue;
                string fname = blocks[i].url;
                int slash = fname.LastIndexOf('/');
                if (slash >= 0) fname = fname.Substring(slash + 1);
                try { fname = Uri.UnescapeDataString(fname); } catch { }
                if (string.Equals(fname, installerName, StringComparison.OrdinalIgnoreCase)) pick = blocks[i];
            }
            if (pick == null)
            {
                for (int i = 0; i < blocks.Count && pick == null; i++)
                    if (!string.IsNullOrEmpty(blocks[i].silent)) pick = blocks[i];
            }
            if (pick == null && blocks.Count > 0) pick = blocks[0];

            string t2 = pick != null && !string.IsNullOrEmpty(pick.type) ? pick.type : gType;
            string s2 = pick != null && !string.IsNullOrEmpty(pick.silent) ? pick.silent : gSilent;

            kind = MapKind(t2);
            silent = CleanSwitch(s2);
        }

        static string CleanSwitch(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            string v = s.Trim();
            if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') v = v.Substring(1, v.Length - 2);
            if (v.Length >= 2 && v[0] == '\'' && v[v.Length - 1] == '\'') v = v.Substring(1, v.Length - 2);

            // в манифестах winget встречается незакрытая кавычка: /Q /C:"msiexec /i ""vcredist.msi"" /quiet
            int q = 0;
            for (int i = 0; i < v.Length; i++) if (v[i] == '"') q++;
            if ((q % 2) == 1) v = v + "\"";

            v = v.Trim();
            return v.Length == 0 ? null : v;
        }

        static string MapKind(string t)
        {
            if (string.IsNullOrEmpty(t)) return null;
            t = t.ToLowerInvariant();
            if (t == "burn") return "burn";
            if (t == "inno") return "inno";
            if (t == "nullsoft") return "nsis";
            if (t == "msi") return "msi";
            if (t == "msix" || t == "appx" || t == "msixbundle" || t == "appxbundle") return "msix";
            if (t == "squirrel") return "squirrel";
            if (t == "installshield") return "installshield";
            if (t == "zip" || t == "7z") return "archive";
            return null;
        }

        public static Manifest LoadBundle(string bundleDir)
        {
            return Json.Load<Manifest>(Path.Combine(bundleDir, "bundle.json"), null);
        }

        // сохранить выверенные вручную записи: если в манифесте уже был этот пункт
        // с большим числом конфигов / своим названием, берём его данные
        public static void MergeCurated(List<Item> found, List<Item> curated)
        {
            if (found == null || curated == null) return;
            for (int i = 0; i < found.Count; i++)
            {
                Item f = found[i];
                for (int j = 0; j < curated.Count; j++)
                {
                    Item c = curated[j];
                    bool same = false;
                    if (!string.IsNullOrEmpty(f.wingetId) && !string.IsNullOrEmpty(c.wingetId) &&
                        string.Equals(f.wingetId, c.wingetId, StringComparison.OrdinalIgnoreCase)) same = true;
                    if (!same && !string.IsNullOrEmpty(f.id) && !string.IsNullOrEmpty(c.id) &&
                        string.Equals(f.id, c.id, StringComparison.OrdinalIgnoreCase)) same = true;
                    if (!same) continue;

                    int fc = f.configs == null ? 0 : f.configs.Count;
                    int cc = c.configs == null ? 0 : c.configs.Count;
                    if (cc > fc) f.configs = Clone(c).configs;
                    if (!string.IsNullOrEmpty(c.name) && c.name.Length > 1) f.name = c.name;
                    if (c.stage > 0) f.stage = c.stage;
                    if (!string.IsNullOrEmpty(c.source) && c.source != "manual") f.source = c.source;
                    if (c.timeoutSec > 0) f.timeoutSec = c.timeoutSec;
                    break;
                }
            }
        }

        // ---------------------------------------------------------- утилиты

        static Item Clone(Item src)
        {
            Item c = new Item();
            c.id = src.id;
            c.name = src.name;
            c.source = src.source;
            c.wingetId = src.wingetId;
            c.filePath = src.filePath;
            c.silentArgs = src.silentArgs;
            c.kind = src.kind;
            c.target = src.target;
            c.stage = src.stage;
            c.enabled = src.enabled;
            c.notes = src.notes;
            c.timeoutSec = src.timeoutSec;
            c.installerState = src.installerState;
            c.postRestore = new List<PostAction>();
            if (src.postRestore != null)
            {
                for (int i = 0; i < src.postRestore.Count; i++)
                {
                    PostAction s = src.postRestore[i];
                    PostAction d = new PostAction();
                    d.type = s.type;
                    d.name = s.name;
                    d.waitSec = s.waitSec;
                    c.postRestore.Add(d);
                }
            }
            c.preRestore = new List<PostAction>();
            if (src.preRestore != null)
            {
                for (int i = 0; i < src.preRestore.Count; i++)
                {
                    PostAction s = src.preRestore[i];
                    PostAction d = new PostAction();
                    d.type = s.type;
                    d.name = s.name;
                    d.waitSec = s.waitSec;
                    c.preRestore.Add(d);
                }
            }
            c.configs = new List<ConfigEntry>();
            if (src.configs != null)
            {
                for (int i = 0; i < src.configs.Count; i++)
                {
                    ConfigEntry s = src.configs[i];
                    ConfigEntry d = new ConfigEntry();
                    d.path = s.path;
                    d.type = s.type;
                    d.mode = s.mode;
                    d.include = s.include;
                    d.exclude = s.exclude;
                    d.restoreSkip = s.restoreSkip;
                    c.configs.Add(d);
                }
            }
            return c;
        }

        static string Rel(string root, string full)
        {
            try
            {
                if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    string r = full.Substring(root.Length);
                    if (r.StartsWith("\\")) r = r.Substring(1);
                    return r;
                }
            }
            catch { }
            return full;
        }

        static string NiceName(string wingetId)
        {
            if (string.IsNullOrEmpty(wingetId)) return wingetId;

            string known = KnownName(wingetId);
            if (known != null) return known;

            // "Microsoft.VCRedist.2015+.x64" -> "VCRedist 2015+ x64" (издателя отрезаем)
            string[] parts = wingetId.Split('.');
            if (parts.Length >= 2)
            {
                string tail = "";
                for (int i = 1; i < parts.Length; i++)
                {
                    if (tail.Length > 0) tail += " ";
                    tail += parts[i];
                }
                tail = tail.Replace('_', ' ').Trim();
                if (tail.Length >= 3 && !OnlyDigits(tail)) return tail;
            }

            return wingetId.Replace('.', ' ').Replace('_', ' ').Trim();
        }

        static bool OnlyDigits(string s)
        {
            for (int i = 0; i < s.Length; i++)
                if (!char.IsDigit(s[i]) && s[i] != ' ') return false;
            return true;
        }

        // узнаваемые названия для частых пакетов
        static string KnownName(string wingetId)
        {
            string s = wingetId.ToLowerInvariant();

            if (s.StartsWith("microsoft.vcredist."))
            {
                string[] p = wingetId.Split('.');
                string year = p.Length > 2 ? p[2] : "";
                string arch = p.Length > 3 ? p[3] : "";
                return ("Visual C++ " + year + " (" + arch + ")").Replace(" ()", "");
            }
            if (s.StartsWith("microsoft.vclibs.desktop")) return "VCLibs Desktop";
            if (s.StartsWith("microsoft.vclibs")) return "VCLibs";
            if (s.StartsWith("microsoft.ui.xaml")) return "UI Xaml " + LastSeg(wingetId);
            if (s.StartsWith("microsoft.windowsappruntime")) return "Windows App Runtime " + LastSeg(wingetId);
            if (s.StartsWith("microsoft.dotnet.native.runtime")) return ".NET Native Runtime";
            if (s.StartsWith("microsoft.appinstaller")) return "App Installer";
            if (s.StartsWith("microsoft.directx")) return "DirectX";
            if (s.StartsWith("microsoft.gameinput")) return "Game Input";
            if (s.StartsWith("microsoft.windowsterminal")) return "Windows Terminal";
            if (s.StartsWith("valve.steam")) return "Steam";
            if (s.StartsWith("ubisoft.connect")) return "Ubisoft Connect";
            if (s.StartsWith("openjs.nodejs.lts")) return "Node.js LTS";
            if (s.StartsWith("openjs.nodejs")) return "Node.js";
            if (s.StartsWith("zen-team.zen-browser")) return "Zen Browser";
            if (s.StartsWith("cloudflare.warp")) return "Cloudflare WARP";
            if (s.StartsWith("ramensoftware.windhawk")) return "Windhawk";
            if (s.StartsWith("lizardbyte.sunshine")) return "Sunshine";
            if (s.StartsWith("7zip.7zip")) return "7-Zip";
            if (s.StartsWith("rarlab.winrar")) return "WinRAR";
            if (s.StartsWith("revouninstaller")) return "Revo Uninstaller Pro";
            if (s.StartsWith("namazso.pawnio")) return "PawnIO";
            if (s.StartsWith("happ.happ")) return "Happ";
            if (s.StartsWith("playnite.playnite")) return "Playnite";
            if (s.StartsWith("beeradmoore.dlss-swapper")) return "DLSS Swapper";
            if (s.StartsWith("anomaly.opencode")) return "OpenCode Desktop";
            if (s.StartsWith("creativetechnology.openal")) return "OpenAL";
            if (s.StartsWith("clockworkmod.universaladbdriver")) return "Universal ADB Driver";
            if (s.StartsWith("nvidia.physx")) return "NVIDIA PhysX";
            return null;
        }

        static string LastSeg(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int i = s.LastIndexOf('.');
            if (i >= 0 && i < s.Length - 1) return s.Substring(i + 1);
            return s;
        }

        static string CleanName(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            string r = s.Trim();
            r = r.Replace("\u2122", "").Replace("\u00AE", "").Replace("\u00A9", "");
            // убрать (x64), (x86), (64-bit), (32-bit), (Русский)
            r = System.Text.RegularExpressions.Regex.Replace(r, "\\s*\\((?:x64|x86|64-bit|32-bit|64 bit|32 bit)\\)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            // убрать хвост с версией: " v1.2.3", " 1.2.3"
            r = System.Text.RegularExpressions.Regex.Replace(r, "\\s+v?\\d+(\\.\\d+)+.*$", "");
            // убрать длинные хвосты после разделителя
            int dash = r.IndexOf(" - ");
            if (dash > 2) r = r.Substring(0, dash);
            r = r.Trim();
            // убрать повисшие разделители после чистки версии: "Name -", "Name:"
            while (r.Length > 1)
            {
                char last = r[r.Length - 1];
                if (last == '-' || last == ':' || last == '|' || last == ',')
                {
                    r = r.Substring(0, r.Length - 1).Trim();
                    continue;
                }
                break;
            }
            return r;
        }

        static string Norm(string s)
        {
            if (s == null) return "";
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                char c = char.ToLowerInvariant(s[i]);
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            }
            return sb.ToString();
        }

        static bool IsNoise(string name)
        {
            string n = name.ToLowerInvariant();
            if (n.StartsWith("update for ")) return true;
            if (n.StartsWith("\u041e\u0431\u043d\u043e\u0432\u043b\u0435\u043d\u0438\u0435 \u0434\u043b\u044f")) return true;
            if (n.IndexOf("security update") >= 0) return true;
            if (n.IndexOf("service pack") >= 0) return true;
            if (n.StartsWith("kb") && n.Length < 10) return true;

            // драйверы и системные рантаймы ставит Windows / сам установщик
            if (n.IndexOf("driver") >= 0) return true;
            if (n.IndexOf("\u0434\u0440\u0430\u0439\u0432\u0435\u0440") >= 0) return true;
            if (n.StartsWith("\u043f\u0430\u043a\u0435\u0442 ")) return true;
            if (n.StartsWith("microsoft .net host")) return true;
            if (n.StartsWith("microsoft .net runtime")) return true;
            if (n.StartsWith("microsoft .net native")) return true;
            if (n.StartsWith("microsoft windows desktop runtime")) return true;
            if (n.StartsWith("microsoft asp.net core")) return true;
            if (n.StartsWith("mozilla maintenance service")) return true;

            if (n.StartsWith("nvidia ") &&
                n.IndexOf("app") < 0 && n.IndexOf("physx") < 0 &&
                n.IndexOf("broadcast") < 0 && n.IndexOf("geforce experience") < 0) return true;

            return false;
        }

        // игры не берём: их ставят Steam/Epic/GOG/EA/Ubisoft, а не установщик
        static bool IsGame(RegistryKey k, string keyName, string display, Action<string> log)
        {
            if (keyName != null && keyName.StartsWith("Steam App ", StringComparison.OrdinalIgnoreCase)) return true;
            if (keyName != null && keyName.IndexOf("Riot Game ", StringComparison.OrdinalIgnoreCase) == 0) return true;

            string dn = (display == null ? "" : display).ToLowerInvariant();
            string[] fields = new string[] { "InstallLocation", "UninstallString", "QuietUninstallString", "DisplayIcon" };

            for (int i = 0; i < fields.Length; i++)
            {
                string v = null;
                try { v = k.GetValue(fields[i]) as string; } catch { }
                if (string.IsNullOrEmpty(v)) continue;

                string s = v.Replace('/', '\\').ToLowerInvariant();

                if (s.IndexOf("steamapps") >= 0) return true;
                if (s.IndexOf("steam://") >= 0) return true;
                if (s.IndexOf("--uninstall-product") >= 0) return true;
                if (s.IndexOf("\\games\\") >= 0) return true;            // D:\Games\<игра>
                if (s.IndexOf("\\gog games\\") >= 0) return true;
                if (s.IndexOf("\\ea games\\") >= 0) return true;
                if (s.IndexOf("\\origin games\\") >= 0) return true;
                if (s.IndexOf("\\ubisoft game launcher\\games\\") >= 0) return true;
                if (s.IndexOf("riot games\\metadata") >= 0) return true;
                if (s.IndexOf("\\epic games\\") >= 0 && dn.IndexOf("launcher") < 0) return true;
                if (s.IndexOf("\\battle.net\\") >= 0 && dn.IndexOf("battle.net") < 0) return true;
            }
            return false;
        }

        static int StageFor(string wingetId, string name)
        {
            string s = ((wingetId == null ? "" : wingetId) + " " + (name == null ? "" : name)).ToLowerInvariant();

            string[] first = new string[] {
                "vcredist", "visual c++", "dotnet", ".net", "directx", "webview",
                "appinstaller", "vclibs", "uixaml", "windowsappruntime", "vulkan", "openal"
            };
            for (int i = 0; i < first.Length; i++)
                if (s.IndexOf(first[i]) >= 0) return 1;

            string[] second = new string[] {
                "windhawk", "ghelper", "g-helper", "wireguard", "warp", "nvidia", "amd",
                "realtek", "driver", "armoury", "afterburner", "rivatuner", "flawless",
                "everything", "7zip", "7-zip", "winrar", "powertoys", "terminal"
            };
            for (int i = 0; i < second.Length; i++)
                if (s.IndexOf(second[i]) >= 0) return 2;

            return 3;
        }
    }
}
