using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Veylix
{
    // Составные элементы, из которых собраны страницы
    public static class Ui
    {
        public static string FgKey(string level)
        {
            switch (level)
            {
                case "ok": return "Ok";
                case "warn": case "busy": return "Warn";
                case "err": return "Err";
                case "info": return "Info";
                case "accent": return "Accent";
                default: return "Text3";
            }
        }

        public static string SoftKey(string level)
        {
            switch (level)
            {
                case "ok": return "OkSoft";
                case "warn": case "busy": return "WarnSoft";
                case "err": return "ErrSoft";
                case "info": return "InfoSoft";
                case "accent": return "AccentSoft";
                default: return "Surface2";
            }
        }

        public static int Glyph(string level)
        {
            switch (level)
            {
                case "ok": return 0xE73E;     // галочка
                case "warn": return 0xE7BA;   // предупреждение
                case "err": return 0xE711;    // крестик
                case "busy": return 0xE895;   // синхронизация
                case "info": return 0xE946;   // информация
                default: return 0xE9AE;       // точка-ожидание
            }
        }

        // Кнопка с текстом и необязательной иконкой
        public static VButton Button(string text, int glyph, bool primary)
        {
            VButton b = new VButton(primary ? "Accent" : "Surface2", primary ? null : "Stroke", 10);
            b.Height = 36;
            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            sp.HorizontalAlignment = HorizontalAlignment.Center;
            sp.VerticalAlignment = VerticalAlignment.Center;
            sp.Margin = new Thickness(14, 0, 14, 0);
            string fg = primary ? "OnAccent" : "Text";
            if (glyph != 0)
            {
                TextBlock ic = Theme.Icon(glyph, 13, fg);
                ic.Margin = new Thickness(0, 1, 8, 0);
                sp.Children.Add(ic);
            }
            TextBlock t = Theme.T(text, 13, fg, true);
            t.VerticalAlignment = VerticalAlignment.Center;
            sp.Children.Add(t);
            b.Content = sp;
            b.Tag = t;
            return b;
        }

        public static void SetButtonText(VButton b, string text)
        {
            TextBlock t = b.Tag as TextBlock;
            if (t != null) t.Text = text;
        }

        public static VButton IconButton(int glyph, string tip)
        {
            VButton b = new VButton(null, null, 9);
            b.Width = 34;
            b.Height = 34;
            b.Content = Theme.Icon(glyph, 12, "Text2");
            b.ToolTip = tip;
            return b;
        }

        // Круглый значок состояния
        public static Border StatusIcon(double size)
        {
            Border b = new Border();
            b.Width = size;
            b.Height = size;
            b.CornerRadius = new CornerRadius(size * 0.34);
            b.Child = Theme.Icon(0xE9AE, size * 0.42, "Text3");
            SetStatus(b, "idle");
            return b;
        }

        public static void SetStatus(Border b, string level)
        {
            b.SetResourceReference(Border.BackgroundProperty, SoftKey(level));
            TextBlock t = (TextBlock)b.Child;
            t.Text = char.ConvertFromUtf32(Glyph(level));
            t.SetResourceReference(TextBlock.ForegroundProperty, FgKey(level));
        }

        // Плашка с коротким словом состояния
        public static Border Chip(string text, string level)
        {
            Border b = new Border();
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(9, 3, 9, 4);
            b.VerticalAlignment = VerticalAlignment.Center;
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.Child = Theme.T(text, 11.5, FgKey(level), true);
            SetChip(b, text, level);
            return b;
        }

        public static void SetChip(Border b, string text, string level)
        {
            b.SetResourceReference(Border.BackgroundProperty, SoftKey(level));
            TextBlock t = (TextBlock)b.Child;
            t.Text = text;
            t.SetResourceReference(TextBlock.ForegroundProperty, FgKey(level));
        }

        public static StackPanel Heading(string title, string subtitle)
        {
            StackPanel sp = new StackPanel();
            sp.Children.Add(Theme.T(title, 16, "Text", true));
            if (subtitle != null)
            {
                TextBlock s = Theme.Wrap(subtitle, 12, "Text2");
                s.Margin = new Thickness(0, 3, 0, 0);
                sp.Children.Add(s);
            }
            return sp;
        }

        // Строка настройки: заголовок и пояснение слева, элемент управления справа
        public static Grid SettingRow(string title, string description, FrameworkElement control, out TextBlock desc)
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 8, 0, 8);
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.ColumnDefinitions.Add(Theme.Col(-1));
            StackPanel sp = new StackPanel();
            sp.VerticalAlignment = VerticalAlignment.Center;
            sp.Margin = new Thickness(0, 0, 16, 0);
            sp.Children.Add(Theme.T(title, 13.5, "Text", true));
            desc = Theme.Wrap(description ?? "", 12, "Text2");
            desc.Margin = new Thickness(0, 2, 0, 0);
            if (string.IsNullOrEmpty(description)) desc.Visibility = Visibility.Collapsed;
            sp.Children.Add(desc);
            g.Children.Add(sp);
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(control, 1);
            g.Children.Add(control);
            return g;
        }

        public static Border Divider()
        {
            Border d = new Border();
            d.Height = 1;
            d.Margin = new Thickness(0, 4, 0, 4);
            d.SetResourceReference(Border.BackgroundProperty, "Stroke");
            return d;
        }

        // Сегментированный переключатель; возвращает кнопки, чтобы владелец мог подсветить выбранную
        public static Border Segmented(string[] labels, VButton[] outButtons, Action<int> onPick)
        {
            Border wrap = new Border();
            wrap.CornerRadius = new CornerRadius(11);
            wrap.Padding = new Thickness(3);
            wrap.BorderThickness = new Thickness(1);
            wrap.SetResourceReference(Border.BackgroundProperty, "Surface2");
            wrap.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            wrap.HorizontalAlignment = HorizontalAlignment.Left;
            StackPanel sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            for (int i = 0; i < labels.Length; i++)
            {
                int idx = i;
                VButton b = new VButton(null, null, 8);
                b.Height = 30;
                TextBlock t = Theme.T(labels[i], 12.5, "Text2", true);
                t.Margin = new Thickness(12, 0, 12, 0);
                t.VerticalAlignment = VerticalAlignment.Center;
                b.Content = t;
                b.Tag = t;
                b.Click += delegate { onPick(idx); };
                outButtons[i] = b;
                sp.Children.Add(b);
            }
            wrap.Child = sp;
            return wrap;
        }

        public static void SelectSegment(VButton[] buttons, int index)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                bool on = i == index;
                buttons[i].SetKeys(on ? "Surface" : null, on ? "Stroke" : null);
                TextBlock t = buttons[i].Tag as TextBlock;
                if (t != null) t.SetResourceReference(TextBlock.ForegroundProperty, on ? "Text" : "Text2");
            }
        }
    }
}
