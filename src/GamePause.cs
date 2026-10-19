using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace Veylix
{
    // Игровой режим: пока запущена игра из списка, обход на паузе (winws.exe остановлен, драйвер WinDivert выгружен),
    // после её закрытия обход включается снова с той же стратегией.
    public partial class MainWindow
    {
        string gamePausedFor;        // exe игры, из-за которой обход сейчас на паузе (или null)
        Strategy gamePausedStrategy; // стратегия, которую вернуть после игры
        string gameOverride;         // обход включили вручную во время этой игры — не выключаем, пока она не закроется
        int gameGoneChecks;          // сколько проверок подряд игры нет (защита от перезапуска игры/лаунчера)
        bool gameChecking;

        List<string> GameList { get { return GameWatch.Parse(cfg.GameWatchList); } }

        // Вызывается таймером раз в 2 секунды
        async void CheckGames()
        {
            if (gameChecking || selfTest) return;
            List<string> list = GameList;
            if (!cfg.GameWatch || list.Count == 0)
            {
                gameOverride = null;
                if (gamePausedFor != null) await ResumeAfterGame("игровой режим выключен");
                return;
            }
            gameChecking = true;
            string game;
            try { game = await Task.Run(() => GameWatch.FindRunning(list, GameWatch.RunningProcessNames())); }
            finally { gameChecking = false; }

            if (game != null)
            {
                gameGoneChecks = 0;
                if (gamePausedFor == null && engine.IsRunning && !busy && !tuning && !updating && gameOverride != game)
                    await PauseForGame(game);
                RefreshGameStatus();
                return;
            }
            gameOverride = null;
            if (gamePausedFor != null && ++gameGoneChecks >= 3)   // ~6 секунд без игры
                await ResumeAfterGame(GameWatch.Pretty(gamePausedFor) + " закрыта");
            RefreshGameStatus();
        }

        async Task PauseForGame(string game)
        {
            gamePausedFor = game;
            gamePausedStrategy = engine.Current ?? selected;
            string name = GameWatch.Pretty(game);
            log.Add("info", "Игровой режим: запущена " + name + " (" + game + ") — останавливаю обход и выгружаю WinDivert, чтобы античит не мешал игре.");
            await StopAsync();
            events.Add("game", "info", "Обход на паузе: " + name, "Включится сам после закрытия игры.", "settings");
            Toast("game-pause", "info", "Обход на паузе: " + name, "Античиты не любят драйвер WinDivert. Обход включится сам, когда игра закроется.", null, true);
            if (!IsVisible) TrayBalloon("Обход на паузе", name + " запущена. Обход включится после выхода из игры.", WF.ToolTipIcon.Info);
            RefreshUi();
        }

        async Task ResumeAfterGame(string why)
        {
            Strategy s = gamePausedStrategy ?? selected;
            gamePausedFor = null;
            gamePausedStrategy = null;
            gameGoneChecks = 0;
            RefreshUi();
            if (engine.IsRunning || busy || tuning || updating || s == null) return;
            log.Add("info", "Игровой режим: " + why + " — включаю обход (" + s.Display + ").");
            bool ok = await StartAsync(s);
            if (ok)
            {
                Toast("game-resume", "ok", "Обход снова включён", why + ". Стратегия " + s.Display + ".", null, false);
                if (!IsVisible) TrayBalloon("Обход снова включён", why + ".", WF.ToolTipIcon.Info);
            }
        }

        // Пользователь сам нажал «включить» во время паузы — уважаем выбор до закрытия игры
        void ClearGamePauseByUser()
        {
            if (gamePausedFor == null) return;
            gameOverride = gamePausedFor;
            log.Add("warn", "Обход включён вручную во время игры " + GameWatch.Pretty(gamePausedFor) + ". Игровой режим не будет его выключать, пока игра не закроется.");
            gamePausedFor = null;
            gamePausedStrategy = null;
        }

        // При автозапуске: игра уже идёт — обход не включаем, ждём её закрытия
        bool DeferStartForGame(Strategy s)
        {
            List<string> list = GameList;
            if (!cfg.GameWatch || list.Count == 0) return false;
            string game = GameWatch.FindRunning(list, GameWatch.RunningProcessNames());
            if (game == null) return false;
            gamePausedFor = game;
            gamePausedStrategy = s;
            log.Add("info", "Игровой режим: запущена " + GameWatch.Pretty(game) + " — обход включится после её закрытия.");
            return true;
        }

        // Состояние «пауза для игры» поверх обычного «выключен»
        void ApplyGamePauseState()
        {
            if (gamePausedFor == null || engine.IsRunning || busy || tuning) return;
            string name = GameWatch.Pretty(gamePausedFor);
            statusText.Text = "Пауза: " + name;
            navStatus.Text = "Пауза для игры";
            navSub.Text = name;
            Ui.SetChip(heroChip, "Пауза для игры", "warn");
        }

        // ====================== карточка в настройках ======================

        VToggle tgGames;
        TextBlock gamesDesc, gamesStatus;
        WrapPanel gamesPresets;
        StackPanel gamesList;
        TextBox gamesInput;
        Card gamesCard;

        FrameworkElement BuildGameModeCard()
        {
            Card c = new Card(22);
            c.Margin = new Thickness(0, 0, 0, 16);
            c.Inner = new Thickness(24, 20, 24, 16);
            StackPanel s = new StackPanel();
            s.Children.Add(Ui.Heading("Игровой режим",
                "Некоторые античиты (Vanguard в Valorant и League of Legends, FACEIT и другие) не дают запустить игру, пока загружен драйвер WinDivert, через который работает обход. " +
                "Пока запущена программа из списка, Veylix держит обход на паузе и выгружает драйвер, а после её закрытия включает обход снова."));
            tgGames = new VToggle();
            tgGames.Clicked += delegate
            {
                cfg.GameWatch = !cfg.GameWatch;
                SaveSettings();
                log.Add("info", cfg.GameWatch ? "Игровой режим включён." : "Игровой режим выключен.");
                RefreshGameModeCard();
                CheckGames();
            };
            s.Children.Add(Ui.SettingRow("Ставить обход на паузу во время игры", null, tgGames, out gamesDesc));
            gamesStatus = Theme.Wrap("", 12, "Text2");
            gamesStatus.Margin = new Thickness(0, 0, 0, 6);
            s.Children.Add(gamesStatus);
            s.Children.Add(Ui.Divider());

            TextBlock pt = Theme.T("Быстро добавить", 12.5, "Text2", true);
            pt.Margin = new Thickness(0, 8, 0, 6);
            s.Children.Add(pt);
            gamesPresets = new WrapPanel();
            s.Children.Add(gamesPresets);

            Grid ig = new Grid();
            ig.Margin = new Thickness(0, 10, 0, 8);
            ig.ColumnDefinitions.Add(Theme.Col(0));
            ig.ColumnDefinitions.Add(Theme.Col(-1));
            ig.ColumnDefinitions.Add(Theme.Col(-1));
            Grid ib = new Grid();
            gamesInput = new TextBox();
            gamesInput.Padding = new Thickness(8, 0, 8, 0);
            ib.Children.Add(gamesInput);
            TextBlock ph = Theme.T("Имя программы, например RainbowSix.exe", 12.5, "Text3", false);
            ph.VerticalAlignment = VerticalAlignment.Center;
            ph.Margin = new Thickness(12, 0, 0, 0);
            ph.IsHitTestVisible = false;
            ib.Children.Add(ph);
            gamesInput.TextChanged += delegate { ph.Visibility = gamesInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            gamesInput.KeyDown += delegate(object o, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                AddGames(new string[] { gamesInput.Text });
                gamesInput.Text = "";
            };
            ig.Children.Add(ib);
            VButton add = Ui.Button("Добавить", 0xE710, false);
            add.Margin = new Thickness(8, 0, 0, 0);
            add.Click += delegate { AddGames(new string[] { gamesInput.Text }); gamesInput.Text = ""; };
            Grid.SetColumn(add, 1);
            ig.Children.Add(add);
            VButton pick = Ui.Button("Выбрать .exe", 0xE8E5, false);
            pick.Margin = new Thickness(8, 0, 0, 0);
            pick.Click += delegate
            {
                OpenFileDialog d = new OpenFileDialog();
                d.Title = "Игра или античит, во время которых обход ставится на паузу";
                d.Filter = "Программы (*.exe)|*.exe";
                d.Multiselect = true;
                if (d.ShowDialog(this) == true) AddGames(d.FileNames);
            };
            Grid.SetColumn(pick, 2);
            ig.Children.Add(pick);
            s.Children.Add(ig);

            gamesList = new StackPanel();
            s.Children.Add(gamesList);
            c.Content = s;
            gamesCard = c;
            return c;
        }

        void AddGames(IEnumerable<string> items)
        {
            List<string> list = GameList;
            List<string> added = new List<string>();
            foreach (string it in items)
            {
                string n = GameWatch.Normalize(it);
                if (n == null || list.Contains(n)) continue;
                list.Add(n);
                added.Add(n);
            }
            if (added.Count == 0) return;
            cfg.GameWatchList = GameWatch.Join(list);
            SaveSettings();
            log.Add("info", "Игровой режим: добавлено " + string.Join(", ", added.ToArray()) + ".");
            RefreshGameModeCard();
            CheckGames();
        }

        void RemoveGames(IEnumerable<string> names)
        {
            List<string> list = GameList;
            foreach (string n in names) list.Remove(n);
            cfg.GameWatchList = GameWatch.Join(list);
            SaveSettings();
            RefreshGameModeCard();
            CheckGames();
        }

        void RefreshGameModeCard()
        {
            if (tgGames == null) return;
            List<string> list = GameList;
            tgGames.IsOn = cfg.GameWatch;
            gamesDesc.Text = list.Count == 0 ? "Сначала добавьте игры или античиты ниже." : "Программ в списке: " + list.Count + ". Проверка раз в 2 секунды, без нагрузки на систему.";
            gamesDesc.Visibility = Visibility.Visible;
            RefreshGameStatus();

            gamesPresets.Children.Clear();
            foreach (GameWatch.Preset pr in GameWatch.Presets)
            {
                GameWatch.Preset p = pr;
                bool on = GameWatch.HasPreset(list, p);
                VButton b = new VButton(on ? "AccentSoft" : "Surface2", on ? null : "Stroke", 10);
                b.Height = 32;
                b.Margin = new Thickness(0, 0, 8, 8);
                StackPanel sp = Theme.Row();
                sp.Margin = new Thickness(12, 0, 12, 0);
                sp.VerticalAlignment = VerticalAlignment.Center;
                TextBlock ic = Theme.Icon(on ? 0xE73E : 0xE710, 11, on ? "Accent" : "Text2");
                ic.Margin = new Thickness(0, 1, 7, 0);
                sp.Children.Add(ic);
                sp.Children.Add(Theme.T(p.Title, 12.5, on ? "Accent" : "Text", true));
                b.Content = sp;
                b.ToolTip = p.Hint + ": " + string.Join(", ", p.Names) + (on ? "\nНажмите, чтобы убрать из списка." : "");
                b.Click += delegate { if (on) RemoveGames(p.Names); else AddGames(p.Names); };
                gamesPresets.Children.Add(b);
            }

            gamesList.Children.Clear();
            if (list.Count == 0)
            {
                TextBlock empty = Theme.Wrap("Список пуст. Добавьте готовый набор выше или имя exe игры — его видно в Диспетчере задач (вкладка «Подробности»).", 12, "Text3");
                empty.Margin = new Thickness(2, 2, 0, 4);
                gamesList.Children.Add(empty);
            }
            foreach (string n in list)
            {
                string name = n;
                Grid r = new Grid();
                r.Margin = new Thickness(0, 1, 0, 1);
                r.ColumnDefinitions.Add(Theme.Col(-1));
                r.ColumnDefinitions.Add(Theme.Col(0));
                r.ColumnDefinitions.Add(Theme.Col(-1));
                TextBlock ic = Theme.Icon(0xE7FC, 13, "Text2");
                ic.VerticalAlignment = VerticalAlignment.Center;
                ic.Margin = new Thickness(4, 0, 10, 0);
                r.Children.Add(ic);
                StackPanel t = Theme.Row();
                t.VerticalAlignment = VerticalAlignment.Center;
                t.Children.Add(Theme.T(name, 13, "Text", false));
                string pretty = GameWatch.Pretty(name);
                if (!string.Equals(pretty + ".exe", name, StringComparison.OrdinalIgnoreCase))
                {
                    TextBlock pn = Theme.T("  · " + pretty, 12, "Text3", false);
                    pn.VerticalAlignment = VerticalAlignment.Center;
                    t.Children.Add(pn);
                }
                Grid.SetColumn(t, 1);
                r.Children.Add(t);
                VButton del = Ui.IconButton(0xE711, "Убрать из списка");
                del.Click += delegate { RemoveGames(new string[] { name }); };
                Grid.SetColumn(del, 2);
                r.Children.Add(del);
                gamesList.Children.Add(r);
            }
        }

        void RefreshGameStatus()
        {
            if (gamesStatus == null) return;
            string text, key;
            if (!cfg.GameWatch) { text = "Игровой режим выключен."; key = "Text3"; }
            else if (gamePausedFor != null) { text = "Сейчас: запущена " + GameWatch.Pretty(gamePausedFor) + " — обход на паузе и включится после её закрытия."; key = "Warn"; }
            else if (gameOverride != null) { text = "Сейчас: запущена " + GameWatch.Pretty(gameOverride) + ", но обход включён вручную — не трогаю его до закрытия игры."; key = "Warn"; }
            else { text = "Сейчас: игр из списка не запущено."; key = "Text2"; }
            gamesStatus.Text = text;
            gamesStatus.SetResourceReference(TextBlock.ForegroundProperty, key);
        }
    }
}
