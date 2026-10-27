using System;
using System.Collections.Generic;
using SD = System.Drawing;
using SD2 = System.Drawing.Drawing2D;

namespace Veylix
{
    // Значок в трее с точкой состояния в углу: видно, работает ли обход, не открывая Veylix
    public static class TrayBadge
    {
        // Приоритет: идёт операция → обход работает → пауза для игры → ошибка → выключен
        public static string State(bool running, bool busy, bool paused, bool error)
        {
            if (busy) return "busy";
            if (running) return "run";
            if (paused) return "pause";
            if (error) return "err";
            return "off";
        }

        public static SD.Color DotColor(string state)
        {
            switch (state)
            {
                case "run": return SD.Color.FromArgb(34, 197, 94);     // зелёный
                case "busy":
                case "pause": return SD.Color.FromArgb(245, 158, 11);  // янтарный
                case "err": return SD.Color.FromArgb(239, 68, 68);     // красный
                default: return SD.Color.FromArgb(148, 152, 160);      // серый
            }
        }

        public static string Tip(string state)
        {
            switch (state)
            {
                case "run": return "работает";
                case "busy": return "применяю…";
                case "pause": return "пауза для игры";
                case "err": return "ошибка";
                default: return "обход выключен";
            }
        }

        // Рисует иконку 32×32 с точкой; Windows сама уменьшит её до размера трея
        public static SD.Icon Make(SD.Icon baseIcon, string state)
        {
            using (SD.Bitmap bmp = new SD.Bitmap(32, 32, SD.Imaging.PixelFormat.Format32bppArgb))
            {
                using (SD.Graphics g = SD.Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SD2.SmoothingMode.AntiAlias;
                    g.InterpolationMode = SD2.InterpolationMode.HighQualityBicubic;
                    g.Clear(SD.Color.Transparent);
                    if (baseIcon != null)
                        using (SD.Icon sized = new SD.Icon(baseIcon, 32, 32))
                        using (SD.Bitmap b = sized.ToBitmap())
                            g.DrawImage(b, 0, 0, 32, 32);
                    const float d = 15f, x = 32 - d - 0.5f, y = 32 - d - 0.5f;
                    // тёмная обводка, чтобы точка читалась и на светлой, и на тёмной панели задач
                    using (SD.SolidBrush ring = new SD.SolidBrush(SD.Color.FromArgb(235, 22, 22, 26)))
                        g.FillEllipse(ring, x - 1.5f, y - 1.5f, d + 3f, d + 3f);
                    using (SD.SolidBrush fill = new SD.SolidBrush(DotColor(state)))
                        g.FillEllipse(fill, x, y, d, d);
                }
                IntPtr h = bmp.GetHicon();
                try
                {
                    using (SD.Icon tmp = SD.Icon.FromHandle(h))
                        return (SD.Icon)tmp.Clone();   // копия владеет своим дескриптором — исходный можно освободить
                }
                finally { DestroyIcon(h); }
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr handle);
    }
}
