using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace Veylix
{
    // Короткий список изменений по версиям — показывается один раз после обновления
    public static class WhatsNew
    {
        public class Entry
        {
            public string Version;
            public string[] Items;
            public Entry(string version, params string[] items) { Version = version; Items = items; }
        }

        // Новые версии — сверху. Пункты короткие: окно читают за 10 секунд.
        public static readonly Entry[] Log =
        {
            new Entry("2.3.6",
                "Новый раздел «Сайты и DNS»: введите адрес, и Veylix объяснит, почему сайт не открывается, и одной кнопкой добавит его в обход.",
                "Защищённый DNS: Veylix находит подмену DNS провайдером и включает DNS через HTTPS (Cloudflare или Google). Вернуть как было — тоже одной кнопкой.",
                "Это окно: после обновления Veylix коротко рассказывает, что изменилось."),
            new Entry("2.3.5",
                "При автозапуске с Windows обход включается надёжнее: Veylix ждёт сеть и повторяет попытку."),
            new Entry("2.3.4",
                "Игровой режим: пока запущена игра с античитом (Valorant, FACEIT и др.), обход ставится на паузу и включается сам после игры. Настройки → «Игровой режим»."),
            new Entry("2.3.3",
                "Исправлен автозапуск вместе с Windows.",
                "Значок в трее: левый щелчок открывает окно, правый — панель."),
            new Entry("2.3.2",
                "Новая панель в трее в стиле Windows 11: стратегию, IPSet и игровой фильтр можно менять прямо из неё."),
            new Entry("2.3.1",
                "Плитки «IPSet» и «Игровой фильтр» на главной открывают список режимов."),
            new Entry("2.3.0",
                "Плитка «Стратегия» на главной открывает список стратегий вместо переключения по кругу."),
        };

        // Что показать после перехода с версии seen на current. seen пустая — показываем только текущую.
        public static List<Entry> Since(string seen, string current, int max)
        {
            List<Entry> r = new List<Entry>();
            foreach (Entry e in Log)
            {
                if (AppUpdate.Compare(e.Version, current) > 0) continue;   // запись из будущего (тестовая сборка)
                if (string.IsNullOrEmpty(seen)) { if (e.Version == current) r.Add(e); continue; }
                if (AppUpdate.Compare(e.Version, seen) > 0) r.Add(e);
            }
            if (r.Count > max) r.RemoveRange(max, r.Count - max);
            return r;
        }

        // Последние версии до текущей включительно (для команды «Что нового»)
        public static List<Entry> Recent(string current, int max)
        {
            List<Entry> r = new List<Entry>();
            foreach (Entry e in Log)
                if (AppUpdate.Compare(e.Version, current) <= 0 && r.Count < max) r.Add(e);
            return r;
        }

        // Нужно ли показывать окно: обновились (а не поставили впервые) и ещё не видели эту версию
        public static bool ShouldShow(string seen, bool settingsExisted, string current)
        {
            if (string.IsNullOrEmpty(seen)) return settingsExisted;
            return AppUpdate.Compare(current, seen) > 0;
        }
    }

    public partial class MainWindow
    {
        bool whatsNewChecked;

        // Вызывается, когда окно впервые видно пользователю (в том числе после запуска в трее)
        void MaybeShowWhatsNew()
        {
            if (whatsNewChecked || selfTest || !IsVisible || WindowState == WindowState.Minimized) return;
            whatsNewChecked = true;
            string seen = cfg.SeenVersion;
            bool show = WhatsNew.ShouldShow(seen, cfg.LoadedFromFile, Program.Version);
            if (seen != Program.Version)
            {
                cfg.SeenVersion = Program.Version;
                SaveSettings();
            }
            if (!show) return;
            List<WhatsNew.Entry> list = WhatsNew.Since(seen, Program.Version, 3);
            if (list.Count == 0) return;
            // после отрисовки главного окна, чтобы диалог не появлялся поверх пустого экрана
            Dispatcher.BeginInvoke(new Action(delegate { ShowWhatsNew(list, true); }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        void ShowWhatsNew(List<WhatsNew.Entry> list, bool afterUpdate)
        {
            VDialog.ShowContent(this,
                afterUpdate ? "Veylix обновлён до " + Program.Version : "Что нового в Veylix",
                afterUpdate ? "Коротко о том, что изменилось:" : null,
                WhatsNewContent(list), "Понятно", "ok");
        }

        FrameworkElement WhatsNewContent(List<WhatsNew.Entry> list)
        {
            StackPanel sp = new StackPanel();
            for (int i = 0; i < list.Count; i++)
            {
                WhatsNew.Entry e = list[i];
                if (list.Count > 1)
                {
                    Border chip = Ui.Chip("Версия " + e.Version, i == 0 ? "accent" : "idle");
                    chip.Margin = new Thickness(0, i == 0 ? 0 : 12, 0, 6);
                    sp.Children.Add(chip);
                }
                foreach (string it in e.Items)
                {
                    Grid row = new Grid();
                    row.Margin = new Thickness(0, 3, 0, 3);
                    row.ColumnDefinitions.Add(Theme.Col(20));
                    row.ColumnDefinitions.Add(Theme.Col(0));
                    TextBlock dot = Theme.Icon(0xE73E, 11, "Accent");
                    dot.VerticalAlignment = VerticalAlignment.Top;
                    dot.Margin = new Thickness(0, 3, 0, 0);
                    row.Children.Add(dot);
                    TextBlock t = Theme.Wrap(it, 12.5, "Text");
                    Grid.SetColumn(t, 1);
                    row.Children.Add(t);
                    sp.Children.Add(row);
                }
            }
            VButton more = new VButton(null, null, 8);
            more.HorizontalAlignment = HorizontalAlignment.Left;
            more.Margin = new Thickness(-6, 10, 0, 0);
            TextBlock mt = Theme.T("Все изменения на GitHub", 12, "Accent", true);
            mt.Margin = new Thickness(6, 4, 6, 4);
            more.Content = mt;
            more.Click += delegate
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AppUpdate.ReleasesUrl) { UseShellExecute = true }); }
                catch (Exception ex) { log.Add("err", "Не удалось открыть браузер: " + ex.Message); }
            };
            sp.Children.Add(more);
            return sp;
        }
    }
}
