using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerLanguage.Strategy
{
    // Уровни стопов и ATR дня (ТЗ §5).
    public static class LraLevels
    {
        // Ближайший по времени фрактал за ценой: side +1 — верхний выше price (+ отступ), -1 — нижний ниже (- отступ). Нет — NaN.
        // Фрактал — бар, чей хай выше (лоу ниже) FractalBars соседей с каждой стороны.
        public static double Nearest(IList<LraBar> h, int side, double price, LraSettings s)
        {
            // соседи i±FractalBars должны существовать; глубина — не дальше SwingLookback баров от текущего
            int w = Math.Max(1, s.FractalBars);
            int last = Math.Max(w, h.Count - 1 - s.SwingLookback);
            for (int i = h.Count - 1 - w; i >= last; i--)
            {
                if (side > 0)
                {
                    if (h[i].High > price && Fractal(h, i, w, 1))
                        return h[i].High + s.OffsetTicks * s.Tick;
                }
                else if (h[i].Low < price && Fractal(h, i, w, -1))
                    return h[i].Low - s.OffsetTicks * s.Tick;
            }
            return double.NaN;
        }

        static bool Fractal(IList<LraBar> h, int i, int w, int side)
        {
            for (int k = 1; k <= w; k++)
                if (side > 0 ? h[i].High <= h[i - k].High || h[i].High <= h[i + k].High
                             : h[i].Low >= h[i - k].Low || h[i].Low >= h[i + k].Low) return false;
            return true;
        }

        // Средний размах торговых дней (дата Paris) за AtrPeriod последних полных дней; текущий день не считается. Меньше 3 дней — NaN.
        // Возвращает цену (не пипсы): пипсы = результат / PipSize.
        public static double DayAtr(IList<LraBar> h, LraSettings s)
        {
            if (h.Count == 0) return double.NaN;
            DateTime cur = h[h.Count - 1].Paris.Date;
            var ranges = h.Where(b => b.Paris.Date < cur)
                          .GroupBy(b => b.Paris.Date).OrderBy(g => g.Key)
                          .Select(g => g.Max(b => b.High) - g.Min(b => b.Low)).ToList();
            if (ranges.Count < 3) return double.NaN;
            return ranges.Skip(Math.Max(0, ranges.Count - s.AtrPeriod)).Average();
        }
    }
}
