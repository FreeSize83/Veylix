using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Veylix
{
    // Построение страниц интерфейса
    public partial class MainWindow
    {
        class CheckRow
        {
            public Border Icon, Chip;
            public TextBlock Detail;
            public Canvas Spark;
            public VButton Fix;

            public void Set(string level, string detail, string chip)
            {
                Ui.SetStatus(Icon, level);
                Detail.Text = detail;
                Detail.ToolTip = detail;
                Ui.SetChip(Chip, chip, level);
            }
        }

        class StrategyCard
        {
            public VButton Button;
            public Ellipse Dot;
            public TextBlock Result, Name;
        }

        // общие
        Canvas blobs;
        readonly Dictionary<string, FrameworkElement> pages = new Dictionary<string, FrameworkElement>();
        readonly Dictionary<string, VButton> navButtons = new Dictionary<string, VButton>();
        readonly Dictionary<string, Border> navMarks = new Dictionary<string, Border>();
        string page = "";
        TextBlock pageTitle, pageSub, navStatus, navSub;
        Ellipse navDot;

        // иллюстрированные темы
        Border sceneHeader, heroArtHost, navArtHost, heroChip;
        TextBlock navArtLabel;
        Ellipse powerRing;
        readonly Dictionary<string, List<int>> latency = new Dictionary<string, List<int>>();
        const int SparkPoints = 16;

        // главная
        VButton powerBtn, checkBtn;
        TextBlock powerGlyph, statusText, stratText, uptimeText, checkWhen;
        Ellipse halo;
        string haloMode = "";
        List<CheckRow> checkRows;
        TextBlock tileStrat, tileIpset, tileGame, tileTune;
        StackPanel recentList;

        // стратегии
        WrapPanel strategyWrap;
        readonly Dictionary<Strategy, StrategyCard> cards = new Dictionary<Strategy, StrategyCard>();
        VButton tuneBtn;
        Border tuneFill;
        TextBlock tuneStatus, tuneExplain;

        // диагностика
        FrameworkElement diagPage;
        VButton diagBtn;
        TextBlock diagStatus;
        StackPanel diagList;
        bool diagVisited;

        // журнал
        StackPanel logList;
        ScrollViewer logScroll;
        TextBlock logCount;
        string logFilter = "all", logQuery = "";
        bool journalDirty = true;
        VButton[] logSeg = new VButton[6];
        static readonly string[] LogFilterIds = { "all", "err", "warn", "ok", "info", "proc" };

        // настройки
        WrapPanel themeWrap;
        readonly Dictionary<string, VButton> themeTiles = new Dictionary<string, VButton>();
        VButton[] modeSeg = new VButton[3];
        TextBlock modeDesc, glassDesc, autorunDesc;
        VToggle tgGlass, tgAnim, tgAutorun, tgTray, tgCheckUpd, tgToasts;
        TextBlock updInstalled, updLatest, updTitle, updNotes, updStatus;
        Border updNotesBox;
        VButton btnCheckUpd, btnInstall, btnRelease, btnRollback;
        string previewMode;

        // ====================== каркас ======================

        Grid BuildRoot()
        {
            Grid root = new Grid();
            root.Width = DW;
            root.Height = DH;
            root.ClipToBounds = true;

            blobs = BuildBlobs();
            root.Children.Add(blobs);

            Grid layout = new Grid();
            layout.ColumnDefinitions.Add(Theme.Col(212));
            layout.ColumnDefinitions.Add(Theme.Col(0));
            layout.Children.Add(BuildNav());

            // сцена в шапке (только иллюстрированные темы) — под содержимым страницы
            sceneHeader = new Border();
            // сцена уменьшена, чтобы целиком помещаться над карточками
            sceneHeader.Width = Scenes.HeaderW * 0.78;
            sceneHeader.Height = Scenes.HeaderH * 0.78;
            sceneHeader.HorizontalAlignment = HorizontalAlignment.Right;
            sceneHeader.VerticalAlignment = VerticalAlignment.Top;
            sceneHeader.IsHitTestVisible = false;
            sceneHeader.Margin = new Thickness(0, 0, 112, 0);   // не под кнопками окна
            Grid.SetColumn(sceneHeader, 1);
            layout.Children.Add(sceneHeader);

            Grid content = new Grid();
            content.Margin = new Thickness(28, 0, 28, 24);
            content.RowDefinitions.Add(Theme.RowDef(76));
            content.RowDefinitions.Add(Theme.RowDef(0));
            Grid.SetColumn(content, 1);
            content.Children.Add(BuildHeader());

            Grid host = new Grid();
            Grid.SetRow(host, 1);
            AddPage(host, "home", BuildHome());
            AddPage(host, "strategies", BuildStrategies());
            AddPage(host, "pulse", BuildPulse());
            diagPage = BuildDiagnostics();
            AddPage(host, "diag", diagPage);
            AddPage(host, "sites", BuildSites());
            AddPage(host, "assistant", BuildAssistant());
            AddPage(host, "journal", BuildJournal());
            AddPage(host, "events", BuildEvents());
            AddPage(host, "updates", BuildUpdates());
            AddPage(host, "settings", BuildSettings());
            content.Children.Add(host);
            layout.Children.Add(content);
            root.Children.Add(layout);
            root.Children.Add(BuildToasts());
            root.Children.Add(BuildTilePicker());
            root.Children.Add(BuildPalette());

            ShowPage("home");
            ResetChecks();
            RefreshUi();
            RefreshAppearanceTexts();
            RefreshSettingsState();
            RefreshUpdateCard(null);
            UpdateScenes();
            return root;
        }

        void AddPage(Grid host, string name, FrameworkElement el)
        {
            el.Visibility = Visibility.Collapsed;
            pages[name] = el;
            host.Children.Add(el);
        }

        void DragArea(UIElement e)
        {
            e.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs a)
            {
                if (a.ButtonState == MouseButtonState.Pressed && a.ClickCount == 1)
                {
                    try { DragMove(); } catch { }
                }
            };
        }

        // Цветные пятна за стеклом (только Liquid Glass): дают глубину полупрозрачным слоям
        Canvas BuildBlobs()
        {
            Canvas c = new Canvas();
            c.IsHitTestVisible = false;
            double[,] pos = { { -160, -200 }, { 640, -160 }, { 260, 360 } };
            for (int i = 0; i < 3; i++)
            {
                Ellipse e = new Ellipse();
                e.Width = 620;
                e.Height = 620;
                Canvas.SetLeft(e, pos[i, 0]);
                Canvas.SetTop(e, pos[i, 1]);
                e.RenderTransform = new TranslateTransform();
                c.Children.Add(e);
            }
            c.Visibility = Visibility.Collapsed;
            return c;
        }

        void UpdateBlobs()
        {
            if (blobs == null || Theme.Current == null) return;
            Palette p = Theme.Current;
            if (!p.Glass || p.Blobs == null)
            {
                blobs.Visibility = Visibility.Collapsed;
                foreach (UIElement el in blobs.Children)
                {
                    TranslateTransform tt = (TranslateTransform)((Ellipse)el).RenderTransform;
                    tt.BeginAnimation(TranslateTransform.XProperty, null);
                    tt.BeginAnimation(TranslateTransform.YProperty, null);
                }
                return;
            }
            blobs.Visibility = Visibility.Visible;
            // при настоящем размытии пятна слабее — основную глубину даёт размытая подложка
            blobs.Opacity = (Theme.GlassActive ? 0.55 : 1.0) * Math.Min(1.0, Theme.GlowFactor);
            bool animate = Theme.Animations && !cfg.GlassReduced;
            for (int i = 0; i < blobs.Children.Count; i++)
            {
                Ellipse e = (Ellipse)blobs.Children[i];
                RadialGradientBrush rb = new RadialGradientBrush(p.Blobs[i], Color.FromArgb(0, p.Blobs[i].R, p.Blobs[i].G, p.Blobs[i].B));
                rb.Freeze();
                e.Fill = rb;
                TranslateTransform tt = (TranslateTransform)e.RenderTransform;
                if (animate)
                {
                    DoubleAnimation ax = new DoubleAnimation(-40 + i * 25, 50 - i * 20, new Duration(Theme.Dur(17 + i * 4)));
                    ax.AutoReverse = true;
                    ax.RepeatBehavior = RepeatBehavior.Forever;
                    ax.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
                    DoubleAnimation ay = new DoubleAnimation(30 - i * 15, -35 + i * 10, new Duration(Theme.Dur(21 + i * 3)));
                    ay.AutoReverse = true;
                    ay.RepeatBehavior = RepeatBehavior.Forever;
                    ay.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
                    tt.BeginAnimation(TranslateTransform.XProperty, ax);
                    tt.BeginAnimation(TranslateTransform.YProperty, ay);
                }
                else
                {
                    tt.BeginAnimation(TranslateTransform.XProperty, null);
                    tt.BeginAnimation(TranslateTransform.YProperty, null);
                }
            }
        }

        FrameworkElement BuildNav()
        {
            Border nav = new Border();
            nav.SetResourceReference(Border.BackgroundProperty, "Nav");
            nav.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            nav.BorderThickness = new Thickness(0, 0, 1, 0);

            Grid g = new Grid();
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(0));
            g.RowDefinitions.Add(Theme.RowDef(-1));

            Grid brand = new Grid();
            brand.Background = Brushes.Transparent;
            brand.Margin = new Thickness(20, 24, 16, 26);
            brand.ColumnDefinitions.Add(Theme.Col(-1));
            brand.ColumnDefinitions.Add(Theme.Col(0));
            brand.Children.Add(Theme.Logo(38));
            StackPanel bt = new StackPanel();
            bt.Margin = new Thickness(12, 0, 0, 0);
            bt.VerticalAlignment = VerticalAlignment.Center;
            bt.Children.Add(Theme.T("Veylix", 21, "Text", true));
            bt.Children.Add(Theme.T("milyash · Flowseal", 11.5, "Text3", false));
            Grid.SetColumn(bt, 1);
            brand.Children.Add(bt);
            DragArea(brand);
            g.Children.Add(brand);

            StackPanel items = new StackPanel();
            Grid.SetRow(items, 1);
            items.Children.Add(NavItem("home", 0xE80F, "Главная"));
            items.Children.Add(NavItem("strategies", 0xE8FD, "Стратегии"));
            items.Children.Add(NavItem("pulse", 0xE9D2, "Network Pulse"));
            items.Children.Add(NavItem("diag", 0xE9D9, "Диагностика"));
            items.Children.Add(NavItem("sites", 0xE774, "Сайты и DNS"));
            items.Children.Add(NavItem("assistant", 0xEA80, "Помощник"));
            items.Children.Add(NavItem("journal", 0xE7C3, "Журнал"));
            items.Children.Add(NavItem("events", 0xE81C, "События"));
            items.Children.Add(NavItem("updates", 0xE895, "Обновления"));
            items.Children.Add(NavItem("settings", 0xE713, "Настройки"));
            g.Children.Add(items);

            Border spacer = new Border();
            spacer.Background = Brushes.Transparent;
            Grid.SetRow(spacer, 2);
            DragArea(spacer);
            g.Children.Add(spacer);
            navArtHost = new Border();
            navArtHost.VerticalAlignment = VerticalAlignment.Bottom;
            navArtHost.HorizontalAlignment = HorizontalAlignment.Left;
            navArtHost.Margin = new Thickness(12, 0, 0, 6);
            navArtHost.IsHitTestVisible = false;
            Grid.SetRow(navArtHost, 2);
            g.Children.Add(navArtHost);

            Card st = new Card(14);
            st.Inner = new Thickness(14, 12, 14, 12);
            st.Margin = new Thickness(12, 0, 12, 16);
            Grid sg = new Grid();
            sg.ColumnDefinitions.Add(Theme.Col(18));
            sg.ColumnDefinitions.Add(Theme.Col(0));
            navDot = new Ellipse();
            navDot.Width = 9; navDot.Height = 9;
            navDot.VerticalAlignment = VerticalAlignment.Top;
            navDot.Margin = new Thickness(0, 5, 0, 0);
            sg.Children.Add(navDot);
            StackPanel sp = new StackPanel();
            navStatus = Theme.T("Выключен", 13, "Text", true);
            navSub = Theme.T("", 11.5, "Text2", false);
            sp.Children.Add(navStatus);
            sp.Children.Add(navSub);
            Grid.SetColumn(sp, 1);
            sg.Children.Add(sp);
            st.Content = sg;
            Grid.SetRow(st, 3);
            g.Children.Add(st);

            nav.Child = g;
            return nav;
        }

        VButton NavItem(string name, int glyph, string label)
        {
            VButton b = new VButton(null, null, 10);
            b.Height = 36;
            b.Margin = new Thickness(12, 1, 12, 1);
            Grid g = new Grid();
            Border mark = new Border();
            mark.Width = 3;
            mark.Height = 18;
            mark.CornerRadius = new CornerRadius(2);
            mark.HorizontalAlignment = HorizontalAlignment.Left;
            mark.SetResourceReference(Border.BackgroundProperty, "Accent");
            mark.Visibility = Visibility.Hidden;
            g.Children.Add(mark);
            TextBlock ic = Theme.Icon(glyph, 15, "Text2");
            ic.HorizontalAlignment = HorizontalAlignment.Left;
            ic.Margin = new Thickness(16, 0, 0, 0);
            g.Children.Add(ic);
            TextBlock t = Theme.T(label, 13.5, "Text", false);
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(46, 0, 0, 0);
            g.Children.Add(t);
            b.Content = g;
            b.Tag = new object[] { ic, t };
            b.Click += delegate { ShowPage(name); };
            navButtons[name] = b;
            navMarks[name] = mark;
            return b;
        }

        FrameworkElement BuildHeader()
        {
            Grid g = new Grid();
            g.Background = Brushes.Transparent;
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.ColumnDefinitions.Add(Theme.Col(-1));
            DragArea(g);

            StackPanel t = new StackPanel();
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Margin = new Thickness(0, 6, 0, 0);
            pageTitle = Theme.T("", 24, "Text", true);
            pageSub = Theme.T("", 12.5, "Text2", false);
            pageSub.Margin = new Thickness(0, 2, 0, 0);
            t.Children.Add(pageTitle);
            t.Children.Add(pageSub);
            g.Children.Add(t);

            StackPanel wb = Theme.Row();
            wb.VerticalAlignment = VerticalAlignment.Top;
            wb.Margin = new Thickness(0, 14, -14, 0);
            activityPanel = Theme.Row();
            activityPanel.VerticalAlignment = VerticalAlignment.Center;
            wb.Children.Add(activityPanel);
            VButton pal = new VButton("Surface2", "Stroke", 9);
            pal.Height = 30;
            pal.Margin = new Thickness(0, 2, 6, 0);
            StackPanel pp = Theme.Row();
            pp.Margin = new Thickness(10, 0, 10, 0);
            pp.VerticalAlignment = VerticalAlignment.Center;
            TextBlock psi = Theme.Icon(0xE721, 11, "Text2");
            psi.Margin = new Thickness(0, 1, 7, 0);
            pp.Children.Add(psi);
            pp.Children.Add(Theme.T("Ctrl+K", 11.5, "Text2", true));
            pal.Content = pp;
            pal.ToolTip = "Палитра команд: поиск и запуск любых действий";
            pal.Click += delegate { OpenPalette(); };
            wb.Children.Add(pal);
            VButton qd = Ui.IconButton(0xE9D9, "Быстрая диагностика (Ctrl+D)");
            qd.Click += delegate { commandById["diag.run"].Run(); };
            wb.Children.Add(qd);
            VButton min = Ui.IconButton(0xE921, "Свернуть");
            min.Click += delegate { WindowState = WindowState.Minimized; };
            VButton tr = Ui.IconButton(0xE8A0, "Скрыть в трей (обход продолжит работать)");
            tr.Click += delegate { HideToTray(); };
            VButton close = Ui.IconButton(0xE8BB, "Закрыть");
            close.Click += delegate { OnCloseClick(); };
            close.Margin = new Thickness(4, 0, 0, 0);
            wb.Children.Add(min);
            wb.Children.Add(tr);
            wb.Children.Add(close);
            Grid.SetColumn(wb, 1);
            g.Children.Add(wb);
            return g;
        }

        void ShowPage(string name)
        {
            if (!pages.ContainsKey(name)) return;
            bool changed = page != name;
            page = name;
            foreach (KeyValuePair<string, FrameworkElement> kv in pages)
                kv.Value.Visibility = kv.Key == name ? Visibility.Visible : Visibility.Collapsed;
            foreach (KeyValuePair<string, VButton> kv in navButtons)
            {
                bool on = kv.Key == name;
                kv.Value.SetKeys(on ? "AccentSoft" : null, null);
                object[] parts = (object[])kv.Value.Tag;
                ((TextBlock)parts[0]).SetResourceReference(TextBlock.ForegroundProperty, on ? "Accent" : "Text2");
                TextBlock lbl = (TextBlock)parts[1];
                lbl.SetResourceReference(TextBlock.ForegroundProperty, on ? "Accent" : "Text");
                lbl.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                navMarks[kv.Key].Visibility = on ? Visibility.Visible : Visibility.Hidden;
            }
            switch (name)
            {
                case "home": pageTitle.Text = "Главная"; pageSub.Text = "Управление обходом и быстрая проверка доступа"; break;
                case "strategies": pageTitle.Text = "Стратегии"; pageSub.Text = strategies.Count + " стратегий Flowseal " + (engine.InstalledVersion() ?? "") + " · лаборатория, сравнение и автоподбор"; break;
                case "pulse": pageTitle.Text = "Network Pulse"; pageSub.Text = "Задержка и доступность контрольных адресов во времени"; break;
                case "events": pageTitle.Text = "События"; pageSub.Text = "Хронология и статистика текущего сеанса"; break;
                case "assistant": pageTitle.Text = "Помощник"; pageSub.Text = "Что происходит и что с этим делать — простым языком"; break;
                case "diag": pageTitle.Text = "Диагностика"; pageSub.Text = "Состояние приложения, процессов, сети и сервисов"; break;
                case "sites": pageTitle.Text = "Сайты и DNS"; pageSub.Text = "Почему не открывается сайт и как это исправить"; break;
                case "journal": pageTitle.Text = "Журнал"; pageSub.Text = "События Veylix и вывод winws.exe"; break;
                case "updates": pageTitle.Text = "Обновления"; pageSub.Text = "Veylix и Flowseal: версии, изменения и безопасная установка"; break;
                case "settings": pageTitle.Text = "Настройки"; pageSub.Text = "Оформление, запуск вместе с Windows и трей"; break;
            }
            if (name == "journal" && journalDirty) RefreshJournal();
            if (name == "events") RefreshEvents();
            if (name == "assistant") { Analyze(); RenderAssistant(null); }
            if (name == "pulse") { RefreshPulseHeader(); foreach (PulseSeries ps in pulseMon.All.Values) DrawPulseCard(ps); }
            if (name == "strategies") RefreshLab();
            if (name == "sites") { RefreshSiteList(); ReloadDnsState(); }
            if (name == "diag" && !diagVisited && !selfTest)
            {
                diagVisited = true;
                RunDiagnostics();
            }
            if (changed && Theme.Animations)
            {
                FrameworkElement el = pages[name];
                TranslateTransform tt = new TranslateTransform(0, 8);
                el.RenderTransform = tt;
                el.Opacity = 0;
                Theme.Animate(el, UIElement.OpacityProperty, 1, 200);
                Theme.Animate(tt, TranslateTransform.YProperty, 0, 240);
            }
        }

        // ====================== главная ======================

        FrameworkElement BuildHome()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(Theme.RowDef(296));
            g.RowDefinitions.Add(Theme.RowDef(14));
            g.RowDefinitions.Add(Theme.RowDef(108));
            g.RowDefinitions.Add(Theme.RowDef(14));
            g.RowDefinitions.Add(Theme.RowDef(0));

            Grid top = new Grid();
            top.ColumnDefinitions.Add(Theme.Col(360));
            top.ColumnDefinitions.Add(Theme.Col(16));
            top.ColumnDefinitions.Add(Theme.Col(0));
            top.Children.Add(BuildHero());
            FrameworkElement access = BuildAccess();
            Grid.SetColumn(access, 2);
            top.Children.Add(access);
            g.Children.Add(top);

            Grid tiles = new Grid();
            Grid.SetRow(tiles, 2);
            for (int i = 0; i < 7; i++) tiles.ColumnDefinitions.Add(Theme.Col(i % 2 == 1 ? 12 : 0));
            VButton t1 = Tile(1, 0xE8FD, "Стратегия", "Выбрать стратегию из списка. Подробные результаты — на странице «Стратегии».", out tileStrat);
            t1.Click += delegate { OpenStrategyPicker(t1); };
            VButton t2 = Tile(2, 0xE774, "IPSet", "Выбрать режим IPSet: Выкл, Все IP или Список. Как в service.bat.", out tileIpset);
            t2.Click += delegate { OpenIpsetPicker(t2); };
            VButton t3 = Tile(3, 0xE7FC, "Игровой фильтр", "Выбрать режим: Выкл, TCP+UDP, TCP или UDP. Порты 1024–65535 для игр.", out tileGame);
            t3.Click += delegate { OpenGamePicker(t3); };
            VButton t4 = Tile(4, 0xE9F5, "Автоподбор", "Проверить стратегии и выбрать лучшую.", out tileTune);
            tileTune.Text = "Запустить";
            t4.Click += delegate { ShowPage("strategies"); if (!tuning) OnTune(); };
            VButton[] ts = { t1, t2, t3, t4 };
            for (int i = 0; i < ts.Length; i++) { Grid.SetColumn(ts[i], i * 2); tiles.Children.Add(ts[i]); }
            g.Children.Add(tiles);

            Grid bottom = new Grid();
            Grid.SetRow(bottom, 4);
            bottom.ColumnDefinitions.Add(Theme.Col(0));
            bottom.ColumnDefinitions.Add(Theme.Col(16));
            bottom.ColumnDefinitions.Add(Theme.Col(310));
            bottom.Children.Add(BuildMapCard());
            FrameworkElement quick = BuildQuickActions();
            Grid.SetColumn(quick, 2);
            bottom.Children.Add(quick);
            g.Children.Add(bottom);
            return g;
        }

        FrameworkElement BuildHero()
        {
            Card c = new Card(22);
            c.Inner = new Thickness(20);
            StackPanel sp = new StackPanel();
            sp.VerticalAlignment = VerticalAlignment.Center;
            sp.HorizontalAlignment = HorizontalAlignment.Center;

            Grid well = new Grid();
            well.Width = 168;
            well.Height = 168;
            halo = new Ellipse();
            halo.SetResourceReference(Shape.FillProperty, "AccentSoft");
            halo.Opacity = 0;
            well.Children.Add(halo);
            powerRing = new Ellipse();
            powerRing.Width = 146;
            powerRing.Height = 146;
            powerRing.StrokeThickness = 1;
            powerRing.SetResourceReference(Shape.StrokeProperty, "Stroke");
            well.Children.Add(powerRing);
            powerBtn = new VButton("Surface2", "StrokeStrong", 63);
            powerBtn.Width = 126;
            powerBtn.Height = 126;
            powerGlyph = Theme.Icon(0xE7E8, 44, "Accent");
            powerBtn.Content = powerGlyph;
            powerBtn.ToolTip = "Включить / выключить обход";
            powerBtn.Click += delegate { OnPower(); };
            well.Children.Add(powerBtn);
            sp.Children.Add(well);

            statusText = Theme.T("Обход выключен", 21, "Text", true);
            statusText.HorizontalAlignment = HorizontalAlignment.Center;
            statusText.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(statusText);
            stratText = Theme.T("", 13, "Text2", false);
            stratText.HorizontalAlignment = HorizontalAlignment.Center;
            stratText.Margin = new Thickness(0, 3, 0, 0);
            sp.Children.Add(stratText);
            uptimeText = Theme.T("", 12, "Text3", false);
            uptimeText.HorizontalAlignment = HorizontalAlignment.Center;
            uptimeText.Margin = new Thickness(0, 2, 0, 0);
            sp.Children.Add(uptimeText);

            Grid hg = new Grid();
            hg.Children.Add(sp);
            heroChip = Ui.Chip("Обход выключен", "idle");
            heroChip.VerticalAlignment = VerticalAlignment.Top;
            heroChip.Margin = new Thickness(-4, -6, 0, 0);
            hg.Children.Add(heroChip);
            heroArtHost = new Border();
            heroArtHost.HorizontalAlignment = HorizontalAlignment.Right;
            heroArtHost.VerticalAlignment = VerticalAlignment.Top;
            heroArtHost.Margin = new Thickness(0, -10, -12, 0);
            heroArtHost.IsHitTestVisible = false;
            hg.Children.Add(heroArtHost);
            hg.Children.Add(BuildCompanion());
            c.Content = hg;
            return c;
        }

        // Пересоздаёт иллюстрации под текущую тему (пчёлки, сцена в шапке)
        void UpdateScenes()
        {
            if (sceneHeader == null || Theme.Current == null) return;
            Palette p = Theme.Current;
            navArtLabel = null;
            if (p.Scene == null)
            {
                sceneHeader.Child = null;
                heroArtHost.Child = null;
                navArtHost.Child = null;
            }
            else
            {
                Viewbox hv = new Viewbox();
                hv.Child = Scenes.Header(p);
                sceneHeader.Child = hv;
                Viewbox vb = new Viewbox();
                vb.Width = 96;
                vb.Child = Scenes.HeroArt(p);
                heroArtHost.Child = vb;
                // пчёлка боковой панели — тот же объект, что и раньше: переносится в новую сцену, а не создаётся заново
                navArtLabel = Theme.T("", 12.5, "Text2", false);
                navArtLabel.TextTrimming = TextTrimming.None;
                navArtHost.Child = Scenes.NavArt(p, NavBee(), navArtLabel);
                companionState = "";
            }
            haloMode = "";
            if (engine != null && powerBtn != null) RefreshUi();
            UpdateCompanion();
        }

        void ApplyPowerState(string state, string name)
        {
            string fg, title, titleKey, dotKey, navText;
            bool glow = Theme.Current != null && Theme.Current.Glow;
            if (state == "run")
            {
                // в иллюстрированных темах — тёмная кнопка со светящимся зелёным кольцом
                if (glow) powerBtn.SetKeys("Surface2", "Ok"); else powerBtn.SetKeys("Accent", null);
                fg = glow ? "Ok" : "OnAccent"; title = "Обход работает"; titleKey = "Text"; dotKey = "Ok"; navText = "Работает";
            }
            else if (state == "busy" || state == "tune")
            {
                powerBtn.SetKeys("WarnSoft", "Warn");
                fg = "Warn"; title = state == "tune" ? "Подбор стратегии…" : "Подождите…"; titleKey = "Warn"; dotKey = "Warn";
                navText = state == "tune" ? "Автоподбор" : "Применяю…";
            }
            else
            {
                powerBtn.SetKeys("Surface2", "StrokeStrong");
                fg = "Accent"; title = "Обход выключен"; titleKey = "Text"; dotKey = "Text3"; navText = "Выключен";
            }
            powerGlyph.SetResourceReference(TextBlock.ForegroundProperty, fg);
            statusText.Text = title;
            statusText.SetResourceReference(TextBlock.ForegroundProperty, titleKey);
            stratText.Text = "Стратегия: " + name;
            navDot.SetResourceReference(Shape.FillProperty, dotKey);
            navStatus.Text = navText;
            navSub.Text = name;
            Ui.SetChip(heroChip,
                state == "run" ? "Обход включён" : state == "tune" ? "Автоподбор" : state == "busy" ? "Подключение…" : "Обход выключен",
                state == "run" ? "ok" : state == "off" ? "idle" : "busy");
            powerRing.SetResourceReference(Shape.StrokeProperty, state == "run" ? "Ok" : "Stroke");
            powerRing.StrokeThickness = state == "run" && glow ? 3 : 1;
            if (state == "run" && glow && Theme.GlowFactor > 0.01)
            {
                System.Windows.Media.Effects.DropShadowEffect fx = new System.Windows.Media.Effects.DropShadowEffect();
                SolidColorBrush ok = Theme.Res("Ok") as SolidColorBrush;
                fx.Color = ok != null ? ok.Color : Colors.LimeGreen;
                fx.ShadowDepth = 0;
                fx.BlurRadius = 22 * Theme.GlowFactor;
                fx.Opacity = Math.Min(1, 0.9 * Theme.GlowFactor);
                powerRing.Effect = fx;
            }
            else powerRing.Effect = null;

            string hm = state == "off" ? "off" : state == "run" ? "run" : "busy";
            if (hm == haloMode) return;
            haloMode = hm;
            halo.SetResourceReference(Shape.FillProperty, hm == "busy" ? "WarnSoft" : "AccentSoft");
            if (hm == "off")
            {
                Theme.Animate(halo, UIElement.OpacityProperty, 0, 300);
            }
            else if (Theme.Animations)
            {
                double top = Math.Min(1.0, Math.Max(0.0, Theme.GlowFactor));
                DoubleAnimation a = new DoubleAnimation(0.35 * top, top, new Duration(Theme.Dur(hm == "busy" ? 0.8 : 2.2)));
                a.AutoReverse = true;
                a.RepeatBehavior = RepeatBehavior.Forever;
                a.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
                halo.BeginAnimation(UIElement.OpacityProperty, a);
            }
            else
            {
                halo.BeginAnimation(UIElement.OpacityProperty, null);
                halo.Opacity = 0.8;
            }
        }

        FrameworkElement BuildAccess()
        {
            Card c = new Card(22);
            c.Inner = new Thickness(22, 18, 22, 16);
            Grid g = new Grid();
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(0));
            g.RowDefinitions.Add(Theme.RowDef(-1));

            Grid head = new Grid();
            head.ColumnDefinitions.Add(Theme.Col(0));
            head.ColumnDefinitions.Add(Theme.Col(-1));
            head.Children.Add(Ui.Heading("Доступность сервисов", "DNS → TCP → TLS → HTTP · сбои перепроверяются"));
            checkBtn = Ui.Button("Проверить", 0xE72C, false);
            checkBtn.VerticalAlignment = VerticalAlignment.Top;
            checkBtn.Click += delegate { RunAccessTest(); };
            Grid.SetColumn(checkBtn, 1);
            head.Children.Add(checkBtn);
            g.Children.Add(head);

            StackPanel rows = new StackPanel();
            rows.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(rows, 1);
            checkRows = new List<CheckRow>();
            checkRows.Add(MakeCheckRow(rows, "Интернет"));
            checkRows.Add(MakeCheckRow(rows, "Discord"));
            checkRows.Add(MakeCheckRow(rows, "YouTube"));
            g.Children.Add(rows);

            Grid foot = new Grid();
            Grid.SetRow(foot, 2);
            foot.ColumnDefinitions.Add(Theme.Col(0));
            foot.ColumnDefinitions.Add(Theme.Col(-1));
            checkWhen = Theme.T("", 11.5, "Text3", false);
            checkWhen.VerticalAlignment = VerticalAlignment.Center;
            foot.Children.Add(checkWhen);
            VButton more = LinkButton("Подробнее в диагностике");
            more.Click += delegate { ShowPage("diag"); };
            Grid.SetColumn(more, 1);
            foot.Children.Add(more);
            g.Children.Add(foot);
            c.Content = g;
            return c;
        }

        CheckRow MakeCheckRow(StackPanel parent, string name)
        {
            Grid row = new Grid();
            row.Margin = new Thickness(0, 6, 0, 6);
            row.ColumnDefinitions.Add(Theme.Col(46));
            row.ColumnDefinitions.Add(Theme.Col(0));
            row.ColumnDefinitions.Add(Theme.Col(98));
            row.ColumnDefinitions.Add(Theme.Col(-1));
            CheckRow r = new CheckRow();
            r.Spark = new Canvas();
            r.Spark.Width = 84;
            r.Spark.Height = 26;
            r.Spark.Background = Brushes.Transparent;
            r.Spark.HorizontalAlignment = HorizontalAlignment.Left;
            r.Spark.VerticalAlignment = VerticalAlignment.Center;
            r.Spark.ToolTip = "История задержки: появится после двух проверок";
            Grid.SetColumn(r.Spark, 2);
            row.Children.Add(r.Spark);
            r.Icon = Ui.StatusIcon(34);
            row.Children.Add(r.Icon);
            StackPanel sp = new StackPanel();
            sp.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(Theme.T(name, 14, "Text", true));
            r.Detail = Theme.T("", 12, "Text2", false);
            r.Detail.Margin = new Thickness(0, 1, 12, 0);
            sp.Children.Add(r.Detail);
            Grid.SetColumn(sp, 1);
            row.Children.Add(sp);
            r.Chip = Ui.Chip("", "idle");
            StackPanel right = new StackPanel();
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Children.Add(r.Chip);
            r.Fix = FixButton("svc:" + name);
            r.Fix.Margin = new Thickness(-8, 0, -8, -6);
            r.Fix.Visibility = Visibility.Collapsed;
            right.Children.Add(r.Fix);
            Grid.SetColumn(right, 3);
            row.Children.Add(right);
            parent.Children.Add(row);
            return r;
        }

        static string Level(ServiceStatus s)
        {
            switch (s)
            {
                case ServiceStatus.Ok: return "ok";
                case ServiceStatus.Partial: return "warn";
                case ServiceStatus.Unavailable: return "err";
                case ServiceStatus.NoNetwork: return "warn";
                default: return "idle";
            }
        }

        void ShowChecks(ServiceResult[] res)
        {
            lastChecksTime = DateTime.Now;
            lastChecksWithEngine = engine.IsRunning;
            Analyze();
            for (int i = 0; i < res.Length && i < checkRows.Count; i++)
            {
                ServiceResult s = res[i];
                string detail = s.Summary;
                if (s.Status == ServiceStatus.Ok)
                {
                    int tls = 0, n = 0;
                    foreach (ProbeResult p in s.Probes) if (p.TlsMs >= 0) { tls += p.TlsMs; n++; }
                    if (n > 0) detail += " · TLS ≈ " + (tls / n) + " мс";
                }
                checkRows[i].Set(Level(s.Status), detail, NetCheck.StatusWord(s.Status));
                checkRows[i].Fix.Visibility = HasFix("svc:" + s.Name) ? Visibility.Visible : Visibility.Collapsed;
                PushLatency(s);
                DrawSpark(checkRows[i], s.Name, Level(s.Status));
            }
            checkWhen.Text = "Проверено в " + DateTime.Now.ToString("HH:mm:ss") + (engine.IsRunning && engine.Current != null ? " · стратегия " + engine.Current.Display : " · обход выключен");
        }

        // Средняя задержка установления соединения по ответившим адресам (TLS, иначе TCP); -1 — сервис не ответил
        void PushLatency(ServiceResult s)
        {
            if (s.Probes.Count == 0) return;
            int sum = 0, n = 0;
            foreach (ProbeResult p in s.Probes)
            {
                if (!p.Passed) continue;
                int ms = p.TlsMs >= 0 ? p.TlsMs : p.TcpMs;
                if (ms >= 0) { sum += ms; n++; }
            }
            List<int> h;
            if (!latency.TryGetValue(s.Name, out h)) { h = new List<int>(); latency[s.Name] = h; }
            h.Add(n > 0 ? sum / n : -1);
            if (h.Count > SparkPoints) h.RemoveRange(0, h.Count - SparkPoints);
        }

        void DrawSpark(CheckRow row, string name, string level)
        {
            Canvas c = row.Spark;
            c.Children.Clear();
            List<int> h;
            if (!latency.TryGetValue(name, out h) || h.Count < 2) return;
            double w = c.Width, ht = c.Height;
            int max = 1, min = int.MaxValue, fails = 0;
            foreach (int v in h)
            {
                if (v < 0) { fails++; continue; }
                if (v > max) max = v;
                if (v < min) min = v;
            }
            string key = level == "ok" ? "Ok" : level == "err" ? "Err" : "Warn";
            double step = w / (SparkPoints - 1);
            double x0 = w - step * (h.Count - 1);
            Polyline line = null;
            for (int i = 0; i < h.Count; i++)
            {
                double x = x0 + step * i;
                if (h[i] < 0)
                {
                    line = null;
                    Ellipse f = new Ellipse();
                    f.Width = 4; f.Height = 4;
                    f.SetResourceReference(Shape.FillProperty, "Err");
                    Canvas.SetLeft(f, x - 2);
                    Canvas.SetTop(f, ht - 4);
                    c.Children.Add(f);
                    continue;
                }
                double y = ht - 3 - (h[i] / (double)max) * (ht - 7);
                if (line == null)
                {
                    line = new Polyline();
                    line.StrokeThickness = 1.8;
                    line.StrokeLineJoin = PenLineJoin.Round;
                    line.SetResourceReference(Shape.StrokeProperty, key);
                    c.Children.Add(line);
                }
                line.Points.Add(new Point(x, y));
                if (i == h.Count - 1)
                {
                    Ellipse d = new Ellipse();
                    d.Width = 5; d.Height = 5;
                    d.SetResourceReference(Shape.FillProperty, key);
                    Canvas.SetLeft(d, x - 2.5);
                    Canvas.SetTop(d, y - 2.5);
                    c.Children.Add(d);
                }
            }
            c.ToolTip = "Последние " + h.Count + " проверок: " +
                (min == int.MaxValue ? "ни одного ответа" : "задержка " + min + "–" + max + " мс") +
                (fails > 0 ? ", без ответа: " + fails : "");
        }

        VButton Tile(int index, int glyph, string caption, string tip, out TextBlock value)
        {
            VButton b = new VButton("Tile" + index, "TileStroke", 18);
            b.ToolTip = tip;
            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(16, 12, 16, 10);
            Border ic = new Border();
            ic.Width = 30; ic.Height = 30;
            ic.CornerRadius = new CornerRadius(11);
            ic.HorizontalAlignment = HorizontalAlignment.Left;
            ic.SetResourceReference(Border.BackgroundProperty, "TileIconBg");
            ic.Child = Theme.Icon(glyph, 16, "TileIcon");
            sp.Children.Add(ic);
            value = Theme.T("", 18, "TileFg", true);
            value.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(value);
            sp.Children.Add(Theme.T(caption, 12.5, "TileFg2", false));
            b.Content = sp;
            return b;
        }

        VButton LinkButton(string text)
        {
            VButton b = new VButton(null, null, 8);
            b.Padding = new Thickness(0);
            TextBlock t = Theme.T(text, 12, "Accent", true);
            t.Margin = new Thickness(8, 4, 8, 4);
            b.Content = t;
            b.Tag = t;
            return b;
        }

        WrapPanel quickWrap;

        FrameworkElement BuildQuickActions()
        {
            Card c = new Card(22);
            c.Inner = new Thickness(20, 14, 18, 8);
            StackPanel sp = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(Theme.Col(0));
            head.ColumnDefinitions.Add(Theme.Col(-1));
            head.Children.Add(Theme.T("Быстрые действия", 15, "Text", true));
            VButton cfgBtn = LinkButton("Настроить");
            cfgBtn.ToolTip = "Откроется палитра команд: звёздочка или Ctrl+P закрепляет действие здесь";
            cfgBtn.Margin = new Thickness(0, -4, -8, 0);
            cfgBtn.Click += delegate { OpenPalette(); paletteHint.Text = "Нажмите ☆ у команды или Ctrl+P, чтобы закрепить её в «Быстрых действиях»"; };
            Grid.SetColumn(cfgBtn, 1);
            head.Children.Add(cfgBtn);
            sp.Children.Add(head);
            quickWrap = new WrapPanel();
            quickWrap.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(quickWrap);
            c.Content = Theme.Scroll(sp);
            RebuildQuickActions();
            return c;
        }

        void RebuildQuickActions()
        {
            if (quickWrap == null) return;
            quickWrap.Children.Clear();
            foreach (string id in cfg.PinnedActions.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                AppCommand cmd;
                if (!commandById.TryGetValue(id, out cmd)) continue;
                AppCommand cc = cmd;
                VButton b = new VButton("Surface2", "Stroke", 9);
                b.Height = 32;
                b.Margin = new Thickness(0, 0, 6, Math.Round(6 * Theme.Density));
                StackPanel row = Theme.Row();
                row.Margin = new Thickness(10, 0, 11, 0);
                row.VerticalAlignment = VerticalAlignment.Center;
                TextBlock ic = Theme.Icon(cc.Glyph, 11.5, "Accent");
                ic.Margin = new Thickness(0, 1, 7, 0);
                row.Children.Add(ic);
                row.Children.Add(Theme.T(cc.ShortTitle, 12, "Text", true));
                b.Content = row;
                b.ToolTip = cc.CurrentTitle + (cc.Shortcut.Length > 0 ? " (" + cc.Shortcut + ")" : "");
                b.Click += delegate
                {
                    string why = cc.Reason();
                    if (why != null) Toast("cmd-" + cc.Id, "info", cc.CurrentTitle, "Сейчас недоступно: " + why + ".", null, false);
                    else cc.Run();
                };
                quickWrap.Children.Add(b);
            }
            if (quickWrap.Children.Count == 0)
                quickWrap.Children.Add(Theme.Wrap("Нет закреплённых действий. Нажмите «Настроить».", 12, "Text3"));
        }

        FrameworkElement BuildRecent()
        {
            Card c = new Card(22);
            c.Inner = new Thickness(22, 18, 22, 14);
            Grid g = new Grid();
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(0));
            Grid head = new Grid();
            head.ColumnDefinitions.Add(Theme.Col(0));
            head.ColumnDefinitions.Add(Theme.Col(-1));
            head.Children.Add(Ui.Heading("Последние события", null));
            VButton all = LinkButton("Весь журнал");
            all.Click += delegate { ShowPage("journal"); };
            all.Margin = new Thickness(0, -4, -8, 0);
            Grid.SetColumn(all, 1);
            head.Children.Add(all);
            g.Children.Add(head);
            recentList = new StackPanel();
            recentList.Margin = new Thickness(0, 10, 0, 0);
            recentList.ClipToBounds = true;
            Grid.SetRow(recentList, 1);
            g.Children.Add(recentList);
            c.Content = g;
            return c;
        }

        void AddRecent(LogEntry e)
        {
            if (recentList == null || e.Level == "proc") return;
            Grid row = new Grid();
            row.Margin = new Thickness(0, 2, 0, 3);
            row.ColumnDefinitions.Add(Theme.Col(16));
            row.ColumnDefinitions.Add(Theme.Col(68));
            row.ColumnDefinitions.Add(Theme.Col(0));
            Ellipse dot = new Ellipse();
            dot.Width = 7; dot.Height = 7;
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.SetResourceReference(Shape.FillProperty, Ui.FgKey(e.Level == "info" ? "info" : e.Level));
            row.Children.Add(dot);
            TextBlock tm = Theme.T(e.Time.ToString("HH:mm:ss"), 11.5, "Text3", false);
            tm.FontFamily = Theme.Mono;
            tm.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tm, 1);
            row.Children.Add(tm);
            TextBlock tx = Theme.T(e.Text, 12.5, "Text", false);
            tx.ToolTip = e.Text;
            Grid.SetColumn(tx, 2);
            row.Children.Add(tx);
            recentList.Children.Insert(0, row);
            while (recentList.Children.Count > 4) recentList.Children.RemoveAt(recentList.Children.Count - 1);
        }

        // ====================== стратегии ======================

        FrameworkElement BuildStrategies()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(16));
            g.RowDefinitions.Add(Theme.RowDef(0));

            Card c = new Card(22);
            c.Inner = new Thickness(22, 16, 22, 16);
            StackPanel sp = new StackPanel();
            Grid head = new Grid();
            head.ColumnDefinitions.Add(Theme.Col(0));
            head.ColumnDefinitions.Add(Theme.Col(-1));
            StackPanel h = Ui.Heading("Лаборатория стратегий",
                "Каждая стратегия проверяется на трёх адресах Discord и трёх адресах YouTube (DNS → TCP → TLS → HTTP, неудачи — повторно). " +
                "Текущая стратегия проверяется первой и остаётся, если работает; победитель подтверждается контрольной проверкой.");
            h.Margin = new Thickness(0, 0, 24, 0);
            head.Children.Add(h);
            tuneBtn = Ui.Button("Запустить автоподбор", 0xE768, true);
            tuneBtn.VerticalAlignment = VerticalAlignment.Top;
            tuneBtn.Click += delegate { OnTune(); };
            Grid.SetColumn(tuneBtn, 1);
            head.Children.Add(tuneBtn);
            sp.Children.Add(head);

            Border track = new Border();
            track.Height = 6;
            track.CornerRadius = new CornerRadius(3);
            track.Margin = new Thickness(0, 12, 0, 0);
            track.SetResourceReference(Border.BackgroundProperty, "Surface2");
            tuneFill = new Border();
            tuneFill.CornerRadius = new CornerRadius(3);
            tuneFill.HorizontalAlignment = HorizontalAlignment.Left;
            tuneFill.Width = 0;
            tuneFill.SetResourceReference(Border.BackgroundProperty, "Accent");
            track.Child = tuneFill;
            sp.Children.Add(track);
            tuneStatus = Theme.T("Подбор ещё не запускался", 12, "Text2", false);
            tuneStatus.Margin = new Thickness(0, 8, 0, 0);
            sp.Children.Add(tuneStatus);
            tuneExplain = Theme.Wrap("", 12.5, "Text");
            tuneExplain.Margin = new Thickness(0, 4, 0, 0);
            sp.Children.Add(tuneExplain);
            sp.Children.Add(BuildLabPanel());
            c.Content = sp;
            g.Children.Add(c);

            Grid list = new Grid();
            list.RowDefinitions.Add(Theme.RowDef(-1));
            list.RowDefinitions.Add(Theme.RowDef(0));
            list.Children.Add(BuildLabSort());
            strategyWrap = new WrapPanel();
            ScrollViewer sv = Theme.Scroll(strategyWrap);
            Grid.SetRow(sv, 1);
            list.Children.Add(sv);
            Grid.SetRow(list, 2);
            g.Children.Add(list);
            RebuildStrategyCards();
            return g;
        }

        void SetTuneProgress(double fraction, string text)
        {
            tuneStatus.Text = text;
            FrameworkElement track = (FrameworkElement)tuneFill.Parent;
            double w = Math.Max(0, Math.Min(1, fraction)) * Math.Max(0, (track.ActualWidth > 0 ? track.ActualWidth : 860));
            Theme.Animate(tuneFill, FrameworkElement.WidthProperty, w, 300);
        }

        void RebuildStrategyCards()
        {
            if (strategyWrap == null) return;
            strategyWrap.Children.Clear();
            cards.Clear();
            foreach (Strategy s in OrderedStrategies())
            {
                Strategy st = s;
                StrategyCard sc = new StrategyCard();
                VButton b = new VButton("Surface", "Stroke", 14);
                b.Width = 212;
                b.Height = 90;
                b.Margin = new Thickness(0, 0, 12, Math.Round(12 * Theme.Density));
                Grid g = new Grid();
                g.Margin = new Thickness(14, 12, 14, 11);
                g.RowDefinitions.Add(Theme.RowDef(-1));
                g.RowDefinitions.Add(Theme.RowDef(-1));
                g.RowDefinitions.Add(Theme.RowDef(0));
                g.RowDefinitions.Add(Theme.RowDef(-1));
                Grid top = new Grid();
                top.ColumnDefinitions.Add(Theme.Col(0));
                top.ColumnDefinitions.Add(Theme.Col(-1));
                sc.Name = Theme.T(s.Display, 14.5, "Text", true);
                top.Children.Add(sc.Name);
                sc.Dot = new Ellipse();
                sc.Dot.Width = 8; sc.Dot.Height = 8;
                sc.Dot.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(sc.Dot, 1);
                top.Children.Add(sc.Dot);
                g.Children.Add(top);
                TextBlock note = Theme.T(s.Note, 11.5, s.Warn ? "Warn" : "Text2", false);
                Grid.SetRow(note, 1);
                g.Children.Add(note);
                sc.Result = Theme.T("", 11.5, "Text3", false);
                Grid.SetRow(sc.Result, 3);
                g.Children.Add(sc.Result);
                b.Content = g;
                b.Click += delegate { OnPickStrategy(st); };
                sc.Button = b;
                cards[s] = sc;
                strategyWrap.Children.Add(b);
            }
            RefreshStrategyCards();
        }

        void RefreshStrategyCards()
        {
            if (cards.Count == 0) return;
            bool run = engine.IsRunning;
            Strategy cur = engine.Current;
            foreach (KeyValuePair<Strategy, StrategyCard> kv in cards)
            {
                Strategy s = kv.Key;
                StrategyCard sc = kv.Value;
                bool sel = object.ReferenceEquals(s, selected);
                sc.Button.SetKeys(sel ? "AccentSoft" : "Surface", sel ? "Accent" : "Stroke");
                bool active = run && object.ReferenceEquals(s, cur);
                sc.Dot.SetResourceReference(Shape.FillProperty, active ? "Ok" : "Stroke");
                bool pref = s.Display == cfg.PreferredStrategy;
                sc.Name.Text = s.Display + (pref ? "  ★" : "");
                string tip = System.IO.Path.GetFileName(s.BatPath) + (active ? "\nСейчас работает" : "") + (pref ? "\n★ Предпочтительная" : "");
                if (s.LastScore < 0)
                {
                    sc.Result.Text = active ? "работает · не проверялась" : "не проверялась";
                    sc.Result.SetResourceReference(TextBlock.ForegroundProperty, "Text3");
                }
                else
                {
                    sc.Result.Text = (s.LastScore == s.LastMax ? "✓ " : s.LastScore == 0 ? "✕ " : "◐ ") + s.LastSummary;
                    sc.Result.SetResourceReference(TextBlock.ForegroundProperty, s.LastScore == s.LastMax ? "Ok" : s.LastScore == 0 ? "Err" : "Warn");
                    tip += "\nПроверена в " + s.LastTested.ToString("HH:mm:ss") + ": " + s.LastSummary;
                    if (s.LastResults != null) tip += "\nПодробности — кнопка «Подробности» в лаборатории";
                }
                sc.Button.ToolTip = tip;
            }
            if (tileStrat != null && selected != null) tileStrat.Text = selected.Display;
        }

        // ====================== диагностика ======================

        FrameworkElement BuildDiagnostics()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(16));
            g.RowDefinitions.Add(Theme.RowDef(0));

            Card c = new Card(22);
            c.Inner = new Thickness(22, 16, 22, 16);
            Grid head = new Grid();
            head.ColumnDefinitions.Add(Theme.Col(0));
            head.ColumnDefinitions.Add(Theme.Col(-1));
            StackPanel left = new StackPanel();
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(Theme.T("Центр диагностики", 16, "Text", true));
            diagStatus = Theme.T("Диагностика ещё не запускалась", 12, "Text2", false);
            diagStatus.Margin = new Thickness(0, 3, 0, 0);
            left.Children.Add(diagStatus);
            head.Children.Add(left);
            StackPanel btns = Theme.Row();
            diagBtn = Ui.Button("Запустить", 0xE72C, true);
            diagBtn.Click += delegate { RunDiagnostics(); };
            VButton copy = Ui.Button("Копировать отчёт", 0xE8C8, false);
            copy.Margin = new Thickness(8, 0, 0, 0);
            copy.ToolTip = "Без имени пользователя, компьютера и полных IP-адресов";
            copy.Click += delegate { CopyReport(); };
            VButton save = Ui.Button("Сохранить", 0xE74E, false);
            save.Margin = new Thickness(8, 0, 0, 0);
            save.Click += delegate { SaveReport(); };
            btns.Children.Add(diagBtn);
            btns.Children.Add(copy);
            btns.Children.Add(save);
            Grid.SetColumn(btns, 1);
            head.Children.Add(btns);
            c.Content = head;
            g.Children.Add(c);

            diagList = new StackPanel();
            diagList.Margin = new Thickness(0, 0, 6, 0);
            Card ph = new Card(22);
            ph.Content = Theme.Wrap("Здесь появятся сведения о правах, файлах Flowseal, процессах winws.exe, драйвере WinDivert, конфликтующих программах, прокси, DNS и доступности Discord/YouTube по слоям. Диагностика только читает состояние системы и ничего не меняет.", 12.5, "Text2");
            diagList.Children.Add(ph);
            ScrollViewer sv = Theme.Scroll(diagList);
            Grid.SetRow(sv, 2);
            g.Children.Add(sv);
            return g;
        }

        void RenderDiagnostics()
        {
            if (lastDiag == null) return;
            Analyze();
            diagList.Children.Clear();
            string group = null;
            StackPanel body = null;
            foreach (DiagItem d in lastDiag)
            {
                if (d.Group != group)
                {
                    group = d.Group;
                    body = DiagGroup(group);
                }
                body.Children.Add(DiagRow(d.Level, d.Name, d.Value, d.Hint, (d.Level == "err" || d.Level == "warn") && HasFix("diag:" + d.Name) ? "diag:" + d.Name : null));
            }
            if (lastChecks != null)
            {
                body = DiagGroup("Доступность (по слоям)");
                foreach (ServiceResult s in lastChecks)
                {
                    body.Children.Add(DiagRow(Level(s.Status), s.Name, NetCheck.StatusWord(s.Status) + " — " + s.Summary, null, HasFix("svc:" + s.Name) ? "svc:" + s.Name : null));
                    foreach (ProbeResult p in s.Probes)
                    {
                        Grid r = new Grid();
                        r.Margin = new Thickness(44, 0, 0, 4);
                        r.ColumnDefinitions.Add(Theme.Col(20));
                        r.ColumnDefinitions.Add(Theme.Col(200));
                        r.ColumnDefinitions.Add(Theme.Col(0));
                        TextBlock mk = Theme.T(p.Passed ? "✓" : (p.Status == ProbeStatus.Partial ? "◐" : "✕"), 12, p.Passed ? "Ok" : (p.Status == ProbeStatus.Partial ? "Warn" : "Err"), true);
                        r.Children.Add(mk);
                        TextBlock host = Theme.T(p.Target.Host, 12, "Text", true);
                        Grid.SetColumn(host, 1);
                        r.Children.Add(host);
                        string info = p.Timing();
                        if (!p.Passed) info = (info.Length > 0 ? info + " · " : "") + "[" + p.Stage + "] " + p.Detail;
                        else if (p.Flaky) info += " · со второй попытки";
                        TextBlock tx = Theme.Wrap(info, 12, p.Passed ? "Text2" : "Text");
                        Grid.SetColumn(tx, 2);
                        r.Children.Add(tx);
                        body.Children.Add(r);
                    }
                }
            }
        }

        StackPanel DiagGroup(string title)
        {
            Card c = new Card(20);
            c.Margin = new Thickness(0, 0, 0, 14);
            c.Inner = new Thickness(22, 16, 22, 12);
            StackPanel sp = new StackPanel();
            TextBlock t = Theme.T(title, 14.5, "Text", true);
            t.Margin = new Thickness(0, 0, 0, 8);
            sp.Children.Add(t);
            c.Content = sp;
            diagList.Children.Add(c);
            return sp;
        }

        FrameworkElement DiagRow(string level, string name, string value, string hint, string fixAnchor)
        {
            Grid r = new Grid();
            r.Margin = new Thickness(0, 4 * Theme.Density, 0, 6 * Theme.Density);
            r.ColumnDefinitions.Add(Theme.Col(44));
            r.ColumnDefinitions.Add(Theme.Col(210));
            r.ColumnDefinitions.Add(Theme.Col(0));
            Border ic = Ui.StatusIcon(28);
            Ui.SetStatus(ic, level == "info" ? "idle" : level);
            ic.VerticalAlignment = VerticalAlignment.Top;
            ic.HorizontalAlignment = HorizontalAlignment.Left;
            r.Children.Add(ic);
            TextBlock n = Theme.T(name, 13, "Text", true);
            n.Margin = new Thickness(0, 5, 12, 0);
            n.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(n, 1);
            r.Children.Add(n);
            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(0, 5, 0, 0);
            TextBlock v = Theme.Wrap(value, 13, "Text");
            sp.Children.Add(v);
            if (!string.IsNullOrEmpty(hint))
            {
                TextBlock h = Theme.Wrap(hint, 12, "Text2");
                h.Margin = new Thickness(0, 2, 0, 0);
                sp.Children.Add(h);
            }
            if (fixAnchor != null)
            {
                VButton fix = FixButton(fixAnchor);
                fix.HorizontalAlignment = HorizontalAlignment.Left;
                fix.Margin = new Thickness(-8, 0, 0, 0);
                sp.Children.Add(fix);
            }
            Grid.SetColumn(sp, 2);
            r.Children.Add(sp);
            return r;
        }

        // ====================== журнал ======================

        FrameworkElement BuildJournal()
        {
            Grid g = new Grid();
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(14));
            g.RowDefinitions.Add(Theme.RowDef(0));

            Grid bar = new Grid();
            bar.ColumnDefinitions.Add(Theme.Col(250));
            bar.ColumnDefinitions.Add(Theme.Col(12));
            bar.ColumnDefinitions.Add(Theme.Col(-1));
            bar.ColumnDefinitions.Add(Theme.Col(0));
            bar.ColumnDefinitions.Add(Theme.Col(-1));

            Grid search = new Grid();
            TextBox box = new TextBox();
            box.Padding = new Thickness(26, 0, 8, 0);
            search.Children.Add(box);
            TextBlock sic = Theme.Icon(0xE721, 12, "Text3");
            sic.HorizontalAlignment = HorizontalAlignment.Left;
            sic.Margin = new Thickness(12, 0, 0, 0);
            sic.IsHitTestVisible = false;
            search.Children.Add(sic);
            TextBlock ph = Theme.T("Поиск по журналу", 13, "Text3", false);
            ph.VerticalAlignment = VerticalAlignment.Center;
            ph.Margin = new Thickness(38, 0, 0, 0);
            ph.IsHitTestVisible = false;
            search.Children.Add(ph);
            box.TextChanged += delegate
            {
                ph.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                logQuery = box.Text.Trim();
                RefreshJournal();
            };
            bar.Children.Add(search);

            Border seg = Ui.Segmented(new string[] { "Все", "Ошибки", "Предупр.", "Успех", "Инфо", "winws" }, logSeg, delegate(int i)
            {
                logFilter = LogFilterIds[i];
                Ui.SelectSegment(logSeg, i);
                RefreshJournal();
            });
            Ui.SelectSegment(logSeg, 0);
            Grid.SetColumn(seg, 2);
            bar.Children.Add(seg);

            StackPanel btns = Theme.Row();
            VButton exp = Ui.Button("Экспорт", 0xE74E, false);
            exp.ToolTip = "Сохранить показанные записи в .txt (личные данные скрываются)";
            exp.Click += delegate { ExportLog(); };
            VButton clr = Ui.IconButton(0xE74D, "Очистить журнал в окне (файл veylix.log сохраняется)");
            clr.Margin = new Thickness(6, 0, 0, 0);
            clr.Click += delegate { log.Clear(); };
            VButton fld = Ui.IconButton(0xE838, "Открыть папку с veylix.log");
            fld.Click += delegate { OpenPath("explorer.exe", Settings.Dir); };
            btns.Children.Add(exp);
            btns.Children.Add(clr);
            btns.Children.Add(fld);
            Grid.SetColumn(btns, 4);
            bar.Children.Add(btns);
            g.Children.Add(bar);

            Card c = new Card(20);
            c.Inner = new Thickness(6, 8, 6, 6);
            Grid lg = new Grid();
            lg.RowDefinitions.Add(Theme.RowDef(0));
            lg.RowDefinitions.Add(Theme.RowDef(-1));
            logList = new StackPanel();
            logScroll = Theme.Scroll(logList);
            lg.Children.Add(logScroll);
            logCount = Theme.T("", 11.5, "Text3", false);
            logCount.Margin = new Thickness(12, 6, 12, 2);
            Grid.SetRow(logCount, 1);
            lg.Children.Add(logCount);
            c.Content = lg;
            Grid.SetRow(c, 2);
            g.Children.Add(c);
            return g;
        }

        bool Matches(LogEntry e)
        {
            if (logFilter != "all" && e.Level != logFilter) return false;
            if (logQuery.Length > 0 && e.Text.IndexOf(logQuery, StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        List<LogEntry> FilteredLog()
        {
            List<LogEntry> r = new List<LogEntry>();
            foreach (LogEntry e in log.Snapshot()) if (Matches(e)) r.Add(e);
            return r;
        }

        const int MaxLogRows = 400;

        void RefreshJournal()
        {
            if (logList == null) return;
            journalDirty = false;
            List<LogEntry> all = log.Snapshot();
            List<LogEntry> m = new List<LogEntry>();
            foreach (LogEntry e in all) if (Matches(e)) m.Add(e);
            logList.Children.Clear();
            for (int i = Math.Max(0, m.Count - MaxLogRows); i < m.Count; i++) logList.Children.Add(LogRow(m[i]));
            logCount.Text = "Показано " + Math.Min(m.Count, MaxLogRows) + " из " + m.Count + (m.Count != all.Count ? " (всего записей: " + all.Count + ")" : "") +
                            " · полный журнал: veylix.log в папке данных";
            logScroll.ScrollToEnd();
        }

        void OnLogAdded(LogEntry e)
        {
            AddRecent(e);
            if (logList == null) return;
            if (page != "journal") { journalDirty = true; return; }
            if (!Matches(e)) return;
            bool atEnd = logScroll.VerticalOffset >= logScroll.ScrollableHeight - 24;
            logList.Children.Add(LogRow(e));
            while (logList.Children.Count > MaxLogRows) logList.Children.RemoveAt(0);
            if (atEnd) logScroll.ScrollToEnd();
        }

        FrameworkElement LogRow(LogEntry e)
        {
            Grid r = new Grid();
            r.Margin = new Thickness(10, 4 * Theme.Density, 10, 4 * Theme.Density);
            r.ColumnDefinitions.Add(Theme.Col(70));
            r.ColumnDefinitions.Add(Theme.Col(96));
            r.ColumnDefinitions.Add(Theme.Col(0));
            TextBlock tm = Theme.T(e.Time.ToString("HH:mm:ss"), 12, "Text3", false);
            tm.FontFamily = Theme.Mono;
            tm.ToolTip = e.Time.ToString("dd.MM.yyyy HH:mm:ss.fff");
            tm.Margin = new Thickness(0, 2, 0, 0);
            r.Children.Add(tm);
            string lv = e.Level == "proc" ? "idle" : e.Level;
            Border chip = Ui.Chip(LogStore.LevelName(e.Level), lv);
            chip.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(chip, 1);
            r.Children.Add(chip);
            TextBlock tx = Theme.Wrap(e.Text, 12.5, e.Level == "proc" ? "Text2" : "Text");
            if (e.Level == "proc") tx.FontFamily = Theme.Mono;
            tx.Margin = new Thickness(0, 1, 0, 0);
            Grid.SetColumn(tx, 2);
            r.Children.Add(tx);
            return r;
        }

        // ====================== настройки ======================

        FrameworkElement BuildSettings()
        {
            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(0, 0, 8, 0);

            // ---- оформление ----
            Card c1 = new Card(22);
            c1.Margin = new Thickness(0, 0, 0, 16);
            c1.Inner = new Thickness(24, 20, 24, 14);
            StackPanel s1 = new StackPanel();
            s1.Children.Add(Ui.Heading("Оформление", "Выбор сохраняется между запусками. Режим «Как в Windows» и расписание переключают светлый и тёмный вариант, не меняя сохранённую тему."));
            themeWrap = new WrapPanel();
            themeWrap.Margin = new Thickness(0, 14, 0, 6);
            s1.Children.Add(themeWrap);
            BuildThemeTiles();
            s1.Children.Add(Ui.Divider());
            string[] modes = { "manual", "system", "schedule" };
            Border seg = Ui.Segmented(new string[] { "Вручную", "Как в Windows", "По расписанию" }, modeSeg, delegate(int i) { PickMode(modes[i]); });
            s1.Children.Add(Ui.SettingRow("Светлая и тёмная тема", null, seg, out modeDesc));
            tgGlass = new VToggle();
            tgGlass.Clicked += delegate { ToggleGlassReduced(); };
            s1.Children.Add(Ui.SettingRow("Упрощённый Liquid Glass", null, tgGlass, out glassDesc));
            tgAnim = new VToggle();
            tgAnim.Clicked += delegate { ToggleAnimations(); };
            TextBlock d2;
            s1.Children.Add(Ui.SettingRow("Анимации интерфейса", "Плавные переходы, подсветка при наведении и пульсация кнопки питания. Если анимации отключены в Windows, они не включаются.", tgAnim, out d2));
            c1.Content = s1;
            sp.Children.Add(c1);
            sp.Children.Add(BuildStudio());

            // ---- запуск и трей ----
            Card c2 = new Card(22);
            c2.Margin = new Thickness(0, 0, 0, 16);
            c2.Inner = new Thickness(24, 20, 24, 14);
            StackPanel s2 = new StackPanel();
            s2.Children.Add(Ui.Heading("Запуск, трей и уведомления", null));
            tgAutorun = new VToggle();
            tgAutorun.Clicked += delegate { OnAutorun(); };
            s2.Children.Add(Ui.SettingRow("Запускать вместе с Windows",
                "Добровольно, по вашему выбору: задание «" + Engine.TaskName + "» в Планировщике. Veylix стартует скрыто в трее и включает последнюю стратегию.", tgAutorun, out autorunDesc));
            tgTray = new VToggle();
            tgTray.Clicked += delegate { ToggleCloseToTray(); };
            TextBlock d3;
            s2.Children.Add(Ui.SettingRow("Закрывать в трей", "Если обход работает, крестик прячет окно в трей вместо выхода. Значок в трее: левая кнопка — открыть окно, правая — панель трея.", tgTray, out d3));
            tgToasts = new VToggle();
            tgToasts.Clicked += delegate { ToggleToasts(); };
            TextBlock d5;
            s2.Children.Add(Ui.SettingRow("Необязательные уведомления",
                "Всплывающие сообщения внутри окна: восстановление соединения, завершение диагностики, новая версия Flowseal. " +
                "Потеря соединения и критические ошибки показываются всегда. Повторы объединяются, звуков нет; история — в разделе «События».", tgToasts, out d5));
            c2.Content = s2;
            sp.Children.Add(c2);
            sp.Children.Add(BuildGameModeCard());

            // ---- о программе ----
            Card c4 = new Card(22);
            c4.Inner = new Thickness(24, 20, 24, 18);
            StackPanel s4 = new StackPanel();
            s4.Children.Add(Ui.Heading("О Veylix " + Program.Version,
                "Veylix — оболочка для zapret-discord-youtube (Flowseal). Обход выполняет winws.exe с драйвером WinDivert; " +
                "Veylix запускает оригинальные стратегии general*.bat без изменений и не трогает системные настройки сети и прокси."));
            WrapPanel ab = new WrapPanel();
            ab.Margin = new Thickness(0, 14, 0, 0);
            VButton dd = Ui.Button("Папка данных", 0xE838, false);
            dd.Margin = new Thickness(0, 0, 8, 0);
            dd.Click += delegate { OpenPath("explorer.exe", Settings.Dir); };
            VButton fd = Ui.Button("Файлы обхода", 0xE838, false);
            fd.Click += delegate { OpenPath("explorer.exe", engine.Root); };
            ab.Children.Add(dd);
            ab.Children.Add(fd);
            s4.Children.Add(ab);
            c4.Content = s4;
            sp.Children.Add(c4);

            return Theme.Scroll(sp);
        }

        FrameworkElement BuildUpdates()
        {
            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(0, 0, 8, 0);
            sp.Children.Add(BuildAppUpdateCard());
            // ---- обновления ----
            Card c3 = new Card(22);
            c3.Margin = new Thickness(0, 0, 0, 16);
            c3.Inner = new Thickness(24, 20, 24, 16);
            StackPanel s3 = new StackPanel();
            s3.Children.Add(Ui.Heading("Обновления Flowseal", "Источник — официальный репозиторий, который указан в service.bat. Ничего не скачивается без вашего подтверждения."));
            Grid info = new Grid();
            info.Margin = new Thickness(0, 14, 0, 4);
            info.ColumnDefinitions.Add(Theme.Col(170));
            info.ColumnDefinitions.Add(Theme.Col(0));
            for (int i = 0; i < 3; i++) info.RowDefinitions.Add(Theme.RowDef(-1));
            updInstalled = InfoLine(info, 0, "Установлено");
            updLatest = InfoLine(info, 1, "Последний релиз");
            TextBlock srcL = Theme.T("Источник", 13, "Text2", false);
            srcL.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(srcL, 2);
            info.Children.Add(srcL);
            VButton src = LinkButton("github.com/" + Updater.Repo);
            src.HorizontalAlignment = HorizontalAlignment.Left;
            src.Margin = new Thickness(-8, 0, 0, 0);
            src.Click += delegate { OpenUrl(Updater.RepoUrl); };
            Grid.SetRow(src, 2);
            Grid.SetColumn(src, 1);
            info.Children.Add(src);
            s3.Children.Add(info);

            updTitle = Theme.T("", 13.5, "Text", true);
            updTitle.Margin = new Thickness(0, 6, 0, 0);
            s3.Children.Add(updTitle);
            updNotesBox = new Border();
            updNotesBox.CornerRadius = new CornerRadius(12);
            updNotesBox.BorderThickness = new Thickness(1);
            updNotesBox.SetResourceReference(Border.BackgroundProperty, "Surface2");
            updNotesBox.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            updNotesBox.Margin = new Thickness(0, 8, 0, 0);
            updNotesBox.MaxHeight = 170;
            updNotes = Theme.Wrap("", 12, "Text2");
            updNotes.Margin = new Thickness(14, 10, 14, 10);
            updNotesBox.Child = Theme.Scroll(updNotes);
            s3.Children.Add(updNotesBox);

            WrapPanel ub = new WrapPanel();
            ub.Margin = new Thickness(0, 14, 0, 0);
            btnCheckUpd = Ui.Button("Проверить обновления", 0xE72C, false);
            btnCheckUpd.Click += async delegate { await CheckUpdates(true); };
            btnInstall = Ui.Button("Установить", 0xE896, true);
            btnInstall.Click += delegate { InstallUpdate(); };
            btnRelease = Ui.Button("Страница релиза", 0xE8A7, false);
            btnRelease.Click += delegate { OpenUrl(latest != null ? latest.HtmlUrl : Updater.ReleasesUrl); };
            btnRollback = Ui.Button("Вернуть прошлую версию", 0xE7A7, false);
            btnRollback.Click += delegate { RollbackUpdate(); };
            foreach (VButton b in new VButton[] { btnCheckUpd, btnInstall, btnRelease, btnRollback })
            {
                b.Margin = new Thickness(0, 0, 8, 8);
                ub.Children.Add(b);
            }
            s3.Children.Add(ub);
            updStatus = Theme.Wrap("", 12, "Text2");
            s3.Children.Add(updStatus);
            s3.Children.Add(Ui.Divider());
            tgCheckUpd = new VToggle();
            tgCheckUpd.Clicked += delegate { ToggleCheckUpdates(); };
            TextBlock d4;
            s3.Children.Add(Ui.SettingRow("Проверять при запуске", "Только сравнение версий и уведомление. Тот же флаг utils\\check_updates.enabled, что использует service.bat.", tgCheckUpd, out d4));
            c3.Content = s3;
            sp.Children.Add(c3);

            return Theme.Scroll(sp);
        }

        TextBlock InfoLine(Grid g, int row, string label)
        {
            TextBlock l = Theme.T(label, 13, "Text2", false);
            l.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(l, row);
            g.Children.Add(l);
            TextBlock v = Theme.T("—", 13, "Text", true);
            v.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 1);
            g.Children.Add(v);
            return v;
        }

        // Плитки тем с живым превью той палитры, которая будет показана при текущем режиме
        void BuildThemeTiles()
        {
            themeWrap.Children.Clear();
            themeTiles.Clear();
            previewMode = cfg.ThemeMode;
            bool sysLight = Theme.SystemPrefersLight();
            foreach (string fam in Theme.Families)
            {
                string f = fam;
                Palette p = Theme.Resolve(f, cfg.ThemeMode, sysLight, DateTime.Now);
                VButton b = new VButton("Surface2", "Stroke", 14);
                b.Width = 150;
                b.Margin = new Thickness(0, 0, 12, 10);
                StackPanel sp = new StackPanel();
                sp.Margin = new Thickness(8);
                sp.Children.Add(Preview(p));
                Grid nm = new Grid();
                nm.Margin = new Thickness(4, 8, 2, 2);
                nm.ColumnDefinitions.Add(Theme.Col(0));
                nm.ColumnDefinitions.Add(Theme.Col(-1));
                nm.Children.Add(Theme.T(Theme.FamilyTitle(f), 12.5, "Text", true));
                TextBlock chk = Theme.Icon(0xE73E, 12, "Accent");
                chk.Visibility = Visibility.Hidden;
                Grid.SetColumn(chk, 1);
                nm.Children.Add(chk);
                sp.Children.Add(nm);
                b.Content = sp;
                b.Tag = chk;
                b.ToolTip = p.Title;
                b.Click += delegate { PickTheme(f); };
                themeTiles[f] = b;
                themeWrap.Children.Add(b);
            }
        }

        static FrameworkElement Preview(Palette p)
        {
            Border bg = new Border();
            bg.Height = 74;
            bg.CornerRadius = new CornerRadius(9);
            bg.Background = (Brush)p.Res["Bg"];
            bg.ClipToBounds = true;
            Grid g = new Grid();
            if (p.Glass && p.Blobs != null)
            {
                Ellipse e1 = new Ellipse();
                e1.Width = 90; e1.Height = 90;
                e1.HorizontalAlignment = HorizontalAlignment.Left;
                e1.VerticalAlignment = VerticalAlignment.Top;
                e1.Margin = new Thickness(-20, -30, 0, 0);
                e1.Fill = new RadialGradientBrush(p.Blobs[0], Colors.Transparent);
                g.Children.Add(e1);
                Ellipse e2 = new Ellipse();
                e2.Width = 80; e2.Height = 80;
                e2.HorizontalAlignment = HorizontalAlignment.Right;
                e2.VerticalAlignment = VerticalAlignment.Bottom;
                e2.Margin = new Thickness(0, 0, -20, -30);
                e2.Fill = new RadialGradientBrush(p.Blobs[1], Colors.Transparent);
                g.Children.Add(e2);
            }
            Border nav = new Border();
            nav.Width = 24;
            nav.HorizontalAlignment = HorizontalAlignment.Left;
            nav.Background = (Brush)p.Res["Nav"];
            g.Children.Add(nav);
            Border card = new Border();
            card.Margin = new Thickness(32, 10, p.Scene != null ? 40 : 10, 10);
            card.CornerRadius = new CornerRadius(6);
            card.Background = (Brush)p.Res["Surface"];
            card.BorderBrush = (Brush)p.Res["Stroke"];
            card.BorderThickness = new Thickness(1);
            Grid cg = new Grid();
            Ellipse pw = new Ellipse();
            pw.Width = 20; pw.Height = 20;
            pw.HorizontalAlignment = HorizontalAlignment.Left;
            pw.Margin = new Thickness(8, 0, 0, 0);
            pw.Fill = (Brush)p.Res["Accent"];
            cg.Children.Add(pw);
            StackPanel lines = new StackPanel();
            lines.VerticalAlignment = VerticalAlignment.Center;
            lines.Margin = new Thickness(36, 0, 8, 0);
            Border l1 = new Border();
            l1.Height = 5; l1.Width = 46; l1.CornerRadius = new CornerRadius(2.5);
            l1.HorizontalAlignment = HorizontalAlignment.Left;
            l1.Background = (Brush)p.Res["Text"];
            l1.Opacity = 0.8;
            Border l2 = new Border();
            l2.Height = 4; l2.Width = 32; l2.CornerRadius = new CornerRadius(2);
            l2.HorizontalAlignment = HorizontalAlignment.Left;
            l2.Margin = new Thickness(0, 5, 0, 0);
            l2.Background = (Brush)p.Res["Text2"];
            l2.Opacity = 0.7;
            lines.Children.Add(l1);
            lines.Children.Add(l2);
            cg.Children.Add(lines);
            card.Child = cg;
            g.Children.Add(card);
            if (p.Scene != null) Scenes.Mini(g, p);
            bg.Child = g;
            return bg;
        }

        void RefreshAppearanceTexts()
        {
            if (themeWrap == null || Theme.Current == null) return;
            if (previewMode != cfg.ThemeMode || cfg.ThemeMode == "system" || cfg.ThemeMode == "schedule") BuildThemeTiles();
            foreach (KeyValuePair<string, VButton> kv in themeTiles)
            {
                bool on = kv.Key == cfg.Theme;
                kv.Value.SetKeys(on ? "AccentSoft" : "Surface2", on ? "Accent" : "Stroke");
                ((TextBlock)kv.Value.Tag).Visibility = on ? Visibility.Visible : Visibility.Hidden;
            }
            int mi = cfg.ThemeMode == "system" ? 1 : cfg.ThemeMode == "schedule" ? 2 : 0;
            Ui.SelectSegment(modeSeg, mi);
            string now = "Сейчас: " + Theme.Current.Title + ". ";
            if (cfg.ThemeMode == "schedule") now += "Светлый вариант с 07:00 до 19:00, тёмный — в остальное время (для Cream/Graphite — Cream днём и Graphite вечером).";
            else if (cfg.ThemeMode == "system") now += "Вариант следует за настройкой «Режим приложений» в Windows (" + (Theme.SystemPrefersLight() ? "сейчас светлый" : "сейчас тёмный") + ").";
            else now += "Cream и Morning Meadow всегда светлые; Graphite, Bee Night, Sunset Hive и Aurora — тёмные; Purple, Mint и Liquid Glass подстраиваются под тему Windows.";
            if (Theme.IsIllustrated(cfg.Theme) && cfg.ThemeMode != "manual") now += " Для иллюстрированных тем светлый вариант — Morning Meadow.";
            modeDesc.Text = now;
            modeDesc.Visibility = Visibility.Visible;

            string reason = Backdrop.BlurUnavailableReason(cfg.GlassReduced);
            string gd;
            if (cfg.Theme != "glass") gd = "Действует для темы Liquid Glass. ";
            else gd = Theme.GlassActive ? "Сейчас: системное размытие Windows (Acrylic) включено. " : "Сейчас: упрощённый режим — " + (reason ?? "размытие не применилось") + ". ";
            gd += "Упрощённый режим отключает размытие подложки и движение фона: полупрозрачные слои остаются, нагрузка на видеокарту меньше.";
            glassDesc.Text = gd;
            glassDesc.Visibility = Visibility.Visible;
            tgGlass.IsOn = cfg.GlassReduced;
            tgAnim.IsOn = cfg.Animations;
            tgAnim.IsEnabled = SystemParameters.ClientAreaAnimation;
        }

        void RefreshSettingsState()
        {
            if (tgAutorun == null) return;
            RefreshGameModeCard();
            tgAutorun.IsOn = autorunOn;
            tgAutorun.IsEnabled = !autorunBusy;
            tgTray.IsOn = cfg.CloseToTray;
            tgToasts.IsOn = cfg.Toasts;
            tgCheckUpd.IsOn = engine.CheckUpdatesEnabled;
        }

        void RefreshUpdateCard(string status)
        {
            if (updInstalled == null) return;
            string inst = engine.InstalledVersion();
            updInstalled.Text = (inst ?? "не определена") + (engine.IsEmbeddedRoot ? "" : " · внешняя папка");
            bool newer = latest != null && Updater.CompareVersions(latest.Version, inst) > 0;
            if (latest == null)
            {
                updLatest.Text = "ещё не проверялся";
                updTitle.Visibility = Visibility.Collapsed;
                updNotesBox.Visibility = Visibility.Collapsed;
            }
            else
            {
                updLatest.Text = latest.Version + (latest.Published != DateTime.MinValue ? " · " + latest.Published.ToString("dd.MM.yyyy") : "") +
                                 (newer ? " · доступно обновление" : " · у вас актуальная версия");
                updLatest.SetResourceReference(TextBlock.ForegroundProperty, newer ? "Accent" : "Text");
                updTitle.Text = "Что нового в " + (latest.Name.Length > 0 ? latest.Name : latest.Version);
                updTitle.Visibility = Visibility.Visible;
                updNotes.Text = latest.Body.Length > 0 ? latest.Body.Replace("\r", "").Trim() : "Описание релиза отсутствует.";
                updNotesBox.Visibility = Visibility.Visible;
            }
            btnCheckUpd.IsEnabled = !updating;
            btnInstall.Visibility = newer ? Visibility.Visible : Visibility.Collapsed;
            if (newer)
            {
                Ui.SetButtonText(btnInstall, latest.CanInstall ? "Установить " + latest.Version : "Скачать на GitHub");
                btnInstall.ToolTip = latest.CanInstall
                    ? "Архив " + latest.ZipName + " будет проверен по SHA-256 из данных релиза GitHub"
                    : "У релиза нет .zip с контрольной суммой — безопасная автоустановка невозможна, откроется страница релиза";
            }
            btnInstall.IsEnabled = !updating && !busy && !tuning;
            btnRollback.Visibility = Updater.LatestBackup(engine.Root) != null ? Visibility.Visible : Visibility.Collapsed;
            btnRollback.IsEnabled = !updating && !busy && !tuning;
            if (status != null) updStatus.Text = status;
        }
    }
}
