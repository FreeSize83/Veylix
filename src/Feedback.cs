using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace Veylix
{
    // Обратная связь: сообщение уходит на Cloudflare Worker автора (tools/feedback-worker.js),
    // а тот пересылает его в Telegram. Токена бота в программе нет.
    public static class Feedback
    {
        // Адрес Worker; пусто — форма показывает, что отправка не настроена
        public const string Endpoint = "https://veylix-feedback.freesize83.workers.dev/feedback";

        public const int MaxText = 2000;
        public const int MaxContact = 120;
        public const int MaxReport = 60000;
        public static readonly string[] Kinds = { "idea", "bug", "other" };

        public static bool Configured { get { return Endpoint.StartsWith("https://"); } }

        public static string WindowsVersion()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string build = Convert.ToString(k.GetValue("CurrentBuildNumber"));
                    string disp = Convert.ToString(k.GetValue("DisplayVersion"));
                    int b;
                    string name = int.TryParse(build, out b) && b >= 22000 ? "Windows 11" : "Windows 10";
                    return name + (disp.Length > 0 ? " " + disp : "") + " (" + build + ")";
                }
            }
            catch { return Environment.OSVersion.VersionString; }
        }

        public static string Clip(string s, int max)
        {
            s = (s ?? "").Trim();
            return s.Length > max ? s.Substring(0, max) : s;
        }

        // Что проверить до отправки; null — можно отправлять
        public static string Validate(string text)
        {
            string t = (text ?? "").Trim();
            if (t.Length < 3) return "Напишите хотя бы пару слов.";
            if (t.Length > MaxText) return "Слишком длинно: не больше " + MaxText + " символов.";
            return null;
        }

        public static string BuildPayload(string kind, string text, string contact, string strategy, bool running, string os, string report)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["kind"] = Array.IndexOf(Kinds, kind) >= 0 ? kind : "other";
            d["text"] = Clip(text, MaxText);
            d["contact"] = Clip(contact, MaxContact);
            d["version"] = Program.Version;
            d["os"] = os ?? "";
            d["strategy"] = strategy ?? "";
            d["running"] = running;
            d["report"] = Clip(report, MaxReport);
            JavaScriptSerializer js = new JavaScriptSerializer();
            js.MaxJsonLength = 1024 * 1024;
            return js.Serialize(d);
        }

        // Ответ Worker → null (доставлено) или понятная ошибка
        public static string ParseResponse(int status, string body)
        {
            string err = null;
            bool ok = false;
            try
            {
                Dictionary<string, object> d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(body ?? "");
                object v;
                if (d != null && d.TryGetValue("ok", out v) && v is bool) ok = (bool)v;
                if (d != null && d.TryGetValue("error", out v) && v != null) err = Convert.ToString(v);
            }
            catch { }
            if (status == 200 && ok) return null;
            if (status == 429) return "Слишком много сообщений подряд — попробуйте через час.";
            if (!string.IsNullOrEmpty(err)) return "Сервер ответил: " + err + ".";
            return "Сервер ответил кодом " + status + ". Попробуйте позже.";
        }

        public static string Send(string payload, int timeoutMs)
        {
            if (!Configured) return "Отправка пока не настроена в этой версии Veylix.";
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(Endpoint);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Accept = "application/json";
                req.UserAgent = Http.UserAgent;
                req.Timeout = timeoutMs;
                req.ReadWriteTimeout = timeoutMs;
                req.KeepAlive = false;
                byte[] data = Encoding.UTF8.GetBytes(payload);
                req.ContentLength = data.Length;
                using (Stream s = req.GetRequestStream()) s.Write(data, 0, data.Length);
                using (HttpWebResponse r = (HttpWebResponse)req.GetResponse())
                using (StreamReader sr = new StreamReader(r.GetResponseStream(), Encoding.UTF8))
                    return ParseResponse((int)r.StatusCode, sr.ReadToEnd());
            }
            catch (WebException ex)
            {
                HttpWebResponse hr = ex.Response as HttpWebResponse;
                if (hr != null)
                {
                    try
                    {
                        using (StreamReader sr = new StreamReader(hr.GetResponseStream(), Encoding.UTF8))
                            return ParseResponse((int)hr.StatusCode, sr.ReadToEnd());
                    }
                    catch { return ParseResponse((int)hr.StatusCode, null); }
                }
                return "Не удалось связаться с сервером: " + Http.Describe(ex) + ". Проверьте интернет или включите обход и попробуйте ещё раз.";
            }
            catch (Exception ex) { return "Не удалось отправить: " + ex.Message; }
        }
    }

    public partial class MainWindow
    {
        Card feedbackCard;
        TextBox fbText, fbContact;
        VButton[] fbSeg = new VButton[3];
        VToggle tgFbReport;
        VButton fbSend;
        TextBlock fbStatus, fbCount;
        string fbKind = "idea";
        bool fbSending;
        DateTime fbLastSent = DateTime.MinValue;

        // Многострочное поле: общий стиль TextBox центрирует текст по вертикали, поэтому своя рамка
        static Border FieldBox(TextBox tb, double height, string placeholder)
        {
            tb.Style = new Style(typeof(TextBox));
            tb.Background = System.Windows.Media.Brushes.Transparent;
            tb.BorderThickness = new Thickness(0);
            tb.FontFamily = Theme.Ui;
            tb.FontSize = 13;
            tb.SetResourceReference(TextBox.ForegroundProperty, "Text");
            tb.SetResourceReference(TextBox.CaretBrushProperty, "Text");
            tb.SetResourceReference(TextBox.SelectionBrushProperty, "Accent");
            tb.Padding = new Thickness(0);
            Border b = new Border();
            b.CornerRadius = new CornerRadius(10);
            b.BorderThickness = new Thickness(1);
            b.Padding = new Thickness(10, 8, 10, 8);
            b.Height = height;
            b.SetResourceReference(Border.BackgroundProperty, "Input");
            b.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            Grid g = new Grid();
            g.Children.Add(tb);
            TextBlock ph = Theme.Wrap(placeholder, 12.5, "Text3");
            ph.IsHitTestVisible = false;
            ph.Margin = new Thickness(2, height > 40 ? 0 : 1, 0, 0);
            ph.VerticalAlignment = height > 40 ? VerticalAlignment.Top : VerticalAlignment.Center;
            g.Children.Add(ph);
            tb.VerticalAlignment = height > 40 ? VerticalAlignment.Stretch : VerticalAlignment.Center;
            tb.TextChanged += delegate { ph.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            tb.GotKeyboardFocus += delegate { b.SetResourceReference(Border.BorderBrushProperty, "Accent"); };
            tb.LostKeyboardFocus += delegate { b.SetResourceReference(Border.BorderBrushProperty, "Stroke"); };
            b.Child = g;
            return b;
        }

        FrameworkElement BuildFeedbackCard()
        {
            Card c = new Card(22);
            c.Margin = new Thickness(0, 0, 0, 16);
            c.Inner = new Thickness(24, 20, 24, 16);
            StackPanel s = new StackPanel();
            s.Children.Add(Ui.Heading("Написать разработчику",
                "Идея, ошибка или вопрос — сообщение сразу придёт автору Veylix. Никаких аккаунтов и регистрации не нужно."));

            string[] kinds = Feedback.Kinds;
            Border seg = Ui.Segmented(new string[] { "Идея", "Ошибка", "Другое" }, fbSeg, delegate(int i) { fbKind = kinds[i]; RefreshFeedbackCard(); });
            seg.HorizontalAlignment = HorizontalAlignment.Left;
            seg.Margin = new Thickness(0, 14, 0, 10);
            s.Children.Add(seg);

            fbText = new TextBox();
            fbText.AcceptsReturn = true;
            fbText.TextWrapping = TextWrapping.Wrap;
            fbText.MaxLength = Feedback.MaxText;
            fbText.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            fbText.TextChanged += delegate { RefreshFeedbackCard(); };
            s.Children.Add(FieldBox(fbText, 124, "Что добавить или что не работает? Чем подробнее, тем быстрее получится помочь."));
            fbCount = Theme.T("", 11, "Text3", false);
            fbCount.HorizontalAlignment = HorizontalAlignment.Right;
            fbCount.Margin = new Thickness(0, 4, 2, 6);
            s.Children.Add(fbCount);

            fbContact = new TextBox();
            fbContact.MaxLength = Feedback.MaxContact;
            Border cb = FieldBox(fbContact, 38, "Как с вами связаться (необязательно): Telegram, Discord или почта");
            s.Children.Add(cb);

            tgFbReport = new VToggle();
            tgFbReport.Clicked += delegate { tgFbReport.IsOn = !tgFbReport.IsOn; RefreshFeedbackCard(); };
            TextBlock rd;
            Grid rr = Ui.SettingRow("Приложить отчёт диагностики и журнал",
                "Помогает разобраться с ошибкой. Имя пользователя, компьютера и полные IP-адреса в отчёте скрыты.", tgFbReport, out rd);
            rr.Margin = new Thickness(0, 10, 0, 0);
            s.Children.Add(rr);

            Grid bottom = new Grid();
            bottom.Margin = new Thickness(0, 12, 0, 2);
            bottom.ColumnDefinitions.Add(Theme.Col(0));
            bottom.ColumnDefinitions.Add(Theme.Col(-1));
            fbStatus = Theme.Wrap("", 12, "Text3");
            fbStatus.VerticalAlignment = VerticalAlignment.Center;
            fbStatus.Margin = new Thickness(0, 0, 12, 0);
            bottom.Children.Add(fbStatus);
            fbSend = Ui.Button("Отправить", 0xE724, true);
            fbSend.Click += delegate { SendFeedback(); };
            Grid.SetColumn(fbSend, 1);
            bottom.Children.Add(fbSend);
            s.Children.Add(bottom);

            c.Content = s;
            feedbackCard = c;
            RefreshFeedbackCard();
            return c;
        }

        void RefreshFeedbackCard()
        {
            if (fbText == null) return;
            Ui.SelectSegment(fbSeg, Math.Max(0, Array.IndexOf(Feedback.Kinds, fbKind)));
            fbCount.Text = fbText.Text.Trim().Length + " / " + Feedback.MaxText;
            fbSend.IsEnabled = !fbSending && Feedback.Configured && fbText.Text.Trim().Length >= 3;
            if (!fbSending && fbStatus.Tag == null)
            {
                fbStatus.Text = Feedback.Configured
                    ? "Вместе с текстом отправятся версии Veylix и Windows и текущая стратегия."
                    : "Отправка ещё не настроена в этой сборке.";
                fbStatus.SetResourceReference(TextBlock.ForegroundProperty, "Text3");
            }
        }

        void OpenFeedback()
        {
            if (!IsVisible) ShowFromTray();
            ShowPage("settings");
            if (feedbackCard != null)
            {
                feedbackCard.BringIntoView();
                fbText.Focus();
            }
        }

        string FeedbackReport()
        {
            StringBuilder sb = new StringBuilder();
            if (lastDiag != null) sb.Append(DiagReport()).Append("\r\n\r\n");
            else sb.Append("Диагностика в этом сеансе не запускалась.\r\n\r\n");
            sb.Append("[Журнал — последние записи]\r\n");
            List<LogEntry> all = log.Snapshot();
            for (int i = Math.Max(0, all.Count - 150); i < all.Count; i++)
                sb.Append(LogStore.Format(all[i])).Append("\r\n");
            return LogStore.Sanitize(sb.ToString());
        }

        async void SendFeedback()
        {
            if (fbSending) return;
            string bad = Feedback.Validate(fbText.Text);
            if (bad != null) { SetFeedbackStatus(bad, "Warn"); return; }
            if ((DateTime.Now - fbLastSent).TotalSeconds < 60) { SetFeedbackStatus("Сообщение только что отправлено — подождите минуту перед следующим.", "Warn"); return; }
            fbSending = true;
            Ui.SetButtonText(fbSend, "Отправляю…");
            SetFeedbackStatus("Отправляю…", "Text2");
            RefreshFeedbackCard();
            try
            {
                Strategy cur = engine.Current ?? selected;
                string payload = Feedback.BuildPayload(fbKind, fbText.Text, fbContact.Text,
                    cur != null ? cur.Display : "", engine.IsRunning, Feedback.WindowsVersion(), tgFbReport.IsOn ? FeedbackReport() : "");
                string err = await Task.Run(() => Feedback.Send(payload, 20000));
                if (err == null)
                {
                    fbLastSent = DateTime.Now;
                    fbText.Text = "";
                    tgFbReport.IsOn = false;
                    SetFeedbackStatus("Спасибо! Сообщение доставлено." + (fbContact.Text.Trim().Length > 0 ? " Если понадобится, с вами свяжутся." : ""), "Ok");
                    log.Add("ok", "Сообщение разработчику отправлено.");
                    Toast("feedback", "ok", "Сообщение отправлено", "Спасибо за отзыв!", null, false);
                }
                else
                {
                    SetFeedbackStatus(err, "Err");
                    log.Add("warn", "Сообщение разработчику не отправлено: " + err);
                }
            }
            catch (Exception ex) { SetFeedbackStatus("Не удалось отправить: " + ex.Message, "Err"); }
            finally
            {
                fbSending = false;
                Ui.SetButtonText(fbSend, "Отправить");
                RefreshFeedbackCard();
            }
        }

        void SetFeedbackStatus(string text, string key)
        {
            fbStatus.Tag = text;   // своё сообщение не перетирается подсказкой по умолчанию
            fbStatus.Text = text;
            fbStatus.SetResourceReference(TextBlock.ForegroundProperty, key);
        }
    }
}
