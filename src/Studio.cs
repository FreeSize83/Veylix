using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace Veylix
{
    public partial class MainWindow
    {
        VButton[] glowSeg = new VButton[4], cornerSeg = new VButton[3], densitySeg = new VButton[3], speedSeg = new VButton[3];
        readonly Dictionary<string, VButton> swatches = new Dictionary<string, VButton>();
        TextBox accentBox;
        TextBlock accentNote, studioStatus;
        VToggle tgBee, tgBeeAnim;
        Bee previewBee;
        Ellipse previewRing;
        StackPanel previewList;
        VToggle previewToggle;

        Card studioCard;

        FrameworkElement BuildStudio()
        {
            Card c = new Card(22);
            c.Margin = new Thickness(0, 0, 0, 16);
            c.Inner = new Thickness(24, 20, 24, 18);
            StackPanel sp = new StackPanel();
            sp.Children.Add(Ui.Heading("Theme Studio",
                "Тонкая настройка поверх выбранной темы — работает со всеми темами, включая Liquid Glass. Изменения видны сразу и сохраняются между запусками."));

            Grid cols = new Grid();
            cols.Margin = new Thickness(0, 14, 0, 0);
            cols.ColumnDefinitions.Add(Theme.Col(0));
            cols.ColumnDefinitions.Add(Theme.Col(24));
            cols.ColumnDefinitions.Add(Theme.Col(280));
            StackPanel left = new StackPanel();

            // ---- акцент ----
            left.Children.Add(Label("Акцентный цвет"));
            WrapPanel sw = new WrapPanel();
            VButton themeAcc = new VButton("Surface2", "Stroke", 9);
            themeAcc.Height = 30;
            themeAcc.Margin = new Thickness(0, 0, 6, 6);
            TextBlock ta = Theme.T("Как в теме", 12, "Text", true);
            ta.Margin = new Thickness(10, 0, 10, 0);
            ta.VerticalAlignment = VerticalAlignment.Center;
            themeAcc.Content = ta;
            themeAcc.Click += delegate { SetAccent(""); };
            swatches[""] = themeAcc;
            sw.Children.Add(themeAcc);
            foreach (string hex in ThemeOptions.AccentPresets)
            {
                string h = hex;
                VButton b = new VButton(null, "Stroke", 15);
                b.Width = 30; b.Height = 30;
                b.Margin = new Thickness(0, 0, 6, 6);
                Ellipse dot = new Ellipse();
                dot.Width = 20; dot.Height = 20;
                dot.Fill = Theme.B(h);
                b.Content = dot;
                b.ToolTip = h;
                b.Click += delegate { SetAccent(h); };
                swatches[h] = b;
                sw.Children.Add(b);
            }
            left.Children.Add(sw);
            StackPanel custom = Theme.Row();
            accentBox = new TextBox();
            accentBox.Width = 110;
            accentBox.Height = 30;
            accentBox.Padding = new Thickness(8, 0, 8, 0);
            accentBox.MaxLength = 7;
            accentBox.ToolTip = "Свой цвет в формате #RRGGBB";
            custom.Children.Add(accentBox);
            VButton apply = Ui.Button("Применить", 0, false);
            apply.Height = 30;
            apply.Margin = new Thickness(8, 0, 0, 0);
            apply.Click += delegate
            {
                string v = accentBox.Text.Trim();
                if (!v.StartsWith("#")) v = "#" + v;
                if (!ThemeOptions.ValidAccent(v)) { accentNote.Text = "Нужен цвет в виде #RRGGBB, например #3A7BFF."; return; }
                SetAccent(v.ToUpperInvariant());
            };
            custom.Children.Add(apply);
            left.Children.Add(custom);
            accentNote = Theme.Wrap("", 11.5, "Text3");
            accentNote.Margin = new Thickness(0, 4, 0, 0);
            left.Children.Add(accentNote);

            // ---- сегменты ----
            left.Children.Add(Label("Свечение и блики"));
            left.Children.Add(Ui.Segmented(new string[] { "Нет", "Мягко", "Обычно", "Ярко" }, glowSeg, delegate(int i) { cfg.Studio.Glow = ThemeOptions.Glows[i]; ApplyStudio(); }));
            left.Children.Add(Label("Скругление карточек"));
            left.Children.Add(Ui.Segmented(new string[] { "Строгое", "Стандартное", "Мягкое" }, cornerSeg, delegate(int i) { cfg.Studio.Corners = ThemeOptions.CornerNames[i]; ApplyStudio(); }));
            left.Children.Add(Label("Плотность списков (журнал, события, стратегии, диагностика)"));
            left.Children.Add(Ui.Segmented(new string[] { "Плотно", "Обычно", "Свободно" }, densitySeg, delegate(int i) { cfg.Studio.Density = ThemeOptions.DensityNames[i]; ApplyStudio(); }));
            left.Children.Add(Label("Скорость анимаций"));
            left.Children.Add(Ui.Segmented(new string[] { "Медленно", "Обычно", "Быстро" }, speedSeg, delegate(int i) { cfg.Studio.Speed = ThemeOptions.SpeedNames[i]; ApplyStudio(); }));

            tgBee = new VToggle();
            tgBee.Clicked += delegate { cfg.CompanionVisible = !cfg.CompanionVisible; ApplyStudio(); };
            TextBlock d1;
            Grid r1 = Ui.SettingRow("Пчёлка-компаньон", "Показывает состояние обхода на главной; щелчок по ней открывает помощника.", tgBee, out d1);
            r1.Margin = new Thickness(0, 14, 0, 4);
            left.Children.Add(r1);
            tgBeeAnim = new VToggle();
            tgBeeAnim.Clicked += delegate { cfg.CompanionAnimated = !cfg.CompanionAnimated; ApplyStudio(); };
            TextBlock d2;
            left.Children.Add(Ui.SettingRow("Анимация пчёлки", "Парение и взмахи крыльев. Общий выключатель анимаций выше тоже действует.", tgBeeAnim, out d2));

            WrapPanel act = new WrapPanel();
            act.Margin = new Thickness(0, 12, 0, 0);
            VButton exp = Ui.Button("Экспорт…", 0xE74E, false);
            exp.ToolTip = "Сохранить тему в файл .veylix-theme.json (только параметры оформления)";
            exp.Click += delegate { ExportStudio(); };
            VButton imp = Ui.Button("Импорт…", 0xE8E5, false);
            imp.ToolTip = "Загрузить тему из файла: принимаются только известные параметры с допустимыми значениями";
            imp.Click += delegate { ImportStudio(); };
            VButton rst = Ui.Button("Сбросить", 0xE777, false);
            rst.ToolTip = "Вернуть значения по умолчанию (выбранная тема не меняется)";
            rst.Click += delegate
            {
                cfg.Studio = new ThemeOptions();
                cfg.CompanionVisible = true;
                cfg.CompanionAnimated = true;
                accentBox.Text = "";
                ApplyStudio();
                studioStatus.Text = "Настройки Theme Studio сброшены.";
            };
            foreach (VButton b in new VButton[] { exp, imp, rst }) { b.Margin = new Thickness(0, 0, 8, 6); act.Children.Add(b); }
            left.Children.Add(act);
            studioStatus = Theme.Wrap("", 12, "Text2");
            left.Children.Add(studioStatus);
            cols.Children.Add(left);

            FrameworkElement preview = BuildStudioPreview();
            Grid.SetColumn(preview, 2);
            cols.Children.Add(preview);
            sp.Children.Add(cols);
            c.Content = sp;
            studioCard = c;
            RefreshStudio();
            return c;
        }

        static TextBlock Label(string t)
        {
            TextBlock l = Theme.T(t, 11.5, "Text3", true);
            l.Margin = new Thickness(0, 12, 0, 5);
            return l;
        }

        // Живой предпросмотр: настоящие элементы Veylix на текущих ресурсах темы
        FrameworkElement BuildStudioPreview()
        {
            Card c = new Card(18, "Surface2", "Stroke");
            c.Inner = new Thickness(16, 14, 16, 14);
            c.VerticalAlignment = VerticalAlignment.Top;
            StackPanel sp = new StackPanel();
            Grid top = new Grid();
            top.ColumnDefinitions.Add(Theme.Col(0));
            top.ColumnDefinitions.Add(Theme.Col(-1));
            StackPanel tt = new StackPanel();
            tt.Children.Add(Theme.T("Предпросмотр", 13.5, "Text", true));
            tt.Children.Add(Theme.T("обновляется сразу", 11, "Text3", false));
            top.Children.Add(tt);
            previewBee = new Bee(52);
            previewBee.SetMood("happy");
            Grid.SetColumn(previewBee, 1);
            top.Children.Add(previewBee);
            sp.Children.Add(top);

            Grid mid = new Grid();
            mid.Margin = new Thickness(0, 10, 0, 0);
            mid.ColumnDefinitions.Add(Theme.Col(78));
            mid.ColumnDefinitions.Add(Theme.Col(0));
            Grid ring = new Grid();
            ring.Width = 66; ring.Height = 66;
            ring.HorizontalAlignment = HorizontalAlignment.Left;
            previewRing = new Ellipse();
            previewRing.StrokeThickness = 3;
            previewRing.SetResourceReference(Shape.StrokeProperty, "Ok");
            ring.Children.Add(previewRing);
            VButton pw = new VButton("Accent", null, 45);
            pw.Width = 50; pw.Height = 50;
            pw.Content = Theme.Icon(0xE7E8, 18, "OnAccent");
            ring.Children.Add(pw);
            mid.Children.Add(ring);
            StackPanel btns = new StackPanel();
            btns.VerticalAlignment = VerticalAlignment.Center;
            VButton p1 = Ui.Button("Основная", 0, true);
            p1.Height = 30;
            p1.Margin = new Thickness(0, 0, 0, 6);
            VButton p2 = Ui.Button("Обычная", 0, false);
            p2.Height = 30;
            btns.Children.Add(p1);
            btns.Children.Add(p2);
            Grid.SetColumn(btns, 1);
            mid.Children.Add(btns);
            sp.Children.Add(mid);

            StackPanel chips = Theme.Row();
            chips.Margin = new Thickness(0, 10, 0, 0);
            chips.Children.Add(Ui.Chip("Доступен", "ok"));
            Border w = Ui.Chip("Частично", "warn");
            w.Margin = new Thickness(6, 0, 6, 0);
            chips.Children.Add(w);
            previewToggle = new VToggle();
            previewToggle.IsOn = true;
            previewToggle.Clicked += delegate { previewToggle.IsOn = !previewToggle.IsOn; };
            chips.Children.Add(previewToggle);
            sp.Children.Add(chips);

            Card list = new Card(12);
            list.Margin = new Thickness(0, 10, 0, 0);
            list.Inner = new Thickness(8, 4, 8, 4);
            previewList = new StackPanel();
            list.Content = previewList;
            sp.Children.Add(list);
            c.Content = sp;
            return c;
        }

        void RefreshStudio()
        {
            if (glowSeg[0] == null || accentNote == null) return;
            ThemeOptions o = cfg.Studio;
            Ui.SelectSegment(glowSeg, Array.IndexOf(ThemeOptions.Glows, o.Glow));
            Ui.SelectSegment(cornerSeg, Array.IndexOf(ThemeOptions.CornerNames, o.Corners));
            Ui.SelectSegment(densitySeg, Array.IndexOf(ThemeOptions.DensityNames, o.Density));
            Ui.SelectSegment(speedSeg, Array.IndexOf(ThemeOptions.SpeedNames, o.Speed));
            foreach (KeyValuePair<string, VButton> kv in swatches)
            {
                bool on = kv.Key == o.Accent;
                if (kv.Key.Length == 0) kv.Value.SetKeys(on ? "AccentSoft" : "Surface2", on ? "Accent" : "Stroke");
                else kv.Value.SetKeys(null, on ? "Text" : "Stroke");
                kv.Value.BorderThickness = new Thickness(on ? 2 : 1);
            }
            accentNote.Text = o.Accent.Length == 0 ? "Используется акцент выбранной темы."
                : "Акцент " + o.Accent + (Theme.AccentAdjusted ? " — немного скорректирован, чтобы текст и ссылки оставались читаемыми на этой теме." : ".");
            tgBee.IsOn = cfg.CompanionVisible;
            tgBeeAnim.IsOn = cfg.CompanionAnimated;
            tgBeeAnim.IsEnabled = cfg.CompanionVisible;

            // предпросмотр
            previewBee.Visibility = cfg.CompanionVisible ? Visibility.Visible : Visibility.Collapsed;
            previewBee.Animated = cfg.CompanionVisible && cfg.CompanionAnimated;
            previewBee.Restyle();
            SolidColorBrush ok = Theme.Res("Ok") as SolidColorBrush;
            if (Theme.GlowFactor > 0.01)
            {
                DropShadowEffect fx = new DropShadowEffect();
                fx.Color = ok != null ? ok.Color : Colors.LimeGreen;
                fx.ShadowDepth = 0;
                fx.BlurRadius = 18 * Theme.GlowFactor;
                fx.Opacity = Math.Min(1, 0.6 * Theme.GlowFactor);
                previewRing.Effect = fx;
            }
            else previewRing.Effect = null;
            previewList.Children.Clear();
            string[][] rows = { new string[] { "ok", "Обход работает: ALT3" }, new string[] { "info", "Проверка сервисов завершена" }, new string[] { "warn", "Discord — частично" } };
            foreach (string[] r in rows)
            {
                Grid g = new Grid();
                g.Margin = new Thickness(4, 4 * Theme.Density, 4, 4 * Theme.Density);
                g.ColumnDefinitions.Add(Theme.Col(16));
                g.ColumnDefinitions.Add(Theme.Col(0));
                Ellipse d = new Ellipse();
                d.Width = 7; d.Height = 7;
                d.SetResourceReference(Shape.FillProperty, Ui.FgKey(r[0]));
                g.Children.Add(d);
                TextBlock t = Theme.T(r[1], 12, "Text", false);
                Grid.SetColumn(t, 1);
                g.Children.Add(t);
                previewList.Children.Add(g);
            }
        }

        void SetAccent(string hex)
        {
            cfg.Studio.Accent = hex;
            if (hex.Length > 0) accentBox.Text = hex;
            ApplyStudio();
        }

        // Применить настройки Studio ко всему окну без перезапуска
        void ApplyStudio()
        {
            Theme.Options = cfg.Studio;
            SaveSettings();
            ApplyTheme();
            Theme.RescaleTree(design);
            if (paletteLayer != null) Theme.RescaleTree(paletteLayer);
            if (logList != null) RefreshJournal();
            if (eventList != null) RefreshEvents();
            if (lastDiag != null) RenderDiagnostics();
            RebuildStrategyCards();
            RebuildQuickActions();
            if (pulseList != null) RebuildPulseCards();
            if (netMap != null) netMap.Restyle();
            if (companion != null) companion.Restyle();
            if (navBee != null) navBee.Restyle();
            UpdateCompanion();
            RefreshStudio();
        }

        string StudioSummary(ThemeOptions o, string baseTheme)
        {
            return "Тема: " + Theme.FamilyTitle(baseTheme ?? cfg.Theme) +
                   " · акцент: " + (o.Accent.Length > 0 ? o.Accent : "как в теме") +
                   " · свечение " + o.Glow + "% · углы: " + o.Corners + " · плотность: " + o.Density + " · скорость: " + o.Speed;
        }

        void ExportStudio()
        {
            SaveFileDialog d = new SaveFileDialog();
            d.FileName = "my.veylix-theme.json";
            d.Filter = "Тема Veylix (*.veylix-theme.json)|*.veylix-theme.json|JSON (*.json)|*.json";
            if (d.ShowDialog(this) != true) return;
            try
            {
                File.WriteAllText(d.FileName, cfg.Studio.ToJson(cfg.Theme), new UTF8Encoding(false));
                studioStatus.Text = "Тема сохранена: " + System.IO.Path.GetFileName(d.FileName) + ". В файле только параметры оформления.";
            }
            catch (Exception ex) { studioStatus.Text = "Не удалось сохранить: " + ex.Message; }
        }

        void ImportStudio()
        {
            OpenFileDialog d = new OpenFileDialog();
            d.Filter = "Тема Veylix (*.veylix-theme.json;*.json)|*.veylix-theme.json;*.json";
            if (d.ShowDialog(this) != true) return;
            string json, err, baseTheme;
            int ignored;
            try
            {
                FileInfo fi = new FileInfo(d.FileName);
                if (fi.Length > ThemeOptions.MaxImportBytes) { studioStatus.Text = "Файл больше 16 КБ — это не тема Veylix."; return; }
                json = File.ReadAllText(d.FileName, Encoding.UTF8);
            }
            catch (Exception ex) { studioStatus.Text = "Не удалось прочитать файл: " + ex.Message; return; }
            ThemeOptions o = ThemeOptions.FromJson(json, out baseTheme, out ignored, out err);
            if (o == null) { studioStatus.Text = "Тема не загружена: " + err + "."; return; }
            if (!VDialog.Ask(this, "Применить тему из файла?", StudioSummary(o, baseTheme) +
                (ignored > 0 ? "\n\nНеизвестных полей пропущено: " + ignored + "." : ""), null, "Применить", "Отмена", "info")) return;
            if (baseTheme != null) cfg.Theme = baseTheme;
            cfg.Studio = o;
            accentBox.Text = o.Accent;
            ApplyStudio();
            studioStatus.Text = "Тема загружена из " + System.IO.Path.GetFileName(d.FileName) + ".";
        }
    }
}
