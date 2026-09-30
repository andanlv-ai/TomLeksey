using System;
using System.Collections.Generic;

namespace PowerLanguage.Strategy
{
    // Диапазоны ЛРА (ТЗ §4).
    public static class LraRanges
    {
        // Вызывать на каждом закрытом баре. Отмечает Done/Expired у прежних диапазонов (кладёт их в changed),
        // находит новый диапазон (добавляет в ranges и в changed, возвращает его) или возвращает null.
        public static LraRange Update(IList<LraBar> h, List<LraRange> ranges, LraSettings s, List<LraRange> changed)
        {
            int last = h.Count - 1;
            LraBar cur = h[last];

            // 1. Прежние активные: Done / Expired
            foreach (LraRange r in ranges)
            {
                if (!r.Active) continue;
                if (last > r.BornIdx && cur.Low <= r.Poc && r.Poc <= cur.High) r.Done = true;
                else if (last - r.BornIdx > s.MaxRangeAgeBars) r.Expired = true;
                else continue;
                changed.Add(r);
            }

            // 2. Кандидат из баров до текущего: наибольшее окно, заканчивающееся на last-1
            int end = last - 1;
            if (end < 0) return null;
            double hi = h[end].High, lo = h[end].Low;
            if ((hi - lo) / s.PipSize > s.MaxRangePips + 1e-9) return null;
            int start = end;
            while (start > 0)
            {
                double nh = Math.Max(hi, h[start - 1].High), nl = Math.Min(lo, h[start - 1].Low);
                if ((nh - nl) / s.PipSize > s.MaxRangePips + 1e-9) break;
                hi = nh; lo = nl; start--;
            }
            int n = end - start + 1;
            if (n < s.MinRangeBars) return null;
            int active = 0;
            for (int i = start; i <= end; i++)
                if (h[i].Session == "EUROPE" || h[i].Session == "AMERICA") active++;
            if (active < s.MinActiveBars) return null;

            int dir = cur.Close > hi ? 1 : cur.Close < lo ? -1 : 0;
            if (dir == 0) return null;
            foreach (LraRange r in ranges)
                if (r.Active && lo <= r.High && hi >= r.Low) return null;

            // Poc: цена с наибольшей суммой Ask+Bid по уровням всех баров окна
            var vol = new Dictionary<long, double>();
            var price = new Dictionary<long, double>();
            double total = 0;
            for (int i = start; i <= end; i++)
            {
                LraBar b = h[i];
                total += b.Volume;
                for (int k = 0; k < b.LvPrice.Length; k++)
                {
                    long key = (long)Math.Round(b.LvPrice[k] / s.Tick);
                    double old;
                    vol.TryGetValue(key, out old);
                    vol[key] = old + b.LvAsk[k] + b.LvBid[k];
                    if (!price.ContainsKey(key)) price[key] = b.LvPrice[k];
                }
            }
            double poc = (hi + lo) / 2, best = double.NegativeInfinity;
            foreach (var kv in vol)
                if (kv.Value > best || (kv.Value == best && price[kv.Key] < poc))
                { best = kv.Value; poc = price[kv.Key]; }

            var nr = new LraRange
            {
                StartIdx = start, EndIdx = end, BornIdx = last,
                Start = h[start].Time, End = h[end].Time,
                High = hi, Low = lo, Poc = poc, Volume = total, Bars = n, ExitDir = dir,
                Id = h[start].Time.ToString("yyyyMMddHHmm", System.Globalization.CultureInfo.InvariantCulture)
            };
            ranges.Add(nr);
            changed.Add(nr);
            return nr;
        }

        // Активный диапазон с минимальным |price - Poc| или null.
        public static LraRange NearestActive(List<LraRange> ranges, double price)
        {
            LraRange best = null;
            foreach (LraRange r in ranges)
                if (r.Active && (best == null || Math.Abs(price - r.Poc) < Math.Abs(price - best.Poc))) best = r;
            return best;
        }
    }
}
