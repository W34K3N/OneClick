using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace WinSetup
{
    // ---------------------------------------------------------------- модель

    public class ConfigEntry
    {
        public string path { get; set; }    // %APPDATA%\WinRAR  или  HKCU\Software\WinRAR
        public string type { get; set; }    // dir | file | reg
        public string mode { get; set; }    // full | selective
        public List<string> include { get; set; }
        public List<string> exclude { get; set; }
        // что НЕ восстанавливать из бэкапа (обычно *.exe/*.dll, если папка совпадает
        // с папкой установки: свежие файлы установщика трогать нельзя)
        public List<string> restoreSkip { get; set; }

        public ConfigEntry() { type = "dir"; mode = "full"; }
    }

    public class Item
    {
        public string id { get; set; }
        public string name { get; set; }
        public string source { get; set; }      // winget | file | portable | script
        public string wingetId { get; set; }
        public string filePath { get; set; }    // относительно склада
        public string silentArgs { get; set; }
        public int timeoutSec { get; set; }     // лимит на установку, сек (0 = 900)
        public string kind { get; set; }        // nsis | inno | squirrel | burn | msi | vcredist | portable | 7zsfx
        public string target { get; set; }      // куда ставить portable
        public int stage { get; set; }          // 1..5 порядок этапов
        public bool enabled { get; set; }
        public string notes { get; set; }
        public string installerState { get; set; }   // скачан | из интернета | нет | ?
        public List<ConfigEntry> configs { get; set; }
        public List<PostAction> preRestore { get; set; }    // что сделать ДО восстановления (например, закрыть explorer)
        public List<PostAction> postRestore { get; set; }   // что сделать после восстановления настроек

        public Item() { enabled = true; stage = 3; timeoutSec = 900; configs = new List<ConfigEntry>(); }
    }

    // действие до/после восстановления настроек (закрыть/перезапустить процесс или службу)
    public class PostAction
    {
        public string type { get; set; }   // service | restart | start | kill
        public string name { get; set; }   // имя службы или процесса
        public int waitSec { get; set; }   // пауза перед/после, сек (0 = 3)
    }

    public class Manifest
    {
        public int version { get; set; }
        public string warehouse { get; set; }
        public List<Item> items { get; set; }
        public Manifest() { version = 1; items = new List<Item>(); }
    }

    // ---------------------------------------------------------------- json

    public static class Json
    {
        public static T Load<T>(string path, T fallback)
        {
            try
            {
                if (!File.Exists(path)) return fallback;
                string text = File.ReadAllText(path, Encoding.UTF8);
                if (text.Trim().Length == 0) return fallback;
                JavaScriptSerializer js = new JavaScriptSerializer();
                js.MaxJsonLength = 64 * 1024 * 1024;
                return js.Deserialize<T>(text);
            }
            catch (Exception)
            {
                return fallback;
            }
        }

        public static void Save<T>(string path, T value)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 64 * 1024 * 1024;
            string text = js.Serialize(value);
            File.WriteAllText(path, text, new UTF8Encoding(true));
        }
    }

    // ---------------------------------------------------------------- утилиты

    public static class Util
    {
        // диагностика копирования: сколько файлов/папок не удалось перенести
        public static int CopySkips;
        public static string CopySkipSample;

        public static void ResetCopyStats()
        {
            CopySkips = 0;
            CopySkipSample = null;
        }

        public static string CopyStatsText()
        {
            if (CopySkips <= 0) return null;
            return CopySkips + " объектов не скопировано" +
                   (CopySkipSample == null ? "" : " (первый: " + CopySkipSample + ")");
        }

        public static string Expand(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return Environment.ExpandEnvironmentVariables(s);
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes.ToString() + " Б";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " КБ";
            if (bytes < 1024L * 1024 * 1024) return (bytes / 1048576.0).ToString("0.0") + " МБ";
            return (bytes / 1073741824.0).ToString("0.00") + " ГБ";
        }

        public static long DirSize(string dir)
        {
            long total = 0;
            try
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        // простой wildcard: * и ?
        public static bool Match(string pattern, string text)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            string rx = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(text, rx, RegexOptions.IgnoreCase);
        }

        public static bool MatchAny(List<string> patterns, string text)
        {
            if (patterns == null || patterns.Count == 0) return false;
            foreach (string p in patterns)
            {
                if (Match(p, text)) return true;
                // совпадение по любому сегменту пути
                string[] parts = text.Split(new char[] { '\\', '/' });
                foreach (string part in parts)
                {
                    if (Match(p, part)) return true;
                }
            }
            return false;
        }

        // мусор, который не переносим в режиме selective
        public static bool IsJunk(string relPath)
        {
            string[] junk = new string[] {
                "Cache", "cache", "Cache2", "CachedData", "Code Cache", "GPUCache",
                "ShaderCache", "GrShaderCache", "browsercache", "logs", "log",
                "Crashpad", "crashpad", "temp", "Temp", "tmp", "Blob Storage",
                "Service Worker", "DawnGraphiteCache", "DawnWebGPUCache",
                "GPUCache", "component_crx_cache", "extensions_crx_cache"
            };
            string[] parts = relPath.Split(new char[] { '\\', '/' });
            for (int i = 0; i < parts.Length - 1; i++)   // последний (имя файла) не проверяем
            {
                for (int j = 0; j < junk.Length; j++)
                {
                    if (string.Equals(parts[i], junk[j], StringComparison.OrdinalIgnoreCase)) return true;
                }
            }
            return false;
        }

        public static string LongPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            if (p.StartsWith("\\\\?\\")) return p;
            // до 250 символов Windows и .NET берут путь как есть;
            // префикс \\?\ нужен только для действительно длинных путей
            // (он работает лишь при UseLegacyPathHandling=false — см. app.config)
            if (p.Length < 250) return p;
            if (p.StartsWith("\\\\")) return "\\\\?\\UNC\\" + p.Substring(2);
            return "\\\\?\\" + p;
        }

        // Закрыть процессы, запущенные из папки dir: иначе их файлы заняты
        // и приложение затирает восстановленные настройки своим состоянием.
        public static int KillProcessesIn(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return 0;
            string root;
            try { root = Path.GetFullPath(Expand(dir)).TrimEnd('\\') + "\\"; }
            catch { return 0; }

            int killed = 0;
            Process[] ps;
            try { ps = Process.GetProcesses(); }
            catch { return 0; }

            for (int i = 0; i < ps.Length; i++)
            {
                try
                {
                    string path = ps[i].MainModule.FileName;
                    if (string.IsNullOrEmpty(path)) continue;
                    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    try { ps[i].Kill(); killed++; } catch { }
                }
                catch { }
                finally { try { ps[i].Dispose(); } catch { } }
            }
            if (killed > 0)
            {
                try { System.Threading.Thread.Sleep(1500); } catch { }
            }
            return killed;
        }

        // полное удаление папки (в т.ч. с длинными путями), ошибки не критичны.
        // Directory.Delete(..., true) с префиксом \\?\ на «мусорных» именах падает,
        // поэтому обходим дерево сами и удаляем по одному элементу
        public static void RemoveDir(string p)
        {
            if (string.IsNullOrEmpty(p)) return;
            try { DeleteTree(LongPath(p)); } catch { }
        }

        static void DeleteTree(string dir)
        {
            string d = ForceLongPath(dir);
            string[] files;
            try { files = Directory.GetFiles(d); } catch { files = new string[0]; }
            for (int i = 0; i < files.Length; i++)
            {
                try
                {
                    File.SetAttributes(files[i], FileAttributes.Normal);
                    File.Delete(files[i]);
                }
                catch { }
            }

            string[] dirs;
            try { dirs = Directory.GetDirectories(d); } catch { dirs = new string[0]; }
            for (int i = 0; i < dirs.Length; i++) DeleteTree(dirs[i]);

            try { Directory.Delete(d, false); } catch { }
        }

        // префикс \\?\ ставим всегда: при удалении смешанных по длине путей
        // (родитель короткий, ребёнок длинный) без префикса Windows отказывает
        public static string ForceLongPath(string p)
        {
            if (string.IsNullOrEmpty(p)) return p;
            if (p.StartsWith("\\\\?\\")) return p;
            if (p.StartsWith("\\\\")) return "\\\\?\\UNC\\" + p.Substring(2);
            return "\\\\?\\" + p;
        }

        // подмена папки бэкапа новой копией: старые файлы не остаются
        public static void ReplaceDir(string tmp, string dest)
        {
            try
            {
                string parent = Path.GetDirectoryName(dest);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(LongPath(parent));
                RemoveDir(dest);
                if (!Directory.Exists(LongPath(dest)))
                {
                    Directory.Move(LongPath(tmp), LongPath(dest));
                    return;
                }
            }
            catch { }

            // перенос не удался — копируем по файлам поверх
            try { Directory.CreateDirectory(LongPath(dest)); } catch { }
            long b = 0, f = 0;
            CopyTree(tmp, dest, null, null, false, ref b, ref f, null);
            RemoveDir(tmp);
        }

        static void Skip(string what)
        {
            CopySkips++;
            if (CopySkipSample == null) CopySkipSample = what;
        }

        public static void CopyTree(string src, string dst, List<string> include, List<string> exclude,
                                    bool selective, ref long bytes, ref long files, Func<bool> cancelled)
        {
            if (cancelled != null && cancelled()) return;
            string srcLong = LongPath(src);
            string dstLong = LongPath(dst);
            try { Directory.CreateDirectory(dstLong); }
            catch (Exception ex)
            {
                Skip("папка " + dst + ": " + ex.Message);
                return;
            }

            string[] filesHere;
            try { filesHere = Directory.GetFiles(srcLong); }
            catch (Exception ex)
            {
                Skip("перечисление " + src + ": " + ex.Message);
                filesHere = new string[0];
            }
            for (int i = 0; i < filesHere.Length; i++)
            {
                if (cancelled != null && cancelled()) return;
                string f = filesHere[i];
                string fLong = LongPath(f);          // источник тоже бывает длиннее 259
                string name = Path.GetFileName(f);
                if (exclude != null && MatchAny(exclude, name)) continue;
                if (selective && IsJunk(name)) continue;
                if (include != null && include.Count > 0 && !MatchAny(include, name)) continue;

                string target = LongPath(Path.Combine(dst, name));
                Exception last = null;
                for (int attempt = 0; attempt < 3; attempt++)   // файл может быть занят антивирусом сразу после установки
                {
                    try
                    {
                        File.Copy(fLong, target, true);
                        try { File.SetLastWriteTime(target, File.GetLastWriteTime(fLong)); } catch { }
                        bytes += new FileInfo(fLong).Length;
                        files++;
                        last = null;
                        break;
                    }
                    catch (Exception ex)
                    {
                        last = ex;
                        System.Threading.Thread.Sleep(400);
                    }
                }
                if (last != null) Skip(name + ": " + last.Message);
            }

            string[] dirsHere;
            try { dirsHere = Directory.GetDirectories(srcLong); }
            catch (Exception ex)
            {
                Skip("перечисление " + src + ": " + ex.Message);
                dirsHere = new string[0];
            }
            for (int i = 0; i < dirsHere.Length; i++)
            {
                if (cancelled != null && cancelled()) return;
                string d = dirsHere[i];
                string dname = Path.GetFileName(d);
                if (exclude != null && MatchAny(exclude, dname)) continue;
                if (selective && IsJunk(dname)) continue;
                CopyTree(d, Path.Combine(dst, dname), include, exclude, selective, ref bytes, ref files, cancelled);
            }
        }

        // helper: check that the taskbar window (shell) exists
        public static bool ShellTrayExists()
        {
            bool found = false;
            EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                StringBuilder sb = new StringBuilder(64);
                GetClassNameW(h, sb, 64);
                if (sb.ToString() == "Shell_TrayWnd") { found = true; return false; }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int GetClassNameW(IntPtr h, StringBuilder sb, int n);

        public static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "app";
            char[] bad = Path.GetInvalidFileNameChars();
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                bool isBad = false;
                for (int i = 0; i < bad.Length; i++) if (bad[i] == c) { isBad = true; break; }
                sb.Append(isBad ? '_' : c);
            }
            return sb.ToString();
        }
    }

    // ---------------------------------------------------------------- запуск процессов

    public class ProcResult
    {
        public int ExitCode;
        public string Output;
        public bool Cancelled;
    }

    public static class Proc
    {
        public static ProcResult Run(string file, string args, string workDir, int timeoutMs,
                                     Action<string> onLine, Func<bool> cancelled)
        {
            ProcResult res = new ProcResult();
            StringBuilder output = new StringBuilder();
            Process p = new Process();
            p.StartInfo.FileName = file;
            p.StartInfo.Arguments = args;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            if (!string.IsNullOrEmpty(workDir)) p.StartInfo.WorkingDirectory = workDir;

            DataReceivedEventHandler handler = delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                output.AppendLine(e.Data);
                if (onLine != null) onLine(e.Data);
            };
            p.OutputDataReceived += handler;
            p.ErrorDataReceived += handler;

            try { p.Start(); }
            catch (Exception ex)
            {
                res.ExitCode = -1;
                res.Output = "не удалось запустить: " + ex.Message;
                return res;
            }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            DateTime started = DateTime.UtcNow;
            int beat = 0;
            while (!p.HasExited)
            {
                if (cancelled != null && cancelled())
                {
                    KillTree(p);
                    res.Cancelled = true;
                    break;
                }
                double elapsed = (DateTime.UtcNow - started).TotalSeconds;
                if (onLine != null && elapsed >= beat + 30)
                {
                    beat = beat + 30;
                    onLine("   ... идёт " + beat + " с");
                }
                if (timeoutMs > 0 && (DateTime.UtcNow - started).TotalMilliseconds > timeoutMs)
                {
                    KillTree(p);
                    res.Output = output.ToString() + "\n(таймаут)";
                    res.ExitCode = -2;
                    return res;
                }
                System.Threading.Thread.Sleep(150);
            }
            try { p.WaitForExit(5000); } catch { }
            res.ExitCode = res.Cancelled ? -3 : 0;
            if (!res.Cancelled) { try { res.ExitCode = p.ExitCode; } catch { } }
            res.Output = output.ToString();
            return res;
        }

        // убить процесс вместе с потомками (msiexec, setup-хелперы)
        public static void KillTree(Process p)
        {
            if (p == null) return;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("taskkill", "/T /F /PID " + p.Id);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                Process.Start(psi);
            }
            catch { }
            try { p.Kill(); } catch { }
        }
    }

    // ---------------------------------------------------------------- определение типа установщика

    public static class Detector
    {
        public static string Detect(string file)
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".msi") return "msi";
            if (ext == ".msix" || ext == ".appx") return "msix";
            if (ext == ".zip" || ext == ".7z" || ext == ".rar") return "archive";

            string head = ReadHead(file, 3 * 1024 * 1024);
            if (head == null) return "unknown";
            // 7-Zip SFX (7zS.sfx): аргументы командной строки SFX передаёт вложенному
            // установщику, поэтому тип определяем по обёртке отдельно.
            if (head.IndexOf("7zS.sfx", StringComparison.Ordinal) >= 0 ||
                head.IndexOf("7-Zip Self-extracting Archive", StringComparison.OrdinalIgnoreCase) >= 0) return "sfx";
            if (head.IndexOf("Inno Setup", StringComparison.OrdinalIgnoreCase) >= 0) return "inno";
            if (head.IndexOf("Nullsoft", StringComparison.OrdinalIgnoreCase) >= 0) return "nsis";
            if (head.IndexOf("NSIS", StringComparison.Ordinal) >= 0) return "nsis";
            if (head.IndexOf("Squirrel", StringComparison.OrdinalIgnoreCase) >= 0) return "squirrel";
            if (head.IndexOf("InstallShield", StringComparison.OrdinalIgnoreCase) >= 0) return "installshield";
            // только точные маркеры: голое "WiX" встречается в сжатых данных случайно
            if (head.IndexOf("WixBundle", StringComparison.Ordinal) >= 0 ||
                head.IndexOf("WixStandardBootstrapperApplication", StringComparison.Ordinal) >= 0) return "burn";
            if (head.IndexOf("7-Zip SFX", StringComparison.OrdinalIgnoreCase) >= 0) return "7zsfx";
            if (head.IndexOf("vcredist", StringComparison.OrdinalIgnoreCase) >= 0) return "vcredist";
            return "unknown";
        }

        static string ReadHead(string file, int max)
        {
            try
            {
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    int len = (int)Math.Min(max, fs.Length);
                    byte[] buf = new byte[len];
                    int read = fs.Read(buf, 0, len);
                    char[] chars = new char[read];
                    for (int i = 0; i < read; i++) chars[i] = (char)buf[i];
                    return new string(chars);
                }
            }
            catch { return null; }
        }

        public static string SilentArgs(string kind, string file)
        {
            switch (kind)
            {
                case "inno": return "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOCANCEL";
                case "msi": return "/qn /norestart";
                case "nsis": return "/S";
                case "squirrel": return "/S";
                case "installshield": return "/s /v\"/qn /norestart\"";
                // WiX Burn понимает только "-quiet" (с дефисом): с "/quiet" он молча
                // открывает мастер установки и ждёт пользователя.
                case "burn": return "-quiet";
                // 7-Zip SFX: ключи уходят вложенному установщику; внутри у таких
                // сборок почти всегда NSIS (Zen Browser — именно так).
                case "sfx": return "/S";
                case "vcredist": return "/passive /norestart";
                case "7zsfx": return "-y -o\"%TEMP%\\winsetup_sfx\" -gm2";
                default: return "";
            }
        }

        public static string KindLabel(string kind)
        {
            switch (kind)
            {
                case "inno": return "Inno Setup";
                case "nsis": return "NSIS";
                case "squirrel": return "Squirrel/Electron";
                case "installshield": return "InstallShield";
                case "burn": return "WiX Burn";
                case "sfx": return "7z SFX";
                case "msi": return "MSI";
                case "msix": return "MSIX";
                case "vcredist": return "VC++ Redist";
                case "portable": return "Portable";
                case "archive": return "Архив";
                case "script": return "Скрипт";
                case "7zsfx": return "7z SFX";
                default: return "неизвестно";
            }
        }
    }
}
