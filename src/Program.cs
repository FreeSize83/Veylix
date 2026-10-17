using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Veylix
{
    public static class Program
    {
        public const string Version = "2.3.1";

        const string MutexName = "Global\\VeylixLauncher_v2";
        const string LegacyMutexName = "Global\\BeeBotLauncher_v1";
        const string ShowEventName = "Global\\VeylixLauncher_Show";

        static void Crash(string where, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Settings.Dir);
                File.AppendAllText(Path.Combine(Settings.Dir, "crash.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [" + where + "]\r\n" + LogStore.Sanitize(ex.ToString()) + "\r\n\r\n");
            }
            catch { }
        }

        public static void SetupTls()
        {
            // TLS 1.2 + 1.3 (если поддерживается системой); проверку сертификатов не трогаем
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)(3072 | 12288); }
            catch { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            bool auto = false, testFailStart = false;
            string selfTestDir = null, applyPlan = null, fromZip = null, fromSha = null;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--autostart") auto = true;
                else if (args[i] == "--selftest-ui" && i + 1 < args.Length) selfTestDir = args[++i];
                else if (args[i] == "--data-dir" && i + 1 < args.Length) Settings.OverrideDir(Path.GetFullPath(args[++i]));   // переносной режим / проверка
                else if (args[i] == "--apply-update" && i + 1 < args.Length) applyPlan = args[++i];      // режим помощника обновления
                else if (args[i] == "--post-update" && i + 1 < args.Length) PostUpdatePlan = args[++i];   // первый запуск после обновления
                else if (args[i] == "--update-from-file" && i + 2 < args.Length) { fromZip = args[++i]; fromSha = args[++i]; }
                else if (args[i] == "--test-fail-start") testFailStart = true;
            }
            if (selfTestDir != null) return SelfTest(selfTestDir);
            // помощник обновления: без окна и без мьютекса приложения — только замена файла с откатом
            if (applyPlan != null) return AppUpdate.ApplyUpdate(Path.GetFullPath(applyPlan));
            if (fromZip != null) return UpdateFromFile(fromZip, fromSha, testFailStart);

            bool created;
            Mutex mutex = new Mutex(true, MutexName, out created);
            if (!created)
            {
                // уже запущен — просим его показать окно и выходим
                try
                {
                    EventWaitHandle ev;
                    if (EventWaitHandle.TryOpenExisting(ShowEventName, out ev)) { ev.Set(); ev.Dispose(); }
                }
                catch { }
                return 0;
            }
            Mutex legacy;
            if (Mutex.TryOpenExisting(LegacyMutexName, out legacy))
            {
                legacy.Dispose();
                MessageBox.Show("Запущен старый BeeBot. Закройте его (значок в трее → Выход), иначе два лаунчера будут мешать друг другу.", "Veylix");
                return 1;
            }

            SetupTls();
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Exception ex = e.ExceptionObject as Exception;
                if (ex != null) Crash("domain", ex);
            };
            TaskScheduler.UnobservedTaskException += delegate(object s, UnobservedTaskExceptionEventArgs e)
            {
                Crash("task", e.Exception);
                e.SetObserved();
            };

            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += delegate(object s, DispatcherUnhandledExceptionEventArgs e)
            {
                Crash("ui", e.Exception);
                e.Handled = true;
                try { VDialog.Fallback("Veylix — ошибка", e.Exception.Message + "\n\nПодробности записаны в crash.log в папке данных."); }
                catch { }
            };

            MainWindow w;
            try
            {
                w = new MainWindow(auto, false);
            }
            catch (Exception ex)
            {
                Crash("start", ex);
                VDialog.Fallback("Veylix не запустился", "Не удалось подготовить файлы обхода:\n" + ex.Message);
                return 1;
            }

            EventWaitHandle show = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            Thread listener = new Thread(delegate()
            {
                while (true)
                {
                    try { show.WaitOne(); } catch { return; }
                    try { app.Dispatcher.BeginInvoke(new Action(w.ShowFromTray)); } catch { return; }
                }
            });
            listener.IsBackground = true;
            listener.Start();

            if (auto) w.StartInBackground();
            else w.Show();
            app.Run();
            GC.KeepAlive(mutex);
            GC.KeepAlive(show);
            return 0;
        }

        public static string PostUpdatePlan;   // путь к плану, если это первый запуск после обновления

        // Обновление из локального пакета без окна (для проверки полного цикла на тестовом пакете).
        // Проверки те же, что и при обновлении с GitHub: SHA-256, состав пакета, имя и версия сборки.
        static int UpdateFromFile(string zip, string sha, bool testFailStart)
        {
            try
            {
                string name = Path.GetFileName(zip);
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(name, @"^Veylix-(\d+\.\d+\.\d+)-win-x64\.zip$");
                if (!m.Success) { Console.Error.WriteLine("имя пакета должно быть Veylix-X.Y.Z-win-x64.zip"); return 10; }
                string ver = m.Groups[1].Value;
                if (AppUpdate.Compare(ver, Version) <= 0) { Console.Error.WriteLine("версия пакета не новее текущей " + Version); return 11; }
                Directory.CreateDirectory(AppUpdate.UpdatesDir);
                string local = AppUpdate.PackagePath(ver);
                if (!string.Equals(Path.GetFullPath(zip), local, StringComparison.OrdinalIgnoreCase)) File.Copy(zip, local, true);
                string exe, exeSha;
                string err = AppUpdate.Stage(local, sha, 0, ver, out exe, out exeSha);
                if (err != null) { Console.Error.WriteLine(err); File.WriteAllText(Path.Combine(AppUpdate.UpdatesDir, "stage-error.txt"), err); return 12; }
                string target = System.Reflection.Assembly.GetExecutingAssembly().Location;
                err = AppUpdate.CheckWritable(target);
                if (err != null) { Console.Error.WriteLine(err); return 13; }
                UpdatePlan p = AppUpdate.MakePlan(exe, exeSha, target, ver, false);
                p.TestFailStart = testFailStart;
                p.ReadyTimeoutSec = 40;
                err = AppUpdate.LaunchHelper(p);
                if (err != null) { Console.Error.WriteLine(err); return 14; }
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 15; }
        }

        // Отрисовка всех тем и страниц в PNG без запуска обхода (для проверки оформления)
        static int SelfTest(string dir)
        {
            try
            {
                dir = Path.GetFullPath(dir);
                Settings.OverrideDir(Path.Combine(dir, "data"));
                Application app = new Application();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                int code = 0;
                app.Startup += delegate
                {
                    app.Dispatcher.BeginInvoke(new Action(delegate
                    {
                        try
                        {
                            MainWindow w = new MainWindow(false, true);
                            w.RenderSnapshots(dir);
                            Console.WriteLine("selftest-ui: OK");
                        }
                        catch (Exception ex)
                        {
                            File.WriteAllText(Path.Combine(dir, "selftest-error.txt"), ex.ToString());
                            code = 2;
                        }
                        app.Shutdown(code);
                    }));
                };
                app.Run();
                return code;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(dir, "selftest-error.txt"), ex.ToString()); } catch { }
                return 3;
            }
        }
    }
}
