using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Veylix
{
    // Плашка выбора стратегии над плиткой «Стратегия» на главной: прокручиваемый список всех стратегий
    public partial class MainWindow
    {
        Grid pickerLayer;
        Card pickerCard;
        StackPanel pickerList;
        ScrollViewer pickerScroll;
        TextBlock pickerHint;
        readonly List<VButton> pickerRows = new List<VButton>();
        int pickerIndex;
        FrameworkElement pickerAnchor;

        FrameworkElement BuildStrategyPicker()
        {
            pickerLayer = new Grid();
            pickerLayer.Visibility = Visibility.Collapsed;
            pickerLayer.Focusable = true;
            pickerLayer.FocusVisualStyle = null;
            // прозрачная подложка: клик мимо плашки закрывает её
            Border scrim = new Border();
            scrim.Background = Brushes.Transparent;
            scrim.MouseLeftButtonDown += delegate { CloseStrategyPicker(); };
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
            head.Children.Add(Theme.T("Выбор стратегии", 14, "Text", true));
            pickerHint = Theme.T("", 11.5, "Text2", false);
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

        void OpenStrategyPicker(FrameworkElement anchor)
        {
            if (pickerLayer == null) return;
            if (busy || tuning || updating)
            {
                Toast("picker-busy", "info", "Стратегию сейчас не сменить", tuning ? "Идёт автоподбор стратегии." : "Дождитесь окончания текущего действия.", null, false);
                return;
            }
            pickerAnchor = anchor;
            FillStrategyPicker();
            PlacePicker();
            pickerLayer.Visibility = Visibility.Visible;
            pickerLayer.Opacity = 0;
            Theme.Animate(pickerLayer, UIElement.OpacityProperty, 1, 140);
            TranslateTransform tt = new TranslateTransform(0, 6);
            pickerCard.RenderTransform = tt;
            Theme.Animate(tt, TranslateTransform.YProperty, 0, 180);
            pickerLayer.Focus();
            Keyboard.Focus(pickerLayer);
            // прокрутить к текущей стратегии, когда список будет размечен
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

        void CloseStrategyPicker()
        {
            if (!PickerOpen) return;
            pickerLayer.BeginAnimation(UIElement.OpacityProperty, null);
            pickerLayer.Visibility = Visibility.Collapsed;
        }

        void FillStrategyPicker()
        {
            pickerList.Children.Clear();
            pickerRows.Clear();
            pickerIndex = Math.Max(0, strategies.IndexOf(selected));
            for (int i = 0; i < strategies.Count; i++)
            {
                VButton r = PickerRow(strategies[i], strategies[i] == selected);
                pickerRows.Add(r);
                pickerList.Children.Add(r);
            }
            pickerHint.Text = strategies.Count + " стратегий · сейчас " + (selected != null ? selected.Display : "—") +
                              (engine.IsRunning ? " · обход перезапустится" : "");
            HighlightPicker();
        }

        VButton PickerRow(Strategy s, bool current)
        {
            VButton b = new VButton(null, null, 10);
            b.Margin = new Thickness(0, 1, 0, 1);
            Grid g = new Grid();
            g.Margin = new Thickness(10, 7 * Theme.Density, 10, 7 * Theme.Density);
            g.ColumnDefinitions.Add(Theme.Col(22));
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.ColumnDefinitions.Add(Theme.Col(-1));

            Ellipse dot = new Ellipse();
            dot.Width = 8; dot.Height = 8;
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.HorizontalAlignment = HorizontalAlignment.Left;
            dot.SetResourceReference(Shape.FillProperty, current ? "Accent" : "Stroke");
            g.Children.Add(dot);

            StackPanel t = new StackPanel();
            t.VerticalAlignment = VerticalAlignment.Center;
            TextBlock name = Theme.T(s.Display, 13.5, current ? "Accent" : "Text", true);
            t.Children.Add(name);
            if (!string.IsNullOrEmpty(s.Note))
            {
                TextBlock note = Theme.T(s.Note, 11.5, s.Warn ? "Warn" : "Text2", false);
                note.TextTrimming = TextTrimming.CharacterEllipsis;
                note.ToolTip = s.Note;
                t.Children.Add(note);
            }
            Grid.SetColumn(t, 1);
            g.Children.Add(t);

            if (s.LastScore >= 0)
            {
                string lv = s.LastScore == s.LastMax ? "ok" : s.LastScore == 0 ? "err" : "warn";
                Border chip = Ui.Chip(s.LastScore + "/" + s.LastMax, lv);
                chip.Margin = new Thickness(8, 0, 0, 0);
                chip.ToolTip = "Проверена в " + s.LastTested.ToString("HH:mm:ss") + ": " + s.LastSummary;
                Grid.SetColumn(chip, 2);
                g.Children.Add(chip);
            }
            else if (current)
            {
                Border chip = Ui.Chip("текущая", "accent");
                chip.Margin = new Thickness(8, 0, 0, 0);
                Grid.SetColumn(chip, 2);
                g.Children.Add(chip);
            }
            b.Content = g;
            b.Click += delegate { PickFromPicker(s); };
            b.MouseEnter += delegate { pickerIndex = pickerRows.IndexOf(b); HighlightPicker(); };
            return b;
        }

        void HighlightPicker()
        {
            for (int i = 0; i < pickerRows.Count; i++)
            {
                bool cur = strategies[i] == selected;
                pickerRows[i].SetKeys(i == pickerIndex ? "AccentSoft" : cur ? "Surface2" : null, null);
            }
        }

        void PickFromPicker(Strategy s)
        {
            CloseStrategyPicker();
            if (s != selected || !engine.IsRunning) OnPickStrategy(s);
        }

        void OnPickerKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { CloseStrategyPicker(); e.Handled = true; return; }
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
                PickFromPicker(strategies[pickerIndex]);
                e.Handled = true;
            }
        }
    }
}
