using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Veylix
{
    // Пункт плашки выбора над плиткой главной
    class PickerItem
    {
        public string Title, Note, ChipText, ChipLevel, ChipTip;
        public bool NoteWarn, Current;
        public string Disabled;   // причина, по которой пункт сейчас недоступен, или null
        public int Glyph;         // иконка вместо точки (для действий вроде «Скачать список»)
        public Action Run;
    }

    // Плашки выбора над плитками «Стратегия», «IPSet» и «Игровой фильтр» на главной:
    // прокручиваемый список вариантов вместо переключения по кругу
    public partial class MainWindow
    {
        Grid pickerLayer;
        Card pickerCard;
        StackPanel pickerList;
        ScrollViewer pickerScroll;
        TextBlock pickerTitle, pickerHint;
        readonly List<VButton> pickerRows = new List<VButton>();
        List<PickerItem> pickerItems = new List<PickerItem>();
        int pickerIndex;
        FrameworkElement pickerAnchor;

        FrameworkElement BuildTilePicker()
        {
            pickerLayer = new Grid();
            pickerLayer.Visibility = Visibility.Collapsed;
            pickerLayer.Focusable = true;
            pickerLayer.FocusVisualStyle = null;
            // прозрачная подложка: клик мимо плашки закрывает её
            Border scrim = new Border();
            scrim.Background = Brushes.Transparent;
            scrim.MouseLeftButtonDown += delegate { ClosePicker(); };
            pickerLayer.Children.Add(scrim);

            pickerCard = new Card(18, "Popup", "StrokeStrong");
            pickerCard.Width = 340;
            pickerCard.HorizontalAlignment = HorizontalAlignment.Left;
            pickerCard.VerticalAlignment = VerticalAlignment.Bottom;
            pickerCard.Inner = new Thickness(0);
            Grid g = new Grid();
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(-1));

            StackPanel head = new StackPanel();
            head.Margin = new Thickness(18, 14, 18, 8);
            pickerTitle = Theme.T("", 14, "Text", true);
            head.Children.Add(pickerTitle);
            pickerHint = Theme.Wrap("", 11.5, "Text2");
            pickerHint.Margin = new Thickness(0, 2, 0, 0);
            head.Children.Add(pickerHint);
            g.Children.Add(head);

            pickerList = new StackPanel();
            pickerList.Margin = new Thickness(8, 0, 8, 0);
            pickerScroll = Theme.Scroll(pickerList);
            pickerScroll.MaxHeight = 300;
            Grid.SetRow(pickerScroll, 1);
            g.Children.Add(pickerScroll);

            Border foot = new Border();
            foot.BorderThickness = new Thickness(0, 1, 0, 0);
            foot.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            foot.Margin = new Thickness(0, 8, 0, 0);
            TextBlock help = Theme.T("↑ ↓ — выбор · Enter — применить · Esc — закрыть", 11.5, "Text3", false);
            help.Margin = new Thickness(18, 9, 18, 10);
            foot.Child = help;
            Grid.SetRow(foot, 2);
            g.Children.Add(foot);
            pickerCard.Content = g;
            pickerLayer.Children.Add(pickerCard);

            pickerLayer.PreviewKeyDown += OnPickerKey;
            return pickerLayer;
        }

        bool PickerOpen { get { return pickerLayer != null && pickerLayer.Visibility == Visibility.Visible; } }

        bool PickerBlocked(string what)
        {
            if (!(busy || tuning || updating)) return false;
            Toast("picker-busy", "info", what + " сейчас не сменить", tuning ? "Идёт автоподбор стратегии." : "Дождитесь окончания текущего действия.", null, false);
            return true;
        }

        void OpenPicker(FrameworkElement anchor, string title, string hint, List<PickerItem> items)
        {
            if (pickerLayer == null) return;
            pickerAnchor = anchor;
            pickerTitle.Text = title;
            pickerHint.Text = hint;
            pickerItems = items;
            pickerList.Children.Clear();
            pickerRows.Clear();
            pickerIndex = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Current) pickerIndex = i;
                VButton r = PickerRow(items[i]);
                pickerRows.Add(r);
                pickerList.Children.Add(r);
            }
            HighlightPicker();
            PlacePicker();
            pickerScroll.ScrollToTop();
            pickerLayer.Visibility = Visibility.Visible;
            pickerLayer.Opacity = 0;
            Theme.Animate(pickerLayer, UIElement.OpacityProperty, 1, 140);
            TranslateTransform tt = new TranslateTransform(0, 6);
            pickerCard.RenderTransform = tt;
            Theme.Animate(tt, TranslateTransform.YProperty, 0, 180);
            pickerLayer.Focus();
            Keyboard.Focus(pickerLayer);
            // прокрутить к текущему варианту, когда список будет размечен
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (pickerIndex >= 0 && pickerIndex < pickerRows.Count) pickerRows[pickerIndex].BringIntoView();
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // Плашка открывается над плиткой, левым краем по плитке
        void PlacePicker()
        {
            double left = 240, bottom = 360;
            try
            {
                if (pickerAnchor == null || pickerAnchor.ActualHeight <= 0) throw new InvalidOperationException("плитка ещё не размечена");
                Point p = pickerAnchor.TranslatePoint(new Point(0, 0), design);
                left = Math.Max(8, Math.Min(p.X, DW - pickerCard.Width - 8));
                bottom = Math.Max(8, DH - p.Y + 8);
            }
            catch { }
            pickerCard.Margin = new Thickness(left, 0, 0, bottom);
        }

        void ClosePicker()
        {
            if (!PickerOpen) return;
            pickerLayer.BeginAnimation(UIElement.OpacityProperty, null);
            pickerLayer.Visibility = Visibility.Collapsed;
        }

        VButton PickerRow(PickerItem it)
        {
            VButton b = new VButton(null, null, 10);
            b.Margin = new Thickness(0, 1, 0, 1);
            Grid g = new Grid();
            g.Margin = new Thickness(10, 7 * Theme.Density, 10, 7 * Theme.Density);
            g.ColumnDefinitions.Add(Theme.Col(22));
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.ColumnDefinitions.Add(Theme.Col(-1));

            if (it.Glyph != 0)
            {
                TextBlock ic = Theme.Icon(it.Glyph, 12, "Text2");
                ic.HorizontalAlignment = HorizontalAlignment.Left;
                ic.VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(ic);
            }
            else
            {
                Ellipse dot = new Ellipse();
                dot.Width = 8; dot.Height = 8;
                dot.VerticalAlignment = VerticalAlignment.Center;
                dot.HorizontalAlignment = HorizontalAlignment.Left;
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
            b.Click += delegate { RunPickerItem(it); };
            b.MouseEnter += delegate { pickerIndex = pickerRows.IndexOf(b); HighlightPicker(); };
            return b;
        }

        void HighlightPicker()
        {
            for (int i = 0; i < pickerRows.Count; i++)
                pickerRows[i].SetKeys(i == pickerIndex ? "AccentSoft" : pickerItems[i].Current ? "Surface2" : null, null);
        }

        void RunPickerItem(PickerItem it)
        {
            if (it.Disabled != null) return;
            ClosePicker();
            if (it.Run != null) it.Run();
        }

        void OnPickerKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { ClosePicker(); e.Handled = true; return; }
            if (pickerRows.Count == 0) return;
            if (e.Key == Key.Down || e.Key == Key.Up)
            {
                pickerIndex = (pickerIndex + (e.Key == Key.Down ? 1 : pickerRows.Count - 1)) % pickerRows.Count;
                HighlightPicker();
                pickerRows[pickerIndex].BringIntoView();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                RunPickerItem(pickerItems[pickerIndex]);
                e.Handled = true;
            }
        }

        // ====================== содержимое плашек ======================

        // Списки вариантов — общие для плашек на главной и для панели трея

        List<PickerItem> StrategyItems(out string title, out string hint)
        {
            List<PickerItem> items = new List<PickerItem>();
            foreach (Strategy st in strategies)
            {
                Strategy s = st;
                PickerItem it = new PickerItem();
                it.Title = s.Display;
                it.Note = s.Note;
                it.NoteWarn = s.Warn;
                it.Current = s == selected;
                if (s.LastScore >= 0)
                {
                    it.ChipText = s.LastScore + "/" + s.LastMax;
                    it.ChipLevel = s.LastScore == s.LastMax ? "ok" : s.LastScore == 0 ? "err" : "warn";
                    it.ChipTip = "Проверена в " + s.LastTested.ToString("HH:mm:ss") + ": " + s.LastSummary;
                }
                it.Run = delegate { if (s != selected || !engine.IsRunning) OnPickStrategy(s); };
                items.Add(it);
            }
            title = "Выбор стратегии";
            hint = strategies.Count + " стратегий · сейчас " + (selected != null ? selected.Display : "—") + (engine.IsRunning ? " · обход перезапустится" : "");
            return items;
        }

        List<PickerItem> IpsetItems(out string title, out string hint)
        {
            string cur = engine.GetIpsetMode();
            bool hasList = engine.HasIpsetList;
            List<PickerItem> items = new List<PickerItem>();
            items.Add(IpsetItem("none", cur, "IPSet не используется, обход — только по спискам доменов", null));
            items.Add(IpsetItem("any", cur, "Обход применяется ко всем IP-адресам", null));
            items.Add(IpsetItem("loaded", cur, "Только адреса из списка ipset-all.txt",
                hasList ? null : "Списка ещё нет — скачайте его пунктом ниже"));
            PickerItem upd = new PickerItem();
            upd.Title = "Обновить список IPSet";
            upd.Note = "Скачать свежий список с GitHub Flowseal и включить «Список»";
            upd.Glyph = 0xE896;
            upd.Run = OnUpdateIpset;
            items.Add(upd);
            title = "IPSet";
            hint = "Сейчас: " + IpsetLabel(cur) + (engine.IsRunning ? " · обход перезапустится" : "");
            return items;
        }

        PickerItem IpsetItem(string mode, string cur, string note, string disabled)
        {
            PickerItem it = new PickerItem();
            it.Title = IpsetLabel(mode);
            it.Note = note;
            it.Current = mode == cur;
            it.Disabled = disabled;
            it.Run = delegate { OnSetIpset(mode); };
            return it;
        }

        List<PickerItem> GameItems(out string title, out string hint)
        {
            string cur = engine.GameMode;
            List<PickerItem> items = new List<PickerItem>();
            items.Add(GameItem("disabled", cur, "Игровые порты не обрабатываются"));
            items.Add(GameItem("all", cur, "TCP и UDP, порты 1024–65535"));
            items.Add(GameItem("tcp", cur, "Только TCP, порты 1024–65535"));
            items.Add(GameItem("udp", cur, "Только UDP, порты 1024–65535"));
            title = "Игровой фильтр";
            hint = "Сейчас: " + GameLabel(cur) + (engine.IsRunning ? " · обход перезапустится" : "");
            return items;
        }

        PickerItem GameItem(string mode, string cur, string note)
        {
            PickerItem it = new PickerItem();
            it.Title = GameLabel(mode);
            it.Note = note;
            it.Current = mode == cur;
            it.Run = delegate { OnSetGame(mode); };
            return it;
        }

        List<PickerItem> PickerItemsFor(string kind, out string title, out string hint)
        {
            if (kind == "ipset") return IpsetItems(out title, out hint);
            if (kind == "game") return GameItems(out title, out hint);
            return StrategyItems(out title, out hint);
        }

        void OpenStrategyPicker(FrameworkElement anchor) { OpenKindPicker(anchor, "strategy", "Стратегию"); }
        void OpenIpsetPicker(FrameworkElement anchor) { OpenKindPicker(anchor, "ipset", "IPSet"); }
        void OpenGamePicker(FrameworkElement anchor) { OpenKindPicker(anchor, "game", "Игровой фильтр"); }

        void OpenKindPicker(FrameworkElement anchor, string kind, string what)
        {
            if (PickerBlocked(what)) return;
            string title, hint;
            List<PickerItem> items = PickerItemsFor(kind, out title, out hint);
            OpenPicker(anchor, title, hint, items);
        }
    }
}
