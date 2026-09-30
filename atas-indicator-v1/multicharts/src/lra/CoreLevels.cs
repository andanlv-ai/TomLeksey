using System;
using System.Collections.Generic;
using System.Linq;

namespace PowerLanguage.Strategy
{
    // Уровни стопов и ATR дня (ТЗ §5).
    public static class LraLevels
    {
        // Ближайший по времени свинг за ценой: side +1 — верхний свинг выше price (+ отступ), -1 — нижний ниже (- отступ). Нет — NaN.
        public static double Nearest(IList<LraBar> h, int side, double price, LraSettings s)
        {
            // i+1 должен существовать (i <= Count-2), i-1 тоже (i >= 1); глубина — не дальше SwingLookback баров от текущего
            int last = Math.Max(1, h.Count - 1 - s.SwingLookback);
            for (int i = h.Count - 2; i >= last; i--)
            {
                if (side > 0)
                {
                    if (h[i].High > h[i - 1].High && h[i].High > h[i + 1].High && h[i].High > price)
                        return h[i].High + s.OffsetTicks * s.Tick;
                }
                else if (h[i].Low < h[i - 1].Low && h[i].Low < h[i + 1].Low && h[i].Low < price)
                    return h[i].Low - s.OffsetTicks * s.Tick;
            }
            return double.NaN;
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
