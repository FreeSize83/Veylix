using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Veylix
{
    // Рекомендация стратегии только по фактически измеренным результатам
    public static class StrategyLab
    {
        public static double Ratio(Strategy s)
        {
            return s.LastMax > 0 ? s.LastScore / (double)s.LastMax : -1;
        }

        // Сравнение: больше ответивших адресов → меньше задержка → предпочтительная → текущая
        public static int Compare(Strategy a, Strategy b, string preferred, Strategy current)
        {
            int c = Ratio(b).CompareTo(Ratio(a));
            if (c != 0) return c;
            int la = a.LastLatency >= 0 ? a.LastLatency : int.MaxValue, lb = b.LastLatency >= 0 ? b.LastLatency : int.MaxValue;
            // разница меньше 15 мс — в пределах погрешности, задержкой не различаем
            if (Math.Abs((long)la - lb) >= 15) return la.CompareTo(lb);
            bool pa = a.Display == preferred, pb = b.Display == preferred;
            if (pa != pb) return pa ? -1 : 1;
            bool ca = object.ReferenceEquals(a, current), cb = object.ReferenceEquals(b, current);
            if (ca != cb) return ca ? -1 : 1;
            return 0;
        }

        public static Strategy Recommend(IList<Strategy> list, string preferred, Strategy current, DateTime now, out string reason)
        {
            List<Strategy> tested = new List<Strategy>();
            foreach (Strategy s in list) if (s.LastScore >= 0 && s.LastMax > 0 && s.LastResults != null) tested.Add(s);
            if (tested.Count == 0)
            {
                reason = "Пока нет измерений. Запустите автоподбор или проверьте выбранную стратегию — рекомендация строится только по реальным результатам.";
                return null;
            }
            tested.Sort(delegate(Strategy a, Strategy b) { return Compare(a, b, preferred, current); });
            Strategy best = tested[0];
            if (best.LastScore == 0)
            {
                reason = "Ни одна из проверенных стратегий (" + tested.Count + ") не открыла Discord и YouTube — рекомендовать нечего.";
                return null;
            }
            StringBuilder sb = new StringBuilder();
            sb.Append(best.LastSummary);
            if (best.LastLatency >= 0) sb.Append(", средняя задержка TCP+TLS ").Append(best.LastLatency).Append(" мс");
            sb.Append(" (проверена в ").Append(best.LastTested.ToString("HH:mm")).Append("). ");
            sb.Append(best.LastScore == best.LastMax ? "Ответили все адреса. " : "Лучший результат среди проверенных, но не все адреса ответили. ");
            if (tested.Count > 1)
            {
                Strategy second = tested[1];
                if (Math.Abs(Ratio(second) - Ratio(best)) < 1e-9)
                {
                    if (second.LastLatency >= 0 && best.LastLatency >= 0 && second.LastLatency - best.LastLatency >= 15)
                        sb.Append("У ").Append(second.Display).Append(" тот же результат, но задержка выше (").Append(second.LastLatency).Append(" мс). ");
                    else if (best.Display == preferred) sb.Append("При равном результате выбрана ваша предпочтительная стратегия. ");
                    else if (object.ReferenceEquals(best, current)) sb.Append("При равном результате оставлена текущая стратегия — переключение ничего не даст. ");
                }
            }
            sb.Append("Сравнено стратегий: ").Append(tested.Count).Append(".");
            if (now - best.LastTested > TimeSpan.FromMinutes(30)) sb.Append(" Результату больше 30 минут — лучше перепроверить.");
            reason = sb.ToString();
            return best;
        }

        public static int AvgLatency(params ServiceResult[] rs)
        {
            int sum = 0, n = 0;
            foreach (ServiceResult r in rs)
                foreach (ProbeResult p in r.Probes)
                    if (p.Passed && p.TcpMs >= 0 && p.TlsMs >= 0) { sum += p.TcpMs + p.TlsMs; n++; }
            return n > 0 ? sum / n : -1;
        }
    }

    public partial class MainWindow
    {
        bool labRunning;
        TextBlock labRecommend;
        VButton labApply, labTest, labDetails, labPrefer;
        Strategy recommended;
        VButton[] labSortSeg = new VButton[2];
        bool sortByResult;

        // Записать полный результат проверки стратегии (используют автоподбор, тест и ручная проверка)
        void RecordResult(Strategy s, ServiceResult d, ServiceResult y)
        {
            s.LastResults = new ServiceResult[] { d, y };
            s.LastLatency = StrategyLab.AvgLatency(d, y);
            Remember(s, d.Score + y.Score, d.MaxScore + y.MaxScore, ShortResult(d, y) + (s.LastLatency >= 0 ? " · " + s.LastLatency + " мс" : ""));
            RefreshLab();
        }

        FrameworkElement BuildLabPanel()
        {
            StackPanel sp = new StackPanel();
            sp.Margin = new Thickness(0, 14, 0, 0);
            Border box = new Border();
            box.CornerRadius = new CornerRadius(14);
            box.BorderThickness = new Thickness(1);
            box.SetResourceReference(Border.BackgroundProperty, "Surface2");
            box.SetResourceReference(Border.BorderBrushProperty, "Stroke");
            box.Padding = new Thickness(14, 10, 10, 10);
            Grid g = new Grid();
            g.ColumnDefinitions.Add(Theme.Col(30));
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.ColumnDefinitions.Add(Theme.Col(-1));
            TextBlock ic = Theme.Icon(0xE734, 15, "Accent");
            ic.VerticalAlignment = VerticalAlignment.Top;
            ic.Margin = new Thickness(0, 2, 0, 0);
            g.Children.Add(ic);
            labRecommend = Theme.Wrap("", 12.5, "Text");
            labRecommend.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(labRecommend, 1);
            g.Children.Add(labRecommend);
            labApply = Ui.Button("Применить", 0xE73E, true);
            labApply.Margin = new Thickness(12, 0, 0, 0);
            labApply.VerticalAlignment = VerticalAlignment.Center;
            labApply.Click += delegate { if (recommended != null) OnPickStrategy(recommended); };
            Grid.SetColumn(labApply, 2);
            g.Children.Add(labApply);
            box.Child = g;
            sp.Children.Add(box);

            WrapPanel w = new WrapPanel();
            w.Margin = new Thickness(0, 10, 0, 0);
            labTest = Ui.Button("Проверить выбранную", 0xE9D9, false);
            labTest.ToolTip = "Последовательно: запустить выбранную стратегию, проверить Discord и YouTube, затем вернуть прежнее состояние обхода";
            labTest.Click += delegate { TestSelectedStrategy(); };
            labDetails = Ui.Button("Подробности", 0xE946, false);
            labDetails.ToolTip = "Результаты по каждому адресу для выбранной стратегии";
            labDetails.Click += delegate { ShowStrategyDetails(selected); };
            labPrefer = Ui.Button("Сделать предпочтительной", 0xE734, false);
            labPrefer.ToolTip = "Предпочтительная стратегия выбирается при запуске Veylix и проверяется автоподбором одной из первых";
            labPrefer.Click += delegate { MarkPreferred(); };
            foreach (VButton b in new VButton[] { labTest, labDetails, labPrefer })
            {
                b.Margin = new Thickness(0, 0, 8, 0);
                w.Children.Add(b);
            }
            sp.Children.Add(w);
            RefreshLab();
            return sp;
        }

        FrameworkElement BuildLabSort()
        {
            Grid g = new Grid();
            g.Margin = new Thickness(0, 0, 0, 12);
            g.ColumnDefinitions.Add(Theme.Col(0));
            g.ColumnDefinitions.Add(Theme.Col(-1));
            g.Children.Add(Theme.T("Сравнение стратегий · одинаковые проверки: 3 адреса Discord и 3 адреса YouTube", 12.5, "Text2", false));
            Border seg = Ui.Segmented(new string[] { "По списку", "По результату" }, labSortSeg, delegate(int i)
            {
                sortByResult = i == 1;
                Ui.SelectSegment(labSortSeg, i);
                RebuildStrategyCards();
            });
            Ui.SelectSegment(labSortSeg, 0);
            Grid.SetColumn(seg, 1);
            g.Children.Add(seg);
            return g;
        }

        List<Strategy> OrderedStrategies()
        {
            List<Strategy> l = new List<Strategy>(strategies);
            if (sortByResult)
            {
                Strategy cur = engine.Current;
                string pref = cfg.PreferredStrategy;
                // стабильная сортировка: непроверенные — в конце, в исходном порядке
                List<KeyValuePair<int, Strategy>> idx = new List<KeyValuePair<int, Strategy>>();
                for (int i = 0; i < l.Count; i++) idx.Add(new KeyValuePair<int, Strategy>(i, l[i]));
                idx.Sort(delegate(KeyValuePair<int, Strategy> a, KeyValuePair<int, Strategy> b)
                {
                    bool ta = a.Value.LastScore >= 0, tb = b.Value.LastScore >= 0;
                    if (ta != tb) return ta ? -1 : 1;
                    int c = ta ? StrategyLab.Compare(a.Value, b.Value, pref, cur) : 0;
                    return c != 0 ? c : a.Key.CompareTo(b.Key);
                });
                l.Clear();
                foreach (KeyValuePair<int, Strategy> kv in idx) l.Add(kv.Value);
            }
            return l;
        }

        void RefreshLab()
        {
            if (labRecommend == null) return;
            string why;
            recommended = StrategyLab.Recommend(strategies, cfg.PreferredStrategy, engine.Current, DateTime.Now, out why);
            labRecommend.Text = (recommended != null ? "Рекомендуется " + recommended.Display + ". " : "") + why;
            bool already = recommended != null && engine.IsRunning && object.ReferenceEquals(engine.Current, recommended);
            labApply.Visibility = recommended != null && !already ? Visibility.Visible : Visibility.Collapsed;
            bool free = !busy && !tuning && !updating && !labRunning;
            labApply.IsEnabled = free;
            labTest.IsEnabled = free;
            Ui.SetButtonText(labTest, labRunning ? "Проверяю…" : "Проверить " + (selected != null ? selected.Display : "выбранную"));
            labDetails.IsEnabled = selected != null && selected.LastResults != null;
            Strategy target = engine.Current ?? selected;
            labPrefer.IsEnabled = target != null && cfg.PreferredStrategy != target.Display;
            Ui.SetButtonText(labPrefer, target != null && cfg.PreferredStrategy == target.Display ? target.Display + " — предпочтительная" : "Сделать " + (target != null ? target.Display : "") + " предпочтительной");
        }

        void MarkPreferred()
        {
            Strategy s = engine.Current ?? selected;
            if (s == null) return;
            cfg.PreferredStrategy = s.Display;
            SaveSettings();
            log.Add("ok", "Предпочтительная стратегия: " + s.Display + ". Она будет выбрана при следующем запуске Veylix.");
            events.Add("strategy", "info", "Предпочтительная стратегия: " + s.Display, "Сохранено в настройках", "strategies");
            RefreshStrategyCards();
            RefreshLab();
        }

        // Тест одной стратегии без потери текущего состояния: запуск → проверки → возврат как было
        async void TestSelectedStrategy()
        {
            if (labRunning || busy || tuning || updating || selected == null) return;
            Strategy s = selected;
            Strategy before = engine.IsRunning ? engine.Current : null;
            labRunning = true;
            RefreshLab();
            RefreshUi();
            log.Add("info", "Лаборатория: проверяю " + s.Display + (before != null ? " (потом вернусь к " + before.Display + ")" : " (потом выключу обход)") + "…");
            string summary = null;
            try
            {
                bool ok = await Task.Run(() => engine.Start(s));
                if (ok) { await Task.Delay(2500); ok = engine.IsRunning; }
                if (!ok)
                {
                    Remember(s, 0, 1, "не запустилась");
                    summary = "winws.exe не запустился";
                }
                else
                {
                    Task<ServiceResult> td = NetCheck.CheckService(NetCheck.Discord);
                    Task<ServiceResult> ty = NetCheck.CheckService(NetCheck.YouTube);
                    await Task.WhenAll(td, ty);
                    session.CountServices(new ServiceResult[] { td.Result, ty.Result });
                    RecordResult(s, td.Result, ty.Result);
                    summary = s.LastSummary;
                }
                session.StrategiesTested++;
            }
            catch (Exception ex) { summary = "ошибка: " + ex.Message; }
            // восстановление исходного состояния (выполняется всегда: исключения выше перехвачены)
            try
            {
                if (before != null && !object.ReferenceEquals(before, s)) await Task.Run(() => engine.Start(before));
                else if (before == null) await Task.Run(() => engine.Stop());
            }
            catch (Exception ex) { log.Add("err", "Не удалось вернуть исходное состояние: " + ex.Message); }
            labRunning = false;
            log.Add(s.LastScore == s.LastMax ? "ok" : "warn", "Лаборатория: " + s.Display + " — " + summary + ". Состояние обхода восстановлено.");
            events.Add("strategy", s.LastScore == s.LastMax ? "ok" : "info", "Тест стратегии " + s.Display, summary, "strategies");
            RefreshUi();
            RefreshLab();
        }

        void ShowStrategyDetails(Strategy s)
        {
            if (s == null || s.LastResults == null) return;
            StringBuilder sb = new StringBuilder();
            foreach (ServiceResult r in s.LastResults)
            {
                sb.Append(r.Name).Append(": ").Append(NetCheck.StatusWord(r.Status)).Append(" — ").Append(r.Summary).Append("\n");
                foreach (ProbeResult p in r.Probes)
                {
                    sb.Append("   ").Append(p.Passed ? "✓ " : "✕ ").Append(p.Target.Host).Append("  ").Append(p.Timing());
                    if (!p.Passed) sb.Append("  [").Append(p.Stage).Append("] ").Append(p.Detail);
                    else if (p.Flaky) sb.Append("  (со второй попытки)");
                    sb.Append("\n");
                }
                sb.Append("\n");
            }
            VDialog.Ask(this, "Стратегия " + s.Display, "Проверена в " + s.LastTested.ToString("HH:mm:ss") + ". " + s.LastSummary + ".",
                sb.ToString().TrimEnd(), "Закрыть", null, "info");
        }
    }
}
