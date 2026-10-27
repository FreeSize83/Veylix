using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Shell;
using WF = System.Windows.Forms;

namespace Veylix
{
    // Панель трея в стиле быстрых настроек Windows 11: плитки действий, состояние обхода и сервисов.
    // Открывается кликом по значку в трее (левым или правым) и закрывается, когда теряет фокус.
    public partial class MainWindow
    {
        Window flyout;
        DateTime flyoutClosedAt;
        string flyoutView = "main";      // main — плитки; strategy | ipset | game — список выбора внутри панели
        bool flyoutTopAnchored;          // панель задач сверху: панель растёт вниз от верхнего края
        double flyoutBottom;             // нижний край панели, чтобы при смене высоты она оставалась у трея

        const double FlyoutW = 372;

        void ToggleFlyout()
        {
            // клик по значку при открытой панели: она уже закрылась по потере фокуса — не открываем снова
            if (flyout != null) { CloseFlyout(); return; }
            if ((DateTime.Now - flyoutClosedAt).TotalMilliseconds < 300) return;
            ShowFlyout();
        }

        void ShowFlyout()
        {
            Window w = new Window();
            w.Title = "Veylix";
            w.WindowStyle = WindowStyle.None;
            w.ResizeMode = ResizeMode.NoResize;
            w.ShowInTaskbar = false;
            w.Topmost = true;
            w.SizeToContent = SizeToContent.Height;
            w.Width = FlyoutW;
            w.FontFamily = Theme.Ui;
            w.UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);

            bool blur = Backdrop.BlurUnavailableReason(cfg.GlassReduced) == null;
            WindowChrome wc = new WindowChrome();
            wc.CaptionHeight = 0;
            wc.ResizeBorderThickness = new Thickness(0);
            wc.GlassFrameThickness = blur ? new Thickness(-1) : new Thickness(0, 0, 0, 1);
            wc.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(w, wc);
            w.SetResourceReference(Window.BackgroundProperty, "Popup");

            w.SourceInitialized += delegate
            {
                bool dark = Theme.Current != null && Theme.Current.Dark;
                if (Backdrop.Apply(w, blur, dark))
                {
                    // поверх системного размытия — полупрозрачный цвет панели из темы
                    SolidColorBrush pb = Theme.Res("Popup") as SolidColorBrush;
                    Color c = pb != null ? pb.Color : (dark ? Color.FromRgb(32, 32, 36) : Color.FromRgb(248, 248, 250));
                    w.Background = new SolidColorBrush(Color.FromArgb(dark ? (byte)0xB8 : (byte)0xC8, c.R, c.G, c.B));
                }
            };
            w.Deactivated += delegate { CloseFlyout(); };
            w.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                if (flyoutView != "main") ShowFlyoutView("main");   // из списка — назад к плиткам
                else CloseFlyout();
            };

            Border frame = new Border();
            frame.BorderThickness = new Thickness(1);
            frame.SetResourceReference(Border.BorderBrushProperty, "StrokeStrong");
            flyoutView = "main";
            frame.Child = BuildFlyoutContent();
            w.Content = frame;

            flyout = w;
            w.Opacity = 0;
            w.Show();
            PlaceFlyout(w);
            w.Activate();

            // появление как у панелей Windows: лёгкий сдвиг снизу и проявление
            if (Theme.Animations)
            {
                TranslateTransform tt = new TranslateTransform(0, 14);
                frame.RenderTransform = tt;
                DoubleAnimation slide = new DoubleAnimation(14, 0, new Duration(TimeSpan.FromMilliseconds(220)));
                slide.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                tt.BeginAnimation(TranslateTransform.YProperty, slide);
                w.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(160))));
            }
            else w.Opacity = 1;
        }

        void CloseFlyout()
        {
            Window w = flyout;
            if (w == null) return;
            flyout = null;
            flyoutClosedAt = DateTime.Now;
            try { w.Close(); } catch { }
        }

        // Угол экрана у значка трея: панель задач может быть снизу, сверху или сбоку
        void PlaceFlyout(Window w)
        {
            w.UpdateLayout();
            double h = w.ActualHeight, scale = 1;
            try
            {
                PresentationSource src = PresentationSource.FromVisual(w);
                if (src != null && src.CompositionTarget != null) scale = src.CompositionTarget.TransformToDevice.M11;
            }
            catch { }
            WF.Screen scr = WF.Screen.FromPoint(WF.Cursor.Position);
            System.Drawing.Rectangle wa = scr.WorkingArea, b = scr.Bounds;
            double l = wa.Left / scale, t = wa.Top / scale, r = wa.Right / scale, bt = wa.Bottom / scale;
            double cx = WF.Cursor.Position.X / scale;
            const double gap = 12;

            double left = Math.Min(Math.Max(l + gap, cx - FlyoutW / 2), r - FlyoutW - gap);
            double top = bt - h - gap;                                   // панель задач снизу (обычный случай)
            if (wa.Top > b.Top) top = t + gap;                           // сверху
            if (wa.Left > b.Left) { left = l + gap; top = bt - h - gap; }   // слева
            if (wa.Right < b.Right) { left = r - FlyoutW - gap; top = bt - h - gap; }   // справа
            if (wa.Bottom == b.Bottom && wa.Top == b.Top && wa.Left == b.Left && wa.Right == b.Right) left = r - FlyoutW - gap;   // скрытая панель задач
            w.Left = left;
            w.Top = Math.Max(t + gap, top);
            flyoutTopAnchored = wa.Top > b.Top;
            flyoutBottom = w.Top + h;
        }

        void RefreshFlyout()
        {
            if (flyout == null) return;
            Border frame = flyout.Content as Border;
            if (frame == null) return;
            frame.Child = BuildFlyoutContent();
            // высота меняется (плитки ↔ список): держим нижний край у панели задач
            flyout.UpdateLayout();
            if (!flyoutTopAnchored) flyout.Top = flyoutBottom - flyout.ActualHeight;
        }

        void ShowFlyoutView(string view)
        {
            flyoutView = view;
            RefreshFlyout();
        }

        // ---------- содержимое ----------

        FrameworkElement BuildFlyoutContent()
        {
            if (flyoutView != "main") return BuildFlyoutList(flyoutView);
            bool run = engine.IsRunning;
            bool locked = busy || tuning || updating;
            StackPanel root = new StackPanel();
            root.Margin = new Thickness(18, 18, 18, 0);

            // шапка: логотип, состояние, время работы
            Grid head = new Grid();
            head.ColumnDefinitions.Add(Theme.Col(-1));
            head.ColumnDefinitions.Add(Theme.Col(0));
            head.ColumnDefinitions.Add(Theme.Col(-1));
            head.Children.Add(Theme.Logo(36));
            StackPanel ht = new StackPanel();
            ht.Margin = new Thickness(12, 0, 8, 0);
            ht.VerticalAlignment = VerticalAlignment.Center;
            ht.Children.Add(Theme.T("Veylix", 15, "Text", true));
            string sub;
            if (tuning) sub = "Идёт автоподбор стратегии…";
            else if (busy) sub = "Подождите…";
            else if (run)
            {
                TimeSpan u = DateTime.Now - engine.StartedAt;
                sub = (engine.Current != null ? engine.Current.Display : "") + " · " + string.Format("{0:00}:{1:00}:{2:00}", (int)u.TotalHours, u.Minutes, u.Seconds);
            }
            else if (gamePausedFor != null) sub = "Пауза для игры: " + GameWatch.Pretty(gamePausedFor);
            else sub = "Обход выключен";
            TextBlock st = Theme.T(sub, 12, "Text2", false);
            st.TextTrimming = TextTrimming.CharacterEllipsis;
            ht.Children.Add(st);
            Grid.SetColumn(ht, 1);
            head.Children.Add(ht);
            bool paused = !run && gamePausedFor != null;
            Border chip = Ui.Chip(tuning ? "Подбор" : busy ? "…" : run ? "Работает" : paused ? "Пауза" : "Выключен", tuning || busy ? "busy" : run ? "ok" : paused ? "warn" : "idle");
            Grid.SetColumn(chip, 2);
            head.Children.Add(chip);
            root.Children.Add(head);

            // плитки
            UniformGrid tiles = new UniformGrid();
            tiles.Columns = 3;
            tiles.Margin = new Thickness(-4, 14, -4, 0);
            FlyoutTile(tiles, 0xE7E8, run ? "Обход включён" : "Обход", run ? "работает" : "выключен", run, false,
                tuning || updating ? "сейчас недоступно" : null, delegate { CloseFlyout(); OnPower(); });
            FlyoutTile(tiles, 0xE8FD, "Стратегия", selected != null ? selected.Display : "—", false, true,
                locked ? "сейчас недоступно" : null, delegate { ShowFlyoutView("strategy"); });
            FlyoutTile(tiles, 0xE774, "IPSet", IpsetLabel(engine.GetIpsetMode()), false, true,
                locked ? "сейчас недоступно" : null, delegate { ShowFlyoutView("ipset"); });
            FlyoutTile(tiles, 0xE7FC, "Игры", GameLabel(engine.GameMode), engine.GameMode != "disabled", true,
                locked ? "сейчас недоступно" : null, delegate { ShowFlyoutView("game"); });
            FlyoutTile(tiles, 0xE72C, "Проверка", checking ? "идёт проверка…" : "Discord и YouTube", false, false,
                checking ? "идёт проверка" : null, delegate { RunAccessTest(); RefreshFlyout(); });
            FlyoutTile(tiles, 0xE9D9, "Диагностика", "отчёт о системе", false, false, null,
                delegate { CloseFlyout(); commandById["diag.run"].Run(); });
            root.Children.Add(tiles);

            // последняя проверка сервисов
            if (lastChecks != null && lastChecks.Length > 0)
            {
                WrapPanel svc = new WrapPanel();
                svc.Margin = new Thickness(0, 12, 0, 0);
                foreach (ServiceResult r in lastChecks)
                {
                    string lv = r.Status == ServiceStatus.Ok ? "ok" : r.Status == ServiceStatus.Partial ? "warn" :
                                r.Status == ServiceStatus.Unknown ? "idle" : "err";
                    Border c = Ui.Chip(r.Name + " · " + NetCheck.StatusWord(r.Status).ToLowerInvariant(), lv);
                    c.Margin = new Thickness(0, 0, 6, 6);
                    c.ToolTip = r.Summary;
                    svc.Children.Add(c);
                }
                root.Children.Add(svc);
            }

            // нижняя полоса, как у панели Windows
            Border foot = new Border();
            foot.Margin = new Thickness(-18, 10, -18, 0);
            foot.Padding = new Thickness(12, 8, 10, 8);
            foot.BorderThickness = new Thickness(0, 1, 0, 0);
            foot.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            foot.SetResourceReference(Border.BackgroundProperty, "Surface2");
            Grid fg = new Grid();
            fg.ColumnDefinitions.Add(Theme.Col(0));
            fg.ColumnDefinitions.Add(Theme.Col(-1));
            fg.ColumnDefinitions.Add(Theme.Col(-1));
            fg.ColumnDefinitions.Add(Theme.Col(-1));
            VButton open = new VButton(null, null, 9);
            open.Height = 32;
            open.HorizontalAlignment = HorizontalAlignment.Left;
            StackPanel op = Theme.Row();
            op.Margin = new Thickness(8, 0, 10, 0);
            op.VerticalAlignment = VerticalAlignment.Center;
            TextBlock oi = Theme.Icon(0xE8A7, 12, "Accent");
            oi.Margin = new Thickness(0, 1, 8, 0);
            op.Children.Add(oi);
            op.Children.Add(Theme.T("Открыть Veylix", 12.5, "Accent", true));
            open.Content = op;
            open.Click += delegate { CloseFlyout(); ShowFromTray(); };
            fg.Children.Add(open);
            VButton wgt = Ui.IconButton(0xE737, widget != null ? "Скрыть мини-виджет" : "Показать мини-виджет поверх окон");
            wgt.Click += delegate { CloseFlyout(); ToggleWidget(); };
            Grid.SetColumn(wgt, 1);
            fg.Children.Add(wgt);
            VButton set = Ui.IconButton(0xE713, "Настройки");
            set.Click += delegate { CloseFlyout(); ShowFromTray(); ShowPage("settings"); };
            Grid.SetColumn(set, 2);
            fg.Children.Add(set);
            VButton exit = Ui.IconButton(0xE7E8, "Выйти из Veylix (обход остановится)");
            exit.Click += delegate { CloseFlyout(); ExitApp(); };
            Grid.SetColumn(exit, 3);
            fg.Children.Add(exit);
            foot.Child = fg;
            root.Children.Add(foot);
            return root;
        }

        // Плитка как в быстрых настройках Windows: кнопка с иконкой (и стрелкой, если есть выбор) и подписи под ней
        void FlyoutTile(UniformGrid host, int glyph, string title, string value, bool on, bool chevron, string disabled, Action click)
        {
            StackPanel cell = new StackPanel();
            cell.Margin = new Thickness(4, 0, 4, 10);
            VButton b = new VButton(on ? "Accent" : "Surface2", on ? null : "Stroke", 10);
            b.Height = 50;
            Grid g = new Grid();
            string fg = on ? "OnAccent" : "Text";
            TextBlock ic = Theme.Icon(glyph, 16, fg);
            ic.HorizontalAlignment = chevron ? HorizontalAlignment.Left : HorizontalAlignment.Center;
            ic.VerticalAlignment = VerticalAlignment.Center;
            ic.Margin = new Thickness(chevron ? 22 : 0, 0, 0, 0);
            g.Children.Add(ic);
            if (chevron)
            {
                Border sep = new Border();
                sep.Width = 1;
                sep.Margin = new Thickness(0, 12, 32, 12);
                sep.HorizontalAlignment = HorizontalAlignment.Right;
                sep.SetResourceReference(Border.BackgroundProperty, on ? "OnAccent" : "Stroke");
                sep.Opacity = 0.5;
                g.Children.Add(sep);
                TextBlock ch = Theme.Icon(0xE76C, 10, fg);
                ch.HorizontalAlignment = HorizontalAlignment.Right;
                ch.VerticalAlignment = VerticalAlignment.Center;
                ch.Margin = new Thickness(0, 0, 12, 0);
                g.Children.Add(ch);
            }
            b.Content = g;
            b.ToolTip = disabled != null ? title + ": " + disabled : value;
            if (disabled != null) b.IsEnabled = false;
            b.Click += delegate { click(); };
            cell.Children.Add(b);
            TextBlock t = Theme.T(title, 12, "Text", true);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, 6, 0, 0);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            cell.Children.Add(t);
            TextBlock v = Theme.T(value, 11, "Text2", false);
            v.HorizontalAlignment = HorizontalAlignment.Center;
            v.TextTrimming = TextTrimming.CharacterEllipsis;
            cell.Children.Add(v);
            host.Children.Add(cell);
        }

        // Список выбора прямо в панели трея (как список сетей Wi-Fi в быстрых настройках Windows)
        FrameworkElement BuildFlyoutList(string kind)
        {
            string title, hint;
            List<PickerItem> items = PickerItemsFor(kind, out title, out hint);
            StackPanel root = new StackPanel();
            root.Margin = new Thickness(10, 12, 10, 10);

            Grid head = new Grid();
            head.Margin = new Thickness(0, 0, 8, 8);
            head.ColumnDefinitions.Add(Theme.Col(-1));
            head.ColumnDefinitions.Add(Theme.Col(0));
            VButton back = Ui.IconButton(0xE72B, "Назад (Esc)");
            back.VerticalAlignment = VerticalAlignment.Top;
            back.Click += delegate { ShowFlyoutView("main"); };
            head.Children.Add(back);
            StackPanel ht = new StackPanel();
            ht.Margin = new Thickness(8, 4, 0, 0);
            ht.Children.Add(Theme.T(title, 15, "Text", true));
            TextBlock hb = Theme.Wrap(hint, 11.5, "Text2");
            hb.Margin = new Thickness(0, 2, 0, 0);
            ht.Children.Add(hb);
            Grid.SetColumn(ht, 1);
            head.Children.Add(ht);
            root.Children.Add(head);

            StackPanel list = new StackPanel();
            VButton currentRow = null;
            foreach (PickerItem it in items)
            {
                VButton r = FlyoutRow(it);
                if (it.Current) currentRow = r;
                list.Children.Add(r);
            }
            ScrollViewer sv = Theme.Scroll(list);
            sv.MaxHeight = 330;
            root.Children.Add(sv);
            if (currentRow != null)
            {
                VButton cr = currentRow;
                Dispatcher.BeginInvoke(new Action(delegate { cr.BringIntoView(); }), System.Windows.Threading.DispatcherPriority.Loaded);
            }
            return root;
        }

        VButton FlyoutRow(PickerItem it)
        {
            VButton b = new VButton(it.Current ? "AccentSoft" : null, null, 10);
            b.Margin = new Thickness(0, 1, 0, 1);
            Grid g = new Grid();
            g.Margin = new Thickness(12, 8, 10, 8);
            g.ColumnDefinitions.Add(Theme.Col(22));
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.ColumnDefinitions.Add(Theme.Col(-1));
            if (it.Glyph != 0)
            {
                TextBlock ic = Theme.Icon(it.Glyph, 12, "Text2");
                ic.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(ic);
            }
            else
            {
                Ellipse dot = new Ellipse();
                dot.Width = 8; dot.Height = 8;
                dot.HorizontalAlignment = HorizontalAlignment.Left;
                dot.VerticalAlignment = VerticalAlignment.Center;
                dot.SetResourceReference(Shape.FillProperty, it.Current ? "Accent" : "Stroke");
                g.Children.Add(dot);
            }
            StackPanel t = new StackPanel();
            t.VerticalAlignment = VerticalAlignment.Center;
            t.Children.Add(Theme.T(it.Title, 13.5, it.Current ? "Accent" : "Text", true));
            string note = it.Disabled ?? it.Note;
            if (!string.IsNullOrEmpty(note))
            {
                TextBlock n = Theme.T(note, 11.5, it.NoteWarn || it.Disabled != null ? "Warn" : "Text2", false);
                n.TextTrimming = TextTrimming.CharacterEllipsis;
                n.ToolTip = note;
                t.Children.Add(n);
            }
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            string chipText = it.ChipText ?? (it.Current ? "сейчас" : null);
            if (chipText != null)
            {
                Border chip = Ui.Chip(chipText, it.ChipText != null ? it.ChipLevel : "accent");
                chip.Margin = new Thickness(8, 0, 0, 0);
                if (it.ChipTip != null) chip.ToolTip = it.ChipTip;
                Grid.SetColumn(chip, 2);
                g.Children.Add(chip);
            }
            b.Content = g;
            if (it.Disabled != null) b.Opacity = 0.55;
            b.Click += delegate
            {
                if (it.Disabled != null) return;
                // панель остаётся открытой и возвращается к плиткам с новым значением
                flyoutView = "main";
                if (it.Run != null) it.Run();
                RefreshFlyout();
            };
            return b;
        }
    }
}
