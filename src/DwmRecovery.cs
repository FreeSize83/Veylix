using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Veylix
{
    // Полноэкранные игры и античиты (например, Arena Breakout Infinite) иногда на время отключают композицию
    // рабочего стола. WPF в этот момент бросает DWM_E_COMPOSITIONDISABLED, а оформление окон (размытие, рамка)
    // может слететь. Veylix не показывает ошибку, сам восстанавливает оформление, когда композиция вернётся,
    // и при следующем открытии окна объясняет, что произошло и что делать, если игра конфликтует с обходом.
    public static class DwmHint
    {
        public const int WM_DWMCOMPOSITIONCHANGED = 0x031E;

        public static string Title { get { return "Игра отключала эффекты Windows"; } }

        // exe — программа на переднем плане в момент сбоя (обычно сама игра) или null
        public static string Text(string exe, bool inGameList)
        {
            string who = exe != null ? GameWatch.Pretty(exe) + " (" + exe + ")" : "Полноэкранная игра или античит";
            string s = who + " на время отключила композицию рабочего стола — так делают некоторые игры. " +
                       "Veylix продолжил работать, оформление окна восстановлено, ничего делать не нужно.";
            if (exe == null)
                return s + " Если игра вылетает или не запускается при включённом обходе — добавьте её в «Игровой режим» в настройках.";
            if (inGameList)
                return s + " Игра уже в «Игровом режиме»: на время игры обход ставится на паузу.";
            return s + " Если игра вылетает или не запускается при включённом обходе — нажмите, чтобы добавить её в «Игровой режим»: Veylix будет ставить обход на паузу, пока она запущена.";
        }

        // Похоже ли имя на игру, а не на системную программу или сам Veylix
        public static bool LooksLikeGame(string exe)
        {
            if (string.IsNullOrEmpty(exe)) return false;
            string e = exe.ToLowerInvariant();
            foreach (string s in new string[] { "explorer.exe", "dwm.exe", "veylix.exe", "searchhost.exe", "shellexperiencehost.exe", "startmenuexperiencehost.exe",
                                                "applicationframehost.exe", "taskmgr.exe", "lockapp.exe", "textinputhost.exe", "systemsettings.exe" })
                if (e == s) return false;
            return true;
        }

        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern int GetWindowThreadProcessId(IntPtr hWnd, out int pid);

        public static string ForegroundExe()
        {
            try
            {
                int pid;
                GetWindowThreadProcessId(GetForegroundWindow(), out pid);
                if (pid == 0 || pid == Process.GetCurrentProcess().Id) return null;
                using (Process p = Process.GetProcessById(pid))
                {
                    string exe = GameWatch.Normalize(p.ProcessName);
                    return LooksLikeGame(exe) ? exe : null;
                }
            }
            catch { return null; }
        }
    }

    public partial class MainWindow
    {
        bool dwmHintPending, dwmHintShown;
        string dwmGame;
        DateTime dwmLastOff = DateTime.MinValue;

        // Вызывается из обработчика ошибок Program, когда WPF сообщил об отключённой композиции
        public void OnDwmCompositionOff()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(OnDwmCompositionOff)); return; }
            if (DateTime.Now - dwmLastOff < TimeSpan.FromMinutes(10)) return;   // повторы во время одной игры
            dwmLastOff = DateTime.Now;
            string exe = DwmHint.ForegroundExe();
            if (exe != null) dwmGame = exe;
            log.Add("info", "Windows на время отключила композицию рабочего стола" + (exe != null ? " (" + GameWatch.Pretty(exe) + ")" : "") +
                " — так бывает при запуске полноэкранных игр. Veylix работает, оформление окна восстановится само.");
            events.Add("error", "info", DwmHint.Title, DwmHint.Text(dwmGame, dwmGame != null && GameList.Contains(dwmGame)), "settings");
            if (!dwmHintShown) dwmHintPending = true;
            if (IsVisible && IsActive) ShowDwmHint();
        }

        // Подсказка — не поверх игры, а когда пользователь сам откроет Veylix
        void ShowDwmHint()
        {
            if (!dwmHintPending) return;
            dwmHintPending = false;
            dwmHintShown = true;
            bool inList = dwmGame != null && GameList.Contains(dwmGame);
            if (dwmGame != null && !inList && gamesInput != null) gamesInput.Text = dwmGame;   // по клику останется нажать «Добавить»
            Toast("dwm", "info", DwmHint.Title, DwmHint.Text(dwmGame, inList), "settings", true);
        }

        void HookDwm()
        {
            HwndSource src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (src == null) return;
            src.AddHook(delegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
            {
                if (msg == DwmHint.WM_DWMCOMPOSITIONCHANGED)
                    Dispatcher.BeginInvoke(new Action(RestoreAfterDwm), DispatcherPriority.Background);
                return IntPtr.Zero;
            });
            Activated += delegate { ShowDwmHint(); };
        }

        // Композиция вернулась — заново применяем оформление главного окна и виджета
        void RestoreAfterDwm()
        {
            try
            {
                ApplyTheme();
                if (widget != null) { CloseWidget(); ShowWidget(); }
                if (dwmLastOff != DateTime.MinValue && DateTime.Now - dwmLastOff < TimeSpan.FromHours(6))
                    log.Add("info", "Эффекты Windows снова включены — оформление окон Veylix восстановлено.");
            }
            catch { }
        }
    }
}
