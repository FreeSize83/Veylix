using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shell;

namespace Veylix
{
    // Диалог в стиле текущей темы вместо стандартного MessageBox
    public class VDialog : Window
    {
        bool result;

        VDialog(string title, string message, string details, string primary, string secondary, string kind)
            : this(title, message, details, null, primary, secondary, kind) { }

        VDialog(string title, string message, string details, FrameworkElement custom, string primary, string secondary, string kind)
        {
            Title = "Veylix";
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.Height;
            Width = 460;
            ShowInTaskbar = false;
            FontFamily = Theme.Ui;
            SetResourceReference(BackgroundProperty, "Popup");
            WindowChrome wc = new WindowChrome();
            wc.CaptionHeight = 0;
            wc.ResizeBorderThickness = new Thickness(0);
            wc.GlassFrameThickness = new Thickness(0, 0, 0, 1);
            wc.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, wc);
            SourceInitialized += delegate
            {
                Backdrop.Apply(this, false, Theme.Current != null && Theme.Current.Dark);
            };

            Border frame = new Border();
            frame.BorderThickness = new Thickness(1);
            frame.SetResourceReference(Border.BorderBrushProperty, "StrokeStrong");
            frame.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
            };

            Grid g = new Grid();
            g.Margin = new Thickness(22, 20, 22, 18);
            g.ColumnDefinitions.Add(Theme.Col(46));
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(-1));
            g.RowDefinitions.Add(Theme.RowDef(-1));

            string fg = kind == "err" ? "Err" : kind == "warn" ? "Warn" : kind == "ok" ? "Ok" : "Accent";
            string soft = kind == "err" ? "ErrSoft" : kind == "warn" ? "WarnSoft" : kind == "ok" ? "OkSoft" : "AccentSoft";
            int glyph = kind == "err" ? 0xE783 : kind == "warn" ? 0xE7BA : kind == "ok" ? 0xE73E : 0xE946;
            Border ic = new Border();
            ic.Width = 34; ic.Height = 34;
            ic.CornerRadius = new CornerRadius(11);
            ic.VerticalAlignment = VerticalAlignment.Top;
            ic.HorizontalAlignment = HorizontalAlignment.Left;
            ic.SetResourceReference(Border.BackgroundProperty, soft);
            ic.Child = Theme.Icon(glyph, 16, fg);
            g.Children.Add(ic);

            StackPanel body = new StackPanel();
            Grid.SetColumn(body, 1);
            TextBlock tt = Theme.T(title, 17, "Text", true);
            tt.TextWrapping = TextWrapping.Wrap;
            tt.TextTrimming = TextTrimming.None;
            body.Children.Add(tt);
            if (!string.IsNullOrEmpty(message))
            {
                TextBlock m = Theme.Wrap(message, 13, "Text2");
                m.Margin = new Thickness(0, 6, 0, 0);
                body.Children.Add(m);
            }
            g.Children.Add(body);

            if (custom != null)
            {
                ScrollViewer cs = Theme.Scroll(custom);
                cs.MaxHeight = 380;
                cs.Margin = new Thickness(46, 14, 0, 0);
                Grid.SetRow(cs, 1);
                Grid.SetColumnSpan(cs, 2);
                g.Children.Add(cs);
            }
            else if (!string.IsNullOrEmpty(details))
            {
                Border box = new Border();
                box.CornerRadius = new CornerRadius(10);
                box.BorderThickness = new Thickness(1);
                box.SetResourceReference(Border.BorderBrushProperty, "Stroke");
                box.SetResourceReference(Border.BackgroundProperty, "Surface2");
                box.Margin = new Thickness(0, 14, 0, 0);
                box.MaxHeight = 260;
                TextBlock dt = Theme.Wrap(details, 12, "Text2");
                dt.Margin = new Thickness(12, 10, 12, 10);
                ScrollViewer sv = Theme.Scroll(dt);
                box.Child = sv;
                Grid.SetRow(box, 1);
                Grid.SetColumnSpan(box, 2);
                g.Children.Add(box);
            }

            StackPanel buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.Margin = new Thickness(0, 18, 0, 0);
            Grid.SetRow(buttons, 2);
            Grid.SetColumnSpan(buttons, 2);
            if (secondary != null)
            {
                VButton b2 = Ui.Button(secondary, 0, false);
                b2.Margin = new Thickness(0, 0, 8, 0);
                b2.Click += delegate { result = false; Close(); };
                buttons.Children.Add(b2);
            }
            VButton b1 = Ui.Button(primary, 0, true);
            b1.Click += delegate { result = true; Close(); };
            buttons.Children.Add(b1);
            g.Children.Add(buttons);

            frame.Child = g;
            Content = frame;

            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { result = false; Close(); e.Handled = true; }
                else if (e.Key == Key.Enter && !(Keyboard.FocusedElement is VButton)) { result = true; Close(); e.Handled = true; }
            };
            Loaded += delegate { b1.Focus(); };
        }

        static bool Run(Window owner, VDialog d)
        {
            if (owner != null && owner.IsVisible && owner.WindowState != WindowState.Minimized)
            {
                d.Owner = owner;
                d.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                d.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                d.Topmost = true;
            }
            d.ShowDialog();
            return d.result;
        }

        public static bool Ask(Window owner, string title, string message, string details, string primary, string secondary, string kind)
        {
            return Run(owner, new VDialog(title, message, details, primary, secondary, kind));
        }

        public static void Show(Window owner, string title, string message, string kind)
        {
            Run(owner, new VDialog(title, message, null, "Понятно", null, kind));
        }

        // Диалог с произвольным содержимым под текстом (например, список изменений)
        public static void ShowContent(Window owner, string title, string message, FrameworkElement content, string primary, string kind)
        {
            VDialog d = new VDialog(title, message, null, content, primary, null, kind);
            d.Width = 520;
            Run(owner, d);
        }

        // Если интерфейс ещё не готов (или упал), используем стандартное окно
        public static void Fallback(string title, string message)
        {
            try
            {
                if (Application.Current != null && Theme.Current != null)
                {
                    Show(null, title, message, "err");
                    return;
                }
            }
            catch { }
            MessageBox.Show(message, title);
        }
    }
}
