using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Veylix
{
    // Состояние пчёлки-компаньона выводится только из реального состояния приложения
    public static class CompanionLogic
    {
        // Приоритет: восстановление связи → ошибка → идёт проверка/операция → обход работает → ожидание
        public static string Resolve(bool running, bool busy, bool error, bool restored)
        {
            if (restored) return "restored";
            if (error) return "error";
            if (busy) return "diag";
            if (running) return "work";
            return "idle";
        }

        public static string Mood(string state)
        {
            switch (state)
            {
                case "work": return "happy";
                case "diag": return "diag";
                case "error": return "error";
                case "restored": return "restored";
                default: return "idle";
            }
        }

        public static string Title(string state)
        {
            switch (state)
            {
                case "work": return "Работаю";
                case "diag": return "Проверяю";
                case "error": return "Что-то не так";
                case "restored": return "Связь вернулась";
                default: return "Отдыхаю";
            }
        }
    }

    public partial class MainWindow
    {
        Bee companion, navBee;
        Grid companionHost;
        Border bubble;
        TextBlock bubbleText;
        DispatcherTimer bubbleTimer;
        string companionState = "";
        DateTime restoredUntil = DateTime.MinValue, lastExitAt = DateTime.MinValue, lastStartFailAt = DateTime.MinValue;
        int lastExitCode = -1;
        string restoredWhat = "";

        // Создаётся один раз и живёт всё время работы окна
        FrameworkElement BuildCompanion()
        {
            companionHost = new Grid();
            companionHost.HorizontalAlignment = HorizontalAlignment.Right;
            companionHost.VerticalAlignment = VerticalAlignment.Top;
            companionHost.Margin = new Thickness(0, -10, -10, 0);
            companion = new Bee(68);
            companion.IsHitTestVisible = true;
            Border hit = new Border();
            hit.Width = 72; hit.Height = 72;
            hit.Background = System.Windows.Media.Brushes.Transparent;
            hit.Cursor = Cursors.Hand;
            hit.HorizontalAlignment = HorizontalAlignment.Right;
            hit.VerticalAlignment = VerticalAlignment.Top;
            hit.Child = companion;
            hit.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e) { OpenAssistant(null); e.Handled = true; };
            ToolTipService.SetInitialShowDelay(hit, 200);
            companionHost.Children.Add(hit);

            bubble = new Border();
            bubble.HorizontalAlignment = HorizontalAlignment.Right;
            bubble.VerticalAlignment = VerticalAlignment.Top;
            bubble.Margin = new Thickness(0, 70, 4, 0);
            bubble.MaxWidth = 200;
            bubble.CornerRadius = new CornerRadius(12);
            bubble.BorderThickness = new Thickness(1);
            bubble.Padding = new Thickness(10, 6, 10, 7);
            bubble.SetResourceReference(Border.BackgroundProperty, "Popup");
            bubble.SetResourceReference(Border.BorderBrushProperty, "StrokeStrong");
            bubble.IsHitTestVisible = false;
            bubble.Visibility = Visibility.Collapsed;
            bubbleText = Theme.Wrap("", 11.5, "Text");
            bubble.Child = bubbleText;
            companionHost.Children.Add(bubble);

            bubbleTimer = new DispatcherTimer();
            bubbleTimer.Interval = TimeSpan.FromSeconds(6);
            bubbleTimer.Tick += delegate
            {
                bubbleTimer.Stop();
                bubble.Visibility = Visibility.Collapsed;
            };
            companionHost.Tag = hit;
            return companionHost;
        }

        void Say(string text)
        {
            if (bubble == null || !cfg.CompanionVisible) return;
            bubbleText.Text = text;
            bubble.Visibility = Visibility.Visible;
            bubble.Opacity = 0;
            Theme.Animate(bubble, UIElement.OpacityProperty, 1, 180);
            bubbleTimer.Stop();
            bubbleTimer.Start();
        }

        // Ошибка, которую пчёлка сейчас «видит»: только подтверждённые факты
        string CompanionError()
        {
            if (!engine.IsRunning && lastExitAt != DateTime.MinValue && DateTime.Now - lastExitAt < TimeSpan.FromMinutes(5))
                return "winws.exe неожиданно завершился";
            if (!engine.IsRunning && lastStartFailAt != DateTime.MinValue && DateTime.Now - lastStartFailAt < TimeSpan.FromMinutes(5))
                return "обход не запустился";
            if (SnapOf("Интернет").Status == ServiceStatus.Unavailable) return "нет подключения к интернету";
            foreach (string n in new string[] { "Discord", "YouTube" })
                if (SnapOf(n).Status == ServiceStatus.Unavailable) return n + " не отвечает";
            return null;
        }

        void UpdateCompanion()
        {
            if (companion == null || engine == null) return;
            bool visible = cfg.CompanionVisible;
            companionHost.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            companion.Animated = visible && cfg.CompanionAnimated;
            if (navBee != null)
            {
                navBee.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                navBee.Animated = visible && cfg.CompanionAnimated;
                if (navArtLabel != null) navArtLabel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            }
            if (!visible)
            {
                companion.StopAnimations();
                if (navBee != null) navBee.StopAnimations();
                return;
            }

            bool running = engine.IsRunning;
            bool busyNow = checking || diagRunning || tuning || labRunning || updating || busy || pulseMon.Measuring;
            string err = CompanionError();
            bool restored = DateTime.Now < restoredUntil;
            string state = CompanionLogic.Resolve(running, busyNow, err != null, restored);
            string mood = CompanionLogic.Mood(state);
            companion.SetMood(mood);
            if (navBee != null) navBee.SetMood(mood);

            string strat = engine.Current != null ? engine.Current.Display : (selected != null ? selected.Display : "—");
            string tip;
            switch (state)
            {
                case "work": tip = "Обход работает: " + strat + "."; break;
                case "diag": tip = checking ? "Проверяю доступность сервисов…" : diagRunning ? "Провожу диагностику…" : tuning ? "Подбираю стратегию…" : labRunning ? "Тестирую стратегию…" : updating ? "Обновляю Flowseal…" : pulseMon.Measuring ? "Измеряю задержку…" : "Применяю изменения…"; break;
                case "error": tip = "Похоже, " + err + "."; break;
                case "restored": tip = restoredWhat + ": связь восстановлена!"; break;
                default: tip = "Обход выключен — отдыхаю."; break;
            }
            ((FrameworkElement)companionHost.Tag).ToolTip = CompanionLogic.Title(state) + "\n" + tip + "\nЩелчок — помощник объяснит, что происходит";
            if (navArtLabel != null)
                navArtLabel.Text = state == "work" ? "Всё работает ♥" : state == "error" ? "Есть проблема…" : state == "restored" ? "Ура, связь!" : state == "diag" ? "Проверяю…" : "Отдыхаю…";

            if (state == companionState) return;
            string prev = companionState;
            companionState = state;
            if (prev.Length == 0) return;   // при запуске не болтаем
            if (state == "error") Say(tip + " Нажмите на меня — подскажу, что делать.");
            else if (state == "restored" || state == "work") Say(tip);
            else if (state == "idle" && prev == "work") Say("Обход выключен. Отдыхаю.");
        }

        // Пчёлка в боковой панели иллюстрированных тем — тоже одна на всё время работы
        Bee NavBee()
        {
            if (navBee == null) navBee = new Bee(88);
            return navBee;
        }
    }
}
