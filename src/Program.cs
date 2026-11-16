using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows.Forms;

namespace WinSetup
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            EnableDpiAwareness();

            // служебный режим: сгенерировать .ico для exe (без прав администратора)
            if (args != null && args.Length >= 2 && args[0] == "--make-icon")
            {
                try { Logo.SaveIco(args[1], null); }
                catch { }
                return;
            }

            if (!IsAdministrator())
            {
                if (TryRunAsAdmin())
                {
                    return;
                }
                MessageBox.Show(
                    "Для установки программ и записи в реестр нужны права администратора.\n\n" +
                    "Запустите OneClick от имени администратора (правый клик -> Запуск от имени администратора).",
                    Logo.Name,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.InitScale();
            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            try
            {
                Application.Run(new MainForm());
            }
            catch (Exception ex)
            {
                ShowFatal(ex);
            }
        }

        // ------------------------------------------------------------ DPI
        //
        // Без этого Windows считает программу «слепой» к масштабу и растягивает её
        // готовую картинку: на 150 % и 200 % всё мыльное. Объявляем себя DPI-aware,
        // а размеры раскладки умножаем через Theme.S().

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        static void EnableDpiAwareness()
        {
            try
            {
                // -2 = DPI_AWARENESS_CONTEXT_SYSTEM_AWARE (Win10 1703+)
                if (SetProcessDpiAwarenessContext(new IntPtr(-2))) return;
            }
            catch { }
            try { SetProcessDPIAware(); } catch { }   // старые Windows
        }

        private static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                if (identity == null)
                {
                    return false;
                }
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryRunAsAdmin()
        {
            try
            {
                string exe = typeof(Program).Assembly.Location;
                if (string.IsNullOrEmpty(exe))
                {
                    exe = Application.ExecutablePath;
                }

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WorkingDirectory = System.IO.Path.GetDirectoryName(exe);

                Process.Start(psi);
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Пользователь отказался от запуска от имени администратора.
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void OnThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
        {
            ShowFatal(e.Exception);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = e.ExceptionObject as Exception;
            if (ex != null)
            {
                ShowFatal(ex);
            }
        }

        private static void ShowFatal(Exception ex)
        {
            try
            {
                string text = ex == null ? "Неизвестная ошибка." : ex.Message;
                MessageBox.Show(text, Logo.Name + " - ошибка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch
            {
                // ничего не остаётся делать
            }
        }
    }
}
